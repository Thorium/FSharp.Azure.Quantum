namespace FSharp.Azure.Quantum.Examples.SupplyChain.NetworkFlowOptimization

open System
open System.Diagnostics
open System.IO

open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.GraphOptimization
open FSharp.Azure.Quantum.Quantum

open FSharp.Azure.Quantum.Examples.Common

type NodeType =
    | Source
    | Intermediate
    | Sink

type Node =
    { Id: string
      NodeType: NodeType
      Capacity: int
      Supply: int option
      Demand: int option }

type Route = { From: string; To: string; Cost: float }

module private Parse =
    let private tryGet (k: string) (row: Data.CsvRow) =
        row.Values |> Map.tryFind k |> Option.map (fun s -> s.Trim())

    let private tryInt (s: string option) =
        match s with
        | None -> None
        | Some v when v = "" -> None
        | Some v ->
            match Int32.TryParse v with
            | true, x -> Some x
            | false, _ -> None

    let private tryFloat (s: string option) =
        match s with
        | None -> None
        | Some v when v = "" -> None
        | Some v ->
            match Double.TryParse v with
            | true, x -> Some x
            | false, _ -> None

    let private parseNodeType (s: string) =
        match s.Trim().ToLowerInvariant() with
        | "source" -> Some Source
        | "intermediate" -> Some Intermediate
        | "sink" -> Some Sink
        | _ -> None

    let readNodes (path: string) : Node list * string list =
        let rows, structuralErrors = Data.readCsvWithHeaderWithErrors path

        let nodes, errors =
            rows
            |> List.mapi (fun i row ->
                let rowNum = i + 2
                match tryGet "node_id" row, tryGet "node_type" row, tryGet "capacity" row with
                | Some id, Some nt, Some capStr ->
                    match parseNodeType nt, Int32.TryParse capStr with
                    | Some nodeType, (true, cap) ->
                        let supply = tryInt (tryGet "supply" row)
                        let demand = tryInt (tryGet "demand" row)
                        Ok { Id = id; NodeType = nodeType; Capacity = cap; Supply = supply; Demand = demand }
                    | None, _ -> Error (sprintf "row=%d invalid node_type='%s'" rowNum nt)
                    | _, (false, _) -> Error (sprintf "row=%d invalid capacity='%s'" rowNum capStr)
                | _ -> Error (sprintf "row=%d missing node_id/node_type/capacity" rowNum))
            |> List.fold
                (fun (oks, errs) r ->
                    match r with
                    | Ok v -> (v :: oks, errs)
                    | Error e -> (oks, e :: errs))
                ([], [])

        (List.rev nodes, structuralErrors @ (List.rev errors))

    let readRoutes (path: string) : Route list * string list =
        let rows, structuralErrors = Data.readCsvWithHeaderWithErrors path

        let routes, errors =
            rows
            |> List.mapi (fun i row ->
                let rowNum = i + 2
                match tryGet "from" row, tryGet "to" row, tryGet "cost" row with
                | Some f, Some t, Some costStr ->
                    match Double.TryParse costStr with
                    | true, c -> Ok { From = f; To = t; Cost = c }
                    | false, _ -> Error (sprintf "row=%d invalid cost='%s'" rowNum costStr)
                | _ -> Error (sprintf "row=%d missing from/to/cost" rowNum))
            |> List.fold
                (fun (oks, errs) r ->
                    match r with
                    | Ok v -> (v :: oks, errs)
                    | Error e -> (oks, e :: errs))
                ([], [])

        (List.rev routes, structuralErrors @ (List.rev errors))

module private Model =
    let toQuantumProblem (nodes: Node list) (routes: Route list) : QuantumNetworkFlowSolver.NetworkFlowProblem =
        let sources = nodes |> List.choose (fun n -> if n.NodeType = Source then Some n.Id else None)
        let sinks = nodes |> List.choose (fun n -> if n.NodeType = Sink then Some n.Id else None)
        let intermediates = nodes |> List.choose (fun n -> if n.NodeType = Intermediate then Some n.Id else None)

        let capacities = nodes |> List.map (fun n -> n.Id, n.Capacity) |> Map.ofList
        let demands =
            nodes
            |> List.choose (fun n -> n.Demand |> Option.map (fun d -> n.Id, d))
            |> Map.ofList
        let supplies =
            nodes
            |> List.choose (fun n -> n.Supply |> Option.map (fun s -> n.Id, s))
            |> Map.ofList

        let edges =
            routes
            |> List.map (fun r ->
                { Source = r.From
                  Target = r.To
                  Weight = r.Cost
                  Directed = true
                  Value = Some r.Cost
                  Properties = Map.empty })

        { Sources = sources
          Sinks = sinks
          IntermediateNodes = intermediates
          Edges = edges
          Capacities = capacities
          Demands = demands
          Supplies = supplies }

