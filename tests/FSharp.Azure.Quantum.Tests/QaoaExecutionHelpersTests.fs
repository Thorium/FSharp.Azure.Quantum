module FSharp.Azure.Quantum.Tests.QaoaExecutionHelpersTests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.QaoaExecutionHelpers
open FSharp.Azure.Quantum.Backends

// Helper to create local backend for tests
let private createLocalBackend () : BackendAbstraction.IQuantumBackend =
    LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend

// ============================================================================
// CONFIGURATION PRESET TESTS
// ============================================================================

module ConfigTests =

    [<Fact>]
    let ``defaultConfig has expected values`` () =
        Assert.Equal(2, defaultConfig.NumLayers)
        Assert.Equal(100, defaultConfig.OptimizationShots)
        Assert.Equal(1000, defaultConfig.FinalShots)
        Assert.True(defaultConfig.EnableOptimization)
        Assert.True(defaultConfig.EnableConstraintRepair)
        Assert.Equal(200, defaultConfig.MaxOptimizationIterations)

    [<Fact>]
    let ``fastConfig prioritizes speed over quality`` () =
        Assert.Equal(1, fastConfig.NumLayers)
        Assert.Equal(50, fastConfig.OptimizationShots)
        Assert.Equal(500, fastConfig.FinalShots)
        Assert.False(fastConfig.EnableOptimization)
        Assert.True(fastConfig.EnableConstraintRepair)
        Assert.Equal(100, fastConfig.MaxOptimizationIterations)

    [<Fact>]
    let ``highQualityConfig prioritizes quality over speed`` () =
        Assert.Equal(3, highQualityConfig.NumLayers)
        Assert.Equal(200, highQualityConfig.OptimizationShots)
        Assert.Equal(2000, highQualityConfig.FinalShots)
        Assert.True(highQualityConfig.EnableOptimization)
        Assert.True(highQualityConfig.EnableConstraintRepair)
        Assert.Equal(500, highQualityConfig.MaxOptimizationIterations)

    [<Fact>]
    let ``fastConfig has fewer layers than defaultConfig`` () =
        Assert.True(fastConfig.NumLayers < defaultConfig.NumLayers)

    [<Fact>]
    let ``highQualityConfig has more layers than defaultConfig`` () =
        Assert.True(highQualityConfig.NumLayers > defaultConfig.NumLayers)

    [<Fact>]
    let ``fastConfig has fewer shots than defaultConfig`` () =
        Assert.True(fastConfig.FinalShots < defaultConfig.FinalShots)
        Assert.True(fastConfig.OptimizationShots < defaultConfig.OptimizationShots)

    [<Fact>]
    let ``highQualityConfig has more shots than defaultConfig`` () =
        Assert.True(highQualityConfig.FinalShots > defaultConfig.FinalShots)
        Assert.True(highQualityConfig.OptimizationShots > defaultConfig.OptimizationShots)

// ============================================================================
// evaluateQubo TESTS
// ============================================================================

