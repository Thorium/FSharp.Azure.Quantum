module FSharp.Azure.Quantum.Tests.AuthenticationTests

open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Xunit
open Azure.Core
open Azure.Identity
open FSharp.Azure.Quantum.Core.Authentication

// Mock TokenCredential for testing
type MockTokenCredential(tokenValue: string, expiresOn: DateTimeOffset) =
    inherit TokenCredential()

    override this.GetToken(requestContext: TokenRequestContext, cancellationToken: CancellationToken) =
        AccessToken(tokenValue, expiresOn)

    override this.GetTokenAsync(requestContext: TokenRequestContext, cancellationToken: CancellationToken) =
        System.Threading.Tasks.ValueTask<AccessToken>(AccessToken(tokenValue, expiresOn))

[<Fact>]
let ``TokenManager should acquire token on first request`` () =
    let expiresOn = DateTimeOffset.UtcNow.AddHours(1.0)
    let mockCredential = MockTokenCredential("test-token-123", expiresOn)
    let tokenManager = TokenManager(mockCredential)

    let token = tokenManager.GetAccessTokenAsync() |> Async.RunSynchronously

    Assert.Equal("test-token-123", token)

[<Fact>]
let ``TokenManager should cache token on subsequent requests`` () =
    let expiresOn = DateTimeOffset.UtcNow.AddHours(1.0)
    let mutable callCount = 0

    let trackingCredential =
        { new TokenCredential() with
            member _.GetToken(_, _) =
                callCount <- callCount + 1
                AccessToken("token", expiresOn)

            member _.GetTokenAsync(_, _) =
                callCount <- callCount + 1
                System.Threading.Tasks.ValueTask<AccessToken>(AccessToken("token", expiresOn)) }

    let tokenManager = TokenManager(trackingCredential)

    // First call
    let token1 = tokenManager.GetAccessTokenAsync() |> Async.RunSynchronously
    Assert.Equal(1, callCount)

    // Second call should use cache
    let token2 = tokenManager.GetAccessTokenAsync() |> Async.RunSynchronously
    Assert.Equal(1, callCount) // Should not increment
    Assert.Equal(token1, token2)

[<Fact>]
let ``TokenManager should refresh expired token`` () =
    let mutable callCount = 0
    let mutable currentExpiry = DateTimeOffset.UtcNow.AddMinutes(1.0) // Expires very soon

    let trackingCredential =
        { new TokenCredential() with
            member _.GetToken(_, _) =
                callCount <- callCount + 1
                AccessToken(sprintf "token-%d" callCount, currentExpiry)

            member _.GetTokenAsync(_, _) =
                callCount <- callCount + 1
                System.Threading.Tasks.ValueTask<AccessToken>(AccessToken(sprintf "token-%d" callCount, currentExpiry)) }

    let tokenManager = TokenManager(trackingCredential)

    // First call
    let token1 = tokenManager.GetAccessTokenAsync() |> Async.RunSynchronously
    Assert.Equal(1, callCount)
    Assert.Equal("token-1", token1)

    // Update expiry to force refresh
    currentExpiry <- DateTimeOffset.UtcNow.AddHours(1.0)

    // Manually clear cache to simulate expiry
    tokenManager.ClearCache()

    // Second call should get new token
    let token2 = tokenManager.GetAccessTokenAsync() |> Async.RunSynchronously
    Assert.Equal(2, callCount)
    Assert.Equal("token-2", token2)

[<Fact>]
let ``TokenManager ClearCache should force token refresh`` () =
    let expiresOn = DateTimeOffset.UtcNow.AddHours(1.0)
    let mutable callCount = 0

    let trackingCredential =
        { new TokenCredential() with
            member _.GetToken(_, _) =
                callCount <- callCount + 1
                AccessToken("token", expiresOn)

            member _.GetTokenAsync(_, _) =
                callCount <- callCount + 1
                System.Threading.Tasks.ValueTask<AccessToken>(AccessToken("token", expiresOn)) }

    let tokenManager = TokenManager(trackingCredential)

    let _ = tokenManager.GetAccessTokenAsync() |> Async.RunSynchronously
    Assert.Equal(1, callCount)

    tokenManager.ClearCache()

    let _ = tokenManager.GetAccessTokenAsync() |> Async.RunSynchronously
    Assert.Equal(2, callCount)

// ============================================================================
// Credential Provider Tests
// ============================================================================

[<Fact>]
let ``CredentialProviders createDefaultCredential should return DefaultAzureCredential`` () =
    let credential = CredentialProviders.createDefaultCredential()
    Assert.IsType<DefaultAzureCredential>(credential) |> ignore

[<Fact>]
let ``CredentialProviders createCliCredential should return AzureCliCredential`` () =
    let credential = CredentialProviders.createCliCredential()
    Assert.IsType<AzureCliCredential>(credential) |> ignore

[<Fact>]
let ``CredentialProviders createManagedIdentityCredential should return ManagedIdentityCredential`` () =
    let credential = CredentialProviders.createManagedIdentityCredential()
    Assert.IsType<ManagedIdentityCredential>(credential) |> ignore

// ============================================================================
// HTTP Integration Tests
// ============================================================================

// Test message handler that captures the request
type TestMessageHandler() =
    inherit DelegatingHandler()
    
    member val CapturedRequest : HttpRequestMessage option = None with get, set
    
    override this.SendAsync(request: HttpRequestMessage, cancellationToken: CancellationToken) =
        this.CapturedRequest <- Some request
        let response = new HttpResponseMessage(HttpStatusCode.OK)
        Task.FromResult(response)

[<Fact>]
let ``AuthenticationHandler should add Authorization Bearer header`` () =
    let expiresOn = DateTimeOffset.UtcNow.AddHours(1.0)
    let mockCredential = MockTokenCredential("test-bearer-token", expiresOn)
    let tokenManager = TokenManager(mockCredential)
    
    let testHandler = new TestMessageHandler()
    let authHandler = new AuthenticationHandler(tokenManager, InnerHandler = testHandler)
    let client = new HttpClient(authHandler)
    
    // Make a request
    let request = new HttpRequestMessage(HttpMethod.Get, "https://quantum.azure.com/test")
    let _ = client.SendAsync(request) |> Async.AwaitTask |> Async.RunSynchronously
    
    // Verify Authorization header was added
    match testHandler.CapturedRequest with
    | Some capturedReq ->
        Assert.NotNull(capturedReq.Headers.Authorization)
        Assert.Equal("Bearer", capturedReq.Headers.Authorization.Scheme)
        Assert.Equal("test-bearer-token", capturedReq.Headers.Authorization.Parameter)
    | None ->
        Assert.Fail("No request was captured")

