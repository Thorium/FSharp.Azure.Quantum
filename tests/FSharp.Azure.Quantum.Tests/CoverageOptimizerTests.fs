namespace FSharp.Azure.Quantum.Tests

open System.Threading
open System.Threading.Tasks
open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Business.CoverageOptimizer
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends

[<Collection("NonParallel")>]
module CoverageOptimizerTests =

    let localBackend () =
        LocalBackend.LocalBackend() :> IQuantumBackend

    // ========================================================================
    // CE BUILDER TESTS
    // ========================================================================

    [<Fact>]
    let ``CoverageOptimizer CE - simple shift coverage`` () =
        task {
            let! result =
                coverageOptimizer {
                    universeSize 3

                    option "MorningShift" [ 0; 1 ] 25.0
                    option "AfternoonShift" [ 1; 2 ] 20.0
                    option "FullDay" [ 0; 1; 2 ] 40.0

                    backend (localBackend ())
                }

            match result with
            | Ok r ->
                Assert.True(r.ElementsCovered > 0, "Should cover at least some elements")
                Assert.True(r.TotalCost > 0.0, "Cost should be positive")
                Assert.True(r.SelectedOptions.Length > 0, "Should select at least one option")
            | Error e -> Assert.Fail($"Coverage optimizer failed: %A{e}")
        }
        :> Task

    [<Fact>]
    let ``CoverageOptimizer CE - element auto-expands universe`` () =
        task {
            let! result =
                coverageOptimizer {
                    element 0
                    element 1
                    element 2

                    option "A" [ 0; 1 ] 10.0
                    option "B" [ 1; 2 ] 10.0

                    backend (localBackend ())
                }

            result
            |> Result.map (fun r -> Assert.Equal(3, r.TotalElements))
            |> Result.defaultWith (fun e -> Assert.Fail($"Coverage optimizer failed: %A{e}"))
        }
        :> Task

    [<Fact>]
    let ``CoverageOptimizer CE - single option covers everything`` () =
        task {
            let! result =
                coverageOptimizer {
                    universeSize 2

                    option "AllInOne" [ 0; 1 ] 15.0

                    backend (localBackend ())
                }

            match result with
            | Ok r ->
                Assert.True(r.SelectedOptions.Length >= 1, "Should select the only option")
                Assert.True(r.TotalCost >= 15.0, "Cost should include AllInOne")
            | Error e -> Assert.Fail($"Coverage optimizer failed: %A{e}")
        }
        :> Task

    [<Fact>]
    let ``CoverageOptimizer CE - custom shots`` () =
        task {
            let! result =
                coverageOptimizer {
                    universeSize 2

                    option "A" [ 0 ] 5.0
                    option "B" [ 1 ] 5.0

                    shots 500
                    backend (localBackend ())
                }

            result
            |> Result.map (fun r -> Assert.True(r.SelectedOptions.Length > 0))
            |> Result.defaultWith (fun e -> Assert.Fail($"Coverage optimizer failed: %A{e}"))
        }
        :> Task

    // ========================================================================
    // PROGRAMMATIC API TESTS
    // ========================================================================

    [<Fact>]
    let ``CoverageOptimizer API - programmatic solve`` () =
        task {
            let backend = localBackend ()

            let problem =
                {
                    UniverseSize = 3
                    Options =
                        [
                            {
                                Id = "S1"
                                CoveredElements = [ 0; 1 ]
                                Cost = 10.0
                            }
                            {
                                Id = "S2"
                                CoveredElements = [ 1; 2 ]
                                Cost = 10.0
                            }
                            {
                                Id = "S3"
                                CoveredElements = [ 0; 1; 2 ]
                                Cost = 18.0
                            }
                        ]
                    Backend = Some backend
                    Shots = 1000
                }

            let! result = CoverageOptimizer.solveAsync problem CancellationToken.None

            match result with
            | Ok r ->
                Assert.True(r.SelectedOptions.Length > 0)
                Assert.True(r.TotalCost > 0.0)
            | Error e -> Assert.Fail($"Programmatic solve failed: %A{e}")
        }
        :> Task

    // ========================================================================
    // VALIDATION ERROR TESTS
    // ========================================================================

    [<Fact>]
    let ``CoverageOptimizer - empty universe returns error`` () =
        task {
            let problem =
                {
                    UniverseSize = 0
                    Options =
                        [
                            {
                                Id = "A"
                                CoveredElements = []
                                Cost = 1.0
                            }
                        ]
                    Backend = Some(localBackend ())
                    Shots = 1000
                }

            let! result = CoverageOptimizer.solveAsync problem CancellationToken.None

            match result with
            | Error(QuantumError.ValidationError("UniverseSize", _)) -> ()
            | other -> Assert.Fail($"Expected UniverseSize validation error, got: %A{other}")
        }
        :> Task

    [<Fact>]
    let ``CoverageOptimizer - negative universe size returns error`` () =
        task {
            let problem =
                {
                    UniverseSize = -1
                    Options =
                        [
                            {
                                Id = "A"
                                CoveredElements = []
                                Cost = 1.0
                            }
                        ]
                    Backend = Some(localBackend ())
                    Shots = 1000
                }

            let! result = CoverageOptimizer.solveAsync problem CancellationToken.None

            match result with
            | Error(QuantumError.ValidationError("UniverseSize", _)) -> ()
            | other -> Assert.Fail($"Expected UniverseSize validation error, got: %A{other}")
        }
        :> Task

    [<Fact>]
    let ``CoverageOptimizer - empty options returns error`` () =
        task {
            let problem =
                {
                    UniverseSize = 3
                    Options = []
                    Backend = Some(localBackend ())
                    Shots = 1000
                }

            let! result = CoverageOptimizer.solveAsync problem CancellationToken.None

            match result with
            | Error(QuantumError.ValidationError("Options", _)) -> ()
            | other -> Assert.Fail($"Expected Options validation error, got: %A{other}")
        }
        :> Task

    [<Fact>]
    let ``CoverageOptimizer - negative cost returns error`` () =
        task {
            let problem =
                {
                    UniverseSize = 2
                    Options =
                        [
                            {
                                Id = "A"
                                CoveredElements = [ 0 ]
                                Cost = -5.0
                            }
                        ]
                    Backend = Some(localBackend ())
                    Shots = 1000
                }

            let! result = CoverageOptimizer.solveAsync problem CancellationToken.None

            match result with
            | Error(QuantumError.ValidationError("Cost", _)) -> ()
            | other -> Assert.Fail($"Expected Cost validation error, got: %A{other}")
        }
        :> Task

    [<Fact>]
    let ``CoverageOptimizer - out of range element index returns error`` () =
        task {
            let problem =
                {
                    UniverseSize = 2
                    Options =
                        [
                            {
                                Id = "A"
                                CoveredElements = [ 0; 5 ]
                                Cost = 10.0
                            }
                        ]
                    Backend = Some(localBackend ())
                    Shots = 1000
                }

            let! result = CoverageOptimizer.solveAsync problem CancellationToken.None

            match result with
            | Error(QuantumError.ValidationError("CoveredElements", _)) -> ()
            | other -> Assert.Fail($"Expected CoveredElements validation error, got: %A{other}")
        }
        :> Task

    [<Fact>]
    let ``CoverageOptimizer - negative element index returns error`` () =
        task {
            let problem =
                {
                    UniverseSize = 2
                    Options =
                        [
                            {
                                Id = "A"
                                CoveredElements = [ -1; 0 ]
                                Cost = 10.0
                            }
                        ]
                    Backend = Some(localBackend ())
                    Shots = 1000
                }

            let! result = CoverageOptimizer.solveAsync problem CancellationToken.None

            match result with
            | Error(QuantumError.ValidationError("CoveredElements", _)) -> ()
            | other -> Assert.Fail($"Expected CoveredElements validation error, got: %A{other}")
        }
        :> Task

    [<Fact>]
    let ``CoverageOptimizer - no backend defaults to local simulator`` () =
        task {
            let problem =
                {
                    UniverseSize = 2
                    Options =
                        [
                            {
                                Id = "A"
                                CoveredElements = [ 0; 1 ]
                                Cost = 10.0
                            }
                        ]
                    Backend = None
                    Shots = 1000
                }

            // Quantum-first: omitting a backend defaults to the local simulator (a real quantum
            // backend) and still solves — it must not short-circuit with NotImplemented.
            match! CoverageOptimizer.solveAsync problem CancellationToken.None with
            | Ok _ -> ()
            | other -> Assert.Fail($"Expected Ok via default local simulator, got: %A{other}")
        }
        :> Task

    // ========================================================================
    // EDGE CASES
    // ========================================================================

    [<Fact>]
    let ``CoverageOptimizer - single element single option`` () =
        task {
            let! result =
                coverageOptimizer {
                    universeSize 1

                    option "Only" [ 0 ] 5.0

                    backend (localBackend ())
                }

            match result with
            | Ok r ->
                Assert.Equal(1, r.TotalElements)
                Assert.True(r.SelectedOptions.Length >= 1)
            | Error e -> Assert.Fail($"Single element case failed: %A{e}")
        }
        :> Task

    [<Fact>]
    let ``CoverageOptimizer - zero cost option`` () =
        task {
            let problem =
                {
                    UniverseSize = 1
                    Options =
                        [
                            {
                                Id = "Free"
                                CoveredElements = [ 0 ]
                                Cost = 0.0
                            }
                        ]
                    Backend = Some(localBackend ())
                    Shots = 1000
                }

            let! result = CoverageOptimizer.solveAsync problem CancellationToken.None

            match result with
            | Ok r ->
                Assert.True(r.SelectedOptions.Length >= 1)
                Assert.True(r.TotalCost >= 0.0)
            | Error e -> Assert.Fail($"Zero cost case failed: %A{e}")
        }
        :> Task
