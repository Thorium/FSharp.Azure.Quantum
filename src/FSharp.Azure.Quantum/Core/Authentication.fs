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

    /// Manages Azure AD token acquisition and caching
    type TokenManager(credential: TokenCredential) =

        let mutable cachedToken: AccessToken option = None
        let mutable tokenExpiry: DateTimeOffset = DateTimeOffset.MinValue
        let tokenLock = obj ()

        /// Get access token with automatic refresh
        member this.GetAccessTokenAsync(?cancellationToken: CancellationToken) =
            async {
                let ct = defaultArg cancellationToken CancellationToken.None

                // Check if cached token is still valid (with 5-minute buffer)
                let now = DateTimeOffset.UtcNow

                let needsRefresh =
                    lock tokenLock (fun () ->
                        let isExpired = tokenExpiry <= now.AddMinutes(5.0)

                        match cachedToken, isExpired with
                        | Some _, false -> false // Token is valid
                        | _ -> true // Need to refresh
                    )

                if not needsRefresh then
                    // Return cached token
                    let token = lock tokenLock (fun () -> cachedToken.Value)
                    return token.Token
                else
                    // Acquire new token
                    let tokenRequestContext = TokenRequestContext([| quantumScope |])
                    let! accessToken = credential.GetTokenAsync(tokenRequestContext, ct).AsTask() |> Async.AwaitTask

                    // Cache the token
                    lock tokenLock (fun () ->
                        cachedToken <- Some accessToken
                        tokenExpiry <- accessToken.ExpiresOn)

                    return accessToken.Token
            }

        /// Clear cached token (force refresh on next request)
        member this.ClearCache() =
            lock tokenLock (fun () ->
                cachedToken <- None
                tokenExpiry <- DateTimeOffset.MinValue)

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

