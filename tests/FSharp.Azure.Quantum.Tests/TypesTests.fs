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
    let status =
        JobStatus.Parse("failed", Some "InvalidCircuit", Some "Circuit validation failed")

    match status with
    | JobStatus.Failed(code, message) ->
        Assert.Equal("InvalidCircuit", code)
        Assert.Equal("Circuit validation failed", message)
    | _ -> Assert.True(false, "Expected Failed status")

[<Fact>]
let ``JobStatus should parse Cancelled status`` () =
    let status = JobStatus.Parse("cancelled", None, None)
    Assert.Equal(JobStatus.Cancelled, status)

[<Fact>]
let ``JobStatus keeps an unknown status as Unknown, neither final nor a success`` () =
    let status = JobStatus.Parse("Paused", None, None)

    Assert.Equal(JobStatus.Unknown "Paused", status)
    Assert.False status.IsTerminal

[<Theory>]
[<InlineData("Queued")>]
[<InlineData("Waiting")>]
[<InlineData("Executing")>]
[<InlineData("Finishing")>]
[<InlineData("CancellationRequested")>]
[<InlineData("Cancelling")>]
let ``JobStatus parses every documented status that is not final as not final`` (text: string) =
    let status = JobStatus.Parse(text, None, None)

    Assert.False(status.IsTerminal, $"{text} parsed as {status}")

    match status with
    | JobStatus.Unknown _ -> Assert.Fail($"{text} is a documented status")
    | _ -> ()

[<Theory>]
[<InlineData("Succeeded")>]
[<InlineData("Completed")>]
[<InlineData("Failed")>]
[<InlineData("Cancelled")>]
let ``JobStatus parses every documented final status as final`` (text: string) =
    Assert.True(JobStatus.Parse(text, None, None).IsTerminal)

[<Fact>]
let ``JobStatus maps Queued to Waiting and both cancellation phases to Cancelling`` () =
    Assert.Equal(JobStatus.Waiting, JobStatus.Parse("Queued", None, None))
    Assert.Equal(JobStatus.Finishing, JobStatus.Parse("Finishing", None, None))
    Assert.Equal(JobStatus.Cancelling, JobStatus.Parse("CancellationRequested", None, None))
    Assert.Equal(JobStatus.Cancelling, JobStatus.Parse("Cancelling", None, None))
    Assert.Equal(JobStatus.Completed, JobStatus.Parse("Completed", None, None))
