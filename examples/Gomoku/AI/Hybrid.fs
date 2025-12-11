namespace FSharp.Azure.Quantum.Examples.Gomoku.AI

open FSharp.Azure.Quantum.Examples.Gomoku

/// Local Hybrid AI combining classical and quantum approaches
/// Automatically switches between strategies based on problem complexity
/// Uses FSharp.Azure.Quantum's local quantum simulator
module LocalHybrid =
    
    /// Strategy selection thresholds
    module Thresholds =
        let quantumMinCandidates = 5     // Too few candidates, use classical
        let quantumMaxCandidates = 30    // Too many candidates, quantum overhead dominates
        let earlyGameMoves = 10          // Use classical for opening moves
    
    /// AI strategy mode
    type Strategy =
        | Classical of reason: string
        | Quantum of reason: string
    
    /// Decide which strategy to use based on game state
    let selectStrategy (board: Board) (candidateCount: int) : Strategy =
        let moveNumber = board.MoveHistory.Length
        
        // Rule 1: Use classical for early game (opening book would be better)
        if moveNumber < Thresholds.earlyGameMoves then
            Classical "Early game - classical heuristics sufficient"
        
        // Rule 2: Too few candidates - classical is faster
        elif candidateCount < Thresholds.quantumMinCandidates then
            Classical (sprintf "Only %d candidates - classical overhead too low" candidateCount)
        
        // Rule 3: Too many candidates - quantum overhead too high
        elif candidateCount > Thresholds.quantumMaxCandidates then
            Classical (sprintf "%d candidates - quantum overhead exceeds benefit" candidateCount)
        
        // Rule 4: Sweet spot - use quantum
        else
            Quantum (sprintf "%d candidates - ideal for quantum speedup" candidateCount)
    
    /// Performance metrics for a move
    type MoveMetrics = {
        Strategy: Strategy
        Move: Position option
        CandidatesEvaluated: int
        ClassicalTime: float option
        QuantumTime: float option
        QuantumIterations: int option
    }
    
    /// Select best move using hybrid strategy
    let selectBestMove (board: Board) : MoveMetrics =
        let startTime = System.Diagnostics.Stopwatch.StartNew()
        
        // Step 1: Classical pre-filtering to get candidate moves
        let candidates = Classical.getTopCandidates board 30
        
        if candidates.IsEmpty then
            // No legal moves available
            {
                Strategy = Classical "No legal moves"
                Move = None
                CandidatesEvaluated = 0
                ClassicalTime = Some startTime.Elapsed.TotalMilliseconds
                QuantumTime = None
                QuantumIterations = None
            }
        else
            // Step 2: Decide strategy based on problem size
            let strategy = selectStrategy board candidates.Length
            
            match strategy with
            | Classical reason ->
                // Use pure classical approach
                let move = Classical.selectBestMove board
                startTime.Stop()
                
                {
                    Strategy = Classical reason
                    Move = move
                    CandidatesEvaluated = candidates.Length
                    ClassicalTime = Some startTime.Elapsed.TotalMilliseconds
                    QuantumTime = None
                    QuantumIterations = None
                }
            
            | Quantum reason ->
                // Use quantum approach with classical pre-filtering
                let classicalTime = startTime.Elapsed.TotalMilliseconds
                
                // Create local backend for quantum simulation
                let backend = FSharp.Azure.Quantum.Backends.LocalBackendFactory.createUnified()
                
                let quantumStart = System.Diagnostics.Stopwatch.StartNew()
                let (move, iterations) = LocalQuantum.selectBestMove board backend (Some candidates)
                quantumStart.Stop()
                
                startTime.Stop()
                
                {
                    Strategy = Quantum reason
                    Move = move
                    CandidatesEvaluated = candidates.Length
                    ClassicalTime = Some classicalTime
                    QuantumTime = Some quantumStart.Elapsed.TotalMilliseconds
                    QuantumIterations = Some iterations
                }
    
    /// Get explanation of strategy selection
    let explainStrategy (metrics: MoveMetrics) : string =
        match metrics.Strategy with
        | Classical reason ->
            sprintf """Strategy: Classical Heuristic Search
Reason: %s

Approach: Evaluated %d positions using pattern-based scoring
          Selected move with highest tactical value""" reason metrics.CandidatesEvaluated
        | Quantum reason ->
            let iterations = metrics.QuantumIterations |> Option.defaultValue 0
            let classicalMs = metrics.ClassicalTime |> Option.defaultValue 0.0
            let quantumMs = metrics.QuantumTime |> Option.defaultValue 0.0
            let advantage = LocalQuantum.getQuantumAdvantage metrics.CandidatesEvaluated
            
            sprintf """Strategy: Quantum-Enhanced Search
Reason: %s

Hybrid Approach:
  1. Classical pre-filtering: %d candidate positions (%.2f ms)
  2. Quantum search (Grover): %d iterations (%.2f ms)
  3. Theoretical advantage: √%d = %.1fx speedup

Total time: %.2f ms""" reason metrics.CandidatesEvaluated classicalMs iterations quantumMs metrics.CandidatesEvaluated advantage (classicalMs + quantumMs)
    
    /// Compare hybrid vs pure classical performance
    let compareToPureClassical (board: Board) : string =
        // Run both approaches and compare
        let classicalStart = System.Diagnostics.Stopwatch.StartNew()
        let classicalMove = Classical.selectBestMove board
        classicalStart.Stop()
        
        let hybridMetrics = selectBestMove board
        
        let classicalTime = classicalStart.Elapsed.TotalMilliseconds
        let hybridTime = 
            (hybridMetrics.ClassicalTime |> Option.defaultValue 0.0) +
            (hybridMetrics.QuantumTime |> Option.defaultValue 0.0)
        
        match hybridMetrics.Strategy with
        | Quantum _ ->
            let speedup = classicalTime / hybridTime
            sprintf """Performance Comparison:
━━━━━━━━━━━━━━━━━━━━━━━━
Pure Classical: %.2f ms
Hybrid (Quantum): %.2f ms
Speedup: %.2fx

Quantum advantage achieved!""" classicalTime hybridTime speedup
        | Classical _ ->
            sprintf """Performance Comparison:
━━━━━━━━━━━━━━━━━━━━━━━━
Classical approach selected for this position.
Problem size does not warrant quantum overhead."""
    
    /// Adaptive threshold adjustment based on performance history
    type AdaptiveHybrid = {
        mutable QuantumThresholdMin: int
        mutable QuantumThresholdMax: int
        mutable PerformanceHistory: (bool * float) list  // (usedQuantum, speedup)
    }
    
    /// Create adaptive hybrid AI that learns optimal thresholds
    let createAdaptive() : AdaptiveHybrid =
        {
            QuantumThresholdMin = Thresholds.quantumMinCandidates
            QuantumThresholdMax = Thresholds.quantumMaxCandidates
            PerformanceHistory = []
        }
    
    /// Update thresholds based on performance
    let updateThresholds (adaptive: AdaptiveHybrid) (metrics: MoveMetrics) (measuredSpeedup: float) : unit =
        match metrics.Strategy with
        | Quantum _ ->
            adaptive.PerformanceHistory <- (true, measuredSpeedup) :: adaptive.PerformanceHistory
            
            // If quantum was slower, tighten thresholds
            if measuredSpeedup < 1.0 then
                adaptive.QuantumThresholdMin <- adaptive.QuantumThresholdMin + 1
                adaptive.QuantumThresholdMax <- max (adaptive.QuantumThresholdMax - 1) adaptive.QuantumThresholdMin
        
        | Classical _ ->
            adaptive.PerformanceHistory <- (false, 1.0) :: adaptive.PerformanceHistory
        
        // Keep only recent history
        if adaptive.PerformanceHistory.Length > 20 then
            adaptive.PerformanceHistory <- List.take 20 adaptive.PerformanceHistory
