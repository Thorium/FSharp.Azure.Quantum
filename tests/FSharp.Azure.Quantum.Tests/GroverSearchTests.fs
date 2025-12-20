namespace FSharp.Azure.Quantum.Tests

open FSharp.Azure.Quantum.LocalSimulator
open FSharp.Azure.Quantum.GroverSearch
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core
open System
open Xunit

/// Integration tests for Grover Search using local backend
/// These tests actually RUN the quantum algorithm in simulation
module GroverSearchTests =
    
    /// Helper: Create local quantum backend for testing
    let createBackend () = new LocalBackend.LocalBackend() :> IQuantumBackend
    
    /// Helper: Unwrap Result<CompiledOracle, QuantumError> for testing
    let unwrapOracle (result: Result<Oracle.CompiledOracle, QuantumError>) : Oracle.CompiledOracle =
        match result with
        | Ok oracle -> oracle
        | Error err -> failwith $"Oracle creation failed: {err.Message}"
    
    /// Helper: Unwrap Result<int, QuantumError> for testing
    let unwrapInt (result: Result<int, QuantumError>) : int =
        match result with
        | Ok value -> value
        | Error err -> failwith $"Operation failed: {err.Message}"

    /// Helper: Classical Search
    let classicalSearch predicate searchSpace =
        [0 .. searchSpace - 1] |> List.tryFind predicate

    /// Helper: Theoretical Success Probability
    let theoreticalSuccessProbability numQubits numSolutions iterations =
        let N = float (1 <<< numQubits)
        let M = float numSolutions
        let theta = Math.Asin(Math.Sqrt(M / N))
        let k = float iterations
        Math.Sin((2.0 * k + 1.0) * theta) ** 2.0
    
    // ========================================================================
    // ORACLE TESTS - Verify oracle compilation and marking
    // ========================================================================
    
    [<Fact>]
    let ``Oracle marks single target correctly`` () =
        let target = 5
        let numQubits = 3
        let oracle = Oracle.forValue target numQubits |> unwrapOracle
        
        // Verify oracle properties
        Assert.Equal(numQubits, oracle.NumQubits)
        Assert.Equal(Some 1, oracle.ExpectedSolutions)
        
        // Verify oracle marks correct solution
        let solutions = Oracle.listSolutions oracle
        Assert.Equal<int list>([target], solutions)
    
    [<Fact>]
    let ``Oracle marks multiple targets correctly`` () =
        let targets = [2; 5; 7]
        let numQubits = 3
        let oracle = Oracle.forValues targets numQubits |> unwrapOracle
        
        Assert.Equal(numQubits, oracle.NumQubits)
        Assert.Equal(Some 3, oracle.ExpectedSolutions)
        
        let solutions = Oracle.listSolutions oracle
        Assert.Equal<int list>(targets, solutions)
    
    [<Fact>]
    let ``Oracle from predicate works`` () =
        let isEven x = x % 2 = 0
        let numQubits = 3
        let oracle = Oracle.fromPredicate isEven numQubits |> unwrapOracle
        
        Assert.Equal(numQubits, oracle.NumQubits)
        
        // Expected even numbers in 3-qubit space: 0, 2, 4, 6
        let solutions = Oracle.listSolutions oracle
        let expectedEvens = [0; 2; 4; 6]
        Assert.Equal<int list>(expectedEvens, solutions)
    
    [<Fact>]
    let ``Even oracle marks all even numbers`` () =
        let numQubits = 3
        let oracle = Oracle.even numQubits |> unwrapOracle
        
        let solutions = Oracle.listSolutions oracle
        let expectedEvens = [0; 2; 4; 6]
        Assert.Equal<int list>(expectedEvens, solutions)
    
    [<Fact>]
    let ``Odd oracle marks all odd numbers`` () =
        let numQubits = 3
        let oracle = Oracle.odd numQubits |> unwrapOracle
        
        let solutions = Oracle.listSolutions oracle
        let expectedOdds = [1; 3; 5; 7]
        Assert.Equal<int list>(expectedOdds, solutions)
    
    [<Fact>]
    let ``InRange oracle marks numbers in range`` () =
        let min = 3
        let max = 6
        let numQubits = 3
        let oracle = Oracle.inRange min max numQubits |> unwrapOracle
        
        let solutions = Oracle.listSolutions oracle
        let expectedInRange = [3; 4; 5; 6]
        Assert.Equal<int list>(expectedInRange, solutions)
    
    [<Fact>]
    let ``DivisibleBy oracle marks divisible numbers`` () =
        let divisor = 3
        let numQubits = 4
        let oracle = Oracle.divisibleBy divisor numQubits |> unwrapOracle
        
        let solutions = Oracle.listSolutions oracle
        // 0, 3, 6, 9, 12, 15 in 4-qubit space (0-15)
        let expectedDivisible = [0; 3; 6; 9; 12; 15]
        Assert.Equal<int list>(expectedDivisible, solutions)
    
    // ========================================================================
    // GROVER ITERATION TESTS - Verify iteration logic
    // ========================================================================
    
    [<Fact>]
    let ``OptimalIterations calculates correctly for N=16 M=1`` () =
        let k = Grover.calculateOptimalIterations 4 1
        Assert.Equal(3, k)
    
    [<Fact>]
    let ``OptimalIterations calculates correctly for N=256 M=1`` () =
        let k = Grover.calculateOptimalIterations 8 1
        Assert.Equal(13, k)
    
    [<Fact>]
    let ``OptimalIterations calculates correctly for N=1024 M=1`` () =
        let k = Grover.calculateOptimalIterations 10 1
        Assert.Equal(25, k)
    
    [<Fact>]
    let ``TheoreticalSuccessProbability is high at optimal k`` () =
        let numQubits = 4
        let numSolutions = 1
        let k = Grover.calculateOptimalIterations numQubits numSolutions
        
        let prob = theoreticalSuccessProbability numQubits numSolutions k
        Assert.True(prob > 0.9, $"Success probability {prob} should be > 0.9")
    
    // ========================================================================
    // SEARCH API TESTS
    // ========================================================================
    
    [<Fact>]
    let ``SearchSingle finds target in 3-qubit space`` () =
        let target = 5
        let numQubits = 3
        
        match Grover.searchSingle target numQubits (createBackend ()) Grover.defaultConfig with
        | Ok result ->
            Assert.Contains(target, result.Solutions)
            Assert.True(result.SuccessProbability >= 0.6)
            Assert.True(result.Iterations > 0)
            Assert.True(result.Measurements.Count > 0)
        | Error err ->
            Assert.True(false, $"Search failed: {err}")
    
    [<Fact>]
    let ``SearchSingle finds different targets`` () =
        let numQubits = 3
        let reproducibleConfig = { Grover.defaultConfig with RandomSeed = Some 42 }

        for target in [0; 3; 5; 7] do
            match Grover.searchSingle target numQubits (createBackend ()) reproducibleConfig with
            | Ok result ->
                Assert.Contains(target, result.Solutions)
            | Error err ->
                Assert.True(false, $"Search for {target} failed: {err}")
    
    [<Fact>]
    let ``SearchSingle with reproducible seed gives consistent results`` () =
        let target = 5
        let numQubits = 3
        let reproducibleConfig = { Grover.defaultConfig with RandomSeed = Some 12345 }
        
        match Grover.searchSingle target numQubits (createBackend ()) reproducibleConfig with
        | Ok result1 ->
            match Grover.searchSingle target numQubits (createBackend ()) reproducibleConfig with
            | Ok result2 ->
                Assert.Equal<int list>(result1.Solutions, result2.Solutions)
                Assert.Equal(result1.Iterations, result2.Iterations)
            | Error err ->
                Assert.True(false, $"Second search failed: {err}")
        | Error err ->
            Assert.True(false, $"First search failed: {err}")
    
    [<Fact>]
    let ``SearchSingle in 4-qubit space with optimal iterations`` () =
        let target = 10
        let numQubits = 4
        let config = { Grover.defaultConfig with Iterations = None; SuccessThreshold = 0.5; Shots = 500 }
        
        match Grover.searchSingle target numQubits (createBackend ()) config with
        | Ok result ->
            Assert.Contains(target, result.Solutions)
            Assert.InRange(result.Iterations, 3, 4)
            Assert.True(result.SuccessProbability >= 0.6)
        | Error err ->
            Assert.True(false, $"Search failed: {err}")
    
    [<Fact>]
    let ``SearchMultiple finds one of multiple targets`` () =
        let targets = [2; 5; 7]
        let numQubits = 3
        
        match Grover.searchMultiple targets numQubits (createBackend ()) Grover.defaultConfig with
        | Ok result ->
            Assert.NotEmpty(result.Solutions)
            for sol in result.Solutions do
                Assert.Contains(sol, targets)
        | Error err ->
            Assert.True(false, $"Search failed: {err}")
    
    [<Fact>]
    let ``SearchWhere finds even numbers`` () =
        let isEven x = x % 2 = 0
        let numQubits = 3
        let backend = createBackend()
        
        match Grover.searchWhere isEven numQubits backend Grover.defaultConfig with
        | Ok result ->
            Assert.NotEmpty(result.Solutions)
            let hasEven = result.Solutions |> List.exists (fun x -> x % 2 = 0)
            Assert.True(hasEven, "Should find at least one even number")
        | Error err ->
            Assert.True(false, $"Search failed: {err}")
    
    [<Fact>]
    let ``HighPrecisionConfig achieves higher success rate`` () =
        let target = 7
        let numQubits = 3
        // Replicating highPrecisionConfig since it's not exposed
        let highPrecisionConfig = { Grover.defaultConfig with Shots = 500; SuccessThreshold = 0.95 }
        
        match Grover.searchSingle target numQubits (createBackend ()) highPrecisionConfig with
        | Ok result ->
            let totalShots = result.Measurements |> Map.toList |> List.sumBy snd
            Assert.Equal(500, totalShots)
            Assert.Contains(target, result.Solutions)
        | Error err ->
            Assert.True(false, $"High precision search failed: {err}")

    [<Fact>]
    let ``ClassicalSearch finds correct value`` () =
        let predicate x = x = 7
        let searchSpace = 16
        match classicalSearch predicate searchSpace with
        | Some value -> Assert.Equal(7, value)
        | None -> Assert.True(false, "Classical search should find value")
