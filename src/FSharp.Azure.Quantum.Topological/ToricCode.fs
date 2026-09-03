namespace FSharp.Azure.Quantum.Topological

/// Toric code for topological error correction
/// 
/// The toric code (Kitaev 2003) is a topological quantum error-correcting code
/// defined on a 2D square lattice on a torus. It protects quantum information
/// through topological properties rather than local encoding.
/// 
/// Key concepts (from Simon's "Topological Quantum" Ch 27-30):
/// - Qubits live on edges of a square lattice
/// - Stabilizers: Vertex operators (A_v) and plaquette operators (B_p)
/// - Ground state: Common +1 eigenstate of all stabilizers
/// - Logical operators: Non-contractible loops around torus
/// - Anyonic excitations: Violations of stabilizers (e-particles, m-particles)
/// 
/// Error correction:
/// - Measure syndromes (stabilizer violations)
/// - Identify anyon positions
/// - Find minimum-weight matching
/// - Apply correction operators
/// 
/// References:
/// - Kitaev, "Fault-tolerant quantum computation by anyons" (2003)
/// - Steven H. Simon, "Topological Quantum" (2023), Chapters 27-30
[<RequireQualifiedAccess>]
module ToricCode =
    
    open System
    
    /// Coordinates on the lattice
    [<Struct>]
    type Coords = { X: int; Y: int }
    
    /// Lattice topology for toric code
    /// 
    /// The lattice is a 2D square grid with periodic boundary conditions
    /// (identifying opposite edges to form a torus).
    [<Struct>]
    type Lattice = {
        /// Width of the lattice (number of unit cells in x-direction)
        Width: int
        
        /// Height of the lattice (number of unit cells in y-direction)
        Height: int
    }
    
    /// Qubit state (lives on an edge)
    type QubitState = 
        | Zero
        | One
        /// |+⟩ = (|0⟩ + |1⟩)/√2
        | Plus
        /// |-⟩ = (|0⟩ - |1⟩)/√2
        | Minus
    
    /// Edge orientation
    type EdgeType =
        /// Edge connecting (x,y) to (x+1,y)
        | Horizontal
        /// Edge connecting (x,y) to (x,y+1)
        | Vertical
    
    /// Edge identifier
    type Edge = {
        Position: Coords
        Type: EdgeType
    }
    
    /// Syndrome measurement result
    /// 
    /// Maps each vertex/plaquette to +1 (no excitation) or -1 (excitation present)
    type Syndrome = {
        /// Vertex operator eigenvalues (e-particle positions)
        VertexSyndrome: Map<Coords, int>
        
        /// Plaquette operator eigenvalues (m-particle positions)
        PlaquetteSyndrome: Map<Coords, int>
    }
    
    /// Toric code state
    /// 
    /// Represents the quantum state of all qubits on the lattice
    type ToricCodeState = {
        Lattice: Lattice
        
        /// Qubit states indexed by edge
        Qubits: Map<Edge, QubitState>
    }
    
    /// Anyon type in toric code
    /// 
    /// The toric code has four anyon types (Z₂ × Z₂ theory):
    /// - 1: Vacuum (no excitation)
    /// - e: Electric charge (vertex excitation)
    /// - m: Magnetic flux (plaquette excitation)  
    /// - ε: Fermion (both excitations, ε = e × m)
    type ToricAnyon =
        | Vacuum
        /// e-particle
        | Electric
        /// m-particle
        | Magnetic
        /// ε-particle (e × m)
        | Fermion
    
    /// Create a toric code lattice
    /// 
    /// Returns Error if dimensions are invalid (must be > 0).
    let createLattice (width: int) (height: int) : TopologicalResult<Lattice> =
        if width <= 0 || height <= 0 then
            TopologicalResult.validationError "latticeDimensions" "Width and height must be positive"
        else
            Ok { Width = width; Height = height }
    
    /// Apply periodic boundary conditions
    let private wrapCoords (lattice: Lattice) (coords: Coords) : Coords =
        {
            X = ((coords.X % lattice.Width) + lattice.Width) % lattice.Width
            Y = ((coords.Y % lattice.Height) + lattice.Height) % lattice.Height
        }
    
    /// Get all edges in the lattice
    let getAllEdges (lattice: Lattice) : Edge list =
        let horizontalEdges =
            [0 .. lattice.Height - 1]
            |> List.collect (fun y ->
                [0 .. lattice.Width - 1]
                |> List.map (fun x ->
                    { Position = { X = x; Y = y }; Type = Horizontal }))
        
        let verticalEdges =
            [0 .. lattice.Height - 1]
            |> List.collect (fun y ->
                [0 .. lattice.Width - 1]
                |> List.map (fun x ->
                    { Position = { X = x; Y = y }; Type = Vertical }))
        
        horizontalEdges @ verticalEdges
    
    /// Get edges around a vertex (star operator)
    /// 
    /// Vertex at (x,y) has four adjacent edges:
    /// - Horizontal edge from (x-1,y)
    /// - Horizontal edge from (x,y)
    /// - Vertical edge from (x,y-1)
    /// - Vertical edge from (x,y)
    let getVertexEdges (lattice: Lattice) (vertex: Coords) : Edge list =
        let v = wrapCoords lattice vertex
        [
            { Position = wrapCoords lattice { X = v.X - 1; Y = v.Y }; Type = Horizontal }
            { Position = v; Type = Horizontal }
            { Position = wrapCoords lattice { X = v.X; Y = v.Y - 1 }; Type = Vertical }
            { Position = v; Type = Vertical }
        ]
    
    /// Get edges around a plaquette (face operator)
    /// 
    /// Plaquette at (x,y) has four boundary edges:
    /// - Horizontal edge at (x,y)
    /// - Horizontal edge at (x,y+1)
    /// - Vertical edge at (x,y)
    /// - Vertical edge at (x+1,y)
    let getPlaquetteEdges (lattice: Lattice) (plaquette: Coords) : Edge list =
        let p = wrapCoords lattice plaquette
        [
            { Position = p; Type = Horizontal }
            { Position = wrapCoords lattice { X = p.X; Y = p.Y + 1 }; Type = Horizontal }
            { Position = p; Type = Vertical }
            { Position = wrapCoords lattice { X = p.X + 1; Y = p.Y }; Type = Vertical }
        ]
    
    /// Initialize toric code in ground state
    /// 
    /// Ground state is the common +1 eigenstate of all stabilizers.
    /// We initialize all qubits to |+⟩ state.
    let initializeGroundState (lattice: Lattice) : ToricCodeState =
        let edges = getAllEdges lattice
        let qubits = 
            edges 
            |> List.map (fun edge -> (edge, Plus))
            |> Map.ofList
        
        { Lattice = lattice; Qubits = qubits }
    
    /// Measure vertex operator A_v = ∏_{edges around v} X_edge
    /// 
    /// Returns +1 if no e-particle at vertex, -1 if e-particle present
    let measureVertexOperator (state: ToricCodeState) (vertex: Coords) : int =
        let edges = getVertexEdges state.Lattice vertex
        
        // For now, simplified: count X-basis excitations
        // In full implementation, this would apply X operators and measure
        let excitations =
            edges
            |> List.filter (fun edge ->
                match Map.tryFind edge state.Qubits with
                | Some Minus -> true  // X eigenvalue -1
                | _ -> false)
            |> List.length
        
        if excitations % 2 = 0 then 1 else -1
    
    /// Measure plaquette operator B_p = ∏_{edges around p} Z_edge
    /// 
    /// Returns +1 if no m-particle at plaquette, -1 if m-particle present
    let measurePlaquetteOperator (state: ToricCodeState) (plaquette: Coords) : int =
        let edges = getPlaquetteEdges state.Lattice plaquette
        
        // For now, simplified: count Z-basis excitations
        // In full implementation, this would apply Z operators and measure
        let excitations =
            edges
            |> List.filter (fun edge ->
                match Map.tryFind edge state.Qubits with
                | Some One -> true  // Z eigenvalue -1
                | _ -> false)
            |> List.length
        
        if excitations % 2 = 0 then 1 else -1
    
    /// Measure full syndrome
    /// 
    /// Returns positions of all anyonic excitations (stabilizer violations)
    let measureSyndrome (state: ToricCodeState) : Syndrome =
        let vertices =
            [0 .. state.Lattice.Height - 1]
            |> List.collect (fun y ->
                [0 .. state.Lattice.Width - 1]
                |> List.map (fun x -> { X = x; Y = y }))
        
        let vertexSyndrome =
            vertices
            |> List.map (fun v -> (v, measureVertexOperator state v))
            |> Map.ofList
        
        let plaquetteSyndrome =
            vertices  // Same positions for plaquettes
            |> List.map (fun p -> (p, measurePlaquetteOperator state p))
            |> Map.ofList
        
        { VertexSyndrome = vertexSyndrome
          PlaquetteSyndrome = plaquetteSyndrome }
    
    /// Get positions of e-particles (vertex excitations)
    let getElectricExcitations (syndrome: Syndrome) : Coords list =
        syndrome.VertexSyndrome
        |> Map.toList
        |> List.filter (fun (_, eigenvalue) -> eigenvalue = -1)
        |> List.map fst
    
    /// Get positions of m-particles (plaquette excitations)
    let getMagneticExcitations (syndrome: Syndrome) : Coords list =
        syndrome.PlaquetteSyndrome
        |> Map.toList
        |> List.filter (fun (_, eigenvalue) -> eigenvalue = -1)
        |> List.map fst
    
    /// Calculate Manhattan distance on torus
    let toricDistance (lattice: Lattice) (p1: Coords) (p2: Coords) : int =
        let dx = abs (p1.X - p2.X)
        let dy = abs (p1.Y - p2.Y)
        
        // Account for wrapping around torus
        let dx' = min dx (lattice.Width - dx)
        let dy' = min dy (lattice.Height - dy)
        
        dx' + dy'
    
    /// Apply X gate to an edge (bit flip error)
    let applyXError (state: ToricCodeState) (edge: Edge) : ToricCodeState =
        let newQubits =
            state.Qubits
            |> Map.change edge (Option.map (function
                | Zero -> One
                | One -> Zero
                | Plus -> Plus    // X|+⟩ = |+⟩
                | Minus -> Minus  // X|-⟩ = -|-⟩ (global phase)
            ))
        
        { state with Qubits = newQubits }
    
    /// Apply Z gate to an edge (phase flip error)
    let applyZError (state: ToricCodeState) (edge: Edge) : ToricCodeState =
        let newQubits =
            state.Qubits
            |> Map.change edge (Option.map (function
                | Zero -> Zero    // Z|0⟩ = |0⟩
                | One -> One      // Z|1⟩ = -|1⟩ (global phase)
                | Plus -> Minus   // Z|+⟩ = |-⟩
                | Minus -> Plus   // Z|-⟩ = |+⟩
            ))
        
        { state with Qubits = newQubits }
    
    /// Number of logical qubits encoded
    /// 
    /// For a torus: 2 logical qubits (from 2 independent non-contractible loops)
    let logicalQubits (_lattice: Lattice) : int = 2
    
    /// Number of physical qubits
    /// 
    /// Toric code has one qubit per edge: 2 × Width × Height
    let physicalQubits (lattice: Lattice) : int =
        2 * lattice.Width * lattice.Height
    
    /// Code distance
    /// 
    /// Minimum weight of a logical operator = min(Width, Height)
    let codeDistance (lattice: Lattice) : int =
        min lattice.Width lattice.Height
    
    // ========================================================================
    // MINIMUM-WEIGHT PERFECT MATCHING (MWPM) DECODER
    // ========================================================================
    
    /// A weighted edge between two syndrome positions for matching
    type MatchingEdge = {
        /// First syndrome position
        From: Coords
        /// Second syndrome position
        To: Coords
        /// Manhattan distance on the torus (weight)
        Weight: int
    }
    
    /// Correction chain: a sequence of edges to apply X or Z corrections
    type CorrectionChain = {
        /// Edges to flip (apply X or Z gate)
        Edges: Edge list
    }
    
    /// Result of syndrome decoding
    type DecoderResult = {
        /// Matched pairs of syndrome excitations
        MatchedPairs: (Coords * Coords) list
        /// Correction chains to apply
        Corrections: CorrectionChain list
        /// Total weight of the matching (sum of distances)
        TotalWeight: int
    }
    
    /// Build complete weighted graph from syndrome positions.
    /// 
    /// Every pair of excitations is connected with weight equal to
    /// their toric (Manhattan) distance. This is the input graph for MWPM.
    let buildMatchingGraph (lattice: Lattice) (excitations: Coords list) : MatchingEdge list =
        [ for i in 0 .. excitations.Length - 2 do
            for j in i + 1 .. excitations.Length - 1 do
                { From = excitations.[i]
                  To = excitations.[j]
                  Weight = toricDistance lattice excitations.[i] excitations.[j] } ]
    
    /// Greedy minimum-weight perfect matching.
    /// 
    /// Approximation of MWPM: sort edges by weight, greedily match unmatched
    /// vertices with nearest unmatched partner. Exact for small instances and
    /// a good approximation for larger ones.
    /// 
    /// On a torus, excitations always come in pairs (stabilizer constraint),
    /// so a perfect matching always exists when |excitations| is even.
    /// 
    /// Returns Error if the number of excitations is odd (which would indicate
    /// a measurement error or violated stabilizer constraint).
    let greedyMatching 
        (lattice: Lattice) 
        (excitations: Coords list) 
        : TopologicalResult<(Coords * Coords) list> =
        
        if excitations.Length % 2 <> 0 then
            TopologicalResult.validationError "excitations" 
                "Number of excitations must be even (stabilizer constraint on torus)"
        elif excitations.Length = 0 then
            Ok []
        else
            let edges = buildMatchingGraph lattice excitations
            let sorted = edges |> List.sortBy (fun e -> e.Weight)
            
            let rec matchLoop (remaining: MatchingEdge list) (matched: Set<Coords>) (pairs: (Coords * Coords) list) =
                match remaining with
                | [] -> pairs
                | edge :: rest ->
                    if matched.Contains edge.From || matched.Contains edge.To then
                        matchLoop rest matched pairs
                    else
                        let newMatched = matched |> Set.add edge.From |> Set.add edge.To
                        matchLoop rest newMatched ((edge.From, edge.To) :: pairs)
            
            Ok (matchLoop sorted Set.empty [] |> List.rev)
    
    /// Which syndrome lattice a correction path connects.
    ///
    /// - VertexSyndrome: e-particles live on vertices; the path walks the direct
    ///   lattice, so it crosses Horizontal edges when moving in x and Vertical
    ///   edges when moving in y. Corrections are Z operators (Z strings create
    ///   and annihilate e-particle pairs at their endpoints).
    /// - PlaquetteSyndrome: m-particles live on plaquettes; the path walks the
    ///   dual lattice, so it crosses Vertical edges when moving in x and
    ///   Horizontal edges when moving in y. Corrections are X operators.
    type SyndromeKind =
        | VertexSyndrome
        | PlaquetteSyndrome

    /// Compute a shortest correction path between two syndrome positions on the torus.
    ///
    /// Finds the sequence of edges connecting p1 to p2 via the shortest
    /// Manhattan-distance route (accounting for torus wrap-around).
    /// Each unit step of the path carries the actual edge type it crosses:
    /// - Vertex-to-vertex paths (direct lattice): x-moves cross Horizontal edges,
    ///   y-moves cross Vertical edges.
    /// - Plaquette-to-plaquette paths (dual lattice): x-moves cross Vertical edges,
    ///   y-moves cross Horizontal edges.
    let correctionPath
        (lattice: Lattice)
        (p1: Coords)
        (p2: Coords)
        (kind: SyndromeKind)
        : Edge list =

        let wrapX x = ((x % lattice.Width) + lattice.Width) % lattice.Width
        let wrapY y = ((y % lattice.Height) + lattice.Height) % lattice.Height

        let dx = p2.X - p1.X
        let dy = p2.Y - p1.Y

        // Choose shortest horizontal direction (accounting for wrap-around)
        let dxShortest =
            if abs dx <= lattice.Width - abs dx then dx
            elif dx > 0 then dx - lattice.Width else dx + lattice.Width

        let dyShortest =
            if abs dy <= lattice.Height - abs dy then dy
            elif dy > 0 then dy - lattice.Height else dy + lattice.Height

        // Build horizontal segment: moving from vertex (x,y) to (x+1,y) crosses the
        // Horizontal edge at (x,y); moving to (x-1,y) crosses the one at (x-1,y).
        // On the dual lattice, plaquette (x,y) → (x+1,y) crosses the Vertical edge
        // at (x+1,y); plaquette (x,y) → (x-1,y) crosses the Vertical edge at (x,y).
        let horizontalEdges =
            if dxShortest = 0 then []
            else
                let step = if dxShortest > 0 then 1 else -1
                [ for i in 0 .. abs dxShortest - 1 do
                    let x = p1.X + i * step
                    match kind with
                    | VertexSyndrome ->
                        let edgeX = if step > 0 then x else x - 1
                        { Position = { X = wrapX edgeX; Y = wrapY p1.Y }; Type = Horizontal }
                    | PlaquetteSyndrome ->
                        let edgeX = if step > 0 then x + 1 else x
                        { Position = { X = wrapX edgeX; Y = wrapY p1.Y }; Type = Vertical } ]

        // Build vertical segment (starting from endpoint of horizontal segment).
        // Vertex (x,y) → (x,y+1) crosses the Vertical edge at (x,y); (x,y) → (x,y-1)
        // crosses the one at (x,y-1). Plaquette (x,y) → (x,y+1) crosses the
        // Horizontal edge at (x,y+1); (x,y) → (x,y-1) crosses the one at (x,y).
        let xEnd = wrapX (p1.X + dxShortest)
        let verticalEdges =
            if dyShortest = 0 then []
            else
                let step = if dyShortest > 0 then 1 else -1
                [ for i in 0 .. abs dyShortest - 1 do
                    let y = p1.Y + i * step
                    match kind with
                    | VertexSyndrome ->
                        let edgeY = if step > 0 then y else y - 1
                        { Position = { X = xEnd; Y = wrapY edgeY }; Type = Vertical }
                    | PlaquetteSyndrome ->
                        let edgeY = if step > 0 then y + 1 else y
                        { Position = { X = xEnd; Y = wrapY edgeY }; Type = Horizontal } ]

        horizontalEdges @ verticalEdges
    
    /// Decode vertex syndrome (e-particle excitations) using greedy MWPM.
    ///
    /// Steps:
    /// 1. Extract e-particle positions from syndrome
    /// 2. Build complete weighted graph with toric distances
    /// 3. Find greedy minimum-weight perfect matching
    /// 4. Construct correction chains (Z operators along shortest paths —
    ///    e-particles are endpoints of Z-error strings, so a Z string
    ///    connecting them annihilates the pair)
    ///
    /// The correction chains, when applied to the state, annihilate the
    /// e-particle pairs and restore the ground state code space.
    let decodeVertexSyndrome
        (lattice: Lattice)
        (syndrome: Syndrome)
        : TopologicalResult<DecoderResult> =

        let excitations = getElectricExcitations syndrome

        greedyMatching lattice excitations
        |> Result.map (fun pairs ->
            let corrections =
                pairs
                |> List.map (fun (p1, p2) ->
                    { Edges = correctionPath lattice p1 p2 VertexSyndrome })
            
            let totalWeight = 
                pairs |> List.sumBy (fun (p1, p2) -> toricDistance lattice p1 p2)
            
            { MatchedPairs = pairs
              Corrections = corrections
              TotalWeight = totalWeight })
    
    /// Decode plaquette syndrome (m-particle excitations) using greedy MWPM.
    ///
    /// Same as vertex decoding but uses X corrections along dual-lattice paths
    /// (m-particles are endpoints of X-error strings) to annihilate
    /// m-particle pairs.
    let decodePlaquetteSyndrome
        (lattice: Lattice)
        (syndrome: Syndrome)
        : TopologicalResult<DecoderResult> =

        let excitations = getMagneticExcitations syndrome

        greedyMatching lattice excitations
        |> Result.map (fun pairs ->
            let corrections =
                pairs
                |> List.map (fun (p1, p2) ->
                    { Edges = correctionPath lattice p1 p2 PlaquetteSyndrome })
            
            let totalWeight = 
                pairs |> List.sumBy (fun (p1, p2) -> toricDistance lattice p1 p2)
            
            { MatchedPairs = pairs
              Corrections = corrections
              TotalWeight = totalWeight })
    
    /// Apply correction chains to a toric code state.
    ///
    /// Vertex-syndrome corrections apply Z gates along the chain: e-particles
    /// (vertex excitations, A_v = ∏X) are created by Z errors, so a Z string
    /// connecting the matched pair annihilates them.
    /// Plaquette-syndrome corrections apply X gates along the chain: m-particles
    /// (plaquette excitations, B_p = ∏Z) are created by X errors.
    let applyCorrections
        (state: ToricCodeState)
        (corrections: CorrectionChain list)
        (kind: SyndromeKind)
        : ToricCodeState =

        let applyFn =
            match kind with
            | VertexSyndrome -> applyZError      // Z corrections for e-particles
            | PlaquetteSyndrome -> applyXError   // X corrections for m-particles

        corrections
        |> List.fold (fun currentState chain ->
            chain.Edges
            |> List.fold (fun s edge -> applyFn s edge) currentState
        ) state

    /// Full syndrome decoding: decode both vertex and plaquette syndromes
    /// and apply corrections to restore the code space.
    ///
    /// This is the main entry point for toric code error correction:
    /// 1. Measure syndrome (identify anyon excitations)
    /// 2. Decode vertex syndrome → Z corrections
    /// 3. Decode plaquette syndrome → X corrections
    /// 4. Apply all corrections
    ///
    /// Returns the corrected state and decoder diagnostics.
    let decodeSyndrome
        (state: ToricCodeState)
        : TopologicalResult<ToricCodeState * DecoderResult * DecoderResult> =

        let syndrome = measureSyndrome state

        decodeVertexSyndrome state.Lattice syndrome
        |> Result.bind (fun vertexResult ->
            decodePlaquetteSyndrome state.Lattice syndrome
            |> Result.map (fun plaquetteResult ->
                // Apply vertex corrections (Z-type, annihilate e-particles)
                let afterVertexCorrection =
                    applyCorrections state vertexResult.Corrections VertexSyndrome

                // Apply plaquette corrections (X-type, annihilate m-particles)
                let afterFullCorrection =
                    applyCorrections afterVertexCorrection plaquetteResult.Corrections PlaquetteSyndrome

                (afterFullCorrection, vertexResult, plaquetteResult)))
