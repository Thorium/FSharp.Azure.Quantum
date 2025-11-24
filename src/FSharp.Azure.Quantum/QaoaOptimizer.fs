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
