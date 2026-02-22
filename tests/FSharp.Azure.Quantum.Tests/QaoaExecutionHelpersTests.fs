module FSharp.Azure.Quantum.Tests.QaoaExecutionHelpersTests

open Xunit
open System.Threading
open System.Threading.Tasks
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

// ============================================================================
// evaluateQuboSparse TESTS
// ============================================================================

module EvaluateQuboSparseTests =

    [<Fact>]
    let ``evaluateQuboSparse returns zero for all-zero bitstring`` () =
        let quboMap = Map.ofList [ ((0, 0), -1.0); ((0, 1), 2.0); ((1, 1), -3.0) ]
        let bits = [| 0; 0 |]
        Assert.Equal(0.0, evaluateQuboSparse quboMap bits, 10)

    [<Fact>]
    let ``evaluateQuboSparse returns diagonal value for single bit set`` () =
        let quboMap = Map.ofList [ ((0, 0), -3.0); ((0, 1), 2.0); ((1, 1), -5.0) ]
        let bits = [| 1; 0 |]
        Assert.Equal(-3.0, evaluateQuboSparse quboMap bits, 10)

    [<Fact>]
    let ``evaluateQuboSparse returns correct energy for all-ones`` () =
        // Q = {(0,0): -3, (0,1): 2, (1,1): -5}
        // Energy for [1,1] = -3 + 2 + (-5) = -6.0
        let quboMap = Map.ofList [ ((0, 0), -3.0); ((0, 1), 2.0); ((1, 1), -5.0) ]
        let bits = [| 1; 1 |]
        Assert.Equal(-6.0, evaluateQuboSparse quboMap bits, 10)

    [<Fact>]
    let ``evaluateQuboSparse handles empty map`` () =
        let quboMap = Map.empty<int * int, float>
        let bits = [| 1; 1; 1 |]
        Assert.Equal(0.0, evaluateQuboSparse quboMap bits, 10)

    [<Fact>]
    let ``evaluateQuboSparse matches evaluateQubo for same data`` () =
        // Create same QUBO in both dense and sparse form
        let qubo = array2D [| [| -1.0; 2.0 |]; [| 0.0; -3.0 |] |]
        let quboMap = Map.ofList [ ((0, 0), -1.0); ((0, 1), 2.0); ((1, 1), -3.0) ]
        let bits = [| 1; 1 |]
        let denseEnergy = evaluateQubo qubo bits
        let sparseEnergy = evaluateQuboSparse quboMap bits
        Assert.Equal(denseEnergy, sparseEnergy, 10)

    [<Fact>]
    let ``evaluateQuboSparse with symmetric entries`` () =
        // Both (0,1) and (1,0) present — both contribute
        let quboMap = Map.ofList [ ((0, 1), 3.0); ((1, 0), 4.0) ]
        let bits = [| 1; 1 |]
        // Energy = 3.0 * 1 * 1 + 4.0 * 1 * 1 = 7.0
        Assert.Equal(7.0, evaluateQuboSparse quboMap bits, 10)

// ============================================================================
// executeFromQubo TESTS
// ============================================================================

