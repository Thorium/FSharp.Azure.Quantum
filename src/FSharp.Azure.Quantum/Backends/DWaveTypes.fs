namespace FSharp.Azure.Quantum.Backends

open System

/// D-Wave quantum annealing backend types and configuration.
///
/// This module provides types for:
/// - D-Wave solver selection (Advantage, 2000Q, etc.)
/// - Annealing schedule configuration
/// - Ising problem representation (D-Wave native format)
/// - Execution results from quantum annealing
///
/// Design rationale:
/// - D-Wave uses Ising model (spins ∈ {-1, +1}), not QUBO (binary ∈ {0, 1})
/// - Annealing parameters control quantum-classical transition
/// - Results include chain break information (minor-embedding quality)
module DWaveTypes =
    
    // ============================================================================
    // D-WAVE SOLVER TYPES
    // ============================================================================
    
    /// Available D-Wave quantum annealing solvers
    ///
    /// Solver capabilities:
    /// - Advantage_System6_1: 5640 qubits, Pegasus topology (latest)
    /// - Advantage2_Prototype: 1200+ qubits, Zephyr topology (next-gen)
    /// - Advantage_System4_1: 5000+ qubits, Pegasus (production)
    /// - Advantage_System1_1: 5000+ qubits, Pegasus (legacy)
    /// - DW_2000Q_6: 2048 qubits, Chimera topology (legacy)
    [<Struct>]
    type DWaveSolver =
        | Advantage_System6_1
        | Advantage2_Prototype
        | Advantage_System4_1
        | Advantage_System1_1
        | DW_2000Q_6
    
    /// Get solver name for D-Wave API
    let getSolverName (solver: DWaveSolver) : string =
        match solver with
        | Advantage_System6_1 -> "Advantage_system6.1"
        | Advantage2_Prototype -> "Advantage2_prototype1.1"
        | Advantage_System4_1 -> "Advantage_system4.1"
        | Advantage_System1_1 -> "Advantage_system1.1"
        | DW_2000Q_6 -> "DW_2000Q_6"
    
    /// Get maximum qubits for solver
    let getMaxQubits (solver: DWaveSolver) : int =
        match solver with
        | Advantage_System6_1 -> 5640
        | Advantage2_Prototype -> 1200
        | Advantage_System4_1 -> 5000
        | Advantage_System1_1 -> 5000
        | DW_2000Q_6 -> 2048
    
    // ============================================================================
    // ANNEALING SCHEDULE TYPES
    // ============================================================================
    
    /// Annealing schedule control
    ///
    /// Controls the quantum-classical transition during annealing:
    /// - s=0: Purely classical (ground state of initial Hamiltonian)
    /// - s=1: Purely quantum (ground state of problem Hamiltonian)
    ///
    /// Default: Linear ramp from s=0 to s=1 over annealTime microseconds
    type AnnealingSchedule = {
        /// Total annealing time in microseconds (default: 20 μs)
        AnnealTime: float
        
        /// Mid-anneal pause time in μs (for reverse annealing)
        PauseTime: float option
        
        /// Fast quench time at end in μs (for optimization)
        QuenchTime: float option
        
        /// Custom schedule points: (time_μs, s_parameter)
        /// where s ∈ [0, 1] controls quantum-classical mix
        /// Example: [(0.0, 1.0); (10.0, 0.0); (30.0, 1.0)] for reverse annealing
        Points: (float * float) list
    }
    
    /// Default annealing schedule (linear ramp, 20 μs)
    let defaultAnnealingSchedule = {
        AnnealTime = 20.0
        PauseTime = None
        QuenchTime = None
        Points = []  // Use solver default (linear ramp)
    }
    
    // ============================================================================
    // REVERSE ANNEALING CONFIGURATION
    // ============================================================================
    
    /// Reverse annealing configuration (warm-start from classical solution)
    ///
    /// Process:
    /// 1. Start at s=1 (quantum superposition)
    /// 2. Quickly quench to s=0 (fix to initialState)
    /// 3. Hold at s=0 for holdTime (classical state)
    /// 4. Slowly ramp back to s=1 (explore quantum neighborhood)
    /// 5. Anneal at s=1 for annealTime
    ///
    /// Use case: Refine classical heuristic solutions with quantum local search
    type ReverseAnnealingConfig = {
        /// Starting classical state: qubit index → spin {-1, +1}
        InitialState: Map<int, int>
        
        /// Time to hold at s=0 (classical) in μs (default: 100 μs)
        HoldTime: float
        
        /// Time to ramp from s=0 to s=1 in μs (default: 50 μs)
        RampUpTime: float
        
        /// Time at s=1 (quantum annealing) in μs (default: 20 μs)
        AnnealTime: float
    }
    
    /// Default reverse annealing configuration
    let defaultReverseAnnealingConfig initialState = {
        InitialState = initialState
        HoldTime = 100.0
        RampUpTime = 50.0
        AnnealTime = 20.0
    }
    
    // ============================================================================
    // GAUGE TRANSFORMATION (SPIN REVERSAL)
    // ============================================================================
    
    /// Spin-reversal transform for gauge averaging
    ///
    /// Gauge averaging reduces noise by averaging over equivalent problem formulations:
    /// - Flip subset of spins: s_i → -s_i
    /// - Adjust couplings: J_ij → -J_ij if exactly one of {i, j} is flipped
    /// - Average results over multiple gauge transforms
    ///
    /// Benefit: Suppresses systematic biases in hardware (chain breaks, ICE errors)
    [<Struct>]
    type SpinReversalTransform =
        | NoTransform
        | RandomTransform of count: int
    
    // ============================================================================
    // ISING MODEL REPRESENTATION
    // ============================================================================
    
    /// Ising model (D-Wave native format)
    ///
    /// Energy function:
    ///   E(s) = ∑ h_i * s_i + ∑ J_ij * s_i * s_j + offset
    ///
    /// where:
    /// - s_i ∈ {-1, +1} (spin variables)
    /// - h_i: Linear coefficients (magnetic fields)
    /// - J_ij: Quadratic coefficients (couplings)
    /// - offset: Constant energy term
    ///
    /// Conversion from QUBO:
    ///   QUBO: E(x) = ∑ Q_ij * x_i * x_j  where x ∈ {0, 1}
    ///   Transform: x = (1 + s) / 2  (maps {-1, +1} → {0, 1})
    type IsingProblem = {
        /// Linear coefficients: qubit index → h_i (magnetic field)
        LinearCoeffs: Map<int, float>
        
        /// Quadratic coefficients: (i, j) → J_ij (coupling)
        /// Convention: i < j (upper triangle only)
        QuadraticCoeffs: Map<(int * int), float>
        
        /// Constant energy offset (from QUBO transformation)
        Offset: float
    }
    
    /// Empty Ising problem
    let emptyIsingProblem = {
        LinearCoeffs = Map.empty
        QuadraticCoeffs = Map.empty
        Offset = 0.0
    }
    
    // ============================================================================
    // D-WAVE EXECUTION PARAMETERS
    // ============================================================================
    
    /// D-Wave execution parameters
    ///
    /// Configuration for quantum annealing execution:
    /// - Solver selection
    /// - Number of annealing cycles (shots)
    /// - Optional advanced features (reverse annealing, gauge averaging)
    /// - Auto-scaling and chain strength tuning
    type DWaveExecutionParams = {
        /// D-Wave solver to use
        Solver: DWaveSolver
        
        /// Number of annealing cycles (equivalent to "shots")
        NumReads: int
        
        /// Optional custom annealing schedule
        AnnealingSchedule: AnnealingSchedule option
        
        /// Optional reverse annealing configuration
        ReverseAnnealing: ReverseAnnealingConfig option
        
        /// Spin reversal gauge averaging (default: 10 random transforms)
        SpinReversalTransform: SpinReversalTransform
        
        /// Chain strength for minor-embedding (None = auto-calculate)
        /// Rule of thumb: 2x max |J_ij| coefficient
        ChainStrength: float option
        
        /// Auto-scale QUBO coefficients to fit solver range
        AutoScale: bool
    }
    
    /// Default D-Wave execution parameters
    let defaultParams = {
        Solver = Advantage_System6_1
        NumReads = 1000
        AnnealingSchedule = None
        ReverseAnnealing = None
        SpinReversalTransform = RandomTransform 10
        ChainStrength = None
        AutoScale = true
    }
    
    // ============================================================================
    // D-WAVE SOLUTION AND RESULT TYPES
    // ============================================================================
    
    /// Single solution from D-Wave annealing
    ///
    /// Represents one distinct configuration found during annealing:
    /// - Spins: The spin configuration (s_i ∈ {-1, +1})
    /// - Energy: Ising energy of this configuration
    /// - NumOccurrences: How many annealing cycles produced this solution
    /// - ChainBreakFraction: Quality metric (0.0 = perfect, >0.1 = concerning)
    type DWaveSolution = {
        /// Spin configuration: qubit index → {-1, +1}
        Spins: Map<int, int>
        
        /// Ising energy: E = ∑ h_i*s_i + ∑ J_ij*s_i*s_j + offset
        Energy: float
        
        /// Number of times this solution appeared (out of NumReads)
        NumOccurrences: int
        
        /// Fraction of broken chains (0.0 = good, >0.1 = bad)
        /// Chain breaks occur when qubits representing same logical variable disagree
        ChainBreakFraction: float
    }
    
    /// Result from D-Wave annealing (multiple solutions)
    ///
    /// Contains all distinct solutions found across all annealing cycles:
    /// - Solutions sorted by energy (best first)
    /// - Solver metadata (timing, embedding info, etc.)
    /// - Total reads and best energy for quick access
    type DWaveResult = {
        /// All distinct solutions found, sorted by energy (lowest first)
        Solutions: DWaveSolution list
        
        /// Solver information and timing
        /// Keys: "timing", "embedding_context", "warnings", etc.
        Info: Map<string, obj>
        
        /// Total number of annealing cycles performed
        TotalReads: int
        
        /// Energy of best solution (= Solutions.[0].Energy)
        BestEnergy: float
    }
