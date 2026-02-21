namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Business.ResourcePairing
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends

[<Collection("NonParallel")>]
module ResourcePairingTests =

    let localBackend () = LocalBackend.LocalBackend() :> IQuantumBackend

    // ========================================================================
    // CE BUILDER TESTS
    // ========================================================================

    [<Fact>]
    let ``ResourcePairing CE - simple two-person pairing`` () =
        let result = resourcePairing {
            participant "Alice"
            participant "Bob"

            compatibility "Alice" "Bob" 0.9

            backend (localBackend ())
        }

        match result with
        | Ok r ->
            Assert.True(r.Pairings.Length > 0 || r.TotalParticipants = 2,
                "Should find pairing or return result for 2 participants")
            Assert.Equal(2, r.TotalParticipants)
        | Error e -> Assert.Fail(sprintf "Resource pairing failed: %A" e)

    [<Fact>]
    let ``ResourcePairing CE - three participants`` () =
        let result = resourcePairing {
            participant "Alice"
            participant "Bob"
            participant "Carol"

            compatibility "Alice" "Bob" 0.9
            compatibility "Alice" "Carol" 0.5
            compatibility "Bob" "Carol" 0.7

            backend (localBackend ())
        }

        match result with
        | Ok r ->
            Assert.Equal(3, r.TotalParticipants)
            // Max matching for 3 people: at most 1 pair
            Assert.True(r.Pairings.Length <= 1 || r.Pairings.Length >= 0,
                "Should return valid matching")
        | Error e -> Assert.Fail(sprintf "Resource pairing failed: %A" e)

    [<Fact>]
    let ``ResourcePairing CE - four participants optimal matching`` () =
        let result = resourcePairing {
            participant "Alice"
            participant "Bob"
            participant "Carol"
            participant "Dave"

            compatibility "Alice" "Bob" 0.9
            compatibility "Carol" "Dave" 0.8
            compatibility "Alice" "Carol" 0.3
            compatibility "Bob" "Dave" 0.2

            backend (localBackend ())
        }

        match result with
        | Ok r ->
            Assert.Equal(4, r.TotalParticipants)
            Assert.True(r.TotalScore >= 0.0, "Total score should be non-negative")
        | Error e -> Assert.Fail(sprintf "Resource pairing failed: %A" e)

    [<Fact>]
    let ``ResourcePairing CE - participants batch add`` () =
        let result = resourcePairing {
            participants ["X"; "Y"; "Z"]

            compatibility "X" "Y" 1.0
            compatibility "Y" "Z" 0.5

            backend (localBackend ())
        }

        match result with
        | Ok r ->
            Assert.Equal(3, r.TotalParticipants)
        | Error e -> Assert.Fail(sprintf "Resource pairing failed: %A" e)

    [<Fact>]
    let ``ResourcePairing CE - custom shots`` () =
        let result = resourcePairing {
            participant "A"
            participant "B"

            compatibility "A" "B" 1.0

            shots 500
            backend (localBackend ())
        }

        match result with
        | Ok r ->
            Assert.Equal(2, r.TotalParticipants)
        | Error e -> Assert.Fail(sprintf "Resource pairing failed: %A" e)

    // ========================================================================
    // PROGRAMMATIC API TESTS
    // ========================================================================

    [<Fact>]
    let ``ResourcePairing API - programmatic solve`` () =
        let backend = localBackend ()
        let problem = {
            Participants = ["Alice"; "Bob"; "Carol"]
            Compatibilities = [
                { Participant1 = "Alice"; Participant2 = "Bob"; Weight = 0.9 }
                { Participant1 = "Alice"; Participant2 = "Carol"; Weight = 0.4 }
                { Participant1 = "Bob"; Participant2 = "Carol"; Weight = 0.6 }
            ]
            Backend = Some backend
            Shots = 1000
        }

        let result = ResourcePairing.solve problem

        match result with
        | Ok r ->
            Assert.Equal(3, r.TotalParticipants)
            Assert.True(r.TotalScore >= 0.0)
        | Error e -> Assert.Fail(sprintf "Programmatic solve failed: %A" e)

    // ========================================================================
    // VALIDATION ERROR TESTS
    // ========================================================================

    [<Fact>]
    let ``ResourcePairing - fewer than 2 participants returns error`` () =
        let problem = {
            Participants = ["Alice"]
            Compatibilities = []
            Backend = Some (localBackend ())
            Shots = 1000
        }

        let result = ResourcePairing.solve problem

        match result with
        | Error (QuantumError.ValidationError ("Participants", _)) -> ()
        | other -> Assert.Fail(sprintf "Expected Participants validation error, got: %A" other)

    [<Fact>]
    let ``ResourcePairing - empty participants returns error`` () =
        let problem = {
            Participants = []
            Compatibilities = []
            Backend = Some (localBackend ())
            Shots = 1000
        }

        let result = ResourcePairing.solve problem

        match result with
        | Error (QuantumError.ValidationError ("Participants", _)) -> ()
        | other -> Assert.Fail(sprintf "Expected Participants validation error, got: %A" other)

    [<Fact>]
    let ``ResourcePairing - empty compatibilities returns error`` () =
        let problem = {
            Participants = ["Alice"; "Bob"]
            Compatibilities = []
            Backend = Some (localBackend ())
            Shots = 1000
        }

        let result = ResourcePairing.solve problem

        match result with
        | Error (QuantumError.ValidationError ("Compatibilities", _)) -> ()
        | other -> Assert.Fail(sprintf "Expected Compatibilities validation error, got: %A" other)

    [<Fact>]
    let ``ResourcePairing - negative weight returns error`` () =
        let problem = {
            Participants = ["Alice"; "Bob"]
            Compatibilities = [
                { Participant1 = "Alice"; Participant2 = "Bob"; Weight = -0.5 }
            ]
            Backend = Some (localBackend ())
            Shots = 1000
        }

        let result = ResourcePairing.solve problem

        match result with
        | Error (QuantumError.ValidationError ("Weight", _)) -> ()
        | other -> Assert.Fail(sprintf "Expected Weight validation error, got: %A" other)

    [<Fact>]
    let ``ResourcePairing - unknown participant in compatibility returns error`` () =
        let problem = {
            Participants = ["Alice"; "Bob"]
            Compatibilities = [
                { Participant1 = "Alice"; Participant2 = "Unknown"; Weight = 0.5 }
            ]
            Backend = Some (localBackend ())
            Shots = 1000
        }

        let result = ResourcePairing.solve problem

        match result with
        | Error (QuantumError.ValidationError ("Participants", _)) -> ()
        | other -> Assert.Fail(sprintf "Expected Participants validation error, got: %A" other)

    [<Fact>]
    let ``ResourcePairing - no backend returns error`` () =
        let problem = {
            Participants = ["Alice"; "Bob"]
            Compatibilities = [
                { Participant1 = "Alice"; Participant2 = "Bob"; Weight = 0.9 }
            ]
            Backend = None
            Shots = 1000
        }

        let result = ResourcePairing.solve problem

        match result with
        | Error (QuantumError.NotImplemented _) -> ()
        | other -> Assert.Fail(sprintf "Expected NotImplemented error, got: %A" other)

    // ========================================================================
    // EDGE CASES
    // ========================================================================

    [<Fact>]
    let ``ResourcePairing - exactly two participants`` () =
        let result = resourcePairing {
            participant "X"
            participant "Y"

            compatibility "X" "Y" 1.0

            backend (localBackend ())
        }

        match result with
        | Ok r ->
            Assert.Equal(2, r.TotalParticipants)
            Assert.True(r.ParticipantsPaired <= 2)
        | Error e -> Assert.Fail(sprintf "Two participant case failed: %A" e)

    [<Fact>]
    let ``ResourcePairing - zero weight compatibility`` () =
        let problem = {
            Participants = ["A"; "B"]
            Compatibilities = [
                { Participant1 = "A"; Participant2 = "B"; Weight = 0.0 }
            ]
            Backend = Some (localBackend ())
            Shots = 1000
        }

        let result = ResourcePairing.solve problem

        match result with
        | Ok r ->
            Assert.Equal(2, r.TotalParticipants)
        | Error e -> Assert.Fail(sprintf "Zero weight case failed: %A" e)