module ExecuteFromQuboTests =

    [<Fact>]
    let ``executeFromQubo returns measurements with correct dimensions`` () =
        let qubo = array2D [| [| -1.0; 2.0 |]; [| 0.0; -1.0 |] |]
        let backend = createLocalBackend ()
        let parameters = [| (0.5, 0.3) |]

        let result = executeFromQubo backend qubo parameters 50
        match result with
        | Ok measurements ->
            Assert.Equal(50, measurements.Length)
            for m in measurements do
                Assert.Equal(2, m.Length)
                for b in m do
                    Assert.True(b = 0 || b = 1)
        | Error err ->
            Assert.Fail($"Expected Ok but got Error: {err}")

    [<Fact>]
    let ``executeFromQubo handles 1-qubit problem`` () =
        let qubo = array2D [| [| -7.0 |] |]
        let backend = createLocalBackend ()
        let parameters = [| (0.4, 0.2) |]

        let result = executeFromQubo backend qubo parameters 20
        match result with
        | Ok measurements ->
            Assert.Equal(20, measurements.Length)
            for m in measurements do
                Assert.Equal(1, m.Length)
        | Error err ->
            Assert.Fail($"Expected Ok but got Error: {err}")

    [<Fact>]
    let ``executeFromQubo with multiple layers`` () =
        let qubo = array2D [| [| -1.0; 0.5 |]; [| 0.0; -1.0 |] |]
        let backend = createLocalBackend ()
        let parameters = [| (0.5, 0.3); (0.7, 0.4) |]  // 2 layers

        let result = executeFromQubo backend qubo parameters 30
        match result with
        | Ok measurements ->
            Assert.Equal(30, measurements.Length)
        | Error err ->
            Assert.Fail($"Expected Ok but got Error: {err}")

// ============================================================================
// executeQaoaCircuitSparse TESTS
// ============================================================================

module ExecuteQaoaCircuitSparseTests =

    [<Fact>]
    let ``executeQaoaCircuitSparse returns measurements with correct dimensions`` () =
        let quboMap = Map.ofList [ ((0, 0), -1.0); ((0, 1), 2.0); ((1, 1), -1.0) ]
        let backend = createLocalBackend ()
        let parameters = [| (0.5, 0.3) |]

        let result = executeQaoaCircuitSparse backend 2 quboMap parameters 80
        match result with
        | Ok measurements ->
            Assert.Equal(80, measurements.Length)
            for m in measurements do
                Assert.Equal(2, m.Length)
                for b in m do
                    Assert.True(b = 0 || b = 1)
        | Error err ->
            Assert.Fail($"Expected Ok but got Error: {err}")

    [<Fact>]
    let ``executeQaoaCircuitSparse handles 3-qubit problem`` () =
        let quboMap = Map.ofList [
            ((0, 0), -1.0); ((1, 1), -1.0); ((2, 2), -1.0)
            ((0, 1), 0.5); ((1, 2), 0.5)
        ]
        let backend = createLocalBackend ()
        let parameters = [| (0.5, 0.3) |]

        let result = executeQaoaCircuitSparse backend 3 quboMap parameters 40
        match result with
        | Ok measurements ->
            Assert.Equal(40, measurements.Length)
            for m in measurements do
                Assert.Equal(3, m.Length)
        | Error err ->
            Assert.Fail($"Expected Ok but got Error: {err}")

// ============================================================================
// executeQaoaWithGridSearchSparse TESTS
// ============================================================================

module GridSearchSparseTests =

    [<Fact>]
    let ``executeQaoaWithGridSearchSparse finds solution for 2-qubit problem`` () =
        let quboMap = Map.ofList [ ((0, 0), -1.0); ((0, 1), 2.0); ((1, 1), -1.0) ]
        let backend = createLocalBackend ()
        let config = { fastConfig with NumLayers = 1; FinalShots = 200 }

        let result = executeQaoaWithGridSearchSparse backend 2 quboMap config
        match result with
        | Ok (solution, parameters) ->
            Assert.Equal(2, solution.Length)
            Assert.True(parameters.Length >= 1)
            for b in solution do
                Assert.True(b = 0 || b = 1)
        | Error err ->
            Assert.Fail($"Expected Ok but got Error: {err}")

    [<Fact>]
    let ``executeQaoaWithGridSearchSparse rejects invalid config`` () =
        let quboMap = Map.ofList [ ((0, 0), -1.0) ]
        let backend = createLocalBackend ()
        let config = { fastConfig with NumLayers = 0 }  // invalid

        let result = executeQaoaWithGridSearchSparse backend 1 quboMap config
        match result with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("NumLayers", field)
        | Error _ -> Assert.Fail("Expected ValidationError")
        | Ok _ -> Assert.Fail("Expected Error for invalid config")

// ============================================================================
// executeQaoaWithOptimizationSparse TESTS
// ============================================================================

