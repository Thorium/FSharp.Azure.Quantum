namespace FSharp.Azure.Quantum.Topological

/// <summary>
/// Standard Knot and Link Constructors for Planar Diagrams
/// 
/// This module provides helper functions to construct proper planar diagrams
/// for standard knots and links with full arc connectivity.
///
/// Based on:
/// - Steven Simon (2023). "Topological Quantum", Chapter 2
/// - Rolfsen, D. (1976). "Knots and Links"
/// </summary>
module KnotConstructors =

    open KauffmanBracket
    open KauffmanBracket.Planar
    
    // ========================================
    // Helper Functions for Diagram Construction
    // ========================================
    
    /// <summary>
    /// Create a simple crossing with 4 arcs.
    /// </summary>
    let private createCrossing 
        (id: int) 
        (sign: Crossing) 
        (arcNW: int) 
        (arcNE: int) 
        (arcSW: int) 
        (arcSE: int) : PlanarCrossing =
        {
            Id = id
            Sign = sign
            Connections = 
                Map.empty
                |> Map.add NW arcNW
                |> Map.add NE arcNE
                |> Map.add SW arcSW
                |> Map.add SE arcSE
        }
    
    /// <summary>
    /// Create an arc between two crossing positions.
    /// </summary>
    let private createArc 
        (id: int) 
        (startCrossing: int) 
        (startPos: CrossingPosition)
        (endCrossing: int)
        (endPos: CrossingPosition) : Arc =
        {
            Id = id
            Start = AtCrossing (startCrossing, startPos)
            End = AtCrossing (endCrossing, endPos)
        }
    
    /// <summary>
    /// Create a closed arc (forms a simple loop).
    /// </summary>
    let private createClosedArc (id: int) : Arc =
        {
            Id = id
            Start = FreeEnd 0
            End = FreeEnd 0
        }
    
    // ========================================
    // Standard Knot Constructors
    // ========================================
    
    /// <summary>
    /// Create the unknot (simple loop with no crossings).
    /// This is topologically equivalent to a circle.
    /// </summary>
    let unknot : PlanarDiagram =
        {
            Crossings = Map.empty
            Arcs = Map.ofList [(0, createClosedArc 0)]
        }
    
    /// <summary>
    /// Create a trefoil knot (3₁ in Rolfsen notation).
    /// The simplest non-trivial knot with 3 crossings.
    /// 
    /// Parameters:
    ///   rightHanded - true for right-handed (positive) trefoil, false for left-handed
    /// 
    /// Structure:
    ///   - 3 crossings arranged in a triangular pattern
    ///   - All crossings have the same sign
    ///   - Writhe = +3 (right-handed) or -3 (left-handed)
    /// </summary>
    let trefoil (rightHanded: bool) : PlanarDiagram =
        if rightHanded then
            // Right-handed trefoil: All positive crossings
            // Arc layout (following ONE strand around in a loop):
            // The strand goes: C0 → C1 → C2 → back to C0
            // At each crossing, strand goes over or under
            
            let crossings = 
                Map.ofList [
                    (0, createCrossing 0 Positive 5 3 2 0)  // NW=5, NE=3, SW=2, SE=0
                    (1, createCrossing 1 Positive 3 1 0 4)  // NW=3, NE=1, SW=0, SE=4
                    (2, createCrossing 2 Positive 1 5 4 2)  // NW=1, NE=5, SW=4, SE=2
                ]
            
            let arcs =
                Map.ofList [
                    (0, createArc 0 0 SE 1 SW)  // C0-SE → C1-SW
                    (1, createArc 1 1 NE 2 NW)  // C1-NE → C2-NW
                    (2, createArc 2 2 SE 0 SW)  // C2-SE → C0-SW
                    (3, createArc 3 0 NE 1 NW)  // C0-NE → C1-NW
                    (4, createArc 4 1 SE 2 SW)  // C1-SE → C2-SW
                    (5, createArc 5 2 NE 0 NW)  // C2-NE → C0-NW
                ]
            
            { Crossings = crossings; Arcs = arcs }
        else
            // Left-handed trefoil: All negative crossings
            // Mirror image - arc connectivity is REFLECTED
            let crossings = 
                Map.ofList [
                    (0, createCrossing 0 Negative 3 5 0 2)  // NW=3, NE=5, SW=0, SE=2
                    (1, createCrossing 1 Negative 1 3 4 0)  // NW=1, NE=3, SW=4, SE=0
                    (2, createCrossing 2 Negative 5 1 2 4)  // NW=5, NE=1, SW=2, SE=4
                ]
            
            let arcs =
                Map.ofList [
                    (0, createArc 0 0 SW 1 SE)  // C0-SW → C1-SE
                    (1, createArc 1 1 NW 2 NE)  // C1-NW → C2-NE
                    (2, createArc 2 2 SW 0 SE)  // C2-SW → C0-SE
                    (3, createArc 3 0 NW 1 NE)  // C0-NW → C1-NE
                    (4, createArc 4 1 SW 2 SE)  // C1-SW → C2-SE
                    (5, createArc 5 2 NW 0 NE)  // C2-NW → C0-NE
                ]
            
            { Crossings = crossings; Arcs = arcs }
    
    /// <summary>
    /// Create a figure-eight knot (4₁ in Rolfsen notation).
    /// An achiral knot with 4 crossings.
    /// 
    /// Structure:
    ///   - 4 crossings with alternating signs
    ///   - Writhe = 0 (two positive, two negative)
    ///   - Identical to its mirror image (achiral)
    /// </summary>
    let figureEight : PlanarDiagram =
        // Figure-eight knot (4₁): Correct alternating construction
        // 4 crossings alternating (+,-,+,-), 8 arcs, writhe=0
        
        let crossings = 
            Map.ofList [
                (0, createCrossing 0 Positive 1 3 4 0)   // NW=1, NE=3, SW=4, SE=0
                (1, createCrossing 1 Negative 7 5 4 0)   // NW=7, NE=5, SW=4, SE=0
                (2, createCrossing 2 Positive 3 5 6 2)   // NW=3, NE=5, SW=6, SE=2
                (3, createCrossing 3 Negative 1 6 7 2)   // NW=1, NE=6, SW=7, SE=2
            ]
        
        let arcs =
            Map.ofList [
                (0, createArc 0 0 SE 1 SE)   // C0-SE → C1-SE
                (1, createArc 1 3 NW 0 NW)   // C3-NW → C0-NW
                (2, createArc 2 2 SE 3 SE)   // C2-SE → C3-SE
                (3, createArc 3 0 NE 2 NW)   // C0-NE → C2-NW
                (4, createArc 4 1 SW 0 SW)   // C1-SW → C0-SW
                (5, createArc 5 2 NE 1 NE)   // C2-NE → C1-NE
                (6, createArc 6 3 NE 2 SW)   // C3-NE → C2-SW
                (7, createArc 7 1 NW 3 SW)   // C1-NW → C3-SW
            ]
        
        { Crossings = crossings; Arcs = arcs }
    
    /// <summary>
    /// Create the Hopf link (2²₁ in Rolfsen notation).
    /// The simplest non-trivial link with 2 components and 2 crossings.
    /// 
    /// Structure:
    ///   - 2 crossings, both positive (or both negative for mirror)
    ///   - 2 components (two circles linked together)
    ///   - Writhe = +2 (or -2 for mirror)
    /// 
    /// Parameters:
    ///   positive - true for positive Hopf link, false for negative
    /// </summary>
    let hopfLink (positive: bool) : PlanarDiagram =
        let sign = if positive then Positive else Negative
        
        // Two crossings where two circles link
        // Component 1 (red circle): Arc 0 → C0-NE, Arc 1 ← C0-SW (going through C0)
        //                          Arc 1 → C1-NE, Arc 0 ← C1-SW (going through C1)
        // Component 2 (blue circle): Arc 2 → C0-SE, Arc 3 ← C0-NW (going through C0)
        //                            Arc 3 → C1-SE, Arc 2 ← C1-NW (going through C1)
        
        // Correct topology: Each component passes through both crossings
        // Red: C0(NE→SW) → C1(NE→SW) → back to C0
        // Blue: C0(SE→NW) → C1(SE→NW) → back to C0
        
        // Arc layout for TWO separate circles:
        // Arc 0: C0-SW → C1-NE (red circle, between crossings)
        // Arc 1: C1-SW → C0-NE (red circle, back to first crossing)
        // Arc 2: C0-NW → C1-SE (blue circle, between crossings)  
        // Arc 3: C1-NW → C0-SE (blue circle, back to first crossing)
        
        let crossings = 
            Map.ofList [
                (0, createCrossing 0 sign 2 1 0 3)  // C0: NW=2, NE=1, SW=0, SE=3
                (1, createCrossing 1 sign 3 0 1 2)  // C1: NW=3, NE=0, SW=1, SE=2
            ]
        
        let arcs =
            Map.ofList [
                (0, createArc 0 0 SW 1 NE)  // Red: C0-SW → C1-NE
                (1, createArc 1 1 SW 0 NE)  // Red: C1-SW → C0-NE
                (2, createArc 2 0 NW 1 SE)  // Blue: C0-NW → C1-SE
                (3, createArc 3 1 NW 0 SE)  // Blue: C1-NW → C0-SE
            ]
        
        { Crossings = crossings; Arcs = arcs }
    
    /// <summary>
    /// Create the Borromean rings.
    /// A 3-component link where no two components are linked,
    /// but removing any one component allows the other two to separate.
    /// 
    /// Structure: 6 crossings, 3 components
    /// Constructed as an alternating link (L6a4) with 6 positive crossings.
    /// </summary>
    let borromeanRings : PlanarDiagram =
        // Borromean rings (L6a4): Standard alternating construction
        // 6 crossings, 12 arcs
        // Components: T (Top), R (Right), L (Left)
        // Arcs 0-3: T, Arcs 4-7: R, Arcs 8-11: L
        
        let crossings = 
            Map.ofList [
                (0, createCrossing 0 Positive 4 0 3 5)   // NW=4, NE=0, SW=3, SE=5
                (1, createCrossing 1 Positive 0 4 7 1)   // NW=0, NE=4, SW=7, SE=1
                (2, createCrossing 2 Positive 8 2 1 9)   // NW=8, NE=2, SW=1, SE=9
                (3, createCrossing 3 Positive 2 8 11 3)  // NW=2, NE=8, SW=11, SE=3
                (4, createCrossing 4 Positive 10 6 5 11) // NW=10, NE=6, SW=5, SE=11
                (5, createCrossing 5 Positive 6 10 9 7)  // NW=6, NE=10, SW=9, SE=7
            ]
        
        let arcs =
            Map.ofList [
                // Component T (Top)
                (0, createArc 0 0 NE 1 NW)   // C0-NE → C1-NW
                (1, createArc 1 1 SE 2 SW)   // C1-SE → C2-SW
                (2, createArc 2 2 NE 3 NW)   // C2-NE → C3-NW
                (3, createArc 3 3 SE 0 SW)   // C3-SE → C0-SW
                
                // Component R (Right)
                (4, createArc 4 1 NE 0 NW)   // C1-NE → C0-NW
                (5, createArc 5 0 SE 4 SW)   // C0-SE → C4-SW
                (6, createArc 6 4 NE 5 NW)   // C4-NE → C5-NW
                (7, createArc 7 5 SE 1 SW)   // C5-SE → C1-SW
                
                // Component L (Left)
                (8, createArc 8 3 NE 2 NW)   // C3-NE → C2-NW
                (9, createArc 9 2 SE 5 SW)   // C2-SE → C5-SW
                (10, createArc 10 5 NE 4 NW) // C5-NE → C4-NW
                (11, createArc 11 4 SE 3 SW) // C4-SE → C3-SW
            ]
        
        { Crossings = crossings; Arcs = arcs }
    
    /// <summary>
    /// Create a torus knot T(p,q).
    /// These are knots that can be drawn on the surface of a torus.
    /// Constructed as the closure of the braid (\sigma_1 \sigma_2 ... \sigma_{p-1})^q.
    /// </summary>
    let torusKnot (p: int) (q: int) : PlanarDiagram =
        if p < 1 then invalidArg (nameof p) "Number of strands p must be positive"
        
        // Handle trivial cases
        if p = 1 || q = 0 then unknot
        else
            // Determine sign based on q
            let sign = if q > 0 then Positive else Negative
            let numRepeats = abs q
            
            // Initial state:
            // - nextCrossingId: 0
            // - nextArcId: p (0..p-1 are initial reserved arc IDs)
            // - strands: [0; 1; ...; p-1] (current arc ID at each position)
            // - crossings: empty
            // - arcs: empty
            let initialState = (0, p, List.init p id, Map.empty, Map.empty)

            // Sequence of crossing operations: repeat 'numRepeats' times, applying p-1 crossings
            let operations = 
                seq { for _ in 1 .. numRepeats do for k in 0 .. p - 2 do yield k }

            // Fold over operations to build the knot
            let (finalCrossingId, finalNextArcId, finalStrands, crossings, arcs) =
                operations
                |> Seq.fold (fun (cId, nextArcId, strands: int list, crossings, arcs) k ->
                    // Inputs from current strands
                    let inTop = strands.[k]      // NW input
                    let inBottom = strands.[k+1] // SW input
                    
                    // Create new output arcs
                    let outTop = nextArcId
                    let outBottom = nextArcId + 1
                    
                    // Create crossing
                    // "Bottom" input (SW) crosses OVER to "Top" output (NE) for Positive crossing
                    let crossing = createCrossing cId sign inTop outTop inBottom outBottom
                    
                    // Helper to update an arc's End position
                    let updateArcEnd arcId endPos arcsMap =
                        match Map.tryFind arcId arcsMap with
                        | Some arc -> Map.add arcId { arc with End = endPos } arcsMap
                        | None -> 
                            // Initial arc (0..p-1) not yet in map
                            let arc = { Id = arcId; Start = FreeEnd 0; End = endPos }
                            Map.add arcId arc arcsMap
                    
                    // Update ends of input arcs and add new output arcs
                    let newArcs = 
                        arcs
                        |> updateArcEnd inTop (AtCrossing(cId, NW))
                        |> updateArcEnd inBottom (AtCrossing(cId, SW))
                        |> Map.add outTop { Id = outTop; Start = AtCrossing(cId, NE); End = FreeEnd 0 }
                        |> Map.add outBottom { Id = outBottom; Start = AtCrossing(cId, SE); End = FreeEnd 0 }
                        
                    // Update strand positions: swap k and k+1 with new outputs
                    let newStrands = 
                        strands 
                        |> List.mapi (fun i s -> 
                            if i = k then outTop 
                            elif i = k+1 then outBottom 
                            else s)

                    (cId + 1, nextArcId + 2, newStrands, Map.add cId crossing crossings, newArcs)
                ) initialState
            
            // Closure: Connect final outputs to initial inputs
            // Map: initialArcId -> finalArcId
            let closureMap = 
                List.zip (List.init p id) finalStrands 
                |> Map.ofList
                
            // 1. Update Crossings: Replace references to initial arcs
            let closedCrossings =
                crossings |> Map.map (fun _ c ->
                    let newConns = 
                        c.Connections |> Map.map (fun _ arcId ->
                            if arcId < p then closureMap.[arcId] else arcId
                        )
                    { c with Connections = newConns }
                )
                
            // 2. Update Arcs:
            // - Remove initial arcs (IDs < p)
            // - Update final arcs to close the loop
            let closedArcs =
                arcs
                |> Map.filter (fun id _ -> id >= p) // Remove initial placeholders
                |> Map.map (fun id arc ->
                    // Check if this arc is a final output that needs closure
                    // Find if 'id' is a target in our closure map (reverse lookup)
                    let initIdOpt = 
                        closureMap 
                        |> Map.tryPick (fun init final -> if final = id then Some init else None)
                    
                    match initIdOpt with
                    | Some initId ->
                        // Loop back: retrieve End info from the initial arc
                        match Map.tryFind initId arcs with
                        | Some initArc -> { arc with End = initArc.End }
                        | None -> arc
                    | None -> arc
                )
                
            { Crossings = closedCrossings; Arcs = closedArcs }
    
    // ========================================
    // Validation
    // ========================================
    
    /// <summary>
    /// Validate that a planar diagram is well-formed.
    /// Checks:
    ///   - Each crossing has exactly 4 arcs
    ///   - Arc endpoints match crossing connections
    ///   - No dangling arcs (except for tangles)
    /// </summary>
    let validate (diagram: PlanarDiagram) : Result<unit, string> =
        // Check each crossing has 4 connections
        let checkCrossings =
            diagram.Crossings
            |> Map.toList
            |> List.tryPick (fun (crossingId, crossing) ->
                if crossing.Connections.Count <> 4 then
                    Some (Error $"Crossing {crossingId} does not have exactly 4 connections")
                else
                    // Check all positions are present
                    let positions = [NW; NE; SW; SE]
                    positions
                    |> List.tryPick (fun pos ->
                        if not (crossing.Connections.ContainsKey pos) then
                            Some (Error $"Crossing {crossingId} missing connection at position {pos}")
                        else
                            None))
        
        match checkCrossings with
        | Some err -> err
        | None ->
            // Check arc endpoints reference valid crossings
            let checkArcs =
                diagram.Arcs
                |> Map.toList
                |> List.tryPick (fun (arcId, arc) ->
                    let checkEnd (arcEnd: ArcEnd) =
                        match arcEnd with
                        | AtCrossing (crossingId, pos) ->
                            match Map.tryFind crossingId diagram.Crossings with
                            | None ->
                                Some (Error $"Arc {arcId} references non-existent crossing {crossingId}")
                            | Some crossing ->
                                match Map.tryFind pos crossing.Connections with
                                | None ->
                                    Some (Error $"Arc {arcId} references invalid position {pos} at crossing {crossingId}")
                                | Some connectedArc when connectedArc <> arcId ->
                                    Some (Error $"Arc {arcId} endpoint mismatch at crossing {crossingId} position {pos}")
                                | _ -> None
                        | FreeEnd _ -> None
                    
                    match checkEnd arc.Start with
                    | Some err -> Some err
                    | None -> checkEnd arc.End)
            
            match checkArcs with
            | Some err -> err
            | None -> Ok ()
    
    // ========================================
    // Display Helpers
    // ========================================
    
    /// <summary>
    /// Get a human-readable name for a standard knot.
    /// </summary>
    let knotName (diagram: PlanarDiagram) : string =
        let numCrossings = diagram.Crossings.Count
        let numComponents = countComponents diagram
        let w = writhe diagram
        
        // Try to identify standard knots
        if numCrossings = 0 then
            "Unknot"
        elif numCrossings = 3 && numComponents = 1 && abs w = 3 then
            if w > 0 then "Right-handed trefoil (3₁)" else "Left-handed trefoil (3₁*)"
        elif numCrossings = 4 && numComponents = 1 && w = 0 then
            "Figure-eight knot (4₁)"
        elif numCrossings = 2 && numComponents = 2 then
            if w > 0 then "Positive Hopf link (2²₁)" else "Negative Hopf link"
        elif numCrossings = 6 && numComponents = 3 then
            "Borromean rings (6³₃)"
        else
            $"Knot/Link ({numCrossings} crossings, {numComponents} components, writhe {w})"
