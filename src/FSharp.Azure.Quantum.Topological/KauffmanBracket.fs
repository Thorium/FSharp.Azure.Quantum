namespace FSharp.Azure.Quantum.Topological

open System.Collections.Generic

/// <summary>
/// Kauffman Bracket Invariant and Jones Polynomial for Knot Theory
///
/// This module provides two models:
///
/// 1. A SIMPLIFIED crossing-list model (`KnotDiagram = Crossing list`). It carries
///    NO arc-connectivity information, so its `evaluateBracket` necessarily
///    resolves every crossing independently — mathematically it evaluates each
///    crossing as an isolated Reidemeister-I curl (each positive crossing
///    contributes a factor −A⁻³, each negative crossing −A³). This is EXACT only
///    for diagrams that really are disjoint unions of curls; for genuine knots
///    (trefoil, figure-eight, Hopf link, ...) it does NOT compute the knot's
///    Kauffman bracket or Jones polynomial (it returns a monomial). Its writhe
///    and crossing-count bookkeeping remain correct. Use it for writhe/curl
///    algebra only.
///
/// 2. A RIGOROUS planar-diagram model (`Planar` submodule, `PlanarDiagram`) with
///    full arc connectivity, which computes the actual Kauffman bracket / Jones
///    polynomial via skein recursion or the equivalent state sum. Use this
///    (with the constructors in `KnotConstructors`) for real knot invariants;
///    it reproduces textbook values, e.g. ⟨trefoil⟩ = −A⁵ − A⁻³ + A⁻⁷,
///    ⟨Hopf⟩ = −A⁴ − A⁻⁴, and |V(t = −1)| = knot determinant
///    (3 for the trefoil, 5 for the figure-eight, 2 for the Hopf link).
///
/// Based on:
/// - Steven Simon (2023). "Topological Quantum", Chapter 2 (Kauffman Bracket), Chapter 23 (State-Sum TQFTs)
/// - Kauffman, L. H. (1987). "State models and the Jones polynomial"
/// - Jones, V. (1985). "A polynomial invariant for knots via von Neumann algebras"
///
/// The Kauffman bracket is directly related to quantum amplitudes in topological quantum field theories.
/// </summary>
module KauffmanBracket =

    open System
    open System.Numerics

    // ========================================
    // Simplified Crossing List Model
    // ========================================

    /// <summary>
    /// Crossing type in a knot diagram (blackboard framing convention).
    /// </summary>
    type Crossing =
        /// Positive crossing (+1 contribution to writhe)
        | Positive
        /// Negative crossing (-1 contribution to writhe)  
        | Negative

    /// <summary>
    /// Simplified knot diagram as a bare list of crossing signs.
    ///
    /// ⚠ This type records only how many positive/negative crossings a diagram
    /// has — it does NOT record which strands the crossings connect. It suffices
    /// for writhe computation, but bracket evaluation on it treats every
    /// crossing as an isolated curl (see `evaluateBracket`). For actual knot
    /// invariants use `PlanarDiagram` with the `Planar` module and the
    /// constructors in `KnotConstructors`.
    /// </summary>
    type KnotDiagram = Crossing list

    // ========================================
    // Rigorous Planar Diagram Model
    // ========================================

    /// Position at a crossing (NW, NE, SW, SE in standard orientation)
    [<Struct>]
    type CrossingPosition = NW | NE | SW | SE

    /// Arc endpoint - where an arc connects to
    [<Struct>]
    type ArcEnd =
        | AtCrossing of crossingId: int * position: CrossingPosition
        | FreeEnd of componentId: int

    /// Directed arc in a planar diagram
    [<Struct>]
    type Arc = {
        Id: int
        Start: ArcEnd
        End: ArcEnd
    }

    /// Crossing in a planar diagram with full connectivity
    type PlanarCrossing = {
        Id: int
        Sign: Crossing
        Connections: Map<CrossingPosition, int>  // position -> arc ID
    }

    /// Complete planar diagram with explicit arc-crossing connectivity
    type PlanarDiagram = {
        Crossings: Map<int, PlanarCrossing>
        Arcs: Map<int, Arc>
    }

    // ========================================
    // Core Kauffman Bracket Functions
    // ========================================

    /// <summary>
    /// The loop value d = -A^2 - A^(-2).
    /// Each simple loop contributes a factor of d.
    /// </summary>
    let loopValue (a: Complex) : Complex =
        -(a * a) - (Complex.One / (a * a))

    // ========================================
    // Simplified Implementation (Crossing List)
    // ========================================

    /// <summary>
    /// Evaluate the Kauffman bracket of a crossing list as a DISJOINT UNION OF
    /// CURLS (per-crossing approximation).
    ///
    /// ⚠ LIMITATION — this is NOT a knot invariant computation. A `KnotDiagram`
    /// has no arc connectivity, so each crossing is resolved independently as an
    /// isolated Reidemeister-I curl:
    ///   positive crossing → A + A⁻¹·d = −A⁻³
    ///   negative crossing → A⁻¹ + A·d = −A³
    /// giving ⟨diagram⟩ = (−A⁻³)^(#positive) · (−A³)^(#negative) — a monomial.
    /// This is exact ONLY when the diagram really is n disjoint curls. For real
    /// knots (whose crossings share strands, so smoothings change the global
    /// loop count non-locally) the result is wrong — e.g. the true trefoil
    /// bracket −A⁵ − A⁻³ + A⁻⁷ is a trinomial. Use
    /// `Planar.evaluateBracket` with `KnotConstructors` diagrams for genuine
    /// invariants.
    /// </summary>
    let rec evaluateBracket (diagram: KnotDiagram) (a: Complex) : Complex =
        match diagram with
        | [] ->
            // No crossings = unknot = single loop
            // The Kauffman bracket of the unknot is 1 (by normalization convention).
            // Each ADDITIONAL loop contributes a factor of d = -A² - A⁻².
            Complex.One

        | Positive :: rest ->
            // Positive curl: A * [0-smoothing] + A⁻¹ * [1-smoothing], where the
            // 1-smoothing of an isolated curl detaches one extra loop (factor d).
            // Net factor: A + A⁻¹·d = −A⁻³ (Reidemeister-I twist factor).
            let horizontal = evaluateBracket rest a
            let vertical = (loopValue a) * (evaluateBracket rest a)
            a * horizontal + (Complex.One / a) * vertical

        | Negative :: rest ->
            // Negative curl: A⁻¹ * [0-smoothing] + A * [1-smoothing] = −A³ overall
            let horizontal = evaluateBracket rest a
            let vertical = (loopValue a) * (evaluateBracket rest a)
            (Complex.One / a) * horizontal + a * vertical

    /// <summary>
    /// Calculate writhe (signed sum of crossings).
    /// </summary>
    let writhe (diagram: KnotDiagram) : int =
        diagram
        |> List.sumBy (function
            | Positive -> 1
            | Negative -> -1)

    /// <summary>
    /// Writhe-normalize the simplified (per-crossing curl) bracket:
    ///
    /// V(K) = (-A)^(-3w) * ⟨K⟩
    ///
    /// where w = writhe(K) and ⟨K⟩ is `evaluateBracket` above.
    ///
    /// ⚠ LIMITATION — since `evaluateBracket` treats every crossing as an
    /// isolated curl, the result is always the monomial
    ///   (-A)^(-3w) · (−A⁻³)^(#positive) · (−A³)^(#negative) = A^(−6w),
    /// It is NOT the Jones polynomial of the trefoil/figure-eight/Hopf link —
    /// use `Planar.jonesPolynomial` with `KnotConstructors` diagrams for those.
    /// </summary>
    let jonesPolynomial (diagram: KnotDiagram) (a: Complex) : Complex =
        let w = writhe diagram
        let bracket = evaluateBracket diagram a
        let normalization = Complex.Pow(-a, -3.0 * float w)
        normalization * bracket

    // ========================================
    // Standard Knot Constructors (Simplified — crossing signs ONLY)
    // ========================================
    //
    // ⚠ These constructors record only the CROSSING SIGNS of the standard
    // diagrams (correct crossing counts and writhes). They carry no strand
    // connectivity, so evaluating the simplified bracket/Jones on them does NOT
    // give the corresponding knot invariants (see `evaluateBracket`). For real
    // invariants use the planar constructors: `KnotConstructors.trefoil`,
    // `KnotConstructors.figureEight`, `KnotConstructors.hopfLink`.

    /// Create unknot (simple loop, no crossings)
    let unknot : KnotDiagram = []

    /// Crossing-sign list of the standard trefoil diagram (3 crossings, writhe ±3).
    /// ⚠ Signs only — for the trefoil's actual invariants use `KnotConstructors.trefoil`.
    let trefoil (rightHanded: bool) : KnotDiagram =
        if rightHanded then
            [Positive; Positive; Positive]
        else
            [Negative; Negative; Negative]

    /// Crossing-sign list of the standard figure-eight diagram (4 crossings, writhe 0).
    /// ⚠ Signs only — for the figure-eight's actual invariants use `KnotConstructors.figureEight`.
    let figureEight : KnotDiagram =
        [Positive; Negative; Positive; Negative]

    /// Crossing-sign list of the positive Hopf link diagram (2 crossings, writhe +2).
    /// ⚠ Signs only — for the Hopf link's actual invariants use `KnotConstructors.hopfLink`.
    let hopfLink : KnotDiagram =
        [Positive; Positive]

    // ========================================
    // Standard TQFT Values (simplified model — same curl-only caveat as above)
    // ========================================

    /// Evaluate the simplified (per-crossing curl) bracket at the Ising TQFT value A = exp(iπ/4)
    let evaluateIsing (diagram: KnotDiagram) : Complex =
        let a = Complex(Math.Cos(Math.PI / 4.0), Math.Sin(Math.PI / 4.0))
        evaluateBracket diagram a

    /// Evaluate the simplified (per-crossing curl) bracket at the Fibonacci TQFT value A = exp(i*pi/4 + i*pi/10)
    let evaluateFibonacci (diagram: KnotDiagram) : Complex =
        let angle = Math.PI / 4.0 + Math.PI / 10.0
        let a = Complex(Math.Cos(angle), Math.Sin(angle))
        evaluateBracket diagram a

    /// Evaluate the simplified (per-crossing curl) Jones value at t = -1 (A = exp(iπ/4))
    let evaluateJonesAtMinusOne (diagram: KnotDiagram) : Complex =
        let a = Complex(Math.Cos(Math.PI / 4.0), Math.Sin(Math.PI / 4.0))
        jonesPolynomial diagram a

    // ========================================
    // Rigorous Planar Diagram Implementation
    // ========================================

    module Planar =
        
        /// Create empty planar diagram (unknot)
        let emptyDiagram : PlanarDiagram =
            {
                Crossings = Map.empty
                Arcs = Map.empty
            }

        /// Calculate writhe of planar diagram
        let writhe (diagram: PlanarDiagram) : int =
            diagram.Crossings
            |> Map.toList
            |> List.sumBy (fun (_, crossing) ->
                match crossing.Sign with
                | Positive -> 1
                | Negative -> -1)

        /// Count connected components by following continuous strands through crossings
        let countComponents (diagram: PlanarDiagram) : int =
            if Map.isEmpty diagram.Arcs then
                1  // Empty diagram = unknot = 1 component
            else
                let visited = HashSet<int>()
                
                // Get the arc that continues the strand at a crossing
                // At a crossing, arcs pair up as continuous strands:
                // Positive crossing: (NW,SE) over, (NE,SW) under
                // Negative crossing: (NE,SW) over, (NW,SE) under
                let getStrandContinuation (arcId: int) (crossingId: int) (position: CrossingPosition) : int option =
                    match Map.tryFind crossingId diagram.Crossings with
                    | None -> None
                    | Some crossing ->
                        // Find which arc continues the strand
                        match crossing.Sign with
                        | Positive ->
                            match position with
                            | NW -> Map.tryFind SE crossing.Connections  // Over-strand: NW ↔ SE
                            | SE -> Map.tryFind NW crossing.Connections
                            | NE -> Map.tryFind SW crossing.Connections  // Under-strand: NE ↔ SW
                            | SW -> Map.tryFind NE crossing.Connections
                        | Negative ->
                            match position with
                            | NE -> Map.tryFind SW crossing.Connections  // Over-strand: NE ↔ SW
                            | SW -> Map.tryFind NE crossing.Connections
                            | NW -> Map.tryFind SE crossing.Connections  // Under-strand: NW ↔ SE
                            | SE -> Map.tryFind NW crossing.Connections
                
                // Get arcs that continue the same strand at each endpoint
                let getConnectedArcs (arcId: int) : int list =
                    match Map.tryFind arcId diagram.Arcs with
                    | None -> []
                    | Some arc ->
                        let arcAtStart =
                            match arc.Start with
                            | AtCrossing (crossingId, pos) ->
                                match getStrandContinuation arcId crossingId pos with
                                | Some aid when aid <> arcId -> [aid]
                                | _ -> []
                            | FreeEnd _ -> []
                        
                        let arcAtEnd =
                            match arc.End with
                            | AtCrossing (crossingId, pos) ->
                                match getStrandContinuation arcId crossingId pos with
                                | Some aid when aid <> arcId -> [aid]
                                | _ -> []
                            | FreeEnd _ -> []
                        
                        arcAtStart @ arcAtEnd
                
                let rec traceComponent (arcId: int) =
                    if visited.Add arcId then
                        let connected = getConnectedArcs arcId
                        connected |> List.iter traceComponent
                
                diagram.Arcs.Keys
                |> Seq.fold (fun count arcId ->
                    if not (visited.Contains arcId) then
                        traceComponent arcId
                        count + 1
                    else
                        count) 0

        /// Resolve a crossing by applying the skein relation (with full arc reconnection).
        ///
        /// Returns (0-smoothing, 1-smoothing), where geometrically the
        /// 0-smoothing joins positions (NW,NE) and (SW,SE) and the 1-smoothing
        /// joins (NW,SW) and (NE,SE). The A / A⁻¹ weights (which depend on the
        /// crossing sign) are applied by the evaluators, not here.
        ///
        /// The reconnection walks complete strands through the removed crossing, so
        /// degenerate connectivity is handled correctly:
        ///   - an arc that directly connects two joined positions closes into a
        ///     standalone loop, recorded as a FreeEnd–FreeEnd arc (which
        ///     countComponents counts as one component, like the unknot);
        ///   - an arc whose far end re-enters the SAME crossing at another position
        ///     is followed through the other smoothing junction (chained strands).
        ///
        /// (A previous implementation merged arcs pairwise via a position-blind
        /// "other end" lookup: merged arcs kept endpoints referencing the REMOVED
        /// crossing, so countComponents found no strand continuation and counted
        /// every merged arc as its own loop. That inflated loop counts and produced
        /// wrong invariants — e.g. the Hopf link bracket came out as (A²+A⁻²+2)·d
        /// instead of −A⁴−A⁻⁴, and the trefoil bracket as a wrong trinomial.)
        let resolveCrossing (diagram: PlanarDiagram) (crossingId: int) : (PlanarDiagram * PlanarDiagram) =
            match Map.tryFind crossingId diagram.Crossings with
            | None -> (diagram, diagram)
            | Some crossing ->

            // Build one smoothed diagram for the given junction pairs.
            let smooth (pairs: (CrossingPosition * CrossingPosition) list) : PlanarDiagram =
                let remainingCrossings = Map.remove crossingId diagram.Crossings

                // The junction partner of a position under this smoothing.
                let partner (pos: CrossingPosition) : CrossingPosition =
                    pairs
                    |> List.pick (fun (a, b) ->
                        if a = pos then Some b
                        elif b = pos then Some a
                        else None)

                // The arc occupying a given position, and the endpoint reached by
                // traversing that arc AWAY from this position (position-aware:
                // an arc may have both endpoints on this crossing).
                let farEndOf (pos: CrossingPosition) : int * ArcEnd =
                    let arcId = crossing.Connections.[pos]
                    let arc = diagram.Arcs.[arcId]
                    let isHere (e: ArcEnd) =
                        match e with
                        | AtCrossing (cid, p) -> cid = crossingId && p = pos
                        | FreeEnd _ -> false
                    if isHere arc.Start then (arcId, arc.End)
                    elif isHere arc.End then (arcId, arc.Start)
                    else
                        // Defensive fallback for diagrams whose endpoint metadata is
                        // inconsistent with the crossing's connection map: prefer
                        // the end that is not at this crossing.
                        match arc.Start with
                        | AtCrossing (cid, _) when cid = crossingId -> (arcId, arc.End)
                        | _ -> (arcId, arc.Start)

                let mutable visited : Set<CrossingPosition> = Set.empty
                let mutable nextId =
                    if Map.isEmpty diagram.Arcs then 0
                    else (diagram.Arcs.Keys |> Seq.max) + 1
                let mutable newArcs : Arc list = []
                let mutable oldToNew : Map<int, int> = Map.empty
                let mutable consumedArcs : Set<int> = Set.empty

                // Walk away from `pos` through its arc, hopping across further
                // junctions of THIS crossing, until reaching an endpoint away from
                // this crossing (Some ext) or closing onto an already-visited
                // junction (None = the strand is a closed loop).
                let rec walkFrom (pos: CrossingPosition) (acc: int list) : ArcEnd option * int list =
                    visited <- Set.add pos visited
                    let (arcId, far) = farEndOf pos
                    let acc = arcId :: acc
                    match far with
                    | AtCrossing (cid, p) when cid = crossingId ->
                        visited <- Set.add p visited
                        let p2 = partner p
                        if visited.Contains p2 then (None, acc)   // strand closed into a loop
                        else walkFrom p2 acc
                    | ext -> (Some ext, acc)

                let registerStrand (endpoints: (ArcEnd * ArcEnd) option) (arcIds: int list) =
                    let newArc =
                        match endpoints with
                        | Some (e1, e2) -> { Id = nextId; Start = e1; End = e2 }
                        | None -> { Id = nextId; Start = FreeEnd 0; End = FreeEnd 0 }  // standalone loop
                    newArcs <- newArc :: newArcs
                    for a in arcIds do
                        oldToNew <- Map.add a nextId oldToNew
                        consumedArcs <- Set.add a consumedArcs
                    nextId <- nextId + 1

                for (u, v) in pairs do
                    if not (visited.Contains u || visited.Contains v) then
                        match walkFrom u [] with
                        | (None, arcsU) ->
                            // Closed loop through this junction (v was consumed by the walk)
                            registerStrand None arcsU
                        | (Some e1, arcsU) ->
                            let (endV, arcsV) = walkFrom v []
                            match endV with
                            | Some e2 -> registerStrand (Some (e1, e2)) (arcsU @ arcsV)
                            | None ->
                                // Unreachable for well-formed diagrams (the v-side
                                // can only close onto positions already consumed,
                                // in which case the u-side walk would have closed
                                // first); treat defensively as a loop.
                                registerStrand None (arcsU @ arcsV)

                let arcsAfter =
                    let survivors =
                        diagram.Arcs |> Map.filter (fun id _ -> not (consumedArcs.Contains id))
                    newArcs |> List.fold (fun acc (a: Arc) -> Map.add a.Id a acc) survivors

                let crossingsAfter =
                    remainingCrossings
                    |> Map.map (fun _ c ->
                        { c with
                            Connections =
                                c.Connections
                                |> Map.map (fun _ arcId ->
                                    match Map.tryFind arcId oldToNew with
                                    | Some newId -> newId
                                    | None -> arcId) })

                { Crossings = crossingsAfter; Arcs = arcsAfter }

            let smoothing0 = smooth [ (NW, NE); (SW, SE) ]
            let smoothing1 = smooth [ (NW, SW); (NE, SE) ]
            (smoothing0, smoothing1)

        /// Memoization cache for bracket evaluation (thread-safe)
        let private bracketCache = System.Collections.Concurrent.ConcurrentDictionary<string * Complex, Complex>()

        /// Compute a hash key for memoization that includes full connectivity.
        /// Must distinguish structurally different diagrams with same crossing signs.
        let private diagramHash (diagram: PlanarDiagram) : string =
            let crossingStr = 
                diagram.Crossings
                |> Map.toList
                |> List.sortBy fst
                |> List.map (fun (id, c) -> 
                    let sign = match c.Sign with Positive -> "+" | Negative -> "-"
                    // Include arc connectivity, not just ID and sign
                    let conns = 
                        c.Connections
                        |> Map.toList
                        |> List.sortBy (fun (pos, _) -> $"%A{pos}")
                        |> List.map (fun (pos, arcId) -> $"%A{pos}:%d{arcId}")
                        |> String.concat ";"
                    $"%d{id}%s{sign}(%s{conns})")
                |> String.concat ","
            let arcStr =
                diagram.Arcs
                |> Map.toList
                |> List.sortBy fst
                |> List.map (fun (id, arc) -> $"%d{id}:%A{arc.Start}-%A{arc.End}")
                |> String.concat ","
            $"C[%s{crossingStr}]A[%s{arcStr}]"

        /// Evaluate Kauffman bracket using skein relation (rigorous planar diagram version)
        let rec evaluateBracket (diagram: PlanarDiagram) (a: Complex) : Complex =
            let hash = diagramHash diagram
            let key = (hash, a)
            
            match bracketCache.TryGetValue key with
            | (true, cached) -> cached
            | (false, _) ->
                let result =
                    if Map.isEmpty diagram.Crossings then
                        // n loops → d^(n-1) where d = -A² - A⁻²
                        // Convention: single unknot loop = 1, each ADDITIONAL loop multiplies by d
                        let n = countComponents diagram
                        if n <= 1 then Complex.One
                        else Complex.Pow(loopValue a, float (n - 1))
                    else
                        let crossingId = diagram.Crossings |> Map.toList |> List.head |> fst
                        let crossing = diagram.Crossings.[crossingId]
                        
                        let (smoothing0, smoothing1) = resolveCrossing diagram crossingId
                        let value0 = evaluateBracket smoothing0 a
                        let value1 = evaluateBracket smoothing1 a
                        
                        match crossing.Sign with
                        | Positive -> a * value0 + (Complex.One / a) * value1
                        | Negative -> (Complex.One / a) * value0 + a * value1
                
                bracketCache.TryAdd(key, result) |> ignore
                result

        /// Compute Jones polynomial from planar diagram
        let jonesPolynomial (diagram: PlanarDiagram) (a: Complex) : Complex =
            let w = writhe diagram
            let bracket = evaluateBracket diagram a
            let normalization = Complex.Pow(-a, -3.0 * float w)
            normalization * bracket

        // ========================================
        // State-Sum Formulation (Turaev-Viro Style)
        // ========================================

        /// State: assignment of smoothing choice (0 or 1) to each crossing
        type State = Map<int, int>

        /// Generate all possible states for a diagram (2^n states for n crossings)
        let generateAllStates (diagram: PlanarDiagram) : State list =
            let crossingIds = diagram.Crossings |> Map.toList |> List.map fst
            let n = crossingIds.Length
            
            if n = 0 then [Map.empty]
            else
                [0 .. (1 <<< n) - 1]
                |> List.map (fun stateNum ->
                    crossingIds
                    |> List.mapi (fun i cid ->
                        let bit = (stateNum >>> i) &&& 1
                        (cid, bit))
                    |> Map.ofList)

        /// Apply a state to a diagram (resolve all crossings according to state)
        let applyState (diagram: PlanarDiagram) (state: State) : PlanarDiagram =
            state
            |> Map.fold (fun d cid smoothing ->
                let (d0, d1) = resolveCrossing d cid
                if smoothing = 0 then d0 else d1) diagram

        /// Calculate weight of a state: (product of per-crossing A/A⁻¹ factors) * d^(#loops - 1)
        ///
        /// The smoothing factor is SIGN-AWARE, matching the recursive skein
        /// evaluator: a 0-smoothing contributes A at a positive crossing but A⁻¹
        /// at a negative crossing (and vice versa for the 1-smoothing).
        /// (A previous version used A^(#0-smoothings − #1-smoothings) regardless
        /// of crossing sign — correct only for all-positive or all-negative
        /// diagrams; it disagreed with the recursive evaluator on mixed-sign
        /// diagrams such as the figure-eight knot.)
        let stateWeight (diagram: PlanarDiagram) (state: State) (a: Complex) : Complex =
            let resolved = applyState diagram state
            let loops = countComponents resolved

            // Per-crossing smoothing factors, matching evaluateBracket:
            //   Positive: 0-smoothing → A,   1-smoothing → A⁻¹
            //   Negative: 0-smoothing → A⁻¹, 1-smoothing → A
            let smoothingFactor =
                state
                |> Map.fold (fun acc cid smoothing ->
                    let factor =
                        match Map.tryFind cid diagram.Crossings with
                        | Some crossing ->
                            match crossing.Sign, smoothing with
                            | Positive, 0 | Negative, 1 -> a
                            | _ -> Complex.One / a
                        | None -> Complex.One  // state entry for a non-existent crossing
                    acc * factor) Complex.One

            // d^(n-1): single loop = 1, each additional loop contributes d
            let loopFactor =
                if loops <= 1 then Complex.One
                else Complex.Pow(loopValue a, float (loops - 1))

            smoothingFactor * loopFactor

        /// Evaluate bracket using state-sum formulation (slower but pedagogically clear)
        let evaluateBracketStateSum (diagram: PlanarDiagram) (a: Complex) : Complex =
            if Map.isEmpty diagram.Crossings then
                let n = countComponents diagram
                if n <= 1 then Complex.One
                else Complex.Pow(loopValue a, float (n - 1))
            else
                generateAllStates diagram
                |> List.map (fun state -> stateWeight diagram state a)
                |> List.fold (+) Complex.Zero

        // ========================================
        // Special A Values for TQFT Models
        // ========================================

        /// Standard A value for generic quantum invariant (q = e^(iπ/4))
        let standardA : Complex = 
            Complex(Math.Cos(Math.PI / 4.0), Math.Sin(Math.PI / 4.0))

        /// Ising anyon model A value: A^4 = -1, so A = e^(iπ/4)
        let isingA : Complex = standardA

        /// Fibonacci anyon model A value: d = φ (golden ratio), A = e^(iπ/5)
        let fibonacciA : Complex =
            Complex(Math.Cos(Math.PI / 5.0), Math.Sin(Math.PI / 5.0))