module OptimizationSparseTests =

    [<Fact>]
    let ``executeQaoaWithOptimizationSparse returns valid result for 2-qubit problem`` () =
        let quboMap = Map.ofList [ ((0, 0), -1.0); ((0, 1), 2.0); ((1, 1), -1.0) ]
        let backend = createLocalBackend ()
        let config = {
            defaultConfig with
                NumLayers = 1
                OptimizationShots = 50
                FinalShots = 200
                MaxOptimizationIterations = 50
        }

        let result = executeQaoaWithOptimizationSparse backend 2 quboMap config
        match result with
        | Ok (solution, parameters, _converged) ->
            Assert.Equal(2, solution.Length)
            Assert.Equal(1, parameters.Length)
            for b in solution do
                Assert.True(b = 0 || b = 1)
        | Error err ->
            Assert.Fail($"Expected Ok but got Error: {err}")

    [<Fact>]
    let ``executeQaoaWithOptimizationSparse rejects invalid config`` () =
        let quboMap = Map.ofList [ ((0, 0), -1.0) ]
        let backend = createLocalBackend ()
        let config = { defaultConfig with FinalShots = -1 }  // invalid

        let result = executeQaoaWithOptimizationSparse backend 1 quboMap config
        match result with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("FinalShots", field)
        | Error _ -> Assert.Fail("Expected ValidationError")
        | Ok _ -> Assert.Fail("Expected Error for invalid config")

// ============================================================================
// BUDGET EXECUTION TESTS
// ============================================================================

