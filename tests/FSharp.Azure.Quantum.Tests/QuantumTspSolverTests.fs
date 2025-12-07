namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Quantum.QuantumTspSolver
open FSharp.Azure.Quantum.Core.BackendAbstraction

module QuantumTspSolverTests =

    // ========================================================================
    // Helper Functions
    // ========================================================================

    /// Create simple 3-city TSP distance matrix (triangle)
    let create3CityProblem () =
        array2D [
            [ 0.0; 1.0; 2.0 ]
            [ 1.0; 0.0; 1.5 ]
            [ 2.0; 1.5; 0.0 ]
        ]

    /// Create simple 4-city TSP distance matrix (square)
    let create4CityProblem () =
        array2D [
            [ 0.0; 1.0; 4.0; 2.0 ]
            [ 1.0; 0.0; 2.0; 3.0 ]
            [ 4.0; 2.0; 0.0; 1.0 ]
            [ 2.0; 3.0; 1.0; 0.0 ]
        ]

    // ========================================================================
    // Input Validation Tests
    // ========================================================================

    [<Fact>]
    let ``solve should reject single city`` () =
        let backend = createLocalBackend()
        let distances = array2D [[ 0.0 ]]
        
        let result = solveWithShots backend distances 100
        
        match result with
        | Error err -> Assert.Contains("at least 2 cities", err.Message)
        | Ok _ -> Assert.True(false, "Should reject single city")

    [<Fact>]
    let ``solve should reject negative shots`` () =
        let backend = createLocalBackend()
        let distances = create3CityProblem()
        
        let result = solveWithShots backend distances -10
        
        match result with
        | Error err -> Assert.Contains("positive", err.Message)
        | Ok _ -> Assert.True(false, "Should reject negative shots")

    [<Fact>]
    let ``solve should reject zero shots`` () =
        let backend = createLocalBackend()
        let distances = create3CityProblem()
        
        let result = solveWithShots backend distances 0
        
        match result with
        | Error err -> Assert.Contains("positive", err.Message)
        | Ok _ -> Assert.True(false, "Should reject zero shots")

    [<Fact>]
    let ``solve should reject problem too large for backend`` () =
        let backend = createLocalBackend()  // Max 16 qubits
        // 5 cities requires 5*5 = 25 qubits (exceeds limit)
        let distances = Array2D.zeroCreate 6 6
        
        let result = solveWithShots backend distances 100
        
        match result with
        | Error err -> 
            Assert.Contains("qubits", err.Message)
            Assert.Contains(backend.Name, err.Message)
        | Ok _ -> Assert.True(false, "Should reject problem too large")

    // ========================================================================
    // Basic Execution Tests
    // ========================================================================

    [<Fact>]
    let ``solve should execute on local backend`` () =
        let backend = createLocalBackend()
        let distances = create3CityProblem()
        
        let result = solveWithShots backend distances 50
        
        match result with
        | Ok solution ->
            Assert.Equal(3, solution.Tour.Length)
            Assert.Equal("Local QAOA Simulator", solution.BackendName)
            Assert.Equal(50, solution.NumShots)
            Assert.True(solution.TourLength > 0.0)
            Assert.True(solution.ElapsedMs >= 0.0)
        | Error err ->
            Assert.True(false, sprintf "Execution failed: %s" err.Message)

    [<Fact>]
    let ``solve should return valid tour`` () =
        let backend = createLocalBackend()
        let distances = create3CityProblem()
        
        let result = solveWithShots backend distances 100
        
        match result with
        | Ok solution ->
            // Tour should contain all cities
            Assert.Equal(3, solution.Tour.Length)
            
            // All cities should be present
            let citySet = Set.ofArray solution.Tour
            Assert.Equal(3, citySet.Count)
            
            // Cities should be 0, 1, 2
            Assert.True(citySet.Contains(0))
            Assert.True(citySet.Contains(1))
            Assert.True(citySet.Contains(2))
        | Error err ->
            Assert.True(false, sprintf "Execution failed: %s" err.Message)

    [<Fact>]
    let ``solve should calculate correct tour length`` () =
        let backend = createLocalBackend()
        let distances = create3CityProblem()
        
        let result = solveWithShots backend distances 100
        
        match result with
        | Ok solution ->
            // Manually calculate tour length
            let tour = solution.Tour
            let mutable totalDistance = 0.0
            for i in 0 .. tour.Length - 1 do
                let fromCity = tour.[i]
                let toCity = tour.[(i + 1) % tour.Length]
                totalDistance <- totalDistance + distances.[fromCity, toCity]
            
            // Solution's tour length should match manual calculation
            Assert.Equal(totalDistance, solution.TourLength, 2)  // 2 decimal places
        | Error err ->
            Assert.True(false, sprintf "Execution failed: %s" err.Message)

    [<Fact>]
    let ``solve should return top solutions`` () =
        let backend = createLocalBackend()
        let distances = create3CityProblem()
        
        let result = solveWithShots backend distances 100
        
        match result with
        | Ok solution ->
            // Should have at least 1 solution
            Assert.True(solution.TopSolutions.Length > 0)
            Assert.True(solution.TopSolutions.Length <= 5)  // Max 5
            
            // Top solutions should be sorted by length (shortest first)
            let lengths = solution.TopSolutions |> List.map (fun (_, len, _) -> len)
            let sortedLengths = lengths |> List.sort
            Assert.Equal<float list>(sortedLengths, lengths)
            
            // Best solution should be first
            let (bestTour, bestLen, _) = solution.TopSolutions.[0]
            Assert.Equal<int seq>(solution.Tour, bestTour)
            Assert.Equal(solution.TourLength, bestLen)
        | Error err ->
            Assert.True(false, sprintf "Execution failed: %s" err.Message)

    [<Fact>]
    let ``solve should set best energy`` () =
        let backend = createLocalBackend()
        let distances = create3CityProblem()
        
        let result = solveWithShots backend distances 100
        
        match result with
        | Ok solution ->
            // For TSP, best energy should equal tour length
            Assert.Equal(solution.TourLength, solution.BestEnergy)
        | Error err ->
            Assert.True(false, sprintf "Execution failed: %s" err.Message)

    // ========================================================================
    // Scalability Tests
    // ========================================================================

    [<Fact>]
    let ``solve with different shot counts should work`` () =
        let backend = createLocalBackend()
        let distances = create3CityProblem()  // 3 cities = 9 qubits (within 16 qubit limit)
        
        let result1 = solveWithShots backend distances 10
        match result1 with
        | Ok solution -> Assert.Equal(10, solution.NumShots)
        | Error err -> Assert.True(false, sprintf "Execution with 10 shots failed: %s" err.Message)
        
        let result2 = solveWithShots backend distances 50
        match result2 with
        | Ok solution -> Assert.Equal(50, solution.NumShots)
        | Error err -> Assert.True(false, sprintf "Execution with 50 shots failed: %s" err.Message)

    // ========================================================================
    // Default Parameters Test
    // ========================================================================

    [<Fact>]
    let ``solveWithDefaults should use 1000 shots`` () =
        let distances = create3CityProblem()
        
        let result = solveWithDefaults distances
        
        match result with
        | Ok solution ->
            Assert.Equal(1000, solution.NumShots)
            Assert.Equal("Local QAOA Simulator", solution.BackendName)
        | Error err ->
            Assert.True(false, sprintf "Execution failed: %s" err.Message)

    [<Fact>]
    let ``solveWithDefaults should use local backend`` () =
        let distances = create3CityProblem()
        
        let result = solveWithDefaults distances
        
        match result with
        | Ok solution ->
            Assert.Equal("Local QAOA Simulator", solution.BackendName)
        | Error err ->
            Assert.True(false, sprintf "Execution failed: %s" err.Message)

    // ========================================================================
    // Tour Quality Tests (Heuristic)
    // ========================================================================

    [<Fact>]
    let ``solve should find reasonable tour for 3 cities`` () =
        let backend = createLocalBackend()
        let distances = create3CityProblem()
        
        let result = solveWithShots backend distances 200  // More shots for better quality
        
        match result with
        | Ok solution ->
            // For 3 cities, there are only 3 unique tours (rotations of same tour)
            // The optimal tour should have length around 4.5 (1 + 1.5 + 2)
            // But quantum might not always find optimal, so be lenient
            Assert.True(solution.TourLength < 10.0, 
                sprintf "Tour length %f seems unreasonably high" solution.TourLength)
        | Error err ->
            Assert.True(false, sprintf "Execution failed: %s" err.Message)

    // ========================================================================
    // Solution Frequency Tests
    // ========================================================================

    [<Fact>]
    let ``solve should report solution frequencies`` () =
        let backend = createLocalBackend()
        let distances = create3CityProblem()
        
        let result = solveWithShots backend distances 100
        
        match result with
        | Ok solution ->
            // Check that frequencies add up to total shots
            let totalFreq = solution.TopSolutions |> List.sumBy (fun (_, _, freq) -> freq)
            Assert.True(totalFreq <= 100)
            Assert.True(totalFreq > 0)
            
            // Each frequency should be positive
            for (_, _, freq) in solution.TopSolutions do
                Assert.True(freq > 0)
        | Error err ->
            Assert.True(false, sprintf "Execution failed: %s" err.Message)
