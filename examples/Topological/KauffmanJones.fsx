#!/usr/bin/env dotnet fsi
// ============================================================================
// Kauffman Bracket and Jones Polynomial Examples
// ============================================================================
// This script demonstrates knot invariant calculations using the Kauffman
// bracket and Jones polynomial, following Steven Simon's "Topological Quantum"
// (Chapter 2, pages 14-33).
//
// Key concepts:
// - Kauffman bracket: A polynomial invariant of framed knots/links
// - Jones polynomial: Normalized Kauffman bracket with writhe correction
// - TQFT evaluations: Special A values for topological field theories
// ============================================================================

#r "nuget: FSharp.Azure.Quantum"
#load "../../src/FSharp.Azure.Quantum.Topological/KauffmanBracket.fs"

open System
open System.Numerics
open FSharp.Azure.Quantum.Topological.KauffmanBracket

// ============================================================================
// Constants
// ============================================================================

/// Standard A value for Kauffman bracket calculations
/// Using A = exp(2πi/5) which gives non-trivial values
/// This corresponds to the Jones polynomial at q = exp(πi/5)
let standardA = Complex.FromPolarCoordinates(1.0, 2.0 * Math.PI / 5.0)

// ============================================================================
// Helper Functions
// ============================================================================

let printComplex (name: string) (value: Complex) =
    if Math.Abs(value.Imaginary) < 1e-10 then
        printfn "%s = %.6f" name value.Real
    else
        let sign = if value.Imaginary >= 0.0 then "+" else ""
        printfn "%s = %.6f %s %.6fi" name value.Real sign value.Imaginary

let printSeparator () =
    printfn "%s" (String.replicate 70 "=")

// ============================================================================
// Example 1: Unknot - The Simplest Case
// ============================================================================

printfn "Example 1: Unknot (Simple Loop)"
printSeparator ()

let unknotDiagram = unknot
printfn "Diagram: %A" unknotDiagram
printfn "Number of crossings: %d" (List.length unknotDiagram)

let a1 = standardA
printfn "\nUsing standard A value:"
printComplex "A" a1

let unknotBracket = evaluateBracket unknotDiagram a1
printComplex "Kauffman bracket" unknotBracket

let unknotWrithe = writhe unknotDiagram
printfn "Writhe: %d" unknotWrithe

let unknotJones = jonesPolynomial unknotDiagram a1
printComplex "Jones polynomial" unknotJones

printfn "\nExpected: Jones(unknot) = 1.0"
printfn ""

// ============================================================================
// Example 2: Trefoil Knot - Simplest Non-Trivial Knot
// ============================================================================

printfn "Example 2: Trefoil Knot (Right-Handed)"
printSeparator ()

let trefoilRight = trefoil true
printfn "Diagram: %A" trefoilRight
printfn "Number of crossings: %d" (List.length trefoilRight)

printfn "\nUsing standard A value:"
printComplex "A" a1

let trefoilBracket = evaluateBracket trefoilRight a1
printComplex "Kauffman bracket" trefoilBracket

let trefoilWrithe = writhe trefoilRight
printfn "Writhe: %d" trefoilWrithe

let trefoilJones = jonesPolynomial trefoilRight a1
printComplex "Jones polynomial" trefoilJones

printfn "\nNote: Trefoil has writhe +3, all positive crossings"
printfn ""

// ============================================================================
// Example 3: Mirror Symmetry - Left vs Right Trefoil
// ============================================================================

printfn "Example 3: Mirror Symmetry (Chirality)"
printSeparator ()

let trefoilLeft = trefoil false
printfn "Left-handed trefoil: %A" trefoilLeft
printfn "Right-handed trefoil: %A" trefoilRight

let leftBracket = evaluateBracket trefoilLeft a1
let rightBracket = evaluateBracket trefoilRight a1

printComplex "Left Kauffman bracket" leftBracket
printComplex "Right Kauffman bracket" rightBracket

let leftJones = jonesPolynomial trefoilLeft a1
let rightJones = jonesPolynomial trefoilRight a1

printComplex "Left Jones polynomial" leftJones
printComplex "Right Jones polynomial" rightJones

printfn "\nNote: Mirror knots have different invariants (trefoil is chiral)"
printfn ""

// ============================================================================
// Example 4: Figure-Eight Knot - Achiral Knot
// ============================================================================

printfn "Example 4: Figure-Eight Knot"
printSeparator ()

let figureEight = figureEight
printfn "Diagram: %A" figureEight
printfn "Number of crossings: %d" (List.length figureEight)

let fig8Writhe = writhe figureEight
printfn "Writhe: %d" fig8Writhe

let fig8Bracket = evaluateBracket figureEight a1
printComplex "Kauffman bracket" fig8Bracket

let fig8Jones = jonesPolynomial figureEight a1
printComplex "Jones polynomial" fig8Jones

printfn "\nNote: Figure-eight has writhe 0 (alternating positive/negative crossings)"
printfn "This knot is achiral (identical to its mirror image)"
printfn ""

// ============================================================================
// Example 5: Hopf Link - Simplest Non-Trivial Link
// ============================================================================