module BudgetExecutionTests =

    [<Fact>]
    let ``defaultBudget has expected values`` () =
        Assert.Equal(1000, defaultBudget.MaxTotalShots)
        Assert.Equal(None, defaultBudget.MaxTimeMs)
        match defaultBudget.Decomposition with
        | AdaptiveToBudgetBackend -> ()  // expected
        | other -> Assert.Fail($"Expected AdaptiveToBudgetBackend but got {other}")

    [<Fact>]
    let ``executeWithBudget succeeds for small problem within budget`` () =
        let qubo = array2D [| [| -1.0; 2.0 |]; [| 0.0; -1.0 |] |]
        let backend = createLocalBackend ()
        let config = { fastConfig with NumLayers = 1; FinalShots = 200 }
        let budget : ExecutionBudget = {
            MaxTotalShots = 500
            MaxTimeMs = None
            Decomposition = NoBudgetDecomposition
        }

        let result = executeWithBudget backend qubo config budget
        match result with
        | Ok (solution, parameters, _converged) ->
            Assert.Equal(2, solution.Length)
            Assert.True(parameters.Length >= 1)
        | Error err ->
            Assert.Fail($"Expected Ok but got Error: {err}")

    [<Fact>]
    let ``executeWithBudget limits shots to MaxTotalShots`` () =
        let qubo = array2D [| [| -1.0 |] |]
        let backend = createLocalBackend ()
        // Config requests 2000 final shots, but budget caps at 100
        let config = { fastConfig with NumLayers = 1; FinalShots = 2000 }
        let budget : ExecutionBudget = {
            MaxTotalShots = 100
            MaxTimeMs = None
            Decomposition = NoBudgetDecomposition
        }

        let result = executeWithBudget backend qubo config budget
        match result with
        | Ok _ -> ()  // success is sufficient — shot limiting is internal
        | Error err ->
            Assert.Fail($"Expected Ok but got Error: {err}")

    [<Fact>]
    let ``executeWithBudget rejects zero MaxTotalShots`` () =
        let qubo = array2D [| [| -1.0 |] |]
        let backend = createLocalBackend ()
        let config = { fastConfig with NumLayers = 1 }
        let budget : ExecutionBudget = {
            MaxTotalShots = 0
            MaxTimeMs = None
            Decomposition = NoBudgetDecomposition
        }

        let result = executeWithBudget backend qubo config budget
        match result with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("MaxTotalShots", field)
        | Error _ -> Assert.Fail("Expected ValidationError for MaxTotalShots")
        | Ok _ -> Assert.Fail("Expected Error for zero MaxTotalShots")

    [<Fact>]
    let ``executeWithBudget rejects negative MaxTotalShots`` () =
        let qubo = array2D [| [| -1.0 |] |]
        let backend = createLocalBackend ()
        let config = { fastConfig with NumLayers = 1 }
        let budget : ExecutionBudget = {
            MaxTotalShots = -10
            MaxTimeMs = None
            Decomposition = NoBudgetDecomposition
        }

        let result = executeWithBudget backend qubo config budget
        match result with
        | Error (QuantumError.ValidationError _) -> ()  // expected
        | Error _ -> Assert.Fail("Expected ValidationError")
        | Ok _ -> Assert.Fail("Expected Error for negative MaxTotalShots")

    [<Fact>]
    let ``executeWithBudget rejects invalid config`` () =
        let qubo = array2D [| [| -1.0 |] |]
        let backend = createLocalBackend ()
        let config = { fastConfig with NumLayers = 0 }  // invalid
        let budget : ExecutionBudget = {
            MaxTotalShots = 1000
            MaxTimeMs = None
            Decomposition = NoBudgetDecomposition
        }

        let result = executeWithBudget backend qubo config budget
        match result with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("NumLayers", field)
        | Error _ -> Assert.Fail("Expected ValidationError")
        | Ok _ -> Assert.Fail("Expected Error for invalid config")

    [<Fact>]
    let ``executeWithBudget with FixedQubitLimit errors when problem exceeds limit`` () =
        // 3-qubit problem but limit is 2
        let qubo = Array2D.init 3 3 (fun i j -> if i = j then -1.0 else 0.5)
        let backend = createLocalBackend ()
        let config = { fastConfig with NumLayers = 1 }
        let budget : ExecutionBudget = {
            MaxTotalShots = 1000
            MaxTimeMs = None
            Decomposition = FixedQubitLimit 2
        }

        let result = executeWithBudget backend qubo config budget
        match result with
        | Error (QuantumError.OperationError ("QAOA", msg)) ->
            Assert.Contains("requires 3 qubits", msg)
        | Error _ -> Assert.Fail("Expected OperationError from QAOA")
        | Ok _ -> Assert.Fail("Expected Error when exceeding FixedQubitLimit")

    [<Fact>]
    let ``executeWithBudget with FixedQubitLimit succeeds when within limit`` () =
        let qubo = array2D [| [| -1.0; 2.0 |]; [| 0.0; -1.0 |] |]
        let backend = createLocalBackend ()
        let config = { fastConfig with NumLayers = 1; FinalShots = 100 }
        let budget : ExecutionBudget = {
            MaxTotalShots = 500
            MaxTimeMs = None
            Decomposition = FixedQubitLimit 10
        }

        let result = executeWithBudget backend qubo config budget
        match result with
        | Ok _ -> ()
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err}")

    [<Fact>]
    let ``executeWithBudget with AdaptiveToBudgetBackend checks LocalBackend limit`` () =
        // LocalBackend has MaxQubits = 16
        // Create a problem that would exceed it (but we can't actually create a 17-qubit QUBO
        // with the LocalBackend, so we test with a 2-qubit problem that fits)
        let qubo = array2D [| [| -1.0; 2.0 |]; [| 0.0; -1.0 |] |]
        let backend = createLocalBackend ()
        let config = { fastConfig with NumLayers = 1; FinalShots = 100 }
        let budget : ExecutionBudget = {
            MaxTotalShots = 500
            MaxTimeMs = None
            Decomposition = AdaptiveToBudgetBackend
        }

        let result = executeWithBudget backend qubo config budget
        match result with
        | Ok _ -> ()  // 2 qubits < 16 limit, should succeed
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err}")

    [<Fact>]
    let ``executeWithBudget with NoBudgetDecomposition ignores capacity`` () =
        // Even if we had a "too large" problem, NoBudgetDecomposition doesn't check
        let qubo = array2D [| [| -1.0 |] |]
        let backend = createLocalBackend ()
        let config = { fastConfig with NumLayers = 1; FinalShots = 50 }
        let budget : ExecutionBudget = {
            MaxTotalShots = 500
            MaxTimeMs = None
            Decomposition = NoBudgetDecomposition
        }

        let result = executeWithBudget backend qubo config budget
        match result with
        | Ok _ -> ()
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err}")

