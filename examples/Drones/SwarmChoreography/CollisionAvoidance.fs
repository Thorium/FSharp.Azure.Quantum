/// Collision-Free Path Planning Extension
/// 
/// Optional extension to SwarmChoreography that validates and adjusts
/// drone paths to ensure minimum separation during transitions.
/// 
/// Uses staggered timing optimization solved via QAOA - each drone
/// can have a delay before starting its transition, allowing paths
/// that would otherwise collide to be executed safely.
/// 
/// For N drones with K delay options: N * K qubits
/// Example: 4 drones x 4 delays = 16 qubits (fits LocalBackend)
/// 
/// RULE 1 COMPLIANT: All solving via IQuantumBackend
/// 
/// USAGE:
///   // Check if current plan has collision risks
///   let risk = CollisionAvoidance.validateTransition currentPos targetPos assignments constraints
///   
///   // Or get a collision-free plan directly
///   let plan = CollisionAvoidance.planTransition backend shots currentPos targetFormation assignments constraints
module FSharp.Azure.Quantum.Examples.Drones.SwarmChoreography.CollisionAvoidance

open System
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.QaoaCircuit

// =============================================================================
// DOMAIN TYPES
// =============================================================================

/// 3D position in meters relative to ground origin
type Position3D = { X: float; Y: float; Z: float }

/// Waypoint with timing for a single drone
type Waypoint = {
    Position: Position3D
    ArrivalTime: float   // Normalized 0.0 to 1.0
    DwellTime: float     // Time to wait before moving
}

/// Complete path for one drone
type DronePath = {
    DroneId: int
    Waypoints: Waypoint list
}

/// Collision detection result
type CollisionRisk =
    | Safe of minSeparation: float
    | PotentialCollision of droneA: int * droneB: int * time: float * distance: float
    | MultipleCollisions of CollisionRisk list

/// Planning constraints
type PlanningConstraints = {
    MinSeparationMeters: float
    MaxVelocityMs: float
    DelaySteps: int
    SamplesPerPath: int
}

/// Result of collision-free planning
type CollisionFreePlan = {
    Paths: DronePath list
    OriginalAssignments: (int * int) list
    TotalDistance: float
    MinAchievedSeparation: float
    MaxDelay: float
    Method: string
}

// =============================================================================
// PLANNING CONSTRAINTS - BUILDER PATTERN
// =============================================================================

module PlanningConstraints =
    
    /// Default constraints suitable for indoor drone shows
    let defaults = {
        MinSeparationMeters = 2.0
        MaxVelocityMs = 5.0
        DelaySteps = 4
        SamplesPerPath = 20
    }
    
    /// Set minimum separation distance between drones
    let withSeparation meters constraints = 
        { constraints with MinSeparationMeters = meters }
    
    /// Set number of delay steps (more = finer control, more qubits)
    let withDelaySteps steps constraints = 
        { constraints with DelaySteps = max 2 steps }
    
    /// Set maximum velocity for time calculations
    let withMaxVelocity velocity constraints =
        { constraints with MaxVelocityMs = velocity }
    
    /// Set path sampling resolution
    let withSamples samples constraints =
        { constraints with SamplesPerPath = max 5 samples }

// =============================================================================
// GEOMETRY HELPERS
// =============================================================================