printfn "Example 5: Hopf Link (Two Components)"
printSeparator ()

let hopf = hopfLink
printfn "Diagram: %A" hopf
printfn "Number of crossings: %d" (List.length hopf)

let hopfWrithe = writhe hopf
printfn "Writhe: %d" hopfWrithe

let hopfBracket = evaluateBracket hopf a1
printComplex "Kauffman bracket" hopfBracket

let hopfJones = jonesPolynomial hopf a1
printComplex "Jones polynomial" hopfJones

printfn "\nNote: Hopf link has 2 components linked together"
printfn ""

// ============================================================================
// Example 6: TQFT Evaluations - Topological Field Theory Applications
// ============================================================================

printfn "Example 6: TQFT Special Values"
printSeparator ()

// Ising TQFT: A = exp(iπ/4)
printfn "\n--- Ising Anyon Theory (A = e^(iπ/4)) ---"
let isingA = Complex.FromPolarCoordinates(1.0, Math.PI / 4.0)
printComplex "A" isingA

let isingUnknot = evaluateIsing unknotDiagram
let isingTrefoil = evaluateIsing trefoilRight
let isingFig8 = evaluateIsing figureEight

printComplex "Ising(unknot)" isingUnknot
printComplex "Ising(trefoil)" isingTrefoil
printComplex "Ising(figure-eight)" isingFig8

// Fibonacci TQFT: A = exp(4πi/5)
printfn "\n--- Fibonacci Anyon Theory (A = e^(4πi/5)) ---"
let fibA = Complex.FromPolarCoordinates(1.0, 4.0 * Math.PI / 5.0)
printComplex "A" fibA

let fibUnknot = evaluateFibonacci unknotDiagram
let fibTrefoil = evaluateFibonacci trefoilRight
let fibFig8 = evaluateFibonacci figureEight

printComplex "Fibonacci(unknot)" fibUnknot
printComplex "Fibonacci(trefoil)" fibTrefoil
printComplex "Fibonacci(figure-eight)" fibFig8

// Jones at t = -1
printfn "\n--- Jones Polynomial at t = -1 ---"
let unknotMinusOne = evaluateJonesAtMinusOne unknotDiagram
let trefoilMinusOne = evaluateJonesAtMinusOne trefoilRight
let fig8MinusOne = evaluateJonesAtMinusOne figureEight

printComplex "J(unknot, -1)" unknotMinusOne
printComplex "J(trefoil, -1)" trefoilMinusOne
printComplex "J(figure-eight, -1)" fig8MinusOne

printfn "\nNote: These special TQFT values are used in:"
printfn "  - Ising: Topological superconductors"
printfn "  - Fibonacci: Non-abelian anyons (quantum computing)"
printfn "  - Jones at -1: Connection to quantum dimensions"
printfn ""

// ============================================================================
// Example 7: Knot Comparison Table
// ============================================================================

printfn "Example 7: Knot Invariant Comparison Table"
printSeparator ()

let knots = [
    ("Unknot", unknot)
    ("Right Trefoil", trefoil true)
    ("Left Trefoil", trefoil false)
    ("Figure-Eight", figureEight)
    ("Hopf Link", hopfLink)
]

printfn "%-20s %10s %15s %15s" "Knot" "Crossings" "Writhe" "Jones@std"
printfn "%s" (String.replicate 70 "-")

for (name, diagram) in knots do
    let crossings = List.length diagram
    let w = writhe diagram
    let jones = jonesPolynomial diagram standardA
    let jonesStr = 
        if Math.Abs(jones.Imaginary) < 1e-10 then
            sprintf "%.4f" jones.Real
        else
            sprintf "%.4f%+.4fi" jones.Real jones.Imaginary
    printfn "%-20s %10d %15d %15s" name crossings w jonesStr

printfn ""

// ============================================================================
// Example 8: Custom Knot Diagram
// ============================================================================

printfn "Example 8: Custom Knot Construction"
printSeparator ()

// Create a custom knot with mixed crossings
let customKnot = [Positive; Positive; Negative; Positive; Negative]
printfn "Custom diagram: %A" customKnot
printfn "Number of crossings: %d" (List.length customKnot)

let customWrithe = writhe customKnot
printfn "Writhe: %d" customWrithe

let customBracket = evaluateBracket customKnot standardA
printComplex "Kauffman bracket" customBracket

let customJones = jonesPolynomial customKnot standardA
printComplex "Jones polynomial" customJones

printfn "\nNote: Custom knots can be constructed using [Positive; Negative; ...]"
printfn ""

// ============================================================================
// Summary
// ============================================================================

printSeparator ()
printfn "Summary:"
printfn "  - Implemented Kauffman bracket using skein relations"
printfn "  - Jones polynomial = (-A^3)^(-writhe) * Kauffman bracket"
printfn "  - Standard knots: unknot, trefoil, figure-eight, Hopf link"
printfn "  - TQFT evaluations: Ising, Fibonacci, Jones at t=-1"
printfn "  - All calculations follow Steven Simon Chapter 2"
printSeparator ()

printfn "\nExample script completed successfully! ✓"