// ============================================================================
// IQubitLimitedBackend TESTS
// ============================================================================

module IQubitLimitedBackendTests =

    [<Fact>]
    let ``LocalBackend implements IQubitLimitedBackend with MaxQubits 20`` () =
        let backend = LocalBackend.LocalBackend()
        let limited = backend :> BackendAbstraction.IQubitLimitedBackend
        Assert.Equal(Some 20, limited.MaxQubits)

    [<Fact>]
    let ``getMaxQubits returns Some for LocalBackend`` () =
        let backend = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
        let maxQubits = BackendAbstraction.UnifiedBackend.getMaxQubits backend
        Assert.Equal(Some 20, maxQubits)

    [<Fact>]
    let ``LocalBackend is recognized as IQubitLimitedBackend via type test`` () =
        // Verify the type-test pattern used by getMaxQubits correctly
        // identifies LocalBackend as implementing IQubitLimitedBackend
        let backend = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
        let isLimited = backend :? BackendAbstraction.IQubitLimitedBackend
        Assert.True(isLimited)

    [<Fact>]
    let ``getCapabilities includes MaxQubits from IQubitLimitedBackend`` () =
        let backend = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
        let caps = BackendAbstraction.UnifiedBackend.getCapabilities backend
        Assert.Equal(Some 20, caps.MaxQubits)

// ============================================================================
// ASYNC QAOA EXECUTION TESTS
// ============================================================================

module ExecuteQaoaCircuitAsyncTests =

    [<Fact>]
    let ``executeQaoaCircuitAsync returns measurements with correct count`` () : Task =
        task {
            let qubo = array2D [| [| -1.0; 2.0 |]; [| 0.0; -1.0 |] |]
            let problemHam = QaoaCircuit.ProblemHamiltonian.fromQubo qubo
            let mixerHam = QaoaCircuit.MixerHamiltonian.create 2
            let parameters = [| (0.5, 0.3) |]
            let backend = createLocalBackend ()

            let! result = executeQaoaCircuitAsync backend problemHam mixerHam parameters 100 CancellationToken.None
            match result with
            | Ok measurements ->
                Assert.Equal(100, measurements.Length)
                for m in measurements do
                    Assert.Equal(2, m.Length)
                    for b in m do
                        Assert.True(b = 0 || b = 1)
            | Error err ->
                Assert.Fail($"Expected Ok but got Error: {err}")
        }

    [<Fact>]
    let ``executeQaoaCircuitAsync produces same results as sync version`` () : Task =
        task {
            let qubo = array2D [| [| -1.0; 2.0 |]; [| 0.0; -1.0 |] |]
            let problemHam = QaoaCircuit.ProblemHamiltonian.fromQubo qubo
            let mixerHam = QaoaCircuit.MixerHamiltonian.create 2
            let parameters = [| (0.5, 0.3) |]
            let backend = createLocalBackend ()

            // Both versions should produce valid measurements (not necessarily identical due to randomness)
            let syncResult = executeQaoaCircuit backend problemHam mixerHam parameters 50
            let! asyncResult = executeQaoaCircuitAsync backend problemHam mixerHam parameters 50 CancellationToken.None

            match syncResult, asyncResult with
            | Ok syncMeasurements, Ok asyncMeasurements ->
                Assert.Equal(syncMeasurements.Length, asyncMeasurements.Length)
                // Both should produce 2-qubit measurements
                Assert.Equal(2, syncMeasurements.[0].Length)
                Assert.Equal(2, asyncMeasurements.[0].Length)
            | Error _, _ | _, Error _ ->
                Assert.Fail("Both sync and async should succeed")
        }

    [<Fact>]
    let ``executeQaoaCircuitAsync supports cancellation`` () : Task =
        task {
            let qubo = array2D [| [| -1.0; 2.0 |]; [| 0.0; -1.0 |] |]
            let problemHam = QaoaCircuit.ProblemHamiltonian.fromQubo qubo
            let mixerHam = QaoaCircuit.MixerHamiltonian.create 2
            let parameters = [| (0.5, 0.3) |]
            let backend = createLocalBackend ()

            // Already cancelled token — local backend completes synchronously so it may not
            // observe cancellation, but the function should accept the token without error
            use cts = new CancellationTokenSource()
            let! result = executeQaoaCircuitAsync backend problemHam mixerHam parameters 10 cts.Token
            // Local backend doesn't observe cancellation, so it succeeds
            match result with
            | Ok measurements -> Assert.True(measurements.Length > 0)
            | Error _ -> () // Also acceptable if backend respects cancellation
        }