module EvaluateQuboTests =

    [<Fact>]
    let ``evaluateQubo returns zero for all-zero bitstring`` () =
        let qubo = Array2D.init 3 3 (fun i j -> if i = j then -1.0 else 0.5)
        let bits = [| 0; 0; 0 |]
        let energy = evaluateQubo qubo bits
        Assert.Equal(0.0, energy, 10)

    [<Fact>]
    let ``evaluateQubo returns diagonal value for single-bit set`` () =
        // 2x2 QUBO with diagonal [-3, -5] and off-diagonal 2
        let qubo = array2D [| [| -3.0; 2.0 |]; [| 0.0; -5.0 |] |]
        // Only bit 0 set: energy = Q[0,0] * 1 * 1 = -3.0
        let bits = [| 1; 0 |]
        let energy = evaluateQubo qubo bits
        Assert.Equal(-3.0, energy, 10)

    [<Fact>]
    let ``evaluateQubo returns correct energy for all-ones bitstring`` () =
        // 2x2 QUBO: Q = [[-3, 2], [0, -5]]
        // Energy for [1,1] = -3*1*1 + 2*1*1 + 0*1*1 + (-5)*1*1 = -6.0
        let qubo = array2D [| [| -3.0; 2.0 |]; [| 0.0; -5.0 |] |]
        let bits = [| 1; 1 |]
        let energy = evaluateQubo qubo bits
        Assert.Equal(-6.0, energy, 10)

    [<Fact>]
    let ``evaluateQubo handles symmetric QUBO correctly`` () =
        // Symmetric 2x2 QUBO: Q = [[-1, 4], [4, -2]]
        // Energy for [1,1] = -1 + 4 + 4 + (-2) = 5.0
        let qubo = array2D [| [| -1.0; 4.0 |]; [| 4.0; -2.0 |] |]
        let bits = [| 1; 1 |]
        let energy = evaluateQubo qubo bits
        Assert.Equal(5.0, energy, 10)

    [<Fact>]
    let ``evaluateQubo with upper-triangular QUBO for MaxCut`` () =
        // MaxCut QUBO for 2-node graph with edge (0,1):
        // Q = [[-1, 2], [0, -1]]
        // [0,0] -> energy = 0 (no nodes selected)
        // [1,0] -> energy = -1 (node 0 in cut)
        // [0,1] -> energy = -1 (node 1 in cut)
        // [1,1] -> energy = -1 + 2 + 0 + (-1) = 0 (both same side)
        let qubo = array2D [| [| -1.0; 2.0 |]; [| 0.0; -1.0 |] |]
        Assert.Equal(0.0, evaluateQubo qubo [| 0; 0 |], 10)
        Assert.Equal(-1.0, evaluateQubo qubo [| 1; 0 |], 10)
        Assert.Equal(-1.0, evaluateQubo qubo [| 0; 1 |], 10)
        Assert.Equal(0.0, evaluateQubo qubo [| 1; 1 |], 10)

    [<Fact>]
    let ``evaluateQubo with 1x1 QUBO`` () =
        let qubo = array2D [| [| -7.0 |] |]
        Assert.Equal(0.0, evaluateQubo qubo [| 0 |], 10)
        Assert.Equal(-7.0, evaluateQubo qubo [| 1 |], 10)

    [<Fact>]
    let ``evaluateQubo with zero QUBO matrix returns zero`` () =
        let qubo = Array2D.zeroCreate 3 3
        let bits = [| 1; 1; 1 |]
        Assert.Equal(0.0, evaluateQubo qubo bits, 10)

// ============================================================================
// quboMapToArray TESTS
// ============================================================================

module QuboMapToArrayTests =

    [<Fact>]
    let ``quboMapToArray converts sparse QuboMatrix to dense array`` () =
        // Build a QuboMatrix with known entries
        let sparse = Map.ofList [ ((0, 0), -3.0); ((0, 1), 2.0); ((1, 1), -5.0) ]
        let quboMatrix : GraphOptimization.QuboMatrix = {
            NumVariables = 2
            Q = sparse
        }
        let dense = quboMapToArray quboMatrix
        Assert.Equal(-3.0, dense.[0, 0], 10)
        Assert.Equal(2.0, dense.[0, 1], 10)
        Assert.Equal(0.0, dense.[1, 0], 10)
        Assert.Equal(-5.0, dense.[1, 1], 10)

    [<Fact>]
    let ``quboMapToArray fills missing entries with zero`` () =
        // Only set one diagonal element; rest should be zero
        let sparse = Map.ofList [ ((1, 1), 42.0) ]
        let quboMatrix : GraphOptimization.QuboMatrix = {
            NumVariables = 3
            Q = sparse
        }
        let dense = quboMapToArray quboMatrix
        Assert.Equal(0.0, dense.[0, 0], 10)
        Assert.Equal(0.0, dense.[0, 1], 10)
        Assert.Equal(0.0, dense.[0, 2], 10)
        Assert.Equal(0.0, dense.[1, 0], 10)
        Assert.Equal(42.0, dense.[1, 1], 10)
        Assert.Equal(0.0, dense.[1, 2], 10)
        Assert.Equal(0.0, dense.[2, 0], 10)
        Assert.Equal(0.0, dense.[2, 1], 10)
        Assert.Equal(0.0, dense.[2, 2], 10)

    [<Fact>]
    let ``quboMapToArray produces correct dimensions`` () =
        let quboMatrix : GraphOptimization.QuboMatrix = {
            NumVariables = 5
            Q = Map.empty
        }
        let dense = quboMapToArray quboMatrix
        Assert.Equal(5, Array2D.length1 dense)
        Assert.Equal(5, Array2D.length2 dense)

// ============================================================================
// Qubo.toDenseArray TESTS
// ============================================================================

