module FSharp.Azure.Quantum.Tests.QaoaOptimizerTests

open Xunit
open FSharp.Azure.Quantum.Core.QaoaOptimizer

[<Fact>]
let ``Optimizer should converge for simple 2-qubit QAOA problem`` () =
    // Simple objective function: minimize (gamma - 0.5)^2 + (beta - 0.3)^2
    // Expected optimal: gamma = 0.5, beta = 0.3
    let objectiveFunction (parameters: float[]) =
        let gamma = parameters[0]
        let beta = parameters[1]
        (gamma - 0.5) ** 2.0 + (beta - 0.3) ** 2.0
    
    // Initial guess: far from optimum
    let initialParameters = [| 0.1; 0.1 |]
    
    // Run optimizer
    let result = Optimizer.minimize objectiveFunction initialParameters
    
    // Verify convergence
    Assert.True(result.Converged, "Optimizer should converge")
    Assert.True(result.Iterations > 0, "Should perform at least one iteration")
    
    // Verify optimized parameters are close to expected
    let optimizedGamma = result.OptimizedParameters[0]
    let optimizedBeta = result.OptimizedParameters[1]
    
    Assert.InRange(optimizedGamma, 0.45, 0.55) // Within 10% of 0.5
    Assert.InRange(optimizedBeta, 0.25, 0.35)  // Within 10% of 0.3
    
    // Verify final objective value is small
    Assert.True(result.FinalObjectiveValue < 0.01, "Final objective value should be near zero")