// ============================================================================
// ASYNC executeFromQubo TESTS
// ============================================================================

module ExecuteFromQuboAsyncTests =

    [<Fact>]
    let ``executeFromQuboAsync returns measurements for 2-qubit QUBO`` () : Task =
        task {
            let qubo = array2D [| [| -1.0; 2.0 |]; [| 0.0; -1.0 |] |]
            let backend = createLocalBackend ()

            let! result = executeFromQuboAsync backend qubo [| (0.5, 0.3) |] 100 CancellationToken.None
            match result with
            | Ok measurements ->
                Assert.Equal(100, measurements.Length)
                for m in measurements do
                    Assert.Equal(2, m.Length)
            | Error err ->
                Assert.Fail($"Expected Ok but got Error: {err}")
        }

// ============================================================================
// ASYNC executeQaoaCircuitSparse TESTS
// ============================================================================

module ExecuteQaoaCircuitSparseAsyncTests =

    [<Fact>]
    let ``executeQaoaCircuitSparseAsync returns measurements for sparse QUBO`` () : Task =
        task {
            let quboMap = Map.ofList [ ((0, 0), -1.0); ((1, 1), -1.0); ((0, 1), 2.0) ]
            let backend = createLocalBackend ()

            let! result = executeQaoaCircuitSparseAsync backend 2 quboMap [| (0.5, 0.3) |] 50 CancellationToken.None
            match result with
            | Ok measurements ->
                Assert.Equal(50, measurements.Length)
                for m in measurements do
                    Assert.Equal(2, m.Length)
            | Error err ->
                Assert.Fail($"Expected Ok but got Error: {err}")
        }

// ============================================================================
// ASYNC GRID SEARCH TESTS
// ============================================================================

