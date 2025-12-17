namespace FSharp.Azure.Quantum.Backends

open System

/// D-Wave quantum annealing backend implementation.
///
/// This module provides:
/// - MockDWaveBackend: Simulated annealing for testing (no API calls)
/// - DWaveBackend: Real D-Wave hardware integration (future - requires Ocean SDK)
///
/// Design rationale:
/// - Implements IQuantumBackend for seamless integration with QAOA solvers
/// - Extracts QUBO from QAOA circuits automatically
/// - Converts QUBO → Ising → executes → converts results back
/// - Mock backend allows testing without D-Wave credentials
module DWaveBackend =
    
    open FSharp.Azure.Quantum.Core.BackendAbstraction
    open FSharp.Azure.Quantum.Core.CircuitAbstraction
    open FSharp.Azure.Quantum.Core
    open FSharp.Azure.Quantum.Backends.DWaveTypes
    open FSharp.Azure.Quantum.Algorithms.QuboToIsing
    open FSharp.Azure.Quantum.Algorithms.QuboExtraction
    
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
    // MOCK D-WAVE SIMULATOR (FOR TESTING)
    // ============================================================================
    
    /// Mock D-Wave annealer using classical simulated annealing
    ///
    /// This provides a testable backend without requiring D-Wave API credentials.
    /// Uses a simple classical simulated annealing algorithm to find approximate solutions.
    ///
    /// Note: This is NOT quantum annealing! It's classical simulation for testing only.
    module MockSimulatedAnnealing =
        
        /// Simulated annealing to find low-energy Ising states
        ///
        /// Parameters:
        /// - problem: Ising problem to solve
        /// - numReads: Number of annealing runs
        /// - seed: Random seed for reproducibility
        ///
        /// Returns: List of solutions with energies and occurrence counts
        let solve (problem: IsingProblem) (numReads: int) (seed: int option) : DWaveSolution list =
            let rng = 
                match seed with
                | Some s -> Random(s)
                | None -> Random()
            
            let numQubits = 
                let linearQubits = problem.LinearCoeffs |> Map.toSeq |> Seq.map fst
                let quadraticQubits = 
                    problem.QuadraticCoeffs 
                    |> Map.toSeq 
                    |> Seq.collect (fun ((i, j), _) -> [i; j])
                
                if Seq.isEmpty linearQubits && Seq.isEmpty quadraticQubits then
                    0
                else
                    Seq.concat [linearQubits; quadraticQubits] |> Seq.max |> (+) 1
            
            /// Generate random spin configuration
            let randomSpins () : Map<int, int> =
                [0 .. numQubits - 1]
                |> List.map (fun i -> (i, if rng.NextDouble() < 0.5 then -1 else 1))
                |> Map.ofList
            
            /// Flip a single spin
            let flipSpin (spins: Map<int, int>) (qubit: int) : Map<int, int> =
                Map.add qubit (-spins.[qubit]) spins
            
            /// Simulated annealing run using recursive approach
            let anneal (initialTemp: float) (coolingRate: float) (maxSteps: int) =
                let initialSpins = randomSpins ()
                let initialEnergy = isingEnergy problem initialSpins
                
                let rec annealStep step temperature currentSpins currentEnergy =
                    if step > maxSteps then
                        (currentSpins, currentEnergy)
                    else
                        // Random qubit to flip
                        let qubit = rng.Next(numQubits)
                        let newSpins = flipSpin currentSpins qubit
                        let newEnergy = isingEnergy problem newSpins
                        
                        // Accept move with Metropolis criterion
                        let deltaE = newEnergy - currentEnergy
                        let acceptProb = if deltaE < 0.0 then 1.0 else exp(-deltaE / temperature)
                        
                        let (nextSpins, nextEnergy) =
                            if rng.NextDouble() < acceptProb 
                            then (newSpins, newEnergy)
                            else (currentSpins, currentEnergy)
                        
                        // Cool down and continue
                        annealStep (step + 1) (temperature * coolingRate) nextSpins nextEnergy
                
                annealStep 1 initialTemp initialSpins initialEnergy
            
            // Run multiple annealing cycles
            let results = 
                [1 .. numReads]
                |> List.map (fun _ -> anneal 10.0 0.95 100)
            
            // Group by spin configuration and count occurrences
            results
            |> List.groupBy fst
            |> List.map (fun (spins, group) ->
                // List.groupBy guarantees non-empty groups, but use pattern matching for clarity
                let energy = 
                    match group with
                    | (_, e) :: _ -> e  // Extract energy from first item
                    | [] -> 0.0  // Should never happen, but safe fallback
                {
                    Spins = spins
                    Energy = energy
                    NumOccurrences = List.length group
                    ChainBreakFraction = 0.0  // Mock: no chain breaks in simulation
                }
            )
            |> List.sortBy (fun sol -> sol.Energy)  // Sort by energy (best first)
    
    // ============================================================================
    // MOCK D-WAVE BACKEND (IDIOMATIC F#)
    // ============================================================================
    
    /// Mock D-Wave backend for testing
    ///
    /// Implements IQuantumBackend using classical simulated annealing.
    /// Allows testing D-Wave integration without requiring API credentials.
    ///
    /// Usage:
    ///   let backend = MockDWaveBackend(Advantage_System6_1, seed = Some 42)
    ///   let result = backend.Execute(qaoaCircuit, 1000)
    type MockDWaveBackend(solver: DWaveSolver, ?seed: int) =
        
        let solverName = getSolverName solver
        let maxQubits = getMaxQubits solver
        
        /// Backend name
        member _.Name = $"Mock D-Wave {solverName}"
        
        /// Maximum number of qubits supported by this solver
        member _.MaxQubits = maxQubits
        
        /// Solver type
        member _.Solver = solver
        
        /// Execute a QAOA circuit using D-Wave annealing backend
        ///
        /// Parameters:
        /// - circuit: QAOA circuit wrapped in QaoaCircuitWrapper
        /// - numShots: Number of annealing runs
        ///
        /// Returns: Result<ExecutionResult, QuantumError>
        ///
        /// Note: This is the PUBLIC API for D-Wave backend execution.
        /// D-Wave backends do NOT implement IQuantumBackend (annealing ≠ gate-based).
        member _.Execute (circuit: ICircuit) (numShots: int) : Result<ExecutionResult, QuantumError> =
            // Step 0: Validate numShots parameter
            if numShots <= 0 then
                Error (QuantumError.ValidationError ("numShots", $"must be > 0, got {numShots}"))
            else
                // Step 1: Extract QUBO from QAOA circuit
                match extractFromICircuit circuit with
                | Error e -> Error (QuantumError.ValidationError ("QUBO extraction", e))
                | Ok qubo ->
                    // Step 2: Convert QUBO to Ising
                    let ising = quboToIsing qubo
                    
                    // Step 3: Validate qubit count
                    let numQubits = getNumVariables qubo
                    if numQubits > maxQubits then
                        Error (QuantumError.ValidationError ("qubit count", $"Problem requires {numQubits} qubits, but {solverName} supports max {maxQubits}"))
                    else
                        // Step 4: Run simulated annealing
                        let solutions = MockSimulatedAnnealing.solve ising numShots seed
                        
                        // Step 5: Validate solutions list is not empty
                        match solutions with
                        | [] -> Error (QuantumError.OperationError ("simulated annealing", "No solutions found"))
                        | bestSolution :: _ ->
                            // Step 6: Convert Ising solutions back to binary measurements
                            // Expand each solution by its occurrence count
                            let measurements = 
                                solutions
                                |> List.collect (fun sol ->
                                    let binary = isingToQubo sol.Spins
                                    let bitstring = 
                                        [0 .. numQubits - 1]
                                        |> List.map (fun i -> Map.tryFind i binary |> Option.defaultValue 0)
                                        |> List.toArray
                                    // Repeat bitstring NumOccurrences times
                                    List.replicate sol.NumOccurrences bitstring
                                )
                                |> List.toArray
                            
                            // Step 7: Create ExecutionResult
                            let metadata = Map.ofList [
                                ("backend_type", box "mock_dwave")
                                ("solver", box solverName)
                                ("best_energy", box bestSolution.Energy)
                                ("num_solutions", box solutions.Length)
                            ]
                            
                            Ok {
                                Measurements = measurements
                                NumShots = numShots
                                BackendName = $"Mock D-Wave {solverName}"
                                Metadata = metadata
                            }
        
        // ====================================================================
        // IQuantumBackend interface implementation
        // ====================================================================
        
        interface BackendAbstraction.IQuantumBackend with
            member _.Name = "D-Wave (Mock)"
            
            /// Execute circuit and return quantum state (annealing samples)
            member this.ExecuteToState (circuit: ICircuit) : Result<QuantumState, QuantumError> =
                // Execute to get measurements
                match this.Execute circuit 1 with
                | Error e -> Error e
                | Ok execResult ->
                    // Extract Ising problem and solutions
                    match extractFromICircuit circuit with
                    | Error e -> Error (QuantumError.ValidationError ("QUBO extraction", e))
                    | Ok qubo ->
                        let ising = quboToIsing qubo
                        let solutions = MockSimulatedAnnealing.solve ising 1 seed
                        
                        // Return as IsingSamples state
                        Ok (QuantumState.IsingSamples (box ising, box solutions))
            
            /// Get backend's native state type (Annealing)
            member _.NativeStateType = QuantumStateType.Annealing
            
            /// Initialize quantum state (annealing backends start from classical input)
            member _.InitializeState (numQubits: int) : Result<QuantumState, QuantumError> =
                // Create empty Ising problem
                let emptyIsing : IsingProblem = { 
                    LinearCoeffs = Map.empty
                    QuadraticCoeffs = Map.empty
                    Offset = 0.0 
                }
                Ok (QuantumState.IsingSamples (box emptyIsing, box []))
            
            /// Apply operation to state (not supported for annealing)
            member _.ApplyOperation (operation: BackendAbstraction.QuantumOperation) (state: QuantumState) : Result<QuantumState, QuantumError> =
                match operation with
                | BackendAbstraction.QuantumOperation.Extension ext ->
                    Error (QuantumError.OperationError ("ApplyOperation", $"Extension operation '{ext.Id}' is not supported by annealing backends"))
                | _ ->
                    Error (QuantumError.OperationError ("ApplyOperation", "D-Wave annealing backend only supports full circuit execution"))
            
            /// Check if operation is supported (only QAOA circuits)
            member _.SupportsOperation (operation: BackendAbstraction.QuantumOperation) : bool =
                false  // Annealing backends don't support incremental operations
    
    // ============================================================================
    // HELPER FUNCTIONS FOR BACKEND CREATION
    // ============================================================================
    
    /// Create mock D-Wave backend for testing
    ///
    /// Parameters:
    /// - solver: D-Wave solver type (determines qubit count)
    /// - seed: Optional random seed for reproducible results
    ///
    /// Returns: MockDWaveBackend instance
    ///
    /// Example:
    ///   let backend = createMockDWaveBackend Advantage_System6_1 (Some 42)
    ///   let result = backend.ExecuteCore(circuit, 1000)
    let createMockDWaveBackend (solver: DWaveSolver) (seed: int option) : MockDWaveBackend =
        MockDWaveBackend(solver, ?seed = seed)
    
    /// Create mock D-Wave backend with default parameters
    ///
    /// Uses Advantage_System6_1 (5640 qubits) and random seed.
    ///
    /// Example:
    ///   let backend = createDefaultMockBackend()
    let createDefaultMockBackend () : MockDWaveBackend =
        createMockDWaveBackend Advantage_System6_1 None
