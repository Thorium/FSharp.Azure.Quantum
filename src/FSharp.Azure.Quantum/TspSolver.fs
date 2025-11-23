namespace FSharp.Azure.Quantum.Classical

open System

module TspSolver =
    
    /// City coordinates (x, y)
    type City = float * float
    
    /// Tour representation as array of city indices
    type Tour = int[]
    
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
    let buildDistanceMatrix (cities: City[]) : DistanceMatrix =
        let n = cities.Length
        Array2D.init n n (fun i j ->
            if i = j then 0.0
            else euclideanDistance cities.[i] cities.[j]
        )
    
    /// Calculate total tour length given distance matrix
    let calculateTourLength (distances: DistanceMatrix) (tour: Tour) : float =
        let n = tour.Length
        let mutable totalDistance = 0.0
        
        for i = 0 to n - 1 do
            let fromCity = tour.[i]
            let toCity = tour.[(i + 1) % n]  // Wrap around to start
            totalDistance <- totalDistance + distances.[fromCity, toCity]
        
        totalDistance
    
    /// Nearest Neighbor initialization
    /// Returns a tour starting from city 0, always visiting the nearest unvisited city
    let nearestNeighborTour (distances: DistanceMatrix) : Tour =
        let n = distances.GetLength(0)
        let visited = Array.create n false
        let tour = Array.zeroCreate n
        
        // Start from city 0
        let mutable currentCity = 0
        tour.[0] <- currentCity
        visited.[currentCity] <- true
        
        // Build tour by always visiting nearest unvisited city
        for step = 1 to n - 1 do
            let mutable nearestCity = -1
            let mutable nearestDistance = Double.MaxValue
            
            for candidate = 0 to n - 1 do
                if not visited.[candidate] then
                    let distance = distances.[currentCity, candidate]
                    if distance < nearestDistance then
                        nearestDistance <- distance
                        nearestCity <- candidate
            
            tour.[step] <- nearestCity
            visited.[nearestCity] <- true
            currentCity <- nearestCity
        
        tour
    
    /// Try to improve tour by reversing segment between i+1 and j (2-opt move)
    /// Returns Some(improvedTour, improvement) if better, None otherwise
    let tryTwoOptSwap (distances: DistanceMatrix) (tour: Tour) (i: int) (j: int) : (Tour * float) option =
        let n = tour.Length
        
        // Calculate current edges: (i, i+1) and (j, j+1)
        let city_i = tour.[i]
        let city_i_plus_1 = tour.[(i + 1) % n]
        let city_j = tour.[j]
        let city_j_plus_1 = tour.[(j + 1) % n]
        
        let currentDistance = 
            distances.[city_i, city_i_plus_1] + 
            distances.[city_j, city_j_plus_1]
        
        // Calculate new edges after reversal: (i, j) and (i+1, j+1)
        let newDistance = 
            distances.[city_i, city_j] + 
            distances.[city_i_plus_1, city_j_plus_1]
        
        let improvement = currentDistance - newDistance
        
        if improvement > 1e-10 then  // Improvement threshold to avoid floating point issues
            // Create new tour with reversed segment
            let newTour = Array.copy tour
            let mutable left = (i + 1) % n
            let mutable right = j
            
            while left <> right && (left - 1 + n) % n <> right do
                let temp = newTour.[left]
                newTour.[left] <- newTour.[right]
                newTour.[right] <- temp
                left <- (left + 1) % n
                right <- (right - 1 + n) % n
            
            Some(newTour, improvement)
        else
            None
    
    /// 2-opt local search algorithm
    /// Iteratively improves tour by removing edge crossings
    let twoOptImprove (distances: DistanceMatrix) (initialTour: Tour) (maxIterations: int) : Tour * int =
        let n = initialTour.Length
        let mutable currentTour = initialTour
        let mutable iteration = 0
        let mutable improved = true
        
        while improved && iteration < maxIterations do
            improved <- false
            iteration <- iteration + 1
            
            // Try all possible 2-opt swaps
            let mutable breakLoop = false
            for i = 0 to n - 2 do
                if not breakLoop then
                    for j = i + 2 to n - 1 do
                        if not breakLoop then
                            match tryTwoOptSwap distances currentTour i j with
                            | Some(newTour, _improvement) ->
                                currentTour <- newTour
                                improved <- true
                                breakLoop <- true  // Break to restart search
                            | None -> ()
            
        (currentTour, iteration)
    
    /// Solve TSP using nearest neighbor + 2-opt
    let solve (cities: City[]) (config: TspConfig) : TspSolution =
        let startTime = DateTime.UtcNow
        
        // Build distance matrix
        let distances = buildDistanceMatrix cities
        
        // Initialize tour
        let initialTour = 
            if config.UseNearestNeighbor then
                nearestNeighborTour distances
            else
                // Sequential tour: 0, 1, 2, ..., n-1
                Array.init cities.Length id
        
        // Improve with 2-opt
        let (finalTour, iterations) = twoOptImprove distances initialTour config.MaxIterations
        
        let tourLength = calculateTourLength distances finalTour
        let endTime = DateTime.UtcNow
        let elapsedMs = (endTime - startTime).TotalMilliseconds
        
        {
            Tour = finalTour
            TourLength = tourLength
            Iterations = iterations
            ElapsedMs = elapsedMs
        }
    
    /// Solve TSP with custom distance matrix
    let solveWithDistances (distances: DistanceMatrix) (config: TspConfig) : TspSolution =
        let startTime = DateTime.UtcNow
        
        // Initialize tour
        let n = distances.GetLength(0)
        let initialTour = 
            if config.UseNearestNeighbor then
                nearestNeighborTour distances
            else
                Array.init n id
        
        // Improve with 2-opt
        let (finalTour, iterations) = twoOptImprove distances initialTour config.MaxIterations
        
        let tourLength = calculateTourLength distances finalTour
        let endTime = DateTime.UtcNow
        let elapsedMs = (endTime - startTime).TotalMilliseconds
        
        {
            Tour = finalTour
            TourLength = tourLength
            Iterations = iterations
            ElapsedMs = elapsedMs
        }
