namespace FSharp.Azure.Quantum

open System

/// Quantum Advisor Decision Framework - provides quantum vs classical solver recommendations
module QuantumAdvisor =

    /// Recommendation types with increasing strength toward quantum
    [<Struct>]
    type RecommendationType =
        | StronglyRecommendClassical // Classical clearly better
        | ConsiderQuantum // Borderline - depends on factors
        | StronglyRecommendQuantum // Quantum clearly better

    /// Configurable decision thresholds for quantum recommendation
    type DecisionThresholds =
        {
            /// Problem size below which classical is strongly recommended
            SmallProblemThreshold: int

            /// Problem size below which classical is still recommended
            MediumProblemThreshold: int

            /// Problem size below which quantum should be considered
            LargeProblemThreshold: int

            /// Quantum speedup factor required to recommend quantum
            MinQuantumSpeedupFactor: float

            /// Confidence threshold for strong recommendations
            HighConfidenceThreshold: float
        }

    /// Default conservative thresholds (bias toward classical)
    let defaultThresholds =
        { SmallProblemThreshold = 10
          MediumProblemThreshold = 20
          LargeProblemThreshold = 50
          MinQuantumSpeedupFactor = 2.0
          HighConfidenceThreshold = 0.85 }

    /// Quantum advisor recommendation with reasoning
    type Recommendation =
        {
            /// Recommendation type
            RecommendationType: RecommendationType

            /// Human-readable reasoning for the recommendation
            Reasoning: string

            /// Confidence level (0.0 to 1.0)
            Confidence: float

            /// Problem size analyzed
            ProblemSize: int

            /// Estimated quantum speedup factor (if available)
            QuantumSpeedup: float option

            /// Estimated classical solving time in milliseconds (if available)
            EstimatedClassicalTimeMs: float option

            /// Estimated quantum solving time in milliseconds (if available)
            EstimatedQuantumTimeMs: float option
        }

    /// Generate human-readable reasoning based on analysis
    let private generateReasoning
        (n: int)
        (recommendationType: RecommendationType)
        (quantumAdvantage: ProblemAnalysis.QuantumAdvantage option)
        : string =
        match quantumAdvantage with
        | Some qa ->
            // Use quantum advantage data for detailed reasoning
            let classicalTime = qa.EstimatedClassicalTimeMs
            let quantumTime = qa.EstimatedQuantumTimeMs
            let speedup = qa.QuantumSpeedup

            match recommendationType with
            | StronglyRecommendClassical ->
                if n < 10 then
                    $"Small problem (n={n}). Classical algorithms are significantly faster for problems with fewer than 10 variables. "
                    + $"Estimated classical solving time: {classicalTime:F2}ms vs quantum: {quantumTime:F2}ms (speedup: {speedup:F2}x). "
                    + "Quantum overhead not justified."
                else
                    $"Medium problem (n={n}). Classical solvers remain competitive with estimated solving time of {classicalTime:F2}ms. "
                    + $"Quantum speedup ({speedup:F2}x) does not justify quantum hardware costs."

            | ConsiderQuantum ->
                $"Large problem (n={n}). Quantum computing may provide advantage with estimated speedup of {speedup:F2}x "
                + $"(classical: {classicalTime:F2}ms vs quantum: {quantumTime:F2}ms). "
                + "Consider quantum depending on time constraints and hardware availability."

            | StronglyRecommendQuantum ->
                if Double.IsInfinity(classicalTime) then
                    $"Very large problem (n={n}). Classical solving time exceeds practical limits (>hours). "
                    + $"Quantum computing strongly recommended with estimated solving time of {quantumTime:F2}ms. "
                    + "Quantum provides the only practical solution."
                else
                    $"Very large problem (n={n}). Quantum computing strongly recommended with significant speedup of {speedup:F2}x "
                    + $"(classical: {classicalTime:F2}ms vs quantum: {quantumTime:F2}ms). "
                    + "Exponential classical complexity makes quantum the clear choice."

        | None ->
            // Fallback to simple size-based reasoning
            match recommendationType with
            | StronglyRecommendClassical ->
                if n < 10 then
                    $"Small problem (n={n}). Classical algorithms are significantly faster for problems with fewer than 10 variables."
                else
                    $"Medium problem (n={n}). Classical solvers remain competitive. Quantum overhead not justified."

            | ConsiderQuantum ->
                $"Large problem (n={n}). Quantum computing may provide advantage depending on time constraints and hardware availability."

            | StronglyRecommendQuantum ->
                $"Very large problem (n={n}). Quantum computing strongly recommended due to exponential classical complexity."

    /// Make recommendation with configurable thresholds
    let getRecommendationWithThresholds (thresholds: DecisionThresholds) (input: 'T) : Result<Recommendation, string> =
        // Use ProblemAnalysis to classify the problem
        match ProblemAnalysis.classifyProblem input with
        | Error msg -> Error msg
        | Ok problemInfo ->
            let n = problemInfo.Size

            // Get quantum advantage estimation for more informed decision
            let quantumAdvantage =
                match ProblemAnalysis.estimateQuantumAdvantage input with
                | Ok qa -> Some qa
                | Error _ -> None

            // Determine recommendation type based on thresholds
            let recommendationType, confidence =
                if n < thresholds.SmallProblemThreshold then
                    // Small problems: strongly recommend classical
                    (StronglyRecommendClassical, 0.95)

                elif n < thresholds.MediumProblemThreshold then
                    // Medium problems: classical still better
                    (StronglyRecommendClassical, 0.85)

                elif n < thresholds.LargeProblemThreshold then
                    // Large problems: consider quantum based on speedup
                    match quantumAdvantage with
                    | Some qa when qa.QuantumSpeedup >= thresholds.MinQuantumSpeedupFactor * 2.0 ->
                        (ConsiderQuantum, 0.75)
                    | _ -> (ConsiderQuantum, 0.70)

                else
                    // Very large problems: strongly recommend quantum
                    match quantumAdvantage with
                    | Some qa when qa.QuantumSpeedup >= thresholds.MinQuantumSpeedupFactor * 5.0 ->
                        (StronglyRecommendQuantum, 0.95)
                    | _ -> (StronglyRecommendQuantum, 0.90)

            // Generate reasoning based on analysis
            let reasoning = generateReasoning n recommendationType quantumAdvantage

            // Extract optional metrics
            let speedup, classicalTime, quantumTime =
                match quantumAdvantage with
                | Some qa -> (Some qa.QuantumSpeedup, Some qa.EstimatedClassicalTimeMs, Some qa.EstimatedQuantumTimeMs)
                | None -> (None, None, None)

            Ok
                { RecommendationType = recommendationType
                  Reasoning = reasoning
                  Confidence = confidence
                  ProblemSize = n
                  QuantumSpeedup = speedup
                  EstimatedClassicalTimeMs = classicalTime
                  EstimatedQuantumTimeMs = quantumTime }

    /// Get recommendation with default conservative thresholds
    /// Returns Result with Recommendation or error message
    let getRecommendation (input: 'T) : Result<Recommendation, string> =
        getRecommendationWithThresholds defaultThresholds input
