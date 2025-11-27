module FSharp.Azure.Quantum.Tests.TspSolverTests

open System
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Classical.TspSolver

// Test helper: Create simple test cities
let createSimpleCities n = Array.init n (fun i -> TspTypes.create (float i) 0.0)

// Test helper: Create cities in a circle (known optimal tour)
let createCircleCities n =
    Array.init n (fun i ->
        let angle = 2.0 * Math.PI * float i / float n
        TspTypes.create (Math.Cos(angle)) (Math.Sin(angle)))

[<Fact>]
let ``buildDistanceMatrix should create symmetric matrix`` () =
    let cities = [| TspTypes.create 0.0 0.0; TspTypes.create 1.0 0.0; TspTypes.create 0.0 1.0 |]
    let distances = buildDistanceMatrix cities

    // Check symmetry
    Assert.Equal(distances.[0, 1], distances.[1, 0])
    Assert.Equal(distances.[0, 2], distances.[2, 0])
    Assert.Equal(distances.[1, 2], distances.[2, 1])

    // Check diagonal is zero
    Assert.Equal(0.0, distances.[0, 0])
    Assert.Equal(0.0, distances.[1, 1])
    Assert.Equal(0.0, distances.[2, 2])

[<Fact>]
let ``euclideanDistance should calculate correct distance`` () =
    let city1 = TspTypes.create 0.0 0.0
    let city2 = TspTypes.create 3.0 4.0
    let distance = TspTypes.distance city1 city2

    // 3-4-5 triangle
    Assert.Equal(5.0, distance, 6)

[<Fact>]
let ``calculateTourLength should sum all edge distances`` () =
    let cities = [| TspTypes.create 0.0 0.0; TspTypes.create 1.0 0.0; TspTypes.create 1.0 1.0; TspTypes.create 0.0 1.0 |]
    let distances = buildDistanceMatrix cities
    let tour = [| 0; 1; 2; 3 |] // Square tour

    let length = calculateTourLength distances tour

    // Square: 1 + 1 + 1 + 1 = 4
    Assert.Equal(4.0, length, 6)

[<Fact>]
let ``nearestNeighborTour should start from city 0`` () =
    let cities = createSimpleCities 5
    let distances = buildDistanceMatrix cities
    let tour = nearestNeighborTour distances

    Assert.Equal(0, tour.[0])

[<Fact>]
let ``nearestNeighborTour should visit all cities exactly once`` () =
    let cities = createSimpleCities 10
    let distances = buildDistanceMatrix cities
    let tour = nearestNeighborTour distances

    // Check length
    Assert.Equal(10, tour.Length)

    // Check all cities visited exactly once
    let visited = tour |> Array.sort

    for i = 0 to 9 do
        Assert.Equal(i, visited.[i])

[<Fact>]
let ``nearestNeighborTour should produce reasonable tour for line cities`` () =
    // Cities in a line: 0-1-2-3-4
    // Optimal tour: sequential 0-1-2-3-4-0
    let cities = Array.init 5 (fun i -> TspTypes.create (float i) 0.0)
    let distances = buildDistanceMatrix cities
    let tour = nearestNeighborTour distances

    // For cities in a line, nearest neighbor should produce sequential tour
    // Tour should be close to sequential
    let tourLength = calculateTourLength distances tour

    // Optimal tour length for line: 1+1+1+1+4=8 (or better if not returning)
    // Nearest neighbor should get close to this
    Assert.True(tourLength < 12.0, $"Tour too long: {tourLength}")

[<Fact>]
let ``solve with 5 cities should find reasonable solution quickly`` () =
    let cities = createCircleCities 5
    let config = defaultConfig
    let solution = solve cities config

    // Should complete quickly
    Assert.True(solution.ElapsedMs < 100.0, $"Too slow: {solution.ElapsedMs}ms")

    // Should visit all cities
    Assert.Equal(5, solution.Tour.Length)

    // Should have valid tour
    let visitedCities = solution.Tour |> Array.sort

    for i = 0 to 4 do
        Assert.Equal(i, visitedCities.[i])

    // Tour length should be reasonable (circle circumference = 2π ≈ 6.28)
    Assert.True(solution.TourLength < 8.0, $"Tour too long: {solution.TourLength}")

