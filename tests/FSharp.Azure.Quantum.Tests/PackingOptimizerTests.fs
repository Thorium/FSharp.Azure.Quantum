namespace FSharp.Azure.Quantum.Tests

open System.Threading
open System.Threading.Tasks
open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Business.PackingOptimizer
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends

[<Collection("NonParallel")>]
module PackingOptimizerTests =

    let localBackend () =
        LocalBackend.LocalBackend() :> IQuantumBackend

    // ========================================================================
    // CE BUILDER TESTS
    // ========================================================================

    [<Fact; Trait("Category", "Slow")>]
    let ``PackingOptimizer CE - simple bin packing`` () =
        task {
            let! result =
                packingOptimizer {
                    containerCapacity 100.0

                    item "Crate-A" 45.0
                    item "Crate-B" 35.0
                    item "Crate-C" 25.0

                    backend (localBackend ())
                }

            match result with
            | Ok r ->
                Assert.True(r.ItemsAssigned > 0, "Should assign at least some items")
                Assert.True(r.BinsUsed > 0, "Should use at least one bin")
                Assert.Equal(3, r.TotalItems)
            | Error e -> Assert.Fail($"Packing optimizer failed: %A{e}")
        }
        :> Task

    [<Fact; Trait("Category", "Slow")>]
    let ``PackingOptimizer CE - items fit in one bin`` () =
        task {
            let! result =
                packingOptimizer {
                    containerCapacity 100.0

                    item "Small1" 10.0
                    item "Small2" 20.0
                    item "Small3" 15.0

                    backend (localBackend ())
                }

            match result with
            | Ok r ->
                Assert.Equal(3, r.TotalItems)
                Assert.True(r.BinsUsed >= 1, "Should use at least one bin")
            | Error e -> Assert.Fail($"Packing optimizer failed: %A{e}")
        }
        :> Task

    [<Fact>]
    let ``PackingOptimizer CE - custom shots`` () =
        task {
            let! result =
                packingOptimizer {
                    containerCapacity 50.0

                    item "A" 25.0
                    item "B" 25.0

                    shots 500
                    backend (localBackend ())
                }

            result
            |> Result.map (fun r -> Assert.Equal(2, r.TotalItems))
            |> Result.defaultWith (fun e -> Assert.Fail($"Packing optimizer failed: %A{e}"))
        }
        :> Task

    [<Fact>]
    let ``PackingOptimizer CE - multiple bins needed`` () =
        task {
            let! result =
                packingOptimizer {
                    containerCapacity 30.0

                    item "Big1" 25.0
                    item "Big2" 25.0

                    backend (localBackend ())
                }

            match result with
            | Ok r ->
                Assert.Equal(2, r.TotalItems)
                // Each item is 25, capacity is 30, so minimum 2 bins
                Assert.True(r.BinsUsed >= 2 || r.ItemsAssigned < 2, "Should need at least 2 bins or not assign all")
            | Error e -> Assert.Fail($"Packing optimizer failed: %A{e}")
        }
        :> Task

    // ========================================================================
    // PROGRAMMATIC API TESTS
    // ========================================================================

    [<Fact; Trait("Category", "Slow")>]
    let ``PackingOptimizer API - programmatic solve`` () =
        task {
            let backend = localBackend ()

            let problem =
                {
                    Items =
                        [
                            { Id = "Item1"; Size = 30.0 }
                            { Id = "Item2"; Size = 40.0 }
                            { Id = "Item3"; Size = 20.0 }
                        ]
                    BinCapacity = 50.0
                    Backend = Some backend
                    Shots = 1000
                }

            match! PackingOptimizer.solveAsync problem CancellationToken.None with
            | Ok r ->
                Assert.Equal(3, r.TotalItems)
                Assert.True(r.BinsUsed > 0)
            | Error e -> Assert.Fail($"Programmatic solve failed: %A{e}")
        }
        :> Task

    // ========================================================================
    // VALIDATION ERROR TESTS
    // ========================================================================

    [<Fact>]
    let ``PackingOptimizer - empty items returns error`` () =
        task {
            let problem =
                {
                    Items = []
                    BinCapacity = 100.0
                    Backend = Some(localBackend ())
                    Shots = 1000
                }

            match! PackingOptimizer.solveAsync problem CancellationToken.None with
            | Error(QuantumError.ValidationError("Items", _)) -> ()
            | other -> Assert.Fail($"Expected Items validation error, got: %A{other}")
        }
        :> Task

    [<Fact>]
    let ``PackingOptimizer - zero bin capacity returns error`` () =
        task {
            let problem =
                {
                    Items = [ { Id = "A"; Size = 10.0 } ]
                    BinCapacity = 0.0
                    Backend = Some(localBackend ())
                    Shots = 1000
                }

            match! PackingOptimizer.solveAsync problem CancellationToken.None with
            | Error(QuantumError.ValidationError("BinCapacity", _)) -> ()
            | other -> Assert.Fail($"Expected BinCapacity validation error, got: %A{other}")
        }
        :> Task

    [<Fact>]
    let ``PackingOptimizer - negative bin capacity returns error`` () =
        task {
            let problem =
                {
                    Items = [ { Id = "A"; Size = 10.0 } ]
                    BinCapacity = -50.0
                    Backend = Some(localBackend ())
                    Shots = 1000
                }

            match! PackingOptimizer.solveAsync problem CancellationToken.None with
            | Error(QuantumError.ValidationError("BinCapacity", _)) -> ()
            | other -> Assert.Fail($"Expected BinCapacity validation error, got: %A{other}")
        }
        :> Task

    [<Fact>]
    let ``PackingOptimizer - zero item size returns error`` () =
        task {
            let problem =
                {
                    Items = [ { Id = "A"; Size = 0.0 } ]
                    BinCapacity = 100.0
                    Backend = Some(localBackend ())
                    Shots = 1000
                }

            match! PackingOptimizer.solveAsync problem CancellationToken.None with
            | Error(QuantumError.ValidationError("ItemSize", _)) -> ()
            | other -> Assert.Fail($"Expected ItemSize validation error, got: %A{other}")
        }
        :> Task

    [<Fact>]
    let ``PackingOptimizer - negative item size returns error`` () =
        task {
            let problem =
                {
                    Items = [ { Id = "A"; Size = -10.0 } ]
                    BinCapacity = 100.0
                    Backend = Some(localBackend ())
                    Shots = 1000
                }

            match! PackingOptimizer.solveAsync problem CancellationToken.None with
            | Error(QuantumError.ValidationError("ItemSize", _)) -> ()
            | other -> Assert.Fail($"Expected ItemSize validation error, got: %A{other}")
        }
        :> Task

    [<Fact>]
    let ``PackingOptimizer - item exceeds bin capacity returns error`` () =
        task {
            let problem =
                {
                    Items = [ { Id = "TooBig"; Size = 150.0 } ]
                    BinCapacity = 100.0
                    Backend = Some(localBackend ())
                    Shots = 1000
                }

            match! PackingOptimizer.solveAsync problem CancellationToken.None with
            | Error(QuantumError.ValidationError("ItemSize", _)) -> ()
            | other -> Assert.Fail($"Expected ItemSize validation error, got: %A{other}")
        }
        :> Task

    [<Fact>]
    let ``PackingOptimizer - no backend defaults to local simulator`` () =
        task {
            let problem =
                {
                    Items = [ { Id = "A"; Size = 10.0 } ]
                    BinCapacity = 100.0
                    Backend = None
                    Shots = 1000
                }

            // Quantum-first: omitting a backend defaults to the local simulator (a real quantum
            // backend) and still solves — it must not short-circuit with NotImplemented.
            match! PackingOptimizer.solveAsync problem CancellationToken.None with
            | Ok _ -> ()
            | other -> Assert.Fail($"Expected Ok via default local simulator, got: %A{other}")
        }
        :> Task

    // ========================================================================
    // EDGE CASES
    // ========================================================================

    [<Fact>]
    let ``PackingOptimizer - single item fits in one bin`` () =
        task {
            let! result =
                packingOptimizer {
                    containerCapacity 100.0

                    item "Only" 50.0

                    backend (localBackend ())
                }

            match result with
            | Ok r ->
                Assert.Equal(1, r.TotalItems)
                Assert.True(r.BinsUsed >= 1)
            | Error e -> Assert.Fail($"Single item case failed: %A{e}")
        }
        :> Task

    [<Fact>]
    let ``PackingOptimizer - item exactly fills bin`` () =
        task {
            let problem =
                {
                    Items = [ { Id = "Exact"; Size = 100.0 } ]
                    BinCapacity = 100.0
                    Backend = Some(localBackend ())
                    Shots = 1000
                }

            match! PackingOptimizer.solveAsync problem CancellationToken.None with
            | Ok r ->
                Assert.Equal(1, r.TotalItems)
                Assert.True(r.BinsUsed >= 1)
            | Error e -> Assert.Fail($"Exact fit case failed: %A{e}")
        }
        :> Task
