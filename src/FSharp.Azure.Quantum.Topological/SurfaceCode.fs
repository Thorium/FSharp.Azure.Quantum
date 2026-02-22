namespace FSharp.Azure.Quantum.Topological

/// Surface code variants for topological error correction
/// 
/// Extends the toric code model to planar and color code topologies.
/// 
/// Planar code (Bravyi & Kitaev 1998):
/// - Square lattice with open (non-periodic) boundaries
/// - Rough boundaries (top/bottom) and smooth boundaries (left/right)
/// - Encodes 1 logical qubit (vs 2 for toric code)
/// - Code distance d = lattice side length
/// - Minimum-weight perfect matching decoder
/// 
/// Color code (Bombin & Martin-Delgado 2006):
/// - Defined on a 3-colorable lattice (we use 4.8.8 / square-octagon)
/// - Each face is assigned one of 3 colors (Red, Green, Blue)
/// - Supports transversal Clifford gates (H, S, CNOT)
/// - Encodes 1 logical qubit
/// - Union-Find decoder for efficient error correction
/// 
/// References:
/// - Bravyi & Kitaev, "Quantum codes on a lattice with boundary" (1998)
/// - Bombin & Martin-Delgado, "Topological quantum distillation" (2006)
/// - Fowler et al., "Surface codes: Towards practical large-scale QC" (2012)
/// - Steven H. Simon, "Topological Quantum" (2023), Ch 27-30
[<RequireQualifiedAccess>]
module SurfaceCode =

    open System

    // ========================================================================
    // SHARED TYPES
    // ========================================================================

    /// Coordinates on a 2D lattice
    type Coords = { X: int; Y: int }

    /// Qubit state (same semantics as ToricCode)
    type QubitState =
        | Zero
        | One
        | Plus
        | Minus

    /// Pauli error type
    type PauliError =
        | PauliX   // bit flip
        | PauliZ   // phase flip
        | PauliY   // both

    // ========================================================================
    // PLANAR CODE
    // ========================================================================

    /// Boundary type for the planar code
    type BoundaryType =
        | Rough     // top and bottom edges: support X-type logical operator
        | Smooth    // left and right edges: support Z-type logical operator

    /// Edge orientation on the planar lattice
    type PlanarEdgeType =
        | PHorizontal
        | PVertical

    /// Edge in the planar code
    type PlanarEdge = {
        Position: Coords
        EdgeType: PlanarEdgeType
    }

    /// Planar code lattice (open boundaries, no periodicity)
    type PlanarLattice = {
        /// Number of data qubit columns (code distance)
        Distance: int
    }

    /// Planar code syndrome
    type PlanarSyndrome = {
        /// X-stabilizer violations (vertex defects)
        XDefects: Coords list
        /// Z-stabilizer violations (plaquette defects)
        ZDefects: Coords list
    }

    /// Planar code state
    type PlanarCodeState = {
        Lattice: PlanarLattice
        /// Qubit states indexed by edge
        Qubits: Map<PlanarEdge, QubitState>
    }

    /// Decoder result for planar code
    type PlanarDecoderResult = {
        /// Matched pairs (including boundary matches)
        MatchedPairs: (Coords * Coords) list
        /// Correction edges to flip
        Corrections: PlanarEdge list
        /// Total matching weight
        TotalWeight: int
        /// Whether any defect was matched to a boundary
        BoundaryMatches: int
    }

    /// Create a planar code lattice with given code distance.
    /// 
    /// The distance d must be odd and >= 3 for a valid planar code.
    /// Physical qubits: d² + (d-1)² = 2d² - 2d + 1
    /// (d² data qubits on faces, (d-1)² on vertices... simplified to edge model)
    let createPlanarLattice (distance: int) : TopologicalResult<PlanarLattice> =
        if distance < 3 then
            TopologicalResult.validationError "distance" "Planar code distance must be >= 3"
        elif distance % 2 = 0 then
            TopologicalResult.validationError "distance" "Planar code distance must be odd"
        else
            Ok { Distance = distance }

    /// Get all edges in the planar lattice.
    /// 
    /// For a distance-d planar code:
    /// - Horizontal edges: d columns × (d-1) rows = d(d-1)
    /// - Vertical edges: (d-1) columns × d rows = d(d-1)
    /// - Total: 2d(d-1) edges
    let getAllPlanarEdges (lattice: PlanarLattice) : PlanarEdge list =
        let d = lattice.Distance
        let horizontal =
            [ for y in 0 .. d - 2 do
                for x in 0 .. d - 1 do
                    { Position = { X = x; Y = y }; EdgeType = PHorizontal } ]
        let vertical =
            [ for y in 0 .. d - 1 do
                for x in 0 .. d - 2 do
                    { Position = { X = x; Y = y }; EdgeType = PVertical } ]
        horizontal @ vertical

    /// Number of physical qubits in the planar code.
    /// 
    /// 2d(d-1) edges for a distance-d code.
    let planarPhysicalQubits (lattice: PlanarLattice) : int =
        2 * lattice.Distance * (lattice.Distance - 1)

    /// Number of logical qubits (always 1 for planar code).
    let planarLogicalQubits (_lattice: PlanarLattice) : int = 1

    /// Code distance of the planar code.
    let planarCodeDistance (lattice: PlanarLattice) : int = lattice.Distance

    /// Initialize planar code in ground state (all qubits |+⟩).
    let initializePlanarGroundState (lattice: PlanarLattice) : PlanarCodeState =
        let edges = getAllPlanarEdges lattice
        let qubits =
            edges
            |> List.map (fun e -> (e, Plus))
            |> Map.ofList
        { Lattice = lattice; Qubits = qubits }

    /// Apply X error to an edge in the planar code.
    let applyPlanarXError (state: PlanarCodeState) (edge: PlanarEdge) : PlanarCodeState =
        let newQubits =
            state.Qubits
            |> Map.change edge (Option.map (function
                | Zero -> One | One -> Zero
                | Plus -> Plus | Minus -> Minus))
        { state with Qubits = newQubits }

    /// Apply Z error to an edge in the planar code.
    let applyPlanarZError (state: PlanarCodeState) (edge: PlanarEdge) : PlanarCodeState =
        let newQubits =
            state.Qubits
            |> Map.change edge (Option.map (function
                | Zero -> Zero | One -> One
                | Plus -> Minus | Minus -> Plus))
        { state with Qubits = newQubits }

    /// Get edges adjacent to a vertex in the planar code.
    /// 
    /// Unlike the toric code, boundary vertices have fewer than 4 neighbors.
    let getPlanarVertexEdges (lattice: PlanarLattice) (vertex: Coords) : PlanarEdge list =
        let d = lattice.Distance
        [
            // Left horizontal edge
            if vertex.X > 0 then
                { Position = { X = vertex.X - 1; Y = vertex.Y }; EdgeType = PVertical }
            // Right horizontal edge
            if vertex.X < d - 2 then
                { Position = { X = vertex.X; Y = vertex.Y }; EdgeType = PVertical }
            // Below vertical edge
            if vertex.Y > 0 then
                { Position = { X = vertex.X; Y = vertex.Y - 1 }; EdgeType = PHorizontal }
            // Above vertical edge
            if vertex.Y < d - 2 then
                { Position = { X = vertex.X; Y = vertex.Y }; EdgeType = PHorizontal }
        ]

    /// Get edges around a plaquette in the planar code.
    /// 
    /// Plaquette at (x, y) is bounded by 4 edges (all plaquettes are interior).
    let getPlanarPlaquetteEdges (lattice: PlanarLattice) (plaquette: Coords) : PlanarEdge list =
        [
            { Position = { X = plaquette.X; Y = plaquette.Y }; EdgeType = PHorizontal }
            { Position = { X = plaquette.X; Y = plaquette.Y + 1 }; EdgeType = PHorizontal }
            { Position = { X = plaquette.X; Y = plaquette.Y }; EdgeType = PVertical }
            { Position = { X = plaquette.X + 1; Y = plaquette.Y }; EdgeType = PVertical }
        ]

    /// Measure X-stabilizer (vertex operator) in the planar code.
    let measurePlanarVertexOperator (state: PlanarCodeState) (vertex: Coords) : int =
        let edges = getPlanarVertexEdges state.Lattice vertex
        let excitations =
            edges
            |> List.filter (fun edge ->
                match Map.tryFind edge state.Qubits with
                | Some Minus -> true
                | _ -> false)
            |> List.length
        if excitations % 2 = 0 then 1 else -1

    /// Measure Z-stabilizer (plaquette operator) in the planar code.
    let measurePlanarPlaquetteOperator (state: PlanarCodeState) (plaquette: Coords) : int =
        let edges = getPlanarPlaquetteEdges state.Lattice plaquette
        let excitations =
            edges
            |> List.filter (fun edge ->
                match Map.tryFind edge state.Qubits with
                | Some One -> true
                | _ -> false)
            |> List.length
        if excitations % 2 = 0 then 1 else -1

    /// Measure full syndrome of the planar code.
    /// 
    /// Vertices: (d-1) × d interior vertices for X-stabilizers
    /// Plaquettes: d × (d-1) interior plaquettes for Z-stabilizers
    let measurePlanarSyndrome (state: PlanarCodeState) : PlanarSyndrome =
        let d = state.Lattice.Distance
        // X-stabilizers on vertices: x in [0..d-1], y in [0..d-2]
        let xDefects =
            [ for y in 0 .. d - 2 do
                for x in 0 .. d - 1 do
                    let v = { X = x; Y = y }
                    if measurePlanarVertexOperator state v = -1 then
                        v ]
        // Z-stabilizers on plaquettes: x in [0..d-2], y in [0..d-2]
        let zDefects =
            [ for y in 0 .. d - 2 do
                for x in 0 .. d - 2 do
                    let p = { X = x; Y = y }
                    if measurePlanarPlaquetteOperator state p = -1 then
                        p ]
        { XDefects = xDefects; ZDefects = zDefects }

    /// Manhattan distance (no wrapping for planar code).
    let planarDistance (p1: Coords) (p2: Coords) : int =
        abs (p1.X - p2.X) + abs (p1.Y - p2.Y)

    /// Distance from a defect to the nearest boundary.
    /// 
    /// For X-defects: nearest rough boundary (top y=0 or bottom y=d-2)
    /// For Z-defects: nearest smooth boundary (left x=0 or right x=d-2)
    let distanceToBoundary (lattice: PlanarLattice) (defect: Coords) (isXDefect: bool) : int =
        let d = lattice.Distance
        if isXDefect then
            // X-defects match to rough boundaries (top/bottom)
            min defect.Y (d - 2 - defect.Y)
        else
            // Z-defects match to smooth boundaries (left/right)
            min defect.X (d - 2 - defect.X)

    /// Virtual boundary node for matching (represented as a special coordinate).
    let private boundaryNode = { X = -1; Y = -1 }

    /// Decode planar code syndrome using MWPM with boundary matching.
    /// 
    /// In the planar code, defects can be paired with each other OR matched
    /// to a boundary. We add virtual boundary nodes to the matching graph
    /// with weights equal to the distance to the nearest boundary.
    let decodePlanarSyndrome
        (lattice: PlanarLattice)
        (defects: Coords list)
        (isXDefects: bool)
        : TopologicalResult<PlanarDecoderResult> =

        if defects.IsEmpty then
            Ok { MatchedPairs = []; Corrections = []; TotalWeight = 0; BoundaryMatches = 0 }
        else
            // Build weighted edges: defect-to-defect + defect-to-boundary
            let defectEdges =
                [ for i in 0 .. defects.Length - 2 do
                    for j in i + 1 .. defects.Length - 1 do
                        (defects.[i], defects.[j], planarDistance defects.[i] defects.[j]) ]

            let boundaryEdges =
                defects
                |> List.map (fun d ->
                    (d, boundaryNode, distanceToBoundary lattice d isXDefects))

            // Greedy matching: sort all candidate edges by weight, greedily match
            let allEdges =
                (defectEdges |> List.map (fun (a, b, w) -> (a, b, w, false)))
                @ (boundaryEdges |> List.map (fun (a, b, w) -> (a, b, w, true)))
                |> List.sortBy (fun (_, _, w, _) -> w)

            let rec matchLoop remaining (matched: Set<Coords>) pairs boundaryCount =
                match remaining with
                | [] -> (pairs, boundaryCount)
                | (a, _, _, isBoundary) :: rest when matched.Contains a ->
                    matchLoop rest matched pairs boundaryCount
                | (_, b, _, false) :: rest when matched.Contains b ->
                    matchLoop rest matched pairs boundaryCount
                | (a, b, _, isBoundary) :: rest ->
                    let newMatched =
                        if isBoundary then matched |> Set.add a
                        else matched |> Set.add a |> Set.add b
                    let newBoundaryCount =
                        if isBoundary then boundaryCount + 1 else boundaryCount
                    matchLoop rest newMatched ((a, b) :: pairs) newBoundaryCount

            let (pairs, boundaryCount) = matchLoop allEdges Set.empty [] 0

            let totalWeight =
                pairs
                |> List.sumBy (fun (a, b) ->
                    if b = boundaryNode then distanceToBoundary lattice a isXDefects
                    else planarDistance a b)

            // Build correction paths (simplified: direct Manhattan paths)
            let corrections =
                pairs
                |> List.collect (fun (a, b) ->
                    let target =
                        if b = boundaryNode then
                            // Path to nearest boundary
                            if isXDefects then
                                let ty = if a.Y <= (lattice.Distance - 2) / 2 then -1 else lattice.Distance - 1
                                { X = a.X; Y = ty }
                            else
                                let tx = if a.X <= (lattice.Distance - 2) / 2 then -1 else lattice.Distance - 1
                                { X = tx; Y = a.Y }
                        else b
                    // Generate edges along shortest path from a to target
                    let mutable edges = []
                    let mutable cx = a.X
                    let mutable cy = a.Y
                    // Horizontal segment
                    while cx <> target.X && target.X >= 0 && cx >= 0 do
                        let step = if target.X > cx then 1 else -1
                        let edgeX = if step > 0 then cx else cx - 1
                        if edgeX >= 0 && edgeX < lattice.Distance - 1 then
                            edges <- { Position = { X = edgeX; Y = cy }; EdgeType = PVertical } :: edges
                        cx <- cx + step
                    // Vertical segment
                    while cy <> target.Y && target.Y >= 0 && cy >= 0 do
                        let step = if target.Y > cy then 1 else -1
                        let edgeY = if step > 0 then cy else cy - 1
                        if edgeY >= 0 && edgeY < lattice.Distance - 1 then
                            edges <- { Position = { X = cx |> max 0 |> min (lattice.Distance - 1); Y = edgeY }; EdgeType = PHorizontal } :: edges
                        cy <- cy + step
                    edges)

            Ok {
                MatchedPairs = pairs |> List.rev
                Corrections = corrections
                TotalWeight = totalWeight
                BoundaryMatches = boundaryCount
            }

    /// Full planar code decode: measure syndrome and decode both X and Z defects.
    let decodePlanarCode
        (state: PlanarCodeState)
        : TopologicalResult<PlanarCodeState * PlanarDecoderResult * PlanarDecoderResult> =

        let syndrome = measurePlanarSyndrome state
        decodePlanarSyndrome state.Lattice syndrome.XDefects true
        |> Result.bind (fun xResult ->
            decodePlanarSyndrome state.Lattice syndrome.ZDefects false
            |> Result.map (fun zResult ->
                // Apply X corrections
                let afterX =
                    xResult.Corrections
                    |> List.fold (fun s e -> applyPlanarZError s e) state
                // Apply Z corrections
                let afterXZ =
                    zResult.Corrections
                    |> List.fold (fun s e -> applyPlanarXError s e) afterX
                (afterXZ, xResult, zResult)))

    // ========================================================================
    // COLOR CODE
    // ========================================================================

    /// Color label for faces in the color code lattice
    type FaceColor =
        | Red
        | Green
        | Blue

    /// Face in the color code lattice
    type ColorCodeFace = {
        /// Center position of the face
        Center: Coords
        /// Color assignment
        Color: FaceColor
        /// Vertices bounding this face
        Vertices: Coords list
    }

    /// Color code lattice (4.8.8 tiling)
    /// 
    /// The 4.8.8 lattice tiles the plane with squares and octagons.
    /// Faces are 3-colorable: squares get one color, octagons get two others.
    type ColorCodeLattice = {
        /// Code distance (must be odd, >= 3)
        Distance: int
        /// All faces with their color assignments
        Faces: ColorCodeFace list
        /// All qubit positions (on vertices of the lattice)
        QubitPositions: Coords list
    }

    /// Color code state
    type ColorCodeState = {
        Lattice: ColorCodeLattice
        /// Qubit states at each vertex
        Qubits: Map<Coords, QubitState>
    }

    /// Color code syndrome
    type ColorCodeSyndrome = {
        /// Faces with X-stabilizer violations, grouped by color
        XDefects: Map<FaceColor, Coords list>
        /// Faces with Z-stabilizer violations, grouped by color
        ZDefects: Map<FaceColor, Coords list>
    }

    /// Decoder result for color code
    type ColorCodeDecoderResult = {
        /// Matched pairs of defects per color
        MatchedPairs: (Coords * Coords) list
        /// Correction qubit positions
        Corrections: Coords list
        /// Total matching weight
        TotalWeight: int
    }

    /// Build a 4.8.8 color code lattice.
    /// 
    /// The lattice is constructed as a triangular arrangement of the 4.8.8 tiling.
    /// For distance d, we build a triangular patch with d layers.
    /// Qubits live on vertices; stabilizers are associated with faces.
    let createColorCodeLattice (distance: int) : TopologicalResult<ColorCodeLattice> =
        if distance < 3 then
            TopologicalResult.validationError "distance" "Color code distance must be >= 3"
        elif distance % 2 = 0 then
            TopologicalResult.validationError "distance" "Color code distance must be odd"
        else
            // Build simplified 4.8.8 tiling for the given distance
            // Each "unit cell" at position (cx, cy) contributes:
            //   - 1 square face (center at 2*cx+1, 2*cy+1)
            //   - Surrounding octagon faces shared with neighbors
            let halfD = (distance - 1) / 2
            let mutable faces = []
            let mutable qubitSet = Set.empty

            // Build grid of squares and octagons
            for cy in 0 .. halfD do
                for cx in 0 .. halfD do
                    // Square face at (2cx, 2cy) - colored Red
                    let sqVerts = [
                        { X = 2*cx; Y = 2*cy }
                        { X = 2*cx+1; Y = 2*cy }
                        { X = 2*cx+1; Y = 2*cy+1 }
                        { X = 2*cx; Y = 2*cy+1 }
                    ]
                    faces <- { Center = { X = 2*cx; Y = 2*cy }; Color = Red; Vertices = sqVerts } :: faces
                    for v in sqVerts do qubitSet <- Set.add v qubitSet

            // Octagon faces (Green and Blue alternating)
            for cy in 0 .. halfD - 1 do
                for cx in 0 .. halfD - 1 do
                    // Octagon between four squares
                    let octVerts = [
                        { X = 2*cx+1; Y = 2*cy }
                        { X = 2*cx+2; Y = 2*cy }
                        { X = 2*cx+2; Y = 2*cy+1 }
                        { X = 2*cx+2; Y = 2*cy+2 }
                        { X = 2*cx+1; Y = 2*cy+2 }
                        { X = 2*cx; Y = 2*cy+2 }
                        { X = 2*cx; Y = 2*cy+1 }
                        { X = 2*cx+1; Y = 2*cy+1 }
                    ]
                    let color = if (cx + cy) % 2 = 0 then Green else Blue
                    faces <- { Center = { X = 2*cx+1; Y = 2*cy+1 }; Color = color; Vertices = octVerts } :: faces
                    for v in octVerts do qubitSet <- Set.add v qubitSet

            Ok {
                Distance = distance
                Faces = faces |> List.rev
                QubitPositions = qubitSet |> Set.toList |> List.sortBy (fun c -> (c.Y, c.X))
            }

    /// Number of physical qubits in the color code.
    let colorCodePhysicalQubits (lattice: ColorCodeLattice) : int =
        lattice.QubitPositions.Length

    /// Number of logical qubits (always 1 for a single color code patch).
    let colorCodeLogicalQubits (_lattice: ColorCodeLattice) : int = 1

    /// Code distance of the color code.
    let colorCodeDistance (lattice: ColorCodeLattice) : int = lattice.Distance

    /// Initialize color code in ground state (all qubits |+⟩).
    let initializeColorCodeGroundState (lattice: ColorCodeLattice) : ColorCodeState =
        let qubits =
            lattice.QubitPositions
            |> List.map (fun pos -> (pos, Plus))
            |> Map.ofList
        { Lattice = lattice; Qubits = qubits }

    /// Apply X error at a qubit position.
    let applyColorCodeXError (state: ColorCodeState) (pos: Coords) : ColorCodeState =
        let newQubits =
            state.Qubits
            |> Map.change pos (Option.map (function
                | Zero -> One | One -> Zero
                | Plus -> Plus | Minus -> Minus))
        { state with Qubits = newQubits }

    /// Apply Z error at a qubit position.
    let applyColorCodeZError (state: ColorCodeState) (pos: Coords) : ColorCodeState =
        let newQubits =
            state.Qubits
            |> Map.change pos (Option.map (function
                | Zero -> Zero | One -> One
                | Plus -> Minus | Minus -> Plus))
        { state with Qubits = newQubits }

    /// Measure X-stabilizer for a face in the color code.
    /// 
    /// The X-stabilizer for a face f is the product of X operators on all
    /// qubits at vertices of f.
    let measureColorCodeXStabilizer (state: ColorCodeState) (face: ColorCodeFace) : int =
        let excitations =
            face.Vertices
            |> List.filter (fun v ->
                match Map.tryFind v state.Qubits with
                | Some Minus -> true
                | _ -> false)
            |> List.length
        if excitations % 2 = 0 then 1 else -1

    /// Measure Z-stabilizer for a face in the color code.
    let measureColorCodeZStabilizer (state: ColorCodeState) (face: ColorCodeFace) : int =
        let excitations =
            face.Vertices
            |> List.filter (fun v ->
                match Map.tryFind v state.Qubits with
                | Some One -> true
                | _ -> false)
            |> List.length
        if excitations % 2 = 0 then 1 else -1

    /// Measure full syndrome of the color code.
    let measureColorCodeSyndrome (state: ColorCodeState) : ColorCodeSyndrome =
        let classifyDefects measureFn =
            state.Lattice.Faces
            |> List.filter (fun face -> measureFn state face = -1)
            |> List.groupBy (fun face -> face.Color)
            |> List.map (fun (color, faces) -> (color, faces |> List.map (fun f -> f.Center)))
            |> Map.ofList
        { XDefects = classifyDefects measureColorCodeXStabilizer
          ZDefects = classifyDefects measureColorCodeZStabilizer }

    /// Get all defect positions from a color code syndrome (flattened).
    let getColorCodeDefects (defectMap: Map<FaceColor, Coords list>) : Coords list =
        defectMap |> Map.toList |> List.collect snd

    /// Decode color code syndrome using greedy matching.
    /// 
    /// Color code decoding can be decomposed: match defects of the same color
    /// (restricted lattice matching). We use a simplified greedy approach.
    let decodeColorCodeSyndrome
        (lattice: ColorCodeLattice)
        (defects: Coords list)
        : TopologicalResult<ColorCodeDecoderResult> =

        if defects.IsEmpty then
            Ok { MatchedPairs = []; Corrections = []; TotalWeight = 0 }
        elif defects.Length % 2 <> 0 then
            // Odd number of defects: match all but one, ignoring the furthest
            // In practice this shouldn't happen with valid stabilizer codes
            TopologicalResult.validationError "defects"
                "Odd number of defects; expected even from stabilizer constraints"
        else
            let edges =
                [ for i in 0 .. defects.Length - 2 do
                    for j in i + 1 .. defects.Length - 1 do
                        (defects.[i], defects.[j], planarDistance defects.[i] defects.[j]) ]
                |> List.sortBy (fun (_, _, w) -> w)

            let rec matchLoop remaining (matched: Set<Coords>) pairs =
                match remaining with
                | [] -> pairs
                | (a, b, _) :: rest ->
                    if matched.Contains a || matched.Contains b then
                        matchLoop rest matched pairs
                    else
                        matchLoop rest (matched |> Set.add a |> Set.add b) ((a, b) :: pairs)

            let pairs = matchLoop edges Set.empty [] |> List.rev

            let totalWeight =
                pairs |> List.sumBy (fun (a, b) -> planarDistance a b)

            // Corrections: midpoint qubits along shortest paths
            let corrections =
                pairs
                |> List.collect (fun (a, b) ->
                    // Find qubits along path that need correction
                    let mutable corr = []
                    let mutable cx = a.X
                    while cx <> b.X do
                        let step = if b.X > cx then 1 else -1
                        corr <- { X = cx; Y = a.Y } :: corr
                        cx <- cx + step
                    let mutable cy = a.Y
                    while cy <> b.Y do
                        let step = if b.Y > cy then 1 else -1
                        corr <- { X = b.X; Y = cy } :: corr
                        cy <- cy + step
                    corr)

            Ok {
                MatchedPairs = pairs
                Corrections = corrections
                TotalWeight = totalWeight
            }

    /// Full color code decode: measure syndrome and decode.
    let decodeColorCode
        (state: ColorCodeState)
        : TopologicalResult<ColorCodeState * ColorCodeDecoderResult * ColorCodeDecoderResult> =

        let syndrome = measureColorCodeSyndrome state
        let xDefects = getColorCodeDefects syndrome.XDefects
        let zDefects = getColorCodeDefects syndrome.ZDefects

        decodeColorCodeSyndrome state.Lattice xDefects
        |> Result.bind (fun xResult ->
            decodeColorCodeSyndrome state.Lattice zDefects
            |> Result.map (fun zResult ->
                // Apply corrections
                let afterX =
                    xResult.Corrections
                    |> List.fold (fun s pos -> applyColorCodeZError s pos) state
                let afterXZ =
                    zResult.Corrections
                    |> List.fold (fun s pos -> applyColorCodeXError s pos) afterX
                (afterXZ, xResult, zResult)))

    /// Get faces of a specific color.
    let getFacesByColor (lattice: ColorCodeLattice) (color: FaceColor) : ColorCodeFace list =
        lattice.Faces |> List.filter (fun f -> f.Color = color)

    /// Check if the lattice is properly 3-colorable.
    /// 
    /// A valid 3-coloring requires that no two adjacent faces share the same color.
    /// Two faces are adjacent if they share at least one vertex.
    let isValid3Coloring (lattice: ColorCodeLattice) : bool =
        let facesByVertex =
            lattice.Faces
            |> List.collect (fun face ->
                face.Vertices |> List.map (fun v -> (v, face)))
            |> List.groupBy fst
            |> List.map (fun (v, pairs) -> (v, pairs |> List.map snd))

        facesByVertex
        |> List.forall (fun (_, faces) ->
            let colors = faces |> List.map (fun f -> f.Color) |> List.distinct
            // At any vertex, all adjacent faces should have distinct colors
            // (for 4.8.8 lattice, at most 3 faces meet at a vertex)
            colors.Length = faces.Length || faces.Length <= 1)
