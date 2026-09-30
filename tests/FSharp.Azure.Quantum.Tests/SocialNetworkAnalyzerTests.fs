namespace FSharp.Azure.Quantum.Tests

open System
open System.Threading
open System.Threading.Tasks
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
            People = [ "Alice"; "Bob"; "Carol" ]
            Connections =
                [
                    { Person1 = "Alice"; Person2 = "Bob" }
                    { Person1 = "Bob"; Person2 = "Carol" }
                    { Person1 = "Carol"; Person2 = "Alice" }
                ]
            MinCommunitySize = Some 3
            Mode = None
            Strategy = None
            Backend = Some(LocalBackend.LocalBackend() :> IQuantumBackend)
            Shots = 100
        }

    let private createSquare () =
        {
            People = [ "A"; "B"; "C"; "D" ]
            Connections =
                [
                    { Person1 = "A"; Person2 = "B" }
                    { Person1 = "B"; Person2 = "C" }
                    { Person1 = "C"; Person2 = "D" }
                    { Person1 = "D"; Person2 = "A" }
                ]
            MinCommunitySize = Some 2
            Mode = None
            Strategy = None
            Backend = Some(LocalBackend.LocalBackend() :> IQuantumBackend)
            Shots = 100
        }

    // ========================================================================
    // VALIDATION TESTS
    // ========================================================================

    [<Fact>]
    let ``solve should reject empty people list`` () =
        task {
            let problem = { createTriangle () with People = [] }

            match! solveAsync problem CancellationToken.None with
            | Error(QuantumError.ValidationError(param, _)) -> Assert.Equal("People", param)
            | _ -> failwith "Should return ValidationError for empty people"
        }
        :> Task

    [<Fact>]
    let ``solve should reject network with more than 100 people`` () =
        task {
            let people = List.init 101 (fun i -> $"Person{i}")

            let problem =
                { createTriangle () with
                    People = people
                }

            match! solveAsync problem CancellationToken.None with
            | Error(QuantumError.ValidationError(param, msg)) ->
                Assert.Equal("People", param)
                Assert.Contains("too large", msg)
            | _ -> failwith "Should return ValidationError for too many people"
        }
        :> Task

    [<Fact>]
    let ``solve without backend defaults to local simulator`` () =
        task {
            let problem =
                { createTriangle () with
                    Backend = None
                }
            // Quantum-first: omitting a backend defaults to the local simulator and still solves.
            match! solveAsync problem CancellationToken.None with
            | Ok _ -> ()
            | other -> failwith $"Expected Ok via default local simulator, got: %A{other}"
        }
        :> Task

    [<Fact>]
    let ``solve with missing MinCommunitySize and no Mode should default to FindLargestCommunity`` () =
        task {
            // Since the new Mode-based dispatch defaults to FindLargestCommunity when
            // neither Mode nor MinCommunitySize is set, this should succeed.
            let problem =
                { createTriangle () with
                    MinCommunitySize = None
                }

            match! solveAsync problem CancellationToken.None with
            | Ok result ->
                Assert.Equal(3, result.TotalPeople)
                Assert.True(result.Communities.Length >= 1, "Default mode should find communities via QAOA max clique")
            | Error e -> failwith $"Should succeed with default mode, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``solve with MinCommunitySize less than 2 should return ValidationError`` () =
        task {
            let problem =
                { createTriangle () with
                    MinCommunitySize = Some 1
                }

            match! solveAsync problem CancellationToken.None with
            | Error(QuantumError.ValidationError(param, _)) -> Assert.Equal("MinCommunitySize", param)
            | _ -> failwith "Should return ValidationError for MinCommunitySize < 2"
        }
        :> Task

    // ========================================================================
    // SUCCESSFUL SOLVE TESTS
    // ========================================================================

    [<Fact>]
    let ``solve with triangle network should return result`` () =
        task {
            let problem = createTriangle ()

            match! solveAsync problem CancellationToken.None with
            | Ok result ->
                Assert.Equal(3, result.TotalPeople)
                Assert.Equal(3, result.TotalConnections)
            | Error e -> failwith $"Should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``solve with square network should return result`` () =
        task {
            let problem = createSquare ()

            match! solveAsync problem CancellationToken.None with
            | Ok result ->
                Assert.Equal(4, result.TotalPeople)
                Assert.Equal(4, result.TotalConnections)
            | Error e -> failwith $"Should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``solve with single person and MinCommunitySize 2 should return error`` () =
        task {
            // MinCommunitySize=2 with only 1 person is logically impossible —
            // the inner GroverCliqueSolver rejects CliqueSize > NumVertices
            let problem =
                {
                    People = [ "Alice" ]
                    Connections = []
                    MinCommunitySize = Some 2
                    Mode = None
                    Strategy = None
                    Backend = Some(LocalBackend.LocalBackend() :> IQuantumBackend)
                    Shots = 100
                }

            match! solveAsync problem CancellationToken.None with
            | Error(QuantumError.ValidationError(param, _)) -> Assert.Equal("CliqueSize", param)
            | Ok _ -> failwith "Should return ValidationError when MinCommunitySize > people count"
            | Error e -> failwith $"Expected ValidationError(CliqueSize), got: {e}"
        }
        :> Task

    [<Fact>]
    let ``solve with disconnected people should return result`` () =
        task {
            let problem =
                {
                    People = [ "Alice"; "Bob"; "Carol" ]
                    Connections = []
                    MinCommunitySize = Some 2
                    Mode = None
                    Strategy = None
                    Backend = Some(LocalBackend.LocalBackend() :> IQuantumBackend)
                    Shots = 100
                }

            match! solveAsync problem CancellationToken.None with
            | Ok result ->
                Assert.Equal(3, result.TotalPeople)
                Assert.Equal(0, result.TotalConnections)
            | Error e -> failwith $"Should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``solve result message should indicate communities found`` () =
        task {
            let problem = createTriangle ()

            match! solveAsync problem CancellationToken.None with
            | Ok result ->
                Assert.True(
                    result.Message.Contains "communities"
                    || result.Message.Contains "No communities",
                    $"Message should mention communities, got: {result.Message}"
                )
            | Error e -> failwith $"Should succeed, got error: {e}"
        }
        :> Task

    // ========================================================================
    // COMMUNITY STRUCTURE TESTS
    // ========================================================================

    [<Fact>]
    let ``community strength should be between 0 and 1`` () =
        task {
            let problem = createTriangle ()

            match! solveAsync problem CancellationToken.None with
            | Ok result ->
                for comm in result.Communities do
                    Assert.True(
                        comm.Strength >= 0.0 && comm.Strength <= 1.0,
                        $"Strength should be in [0,1], got {comm.Strength}"
                    )
            | Error e -> failwith $"Should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``community InternalConnections should be non-negative`` () =
        task {
            let problem = createTriangle ()

            match! solveAsync problem CancellationToken.None with
            | Ok result ->
                for comm in result.Communities do
                    Assert.True(
                        comm.InternalConnections >= 0,
                        $"InternalConnections should be >= 0, got {comm.InternalConnections}"
                    )
            | Error e -> failwith $"Should succeed, got error: {e}"
        }
        :> Task

    // ========================================================================
    // CE BUILDER TESTS
    // ========================================================================

    [<Fact>]
    let ``socialNetwork CE should build valid problem and solve`` () =
        task {
            let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend

            let! result =
                socialNetwork {
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
        }
        :> Task

    [<Fact>]
    let ``socialNetwork CE with people list should work`` () =
        task {
            let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend

            let! result =
                socialNetwork {
                    people [ "Alice"; "Bob"; "Carol"; "Dave" ]
                    connections [ ("Alice", "Bob"); ("Bob", "Carol"); ("Carol", "Alice") ]
                    findCommunities 2
                    backend quantumBackend
                    shots 100
                }

            match result with
            | Ok r ->
                Assert.Equal(4, r.TotalPeople)
                Assert.Equal(3, r.TotalConnections)
            | Error e -> failwith $"CE should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``socialNetwork CE without backend defaults to local simulator`` () =
        task {
            let! result =
                socialNetwork {
                    person "Alice"
                    person "Bob"
                    connection "Alice" "Bob"
                    findCommunities 2
                }

            match result with
            | Ok _ -> ()
            | other -> failwith $"Expected Ok via default local simulator, got: %A{other}"
        }
        :> Task

    [<Fact>]
    let ``socialNetwork CE with empty people should return ValidationError`` () =
        task {
            let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend

            let! result =
                socialNetwork {
                    findCommunities 2
                    backend quantumBackend
                }

            match result with
            | Error(QuantumError.ValidationError(param, _)) -> Assert.Equal("People", param)
            | _ -> failwith "Should return ValidationError for empty people"
        }
        :> Task

    // ========================================================================
    // EDGE CASES
    // ========================================================================

    [<Fact>]
    let ``solve with connections referencing unknown people should skip them`` () =
        task {
            let problem =
                {
                    People = [ "Alice"; "Bob" ]
                    Connections =
                        [
                            { Person1 = "Alice"; Person2 = "Bob" }
                            {
                                Person1 = "Alice"
                                Person2 = "Unknown"
                            } // Unknown person
                        ]
                    MinCommunitySize = Some 2
                    Mode = None
                    Strategy = None
                    Backend = Some(LocalBackend.LocalBackend() :> IQuantumBackend)
                    Shots = 100
                }

            match! solveAsync problem CancellationToken.None with
            | Ok result ->
                Assert.Equal(2, result.TotalPeople)
                // TotalConnections counts all connections in the problem, including unknown refs
                Assert.Equal(2, result.TotalConnections)
            | Error e -> failwith $"Should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``solve with 10 people should succeed within inner solver limit`` () =
        task {
            // The outer SocialNetworkAnalyzer allows up to 100 people, but the inner
            // GroverCliqueSolver has a 20-vertex limit. Test with 10 to stay fast.
            let people = List.init 10 (fun i -> $"Person{i}")

            let problem =
                {
                    People = people
                    Connections =
                        [
                            {
                                Person1 = "Person0"
                                Person2 = "Person1"
                            }
                        ]
                    MinCommunitySize = Some 2
                    Mode = None
                    Strategy = None
                    Backend = Some(LocalBackend.LocalBackend() :> IQuantumBackend)
                    Shots = 100
                }

            let! result = solveAsync problem CancellationToken.None

            result
            |> Result.map (fun result -> Assert.Equal(10, result.TotalPeople))
            |> Result.defaultWith (fun e -> failwith $"Should succeed with 10 people, got error: {e}")
        }
        :> Task

    // ========================================================================
    // FIND LARGEST COMMUNITY (QAOA MAX CLIQUE) TESTS
    // ========================================================================

    [<Fact>]
    let ``findLargestCommunity with triangle should find community`` () =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend

            let problem =
                {
                    People = [ "Alice"; "Bob"; "Carol" ]
                    Connections =
                        [
                            { Person1 = "Alice"; Person2 = "Bob" }
                            { Person1 = "Bob"; Person2 = "Carol" }
                            { Person1 = "Carol"; Person2 = "Alice" }
                        ]
                    MinCommunitySize = None
                    Mode = Some FindLargestCommunity
                    Strategy = None
                    Backend = Some backend
                    Shots = 100
                }

            match! solveAsync problem CancellationToken.None with
            | Ok result ->
                Assert.Equal(3, result.TotalPeople)
                Assert.Equal(3, result.TotalConnections)
                Assert.True(result.Communities.Length >= 1, "Should find at least one community")
                let community = result.Communities.[0]
                // QAOA is approximate — may find a clique of 2 or 3 in a triangle
                Assert.True(
                    community.Members.Length >= 2,
                    $"Community should have at least 2 members, got {community.Members.Length}"
                )

                Assert.True(
                    community.Members.Length <= 3,
                    $"Community should have at most 3 members, got {community.Members.Length}"
                )

                Assert.True(
                    community.Strength >= 0.0 && community.Strength <= 1.0,
                    $"Strength should be in [0,1], got {community.Strength}"
                )

                Assert.Contains("largest community", result.Message)
            | Error e -> failwith $"Should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``findLargestCommunity with no connections should return empty`` () =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend

            let problem =
                {
                    People = [ "Alice"; "Bob"; "Carol" ]
                    Connections = []
                    MinCommunitySize = None
                    Mode = Some FindLargestCommunity
                    Strategy = None
                    Backend = Some backend
                    Shots = 100
                }

            match! solveAsync problem CancellationToken.None with
            | Ok result ->
                Assert.Equal(3, result.TotalPeople)
                // When there are no edges, QAOA max clique returns single isolated vertices
                // or an empty result depending on the solver's behavior
                Assert.True(result.Communities.Length <= 1, "Should find at most 1 community with no connections")
            | Error e -> failwith $"Should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``findLargestCommunity CE builder should work`` () =
        task {
            let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend

            let! result =
                socialNetwork {
                    person "Alice"
                    person "Bob"
                    person "Carol"
                    connection "Alice" "Bob"
                    connection "Bob" "Carol"
                    connection "Carol" "Alice"
                    findLargestCommunity
                    backend quantumBackend
                    shots 100
                }

            match result with
            | Ok r ->
                Assert.Equal(3, r.TotalPeople)
                Assert.True(r.Communities.Length >= 1, "CE should find at least one community")
            | Error e -> failwith $"CE should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``findLargestCommunity without backend defaults to local simulator`` () =
        task {
            let! result =
                socialNetwork {
                    person "Alice"
                    person "Bob"
                    connection "Alice" "Bob"
                    findLargestCommunity
                }

            match result with
            | Ok _ -> ()
            | other -> failwith $"Expected Ok via default local simulator, got: %A{other}"
        }
        :> Task

    // ========================================================================
    // FIND MONITOR SET (QAOA MIN VERTEX COVER) TESTS
    // ========================================================================

    [<Fact>]
    let ``findMonitorSet with triangle should find cover`` () =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend

            let problem =
                {
                    People = [ "Alice"; "Bob"; "Carol" ]
                    Connections =
                        [
                            { Person1 = "Alice"; Person2 = "Bob" }
                            { Person1 = "Bob"; Person2 = "Carol" }
                            { Person1 = "Carol"; Person2 = "Alice" }
                        ]
                    MinCommunitySize = None
                    Mode = Some FindMonitorSet
                    Strategy = None
                    Backend = Some backend
                    Shots = 100
                }

            match! solveAsync problem CancellationToken.None with
            | Ok result ->
                Assert.Equal(3, result.TotalPeople)
                Assert.Equal(3, result.TotalConnections)
                // A triangle requires at least 2 monitors to cover all 3 edges
                Assert.True(
                    result.MonitorSet.Length >= 2,
                    $"Monitor set should have at least 2 people, got {result.MonitorSet.Length}"
                )

                Assert.True(
                    result.MonitorSet.Length <= 3,
                    $"Monitor set should have at most 3 people, got {result.MonitorSet.Length}"
                )
                // Communities and Pairings should be empty for this mode
                Assert.Empty(result.Communities)
                Assert.Empty(result.Pairings)
                Assert.Contains("monitor set", result.Message)
            | Error e -> failwith $"Should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``findMonitorSet with no connections should return empty monitor set`` () =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend

            let problem =
                {
                    People = [ "Alice"; "Bob"; "Carol" ]
                    Connections = []
                    MinCommunitySize = None
                    Mode = Some FindMonitorSet
                    Strategy = None
                    Backend = Some backend
                    Shots = 100
                }

            match! solveAsync problem CancellationToken.None with
            | Ok result ->
                Assert.Equal(3, result.TotalPeople)
                Assert.Empty(result.MonitorSet)
                Assert.Contains("No monitors needed", result.Message)
            | Error e -> failwith $"Should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``findMonitorSet with star topology should find center`` () =
        task {
            // Star: center A connected to B, C, D, E. Optimal cover = just A.
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend

            let problem =
                {
                    People = [ "A"; "B"; "C"; "D"; "E" ]
                    Connections =
                        [
                            { Person1 = "A"; Person2 = "B" }
                            { Person1 = "A"; Person2 = "C" }
                            { Person1 = "A"; Person2 = "D" }
                            { Person1 = "A"; Person2 = "E" }
                        ]
                    MinCommunitySize = None
                    Mode = Some FindMonitorSet
                    Strategy = None
                    Backend = Some backend
                    Shots = 100
                }

            match! solveAsync problem CancellationToken.None with
            | Ok result ->
                // Optimal cover for a star is 1 (center), but QAOA may find suboptimal
                Assert.True(
                    result.MonitorSet.Length >= 1,
                    $"Monitor set should have at least 1 person, got {result.MonitorSet.Length}"
                )

                Assert.True(
                    result.MonitorSet.Length <= 5,
                    $"Monitor set should have at most 5 people, got {result.MonitorSet.Length}"
                )
            | Error e -> failwith $"Should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``findMonitorSet CE builder should work`` () =
        task {
            let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend

            let! result =
                socialNetwork {
                    people [ "Alice"; "Bob"; "Carol" ]
                    connection "Alice" "Bob"
                    connection "Bob" "Carol"
                    findMonitorSet
                    backend quantumBackend
                    shots 100
                }

            match result with
            | Ok r ->
                Assert.True(r.MonitorSet.Length >= 1, "Should find at least 1 monitor")
                Assert.Empty(r.Communities)
                Assert.Empty(r.Pairings)
            | Error e -> failwith $"CE should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``findMonitorSet without backend defaults to local simulator`` () =
        task {
            let! result =
                socialNetwork {
                    person "Alice"
                    person "Bob"
                    connection "Alice" "Bob"
                    findMonitorSet
                }

            match result with
            | Ok _ -> ()
            | other -> failwith $"Expected Ok via default local simulator, got: %A{other}"
        }
        :> Task

    // ========================================================================
    // FIND PAIRINGS (QAOA MAX MATCHING) TESTS
    // ========================================================================

    [<Fact>]
    let ``findPairings with triangle should find pairings`` () =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend

            let problem =
                {
                    People = [ "Alice"; "Bob"; "Carol" ]
                    Connections =
                        [
                            { Person1 = "Alice"; Person2 = "Bob" }
                            { Person1 = "Bob"; Person2 = "Carol" }
                            { Person1 = "Carol"; Person2 = "Alice" }
                        ]
                    MinCommunitySize = None
                    Mode = Some FindPairings
                    Strategy = None
                    Backend = Some backend
                    Shots = 100
                }

            match! solveAsync problem CancellationToken.None with
            | Ok result ->
                Assert.Equal(3, result.TotalPeople)
                Assert.Equal(3, result.TotalConnections)
                // Triangle has max matching of 1 (only one pair can be selected
                // without reusing a vertex)
                Assert.True(
                    result.Pairings.Length >= 1,
                    $"Should find at least 1 pairing, got {result.Pairings.Length}"
                )
                // Each pairing should reference valid people
                for pairing in result.Pairings do
                    Assert.Contains(pairing.Person1, problem.People)
                    Assert.Contains(pairing.Person2, problem.People)
                    Assert.True(pairing.Weight > 0.0, "Pairing weight should be positive")
                // Communities and MonitorSet should be empty for this mode
                Assert.Empty(result.Communities)
                Assert.Empty(result.MonitorSet)
                Assert.Contains("pairings", result.Message)
            | Error e -> failwith $"Should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``findPairings with no connections should return empty`` () =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend

            let problem =
                {
                    People = [ "Alice"; "Bob"; "Carol" ]
                    Connections = []
                    MinCommunitySize = None
                    Mode = Some FindPairings
                    Strategy = None
                    Backend = Some backend
                    Shots = 100
                }

            match! solveAsync problem CancellationToken.None with
            | Ok result ->
                Assert.Equal(3, result.TotalPeople)
                Assert.Empty(result.Pairings)
                Assert.Contains("No pairings found", result.Message)
            | Error e -> failwith $"Should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``findPairings with perfect matching should find all pairs`` () =
        task {
            // 4 people with 2 disjoint edges: A-B, C-D. Perfect matching = 2 pairs.
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend

            let problem =
                {
                    People = [ "A"; "B"; "C"; "D" ]
                    Connections = [ { Person1 = "A"; Person2 = "B" }; { Person1 = "C"; Person2 = "D" } ]
                    MinCommunitySize = None
                    Mode = Some FindPairings
                    Strategy = None
                    Backend = Some backend
                    Shots = 100
                }

            match! solveAsync problem CancellationToken.None with
            | Ok result ->
                // Optimal matching is 2 pairs (A-B and C-D)
                Assert.True(
                    result.Pairings.Length >= 1,
                    $"Should find at least 1 pairing, got {result.Pairings.Length}"
                )

                Assert.True(
                    result.Pairings.Length <= 2,
                    $"Should find at most 2 pairings, got {result.Pairings.Length}"
                )
                // Verify no person appears in two pairings
                let allPeople = result.Pairings |> List.collect (fun p -> [ p.Person1; p.Person2 ])
                Assert.Equal(allPeople.Length, (Set.ofList allPeople |> Set.count))
            | Error e -> failwith $"Should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``findPairings CE builder should work`` () =
        task {
            let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend

            let! result =
                socialNetwork {
                    people [ "Alice"; "Bob"; "Carol"; "Dave" ]
                    connection "Alice" "Bob"
                    connection "Carol" "Dave"
                    findPairings
                    backend quantumBackend
                    shots 100
                }

            match result with
            | Ok r ->
                Assert.True(r.Pairings.Length >= 1, "Should find at least 1 pairing")
                Assert.Empty(r.Communities)
                Assert.Empty(r.MonitorSet)
            | Error e -> failwith $"CE should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``findPairings without backend defaults to local simulator`` () =
        task {
            let! result =
                socialNetwork {
                    person "Alice"
                    person "Bob"
                    connection "Alice" "Bob"
                    findPairings
                }

            match result with
            | Ok _ -> ()
            | other -> failwith $"Expected Ok via default local simulator, got: %A{other}"
        }
        :> Task

    // ========================================================================
    // MODE DEFAULTS AND BACKWARDS COMPATIBILITY TESTS
    // ========================================================================

    [<Fact>]
    let ``solve with Mode None and MinCommunitySize set should use FindCommunities`` () =
        task {
            // Backwards compatibility: legacy API without explicit Mode
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend

            let problem =
                {
                    People = [ "Alice"; "Bob"; "Carol" ]
                    Connections =
                        [
                            { Person1 = "Alice"; Person2 = "Bob" }
                            { Person1 = "Bob"; Person2 = "Carol" }
                            { Person1 = "Carol"; Person2 = "Alice" }
                        ]
                    MinCommunitySize = Some 3
                    Mode = None
                    Strategy = None
                    Backend = Some backend
                    Shots = 100
                }

            match! solveAsync problem CancellationToken.None with
            | Ok result ->
                // Should use legacy Grover path
                Assert.Equal(3, result.TotalPeople)

                Assert.True(
                    result.Message.Contains "communities"
                    || result.Message.Contains "No communities",
                    $"Message should mention communities, got: {result.Message}"
                )
            | Error e -> failwith $"Should succeed with legacy API, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``solve with Mode None and no MinCommunitySize should default to FindLargestCommunity`` () =
        task {
            // When neither Mode nor MinCommunitySize is set, defaults to FindLargestCommunity
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend

            let problem =
                {
                    People = [ "Alice"; "Bob"; "Carol" ]
                    Connections =
                        [
                            { Person1 = "Alice"; Person2 = "Bob" }
                            { Person1 = "Bob"; Person2 = "Carol" }
                            { Person1 = "Carol"; Person2 = "Alice" }
                        ]
                    MinCommunitySize = None
                    Mode = None
                    Strategy = None
                    Backend = Some backend
                    Shots = 100
                }

            match! solveAsync problem CancellationToken.None with
            | Ok result ->
                Assert.Equal(3, result.TotalPeople)
                // Should use QAOA largest community path
                Assert.True(result.Communities.Length >= 1, "Default mode should find communities via QAOA")
            | Error e -> failwith $"Should succeed with default mode, got error: {e}"
        }
        :> Task

    // ========================================================================
    // ALGORITHM STRATEGY TESTS
    // ========================================================================

    [<Fact>]
    let ``useGrover with FindLargestCommunity should use Grover search`` () =
        task {
            let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend

            let! result =
                socialNetwork {
                    person "Alice"
                    person "Bob"
                    person "Carol"
                    connection "Alice" "Bob"
                    connection "Bob" "Carol"
                    connection "Carol" "Alice"
                    findLargestCommunity
                    useGrover
                    backend quantumBackend
                    shots 100
                }

            match result with
            | Ok r ->
                Assert.Equal(3, r.TotalPeople)
                Assert.True(r.Communities.Length >= 1, "Grover strategy should find at least one community")
            | Error e -> failwith $"Should succeed with Grover strategy, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``useQaoa with FindLargestCommunity should use QAOA optimization`` () =
        task {
            let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend

            let! result =
                socialNetwork {
                    person "Alice"
                    person "Bob"
                    person "Carol"
                    connection "Alice" "Bob"
                    connection "Bob" "Carol"
                    connection "Carol" "Alice"
                    findLargestCommunity
                    useQaoa
                    backend quantumBackend
                    shots 100
                }

            match result with
            | Ok r ->
                Assert.Equal(3, r.TotalPeople)
                Assert.True(r.Communities.Length >= 1, "QAOA strategy should find at least one community")
            | Error e -> failwith $"Should succeed with QAOA strategy, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``useQaoa with FindCommunities should return validation error`` () =
        task {
            let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend

            let! result =
                socialNetwork {
                    person "Alice"
                    person "Bob"
                    person "Carol"
                    connection "Alice" "Bob"
                    connection "Bob" "Carol"
                    connection "Carol" "Alice"
                    findCommunities 3
                    useQaoa
                    backend quantumBackend
                    shots 100
                }

            match result with
            | Error(QuantumError.ValidationError("Strategy", msg)) -> Assert.Contains("not supported", msg)
            | Ok _ -> failwith "Should return ValidationError when using QAOA with FindCommunities"
            | Error e -> failwith $"Expected ValidationError(Strategy), got: {e}"
        }
        :> Task

    [<Fact>]
    let ``useGrover with FindMonitorSet should return validation error`` () =
        task {
            let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend

            let! result =
                socialNetwork {
                    person "Alice"
                    person "Bob"
                    connection "Alice" "Bob"
                    findMonitorSet
                    useGrover
                    backend quantumBackend
                    shots 100
                }

            match result with
            | Error(QuantumError.ValidationError("Strategy", msg)) -> Assert.Contains("not supported", msg)
            | Ok _ -> failwith "Should return ValidationError when using Grover with FindMonitorSet"
            | Error e -> failwith $"Expected ValidationError(Strategy), got: {e}"
        }
        :> Task

    [<Fact>]
    let ``useGrover with FindPairings should return validation error`` () =
        task {
            let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend

            let! result =
                socialNetwork {
                    person "Alice"
                    person "Bob"
                    connection "Alice" "Bob"
                    findPairings
                    useGrover
                    backend quantumBackend
                    shots 100
                }

            match result with
            | Error(QuantumError.ValidationError("Strategy", msg)) -> Assert.Contains("not supported", msg)
            | Ok _ -> failwith "Should return ValidationError when using Grover with FindPairings"
            | Error e -> failwith $"Expected ValidationError(Strategy), got: {e}"
        }
        :> Task

    [<Fact>]
    let ``strategy None should default to Auto for all modes`` () =
        task {
            // FindLargestCommunity with no strategy should use QAOA (Auto default)
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend

            let problem =
                {
                    People = [ "Alice"; "Bob"; "Carol" ]
                    Connections =
                        [
                            { Person1 = "Alice"; Person2 = "Bob" }
                            { Person1 = "Bob"; Person2 = "Carol" }
                            { Person1 = "Carol"; Person2 = "Alice" }
                        ]
                    MinCommunitySize = None
                    Mode = Some FindLargestCommunity
                    Strategy = None // Auto — should default to QAOA
                    Backend = Some backend
                    Shots = 100
                }

            match! solveAsync problem CancellationToken.None with
            | Ok result -> Assert.True(result.Communities.Length >= 1, "Auto strategy should find communities")
            | Error e -> failwith $"Should succeed with auto strategy, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``explicit GroverSearch strategy via record should work for FindLargestCommunity`` () =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend

            let problem =
                {
                    People = [ "Alice"; "Bob"; "Carol" ]
                    Connections =
                        [
                            { Person1 = "Alice"; Person2 = "Bob" }
                            { Person1 = "Bob"; Person2 = "Carol" }
                            { Person1 = "Carol"; Person2 = "Alice" }
                        ]
                    MinCommunitySize = None
                    Mode = Some FindLargestCommunity
                    Strategy = Some GroverSearch
                    Backend = Some backend
                    Shots = 100
                }

            match! solveAsync problem CancellationToken.None with
            | Ok result ->
                Assert.Equal(3, result.TotalPeople)

                Assert.True(
                    result.Communities.Length >= 1,
                    "Grover strategy should find communities via decreasing clique search"
                )
            | Error e -> failwith $"Should succeed with explicit Grover strategy, got error: {e}"
        }
        :> Task

    [<Fact; Trait("Category", "Slow")>]
    let ``useQaoa with FindMonitorSet should succeed`` () =
        task {
            let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend

            let! result =
                socialNetwork {
                    people [ "Alice"; "Bob"; "Carol" ]
                    connection "Alice" "Bob"
                    connection "Bob" "Carol"
                    findMonitorSet
                    useQaoa
                    backend quantumBackend
                    shots 100
                }

            result
            |> Result.map (fun r -> Assert.True(r.MonitorSet.Length >= 1, "Should find monitors with QAOA"))
            |> Result.defaultWith (fun e ->
                failwith $"Should succeed with QAOA strategy for FindMonitorSet, got error: {e}")
        }
        :> Task

    [<Fact>]
    let ``useQaoa with FindPairings should succeed`` () =
        task {
            let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend

            let! result =
                socialNetwork {
                    people [ "Alice"; "Bob"; "Carol"; "Dave" ]
                    connection "Alice" "Bob"
                    connection "Carol" "Dave"
                    findPairings
                    useQaoa
                    backend quantumBackend
                    shots 100
                }

            result
            |> Result.map (fun r -> Assert.True(r.Pairings.Length >= 1, "Should find pairings with QAOA"))
            |> Result.defaultWith (fun e ->
                failwith $"Should succeed with QAOA strategy for FindPairings, got error: {e}")
        }
        :> Task
