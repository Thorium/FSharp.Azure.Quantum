namespace FSharp.Azure.Quantum.Backends

/// Real D-Wave Backend using D-Wave Leap Cloud API
///
/// This module provides direct integration with D-Wave's SAPI (Solver API):
/// - REST API client for D-Wave Leap Cloud
/// - No additional dependencies (uses System.Net.Http)
/// - Implements IQuantumBackend for seamless integration
/// - Supports all D-Wave solvers (Advantage, Advantage2, DW_2000Q)
///
/// Configuration via environment variables:
/// - DWAVE_API_TOKEN: Your D-Wave API token from https://cloud.dwavesys.com
/// - DWAVE_ENDPOINT: API endpoint (default: https://cloud.dwavesys.com/sapi/v2/)
///
/// Example:
///   let config = { ApiToken = "DEV-xxxxx"; Endpoint = "..."; Solver = "Advantage_system6.1" }
///   let backend = RealDWaveBackend.create config
///   let result = backend.Execute circuit 1000
module RealDWaveBackend =
    
    open System
    open System.Net.Http
    open System.Text
    open System.Text.Json
    open System.Threading
    open System.Threading.Tasks
    open FSharp.Azure.Quantum.Core.CircuitAbstraction
    open FSharp.Azure.Quantum.Core.BackendAbstraction
    open FSharp.Azure.Quantum.Core
    open FSharp.Azure.Quantum.Algorithms.QuboExtraction
    open FSharp.Azure.Quantum.Algorithms.QuboToIsing
    open FSharp.Azure.Quantum.Backends.DWaveTypes
    
    // ============================================================================
    // LOCAL TYPES
    // ============================================================================
    
    /// Execution result for D-Wave annealing backends
    type ExecutionResult = {
        Measurements: int[][]
        NumShots: int
        BackendName: string
        Metadata: Map<string, obj>
    }
    
    // ============================================================================
    // CONFIGURATION
    // ============================================================================
    
    /// Configuration for D-Wave Leap Cloud API
    type DWaveConfig = {
        /// D-Wave API token (get from https://cloud.dwavesys.com/leap/)
        ApiToken: string
        
        /// D-Wave SAPI endpoint
        Endpoint: string
        
        /// Solver to use (e.g., "Advantage_system6.1")
        Solver: string
        
        /// Request timeout in milliseconds
        TimeoutMs: int option
    }
    
    /// Create default D-Wave configuration from environment variables
    let defaultConfig () : QuantumResult<DWaveConfig> =
        let apiToken = Environment.GetEnvironmentVariable("DWAVE_API_TOKEN")
        let endpoint = 
            let env = Environment.GetEnvironmentVariable("DWAVE_ENDPOINT")
            if String.IsNullOrEmpty(env) then 
                "https://cloud.dwavesys.com/sapi/v2/"
            else env
        
        let solver =
            let env = Environment.GetEnvironmentVariable("DWAVE_SOLVER")
            if String.IsNullOrEmpty(env) then
                "Advantage_system6.1"
            else env
        
        if String.IsNullOrEmpty(apiToken) then
            Error (QuantumError.ValidationError ("Configuration", "DWAVE_API_TOKEN environment variable not set"))
        else
            Ok {
                ApiToken = apiToken
                Endpoint = endpoint
                Solver = solver
                TimeoutMs = Some 300000  // 5 minutes
            }
    
    // ============================================================================
    // D-WAVE SAPI CLIENT
    // ============================================================================
    
    /// D-Wave problem submission
    type private DWaveProblem = {
        solver: string
        data: obj
        ``type``: string
        num_reads: int
    }
    
    /// D-Wave job response
    type private DWaveJobResponse = {
        id: string
        status: string
        solved_on: string option
    }
    
    /// D-Wave solution result
    type private DWaveSolution = {
        solutions: int[][]
        energies: float[]
        num_occurrences: int[]
        timing: Map<string, float> option
    }
    
    /// D-Wave SAPI client
    type private DWaveClient(config: DWaveConfig) =
        
        let httpClient = new HttpClient()
        do 
            httpClient.DefaultRequestHeaders.Add("X-Auth-Token", config.ApiToken)
            config.TimeoutMs |> Option.iter (fun ms ->
                httpClient.Timeout <- TimeSpan.FromMilliseconds(float ms)
            )
        
        let jsonOptions = JsonSerializerOptions()
        do jsonOptions.PropertyNamingPolicy <- JsonNamingPolicy.SnakeCaseLower
        
        /// Submit Ising problem to D-Wave
        member _.SubmitProblemAsync(ising: IsingProblem, numReads: int) : Async<Result<string, string>> =
            async {
                try
                    // Prepare Ising problem data
                    let linear = ising.LinearCoeffs |> Map.toArray |> Array.map (fun (i, h) -> (i, h))
                    let quadratic = ising.QuadraticCoeffs |> Map.toArray |> Array.map (fun ((i,j), J) -> (i, j, J))
                    
                    let problemData = {|
                        linear = linear
                        quadratic = quadratic
                        offset = ising.Offset
                    |}
                    
                    let problem = {
                        solver = config.Solver
                        data = box problemData
                        ``type`` = "ising"
                        num_reads = numReads
                    }
                    
                    let json = JsonSerializer.Serialize(problem, jsonOptions)
                    let content = new StringContent(json, Encoding.UTF8, "application/json")
                    
                    let! response = 
                        httpClient.PostAsync($"{config.Endpoint}problems/", content)
                        |> Async.AwaitTask
                    
                    if not response.IsSuccessStatusCode then
                        let! errorBody = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                        return Error $"D-Wave API error ({int response.StatusCode}): {errorBody}"
                    else
                        let! responseBody = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                        let jobResponse = JsonSerializer.Deserialize<DWaveJobResponse>(responseBody, jsonOptions)
                        return Ok jobResponse.id
                
                with ex ->
                    return Error $"Failed to submit D-Wave problem: {ex.Message}"
            }
        
        /// Poll for job completion
        member _.PollJobAsync(jobId: string) : Async<Result<DWaveSolution, string>> =
            let rec pollLoop attempts =
                async {
                    if attempts >= 60 then
                        return Error $"D-Wave job {jobId} timed out after 300 seconds"
                    else
                        let! response =
                            httpClient.GetAsync($"{config.Endpoint}problems/{jobId}")
                            |> Async.AwaitTask
                        
                        if not response.IsSuccessStatusCode then
                            let! errorBody = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                            return Error $"D-Wave API error ({int response.StatusCode}): {errorBody}"
                        else
                            let! responseBody = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                            let jobStatus = JsonSerializer.Deserialize<DWaveJobResponse>(responseBody, jsonOptions)
                            
                            match jobStatus.status.ToLower() with
                            | "completed" | "solved" ->
                                // Fetch final results
                                let! answerResponse =
                                    httpClient.GetAsync($"{config.Endpoint}problems/{jobId}/answer")
                                    |> Async.AwaitTask
                                
                                let! resultBody = answerResponse.Content.ReadAsStringAsync() |> Async.AwaitTask
                                let solution = JsonSerializer.Deserialize<DWaveSolution>(resultBody, jsonOptions)
                                return Ok solution
                            | "failed" | "cancelled" ->
                                return Error $"D-Wave job {jobId} failed with status: {jobStatus.status}"
                            | _ ->
                                // Still pending - wait and retry
                                do! Async.Sleep 5000
                                return! pollLoop (attempts + 1)
                }
            
            async {
                try
                    return! pollLoop 0
                with ex ->
                    return Error $"Failed to poll D-Wave job: {ex.Message}"
            }
        
        interface IDisposable with
            member _.Dispose() = httpClient.Dispose()
    
    // ============================================================================
    // BACKEND IMPLEMENTATION
    // ============================================================================
    
    /// Real D-Wave backend using Leap Cloud API
    type RealDWaveBackend(config: DWaveConfig) =
        
        let client = new DWaveClient(config)
        
        /// Convert a raw SAPI solution (Ising spins) to a DWaveSolution domain type.
        /// D-Wave SAPI returns Ising solutions as {-1, +1} natively.
        let convertSapiSolution (spins: int[]) (energy: float) (occurrences: int) : DWaveTypes.DWaveSolution =
            let spinMap =
                spins
                |> Array.mapi (fun i s -> (i, s))
                |> Map.ofArray
            {
                Spins = spinMap
                Energy = energy
                NumOccurrences = occurrences
                ChainBreakFraction = 0.0
            }
        
        /// Get max qubits for solver
        let getMaxQubits (solverName: string) : int =
            if solverName.Contains("system6") then 5640
            elif solverName.Contains("system4") then 5000
            elif solverName.Contains("system1") then 5000
            elif solverName.Contains("prototype") then 1200
            elif solverName.Contains("2000q") then 2048
            else 5000  // Default
        
        /// Execute circuit on D-Wave hardware
        member private _.ExecuteCore(circuit: ICircuit, numShots: int) : Async<Result<ExecutionResult, QuantumError>> =
            async {
                // Extract QUBO from QAOA circuit
                match extractFromICircuit circuit with
                | Error e ->
                    return Error (QuantumError.ValidationError ("QUBO extraction", $"Failed to extract QUBO from circuit: {e}"))
                
                | Ok qubo ->
                    // Convert QUBO to Ising
                    let ising = quboToIsing qubo
                    
                    // Validate qubit count
                    let numQubits = getNumVariables qubo
                    let maxQubits = getMaxQubits config.Solver
                    
                    if numQubits > maxQubits then
                        return Error (QuantumError.ValidationError ("qubit count", $"Problem requires {numQubits} qubits, but {config.Solver} supports max {maxQubits}"))
                    else
                        // Submit to D-Wave
                        let! submitResult = client.SubmitProblemAsync(ising, numShots)
                        
                        match submitResult with
                        | Error e -> return Error (QuantumError.BackendError ("D-Wave Submit", e))
                        | Ok jobId ->
                            // Wait for completion
                            let! pollResult = client.PollJobAsync(jobId)
                            
                            match pollResult with
                            | Error e -> return Error (QuantumError.BackendError ("D-Wave Poll", e))
                            | Ok solution ->
                                // Convert Ising spin solutions to binary measurements
                                // D-Wave returns Ising spins {-1,+1}; convert to QUBO binary {0,1}
                                let measurements =
                                    Array.zip solution.solutions solution.num_occurrences
                                    |> Array.collect (fun (spins, occurrences) ->
                                        let spinMap =
                                            spins
                                            |> Array.mapi (fun i s -> (i, s))
                                            |> Map.ofArray
                                        let binary = isingToQubo spinMap
                                        let bitstring =
                                            [| 0 .. numQubits - 1 |]
                                            |> Array.map (fun i -> Map.tryFind i binary |> Option.defaultValue 0)
                                        Array.replicate occurrences bitstring
                                    )
                                
                                let metadata =
                                    Map.ofList [
                                        ("job_id", box jobId)
                                        ("solver", box config.Solver)
                                        ("endpoint", box config.Endpoint)
                                        ("timing", box solution.timing)
                                    ]
                                
                                let result = {
                                    Measurements = measurements
                                    NumShots = numShots
                                    BackendName = $"D-Wave {config.Solver}"
                                    Metadata = metadata
                                }
                                
                                return Ok result
            }
        
        /// Execute circuit and return full result with measurements
        [<System.Obsolete("Use ExecuteCore (async) instead. This synchronous wrapper blocks the calling thread.")>]
        member this.Execute (circuit: ICircuit) (numShots: int) : Result<ExecutionResult, QuantumError> =
            if numShots <= 0 then
                Error (QuantumError.ValidationError ("numShots", $"must be > 0, got {numShots}"))
            else
                this.ExecuteCore(circuit, numShots) |> Async.RunSynchronously
        
        // ================================================================
        // IQuantumBackend IMPLEMENTATION
        // D-Wave annealing backends extract QUBO from circuits, convert
        // to Ising, and submit to real hardware via Leap Cloud API.
        // ================================================================
        
        interface BackendAbstraction.IQuantumBackend with
            member _.Name = $"D-Wave {config.Solver}"
            
            member _.NativeStateType = QuantumStateType.Annealing
            
            member _.ExecuteToState (circuit: ICircuit) : Result<QuantumState, QuantumError> =
                match extractFromICircuit circuit with
                | Error e ->
                    Error (QuantumError.ValidationError ("QUBO extraction", $"Failed to extract QUBO from circuit: {e}"))
                | Ok qubo ->
                    let ising = quboToIsing qubo
                    let numQubits = getNumVariables qubo
                    let maxQubits = getMaxQubits config.Solver
                    if numQubits > maxQubits then
                        Error (QuantumError.ValidationError ("qubit count", $"Problem requires {numQubits} qubits, but {config.Solver} supports max {maxQubits}"))
                    else
                        // Submit to D-Wave and poll for result synchronously
                        let submitResult = client.SubmitProblemAsync(ising, 1) |> Async.RunSynchronously
                        match submitResult with
                        | Error e ->
                            Error (QuantumError.BackendError ("D-Wave Submit", e))
                        | Ok jobId ->
                            let pollResult = client.PollJobAsync(jobId) |> Async.RunSynchronously
                            match pollResult with
                            | Error e ->
                                Error (QuantumError.BackendError ("D-Wave Poll", e))
                            | Ok solution ->
                                // Convert D-Wave SAPI solutions to DWaveSolution format
                                let dwaveSolutions =
                                    Array.zip3 solution.solutions solution.energies solution.num_occurrences
                                    |> Array.map (fun (spins, energy, occurrences) ->
                                        convertSapiSolution spins energy occurrences)
                                    |> Array.toList
                                Ok (QuantumState.IsingSamples (box ising, box dwaveSolutions))
            
            member _.InitializeState (numQubits: int) : Result<QuantumState, QuantumError> =
                let emptyIsing : IsingProblem = {
                    LinearCoeffs = Map.empty
                    QuadraticCoeffs = Map.empty
                    Offset = 0.0
                }
                Ok (QuantumState.IsingSamples (box emptyIsing, box []))
            
            member this.ApplyOperation (operation: BackendAbstraction.QuantumOperation) (state: QuantumState) : Result<QuantumState, QuantumError> =
                match operation with
                | BackendAbstraction.QuantumOperation.Sequence ops ->
                    ops
                    |> List.fold (fun stateResult op ->
                        match stateResult with
                        | Error err -> Error err
                        | Ok currentState ->
                            (this :> BackendAbstraction.IQuantumBackend).ApplyOperation op currentState
                    ) (Ok state)
                | BackendAbstraction.QuantumOperation.Extension (:? DWaveBackend.AnnealIsingOperation as annealOp) ->
                    if annealOp.NumReads <= 0 then
                        Error (QuantumError.ValidationError ("numReads", $"must be > 0, got {annealOp.NumReads}"))
                    else
                        match state with
                        | QuantumState.IsingSamples _ ->
                            // Submit annealing problem to real D-Wave hardware
                            let submitResult =
                                client.SubmitProblemAsync(annealOp.Problem, annealOp.NumReads)
                                |> Async.RunSynchronously
                            match submitResult with
                            | Error e ->
                                Error (QuantumError.BackendError ("D-Wave Submit", e))
                            | Ok jobId ->
                                let pollResult = client.PollJobAsync(jobId) |> Async.RunSynchronously
                                match pollResult with
                                | Error e ->
                                    Error (QuantumError.BackendError ("D-Wave Poll", e))
                                | Ok solution ->
                                    let dwaveSolutions =
                                        Array.zip3 solution.solutions solution.energies solution.num_occurrences
                                        |> Array.map (fun (spins, energy, occurrences) ->
                                            convertSapiSolution spins energy occurrences)
                                        |> Array.toList
                                    Ok (QuantumState.IsingSamples (box annealOp.Problem, box dwaveSolutions))
                        | _ ->
                            Error (QuantumError.OperationError ("ApplyOperation", $"AnnealIsingOperation requires Annealing state, got {QuantumState.stateType state}"))
                | BackendAbstraction.QuantumOperation.Extension ext ->
                    Error (QuantumError.OperationError ("ApplyOperation", $"Extension operation '{ext.Id}' is not supported by D-Wave backend"))
                | _ ->
                    Error (QuantumError.OperationError ("ApplyOperation", "D-Wave annealing backend only supports annealing intent operations"))
            
            member this.SupportsOperation (operation: BackendAbstraction.QuantumOperation) : bool =
                match operation with
                | BackendAbstraction.QuantumOperation.Extension (:? DWaveBackend.AnnealIsingOperation) -> true
                | BackendAbstraction.QuantumOperation.Sequence ops ->
                    ops |> List.forall (fun op -> (this :> BackendAbstraction.IQuantumBackend).SupportsOperation op)
                | _ -> false

            member this.ExecuteToStateAsync (circuit: ICircuit) (_ct: CancellationToken) : Task<Result<QuantumState, QuantumError>> =
                // RealDWaveBackend has true async I/O (client.SubmitProblemAsync / client.PollJobAsync).
                // ExecuteToState already uses these internally via Async.RunSynchronously.
                // Here we bridge the Async pipeline to Task without blocking.
                match extractFromICircuit circuit with
                | Error e ->
                    Task.FromResult(Error (QuantumError.ValidationError ("QUBO extraction", $"Failed to extract QUBO from circuit: {e}")))
                | Ok qubo ->
                    let ising = quboToIsing qubo
                    let numQubits = getNumVariables qubo
                    let maxQubits = getMaxQubits config.Solver
                    if numQubits > maxQubits then
                        Task.FromResult(Error (QuantumError.ValidationError ("qubit count", $"Problem requires {numQubits} qubits, but {config.Solver} supports max {maxQubits}")))
                    else
                        let asyncWork = async {
                            let! submitResult = client.SubmitProblemAsync(ising, 1)
                            match submitResult with
                            | Error e ->
                                return Error (QuantumError.BackendError ("D-Wave Submit", e))
                            | Ok jobId ->
                                let! pollResult = client.PollJobAsync(jobId)
                                match pollResult with
                                | Error e ->
                                    return Error (QuantumError.BackendError ("D-Wave Poll", e))
                                | Ok solution ->
                                    let dwaveSolutions =
                                        Array.zip3 solution.solutions solution.energies solution.num_occurrences
                                        |> Array.map (fun (spins, energy, occurrences) ->
                                            convertSapiSolution spins energy occurrences)
                                        |> Array.toList
                                    return Ok (QuantumState.IsingSamples (box ising, box dwaveSolutions))
                        }
                        Async.StartAsTask(asyncWork)

            member this.ApplyOperationAsync (operation: BackendAbstraction.QuantumOperation) (state: QuantumState) (_ct: CancellationToken) : Task<Result<QuantumState, QuantumError>> =
                match operation with
                | BackendAbstraction.QuantumOperation.Sequence ops ->
                    // Apply sequence by folding async over operations
                    let asyncWork = async {
                        let mutable current = Ok state
                        for op in ops do
                            match current with
                            | Error _ -> ()
                            | Ok currentState ->
                                let! next =
                                    (this :> BackendAbstraction.IQuantumBackend).ApplyOperationAsync op currentState CancellationToken.None
                                    |> Async.AwaitTask
                                current <- next
                        return current
                    }
                    Async.StartAsTask(asyncWork)
                | BackendAbstraction.QuantumOperation.Extension (:? DWaveBackend.AnnealIsingOperation as annealOp) ->
                    if annealOp.NumReads <= 0 then
                        Task.FromResult(Error (QuantumError.ValidationError ("numReads", $"must be > 0, got {annealOp.NumReads}")))
                    else
                        match state with
                        | QuantumState.IsingSamples _ ->
                            let asyncWork = async {
                                let! submitResult = client.SubmitProblemAsync(annealOp.Problem, annealOp.NumReads)
                                match submitResult with
                                | Error e ->
                                    return Error (QuantumError.BackendError ("D-Wave Submit", e))
                                | Ok jobId ->
                                    let! pollResult = client.PollJobAsync(jobId)
                                    match pollResult with
                                    | Error e ->
                                        return Error (QuantumError.BackendError ("D-Wave Poll", e))
                                    | Ok solution ->
                                        let dwaveSolutions =
                                            Array.zip3 solution.solutions solution.energies solution.num_occurrences
                                            |> Array.map (fun (spins, energy, occurrences) ->
                                                convertSapiSolution spins energy occurrences)
                                            |> Array.toList
                                        return Ok (QuantumState.IsingSamples (box annealOp.Problem, box dwaveSolutions))
                            }
                            Async.StartAsTask(asyncWork)
                        | _ ->
                            Task.FromResult(Error (QuantumError.OperationError ("ApplyOperation", $"AnnealIsingOperation requires Annealing state, got {QuantumState.stateType state}")))
                | BackendAbstraction.QuantumOperation.Extension ext ->
                    Task.FromResult(Error (QuantumError.OperationError ("ApplyOperation", $"Extension operation '{ext.Id}' is not supported by D-Wave backend")))
                | _ ->
                    Task.FromResult(Error (QuantumError.OperationError ("ApplyOperation", "D-Wave annealing backend only supports annealing intent operations")))
        
        interface IDisposable with
            member _.Dispose() = (client :> IDisposable).Dispose()
    
    // ============================================================================
    // FACTORY FUNCTIONS
    // ============================================================================
    
    /// Create real D-Wave backend with configuration
    ///
    /// Parameters:
    /// - config: D-Wave configuration with API token
    ///
    /// Returns: RealDWaveBackend for D-Wave hardware
    ///
    /// Example:
    ///   let config = { 
    ///       ApiToken = "DEV-xxxxx"
    ///       Endpoint = "https://cloud.dwavesys.com/sapi/v2/"
    ///       Solver = "Advantage_system6.1"
    ///       TimeoutMs = Some 300000
    ///   }
    ///   let backend = create config
    let create (config: DWaveConfig) : RealDWaveBackend =
        new RealDWaveBackend(config)
    
    /// Create real D-Wave backend from environment variables
    ///
    /// Requires:
    /// - DWAVE_API_TOKEN: Your API token
    /// - DWAVE_ENDPOINT: API endpoint (optional, defaults to cloud.dwavesys.com)
    /// - DWAVE_SOLVER: Solver name (optional, defaults to Advantage_system6.1)
    ///
    /// Returns: QuantumResult<RealDWaveBackend>
    ///
    /// Example:
    ///   match createFromEnv() with
    ///   | Ok backend -> backend.Execute circuit 1000
    ///   | Error msg -> printfn $"Error: {msg}"
    let createFromEnv () : QuantumResult<RealDWaveBackend> =
        defaultConfig()
        |> Result.map create
