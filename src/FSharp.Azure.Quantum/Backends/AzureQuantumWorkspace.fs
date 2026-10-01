namespace FSharp.Azure.Quantum.Backends

open FSharp.Azure.Quantum.Core

open System
open System.Net.Http
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Azure.Identity

/// Azure Quantum Workspace Integration: quota and provider-status queries over the
/// workspace REST data plane — the same host and api-version Client.fs uses for job CRUD,
/// so no Azure Quantum SDK package is needed (Azure.Identity supplies the credential).
module AzureQuantumWorkspace =

    // ========================================================================
    // TYPES
    // ========================================================================

    type WorkspaceConfig =
        {
            SubscriptionId: string
            ResourceGroupName: string
            WorkspaceName: string
            Location: string
            /// Credential for the bearer token; None = DefaultAzureCredential.
            /// Not used when HttpClient is given.
            Credential: Azure.Core.TokenCredential option
            /// An already authenticated client, e.g. from LocalQuantumService.CreateHttpClient();
            /// None = Authentication.createAuthenticatedClient over Credential, owned and
            /// disposed by the workspace.
            HttpClient: HttpClient option
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
    // RESPONSE PARSING
    // ========================================================================

    let private tryString (name: string) (element: JsonElement) : string option =
        match element.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.String -> Some(value.GetString())
        | _ -> None

    let private tryNumber (name: string) (element: JsonElement) : float option =
        match element.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.Number -> Some(value.GetDouble())
        | _ -> None

    /// One entry of GET {workspace}/quotas: dimension, scope, providerId, utilization,
    /// holds, limit, period.
    let private readQuota (quota: JsonElement) : QuotaInfo =
        let limit = tryNumber "limit" quota
        let used = tryNumber "utilization" quota

        {
            Provider = tryString "providerId" quota |> Option.defaultValue ""
            Limit = limit
            Used = used
            Remaining =
                match limit, used with
                | Some l, Some u -> Some(l - u)
                | _ -> None
            Scope = tryString "scope" quota
            Period = tryString "period" quota
        }

    /// One entry of GET {workspace}/providerStatus: id, currentAvailability, targets.
    let private readProviderStatus (provider: JsonElement) : ProviderStatus =
        {
            ProviderId = tryString "id" provider |> Option.defaultValue ""
            CurrentAvailability = tryString "currentAvailability" provider
            TargetCount =
                match provider.TryGetProperty "targets" with
                | true, targets when targets.ValueKind = JsonValueKind.Array -> targets.GetArrayLength()
                | _ -> 0
        }

    /// One page of a `{ "value": [...], "nextLink": ... }` collection: its items and the
    /// link to the next page, if any.
    let private getPageAsync
        (httpClient: HttpClient)
        (read: JsonElement -> 'T)
        (cancellationToken: CancellationToken)
        (url: string)
        : Task<'T list * string option> =
        task {
            use request = new HttpRequestMessage(HttpMethod.Get, url)
            use! response = httpClient.SendAsync(request, cancellationToken)
            let! body = response.Content.ReadAsStringAsync cancellationToken

            if not response.IsSuccessStatusCode then
                raise (
                    HttpRequestException(
                        $"Azure Quantum workspace API returned {int response.StatusCode} for {url}: {body}",
                        null,
                        response.StatusCode
                    )
                )

            use document = JsonDocument.Parse body
            let root = document.RootElement

            let items =
                match root.TryGetProperty "value" with
                | true, value when value.ValueKind = JsonValueKind.Array ->
                    value.EnumerateArray() |> Seq.map read |> List.ofSeq
                | _ -> []

            let next =
                tryString "nextLink" root |> Option.filter (String.IsNullOrWhiteSpace >> not)

            return items, next
        }

    /// Every item of a paged collection, following `nextLink` to the end.
    let private getPagesAsync
        (httpClient: HttpClient)
        (read: JsonElement -> 'T)
        (cancellationToken: CancellationToken)
        (firstUrl: string)
        : Task<'T list> =
        let rec collect url pages =
            task {
                let! items, next = getPageAsync httpClient read cancellationToken url

                match next with
                | Some link -> return! collect link (items :: pages)
                | None -> return List.concat (List.rev (items :: pages))
            }

        collect firstUrl []

    // ========================================================================
    // WORKSPACE CLIENT
    // ========================================================================

    type QuantumWorkspace(config: WorkspaceConfig) =

        // A caller-supplied client is used as is; otherwise the workspace builds one over the
        // credential (the default one when none is given) and owns both.
        let httpClient, ownsClient, credential =
            match config.HttpClient with
            | Some client -> client, false, None
            | None ->
                let credential =
                    defaultArg config.Credential (DefaultAzureCredential() :> Azure.Core.TokenCredential)

                Authentication.createAuthenticatedClient credential, true, Some credential

        let mutable disposed = false

        let throwIfDisposed () =
            if disposed then
                raise (ObjectDisposedException(nameof QuantumWorkspace))

        let workspaceUrl path =
            Client.Endpoints.fullUrl config.Location path

        member _.Config = config

        member _.ListQuotasAsync(cancellationToken: CancellationToken) : Task<QuotaInfo list> =
            throwIfDisposed ()

            Client.Endpoints.quotasPath config.SubscriptionId config.ResourceGroupName config.WorkspaceName
            |> workspaceUrl
            |> getPagesAsync httpClient readQuota cancellationToken

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

        member this.GetProviderQuotaAsync
            (provider: string, cancellationToken: CancellationToken)
            : Task<QuotaInfo option> =
            throwIfDisposed ()

            task {
                let! quotas = this.ListQuotasAsync cancellationToken
                return quotas |> List.tryFind (fun q -> q.Provider = provider)
            }

        member _.ListProvidersAsync(cancellationToken: CancellationToken) : Task<ProviderStatus list> =
            throwIfDisposed ()

            Client.Endpoints.providerStatusPath config.SubscriptionId config.ResourceGroupName config.WorkspaceName
            |> workspaceUrl
            |> getPagesAsync httpClient readProviderStatus cancellationToken

        // ========================================================================
        // IDISPOSABLE IMPLEMENTATION
        // ========================================================================

        /// Dispose of unmanaged resources
        member private this.Dispose(disposing: bool) =
            if not disposed then
                if disposing then
                    // Dispose managed resources: the client the workspace created (its handler
                    // chain owns the token manager) and the credential it created or was given.
                    // A client supplied by the caller stays the caller's to dispose.
                    if ownsClient then
                        httpClient.Dispose()

                    match credential |> Option.map box with
                    | Some(:? IDisposable as disposable) -> disposable.Dispose()
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
                HttpClient = None
            }

    let createWithCredential subscriptionId resourceGroup workspaceName location credential =
        create
            {
                SubscriptionId = subscriptionId
                ResourceGroupName = resourceGroup
                WorkspaceName = workspaceName
                Location = location
                Credential = Some credential
                HttpClient = None
            }

    /// A workspace client over an already authenticated HttpClient (for example the one
    /// LocalQuantumService.CreateHttpClient returns). The caller keeps ownership of the client.
    let createWithHttpClient subscriptionId resourceGroup workspaceName location (httpClient: HttpClient) =
        create
            {
                SubscriptionId = subscriptionId
                ResourceGroupName = resourceGroup
                WorkspaceName = workspaceName
                Location = location
                Credential = None
                HttpClient = Some httpClient
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
        with ex ->
            Error(QuantumError.OperationError("Workspace creation", $"Failed: {ex.Message}"))
