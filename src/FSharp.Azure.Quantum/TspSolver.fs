namespace FSharp.Azure.Quantum.Classical

open System

module TspSolver =

    /// City coordinates (x, y)
    type City = float * float

    /// Tour representation as array of city indices
    type Tour = int array

    /// Distance matrix between cities
    type DistanceMatrix = float[,]

    /// TSP solver configuration
    type TspConfig = {
        /// Maximum number of 2-opt iterations (default: 1000)
        MaxIterations: int

        /// Whether to use nearest neighbor initialization (default: true)
        UseNearestNeighbor: bool
    }

    /// Create default TSP configuration
    let defaultConfig = {
        MaxIterations = 1000
        UseNearestNeighbor = true
    }

    /// TSP solution result
    type TspSolution = {
        /// Tour as sequence of city indices
        Tour: Tour

        /// Total tour length
        TourLength: float

        /// Number of iterations performed
        Iterations: int

        /// Time taken to solve (milliseconds)
        ElapsedMs: float
    }

    /// Calculate Euclidean distance between two cities
    let euclideanDistance (x1, y1) (x2, y2) =
        let dx = x2 - x1
        let dy = y2 - y1
        sqrt (dx * dx + dy * dy)

    /// Build distance matrix from city coordinates
    let buildDistanceMatrix (cities: City array) : DistanceMatrix =
        let n = cities.Length
        Array2D.init n n (fun i j ->
            if i = j then 0.0
            else euclideanDistance cities.[i] cities.[j])

    /// Calculate total tour length given distance matrix
    let calculateTourLength (distances: DistanceMatrix) (tour: Tour) : float =
        let n = tour.Length
        [0 .. n - 1]
        |> List.sumBy (fun i ->
            let fromCity = tour.[i]
            let toCity = tour.[(i + 1) % n]
            distances.[fromCity, toCity])

    /// Find nearest unvisited city
    let private findNearestCity (distances: DistanceMatrix) (currentCity: int) (visited: Set<int>) (n: int) : int =
        [0 .. n - 1]
        |> List.filter (fun candidate -> not (Set.contains candidate visited))
        |> List.minBy (fun candidate -> distances.[currentCity, candidate])

    /// Nearest Neighbor initialization
    /// Returns a tour starting from city 0, always visiting the nearest unvisited city
    let nearestNeighborTour (distances: DistanceMatrix) : Tour =
        let n = distances.GetLength(0)
        
        let rec buildTour (currentCity: int) (visited: Set<int>) (tour: int list) : int list =
            if Set.count visited = n then
                List.rev tour
            else
                let nearestCity = findNearestCity distances currentCity visited n
                buildTour nearestCity (Set.add nearestCity visited) (nearestCity :: tour)
        
        let initialCity = 0
        let tour = buildTour initialCity (Set.singleton initialCity) [initialCity]
        List.toArray tour

    /// Reverse array segment between indices (inclusive, with wrapping)
    let private reverseSegment (arr: 'T array) (start: int) (finish: int) : 'T array =
        let n = arr.Length
        let newArr = Array.copy arr
        let mutable left = start
        let mutable right = finish
        
        while left <> right && (left - 1 + n) % n <> right do
            let temp = newArr.[left]
            newArr.[left] <- newArr.[right]
            newArr.[right] <- temp
            left <- (left + 1) % n
            right <- (right - 1 + n) % n
        
        newArr

    /// Try to improve tour by reversing segment between i+1 and j (2-opt move)
    /// Returns Some(improvedTour, improvement) if better, None otherwise
    let tryTwoOptSwap (distances: DistanceMatrix) (tour: Tour) (i: int) (j: int) : (Tour * float) option =
        let n = tour.Length

        // Calculate current edges: (i, i+1) and (j, j+1)
        let cityI = tour.[i]
        let cityIPlus1 = tour.[(i + 1) % n]
        let cityJ = tour.[j]
        let cityJPlus1 = tour.[(j + 1) % n]

        let currentDistance = distances.[cityI, cityIPlus1] + distances.[cityJ, cityJPlus1]
        let newDistance = distances.[cityI, cityJ] + distances.[cityIPlus1, cityJPlus1]
        let improvement = currentDistance - newDistance

        if improvement > 1e-10 then
            let newTour = reverseSegment tour ((i + 1) % n) j
            Some (newTour, improvement)
        else
            None

    /// Try all 2-opt swaps and return first improvement found
    let private tryAllSwaps (distances: DistanceMatrix) (tour: Tour) : Tour option =
        let n = tour.Length
        let rec tryPairs i j =
            if i >= n - 2 then
                None
            elif j >= n then
                tryPairs (i + 1) (i + 3)
            else
                match tryTwoOptSwap distances tour i j with
                | Some (newTour, _) -> Some newTour
                | None -> tryPairs i (j + 1)
        
        tryPairs 0 2

    /// 2-opt local search algorithm
    /// Iteratively improves tour by removing edge crossings
    let twoOptImprove (distances: DistanceMatrix) (initialTour: Tour) (maxIterations: int) : Tour * int =
        let rec improve (tour: Tour) (iteration: int) : Tour * int =
            if iteration >= maxIterations then
                (tour, iteration)
            else
                match tryAllSwaps distances tour with
                | Some improvedTour -> improve improvedTour (iteration + 1)
                | None -> (tour, iteration)
        
        improve initialTour 0

    /// Create initial tour based on configuration
    let private createInitialTour (distances: DistanceMatrix) (config: TspConfig) : Tour =
        let n = distances.GetLength(0)
        if config.UseNearestNeighbor then
            nearestNeighborTour distances
        else
            Array.init n id

    /// Solve TSP and return solution with timing
    let private solveTsp (distances: DistanceMatrix) (config: TspConfig) : TspSolution =
        let startTime = DateTime.UtcNow
        let initialTour = createInitialTour distances config
        let (finalTour, iterations) = twoOptImprove distances initialTour config.MaxIterations
        let tourLength = calculateTourLength distances finalTour
        let elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds

        {
            Tour = finalTour
            TourLength = tourLength
            Iterations = iterations
            ElapsedMs = elapsedMs
        }

    /// Solve TSP using nearest neighbor + 2-opt
    let solve (cities: City array) (config: TspConfig) : TspSolution =
        let distances = buildDistanceMatrix cities
        solveTsp distances config

    /// Solve TSP with custom distance matrix
    let solveWithDistances (distances: DistanceMatrix) (config: TspConfig) : TspSolution =
        solveTsp distances config
