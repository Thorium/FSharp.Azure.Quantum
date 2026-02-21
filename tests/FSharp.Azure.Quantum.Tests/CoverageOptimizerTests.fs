namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Business.CoverageOptimizer
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends

[<Collection("NonParallel")>]
module CoverageOptimizerTests =

    let localBackend () = LocalBackend.LocalBackend() :> IQuantumBackend

    // ========================================================================
    // CE BUILDER TESTS
    // ========================================================================

    [<Fact>]
    let ``CoverageOptimizer CE - simple shift coverage`` () =
        let result = coverageOptimizer {
            universeSize 3

            option "MorningShift" [0; 1] 25.0
            option "AfternoonShift" [1; 2] 20.0
            option "FullDay" [0; 1; 2] 40.0

            backend (localBackend ())
        }

        match result with
        | Ok r ->
            Assert.True(r.ElementsCovered > 0, "Should cover at least some elements")
            Assert.True(r.TotalCost > 0.0, "Cost should be positive")
            Assert.True(r.SelectedOptions.Length > 0, "Should select at least one option")
        | Error e -> Assert.Fail(sprintf "Coverage optimizer failed: %A" e)

    [<Fact>]
    let ``CoverageOptimizer CE - element auto-expands universe`` () =
        let result = coverageOptimizer {
            element 0
            element 1
            element 2

            option "A" [0; 1] 10.0
            option "B" [1; 2] 10.0

            backend (localBackend ())
        }

        match result with
        | Ok r ->
            Assert.Equal(3, r.TotalElements)
        | Error e -> Assert.Fail(sprintf "Coverage optimizer failed: %A" e)

    [<Fact>]
    let ``CoverageOptimizer CE - single option covers everything`` () =
        let result = coverageOptimizer {
            universeSize 2

            option "AllInOne" [0; 1] 15.0

            backend (localBackend ())
        }

        match result with
        | Ok r ->
            Assert.True(r.SelectedOptions.Length >= 1, "Should select the only option")
            Assert.True(r.TotalCost >= 15.0, "Cost should include AllInOne")
        | Error e -> Assert.Fail(sprintf "Coverage optimizer failed: %A" e)

    [<Fact>]
    let ``CoverageOptimizer CE - custom shots`` () =
        let result = coverageOptimizer {
            universeSize 2

            option "A" [0] 5.0
            option "B" [1] 5.0

            shots 500
            backend (localBackend ())
        }

        match result with
        | Ok r ->
            Assert.True(r.SelectedOptions.Length > 0)
        | Error e -> Assert.Fail(sprintf "Coverage optimizer failed: %A" e)

    // ========================================================================
    // PROGRAMMATIC API TESTS
    // ========================================================================

    [<Fact>]
    let ``CoverageOptimizer API - programmatic solve`` () =
        let backend = localBackend ()
        let problem = {
            UniverseSize = 3
            Options = [
                { Id = "S1"; CoveredElements = [0; 1]; Cost = 10.0 }
                { Id = "S2"; CoveredElements = [1; 2]; Cost = 10.0 }
                { Id = "S3"; CoveredElements = [0; 1; 2]; Cost = 18.0 }
            ]
            Backend = Some backend
            Shots = 1000
        }

        let result = CoverageOptimizer.solve problem

        match result with
        | Ok r ->
            Assert.True(r.SelectedOptions.Length > 0)
            Assert.True(r.TotalCost > 0.0)
        | Error e -> Assert.Fail(sprintf "Programmatic solve failed: %A" e)

    // ========================================================================
    // VALIDATION ERROR TESTS
    // ========================================================================

    [<Fact>]
    let ``CoverageOptimizer - empty universe returns error`` () =
        let problem = {
            UniverseSize = 0
            Options = [{ Id = "A"; CoveredElements = []; Cost = 1.0 }]
            Backend = Some (localBackend ())
            Shots = 1000
        }

        let result = CoverageOptimizer.solve problem

        match result with
        | Error (QuantumError.ValidationError ("UniverseSize", _)) -> ()
        | other -> Assert.Fail(sprintf "Expected UniverseSize validation error, got: %A" other)

    [<Fact>]
    let ``CoverageOptimizer - negative universe size returns error`` () =
        let problem = {
            UniverseSize = -1
            Options = [{ Id = "A"; CoveredElements = []; Cost = 1.0 }]
            Backend = Some (localBackend ())
            Shots = 1000
        }

        let result = CoverageOptimizer.solve problem

        match result with
        | Error (QuantumError.ValidationError ("UniverseSize", _)) -> ()
        | other -> Assert.Fail(sprintf "Expected UniverseSize validation error, got: %A" other)

    [<Fact>]
    let ``CoverageOptimizer - empty options returns error`` () =
        let problem = {
            UniverseSize = 3
            Options = []
            Backend = Some (localBackend ())
            Shots = 1000
        }

        let result = CoverageOptimizer.solve problem

        match result with
        | Error (QuantumError.ValidationError ("Options", _)) -> ()
        | other -> Assert.Fail(sprintf "Expected Options validation error, got: %A" other)

    [<Fact>]
    let ``CoverageOptimizer - negative cost returns error`` () =
        let problem = {
            UniverseSize = 2
            Options = [{ Id = "A"; CoveredElements = [0]; Cost = -5.0 }]
            Backend = Some (localBackend ())
            Shots = 1000
        }

        let result = CoverageOptimizer.solve problem

        match result with
        | Error (QuantumError.ValidationError ("Cost", _)) -> ()
        | other -> Assert.Fail(sprintf "Expected Cost validation error, got: %A" other)

    [<Fact>]
    let ``CoverageOptimizer - out of range element index returns error`` () =
        let problem = {
            UniverseSize = 2
            Options = [{ Id = "A"; CoveredElements = [0; 5]; Cost = 10.0 }]
            Backend = Some (localBackend ())
            Shots = 1000
        }

        let result = CoverageOptimizer.solve problem

        match result with
        | Error (QuantumError.ValidationError ("CoveredElements", _)) -> ()
        | other -> Assert.Fail(sprintf "Expected CoveredElements validation error, got: %A" other)

    [<Fact>]
    let ``CoverageOptimizer - negative element index returns error`` () =
        let problem = {
            UniverseSize = 2
            Options = [{ Id = "A"; CoveredElements = [-1; 0]; Cost = 10.0 }]
            Backend = Some (localBackend ())
            Shots = 1000
        }

        let result = CoverageOptimizer.solve problem

        match result with
        | Error (QuantumError.ValidationError ("CoveredElements", _)) -> ()
        | other -> Assert.Fail(sprintf "Expected CoveredElements validation error, got: %A" other)

    [<Fact>]
    let ``CoverageOptimizer - no backend returns error`` () =
        let problem = {
            UniverseSize = 2
            Options = [{ Id = "A"; CoveredElements = [0; 1]; Cost = 10.0 }]
            Backend = None
            Shots = 1000
        }

        let result = CoverageOptimizer.solve problem

        match result with
        | Error (QuantumError.NotImplemented _) -> ()
        | other -> Assert.Fail(sprintf "Expected NotImplemented error, got: %A" other)

    // ========================================================================
    // EDGE CASES
    // ========================================================================

    [<Fact>]
    let ``CoverageOptimizer - single element single option`` () =
        let result = coverageOptimizer {
            universeSize 1

            option "Only" [0] 5.0

            backend (localBackend ())
        }

        match result with
        | Ok r ->
            Assert.Equal(1, r.TotalElements)
            Assert.True(r.SelectedOptions.Length >= 1)
        | Error e -> Assert.Fail(sprintf "Single element case failed: %A" e)

    [<Fact>]
    let ``CoverageOptimizer - zero cost option`` () =
        let problem = {
            UniverseSize = 1
            Options = [{ Id = "Free"; CoveredElements = [0]; Cost = 0.0 }]
            Backend = Some (localBackend ())
            Shots = 1000
        }

        let result = CoverageOptimizer.solve problem

        match result with
        | Ok r ->
            Assert.True(r.SelectedOptions.Length >= 1)
            Assert.True(r.TotalCost >= 0.0)
        | Error e -> Assert.Fail(sprintf "Zero cost case failed: %A" e)
