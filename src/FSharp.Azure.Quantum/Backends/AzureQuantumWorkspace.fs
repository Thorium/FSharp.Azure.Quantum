namespace FSharp.Azure.Quantum.Backends

open FSharp.Azure.Quantum.Core

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Azure.Identity
open Azure.Quantum.Jobs

/// Azure Quantum Workspace Integration
module AzureQuantumWorkspace =

    // ========================================================================
    // HELPER: Async Enumerable to List
    // ========================================================================

    /// Convert IAsyncEnumerable to list as a Task
    ///
    /// Properly handles disposal in both success and error cases.
    /// If both enumeration and disposal fail, preserves the original exception.
    let private asyncEnumerableToList
        (enumerable: IAsyncEnumerable<'T>)
        (cancellationToken: CancellationToken)
        : Task<'T list> =
        task {
            let results = ResizeArray<'T>()
            let enumerator = enumerable.GetAsyncEnumerator cancellationToken
            let mutable enumerationException: exn option = None

            try
                let mutable moveNext = true

                while moveNext do
                    let! next = enumerator.MoveNextAsync().AsTask()
                    moveNext <- next

                    if moveNext then
                        results.Add enumerator.Current

            with ex when not (ex :? OperationCanceledException) ->
                // Store the enumeration exception
                enumerationException <- Some ex

            // Always dispose, even if enumeration failed
            try
                do! enumerator.DisposeAsync().AsTask()
            with disposeEx ->
                // If we had an enumeration exception, preserve it
                // Otherwise, throw the disposal exception
                match enumerationException with
                | Some originalEx ->
                    // Log disposal error but throw original exception
                    System.Diagnostics.Debug.WriteLine(
                        $"Warning: Disposal failed after enumeration error: {disposeEx.Message}"
                    )
                | None ->
                    // No enumeration error, so disposal error is the primary issue
                    raise disposeEx

            // If we had an enumeration exception, throw it now
            return
                match enumerationException with
                | Some ex -> raise ex
                | None -> results |> Seq.toList
        }

    // ========================================================================
    // TYPES
    // ========================================================================

    type WorkspaceConfig =
        {
            SubscriptionId: string
            ResourceGroupName: string
            WorkspaceName: string
            Location: string
            Credential: Azure.Core.TokenCredential option
        }

    type QuotaInfo =
        {
            Provider: string
            Limit: float option
            Used: float option
            Remaining: float option
            Scope: string option
            Period: string option
        }

    type ProviderStatus =
        {
            ProviderId: string
            CurrentAvailability: string option
            TargetCount: int
        }

    // ========================================================================
    // WORKSPACE CLIENT
    // ========================================================================

    type QuantumWorkspace(config: WorkspaceConfig) =

        let credential =
            defaultArg config.Credential (DefaultAzureCredential() :> Azure.Core.TokenCredential)

        let client =
            QuantumJobClient(
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

        member _.ListQuotasAsync(cancellationToken: CancellationToken) : Task<QuotaInfo list> =
            throwIfDisposed ()

            task {
                let quotasEnumerable = client.GetQuotasAsync cancellationToken
                let! quotasList = asyncEnumerableToList quotasEnumerable cancellationToken

                return
                    quotasList
                    |> List.map (fun q ->
                        let limit = if q.Limit.HasValue then Some(float q.Limit.Value) else None

                        let used =
                            if q.Utilization.HasValue then
                                Some(float q.Utilization.Value)
                            else
                                None

                        let remaining =
                            match limit, used with
                            | Some l, Some u -> Some(l - u)
                            | _ -> None

                        {
                            Provider = q.ProviderId
                            Limit = limit
                            Used = used
                            Remaining = remaining
                            Scope =
                                if q.Scope.HasValue then
                                    Some(q.Scope.Value.ToString())
                                else
                                    None
                            Period =
                                if q.Period.HasValue then
                                    Some(q.Period.Value.ToString())
                                else
                                    None
                        })
            }

        member this.GetTotalQuotaAsync(cancellationToken: CancellationToken) : Task<QuotaInfo> =
            throwIfDisposed ()

            task {
                let! quotas = this.ListQuotasAsync cancellationToken

                let totalLimit =
                    quotas
                    |> List.choose (fun q -> q.Limit)
                    |> function
                        | [] -> None
                        | xs -> Some(List.sum xs)

                let totalUsed =
                    quotas
                    |> List.choose (fun q -> q.Used)
                    |> function
                        | [] -> None
                        | xs -> Some(List.sum xs)

                let totalRemaining =
                    match totalLimit, totalUsed with
                    | Some l, Some u -> Some(l - u)
                    | _ -> None

                return
                    {
                        Provider = "All Providers"
                        Limit = totalLimit
                        Used = totalUsed
                        Remaining = totalRemaining
                        Scope = Some "Workspace"
                        Period = Some "Monthly"
                    }
            }

        member this.GetProviderQuotaAsync(provider: string, cancellationToken: CancellationToken) : Task<QuotaInfo option> =
            throwIfDisposed ()

            task {
                let! quotas = this.ListQuotasAsync cancellationToken
                return quotas |> List.tryFind (fun q -> q.Provider = provider)
            }

        member _.ListProvidersAsync(cancellationToken: CancellationToken) : Task<ProviderStatus list> =
            throwIfDisposed ()

            task {
                let providersEnumerable = client.GetProviderStatusAsync cancellationToken
                let! providersList = asyncEnumerableToList providersEnumerable cancellationToken

                return
                    providersList
                    |> List.map (fun p ->
                        {
                            ProviderId = p.Id
                            CurrentAvailability =
                                if p.CurrentAvailability.HasValue then
                                    Some(p.CurrentAvailability.Value.ToString())
                                else
                                    None
                            TargetCount = p.Targets |> Seq.length
                        })
            }

        member _.InnerClient =
            throwIfDisposed ()
            client

        // ========================================================================
        // IDISPOSABLE IMPLEMENTATION
        // ========================================================================

        /// Dispose of unmanaged resources
        member private this.Dispose(disposing: bool) =
            if not disposed then
                if disposing then
                    // Dispose managed resources
                    // Note: Azure.Quantum.Jobs.QuantumJobClient does not implement IDisposable
                    // Some credentials (like DefaultAzureCredential) may implement IDisposable
                    match box credential with
                    | :? IDisposable as disposable -> disposable.Dispose()
                    | _ -> ()

                disposed <- true

        interface IDisposable with
            member this.Dispose() =
                this.Dispose true
                GC.SuppressFinalize(this)

        /// Finalizer for cleanup if Dispose not called
        override this.Finalize() = this.Dispose false

    // ========================================================================
    // BUILDER FUNCTIONS
    // ========================================================================

    let create (config: WorkspaceConfig) : QuantumWorkspace = new QuantumWorkspace(config)

    let createDefault subscriptionId resourceGroup workspaceName location =
        create
            {
                SubscriptionId = subscriptionId
                ResourceGroupName = resourceGroup
                WorkspaceName = workspaceName
                Location = location
                Credential = None
            }

    let createWithCredential subscriptionId resourceGroup workspaceName location credential =
        create
            {
                SubscriptionId = subscriptionId
                ResourceGroupName = resourceGroup
                WorkspaceName = workspaceName
                Location = location
                Credential = Some credential
            }

    let createFromEnvironment () : QuantumResult<QuantumWorkspace> =
        try
            let getEnvVar name =
                match Environment.GetEnvironmentVariable(name) with
                | null
                | "" -> Error(QuantumError.ValidationError("Configuration", $"Environment variable {name} not set"))
                | value -> Ok value

            match
                getEnvVar "AZURE_QUANTUM_SUBSCRIPTION_ID",
                getEnvVar "AZURE_QUANTUM_RESOURCE_GROUP",
                getEnvVar "AZURE_QUANTUM_WORKSPACE_NAME",
                getEnvVar "AZURE_QUANTUM_LOCATION"
            with
            | Ok sub, Ok rg, Ok ws, Ok loc -> Ok(createDefault sub rg ws loc)
            | Error msg, _, _, _
            | _, Error msg, _, _
            | _, _, Error msg, _
            | _, _, _, Error msg -> Error msg
        with ex when not (ex :? OperationCanceledException) ->
            Error(QuantumError.OperationError("Workspace creation", $"Failed: {ex.Message}"))
