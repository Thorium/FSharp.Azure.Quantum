namespace FSharp.Azure.Quantum.Core

/// QAOA Circuit Generator Module
/// 
/// Implements QAOA (Quantum Approximate Optimization Algorithm) circuit construction
/// from QUBO matrices for Azure Quantum submission.
/// 
/// ⚠️ CRITICAL: ALL QAOA circuit code in this SINGLE FILE for AI context optimization
module QaoaCircuit =
    
    // ============================================================================
    // 1. TYPES AND RECORDS (Primitives, no dependencies)
    // ============================================================================
    
    /// Pauli operators for quantum gates
    type PauliOperator =
        | PauliI  // Identity
        | PauliX  // Pauli-X (bit flip)
        | PauliY  // Pauli-Y
        | PauliZ  // Pauli-Z (phase flip)
    
    /// Hamiltonian term representing Pauli string with coefficient
    type HamiltonianTerm = {
        Coefficient: float
        QubitsIndices: int[]
        PauliOperators: PauliOperator[]
    }
    
    /// Problem Hamiltonian (cost function from QUBO)
    type ProblemHamiltonian = {
        NumQubits: int
        Terms: HamiltonianTerm[]
    }
    
    /// Mixer Hamiltonian (X rotations for exploration)
    type MixerHamiltonian = {
        NumQubits: int
        Terms: HamiltonianTerm[]
    }
    
    /// Quantum gate types
    type QuantumGate =
        | H of qubit: int                           // Hadamard gate
        | RX of qubit: int * angle: float           // X rotation
        | RY of qubit: int * angle: float           // Y rotation
        | RZ of qubit: int * angle: float           // Z rotation
        | RZZ of qubit1: int * qubit2: int * angle: float  // ZZ rotation (two-qubit)
        | CNOT of control: int * target: int        // CNOT gate
    
    /// QAOA circuit layer
    type QaoaLayer = {
        /// Cost layer gates (apply e^(-iγH_problem))
        CostGates: QuantumGate[]
        
        /// Mixer layer gates (apply e^(-iβH_mix))
        MixerGates: QuantumGate[]
        
        /// Gamma parameter (cost layer angle)
        Gamma: float
        
        /// Beta parameter (mixer layer angle)
        Beta: float
    }
    
    /// Complete QAOA circuit
    type QaoaCircuit = {
        /// Number of qubits
        NumQubits: int
        
        /// Initial state preparation gates (Hadamard on all qubits)
        InitialStateGates: QuantumGate[]
        
        /// QAOA layers (p layers for p-level QAOA)
        Layers: QaoaLayer[]
        
        /// Problem and mixer Hamiltonians (for reference)
        ProblemHamiltonian: ProblemHamiltonian
        MixerHamiltonian: MixerHamiltonian
    }
    
    // ============================================================================
    // 2. PROBLEM HAMILTONIAN CONSTRUCTION
    // ============================================================================
    
    module ProblemHamiltonian =
        
        /// Convert QUBO matrix to Problem Hamiltonian
        /// 
        /// QUBO formulation: minimize x^T Q x where x ∈ {0,1}^n
        /// 
        /// For binary variables x_i ∈ {0,1}, we map to qubits using:
        /// x_i = (1 - Z_i) / 2
        /// 
        /// Substituting this into QUBO:
        /// - Diagonal term Q_ii * x_i => Q_ii/2 * (1 - Z_i)
        /// - Off-diagonal Q_ij * x_i * x_j => Q_ij/4 * (1 - Z_i)(1 - Z_j)
        ///                                   = Q_ij/4 * (1 - Z_i - Z_j + Z_i*Z_j)
        /// 
        /// We keep only the Z terms (constant offset dropped):
        /// - Diagonal: -Q_ii/2 * Z_i
        /// - Off-diagonal: Q_ij/4 * Z_i*Z_j
        let fromQubo (quboMatrix: float[,]) : ProblemHamiltonian =
            let n = Array2D.length1 quboMatrix
            
            if n <> Array2D.length2 quboMatrix then
                failwith "QUBO matrix must be square"
            
            let terms = ResizeArray<HamiltonianTerm>()
            
            // Process diagonal terms (single-qubit Z operators)
            for i in 0 .. n - 1 do
                let qii = quboMatrix[i, i]
                if abs qii > 1e-10 then  // Skip near-zero terms
                    terms.Add {
                        Coefficient = -qii / 2.0
                        QubitsIndices = [| i |]
                        PauliOperators = [| PauliZ |]
                    }
            
            // Process off-diagonal terms (two-qubit ZZ interactions)
            // QUBO is symmetric, so we only need upper triangle
            for i in 0 .. n - 1 do
                for j in i + 1 .. n - 1 do
                    let qij = quboMatrix[i, j] + quboMatrix[j, i]  // Symmetrize
                    if abs qij > 1e-10 then  // Skip near-zero terms
                        terms.Add {
                            Coefficient = qij / 4.0
                            QubitsIndices = [| i; j |]
                            PauliOperators = [| PauliZ; PauliZ |]
                        }
            
            {
                NumQubits = n
                Terms = terms.ToArray()
            }
    
    // ============================================================================
    // 3. MIXER HAMILTONIAN CONSTRUCTION
    // ============================================================================
    
    module MixerHamiltonian =
        
        /// Create standard mixer Hamiltonian (X rotations on all qubits)
        /// 
        /// The mixer Hamiltonian is: H_mix = Σ_i X_i
        /// This allows exploration of the solution space in QAOA
        let create (numQubits: int) : MixerHamiltonian =
            let terms = Array.init numQubits (fun i ->
                {
                    Coefficient = 1.0
                    QubitsIndices = [| i |]
                    PauliOperators = [| PauliX |]
                })
            
            {
                NumQubits = numQubits
                Terms = terms
            }
    
    // ============================================================================
    // 4. QAOA CIRCUIT CONSTRUCTION
    // ============================================================================
    
    module QaoaCircuit =
        
        /// Build a single QAOA layer from Hamiltonians and parameters
        /// 
        /// A QAOA layer consists of:
        /// 1. Cost layer: Apply e^(-iγH_problem) via parameterized rotations
        /// 2. Mixer layer: Apply e^(-iβH_mix) via parameterized X rotations
        /// 
        /// For Hamiltonian term with coefficient c and Pauli operator P:
        /// - Single-qubit Z: RZ(2*c*γ) 
        /// - Two-qubit ZZ: RZZ(2*c*γ)
        /// - Single-qubit X: RX(2*c*β)
        let buildLayer (problemHam: ProblemHamiltonian) (mixerHam: MixerHamiltonian) (gamma: float) (beta: float) : QaoaLayer =
            let costGates = ResizeArray<QuantumGate>()
            
            // Build cost layer gates from problem Hamiltonian
            for term in problemHam.Terms do
                let angle = 2.0 * term.Coefficient * gamma
                
                match term.QubitsIndices.Length with
                | 1 -> 
                    // Single-qubit Z rotation
                    match term.PauliOperators[0] with
                    | PauliZ -> costGates.Add(RZ(term.QubitsIndices[0], angle))
                    | _ -> failwith "Unsupported Pauli operator in problem Hamiltonian"
                
                | 2 ->
                    // Two-qubit ZZ rotation
                    match term.PauliOperators[0], term.PauliOperators[1] with
                    | PauliZ, PauliZ -> 
                        costGates.Add(RZZ(term.QubitsIndices[0], term.QubitsIndices[1], angle))
                    | _ -> failwith "Unsupported Pauli operators in problem Hamiltonian"
                
                | _ -> failwith "Only single and two-qubit terms supported"
            
            let mixerGates = ResizeArray<QuantumGate>()
            
            // Build mixer layer gates from mixer Hamiltonian
            for term in mixerHam.Terms do
                let angle = 2.0 * term.Coefficient * beta
                
                match term.QubitsIndices.Length with
                | 1 ->
                    // Single-qubit X rotation
                    match term.PauliOperators[0] with
                    | PauliX -> mixerGates.Add(RX(term.QubitsIndices[0], angle))
                    | _ -> failwith "Unsupported Pauli operator in mixer Hamiltonian"
                
                | _ -> failwith "Mixer should only have single-qubit terms"
            
            {
                CostGates = costGates.ToArray()
                MixerGates = mixerGates.ToArray()
                Gamma = gamma
                Beta = beta
            }
        
        /// Build complete QAOA circuit with p layers
        /// 
        /// Parameters: array of (gamma, beta) tuples, one per layer
        /// Returns: Complete QAOA circuit with initial state + p layers
        let build (problemHam: ProblemHamiltonian) (mixerHam: MixerHamiltonian) (parameters: (float * float)[]) : QaoaCircuit =
            if problemHam.NumQubits <> mixerHam.NumQubits then
                failwith "Problem and mixer Hamiltonians must have same number of qubits"
            
            let numQubits = problemHam.NumQubits
            
            // Create initial state gates (Hadamard on all qubits)
            let initialStateGates = Array.init numQubits (fun i -> H(i))
            
            // Build layers from parameters
            let layers = 
                parameters
                |> Array.map (fun (gamma, beta) -> buildLayer problemHam mixerHam gamma beta)
            
            {
                NumQubits = numQubits
                InitialStateGates = initialStateGates
                Layers = layers
                ProblemHamiltonian = problemHam
                MixerHamiltonian = mixerHam
            }
        
        /// Convert quantum gate to OpenQASM 2.0 instruction
        let private gateToQasm (gate: QuantumGate) : string =
            match gate with
            | H qubit -> $"h q[{qubit}];"
            | RX (qubit, angle) -> $"rx({angle}) q[{qubit}];"
            | RY (qubit, angle) -> $"ry({angle}) q[{qubit}];"
            | RZ (qubit, angle) -> $"rz({angle}) q[{qubit}];"
            | RZZ (qubit1, qubit2, angle) -> $"rzz({angle}) q[{qubit1}],q[{qubit2}];"
            | CNOT (control, target) -> $"cx q[{control}],q[{target}];"
        
        /// Serialize QAOA circuit to OpenQASM 2.0 format
        /// 
        /// OpenQASM 2.0 format:
        /// - Header: OPENQASM 2.0; include "qelib1.inc";
        /// - Register declaration: qreg q[n];
        /// - Gates: h q[0]; rx(0.5) q[1]; rzz(0.25) q[0],q[1];
        let toOpenQasm (circuit: QaoaCircuit) : string =
            let sb = System.Text.StringBuilder()
            
            // Header
            sb.AppendLine("OPENQASM 2.0;") |> ignore
            sb.AppendLine("include \"qelib1.inc\";") |> ignore
            sb.AppendLine() |> ignore
            
            // Register declaration
            sb.AppendLine($"qreg q[{circuit.NumQubits}];") |> ignore
            sb.AppendLine() |> ignore
            
            // Initial state gates
            sb.AppendLine("// Initial state preparation") |> ignore
            for gate in circuit.InitialStateGates do
                sb.AppendLine(gateToQasm gate) |> ignore
            sb.AppendLine() |> ignore
            
            // QAOA layers
            for i, layer in Array.indexed circuit.Layers do
                sb.AppendLine($"// QAOA Layer {i + 1} (γ={layer.Gamma}, β={layer.Beta})") |> ignore
                
                // Cost layer
                sb.AppendLine("// Cost layer") |> ignore
                for gate in layer.CostGates do
                    sb.AppendLine(gateToQasm gate) |> ignore
                
                // Mixer layer
                sb.AppendLine("// Mixer layer") |> ignore
                for gate in layer.MixerGates do
                    sb.AppendLine(gateToQasm gate) |> ignore
                
                sb.AppendLine() |> ignore
            
            sb.ToString()
