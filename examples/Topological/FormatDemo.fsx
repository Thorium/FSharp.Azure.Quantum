/// Demonstration of .tqp file format import/export
///
/// This example shows how to:
/// 1. Create topological programs programmatically
/// 2. Save them to .tqp files
/// 3. Load .tqp files and execute them
/// 4. Round-trip: program -> file -> program

#r "../../src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"

open System
open System.IO
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Topological.TopologicalFormat

// ============================================================================
// EXAMPLE 1: Create and save a program
// ============================================================================

printfn "=== Example 1: Create and Save .tqp File ==="
printfn ""

// Create a simple program programmatically
let simpleProgram = {
    AnyonType = AnyonSpecies.AnyonType.Fibonacci
    Operations = [
        Initialize 2
        Braid 0
        Measure 0
    ]
}

// Save to file
let outputPath = "fibonacci-simple.tqp"
match Serializer.serializeToFile simpleProgram outputPath with
| Ok () -> 
    printfn "✓ Program saved to: %s" outputPath
    printfn ""
    
    // Show the file contents
    printfn "File contents:"
    printfn "─────────────────────────────────────────"
    File.ReadAllText(outputPath) |> printfn "%s"
    printfn "─────────────────────────────────────────"
    printfn ""
| Error msg ->
    printfn "✗ Failed to save: %s" msg

// ============================================================================
// EXAMPLE 2: Load and parse a .tqp file
// ============================================================================

printfn "=== Example 2: Load and Parse .tqp File ==="
printfn ""

// Load the Bell state example
let bellStatePath = "bell-state.tqp"

match Parser.parseFile bellStatePath with
| Ok program ->
    printfn "✓ Loaded program from: %s" bellStatePath
    printfn ""
    printfn "Anyon Type: %A" program.AnyonType
    printfn "Operations: %d" program.Operations.Length
    printfn ""
    
    // Display operations
    printfn "Operation sequence:"
    program.Operations 
    |> List.iteri (fun i op ->
        match op with
        | Comment text -> printfn "  [%d] %s" i text
        | Initialize count -> printfn "  [%d] Initialize %d anyons" i count
        | Braid index -> printfn "  [%d] Braid at index %d" i index
        | Measure index -> printfn "  [%d] Measure at index %d" i index
        | FMove (dir, depth) -> printfn "  [%d] F-Move %A at depth %d" i dir depth
    )
    printfn ""
| Error msg ->
    printfn "✗ Failed to parse: %s" msg
    printfn ""

// ============================================================================
// EXAMPLE 3: Round-trip test (program -> file -> program)
// ============================================================================

printfn "=== Example 3: Round-Trip Test ==="
printfn ""

// Create a complex program
let originalProgram = {
    AnyonType = AnyonSpecies.AnyonType.Ising
    Operations = [
        Comment "# Quantum algorithm using Ising anyons"
        Initialize 6
        Braid 0
        Braid 2
        Braid 4
        FMove (FMoveDirection.Left, 1)
        Measure 1
        Measure 3
    ]
}

// Serialize to string
let serialized = Serializer.serializeProgram originalProgram

printfn "Serialized program:"
printfn "─────────────────────────────────────────"
printfn "%s" serialized
printfn "─────────────────────────────────────────"
printfn ""

// Parse it back
match Parser.parseProgram serialized with
| Ok parsedProgram ->
    printfn "✓ Round-trip successful!"
    printfn ""
    printfn "Original anyon type: %A" originalProgram.AnyonType
    printfn "Parsed anyon type:   %A" parsedProgram.AnyonType
    printfn "Match: %b" (originalProgram.AnyonType = parsedProgram.AnyonType)
    printfn ""
    
    // Compare operations (excluding auto-generated comments)
    let originalOps = originalProgram.Operations
    let parsedOps = parsedProgram.Operations |> List.filter (fun op ->
        match op with
        | Comment c when c.Contains("Generated:") -> false
        | _ -> true
    )
    
    printfn "Original operations: %d" originalOps.Length
    printfn "Parsed operations:   %d" parsedOps.Length
    printfn ""
| Error msg ->
    printfn "✗ Round-trip failed: %s" msg
    printfn ""

// ============================================================================
// EXAMPLE 4: Create programs for different anyon types
// ============================================================================

printfn "=== Example 4: Different Anyon Types ==="
printfn ""

let anyonTypes = [
    ("Ising", AnyonSpecies.AnyonType.Ising)
    ("Fibonacci", AnyonSpecies.AnyonType.Fibonacci)
    ("SU(2)_3", AnyonSpecies.AnyonType.SU2Level 3)
]

for (name, anyonType) in anyonTypes do
    let program = {
        AnyonType = anyonType
        Operations = [
            Comment $"# {name} anyon example"
            Initialize 4
            Braid 0
            Braid 1
            Measure 0
        ]
    }
    
    let cleanName = name.ToLowerInvariant().Replace("(", "").Replace(")", "").Replace("_", "-")
    let filename = $"{cleanName}-example.tqp"
    
    match Serializer.serializeToFile program filename with
    | Ok () -> printfn "✓ Created: %s" filename
    | Error msg -> printfn "✗ Failed to create %s: %s" filename msg

printfn ""

// ============================================================================
// EXAMPLE 5: Execute a .tqp file on simulator backend
// ============================================================================

printfn "=== Example 5: Execute .tqp File ==="
printfn ""

// Note: Full execution requires TopologicalBackend implementation
// This example shows the execution flow

open System.Threading.Tasks
open TopologicalBackend

// Create a simulator backend (Fibonacci anyons, max 10 anyons)
let backend = SimulatorBackend(AnyonSpecies.AnyonType.Fibonacci, 10) :> ITopologicalBackend

// Load and execute the simple Fibonacci program
task {
    match Parser.parseFile "fibonacci-simple.tqp" with
    | Ok program ->
        printfn "Executing program from fibonacci-simple.tqp..."
        
        let! result = Executor.executeProgram backend program
        
        match result with
        | Ok execResult ->
            printfn "✓ Execution successful!"
            printfn ""
            printfn "Final state: %A" execResult.FinalState
            printfn "Measurements: %d" execResult.MeasurementOutcomes.Length
            printfn ""
            
            // Display measurement results
            execResult.MeasurementOutcomes
            |> List.iteri (fun i outcome ->
                printfn "  Measurement %d: %A" (i+1) outcome
            )
            printfn ""
        | Error err ->
            printfn "✗ Execution failed: %s" err.Message
    | Error msg ->
        printfn "✗ Failed to parse file: %s" msg
} |> Async.AwaitTask |> Async.RunSynchronously

printfn "=== Demo Complete ==="

// ============================================================================
// CLEANUP (optional)
// ============================================================================

// Uncomment to delete generated files
(*
let filesToClean = [
    "fibonacci-simple.tqp"
    "ising-example.tqp"
    "fibonacci-example.tqp"
    "su2-3-example.tqp"
]

for file in filesToClean do
    if File.Exists(file) then
        File.Delete(file)
        printfn "Deleted: %s" file
*)
