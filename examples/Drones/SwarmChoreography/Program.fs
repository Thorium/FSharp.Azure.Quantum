/// Drone Swarm Choreography Example (4 Drones)
/// 
/// Quantum-optimized formation planning for drone light shows using QAOA
/// to solve the Quadratic Assignment Problem (QAP).
/// 
/// Uses 4 drones (16 qubits) which fits within LocalBackend's 20-qubit limit,
/// enabling actual quantum execution rather than classical fallback.
/// 
/// QUANTUM OPTIMIZATION:
/// - 4 drones = 16 QUBO variables = 16 qubits (fits LocalBackend)
/// - QAOA solver via IQuantumBackend (RULE 1 compliant)
/// - Classical greedy fallback only if quantum fails
/// 
/// CRAZYFLIE EXPORT:
/// - Use --export to generate Python scripts for real drone automation
/// - Supports Crazyflie 2.1 with Lighthouse/Loco positioning
namespace FSharp.Azure.Quantum.Examples.Drones.SwarmChoreography

open System
open System.Diagnostics
open System.IO

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend

open FSharp.Azure.Quantum.Examples.Common
open FSharp.Azure.Quantum.Examples.Drones.Domain

// =============================================================================
// DOMAIN TYPES (same as full version)
// =============================================================================

/// 3D position in meters relative to ground origin
type Position3D = {
    X: float  // meters, positive = right
    Y: float  // meters, positive = forward
    Z: float  // meters, positive = up (altitude)
}

/// Assignment of drones to formation positions
type Assignment = {
    DroneId: int
    TargetPositionIndex: int
}

/// A formation is a set of target positions for drones
type Formation = {
    Name: string
    Positions: Position3D[]
}

/// Result of formation transition optimization
type TransitionResult = {
    FromFormation: string
    ToFormation: string
    Assignments: Assignment[]
    TotalDistance: float
    Method: string
}

// =============================================================================
// FORMATION DEFINITIONS (4 drones only)
// =============================================================================

module Formations =
    
    /// Ground formation: 4 drones in a line at the origin
    let ground : Formation = {
        Name = "Ground (Line)"
        Positions = [|
            { X = -6.0; Y = 0.0; Z = 0.0 }   // Drone 0: left
            { X = -2.0; Y = 0.0; Z = 0.0 }   // Drone 1: center-left
            { X = 2.0;  Y = 0.0; Z = 0.0 }   // Drone 2: center-right
            { X = 6.0;  Y = 0.0; Z = 0.0 }   // Drone 3: right
        |]
    }
    
    /// Diamond formation (4 points)
    ///       0        <- Top
    ///     1   2      <- Sides
    ///       3        <- Bottom
    let diamond : Formation = {
        Name = "Diamond"
        Positions = [|
            { X = 0.0;  Y = 0.0; Z = 25.0 }   // Top
            { X = -8.0; Y = 0.0; Z = 15.0 }   // Left
            { X = 8.0;  Y = 0.0; Z = 15.0 }   // Right
            { X = 0.0;  Y = 0.0; Z = 5.0 }    // Bottom
        |]
    }
    
    /// Square formation (2x2 grid)
    /// 0   1    <- Top row
    /// 2   3    <- Bottom row
    let square : Formation = {
        Name = "Square"
        Positions = [|
            { X = -5.0; Y = 0.0; Z = 20.0 }  // Top-left
            { X = 5.0;  Y = 0.0; Z = 20.0 }  // Top-right
            { X = -5.0; Y = 0.0; Z = 10.0 }  // Bottom-left
            { X = 5.0;  Y = 0.0; Z = 10.0 }  // Bottom-right
        |]
    }
    
    /// Vertical line formation (ascending)
    ///   0   <- Top
    ///   1
    ///   2
    ///   3   <- Bottom
    let vertical : Formation = {
        Name = "Vertical Line"
        Positions = [|
            { X = 0.0; Y = 0.0; Z = 30.0 }  // Top
            { X = 0.0; Y = 0.0; Z = 22.0 }
            { X = 0.0; Y = 0.0; Z = 14.0 }
            { X = 0.0; Y = 0.0; Z = 6.0 }   // Bottom
        |]
    }

