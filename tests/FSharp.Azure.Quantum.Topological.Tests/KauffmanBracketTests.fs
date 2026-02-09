module FSharp.Azure.Quantum.Topological.Tests.KauffmanBracketTests

open Xunit
open FSharp.Azure.Quantum.Topological.KauffmanBracket
open System.Numerics

// ========================================
// Helper Functions
// ========================================

/// Assert two complex numbers are approximately equal (within tolerance)
let assertComplexEqual (expected: Complex) (actual: Complex) (tolerance: float) =
    let diffReal = abs (expected.Real - actual.Real)
    let diffImag = abs (expected.Imaginary - actual.Imaginary)
    Assert.True(
        diffReal < tolerance && diffImag < tolerance,
        sprintf "Expected: %A, Actual: %A, Tolerance: %f" expected actual tolerance
    )

/// Get standard A value for testing (exp(i*pi/4))
let standardA = Complex(System.Math.Cos(System.Math.PI / 4.0), System.Math.Sin(System.Math.PI / 4.0))

/// Calculate expected d value: d = -A^2 - A^(-2)
let expectedD (a: Complex) : Complex =
    -(a * a) - (Complex.One / (a * a))

// ========================================
// TDD Cycle 1: Unknot (Simple Loop)
// ========================================

[<Fact>]
let ``Unknot has Kauffman bracket value 1`` () =
    // Arrange
    let a = standardA
    
    // Act
    let actualValue = evaluateBracket unknot a
    
    // Assert - unknot (empty diagram) evaluates to 1, not d
    assertComplexEqual Complex.One actualValue 1e-10

[<Fact>]
let ``Loop value function returns correct d`` () =
    // Arrange
    let a = standardA
    let expected = expectedD a
    
    // Act
    let actual = loopValue a
    
    // Assert
    assertComplexEqual expected actual 1e-10

[<Fact>]
let ``Unknot constructor creates empty diagram`` () =
    // Arrange & Act
    let knot = unknot
    
    // Assert
    Assert.Empty(knot)

// ========================================
// TDD Cycle 2: Writhe Calculation
// ========================================

[<Fact>]
let ``Unknot has writhe zero`` () =
    // Arrange & Act
    let w = writhe unknot
    
    // Assert
    Assert.Equal(0, w)

[<Fact>]
let ``Right-handed trefoil has writhe +3`` () =
    // Arrange
    let knot = trefoil true
    
    // Act
    let w = writhe knot
    
    // Assert
    Assert.Equal(3, w)

[<Fact>]
let ``Left-handed trefoil has writhe -3`` () =
    // Arrange
    let knot = trefoil false
    
    // Act
    let w = writhe knot
    
    // Assert
    Assert.Equal(-3, w)

[<Fact>]
let ``Figure-eight knot has writhe zero`` () =
    // Arrange & Act
    let w = writhe figureEight
    
    // Assert
    Assert.Equal(0, w)

// ========================================
// TDD Cycle 3: Trefoil Knot
// ========================================

[<Fact>]
let ``Trefoil constructor creates 3 crossings`` () =
    // Arrange & Act
    let knot = trefoil true
    
    // Assert
    Assert.Equal(3, List.length knot)

[<Fact>]
let ``Right-handed trefoil has all positive crossings`` () =
    // Arrange & Act
    let knot = trefoil true
    
    // Assert
    Assert.All(knot, fun crossing ->
        match crossing with
        | Positive -> ()
        | Negative -> Assert.Fail("Expected positive crossing"))

[<Fact>]
let ``Left-handed trefoil has all negative crossings`` () =
    // Arrange & Act
    let knot = trefoil false
    
    // Assert
    Assert.All(knot, fun crossing ->
        match crossing with
        | Negative -> ()
        | Positive -> Assert.Fail("Expected negative crossing"))

[<Fact>]
let ``Trefoil Kauffman bracket is non-zero`` () =
    // Arrange
    let knot = trefoil true
    let a = standardA
    
    // Act
    let bracket = evaluateBracket knot a
    
    // Assert
    Assert.NotEqual(Complex.Zero, bracket)

[<Fact>]
let ``Left and right trefoils have different Kauffman brackets`` () =
    // Arrange
    let rightTrefoil = trefoil true
    let leftTrefoil = trefoil false
    let a = standardA
    
    // Act
    let rightBracket = evaluateBracket rightTrefoil a
    let leftBracket = evaluateBracket leftTrefoil a
    
    // Assert - they should be complex conjugates or at least different
    Assert.NotEqual(rightBracket, leftBracket)

