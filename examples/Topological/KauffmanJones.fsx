(*
    Kauffman Bracket & Jones Polynomial -- Topological Quantum Computing
    =====================================================================

    Knot invariant calculations using the Kauffman bracket and Jones
    polynomial, following Steven Simon's "Topological Quantum" (Ch. 2).

    Examples:
      1  Unknot (simplest case)
      2  Trefoil (right-handed, simplest non-trivial)
      3  Mirror symmetry (left vs right trefoil -- chirality)
      4  Figure-eight (achiral knot)
      5  Hopf link (simplest non-trivial link)
      6  TQFT evaluations (Ising / Fibonacci / Jones@-1)
      7  Knot comparison table
      8  Custom knot construction

    Run with: dotnet fsi KauffmanJones.fsx
              dotnet fsi KauffmanJones.fsx -- --example 6
              dotnet fsi KauffmanJones.fsx -- --quiet --output r.json --csv r.csv
*)

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "../../src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open System.Numerics
open FSharp.Azure.Quantum.Topological.KauffmanBracket
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Examples.Common

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "KauffmanJones.fsx" "Kauffman bracket and Jones polynomial knot invariants"
    [ { Name = "example";    Description = "Which example: 1-8|all"; Default = Some "all" }
      { Name = "crossings";  Description = "Custom crossing list (e.g. P,P,N,P)"; Default = None }
      { Name = "output";     Description = "Write results to JSON file"; Default = None }
      { Name = "csv";        Description = "Write results to CSV file";  Default = None }
      { Name = "quiet";      Description = "Suppress console output";    Default = None } ] args

let quiet      = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath    = Cli.tryGet "csv" args
let exChoice   = Cli.getOr "example" "all" args
let customCrossingsOpt = Cli.tryGet "crossings" args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt
let shouldRun ex = exChoice = "all" || exChoice = string ex
let separator () = pr "%s" (String.replicate 70 "=")

// ---------------------------------------------------------------------------
// Quantum backend (Rule 1) -- available for downstream execution
// ---------------------------------------------------------------------------
let quantumBackend = TopologicalUnifiedBackendFactory.createIsing 10

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
let standardA = Complex.FromPolarCoordinates(1.0, 2.0 * Math.PI / 5.0)

let fmtComplex (c: Complex) =
    if Math.Abs(c.Imaginary) < 1e-10 then
        sprintf "%.6f" c.Real
    else
        let sign = if c.Imaginary >= 0.0 then "+" else ""
        sprintf "%.6f %s%.6fi" c.Real sign c.Imaginary

let printComplex name (c: Complex) =
    pr "  %s = %s" name (fmtComplex c)

let parseCrossings (s: string) =
    s.Split(',')
    |> Array.map (fun t ->
        match t.Trim().ToUpperInvariant() with
        | "P" | "POSITIVE" | "+" -> Positive
        | "N" | "NEGATIVE" | "-" -> Negative
        | x -> failwithf "Unknown crossing '%s'. Use P or N." x)
    |> Array.toList

// Results accumulators
let mutable jsonResults : (string * obj) list = []
let mutable csvRows     : string list list    = []

// ---------------------------------------------------------------------------
// Example 1 -- Unknot
// ---------------------------------------------------------------------------
if shouldRun 1 then
    separator ()
    pr "EXAMPLE 1: Unknot (Simple Loop)"
    separator ()

    let diagram = unknot
    let bracket = evaluateBracket diagram standardA
    let w = writhe diagram
    let jones = jonesPolynomial diagram standardA

    pr "  Crossings: %d   Writhe: %d" diagram.Length w
    printComplex "Kauffman bracket" bracket
    printComplex "Jones polynomial" jones
    pr "  Expected Jones(unknot) = 1.0"

    jsonResults <- ("1_unknot", box {| crossings = diagram.Length; writhe = w; jones = fmtComplex jones |}) :: jsonResults
    csvRows <- [ "1_unknot"; string diagram.Length; string w; fmtComplex jones ] :: csvRows