module private Validate =
    type Violation = { kind: string; node: string; details: string }

    let incoming (edges: Edge<float> list) (nodeId: string) =
        edges |> List.filter (fun e -> e.Target = nodeId)

    let outgoing (edges: Edge<float> list) (nodeId: string) =
        edges |> List.filter (fun e -> e.Source = nodeId)

    let validateBinaryFlow (problem: QuantumNetworkFlowSolver.NetworkFlowProblem) (selected: Edge<float> list) : Violation list =
        let sinks = problem.Sinks
        let intermediates = problem.IntermediateNodes

        let sinkViolations =
            sinks
            |> List.choose (fun s ->
                let inCount = incoming selected s |> List.length
                if inCount >= 1 then None
                else Some { kind = "sink_unserved"; node = s; details = "no incoming selected routes" })

        let conservationViolations =
            intermediates
            |> List.choose (fun n ->
                let inCount = incoming selected n |> List.length
                let outCount = outgoing selected n |> List.length
                if inCount = outCount then None
                else
                    Some
                        { kind = "flow_conservation"
                          node = n
                          details = sprintf "in=%d out=%d" inCount outCount })

        sinkViolations @ conservationViolations

module private Classical =
    let solveGreedy (problem: QuantumNetworkFlowSolver.NetworkFlowProblem) : Edge<float> list * Validate.Violation list =
        // Baseline: ensure each sink has at least one incoming edge, then iteratively
        // close conservation gaps for intermediate nodes by adding cheapest missing incoming edges.
        let allEdges = problem.Edges
        let sinks = problem.Sinks

        let initialSelected =
            sinks
            |> List.choose (fun s ->
                allEdges
                |> List.filter (fun e -> e.Target = s)
                |> List.sortBy (fun e -> e.Weight)
                |> List.tryHead)

        let cheapestIncoming (nodeId: string) =
            allEdges
            |> List.filter (fun e -> e.Target = nodeId)
            |> List.sortBy (fun e -> e.Weight)
            |> List.tryHead

        // Iteratively satisfy conservation: if an intermediate has out>in, add incoming.
        let rec closeConservation (remainingPasses: int) (selected: Edge<float> list) : Edge<float> list =
            if remainingPasses <= 0 then
                selected
            else
                let updated, changed =
                    problem.IntermediateNodes
                    |> List.fold
                        (fun (sel, anyChanged) n ->
                            let inCount = sel |> List.filter (fun e -> e.Target = n) |> List.length
                            let outCount = sel |> List.filter (fun e -> e.Source = n) |> List.length

                            if outCount > inCount then
                                match cheapestIncoming n with
                                | None -> (sel, anyChanged)
                                | Some e when sel |> List.exists (fun x -> x.Source = e.Source && x.Target = e.Target) -> (sel, anyChanged)
                                | Some e -> (e :: sel, true)
                            else
                                (sel, anyChanged))
                        (selected, false)

                if changed then closeConservation (remainingPasses - 1) updated else updated

        let selected = closeConservation 100 initialSelected |> List.distinctBy (fun e -> e.Source, e.Target)
        let violations = Validate.validateBinaryFlow problem selected
        (selected, violations)

type Metrics =
    { run_id: string
      nodes_path: string
      routes_path: string
      nodes_sha256: string
      routes_sha256: string
      nodes_count: int
      routes_count: int
      shots: int
      classical_cost: float
      classical_fill_rate: float
      classical_violations: int
      quantum_cost: float
      quantum_fill_rate: float
      quantum_violations: int
      elapsed_ms_total: int64
      elapsed_ms_classical: int64
      elapsed_ms_quantum: int64 }

