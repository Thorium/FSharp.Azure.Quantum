namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core.BackendAbstraction

module SocialNetworkAnalyzerTests =

    open SocialNetworkAnalyzer

    // ========================================================================
    // HELPERS
    // ========================================================================

    let private createTriangle () =
        {
            People = ["Alice"; "Bob"; "Carol"]
            Connections = [
                { Person1 = "Alice"; Person2 = "Bob" }
                { Person1 = "Bob"; Person2 = "Carol" }
                { Person1 = "Carol"; Person2 = "Alice" }
            ]
            MinCommunitySize = Some 3
            Backend = Some (LocalBackend.LocalBackend() :> IQuantumBackend)
            Shots = 100
        }

    let private createSquare () =
        {
            People = ["A"; "B"; "C"; "D"]
            Connections = [
                { Person1 = "A"; Person2 = "B" }
                { Person1 = "B"; Person2 = "C" }
                { Person1 = "C"; Person2 = "D" }
                { Person1 = "D"; Person2 = "A" }
            ]
            MinCommunitySize = Some 2
            Backend = Some (LocalBackend.LocalBackend() :> IQuantumBackend)
            Shots = 100
        }

    // ========================================================================
    // VALIDATION TESTS
    // ========================================================================

    [<Fact>]
    let ``solve should reject empty people list`` () =
        let problem = { createTriangle() with People = [] }
        match solve problem with
        | Error (QuantumError.ValidationError (param, _)) ->
            Assert.Equal("People", param)
        | _ -> failwith "Should return ValidationError for empty people"

    [<Fact>]
    let ``solve should reject network with more than 100 people`` () =
        let people = List.init 101 (fun i -> $"Person{i}")
        let problem = { createTriangle() with People = people }
        match solve problem with
        | Error (QuantumError.ValidationError (param, msg)) ->
            Assert.Equal("People", param)
            Assert.Contains("too large", msg)
        | _ -> failwith "Should return ValidationError for too many people"

    [<Fact>]
    let ``solve without backend should return NotImplemented error`` () =
        let problem = { createTriangle() with Backend = None }
        match solve problem with
        | Error (QuantumError.NotImplemented (feature, _)) ->
            Assert.Contains("Classical", feature)
        | _ -> failwith "Should return NotImplemented when no backend provided"

    [<Fact>]
    let ``solve with missing MinCommunitySize should return ValidationError`` () =
        let problem = { createTriangle() with MinCommunitySize = None }
        match solve problem with
        | Error (QuantumError.ValidationError (param, _)) ->
            Assert.Equal("MinCommunitySize", param)
        | _ -> failwith "Should return ValidationError for missing MinCommunitySize"

    [<Fact>]
    let ``solve with MinCommunitySize less than 2 should return ValidationError`` () =
        let problem = { createTriangle() with MinCommunitySize = Some 1 }
        match solve problem with
        | Error (QuantumError.ValidationError (param, _)) ->
            Assert.Equal("MinCommunitySize", param)
        | _ -> failwith "Should return ValidationError for MinCommunitySize < 2"

    // ========================================================================
    // SUCCESSFUL SOLVE TESTS
    // ========================================================================

    [<Fact>]
    let ``solve with triangle network should return result`` () =
        let problem = createTriangle()
        match solve problem with
        | Ok result ->
            Assert.Equal(3, result.TotalPeople)
            Assert.Equal(3, result.TotalConnections)
        | Error e -> failwith $"Should succeed, got error: {e}"

    [<Fact>]
    let ``solve with square network should return result`` () =
        let problem = createSquare()
        match solve problem with
        | Ok result ->
            Assert.Equal(4, result.TotalPeople)
            Assert.Equal(4, result.TotalConnections)
        | Error e -> failwith $"Should succeed, got error: {e}"

    [<Fact>]
    let ``solve with single person and MinCommunitySize 2 should return error`` () =
        // MinCommunitySize=2 with only 1 person is logically impossible —
        // the inner GroverCliqueSolver rejects CliqueSize > NumVertices
        let problem = {
            People = ["Alice"]
            Connections = []
            MinCommunitySize = Some 2
            Backend = Some (LocalBackend.LocalBackend() :> IQuantumBackend)
            Shots = 100
        }
        match solve problem with
        | Error (QuantumError.ValidationError (param, _)) ->
            Assert.Equal("CliqueSize", param)
        | Ok _ -> failwith "Should return ValidationError when MinCommunitySize > people count"
        | Error e -> failwith $"Expected ValidationError(CliqueSize), got: {e}"

    [<Fact>]
    let ``solve with disconnected people should return result`` () =
        let problem = {
            People = ["Alice"; "Bob"; "Carol"]
            Connections = []
            MinCommunitySize = Some 2
            Backend = Some (LocalBackend.LocalBackend() :> IQuantumBackend)
            Shots = 100
        }
        match solve problem with
        | Ok result ->
            Assert.Equal(3, result.TotalPeople)
            Assert.Equal(0, result.TotalConnections)
        | Error e -> failwith $"Should succeed, got error: {e}"

    [<Fact>]
    let ``solve result message should indicate communities found`` () =
        let problem = createTriangle()
        match solve problem with
        | Ok result ->
            Assert.True(
                result.Message.Contains("communities") || result.Message.Contains("No communities"),
                $"Message should mention communities, got: {result.Message}")
        | Error e -> failwith $"Should succeed, got error: {e}"

    // ========================================================================
    // COMMUNITY STRUCTURE TESTS
    // ========================================================================

    [<Fact>]
    let ``community strength should be between 0 and 1`` () =
        let problem = createTriangle()
        match solve problem with
        | Ok result ->
            for comm in result.Communities do
                Assert.True(comm.Strength >= 0.0 && comm.Strength <= 1.0,
                    $"Strength should be in [0,1], got {comm.Strength}")
        | Error e -> failwith $"Should succeed, got error: {e}"

    [<Fact>]
    let ``community InternalConnections should be non-negative`` () =
        let problem = createTriangle()
        match solve problem with
        | Ok result ->
            for comm in result.Communities do
                Assert.True(comm.InternalConnections >= 0,
                    $"InternalConnections should be >= 0, got {comm.InternalConnections}")
        | Error e -> failwith $"Should succeed, got error: {e}"

    // ========================================================================
    // CE BUILDER TESTS
    // ========================================================================

    [<Fact>]
    let ``socialNetwork CE should build valid problem and solve`` () =
        let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend
        let result = socialNetwork {
            person "Alice"
            person "Bob"
            person "Carol"
            connection "Alice" "Bob"
            connection "Bob" "Carol"
            connection "Carol" "Alice"
            findCommunities 3
            backend quantumBackend
            shots 100
        }
        match result with
        | Ok r ->
            Assert.Equal(3, r.TotalPeople)
            Assert.Equal(3, r.TotalConnections)
        | Error e -> failwith $"CE should succeed, got error: {e}"

    [<Fact>]
    let ``socialNetwork CE with people list should work`` () =
        let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend
        let result = socialNetwork {
            people ["Alice"; "Bob"; "Carol"; "Dave"]
            connections [("Alice", "Bob"); ("Bob", "Carol"); ("Carol", "Alice")]
            findCommunities 2
            backend quantumBackend
            shots 100
        }
        match result with
        | Ok r ->
            Assert.Equal(4, r.TotalPeople)
            Assert.Equal(3, r.TotalConnections)
        | Error e -> failwith $"CE should succeed, got error: {e}"

    [<Fact>]
    let ``socialNetwork CE without backend should return NotImplemented`` () =
        let result = socialNetwork {
            person "Alice"
            person "Bob"
            connection "Alice" "Bob"
            findCommunities 2
        }
        match result with
        | Error (QuantumError.NotImplemented _) -> ()
        | _ -> failwith "Should return NotImplemented without backend"

    [<Fact>]
    let ``socialNetwork CE with empty people should return ValidationError`` () =
        let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend
        let result = socialNetwork {
            findCommunities 2
            backend quantumBackend
        }
        match result with
        | Error (QuantumError.ValidationError (param, _)) ->
            Assert.Equal("People", param)
        | _ -> failwith "Should return ValidationError for empty people"

    // ========================================================================
    // EDGE CASES
    // ========================================================================

    [<Fact>]
    let ``solve with connections referencing unknown people should skip them`` () =
        let problem = {
            People = ["Alice"; "Bob"]
            Connections = [
                { Person1 = "Alice"; Person2 = "Bob" }
                { Person1 = "Alice"; Person2 = "Unknown" }  // Unknown person
            ]
            MinCommunitySize = Some 2
            Backend = Some (LocalBackend.LocalBackend() :> IQuantumBackend)
            Shots = 100
        }
        match solve problem with
        | Ok result ->
            Assert.Equal(2, result.TotalPeople)
            // TotalConnections counts all connections in the problem, including unknown refs
            Assert.Equal(2, result.TotalConnections)
        | Error e -> failwith $"Should succeed, got error: {e}"

    [<Fact>]
    let ``solve with 10 people should succeed within inner solver limit`` () =
        // The outer SocialNetworkAnalyzer allows up to 100 people, but the inner
        // GroverCliqueSolver has a 20-vertex limit. Test with 10 to stay fast.
        let people = List.init 10 (fun i -> $"Person{i}")
        let problem = {
            People = people
            Connections = [{ Person1 = "Person0"; Person2 = "Person1" }]
            MinCommunitySize = Some 2
            Backend = Some (LocalBackend.LocalBackend() :> IQuantumBackend)
            Shots = 100
        }
        match solve problem with
        | Ok result -> Assert.Equal(10, result.TotalPeople)
        | Error e -> failwith $"Should succeed with 10 people, got error: {e}"