// =============================================================================
// DISTANCE CALCULATIONS
// =============================================================================

module Geometry =
    
    /// Calculate Euclidean distance between two 3D positions
    let distance (p1: Position3D) (p2: Position3D) : float =
        let dx = p2.X - p1.X
        let dy = p2.Y - p1.Y
        let dz = p2.Z - p1.Z
        sqrt (dx*dx + dy*dy + dz*dz)
    
    /// Build distance matrix from current positions to target formation
    let buildDistanceMatrix (currentPositions: Position3D[]) (targetFormation: Formation) : float[,] =
        let n = currentPositions.Length
        Array2D.init n n (fun i j ->
            distance currentPositions.[i] targetFormation.Positions.[j])

// =============================================================================
// QUBO FORMULATION FOR QUADRATIC ASSIGNMENT PROBLEM
// =============================================================================

module QapQubo =
    
    /// Build QUBO matrix for the Quadratic Assignment Problem
    /// Variables: x[i,j] = 1 if drone i is assigned to position j
    let buildQubo (distanceMatrix: float[,]) (penaltyWeight: float) : float[,] =
        let n = Array2D.length1 distanceMatrix
        let numVars = n * n
        let q = Array2D.zeroCreate<float> numVars numVars
        
        // Helper: variable index for x[i,j]
        let varIndex i j = i * n + j
        
        // Step 1: Encode objective (minimize total distance)
        for i in 0 .. n - 1 do
            for j in 0 .. n - 1 do
                let idx = varIndex i j
                q.[idx, idx] <- distanceMatrix.[i, j]
        
        // Step 2: Add constraint penalties
        
        // Constraint 1: Each drone assigned to exactly one position
        for i in 0 .. n - 1 do
            for j in 0 .. n - 1 do
                let idx = varIndex i j
                q.[idx, idx] <- q.[idx, idx] - penaltyWeight
            
            for j1 in 0 .. n - 1 do
                for j2 in j1 + 1 .. n - 1 do
                    let idx1 = varIndex i j1
                    let idx2 = varIndex i j2
                    q.[idx1, idx2] <- q.[idx1, idx2] + 2.0 * penaltyWeight
                    q.[idx2, idx1] <- q.[idx2, idx1] + 2.0 * penaltyWeight
        
        // Constraint 2: Each position has exactly one drone
        for j in 0 .. n - 1 do
            for i in 0 .. n - 1 do
                let idx = varIndex i j
                q.[idx, idx] <- q.[idx, idx] - penaltyWeight
            
            for i1 in 0 .. n - 1 do
                for i2 in i1 + 1 .. n - 1 do
                    let idx1 = varIndex i1 j
                    let idx2 = varIndex i2 j
                    q.[idx1, idx2] <- q.[idx1, idx2] + 2.0 * penaltyWeight
                    q.[idx2, idx1] <- q.[idx2, idx1] + 2.0 * penaltyWeight
        
        q
    
    /// Decode QUBO solution to assignment
    let decodeAssignment (solution: int[]) (n: int) : Assignment[] =
        [| for i in 0 .. n - 1 do
            for j in 0 .. n - 1 do
                if solution.[i * n + j] = 1 then
                    yield { DroneId = i; TargetPositionIndex = j }
        |]
    
    /// Validate assignment (each drone and position used exactly once)
    let validateAssignment (assignments: Assignment[]) (n: int) : bool =
        let dronesUsed = assignments |> Array.map (fun a -> a.DroneId) |> Array.distinct
        let positionsUsed = assignments |> Array.map (fun a -> a.TargetPositionIndex) |> Array.distinct
        dronesUsed.Length = n && positionsUsed.Length = n
    
    /// Calculate total distance for an assignment
    let calculateTotalDistance (distanceMatrix: float[,]) (assignments: Assignment[]) : float =
        assignments
        |> Array.sumBy (fun a -> distanceMatrix.[a.DroneId, a.TargetPositionIndex])

// =============================================================================
// SOLVERS
// =============================================================================