module QuboToDenseArrayTests =

    [<Fact>]
    let ``toDenseArray converts sparse map to dense array`` () =
        let sparse = Map.ofList [ ((0, 0), -1.0); ((0, 1), 3.0); ((1, 1), -2.0) ]
        let dense = Qubo.toDenseArray 2 sparse
        Assert.Equal(-1.0, dense.[0, 0], 10)
        Assert.Equal(3.0, dense.[0, 1], 10)
        Assert.Equal(0.0, dense.[1, 0], 10)
        Assert.Equal(-2.0, dense.[1, 1], 10)

    [<Fact>]
    let ``toDenseArray fills unset entries with zero`` () =
        let sparse = Map.ofList [ ((2, 2), 7.0) ]
        let dense = Qubo.toDenseArray 4 sparse
        // Only (2,2) should be non-zero
        for i in 0 .. 3 do
            for j in 0 .. 3 do
                if i = 2 && j = 2 then
                    Assert.Equal(7.0, dense.[i, j], 10)
                else
                    Assert.Equal(0.0, dense.[i, j], 10)

    [<Fact>]
    let ``toDenseArray with empty map produces all-zero array`` () =
        let dense = Qubo.toDenseArray 3 Map.empty
        for i in 0 .. 2 do
            for j in 0 .. 2 do
                Assert.Equal(0.0, dense.[i, j], 10)

    [<Fact>]
    let ``toDenseArray round-trip preserves values`` () =
        // Build sparse, convert to dense, then verify entries match
        let entries = [ ((0, 1), 2.5); ((1, 0), -1.3); ((2, 2), 4.0) ]
        let sparse = Map.ofList entries
        let dense = Qubo.toDenseArray 3 sparse
        for ((i, j), v) in entries do
            Assert.Equal(v, dense.[i, j], 10)

    [<Fact>]
    let ``toDenseArray and quboMapToArray produce identical output for same data`` () =
        // Same sparse data fed through both conversion paths
        let sparse = Map.ofList [ ((0, 0), -3.0); ((0, 1), 2.0); ((1, 1), -5.0) ]
        
        // Path 1: Qubo.toDenseArray
        let dense1 = Qubo.toDenseArray 2 sparse
        
        // Path 2: quboMapToArray via QuboMatrix
        let quboMatrix : GraphOptimization.QuboMatrix = {
            NumVariables = 2
            Q = sparse
        }
        let dense2 = quboMapToArray quboMatrix
        
        // Should be identical
        for i in 0 .. 1 do
            for j in 0 .. 1 do
                Assert.Equal(dense1.[i, j], dense2.[i, j], 10)

// ============================================================================
// Qubo.atMostOneConstraint TESTS
// ============================================================================

module AtMostOneConstraintTests =

    [<Fact>]
    let ``atMostOneConstraint produces off-diagonal penalty terms`` () =
        // 3 variables: indices [0; 1; 2], penalty 10.0
        let result = Qubo.atMostOneConstraint [0; 1; 2] 10.0
        // Should have pairs (0,1), (0,2), (1,2)
        Assert.Equal(10.0, result |> Map.find (0, 1))
        Assert.Equal(10.0, result |> Map.find (0, 2))
        Assert.Equal(10.0, result |> Map.find (1, 2))

    [<Fact>]
    let ``atMostOneConstraint produces no diagonal terms`` () =
        let result = Qubo.atMostOneConstraint [0; 1; 2] 10.0
        // No diagonal entries
        Assert.False(result |> Map.containsKey (0, 0))
        Assert.False(result |> Map.containsKey (1, 1))
        Assert.False(result |> Map.containsKey (2, 2))

    [<Fact>]
    let ``atMostOneConstraint with 2 variables produces one pair`` () =
        let result = Qubo.atMostOneConstraint [5; 8] 7.0
        Assert.Equal(1, result.Count)
        Assert.Equal(7.0, result |> Map.find (5, 8))

    [<Fact>]
    let ``atMostOneConstraint with 1 variable produces empty map`` () =
        let result = Qubo.atMostOneConstraint [0] 10.0
        Assert.True(result.IsEmpty)

    [<Fact>]
    let ``atMostOneConstraint with empty list produces empty map`` () =
        let result = Qubo.atMostOneConstraint [] 10.0
        Assert.True(result.IsEmpty)

    [<Fact>]
    let ``atMostOneConstraint allows zero bits set`` () =
        // Convert to dense and check that [0,0,0] has energy 0
        let result = Qubo.atMostOneConstraint [0; 1; 2] 10.0
        let dense = Qubo.toDenseArray 3 result
        let energy = evaluateQubo dense [| 0; 0; 0 |]
        Assert.Equal(0.0, energy, 10)

    [<Fact>]
    let ``atMostOneConstraint allows exactly one bit set`` () =
        let result = Qubo.atMostOneConstraint [0; 1; 2] 10.0
        let dense = Qubo.toDenseArray 3 result
        // Each single-bit solution should have zero energy (no penalty)
        Assert.Equal(0.0, evaluateQubo dense [| 1; 0; 0 |], 10)
        Assert.Equal(0.0, evaluateQubo dense [| 0; 1; 0 |], 10)
        Assert.Equal(0.0, evaluateQubo dense [| 0; 0; 1 |], 10)

    [<Fact>]
    let ``atMostOneConstraint penalizes two bits set`` () =
        let result = Qubo.atMostOneConstraint [0; 1; 2] 10.0
        let dense = Qubo.toDenseArray 3 result
        // Two bits set: should get penalty 10.0
        Assert.Equal(10.0, evaluateQubo dense [| 1; 1; 0 |], 10)
        Assert.Equal(10.0, evaluateQubo dense [| 1; 0; 1 |], 10)
        Assert.Equal(10.0, evaluateQubo dense [| 0; 1; 1 |], 10)

    [<Fact>]
    let ``atMostOneConstraint penalizes three bits set`` () =
        let result = Qubo.atMostOneConstraint [0; 1; 2] 10.0
        let dense = Qubo.toDenseArray 3 result
        // Three bits set: penalty = 10 * 3 pairs = 30.0
        Assert.Equal(30.0, evaluateQubo dense [| 1; 1; 1 |], 10)

