namespace FSharp.Azure.Quantum.Topological.Tests

open System
open System.IO
open System.Threading.Tasks
open Xunit
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Topological.TopologicalFormat
open FSharp.Azure.Quantum.Topological.TopologicalUnifiedBackendFactory

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

    // ========================================================================
    // EXECUTOR ASYNC TESTS
    // ========================================================================

    [<Fact>]
    let ``executeProgramAsync should produce same result as sync executeProgram`` () : Task =
        task {
            let backend =
                TopologicalUnifiedBackendFactory.createIsing 16

            let program = {
                AnyonType = AnyonSpecies.AnyonType.Ising
                Operations = [
                    Initialize 4
                    Braid 0
                    Braid 2
                ]
            }

            let syncResult = Executor.executeProgram backend program

            let! asyncResult =
                Executor.executeProgramAsync backend program System.Threading.CancellationToken.None

            match syncResult, asyncResult with
            | Ok syncExec, Ok asyncExec ->
                // Both should produce a valid final state
                Assert.NotNull(box syncExec.FinalState)
                Assert.NotNull(box asyncExec.FinalState)
                // Messages should have same count (one per operation)
                Assert.Equal(syncExec.Messages.Length, asyncExec.Messages.Length)
            | Error syncErr, _ ->
                failwith $"Sync execution failed: {syncErr}"
            | _, Error asyncErr ->
                failwith $"Async execution failed: {asyncErr}"
        }

    [<Fact>]
    let ``executeProgramAsync should fail without INIT operation`` () : Task =
        task {
            let backend =
                TopologicalUnifiedBackendFactory.createIsing 16

            let program = {
                AnyonType = AnyonSpecies.AnyonType.Ising
                Operations = [ Braid 0 ]
            }

            let! result =
                Executor.executeProgramAsync backend program System.Threading.CancellationToken.None

            match result with
            | Error err ->
                let errStr = $"{err}"
                Assert.Contains("INIT", errStr)
            | Ok _ ->
                failwith "Should have failed without INIT operation"
        }

    [<Fact>]
    let ``executeProgramAsync should respect cancellation`` () : Task =
        task {
            let backend =
                TopologicalUnifiedBackendFactory.createIsing 16

            let program = {
                AnyonType = AnyonSpecies.AnyonType.Ising
                Operations = [
                    Initialize 4
                    Braid 0
                    Braid 1
                    Braid 2
                ]
            }

            use cts = new System.Threading.CancellationTokenSource()
            cts.Cancel()

            let mutable threw = false
            try
                let! _ = Executor.executeProgramAsync backend program cts.Token
                ()
            with
            | :? System.OperationCanceledException -> threw <- true

            Assert.True(threw, "Should have thrown OperationCanceledException or TaskCanceledException")
        }

    [<Fact>]
    let ``executeFileAsync should execute program from file`` () : Task =
        task {
            let tempFile = Path.GetTempFileName()

            try
                let program = {
                    AnyonType = AnyonSpecies.AnyonType.Ising
                    Operations = [
                        Initialize 4
                        Braid 0
                        Braid 2
                    ]
                }

                match Serializer.serializeToFile program tempFile with
                | Error msg -> failwith $"Write failed: {msg}"
                | Ok () ->
                    let backend =
                        TopologicalUnifiedBackendFactory.createIsing 16

                    let! result =
                        Executor.executeFileAsync backend tempFile System.Threading.CancellationToken.None

                    match result with
                    | Ok exec ->
                        Assert.NotNull(box exec.FinalState)
                        Assert.True(exec.Messages.Length > 0, "Should have execution messages")
                    | Error err ->
                        failwith $"Async file execution failed: {err}"
            finally
                if File.Exists(tempFile) then
                    File.Delete(tempFile)
        }

    [<Fact>]
    let ``executeFileAsync should fail with non-existent file`` () : Task =
        task {
            let backend =
                TopologicalUnifiedBackendFactory.createIsing 16

            let! result =
                Executor.executeFileAsync backend "/nonexistent/file.tqp" System.Threading.CancellationToken.None

            match result with
            | Error err ->
                let errStr = $"{err}"
                Assert.Contains("filePath", errStr)
            | Ok _ ->
                failwith "Should have failed with non-existent file"
        }

    [<Fact>]
    let ``executeFileAsync roundtrip with async file I/O`` () : Task =
        task {
            let tempFile = Path.GetTempFileName()

            try
                let program = {
                    AnyonType = AnyonSpecies.AnyonType.Ising
                    Operations = [
                        Initialize 6
                        Braid 0
                        Braid 2
                        Braid 4
                    ]
                }

                // Write asynchronously
                let! writeResult = Serializer.serializeToFileAsync program tempFile System.Threading.CancellationToken.None
                match writeResult with
                | Error msg -> failwith $"Async write failed: {msg}"
                | Ok () -> ()

                // Read and execute asynchronously
                let backend =
                    TopologicalUnifiedBackendFactory.createIsing 16

                let! result =
                    Executor.executeFileAsync backend tempFile System.Threading.CancellationToken.None

                match result with
                | Ok exec ->
                    Assert.NotNull(box exec.FinalState)
                    // 3 braid operations should produce 3 messages
                    Assert.Equal(3, exec.Messages.Length)
                | Error err ->
                    failwith $"Async roundtrip failed: {err}"
            finally
                if File.Exists(tempFile) then
                    File.Delete(tempFile)
        }
