namespace FSharp.Azure.Quantum.Examples.SupplyChain.NetworkFlowOptimization

open System
open System.Diagnostics
open System.IO
open System.Threading

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
    {
        Id: string
        NodeType: NodeType
        Capacity: int
        Supply: int option
        Demand: int option
    }

type Route =
    {
        From: string
        To: string
        Cost: float
    }

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

                        Ok
                            {
                                Id = id
                                NodeType = nodeType
                                Capacity = cap
                                Supply = supply
                                Demand = demand
                            }
                    | None, _ -> Error $"row=%d{rowNum} invalid node_type='%s{nt}'"
                    | _, (false, _) -> Error $"row=%d{rowNum} invalid capacity='%s{capStr}'"
                | _ -> Error $"row=%d{rowNum} missing node_id/node_type/capacity")
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
                    | false, _ -> Error $"row=%d{rowNum} invalid cost='%s{costStr}'"
                | _ -> Error $"row=%d{rowNum} missing from/to/cost")
            |> List.fold
                (fun (oks, errs) r ->
                    match r with
                    | Ok v -> (v :: oks, errs)
                    | Error e -> (oks, e :: errs))
                ([], [])

        (List.rev routes, structuralErrors @ (List.rev errors))

module private Model =
    let toQuantumProblem (nodes: Node list) (routes: Route list) : QuantumNetworkFlowSolver.NetworkFlowProblem =
        let sources =
            nodes |> List.choose (fun n -> if n.NodeType = Source then Some n.Id else None)

        let sinks =
            nodes |> List.choose (fun n -> if n.NodeType = Sink then Some n.Id else None)

        let intermediates =
            nodes
            |> List.choose (fun n -> if n.NodeType = Intermediate then Some n.Id else None)

        // Only intermediate nodes carry a throughput capacity in this model:
        // sources are limited by Supply and sinks by Demand. The CSV uses
        // capacity=0 for sinks as "not applicable", so including them in the
        // solver's Capacities map would make every flow that serves a sink
        // invalid under the solver's classical flow validation.
        let capacities =
            nodes
            |> List.choose (fun n ->
                if n.NodeType = Intermediate then
                    Some(n.Id, n.Capacity)
                else
                    None)
            |> Map.ofList

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
                {
                    Source = r.From
                    Target = r.To
                    Weight = r.Cost
                    Directed = true
                    Value = Some r.Cost
                    Properties = Map.empty
                })

        {
            Sources = sources
            Sinks = sinks
            IntermediateNodes = intermediates
            Edges = edges
            Capacities = capacities
            Demands = demands
            Supplies = supplies
        }

module private Validate =
    type Violation =
        {
            kind: string
            node: string
            details: string
        }

    let incoming (edges: Edge<float> list) (nodeId: string) =
        edges |> List.filter (fun e -> e.Target = nodeId)

    let outgoing (edges: Edge<float> list) (nodeId: string) =
        edges |> List.filter (fun e -> e.Source = nodeId)

    let validateBinaryFlow
        (problem: QuantumNetworkFlowSolver.NetworkFlowProblem)
        (selected: Edge<float> list)
        : Violation list =
        let sinks = problem.Sinks
        let intermediates = problem.IntermediateNodes

        let sinkViolations =
            sinks
            |> List.choose (fun s ->
                let inCount = incoming selected s |> List.length

                if inCount >= 1 then
                    None
                else
                    Some
                        {
                            kind = "sink_unserved"
                            node = s
                            details = "no incoming selected routes"
                        })

        let conservationViolations =
            intermediates
            |> List.choose (fun n ->
                let inCount = incoming selected n |> List.length
                let outCount = outgoing selected n |> List.length

                if inCount = outCount then
                    None
                else
                    Some
                        {
                            kind = "flow_conservation"
                            node = n
                            details = $"in=%d{inCount} out=%d{outCount}"
                        })

        sinkViolations @ conservationViolations