// ============================================================================
// executeQaoaCircuit TESTS
// ============================================================================

module ExecuteQaoaCircuitTests =

    [<Fact>]
    let ``executeQaoaCircuit returns measurements with correct count`` () =
        // Minimal 2-qubit QUBO
        let qubo = array2D [| [| -1.0; 2.0 |]; [| 0.0; -1.0 |] |]
        let problemHam = QaoaCircuit.ProblemHamiltonian.fromQubo qubo
        let mixerHam = QaoaCircuit.MixerHamiltonian.create 2
        let parameters = [| (0.5, 0.3) |]  // 1 layer
        let backend = createLocalBackend ()

        let result = executeQaoaCircuit backend problemHam mixerHam parameters 100
        match result with
        | Ok measurements ->
            Assert.Equal(100, measurements.Length)
            // Each measurement should have 2 bits
            for m in measurements do
                Assert.Equal(2, m.Length)
                // Each bit should be 0 or 1
                for b in m do
                    Assert.True(b = 0 || b = 1)
        | Error err ->
            Assert.Fail($"Expected Ok but got Error: {err}")

    [<Fact>]
    let ``executeQaoaCircuit returns valid bitstrings for 3-qubit problem`` () =
        let qubo = Array2D.init 3 3 (fun i j -> if i = j then -1.0 else 0.5)
        let problemHam = QaoaCircuit.ProblemHamiltonian.fromQubo qubo
        let mixerHam = QaoaCircuit.MixerHamiltonian.create 3
        let parameters = [| (0.7, 0.4); (0.3, 0.2) |]  // 2 layers
        let backend = createLocalBackend ()

        let result = executeQaoaCircuit backend problemHam mixerHam parameters 50
        match result with
        | Ok measurements ->
            Assert.Equal(50, measurements.Length)
            for m in measurements do
                Assert.Equal(3, m.Length)
        | Error err ->
            Assert.Fail($"Expected Ok but got Error: {err}")

// ============================================================================
// createObjectiveFunction TESTS
// ============================================================================

module CreateObjectiveFunctionTests =

    [<Fact>]
    let ``createObjectiveFunction returns finite value`` () =
        let qubo = array2D [| [| -1.0; 2.0 |]; [| 0.0; -1.0 |] |]
        let problemHam = QaoaCircuit.ProblemHamiltonian.fromQubo qubo
        let mixerHam = QaoaCircuit.MixerHamiltonian.create 2
        let backend = createLocalBackend ()

        let objective = createObjectiveFunction backend qubo problemHam mixerHam 1 50
        let value = objective [| 0.5; 0.3 |]
        Assert.True(System.Double.IsFinite value, $"Expected finite value but got {value}")

    [<Fact>]
    let ``createObjectiveFunction returns different values for different parameters`` () =
        let qubo = array2D [| [| -1.0; 2.0 |]; [| 0.0; -1.0 |] |]
        let problemHam = QaoaCircuit.ProblemHamiltonian.fromQubo qubo
        let mixerHam = QaoaCircuit.MixerHamiltonian.create 2
        let backend = createLocalBackend ()

        let objective = createObjectiveFunction backend qubo problemHam mixerHam 1 200
        // Try several distinct parameter sets; at least some should differ
        let values = 
            [| [| 0.1; 0.1 |]; [| 1.0; 0.5 |]; [| 2.0; 1.0 |]; [| 0.5; 0.8 |] |]
            |> Array.map objective
        let distinctCount = values |> Array.distinct |> Array.length
        // With 200 shots and 4 different parameter settings, we expect some variation
        Assert.True(distinctCount >= 2, $"Expected at least 2 distinct values but got {distinctCount}")