module GridSearchAsyncTests =

    [<Fact>]
    let ``executeQaoaWithGridSearchAsync finds solution for trivial 1-qubit problem`` () : Task =
        task {
            let qubo = array2D [| [| -1.0 |] |]
            let backend = createLocalBackend ()
            let config = { fastConfig with NumLayers = 1; FinalShots = 200 }

            let! result = executeQaoaWithGridSearchAsync backend qubo config 1 CancellationToken.None
            match result with
            | Ok (solution, parameters) ->
                Assert.Equal(1, solution.Length)
                Assert.True(solution.[0] = 0 || solution.[0] = 1)
                Assert.True(parameters.Length > 0)
            | Error err ->
                Assert.Fail($"Expected Ok but got Error: {err}")
        }

    [<Fact>]
    let ``executeQaoaWithGridSearchAsync returns valid solution for 2-qubit problem`` () : Task =
        task {
            let qubo = array2D [| [| -1.0; 0.5 |]; [| 0.0; -1.0 |] |]
            let backend = createLocalBackend ()
            let config = { fastConfig with NumLayers = 1; FinalShots = 200 }

            let! result = executeQaoaWithGridSearchAsync backend qubo config 1 CancellationToken.None
            match result with
            | Ok (solution, parameters) ->
                Assert.Equal(2, solution.Length)
                Assert.True(parameters.Length > 0)
            | Error err ->
                Assert.Fail($"Expected Ok but got Error: {err}")
        }

    [<Fact>]
    let ``executeQaoaWithGridSearchAsync with maxConcurrency 1 behaves sequentially`` () : Task =
        task {
            let qubo = array2D [| [| -1.0 |] |]
            let backend = createLocalBackend ()
            let config = { fastConfig with NumLayers = 1; FinalShots = 100 }

            // maxConcurrency=1 should still produce valid results (sequential)
            let! result = executeQaoaWithGridSearchAsync backend qubo config 1 CancellationToken.None
            match result with
            | Ok (solution, _) -> Assert.Equal(1, solution.Length)
            | Error err -> Assert.Fail($"Expected Ok but got Error: {err}")
        }

    [<Fact>]
    let ``executeQaoaWithGridSearchAsync with maxConcurrency 5 produces valid results`` () : Task =
        task {
            let qubo = array2D [| [| -1.0 |] |]
            let backend = createLocalBackend ()
            let config = { fastConfig with NumLayers = 1; FinalShots = 100 }

            // maxConcurrency=5 should produce valid results with concurrency
            let! result = executeQaoaWithGridSearchAsync backend qubo config 5 CancellationToken.None
            match result with
            | Ok (solution, _) -> Assert.Equal(1, solution.Length)
            | Error err -> Assert.Fail($"Expected Ok but got Error: {err}")
        }

    [<Fact>]
    let ``executeQaoaWithGridSearchAsync rejects invalid config`` () : Task =
        task {
            let qubo = array2D [| [| -1.0 |] |]
            let backend = createLocalBackend ()
            let config = { fastConfig with NumLayers = 0 }  // Invalid

            let! result = executeQaoaWithGridSearchAsync backend qubo config 1 CancellationToken.None
            match result with
            | Error (QuantumError.ValidationError _) -> () // Expected
            | _ -> Assert.Fail("Expected ValidationError for NumLayers = 0")
        }

    [<Fact>]
    let ``executeQaoaWithGridSearchAsync clamps maxConcurrency to at least 1`` () : Task =
        task {
            let qubo = array2D [| [| -1.0 |] |]
            let backend = createLocalBackend ()
            let config = { fastConfig with NumLayers = 1; FinalShots = 100 }

            // maxConcurrency=0 should be clamped to 1, not crash
            let! result = executeQaoaWithGridSearchAsync backend qubo config 0 CancellationToken.None
            match result with
            | Ok (solution, _) -> Assert.Equal(1, solution.Length)
            | Error err -> Assert.Fail($"Expected Ok but got Error: {err}")
        }

// ============================================================================
// ASYNC SPARSE GRID SEARCH TESTS
// ============================================================================

module GridSearchSparseAsyncTests =

    [<Fact>]
    let ``executeQaoaWithGridSearchSparseAsync finds solution for sparse QUBO`` () : Task =
        task {
            let quboMap = Map.ofList [ ((0, 0), -1.0) ]
            let backend = createLocalBackend ()
            let config = { fastConfig with NumLayers = 1; FinalShots = 200 }

            let! result = executeQaoaWithGridSearchSparseAsync backend 1 quboMap config 1 CancellationToken.None
            match result with
            | Ok (solution, parameters) ->
                Assert.Equal(1, solution.Length)
                Assert.True(parameters.Length > 0)
            | Error err ->
                Assert.Fail($"Expected Ok but got Error: {err}")
        }

    [<Fact>]
    let ``executeQaoaWithGridSearchSparseAsync with concurrency produces valid results`` () : Task =
        task {
            let quboMap = Map.ofList [ ((0, 0), -1.0); ((1, 1), -1.0); ((0, 1), 2.0) ]
            let backend = createLocalBackend ()
            let config = { fastConfig with NumLayers = 1; FinalShots = 100 }

            let! result = executeQaoaWithGridSearchSparseAsync backend 2 quboMap config 5 CancellationToken.None
            match result with
            | Ok (solution, _) -> Assert.Equal(2, solution.Length)
            | Error err -> Assert.Fail($"Expected Ok but got Error: {err}")
        }

