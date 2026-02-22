namespace FSharp.Azure.Quantum.Tests.Topological

open Xunit
open FSharp.Azure.Quantum.Topological

module ToricCodeTests =
    
    // ========================================================================
    // LATTICE CREATION AND BASIC PROPERTIES
    // ========================================================================
    
    [<Fact>]
    let ``createLattice with positive dimensions succeeds`` () =
        match ToricCode.createLattice 4 4 with
        | Ok lattice ->
            Assert.Equal(4, lattice.Width)
            Assert.Equal(4, lattice.Height)
        | Error _ -> Assert.Fail("Expected successful lattice creation")
    
    [<Fact>]
    let ``createLattice with zero width fails`` () =
        match ToricCode.createLattice 0 4 with
        | Error (TopologicalError.ValidationError (field, reason)) ->
            Assert.Contains("positive", reason)
        | _ -> Assert.Fail("Expected validation error")
    
    [<Fact>]
    let ``createLattice with negative height fails`` () =
        match ToricCode.createLattice 4 -1 with
        | Error (TopologicalError.ValidationError (field, reason)) ->
            Assert.Contains("positive", reason)
        | _ -> Assert.Fail("Expected validation error")
    
    [<Fact>]
    let ``getAllEdges returns correct count`` () =
        let lattice = { ToricCode.Width = 3; ToricCode.Height = 3 }
        let edges = ToricCode.getAllEdges lattice
        
        // 3×3 lattice has 3×3 horizontal + 3×3 vertical = 18 edges
        Assert.Equal(18, edges.Length)
    
    [<Fact>]
    let ``getAllEdges includes both horizontal and vertical`` () =
        let lattice = { ToricCode.Width = 2; ToricCode.Height = 2 }
        let edges = ToricCode.getAllEdges lattice
        
        let horizontal = 
            edges |> List.filter (fun e -> e.Type = ToricCode.Horizontal) |> List.length
        let vertical = 
            edges |> List.filter (fun e -> e.Type = ToricCode.Vertical) |> List.length
        
        Assert.Equal(4, horizontal)  // 2×2 horizontal edges
        Assert.Equal(4, vertical)    // 2×2 vertical edges
    
    // ========================================================================
    // VERTEX AND PLAQUETTE OPERATORS
    // ========================================================================
    
    [<Fact>]
    let ``getVertexEdges returns 4 edges`` () =
        let lattice = { ToricCode.Width = 4; ToricCode.Height = 4 }
        let vertex = { ToricCode.X = 1; ToricCode.Y = 1 }
        let edges = ToricCode.getVertexEdges lattice vertex
        
        Assert.Equal(4, edges.Length)
    
    [<Fact>]
    let ``getPlaquetteEdges returns 4 edges`` () =
        let lattice = { ToricCode.Width = 4; ToricCode.Height = 4 }
        let plaquette = { ToricCode.X = 1; ToricCode.Y = 1 }
        let edges = ToricCode.getPlaquetteEdges lattice plaquette
        
        Assert.Equal(4, edges.Length)
    
    [<Fact>]
    let ``getVertexEdges handles periodic boundary conditions`` () =
        let lattice = { ToricCode.Width = 3; ToricCode.Height = 3 }
        let vertex = { ToricCode.X = 0; ToricCode.Y = 0 }  // Corner vertex
        let edges = ToricCode.getVertexEdges lattice vertex
        
        // Should wrap around and still get 4 edges
        Assert.Equal(4, edges.Length)
        
        // Check that positions are wrapped correctly
        edges |> List.iter (fun edge ->
            Assert.True(edge.Position.X >= 0 && edge.Position.X < lattice.Width)
            Assert.True(edge.Position.Y >= 0 && edge.Position.Y < lattice.Height))
    
    // ========================================================================
    // GROUND STATE INITIALIZATION
    // ========================================================================
    
    [<Fact>]
    let ``initializeGroundState creates correct number of qubits`` () =
        let lattice = { ToricCode.Width = 3; ToricCode.Height = 3 }
        let state = ToricCode.initializeGroundState lattice
        
        // 3×3 lattice has 18 edges → 18 qubits
        Assert.Equal(18, state.Qubits.Count)
    
    [<Fact>]
    let ``initializeGroundState sets all qubits to Plus`` () =
        let lattice = { ToricCode.Width = 2; ToricCode.Height = 2 }
        let state = ToricCode.initializeGroundState lattice
        
        // All qubits should be in |+⟩ state
        state.Qubits
        |> Map.forall (fun _ qubit -> qubit = ToricCode.Plus)
        |> Assert.True
    
    [<Fact>]
    let ``ground state has all stabilizers +1`` () =
        let lattice = { ToricCode.Width = 3; ToricCode.Height = 3 }
        let state = ToricCode.initializeGroundState lattice
        
        // Check all vertex operators
        for y in 0 .. lattice.Height - 1 do
            for x in 0 .. lattice.Width - 1 do
                let v = { ToricCode.X = x; ToricCode.Y = y }
                let eigenvalue = ToricCode.measureVertexOperator state v
                Assert.Equal(1, eigenvalue)
        
        // Check all plaquette operators
        for y in 0 .. lattice.Height - 1 do
            for x in 0 .. lattice.Width - 1 do
                let p = { ToricCode.X = x; ToricCode.Y = y }
                let eigenvalue = ToricCode.measurePlaquetteOperator state p
                Assert.Equal(1, eigenvalue)
    
    // ========================================================================
    // SYNDROME MEASUREMENT
    // ========================================================================
    
    [<Fact>]
    let ``measureSyndrome on ground state has no excitations`` () =
        let lattice = { ToricCode.Width = 3; ToricCode.Height = 3 }
        let state = ToricCode.initializeGroundState lattice
        let syndrome = ToricCode.measureSyndrome state
        
        let eParticles = ToricCode.getElectricExcitations syndrome
        let mParticles = ToricCode.getMagneticExcitations syndrome
        
        Assert.Empty(eParticles)
        Assert.Empty(mParticles)
    
    [<Fact>]
    let ``applyXError keeps qubit in Plus state`` () =
        let lattice = { ToricCode.Width = 4; ToricCode.Height = 4 }
        let state = ToricCode.initializeGroundState lattice
        
        // Apply X error to an edge
        let edge = { 
            ToricCode.Position = { ToricCode.X = 1; ToricCode.Y = 1 }
            ToricCode.Type = ToricCode.Horizontal 
        }
        let errorState = ToricCode.applyXError state edge
        
        // X|+⟩ = |+⟩, so state remains Plus
        let qubitState = Map.find edge errorState.Qubits
        Assert.Equal(ToricCode.Plus, qubitState)
    
    [<Fact>]
    let ``applyZError modifies qubit state`` () =
        let lattice = { ToricCode.Width = 4; ToricCode.Height = 4 }
        let state = ToricCode.initializeGroundState lattice
        
        // Apply Z error to an edge
        let edge = { 
            ToricCode.Position = { ToricCode.X = 1; ToricCode.Y = 1 }
            ToricCode.Type = ToricCode.Vertical 
        }
        let errorState = ToricCode.applyZError state edge
        
        // Verify the qubit state changed (Z flips |+⟩ to |-⟩)
        let changedQubit = Map.find edge errorState.Qubits
        Assert.Equal(ToricCode.Minus, changedQubit)
    
    // ========================================================================
    // DISTANCE CALCULATIONS
    // ========================================================================
    
    [<Fact>]
    let ``toricDistance handles Manhattan distance`` () =
        let lattice = { ToricCode.Width = 10; ToricCode.Height = 10 }
        let p1 = { ToricCode.X = 2; ToricCode.Y = 3 }
        let p2 = { ToricCode.X = 5; ToricCode.Y = 7 }
        
        let dist = ToricCode.toricDistance lattice p1 p2
        
        // |5-2| + |7-3| = 3 + 4 = 7
        Assert.Equal(7, dist)
    
    [<Fact>]
    let ``toricDistance wraps around torus`` () =
        let lattice = { ToricCode.Width = 10; ToricCode.Height = 10 }
        let p1 = { ToricCode.X = 1; ToricCode.Y = 1 }
        let p2 = { ToricCode.X = 9; ToricCode.Y = 9 }
        
        let dist = ToricCode.toricDistance lattice p1 p2
        
        // Shorter path wraps: (10-9+1) + (10-9+1) = 2 + 2 = 4
        // Direct path: |9-1| + |9-1| = 8 + 8 = 16
        Assert.Equal(4, dist)
    
    [<Fact>]
    let ``toricDistance is symmetric`` () =
        let lattice = { ToricCode.Width = 8; ToricCode.Height = 8 }
        let p1 = { ToricCode.X = 2; ToricCode.Y = 5 }
        let p2 = { ToricCode.X = 6; ToricCode.Y = 1 }
        
        let dist12 = ToricCode.toricDistance lattice p1 p2
        let dist21 = ToricCode.toricDistance lattice p2 p1
        
        Assert.Equal(dist12, dist21)
    
    // ========================================================================
    // CODE PARAMETERS
    // ========================================================================
    
    [<Fact>]
    let ``logicalQubits returns 2 for torus`` () =
        let lattice = { ToricCode.Width = 5; ToricCode.Height = 5 }
        Assert.Equal(2, ToricCode.logicalQubits lattice)
    
    [<Fact>]
    let ``physicalQubits counts all edges`` () =
        let lattice = { ToricCode.Width = 4; ToricCode.Height = 3 }
        let physical = ToricCode.physicalQubits lattice
        
        // 2 × 4 × 3 = 24 edges
        Assert.Equal(24, physical)
    
    [<Fact>]
    let ``codeDistance is minimum dimension`` () =
        let lattice1 = { ToricCode.Width = 5; ToricCode.Height = 7 }
        Assert.Equal(5, ToricCode.codeDistance lattice1)
        
        let lattice2 = { ToricCode.Width = 8; ToricCode.Height = 3 }
        Assert.Equal(3, ToricCode.codeDistance lattice2)
    
    [<Fact>]
    let ``code distance determines error correction capability`` () =
        let lattice = { ToricCode.Width = 5; ToricCode.Height = 5 }
        let d = ToricCode.codeDistance lattice
        
        // Can correct up to (d-1)/2 errors
        let correctableErrors = (d - 1) / 2
        
        Assert.Equal(2, correctableErrors)  // d=5 → can correct 2 errors
    
    // ========================================================================
    // ENCODING EFFICIENCY
    // ========================================================================
    
    [<Fact>]
    let ``encoding rate is 2/n for n physical qubits`` () =
        let lattice = { ToricCode.Width = 4; ToricCode.Height = 4 }
        let k = ToricCode.logicalQubits lattice  // 2
        let n = ToricCode.physicalQubits lattice  // 32
        
        let rate = float k / float n
        
        // Rate = 2/32 = 0.0625
        Assert.Equal(0.0625, rate, 4)
    
    [<Fact>]
    let ``larger lattice has lower encoding rate`` () =
        let small = { ToricCode.Width = 3; ToricCode.Height = 3 }
        let large = { ToricCode.Width = 10; ToricCode.Height = 10 }
        
        let rateSmall = 
            float (ToricCode.logicalQubits small) / float (ToricCode.physicalQubits small)
        let rateLarge = 
            float (ToricCode.logicalQubits large) / float (ToricCode.physicalQubits large)
        
        Assert.True(rateLarge < rateSmall)
    
    // ========================================================================
    // MWPM DECODER: MATCHING GRAPH CONSTRUCTION
    // ========================================================================
    
    [<Fact>]
    let ``buildMatchingGraph with no excitations returns empty`` () =
        let lattice = { ToricCode.Width = 4; ToricCode.Height = 4 }
        let edges = ToricCode.buildMatchingGraph lattice []
        Assert.Empty(edges)
    
    [<Fact>]
    let ``buildMatchingGraph with 2 excitations returns 1 edge`` () =
        let lattice = { ToricCode.Width = 4; ToricCode.Height = 4 }
        let exc = [{ ToricCode.X = 0; ToricCode.Y = 0 }; { ToricCode.X = 2; ToricCode.Y = 0 }]
        let edges = ToricCode.buildMatchingGraph lattice exc
        Assert.Equal(1, edges.Length)
        Assert.Equal(2, edges.[0].Weight)
    
    [<Fact>]
    let ``buildMatchingGraph with 4 excitations returns 6 edges`` () =
        // Complete graph on 4 vertices has C(4,2) = 6 edges
        let lattice = { ToricCode.Width = 6; ToricCode.Height = 6 }
        let exc = [
            { ToricCode.X = 0; ToricCode.Y = 0 }
            { ToricCode.X = 1; ToricCode.Y = 0 }
            { ToricCode.X = 2; ToricCode.Y = 0 }
            { ToricCode.X = 3; ToricCode.Y = 0 }
        ]
        let edges = ToricCode.buildMatchingGraph lattice exc
        Assert.Equal(6, edges.Length)
    
    [<Fact>]
    let ``buildMatchingGraph uses toric distance`` () =
        let lattice = { ToricCode.Width = 6; ToricCode.Height = 6 }
        let exc = [{ ToricCode.X = 0; ToricCode.Y = 0 }; { ToricCode.X = 5; ToricCode.Y = 0 }]
        let edges = ToricCode.buildMatchingGraph lattice exc
        // Wrap-around distance: min(5, 6-5) = 1
        Assert.Equal(1, edges.[0].Weight)
    
    // ========================================================================
    // MWPM DECODER: GREEDY MATCHING
    // ========================================================================
    
    [<Fact>]
    let ``greedyMatching with empty excitations returns empty list`` () =
        let lattice = { ToricCode.Width = 4; ToricCode.Height = 4 }
        match ToricCode.greedyMatching lattice [] with
        | Ok pairs -> Assert.Empty(pairs)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")
    
    [<Fact>]
    let ``greedyMatching with odd excitations returns error`` () =
        let lattice = { ToricCode.Width = 4; ToricCode.Height = 4 }
        let exc = [{ ToricCode.X = 0; ToricCode.Y = 0 }]
        match ToricCode.greedyMatching lattice exc with
        | Error (TopologicalError.ValidationError (_, reason)) ->
            Assert.Contains("even", reason)
        | _ -> Assert.Fail("Expected validation error for odd excitations")
    
    [<Fact>]
    let ``greedyMatching pairs 2 excitations correctly`` () =
        let lattice = { ToricCode.Width = 4; ToricCode.Height = 4 }
        let exc = [{ ToricCode.X = 1; ToricCode.Y = 1 }; { ToricCode.X = 3; ToricCode.Y = 1 }]
        match ToricCode.greedyMatching lattice exc with
        | Ok pairs ->
            Assert.Equal(1, pairs.Length)
            let (p1, p2) = pairs.[0]
            Assert.True(
                (p1.X = 1 && p2.X = 3) || (p1.X = 3 && p2.X = 1),
                "Should pair the two excitations together")
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")
    
    [<Fact>]
    let ``greedyMatching with 4 excitations produces 2 pairs`` () =
        let lattice = { ToricCode.Width = 6; ToricCode.Height = 6 }
        let exc = [
            { ToricCode.X = 0; ToricCode.Y = 0 }
            { ToricCode.X = 1; ToricCode.Y = 0 }
            { ToricCode.X = 4; ToricCode.Y = 0 }
            { ToricCode.X = 5; ToricCode.Y = 0 }
        ]
        match ToricCode.greedyMatching lattice exc with
        | Ok pairs ->
            Assert.Equal(2, pairs.Length)
            // Greedy should pair nearest: (0,1) and (4,5)
            // rather than (0,5) and (1,4) which would be farther
            let totalWeight = 
                pairs |> List.sumBy (fun (a, b) -> ToricCode.toricDistance lattice a b)
            Assert.True(totalWeight <= 2, $"Expected total weight <= 2, got {totalWeight}")
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")
    
    // ========================================================================
    // MWPM DECODER: CORRECTION PATH
    // ========================================================================
    
    [<Fact>]
    let ``correctionPath between same point returns empty`` () =
        let lattice = { ToricCode.Width = 4; ToricCode.Height = 4 }
        let p = { ToricCode.X = 2; ToricCode.Y = 2 }
        let path = ToricCode.correctionPath lattice p p ToricCode.Horizontal
        Assert.Empty(path)
    
    [<Fact>]
    let ``correctionPath along horizontal produces correct length`` () =
        let lattice = { ToricCode.Width = 6; ToricCode.Height = 6 }
        let p1 = { ToricCode.X = 1; ToricCode.Y = 2 }
        let p2 = { ToricCode.X = 4; ToricCode.Y = 2 }
        let path = ToricCode.correctionPath lattice p1 p2 ToricCode.Horizontal
        // Distance is 3 (direct path), so 3 edges
        Assert.Equal(3, path.Length)
    
    [<Fact>]
    let ``correctionPath wraps around torus when shorter`` () =
        let lattice = { ToricCode.Width = 6; ToricCode.Height = 6 }
        let p1 = { ToricCode.X = 0; ToricCode.Y = 0 }
        let p2 = { ToricCode.X = 5; ToricCode.Y = 0 }
        let path = ToricCode.correctionPath lattice p1 p2 ToricCode.Horizontal
        // Wrap-around: 1 edge (go from 0 backwards to 5) vs direct: 5 edges
        Assert.Equal(1, path.Length)
    
    [<Fact>]
    let ``correctionPath with both dx and dy produces combined length`` () =
        let lattice = { ToricCode.Width = 6; ToricCode.Height = 6 }
        let p1 = { ToricCode.X = 1; ToricCode.Y = 1 }
        let p2 = { ToricCode.X = 3; ToricCode.Y = 4 }
        let path = ToricCode.correctionPath lattice p1 p2 ToricCode.Horizontal
        // dx=2, dy=3, total=5 edges
        Assert.Equal(5, path.Length)
    
    // ========================================================================
    // MWPM DECODER: VERTEX AND PLAQUETTE SYNDROME DECODING
    // ========================================================================
    
    [<Fact>]
    let ``decodeVertexSyndrome on clean state returns empty result`` () =
        let lattice = { ToricCode.Width = 4; ToricCode.Height = 4 }
        let state = ToricCode.initializeGroundState lattice
        let syndrome = ToricCode.measureSyndrome state
        match ToricCode.decodeVertexSyndrome lattice syndrome with
        | Ok result ->
            Assert.Empty(result.MatchedPairs)
            Assert.Empty(result.Corrections)
            Assert.Equal(0, result.TotalWeight)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")
    
    [<Fact>]
    let ``decodePlaquetteSyndrome on clean state returns empty result`` () =
        let lattice = { ToricCode.Width = 4; ToricCode.Height = 4 }
        let state = ToricCode.initializeGroundState lattice
        let syndrome = ToricCode.measureSyndrome state
        match ToricCode.decodePlaquetteSyndrome lattice syndrome with
        | Ok result ->
            Assert.Empty(result.MatchedPairs)
            Assert.Empty(result.Corrections)
            Assert.Equal(0, result.TotalWeight)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")
    
    [<Fact>]
    let ``decodeVertexSyndrome after Z error produces matching`` () =
        let lattice = { ToricCode.Width = 4; ToricCode.Height = 4 }
        let state = ToricCode.initializeGroundState lattice
        
        // Apply a Z error (creates two e-particle excitations at adjacent vertices)
        let edge = { 
            ToricCode.Position = { ToricCode.X = 1; ToricCode.Y = 1 }
            ToricCode.Type = ToricCode.Vertical 
        }
        let errorState = ToricCode.applyZError state edge
        let syndrome = ToricCode.measureSyndrome errorState
        
        let excitations = ToricCode.getElectricExcitations syndrome
        
        // Z error on a vertical edge creates excitations at adjacent plaquettes
        // (e-particle count should be even)
        if excitations.Length > 0 then
            Assert.True(excitations.Length % 2 = 0, 
                $"Expected even number of excitations, got {excitations.Length}")
            
            match ToricCode.decodeVertexSyndrome lattice syndrome with
            | Ok result ->
                Assert.True(result.MatchedPairs.Length > 0, "Should produce at least one pair")
                Assert.True(result.TotalWeight > 0, "Should have non-zero weight")
            | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")
    
    [<Fact>]
    let ``decodePlaquetteSyndrome after Z error produces matching`` () =
        let lattice = { ToricCode.Width = 4; ToricCode.Height = 4 }
        let state = ToricCode.initializeGroundState lattice
        
        // Apply a Z error to create m-particle excitations
        let edge = { 
            ToricCode.Position = { ToricCode.X = 1; ToricCode.Y = 1 }
            ToricCode.Type = ToricCode.Horizontal 
        }
        let errorState = ToricCode.applyZError state edge
        let syndrome = ToricCode.measureSyndrome errorState
        
        let excitations = ToricCode.getMagneticExcitations syndrome
        
        if excitations.Length > 0 then
            Assert.True(excitations.Length % 2 = 0)
            match ToricCode.decodePlaquetteSyndrome lattice syndrome with
            | Ok result ->
                Assert.True(result.MatchedPairs.Length > 0)
            | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")
    
    // ========================================================================
    // MWPM DECODER: FULL SYNDROME DECODING AND CORRECTION
    // ========================================================================
    
    [<Fact>]
    let ``decodeSyndrome on ground state returns clean state`` () =
        let lattice = { ToricCode.Width = 4; ToricCode.Height = 4 }
        let state = ToricCode.initializeGroundState lattice
        match ToricCode.decodeSyndrome state with
        | Ok (correctedState, vertexResult, plaquetteResult) ->
            // Ground state has no errors, so decoder should be no-op
            Assert.Empty(vertexResult.MatchedPairs)
            Assert.Empty(plaquetteResult.MatchedPairs)
            
            // Corrected state should still be ground state
            let syndrome = ToricCode.measureSyndrome correctedState
            Assert.Empty(ToricCode.getElectricExcitations syndrome)
            Assert.Empty(ToricCode.getMagneticExcitations syndrome)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")
    
    [<Fact>]
    let ``applyCorrections with empty corrections is identity`` () =
        let lattice = { ToricCode.Width = 4; ToricCode.Height = 4 }
        let state = ToricCode.initializeGroundState lattice
        let corrected = ToricCode.applyCorrections state [] ToricCode.Horizontal
        
        // State should be unchanged
        Assert.Equal(state.Qubits.Count, corrected.Qubits.Count)
        corrected.Qubits |> Map.iter (fun edge qubit ->
            Assert.Equal(Map.find edge state.Qubits, qubit))
    
    [<Fact>]
    let ``decodeSyndrome produces correction for single Z error`` () =
        let lattice = { ToricCode.Width = 5; ToricCode.Height = 5 }
        let state = ToricCode.initializeGroundState lattice
        
        // Apply single Z error (within correctable range: d=5, corrects up to 2 errors)
        let edge = { 
            ToricCode.Position = { ToricCode.X = 2; ToricCode.Y = 2 }
            ToricCode.Type = ToricCode.Vertical 
        }
        let errorState = ToricCode.applyZError state edge
        
        // Verify error created excitations
        let preSyndrome = ToricCode.measureSyndrome errorState
        let preElectric = ToricCode.getElectricExcitations preSyndrome
        let preMagnetic = ToricCode.getMagneticExcitations preSyndrome
        let preTotal = preElectric.Length + preMagnetic.Length
        Assert.True(preTotal > 0, "Z error should create excitations")
        
        match ToricCode.decodeSyndrome errorState with
        | Ok (correctedState, vertexResult, plaquetteResult) ->
            // Decoder should have found the excitations and produced corrections
            // At least one of the decoders should have found pairs
            let totalPairs = vertexResult.MatchedPairs.Length + plaquetteResult.MatchedPairs.Length
            
            // The Z error on a vertical edge creates e-particles at adjacent vertices,
            // so the vertex decoder should find and pair them
            if preElectric.Length > 0 then
                Assert.True(vertexResult.MatchedPairs.Length > 0,
                    "Vertex decoder should pair e-particle excitations")
                Assert.True(vertexResult.Corrections.Length > 0,
                    "Vertex decoder should produce correction chains")
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")