// ============================================================================
// executeQaoaWithGridSearch INTEGRATION TESTS
// ============================================================================

module GridSearchIntegrationTests =

    [<Fact>]
    let ``executeQaoaWithGridSearch finds solution for trivial 1-qubit problem`` () =
        // QUBO: Q = [[-1.0]] -> minimum at x=1
        let qubo = array2D [| [| -1.0 |] |]
        let backend = createLocalBackend ()
        let config = { fastConfig with NumLayers = 1; FinalShots = 200 }

        let result = executeQaoaWithGridSearch backend qubo config
        match result with
        | Ok (solution, parameters) ->
            Assert.Equal(1, solution.Length)
            // For this trivial QUBO, optimal is x=1 with energy -1
            // QAOA may not always find the exact optimum, but should return a valid result
            Assert.True(solution.[0] = 0 || solution.[0] = 1)
            Assert.True(parameters.Length > 0)
        | Error err ->
            Assert.Fail($"Expected Ok but got Error: {err}")

    [<Fact>]
    let ``executeQaoaWithGridSearch returns valid solution for 2-qubit MaxCut`` () =
        // MaxCut QUBO for K2 (complete graph on 2 nodes):
        // Q = [[-1, 2], [0, -1]]
        // Optimal: [1,0] or [0,1] with energy -1
        let qubo = array2D [| [| -1.0; 2.0 |]; [| 0.0; -1.0 |] |]
        let backend = createLocalBackend ()
        let config = { fastConfig with NumLayers = 1; FinalShots = 500 }

        let result = executeQaoaWithGridSearch backend qubo config
        match result with
        | Ok (solution, parameters) ->
            Assert.Equal(2, solution.Length)
            let energy = evaluateQubo qubo solution
            // QAOA should find a solution with non-positive energy for MaxCut
            // (at worst [0,0] or [1,1] with energy 0, ideally [1,0] or [0,1] with energy -1)
            Assert.True(energy <= 0.0, $"Expected non-positive energy but got {energy}")
            Assert.True(parameters.Length >= 1)
        | Error err ->
            Assert.Fail($"Expected Ok but got Error: {err}")

    [<Fact>]
    let ``executeQaoaWithGridSearch respects numLayers in parameters`` () =
        let qubo = array2D [| [| -1.0; 2.0 |]; [| 0.0; -1.0 |] |]
        let backend = createLocalBackend ()
        let config = { fastConfig with NumLayers = 2; FinalShots = 200 }

        let result = executeQaoaWithGridSearch backend qubo config
        match result with
        | Ok (_, parameters) ->
            Assert.Equal(2, parameters.Length)  // 2 layers = 2 (gamma, beta) pairs
        | Error err ->
            Assert.Fail($"Expected Ok but got Error: {err}")

// ============================================================================
// executeQaoaWithOptimization INTEGRATION TESTS
// ============================================================================

module OptimizationIntegrationTests =

    [<Fact>]
    let ``executeQaoaWithOptimization returns valid result for 2-qubit problem`` () =
        let qubo = array2D [| [| -1.0; 2.0 |]; [| 0.0; -1.0 |] |]
        let backend = createLocalBackend ()
        let config = { 
            defaultConfig with 
                NumLayers = 1
                OptimizationShots = 50
                FinalShots = 200
                MaxOptimizationIterations = 50 
        }

        let result = executeQaoaWithOptimization backend qubo config
        match result with
        | Ok (solution, parameters, _converged) ->
            Assert.Equal(2, solution.Length)
            Assert.Equal(1, parameters.Length)  // 1 layer
            for b in solution do
                Assert.True(b = 0 || b = 1, $"Expected 0 or 1 but got {b}")
        | Error err ->
            Assert.Fail($"Expected Ok but got Error: {err}")

    [<Fact>]
    let ``executeQaoaWithOptimization returns parameters matching numLayers`` () =
        let qubo = array2D [| [| -1.0; 0.0 |]; [| 0.0; -1.0 |] |]
        let backend = createLocalBackend ()
        let config = { 
            defaultConfig with 
                NumLayers = 2
                OptimizationShots = 30
                FinalShots = 100
                MaxOptimizationIterations = 30 
        }

        let result = executeQaoaWithOptimization backend qubo config
        match result with
        | Ok (_, parameters, _) ->
            Assert.Equal(2, parameters.Length)  // 2 layers = 2 (gamma, beta) pairs
        | Error err ->
            Assert.Fail($"Expected Ok but got Error: {err}")