// ---------------------------------------------------------------------------
// Example 2 -- Trefoil
// ---------------------------------------------------------------------------
if shouldRun 2 then
    separator ()
    pr "EXAMPLE 2: Trefoil Knot (Right-Handed)"
    separator ()

    let diagram = trefoil true
    let bracket = evaluateBracket diagram standardA
    let w = writhe diagram
    let jones = jonesPolynomial diagram standardA

    pr "  Crossings: %d   Writhe: %d" diagram.Length w
    printComplex "Kauffman bracket" bracket
    printComplex "Jones polynomial" jones
    pr "  All positive crossings, writhe = +3"

    jsonResults <- ("2_trefoil", box {| crossings = diagram.Length; writhe = w; jones = fmtComplex jones |}) :: jsonResults
    csvRows <- [ "2_trefoil"; string diagram.Length; string w; fmtComplex jones ] :: csvRows

// ---------------------------------------------------------------------------
// Example 3 -- Mirror symmetry
// ---------------------------------------------------------------------------
if shouldRun 3 then
    separator ()
    pr "EXAMPLE 3: Mirror Symmetry (Chirality)"
    separator ()

    let left = trefoil false
    let right = trefoil true
    let leftJ = jonesPolynomial left standardA
    let rightJ = jonesPolynomial right standardA

    printComplex "Left  Jones" leftJ
    printComplex "Right Jones" rightJ
    pr "  Mirror knots differ -> trefoil is chiral"

    jsonResults <- ("3_mirror", box {| leftJones = fmtComplex leftJ; rightJones = fmtComplex rightJ |}) :: jsonResults
    csvRows <- [ "3_mirror"; fmtComplex leftJ; fmtComplex rightJ ] :: csvRows

// ---------------------------------------------------------------------------
// Example 4 -- Figure-eight
// ---------------------------------------------------------------------------
if shouldRun 4 then
    separator ()
    pr "EXAMPLE 4: Figure-Eight Knot"
    separator ()

    let diagram = figureEight
    let w = writhe diagram
    let bracket = evaluateBracket diagram standardA
    let jones = jonesPolynomial diagram standardA

    pr "  Crossings: %d   Writhe: %d" diagram.Length w
    printComplex "Kauffman bracket" bracket
    printComplex "Jones polynomial" jones
    pr "  Achiral: identical to its mirror image"

    jsonResults <- ("4_figure_eight", box {| crossings = diagram.Length; writhe = w; jones = fmtComplex jones |}) :: jsonResults
    csvRows <- [ "4_figure_eight"; string diagram.Length; string w; fmtComplex jones ] :: csvRows

// ---------------------------------------------------------------------------
// Example 5 -- Hopf link
// ---------------------------------------------------------------------------
if shouldRun 5 then
    separator ()
    pr "EXAMPLE 5: Hopf Link (Two Components)"
    separator ()

    let diagram = hopfLink
    let w = writhe diagram
    let bracket = evaluateBracket diagram standardA
    let jones = jonesPolynomial diagram standardA

    pr "  Crossings: %d   Writhe: %d" diagram.Length w
    printComplex "Kauffman bracket" bracket
    printComplex "Jones polynomial" jones
    pr "  Two components linked together"

    jsonResults <- ("5_hopf_link", box {| crossings = diagram.Length; writhe = w; jones = fmtComplex jones |}) :: jsonResults
    csvRows <- [ "5_hopf_link"; string diagram.Length; string w; fmtComplex jones ] :: csvRows

// ---------------------------------------------------------------------------
// Example 6 -- TQFT evaluations
// ---------------------------------------------------------------------------
if shouldRun 6 then
    separator ()
    pr "EXAMPLE 6: TQFT Special Values"
    separator ()

    let knots = [ ("unknot", unknot); ("trefoil", trefoil true); ("figure-eight", figureEight) ]

    pr ""
    pr "--- Ising Anyon Theory (A = e^(ipi/4)) ---"
    for (name, diagram) in knots do
        let v = evaluateIsing diagram
        printComplex (sprintf "Ising(%s)" name) v

    pr ""
    pr "--- Fibonacci Anyon Theory (A = e^(4pi*i/5)) ---"
    for (name, diagram) in knots do
        let v = evaluateFibonacci diagram
        printComplex (sprintf "Fibonacci(%s)" name) v

    pr ""
    pr "--- Jones Polynomial at t = -1 ---"
    for (name, diagram) in knots do
        let v = evaluateJonesAtMinusOne diagram
        printComplex (sprintf "J(%s,-1)" name) v

    pr ""
    pr "  Ising: Topological superconductors"
    pr "  Fibonacci: Non-abelian anyons (universal QC)"
    pr "  Jones at -1: Connection to quantum dimensions"

    let isingVals = knots |> List.map (fun (n,d) -> (n, fmtComplex (evaluateIsing d)))
    jsonResults <- ("6_tqft", box {| ising = isingVals |}) :: jsonResults
    csvRows <- ("6_tqft" :: (isingVals |> List.map snd)) :: csvRows

