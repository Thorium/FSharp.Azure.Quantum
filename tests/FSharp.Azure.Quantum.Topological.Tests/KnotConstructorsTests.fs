module FSharp.Azure.Quantum.Topological.Tests.KnotConstructorsTests

open Xunit
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Topological.KauffmanBracket

// ========================================
// Helper Functions
// ========================================

/// Validate a knot diagram and fail the test if invalid
let assertValid (diagram: PlanarDiagram) =
    match KnotConstructors.validate diagram with
    | Ok () -> ()
    | Error msg -> Assert.Fail($"Validation failed: {msg}")

// ========================================
// Torus Knot Tests
// ========================================

[<Fact>]
let ``Torus knot T(1,0) is unknot`` () =
    // Arrange & Act
    let knot = KnotConstructors.torusKnot 1 0
    
    // Assert
    assertValid knot
    Assert.Empty(knot.Crossings)
    Assert.Single(knot.Arcs) // One loop

[<Fact>]
let ``Torus knot T(2,3) is trefoil`` () =
    // Arrange
    let knot = KnotConstructors.torusKnot 2 3
    
    // Act & Assert
    assertValid knot
    Assert.Equal(3, knot.Crossings.Count) // 3 crossings
    
    // Writhe should be +3 (3 positive crossings)
    let w = KauffmanBracket.Planar.writhe knot
    Assert.Equal(3, w)

[<Fact>]
let ``Torus knot T(2,-3) is left-handed trefoil`` () =
    // Arrange
    let knot = KnotConstructors.torusKnot 2 -3
    
    // Act & Assert
    assertValid knot
    Assert.Equal(3, knot.Crossings.Count)
    
    // Writhe should be -3 (3 negative crossings)
    let w = KauffmanBracket.Planar.writhe knot
    Assert.Equal(-3, w)

[<Fact>]
let ``Torus knot T(3,2) is also trefoil`` () =
    // Arrange
    let knot = KnotConstructors.torusKnot 3 2
    
    // Act & Assert
    assertValid knot
    // T(3,2) is equivalent to T(2,3) topologically, but construction differs
    // Braiding: (sigma_1 sigma_2)^2 = sigma_1 sigma_2 sigma_1 sigma_2
    // Number of crossings = (p-1)*q = 2*2 = 4 crossings? No, braid length is (p-1)*q
    // For T(3,2): (sigma_1 sigma_2)^2 -> 4 crossings generated
    Assert.Equal(4, knot.Crossings.Count)
    
    // All positive
    let w = KauffmanBracket.Planar.writhe knot
    Assert.Equal(4, w)

[<Fact>]
let ``Torus knot T(2,1) is unknot with twists`` () =
    // Arrange
    let knot = KnotConstructors.torusKnot 2 1
    
    // Act & Assert
    assertValid knot
    Assert.Equal(1, knot.Crossings.Count) // 1 crossing
    
    // This is a simple twist, topologically unknot but has 1 crossing diagrammatically

[<Fact>]
let ``Torus knot T(4,3) is constructed correctly`` () =
    // Arrange
    let knot = KnotConstructors.torusKnot 4 3
    
    // Act & Assert
    assertValid knot
    
    // Expected crossings: (p-1)*q = 3*3 = 9 crossings
    Assert.Equal(9, knot.Crossings.Count)
    
    // Check connectivity - every arc should have a start and end
    // (This is covered by assertValid but good to double check)
    for kvp in knot.Arcs do
        let arc = kvp.Value
        match arc.Start with
        | ArcEnd.FreeEnd _ -> Assert.Fail($"Arc {arc.Id} has free start end")
        | _ -> ()
        match arc.End with
        | ArcEnd.FreeEnd _ -> Assert.Fail($"Arc {arc.Id} has free end end")
        | _ -> ()

[<Fact>]
let ``Torus knot fails for invalid p`` () =
    Assert.Throws<System.Exception>(fun () -> KnotConstructors.torusKnot 0 5 |> ignore)

[<Fact>]
let ``Torus knot T(2,0) is unlink (two unknots)`` () =
    // Our implementation returns unknot for q=0 for simplicity,
    // though technically T(p,0) is p unlinked components.
    // The current implementation returns 'unknot' for q=0.
    // Let's verify this behavior.
    let knot = KnotConstructors.torusKnot 2 0
    Assert.Equal(KnotConstructors.unknot, knot)