module private Classical =
    let solveGreedy
        (problem: QuantumNetworkFlowSolver.NetworkFlowProblem)
        : Edge<float> list * Validate.Violation list =
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
                                | Some e when sel |> List.exists (fun x -> x.Source = e.Source && x.Target = e.Target) ->
                                    (sel, anyChanged)
                                | Some e -> (e :: sel, true)
                            else
                                (sel, anyChanged))
                        (selected, false)

                if changed then
                    closeConservation (remainingPasses - 1) updated
                else
                    updated

        let selected =
            closeConservation 100 initialSelected
            |> List.distinctBy (fun e -> e.Source, e.Target)

        let violations = Validate.validateBinaryFlow problem selected
        (selected, violations)

/// Service measures of a route set. Each open route carries one unit.
module private Measure =
    let private unitsInto (selected: Edge<float> list) (sink: string) =
        selected |> List.filter (fun e -> e.Target = sink) |> List.length

    let private demandOf (problem: QuantumNetworkFlowSolver.NetworkFlowProblem) (sink: string) =
        problem.Demands |> Map.tryFind sink |> Option.defaultValue 1

    /// Units delivered: one per open route into a customer, capped at its demand.
    let delivered (problem: QuantumNetworkFlowSolver.NetworkFlowProblem) (selected: Edge<float> list) =
        problem.Sinks
        |> List.sumBy (fun s -> min (unitsInto selected s) (demandOf problem s))

    /// Customers whose whole demand is delivered.
    let customersServed (problem: QuantumNetworkFlowSolver.NetworkFlowProblem) (selected: Edge<float> list) =
        problem.Sinks
        |> List.filter (fun s -> unitsInto selected s >= demandOf problem s)
        |> List.length

    /// Demand fill rate: delivered units over demanded units.
    let demandFillRate (problem: QuantumNetworkFlowSolver.NetworkFlowProblem) (selected: Edge<float> list) =
        let total = problem.Sinks |> List.sumBy (demandOf problem)

        if total = 0 then
            0.0
        else
            float (delivered problem selected) / float total