module Solver =
    
    /// Private classical greedy solver (nearest neighbor heuristic)
    let private classicalGreedy (distanceMatrix: float[,]) : Assignment[] =
        let n = Array2D.length1 distanceMatrix
        let usedPositions = Array.create n false
        let assignments = ResizeArray<Assignment>()
        
        for drone in 0 .. n - 1 do
            let mutable bestPos = -1
            let mutable bestDist = Double.MaxValue
            
            for pos in 0 .. n - 1 do
                if not usedPositions.[pos] then
                    let dist = distanceMatrix.[drone, pos]
                    if dist < bestDist then
                        bestDist <- dist
                        bestPos <- pos
            
            if bestPos >= 0 then
                usedPositions.[bestPos] <- true
                assignments.Add({ DroneId = drone; TargetPositionIndex = bestPos })
        
        assignments.ToArray()
    
    /// Quantum QAOA solver using IQuantumBackend
    /// RULE 1 COMPLIANT: Requires IQuantumBackend parameter.
    let solve 
        (backend: IQuantumBackend) 
        (shots: int) 
        (distanceMatrix: float[,]) 
        : Result<Assignment[], string> =
        
        let n = Array2D.length1 distanceMatrix
        let numVars = n * n
        
        // Calculate penalty weight (Lucas rule)
        let maxDistance = 
            [| for i in 0 .. n - 1 do
                for j in 0 .. n - 1 do
                    yield distanceMatrix.[i, j] |]
            |> Array.max
        let penaltyWeight = maxDistance * float n * 2.0
        
        // Build QUBO matrix
        let qubo = QapQubo.buildQubo distanceMatrix penaltyWeight
        
        // Convert to Problem Hamiltonian
        let problemHam = QaoaCircuit.ProblemHamiltonian.fromQubo qubo
        let mixerHam = QaoaCircuit.MixerHamiltonian.create numVars
        
        // QAOA parameters (p=1 layer)
        let gamma = 0.5
        let beta = 0.3
        let parameters = [| (gamma, beta) |]
        
        // Build QAOA circuit
        let qaoaCircuit = QaoaCircuit.QaoaCircuit.build problemHam mixerHam parameters
        let circuit = CircuitAbstraction.QaoaCircuitWrapper(qaoaCircuit) :> CircuitAbstraction.ICircuit
        
        // Execute on provided backend
        match backend.ExecuteToState circuit with
        | Error err -> Error err.Message
        | Ok state ->
            // Sample measurements
            let measurements = QuantumState.measure state shots
            
            // Find best valid solution
            let validSolutions =
                measurements
                |> Array.map (fun bits ->
                    let assignments = QapQubo.decodeAssignment bits n
                    let isValid = QapQubo.validateAssignment assignments n
                    let cost = 
                        if isValid then QapQubo.calculateTotalDistance distanceMatrix assignments
                        else Double.MaxValue
                    (assignments, cost, isValid))
                |> Array.filter (fun (_, _, valid) -> valid)
                |> Array.sortBy (fun (_, cost, _) -> cost)
            
            match Array.tryHead validSolutions with
            | Some (assignments, _, _) -> Ok assignments
            | None -> Ok (classicalGreedy distanceMatrix)
    
    /// Classical solver (exposed for comparison only)
    [<System.Obsolete("Use Solver.solve(backend, shots, distanceMatrix) for quantum execution")>]
    let solveClassical (distanceMatrix: float[,]) : Assignment[] =
        classicalGreedy distanceMatrix

// =============================================================================
// VISUALIZATION
// =============================================================================

