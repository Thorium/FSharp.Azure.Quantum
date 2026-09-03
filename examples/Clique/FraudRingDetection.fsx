/// Maximum Clique — Fraud-Ring Detection
///
/// USE CASE: In a payments network, accounts that repeatedly transact with one
/// another form tightly-knit groups. A *fraud ring* shows up as a clique — a set
/// of accounts where every pair has transacted directly. Finding the largest such
/// clique surfaces the core of a collusion ring for investigation.
///
/// PROBLEM: Given a graph of accounts (vertices) and "transacted-with" links
/// (edges), find the maximum clique — the largest subset where all pairs are
/// connected.
///
/// QUANTUM: Encoded as a QAOA optimisation and executed on the unified
/// IQuantumBackend. This example runs on the local simulator; swap in any cloud
/// backend (IonQ / Rigetti / Quantinuum) to run on real hardware.
///
/// Run with: dotnet fsi FraudRingDetection.fsx

//#r "nuget: FSharp.Azure.Quantum"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "nuget: MathNet.Numerics, 5.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Quantum

// ---------------------------------------------------------------------------
// Build the account-transaction graph.
//   Accounts A..E all transact with one another (a 5-clique — the fraud ring).
//   Accounts F, G are ordinary customers with a couple of links to the ring.
// ---------------------------------------------------------------------------

let accounts =
    [ "A"; "B"; "C"; "D"; "E"; "F"; "G" ]
    |> List.map (fun id -> { QuantumCliqueSolver.Id = id; QuantumCliqueSolver.Weight = 1.0 })

let idx = accounts |> List.mapi (fun i v -> v.Id, i) |> Map.ofList

// "transacted-with" links (undirected)
let links =
    [ "A","B"; "A","C"; "A","D"; "A","E"
      "B","C"; "B","D"; "B","E"
      "C","D"; "C","E"
      "D","E"                         // A-B-C-D-E fully connected → the ring
      "A","F"; "F","G" ]              // ordinary customers on the fringe
    |> List.map (fun (a, b) -> idx.[a], idx.[b])

let problem : QuantumCliqueSolver.Problem =
    { Vertices = accounts
      Edges = links }

// ---------------------------------------------------------------------------
// Solve on the local simulator (a real quantum backend). Pass a cloud backend
// here to execute on hardware instead.
// ---------------------------------------------------------------------------

let backend = LocalBackend.LocalBackend() :> IQuantumBackend
[<Literal>]
let shots = 1000
let config = { QuantumCliqueSolver.defaultConfig with FinalShots = shots }

printfn "Fraud-Ring Detection — Maximum Clique (QAOA)\n"
printfn "Accounts: %d, transaction links: %d\n" accounts.Length links.Length

let result =
    QuantumCliqueSolver.solveWithConfigAsync backend problem config System.Threading.CancellationToken.None
    |> Async.AwaitTask
    |> Async.RunSynchronously

match result with
| Ok solution ->
    let ring = solution.CliqueVertices |> List.map (fun v -> v.Id) |> String.concat ", "
    printfn "Largest fraud ring (clique): { %s }" ring
    printfn "Ring size            : %d accounts" solution.CliqueSize
    printfn "All pairs connected  : %b" solution.IsValid
    printfn "Constraint repaired  : %b" solution.WasRepaired
    printfn "Backend              : %s (%d shots)" solution.BackendName solution.NumShots
    if solution.CliqueSize >= 5 then
        printfn "\n⚠  A ring of %d fully-colluding accounts detected — flag for investigation." solution.CliqueSize
| Error err ->
    eprintfn "Clique detection failed: %s" err.Message
    exit 1