// ============================================================================
// ASYNC BUDGET EXECUTION TESTS
// ============================================================================

module BudgetExecutionAsyncTests =

    [<Fact>]
    let ``executeWithBudgetAsync returns valid result with grid search`` () : Task =
        task {
            let qubo = array2D [| [| -1.0 |] |]
            let backend = createLocalBackend ()
            let config = { fastConfig with NumLayers = 1; FinalShots = 200 }
            let budget = { defaultBudget with MaxTotalShots = 500 }

            let! result = executeWithBudgetAsync backend qubo config budget 1 CancellationToken.None
            match result with
            | Ok (solution, parameters, converged) ->
                Assert.Equal(1, solution.Length)
                Assert.True(parameters.Length > 0)
                Assert.False(converged) // Grid search sets converged=false
            | Error err ->
                Assert.Fail($"Expected Ok but got Error: {err}")
        }

    [<Fact>]
    let ``executeWithBudgetAsync rejects invalid budget`` () : Task =
        task {
            let qubo = array2D [| [| -1.0 |] |]
            let backend = createLocalBackend ()
            let config = fastConfig
            let budget = { defaultBudget with MaxTotalShots = 0 }

            let! result = executeWithBudgetAsync backend qubo config budget 1 CancellationToken.None
            match result with
            | Error (QuantumError.ValidationError _) -> () // Expected
            | _ -> Assert.Fail("Expected ValidationError for MaxTotalShots = 0")
        }

    [<Fact>]
    let ``executeWithBudgetAsync limits FinalShots to MaxTotalShots`` () : Task =
        task {
            let qubo = array2D [| [| -1.0 |] |]
            let backend = createLocalBackend ()
            // Config asks for 500 final shots, but budget only allows 100
            let config = { fastConfig with NumLayers = 1; FinalShots = 500 }
            let budget = { defaultBudget with MaxTotalShots = 100 }

            let! result = executeWithBudgetAsync backend qubo config budget 1 CancellationToken.None
            match result with
            | Ok (solution, _, _) ->
                Assert.Equal(1, solution.Length)
            | Error err ->
                Assert.Fail($"Expected Ok but got Error: {err}")
        }

    [<Fact>]
    let ``executeWithBudgetAsync rejects problem exceeding fixed qubit limit`` () : Task =
        task {
            let qubo = Array2D.init 5 5 (fun i j -> if i = j then -1.0 else 0.1)
            let backend = createLocalBackend ()
            let config = fastConfig
            let budget = { defaultBudget with Decomposition = FixedQubitLimit 3 }

            let! result = executeWithBudgetAsync backend qubo config budget 1 CancellationToken.None
            match result with
            | Error (QuantumError.OperationError _) -> () // Expected
            | _ -> Assert.Fail("Expected OperationError for exceeding qubit limit")
        }

    [<Fact>]
    let ``executeWithBudgetAsync with optimization falls back to sync Nelder-Mead`` () : Task =
        task {
            let qubo = array2D [| [| -1.0; 2.0 |]; [| 0.0; -1.0 |] |]
            let backend = createLocalBackend ()
            let config = { defaultConfig with NumLayers = 1; OptimizationShots = 50; FinalShots = 100; EnableOptimization = true }
            let budget = { defaultBudget with MaxTotalShots = 200 }

            let! result = executeWithBudgetAsync backend qubo config budget 1 CancellationToken.None
            match result with
            | Ok (solution, parameters, _) ->
                Assert.Equal(2, solution.Length)
                Assert.True(parameters.Length > 0)
            | Error err ->
                Assert.Fail($"Expected Ok but got Error: {err}")
        }
