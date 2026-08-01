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
    // D-WAVE SAPI "qp" WIRE FORMAT
    //
    // SAPI structured (QPU) solvers accept problem data only in the "qp"
    // encoding: linear/quadratic coefficients are packed as little-endian
    // 64-bit doubles, base64-encoded, and ordered by the solver's `qubits`
    // and `couplers` properties. Answers come back the same way (base64
    // doubles/int32s plus bit-packed solutions, MSB-first within each byte).
    //
    // Reference:
    // - https://docs.dwavequantum.com/en/latest/leap_sapi/sapi_rest.html
    // - dwave-cloud-client (dwave/cloud/coders.py: encode_problem_as_qp / decode_qp)
    // ============================================================================

    /// D-Wave solution result (decoded from SAPI "qp" answer)
    type private DWaveSolution = {
        solutions: int[][]
        energies: float[]
        num_occurrences: int[]
        timing: Map<string, float> option
    }

    /// QPU solver working graph, from GET solvers/remote/{id}/
    /// (properties.qubits and properties.couplers)
    type private SolverTopology = {
        /// Active qubit indices, in SAPI encoding order
        Qubits: int[]
        /// Active couplers (qubit pairs), in SAPI encoding order
        Couplers: (int * int)[]
        /// Qubits as a set, for validation
        QubitSet: Set<int>
        /// Couplers normalized to (min, max), for validation
        CouplerSet: Set<int * int>
    }

    /// Encode a float array as base64 little-endian 64-bit doubles (SAPI "qp" encoding)
    let private encodeDoubles (values: float[]) : string =
        let bytes = Array.zeroCreate<byte> (values.Length * 8)
        values |> Array.iteri (fun i v ->
            let b = BitConverter.GetBytes(v)
            let b = if BitConverter.IsLittleEndian then b else Array.rev b
            Array.blit b 0 bytes (i * 8) 8)
        Convert.ToBase64String(bytes)

    /// Decode base64 little-endian 64-bit doubles (SAPI "qp" encoding)
    let private decodeDoubles (base64: string) : float[] =
        let bytes = Convert.FromBase64String(base64)
        Array.init (bytes.Length / 8) (fun i ->
            let slice = bytes.[i * 8 .. i * 8 + 7]
            let slice = if BitConverter.IsLittleEndian then slice else Array.rev slice
            BitConverter.ToDouble(slice, 0))

    /// Decode base64 little-endian 32-bit integers (SAPI "qp" encoding)
    let private decodeInts (base64: string) : int[] =
        let bytes = Convert.FromBase64String(base64)
        Array.init (bytes.Length / 4) (fun i ->
            let slice = bytes.[i * 4 .. i * 4 + 3]
            let slice = if BitConverter.IsLittleEndian then slice else Array.rev slice
            BitConverter.ToInt32(slice, 0))

    /// Encode an Ising problem in the SAPI "qp" data format for a given solver.
    ///
    /// Per the SAPI REST spec:
    /// - `lin` holds one double per qubit in the solver's `qubits` property
    ///   order: the linear bias for qubits used by the problem (active), NaN
    ///   for unused (inactive) qubits.
    /// - `quad` holds one double per coupler in the solver's `couplers`
    ///   property order whose both endpoints are active.
    ///
    /// The problem must already be expressed on the solver's working graph:
    /// every variable must be a physical qubit and every quadratic term a
    /// physical coupler. Otherwise an Error is returned (minor-embedding of
    /// logical problems is not performed here).
    let private encodeProblemAsQp (topology: SolverTopology) (ising: IsingProblem) : Result<{| lin: string; quad: string |}, string> =
        // Active qubits = all variables mentioned by the problem
        let activeQubits =
            let fromLinear = ising.LinearCoeffs |> Map.toSeq |> Seq.map fst
            let fromQuadratic = ising.QuadraticCoeffs |> Map.toSeq |> Seq.collect (fun ((i, j), _) -> [i; j])
            Set.ofSeq (Seq.append fromLinear fromQuadratic)

        // Validate against the hardware graph
        let invalidQubits =
            activeQubits |> Set.filter (fun q -> not (topology.QubitSet.Contains q))
        let invalidCouplers =
            ising.QuadraticCoeffs
            |> Map.toSeq
            |> Seq.map fst
            |> Seq.filter (fun (i, j) -> not (topology.CouplerSet.Contains (min i j, max i j)))
            |> Seq.toList

        if not (Set.isEmpty invalidQubits) then
            let sample = invalidQubits |> Seq.truncate 5 |> Seq.map string |> String.concat ", "
            Error ($"Problem uses {Set.count invalidQubits} variable(s) that are not working qubits on this solver (e.g. {sample}). " +
                   "Direct QPU submission requires the problem to be expressed on the solver's working graph; " +
                   "minor-embedding of logical problems is not implemented in this backend.")
        elif not (List.isEmpty invalidCouplers) then
            let sample = invalidCouplers |> Seq.truncate 5 |> Seq.map string |> String.concat ", "
            Error ($"Problem uses {List.length invalidCouplers} coupling(s) that are not physical couplers on this solver (e.g. {sample}). " +
                   "Direct QPU submission requires the problem to be expressed on the solver's working graph; " +
                   "minor-embedding of logical problems is not implemented in this backend.")
        else
            let lin =
                topology.Qubits
                |> Array.map (fun q ->
                    if activeQubits.Contains q then
                        ising.LinearCoeffs |> Map.tryFind q |> Option.defaultValue 0.0
                    else
                        Double.NaN)

            let quad =
                topology.Couplers
                |> Array.choose (fun (q1, q2) ->
                    if activeQubits.Contains q1 && activeQubits.Contains q2 then
                        let j12 = ising.QuadraticCoeffs |> Map.tryFind (q1, q2) |> Option.defaultValue 0.0
                        let j21 = ising.QuadraticCoeffs |> Map.tryFind (q2, q1) |> Option.defaultValue 0.0
                        Some (j12 + j21)
                    else
                        None)

            Ok {| lin = encodeDoubles lin; quad = encodeDoubles quad |}

    /// Decode a SAPI "qp"-format answer for an Ising problem.
    ///
    /// Answer fields (all base64):
    /// - energies: little-endian doubles, ordered low to high
    /// - active_variables: little-endian int32 indices of active qubits
    /// - num_occurrences: little-endian int32 (present in histogram mode)
    /// - solutions: bit-packed samples, one bit per active variable in
    ///   active_variables order, MSB-first within each byte, each solution
    ///   padded to a byte boundary; bit 0 = spin -1, bit 1 = spin +1.
    let private decodeQpAnswer (answer: JsonElement) : Result<DWaveSolution, string> =
        try
            let format =
                match answer.TryGetProperty("format") with
                | true, f -> f.GetString()
                | _ -> ""

            if format <> "qp" then
                Error $"Unsupported D-Wave answer format '{format}' (expected 'qp')"
            else
                let energiesRaw = decodeDoubles (answer.GetProperty("energies").GetString())
                let offset =
                    match answer.TryGetProperty("offset") with
                    | true, o when o.ValueKind = JsonValueKind.Number -> o.GetDouble()
                    | _ -> 0.0
                let energies = energiesRaw |> Array.map (fun e -> e + offset)

                let activeVariables = decodeInts (answer.GetProperty("active_variables").GetString())

                let numOccurrences =
                    match answer.TryGetProperty("num_occurrences") with
                    | true, n when n.ValueKind = JsonValueKind.String -> decodeInts (n.GetString())
                    | _ -> Array.create energies.Length 1  // answer_mode=raw: one occurrence each

                let numVariables = answer.GetProperty("num_variables").GetInt32()
                let solutionBytes = Convert.FromBase64String(answer.GetProperty("solutions").GetString())
                let bytesPerSolution = (activeVariables.Length + 7) / 8

                let solutions =
                    Array.init energies.Length (fun s ->
                        // Inactive variables default to 0 ("unused" spin)
                        let solution = Array.zeroCreate<int> numVariables
                        for k in 0 .. activeVariables.Length - 1 do
                            let b = int solutionBytes.[s * bytesPerSolution + (k / 8)]
                            let bit = (b >>> (7 - (k % 8))) &&& 1
                            solution.[activeVariables.[k]] <- if bit = 1 then 1 else -1
                        solution)

                let timing =
                    match answer.TryGetProperty("timing") with
                    | true, t when t.ValueKind = JsonValueKind.Object ->
                        t.EnumerateObject()
                        |> Seq.choose (fun p ->
                            if p.Value.ValueKind = JsonValueKind.Number then
                                Some (p.Name, p.Value.GetDouble())
                            else None)
                        |> Map.ofSeq
                        |> Some
                    | _ -> None

                Ok {
                    solutions = solutions
                    energies = energies
                    num_occurrences = numOccurrences
                    timing = timing
                }
        with ex ->
            Error $"Failed to decode D-Wave 'qp' answer: {ex.Message}"

    // ============================================================================
    // D-WAVE SAPI CLIENT
    // ============================================================================

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

        /// Cached solver topology (fetched once per client)
        let mutable topologyCache : SolverTopology option = None

        /// Fetch the solver's working graph (qubits/couplers) from SAPI.
        /// Required to build the "qp" problem encoding, whose coefficient
        /// arrays are ordered by these solver properties.
        member _.GetSolverTopologyAsync() : Async<Result<SolverTopology, string>> =
            async {
                match topologyCache with
                | Some topology -> return Ok topology
                | None ->
                    try
                        use! response =
                            httpClient.GetAsync($"{config.Endpoint}solvers/remote/{config.Solver}/")
                            |> Async.AwaitTask

                        if not response.IsSuccessStatusCode then
                            let! errorBody = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                            return Error $"Failed to fetch solver '{config.Solver}' ({int response.StatusCode}): {errorBody}"
                        else
                            let! responseBody = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                            use doc = JsonDocument.Parse(responseBody)

                            match doc.RootElement.TryGetProperty("properties") with
                            | false, _ ->
                                return Error $"Solver '{config.Solver}' response has no 'properties' field"
                            | true, properties ->
                                match properties.TryGetProperty("qubits"), properties.TryGetProperty("couplers") with
                                | (true, qubitsEl), (true, couplersEl) ->
                                    let qubits =
                                        qubitsEl.EnumerateArray()
                                        |> Seq.map (fun q -> q.GetInt32())
                                        |> Seq.toArray
                                    let couplers =
                                        couplersEl.EnumerateArray()
                                        |> Seq.map (fun c -> (c.[0].GetInt32(), c.[1].GetInt32()))
                                        |> Seq.toArray
                                    let topology = {
                                        Qubits = qubits
                                        Couplers = couplers
                                        QubitSet = Set.ofArray qubits
                                        CouplerSet = couplers |> Array.map (fun (a, b) -> (min a b, max a b)) |> Set.ofArray
                                    }
                                    topologyCache <- Some topology
                                    return Ok topology
                                | _ ->
                                    return Error ($"Solver '{config.Solver}' does not expose qubits/couplers properties. " +
                                                  "Only structured QPU solvers are supported by this backend (not hybrid solvers).")
                    with ex ->
                        return Error $"Failed to fetch solver topology: {ex.Message}"
            }

        /// Submit Ising problem to D-Wave using the SAPI "qp" data format.
        /// SAPI expects POST problems/ with a JSON array of problem messages:
        ///   [{ "solver": ..., "type": "ising",
        ///      "data": { "format": "qp", "lin": <b64>, "quad": <b64>, "offset": ... },
        ///      "params": { "num_reads": ... } }]
        member this.SubmitProblemAsync(ising: IsingProblem, numReads: int) : Async<Result<string, string>> =
            async {
                try
                    let! topologyResult = this.GetSolverTopologyAsync()

                    match topologyResult with
                    | Error e -> return Error e
                    | Ok topology ->
                        match encodeProblemAsQp topology ising with
                        | Error e -> return Error e
                        | Ok qp ->
                            let problem = {|
                                solver = config.Solver
                                ``type`` = "ising"
                                data = {|
                                    format = "qp"
                                    lin = qp.lin
                                    quad = qp.quad
                                    // Always submit 0: whether SAPI folds a problem offset into
                                    // returned energies is version-dependent, so keeping the wire
                                    // offset at 0 makes answer energies unambiguously the raw
                                    // lin/quad Ising energies. The local ising.Offset is added
                                    // exactly once in PollJobAsync.
                                    offset = 0.0
                                |}
                                ``params`` = {| num_reads = numReads |}
                            |}

                            // SAPI expects an array of problem messages
                            let json = JsonSerializer.Serialize([| problem |], jsonOptions)
                            let content = new StringContent(json, Encoding.UTF8, "application/json")

                            use! response =
                                httpClient.PostAsync($"{config.Endpoint}problems/", content)
                                |> Async.AwaitTask

                            if not response.IsSuccessStatusCode then
                                let! errorBody = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                                return Error $"D-Wave API error ({int response.StatusCode}): {errorBody}"
                            else
                                // Response is an array of problem status messages
                                let! responseBody = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                                use doc = JsonDocument.Parse(responseBody)
                                let root = doc.RootElement

                                if root.ValueKind <> JsonValueKind.Array || root.GetArrayLength() = 0 then
                                    return Error $"Unexpected D-Wave submit response: {responseBody}"
                                else
                                    let status = root.[0]
                                    match status.TryGetProperty("id") with
                                    | true, idEl when idEl.ValueKind = JsonValueKind.String ->
                                        return Ok (idEl.GetString())
                                    | _ ->
                                        // Per-problem submission error (e.g. invalid solver/params)
                                        let errorMsg =
                                            match status.TryGetProperty("error_msg") with
                                            | true, e -> e.GetString()
                                            | _ ->
                                                match status.TryGetProperty("error_message") with
                                                | true, e -> e.GetString()
                                                | _ -> status.GetRawText()
                                        return Error $"D-Wave rejected problem submission: {errorMsg}"

                with ex ->
                    return Error $"Failed to submit D-Wave problem: {ex.Message}"
            }
        
        /// Poll for job completion.
        /// GET problems/{id}/ returns the problem status message; once the
        /// status is COMPLETED the same message carries the "answer" object
        /// in the SAPI "qp" format, which is decoded here.
        /// `problemOffset` is the Ising constant term of the submitted problem
        /// (sent as 0 on the wire — see SubmitProblemAsync); it is added to the
        /// decoded energies here, exactly once, so reported energies match the
        /// caller's QUBO/Ising energy scale.
        member _.PollJobAsync(jobId: string, problemOffset: float) : Async<Result<DWaveSolution, string>> =
            let rec pollLoop attempts =
                async {
                    if attempts >= 60 then
                        return Error $"D-Wave job {jobId} timed out after 300 seconds"
                    else
                        use! response =
                            httpClient.GetAsync($"{config.Endpoint}problems/{jobId}/")
                            |> Async.AwaitTask

                        if not response.IsSuccessStatusCode then
                            let! errorBody = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                            return Error $"D-Wave API error ({int response.StatusCode}): {errorBody}"
                        else
                            let! responseBody = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                            use doc = JsonDocument.Parse(responseBody)
                            let root = doc.RootElement

                            let status =
                                match root.TryGetProperty("status") with
                                | true, s when s.ValueKind = JsonValueKind.String -> s.GetString()
                                | _ -> ""

                            // SAPI statuses: PENDING, IN_PROGRESS, COMPLETED, FAILED, CANCELLED
                            match status.ToUpperInvariant() with
                            | "COMPLETED" ->
                                match root.TryGetProperty("answer") with
                                | true, answer ->
                                    return
                                        decodeQpAnswer answer
                                        |> Result.map (fun sol ->
                                            if problemOffset = 0.0 then sol
                                            else { sol with energies = sol.energies |> Array.map (fun e -> e + problemOffset) })
                                | _ -> return Error $"D-Wave job {jobId} completed but response contains no answer"
                            | "FAILED" | "CANCELLED" ->
                                let detail =
                                    match root.TryGetProperty("error_message") with
                                    | true, e when e.ValueKind = JsonValueKind.String -> $": {e.GetString()}"
                                    | _ -> ""
                                return Error $"D-Wave job {jobId} failed with status: {status}{detail}"
                            | _ ->
                                // PENDING / IN_PROGRESS - wait and retry
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
                            let! pollResult = client.PollJobAsync(jobId, ising.Offset)
                            
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
                            let pollResult = client.PollJobAsync(jobId, ising.Offset) |> Async.RunSynchronously
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
                                let pollResult = client.PollJobAsync(jobId, annealOp.Problem.Offset) |> Async.RunSynchronously
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
                                let! pollResult = client.PollJobAsync(jobId, ising.Offset)
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
                                    let! pollResult = client.PollJobAsync(jobId, annealOp.Problem.Offset)
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
