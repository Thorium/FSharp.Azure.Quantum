namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Business.PackingOptimizer
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends

[<Collection("NonParallel")>]
module PackingOptimizerTests =

    let localBackend () = LocalBackend.LocalBackend() :> IQuantumBackend

    // ========================================================================
    // CE BUILDER TESTS
    // ========================================================================

    [<Fact>]
    let ``PackingOptimizer CE - simple bin packing`` () =
        let result = packingOptimizer {
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
        | Error e -> Assert.Fail(sprintf "Packing optimizer failed: %A" e)

    [<Fact>]
    let ``PackingOptimizer CE - items fit in one bin`` () =
        let result = packingOptimizer {
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
        | Error e -> Assert.Fail(sprintf "Packing optimizer failed: %A" e)

    [<Fact>]
    let ``PackingOptimizer CE - custom shots`` () =
        let result = packingOptimizer {
            containerCapacity 50.0

            item "A" 25.0
            item "B" 25.0

            shots 500
            backend (localBackend ())
        }

        match result with
        | Ok r ->
            Assert.Equal(2, r.TotalItems)
        | Error e -> Assert.Fail(sprintf "Packing optimizer failed: %A" e)

    [<Fact>]
    let ``PackingOptimizer CE - multiple bins needed`` () =
        let result = packingOptimizer {
            containerCapacity 30.0

            item "Big1" 25.0
            item "Big2" 25.0

            backend (localBackend ())
        }

        match result with
        | Ok r ->
            Assert.Equal(2, r.TotalItems)
            // Each item is 25, capacity is 30, so minimum 2 bins
            Assert.True(r.BinsUsed >= 2 || r.ItemsAssigned < 2,
                "Should need at least 2 bins or not assign all")
        | Error e -> Assert.Fail(sprintf "Packing optimizer failed: %A" e)

    // ========================================================================
    // PROGRAMMATIC API TESTS
    // ========================================================================

    [<Fact>]
    let ``PackingOptimizer API - programmatic solve`` () =
        let backend = localBackend ()
        let problem = {
            Items = [
                { Id = "Item1"; Size = 30.0 }
                { Id = "Item2"; Size = 40.0 }
                { Id = "Item3"; Size = 20.0 }
            ]
            BinCapacity = 50.0
            Backend = Some backend
            Shots = 1000
        }

        let result = PackingOptimizer.solve problem

        match result with
        | Ok r ->
            Assert.Equal(3, r.TotalItems)
            Assert.True(r.BinsUsed > 0)
        | Error e -> Assert.Fail(sprintf "Programmatic solve failed: %A" e)

    // ========================================================================
    // VALIDATION ERROR TESTS
    // ========================================================================

    [<Fact>]
    let ``PackingOptimizer - empty items returns error`` () =
        let problem = {
            Items = []
            BinCapacity = 100.0
            Backend = Some (localBackend ())
            Shots = 1000
        }

        let result = PackingOptimizer.solve problem

        match result with
        | Error (QuantumError.ValidationError ("Items", _)) -> ()
        | other -> Assert.Fail(sprintf "Expected Items validation error, got: %A" other)

    [<Fact>]
    let ``PackingOptimizer - zero bin capacity returns error`` () =
        let problem = {
            Items = [{ Id = "A"; Size = 10.0 }]
            BinCapacity = 0.0
            Backend = Some (localBackend ())
            Shots = 1000
        }

        let result = PackingOptimizer.solve problem

        match result with
        | Error (QuantumError.ValidationError ("BinCapacity", _)) -> ()
        | other -> Assert.Fail(sprintf "Expected BinCapacity validation error, got: %A" other)

    [<Fact>]
    let ``PackingOptimizer - negative bin capacity returns error`` () =
        let problem = {
            Items = [{ Id = "A"; Size = 10.0 }]
            BinCapacity = -50.0
            Backend = Some (localBackend ())
            Shots = 1000
        }

        let result = PackingOptimizer.solve problem

        match result with
        | Error (QuantumError.ValidationError ("BinCapacity", _)) -> ()
        | other -> Assert.Fail(sprintf "Expected BinCapacity validation error, got: %A" other)

    [<Fact>]
    let ``PackingOptimizer - zero item size returns error`` () =
        let problem = {
            Items = [{ Id = "A"; Size = 0.0 }]
            BinCapacity = 100.0
            Backend = Some (localBackend ())
            Shots = 1000
        }

        let result = PackingOptimizer.solve problem

        match result with
        | Error (QuantumError.ValidationError ("ItemSize", _)) -> ()
        | other -> Assert.Fail(sprintf "Expected ItemSize validation error, got: %A" other)

    [<Fact>]
    let ``PackingOptimizer - negative item size returns error`` () =
        let problem = {
            Items = [{ Id = "A"; Size = -10.0 }]
            BinCapacity = 100.0
            Backend = Some (localBackend ())
            Shots = 1000
        }

        let result = PackingOptimizer.solve problem

        match result with
        | Error (QuantumError.ValidationError ("ItemSize", _)) -> ()
        | other -> Assert.Fail(sprintf "Expected ItemSize validation error, got: %A" other)

    [<Fact>]
    let ``PackingOptimizer - item exceeds bin capacity returns error`` () =
        let problem = {
            Items = [{ Id = "TooBig"; Size = 150.0 }]
            BinCapacity = 100.0
            Backend = Some (localBackend ())
            Shots = 1000
        }

        let result = PackingOptimizer.solve problem

        match result with
        | Error (QuantumError.ValidationError ("ItemSize", _)) -> ()
        | other -> Assert.Fail(sprintf "Expected ItemSize validation error, got: %A" other)

    [<Fact>]
    let ``PackingOptimizer - no backend returns error`` () =
        let problem = {
            Items = [{ Id = "A"; Size = 10.0 }]
            BinCapacity = 100.0
            Backend = None
            Shots = 1000
        }

        let result = PackingOptimizer.solve problem

        match result with
        | Error (QuantumError.NotImplemented _) -> ()
        | other -> Assert.Fail(sprintf "Expected NotImplemented error, got: %A" other)

    // ========================================================================
    // EDGE CASES
    // ========================================================================

    [<Fact>]
    let ``PackingOptimizer - single item fits in one bin`` () =
        let result = packingOptimizer {
            containerCapacity 100.0

            item "Only" 50.0

            backend (localBackend ())
        }

        match result with
        | Ok r ->
            Assert.Equal(1, r.TotalItems)
            Assert.True(r.BinsUsed >= 1)
        | Error e -> Assert.Fail(sprintf "Single item case failed: %A" e)

    [<Fact>]
    let ``PackingOptimizer - item exactly fills bin`` () =
        let problem = {
            Items = [{ Id = "Exact"; Size = 100.0 }]
            BinCapacity = 100.0
            Backend = Some (localBackend ())
            Shots = 1000
        }

        let result = PackingOptimizer.solve problem

        match result with
        | Ok r ->
            Assert.Equal(1, r.TotalItems)
            Assert.True(r.BinsUsed >= 1)
        | Error e -> Assert.Fail(sprintf "Exact fit case failed: %A" e)
