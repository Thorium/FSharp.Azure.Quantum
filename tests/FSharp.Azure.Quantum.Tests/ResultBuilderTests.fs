namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum.Core

module ResultBuilderTests =

    // ========================================================================
    // RESULT CE BUILDER
    // ========================================================================

    [<Fact>]
    let ``result CE returns Ok value`` () =
        let r = result {
            return 42
        }
        Assert.Equal(Ok 42, r)

    [<Fact>]
    let ``result CE binds Ok values`` () =
        let r = result {
            let! x = Ok 10
            let! y = Ok 20
            return x + y
        }
        Assert.Equal(Ok 30, r)

    [<Fact>]
    let ``result CE short-circuits on Error`` () =
        let mutable reached = false
        let r : Result<int, string> = result {
            let! x = Ok 10
            let! _ = Error "fail"
            reached <- true
            return x
        }
        Assert.Equal(Error "fail", r)
        Assert.False(reached, "Should not execute code after Error")

    [<Fact>]
    let ``result CE ReturnFrom passes Result through`` () =
        let r = result {
            return! Ok 99
        }
        Assert.Equal(Ok 99, r)

    [<Fact>]
    let ``result CE ReturnFrom passes Error through`` () =
        let r : Result<int, string> = result {
            return! Error "oops"
        }
        Assert.Equal(Error "oops", r)

    [<Fact>]
    let ``result CE Zero returns Ok unit`` () =
        let r : Result<unit, string> = result {
            ()
        }
        Assert.Equal(Ok (), r)

    [<Fact>]
    let ``result CE For iterates over sequence`` () =
        let r = result {
            for i in [1; 2; 3] do
                if i > 10 then
                    return! Error "too big"
        }
        match r with
        | Ok () -> ()
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``result CE For short-circuits on Error`` () =
        let mutable count = 0
        let r : Result<unit, string> = result {
            for i in [1; 2; 3; 4; 5] do
                count <- count + 1
                if i = 3 then
                    return! Error "stopped at 3"
        }
        Assert.Equal(Error "stopped at 3", r)
        Assert.Equal(3, count)

    [<Fact>]
    let ``result CE TryWith catches exceptions`` () =
        let r = result {
            try
                failwith "boom"
                return 1
            with ex ->
                return -1
        }
        Assert.Equal(Ok -1, r)

    [<Fact>]
    let ``result CE TryFinally runs compensation`` () =
        let mutable cleaned = false
        let r = result {
            try
                return 42
            finally
                cleaned <- true
        }
        Assert.Equal(Ok 42, r)
        Assert.True(cleaned)

    // ========================================================================
    // RESULT MODULE EXTENSIONS
    // ========================================================================

    [<Fact>]
    let ``Result.map transforms Ok value`` () =
        let r = Result.map (fun x -> x * 2) (Ok 5)
        Assert.Equal(Ok 10, r)

    [<Fact>]
    let ``Result.map propagates Error`` () =
        let r : Result<int, string> = Result.map (fun x -> x * 2) (Error "nope")
        Assert.Equal(Error "nope", r)

    [<Fact>]
    let ``Result.mapError transforms Error value`` () =
        let r : Result<int, int> = Result.mapError String.length (Error "abc")
        Assert.Equal(Error 3, r)

    [<Fact>]
    let ``Result.mapError preserves Ok`` () =
        let r : Result<int, int> = Result.mapError String.length (Ok 42)
        Assert.Equal(Ok 42, r)

    [<Fact>]
    let ``Result.bind chains Ok values`` () =
        let r = Result.bind (fun x -> if x > 0 then Ok (x * 2) else Error "neg") (Ok 5)
        Assert.Equal(Ok 10, r)

    [<Fact>]
    let ``Result.bind short-circuits Error`` () =
        let r : Result<int, string> = Result.bind (fun x -> Ok (x * 2)) (Error "early")
        Assert.Equal(Error "early", r)

    [<Fact>]
    let ``Result.toOption converts Ok to Some`` () =
        Assert.Equal(Some 42, Result.toOption (Ok 42))

    [<Fact>]
    let ``Result.toOption converts Error to None`` () =
        let r : int option = Result.toOption (Error "x")
        Assert.Equal(None, r)

    [<Fact>]
    let ``Result.defaultValue returns Ok value`` () =
        Assert.Equal(42, Result.defaultValue 0 (Ok 42))

    [<Fact>]
    let ``Result.defaultValue returns default on Error`` () =
        Assert.Equal(0, Result.defaultValue 0 (Error "x"))

    [<Fact>]
    let ``Result.defaultWith computes default from error`` () =
        let r = Result.defaultWith String.length (Error "abc")
        Assert.Equal(3, r)

    [<Fact>]
    let ``Result.defaultWith returns Ok value when Ok`` () =
        let r = Result.defaultWith String.length (Ok 99)
        Assert.Equal(99, r)

    [<Fact>]
    let ``Result.isOk returns true for Ok`` () =
        Assert.True(Result.isOk (Ok 1))

    [<Fact>]
    let ``Result.isOk returns false for Error`` () =
        Assert.False(Result.isOk (Error "x"))

    [<Fact>]
    let ``Result.isError returns true for Error`` () =
        Assert.True(Result.isError (Error "x"))

    [<Fact>]
    let ``Result.isError returns false for Ok`` () =
        Assert.False(Result.isError (Ok 1))

    [<Fact>]
    let ``Result.zip combines two Ok values`` () =
        let r = Result.zip (Ok 1) (Ok "a")
        Assert.Equal(Ok (1, "a"), r)

    [<Fact>]
    let ``Result.zip returns first Error`` () =
        let r : Result<int * string, string> = Result.zip (Error "e1") (Ok "a")
        Assert.Equal(Error "e1", r)

    [<Fact>]
    let ``Result.zip returns second Error`` () =
        let r : Result<int * string, string> = Result.zip (Ok 1) (Error "e2")
        Assert.Equal(Error "e2", r)

    [<Fact>]
    let ``Result.zip returns first Error when both are Error`` () =
        let r : Result<int * int, string> = Result.zip (Error "e1") (Error "e2")
        Assert.Equal(Error "e1", r)

    // ========================================================================
    // SEQUENCE / TRAVERSE
    // ========================================================================

    [<Fact>]
    let ``Result.sequence with all Ok returns Ok list`` () =
        let r = Result.sequence [Ok 1; Ok 2; Ok 3]
        Assert.Equal(Ok [1; 2; 3], r)

    [<Fact>]
    let ``Result.sequence with empty list returns Ok empty`` () =
        let r : Result<int list, string> = Result.sequence []
        Assert.Equal(Ok [], r)

    [<Fact>]
    let ``Result.sequence with Error returns first Error`` () =
        let r : Result<int list, string> = Result.sequence [Ok 1; Error "bad"; Ok 3]
        Assert.Equal(Error "bad", r)

    [<Fact>]
    let ``Result.traverse maps and sequences`` () =
        let r = Result.traverse (fun x -> if x > 0 then Ok (x * 2) else Error "neg") [1; 2; 3]
        Assert.Equal(Ok [2; 4; 6], r)

    [<Fact>]
    let ``Result.traverse returns first Error`` () =
        let r = Result.traverse (fun x -> if x > 0 then Ok (x * 2) else Error "neg") [1; -1; 3]
        Assert.Equal(Error "neg", r)

    [<Fact>]
    let ``Result.sequenceArray with all Ok returns Ok array`` () =
        let r = Result.sequenceArray [| Ok 10; Ok 20; Ok 30 |]
        match r with
        | Ok arr -> Assert.Equal<int array>([| 10; 20; 30 |], arr)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``Result.sequenceArray with empty array returns Ok empty`` () =
        let r : Result<int array, string> = Result.sequenceArray [||]
        match r with
        | Ok arr -> Assert.Empty(arr)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``Result.sequenceArray with Error returns first Error`` () =
        let r : Result<int array, string> = Result.sequenceArray [| Ok 1; Error "e1"; Error "e2" |]
        Assert.Equal(Error "e1", r)

    [<Fact>]
    let ``Result.traverseArray maps and sequences array`` () =
        let r = Result.traverseArray (fun x -> if x > 0 then Ok (x * 10) else Error "neg") [| 1; 2; 3 |]
        match r with
        | Ok arr -> Assert.Equal<int array>([| 10; 20; 30 |], arr)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``Result.traverseArray returns first Error`` () =
        let r = Result.traverseArray (fun x -> if x > 0 then Ok x else Error "neg") [| 1; -2; 3 |]
        Assert.Equal(Error "neg", r)

    // ========================================================================
    // ORELSE
    // ========================================================================

    [<Fact>]
    let ``Result.orElseWith returns Ok when Ok`` () =
        let r = Result.orElseWith (fun _ -> Ok 99) (Ok 42)
        Assert.Equal(Ok 42, r)

    [<Fact>]
    let ``Result.orElseWith computes alternative on Error`` () =
        let r = Result.orElseWith (fun e -> Ok (String.length e)) (Error "abc")
        Assert.Equal(Ok 3, r)

    [<Fact>]
    let ``Result.orElseWith can return new Error`` () =
        let r : Result<int, int> = Result.orElseWith (fun e -> Error (String.length e)) (Error "abc")
        Assert.Equal(Error 3, r)

    // ========================================================================
    // UNSAFEGET
    // ========================================================================

    [<Fact>]
    let ``Result.unsafeGet returns value for Ok`` () =
        Assert.Equal(42, Result.unsafeGet (Ok 42))

    [<Fact>]
    let ``Result.unsafeGet throws for Error`` () =
        Assert.Throws<InvalidOperationException>(fun () ->
            Result.unsafeGet (Error "boom") |> ignore
        ) |> ignore
