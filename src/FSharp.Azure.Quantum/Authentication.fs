namespace FSharp.Azure.Quantum.Core

open System
open System.Threading
open Azure.Core

module Authentication =
    
    /// Quantum API scope for Azure AD
    let private quantumScope = "https://quantum.microsoft.com/.default"
    
    /// Manages Azure AD token acquisition and caching
    type TokenManager(credential: TokenCredential) =
        
        let mutable cachedToken: AccessToken option = None
        let mutable tokenExpiry: DateTimeOffset = DateTimeOffset.MinValue
        let tokenLock = obj()
        
        /// Get access token with automatic refresh
        member this.GetAccessTokenAsync(?cancellationToken: CancellationToken) = async {
            let ct = defaultArg cancellationToken CancellationToken.None
            
            // Check if cached token is still valid (with 5-minute buffer)
            let now = DateTimeOffset.UtcNow
            let needsRefresh = 
                lock tokenLock (fun () ->
                    let isExpired = tokenExpiry <= now.AddMinutes(5.0)
                    match cachedToken, isExpired with
                    | Some _, false -> false  // Token is valid
                    | _ -> true  // Need to refresh
                )
            
            if not needsRefresh then
                // Return cached token
                let token = lock tokenLock (fun () -> cachedToken.Value)
                return token.Token
            else
                // Acquire new token
                let tokenRequestContext = TokenRequestContext([| quantumScope |])
                let! accessToken = 
                    credential.GetTokenAsync(tokenRequestContext, ct).AsTask() 
                    |> Async.AwaitTask
                
                // Cache the token
                lock tokenLock (fun () ->
                    cachedToken <- Some accessToken
                    tokenExpiry <- accessToken.ExpiresOn
                )
                
                return accessToken.Token
        }
        
        /// Clear cached token (force refresh on next request)
        member this.ClearCache() =
            lock tokenLock (fun () ->
                cachedToken <- None
                tokenExpiry <- DateTimeOffset.MinValue
            )
