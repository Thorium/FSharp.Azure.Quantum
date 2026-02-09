namespace FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core

open System
open System.Collections.Generic
open Azure.Identity
open Microsoft.Azure.Quantum

/// Azure Quantum Workspace Integration
module AzureQuantumWorkspace =
    
    // ========================================================================
    // HELPER: Async Enumerable to List
    // ========================================================================
    
    /// Convert IAsyncEnumerable to list using F# async
    /// 
    /// Properly handles disposal in both success and error cases.
    /// If both enumeration and disposal fail, preserves the original exception.
    let private asyncEnumerableToList (enumerable: IAsyncEnumerable<'T>) : Async<'T list> =
        async {
            let results = ResizeArray<'T>()
            let enumerator = enumerable.GetAsyncEnumerator()
            let mutable enumerationException: exn option = None
            
            try
                let rec loop () =
                    async {
                        let! moveNext = enumerator.MoveNextAsync().AsTask() |> Async.AwaitTask
                        if moveNext then
                            results.Add(enumerator.Current)
                            return! loop()
                        else
                            return ()
                    }
                
                do! loop()
                
            with ex ->
                // Store the enumeration exception
                enumerationException <- Some ex
            
            // Always dispose, even if enumeration failed
            try
                do! enumerator.DisposeAsync().AsTask() |> Async.AwaitTask
            with disposeEx ->
                // If we had an enumeration exception, preserve it
                // Otherwise, throw the disposal exception
                match enumerationException with
                | Some originalEx -> 
                    // Log disposal error but throw original exception
                    System.Diagnostics.Debug.WriteLine($"Warning: Disposal failed after enumeration error: {disposeEx.Message}")
                | None -> 
                    // No enumeration error, so disposal error is the primary issue
                    return raise disposeEx
            
            // If we had an enumeration exception, throw it now
            match enumerationException with
            | Some ex -> return raise ex
            | None -> return results |> Seq.toList
        }
    
    // ========================================================================
    // TYPES
    // ========================================================================
    
    type WorkspaceConfig = {
        SubscriptionId: string
        ResourceGroupName: string
        WorkspaceName: string
        Location: string
        Credential: Azure.Core.TokenCredential option
    }
    
    type QuotaInfo = {
        Provider: string
        Limit: float option
        Used: float option
        Remaining: float option
        Scope: string option
        Period: string option
    }
    
    type ProviderStatus = {
        ProviderId: string
        CurrentAvailability: string option
        TargetCount: int
    }
    
    // ========================================================================
    // WORKSPACE CLIENT
    // ========================================================================
    
    type QuantumWorkspace(config: WorkspaceConfig) =
        
        let credential = 
            defaultArg config.Credential (new DefaultAzureCredential() :> Azure.Core.TokenCredential)
        
        let workspace = 
            new Workspace(
                config.SubscriptionId,
                config.ResourceGroupName,
                config.WorkspaceName,
                config.Location,
                credential
            )
        
        let mutable disposed = false
        
        let throwIfDisposed () =
            if disposed then
                raise (ObjectDisposedException(nameof QuantumWorkspace))
        
        member _.Config = config
        
        member _.ListQuotasAsync() : Async<QuotaInfo list> =
            throwIfDisposed ()

            async {
                let quotasEnumerable = workspace.ListQuotasAsync()
                let! quotasList = asyncEnumerableToList quotasEnumerable
                
                return
                    quotasList
                    |> List.map (fun q -> 
                        let limit = if q.Limit.HasValue then Some (float q.Limit.Value) else None
                        let used = if q.Utilization.HasValue then Some (float q.Utilization.Value) else None
                        let remaining = 
                            match limit, used with
                            | Some l, Some u -> Some (l - u)
                            | _ -> None
                        
                        {
                            Provider = q.ProviderId
                            Limit = limit
                            Used = used
                            Remaining = remaining
                            Scope = if q.Scope.HasValue then Some (q.Scope.Value.ToString()) else None
                            Period = if q.Period.HasValue then Some (q.Period.Value.ToString()) else None
                        }
                    )
            }
        
        member this.GetTotalQuotaAsync() : Async<QuotaInfo> =
            throwIfDisposed ()
            async {
                let! quotas = this.ListQuotasAsync()
                
                let totalLimit = 
                    quotas 
                    |> List.choose (fun q -> q.Limit) 
                    |> function [] -> None | xs -> Some (List.sum xs)
                
                let totalUsed = 
                    quotas 
                    |> List.choose (fun q -> q.Used) 
                    |> function [] -> None | xs -> Some (List.sum xs)
                
                let totalRemaining =
                    match totalLimit, totalUsed with
                    | Some l, Some u -> Some (l - u)
                    | _ -> None
                
                return {
                    Provider = "All Providers"
                    Limit = totalLimit
                    Used = totalUsed
                    Remaining = totalRemaining
                    Scope = Some "Workspace"
                    Period = Some "Monthly"
                }
            }
        
        member this.GetProviderQuotaAsync(provider: string) : Async<QuotaInfo option> =
            throwIfDisposed ()
            async {
                let! quotas = this.ListQuotasAsync()
                return quotas |> List.tryFind (fun q -> q.Provider = provider)
            }
        
        member _.ListProvidersAsync() : Async<ProviderStatus list> =
            throwIfDisposed ()
            async {
                let providersEnumerable = workspace.ListProvidersStatusAsync()
                let! providersList = asyncEnumerableToList providersEnumerable
                
                return
                    providersList
                    |> List.map (fun p -> {
                        ProviderId = p.ProviderId
                        CurrentAvailability = 
                            if p.CurrentAvailability.HasValue then 
                                Some (p.CurrentAvailability.Value.ToString()) 
                            else None
                        TargetCount = p.Targets |> Seq.length
                    })
            }
        
        member _.InnerWorkspace =
            throwIfDisposed ()
            workspace
        
        // ========================================================================
        // IDISPOSABLE IMPLEMENTATION
        // ========================================================================
        
        /// Dispose of unmanaged resources
        member private this.Dispose(disposing: bool) =
            if not disposed then
                if disposing then
                    // Dispose managed resources
                    // Note: Microsoft.Azure.Quantum.Workspace doesn't implement IDisposable
                    // Some credentials (like DefaultAzureCredential) may implement IDisposable
                    match box credential with
                    | :? IDisposable as disposable -> disposable.Dispose()
                    | _ -> ()
                
                disposed <- true
        
        interface IDisposable with
            member this.Dispose() =
                this.Dispose(true)
                GC.SuppressFinalize(this)
        
        /// Finalizer for cleanup if Dispose not called
        override this.Finalize() =
            this.Dispose(false)
    
    // ========================================================================
    // BUILDER FUNCTIONS
    // ========================================================================
    
    let create (config: WorkspaceConfig) : QuantumWorkspace =
        new QuantumWorkspace(config)
    
    let createDefault subscriptionId resourceGroup workspaceName location =
        create {
            SubscriptionId = subscriptionId
            ResourceGroupName = resourceGroup
            WorkspaceName = workspaceName
            Location = location
            Credential = None
        }
    
    let createWithCredential subscriptionId resourceGroup workspaceName location credential =
        create {
            SubscriptionId = subscriptionId
            ResourceGroupName = resourceGroup
            WorkspaceName = workspaceName
            Location = location
            Credential = Some credential
        }
    
    let createFromEnvironment() : QuantumResult<QuantumWorkspace> =
        try
            let getEnvVar name =
                match Environment.GetEnvironmentVariable(name) with
                | null | "" -> Error (QuantumError.ValidationError ("Configuration", $"Environment variable {name} not set"))
                | value -> Ok value
            
            match getEnvVar "AZURE_QUANTUM_SUBSCRIPTION_ID",
                  getEnvVar "AZURE_QUANTUM_RESOURCE_GROUP",
                  getEnvVar "AZURE_QUANTUM_WORKSPACE_NAME",
                  getEnvVar "AZURE_QUANTUM_LOCATION" with
            | Ok sub, Ok rg, Ok ws, Ok loc -> Ok (createDefault sub rg ws loc)
            | Error msg, _, _, _ | _, Error msg, _, _ | _, _, Error msg, _ | _, _, _, Error msg -> Error msg
        with ex ->
            Error (QuantumError.OperationError ("Workspace creation", $"Failed: {ex.Message}"))
