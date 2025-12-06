namespace FSharp.Azure.Quantum.Core

open System
open System.Net.Http
open System.Net.Http.Headers
open System.Threading
open System.Threading.Tasks
open Azure.Core
open Azure.Identity

module Authentication =

    /// Quantum API scope for Azure AD
    let private quantumScope = "https://quantum.microsoft.com/.default"
    
    /// Credential provider factory functions
    module CredentialProviders =
        
        /// Create DefaultAzureCredential (tries multiple auth methods)
        let createDefaultCredential () : TokenCredential =
            upcast DefaultAzureCredential()
        
        /// Create AzureCliCredential (uses az login credentials)
        let createCliCredential () : TokenCredential =
            upcast AzureCliCredential()
        
        /// Create ManagedIdentityCredential (for Azure VM/App Service)
        let createManagedIdentityCredential () : TokenCredential =
            upcast ManagedIdentityCredential()

    /// Cached token state (immutable record for thread-safety)
    type private TokenCache = {
        Token: AccessToken
        ExpiresOn: DateTimeOffset
    }

    /// Manages Azure AD token acquisition and caching
    type TokenManager(credential: TokenCredential) =

        // Use SemaphoreSlim for async-safe token refresh coordination
        let refreshSemaphore = new SemaphoreSlim(1, 1)
        let mutable cachedToken: TokenCache option = None

        /// Get access token with automatic refresh (async-safe)
        member this.GetAccessTokenAsync(?cancellationToken: CancellationToken) =
            async {
                let ct = defaultArg cancellationToken CancellationToken.None

                // Check if cached token is still valid (with 5-minute buffer)
                let now = DateTimeOffset.UtcNow

                // Quick check without lock (safe - reading option is atomic)
                match cachedToken with
                | Some cache when cache.ExpiresOn > now.AddMinutes(5.0) ->
                    // Token is still valid, return immediately
                    return cache.Token.Token
                | _ ->
                    // Token needs refresh - use semaphore for async coordination
                    do! refreshSemaphore.WaitAsync(ct) |> Async.AwaitTask
                    try
                        // Double-check after acquiring semaphore (another thread may have refreshed)
                        match cachedToken with
                        | Some cache when cache.ExpiresOn > now.AddMinutes(5.0) ->
                            return cache.Token.Token
                        | _ ->
                            // Acquire new token
                            let tokenRequestContext = TokenRequestContext([| quantumScope |])
                            let! accessToken = credential.GetTokenAsync(tokenRequestContext, ct).AsTask() |> Async.AwaitTask

                            // Cache the token (immutable update)
                            cachedToken <- Some {
                                Token = accessToken
                                ExpiresOn = accessToken.ExpiresOn
                            }

                            return accessToken.Token
                    finally
                        refreshSemaphore.Release() |> ignore
            }

        /// Clear cached token (force refresh on next request)
        /// 
        /// Thread-safe - waits for any ongoing token refresh to complete
        /// before clearing the cache.
        member this.ClearCache() =
            refreshSemaphore.Wait()
            try
                cachedToken <- None
            finally
                refreshSemaphore.Release() |> ignore

    /// DelegatingHandler that adds Authorization Bearer token to HTTP requests
    type AuthenticationHandler(tokenManager: TokenManager) =
        inherit DelegatingHandler()
        
        member private this.SendAsyncCore(request: HttpRequestMessage, cancellationToken: CancellationToken, token: string) : Task<HttpResponseMessage> =
            request.Headers.Authorization <- AuthenticationHeaderValue("Bearer", token)
            base.SendAsync(request, cancellationToken)
        
        override this.SendAsync(request: HttpRequestMessage, cancellationToken: CancellationToken) : Task<HttpResponseMessage> =
            // Get bearer token asynchronously without blocking
            async {
                let! token = tokenManager.GetAccessTokenAsync(cancellationToken)
                return! this.SendAsyncCore(request, cancellationToken, token) |> Async.AwaitTask
            } |> Async.StartAsTask
    
    /// Create an authenticated HttpClient for Azure Quantum API calls
    /// 
    /// This is the recommended way to create HttpClients for use with Azure Quantum backends.
    /// The returned client automatically handles Azure AD token acquisition and refresh.
    /// 
    /// Example:
    ///   let credential = CredentialProviders.createDefaultCredential()
    ///   let httpClient = Authentication.createAuthenticatedClient credential
    ///   // Use with IonQBackend, RigettiBackend, or Client modules
    let createAuthenticatedClient (credential: TokenCredential) : HttpClient =
        let tokenManager = TokenManager(credential)
        let authHandler = new AuthenticationHandler(tokenManager)
        new HttpClient(authHandler)

