namespace FSharp.Azure.Quantum.Quantum

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Classical
open FSharp.Azure.Quantum.Core

/// Quantum Portfolio Solver using QAOA and Backend Abstraction
/// 
/// ALGORITHM-LEVEL API (for advanced users):
/// This module provides direct access to quantum portfolio optimization via QAOA.
/// For business-domain API, use the Portfolio module instead.
/// 
/// COMPARISON:
///   // Business Domain (Recommended for most users):
///   open FSharp.Azure.Quantum
///   let allocation = Portfolio.solve problem None  // Automatic LocalBackend
///   
///   // Algorithm Level (This module - for experts):
///   open FSharp.Azure.Quantum.Quantum
///   let backend = BackendAbstraction.createIonQBackend(...)
///   let result = QuantumPortfolioSolver.solve backend assets constraints config
/// 
/// RULE 1 COMPLIANCE:
/// ✅ Requires IQuantumBackend parameter (explicit quantum execution)
/// 
/// TECHNICAL DETAILS:
/// - Execution: Quantum hardware/simulator via backend
/// - Algorithm: QAOA (Quantum Approximate Optimization Algorithm)
/// - Speed: Seconds to minutes (includes job queue wait for cloud backends)
/// - Cost: ~$10-100 per run on real quantum hardware (IonQ, Rigetti)
/// - LocalBackend: Free simulation (limited to ~16 qubits)
///
/// QUANTUM PIPELINE:
/// 1. Portfolio Problem → QUBO Matrix (mean-variance optimization encoding)
/// 2. QUBO → QAOA Circuit (Hamiltonians + Layers)
/// 3. Execute on Quantum Backend (IonQ/Rigetti/Local)
/// 4. Decode Measurements → Asset Allocations
/// 5. Return Best Solution
///
/// Example:
///   let backend = BackendAbstraction.createLocalBackend()
///   let config = { NumShots = 1000; RiskAversion = 0.5; InitialParameters = (0.5, 0.5) }
///   match QuantumPortfolioSolver.solve backend assets constraints config with
///   | Ok result -> printfn "Expected return: %f" result.ExpectedReturn
///   | Error msg -> printfn "Error: %s" msg
module QuantumPortfolioSolver =

    // ================================================================================
    // PROBLEM DEFINITION
    // ================================================================================

    /// Portfolio optimization problem specification
    type PortfolioProblem = {
        /// List of available assets
        Assets: PortfolioTypes.Asset list
        
        /// Portfolio constraints
        Constraints: PortfolioSolver.Constraints
        
        /// Risk aversion parameter (higher = more risk-averse)
        RiskAversion: float
    }
    
    /// Quantum portfolio solution result
    type QuantumPortfolioSolution = {
        /// Asset allocations
        Allocations: PortfolioSolver.Allocation list
        
        /// Total portfolio value
        TotalValue: float
        
        /// Expected portfolio return
        ExpectedReturn: float
        
        /// Portfolio risk (standard deviation)
        Risk: float
        
        /// Sharpe ratio (return / risk)
        SharpeRatio: float
        
        /// Backend used for execution
        BackendName: string
        
        /// Number of measurement shots
        NumShots: int
        
        /// Execution time in milliseconds
        ElapsedMs: float
        
        /// QUBO objective value (energy)
        BestEnergy: float
        
        /// Selected assets (binary: 1 = included, 0 = excluded)
        SelectedAssets: Map<string, bool>
    }

    // ================================================================================
    // QUBO ENCODING FOR PORTFOLIO OPTIMIZATION
    // ================================================================================

    /// Convert sparse QUBO matrix (Map) to dense 2D array
    let private quboMapToArray (quboMatrix: GraphOptimization.QuboMatrix) : float[,] =
        let n = quboMatrix.NumVariables
        let dense = Array2D.zeroCreate n n
        
        for KeyValue((i, j), value) in quboMatrix.Q do
            dense.[i, j] <- value
        
        dense

    /// Encode portfolio optimization as QUBO
    /// 
    /// Variables: x_i = 1 if asset i is included in portfolio
    /// Objective: Maximize return - risk_aversion * risk^2
    ///   Return: Σ (return_i * x_i)
    ///   Risk: Σ (risk_i^2 * x_i) (simplified - assumes no correlation)
    /// 
    /// Constraints (as penalty terms):
    /// 1. Budget constraint: Σ (price_i * x_i) ≤ budget
    /// 2. Diversification: Encourage selecting multiple assets (optional)
    let toQubo (problem: PortfolioProblem) : Result<GraphOptimization.QuboMatrix, string> =
        try
            let numAssets = problem.Assets.Length
            
            if numAssets = 0 then
                Error "Portfolio problem has no assets"
            else
                let mutable quboTerms = []
                
                // Penalty weight for constraint violations
                let penaltyWeight = 
                    let maxReturn = problem.Assets |> List.map (fun a -> abs a.ExpectedReturn) |> List.max
                    maxReturn * 10.0
                
                // ========================================================================
                // OBJECTIVE: Maximize (Return - RiskAversion * Risk^2)
                // Convert to minimization: Minimize -(Return - RiskAversion * Risk^2)
                // ========================================================================
                
                for i in 0 .. numAssets - 1 do
                    let asset = problem.Assets.[i]
                    
                    // Linear term: -return_i (negative because we're minimizing)
                    let returnCoeff = -asset.ExpectedReturn
                    quboTerms <- ((i, i), returnCoeff) :: quboTerms
                    
                    // Quadratic term: +risk_aversion * risk_i^2 (penalty for risk)
                    let riskCoeff = problem.RiskAversion * asset.Risk * asset.Risk
                    quboTerms <- ((i, i), riskCoeff) :: quboTerms
                
                // ========================================================================
                // CONSTRAINT 1: Budget Constraint
                // Σ (price_i * x_i) ≤ budget
                // Penalty form: (Σ (price_i * x_i) - budget)^2 if sum > budget
                // 
                // For QUBO: Use penalty if sum exceeds budget
                // Simplified: Penalize pairs of expensive assets being selected together
                // ========================================================================
                
                let totalBudget = problem.Constraints.Budget
                
                // Encourage not exceeding budget by penalizing expensive combinations
                for i in 0 .. numAssets - 1 do
                    let asset_i = problem.Assets.[i]
                    
                    // Diagonal penalty for selecting expensive assets
                    if asset_i.Price > totalBudget * 0.5 then
                        let budgetPenalty = penaltyWeight * (asset_i.Price / totalBudget)
                        quboTerms <- ((i, i), budgetPenalty) :: quboTerms
                    
                    // Off-diagonal: Penalize pairs that would exceed budget
                    for j in i + 1 .. numAssets - 1 do
                        let asset_j = problem.Assets.[j]
                        
                        if asset_i.Price + asset_j.Price > totalBudget then
                            // Strong penalty for selecting both
                            let pairPenalty = 2.0 * penaltyWeight
                            quboTerms <- ((i, j), pairPenalty) :: quboTerms
                
                // ========================================================================
                // CONSTRAINT 2: Diversification (soft constraint)
                // Encourage selecting multiple assets (not just one)
                // Add small negative bias to encourage more selections
                // ========================================================================
                
                for i in 0 .. numAssets - 1 do
                    let diversificationBonus = -0.1 * penaltyWeight / float numAssets
                    quboTerms <- ((i, i), diversificationBonus) :: quboTerms
                
                // ========================================================================
                // Build QUBO Matrix
                // ========================================================================
                
                // Aggregate terms with same indices (add coefficients)
                let aggregatedTerms =
                    quboTerms
                    |> List.groupBy fst
                    |> List.map (fun (key, terms) ->
                        let totalCoeff = terms |> List.sumBy snd
                        key, totalCoeff)
                    |> Map.ofList
                
                Ok {
                    NumVariables = numAssets
                    Q = aggregatedTerms
                }
        
        with ex ->
            Error (sprintf "Failed to encode portfolio as QUBO: %s" ex.Message)

    // ================================================================================
    // SOLUTION DECODING
    // ================================================================================

    /// Decode QUBO solution bitstring to portfolio allocation
    let private decodeSolution 
        (problem: PortfolioProblem) 
        (bitstring: int array)
        : QuantumPortfolioSolution option =
        
        try
            // Extract selected assets (where bit = 1)
            let selectedAssets =
                bitstring
                |> Array.mapi (fun i bit -> 
                    let asset = problem.Assets.[i]
                    (asset.Symbol, bit = 1))
                |> Map.ofArray
            
            let selectedAssetList =
                problem.Assets
                |> List.mapi (fun i asset -> i, asset)
                |> List.filter (fun (i, _) -> bitstring.[i] = 1)
                |> List.map snd
            
            if selectedAssetList.IsEmpty then
                None
            else
                // Calculate total value and check budget
                let totalCost = selectedAssetList |> List.sumBy (fun a -> a.Price)
                
                if totalCost > problem.Constraints.Budget then
                    // Violates budget - return None
                    None
                else
                    // Calculate allocations (equal weight for selected assets)
                    let numSelected = selectedAssetList.Length
                    let valuePerAsset = problem.Constraints.Budget / float numSelected
                    
                    let allocations =
                        selectedAssetList
                        |> List.map (fun asset ->
                            let shares = valuePerAsset / asset.Price
                            let actualValue = shares * asset.Price
                            {
                                PortfolioSolver.Allocation.Asset = asset
                                PortfolioSolver.Allocation.Shares = shares
                                PortfolioSolver.Allocation.Value = actualValue
                                PortfolioSolver.Allocation.Percentage = actualValue / problem.Constraints.Budget
                            })
                    
                    let totalValue = allocations |> List.sumBy (fun a -> a.Value)
                    
                    // Calculate portfolio metrics
                    let expectedReturn =
                        allocations
                        |> List.sumBy (fun alloc -> alloc.Asset.ExpectedReturn * alloc.Percentage)
                    
                    let risk =
                        allocations
                        |> List.sumBy (fun alloc -> 
                            let weightedRisk = alloc.Percentage * alloc.Asset.Risk
                            weightedRisk * weightedRisk)
                        |> sqrt
                    
                    let sharpeRatio = 
                        if risk = 0.0 then 0.0
                        else expectedReturn / risk
                    
                    // Energy = -(return - risk_aversion * risk^2)
                    let energy = -(expectedReturn - problem.RiskAversion * risk * risk)
                    
                    Some {
                        Allocations = allocations
                        TotalValue = totalValue
                        ExpectedReturn = expectedReturn
                        Risk = risk
                        SharpeRatio = sharpeRatio
                        BackendName = ""  // Will be set by caller
                        NumShots = 0      // Will be set by caller
                        ElapsedMs = 0.0   // Will be set by caller
                        BestEnergy = energy
                        SelectedAssets = selectedAssets
                    }
        
        with _ ->
            None

    // ================================================================================
    // QUANTUM SOLVER
    // ================================================================================

    /// Configuration for quantum portfolio solving
    type QuantumPortfolioConfig = {
        /// Number of shots for execution
        NumShots: int
        
        /// Risk aversion parameter (0 = risk-neutral, 1 = very risk-averse)
        RiskAversion: float
        
        /// Initial QAOA parameters (gamma, beta)
        InitialParameters: float * float
    }
    
    /// Default configuration
    let defaultConfig = {
        NumShots = 1000
        RiskAversion = 0.5
        InitialParameters = (0.5, 0.5)
    }

    /// Solve portfolio optimization using quantum backend via QAOA
    /// 
    /// Full Pipeline:
    /// 1. Portfolio problem → QUBO matrix (mean-variance encoding)
    /// 2. QUBO → QaoaCircuit (Hamiltonians + layers)
    /// 3. Execute circuit on quantum backend
    /// 4. Decode measurements → portfolio allocations
    /// 5. Return best solution
    /// 
    /// Parameters:
    ///   backend - Quantum backend to execute on (LocalBackend, IonQ, Rigetti)
    ///   assets - List of assets to optimize
    ///   constraints - Portfolio constraints (budget, min/max holding)
    ///   config - Configuration for execution
    ///   
    /// Returns:
    ///   Result with QuantumPortfolioSolution or error message
    let solve 
        (backend: BackendAbstraction.IQuantumBackend)
        (assets: PortfolioTypes.Asset list)
        (constraints: PortfolioSolver.Constraints)
        (config: QuantumPortfolioConfig)
        : Result<QuantumPortfolioSolution, string> =
        
        let startTime = DateTime.UtcNow
        
        // Validate inputs
        let numAssets = assets.Length
        let requiredQubits = numAssets
        
        if numAssets = 0 then
            Error "Portfolio problem has no assets"
        elif requiredQubits > backend.MaxQubits then
            Error (sprintf "Problem requires %d qubits but backend '%s' supports max %d qubits" 
                requiredQubits backend.Name backend.MaxQubits)
        elif config.NumShots <= 0 then
            Error "Number of shots must be positive"
        else
            try
                // Build portfolio problem
                let problem : PortfolioProblem = {
                    Assets = assets
                    Constraints = constraints
                    RiskAversion = config.RiskAversion
                }
                
                // Step 1: Encode portfolio as QUBO
                match toQubo problem with
                | Error msg -> Error msg
                | Ok quboMatrix ->
                    
                    // Step 2: Generate QAOA circuit components from QUBO
                    let quboArray = quboMapToArray quboMatrix
                    let problemHam = QaoaCircuit.ProblemHamiltonian.fromQubo quboArray
                    let mixerHam = QaoaCircuit.MixerHamiltonian.create problemHam.NumQubits
                    
                    // Step 3: Build QAOA circuit with parameters
                    let (gamma, beta) = config.InitialParameters
                    let parameters = [| gamma, beta |]
                    let qaoaCircuit = QaoaCircuit.QaoaCircuit.build problemHam mixerHam parameters
                    
                    // Step 4: Execute on backend
                    let circuitWrapper = 
                        CircuitAbstraction.QaoaCircuitWrapper(qaoaCircuit) 
                        :> CircuitAbstraction.ICircuit
                    
                    match backend.Execute circuitWrapper config.NumShots with
                    | Error msg -> Error (sprintf "Backend execution failed: %s" msg)
                    | Ok execResult ->
                        
                        // Step 5: Decode measurements to portfolio solutions
                        let portfolioResults =
                            execResult.Measurements
                            |> Array.choose (decodeSolution problem)
                        
                        if portfolioResults.Length = 0 then
                            Error "No valid portfolio solutions found in quantum measurements"
                        else
                            // Select best solution (minimum energy = maximum utility)
                            let bestSolution = 
                                portfolioResults
                                |> Array.minBy (fun sol -> sol.BestEnergy)
                            
                            let elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds
                            
                            Ok {
                                bestSolution with
                                    BackendName = backend.Name
                                    NumShots = config.NumShots
                                    ElapsedMs = elapsedMs
                            }
            
            with ex ->
                Error (sprintf "Quantum portfolio solver failed: %s" ex.Message)

    /// Solve portfolio with default configuration
    let solveWithDefaults 
        (backend: BackendAbstraction.IQuantumBackend)
        (assets: PortfolioTypes.Asset list)
        (constraints: PortfolioSolver.Constraints)
        : Result<QuantumPortfolioSolution, string> =
        solve backend assets constraints defaultConfig
    
    /// Solve portfolio with custom number of shots and risk aversion
    let solveWithParams 
        (backend: BackendAbstraction.IQuantumBackend)
        (assets: PortfolioTypes.Asset list)
        (constraints: PortfolioSolver.Constraints)
        (numShots: int)
        (riskAversion: float)
        : Result<QuantumPortfolioSolution, string> =
        let config = { defaultConfig with NumShots = numShots; RiskAversion = riskAversion }
        solve backend assets constraints config
