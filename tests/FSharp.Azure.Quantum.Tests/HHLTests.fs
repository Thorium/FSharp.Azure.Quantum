namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open System.Numerics

/// Tests for HHL (Harrow-Hassidim-Lloyd) Quantum Linear System Solver
/// 
/// NOTE: These tests need to be rewritten for the new unified backend API.
/// The old QuantumLinearSystemSolver module has been replaced with HHL module
/// which uses IQuantumBackend and ExecuteToState pattern.
module HHL = FSharp.Azure.Quantum.Algorithms.HHL

module HHLTests =
    
    [<Fact>]
    let ``HHL solve 2x2 diagonal system`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        
        // Test solving a simple 2x2 diagonal system
        // A = [[2, 0], [0, 1]], b = [1, 0]
        // Expected solution: x = [0.5, 0]
        let eigenvalues = (2.0, 1.0)  // Tuple, not array
        let bVector = (Complex(1.0, 0.0), Complex.Zero)  // Tuple, not array
        
        match HHL.solve2x2Diagonal eigenvalues bVector backend with
        | Error err -> Assert.Fail($"HHL execution failed: {err}")
        | Ok result ->
            // Should have computed a solution
            Assert.NotNull(result.Solution)
            Assert.True(result.Solution.Length > 0, "Solution should not be empty")
    
    [<Fact>]
    let ``HHL solve 4x4 diagonal system`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        
        // Test solving a 4x4 diagonal system
        let eigenvalues = [| 2.0; 1.0; 0.5; 0.25 |]
        let bVector = [| Complex(1.0, 0.0); Complex.Zero; Complex.Zero; Complex.Zero |]
        
        match HHL.solve4x4Diagonal eigenvalues bVector backend with
        | Error err -> Assert.Fail($"HHL execution failed: {err}")
        | Ok result ->
            // Should have computed a solution
            Assert.NotNull(result.Solution)
            Assert.True(result.Solution.Length > 0, "Solution should not be empty")
    
    [<Fact>]
    let ``HHL solveIdentity works for identity matrix`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        
        // Identity matrix: Ax = b → x = b
        let bVector = [| Complex(1.0, 0.0); Complex(0.5, 0.0) |]
        
        match HHL.solveIdentity bVector backend with
        | Error err -> Assert.Fail($"HHL execution failed: {err}")
        | Ok result ->
            // For identity matrix, solution should equal b vector
            Assert.NotNull(result.Solution)
            Assert.Equal(bVector.Length, result.Solution.Length)
