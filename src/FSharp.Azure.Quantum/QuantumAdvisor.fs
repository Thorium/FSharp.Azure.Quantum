namespace FSharp.Azure.Quantum.Classical

open System

/// Quantum Advisor Decision Framework - provides quantum vs classical solver recommendations
module QuantumAdvisor =
    
    /// Recommendation types with increasing strength toward quantum
    type RecommendationType =
        | StronglyRecommendClassical    // Classical clearly better
        | ConsiderQuantum                // Borderline - depends on factors
        | StronglyRecommendQuantum      // Quantum clearly better
    
    /// Quantum advisor recommendation with reasoning
    type Recommendation = {
        /// Recommendation type
        RecommendationType: RecommendationType
        
        /// Human-readable reasoning for the recommendation
        Reasoning: string
        
        /// Confidence level (0.0 to 1.0)
        Confidence: float
        
        /// Problem size analyzed
        ProblemSize: int
    }
    
    /// Get recommendation for a given problem input
    /// Returns Result with Recommendation or error message
    let getRecommendation (input: 'T) : Result<Recommendation, string> =
        // Use ProblemAnalysis to classify the problem
        match ProblemAnalysis.classifyProblem input with
        | Error msg -> Error msg
        | Ok problemInfo ->
            let n = problemInfo.Size
            
            // Conservative thresholds (bias toward classical for borderline cases)
            let recommendationType, reasoning, confidence =
                if n < 10 then
                    (StronglyRecommendClassical,
                     $"Small problem (n={n}). Classical algorithms are significantly faster for problems with fewer than 10 variables.",
                     0.95)
                elif n < 20 then
                    (StronglyRecommendClassical,
                     $"Medium problem (n={n}). Classical solvers remain competitive. Quantum overhead not justified.",
                     0.85)
                elif n < 50 then
                    (ConsiderQuantum,
                     $"Large problem (n={n}). Quantum computing may provide advantage depending on time constraints and hardware availability.",
                     0.70)
                else
                    (StronglyRecommendQuantum,
                     $"Very large problem (n={n}). Quantum computing strongly recommended due to exponential classical complexity.",
                     0.90)
            
            Ok {
                RecommendationType = recommendationType
                Reasoning = reasoning
                Confidence = confidence
                ProblemSize = n
            }
