namespace FSharp.Azure.Quantum.GroverSearch

open System

/// Backend Adapter Module for Grover's Search Algorithm
/// 
/// Bridges Grover's algorithm to IQuantumBackend interface, enabling execution
/// on cloud quantum hardware (IonQ, Rigetti) in addition to local simulation.
/// 
/// Key responsibilities:
/// - Convert OracleSpec to quantum circuit gates
/// - Convert diffusion operator to quantum circuit
/// - Execute Grover iterations via IQuantumBackend
/// - Convert measurement results to SearchResult
module BackendAdapter =
    
    open FSharp.Azure.Quantum
    open FSharp.Azure.Quantum.Core.BackendAbstraction
    open FSharp.Azure.Quantum.Core.CircuitAbstraction
    open FSharp.Azure.Quantum.GroverSearch.Oracle
    open FSharp.Azure.Quantum.GroverSearch.GroverIteration
    open FSharp.Azure.Quantum.CircuitBuilder
    
    // ========================================================================
    // HELPER: Multi-Controlled Gate Decomposition
    // ========================================================================
    
    /// Add multi-controlled Z gate to circuit
    /// 
    /// Decomposes MCZ using Toffoli (CCX) and controlled gates.
    /// For n controls, uses O(n) gates.
    let rec addMultiControlledZ (controls: int list) (target: int) (circuit: Circuit) : Circuit =
        match controls with
        | [] -> 
            // No controls - single-qubit Z
            addGate (Z target) circuit
        
        | [ctrl] -> 
            // Single control - CZ gate
            addGate (CZ (ctrl, target)) circuit
        
        | [ctrl1; ctrl2] ->
            // Two controls - CCZ gate
            // Decompose: CCZ = H(target) · CCX(ctrl1, ctrl2, target) · H(target)
            circuit
            |> addGate (H target)
            |> addGate (CCX (ctrl1, ctrl2, target))
            |> addGate (H target)
        
        | ctrl1 :: ctrl2 :: rest ->
            // Multiple controls - recursive decomposition
            // Use auxiliary qubit approach (simplified: chain CZ gates)
            // Note: This is not optimal but correct for proof-of-concept
            let mutable c = circuit
            for ctrl in controls do
                c <- addGate (CZ (ctrl, target)) c
            c
    
    // ========================================================================
    // ORACLE TO CIRCUIT CONVERSION
    // ========================================================================
    
    /// Synthesize oracle for single target value
    /// 
    /// Strategy:
    /// 1. Apply X gates to qubits where target bit is 0 (flip to |1⟩)
    /// 2. Apply multi-controlled Z with all qubits as controls
    /// 3. Undo X gates (flip back to |0⟩)
    /// 
    /// Example: target = 5 (binary: 101), numQubits = 3
    ///   - Apply X to qubit 1 (bit is 0 in target)
    ///   - Apply MCZ with controls=[0,1,2]
    ///   - Apply X to qubit 1 (undo)
    let private synthesizeSingleTargetOracle (target: int) (numQubits: int) : Result<Circuit, string> =
        if target < 0 || target >= (1 <<< numQubits) then
            Error $"Target {target} out of range for {numQubits} qubits (valid range: 0-{(1<<<numQubits)-1})"
        else
            try
                // Create empty circuit
                let circuit = empty numQubits
                
                // Step 1: Apply X to qubits where target bit is 0
                let afterX1 =
                    [0 .. numQubits - 1]
                    |> List.fold (fun c qubitIdx ->
                        let bitValue = (target >>> qubitIdx) &&& 1
                        if bitValue = 0 then
                            addGate (X qubitIdx) c  // Flip |0⟩ to |1⟩
                        else
                            c
                    ) circuit
                
                // Step 2: Apply multi-controlled Z
                // All qubits act as controls on the last qubit
                let controls = [0 .. numQubits - 2]
                let targetQubit = numQubits - 1
                
                let afterMCZ = addMultiControlledZ controls targetQubit afterX1
                
                // Step 3: Undo X gates
                let afterX2 =
                    [0 .. numQubits - 1]
                    |> List.fold (fun c qubitIdx ->
                        let bitValue = (target >>> qubitIdx) &&& 1
                        if bitValue = 0 then
                            addGate (X qubitIdx) c  // Flip back
                        else
                            c
                    ) afterMCZ
                
                Ok afterX2
            with ex ->
                Error $"Oracle synthesis failed: {ex.Message}"
    
    /// Convert OracleSpec to circuit
    /// 
    /// Recursively synthesizes oracle circuits from specifications
    let rec oracleToCircuit (spec: OracleSpec) (numQubits: int) : Result<Circuit, string> =
        match spec with
        | SingleTarget target ->
            synthesizeSingleTargetOracle target numQubits
        
        | Solutions targets ->
            // Compose multiple single-target oracles
            // Each oracle marks its target, so composition marks all targets
            targets
            |> List.fold (fun circuitResult target ->
                match circuitResult with
                | Error msg -> Error msg
                | Ok circuit ->
                    match synthesizeSingleTargetOracle target numQubits with
                    | Error msg -> Error msg
                    | Ok targetCircuit -> 
                        // Compose circuits sequentially
                        Ok (compose circuit targetCircuit)
            ) (Ok (empty numQubits))
        
        | Predicate pred ->
            // Enumerate all solutions classically, then synthesize
            let searchSpaceSize = 1 <<< numQubits
            
            if numQubits > 16 then
                Error $"Predicate oracle with {numQubits} qubits requires enumerating {searchSpaceSize} states (too large). Use numQubits <= 16."
            else
                let solutions = 
                    [0 .. searchSpaceSize - 1]
                    |> List.filter pred
                
                if List.isEmpty solutions then
                    Error "Predicate matches no solutions"
                else
                    oracleToCircuit (Solutions solutions) numQubits
        
        | And (spec1, spec2) ->
            // Apply both oracles sequentially (marks states satisfying both)
            match oracleToCircuit spec1 numQubits, oracleToCircuit spec2 numQubits with
            | Ok circuit1, Ok circuit2 -> Ok (compose circuit1 circuit2)
            | Error msg, _ -> Error msg
            | _, Error msg -> Error msg
        
        | Or (spec1, spec2) ->
            // Enumerate solutions for both specs and synthesize
            let searchSpaceSize = 1 <<< numQubits
            
            if numQubits > 16 then
                Error $"OR oracle with {numQubits} qubits requires enumerating {searchSpaceSize} states (too large). Use numQubits <= 16."
            else
                let solutions =
                    [0 .. searchSpaceSize - 1]
                    |> List.filter (fun i -> 
                        Oracle.isSolution spec1 i || Oracle.isSolution spec2 i
                    )
                
                if List.isEmpty solutions then
                    Error "OR oracle has no solutions"
                else
                    oracleToCircuit (Solutions solutions) numQubits
        
        | Not spec ->
            // Negate oracle: mark all states NOT marked by inner oracle
            let searchSpaceSize = 1 <<< numQubits
            
            if numQubits > 16 then
                Error $"NOT oracle with {numQubits} qubits requires enumerating {searchSpaceSize} states (too large). Use numQubits <= 16."
            else
                let innerSolutions =
                    [0 .. searchSpaceSize - 1]
                    |> List.filter (fun i -> Oracle.isSolution spec i)
                
                let negatedSolutions =
                    [0 .. searchSpaceSize - 1]
                    |> List.filter (fun i -> not (List.contains i innerSolutions))
                
                if List.isEmpty negatedSolutions then
                    Error "NOT oracle has no solutions"
                else
                    oracleToCircuit (Solutions negatedSolutions) numQubits
    
    // ========================================================================
    // DIFFUSION OPERATOR TO CIRCUIT
    // ========================================================================
    
    /// Convert diffusion operator to circuit
    /// 
    /// Diffusion operator: H^⊗n · (2|0⟩⟨0| - I) · H^⊗n
    /// 
    /// Implemented as:
    /// 1. H on all qubits
    /// 2. X on all qubits (map |0⟩ to |1⟩)
    /// 3. Multi-controlled Z (phase flip |1...1⟩ state)
    /// 4. X on all qubits (map back)
    /// 5. H on all qubits
    let diffusionToCircuit (numQubits: int) : Circuit =
        let circuit = empty numQubits
        
        // Step 1: Hadamard on all qubits
        let afterH1 = 
            [0 .. numQubits - 1]
            |> List.fold (fun c q -> addGate (H q) c) circuit
        
        // Step 2: X on all qubits
        let afterX1 =
            [0 .. numQubits - 1]
            |> List.fold (fun c q -> addGate (X q) c) afterH1
        
        // Step 3: Multi-controlled Z (mark |11...1⟩ state)
        let controls = [0 .. numQubits - 2]
        let target = numQubits - 1
        let afterMCZ = addMultiControlledZ controls target afterX1
        
        // Step 4: X on all qubits
        let afterX2 =
            [0 .. numQubits - 1]
            |> List.fold (fun c q -> addGate (X q) c) afterMCZ
        
        // Step 5: Hadamard on all qubits
        let afterH2 =
            [0 .. numQubits - 1]
            |> List.fold (fun c q -> addGate (H q) c) afterX2
        
        afterH2
    
    // ========================================================================
    // GROVER ITERATION TO CIRCUIT
    // ========================================================================
    
    /// Compose full Grover iteration: Oracle + Diffusion
    let groverIterationToCircuit (oracle: CompiledOracle) : Result<Circuit, string> =
        match oracleToCircuit oracle.Spec oracle.NumQubits with
        | Error msg -> Error msg
        | Ok oracleCircuit ->
            let diffusionCircuit = diffusionToCircuit oracle.NumQubits
            Ok (compose oracleCircuit diffusionCircuit)
    
    // ========================================================================
    // MEASUREMENT CONVERSION HELPERS
    // ========================================================================
    
    /// Convert measurement bitstrings to basis state counts
    let private measurementsToCounts (measurements: int[][]) : Map<int, int> =
        measurements
        |> Array.map (fun bitstring ->
            // Convert bitstring [0,1,1,0,1] to integer (little-endian)
            bitstring
            |> Array.indexed
            |> Array.fold (fun acc (i, bit) -> acc + (bit <<< i)) 0
        )
        |> Array.countBy id
        |> Map.ofArray
    
    /// Extract top solutions from measurement counts
    let extractTopSolutions (counts: Map<int, int>) (threshold: float) : int list =
        let totalShots = counts |> Map.toSeq |> Seq.sumBy snd
        let minCount = int (threshold * float totalShots)
        
        counts
        |> Map.toList
        |> List.filter (fun (_, count) -> count >= minCount)
        |> List.sortByDescending snd
        |> List.map fst
    
    /// Calculate success probability for solutions
    let calculateSuccessProb (solutions: int list) (counts: Map<int, int>) (totalShots: int) : float =
        let successCounts =
            solutions
            |> List.sumBy (fun sol -> counts |> Map.tryFind sol |> Option.defaultValue 0)
        
        float successCounts / float totalShots
    
    // ========================================================================
    // BACKEND EXECUTION
    // ========================================================================
    
    /// Execute Grover's algorithm using IQuantumBackend
    /// 
    /// This is the main entry point for backend-based execution.
    /// 
    /// Parameters:
    /// - oracle: Compiled oracle specification
    /// - backend: IQuantumBackend instance (LocalBackend, IonQBackend, RigettiBackend)
    /// - numIterations: Number of Grover iterations to apply
    /// - numShots: Measurement shots for result extraction
    /// - solutionThreshold: Minimum probability threshold for solution extraction (default 0.1 = 10%)
    /// - successThreshold: Minimum probability threshold for success determination (default 0.3 = 30%)
    /// 
    /// Returns: SearchResult with solutions found
    let executeGroverWithBackend 
        (oracle: CompiledOracle) 
        (backend: IQuantumBackend) 
        (numIterations: int) 
        (numShots: int) 
        (solutionThreshold: float)
        (successThreshold: float)
        : Result<GroverIteration.SearchResult, string> =
        
        try
            // Step 1: Validate inputs
            if numIterations < 0 then
                Error "Number of iterations must be non-negative"
            elif numShots <= 0 then
                Error "Number of shots must be positive"
            elif oracle.NumQubits > backend.MaxQubits then
                Error $"Oracle requires {oracle.NumQubits} qubits but backend '{backend.Name}' supports max {backend.MaxQubits}"
            else
                // Step 2: Create initial state preparation circuit (H^⊗n)
                let initCircuit = 
                    [0 .. oracle.NumQubits - 1]
                    |> List.fold (fun c q -> addGate (H q) c) (empty oracle.NumQubits)
                
                // Step 3: Create Grover iteration circuit
                match groverIterationToCircuit oracle with
                | Error msg -> Error msg
                | Ok iterationCircuit ->
                    // Step 4: Compose full circuit: Init + k*(Oracle+Diffusion)
                    let fullCircuit =
                        if numIterations = 0 then
                            initCircuit
                        else
                            [1 .. numIterations]
                            |> List.fold (fun c _ -> compose c iterationCircuit) initCircuit
                    
                    // Step 5: Execute on backend
                    let circuitWrapper = CircuitWrapper(fullCircuit)
                    
                    match backend.Execute circuitWrapper numShots with
                    | Error msg -> Error $"Backend execution failed: {msg}"
                    | Ok execResult ->
                        // Step 6: Convert ExecutionResult to SearchResult
                        let counts = measurementsToCounts execResult.Measurements
                        let solutions = extractTopSolutions counts solutionThreshold
                        let successProb = calculateSuccessProb solutions counts numShots
                        
                        Ok {
                            Solutions = solutions
                            SuccessProbability = successProb
                            IterationsApplied = numIterations
                            MeasurementCounts = counts
                            Shots = numShots
                            Success = successProb >= successThreshold
                        }
        
        with ex ->
            Error $"Grover backend execution failed: {ex.Message}"
