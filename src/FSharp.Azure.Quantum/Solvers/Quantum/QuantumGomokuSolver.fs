namespace FSharp.Azure.Quantum.Quantum

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.GraphOptimization

/// Quantum Gomoku AI Solver using QAOA and Backend Abstraction
/// 
/// ALGORITHM-LEVEL API (for game AI):
/// This module provides quantum move selection for Gomoku using QAOA.
/// Given a board state and candidate positions, finds the optimal move
/// by encoding position evaluation as a QUBO optimization problem.
/// 
/// RULE 1 COMPLIANCE:
/// ✅ Requires IQuantumBackend parameter (explicit quantum execution)
/// 
/// TECHNICAL DETAILS:
/// - Execution: Quantum hardware/simulator via backend
/// - Algorithm: QAOA (Quantum Approximate Optimization Algorithm)
/// - Speed: Seconds (local), minutes (cloud backends)
/// - Cost: Free (LocalBackend) or ~$1-10 per move (IonQ, Rigetti)
/// - LocalBackend: Supports up to 16 candidate positions (16 qubits)
///
/// QUANTUM PIPELINE:
/// 1. Position Candidates + Evaluations → QUBO Matrix
/// 2. QUBO → QAOA Circuit (Problem + Mixer Hamiltonians)
/// 3. Execute on Quantum Backend (IonQ/Rigetti/Local)
/// 4. Decode Measurements → Best Position
/// 5. Return Move with Confidence
///
/// Gomoku Move Selection Problem:
///   Given board state and N candidate positions with evaluation scores,
///   select exactly one position that maximizes:
///   
///   Objective = offense_score + 2 * defense_score
///   
///   Subject to: Exactly one position selected (one-hot constraint)
///
/// Example:
///   let backend = BackendAbstraction.createLocalBackend()
///   let config = defaultConfig
///   match QuantumGomokuSolver.solve backend board candidates config with
///   | Ok (position, confidence) -> 
///       printfn "Selected move: (%d,%d) with %.2f%% confidence" 
///           position.Row position.Col (confidence * 100.0)
///   | Error msg -> printfn "Error: %s" msg
module QuantumGomokuSolver =

    // ================================================================================
    // TYPE DEFINITIONS (match Gomoku example structure)
    // ================================================================================

    /// Gomoku position (row, col)
    type Position = { Row: int; Col: int }
    
    /// Gomoku player
    type Player = Black | White
    
    /// Minimal board interface needed for solver
    type BoardState = {
        Size: int
        GetCell: int -> int -> Player option
        CurrentPlayer: Player
    }
    
    /// Position evaluation result
    type PositionEvaluation = {
        Position: Position
        OffensiveScore: float
        DefensiveScore: float
        TotalScore: float
    }
    
    /// Gomoku move selection result
    type GomokuSolution = {
        /// Selected move position
        Position: Position
        
        /// Confidence (0.0-1.0) based on measurement probability
        Confidence: float
        
        /// Position evaluation scores
        Evaluation: PositionEvaluation
        
        /// All candidate positions evaluated
        Candidates: PositionEvaluation list
        
        /// Backend used for execution
        BackendName: string
        
        /// Number of measurement shots
        NumShots: int
        
        /// Execution time in milliseconds
        ElapsedMs: float
        
        /// QUBO objective value (energy)
        BestEnergy: float
        
        /// Measurement distribution (position index → count)
        MeasurementDistribution: Map<int, int>
    }

    // ================================================================================
    // POSITION EVALUATION (Classical heuristics)
    // ================================================================================

    /// Count threats in all directions from a position
    let private countThreats (board: BoardState) (pos: Position) (player: Player) : int =
        let directions = [
            (1, 0)   // Horizontal
            (0, 1)   // Vertical
            (1, 1)   // Diagonal \
            (1, -1)  // Diagonal /
        ]
        
        let inBounds row col = 
            row >= 0 && row < board.Size && col >= 0 && col < board.Size
        
        let countDirection (dr, dc) =
            let mutable count = 0
            let mutable consecutive = 0
            
            // Count forward
            for i in 1..4 do
                let r = pos.Row + i * dr
                let c = pos.Col + i * dc
                if inBounds r c then
                    match board.GetCell r c with
                    | Some p when p = player -> consecutive <- consecutive + 1
                    | _ -> ()
            
            // Count backward
            for i in 1..4 do
                let r = pos.Row - i * dr
                let c = pos.Col - i * dc
                if inBounds r c then
                    match board.GetCell r c with
                    | Some p when p = player -> consecutive <- consecutive + 1
                    | _ -> ()
            
            if consecutive >= 2 then count <- count + 1
            count
        
        directions |> List.sumBy countDirection
    
    /// Evaluate offensive potential of a position
    let private evaluateOffensive (board: BoardState) (pos: Position) : float =
        let player = board.CurrentPlayer
        let threats = countThreats board pos player
        
        // Scoring: More threats = higher value
        // 4-in-a-row threat = immediate win
        // 3-in-a-row threat = very strong
        // 2-in-a-row threat = good
        match threats with
        | t when t >= 4 -> 10000.0  // Winning move
        | t when t = 3 -> 1000.0    // Strong threat
        | t when t = 2 -> 100.0     // Moderate threat
        | t when t = 1 -> 10.0      // Weak threat
        | _ -> 1.0                   // Neutral
    
    /// Evaluate defensive potential of a position
    let private evaluateDefensive (board: BoardState) (pos: Position) : float =
        let opponent = 
            match board.CurrentPlayer with
            | Black -> White
            | White -> Black
        
        let opponentThreats = countThreats board pos opponent
        
        // Defensive scoring: Block opponent threats
        // Blocking 4-in-a-row = critical
        // Blocking 3-in-a-row = important
        match opponentThreats with
        | t when t >= 4 -> 10000.0  // Must block winning move
        | t when t = 3 -> 1000.0    // Block strong threat
        | t when t = 2 -> 100.0     // Block moderate threat
        | t when t = 1 -> 10.0      // Block weak threat
        | _ -> 1.0                   // Neutral
    
    /// Evaluate a single position (offense + weighted defense)
    let evaluatePosition (board: BoardState) (pos: Position) : PositionEvaluation =
        let offensiveScore = evaluateOffensive board pos
        let defensiveScore = evaluateDefensive board pos
        
        // Weight defensive play 2x (match Classical AI strategy)
        let totalScore = offensiveScore + 2.0 * defensiveScore
        
        {
            Position = pos
            OffensiveScore = offensiveScore
            DefensiveScore = defensiveScore
            TotalScore = totalScore
        }

    // ================================================================================
    // QUBO ENCODING FOR GOMOKU MOVE SELECTION
    // ================================================================================

    /// Encode Gomoku move selection as QUBO
    /// 
    /// Move Selection QUBO formulation:
    /// 
    /// Variables: x_i ∈ {0, 1} where x_i = 1 means select candidate i
    /// 
    /// Objective (to MAXIMIZE):
    ///   Objective = Σ score_i * x_i
    /// 
    /// Constraint: Exactly one position selected
    ///   Σ x_i = 1
    /// 
    /// Penalty method to enforce constraint:
    ///   Penalty = λ * (Σ x_i - 1)²
    ///          = λ * (Σ x_i² - 2 Σ x_i + 1)
    ///          = λ * (Σ x_i - 2 Σ x_i + 1)    [since x_i² = x_i for binary]
    ///          = λ * (Σ x_i - 2 Σ x_i + 1)
    /// 
    /// QUBO form (to MINIMIZE for QAOA):
    ///   Minimize: -Σ score_i * x_i + λ * (Σ x_i - 1)²
    ///           = -Σ score_i * x_i + λ * (Σ x_i² + Σ_i Σ_j x_i*x_j - 2 Σ x_i + 1)
    /// 
    /// Expanded to QUBO matrix Q:
    ///   Q_ii = -score_i + λ - 2λ = -score_i - λ    (diagonal terms)
    ///   Q_ij = 2λ  (i ≠ j)                         (off-diagonal terms)
    let toQubo (evaluations: PositionEvaluation list) : Result<QuboMatrix, string> =
        try
            let numCandidates = evaluations.Length
            
            if numCandidates = 0 then
                Error "No candidate positions to evaluate"
            elif numCandidates = 1 then
                // Only one candidate - trivial QUBO (return it directly)
                Ok {
                    Q = Map.empty |> Map.add (0, 0) 0.0
                    NumVariables = 1
                }
            elif numCandidates > 16 then
                Error (sprintf "Too many candidates (%d) - LocalBackend supports max 16 qubits" numCandidates)
            else
                // Penalty parameter λ (must be larger than max score difference)
                let maxScore = evaluations |> List.map (fun e -> e.TotalScore) |> List.max
                let lambda = maxScore * 10.0  // Large penalty to enforce constraint
                
                // Build QUBO terms as Map<(int * int), float>
                let mutable quboTerms = Map.empty
                
                // Diagonal terms: Q_ii = -score_i - λ
                for i in 0 .. numCandidates - 1 do
                    let score = evaluations.[i].TotalScore
                    quboTerms <- quboTerms |> Map.add (i, i) (-score - lambda)
                
                // Off-diagonal terms: Q_ij = 2λ (enforce one-hot constraint)
                for i in 0 .. numCandidates - 1 do
                    for j in (i + 1) .. numCandidates - 1 do
                        quboTerms <- quboTerms |> Map.add (i, j) (2.0 * lambda)
                
                Ok {
                    Q = quboTerms
                    NumVariables = numCandidates
                }
        with ex ->
            Error (sprintf "Gomoku QUBO encoding failed: %s" ex.Message)

    // ================================================================================
    // SOLUTION DECODING
    // ================================================================================

    /// Decode binary solution to Gomoku position
    let private decodeSolution 
        (evaluations: PositionEvaluation list) 
        (bitstring: int[]) 
        (measurementCounts: Map<int, int>)
        (totalShots: int)
        : GomokuSolution =
        
        // Find selected position (bit = 1)
        let selectedIndex = 
            bitstring 
            |> Array.tryFindIndex (fun bit -> bit = 1)
            |> Option.defaultValue 0  // Fallback to first candidate
        
        let selectedEval = evaluations.[selectedIndex]
        
        // Calculate confidence from measurement distribution
        let selectedBitstring = 
            bitstring 
            |> Array.mapi (fun i bit -> if bit = 1 then 1 <<< i else 0)
            |> Array.sum
        
        let selectedCount = 
            measurementCounts 
            |> Map.tryFind selectedBitstring 
            |> Option.defaultValue 1
        
        let confidence = float selectedCount / float totalShots
        
        // Calculate QUBO energy (negative of score due to minimization)
        let energy = -selectedEval.TotalScore
        
        {
            Position = selectedEval.Position
            Confidence = confidence
            Evaluation = selectedEval
            Candidates = evaluations
            BackendName = ""
            NumShots = totalShots
            ElapsedMs = 0.0
            BestEnergy = energy
            MeasurementDistribution = measurementCounts
        }

    // ================================================================================
    // QAOA CONFIGURATION
    // ================================================================================

    /// QAOA configuration parameters for Gomoku
    type QaoaConfig = {
        /// Number of measurement shots
        NumShots: int
        
        /// Initial QAOA parameters (gamma, beta) for single layer
        /// Typical values: (0.5, 0.5) or (π/4, π/2)
        InitialParameters: float * float
        
        /// Enable parameter optimization (slower but more accurate)
        EnableOptimization: bool
    }
    
    /// Default QAOA configuration for Gomoku
    let defaultConfig : QaoaConfig = {
        NumShots = 500  // Moderate shots for game speed
        InitialParameters = (0.5, 0.5)
        EnableOptimization = false  // Disable for fast gameplay
    }
    
    /// High-accuracy configuration (for strong AI)
    let highAccuracyConfig : QaoaConfig = {
        NumShots = 2000
        InitialParameters = (0.5, 0.5)
        EnableOptimization = true  // Enable for better moves
    }
    
    /// Fast configuration (for quick gameplay)
    let fastConfig : QaoaConfig = {
        NumShots = 200
        InitialParameters = (0.5, 0.5)
        EnableOptimization = false
    }

    // ================================================================================
    // MAIN SOLVER
    // ================================================================================

    /// Solve Gomoku move selection using quantum QAOA
    /// 
    /// Parameters:
    ///   - backend: Quantum backend (LocalBackend, IonQ, Rigetti)
    ///   - board: Current board state
    ///   - candidates: List of candidate positions to evaluate
    ///   - config: QAOA configuration (shots, initial parameters)
    /// 
    /// Returns:
    ///   Result with selected position and confidence, or error message
    let solve 
        (backend: BackendAbstraction.IQuantumBackend)
        (board: BoardState)
        (candidates: Position list)
        (config: QaoaConfig)
        : Result<GomokuSolution, string> =
        
        let startTime = DateTime.UtcNow
        
        try
            // Validate inputs
            if List.isEmpty candidates then
                Error "No candidate positions provided"
            elif candidates.Length > backend.MaxQubits then
                Error (sprintf "Too many candidates (%d) - backend '%s' supports max %d qubits" 
                    candidates.Length backend.Name backend.MaxQubits)
            else
                // Step 1: Evaluate all candidate positions
                let evaluations = 
                    candidates 
                    |> List.map (evaluatePosition board)
                
                // Step 2: Encode as QUBO
                match toQubo evaluations with
                | Error msg -> Error msg
                | Ok quboMatrix ->
                    // Step 3: Convert QUBO to GraphOptimization problem
                    let vertices = 
                        candidates 
                        |> List.mapi (fun i _ -> sprintf "pos_%d" i)
                    
                    let edges = 
                        quboMatrix.Q
                        |> Map.toList
                        |> List.choose (fun ((i, j), weight) ->
                            if i <> j && weight <> 0.0 then
                                Some { Source = sprintf "pos_%d" i
                                       Target = sprintf "pos_%d" j
                                       Weight = weight }
                            else None
                        )
                    
                    let problem : GraphOptimizationProblem<string, unit> = {
                        Vertices = vertices |> List.map (fun v -> { Id = v; Data = () })
                        Edges = edges
                        Objective = Minimize
                    }
                    
                    // Step 4: Build QAOA circuit
                    let quboArray = Array2D.init quboMatrix.NumVariables quboMatrix.NumVariables (fun i j ->
                        quboMatrix.Q |> Map.tryFind (i, j) |> Option.defaultValue 0.0
                    )
                    
                    let problemHam = QaoaCircuit.ProblemHamiltonian.fromQubo quboArray
                    let mixerHam = QaoaCircuit.MixerHamiltonian.standard quboMatrix.NumVariables
                    
                    let (gamma, beta) = config.InitialParameters
                    let qaoaCircuit = QaoaCircuit.QaoaCircuit.build problemHam mixerHam [| (gamma, beta) |]
                    
                    // Step 5: Execute on backend
                    let circuitWrapper = 
                        CircuitAbstraction.QaoaCircuitWrapper(qaoaCircuit) 
                        :> CircuitAbstraction.ICircuit
                    
                    match backend.Execute circuitWrapper config.NumShots with
                    | Error msg -> 
                        Error (sprintf "Backend execution failed: %s" msg)
                    | Ok execResult ->
                        // Step 6: Decode measurements
                        let measurementCounts =
                            execResult.Measurements
                            |> Array.countBy id
                            |> Array.map (fun (bitstring, count) ->
                                // Convert bitstring to integer
                                let index = 
                                    bitstring 
                                    |> Array.mapi (fun i bit -> if bit = 1 then (1 <<< i) else 0)
                                    |> Array.sum
                                (index, count)
                            )
                            |> Map.ofArray
                        
                        // Find most frequently measured bitstring
                        let bestBitstring, bestCount = 
                            measurementCounts 
                            |> Map.toList
                            |> List.maxBy snd
                        
                        // Convert integer back to bitstring
                        let bestBits = 
                            Array.init quboMatrix.NumVariables (fun i ->
                                if (bestBitstring &&& (1 <<< i)) <> 0 then 1 else 0
                            )
                        
                        // Decode solution
                        let solution = decodeSolution evaluations bestBits measurementCounts config.NumShots
                        
                        let elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds
                        
                        Ok {
                            solution with
                                BackendName = backend.Name
                                ElapsedMs = elapsedMs
                        }
        with ex ->
            Error (sprintf "Gomoku quantum solver failed: %s" ex.Message)

    // ================================================================================
    // CONVENIENCE FUNCTIONS
    // ================================================================================

    /// Classical baseline solver (for comparison)
    let solveClassical (board: BoardState) (candidates: Position list) : GomokuSolution =
        let startTime = DateTime.UtcNow
        
        let evaluations = 
            candidates 
            |> List.map (evaluatePosition board)
        
        let best = 
            evaluations 
            |> List.maxBy (fun e -> e.TotalScore)
        
        let elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds
        
        {
            Position = best.Position
            Confidence = 1.0
            Evaluation = best
            Candidates = evaluations
            BackendName = "Classical"
            NumShots = 1
            ElapsedMs = elapsedMs
            BestEnergy = -best.TotalScore
            MeasurementDistribution = Map.empty
        }
    
    /// Solve with default configuration (LocalBackend)
    let solveDefault (board: BoardState) (candidates: Position list) : Result<GomokuSolution, string> =
        let backend = BackendAbstraction.createLocalBackend()
        solve backend board candidates defaultConfig