/// The run as an animated picture (only with --svg): each solution on the
/// same network, its routes switched on stage by stage in shipping order.
module private Picture =
    open Svg

    type Solution =
        {
            Name: string
            Method: string
            Selected: Edge<float> list
            /// Units on a selected route, as the solution states them.
            Flow: string * string -> float
            Cost: float
            /// Delivered units over demanded units.
            DemandFillRate: float
            Violations: Validate.Violation list
            Failed: bool
        }

    let private word =
        function
        | Source -> "suppliers"
        | Intermediate -> "warehouses"
        | Sink -> "customers"

    let draw (path: string) (nodes: Node list) (routes: Route list) (solutions: Solution list) =
        let byId = nodes |> List.map (fun n -> n.Id, n) |> Map.ofList

        let known (a: string) (b: string) =
            byId.ContainsKey a && byId.ContainsKey b

        let routes = routes |> List.filter (fun r -> known r.From r.To)

        let solutions =
            solutions
            |> List.map (fun s ->
                { s with
                    Selected = s.Selected |> List.filter (fun e -> known e.Source e.Target)
                })

        // Columns: sources first, then each node one step after its furthest
        // predecessor, customers last.
        let depth =
            let start =
                nodes
                |> List.map (fun n -> n.Id, (if n.NodeType = Source then 0 else 1))
                |> Map.ofList

            let relax (d: Map<string, int>) =
                routes
                |> List.fold
                    (fun (acc: Map<string, int>) r ->
                        acc.Add(r.To, max acc.[r.To] (min nodes.Length (acc.[r.From] + 1))))
                    d

            let settled = Seq.fold (fun d _ -> relax d) start [ 1 .. nodes.Length ]
            let last = nodes |> List.map (fun n -> settled.[n.Id]) |> List.max |> max 1

            nodes
            |> List.map (fun n -> n.Id, (if n.NodeType = Sink then last else settled.[n.Id]))
            |> Map.ofList

        let levels = (nodes |> List.map (fun n -> depth.[n.Id]) |> List.max) + 1

        let stages =
            routes |> List.map (fun r -> depth.[r.From]) |> List.distinct |> List.sort

        let stageOf (r: Edge<float>) =
            1 + (stages |> List.findIndex ((=) depth.[r.Source]))

        let resultFrame = 1 + stages.Length
        let frames = resultFrame + 3
        let from k = Array.init frames (fun f -> f >= k)
        // 2.8 s a step plus a second to read it, as SvgAnimation.fs gives step pictures.
        let durationS = 3.8 * float frames

        let pic =
            Picture(760.0, 520.0, frames, durationS, "Supply chain route activation: classical greedy and QAOA")

        pic.Text(24.0, 28.0, "Supply chain: which routes to open, classical greedy and QAOA", size = 17.0, bold = true)

        pic.Text(
            24.0,
            47.0,
            "One run on the tiny dataset. Each open route carries 1 unit, as both solvers model it; routes have no capacity limit.",
            size = 11.5,
            fill = grey
        )

        let served (s: Solution) (sink: Node) =
            let delivered = s.Selected |> List.filter (fun e -> e.Target = sink.Id)
            let units = delivered |> List.sumBy (fun e -> s.Flow(e.Source, e.Target))

            if delivered.IsEmpty || units < float (defaultArg sink.Demand 1) then
                None
            else
                Some(delivered |> List.map stageOf |> List.max)

        let sinks = nodes |> List.filter (fun n -> n.NodeType = Sink)

        let servedCount (s: Solution) f =
            sinks
            |> List.filter (fun c ->
                match served s c with
                | Some k -> k <= f
                | None -> false)
            |> List.length

        let stageWords d =
            let at k =
                nodes
                |> List.filter (fun n -> depth.[n.Id] = k)
                |> List.map (fun n -> word n.NodeType)
                |> List.distinct
                |> String.concat "/"

            let next =
                routes
                |> List.filter (fun r -> depth.[r.From] = d)
                |> List.map (fun r -> word byId.[r.To].NodeType)
                |> List.distinct
                |> String.concat "/"

            sprintf "%s to %s" (at d) next

        let summary (s: Solution) =
            if s.Failed then
                $"%s{s.Name}: no valid flow"
            else
                sprintf "%s: %d of %d customers, cost %s" s.Name (servedCount s resultFrame) sinks.Length (num s.Cost)

        pic.FrameText(
            24.0,
            76.0,
            Array.init frames (fun f ->
                if f = 0 then
                    $"The network: %d{routes.Length} candidate routes, each with its cost per unit"
                elif f < resultFrame then
                    sprintf "Stage %d of %d: ship from %s" f stages.Length (stageWords stages.[f - 1])
                else
                    solutions |> List.map summary |> String.concat " · "),
            size = 14.0,
            bold = true
        )

        let panelW, panelH, panelY = 350.0, 336.0, 90.0

        for i, s in List.indexed solutions do
            let px = 24.0 + float i * (panelW + 12.0)
            pic.Rect(px, panelY, panelW, panelH, fill = panel, stroke = frameColour, rx = 4.0)
            pic.Text(px + 14.0, panelY + 22.0, s.Name, size = 13.0, bold = true)
            pic.Text(px + 14.0, panelY + 38.0, s.Method, size = 10.5, fill = grey)

            let areaTop, areaBottom = panelY + 52.0, panelY + 280.0

            let position =
                nodes
                |> List.groupBy (fun n -> depth.[n.Id])
                |> List.collect (fun (d, column) ->
                    let x = px + 50.0 + float d * (panelW - 100.0) / float (max 1 (levels - 1))
                    let step = (areaBottom - areaTop) / float column.Length
                    column |> List.mapi (fun k n -> n.Id, (x, areaTop + step * (float k + 0.5))))
                |> Map.ofList

            // Route ends stop at the node's edge.
            let ends (a: string) (b: string) =
                let (x1, y1), (x2, y2) = position.[a], position.[b]
                let len = max 1e-6 (sqrt ((x2 - x1) ** 2.0 + (y2 - y1) ** 2.0))
                let ux, uy = (x2 - x1) / len, (y2 - y1) / len
                (x1 + ux * 16.0, y1 + uy * 16.0, x2 - ux * 16.0, y2 - uy * 16.0)

            let selected =
                s.Selected |> List.map (fun e -> (e.Source, e.Target), e) |> Map.ofList

            let maxFlow =
                s.Selected
                |> List.map (fun e -> s.Flow(e.Source, e.Target))
                |> List.fold max 1e-9

            let accent = colour 0

            for r in routes do
                let x1, y1, x2, y2 = ends r.From r.To
                pic.Line(x1, y1, x2, y2, stroke = "#c8c8c8", width = 1.5)

            for KeyValue((a, b), e) in selected do
                if position.ContainsKey a && position.ContainsKey b then
                    let x1, y1, x2, y2 = ends a b
                    let share = s.Flow(a, b) / maxFlow
                    let shown = from (stageOf e)
                    pic.Line(x1, y1, x2, y2, stroke = tint 0.6 accent, width = 3.0 + 4.0 * share, shown = shown)
                    pic.Dots(x1, y1, x2, y2, accent, 5.0, 12.0 / max share 0.1, 28.0, shown = shown)

            // Cost labels a third of the way along, clear of crossings at the middle.
            for r in routes do
                let x1, y1, x2, y2 = ends r.From r.To
                let lx, ly = x1 + (x2 - x1) * 0.3, y1 + (y2 - y1) * 0.3
                let label = num r.Cost
                let w = 7.0 + 6.2 * float label.Length
                pic.Rect(lx - w / 2.0, ly - 8.0, w, 15.0, fill = "white", stroke = "#dddddd", rx = 3.0)
                pic.Text(lx, ly + 3.5, label, size = 10.0, fill = grey, anchor = "middle")

                match selected.TryFind((r.From, r.To)) with
                | Some e ->
                    let shown = from (stageOf e)
                    pic.Rect(lx - w / 2.0, ly - 8.0, w, 15.0, fill = "white", stroke = accent, rx = 3.0, shown = shown)

                    pic.Text(
                        lx,
                        ly + 3.5,
                        label,
                        size = 10.0,
                        fill = accent,
                        bold = true,
                        anchor = "middle",
                        shown = shown
                    )
                | None -> ()

            let broken =
                s.Violations
                |> List.filter (fun v -> v.node <> "")
                |> List.map (fun v -> v.node, v.kind)
                |> Map.ofList

            for n in nodes do
                let x, y = position.[n.Id]

                match n.NodeType with
                | Source ->
                    pic.Rect(
                        x - 13.0,
                        y - 13.0,
                        26.0,
                        26.0,
                        fill = tint 0.55 (colour 4),
                        stroke = colour 4,
                        rx = 6.0,
                        strokeWidth = 1.5
                    )
                | Intermediate ->
                    pic.Rect(
                        x - 13.0,
                        y - 13.0,
                        26.0,
                        26.0,
                        fill = tint 0.6 (colour 3),
                        stroke = colour 3,
                        rx = 1.0,
                        strokeWidth = 1.5
                    )
                | Sink ->
                    pic.Circle(x, y, 13.0, fill = "white", stroke = grey, strokeWidth = 1.5)

                    match served s n with
                    | Some k ->
                        pic.Circle(
                            x,
                            y,
                            13.0,
                            fill = tint 0.45 (colour 2),
                            stroke = colour 2,
                            strokeWidth = 2.0,
                            shown = from k
                        )
                    | None -> ()

                pic.Text(x, y + 4.0, n.Id, size = 10.0, bold = true, anchor = "middle")

                let detail =
                    match n.NodeType with
                    | Source -> sprintf "supply %d" (defaultArg n.Supply 0)
                    | Intermediate -> $"capacity %d{n.Capacity}"
                    | Sink -> sprintf "demand %d" (defaultArg n.Demand 0)

                pic.Text(x, y + 28.0, detail, size = 9.5, fill = grey, anchor = "middle", halo = true)

                match broken.TryFind n.Id with
                | Some kind ->
                    let shown = from resultFrame
                    pic.Circle(x, y, 18.0, stroke = colour 1, strokeWidth = 2.0, dash = "4 3", shown = shown)

                    let text =
                        if kind = "sink_unserved" then
                            "not served"
                        else
                            "unbalanced"

                    pic.Text(
                        x,
                        y - 22.0,
                        text,
                        size = 10.0,
                        fill = colour 1,
                        bold = true,
                        anchor = "middle",
                        shown = shown
                    )
                | None -> ()

            if s.Failed then
                pic.Text(
                    px + panelW / 2.0,
                    areaBottom + 20.0,
                    "No valid flow in the samples",
                    size = 12.0,
                    fill = colour 1,
                    bold = true,
                    anchor = "middle"
                )

            // Running totals: cost of the routes open so far, customers served.
            let costAt f =
                s.Selected
                |> List.filter (fun e -> stageOf e <= f)
                |> List.sumBy (fun e -> e.Weight)

            pic.FrameText(
                px + 14.0,
                panelY + 300.0,
                Array.init frames (fun f ->
                    if f < resultFrame then
                        sprintf "Cost so far %s" (num (costAt f))
                    else
                        sprintf "Total cost %s" (num s.Cost)),
                size = 13.0,
                bold = true
            )

            pic.FrameText(
                px + panelW - 14.0,
                panelY + 300.0,
                Array.init frames (fun f ->
                    sprintf "%d of %d customers served" (servedCount s (min f resultFrame)) sinks.Length),
                size = 12.0,
                anchor = "end"
            )

            pic.FrameText(
                px + 14.0,
                panelY + 321.0,
                Array.init frames (fun f ->
                    if f < resultFrame then
                        ""
                    else
                        sprintf
                            "Demand fill rate %.0f%% · rule violations %d"
                            (100.0 * s.DemandFillRate)
                            s.Violations.Length),
                size = 11.0,
                fill = grey
            )

        // Legend.
        let ly = 448.0
        pic.Line(30.0, ly, 54.0, ly, stroke = "#c8c8c8", width = 1.5)
        pic.Text(60.0, ly + 4.0, "candidate route", size = 11.0, fill = grey)
        pic.Line(160.0, ly, 190.0, ly, stroke = tint 0.6 (colour 0), width = 7.0)
        pic.Dots(160.0, ly, 190.0, ly, colour 0, 5.0, 12.0, 28.0)
        pic.Text(196.0, ly + 4.0, "open route, dots = units moving", size = 11.0, fill = grey)

        pic.Rect(
            378.0,
            ly - 8.0,
            16.0,
            16.0,
            fill = tint 0.55 (colour 4),
            stroke = colour 4,
            rx = 4.0,
            strokeWidth = 1.5
        )

        pic.Text(400.0, ly + 4.0, "supplier", size = 11.0, fill = grey)

        pic.Rect(
            456.0,
            ly - 8.0,
            16.0,
            16.0,
            fill = tint 0.6 (colour 3),
            stroke = colour 3,
            rx = 1.0,
            strokeWidth = 1.5
        )

        pic.Text(478.0, ly + 4.0, "warehouse", size = 11.0, fill = grey)
        pic.Circle(548.0, ly, 8.0, fill = tint 0.45 (colour 2), stroke = colour 2, strokeWidth = 2.0)
        pic.Text(562.0, ly + 4.0, "customer served", size = 11.0, fill = grey)
        pic.Circle(668.0, ly, 8.0, stroke = colour 1, strokeWidth = 2.0, dash = "4 3")
        pic.Text(682.0, ly + 4.0, "violation", size = 11.0, fill = grey)

        pic.Progress(24.0, 474.0, 712.0)

        pic.Text(
            24.0,
            500.0,
            "Numbers on routes: cost per unit. Totals: sum of the open routes' costs. QAOA samples differ from run to run.",
            size = 10.0,
            fill = grey
        )

        pic.Save path

