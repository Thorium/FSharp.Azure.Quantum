namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core

module ValidationTests =

    // ========================================================================
    // SUCCESS / FAILURE CONSTRUCTORS
    // ========================================================================

    [<Fact>]
    let ``success creates valid result with no messages`` () =
        let r = Validation.success
        Assert.True(r.IsValid)
        Assert.Empty(r.Messages)

    [<Fact>]
    let ``failure creates invalid result with messages`` () =
        let r = Validation.failure ["err1"; "err2"]
        Assert.False(r.IsValid)
        Assert.Equal(2, r.Messages.Length)
        Assert.Contains("err1", r.Messages)
        Assert.Contains("err2", r.Messages)

    [<Fact>]
    let ``failure with empty list creates invalid result`` () =
        let r = Validation.failure []
        Assert.False(r.IsValid)
        Assert.Empty(r.Messages)

    [<Fact>]
    let ``failWith creates invalid result with single message`` () =
        let r = Validation.failWith "single error"
        Assert.False(r.IsValid)
        Assert.Equal(1, r.Messages.Length)
        Assert.Equal("single error", r.Messages.[0])

    // ========================================================================
    // COMBINE
    // ========================================================================

    [<Fact>]
    let ``combine with all successes returns success`` () =
        let r = Validation.combine [Validation.success; Validation.success]
        Assert.True(r.IsValid)
        Assert.Empty(r.Messages)

    [<Fact>]
    let ``combine with one failure returns failure`` () =
        let r = Validation.combine [Validation.success; Validation.failWith "bad"]
        Assert.False(r.IsValid)
        Assert.Equal(1, r.Messages.Length)
        Assert.Equal("bad", r.Messages.[0])

    [<Fact>]
    let ``combine collects all error messages`` () =
        let r = Validation.combine [
            Validation.failWith "err1"
            Validation.success
            Validation.failure ["err2"; "err3"]
        ]
        Assert.False(r.IsValid)
        Assert.Equal(3, r.Messages.Length)

    [<Fact>]
    let ``combine with empty list returns success`` () =
        let r = Validation.combine []
        Assert.True(r.IsValid)
        Assert.Empty(r.Messages)

    // ========================================================================
    // TO RESULT
    // ========================================================================

    [<Fact>]
    let ``toResult converts success to Ok`` () =
        let r = Validation.toResult "field" 42 Validation.success
        Assert.Equal(Ok 42, r)

    [<Fact>]
    let ``toResult converts failure to Error with ValidationError`` () =
        let r = Validation.toResult "myField" 42 (Validation.failWith "invalid value")
        match r with
        | Error (QuantumError.ValidationError (field, reason)) ->
            Assert.Equal("myField", field)
            Assert.Contains("invalid value", reason)
        | Ok _ -> failwith "Expected Error"
        | Error e -> failwith $"Expected ValidationError, got {e}"

    [<Fact>]
    let ``toResult combines multiple error messages`` () =
        let v = Validation.failure ["err1"; "err2"]
        let r = Validation.toResult "f" 0 v
        match r with
        | Error (QuantumError.ValidationError (_, reason)) ->
            Assert.Contains("err1", reason)
            Assert.Contains("err2", reason)
        | _ -> failwith "Expected ValidationError"

    // ========================================================================
    // FORMAT ERRORS
    // ========================================================================

    [<Fact>]
    let ``formatErrors for success returns passed message`` () =
        let s = Validation.formatErrors Validation.success
        Assert.Equal("Validation passed", s)

    [<Fact>]
    let ``formatErrors for failure includes error count and messages`` () =
        let s = Validation.formatErrors (Validation.failure ["a"; "b"])
        Assert.Contains("2 error(s)", s)
        Assert.Contains("1. a", s)
        Assert.Contains("2. b", s)

    [<Fact>]
    let ``formatErrors for single failure shows 1 error`` () =
        let s = Validation.formatErrors (Validation.failWith "only one")
        Assert.Contains("1 error(s)", s)
        Assert.Contains("1. only one", s)