// ========================================
// TDD Cycle 4: Jones Polynomial
// ========================================

[<Fact>]
let ``Jones polynomial of unknot equals 1 at standard value`` () =
    // Arrange
    let a = standardA
    let w = writhe unknot
    let bracket = evaluateBracket unknot a
    
    // Expected: (-A)^(-3*0) * bracket = 1 * d = d
    let expected = bracket
    
    // Act
    let actual = jonesPolynomial unknot a
    
    // Assert
    assertComplexEqual expected actual 1e-10

[<Fact>]
let ``Jones polynomial incorporates writhe normalization`` () =
    // Arrange
    let knot = trefoil true  // writhe = +3
    let a = standardA
    
    // Act
    let jones = jonesPolynomial knot a
    let bracket = evaluateBracket knot a
    
    // Expected: (-A)^(-9) * bracket
    let expectedNorm = Complex.Pow(-a, -9.0)
    let expected = expectedNorm * bracket
    
    // Assert
    assertComplexEqual expected jones 1e-10

[<Fact>]
let ``Jones polynomial of trefoil is well-defined`` () =
    // Arrange
    let knot = trefoil true
    let a = standardA
    
    // Act
    let jones = jonesPolynomial knot a
    
    // Assert
    Assert.NotEqual(Complex.Zero, jones)
    Assert.False(System.Double.IsNaN(jones.Real))
    Assert.False(System.Double.IsNaN(jones.Imaginary))

// ========================================
// TDD Cycle 5: Standard TQFT Values
// ========================================

[<Fact>]
let ``Ising evaluation uses correct A value`` () =
    // Arrange
    let knot = unknot
    let expectedA = Complex(System.Math.Cos(System.Math.PI / 4.0), System.Math.Sin(System.Math.PI / 4.0))
    let expectedBracket = evaluateBracket knot expectedA
    
    // Act
    let actualBracket = evaluateIsing knot
    
    // Assert
    assertComplexEqual expectedBracket actualBracket 1e-10

[<Fact>]
let ``Fibonacci evaluation returns complex number`` () =
    // Arrange
    let knot = trefoil true
    
    // Act
    let bracket = evaluateFibonacci knot
    
    // Assert
    Assert.NotEqual(Complex.Zero, bracket)
    Assert.False(System.Double.IsNaN(bracket.Real))
    Assert.False(System.Double.IsNaN(bracket.Imaginary))

[<Fact>]
let ``Jones at t=-1 is well-defined for trefoil`` () =
    // Arrange
    let knot = trefoil true
    
    // Act
    let jones = evaluateJonesAtMinusOne knot
    
    // Assert
    Assert.NotEqual(Complex.Zero, jones)
    Assert.False(System.Double.IsNaN(jones.Real))
    Assert.False(System.Double.IsNaN(jones.Imaginary))

// ========================================
// TDD Cycle 6: Figure-Eight Knot
// ========================================

[<Fact>]
let ``Figure-eight constructor creates 4 crossings`` () =
    // Arrange & Act
    let knot = figureEight
    
    // Assert
    Assert.Equal(4, List.length knot)

[<Fact>]
let ``Figure-eight has alternating crossings`` () =
    // Arrange & Act
    let knot = figureEight
    
    // Assert
    Assert.Equal<KnotDiagram>([Positive; Negative; Positive; Negative], knot)

[<Fact>]
let ``Figure-eight Kauffman bracket differs from trefoil`` () =
    // Arrange
    let fig8 = figureEight
    let tref = trefoil true
    let a = standardA
    
    // Act
    let fig8Bracket = evaluateBracket fig8 a
    let trefBracket = evaluateBracket tref a
    
    // Assert
    Assert.NotEqual(fig8Bracket, trefBracket)

// ========================================
// Property-Based Tests
// ========================================

[<Fact>]
let ``Kauffman bracket is non-zero for all standard knots`` () =
    // Arrange
    let knots = [unknot; trefoil true; trefoil false; figureEight; hopfLink]
    let a = standardA
    
    // Act & Assert
    for knot in knots do
        let bracket = evaluateBracket knot a
        Assert.NotEqual(Complex.Zero, bracket)

[<Fact>]
let ``Writhe is additive for concatenated diagrams`` () =
    // Arrange
    let diagram1 = [Positive; Negative]
    let diagram2 = [Positive; Positive]
    let combined = diagram1 @ diagram2
    
    // Act
    let w1 = writhe diagram1
    let w2 = writhe diagram2
    let wCombined = writhe combined
    
    // Assert
    Assert.Equal(w1 + w2, wCombined)
