module FSharp.Azure.Quantum.Tests.TypesTests

open Xunit
open FSharp.Azure.Quantum.Core.Types

[<Fact>]
let ``JobStatus should parse Waiting status`` () =
    let status = JobStatus.Parse("waiting", None, None)
    Assert.Equal(JobStatus.Waiting, status)

[<Fact>]
let ``JobStatus should parse Executing status`` () =
    let status = JobStatus.Parse("executing", None, None)
    Assert.Equal(JobStatus.Executing, status)

[<Fact>]
let ``JobStatus should parse Succeeded status`` () =
    let status = JobStatus.Parse("succeeded", None, None)
    Assert.Equal(JobStatus.Succeeded, status)

[<Fact>]
let ``JobStatus should parse Failed status with error details`` () =
    let status = JobStatus.Parse("failed", Some "InvalidCircuit", Some "Circuit validation failed")
    match status with
    | JobStatus.Failed (code, message) ->
        Assert.Equal("InvalidCircuit", code)
        Assert.Equal("Circuit validation failed", message)
    | _ -> Assert.True(false, "Expected Failed status")

[<Fact>]
let ``JobStatus should parse Cancelled status`` () =
    let status = JobStatus.Parse("cancelled", None, None)
    Assert.Equal(JobStatus.Cancelled, status)

[<Fact>]
let ``JobStatus should handle unknown status as Failed`` () =
    let status = JobStatus.Parse("unknown", None, None)
    match status with
    | JobStatus.Failed (code, message) ->
        Assert.Equal("UnknownStatus", code)
        Assert.Contains("unknown", message)
    | _ -> Assert.True(false, "Expected Failed status")