// ---------------------------------------------------------------------------
// Example 7 -- Comparison table
// ---------------------------------------------------------------------------
if shouldRun 7 then
    separator ()
    pr "EXAMPLE 7: Knot Invariant Comparison Table"
    separator ()

    let knots = [
        ("Unknot",        unknot)
        ("Right Trefoil", trefoil true)
        ("Left Trefoil",  trefoil false)
        ("Figure-Eight",  figureEight)
        ("Hopf Link",     hopfLink)
    ]

    pr "%-20s %10s %8s %20s" "Knot" "Crossings" "Writhe" "Jones@std"
    pr "%s" (String.replicate 62 "-")

    let tableRows =
        knots |> List.map (fun (name, diagram) ->
            let c = diagram.Length
            let w = writhe diagram
            let j = jonesPolynomial diagram standardA
            let js = fmtComplex j
            pr "%-20s %10d %8d %20s" name c w js
            [ name; string c; string w; js ])

    jsonResults <- ("7_table", box {| rows = tableRows.Length |}) :: jsonResults
    csvRows <- csvRows @ tableRows

// ---------------------------------------------------------------------------
// Example 8 -- Custom knot
// ---------------------------------------------------------------------------
if shouldRun 8 then
    separator ()
    pr "EXAMPLE 8: Custom Knot Construction"
    separator ()

    let customKnot =
        match customCrossingsOpt with
        | Some spec -> parseCrossings spec
        | None -> [ Positive; Positive; Negative; Positive; Negative ]

    pr "  Diagram: %A" customKnot
    pr "  Crossings: %d   Writhe: %d" customKnot.Length (writhe customKnot)

    let bracket = evaluateBracket customKnot standardA
    let jones = jonesPolynomial customKnot standardA
    printComplex "Kauffman bracket" bracket
    printComplex "Jones polynomial" jones
    pr "  Tip: --crossings P,P,N,P,N to specify your own"

    jsonResults <- ("8_custom", box {| crossings = customKnot.Length; writhe = writhe customKnot; jones = fmtComplex jones |}) :: jsonResults
    csvRows <- [ "8_custom"; string customKnot.Length; string (writhe customKnot); fmtComplex jones ] :: csvRows

// ---------------------------------------------------------------------------
// Summary
// ---------------------------------------------------------------------------
if not quiet then
    separator ()
    pr "Summary:"
    pr "  Kauffman bracket via skein relations"
    pr "  Jones poly = (-A^3)^(-writhe) * bracket"
    pr "  TQFT evaluations: Ising, Fibonacci, Jones@-1"
    pr "  All calculations follow Steven Simon Ch. 2"
    separator ()

// ---------------------------------------------------------------------------
// Output
// ---------------------------------------------------------------------------
if outputPath.IsSome then
    let payload =
        {| script    = "KauffmanJones.fsx"
           backend   = quantumBackend.Name
           timestamp = DateTime.UtcNow.ToString("o")
           example   = exChoice
           results   = jsonResults |> List.rev |> List.map (fun (k,v) -> {| key = k; value = v |}) |}
    Reporting.writeJson outputPath.Value payload

if csvPath.IsSome then
    let header = [ "example"; "detail1"; "detail2"; "detail3" ]
    Reporting.writeCsv csvPath.Value header (csvRows |> List.rev)

// ---------------------------------------------------------------------------
// Usage hints
// ---------------------------------------------------------------------------
if not quiet && argv.Length = 0 then
    pr ""
    pr "Usage hints:"
    pr "  dotnet fsi KauffmanJones.fsx -- --example 6"
    pr "  dotnet fsi KauffmanJones.fsx -- --crossings P,P,N,N,P"
    pr "  dotnet fsi KauffmanJones.fsx -- --quiet --output r.json --csv r.csv"
    pr "  dotnet fsi KauffmanJones.fsx -- --help"