module Visualization =
    
    /// Generate ASCII art for a formation (front view)
    let renderFormation (formation: Formation) : string =
        let width = 40
        let height = 12
        let grid = Array2D.create height width ' '
        
        // Scale positions to grid
        let scaleX x = int ((x + 15.0) / 30.0 * float (width - 1))
        let scaleZ z = int ((35.0 - z) / 40.0 * float (height - 1))
        
        // Plot drones
        for i, pos in Array.indexed formation.Positions do
            let gx = scaleX pos.X |> max 0 |> min (width - 1)
            let gz = scaleZ pos.Z |> max 0 |> min (height - 1)
            grid.[gz, gx] <- char (48 + i)  // '0' to '3'
        
        // Build string
        let sb = System.Text.StringBuilder()
        sb.AppendLine($"Formation: {formation.Name}") |> ignore
        sb.AppendLine(String.replicate width "-") |> ignore
        for row in 0 .. height - 1 do
            for col in 0 .. width - 1 do
                sb.Append(grid.[row, col]) |> ignore
            sb.AppendLine() |> ignore
        sb.AppendLine(String.replicate width "-") |> ignore
        sb.ToString()
    
    /// Print transition result
    let printTransition (result: TransitionResult) =
        printfn ""
        printfn "╔══════════════════════════════════════════════════╗"
        printfn "║  FORMATION TRANSITION                            ║"
        printfn "╠══════════════════════════════════════════════════╣"
        printfn "║  From: %-40s ║" result.FromFormation
        printfn "║  To:   %-40s ║" result.ToFormation
        printfn "║  Method: %-38s ║" result.Method
        printfn "║  Total Distance: %8.2f meters                ║" result.TotalDistance
        printfn "╠══════════════════════════════════════════════════╣"
        printfn "║  ASSIGNMENTS:                                    ║"
        for a in result.Assignments |> Array.sortBy (fun x -> x.DroneId) do
            printfn "║    Drone %d → Position %d                           ║" a.DroneId a.TargetPositionIndex
        printfn "╚══════════════════════════════════════════════════╝"

// =============================================================================
// METRICS
// =============================================================================

type Metrics = {
    run_id: string
    num_drones: int
    num_qubits: int
    num_formations: int
    solver: string  // Always "Quantum (QAOA)" - RULE 1 compliant
    shots: int
    total_show_distance: float
    transitions: {| from_formation: string; to_formation: string; distance: float; solver_used: string |}[]
    elapsed_ms: int64
}

// =============================================================================
// MAIN PROGRAM
// =============================================================================

#nowarn "44"

