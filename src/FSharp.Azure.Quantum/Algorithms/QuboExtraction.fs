namespace FSharp.Azure.Quantum.Algorithms

open FSharp.Azure.Quantum.Core

/// QUBO extraction from QAOA circuits for D-Wave backend.
///
/// This module provides utilities to extract QUBO matrices from QAOA circuits
/// for execution on quantum annealing hardware (D-Wave).
///
/// Design rationale:
/// - QAOA circuits encode QUBO problems in their Problem Hamiltonian
/// - D-Wave backends need QUBO format (not Hamiltonian format)
/// - Extraction inverts the QUBO → Hamiltonian transformation
///
/// Transformation (inverse of QAOA encoding, x_i = (1 - Z_i)/2):
///   Problem Hamiltonian: H = Σ c_i Z_i + Σ c_ij Z_i Z_j
///   QUBO: Q_ij = 4 * c_ij                      (off-diagonal)
///         Q_ii = -2 * (c_i + Σ_{j≠i} c_ij)     (diagonal; cross terms contribute
///                                               -Q_ij/4 to each linear c_i in the
///                                               forward map and must be removed here)
module QuboExtraction =
    
    open FSharp.Azure.Quantum.Core.QaoaCircuit
    open FSharp.Azure.Quantum.Core.CircuitAbstraction
    
    // ============================================================================
    // QUBO EXTRACTION FROM PROBLEM HAMILTONIAN
    // ============================================================================
    
    /// Extract QUBO matrix from Problem Hamiltonian
    ///
    /// Inverse transformation of QaoaCircuit.ProblemHamiltonian.fromQubo
    /// (which uses x_i = (1 - Z_i)/2, so Q_ij x_i x_j contributes -Q_ij/4 to
    /// BOTH linear coefficients c_i, c_j in addition to the Q_ij/4 ZZ term):
    ///   Off-diagonal: Q_ij = 4 * c_ij
    ///   Diagonal:     Q_ii = -2 * (c_i + Σ_{j≠i} c_ij)
    ///
    /// Parameters:
    /// - hamiltonian: Problem Hamiltonian from QAOA circuit
    ///
    /// Returns: QUBO matrix as Map<(int * int), float>
    ///          Convention: Upper triangle (i <= j)
    ///
    /// Example:
    ///   Hamiltonian: -1.0 * Z_0 + 0.5 * Z_0*Z_1
    ///   QUBO: Q_00 = -2*(-1.0 + 0.5) = 1.0, Q_01 = 2.0, Q_11 = -2*(0 + 0.5) = -1.0
    let fromProblemHamiltonian (hamiltonian: ProblemHamiltonian) : Map<(int * int), float> =

        // First pass: collect linear coefficients and ZZ coefficients per qubit
        let linear = System.Collections.Generic.Dictionary<int, float>()
        let zzSum = System.Collections.Generic.Dictionary<int, float>()
        let offDiagonal = System.Collections.Generic.Dictionary<int * int, float>()

        let addTo (d: System.Collections.Generic.Dictionary<'k, float>) key value =
            match d.TryGetValue key with
            | true, v -> d.[key] <- v + value
            | false, _ -> d.[key] <- value

        for term in hamiltonian.Terms do
            match term.QubitsIndices.Length with
            | 1 ->
                // Single-qubit Z term: c * Z_i
                addTo linear term.QubitsIndices.[0] term.Coefficient
            | 2 ->
                // Two-qubit ZZ term: c * Z_i * Z_j → Q_ij = 4 * c
                let i = term.QubitsIndices.[0]
                let j = term.QubitsIndices.[1]
                let key = if i <= j then (i, j) else (j, i)  // Normalize to upper triangle
                addTo offDiagonal key (4.0 * term.Coefficient)
                // Track Σ c_ij per incident qubit for the diagonal reconstruction
                addTo zzSum i term.Coefficient
                addTo zzSum j term.Coefficient
            | _ ->
                // Higher-order terms not supported in QUBO
                failwith $"Unsupported term order: {term.QubitsIndices.Length} qubits (QUBO supports only 1 and 2)"

        // Second pass: diagonal Q_ii = -2 * (c_i + Σ_{j≠i} c_ij)
        let qubits =
            Seq.append linear.Keys zzSum.Keys |> Seq.distinct

        let diagonal =
            qubits
            |> Seq.choose (fun i ->
                let ci = match linear.TryGetValue i with true, v -> v | _ -> 0.0
                let si = match zzSum.TryGetValue i with true, v -> v | _ -> 0.0
                let qii = -2.0 * (ci + si)
                if abs qii > 1e-12 then Some ((i, i), qii) else None)

        let offDiagonalEntries =
            offDiagonal
            |> Seq.filter (fun kv -> abs kv.Value > 1e-12)
            |> Seq.map (fun kv -> kv.Key, kv.Value)

        Seq.append diagonal offDiagonalEntries
        |> Map.ofSeq
    
    // ============================================================================
    // QUBO EXTRACTION FROM QAOA CIRCUIT
    // ============================================================================
    
    /// Extract QUBO from QAOA circuit
    ///
    /// This is the primary function for D-Wave backend integration.
    /// Extracts the QUBO problem encoded in a QAOA circuit.
    ///
    /// Parameters:
    /// - circuit: QAOA circuit (from QAOA solver)
    ///
    /// Returns: Result<Map<(int * int), float>, string>
    ///          Ok with QUBO matrix, or Error if not a QAOA circuit
    ///
    /// Example:
    ///   let circuit = QaoaCircuit.build problemHam mixerHam params
    ///   match extractQubo circuit with
    ///   | Ok qubo -> // Use qubo with D-Wave
    ///   | Error e -> failwith e
    let extractFromQaoaCircuit (circuit: QaoaCircuit) : Result<Map<(int * int), float>, string> =
        try
            let qubo = fromProblemHamiltonian circuit.ProblemHamiltonian
            Ok qubo
        with ex ->
            Error $"Failed to extract QUBO from circuit: {ex.Message}"
    
    /// Extract QUBO from ICircuit interface
    ///
    /// This function handles the general ICircuit interface used by backends.
    /// It attempts to downcast to QaoaCircuitWrapper to extract QUBO.
    ///
    /// Parameters:
    /// - circuit: ICircuit from backend execution
    ///
    /// Returns: Result<Map<(int * int), float>, string>
    ///          Ok with QUBO if circuit is QAOA, Error otherwise
    ///
    /// Note: This is the function called by DWaveBackend.Execute
    let extractFromICircuit (circuit: ICircuit) : Result<Map<(int * int), float>, string> =
        match circuit with
        | :? QaoaCircuitWrapper as wrapper ->
            extractFromQaoaCircuit wrapper.QaoaCircuit
        | _ ->
            Error "Circuit is not a QAOA circuit (D-Wave backend only supports QAOA)"
    
    // ============================================================================
    // VALIDATION AND UTILITIES
    // ============================================================================
    
    /// Validate that QUBO matrix is symmetric
    ///
    /// For optimization problems, QUBO should be symmetric: Q_ij = Q_ji
    /// This function checks that property.
    ///
    /// Parameters:
    /// - qubo: QUBO matrix as Map<(int * int), float>
    ///
    /// Returns: QuantumResult<unit> - Ok if symmetric, Error with details
    let validateSymmetric (qubo: Map<(int * int), float>) : QuantumResult<unit> =
        let asymmetricTerms = 
            qubo
            |> Map.toList
            |> List.filter (fun ((i, j), qij) ->
                if i = j then
                    false  // Diagonal terms are trivially symmetric
                elif i < j then
                    false  // Upper triangle - accept as implicitly symmetric
                else
                    // Lower triangle (i > j) - check if symmetric counterpart exists
                    let qji = Map.tryFind (j, i) qubo |> Option.defaultValue 0.0
                    abs (qij - qji) > 1e-10  // Check symmetry with tolerance
            )
        
        if List.isEmpty asymmetricTerms then
            Ok ()
        else
            let details = 
                asymmetricTerms 
                |> List.map (fun ((i, j), qij) -> $"Q[{i},{j}] ≠ Q[{j},{i}]")
                |> String.concat ", "
            Error (QuantumError.ValidationError ("QUBO matrix", $"not symmetric: {details}"))
    
    /// Get number of variables (qubits) in QUBO problem
    ///
    /// Parameters:
    /// - qubo: QUBO matrix
    ///
    /// Returns: Number of variables (highest qubit index + 1)
    let getNumVariables (qubo: Map<(int * int), float>) : int =
        if Map.isEmpty qubo then
            0
        else
            qubo
            |> Map.toSeq
            |> Seq.collect (fun ((i, j), _) -> [i; j])
            |> Seq.max
            |> (+) 1  // Convert 0-based index to count
    
    /// Convert QUBO Map to dense 2D array (for visualization/debugging)
    ///
    /// Parameters:
    /// - qubo: Sparse QUBO matrix
    ///
    /// Returns: Dense 2D float array
    ///
    /// Example:
    ///   QUBO: {(0,0)→2.0, (0,1)→-5.0}
    ///   Array: [[2.0, -5.0]; [0.0, 0.0]]  (if numQubits = 2)
    let toDenseArray (qubo: Map<(int * int), float>) : float[,] =
        let n = getNumVariables qubo
        let dense = Array2D.create n n 0.0
        
        for KeyValue((i, j), value) in qubo do
            dense.[i, j] <- value
            if i <> j then
                dense.[j, i] <- value  // Symmetrize
        
        dense
    
    /// Create QUBO from dense 2D array
    ///
    /// Parameters:
    /// - array: Dense 2D float array
    ///
    /// Returns: Sparse QUBO matrix (upper triangle only)
    let fromDenseArray (array: float[,]) : Map<(int * int), float> =
        let n1 = Array2D.length1 array
        let n2 = Array2D.length2 array
        
        if n1 <> n2 then
            failwith "QUBO array must be square"
        
        seq {
            for i in 0 .. n1 - 1 do
                for j in i .. n1 - 1 do  // Upper triangle only
                    let value = array.[i, j]
                    if abs value > 1e-10 then  // Skip near-zero terms
                        yield (i, j), value
        }
        |> Map.ofSeq
