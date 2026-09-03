namespace FSharp.Azure.Quantum.Core

module QaoaOptimizer =
    
    open MathNet.Numerics.Optimization
    open MathNet.Numerics.LinearAlgebra
    
    /// Result of parameter optimization
    type OptimizationResult = {
        /// Optimized parameters (gamma, beta angles)
        OptimizedParameters: float[]
        /// Final objective function value
        FinalObjectiveValue: float
        /// Whether optimization converged
        Converged: bool
        /// Number of iterations performed
        Iterations: int
    }
    
    /// Optimizer module for QAOA parameter optimization
    module Optimizer =
        
        /// Minimize objective function using Nelder-Mead simplex method
        /// Parameters:
        ///   objectiveFunction - Function to minimize (lower is better)
        ///   initialParameters - Initial guess for parameters
        /// Returns:
        ///   OptimizationResult with optimized parameters and convergence info
        let minimize (objectiveFunction: float[] -> float) (initialParameters: float[]) : OptimizationResult =
            
            // Create objective function model for Math.NET
            let objModel = ObjectiveFunction.Value(fun (parameters: Vector<float>) ->
                objectiveFunction (parameters.ToArray())
            )
            
            // Create Nelder-Mead solver
            // Nelder-Mead is derivative-free and robust for noisy functions
            let solver = NelderMeadSimplex(1e-8, 200)  // tolerance, max iterations
            
            // Run optimization
            let initialVector = Vector<float>.Build.DenseOfArray initialParameters
            let result = solver.FindMinimum(objModel, initialVector)
            
            {
                OptimizedParameters = result.MinimizingPoint.ToArray()
                FinalObjectiveValue = result.FunctionInfoAtMinimum.Value
                Converged = 
                    result.ReasonForExit = ExitCondition.Converged || 
                    result.ReasonForExit = ExitCondition.BoundTolerance
                Iterations = result.Iterations
            }
        
        /// Minimize objective function with parameter bounds using Nelder-Mead with quadratic penalty
        /// Parameters:
        ///   objectiveFunction - Function to minimize (lower is better)
        ///   initialParameters - Initial guess for parameters
        ///   lowerBounds - Lower bounds for each parameter
        ///   upperBounds - Upper bounds for each parameter
        ///   tolerance - Convergence tolerance for the Nelder-Mead simplex
        ///   maxIterations - Maximum number of optimizer iterations
        /// Returns:
        ///   OptimizationResult with optimized parameters and convergence info.
        ///   If the iteration limit is reached, the best evaluation seen so far is
        ///   returned with Converged = false instead of throwing.
        let minimizeWithBounds
            (objectiveFunction: float[] -> float)
            (initialParameters: float[])
            (lowerBounds: float[])
            (upperBounds: float[])
            (tolerance: float)
            (maxIterations: int) : OptimizationResult =

            // Create penalty-based objective function that enforces bounds
            // Uses quadratic penalty for parameters outside bounds
            let penaltyWeight = 1e6

            // Track the best evaluation seen so far, so a usable result can be
            // returned even when the optimizer does not converge
            let bestParameters = ref (Array.copy initialParameters)
            let bestPenalizedValue = ref infinity

            let boundedObjective (parameters: float[]) =
                let baseValue = objectiveFunction parameters

                // Add penalty for violating bounds
                let penalty =
                    parameters
                    |> Array.mapi (fun i p ->
                        let lower = lowerBounds[i]
                        let upper = upperBounds[i]
                        if p < lower then (lower - p) ** 2.0 * penaltyWeight
                        elif p > upper then (p - upper) ** 2.0 * penaltyWeight
                        else 0.0
                    )
                    |> Array.sum

                let penalizedValue = baseValue + penalty
                if penalizedValue < bestPenalizedValue.Value then
                    bestPenalizedValue.Value <- penalizedValue
                    bestParameters.Value <- Array.copy parameters
                penalizedValue

            // Create objective function model for Math.NET
            let objModel = ObjectiveFunction.Value(fun (parameters: Vector<float>) ->
                boundedObjective (parameters.ToArray())
            )

            // Use Nelder-Mead with penalty function
            let solver = NelderMeadSimplex(tolerance, maxIterations)

            // Run optimization
            let initialVector = Vector<float>.Build.DenseOfArray initialParameters

            let (minimizingPoint, iterations, converged) =
                try
                    let result = solver.FindMinimum(objModel, initialVector)
                    (result.MinimizingPoint.ToArray(),
                     result.Iterations,
                     result.ReasonForExit = ExitCondition.Converged ||
                     result.ReasonForExit = ExitCondition.BoundTolerance)
                with
                | :? MaximumIterationsException ->
                    // Did not converge within maxIterations:
                    // fall back to the best evaluation seen so far rather than crashing
                    (bestParameters.Value, maxIterations, false)

            // Clamp final result to bounds (in case of numerical errors)
            let clampedParameters =
                minimizingPoint
                |> Array.mapi (fun i p ->
                    let lower = lowerBounds[i]
                    let upper = upperBounds[i]
                    max lower (min upper p)
                )

            {
                OptimizedParameters = clampedParameters
                FinalObjectiveValue = objectiveFunction clampedParameters  // Use original objective
                Converged = converged
                Iterations = iterations
            }
