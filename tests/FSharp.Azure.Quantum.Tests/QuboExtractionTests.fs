namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Core.QaoaCircuit
open FSharp.Azure.Quantum.Algorithms.QuboExtraction

/// Unit tests for QUBO extraction from QAOA circuits
///
/// Tests cover:
/// - QUBO extraction from Problem Hamiltonian
/// - QUBO extraction from QAOA circuits
/// - Validation functions (symmetry, variable count)
/// - Dense/sparse array conversion
/// - Edge cases and error handling
module QuboExtractionTests =
    
    // ============================================================================
    // HELPER FUNCTIONS FOR TEST DATA
    // ============================================================================
    
    /// Create a simple Problem Hamiltonian for testing
    let createSimpleHamiltonian () : ProblemHamiltonian =
        // Hamiltonian: -1.0 * Z_0 + 0.5 * Z_0*Z_1
        // Expected QUBO: Q_00 = 2.0, Q_01 = 2.0
        {
            NumQubits = 2
            Terms = [|
                { Coefficient = -1.0; QubitsIndices = [| 0 |]; PauliOperators = [| PauliZ |] }
                { Coefficient = 0.5; QubitsIndices = [| 0; 1 |]; PauliOperators = [| PauliZ; PauliZ |] }
            |]
        }
    
    /// Create a MaxCut-style Problem Hamiltonian
    let createMaxCutHamiltonian () : ProblemHamiltonian =
        // MaxCut QUBO for edges: (0,1,5.0), (1,2,3.0)
        // QUBO: Q_00=5, Q_11=8, Q_22=3, Q_01=-5, Q_12=-3
        // Hamiltonian: -2.5*Z_0 - 4*Z_1 - 1.5*Z_2 + 1.25*Z_0*Z_1 + 0.75*Z_1*Z_2
        {
            NumQubits = 3
            Terms = [|
                { Coefficient = -2.5; QubitsIndices = [| 0 |]; PauliOperators = [| PauliZ |] }
                { Coefficient = -4.0; QubitsIndices = [| 1 |]; PauliOperators = [| PauliZ |] }
                { Coefficient = -1.5; QubitsIndices = [| 2 |]; PauliOperators = [| PauliZ |] }
                { Coefficient = 1.25; QubitsIndices = [| 0; 1 |]; PauliOperators = [| PauliZ; PauliZ |] }
                { Coefficient = 0.75; QubitsIndices = [| 1; 2 |]; PauliOperators = [| PauliZ; PauliZ |] }
            |]
        }
    
    // ============================================================================
    // QUBO EXTRACTION FROM PROBLEM HAMILTONIAN TESTS
    // ============================================================================
    
    [<Fact>]
    let ``fromProblemHamiltonian extracts single Z term correctly`` () =
        let hamiltonian : ProblemHamiltonian = {
            NumQubits = 1
            Terms = [| { Coefficient = -1.0; QubitsIndices = [| 0 |]; PauliOperators = [| PauliZ |] } |]
        }
        
        let qubo = fromProblemHamiltonian hamiltonian
        
        // Expected: Q_00 = -2 * (-1.0) = 2.0
        Assert.True(Map.containsKey (0, 0) qubo, "QUBO should contain diagonal term")
        Assert.Equal(2.0, qubo.[(0, 0)], precision = 10)
    
    [<Fact>]
    let ``fromProblemHamiltonian extracts ZZ term correctly`` () =
        let hamiltonian : ProblemHamiltonian = {
            NumQubits = 2
            Terms = [| { Coefficient = 0.5; QubitsIndices = [| 0; 1 |]; PauliOperators = [| PauliZ; PauliZ |] } |]
        }
        
        let qubo = fromProblemHamiltonian hamiltonian
        
        // Expected: Q_01 = 4 * 0.5 = 2.0
        Assert.True(Map.containsKey (0, 1) qubo, "QUBO should contain off-diagonal term")
        Assert.Equal(2.0, qubo.[(0, 1)], precision = 10)
    
    [<Fact>]
    let ``fromProblemHamiltonian handles mixed terms`` () =
        let hamiltonian = createSimpleHamiltonian ()
        
        let qubo = fromProblemHamiltonian hamiltonian
        
        // Expected: Q_00 = 2.0, Q_01 = 2.0
        Assert.Equal(2, Map.count qubo)
        Assert.Equal(2.0, qubo.[(0, 0)], precision = 10)
        Assert.Equal(2.0, qubo.[(0, 1)], precision = 10)
    
    [<Fact>]
    let ``fromProblemHamiltonian normalizes indices to upper triangle`` () =
        let hamiltonian : ProblemHamiltonian = {
            NumQubits = 2
            Terms = [| { Coefficient = 0.5; QubitsIndices = [| 1; 0 |]; PauliOperators = [| PauliZ; PauliZ |] } |]
        }
        
        let qubo = fromProblemHamiltonian hamiltonian
        
        // Should store as (0, 1) not (1, 0)
        Assert.True(Map.containsKey (0, 1) qubo, "Should normalize to upper triangle (0,1)")
        Assert.False(Map.containsKey (1, 0) qubo, "Should not have lower triangle (1,0)")
    
    [<Fact>]
    let ``fromProblemHamiltonian accumulates multiple terms for same indices`` () =
        let hamiltonian : ProblemHamiltonian = {
            NumQubits = 2
            Terms = [|
                { Coefficient = 0.5; QubitsIndices = [| 0; 1 |]; PauliOperators = [| PauliZ; PauliZ |] }
                { Coefficient = 0.25; QubitsIndices = [| 0; 1 |]; PauliOperators = [| PauliZ; PauliZ |] }
            |]
        }
        
        let qubo = fromProblemHamiltonian hamiltonian
        
        // Expected: Q_01 = 4 * (0.5 + 0.25) = 3.0
        Assert.Equal(3.0, qubo.[(0, 1)], precision = 10)
    
    [<Fact>]
    let ``fromProblemHamiltonian handles MaxCut problem`` () =
        let hamiltonian = createMaxCutHamiltonian ()
        
        let qubo = fromProblemHamiltonian hamiltonian
        
        // Check diagonal terms
        Assert.Equal(5.0, qubo.[(0, 0)], precision = 10)
        Assert.Equal(8.0, qubo.[(1, 1)], precision = 10)
        Assert.Equal(3.0, qubo.[(2, 2)], precision = 10)
        
        // Check off-diagonal terms
        Assert.Equal(5.0, qubo.[(0, 1)], precision = 10)
        Assert.Equal(3.0, qubo.[(1, 2)], precision = 10)
    
    [<Fact>]
    let ``fromProblemHamiltonian rejects higher-order terms`` () =
        let hamiltonian : ProblemHamiltonian = {
            NumQubits = 3
            Terms = [| { Coefficient = 1.0; QubitsIndices = [| 0; 1; 2 |]; PauliOperators = [| PauliZ; PauliZ; PauliZ |] } |]
        }
        
        // Should throw exception for 3-qubit term
        Assert.Throws<System.Exception>(fun () -> 
            fromProblemHamiltonian hamiltonian |> ignore
        )
    
    [<Fact>]
    let ``fromProblemHamiltonian handles empty Hamiltonian`` () =
        let hamiltonian : ProblemHamiltonian = {
            NumQubits = 2
            Terms = [||]
        }
        
        let qubo = fromProblemHamiltonian hamiltonian
        
        Assert.True(Map.isEmpty qubo, "QUBO should be empty for empty Hamiltonian")
    
    // ============================================================================
    // QAOA CIRCUIT EXTRACTION TESTS
    // ============================================================================
    
    [<Fact>]
    let ``extractFromQaoaCircuit succeeds for valid circuit`` () =
        let hamiltonian = createSimpleHamiltonian ()
        let mixerHam = MixerHamiltonian.create 2
        let parameters = [| (0.5, 0.3) |]
        let circuit = QaoaCircuit.build hamiltonian mixerHam parameters
        
        let result = extractFromQaoaCircuit circuit
        
        Assert.True(result.IsOk, "Extraction should succeed")
        match result with
        | Ok qubo ->
            Assert.Equal(2, Map.count qubo)
            Assert.True(Map.containsKey (0, 0) qubo)
            Assert.True(Map.containsKey (0, 1) qubo)
        | Error e -> Assert.True(false, $"Should not fail: {e}")
    
    [<Fact>]
    let ``extractFromQaoaCircuit preserves QUBO values`` () =
        let hamiltonian = createMaxCutHamiltonian ()
        let mixerHam = MixerHamiltonian.create 3
        let circuit = QaoaCircuit.build hamiltonian mixerHam [| (0.5, 0.3) |]
        
        match extractFromQaoaCircuit circuit with
        | Ok qubo ->
            // Verify MaxCut QUBO structure
            Assert.Equal(5.0, qubo.[(0, 0)], precision = 10)
            Assert.Equal(8.0, qubo.[(1, 1)], precision = 10)
            Assert.Equal(5.0, qubo.[(0, 1)], precision = 10)
        | Error e -> Assert.True(false, $"Extraction failed: {e}")
    
    // ============================================================================
    // VALIDATION TESTS
    // ============================================================================
    
    [<Fact>]
    let ``validateSymmetric accepts symmetric QUBO`` () =
        let qubo = Map.ofList [
            ((0, 0), 2.0)
            ((0, 1), -5.0)
            ((1, 0), -5.0)  // Symmetric with (0,1)
            ((1, 1), 3.0)
        ]
        
        let result = validateSymmetric qubo
        
        Assert.True(result.IsOk, "Symmetric QUBO should pass validation")
    
    [<Fact>]
    let ``validateSymmetric accepts upper triangle only`` () =
        let qubo = Map.ofList [
            ((0, 0), 2.0)
            ((0, 1), -5.0)  // Upper triangle only
            ((1, 1), 3.0)
        ]
        
        let result = validateSymmetric qubo
        
        Assert.True(result.IsOk, "Upper triangle QUBO should pass (implicitly symmetric)")
    
    [<Fact>]
    let ``validateSymmetric rejects asymmetric QUBO`` () =
        let qubo = Map.ofList [
            ((0, 1), -5.0)
            ((1, 0), -3.0)  // Not symmetric!
        ]
        
        let result = validateSymmetric qubo
        
        Assert.True(result.IsError, "Asymmetric QUBO should fail validation")
        match result with
        | Error msg -> Assert.Contains("not symmetric", msg.Message)
        | Ok _ -> Assert.True(false, "Should have failed validation")
    
    [<Fact>]
    let ``validateSymmetric handles diagonal-only QUBO`` () =
        let qubo = Map.ofList [
            ((0, 0), 2.0)
            ((1, 1), 3.0)
            ((2, 2), 1.0)
        ]
        
        let result = validateSymmetric qubo
        
        Assert.True(result.IsOk, "Diagonal-only QUBO is trivially symmetric")
    
    // ============================================================================
    // UTILITY FUNCTION TESTS
    // ============================================================================
    
    [<Fact>]
    let ``getNumVariables returns correct count`` () =
        let qubo = Map.ofList [
            ((0, 0), 2.0)
            ((0, 2), -5.0)
            ((1, 1), 3.0)
        ]
        
        let numVars = getNumVariables qubo
        
        // Max index is 2, so 3 variables (0, 1, 2)
        Assert.Equal(3, numVars)
    
    [<Fact>]
    let ``getNumVariables handles empty QUBO`` () =
        let qubo = Map.empty
        
        let numVars = getNumVariables qubo
        
        Assert.Equal(0, numVars)
    
    [<Fact>]
    let ``getNumVariables handles sparse indices`` () =
        let qubo = Map.ofList [
            ((0, 0), 2.0)
            ((5, 5), 3.0)  // Gap in indices
        ]
        
        let numVars = getNumVariables qubo
        
        // Max index is 5, so 6 variables
        Assert.Equal(6, numVars)
    
    [<Fact>]
    let ``toDenseArray creates correct matrix`` () =
        let qubo = Map.ofList [
            ((0, 0), 2.0)
            ((0, 1), -5.0)
            ((1, 1), 3.0)
        ]
        
        let dense = toDenseArray qubo
        
        Assert.Equal(2, Array2D.length1 dense)
        Assert.Equal(2, Array2D.length2 dense)
        Assert.Equal(2.0, dense.[0, 0])
        Assert.Equal(-5.0, dense.[0, 1])
        Assert.Equal(-5.0, dense.[1, 0])  // Symmetrized
        Assert.Equal(3.0, dense.[1, 1])
    
    [<Fact>]
    let ``fromDenseArray creates sparse QUBO`` () =
        let dense = Array2D.init 3 3 (fun i j ->
            if i = j then float (i + 1)
            elif i < j then -float (i + j)
            else 0.0
        )
        
        let qubo = fromDenseArray dense
        
        // Should only contain upper triangle + diagonal
        Assert.True(Map.containsKey (0, 0) qubo)
        Assert.True(Map.containsKey (1, 1) qubo)
        Assert.True(Map.containsKey (2, 2) qubo)
        Assert.True(Map.containsKey (0, 1) qubo)
        Assert.True(Map.containsKey (0, 2) qubo)
        Assert.True(Map.containsKey (1, 2) qubo)
        Assert.False(Map.containsKey (1, 0) qubo, "Should not have lower triangle")
    
    [<Fact>]
    let ``fromDenseArray skips near-zero terms`` () =
        let dense = Array2D.init 2 2 (fun i j ->
            if i = 0 && j = 0 then 2.0
            else 1e-12  // Very small, should be skipped
        )
        
        let qubo = fromDenseArray dense
        
        // Should only contain (0,0)
        Assert.Equal(1, Map.count qubo)
        Assert.True(Map.containsKey (0, 0) qubo)
    
    [<Fact>]
    let ``toDenseArray and fromDenseArray are inverses`` () =
        let originalQubo = Map.ofList [
            ((0, 0), 2.0)
            ((0, 1), -5.0)
            ((1, 1), 3.0)
        ]
        
        let dense = toDenseArray originalQubo
        let roundtripQubo = fromDenseArray dense
        
        // Compare element by element (order may differ)
        for KeyValue((i, j), value) in originalQubo do
            let key = if i <= j then (i, j) else (j, i)
            Assert.True(Map.containsKey key roundtripQubo, $"Missing key {key}")
            Assert.Equal(value, roundtripQubo.[key], precision = 10)
    
    [<Fact>]
    let ``fromDenseArray rejects non-square matrix`` () =
        let nonSquare = Array2D.init 2 3 (fun _ _ -> 0.0)
        
        Assert.Throws<System.Exception>(fun () ->
            fromDenseArray nonSquare |> ignore
        )
    
    // ============================================================================
    // EDGE CASES AND INTEGRATION TESTS
    // ============================================================================
    
    [<Fact>]
    let ``extraction handles large QUBO (100 variables)`` () =
        let terms = [|
            for i in 0 .. 99 do
                yield { Coefficient = -float i / 2.0; QubitsIndices = [| i |]; PauliOperators = [| PauliZ |] }
            for i in 0 .. 98 do
                yield { Coefficient = float i / 4.0; QubitsIndices = [| i; i + 1 |]; PauliOperators = [| PauliZ; PauliZ |] }
        |]
        
        let hamiltonian : ProblemHamiltonian = { NumQubits = 100; Terms = terms }
        
        let qubo = fromProblemHamiltonian hamiltonian
        
        // Should have 100 diagonal + 99 off-diagonal = 199 terms
        Assert.Equal(199, Map.count qubo)
        Assert.Equal(100, getNumVariables qubo)
    
    [<Fact>]
    let ``extraction preserves negative coefficients`` () =
        let hamiltonian : ProblemHamiltonian = {
            NumQubits = 2
            Terms = [|
                { Coefficient = 2.5; QubitsIndices = [| 0 |]; PauliOperators = [| PauliZ |] }  // Positive
                { Coefficient = -1.25; QubitsIndices = [| 0; 1 |]; PauliOperators = [| PauliZ; PauliZ |] }  // Negative
            |]
        }
        
        let qubo = fromProblemHamiltonian hamiltonian
        
        // Q_00 = -2 * 2.5 = -5.0 (negative)
        // Q_01 = 4 * (-1.25) = -5.0 (negative)
        Assert.Equal(-5.0, qubo.[(0, 0)], precision = 10)
        Assert.Equal(-5.0, qubo.[(0, 1)], precision = 10)
    
    [<Fact>]
    let ``extraction handles very small coefficients`` () =
        let hamiltonian : ProblemHamiltonian = {
            NumQubits = 2
            Terms = [|
                { Coefficient = 1e-12; QubitsIndices = [| 0 |]; PauliOperators = [| PauliZ |] }
                { Coefficient = 1e-6; QubitsIndices = [| 0; 1 |]; PauliOperators = [| PauliZ; PauliZ |] }
            |]
        }
        
        let qubo = fromProblemHamiltonian hamiltonian
        
        // Both terms should be preserved (not skipped in extraction)
        Assert.Equal(2, Map.count qubo)
        Assert.True(abs qubo.[(0, 0)] > 0.0)
        Assert.True(abs qubo.[(0, 1)] > 0.0)
