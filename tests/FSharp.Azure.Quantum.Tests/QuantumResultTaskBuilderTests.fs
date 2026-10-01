module FSharp.Azure.Quantum.Tests.QuantumResultTaskBuilderTests

open System
open System.Threading
open System.Threading.Tasks
open Xunit
open FSharp.Azure.Quantum.Core

let private failure name =
    QuantumError.OperationError(name, "failed")

type QuantumResultTaskBuilderTests() =

    [<Fact>]
    member _.``let! takes every source kind and the block returns Ok``() : Task =
        task {
            let! result =
                quantumResultTask {
                    let! fromResultTask = Task.FromResult(Ok 1)
                    let! fromResult = Ok 2
                    let! fromAsyncResult = async { return Ok 3 }
                    let! fromTask = Task.FromResult 4
                    let! fromAsync = async { return 5 }
                    return fromResultTask + fromResult + fromAsyncResult + fromTask + fromAsync
                }

            Assert.Equal(Ok 15, result)
        }
        :> Task

    [<Fact>]
    member _.``an Error stops the block and is the block's result``() : Task =
        task {
            let mutable ran = false

            let! result =
                quantumResultTask {
                    let! _ = Task.FromResult(Error(failure "first"): QuantumResult<int>)
                    ran <- true
                    return 1
                }

            Assert.Equal(Error(failure "first"), result)
            Assert.False(ran, "the steps after an Error must not run")
        }
        :> Task

    [<Fact>]
    member _.``a Task of QuantumResult binds as a result, not as a value wrapped in Ok``() : Task =
        task {
            let source: Task<QuantumResult<int>> = Task.FromResult(Error(failure "inner"))
            let! result = quantumResultTask { return! source }
            Assert.Equal(Error(failure "inner"), result)
        }
        :> Task

    [<Fact>]
    member _.``the block runs when evaluated and awaits its steps``() : Task =
        task {
            let order = ResizeArray<string>()

            let! result =
                quantumResultTask {
                    order.Add "start"

                    let! delayed =
                        task {
                            do! Task.Delay 20
                            order.Add "awaited"
                            return Ok "value"
                        }

                    order.Add "after"
                    return delayed
                }

            Assert.Equal(Ok "value", result)
            Assert.Equal<string list>([ "start"; "awaited"; "after" ], List.ofSeq order)
        }
        :> Task

    [<Fact>]
    member _.``try with turns an exception into the handler's result``() : Task =
        task {
            let! result =
                quantumResultTask {
                    try
                        let! _ = Task.FromResult(Ok 1)
                        failwith "exploded"
                        return 2
                    with ex ->
                        return! Error(QuantumError.OperationError("solve", ex.Message))
                }

            Assert.Equal(Error(QuantumError.OperationError("solve", "exploded")), result)
        }
        :> Task

    [<Fact>]
    member _.``try finally, use, for and while run their bodies in order``() : Task =
        task {
            let log = ResizeArray<string>()

            let disposable =
                { new IDisposable with
                    member _.Dispose() = log.Add "disposed"
                }

            let! result =
                quantumResultTask {
                    use _ = disposable

                    try
                        for i in [ 1; 2; 3 ] do
                            let! _ = Task.FromResult(Ok i)
                            log.Add $"for {i}"

                        let mutable n = 0

                        while n < 2 do
                            do! Task.FromResult(Ok())
                            n <- n + 1
                            log.Add $"while {n}"

                        return n
                    finally
                        log.Add "finally"
                }

            Assert.Equal(Ok 2, result)

            Assert.Equal<string list>(
                [ "for 1"; "for 2"; "for 3"; "while 1"; "while 2"; "finally"; "disposed" ],
                List.ofSeq log
            )
        }
        :> Task

    [<Fact>]
    member _.``an Error inside a for loop stops the loop``() : Task =
        task {
            let visited = ResizeArray<int>()

            let! result =
                quantumResultTask {
                    for i in 1..5 do
                        visited.Add i

                        if i = 3 then
                            do! Error(failure $"item {i}")

                    return "done"
                }

            Assert.Equal(Error(failure "item 3"), result)
            Assert.Equal<int list>([ 1; 2; 3 ], List.ofSeq visited)
        }
        :> Task

    [<Fact>]
    member _.``a long loop of completed steps does not grow the stack``() : Task =
        task {
            let! result =
                quantumResultTask {
                    let mutable n = 0

                    while n < 200_000 do
                        let! step = Task.FromResult(Ok 1)
                        n <- n + step

                    return n
                }

            Assert.Equal(Ok 200_000, result)
        }
        :> Task

    [<Fact>]
    member _.``a statement-level Error short-circuits the rest of the block``() : Task =
        task {
            let mutable ran = false

            let! result =
                quantumResultTask {
                    do! Task.FromResult(Error(failure "statement"): QuantumResult<unit>)
                    ran <- true
                    return 1
                }

            Assert.Equal(Error(failure "statement"), result)
            Assert.False(ran, "the statements after a failed do! must not run")
        }
        :> Task

    [<Fact>]
    member _.``use and try-finally still clean up when the body returns Error``() : Task =
        task {
            let mutable disposed = false
            let mutable finallyRan = false

            let resource =
                { new IDisposable with
                    member _.Dispose() = disposed <- true
                }

            let! result =
                quantumResultTask {
                    use _ = resource

                    try
                        let! _ = Task.FromResult(Error(failure "body"): QuantumResult<int>)
                        return 1
                    finally
                        finallyRan <- true
                }

            Assert.Equal(Error(failure "body"), result)
            Assert.True(finallyRan, "finally must run on the Error path")
            Assert.True(disposed, "use must dispose on the Error path")
        }
        :> Task

    [<Fact>]
    member _.``an exception after a step faults the task instead of becoming an Error``() : Task =
        task {
            let faulted: Task<QuantumResult<int>> =
                quantumResultTask {
                    let! _ = Task.FromResult(Ok 1)
                    failwith "boom"
                    return 1
                }

            let! ex = Assert.ThrowsAsync<Exception>(fun () -> faulted :> Task)
            Assert.Equal("boom", ex.Message)
        }
        :> Task

    [<Fact>]
    member _.``a cancelled step cancels the block rather than returning an Error``() : Task =
        task {
            use cts = new CancellationTokenSource()
            do! cts.CancelAsync()

            let cancelled: Task<QuantumResult<int>> =
                Task.FromCanceled<QuantumResult<int>>(cts.Token)

            let block =
                quantumResultTask {
                    let! value = cancelled
                    return value
                }

            let! _ = Assert.ThrowsAnyAsync<OperationCanceledException>(fun () -> block :> Task)
            ()
        }
        :> Task
