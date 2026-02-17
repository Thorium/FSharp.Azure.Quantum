namespace FSharp.Azure.Quantum.Tests

open System.Net
open System.Threading
open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.Retry

module RetryTests =

    // ========================================================================
    // DEFAULT CONFIG
    // ========================================================================

    [<Fact>]
    let ``defaultConfig has expected values`` () =
        Assert.Equal(3, defaultConfig.MaxAttempts)
        Assert.Equal(500, defaultConfig.InitialDelayMs)
        Assert.Equal(4000, defaultConfig.MaxDelayMs)
        Assert.True(abs(defaultConfig.JitterFactor - 0.2) < 1e-10)

    // ========================================================================
    // IS TRANSIENT STATUS CODE
    // ========================================================================

    [<Theory>]
    [<InlineData(408)>] // RequestTimeout
    [<InlineData(429)>] // TooManyRequests
    [<InlineData(500)>] // InternalServerError
    [<InlineData(502)>] // BadGateway
    [<InlineData(503)>] // ServiceUnavailable
    [<InlineData(504)>] // GatewayTimeout
    let ``isTransientStatusCode returns true for transient codes`` (code: int) =
        Assert.True(isTransientStatusCode (enum<HttpStatusCode> code))

    [<Theory>]
    [<InlineData(200)>] // OK
    [<InlineData(400)>] // BadRequest
    [<InlineData(401)>] // Unauthorized
    [<InlineData(403)>] // Forbidden
    [<InlineData(404)>] // NotFound
    let ``isTransientStatusCode returns false for non-transient codes`` (code: int) =
        Assert.False(isTransientStatusCode (enum<HttpStatusCode> code))

    // ========================================================================
    // IS TRANSIENT ERROR
    // ========================================================================

    [<Fact>]
    let ``isTransientError returns true for ServiceUnavailable`` () =
        let err = QuantumError.AzureError (AzureQuantumError.ServiceUnavailable None)
        Assert.True(isTransientError err)

    [<Fact>]
    let ``isTransientError returns true for RateLimited`` () =
        let err = QuantumError.AzureError (AzureQuantumError.RateLimited (System.TimeSpan.FromSeconds 60.0))
        Assert.True(isTransientError err)

    [<Fact>]
    let ``isTransientError returns true for NetworkTimeout`` () =
        let err = QuantumError.AzureError (AzureQuantumError.NetworkTimeout 0)
        Assert.True(isTransientError err)

    [<Fact>]
    let ``isTransientError returns false for InvalidCredentials`` () =
        let err = QuantumError.AzureError AzureQuantumError.InvalidCredentials
        Assert.False(isTransientError err)

    [<Fact>]
    let ``isTransientError returns false for ValidationError`` () =
        let err = QuantumError.ValidationError("field", "reason")
        Assert.False(isTransientError err)

    // ========================================================================
    // CALCULATE DELAY
    // ========================================================================

    [<Fact>]
    let ``calculateDelay returns positive value`` () =
        let delay = calculateDelay defaultConfig 1
        Assert.True(delay >= 0, $"Delay should be non-negative, got {delay}")

    [<Fact>]
    let ``calculateDelay increases with attempt number`` () =
        // Use config with no jitter for deterministic results
        let config = { defaultConfig with JitterFactor = 0.0 }
        let delay1 = calculateDelay config 1
        let delay2 = calculateDelay config 2
        let delay3 = calculateDelay config 3
        Assert.True(delay2 >= delay1, $"Delay2 ({delay2}) should be >= delay1 ({delay1})")
        Assert.True(delay3 >= delay2, $"Delay3 ({delay3}) should be >= delay2 ({delay2})")

    [<Fact>]
    let ``calculateDelay caps at MaxDelayMs`` () =
        let config = { defaultConfig with JitterFactor = 0.0; MaxDelayMs = 1000 }
        let delay = calculateDelay config 100  // Very high attempt
        Assert.True(delay <= 1000, $"Delay ({delay}) should be capped at MaxDelayMs (1000)")

    [<Fact>]
    let ``calculateDelay with zero jitter equals base delay`` () =
        let config = { defaultConfig with InitialDelayMs = 100; JitterFactor = 0.0 }
        let delay = calculateDelay config 1
        Assert.Equal(100, delay)

    [<Fact>]
    let ``calculateDelay exponential backoff doubles each attempt`` () =
        let config = { defaultConfig with InitialDelayMs = 100; MaxDelayMs = 100000; JitterFactor = 0.0 }
        let delay1 = calculateDelay config 1
        let delay2 = calculateDelay config 2
        let delay3 = calculateDelay config 3
        Assert.Equal(100, delay1)
        Assert.Equal(200, delay2)
        Assert.Equal(400, delay3)

    // ========================================================================
    // CATEGORIZE HTTP ERROR
    // ========================================================================

    [<Fact>]
    let ``categorizeHttpError maps Unauthorized to InvalidCredentials`` () =
        match categorizeHttpError HttpStatusCode.Unauthorized "" with
        | QuantumError.AzureError AzureQuantumError.InvalidCredentials -> ()
        | e -> failwith $"Expected InvalidCredentials, got {e}"

    [<Fact>]
    let ``categorizeHttpError maps Forbidden to InvalidCredentials`` () =
        match categorizeHttpError HttpStatusCode.Forbidden "" with
        | QuantumError.AzureError AzureQuantumError.InvalidCredentials -> ()
        | e -> failwith $"Expected InvalidCredentials, got {e}"

    [<Fact>]
    let ``categorizeHttpError maps TooManyRequests to RateLimited`` () =
        match categorizeHttpError HttpStatusCode.TooManyRequests "" with
        | QuantumError.AzureError (AzureQuantumError.RateLimited _) -> ()
        | e -> failwith $"Expected RateLimited, got {e}"

    [<Fact>]
    let ``categorizeHttpError maps ServiceUnavailable`` () =
        match categorizeHttpError HttpStatusCode.ServiceUnavailable "" with
        | QuantumError.AzureError (AzureQuantumError.ServiceUnavailable _) -> ()
        | e -> failwith $"Expected ServiceUnavailable, got {e}"

    [<Fact>]
    let ``categorizeHttpError maps RequestTimeout to NetworkTimeout`` () =
        match categorizeHttpError HttpStatusCode.RequestTimeout "" with
        | QuantumError.AzureError (AzureQuantumError.NetworkTimeout _) -> ()
        | e -> failwith $"Expected NetworkTimeout, got {e}"

    [<Fact>]
    let ``categorizeHttpError maps GatewayTimeout to NetworkTimeout`` () =
        match categorizeHttpError HttpStatusCode.GatewayTimeout "" with
        | QuantumError.AzureError (AzureQuantumError.NetworkTimeout _) -> ()
        | e -> failwith $"Expected NetworkTimeout, got {e}"

    [<Fact>]
    let ``categorizeHttpError maps BadRequest with InvalidCircuit to ValidationError`` () =
        match categorizeHttpError HttpStatusCode.BadRequest "InvalidCircuit detected" with
        | QuantumError.ValidationError (field, _) ->
            Assert.Equal("circuit", field)
        | e -> failwith $"Expected ValidationError, got {e}"

    [<Fact>]
    let ``categorizeHttpError maps BadRequest with quota to QuotaExceeded`` () =
        match categorizeHttpError HttpStatusCode.BadRequest "quota limit reached" with
        | QuantumError.AzureError (AzureQuantumError.QuotaExceeded _) -> ()
        | e -> failwith $"Expected QuotaExceeded, got {e}"

    [<Fact>]
    let ``categorizeHttpError maps NotFound with backend to BackendError`` () =
        match categorizeHttpError HttpStatusCode.NotFound "backend not available" with
        | QuantumError.BackendError _ -> ()
        | e -> failwith $"Expected BackendError, got {e}"

    [<Fact>]
    let ``categorizeHttpError maps unknown status to UnknownError`` () =
        match categorizeHttpError (enum<HttpStatusCode> 418) "I'm a teapot" with
        | QuantumError.AzureError (AzureQuantumError.UnknownError (code, body)) ->
            Assert.Equal(418, code)
            Assert.Equal("I'm a teapot", body)
        | e -> failwith $"Expected UnknownError, got {e}"

    // ========================================================================
    // EXECUTE WITH RETRY
    // ========================================================================

    [<Fact>]
    let ``executeWithRetry returns Ok on first success`` () =
        let config = { defaultConfig with MaxAttempts = 3 }
        let operation (_ct: CancellationToken) = async { return Ok 42 }
        let r = executeWithRetry config operation CancellationToken.None |> Async.RunSynchronously
        Assert.Equal(Ok 42, r)

    [<Fact>]
    let ``executeWithRetry returns Error for non-transient error`` () =
        let config = { defaultConfig with MaxAttempts = 3 }
        let mutable attempts = 0
        let operation (_ct: CancellationToken) = async {
            attempts <- attempts + 1
            return Error (QuantumError.ValidationError("x", "bad"))
        }
        let r = executeWithRetry config operation CancellationToken.None |> Async.RunSynchronously
        Assert.Equal(1, attempts)
        match r with
        | Error (QuantumError.ValidationError _) -> ()
        | _ -> failwith "Expected ValidationError"

    [<Fact>]
    let ``executeWithRetry retries on transient error then succeeds`` () =
        let config = { defaultConfig with MaxAttempts = 3; InitialDelayMs = 1; MaxDelayMs = 10 }
        let mutable attempts = 0
        let operation (_ct: CancellationToken) = async {
            attempts <- attempts + 1
            if attempts < 3 then
                return Error (QuantumError.AzureError (AzureQuantumError.ServiceUnavailable None))
            else
                return Ok "success"
        }
        let r = executeWithRetry config operation CancellationToken.None |> Async.RunSynchronously
        Assert.Equal(3, attempts)
        Assert.Equal(Ok "success", r)

    [<Fact>]
    let ``executeWithRetry stops after MaxAttempts`` () =
        let config = { defaultConfig with MaxAttempts = 2; InitialDelayMs = 1; MaxDelayMs = 10 }
        let mutable attempts = 0
        let operation (_ct: CancellationToken) = async {
            attempts <- attempts + 1
            return Error (QuantumError.AzureError (AzureQuantumError.ServiceUnavailable None))
        }
        let r = executeWithRetry config operation CancellationToken.None |> Async.RunSynchronously
        Assert.Equal(2, attempts)
        match r with
        | Error (QuantumError.AzureError (AzureQuantumError.ServiceUnavailable _)) -> ()
        | _ -> failwith "Expected ServiceUnavailable error"

    [<Fact>]
    let ``executeWithRetry respects cancellation`` () =
        let config = { defaultConfig with MaxAttempts = 10 }
        let cts = new CancellationTokenSource()
        cts.Cancel()
        let operation (_ct: CancellationToken) = async { return Ok 1 }
        let r = executeWithRetry config operation cts.Token |> Async.RunSynchronously
        match r with
        | Error (QuantumError.OperationError (_, msg)) ->
            Assert.Contains("cancelled", msg.ToLower())
        | _ -> failwith "Expected cancellation error"