module Program =
    [<EntryPoint>]
    let main argv =
        let args = Cli.parse argv
        if Cli.hasFlag "help" args || Cli.hasFlag "h" args then
            printfn "NetworkFlowOptimization"
            printfn "  --nodes <path>   (CSV: node_id,node_type,capacity,supply,demand)"
            printfn "  --routes <path>  (CSV: from,to,cost)"
            printfn "  --out <dir>      (output folder)"
            printfn "  --shots <n>      (default: 1000)"
            0
        else
            let swTotal = Stopwatch.StartNew()

            let nodesPath = Cli.getOr "nodes" "examples/SupplyChain/_data/nodes_tiny.csv" args
            let routesPath = Cli.getOr "routes" "examples/SupplyChain/_data/routes_tiny.csv" args
            let outDir = Cli.getOr "out" (Path.Combine("runs", "supplychain", "networkflow")) args
            let shots = Cli.getIntOr "shots" 1000 args

            Data.ensureDirectory outDir
            let runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss")

            Reporting.writeJson
                (Path.Combine(outDir, "run-config.json"))
                {| run_id = runId
                   utc = DateTimeOffset.UtcNow
                   nodes = nodesPath
                   routes = routesPath
                   out = outDir
                   shots = shots |}

            let nodesSha = Data.fileSha256Hex nodesPath
            let routesSha = Data.fileSha256Hex routesPath

            let nodes, nodeErrors = Parse.readNodes nodesPath
            let routes, routeErrors = Parse.readRoutes routesPath

            let parseErrors = nodeErrors @ routeErrors
            if not parseErrors.IsEmpty then
                Reporting.writeCsv (Path.Combine(outDir, "bad_rows.csv")) [ "error" ] (parseErrors |> List.map (fun e -> [ e ]))

            if nodes.IsEmpty || routes.IsEmpty then
                Reporting.writeTextFile (Path.Combine(outDir, "run-report.md")) "# Network Flow Optimization\n\nNo nodes or no routes parsed; see bad_rows.csv.\n"
                2
            else
                let problem = Model.toQuantumProblem nodes routes

                let swClassical = Stopwatch.StartNew()
                let classicalSelected, classicalViolations = Classical.solveGreedy problem
                swClassical.Stop()

                let classicalCost = classicalSelected |> List.sumBy (fun e -> e.Weight)
                let classicalFillRate =
                    if problem.Sinks.Length = 0 then 0.0
                    else
                        let served =
                            problem.Sinks
                            |> List.filter (fun s -> classicalSelected |> List.exists (fun e -> e.Target = s))
                            |> List.length
                        float served / float problem.Sinks.Length

                let swQuantum = Stopwatch.StartNew()
                let backend = LocalBackend() :> IQuantumBackend
                let quantumResult = QuantumNetworkFlowSolver.solveWithShots backend problem shots
                swQuantum.Stop()

                let quantumSelected, quantumCost, quantumFillRate, quantumViolations =
                    match quantumResult with
                    | Error _ ->
                        let solverError: Validate.Violation =
                            { kind = "solver_error"
                              node = ""
                              details = "quantum solver failed" }

                        ([], nan, 0.0, [ solverError ])
                    | Ok sol ->
                        let v = Validate.validateBinaryFlow problem sol.SelectedEdges
                        (sol.SelectedEdges, sol.TotalCost, sol.FillRate, v)

                let writeSelected (path: string) (selected: Edge<float> list) =
                    let rows =
                        selected
                        |> List.sortBy (fun e -> e.Source, e.Target)
                        |> List.map (fun e -> [ e.Source; e.Target; sprintf "%.6f" e.Weight ])

                    Reporting.writeCsv path [ "from"; "to"; "cost" ] rows

                writeSelected (Path.Combine(outDir, "solution_classical.csv")) classicalSelected
                writeSelected (Path.Combine(outDir, "solution_quantum.csv")) quantumSelected

                let allViolations =
                    [ ("classical", classicalViolations)
                      ("quantum", quantumViolations) ]
                    |> List.collect (fun (label, vs) ->
                        vs |> List.map (fun v -> [ label; v.kind; v.node; v.details ]))

                Reporting.writeCsv
                    (Path.Combine(outDir, "violations.csv"))
                    [ "solution"; "kind"; "node"; "details" ]
                    allViolations

                swTotal.Stop()

                let metrics: Metrics =
                    { run_id = runId
                      nodes_path = nodesPath
                      routes_path = routesPath
                      nodes_sha256 = nodesSha
                      routes_sha256 = routesSha
                      nodes_count = nodes.Length
                      routes_count = routes.Length
                      shots = shots
                      classical_cost = classicalCost
                      classical_fill_rate = classicalFillRate
                      classical_violations = classicalViolations.Length
                      quantum_cost = quantumCost
                      quantum_fill_rate = quantumFillRate
                      quantum_violations = quantumViolations.Length
                      elapsed_ms_total = swTotal.ElapsedMilliseconds
                      elapsed_ms_classical = swClassical.ElapsedMilliseconds
                      elapsed_ms_quantum = swQuantum.ElapsedMilliseconds }

                Reporting.writeJson (Path.Combine(outDir, "metrics.json")) metrics

                let report =
                    $"""# Supply Chain Network Flow Optimization

This example models supply chain planning as **route activation** (binary decision per route), and compares:

- Classical baseline: greedy route activation
- Quantum: QAOA via `QuantumNetworkFlowSolver` (LocalBackend)

Important: this is *not* a continuous min-cost flow model. It is a small, backend-friendly formulation that is useful as a template for building stronger encodings.

## Inputs

- Nodes: `{nodesPath}` (sha256: `{nodesSha}`)
- Routes: `{routesPath}` (sha256: `{routesSha}`)

## Outputs

- `solution_classical.csv`
- `solution_quantum.csv`
- `violations.csv`
- `metrics.json`
"""

                Reporting.writeTextFile (Path.Combine(outDir, "run-report.md")) report

                printfn "Wrote outputs to: %s" outDir
                0
