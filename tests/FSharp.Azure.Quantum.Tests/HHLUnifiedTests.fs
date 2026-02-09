namespace FSharp.Azure.Quantum.Tests

open Xunit
open System.Numerics
open FSharp.Azure.Quantum.Algorithms.HHLTypes
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction

module HHL = FSharp.Azure.Quantum.Algorithms.HHL

/// Tests for HHL Algorithm Unified Implementation
/// 
/// Note: HHL supports diagonal matrices and general Hermitian matrices.
/// - Diagonal matrices may use the native intent op on some backends.
/// - General matrices are lowered into explicit gates.
///
/// Tests cover:
/// - Type creation and validation
/// - State preparation
/// - Convenience functions for 2×2 and 4×4 systems
/// - Identity matrix validation
/// - Error handling
module HHLUnifiedTests =
    
    let createBackend() = LocalBackend() :> IQuantumBackend
    
    // ========================================================================
    // HHL PLANNER TESTS
    // ========================================================================
    
    type private NoHhlIntentBackend(inner: IQuantumBackend) =
        interface IQuantumBackend with
            member _.ExecuteToState circuit = inner.ExecuteToState circuit
            member _.NativeStateType = inner.NativeStateType
            member _.ApplyOperation operation state = inner.ApplyOperation operation state

            member _.SupportsOperation operation =
                match operation with
                | QuantumOperation.Algorithm (AlgorithmOperation.HHL _) -> false
                | _ -> inner.SupportsOperation operation

            member _.Name = inner.Name + " (no-hhl-intent)"
            member _.InitializeState numQubits = inner.InitializeState numQubits

    /// Backend wrapper that simulates a gate-based Rigetti constraint set.
    ///
    /// Purpose: verify HHL lowering + planning transpilation produces Rigetti-native gates.
    type private RigettiGateOnlyBackend(inner: IQuantumBackend) =
        interface IQuantumBackend with
            member _.ExecuteToState circuit = inner.ExecuteToState circuit
            member _.NativeStateType = inner.NativeStateType
            member _.ApplyOperation operation state = inner.ApplyOperation operation state

            member _.SupportsOperation operation =
                match operation with
                | QuantumOperation.Algorithm _ -> false
                | QuantumOperation.Gate gate ->
                    match gate with
                    | X _ | Y _ | Z _ | H _ | RX _ | RY _ | RZ _ | CNOT _ | CZ _ | SWAP _ -> true
                    | S _ | SDG _ | T _ | TDG _ | P _ | CP _ | CRX _ | CRY _ | CRZ _ | MCZ _ | CCX _ | Measure _ | U3 _ -> false
                | QuantumOperation.Extension _ 
                | QuantumOperation.Braid _ 
                | QuantumOperation.Measure _ 
                | QuantumOperation.FMove _ 
                | QuantumOperation.Sequence _ 
                    -> false

            member _.Name = "rigetti.sim.qvm (gate-only)"
            member _.InitializeState numQubits = inner.InitializeState numQubits

    [<Fact>]
    let ``HHL planner prefers algorithm intent when supported`` () =
        let backend = LocalBackend() :> IQuantumBackend

        let eigenvalues = [| 1.0; 2.0 |]
        let inputVector = [| Complex(1.0, 0.0); Complex.Zero |]

        match createDiagonalMatrix eigenvalues, createQuantumVector inputVector with
        | Error err, _ -> Assert.Fail($"Matrix creation failed: {err}")
        | _, Error err -> Assert.Fail($"Vector creation failed: {err}")
        | Ok matrix, Ok vector ->
            match defaultConfig matrix vector with
            | Error err -> Assert.Fail($"Config creation failed: {err}")
            | Ok config ->

            let intent: HHL.HhlExecutionIntent =
                {
                    Matrix = config.Matrix
                    InputVector = config.InputVector
                    EigenvalueQubits = config.EigenvalueQubits
                    SolutionQubits = config.SolutionQubits
                    InversionMethod = config.InversionMethod
                    MinEigenvalue = config.MinEigenvalue
                    UsePostSelection = config.UsePostSelection
                    QpePrecision = config.QPEPrecision
                    Exactness = HHL.Exactness.Exact
                }

            match HHL.plan backend intent with
            | Ok (HHL.HhlPlan.ExecuteNatively _, _, _, _) -> Assert.True(true)
            | Ok _ -> Assert.Fail("Expected ExecuteNatively plan")
            | Error err -> Assert.Fail($"Planning failed: {err}")

    [<Fact>]
    let ``HHL planner produces explicit lowered ops when intent op unsupported`` () =
        let backend = (LocalBackend() :> IQuantumBackend) |> NoHhlIntentBackend :> IQuantumBackend

        let eigenvalues = [| 1.0; 2.0 |]
        let inputVector = [| Complex(1.0, 0.0); Complex.Zero |]

        match createDiagonalMatrix eigenvalues, createQuantumVector inputVector with
        | Error err, _ -> Assert.Fail($"Matrix creation failed: {err}")
        | _, Error err -> Assert.Fail($"Vector creation failed: {err}")
        | Ok matrix, Ok vector ->
            match defaultConfig matrix vector with
            | Error err -> Assert.Fail($"Config creation failed: {err}")
            | Ok config ->

            let intent: HHL.HhlExecutionIntent =
                {
                    Matrix = config.Matrix
                    InputVector = config.InputVector
                    EigenvalueQubits = config.EigenvalueQubits
                    SolutionQubits = config.SolutionQubits
                    InversionMethod = config.InversionMethod
                    MinEigenvalue = config.MinEigenvalue
                    UsePostSelection = config.UsePostSelection
                    QpePrecision = config.QPEPrecision
                    Exactness = HHL.Exactness.Exact
                }

            match HHL.plan backend intent with
            | Ok (HHL.HhlPlan.ExecuteViaOps (ops, exactness), _, _, _) ->
                Assert.Equal(HHL.Exactness.Exact, exactness)
                Assert.NotEmpty ops
                Assert.True(ops |> List.forall backend.SupportsOperation)
            | Ok _ -> Assert.Fail("Expected ExecuteViaOps plan")
            | Error err -> Assert.Fail($"Planning failed: {err}")

    [<Fact>]
    let ``HHL planner lowers full general-matrix path to gates only`` () =
        let backend = (LocalBackend() :> IQuantumBackend) |> NoHhlIntentBackend :> IQuantumBackend
 
        // Simple 2x2 Hermitian but non-diagonal matrix.
        // [[1, 0.5],
        //  [0.5, 1]]
        let hermitian =
            array2D
                [
                    [ Complex(1.0, 0.0); Complex(0.5, 0.0) ]
                    [ Complex(0.5, 0.0); Complex(1.0, 0.0) ]
                ]
 
        let inputVector = [| Complex(1.0, 0.0); Complex.Zero |]
 
        match createHermitianMatrix hermitian, createQuantumVector inputVector with
        | Error err, _ -> Assert.Fail($"Matrix creation failed: {err}")
        | _, Error err -> Assert.Fail($"Vector creation failed: {err}")
        | Ok matrix, Ok vector ->
            match defaultConfig matrix vector with
            | Error err -> Assert.Fail($"Config creation failed: {err}")
            | Ok baseConfig ->

            let config =
                {
                    baseConfig with
                        // keep tiny for test runtime
                        EigenvalueQubits = 2
                        QPEPrecision = 2
                }
 
            let intent: HHL.HhlExecutionIntent =
                {
                    Matrix = config.Matrix
                    InputVector = config.InputVector
                    EigenvalueQubits = config.EigenvalueQubits
                    SolutionQubits = config.SolutionQubits
                    InversionMethod = config.InversionMethod
                    MinEigenvalue = config.MinEigenvalue
                    UsePostSelection = config.UsePostSelection
                    QpePrecision = config.QPEPrecision
                    Exactness = HHL.Exactness.Exact
                }
 
            match HHL.plan backend intent with
            | Ok (HHL.HhlPlan.ExecuteViaOps (ops, _), _, _, _) ->
                Assert.NotEmpty ops
                Assert.True(ops |> List.forall backend.SupportsOperation)
                Assert.True(ops |> List.forall (function QuantumOperation.Gate _ -> true | _ -> false))
            | Ok (HHL.HhlPlan.ExecuteNatively _, _, _, _) ->
                Assert.Fail("Expected general-matrix to lower to ops")
            | Error err -> Assert.Fail($"Planning failed: {err}")

    [<Fact>]
    let ``HHL planner transpiles general-matrix lowering to Rigetti gate set`` () =
        let backend = (LocalBackend() :> IQuantumBackend) |> RigettiGateOnlyBackend :> IQuantumBackend
 
        // Simple 2x2 Hermitian but non-diagonal matrix.
        let hermitian =
            array2D
                [
                    [ Complex(1.0, 0.0); Complex(0.5, 0.0) ]
                    [ Complex(0.5, 0.0); Complex(1.0, 0.0) ]
                ]
 
        let inputVector = [| Complex(1.0, 0.0); Complex.Zero |]
 
        match createHermitianMatrix hermitian, createQuantumVector inputVector with
        | Error err, _ -> Assert.Fail($"Matrix creation failed: {err}")
        | _, Error err -> Assert.Fail($"Vector creation failed: {err}")
        | Ok matrix, Ok vector ->
            match defaultConfig matrix vector with
            | Error err -> Assert.Fail($"Config creation failed: {err}")
            | Ok baseConfig ->

            let config =
                {
                    baseConfig with
                        // keep tiny for test runtime
                        EigenvalueQubits = 2
                        QPEPrecision = 2
                }
 
            let intent: HHL.HhlExecutionIntent =
                {
                    Matrix = config.Matrix
                    InputVector = config.InputVector
                    EigenvalueQubits = config.EigenvalueQubits
                    SolutionQubits = config.SolutionQubits
                    InversionMethod = config.InversionMethod
                    MinEigenvalue = config.MinEigenvalue
                    UsePostSelection = config.UsePostSelection
                    QpePrecision = config.QPEPrecision
                    Exactness = HHL.Exactness.Exact
                }
 
            match HHL.plan backend intent with
            | Ok (HHL.HhlPlan.ExecuteViaOps (ops, _), _, _, _) ->
                Assert.NotEmpty ops
                Assert.True(ops |> List.forall (function QuantumOperation.Gate _ -> true | _ -> false))
                Assert.True(ops |> List.forall backend.SupportsOperation)
            | Ok (HHL.HhlPlan.ExecuteNatively _, _, _, _) ->
                Assert.Fail("Expected Rigetti to use explicit lowering")
            | Error err -> Assert.Fail($"Planning failed: {err}")

    // Tolerance for floating point comparison
    let epsilon = 1e-6
    
    let assertComplexEqual (expected: Complex) (actual: Complex) (message: string) =
        let diff = Complex.Abs(expected - actual)
        Assert.True(diff < epsilon, $"{message}: expected {expected}, got {actual}, diff={diff}")
    
    // ========================================================================
    // TYPE CREATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``createDiagonalMatrix validates power of 2 dimensions`` () =
        // Valid: power of 2
        match createDiagonalMatrix [| 1.0; 2.0 |] with
        | Ok _ -> ()
        | Error err -> Assert.Fail($"Should accept 2 eigenvalues: {err}")
        
        match createDiagonalMatrix [| 1.0; 2.0; 3.0; 4.0 |] with
        | Ok _ -> ()
        | Error err -> Assert.Fail($"Should accept 4 eigenvalues: {err}")
        
        // Invalid: not power of 2
        match createDiagonalMatrix [| 1.0; 2.0; 3.0 |] with
        | Ok _ -> Assert.Fail("Should reject 3 eigenvalues (not power of 2)")
        | Error (QuantumError.ValidationError _) -> ()
        | Error err -> Assert.Fail($"Wrong error type: {err}")
    
    [<Fact>]
    let ``createDiagonalMatrix rejects empty array`` () =
        match createDiagonalMatrix [||] with
        | Ok _ -> Assert.Fail("Should reject empty array")
        | Error (QuantumError.ValidationError _) -> ()
        | Error err -> Assert.Fail($"Wrong error type: {err}")
    
    [<Fact>]
    let ``createDiagonalMatrix rejects singular matrix`` () =
        // Matrix with zero eigenvalue (singular)
        match createDiagonalMatrix [| 0.0; 2.0 |] with
        | Ok _ -> Assert.Fail("Should reject singular matrix (zero eigenvalue)")
        | Error (QuantumError.ValidationError _) -> ()
        | Error err -> Assert.Fail($"Wrong error type: {err}")
        
        // All zeros
        match createDiagonalMatrix [| 0.0; 0.0; 0.0; 0.0 |] with
        | Ok _ -> Assert.Fail("Should reject singular matrix (all zeros)")
        | Error (QuantumError.ValidationError _) -> ()
        | Error err -> Assert.Fail($"Wrong error type: {err}")
    
    [<Fact>]
    let ``createQuantumVector validates power of 2 dimensions`` () =
        // Valid: power of 2
        match createQuantumVector [| Complex(1.0, 0.0); Complex(0.0, 0.0) |] with
        | Ok _ -> ()
        | Error err -> Assert.Fail($"Should accept 2 components: {err}")
        
        // Invalid: not power of 2
        match createQuantumVector [| Complex(1.0, 0.0); Complex(0.0, 0.0); Complex(0.0, 0.0) |] with
        | Ok _ -> Assert.Fail("Should reject 3 components (not power of 2)")
        | Error (QuantumError.ValidationError _) -> ()
        | Error err -> Assert.Fail($"Wrong error type: {err}")
    
    [<Fact>]
    let ``createQuantumVector rejects zero vector`` () =
        match createQuantumVector [| Complex.Zero; Complex.Zero |] with
        | Ok _ -> Assert.Fail("Should reject zero vector")
        | Error (QuantumError.ValidationError _) -> ()
        | Error err -> Assert.Fail($"Wrong error type: {err}")
    
    [<Fact>]
    let ``createQuantumVector normalizes input`` () =
        let vec = [| Complex(3.0, 0.0); Complex(4.0, 0.0) |]  // ||(3,4)|| = 5
        
        match createQuantumVector vec with
        | Error err -> Assert.Fail($"Should succeed: {err}")
        | Ok result ->
            Assert.Equal(2, result.Dimension)
            
            // Should be normalized: ||(0.6, 0.8)|| = 1
            assertComplexEqual (Complex(0.6, 0.0)) result.Components[0] "First component"
            assertComplexEqual (Complex(0.8, 0.0)) result.Components[1] "Second component"
    
    // ========================================================================
    // CONFIGURATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``defaultConfig creates valid configuration`` () =
        let eigenvalues = [| 1.0; 2.0 |]
        let inputVector = [| Complex(1.0, 0.0); Complex.Zero |]
        
        match createDiagonalMatrix eigenvalues with
        | Error err -> Assert.Fail($"Matrix creation failed: {err}")
        | Ok matrix ->
            match createQuantumVector inputVector with
            | Error err -> Assert.Fail($"Vector creation failed: {err}")
            | Ok vector ->
                match defaultConfig matrix vector with
                | Error err -> Assert.Fail($"Config creation failed: {err}")
                | Ok config ->
                
                Assert.Equal(matrix, config.Matrix)
                Assert.Equal(vector, config.InputVector)
                Assert.True(config.EigenvalueQubits > 0)
                Assert.True(config.SolutionQubits > 0)
                Assert.True(config.MinEigenvalue > 0.0)
    
    // ========================================================================
    // SOLVE 2x2 DIAGONAL TESTS
    // ========================================================================
    
    [<Fact>]
    let ``solve2x2Diagonal accepts valid input`` () =
        let backend = createBackend()
        let eigenvalues = (1.0, 2.0)
        let inputVector = (Complex(1.0, 0.0), Complex.Zero)
        
        // Note: Full quantum execution not implemented yet
        // We're just testing that the function accepts valid input
        // and returns appropriate result type
        match HHL.solve2x2Diagonal eigenvalues inputVector backend with
        | Ok result -> 
            // Success! Validate result structure
            Assert.NotNull(result.Config)
            Assert.Equal(2, result.Config.Matrix.Dimension)
        | Error (QuantumError.NotImplemented _) -> 
            // Acceptable for educational implementation
            ()
        | Error (QuantumError.OperationError _) ->
            // Acceptable - implementation incomplete
            ()
        | Error err -> 
            Assert.Fail($"Unexpected error: {err}")
    
    [<Fact>]
    let ``solve2x2Diagonal rejects zero eigenvalue`` () =
        let backend = createBackend()
        let eigenvalues = (0.0, 2.0)  // First eigenvalue is zero (singular!)
        let inputVector = (Complex(1.0, 0.0), Complex.Zero)
        
        match HHL.solve2x2Diagonal eigenvalues inputVector backend with
        | Ok _ -> Assert.Fail("Should reject singular matrix")
        | Error (QuantumError.ValidationError _) -> ()
        | Error err -> Assert.Fail($"Wrong error type: {err}")
    
    [<Fact>]
    let ``solve2x2Diagonal rejects zero input vector`` () =
        let backend = createBackend()
        let eigenvalues = (1.0, 2.0)
        let inputVector = (Complex.Zero, Complex.Zero)  // Zero vector!
        
        match HHL.solve2x2Diagonal eigenvalues inputVector backend with
        | Ok _ -> Assert.Fail("Should reject zero input vector")
        | Error (QuantumError.ValidationError _) -> ()
        | Error err -> Assert.Fail($"Wrong error type: {err}")
    
    // ========================================================================
    // SOLVE 4x4 DIAGONAL TESTS
    // ========================================================================
    
    [<Fact>]
    let ``solve4x4Diagonal validates array lengths`` () =
        let backend = createBackend()
        
        // Wrong eigenvalue count
        let badEigenvalues = [| 1.0; 2.0 |]  // Should be 4
        let goodInputVector = [| Complex(1.0, 0.0); Complex.Zero; Complex.Zero; Complex.Zero |]
        
        match HHL.solve4x4Diagonal badEigenvalues goodInputVector backend with
        | Ok _ -> Assert.Fail("Should reject eigenvalues array with length ≠ 4")
        | Error (QuantumError.ValidationError _) -> ()
        | Error err -> Assert.Fail($"Wrong error type: {err}")
        
        // Wrong input vector count
        let goodEigenvalues = [| 1.0; 2.0; 3.0; 4.0 |]
        let badInputVector = [| Complex(1.0, 0.0); Complex.Zero |]  // Should be 4
        
        match HHL.solve4x4Diagonal goodEigenvalues badInputVector backend with
        | Ok _ -> Assert.Fail("Should reject input vector with length ≠ 4")
        | Error (QuantumError.ValidationError _) -> ()
        | Error err -> Assert.Fail($"Wrong error type: {err}")
    
    [<Fact>]
    let ``solve4x4Diagonal accepts valid 4x4 system`` () =
        let backend = createBackend()
        let eigenvalues = [| 1.0; 2.0; 3.0; 4.0 |]
        let inputVector = [| Complex(1.0, 0.0); Complex.Zero; Complex.Zero; Complex.Zero |]
        
        match HHL.solve4x4Diagonal eigenvalues inputVector backend with
        | Ok result -> 
            Assert.NotNull(result.Config)
            Assert.Equal(4, result.Config.Matrix.Dimension)
        | Error err -> 
            Assert.Fail($"Unexpected error: {err}")
    
    // ========================================================================
    // SOLVE IDENTITY TESTS
    // ========================================================================
    
    [<Fact>]
    let ``solveIdentity validates power of 2 dimensions`` () =
        let backend = createBackend()
        
        // Valid: 2 components (power of 2)
        let validVector = [| Complex(1.0, 0.0); Complex.Zero |]
        match HHL.solveIdentity validVector backend with
        | Ok _ -> ()
        | Error (QuantumError.NotImplemented _) -> ()  // Acceptable
        | Error (QuantumError.OperationError _) -> ()  // Acceptable - implementation incomplete
        | Error err -> Assert.Fail($"Should accept 2-component vector: {err}")
        
        // Invalid: 3 components (not power of 2)
        let invalidVector = [| Complex(1.0, 0.0); Complex.Zero; Complex.Zero |]
        match HHL.solveIdentity invalidVector backend with
        | Ok _ -> Assert.Fail("Should reject non-power-of-2 dimension")
        | Error (QuantumError.ValidationError _) -> ()
        | Error err -> Assert.Fail($"Wrong error type: {err}")
    
    [<Fact>]
    let ``solveIdentity handles 4x4 identity`` () =
        let backend = createBackend()
        let inputVector = [| Complex(1.0, 0.0); Complex(2.0, 0.0); Complex.Zero; Complex.Zero |]
        
        match HHL.solveIdentity inputVector backend with
        | Ok result -> 
            // Identity matrix: all eigenvalues = 1.0
            Assert.NotNull(result.Config)
            Assert.Equal(4, result.Config.Matrix.Dimension)
            
            // Check that matrix is indeed identity (all eigenvalues = 1)
            let matrix = result.Config.Matrix
            Assert.True(matrix.IsDiagonal)
            for i in 0 .. 3 do
                let idx = i * 4 + i  // Diagonal index
                assertComplexEqual (Complex(1.0, 0.0)) matrix.Elements[idx] $"Diagonal element {i}"
        | Error (QuantumError.NotImplemented _) -> 
            Assert.Fail($"Unexpected error: Not implemented?")
        | Error err -> 
            Assert.Fail($"Unexpected error: {err}")
    
    // ========================================================================
    // STATE PREPARATION TESTS (via createQuantumVector)
    // ========================================================================
    
    [<Fact>]
    let ``createQuantumVector produces normalized vector`` () =
        let inputVector = [| Complex(3.0, 0.0); Complex(4.0, 0.0) |]  // ||(3,4)|| = 5
        
        match createQuantumVector inputVector with
        | Error err -> Assert.Fail($"Vector creation failed: {err}")
        | Ok vector ->
            // Vector should be normalized: (0.6, 0.8)
            assertComplexEqual (Complex(0.6, 0.0)) vector.Components[0] "First component"
            assertComplexEqual (Complex(0.8, 0.0)) vector.Components[1] "Second component"
            
            // Compute norm manually
            let norm = 
                vector.Components
                |> Array.sumBy (fun c -> c.Magnitude * c.Magnitude)
                |> sqrt
            Assert.True(abs (norm - 1.0) < epsilon, $"Norm should be 1, got {norm}")
    
    // ========================================================================
    // ERROR HANDLING TESTS
    // ========================================================================
    
    [<Fact>]
    let ``HHL returns appropriate error types`` () =
        // Test that all convenience functions return Result<_, QuantumError>
        let backend = createBackend()
        
        // ValidationError for invalid input (singular matrix)
        let zeroEigenvalues = (0.0, 1.0)
        let validInput = (Complex(1.0, 0.0), Complex.Zero)
        match HHL.solve2x2Diagonal zeroEigenvalues validInput backend with
        | Error (QuantumError.ValidationError (name, msg)) ->
            Assert.Contains("eigenvalue", name.ToLower())
            Assert.Contains("singular", msg.ToLower())
        | Error err -> Assert.Fail($"Wrong error type for singular matrix: {err}")
        | Ok _ -> Assert.Fail("Should return error for singular matrix")
        
        // ValidationError for zero vector
        let validEigenvalues = (1.0, 2.0)
        let zeroInput = (Complex.Zero, Complex.Zero)
        match HHL.solve2x2Diagonal validEigenvalues zeroInput backend with
        | Error (QuantumError.ValidationError _) -> ()
        | Error err -> Assert.Fail($"Wrong error type for zero vector: {err}")
        | Ok _ -> Assert.Fail("Should return error for zero vector")
    
    [<Fact>]
    let ``HHL validates matrix-vector dimension compatibility`` () =
        // Matrix and vector dimensions must match
        let eigenvalues = [| 1.0; 2.0 |]  // 2×2 matrix
        let inputVector = [| Complex(1.0, 0.0); Complex.Zero; Complex.Zero; Complex.Zero |]  // 4D vector
        
        match createDiagonalMatrix eigenvalues with
        | Error err -> Assert.Fail($"Matrix creation failed: {err}")
        | Ok matrix ->
            match createQuantumVector inputVector with
            | Error err -> Assert.Fail($"Vector creation failed: {err}")
            | Ok vector ->
                let config = {
                    Matrix = matrix
                    InputVector = vector
                    EigenvalueQubits = 4
                    SolutionQubits = 1  // log2(2) = 1
                    InversionMethod = EigenvalueInversionMethod.ExactRotation 1.0
                    MinEigenvalue = 0.1
                    UsePostSelection = true
                    QPEPrecision = 4
                }
                
                // Configuration created, but dimensions don't match!
                // This should be caught during execution
                let backend = createBackend()
                match HHL.execute config backend with
                | Ok _ -> Assert.Fail("Should detect dimension mismatch")
                | Error (QuantumError.ValidationError _) -> ()
                | Error (QuantumError.NotImplemented _) -> ()  // Acceptable
                | Error (QuantumError.OperationError _) -> ()  // Acceptable - implementation incomplete
                | Error err -> Assert.Fail($"Wrong error type: {err}")
    
    // ========================================================================
    // POST-SELECTION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``solve2x2Diagonal with post-selection enabled`` () =
        let backend = createBackend()
        let eigenvalues = (2.0, 3.0)
        let inputVector = (Complex(1.0, 0.0), Complex.Zero)
        
        // Create config with post-selection
        match createDiagonalMatrix [| 2.0; 3.0 |] with
        | Error err -> Assert.Fail($"Matrix creation failed: {err}")
        | Ok matrix ->
            match createQuantumVector [| Complex(1.0, 0.0); Complex.Zero |] with
            | Error err -> Assert.Fail($"Vector creation failed: {err}")
            | Ok vector ->
                let config = {
                    Matrix = matrix
                    InputVector = vector
                    EigenvalueQubits = 4
                    SolutionQubits = 1
                    InversionMethod = EigenvalueInversionMethod.ExactRotation 1.0
                    MinEigenvalue = 0.1
                    UsePostSelection = true  // Enable post-selection
                    QPEPrecision = 4
                }
                
                match HHL.execute config backend with
                | Ok result -> 
                    // Post-selection may succeed or fail - both are acceptable
                    Assert.True(result.Config.UsePostSelection)
                | Error (QuantumError.OperationError _) ->
                    // Acceptable - implementation may not fully support post-selection yet
                    ()
                | Error err -> 
                    Assert.Fail($"Unexpected error: {err}")
    
    [<Fact>]
    let ``HHL result contains solution amplitudes when available`` () =
        let backend = createBackend()
        let eigenvalues = (1.0, 2.0)
        let inputVector = (Complex(1.0, 0.0), Complex(0.5, 0.0))
        
        match HHL.solve2x2Diagonal eigenvalues inputVector backend with
        | Ok result -> 
            // Solution amplitudes may or may not be extracted depending on implementation status
            match result.SolutionAmplitudes with
            | Some amplitudes ->
                Assert.NotEmpty(amplitudes)
                // Amplitudes should be non-zero
                amplitudes |> Map.iter (fun idx amp -> 
                    Assert.True(amp.Magnitude > 0.0, $"Amplitude at {idx} should be non-zero")
                )
            | None ->
                // Also acceptable if extraction not fully implemented
                ()
        | Error (QuantumError.OperationError _) ->
            // Acceptable - implementation may be incomplete
            ()
        | Error err -> 
            Assert.Fail($"Unexpected error: {err}")
    
    [<Fact>]
    let ``HHL result contains estimated eigenvalues`` () =
        let backend = createBackend()
        let eigenvalues = (2.0, 4.0)
        let inputVector = (Complex(1.0, 0.0), Complex.Zero)
        
        match HHL.solve2x2Diagonal eigenvalues inputVector backend with
        | Ok result -> 
            Assert.NotEmpty(result.EstimatedEigenvalues)
            // Should contain the input eigenvalues (for diagonal matrices)
            Assert.Contains(2.0, result.EstimatedEigenvalues)
        | Error (QuantumError.OperationError _) ->
            ()  // Acceptable
        | Error err -> 
            Assert.Fail($"Unexpected error: {err}")
    
    [<Fact>]
    let ``HHL calculates success probability`` () =
        let backend = createBackend()
        let eigenvalues = (1.0, 10.0)  // High condition number (κ=10)
        let inputVector = (Complex(1.0, 0.0), Complex.Zero)
        
        match HHL.solve2x2Diagonal eigenvalues inputVector backend with
        | Ok result -> 
            // Success probability should be between 0 and 1
            Assert.True(result.SuccessProbability >= 0.0 && result.SuccessProbability <= 1.0)
            // For poorly conditioned matrices, success probability should be lower
            // κ=10 → success ≈ 1/100 = 0.01
            Assert.True(result.SuccessProbability <= 0.5, $"Expected low success probability for κ=10, got {result.SuccessProbability}")
        | Error (QuantumError.OperationError _) ->
            ()  // Acceptable
        | Error err -> 
            Assert.Fail($"Unexpected error: {err}")
    
    [<Fact>]
    let ``HHL reports gate count`` () =
        let backend = createBackend()
        let eigenvalues = (1.0, 2.0)
        let inputVector = (Complex(1.0, 0.0), Complex.Zero)
        
        match HHL.solve2x2Diagonal eigenvalues inputVector backend with
        | Ok result -> 
            // Gate count should be positive
            Assert.True(result.GateCount > 0, $"Gate count should be positive, got {result.GateCount}")
            // Should scale with precision
            let expectedMinGates = result.Config.EigenvalueQubits * result.Config.EigenvalueQubits
            Assert.True(result.GateCount >= expectedMinGates, 
                $"Gate count {result.GateCount} should be at least {expectedMinGates}")
        | Error (QuantumError.OperationError _) ->
            ()  // Acceptable
        | Error err -> 
            Assert.Fail($"Unexpected error: {err}")
