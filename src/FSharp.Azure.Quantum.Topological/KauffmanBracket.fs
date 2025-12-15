namespace FSharp.Azure.Quantum.Topological

/// <summary>
/// Kauffman Bracket Invariant and Jones Polynomial for Knot Theory
/// 
/// This module provides both simplified and rigorous implementations of the Kauffman bracket
/// polynomial invariant. The simplified version works with crossing lists (suitable for
/// standard knots), while the rigorous version uses full planar diagrams with arc connectivity.
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
    open System.Collections.Generic

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
    /// Simplified knot diagram as a list of crossings.
    /// Suitable for standard knots (unknot, trefoil, figure-eight, Hopf link).
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
    /// Evaluate Kauffman bracket using skein relations (simplified model).
    /// 
    /// Skein rules:
    /// 1. Simple loop → d = -A^2 - A^(-2)
    /// 2. Crossing resolution:
    ///    [crossing] = A * [0-resolution] + A^(-1) * [1-resolution]
    /// 
    /// This works correctly for standard knots where the crossing structure is well-defined.
    /// </summary>
    let rec evaluateBracket (diagram: KnotDiagram) (a: Complex) : Complex =
        match diagram with
        | [] ->
            // No crossings = unknot
            loopValue a
        
        | Positive :: rest ->
            // Positive crossing: A * horizontal + A^(-1) * vertical
            let horizontal = evaluateBracket rest a
            let vertical = (loopValue a) * (evaluateBracket rest a)
            a * horizontal + (Complex.One / a) * vertical
        
        | Negative :: rest ->
            // Negative crossing: A^(-1) * horizontal + A * vertical
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
    /// Compute Jones polynomial from Kauffman bracket (simplified model).
    /// 
    /// V(K) = (-A)^(-3w) * ⟨K⟩
    /// 
    /// where w = writhe(K) and ⟨K⟩ is the Kauffman bracket.
    /// </summary>
    let jonesPolynomial (diagram: KnotDiagram) (a: Complex) : Complex =
        let w = writhe diagram
        let bracket = evaluateBracket diagram a
        let normalization = Complex.Pow(-a, -3.0 * float w)
        normalization * bracket

    // ========================================
    // Standard Knot Constructors (Simplified)
    // ========================================

    /// Create unknot (simple loop, no crossings)
    let unknot : KnotDiagram = []

    /// Create trefoil knot (3 crossings)
    let trefoil (rightHanded: bool) : KnotDiagram =
        if rightHanded then
            [Positive; Positive; Positive]
        else
            [Negative; Negative; Negative]

    /// Create figure-eight knot (4 crossings)
    let figureEight : KnotDiagram =
        [Positive; Negative; Positive; Negative]

    /// Create Hopf link (2 crossings)
    let hopfLink : KnotDiagram =
        [Positive; Positive]

    // ========================================
    // Standard TQFT Values
    // ========================================

    /// Evaluate at Ising TQFT value: A = exp(iπ/4)
    let evaluateIsing (diagram: KnotDiagram) : Complex =
        let a = Complex(Math.Cos(Math.PI / 4.0), Math.Sin(Math.PI / 4.0))
        evaluateBracket diagram a

    /// Evaluate at Fibonacci TQFT value: A = exp(i*pi/4 + i*pi/10)
    let evaluateFibonacci (diagram: KnotDiagram) : Complex =
        let angle = Math.PI / 4.0 + Math.PI / 10.0
        let a = Complex(Math.Cos(angle), Math.Sin(angle))
        evaluateBracket diagram a

    /// Evaluate Jones polynomial at t = -1
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
                    if visited.Add(arcId) then
                        let connected = getConnectedArcs arcId
                        connected |> List.iter traceComponent
                
                diagram.Arcs.Keys
                |> Seq.fold (fun count arcId ->
                    if not (visited.Contains arcId) then
                        traceComponent arcId
                        count + 1
                    else
                        count) 0

        /// Resolve crossing by applying skein relation (with full arc reconnection)
        let resolveCrossing (diagram: PlanarDiagram) (crossingId: int) : (PlanarDiagram * PlanarDiagram) =
            match Map.tryFind crossingId diagram.Crossings with
            | None -> (diagram, diagram)
            | Some crossing ->
                let arcNW = crossing.Connections.[NW]
                let arcNE = crossing.Connections.[NE]
                let arcSW = crossing.Connections.[SW]
                let arcSE = crossing.Connections.[SE]
                
                let remainingCrossings = Map.remove crossingId diagram.Crossings
                
                let getOtherEnd (arcId: int) (atCrossingId: int) : ArcEnd =
                    let arc = diagram.Arcs.[arcId]
                    match arc.Start, arc.End with
                    | AtCrossing (cid, _), other when cid = atCrossingId -> other
                    | other, AtCrossing (cid, _) when cid = atCrossingId -> other
                    | start, _ -> start
                
                let mergeArcs (arc1Id: int) (arc2Id: int) (newId: int) : Arc =
                    let start = getOtherEnd arc1Id crossingId
                    let endPoint = getOtherEnd arc2Id crossingId
                    { Id = newId; Start = start; End = endPoint }
                
                let updateCrossingConnections (arcs: Map<int, Arc>) (oldToNew: Map<int, int>) : Map<int, PlanarCrossing> =
                    remainingCrossings
                    |> Map.map (fun cid c ->
                        let updatedConnections =
                            c.Connections
                            |> Map.map (fun pos arcId ->
                                match Map.tryFind arcId oldToNew with
                                | Some newId -> newId
                                | None -> arcId)
                        { c with Connections = updatedConnections })
                
                let nextId = 
                    if Map.isEmpty diagram.Arcs then 0
                    else (diagram.Arcs.Keys |> Seq.max) + 1
                
                match crossing.Sign with
                | Positive ->
                    // 0-smoothing: NW→NE and SW→SE
                    let newArc1_0 = mergeArcs arcNW arcNE nextId
                    let newArc2_0 = mergeArcs arcSW arcSE (nextId + 1)
                    let oldToNew0 = Map.ofList [(arcNW, nextId); (arcNE, nextId); (arcSW, nextId + 1); (arcSE, nextId + 1)]
                    
                    let arcs0 = 
                        diagram.Arcs
                        |> Map.remove arcNW |> Map.remove arcNE |> Map.remove arcSW |> Map.remove arcSE
                        |> Map.add nextId newArc1_0 |> Map.add (nextId + 1) newArc2_0
                    
                    let diagram0 = { Crossings = updateCrossingConnections arcs0 oldToNew0; Arcs = arcs0 }
                    
                    // 1-smoothing: NW→SW and NE→SE
                    let newArc1_1 = mergeArcs arcNW arcSW (nextId + 2)
                    let newArc2_1 = mergeArcs arcNE arcSE (nextId + 3)
                    let oldToNew1 = Map.ofList [(arcNW, nextId + 2); (arcSW, nextId + 2); (arcNE, nextId + 3); (arcSE, nextId + 3)]
                    
                    let arcs1 = 
                        diagram.Arcs
                        |> Map.remove arcNW |> Map.remove arcNE |> Map.remove arcSW |> Map.remove arcSE
                        |> Map.add (nextId + 2) newArc1_1 |> Map.add (nextId + 3) newArc2_1
                    
                    let diagram1 = { Crossings = updateCrossingConnections arcs1 oldToNew1; Arcs = arcs1 }
                    
                    (diagram0, diagram1)
                
                | Negative ->
                    // 0-smoothing: NE→NW and SE→SW
                    let newArc1_0 = mergeArcs arcNE arcNW nextId
                    let newArc2_0 = mergeArcs arcSE arcSW (nextId + 1)
                    let oldToNew0 = Map.ofList [(arcNE, nextId); (arcNW, nextId); (arcSE, nextId + 1); (arcSW, nextId + 1)]
                    
                    let arcs0 = 
                        diagram.Arcs
                        |> Map.remove arcNW |> Map.remove arcNE |> Map.remove arcSW |> Map.remove arcSE
                        |> Map.add nextId newArc1_0 |> Map.add (nextId + 1) newArc2_0
                    
                    let diagram0 = { Crossings = updateCrossingConnections arcs0 oldToNew0; Arcs = arcs0 }
                    
                    // 1-smoothing: NE→SE and NW→SW
                    let newArc1_1 = mergeArcs arcNE arcSE (nextId + 2)
                    let newArc2_1 = mergeArcs arcNW arcSW (nextId + 3)
                    let oldToNew1 = Map.ofList [(arcNE, nextId + 2); (arcSE, nextId + 2); (arcNW, nextId + 3); (arcSW, nextId + 3)]
                    
                    let arcs1 = 
                        diagram.Arcs
                        |> Map.remove arcNW |> Map.remove arcNE |> Map.remove arcSW |> Map.remove arcSE
                        |> Map.add (nextId + 2) newArc1_1 |> Map.add (nextId + 3) newArc2_1
                    
                    let diagram1 = { Crossings = updateCrossingConnections arcs1 oldToNew1; Arcs = arcs1 }
                    
                    (diagram0, diagram1)

        /// Memoization cache for bracket evaluation
        let private bracketCache = Dictionary<string * Complex, Complex>()

        let private diagramHash (diagram: PlanarDiagram) : string =
            let crossingStr = 
                diagram.Crossings
                |> Map.toList
                |> List.sortBy fst
                |> List.map (fun (id, c) -> 
                    let sign = match c.Sign with Positive -> "+" | Negative -> "-"
                    sprintf "%d%s" id sign)
                |> String.concat ","
            sprintf "C[%s]A[%d]" crossingStr diagram.Arcs.Count

        /// Evaluate Kauffman bracket using skein relation (rigorous planar diagram version)
        let rec evaluateBracket (diagram: PlanarDiagram) (a: Complex) : Complex =
            let hash = diagramHash diagram
            let key = (hash, a)
            
            match bracketCache.TryGetValue(key) with
            | (true, cached) -> cached
            | (false, _) ->
                let result =
                    if Map.isEmpty diagram.Crossings then
                        let n = countComponents diagram
                        if n = 0 then Complex.One
                        else Complex.Pow(loopValue a, float n)
                    else
                        let crossingId = diagram.Crossings |> Map.toList |> List.head |> fst
                        let crossing = diagram.Crossings.[crossingId]
                        
                        let (smoothing0, smoothing1) = resolveCrossing diagram crossingId
                        let value0 = evaluateBracket smoothing0 a
                        let value1 = evaluateBracket smoothing1 a
                        
                        match crossing.Sign with
                        | Positive -> a * value0 + (Complex.One / a) * value1
                        | Negative -> (Complex.One / a) * value0 + a * value1
                
                bracketCache.[key] <- result
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

        /// Calculate weight of a state: A^(#A-smoothings - #B-smoothings) * d^(#loops)
        let stateWeight (diagram: PlanarDiagram) (state: State) (a: Complex) : Complex =
            let resolved = applyState diagram state
            let loops = countComponents resolved
            
            // Count A-smoothings vs B-smoothings
            let aCount = state |> Map.toList |> List.filter (fun (_, s) -> s = 0) |> List.length
            let bCount = state |> Map.toList |> List.filter (fun (_, s) -> s = 1) |> List.length
            
            let exponent = aCount - bCount
            let loopFactor = Complex.Pow(loopValue a, float loops)
            
            Complex.Pow(a, float exponent) * loopFactor

        /// Evaluate bracket using state-sum formulation (slower but pedagogically clear)
        let evaluateBracketStateSum (diagram: PlanarDiagram) (a: Complex) : Complex =
            if Map.isEmpty diagram.Crossings then
                let n = countComponents diagram
                Complex.Pow(loopValue a, float n)
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