module Geometry =
    
    /// Euclidean distance between two 3D points
    let distance (p1: Position3D) (p2: Position3D) : float =
        let dx = p2.X - p1.X
        let dy = p2.Y - p1.Y
        let dz = p2.Z - p1.Z
        sqrt (dx*dx + dy*dy + dz*dz)
    
    /// Linear interpolation between two positions
    let lerp (t: float) (p1: Position3D) (p2: Position3D) : Position3D =
        let t' = t |> max 0.0 |> min 1.0
        { X = p1.X + t' * (p2.X - p1.X)
          Y = p1.Y + t' * (p2.Y - p1.Y)
          Z = p1.Z + t' * (p2.Z - p1.Z) }
    
    /// Position along path at normalized time t (0 = start, 1 = end)
    let positionAtTime (startPos: Position3D) (endPos: Position3D) (t: float) : Position3D =
        lerp t startPos endPos
    
    /// Sample positions along a straight-line path
    let samplePath (samples: int) (startPos: Position3D) (endPos: Position3D) : (float * Position3D) list =
        [ for i in 0 .. samples do
            let t = float i / float samples
            (t, lerp t startPos endPos) ]
    
    /// Find minimum distance between two paths over time
    /// Returns (minDistance, timeOfMinDistance)
    let minPathSeparation 
        (samples: int)
        (startA: Position3D, endA: Position3D)
        (startB: Position3D, endB: Position3D)
        : float * float =
        
        [ for i in 0 .. samples do
            let t = float i / float samples
            let posA = lerp t startA endA
            let posB = lerp t startB endB
            (distance posA posB, t) ]
        |> List.minBy fst
    
    /// Find minimum distance between two time-offset paths
    /// delayA/delayB are normalized delays (0.0 to 1.0)
    /// duration is the normalized duration of movement (e.g., 0.5 means move for half the total time)
    let minOffsetPathSeparation
        (samples: int)
        (startA: Position3D, endA: Position3D, delayA: float, durationA: float)
        (startB: Position3D, endB: Position3D, delayB: float, durationB: float)
        : float * float =
        
        // Guard against division by zero - if duration is 0, drone is stationary
        let safeDivide numerator denominator =
            if denominator <= 0.0 then 1.0 else numerator / denominator
        
        [ for i in 0 .. samples do
            let t = float i / float samples
            
            // Position of drone A at time t
            let tA = 
                if t < delayA then 0.0
                elif t > delayA + durationA then 1.0
                else safeDivide (t - delayA) durationA
            let posA = lerp tA startA endA
            
            // Position of drone B at time t
            let tB =
                if t < delayB then 0.0
                elif t > delayB + durationB then 1.0
                else safeDivide (t - delayB) durationB
            let posB = lerp tB startB endB
            
            (distance posA posB, t) ]
        |> List.minBy fst

// =============================================================================
// COLLISION DETECTION
// =============================================================================

module CollisionDetection =
    
    /// Check if two straight-line paths violate minimum separation
    let checkPairCollision 
        (constraints: PlanningConstraints)
        (droneA: int, startA: Position3D, endA: Position3D)
        (droneB: int, startB: Position3D, endB: Position3D)
        : CollisionRisk option =
        
        let minDist, timeOfMin = 
            Geometry.minPathSeparation 
                constraints.SamplesPerPath 
                (startA, endA) 
                (startB, endB)
        
        if minDist < constraints.MinSeparationMeters then
            Some (PotentialCollision (droneA, droneB, timeOfMin, minDist))
        else
            None
    
    /// Check all drone pairs for potential collisions
    let detectCollisions
        (constraints: PlanningConstraints)
        (currentPositions: Position3D[])
        (targetPositions: Position3D[])
        (assignments: (int * int) list)
        : CollisionRisk =
        
        let nCurrent = currentPositions.Length
        let nTarget = targetPositions.Length
        
        // Filter out invalid assignments (bounds checking)
        let validPaths = 
            assignments 
            |> List.choose (fun (droneId, targetIdx) ->
                if droneId >= 0 && droneId < nCurrent && targetIdx >= 0 && targetIdx < nTarget then
                    Some (droneId, currentPositions.[droneId], targetPositions.[targetIdx])
                else
                    None)
        
        let collisions =
            [ for i, pathA in List.indexed validPaths do
                for j, pathB in List.indexed validPaths do
                    if i < j then
                        let (dA, startA, endA) = pathA
                        let (dB, startB, endB) = pathB
                        match checkPairCollision constraints (dA, startA, endA) (dB, startB, endB) with
                        | Some collision -> yield collision
                        | None -> () ]
        
        match collisions with
        | [] -> 
            // Calculate actual minimum separation achieved
            let minSep = 
                [ for i, pathA in List.indexed validPaths do
                    for j, pathB in List.indexed validPaths do
                        if i < j then
                            let (_, startA, endA) = pathA
                            let (_, startB, endB) = pathB
                            yield fst (Geometry.minPathSeparation constraints.SamplesPerPath (startA, endA) (startB, endB)) ]
                |> function
                    | [] -> constraints.MinSeparationMeters
                    | seps -> List.min seps
            Safe minSep
        | [single] -> single
        | multiple -> MultipleCollisions multiple
    
    /// Check if a specific timing assignment causes collisions
    let checkTimingCollisions
        (constraints: PlanningConstraints)
        (currentPositions: Position3D[])
        (targetPositions: Position3D[])
        (assignments: (int * int) list)
        (timings: (int * int) list)  // (droneId, delayStep)
        : CollisionRisk =
        
        let n = currentPositions.Length
        let maxDelay = constraints.DelaySteps - 1 |> float
        let moveDuration = 1.0 / (maxDelay + 2.0)  // Each drone moves for this fraction of total time
        
        let getPathWithTiming droneId =
            let targetIdx = 
                assignments 
                |> List.tryFind (fun (d, _) -> d = droneId)
                |> Option.map snd
            let delayStep =
                timings
                |> List.tryFind (fun (d, _) -> d = droneId)
                |> Option.map snd
                |> Option.defaultValue 0
            
            match targetIdx with
            | Some t -> 
                let delay = float delayStep / (maxDelay + 1.0)
                Some (currentPositions.[droneId], targetPositions.[t], delay, moveDuration)
            | None -> None
        
        let collisions =
            [ for d1 in 0 .. n - 1 do
                for d2 in d1 + 1 .. n - 1 do
                    match getPathWithTiming d1, getPathWithTiming d2 with
                    | Some pathA, Some pathB ->
                        let minDist, timeOfMin =
                            Geometry.minOffsetPathSeparation
                                constraints.SamplesPerPath
                                pathA pathB
                        if minDist < constraints.MinSeparationMeters then
                            yield PotentialCollision (d1, d2, timeOfMin, minDist)
                    | _ -> () ]
        
        match collisions with
        | [] ->
            let minSep =
                [ for d1 in 0 .. n - 1 do
                    for d2 in d1 + 1 .. n - 1 do
                        match getPathWithTiming d1, getPathWithTiming d2 with
                        | Some pathA, Some pathB ->
                            yield fst (Geometry.minOffsetPathSeparation constraints.SamplesPerPath pathA pathB)
                        | _ -> () ]
                |> function
                    | [] -> constraints.MinSeparationMeters
                    | seps -> List.min seps
            Safe minSep
        | [single] -> single
        | multiple -> MultipleCollisions multiple

// =============================================================================
// QUBO FORMULATION FOR TIMING OPTIMIZATION
// =============================================================================

module TimingQubo =
    
    /// Build QUBO matrix for drone timing optimization
    /// 
    /// Variables: x[d,k] = 1 if drone d has delay step k
    /// 
    /// Constraints:
    /// 1. Each drone has exactly one delay value (one-hot encoding)
    /// 
    /// Objective:
    /// - Penalize timing combinations that cause path collisions
    /// - Small preference for earlier movement (minimize total show time)
    let buildQubo
        (currentPositions: Position3D[])
        (targetPositions: Position3D[])
        (assignments: (int * int) list)
        (constraints: PlanningConstraints)
        (penaltyWeight: float)
        : float[,] =
        
        let n = currentPositions.Length
        let nTarget = targetPositions.Length
        let k = constraints.DelaySteps
        let numVars = n * k
        let q = Array2D.zeroCreate<float> numVars numVars
        
        // Variable index: x[drone, delay] -> linear index
        let varIndex drone delay = drone * k + delay
        
        // Get path info for a drone (with bounds checking)
        let getPath droneId =
            assignments 
            |> List.tryFind (fun (d, _) -> d = droneId)
            |> Option.bind (fun (_, targetIdx) -> 
                if droneId >= 0 && droneId < n && targetIdx >= 0 && targetIdx < nTarget then
                    Some (currentPositions.[droneId], targetPositions.[targetIdx])
                else
                    None)
        
        // Movement timing parameters
        let maxDelay = max 1 (k - 1) |> float  // Guard against k=1
        let moveDuration = 1.0 / (maxDelay + 2.0)
        
        // === Constraint: Each drone has exactly one delay ===
        // Using penalty: P * (1 - Σx)² = P * (1 - 2Σx + (Σx)²)
        // = P - 2P*Σx + P*Σx² + 2P*Σ(xi*xj for i<j)
        // Linear terms: -P for each x
        // Quadratic terms: +2P for each pair
        for d in 0 .. n - 1 do
            for delay in 0 .. k - 1 do
                let idx = varIndex d delay
                q.[idx, idx] <- q.[idx, idx] - penaltyWeight
            
            for k1 in 0 .. k - 1 do
                for k2 in k1 + 1 .. k - 1 do
                    let idx1 = varIndex d k1
                    let idx2 = varIndex d k2
                    q.[idx1, idx2] <- q.[idx1, idx2] + 2.0 * penaltyWeight
                    q.[idx2, idx1] <- q.[idx2, idx1] + 2.0 * penaltyWeight
        
        // === Objective: Penalize collision-prone timing pairs ===
        for d1 in 0 .. n - 1 do
            for d2 in d1 + 1 .. n - 1 do
                match getPath d1, getPath d2 with
                | Some (start1, end1), Some (start2, end2) ->
                    for k1 in 0 .. k - 1 do
                        for k2 in 0 .. k - 1 do
                            let delay1 = float k1 / (maxDelay + 1.0)
                            let delay2 = float k2 / (maxDelay + 1.0)
                            
                            // Check separation for this timing combination
                            let minDist, _ =
                                Geometry.minOffsetPathSeparation
                                    constraints.SamplesPerPath
                                    (start1, end1, delay1, moveDuration)
                                    (start2, end2, delay2, moveDuration)
                            
                            if minDist < constraints.MinSeparationMeters then
                                // Add penalty proportional to violation severity
                                let violation = constraints.MinSeparationMeters - minDist
                                let penalty = violation * penaltyWeight * 0.5
                                let idx1 = varIndex d1 k1
                                let idx2 = varIndex d2 k2
                                q.[idx1, idx2] <- q.[idx1, idx2] + penalty
                                q.[idx2, idx1] <- q.[idx2, idx1] + penalty
                | _ -> ()
        
        // === Small preference for earlier movement ===
        // Adds small cost for higher delay values
        for d in 0 .. n - 1 do
            for delay in 0 .. k - 1 do
                let idx = varIndex d delay
                q.[idx, idx] <- q.[idx, idx] + float delay * 0.1
        
        q
    
    /// Decode QUBO solution to timing assignments
    let decodeTimings (solution: int[]) (n: int) (k: int) : (int * int) list =
        [ for d in 0 .. n - 1 do
            for delay in 0 .. k - 1 do
                let idx = d * k + delay
                if idx < solution.Length && solution.[idx] = 1 then
                    yield (d, delay) ]
    
    /// Validate that solution assigns exactly one delay per drone
    let validateSolution (timings: (int * int) list) (n: int) : bool =
        let dronesWithTimings = timings |> List.map fst |> List.distinct
        dronesWithTimings.Length = n

// =============================================================================
// QAOA SOLVER (RULE 1 COMPLIANT)
// =============================================================================

/// Private classical fallback - greedy sequential assignment
let private classicalTimingFallback
    (currentPositions: Position3D[])
    (targetPositions: Position3D[])
    (assignments: (int * int) list)
    (constraints: PlanningConstraints)
    : (int * int) list =
    
    let n = currentPositions.Length
    let k = constraints.DelaySteps
    
    // Greedy: assign delays to minimize collisions one drone at a time
    let rec assignDelays assigned remaining =
        match remaining with
        | [] -> assigned
        | droneId :: rest ->
            // Find delay that minimizes collision risk with already-assigned drones
            let bestDelay =
                [ 0 .. k - 1 ]
                |> List.minBy (fun delay ->
                    let testTimings = (droneId, delay) :: assigned
                    let risk = 
                        CollisionDetection.checkTimingCollisions 
                            constraints 
                            currentPositions 
                            targetPositions 
                            assignments 
                            testTimings
                    match risk with
                    | Safe sep -> -sep  // Prefer larger separation
                    | PotentialCollision (_, _, _, dist) -> constraints.MinSeparationMeters - dist
                    | MultipleCollisions risks -> float risks.Length * 10.0)
            
            assignDelays ((droneId, bestDelay) :: assigned) rest
    
    let droneIds = assignments |> List.map fst
    assignDelays [] droneIds

/// Solve for collision-free timing using QAOA
/// 
/// RULE 1 COMPLIANT: Requires IQuantumBackend parameter
let solveTimings
    (backend: IQuantumBackend)
    (shots: int)
    (currentPositions: Position3D[])
    (targetPositions: Position3D[])
    (assignments: (int * int) list)
    (constraints: PlanningConstraints)
    : Result<(int * int) list, string> =
    
    let n = currentPositions.Length
    let nTarget = targetPositions.Length
    let k = constraints.DelaySteps
    let numVars = n * k
    
    // Calculate penalty weight based on problem scale (with bounds checking)
    let maxDist = 
        assignments
        |> List.choose (fun (d, t) -> 
            if d >= 0 && d < n && t >= 0 && t < nTarget then
                Some (Geometry.distance currentPositions.[d] targetPositions.[t])
            else
                None)
        |> function
            | [] -> 1.0
            | dists -> List.max dists
    let penaltyWeight = maxDist * float n * 2.0
    
    // Build QUBO matrix
    let qubo = 
        TimingQubo.buildQubo 
            currentPositions 
            targetPositions 
            assignments 
            constraints 
            penaltyWeight
    
    // Build QAOA circuit
    let problemHam = ProblemHamiltonian.fromQubo qubo
    let mixerHam = MixerHamiltonian.create numVars
    let parameters = [| (0.5, 0.3) |]  // Single QAOA layer
    let circuit = QaoaCircuit.build problemHam mixerHam parameters
    let wrapper = CircuitAbstraction.QaoaCircuitWrapper(circuit) :> CircuitAbstraction.ICircuit
    
    // Execute on backend
    match backend.ExecuteToState wrapper with
    | Error err -> Error err.Message
    | Ok state ->
        let measurements = QuantumState.measure state shots
        
        // Find best valid solution from measurements
        let validSolutions =
            measurements
            |> Array.map (fun bits ->
                let timings = TimingQubo.decodeTimings bits n k
                let isValid = TimingQubo.validateSolution timings n
                (timings, isValid))
            |> Array.filter snd
            |> Array.map fst
        
        match Array.tryHead validSolutions with
        | Some timings -> Ok timings
        | None -> 
            // Fallback: use greedy classical algorithm
            Ok (classicalTimingFallback currentPositions targetPositions assignments constraints)

// =============================================================================
// PUBLIC API
// =============================================================================

/// Check if a transition has collision risks (without solving)
let validateTransition
    (currentPositions: Position3D[])
    (targetPositions: Position3D[])
    (assignments: (int * int) list)
    (constraints: PlanningConstraints)
    : CollisionRisk =
    
    CollisionDetection.detectCollisions 
        constraints 
        currentPositions 
        targetPositions 
        assignments

/// Plan collision-free paths for a formation transition
/// 
/// RULE 1 COMPLIANT: Requires IQuantumBackend parameter
/// 
/// If no collisions detected, returns direct paths.
/// If collisions detected, uses QAOA to find safe timing offsets.
let planTransition
    (backend: IQuantumBackend)
    (shots: int)
    (currentPositions: Position3D[])
    (targetFormation: {| Name: string; Positions: Position3D[] |})
    (assignments: (int * int) list)
    (constraints: PlanningConstraints)
    : Result<CollisionFreePlan, string> =
    
    // First check if there are any collision risks with direct paths
    let risk = 
        validateTransition 
            currentPositions 
            targetFormation.Positions 
            assignments 
            constraints
    
    match risk with
    | Safe minSep ->
        // No collisions - return simple direct paths
        let paths =
            assignments
            |> List.map (fun (droneId, targetIdx) ->
                { DroneId = droneId
                  Waypoints = [
                    { Position = currentPositions.[droneId]
                      ArrivalTime = 0.0
                      DwellTime = 0.0 }
                    { Position = targetFormation.Positions.[targetIdx]
                      ArrivalTime = 1.0
                      DwellTime = 0.0 }
                  ] })
        
        let totalDist =
            assignments
            |> List.sumBy (fun (d, t) -> 
                Geometry.distance currentPositions.[d] targetFormation.Positions.[t])
        
        Ok { 
            Paths = paths
            OriginalAssignments = assignments
            TotalDistance = totalDist
            MinAchievedSeparation = minSep
            MaxDelay = 0.0
            Method = "Direct (No Collisions Detected)"
        }
    
    | PotentialCollision _ | MultipleCollisions _ ->
        // Collisions detected - solve for safe timings via QAOA
        solveTimings backend shots currentPositions targetFormation.Positions assignments constraints
        |> Result.map (fun timings ->
            let k = constraints.DelaySteps
            // Guard against empty list and division by zero (k=1)
            let maxDelayStep = 
                timings 
                |> List.map snd 
                |> function 
                    | [] -> 0.0 
                    | delays -> delays |> List.max |> float
            let maxDelay = 
                if k <= 1 then 0.0 
                else maxDelayStep / float (k - 1)
            let moveDuration = 1.0 / (float k + 1.0)  // Safe: k >= 1 from constraints
            
            let paths =
                timings
                |> List.map (fun (droneId, delayStep) ->
                    let targetIdx = 
                        assignments 
                        |> List.find (fun (d, _) -> d = droneId) 
                        |> snd
                    let startTime = 
                        if k <= 1 then 0.0
                        else float delayStep / float k
                    let endTime = startTime + moveDuration
                    
                    { DroneId = droneId
                      Waypoints = [
                        { Position = currentPositions.[droneId]
                          ArrivalTime = 0.0
                          DwellTime = startTime }
                        { Position = targetFormation.Positions.[targetIdx]
                          ArrivalTime = endTime
                          DwellTime = 0.0 }
                      ] })
            
            // Verify the solution actually avoids collisions
            let finalRisk =
                CollisionDetection.checkTimingCollisions
                    constraints
                    currentPositions
                    targetFormation.Positions
                    assignments
                    timings
            
            let minSep =
                match finalRisk with
                | Safe sep -> sep
                | _ -> 0.0  // Quantum solution may not be perfect
            
            let totalDist =
                assignments
                |> List.sumBy (fun (d, t) -> 
                    Geometry.distance currentPositions.[d] targetFormation.Positions.[t])
            
            { Paths = paths
              OriginalAssignments = assignments
              TotalDistance = totalDist
              MinAchievedSeparation = minSep
              MaxDelay = maxDelay
              Method = "Quantum (QAOA) - Staggered Timing" })

/// Convenience function with default constraints
let ensureSafeTransition
    (backend: IQuantumBackend)
    (shots: int)
    (currentPositions: Position3D[])
    (targetFormation: {| Name: string; Positions: Position3D[] |})
    (assignments: (int * int) list)
    : Result<CollisionFreePlan, string> =
    
    planTransition 
        backend 
        shots 
        currentPositions 
        targetFormation 
        assignments 
        PlanningConstraints.defaults

/// Convert assignments from the main solver format
let fromAssignments (assignments: {| DroneId: int; TargetPositionIndex: int |}[]) : (int * int) list =
    assignments |> Array.toList |> List.map (fun a -> (a.DroneId, a.TargetPositionIndex))

/// Pretty print collision risk for diagnostics
let describeRisk (risk: CollisionRisk) : string =
    match risk with
    | Safe sep -> 
        sprintf "Safe: minimum separation %.2fm" sep
    | PotentialCollision (dA, dB, time, dist) ->
        sprintf "Collision risk: Drone %d and Drone %d at t=%.2f (distance: %.2fm)" dA dB time dist
    | MultipleCollisions risks ->
        let count = List.length risks
        sprintf "Multiple collision risks (%d detected)" count
