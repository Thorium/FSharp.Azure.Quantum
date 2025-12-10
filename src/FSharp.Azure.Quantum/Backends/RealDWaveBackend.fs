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
    open System.Threading.Tasks
    open FSharp.Azure.Quantum.Core.CircuitAbstraction
    open FSharp.Azure.Quantum.Core.BackendAbstraction
    open FSharp.Azure.Quantum.Core
    open FSharp.Azure.Quantum.Algorithms.QuboExtraction
    open FSharp.Azure.Quantum.Algorithms.QuboToIsing
    open FSharp.Azure.Quantum.Backends.DWaveTypes
    
    // ============================================================================
    // LOCAL TYPES (D-Wave annealing backends don't use IQuantumBackend)
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
                    let numQubits = qubo |> Map.toSeq |> Seq.map (fun ((i,j),_) -> max i j) |> Seq.max |> (+) 1
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
                                // Convert solutions to measurements
                                let measurements =
                                    Array.zip solution.solutions solution.num_occurrences
                                    |> Array.collect (fun (bitstring, occurrences) ->
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
        
        // Note: RealDWaveBackend does NOT implement IQuantumBackend interface
        // D-Wave annealing backends are fundamentally different from gate-based backends
        // They work with QUBO/Ising problems, not quantum circuits/states
        // Use ExecuteCore method directly or via QuboExtraction module
        
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
    /// Returns: QuantumResult<IQuantumBackend>
    ///
    /// Example:
    ///   match createFromEnv() with
    ///   | Ok backend -> backend.ExecuteCore circuit 1000
    ///   | Error msg -> printfn $"Error: {msg}"
    let createFromEnv () : QuantumResult<RealDWaveBackend> =
        defaultConfig()
        |> Result.map create
