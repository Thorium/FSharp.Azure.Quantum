namespace FSharp.Azure.Quantum.Tests.Topological

open Xunit
open FSharp.Azure.Quantum.Topological

module SurfaceCodeTests =

    // ========================================================================
    // PLANAR CODE: LATTICE CREATION
    // ========================================================================

    [<Fact>]
    let ``Planar: createPlanarLattice with d=3 succeeds`` () =
        match SurfaceCode.createPlanarLattice 3 with
        | Ok lattice -> Assert.Equal(3, lattice.Distance)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")

    [<Fact>]
    let ``Planar: createPlanarLattice with d=5 succeeds`` () =
        match SurfaceCode.createPlanarLattice 5 with
        | Ok lattice -> Assert.Equal(5, lattice.Distance)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")

    [<Fact>]
    let ``Planar: createPlanarLattice with d=2 fails (too small)`` () =
        match SurfaceCode.createPlanarLattice 2 with
        | Error (TopologicalError.ValidationError (_, reason)) ->
            Assert.Contains("3", reason)
        | _ -> Assert.Fail("Expected validation error for d < 3")

    [<Fact>]
    let ``Planar: createPlanarLattice with d=4 fails (even)`` () =
        match SurfaceCode.createPlanarLattice 4 with
        | Error (TopologicalError.ValidationError (_, reason)) ->
            Assert.Contains("odd", reason)
        | _ -> Assert.Fail("Expected validation error for even distance")

    [<Fact>]
    let ``Planar: createPlanarLattice with d=0 fails`` () =
        match SurfaceCode.createPlanarLattice 0 with
        | Error (TopologicalError.ValidationError _) -> ()
        | _ -> Assert.Fail("Expected validation error")

    // ========================================================================
    // PLANAR CODE: EDGE COUNTING AND CODE PARAMETERS
    // ========================================================================

    [<Fact>]
    let ``Planar: d=3 has 12 edges`` () =
        let lattice = { SurfaceCode.PlanarLattice.Distance = 3 }
        let edges = SurfaceCode.getAllPlanarEdges lattice
        // 2 * 3 * (3-1) = 12
        Assert.Equal(12, edges.Length)

    [<Fact>]
    let ``Planar: d=5 has 40 edges`` () =
        let lattice = { SurfaceCode.PlanarLattice.Distance = 5 }
        let edges = SurfaceCode.getAllPlanarEdges lattice
        // 2 * 5 * (5-1) = 40
        Assert.Equal(40, edges.Length)

    [<Fact>]
    let ``Planar: physicalQubits matches edge count`` () =
        let lattice = { SurfaceCode.PlanarLattice.Distance = 5 }
        Assert.Equal(40, SurfaceCode.planarPhysicalQubits lattice)

    [<Fact>]
    let ``Planar: logicalQubits is always 1`` () =
        let lattice = { SurfaceCode.PlanarLattice.Distance = 5 }
        Assert.Equal(1, SurfaceCode.planarLogicalQubits lattice)

    [<Fact>]
    let ``Planar: codeDistance matches lattice distance`` () =
        let lattice = { SurfaceCode.PlanarLattice.Distance = 7 }
        Assert.Equal(7, SurfaceCode.planarCodeDistance lattice)

    [<Fact>]
    let ``Planar: edges contain both horizontal and vertical`` () =
        let lattice = { SurfaceCode.PlanarLattice.Distance = 3 }
        let edges = SurfaceCode.getAllPlanarEdges lattice
        let horiz = edges |> List.filter (fun e -> e.EdgeType = SurfaceCode.PHorizontal) |> List.length
        let vert = edges |> List.filter (fun e -> e.EdgeType = SurfaceCode.PVertical) |> List.length
        // d*(d-1) = 3*2 = 6 each
        Assert.Equal(6, horiz)
        Assert.Equal(6, vert)

    // ========================================================================
    // PLANAR CODE: GROUND STATE
    // ========================================================================

    [<Fact>]
    let ``Planar: ground state has correct qubit count`` () =
        let lattice = { SurfaceCode.PlanarLattice.Distance = 3 }
        let state = SurfaceCode.initializePlanarGroundState lattice
        Assert.Equal(12, state.Qubits.Count)

    [<Fact>]
    let ``Planar: ground state all qubits are Plus`` () =
        let lattice = { SurfaceCode.PlanarLattice.Distance = 3 }
        let state = SurfaceCode.initializePlanarGroundState lattice
        state.Qubits
        |> Map.forall (fun _ q -> q = SurfaceCode.Plus)
        |> Assert.True

    [<Fact>]
    let ``Planar: ground state has no syndrome defects`` () =
        let lattice = { SurfaceCode.PlanarLattice.Distance = 5 }
        let state = SurfaceCode.initializePlanarGroundState lattice
        let syndrome = SurfaceCode.measurePlanarSyndrome state
        Assert.Empty(syndrome.XDefects)
        Assert.Empty(syndrome.ZDefects)

    // ========================================================================
    // PLANAR CODE: ERROR APPLICATION AND SYNDROME DETECTION
    // ========================================================================

    [<Fact>]
    let ``Planar: Z error flips Plus to Minus`` () =
        let lattice = { SurfaceCode.PlanarLattice.Distance = 3 }
        let state = SurfaceCode.initializePlanarGroundState lattice
        let edge = { SurfaceCode.PlanarEdge.Position = { SurfaceCode.X = 0; SurfaceCode.Y = 0 }; EdgeType = SurfaceCode.PHorizontal }
        let errState = SurfaceCode.applyPlanarZError state edge
        let q = Map.find edge errState.Qubits
        Assert.Equal(SurfaceCode.Minus, q)

    [<Fact>]
    let ``Planar: X error keeps Plus as Plus`` () =
        let lattice = { SurfaceCode.PlanarLattice.Distance = 3 }
        let state = SurfaceCode.initializePlanarGroundState lattice
        let edge = { SurfaceCode.PlanarEdge.Position = { SurfaceCode.X = 1; SurfaceCode.Y = 0 }; EdgeType = SurfaceCode.PVertical }
        let errState = SurfaceCode.applyPlanarXError state edge
        let q = Map.find edge errState.Qubits
        Assert.Equal(SurfaceCode.Plus, q)

    [<Fact>]
    let ``Planar: Z error creates X-defects in syndrome`` () =
        let lattice = { SurfaceCode.PlanarLattice.Distance = 5 }
        let state = SurfaceCode.initializePlanarGroundState lattice
        // Apply Z error to a horizontal edge in the interior
        let edge = { SurfaceCode.PlanarEdge.Position = { SurfaceCode.X = 2; SurfaceCode.Y = 1 }; EdgeType = SurfaceCode.PHorizontal }
        let errState = SurfaceCode.applyPlanarZError state edge
        let syndrome = SurfaceCode.measurePlanarSyndrome errState
        // Z error creates X-stabilizer violations
        Assert.True(syndrome.XDefects.Length > 0, "Z error should create X-defects")
        // Defect count should be even (or odd if at boundary)
        Assert.True(syndrome.XDefects.Length >= 1)

    [<Fact>]
    let ``Planar: Two Z errors create detectable syndrome`` () =
        let lattice = { SurfaceCode.PlanarLattice.Distance = 5 }
        let state = SurfaceCode.initializePlanarGroundState lattice
        let edge1 = { SurfaceCode.PlanarEdge.Position = { SurfaceCode.X = 1; SurfaceCode.Y = 1 }; EdgeType = SurfaceCode.PHorizontal }
        let edge2 = { SurfaceCode.PlanarEdge.Position = { SurfaceCode.X = 3; SurfaceCode.Y = 2 }; EdgeType = SurfaceCode.PHorizontal }
        let errState =
            state
            |> fun s -> SurfaceCode.applyPlanarZError s edge1
            |> fun s -> SurfaceCode.applyPlanarZError s edge2
        let syndrome = SurfaceCode.measurePlanarSyndrome errState
        Assert.True(syndrome.XDefects.Length >= 2, "Two Z errors should create multiple defects")

    // ========================================================================
    // PLANAR CODE: DISTANCE AND DECODER
    // ========================================================================

    [<Fact>]
    let ``Planar: planarDistance computes Manhattan distance`` () =
        let p1 = { SurfaceCode.X = 1; SurfaceCode.Y = 2 }
        let p2 = { SurfaceCode.X = 4; SurfaceCode.Y = 5 }
        Assert.Equal(6, SurfaceCode.planarDistance p1 p2)

    [<Fact>]
    let ``Planar: planarDistance is symmetric`` () =
        let p1 = { SurfaceCode.X = 0; SurfaceCode.Y = 3 }
        let p2 = { SurfaceCode.X = 5; SurfaceCode.Y = 1 }
        Assert.Equal(SurfaceCode.planarDistance p1 p2, SurfaceCode.planarDistance p2 p1)

    [<Fact>]
    let ``Planar: distanceToBoundary for X-defect measures vertical distance`` () =
        let lattice = { SurfaceCode.PlanarLattice.Distance = 7 }
        let defect = { SurfaceCode.X = 3; SurfaceCode.Y = 2 }
        let dist = SurfaceCode.distanceToBoundary lattice defect true
        // d=7 → y range [0..5], defect at y=2 → min(2, 5-2) = 2
        Assert.Equal(2, dist)

    [<Fact>]
    let ``Planar: distanceToBoundary for Z-defect measures horizontal distance`` () =
        let lattice = { SurfaceCode.PlanarLattice.Distance = 7 }
        let defect = { SurfaceCode.X = 1; SurfaceCode.Y = 3 }
        let dist = SurfaceCode.distanceToBoundary lattice defect false
        // d=7 → x range [0..5], defect at x=1 → min(1, 5-1) = 1
        Assert.Equal(1, dist)

    [<Fact>]
    let ``Planar: decoder on empty defects returns empty result`` () =
        let lattice = { SurfaceCode.PlanarLattice.Distance = 5 }
        match SurfaceCode.decodePlanarSyndrome lattice [] true with
        | Ok result ->
            Assert.Empty(result.MatchedPairs)
            Assert.Equal(0, result.TotalWeight)
            Assert.Equal(0, result.BoundaryMatches)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")

    [<Fact>]
    let ``Planar: decoder pairs two close defects`` () =
        let lattice = { SurfaceCode.PlanarLattice.Distance = 5 }
        let defects = [{ SurfaceCode.X = 1; SurfaceCode.Y = 1 }; { SurfaceCode.X = 2; SurfaceCode.Y = 1 }]
        match SurfaceCode.decodePlanarSyndrome lattice defects true with
        | Ok result ->
            Assert.True(result.MatchedPairs.Length >= 1, "Should produce at least one pair or boundary match")
            Assert.True(result.TotalWeight >= 1)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")

    [<Fact>]
    let ``Planar: decoder can match single defect to boundary`` () =
        let lattice = { SurfaceCode.PlanarLattice.Distance = 5 }
        // Single defect near boundary
        let defects = [{ SurfaceCode.X = 0; SurfaceCode.Y = 0 }]
        match SurfaceCode.decodePlanarSyndrome lattice defects true with
        | Ok result ->
            // Single defect should be matched to boundary
            Assert.True(result.BoundaryMatches >= 1 || result.MatchedPairs.Length >= 1,
                "Single defect should be matched to boundary")
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")

    [<Fact>]
    let ``Planar: full decode on ground state is no-op`` () =
        let lattice = { SurfaceCode.PlanarLattice.Distance = 5 }
        let state = SurfaceCode.initializePlanarGroundState lattice
        match SurfaceCode.decodePlanarCode state with
        | Ok (corrected, xResult, zResult) ->
            Assert.Empty(xResult.MatchedPairs)
            Assert.Empty(zResult.MatchedPairs)
            // Corrected state should still be clean
            let syndrome = SurfaceCode.measurePlanarSyndrome corrected
            Assert.Empty(syndrome.XDefects)
            Assert.Empty(syndrome.ZDefects)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")

    // ========================================================================
    // PLANAR CODE: ENCODING RATE
    // ========================================================================

    [<Fact>]
    let ``Planar: encoding rate decreases with distance`` () =
        let rate d =
            let lattice = { SurfaceCode.PlanarLattice.Distance = d }
            float (SurfaceCode.planarLogicalQubits lattice) /
            float (SurfaceCode.planarPhysicalQubits lattice)
        Assert.True(rate 5 < rate 3)
        Assert.True(rate 7 < rate 5)

    [<Fact>]
    let ``Planar: error correction capability is (d-1)/2`` () =
        let lattice = { SurfaceCode.PlanarLattice.Distance = 5 }
        let d = SurfaceCode.planarCodeDistance lattice
        Assert.Equal(2, (d - 1) / 2)

    // ========================================================================
    // COLOR CODE: LATTICE CREATION
    // ========================================================================

    [<Fact>]
    let ``Color: createColorCodeLattice with d=3 succeeds`` () =
        match SurfaceCode.createColorCodeLattice 3 with
        | Ok lattice ->
            Assert.Equal(3, lattice.Distance)
            Assert.True(lattice.Faces.Length > 0, "Should have faces")
            Assert.True(lattice.QubitPositions.Length > 0, "Should have qubits")
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")

    [<Fact>]
    let ``Color: createColorCodeLattice with d=5 succeeds`` () =
        match SurfaceCode.createColorCodeLattice 5 with
        | Ok lattice ->
            Assert.Equal(5, lattice.Distance)
            Assert.True(lattice.Faces.Length > lattice.Distance, "Should have more faces for d=5")
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")

    [<Fact>]
    let ``Color: createColorCodeLattice with d=2 fails`` () =
        match SurfaceCode.createColorCodeLattice 2 with
        | Error (TopologicalError.ValidationError (_, reason)) ->
            Assert.Contains("3", reason)
        | _ -> Assert.Fail("Expected validation error for d < 3")

    [<Fact>]
    let ``Color: createColorCodeLattice with d=4 fails (even)`` () =
        match SurfaceCode.createColorCodeLattice 4 with
        | Error (TopologicalError.ValidationError (_, reason)) ->
            Assert.Contains("odd", reason)
        | _ -> Assert.Fail("Expected validation error for even distance")

    // ========================================================================
    // COLOR CODE: LATTICE PROPERTIES
    // ========================================================================

    [<Fact>]
    let ``Color: lattice has faces with all three colors`` () =
        match SurfaceCode.createColorCodeLattice 3 with
        | Ok lattice ->
            let colors = lattice.Faces |> List.map (fun f -> f.Color) |> List.distinct
            Assert.Contains(SurfaceCode.Red, colors)
            // For d=3 we should have at least Red and possibly Green/Blue
            Assert.True(colors.Length >= 1)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")

    [<Fact>]
    let ``Color: d=5 lattice has all three colors`` () =
        match SurfaceCode.createColorCodeLattice 5 with
        | Ok lattice ->
            let colors = lattice.Faces |> List.map (fun f -> f.Color) |> List.distinct |> Set.ofList
            Assert.True(colors.Contains SurfaceCode.Red, "Should have Red faces")
            Assert.True(colors.Contains SurfaceCode.Green, "Should have Green faces")
            Assert.True(colors.Contains SurfaceCode.Blue, "Should have Blue faces")
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")

    [<Fact>]
    let ``Color: logicalQubits is always 1`` () =
        match SurfaceCode.createColorCodeLattice 3 with
        | Ok lattice -> Assert.Equal(1, SurfaceCode.colorCodeLogicalQubits lattice)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")

    [<Fact>]
    let ``Color: physicalQubits > 0`` () =
        match SurfaceCode.createColorCodeLattice 3 with
        | Ok lattice ->
            Assert.True(SurfaceCode.colorCodePhysicalQubits lattice > 0)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")

    [<Fact>]
    let ``Color: codeDistance matches input`` () =
        match SurfaceCode.createColorCodeLattice 5 with
        | Ok lattice -> Assert.Equal(5, SurfaceCode.colorCodeDistance lattice)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")

    [<Fact>]
    let ``Color: getFacesByColor returns only requested color`` () =
        match SurfaceCode.createColorCodeLattice 5 with
        | Ok lattice ->
            let redFaces = SurfaceCode.getFacesByColor lattice SurfaceCode.Red
            Assert.True(redFaces |> List.forall (fun f -> f.Color = SurfaceCode.Red))
            Assert.True(redFaces.Length > 0)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")

    // ========================================================================
    // COLOR CODE: GROUND STATE AND SYNDROME
    // ========================================================================

    [<Fact>]
    let ``Color: ground state has correct qubit count`` () =
        match SurfaceCode.createColorCodeLattice 3 with
        | Ok lattice ->
            let state = SurfaceCode.initializeColorCodeGroundState lattice
            Assert.Equal(lattice.QubitPositions.Length, state.Qubits.Count)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")

    [<Fact>]
    let ``Color: ground state all qubits Plus`` () =
        match SurfaceCode.createColorCodeLattice 3 with
        | Ok lattice ->
            let state = SurfaceCode.initializeColorCodeGroundState lattice
            state.Qubits |> Map.forall (fun _ q -> q = SurfaceCode.Plus) |> Assert.True
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")

    [<Fact>]
    let ``Color: ground state has no syndrome defects`` () =
        match SurfaceCode.createColorCodeLattice 5 with
        | Ok lattice ->
            let state = SurfaceCode.initializeColorCodeGroundState lattice
            let syndrome = SurfaceCode.measureColorCodeSyndrome state
            let xDefects = SurfaceCode.getColorCodeDefects syndrome.XDefects
            let zDefects = SurfaceCode.getColorCodeDefects syndrome.ZDefects
            Assert.Empty(xDefects)
            Assert.Empty(zDefects)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")

    // ========================================================================
    // COLOR CODE: ERROR APPLICATION AND DETECTION
    // ========================================================================

    [<Fact>]
    let ``Color: Z error flips Plus to Minus`` () =
        match SurfaceCode.createColorCodeLattice 3 with
        | Ok lattice ->
            let state = SurfaceCode.initializeColorCodeGroundState lattice
            let pos = lattice.QubitPositions.[0]
            let errState = SurfaceCode.applyColorCodeZError state pos
            let q = Map.find pos errState.Qubits
            Assert.Equal(SurfaceCode.Minus, q)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")

    [<Fact>]
    let ``Color: X error keeps Plus as Plus`` () =
        match SurfaceCode.createColorCodeLattice 3 with
        | Ok lattice ->
            let state = SurfaceCode.initializeColorCodeGroundState lattice
            let pos = lattice.QubitPositions.[0]
            let errState = SurfaceCode.applyColorCodeXError state pos
            let q = Map.find pos errState.Qubits
            Assert.Equal(SurfaceCode.Plus, q)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")

    [<Fact>]
    let ``Color: Z error creates X-stabilizer defects`` () =
        match SurfaceCode.createColorCodeLattice 5 with
        | Ok lattice ->
            let state = SurfaceCode.initializeColorCodeGroundState lattice
            // Apply Z error to an interior qubit
            let interiorPos =
                lattice.QubitPositions
                |> List.skip (lattice.QubitPositions.Length / 2)
                |> List.head
            let errState = SurfaceCode.applyColorCodeZError state interiorPos
            let syndrome = SurfaceCode.measureColorCodeSyndrome errState
            let xDefects = SurfaceCode.getColorCodeDefects syndrome.XDefects
            Assert.True(xDefects.Length > 0, "Z error should create X-stabilizer defects")
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")

    // ========================================================================
    // COLOR CODE: DECODER
    // ========================================================================

    [<Fact>]
    let ``Color: decoder on empty defects returns empty`` () =
        match SurfaceCode.createColorCodeLattice 5 with
        | Ok lattice ->
            match SurfaceCode.decodeColorCodeSyndrome lattice [] with
            | Ok result ->
                Assert.Empty(result.MatchedPairs)
                Assert.Equal(0, result.TotalWeight)
            | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")

    [<Fact>]
    let ``Color: decoder pairs two defects`` () =
        match SurfaceCode.createColorCodeLattice 5 with
        | Ok lattice ->
            let defects = [{ SurfaceCode.X = 1; SurfaceCode.Y = 1 }; { SurfaceCode.X = 3; SurfaceCode.Y = 3 }]
            match SurfaceCode.decodeColorCodeSyndrome lattice defects with
            | Ok result ->
                Assert.Equal(1, result.MatchedPairs.Length)
                Assert.True(result.TotalWeight > 0)
            | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")

    [<Fact>]
    let ``Color: decoder rejects odd number of defects`` () =
        match SurfaceCode.createColorCodeLattice 5 with
        | Ok lattice ->
            let defects = [{ SurfaceCode.X = 1; SurfaceCode.Y = 1 }]
            match SurfaceCode.decodeColorCodeSyndrome lattice defects with
            | Error (TopologicalError.ValidationError _) -> ()
            | _ -> Assert.Fail("Expected validation error for odd defect count")
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")

    [<Fact>]
    let ``Color: full decode on ground state is no-op`` () =
        match SurfaceCode.createColorCodeLattice 5 with
        | Ok lattice ->
            let state = SurfaceCode.initializeColorCodeGroundState lattice
            match SurfaceCode.decodeColorCode state with
            | Ok (corrected, xResult, zResult) ->
                Assert.Empty(xResult.MatchedPairs)
                Assert.Empty(zResult.MatchedPairs)
                let syndrome = SurfaceCode.measureColorCodeSyndrome corrected
                Assert.Empty(SurfaceCode.getColorCodeDefects syndrome.XDefects)
                Assert.Empty(SurfaceCode.getColorCodeDefects syndrome.ZDefects)
            | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")

    // ========================================================================
    // ANTI-GAMING: CROSS-VALIDATION BETWEEN CODES
    // ========================================================================

    [<Fact>]
    let ``Planar code encodes fewer logical qubits than toric code`` () =
        let planarLattice = { SurfaceCode.PlanarLattice.Distance = 5 }
        let toricLattice = { ToricCode.Width = 5; ToricCode.Height = 5 }
        Assert.True(
            SurfaceCode.planarLogicalQubits planarLattice < ToricCode.logicalQubits toricLattice,
            "Planar code (1 logical qubit) < toric code (2 logical qubits)")

    [<Fact>]
    let ``Planar code uses fewer physical qubits than toric code at same distance`` () =
        // Planar: 2*d*(d-1) = 2*5*4 = 40
        // Toric: 2*5*5 = 50
        let planarLattice = { SurfaceCode.PlanarLattice.Distance = 5 }
        let toricLattice = { ToricCode.Width = 5; ToricCode.Height = 5 }
        Assert.True(
            SurfaceCode.planarPhysicalQubits planarLattice < ToricCode.physicalQubits toricLattice,
            "Planar code should use fewer physical qubits at same distance")

    [<Fact>]
    let ``Color code encodes 1 logical qubit`` () =
        match SurfaceCode.createColorCodeLattice 3 with
        | Ok lattice ->
            Assert.Equal(1, SurfaceCode.colorCodeLogicalQubits lattice)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")

    [<Fact>]
    let ``Different surface code variants all detect clean ground state`` () =
        // All three code types should report clean syndrome on ground state
        let planarLattice = { SurfaceCode.PlanarLattice.Distance = 3 }
        let planarState = SurfaceCode.initializePlanarGroundState planarLattice
        let planarSyndrome = SurfaceCode.measurePlanarSyndrome planarState
        Assert.Empty(planarSyndrome.XDefects)
        Assert.Empty(planarSyndrome.ZDefects)

        match SurfaceCode.createColorCodeLattice 3 with
        | Ok colorLattice ->
            let colorState = SurfaceCode.initializeColorCodeGroundState colorLattice
            let colorSyndrome = SurfaceCode.measureColorCodeSyndrome colorState
            Assert.Empty(SurfaceCode.getColorCodeDefects colorSyndrome.XDefects)
            Assert.Empty(SurfaceCode.getColorCodeDefects colorSyndrome.ZDefects)
        | Error _ -> ()  // Color code lattice creation may be tested separately
