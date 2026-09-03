namespace FSharp.Azure.Quantum

open FSharp.Azure.Quantum.CircuitBuilder

/// Qubit routing for connectivity-limited hardware.
///
/// Many quantum devices are NOT all-to-all connected: a two-qubit gate can only
/// be applied to qubits that are physically coupled. This pass takes a logical
/// circuit and a device `CouplingMap` and inserts SWAP gates so that every
/// two-qubit gate acts on adjacent qubits, while tracking the resulting
/// logical->physical qubit permutation (needed to interpret measurements).
///
/// All-to-all devices (e.g. IonQ, Quantinuum trapped ions) need no routing;
/// this is for grid/linear/ring topologies (e.g. superconducting / Rigetti).
///
/// The input circuit is expected to contain only 1- and 2-qubit gates (run
/// `GateTranspiler` first to decompose CCX/MCZ); 3+-qubit gates are passed
/// through unchanged (their connectivity is not enforced here).
module QubitRouting =

    /// Device connectivity: the set of physically-coupled (undirected) qubit pairs.
    type CouplingMap =
        { /// Number of physical qubits on the device.
          NumQubits: int
          /// Undirected coupling edges, each stored normalised as (min, max).
          Edges: Set<int * int> }

    let private norm (a, b) = if a <= b then (a, b) else (b, a)

    /// Build a coupling map from a list of undirected qubit pairs.
    let fromPairs (numQubits: int) (pairs: (int * int) list) : CouplingMap =
        { NumQubits = numQubits
          Edges = pairs |> List.map norm |> Set.ofList }

    /// Linear (path) topology: 0-1-2-...-(n-1).
    let linear (n: int) : CouplingMap =
        fromPairs n [ for i in 0 .. n - 2 -> (i, i + 1) ]

    /// Ring topology: linear plus a wrap-around edge.
    let ring (n: int) : CouplingMap =
        if n <= 2 then linear n
        else fromPairs n ([ for i in 0 .. n - 2 -> (i, i + 1) ] @ [ (0, n - 1) ])

    /// 2D grid topology (rows x cols), qubits numbered row-major.
    let grid (rows: int) (cols: int) : CouplingMap =
        let idx r c = r * cols + c
        let edges =
            [ for r in 0 .. rows - 1 do
                for c in 0 .. cols - 1 do
                    if c + 1 < cols then yield (idx r c, idx r (c + 1))
                    if r + 1 < rows then yield (idx r c, idx (r + 1) c) ]
        fromPairs (rows * cols) edges

    /// True if two physical qubits are directly coupled.
    let areAdjacent (cm: CouplingMap) (a: int) (b: int) : bool =
        cm.Edges.Contains(norm (a, b))

    let private neighbours (cm: CouplingMap) (q: int) : int list =
        cm.Edges
        |> Set.fold (fun acc (a, b) ->
            if a = q then b :: acc
            elif b = q then a :: acc
            else acc) []

    /// Breadth-first shortest path of physical qubits from `src` to `dst`
    /// (inclusive of both endpoints). None if they are in disconnected
    /// components of the coupling graph.
    let shortestPath (cm: CouplingMap) (src: int) (dst: int) : int list option =
        if src = dst then Some [ src ]
        else
            // Mutable BFS frontier — a standard graph kernel; immutable folds
            // would re-allocate the visited set on every expansion.
            let visited = System.Collections.Generic.HashSet<int>()
            let queue = System.Collections.Generic.Queue<int list>()  // paths stored reversed (head = current)
            queue.Enqueue [ src ]
            visited.Add src |> ignore
            let mutable result = None
            while result.IsNone && queue.Count > 0 do
                let pathRev = queue.Dequeue()
                let cur = List.head pathRev
                if cur = dst then result <- Some(List.rev pathRev)
                else
                    for nb in neighbours cm cur do
                        if not (visited.Contains nb) then
                            visited.Add nb |> ignore
                            queue.Enqueue(nb :: pathRev)
            result

    /// Dijkstra shortest path minimising the total of `edgeCost` over traversed
    /// coupling edges (used for noise-aware routing, e.g. cost = 2-qubit error).
    /// Edge costs are clamped to be non-negative.
    let shortestPathWeighted (edgeCost: int * int -> float) (cm: CouplingMap) (src: int) (dst: int) : int list option =
        if src = dst then Some [ src ]
        else
            let dist = System.Collections.Generic.Dictionary<int, float>()
            let prev = System.Collections.Generic.Dictionary<int, int>()
            let pq = System.Collections.Generic.SortedSet<float * int>()  // (distance, node)
            dist.[src] <- 0.0
            pq.Add((0.0, src)) |> ignore
            let mutable settled = false
            while not settled && pq.Count > 0 do
                let (d, u) = pq.Min
                pq.Remove(pq.Min) |> ignore
                if u = dst then settled <- true
                else
                    for v in neighbours cm u do
                        let w = max 0.0 (edgeCost(norm (u, v)))
                        let nd = d + w
                        let isBetter =
                            match dist.TryGetValue v with
                            | true, old -> nd < old
                            | _ -> true
                        if isBetter then
                            match dist.TryGetValue v with
                            | true, old -> pq.Remove((old, v)) |> ignore
                            | _ -> ()
                            dist.[v] <- nd
                            prev.[v] <- u
                            pq.Add((nd, v)) |> ignore
            if not (dist.ContainsKey dst) then None
            else
                let rec build node acc =
                    if node = src then src :: acc
                    else build prev.[node] (node :: acc)
                Some(build dst [])

    /// Remap every qubit index in a gate through `f` (logical -> physical).
    let rec mapQubits (f: int -> int) (gate: Gate) : Gate =
        match gate with
        | X q -> X(f q)
        | Y q -> Y(f q)
        | Z q -> Z(f q)
        | H q -> H(f q)
        | S q -> S(f q)
        | SDG q -> SDG(f q)
        | T q -> T(f q)
        | TDG q -> TDG(f q)
        | P(q, a) -> P(f q, a)
        | RX(q, a) -> RX(f q, a)
        | RY(q, a) -> RY(f q, a)
        | RZ(q, a) -> RZ(f q, a)
        | U3(q, a, b, c) -> U3(f q, a, b, c)
        | CNOT(c, t) -> CNOT(f c, f t)
        | CZ(c, t) -> CZ(f c, f t)
        | SWAP(c, t) -> SWAP(f c, f t)
        | CP(c, t, a) -> CP(f c, f t, a)
        | CRX(c, t, a) -> CRX(f c, f t, a)
        | CRY(c, t, a) -> CRY(f c, f t, a)
        | CRZ(c, t, a) -> CRZ(f c, f t, a)
        | RXX(c, t, a) -> RXX(f c, f t, a)
        | RYY(c, t, a) -> RYY(f c, f t, a)
        | RZZ(c, t, a) -> RZZ(f c, f t, a)
        | CCX(a, b, c) -> CCX(f a, f b, f c)
        | MCZ(cs, t) -> MCZ(List.map f cs, f t)
        | Measure q -> Measure(f q)
        | Reset q -> Reset(f q)
        | Barrier qs -> Barrier(List.map f qs)
        | Conditional(mq, inner) -> Conditional(f mq, mapQubits f inner)

    /// The two logical endpoints of a two-qubit gate, or None for any other gate.
    let private twoQubitEndpoints (gate: Gate) : (int * int) option =
        match gate with
        | CNOT(a, b) | CZ(a, b) | SWAP(a, b)
        | CP(a, b, _) | CRX(a, b, _) | CRY(a, b, _) | CRZ(a, b, _)
        | RXX(a, b, _) | RYY(a, b, _) | RZZ(a, b, _) -> Some(a, b)
        | _ -> None

    // Core routing loop, parameterised by a path finder (hop-count or weighted).
    let private routeCore (findPath: int -> int -> int list option) (cm: CouplingMap) (circuit: Circuit) : Circuit * int[] =
        // Size the routing tables to the physical device, not just the logical circuit: SWAP paths
        // traverse physical qubits up to cm.NumQubits-1, so routing a small circuit onto a larger
        // device must still have a slot for every physical qubit a path passes through. Sizing by
        // circuit.QubitCount alone throws IndexOutOfRange the moment a path visits a physical qubit
        // index >= QubitCount — i.e. the normal "route N logical qubits on an M>N qubit device" case.
        let n = max cm.NumQubits circuit.QubitCount
        // pos.[logical] = physical location; phys.[physical] = logical occupant.
        // Mutable because routing threads an evolving permutation through the
        // gate stream — the classic in-place SABRE-style bookkeeping.
        let pos = Array.init n id
        let phys = Array.init n id

        let applySwap (pa: int) (pb: int) =
            let la, lb = phys.[pa], phys.[pb]
            phys.[pa] <- lb
            phys.[pb] <- la
            pos.[la] <- pb
            pos.[lb] <- pa

        let out = ResizeArray<Gate>()

        // getGates returns execution order (the Circuit stores gates most-recent-first).
        for gate in getGates circuit do
            match twoQubitEndpoints gate with
            | Some(a, b) ->
                let pa, pb = pos.[a], pos.[b]
                if areAdjacent cm pa pb then
                    out.Add(mapQubits (fun lq -> pos.[lq]) gate)
                else
                    match findPath pa pb with
                    | Some path when List.length path >= 2 ->
                        let arr = List.toArray path
                        // Move the qubit at arr.[0] forward until it sits next to arr.[last].
                        for i in 0 .. arr.Length - 3 do
                            out.Add(SWAP(arr.[i], arr.[i + 1]))
                            applySwap arr.[i] arr.[i + 1]
                        out.Add(mapQubits (fun lq -> pos.[lq]) gate)
                    | _ ->
                        // Disconnected coupling graph: best-effort passthrough.
                        out.Add(mapQubits (fun lq -> pos.[lq]) gate)
            | None ->
                out.Add(mapQubits (fun lq -> pos.[lq]) gate)

        // `out` is in execution order; restore the most-recent-first storage invariant.
        ({ circuit with Gates = out |> Seq.rev |> List.ofSeq }, pos)

    /// Route inserting SWAPs along fewest-hop shortest paths (minimises SWAP count).
    /// Returns the routed circuit plus the final logical->physical mapping
    /// (`mapping.[logicalQubit] = physicalQubit`).
    let route (cm: CouplingMap) (circuit: Circuit) : Circuit * int[] =
        routeCore (shortestPath cm) cm circuit

    /// Noise-aware routing: route SWAPs through the lowest-cost path per `edgeCost`
    /// (e.g. the two-qubit gate error of each link), favouring low-error qubits.
    let routeWith (edgeCost: int * int -> float) (cm: CouplingMap) (circuit: Circuit) : Circuit * int[] =
        routeCore (shortestPathWeighted edgeCost cm) cm circuit

    /// Verify that every two-qubit gate in a circuit respects the coupling map.
    /// (Useful for tests and as a post-routing assertion.)
    let respectsCoupling (cm: CouplingMap) (circuit: Circuit) : bool =
        circuit.Gates
        |> List.forall (fun g ->
            match twoQubitEndpoints g with
            | Some(a, b) -> areAdjacent cm a b
            | None -> true)