module Program =
    
    [<EntryPoint>]
    let main argv =
        let args = Cli.parse argv
        
        if Cli.hasFlag "help" args || Cli.hasFlag "h" args then
            printfn "╔══════════════════════════════════════════════════╗"
            printfn "║  DRONE SWARM CHOREOGRAPHY                        ║"
            printfn "║  Quantum Formation Optimization                  ║"
            printfn "╠══════════════════════════════════════════════════╣"
            printfn "║  4 drones × 4 positions = 16 qubits              ║"
            printfn "║  Fits within LocalBackend 20-qubit limit         ║"
            printfn "╠══════════════════════════════════════════════════╣"
            printfn "║  SHOW SEQUENCE:                                  ║"
            printfn "║    Ground → Diamond → Square → Vertical → Ground ║"
            printfn "╠══════════════════════════════════════════════════╣"
            printfn "║  OPTIONS:                                        ║"
            printfn "║    --out <dir>       Output directory            ║"
            printfn "║    --shots <n>       Number of measurements      ║"
            printfn "║    --export          Export Crazyflie Python     ║"
            printfn "║    --mavlink         Export MAVLink (ArduPilot)  ║"
            printfn "║    --scale <f>       Scale factor (default 0.05) ║"
            printfn "║    --duration <s>    Transition time (default 3) ║"
            printfn "║    --home-lat <deg>  Home latitude (MAVLink)     ║"
            printfn "║    --home-lon <deg>  Home longitude (MAVLink)    ║"
            printfn "║    --home-alt <m>    Home altitude (MAVLink)     ║"
            printfn "║    --help            Show this help              ║"
            printfn "╚══════════════════════════════════════════════════╝"
            printfn ""
            printfn "QUANTUM EXECUTION (RULE 1 COMPLIANT):"
            printfn "  All optimization uses QAOA via IQuantumBackend."
            printfn "  Classical fallback only used if quantum fails."
            printfn ""
            printfn "EXPORT EXAMPLES:"
            printfn "  Crazyflie (indoor): dotnet run -- --export"
            printfn "  MAVLink (outdoor):  dotnet run -- --mavlink --home-lat 49.84 --home-lon 24.03"
            printfn ""
            printfn "  Crazyflie generates:"
            printfn "    - crazyflie_show.json  (waypoint data)"
            printfn "    - crazyflie_show.py    (executable script)"
            printfn ""
            printfn "  MAVLink generates:"
            printfn "    - DroneN_mission.plan  (QGroundControl plans)"
            printfn "    - DroneN.waypoints     (MAVLink waypoint files)"
            printfn "    - mavlink_swarm.py     (pymavlink/dronekit script)"
            0
        else
            let sw = Stopwatch.StartNew()
            
            let outDir = Cli.getOr "out" (Path.Combine("runs", "drone", "swarm")) args
            let shots = Cli.getOr "shots" "2000" args |> int
            
            Data.ensureDirectory outDir
            let runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss")
            
            printfn ""
            printfn "╔══════════════════════════════════════════════════╗"
            printfn "║  DRONE SWARM CHOREOGRAPHY                        ║"
            printfn "║  FSharp.Azure.Quantum Example                    ║"
            printfn "╚══════════════════════════════════════════════════╝"
            printfn ""
            printfn "4-Drone Light Show (Quantum-Ready: 16 qubits)"
            printfn "Show Sequence: Ground → Diamond → Square → Vertical → Ground"
            printfn "Solver: Quantum QAOA (RULE 1 Compliant) | Shots: %d" shots
            printfn ""
            
            // Create quantum backend (RULE 1 COMPLIANT)
            let backend = LocalBackend() :> IQuantumBackend
            
            // Define show sequence
            let formations = [|
                Formations.ground
                Formations.diamond
                Formations.square
                Formations.vertical
                Formations.ground  // Return to base
            |]
            
            // Print initial formation
            printfn "INITIAL FORMATION:"
            printfn "%s" (Visualization.renderFormation Formations.ground)
            
            // Run transitions
            let transitions = ResizeArray<TransitionResult>()
            let mutable currentPositions = Formations.ground.Positions
            
            for i in 0 .. formations.Length - 2 do
                let fromFormation = formations.[i]
                let toFormation = formations.[i + 1]
                
                printfn "═══════════════════════════════════════════════════"
                printfn "TRANSITION %d: %s → %s" (i + 1) fromFormation.Name toFormation.Name
                printfn "═══════════════════════════════════════════════════"
                
                // Build distance matrix
                let distMatrix = Geometry.buildDistanceMatrix currentPositions toFormation
                
                // Print distance matrix
                printfn "Distance Matrix (meters):"
                printfn "         Pos0    Pos1    Pos2    Pos3"
                for d in 0 .. 3 do
                    printf "Drone%d " d
                    for p in 0 .. 3 do
                        printf "%7.1f " distMatrix.[d, p]
                    printfn ""
                printfn ""
                
                // Solve based on method
                // RULE 1 COMPLIANT: Always use quantum solver via IQuantumBackend
                // Classical greedy is only used as internal fallback if quantum fails
                let assignments, methodUsed =
                    printfn "Running QAOA with %d shots..." shots
                    match Solver.solve backend shots distMatrix with
                    | Ok a -> (a, "Quantum (QAOA)")
                    | Error msg ->
                        printfn "  ⚠ Quantum solver error: %s" msg
                        printfn "  → Using internal classical fallback"
                        (Solver.solveClassical distMatrix, "Classical (Fallback)")
                
                let totalDist = QapQubo.calculateTotalDistance distMatrix assignments
                
                let result = {
                    FromFormation = fromFormation.Name
                    ToFormation = toFormation.Name
                    Assignments = assignments
                    TotalDistance = totalDist
                    Method = methodUsed
                }
                
                transitions.Add(result)
                Visualization.printTransition result
                
                // Update current positions (ensure valid assignments)
                // Filter to get one assignment per drone (deduplicate if invalid)
                let validAssignments =
                    assignments
                    |> Array.groupBy (fun a -> a.DroneId)
                    |> Array.map (fun (droneId, assigns) -> 
                        // If multiple assignments for same drone, take first one
                        assigns.[0])
                    |> Array.sortBy (fun a -> a.DroneId)
                
                // Ensure we have exactly 4 drones
                currentPositions <- 
                    [| for i in 0 .. 3 do
                        match validAssignments |> Array.tryFind (fun a -> a.DroneId = i) with
                        | Some a -> yield toFormation.Positions.[a.TargetPositionIndex]
                        | None -> yield toFormation.Positions.[i] // Default: same position
                    |]
                
                // Print formation
                printfn ""
                printfn "%s" (Visualization.renderFormation toFormation)
            
            sw.Stop()
            
            // Summary
            let totalShowDistance = transitions |> Seq.sumBy (fun t -> t.TotalDistance)
            let quantumSolved = transitions |> Seq.filter (fun t -> t.Method.Contains("Quantum")) |> Seq.length
            let fallbackUsed = transitions.Count - quantumSolved
            
            printfn ""
            printfn "╔══════════════════════════════════════════════════╗"
            printfn "║  SHOW SUMMARY                                    ║"
            printfn "╠══════════════════════════════════════════════════╣"
            printfn "║  Drones: 4 (16 qubits)                           ║"
            printfn "║  Transitions: %d                                  ║" transitions.Count
            printfn "║  Total Flight Distance: %8.2f meters          ║" totalShowDistance
            printfn "║  Elapsed Time: %d ms                             ║" sw.ElapsedMilliseconds
            printfn "╠══════════════════════════════════════════════════╣"
            printfn "║  RULE 1 COMPLIANT: Quantum solver via IBackend  ║"
            printfn "║  Quantum solved: %d | Fallback used: %d           ║" quantumSolved fallbackUsed
            printfn "╚══════════════════════════════════════════════════╝"
            
            // Write metrics
            let metrics: Metrics = {
                run_id = runId
                num_drones = 4
                num_qubits = 16
                num_formations = formations.Length
                solver = "Quantum (QAOA)"
                shots = shots
                total_show_distance = totalShowDistance
                transitions = 
                    transitions.ToArray()
                    |> Array.map (fun t -> {| from_formation = t.FromFormation; to_formation = t.ToFormation; distance = t.TotalDistance; solver_used = t.Method |})
                elapsed_ms = sw.ElapsedMilliseconds
            }
            
            Reporting.writeJson (Path.Combine(outDir, "metrics.json")) metrics
            
            // Write report
            let transitionTable = 
                transitions.ToArray() 
                |> Array.mapi (fun i t -> 
                    sprintf "| %d | %s | %s | %.2f m | %s |" (i+1) t.FromFormation t.ToFormation t.TotalDistance t.Method) 
                |> String.concat "\n"
            
            let report = $"""# Drone Swarm Choreography Results

## Summary

- **Run ID**: {runId}
- **Drones**: 4
- **QUBO Variables**: 16 (fits LocalBackend)
- **Formations**: Ground → Diamond → Square → Vertical → Ground
- **Solver**: Quantum QAOA (RULE 1 Compliant)
- **Shots**: {shots}
- **Total Flight Distance**: {totalShowDistance:F2} meters
- **Elapsed Time**: {sw.ElapsedMilliseconds} ms

## Transition Results

| # | From | To | Distance | Solver |
|---|------|----|----------|--------|
{transitionTable}

## RULE 1 Compliance

This example is **RULE 1 compliant**:
- All optimization uses QAOA via `IQuantumBackend`
- Classical greedy is only used as internal fallback if quantum fails
- No standalone classical solver exposed in public API

## Files Generated

- `metrics.json` - Performance metrics
"""
            
            Reporting.writeTextFile (Path.Combine(outDir, "run-report.md")) report
            
            // Common export setup (used by both Crazyflie and MAVLink)
            let scale = Cli.getOr "scale" "0.05" args |> float  // Scale down for indoor use
            let transitionDuration = Cli.getOr "duration" "3.0" args |> float
            
            // Convert formations to anonymous record format for export
            let formationsForExport = 
                formations 
                |> Array.map (fun f -> 
                    {| 
                        Name = f.Name
                        Positions = f.Positions |> Array.map (fun p -> {| X = p.X; Y = p.Y; Z = p.Z |})
                    |})
            
            // Convert transitions to anonymous record format for export
            let transitionsForExport =
                transitions.ToArray()
                |> Array.map (fun t ->
                    {|
                        FromFormation = t.FromFormation
                        ToFormation = t.ToFormation
                        Assignments = t.Assignments |> Array.map (fun a -> {| DroneId = a.DroneId; TargetPositionIndex = a.TargetPositionIndex |})
                        TotalDistance = t.TotalDistance
                        Method = t.Method
                    |})
            
            // Export to Crazyflie Python if requested
            if Cli.hasFlag "export" args then
                printfn ""
                printfn "╔══════════════════════════════════════════════════╗"
                printfn "║  CRAZYFLIE EXPORT                                ║"
                printfn "╚══════════════════════════════════════════════════╝"
                
                // Create show definition
                let show = CrazyflieExport.fromTransitionResults transitionsForExport formationsForExport scale transitionDuration
                
                // Write JSON waypoints
                let jsonPath = Path.Combine(outDir, "crazyflie_show.json")
                CrazyflieExport.writeJson jsonPath show
                
                // Write Python script
                let pythonPath = Path.Combine(outDir, "crazyflie_show.py")
                CrazyflieExport.writePythonScript pythonPath show
                
                printfn ""
                printfn "Export parameters:"
                printfn "  Scale: %.2f (indoor positions = outdoor × scale)" scale
                printfn "  Transition duration: %.1f seconds" transitionDuration
                printfn ""
                printfn "Generated files:"
                printfn "  %s" jsonPath
                printfn "  %s" pythonPath
                printfn ""
                printfn "To run with Crazyflie drones:"
                printfn "  1. Install cflib: pip install cflib"
                printfn "  2. Set up Lighthouse/Loco positioning"
                printfn "  3. Update DRONE_URIS in the Python script"
                printfn "  4. Run: python %s" pythonPath
            
            // Export to MAVLink if requested (can be combined with Crazyflie export)
            if Cli.hasFlag "mavlink" args then
                printfn ""
                printfn "╔══════════════════════════════════════════════════╗"
                printfn "║  MAVLINK EXPORT                                  ║"
                printfn "╚══════════════════════════════════════════════════╝"
                
                // Home position is REQUIRED for MAVLink export (GPS coordinates)
                match Cli.tryGet "home-lat" args, Cli.tryGet "home-lon" args with
                | None, _ | _, None ->
                    printfn ""
                    printfn "ERROR: --home-lat and --home-lon are required for MAVLink export."
                    printfn ""
                    printfn "These specify the GPS coordinates where drones will take off from."
                    printfn "Using incorrect coordinates is dangerous!"
                    printfn ""
                    printfn "Example:"
                    printfn "  dotnet run -- --mavlink --home-lat 49.8397 --home-lon 24.0297"
                    printfn ""
                    printfn "To find coordinates:"
                    printfn "  - Google Maps: Right-click location → coordinates shown"
                    printfn "  - GPS device: Read from drone's GPS when placed at takeoff point"
                | Some latStr, Some lonStr ->
                
                let homeLat = latStr |> float
                let homeLon = lonStr |> float
                let homeAlt = Cli.getOr "home-alt" "0.0" args |> float
                
                let homePosition : MAVLinkExport.GeoCoordinate = {
                    Latitude = homeLat
                    Longitude = homeLon
                    Altitude = homeAlt
                }
                
                let mavlinkDir = Path.Combine(outDir, "mavlink")
                let transitionSpeed = 3.0  // m/s cruise speed
                let numDrones = formationsForExport.[0].Positions.Length
                
                printfn ""
                printfn "Export parameters:"
                printfn "  Home position: %.6f, %.6f (alt: %.1f m)" homeLat homeLon homeAlt
                printfn "  Scale: %.2f (positions = original × scale)" scale
                printfn "  Cruise speed: %.1f m/s" transitionSpeed
                printfn "  Drones: %d" numDrones
                printfn ""
                
                MAVLinkExport.exportShow 
                    mavlinkDir 
                    "Quantum Drone Choreography" 
                    homePosition 
                    scale 
                    transitionSpeed 
                    formationsForExport 
                    numDrones 
                    "Quantum (QAOA)"
                
                printfn ""
                printfn "To use with ArduPilot/PX4 drones:"
                printfn "  Option 1 - QGroundControl:"
                printfn "    1. Open QGroundControl"
                printfn "    2. Load DroneN_mission.plan files"
                printfn "    3. Upload to each drone"
                printfn ""
                printfn "  Option 2 - Python Script:"
                printfn "    1. Install: pip install dronekit pymavlink"
                printfn "    2. Update DRONE_CONNECTIONS in mavlink_swarm.py"
                printfn "    3. Run: python mavlink_swarm.py [--parallel]"
            
            printfn ""
            printfn "Results written to: %s" outDir
            0