[<Fact>]
let ``solve should respect maxIterations limit`` () =
    let cities = createCircleCities 20
    let config = { defaultConfig with MaxIterations = 5 }
    let solution = solve cities config

    Assert.True(solution.Iterations <= 5)

[<Fact>]
let ``twoOptImprove should improve tour quality`` () =
    // Start with a bad tour (reversed)
    let cities = createSimpleCities 5
    let distances = buildDistanceMatrix cities
    let badTour = [| 0; 4; 3; 2; 1 |] // Reversed tour
    let goodTour = [| 0; 1; 2; 3; 4 |] // Sequential tour

    let badLength = calculateTourLength distances badTour
    let goodLength = calculateTourLength distances goodTour

    let (improvedTour, _iterations) = twoOptImprove distances badTour 1000
    let improvedLength = calculateTourLength distances improvedTour

    // Improved tour should be better than bad tour
    Assert.True(improvedLength <= badLength, $"Not improved: bad={badLength}, improved={improvedLength}")

[<Fact>]
let ``solve with small TSP should find optimal solution`` () =
    // 4 cities at corners of unit square
    let cities = [| TspTypes.create 0.0 0.0; TspTypes.create 1.0 0.0; TspTypes.create 1.0 1.0; TspTypes.create 0.0 1.0 |]
    let solution = solve cities defaultConfig

    let optimalLength = 4.0 // Square perimeter

    // Should find optimal or very close (allow small tolerance)
    Assert.True(
        abs (solution.TourLength - optimalLength) < 0.01,
        $"Not optimal: {solution.TourLength} vs {optimalLength}"
    )

[<Fact>]
let ``solve with 10 cities should complete in under 10ms`` () =
    let cities = createCircleCities 10
    let solution = solve cities defaultConfig

    Assert.True(solution.ElapsedMs < 10.0, $"Too slow for 10 cities: {solution.ElapsedMs}ms")

[<Fact>]
let ``solve with 20 cities should complete in under 50ms`` () =
    let cities = createCircleCities 20
    let solution = solve cities defaultConfig

    Assert.True(solution.ElapsedMs < 50.0, $"Too slow for 20 cities: {solution.ElapsedMs}ms")

[<Fact>]
let ``solve with 50 cities should complete in under 1 second`` () =
    let cities = createCircleCities 50
    let solution = solve cities defaultConfig

    Assert.True(solution.ElapsedMs < 1000.0, $"Too slow for 50 cities: {solution.ElapsedMs}ms")

[<Fact>]
let ``solveWithDistances should work with custom distance matrix`` () =
    // Asymmetric TSP with custom distances
    let n = 4

    let distances =
        Array2D.init n n (fun i j -> if i = j then 0.0 else float ((i + 1) * (j + 1)) // Custom distance formula
        )

    let config = defaultConfig
    let solution = solveWithDistances distances config

    // Should produce valid tour
    Assert.Equal(4, solution.Tour.Length)
    let visited = solution.Tour |> Array.sort

    for i = 0 to 3 do
        Assert.Equal(i, visited.[i])

[<Fact>]
let ``solve with circle cities should find near-optimal tour`` () =
    // Cities arranged in circle have known optimal tour (sequential)
    let n = 20
    let cities = createCircleCities n
    let solution = solve cities defaultConfig

    // Optimal tour for circle is sequential: circumference ≈ 2πr
    // For unit circle, optimal ≈ 2π * n / n = 2π (since we go around circle)
    // But we have n points, so optimal distance between consecutive points
    let optimalLength = 2.0 * Math.PI * Math.Sin(Math.PI / float n) * float n

    // Solution should be within 10% of optimal
    let tolerance = 0.10
    let maxAcceptable = optimalLength * (1.0 + tolerance)

    Assert.True(
        solution.TourLength <= maxAcceptable,
        $"Tour quality: {solution.TourLength} vs optimal {optimalLength} (max: {maxAcceptable})"
    )

[<Fact>]
let ``solve without nearest neighbor should still work`` () =
    let cities = createCircleCities 10

    let config =
        { defaultConfig with
            UseNearestNeighbor = false }

    let solution = solve cities config

    // Should complete and produce valid tour
    Assert.Equal(10, solution.Tour.Length)
    Assert.True(solution.TourLength > 0.0)
