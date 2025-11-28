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
            let initialVector = Vector<float>.Build.DenseOfArray(initialParameters)
            let result = solver.FindMinimum(objModel, initialVector)
            
            {
                OptimizedParameters = result.MinimizingPoint.ToArray()
                FinalObjectiveValue = result.FunctionInfoAtMinimum.Value
                Converged = 
                    result.ReasonForExit = ExitCondition.Converged || 
                    result.ReasonForExit = ExitCondition.BoundTolerance
                Iterations = result.Iterations
            }
        
        /// Minimize objective function with parameter bounds using BOBYQA
        /// Parameters:
        ///   objectiveFunction - Function to minimize (lower is better)
        ///   initialParameters - Initial guess for parameters
        ///   lowerBounds - Lower bounds for each parameter
        ///   upperBounds - Upper bounds for each parameter
        /// Returns:
        ///   OptimizationResult with optimized parameters and convergence info
        let minimizeWithBounds 
            (objectiveFunction: float[] -> float) 
            (initialParameters: float[]) 
            (lowerBounds: float[])
            (upperBounds: float[]) : OptimizationResult =
            
            // Create penalty-based objective function that enforces bounds
            // Uses quadratic penalty for parameters outside bounds
            let penaltyWeight = 1e6
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
                
                baseValue + penalty
            
            // Create objective function model for Math.NET
            let objModel = ObjectiveFunction.Value(fun (parameters: Vector<float>) ->
                boundedObjective (parameters.ToArray())
            )
            
            // Use Nelder-Mead with penalty function
            // Increase max iterations for bounded problems
            let solver = NelderMeadSimplex(1e-6, 1000)
            
            // Run optimization
            let initialVector = Vector<float>.Build.DenseOfArray(initialParameters)
            let result = solver.FindMinimum(objModel, initialVector)
            
            // Clamp final result to bounds (in case of numerical errors)
            let clampedParameters = 
                result.MinimizingPoint.ToArray()
                |> Array.mapi (fun i p ->
                    let lower = lowerBounds[i]
                    let upper = upperBounds[i]
                    max lower (min upper p)
                )
            
            {
                OptimizedParameters = clampedParameters
                FinalObjectiveValue = objectiveFunction clampedParameters  // Use original objective
                Converged = 
                    result.ReasonForExit = ExitCondition.Converged || 
                    result.ReasonForExit = ExitCondition.BoundTolerance
                Iterations = result.Iterations
            }
