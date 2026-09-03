/// Minimum Vertex Cover — Network-Monitoring Sensor Placement
///
/// USE CASE: You operate a communications network and must observe traffic on
/// *every* link. Installing a monitor on a router observes all links attached to
/// it. Monitors are expensive, so you want the *fewest* routers whose monitors
/// together cover every link — a minimum vertex cover.
///
/// PROBLEM: Given a graph of routers (vertices) and links (edges), find the
/// smallest (or lowest-weight) set of routers such that every link has at least
/// one endpoint in the set.
///
/// QUANTUM: Encoded as a QAOA optimisation and executed on the unified
/// IQuantumBackend. This example runs on the local simulator; swap in any cloud
/// backend (IonQ / Rigetti / Quantinuum) to run on real hardware.
///
/// Run with: dotnet fsi NetworkMonitoring.fsx

//#r "nuget: FSharp.Azure.Quantum"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "nuget: MathNet.Numerics, 5.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Quantum

// ---------------------------------------------------------------------------
// Build the router topology. A small backbone where two hub routers (R1, R4)
// between them touch every link — so the minimum monitor set should be {R1, R4}.
// ---------------------------------------------------------------------------

let routers =
    [ "R1"; "R2"; "R3"; "R4"; "R5"; "R6" ]
    |> List.map (fun id -> { QuantumVertexCoverSolver.Id = id; QuantumVertexCoverSolver.Weight = 1.0 })

let idx = routers |> List.mapi (fun i v -> v.Id, i) |> Map.ofList

// Physical links between routers (undirected)
let links =
    [ "R1","R2"; "R1","R3"; "R1","R5"    // R1 is a hub
      "R4","R3"; "R4","R5"; "R4","R6" ]  // R4 is a hub; together they touch every link
    |> List.map (fun (a, b) -> idx.[a], idx.[b])

let problem : QuantumVertexCoverSolver.Problem =
    { Vertices = routers
      Edges = links }

// ---------------------------------------------------------------------------
// Solve on the local simulator (a real quantum backend). Pass a cloud backend
// here to execute on hardware instead.
// ---------------------------------------------------------------------------

let backend = LocalBackend.LocalBackend() :> IQuantumBackend
[<Literal>]
let shots = 1000
let config = { QuantumVertexCoverSolver.defaultConfig with FinalShots = shots }

printfn "Network Monitoring — Minimum Vertex Cover (QAOA)\n"
printfn "Routers: %d, links to observe: %d\n" routers.Length links.Length

let result =
    QuantumVertexCoverSolver.solveWithConfigAsync backend problem config System.Threading.CancellationToken.None
    |> Async.AwaitTask
    |> Async.RunSynchronously

match result with
| Ok solution ->
    let monitors = solution.CoverVertices |> List.map (fun v -> v.Id) |> String.concat ", "
    printfn "Place monitors on   : { %s }" monitors
    printfn "Monitors required   : %d of %d routers" solution.CoverSize routers.Length
    printfn "Total cost (weight) : %.1f" solution.CoverWeight
    printfn "Every link covered  : %b" solution.IsValid
    printfn "Constraint repaired : %b" solution.WasRepaired
    printfn "Backend             : %s (%d shots)" solution.BackendName solution.NumShots
| Error err ->
    eprintfn "Vertex-cover optimisation failed: %s" err.Message
    exit 1