type Metrics =
    {
        run_id: string
        nodes_path: string
        routes_path: string
        nodes_sha256: string
        routes_sha256: string
        nodes_count: int
        routes_count: int
        shots: int
        customers_total: int
        classical_cost: float
        classical_customers_served: int
        classical_demand_fill_rate: float
        classical_violations: int
        quantum_cost: float
        quantum_customers_served: int
        quantum_demand_fill_rate: float
        quantum_violations: int
        elapsed_ms_total: int64
        elapsed_ms_classical: int64
        elapsed_ms_quantum: int64
    }

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

            printfn
                "  --svg [path]     (also draw the run as an animated SVG; default: examples/SupplyChain/_images/supply-chain-flow.svg)"

            0
        else
            let swTotal = Stopwatch.StartNew()

            let nodesPath = Cli.getOr "nodes" "examples/SupplyChain/_data/nodes_tiny.csv" args

            let routesPath =
                Cli.getOr "routes" "examples/SupplyChain/_data/routes_tiny.csv" args

            let outDir =
                Cli.getOr "out" (Path.Combine("runs", "supplychain", "networkflow")) args

            let shots = Cli.getIntOr "shots" 1000 args

            Data.ensureDirectory outDir
            let runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss")

            Reporting.writeJson
                (Path.Combine(outDir, "run-config.json"))
                {|
                    run_id = runId
                    utc = DateTimeOffset.UtcNow
                    nodes = nodesPath
                    routes = routesPath
                    out = outDir
                    shots = shots
                |}

            let nodesSha = Data.fileSha256Hex nodesPath
            let routesSha = Data.fileSha256Hex routesPath

            let nodes, nodeErrors = Parse.readNodes nodesPath
            let routes, routeErrors = Parse.readRoutes routesPath

            let parseErrors = nodeErrors @ routeErrors

            if not parseErrors.IsEmpty then
                Reporting.writeCsv
                    (Path.Combine(outDir, "bad_rows.csv"))
                    [ "error" ]
                    (parseErrors |> List.map (fun e -> [ e ]))

            if nodes.IsEmpty || routes.IsEmpty then
                Reporting.writeTextFile
                    (Path.Combine(outDir, "run-report.md"))
                    "# Network Flow Optimization\n\nNo nodes or no routes parsed; see bad_rows.csv.\n"

                2
            else
                let problem = Model.toQuantumProblem nodes routes

                let swClassical = Stopwatch.StartNew()
                let classicalSelected, classicalViolations = Classical.solveGreedy problem
                swClassical.Stop()

                let classicalCost = classicalSelected |> List.sumBy (fun e -> e.Weight)
                let classicalServed = Measure.customersServed problem classicalSelected
                let classicalDemandFillRate = Measure.demandFillRate problem classicalSelected

                let swQuantum = Stopwatch.StartNew()
                let backend = LocalBackend() :> IQuantumBackend
                let quantumResult =
                    QuantumNetworkFlowSolver.solveWithShotsAsync backend problem shots CancellationToken.None
                    |> Async.AwaitTask
                    |> Async.RunSynchronously

                swQuantum.Stop()

                let quantumSelected, quantumCost, quantumViolations =
                    match quantumResult with
                    | Error err ->
                        let solverError: Validate.Violation =
                            {
                                kind = "solver_error"
                                node = ""
                                details = $"quantum solver failed: %s{err.Message}"
                            }

                        // 0.0 (not nan) so metrics stay JSON-serializable; the
                        // solver_error violation row records the failure.
                        ([], 0.0, [ solverError ])
                    | Ok sol ->
                        (sol.SelectedEdges, sol.TotalCost, Validate.validateBinaryFlow problem sol.SelectedEdges)

                let quantumServed = Measure.customersServed problem quantumSelected
                let quantumDemandFillRate = Measure.demandFillRate problem quantumSelected
                let customers = problem.Sinks.Length

                let describe (cost: float) (served: int) (fillRate: float) (violations: int) =
                    $"cost %.0f{cost}, customers served %d{served} of %d{customers}, demand fill rate %.0f{100.0 * fillRate}%%, violations %d{violations}"

                printfn
                    "Classical greedy: %s"
                    (describe classicalCost classicalServed classicalDemandFillRate classicalViolations.Length)

                match quantumResult with
                | Ok _ ->
                    printfn
                        "QAOA (p = 1, %d shots on %s): %s"
                        shots
                        backend.Name
                        (describe quantumCost quantumServed quantumDemandFillRate quantumViolations.Length)
                | Error _ -> printfn "QAOA: %s" quantumViolations.Head.details

                let writeSelected (path: string) (selected: Edge<float> list) =
                    let rows =
                        selected
                        |> List.sortBy (fun e -> e.Source, e.Target)
                        |> List.map (fun e -> [ e.Source; e.Target; $"%.6f{e.Weight}" ])

                    Reporting.writeCsv path [ "from"; "to"; "cost" ] rows

                writeSelected (Path.Combine(outDir, "solution_classical.csv")) classicalSelected
                writeSelected (Path.Combine(outDir, "solution_quantum.csv")) quantumSelected

                let allViolations =
                    [ ("classical", classicalViolations); ("quantum", quantumViolations) ]
                    |> List.collect (fun (label, vs) -> vs |> List.map (fun v -> [ label; v.kind; v.node; v.details ]))

                Reporting.writeCsv
                    (Path.Combine(outDir, "violations.csv"))
                    [ "solution"; "kind"; "node"; "details" ]
                    allViolations

                swTotal.Stop()

                let metrics: Metrics =
                    {
                        run_id = runId
                        nodes_path = nodesPath
                        routes_path = routesPath
                        nodes_sha256 = nodesSha
                        routes_sha256 = routesSha
                        nodes_count = nodes.Length
                        routes_count = routes.Length
                        shots = shots
                        customers_total = customers
                        classical_cost = classicalCost
                        classical_customers_served = classicalServed
                        classical_demand_fill_rate = classicalDemandFillRate
                        classical_violations = classicalViolations.Length
                        quantum_cost = quantumCost
                        quantum_customers_served = quantumServed
                        quantum_demand_fill_rate = quantumDemandFillRate
                        quantum_violations = quantumViolations.Length
                        elapsed_ms_total = swTotal.ElapsedMilliseconds
                        elapsed_ms_classical = swClassical.ElapsedMilliseconds
                        elapsed_ms_quantum = swQuantum.ElapsedMilliseconds
                    }

                Reporting.writeJson (Path.Combine(outDir, "metrics.json")) metrics

                let report =
                    $"""# Supply Chain Network Flow Optimization

This example models supply chain planning as **route activation** (binary decision per route), and compares:

- Classical baseline: greedy route activation
- Quantum: QAOA (p = 1) via `QuantumNetworkFlowSolver` (LocalBackend). Of the valid sampled flows (flow conserved, no node over its capacity, supply or demand), the solver returns the one meeting the most demand, then the cheapest.

Important: this is *not* a continuous min-cost flow model. It is a small, backend-friendly formulation that is useful as a template for building stronger encodings. Each open route carries one unit.

## Measures

- **Customers served**: customers whose whole demand is delivered.
- **Demand fill rate**: delivered units over demanded units, one unit per open route, capped at each customer's demand.

## Results

| Solution | Cost | Customers served | Demand fill rate | Violations |
|---|---|---|---|---|
| Classical greedy | {classicalCost} | {classicalServed} of {customers} | {100.0 * classicalDemandFillRate:F0}%% | {classicalViolations.Length} |
| QAOA | {quantumCost} | {quantumServed} of {customers} | {100.0 * quantumDemandFillRate:F0}%% | {quantumViolations.Length} |

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

                let svgPath =
                    match Cli.tryGet "svg" args with
                    | Some p -> Some p
                    | None when Cli.hasFlag "svg" args ->
                        Some(Path.Combine(__SOURCE_DIRECTORY__, "..", "_images", "supply-chain-flow.svg"))
                    | None -> None

                match svgPath with
                | Some file ->
                    Picture.draw
                        file
                        nodes
                        routes
                        [
                            {
                                Picture.Name = "Classical greedy"
                                Method = "Cheapest route into each customer, then close gaps upstream"
                                Selected = classicalSelected
                                // Route activation: one unit on every open route.
                                Flow = fun _ -> 1.0
                                Cost = classicalCost
                                DemandFillRate = classicalDemandFillRate
                                Violations = classicalViolations
                                Failed = false
                            }
                            {
                                Picture.Name = "QAOA"
                                Method = $"p = 1, %d{shots} shots: most demand met, then cheapest"
                                Selected = quantumSelected
                                Flow = fun _ -> 1.0
                                Cost = quantumCost
                                DemandFillRate = quantumDemandFillRate
                                Violations = quantumViolations
                                Failed = Result.isError quantumResult
                            }
                        ]
                | None -> ()

                printfn "Wrote outputs to: %s" outDir
                0
