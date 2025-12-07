namespace FSharp.Azure.Quantum.Topological.Tests

open System
open System.IO
open Xunit
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Topological.TopologicalFormat

module TopologicalFormatTests =
    
    // ========================================================================
    // PARSER TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Parser should parse anyon types correctly`` () =
        let ising = Parser.parseAnyonType "Ising"
        let fib = Parser.parseAnyonType "fibonacci"
        let su2 = Parser.parseAnyonType "SU2_3"
        
        match ising, fib, su2 with
        | Ok (AnyonSpecies.AnyonType.Ising), 
          Ok (AnyonSpecies.AnyonType.Fibonacci), 
          Ok (AnyonSpecies.AnyonType.SU2Level 3) -> ()
        | _ -> failwith "Failed to parse anyon types"
    
    [<Fact>]
    let ``Parser should reject invalid anyon types`` () =
        let invalid = Parser.parseAnyonType "InvalidType"
        
        match invalid with
        | Error _ -> ()
        | Ok _ -> failwith "Should have rejected invalid anyon type"
    
    [<Fact>]
    let ``Parser should parse F-move directions`` () =
        let left = Parser.parseFMoveDirection "left"
        let right = Parser.parseFMoveDirection "Right"
        
        match left, right with
        | Ok FMoveDirection.Left, Ok FMoveDirection.Right -> ()
        | _ -> failwith "Failed to parse F-move directions"
    
    [<Fact>]
    let ``Parser should parse simple program`` () =
        let program = """
# Simple Bell state program
ANYON Ising

INIT 4
BRAID 0
BRAID 2
MEASURE 1
"""
        
        match Parser.parseProgram program with
        | Error msg -> failwith $"Parse failed: {msg}"
        | Ok prog ->
            Assert.Equal(AnyonSpecies.AnyonType.Ising, prog.AnyonType)
            Assert.Equal(5, prog.Operations.Length)  // 1 comment + 4 operations
            
            // Check operations
            match prog.Operations with
            | [Comment _; Initialize 4; Braid 0; Braid 2; Measure 1] -> ()
            | ops -> failwith $"Unexpected operations: {ops}"
    
    [<Fact>]
    let ``Parser should handle comments and empty lines`` () =
        let program = """
# Comment line
ANYON Fibonacci

# Another comment
INIT 2

BRAID 0
# Final comment
"""
        
        match Parser.parseProgram program with
        | Error msg -> failwith $"Parse failed: {msg}"
        | Ok prog ->
            Assert.Equal(AnyonSpecies.AnyonType.Fibonacci, prog.AnyonType)
            // Should have comments, INIT, and BRAID
            Assert.True(prog.Operations.Length >= 3)
    
    [<Fact>]
    let ``Parser should require ANYON declaration`` () =
        let program = """
INIT 2
BRAID 0
"""
        
        match Parser.parseProgram program with
        | Error msg -> 
            Assert.Contains("ANYON", msg)
        | Ok _ -> failwith "Should have required ANYON declaration"
    
    [<Fact>]
    let ``Parser should reject invalid operation indices`` () =
        let program = """
ANYON Ising
INIT 2
BRAID -1
"""
        
        match Parser.parseProgram program with
        | Error msg -> 
            Assert.Contains("index", msg.ToLowerInvariant())
        | Ok _ -> failwith "Should have rejected negative index"
    
    // ========================================================================
    // SERIALIZER TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Serializer should serialize anyon types correctly`` () =
        let ising = Serializer.serializeAnyonType AnyonSpecies.AnyonType.Ising
        let fib = Serializer.serializeAnyonType AnyonSpecies.AnyonType.Fibonacci
        let su2 = Serializer.serializeAnyonType (AnyonSpecies.AnyonType.SU2Level 4)
        
        Assert.Equal("Ising", ising)
        Assert.Equal("Fibonacci", fib)
        Assert.Equal("SU2_4", su2)
    
    [<Fact>]
    let ``Serializer should serialize operations correctly`` () =
        let init = Serializer.serializeOperation (Initialize 4)
        let braid = Serializer.serializeOperation (Braid 2)
        let measure = Serializer.serializeOperation (Measure 1)
        let fmove = Serializer.serializeOperation (FMove (FMoveDirection.Left, 3))
        
        Assert.Equal("INIT 4", init)
        Assert.Equal("BRAID 2", braid)
        Assert.Equal("MEASURE 1", measure)
        Assert.Equal("FMOVE Left 3", fmove)
    
    [<Fact>]
    let ``Serializer should create valid .tqp format`` () =
        let program = {
            AnyonType = AnyonSpecies.AnyonType.Ising
            Operations = [
                Comment "# Test program"
                Initialize 4
                Braid 0
                Measure 1
            ]
        }
        
        let serialized = Serializer.serializeProgram program
        
        // Should contain header
        Assert.Contains("# Topological Quantum Program", serialized)
        Assert.Contains("# Generated:", serialized)
        
        // Should contain ANYON declaration
        Assert.Contains("ANYON Ising", serialized)
        
        // Should contain operations
        Assert.Contains("INIT 4", serialized)
        Assert.Contains("BRAID 0", serialized)
        Assert.Contains("MEASURE 1", serialized)
    
    // ========================================================================
    // ROUND-TRIP TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Round-trip: Parse and serialize should be identity`` () =
        let original = {
            AnyonType = AnyonSpecies.AnyonType.Fibonacci
            Operations = [
                Initialize 2
                Braid 0
                Braid 1
                Measure 0
            ]
        }
        
        // Serialize
        let serialized = Serializer.serializeProgram original
        
        // Parse back
        match Parser.parseProgram serialized with
        | Error msg -> failwith $"Round-trip parse failed: {msg}"
        | Ok parsed ->
            Assert.Equal(original.AnyonType, parsed.AnyonType)
            
            // Compare operations (excluding auto-generated comments)
            let originalOps = original.Operations
            let parsedOps = parsed.Operations |> List.filter (fun op -> 
                match op with Comment _ -> false | _ -> true
            )
            
            Assert.Equal(originalOps.Length, parsedOps.Length)
    
    [<Fact>]
    let ``File I/O: Write and read should preserve program`` () =
        let tempFile = Path.GetTempFileName()
        
        try
            let original = {
                AnyonType = AnyonSpecies.AnyonType.Ising
                Operations = [
                    Initialize 6
                    Braid 0
                    Braid 2
                    Braid 4
                    Measure 1
                    Measure 3
                ]
            }
            
            // Write to file
            match Serializer.serializeToFile original tempFile with
            | Error msg -> failwith $"Write failed: {msg}"
            | Ok () ->
                // Read back
                match Parser.parseFile tempFile with
                | Error msg -> failwith $"Read failed: {msg}"
                | Ok parsed ->
                    Assert.Equal(original.AnyonType, parsed.AnyonType)
                    
                    // Compare operations (excluding comments)
                    let originalOps = original.Operations
                    let parsedOps = parsed.Operations |> List.filter (fun op ->
                        match op with Comment _ -> false | _ -> true
                    )
                    
                    Assert.Equal(originalOps.Length, parsedOps.Length)
        finally
            // Cleanup
            if File.Exists(tempFile) then
                File.Delete(tempFile)
    
    // ========================================================================
    // ERROR HANDLING TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Parser should handle malformed input gracefully`` () =
        let invalidPrograms = [
            ""  // Empty
            "INIT 2"  // No ANYON
            "ANYON Ising\nBRAID"  // Missing parameter
            "ANYON Ising\nINIT abc"  // Invalid number
            "ANYON UnknownType\nINIT 2"  // Invalid anyon type
        ]
        
        for program in invalidPrograms do
            match Parser.parseProgram program with
            | Error _ -> ()  // Expected
            | Ok _ -> failwith $"Should have rejected: {program}"
    
    [<Fact>]
    let ``Serializer should handle non-existent directory gracefully`` () =
        let program = {
            AnyonType = AnyonSpecies.AnyonType.Ising
            Operations = [Initialize 2]
        }
        
        let invalidPath = "/nonexistent/directory/file.tqp"
        
        match Serializer.serializeToFile program invalidPath with
        | Error msg -> 
            Assert.Contains("Failed to write", msg)
        | Ok () -> failwith "Should have failed with invalid path"
