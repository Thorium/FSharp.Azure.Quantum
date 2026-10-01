namespace FSharp.Azure.Quantum.Quantum

open System
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Backends
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
///   let! allocation = Portfolio.solveAsync problem None CancellationToken.None  // Automatic LocalBackend
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
///   let backend = LocalBackend() :> IQuantumBackend
///   let config = { NumShots = 1000; RiskAversion = 0.5; InitialParameters = (0.5, 0.5) }
///   task {
///     match! QuantumPortfolioSolver.solveAsync backend assets constraints config CancellationToken.None with
///     | Ok result -> printfn "Expected return: %f" result.ExpectedReturn
///     | Error msg -> printfn "Error: %s" msg
///   }
module QuantumPortfolioSolver =

    // ================================================================================
    // PROBLEM DEFINITION
    // ================================================================================

    /// Portfolio optimization problem specification
    type PortfolioProblem =
        {
            /// List of available assets
            Assets: PortfolioTypes.Asset list

            /// Portfolio constraints
            Constraints: PortfolioSolver.Constraints

            /// Risk aversion parameter (higher = more risk-averse)
            RiskAversion: float

            /// Covariance matrix of the asset returns, rows and columns in Assets order.
            /// None treats the assets as independent: Σ = diag(Riskᵢ²).
            Covariance: float[,] option
        }

    /// Quantum portfolio solution result
    type QuantumPortfolioSolution =
        {
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

    /// Fraction of the budget one selected asset receives in the discretised problem:
    /// 1 / n for n assets, raised to MinHolding / Budget and lowered to MaxHolding / Budget
    /// when the holding limits require it. Budget that is not bought stays uninvested.
    let lotWeight (problem: PortfolioProblem) : float =
        let numAssets = problem.Assets.Length
        let budget = problem.Constraints.Budget

        if numAssets = 0 || budget <= 0.0 then
            0.0
        else
            max
                (problem.Constraints.MinHolding / budget)
                (min (1.0 / float numAssets) (problem.Constraints.MaxHolding / budget))

    /// Most assets one selection can hold: floor(1 / lotWeight), at most n.
    let maxHoldings (problem: PortfolioProblem) : int =
        let s = lotWeight problem

        if s <= 0.0 then
            0
        else
            min problem.Assets.Length (int (floor (1.0 / s + 1e-9)))

    /// True when one lot (lotWeight × Budget) buys at least one share of the asset.
    let isAffordable (problem: PortfolioProblem) (asset: PortfolioTypes.Asset) : bool =
        asset.Price <= lotWeight problem * problem.Constraints.Budget * (1.0 + 1e-12)

    /// True when a selection satisfies the discretised problem's constraints: at most
    /// maxHoldings assets, each affordable with one lot.
    let isFeasibleSelection (problem: PortfolioProblem) (selection: int array) : bool =
        let chosen =
            problem.Assets |> List.indexed |> List.filter (fun (i, _) -> selection.[i] = 1)

        chosen.Length <= maxHoldings problem
        && chosen |> List.forall (fun (_, asset) -> isAffordable problem asset)

    /// Covariance used by the encoding: the given matrix, or diag(Riskᵢ²) when there is none.
    let private effectiveCovariance (problem: PortfolioProblem) : float[,] =
        match problem.Covariance with
        | Some sigma -> sigma
        | None ->
            let assets = problem.Assets |> List.toArray

            Array2D.init assets.Length assets.Length (fun i j ->
                if i = j then assets.[i].Risk * assets.[i].Risk else 0.0)

    /// Mean-variance objective -(μᵀw - λ wᵀΣw) of a selection, with w = lotWeight × x.
    /// toQubo's matrix gives every selection of affordable assets this value.
    let meanVarianceEnergy (problem: PortfolioProblem) (selection: int array) : float =
        let s = lotWeight problem
        let weights = Array.init problem.Assets.Length (fun i -> s * float selection.[i])

        let expectedReturn =
            problem.Assets
            |> List.mapi (fun i asset -> weights.[i] * asset.ExpectedReturn)
            |> List.sum

        let variance =
            PortfolioTypes.portfolioVariance weights (effectiveCovariance problem)

        -(expectedReturn - problem.RiskAversion * variance)

    /// Encode portfolio optimization as QUBO
    ///
    /// Discretised problem: x_i = 1 buys asset i for one lot of weight s = lotWeight problem
    /// (a fraction of the budget); budget not bought stays uninvested. Portfolio weights are
    /// w = s x, and the objective is the mean-variance utility of the whole budget:
    ///
    ///   minimise  -(μᵀw - λ wᵀΣw) = -s μᵀx + λ s² xᵀΣx
    ///
    /// with λ = RiskAversion and Σ = Covariance (diag(Riskᵢ²) when None, i.e. independent
    /// assets). Using x_i² = x_i, the QUBO is ProblemTransformer.encodePortfolioCorrelation
    /// applied to returns sμ, covariance Σ and risk weight λs²:
    ///   Q[i,i] = -s μ_i + λ s² Σ[i,i]
    ///   Q[i,j] = 2 λ s² Σ[i,j]  (i < j, stored in the upper triangle)
    ///
    /// Each lot lies within [MinHolding, MaxHolding]. An asset whose price exceeds one lot gets
    /// a diagonal penalty larger than anything its selection could gain, so the QUBO minimum
    /// never holds it. When lots are larger than 1/n, at most maxHoldings assets fit the
    /// budget; the QUBO does not encode that limit, so the solver keeps the best feasible
    /// sample (isFeasibleSelection). With 1/n lots and affordable assets the minimum of xᵀQx is
    /// the discretised mean-variance optimum (see meanVarianceEnergy).
    let toQubo (problem: PortfolioProblem) : Result<GraphOptimization.QuboMatrix, QuantumError> =
        try
            let numAssets = problem.Assets.Length
            let c = problem.Constraints

            if numAssets = 0 then
                Error(QuantumError.ValidationError("numAssets", "Portfolio problem has no assets"))
            elif c.Budget <= 0.0 then
                Error(QuantumError.ValidationError("Budget", $"Budget must be positive: {c.Budget}"))
            elif c.MaxHolding <= 0.0 then
                Error(QuantumError.ValidationError("MaxHolding", $"MaxHolding must be positive: {c.MaxHolding}"))
            elif c.MinHolding > c.MaxHolding then
                Error(
                    QuantumError.ValidationError(
                        "MinHolding",
                        $"MinHolding ({c.MinHolding}) cannot exceed MaxHolding ({c.MaxHolding})"
                    )
                )
            elif c.MinHolding > c.Budget then
                Error(
                    QuantumError.ValidationError(
                        "MinHolding",
                        $"MinHolding ({c.MinHolding}) cannot exceed Budget ({c.Budget})"
                    )
                )
            else
                let covarianceCheck =
                    match problem.Covariance with
                    | Some sigma -> PortfolioTypes.validateCovariance numAssets sigma
                    | None -> Ok()

                match covarianceCheck with
                | Error err -> Error err
                | Ok() ->
                    let s = lotWeight problem

                    let scaledReturns =
                        problem.Assets |> List.map (fun a -> s * a.ExpectedReturn) |> List.toArray

                    let dense =
                        ProblemTransformer.encodePortfolioCorrelation
                            scaledReturns
                            (effectiveCovariance problem)
                            (problem.RiskAversion * s * s)

                    let q = dense.Coefficients
                    let assets = problem.Assets |> List.toArray

                    // Including an unaffordable asset costs more than any combination of its
                    // own and pair terms can gain.
                    let penalty i =
                        if isAffordable problem assets.[i] then
                            0.0
                        else
                            let pairs =
                                Seq.init numAssets id
                                |> Seq.filter (fun j -> j <> i)
                                |> Seq.sumBy (fun j -> abs q.[i, j] + abs q.[j, i])

                            2.0 * (abs q.[i, i] + pairs) + 1e-12

                    // Symmetric xᵀQx → upper triangle: the pair (i, j) carries Q[i,j] + Q[j,i].
                    let terms =
                        [
                            for i in 0 .. numAssets - 1 do
                                let diagonal = q.[i, i] + penalty i

                                if diagonal <> 0.0 then
                                    yield ((i, i), diagonal)

                                for j in i + 1 .. numAssets - 1 do
                                    let pair = q.[i, j] + q.[j, i]

                                    if pair <> 0.0 then
                                        yield ((i, j), pair)
                        ]

                    Ok
                        {
                            NumVariables = numAssets
                            Q = Map.ofList terms
                        }

        with ex ->
            Error(QuantumError.OperationError("QuboEncoding", $"Failed to encode portfolio as QUBO: %s{ex.Message}"))

    // ================================================================================
    // TRANSACTION COST QUBO ENCODING
    // ================================================================================

    /// Transaction cost parameters for portfolio rebalancing
    ///
    /// When rebalancing from a current portfolio to a new portfolio,
    /// transaction costs are incurred for buying or selling assets.
    [<Struct>]
    type TransactionCosts =
        {
            /// Cost rate for buying assets (e.g., 0.001 = 0.1% of transaction value)
            BuyCostRate: float

            /// Cost rate for selling assets (e.g., 0.001 = 0.1% of transaction value)
            SellCostRate: float

            /// Fixed cost per transaction (e.g., $5 per trade)
            FixedCostPerTrade: float
        }

    /// Default transaction costs (typical brokerage fees)
    let defaultTransactionCosts =
        {
            BuyCostRate = 0.001 // 0.1% commission
            SellCostRate = 0.001 // 0.1% commission
            FixedCostPerTrade = 0.0 // No fixed cost
        }

    /// Current portfolio holdings for rebalancing
    type CurrentHoldings =
        {
            /// Current holdings per asset (symbol -> number of shares)
            Holdings: Map<string, float>
        }

    /// Portfolio problem with transaction costs for rebalancing
    type PortfolioProblemWithCosts =
        {
            /// Base portfolio problem
            BaseProblem: PortfolioProblem

            /// Current holdings (empty for new portfolio)
            CurrentHoldings: CurrentHoldings

            /// Transaction cost parameters
            TransactionCosts: TransactionCosts
        }

    /// Encode portfolio optimization with transaction costs as QUBO
    ///
    /// Extends the base QUBO formulation to include:
    /// - Transaction cost penalties for changing positions
    /// - Holding cost considerations for existing positions
    ///
    /// QUBO Formulation:
    ///   Minimize: -Return + RiskAversion * Risk² + TransactionCostPenalty
    ///
    /// Where TransactionCostPenalty:
    ///   - For new positions (buying): rate * price_i * x_i
    ///   - For closing positions (selling): rate * price_i * (1 - x_i)
    ///   - Fixed costs: fixed_cost * |change|
    ///
    /// Variables: x_i = 1 if asset i is included in new portfolio
    ///
    /// The base QUBO is in fractions of the budget (see toQubo), so every dollar cost is
    /// divided by Budget; a buy costs BuyCostRate on one lot (lotWeight × Budget).
    ///
    /// Note: This is a simplified linear approximation of transaction costs.
    /// For exact quadratic encoding of |x_new - x_old|, auxiliary variables
    /// would be needed, which significantly increases problem size.
    let toQuboWithTransactionCosts
        (problemWithCosts: PortfolioProblemWithCosts)
        : Result<GraphOptimization.QuboMatrix, QuantumError> =

        try
            let problem = problemWithCosts.BaseProblem
            let costs = problemWithCosts.TransactionCosts
            let holdings = problemWithCosts.CurrentHoldings.Holdings
            let numAssets = problem.Assets.Length

            if numAssets = 0 then
                Error(QuantumError.ValidationError("numAssets", "Portfolio problem has no assets"))
            elif costs.BuyCostRate < 0.0 || costs.SellCostRate < 0.0 then
                Error(QuantumError.ValidationError("TransactionCosts", "Cost rates must be non-negative"))
            elif costs.FixedCostPerTrade < 0.0 then
                Error(QuantumError.ValidationError("TransactionCosts", "Fixed cost must be non-negative"))
            else
                // First get the base QUBO terms
                match toQubo problem with
                | Error err -> Error err
                | Ok baseQubo ->

                    // ================================================================
                    // TRANSACTION COST TERMS
                    // ================================================================
                    //
                    // For each asset i:
                    // - If currently held (h_i > 0) and not selected (x_i = 0): SELL
                    //   Cost = sell_rate * price_i * h_i
                    //   In QUBO: penalty when x_i = 0, so add positive constant - penalty * x_i
                    //
                    // - If not held (h_i = 0) and selected (x_i = 1): BUY
                    //   Cost = buy_rate * price_i * value_allocated
                    //   In QUBO: penalty when x_i = 1, so add penalty * x_i
                    //
                    // Buy penalty uses the lot value; all costs in budget fractions.
                    // ================================================================

                    let budget = problem.Constraints.Budget
                    let lotValue = lotWeight problem * budget

                    let transactionCostTerms =
                        List.init (max 0 numAssets) (fun i ->
                            let asset = problem.Assets.[i]
                            let currentHolding = holdings |> Map.tryFind asset.Symbol |> Option.defaultValue 0.0

                            let isCurrentlyHeld = currentHolding > 0.0

                            if isCurrentlyHeld then
                                // Currently held: incentivize keeping (penalize selling)
                                // QUBO term: -sellCost * x_i (diagonal term)
                                // When x_i = 1: contribution = -sellCost (bonus for keeping)
                                // When x_i = 0: contribution = 0 (no bonus = effective penalty)
                                // This incentivizes x_i = 1 (keep position) over x_i = 0 (sell)
                                let sellCost = costs.SellCostRate * asset.Price * currentHolding
                                let fixedCost = costs.FixedCostPerTrade
                                ((i, i), -(sellCost + fixedCost) / budget)
                            else
                                // Not held: cost to buy if selected
                                // Add penalty when x_i = 1
                                // QUBO term: +buyCost * x_i
                                // When x_i = 1: penalty = buyCost (buying)
                                // When x_i = 0: no penalty (don't buy)
                                let buyCost = costs.BuyCostRate * lotValue
                                let fixedCost = costs.FixedCostPerTrade
                                ((i, i), (buyCost + fixedCost) / budget))

                    // ================================================================
                    // TURNOVER PENALTY (optional quadratic term)
                    // ================================================================
                    //
                    // NOTE: This is a simplified heuristic, not exact turnover modeling.
                    //
                    // For pairs where one asset is currently held and one is not,
                    // we add a small penalty when BOTH are selected in the new portfolio.
                    // This encourages some consistency with current holdings.
                    //
                    // Limitation: True turnover penalty would require auxiliary variables
                    // to model |x_new - x_old|, which significantly increases problem size.
                    // This heuristic provides a reasonable approximation for small portfolios.
                    // ================================================================

                    let turnoverPenaltyTerms =
                        if costs.FixedCostPerTrade > 0.0 then
                            [ 0 .. numAssets - 2 ]
                            |> List.collect (fun i ->
                                let asset_i = problem.Assets.[i]

                                let held_i =
                                    holdings
                                    |> Map.tryFind asset_i.Symbol
                                    |> Option.map (fun h -> h > 0.0)
                                    |> Option.defaultValue false

                                [ i + 1 .. numAssets - 1 ]
                                |> List.choose (fun j ->
                                    let asset_j = problem.Assets.[j]

                                    let held_j =
                                        holdings
                                        |> Map.tryFind asset_j.Symbol
                                        |> Option.map (fun h -> h > 0.0)
                                        |> Option.defaultValue false

                                    // Penalize selecting both when holdings differ
                                    // This encourages portfolio stability
                                    if held_i <> held_j then
                                        // Small penalty for selecting both (x_i * x_j = 1)
                                        Some((i, j), 0.5 * costs.FixedCostPerTrade / budget)
                                    else
                                        None))
                        else
                            []

                    // ================================================================
                    // Combine base QUBO with transaction cost terms
                    // ================================================================

                    let allTerms =
                        (baseQubo.Q |> Map.toList) @ transactionCostTerms @ turnoverPenaltyTerms

                    // Aggregate terms with same indices
                    let aggregatedTerms =
                        allTerms
                        |> List.groupBy fst
                        |> List.map (fun (key, terms) ->
                            let totalCoeff = terms |> List.sumBy snd
                            key, totalCoeff)
                        |> Map.ofList

                    Ok
                        {
                            NumVariables = numAssets
                            Q = aggregatedTerms
                        }

        with ex ->
            Error(
                QuantumError.OperationError(
                    "QuboEncodingWithCosts",
                    $"Failed to encode portfolio with costs as QUBO: %s{ex.Message}"
                )
            )

    /// Create a portfolio problem with transaction costs
    let createProblemWithCosts
        (assets: PortfolioTypes.Asset list)
        (constraints: PortfolioSolver.Constraints)
        (riskAversion: float)
        (currentHoldings: Map<string, float>)
        (transactionCosts: TransactionCosts)
        : PortfolioProblemWithCosts =
        {
            BaseProblem =
                {
                    Assets = assets
                    Constraints = constraints
                    RiskAversion = riskAversion
                    Covariance = None
                }
            CurrentHoldings = { Holdings = currentHoldings }
            TransactionCosts = transactionCosts
        }

    // ================================================================================
    // SOLUTION DECODING
    // ================================================================================

    /// Decode QUBO solution bitstring to portfolio allocation
    ///
    /// Each selected asset receives one lot, lotWeight problem × Budget, as a possibly
    /// fractional number of shares worth at least one share; the rest of the budget stays
    /// uninvested. ExpectedReturn and Risk describe the
    /// invested holdings (weights = Value / TotalValue); Risk is sqrt(wᵀΣw) with a covariance
    /// matrix, otherwise sqrt(Σ (wᵢσᵢ)²). BestEnergy is meanVarianceEnergy of the bitstring,
    /// the value of the QUBO objective xᵀQx.
    ///
    /// Returns None for malformed measurements (fewer bits than assets), empty selections,
    /// selections that are not isFeasibleSelection and selections that break the holding
    /// constraints.
    let private decodeSolution (problem: PortfolioProblem) (bitstring: int array) : QuantumPortfolioSolution option =

        if bitstring.Length < List.length problem.Assets then
            None
        else
            // Extract selected assets (where bit = 1)
            let selectedAssets =
                problem.Assets
                |> List.mapi (fun i asset -> (asset.Symbol, bitstring.[i] = 1))
                |> Map.ofList

            let selectedIndexed =
                problem.Assets |> List.indexed |> List.filter (fun (i, _) -> bitstring.[i] = 1)

            if selectedIndexed.IsEmpty || not (isFeasibleSelection problem bitstring) then
                None
            else
                let valuePerAsset = lotWeight problem * problem.Constraints.Budget

                let allocationData =
                    selectedIndexed
                    |> List.map (fun (i, asset) ->
                        let shares =
                            if asset.Price = 0.0 then
                                0.0
                            else
                                valuePerAsset / asset.Price

                        let actualValue = shares * asset.Price
                        (i, asset, shares, actualValue))

                let totalValue = allocationData |> List.sumBy (fun (_, _, _, v) -> v)

                // Validate the decoded allocation against the constraints
                // before returning: per-asset MinHolding/MaxHolding and budget.
                let satisfiesConstraints =
                    totalValue > 0.0
                    && totalValue <= problem.Constraints.Budget * (1.0 + 1e-12)
                    && allocationData
                       |> List.forall (fun (_, _, _, v) ->
                           v <= problem.Constraints.MaxHolding * (1.0 + 1e-12)
                           && (problem.Constraints.MinHolding <= 0.0 || v >= problem.Constraints.MinHolding))

                if not satisfiesConstraints then
                    None
                else
                    let allocations =
                        allocationData
                        |> List.map (fun (_, asset, shares, actualValue) ->
                            {
                                PortfolioSolver.Allocation.Asset = asset
                                PortfolioSolver.Allocation.Shares = shares
                                PortfolioSolver.Allocation.Value = actualValue
                                PortfolioSolver.Allocation.Percentage = actualValue / totalValue
                            })

                    let weights = Array.zeroCreate<float> problem.Assets.Length

                    for (i, _, _, actualValue) in allocationData do
                        weights.[i] <- actualValue / totalValue

                    let expectedReturn =
                        allocations
                        |> List.sumBy (fun alloc -> alloc.Asset.ExpectedReturn * alloc.Percentage)

                    let risk = PortfolioTypes.portfolioRisk problem.Assets weights problem.Covariance

                    let sharpeRatio = if risk = 0.0 then 0.0 else expectedReturn / risk

                    Some
                        {
                            Allocations = allocations
                            TotalValue = totalValue
                            ExpectedReturn = expectedReturn
                            Risk = risk
                            SharpeRatio = sharpeRatio
                            BackendName = "" // Will be set by caller
                            NumShots = 0 // Will be set by caller
                            ElapsedMs = 0.0 // Will be set by caller
                            BestEnergy = meanVarianceEnergy problem bitstring
                            SelectedAssets = selectedAssets
                        }

    // ================================================================================
    // QUANTUM SOLVER
    // ================================================================================

    /// Configuration for quantum portfolio solving
    type QuantumPortfolioConfig =
        {
            /// Number of shots for execution
            NumShots: int

            /// Risk aversion parameter (0 = risk-neutral, 1 = very risk-averse)
            RiskAversion: float

            /// Initial QAOA parameters (gamma, beta), in the shared pipeline's units
            /// (normalised Hamiltonian, minimisation convention; see Core.QaoaCircuit)
            InitialParameters: float * float
        }

    /// Default configuration
    let defaultConfig =
        {
            NumShots = 1000
            RiskAversion = 0.5
            InitialParameters = (0.5, 0.5)
        }

    /// γ values of the angle grid, in units of the normalised cost Hamiltonian
    /// (QaoaExecutionHelpers normalises it). γ > 0 minimises (see Core.QaoaCircuit).
    let internal angleGridGammas = [| 0.25; 0.5; 0.75; 1.0; 1.5; 2.0 |]

    /// β values of the angle grid.
    let internal angleGridBetas =
        [| Math.PI / 8.0; Math.PI / 4.0; 3.0 * Math.PI / 8.0 |]

    /// The QUBO restricted to the affordable assets, with the asset index of each variable;
    /// unaffordable assets need no qubit.
    let internal circuitQubo (problem: PortfolioProblem) (qubo: GraphOptimization.QuboMatrix) : float[,] * int[] =
        let dense = Qubo.toDenseArray qubo.NumVariables qubo.Q
        let assets = problem.Assets |> List.toArray

        let kept =
            [|
                for i in 0 .. assets.Length - 1 do
                    if isAffordable problem assets.[i] then
                        yield i
            |]

        Array2D.init kept.Length kept.Length (fun a b -> dense.[kept.[a], kept.[b]]), kept

    /// The (γ, β) pair whose samples have the lowest mean QUBO energy. A function of its own
    /// rather than a lambda inside sampleWithAngleGridAsync: there the lambda keeps the task
    /// from compiling to a static state machine (FS3511 in Release builds).
    let private lowestMeanEnergyAngles (qubo: float[,]) (sampled: ((float * float) * int[][])[]) : float * float =
        sampled
        |> Array.minBy (fun (_, m) -> m |> Array.averageBy (QaoaExecutionHelpers.evaluateQubo qubo))
        |> fst

    /// Samples p = 1 QAOA at `initial` and every (γ, β) of the angle grid with gridShots shots
    /// each, then finalShots at the pair with the lowest mean sampled QUBO energy. Returns every
    /// sample and the chosen pair.
    let internal sampleWithAngleGridAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (qubo: float[,])
        (initial: float * float)
        (gridShots: int)
        (finalShots: int)
        (cancellationToken: CancellationToken)
        : Task<Result<int[][] * (float * float), QuantumError>> =
        task {
            let n = Array2D.length1 qubo
            let problemHam = QaoaCircuit.ProblemHamiltonian.fromQubo qubo
            let mixerHam = QaoaCircuit.MixerHamiltonian.create n

            let candidates =
                [|
                    yield initial
                    for g in angleGridGammas do
                        for b in angleGridBetas do
                            yield (g, b)
                |]
                |> Array.distinct

            let runs = ResizeArray<(float * float) * Result<int[][], QuantumError>>()

            for angles in candidates do
                let! result =
                    QaoaExecutionHelpers.executeQaoaCircuitAsync
                        backend
                        problemHam
                        mixerHam
                        [| angles |]
                        gridShots
                        cancellationToken

                runs.Add((angles, result))

            let failures =
                runs
                |> Seq.choose (fun (_, r) -> r |> Result.map (fun _ -> None) |> Result.defaultWith (fun e -> Some e))
                |> Seq.toList

            let sampled =
                runs
                |> Seq.choose (fun (angles, r) ->
                    match r with
                    | Ok measurements when measurements.Length > 0 -> Some(angles, measurements)
                    | _ -> None)
                |> Seq.toArray

            match failures with
            | err :: _ -> return Error err
            | [] when sampled.Length = 0 ->
                return Error(QuantumError.OperationError("QAOA", "The backend returned no samples"))
            | [] ->
                let bestAngles = lowestMeanEnergyAngles qubo sampled

                let! final =
                    QaoaExecutionHelpers.executeQaoaCircuitAsync
                        backend
                        problemHam
                        mixerHam
                        [| bestAngles |]
                        finalShots
                        cancellationToken

                return
                    match final with
                    | Error err -> Error err
                    | Ok measurements ->
                        let all = Array.append (sampled |> Array.collect snd) measurements
                        Ok(all, bestAngles)
        }

    /// QAOA pipeline shared by solveAsync and solveWithCovarianceAsync.
    let private solveCoreAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (assets: PortfolioTypes.Asset list)
        (covariance: float[,] option)
        (constraints: PortfolioSolver.Constraints)
        (config: QuantumPortfolioConfig)
        (cancellationToken: CancellationToken)
        : Task<Result<QuantumPortfolioSolution, QuantumError>> =

        let startTime = DateTime.UtcNow

        // Validate inputs
        let numAssets = assets.Length

        if numAssets = 0 then
            task { return Error(QuantumError.ValidationError("numAssets", "Portfolio problem has no assets")) }
        // Note: Backend validation removed (MaxQubits/Name properties no longer in interface)
        // Backends will return errors if qubit count exceeded
        elif config.NumShots <= 0 then
            task { return Error(QuantumError.ValidationError("numShots", "Number of shots must be positive")) }
        else
            try
                // Build portfolio problem
                let problem: PortfolioProblem =
                    {
                        Assets = assets
                        Constraints = constraints
                        RiskAversion = config.RiskAversion
                        Covariance = covariance
                    }

                // Step 1: Encode portfolio as QUBO
                match toQubo problem with
                | Error msg -> task { return Error msg }
                | Ok quboMatrix ->

                    // Step 2: QAOA on the affordable assets' QUBO with an angle grid
                    let circuitMatrix, kept = circuitQubo problem quboMatrix

                    let handleMeasurements (measurements: int array array) =
                        // Step 3: map the circuit's bits back to assets and keep the best
                        // feasible selection (minimum energy = maximum utility)
                        let toSelection (bits: int array) =
                            let selection = Array.zeroCreate<int> numAssets
                            kept |> Array.iteri (fun a i -> selection.[i] <- bits.[a])
                            selection

                        let portfolioResults =
                            measurements
                            |> Array.map toSelection
                            |> Array.distinct
                            |> Array.choose (decodeSolution problem)

                        if portfolioResults.Length = 0 then
                            Error(
                                QuantumError.OperationError(
                                    "DecodeSolution",
                                    "No feasible portfolio found in the quantum measurements"
                                )
                            )
                        else
                            let bestSolution = portfolioResults |> Array.minBy (fun sol -> sol.BestEnergy)

                            let elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds

                            Ok
                                { bestSolution with
                                    BackendName = backend.Name
                                    NumShots = config.NumShots
                                    ElapsedMs = elapsedMs
                                }

                    if kept.Length = 0 then
                        let lotValue = lotWeight problem * constraints.Budget

                        task {
                            return
                                Error(
                                    QuantumError.ValidationError(
                                        "Price",
                                        $"No asset can be bought with one lot of {lotValue}"
                                    )
                                )
                        }
                    else
                        task {
                            match!
                                sampleWithAngleGridAsync
                                    backend
                                    circuitMatrix
                                    config.InitialParameters
                                    (max 50 (config.NumShots / 10))
                                    config.NumShots
                                    cancellationToken
                            with
                            | Error err -> return Error err
                            | Ok(measurements, _) -> return handleMeasurements measurements
                        }
            with ex ->
                task {
                    return
                        Error(
                            QuantumError.OperationError(
                                "QuantumPortfolioSolver",
                                $"Quantum portfolio solver failed: %s{ex.Message}"
                            )
                        )
                }

    /// Solve portfolio optimization using quantum backend via QAOA (asynchronous)
    ///
    /// Full Pipeline:
    /// 1. Portfolio problem → QUBO matrix (mean-variance encoding, see toQubo)
    /// 2. QUBO over the affordable assets → p = 1 QAOA circuit (cost Hamiltonian normalised
    ///    to a largest |coefficient| of 1 by QaoaExecutionHelpers)
    /// 3. Sample the circuit at InitialParameters and on a (γ, β) grid (angleGridGammas,
    ///    angleGridBetas; NumShots / 10 shots each, at least 50), then NumShots shots at the
    ///    pair with the lowest mean sampled energy
    /// 4. Decode every sample → portfolio allocations
    /// 5. Return the feasible sample (isFeasibleSelection) with the lowest mean-variance energy
    ///
    /// The assets are treated as independent (Σ = diag(Riskᵢ²)); use
    /// solveWithCovarianceAsync to account for correlations.
    ///
    /// Parameters:
    ///   backend - Quantum backend to execute on (LocalBackend, IonQ, Rigetti)
    ///   assets - List of assets to optimize
    ///   constraints - Portfolio constraints (budget, min/max holding)
    ///   config - Configuration for execution
    ///
    /// Returns:
    ///   Task that returns Result with QuantumPortfolioSolution or QuantumError
    ///
    /// Note: This is the preferred method for cloud backends (IonQ, Rigetti) as it allows
    /// non-blocking execution.
    let solveAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (assets: PortfolioTypes.Asset list)
        (constraints: PortfolioSolver.Constraints)
        (config: QuantumPortfolioConfig)
        (cancellationToken: CancellationToken)
        : Task<Result<QuantumPortfolioSolution, QuantumError>> =
        solveCoreAsync backend assets None constraints config cancellationToken

    /// Solve mean-variance portfolio optimization with a covariance matrix via QAOA (asynchronous)
    ///
    /// Same pipeline as solveAsync, with the off-diagonal covariance terms in the QUBO and
    /// Risk = sqrt(wᵀΣw) in the result.
    ///
    /// Parameters:
    ///   backend - Quantum backend to execute on (LocalBackend, IonQ, Rigetti)
    ///   assets - List of assets to optimize
    ///   covariance - Covariance of the asset returns, rows and columns in asset order
    ///                (validated: square, one row per asset, symmetric, positive semidefinite)
    ///   constraints - Portfolio constraints (budget, min/max holding)
    ///   config - Configuration for execution
    ///   cancellationToken - Cancels the backend execution
    ///
    /// Returns:
    ///   Task with QuantumPortfolioSolution, or ValidationError for an invalid covariance
    let solveWithCovarianceAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (assets: PortfolioTypes.Asset list)
        (covariance: float[,])
        (constraints: PortfolioSolver.Constraints)
        (config: QuantumPortfolioConfig)
        (cancellationToken: CancellationToken)
        : Task<Result<QuantumPortfolioSolution, QuantumError>> =
        solveCoreAsync backend assets (Some covariance) constraints config cancellationToken

    /// Solve portfolio with default configuration (asynchronous)
    let solveWithDefaultsAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (assets: PortfolioTypes.Asset list)
        (constraints: PortfolioSolver.Constraints)
        (cancellationToken: CancellationToken)
        : Task<Result<QuantumPortfolioSolution, QuantumError>> =
        solveAsync backend assets constraints defaultConfig cancellationToken

    /// Solve portfolio with custom number of shots and risk aversion (asynchronous)
    let solveWithParamsAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (assets: PortfolioTypes.Asset list)
        (constraints: PortfolioSolver.Constraints)
        (numShots: int)
        (riskAversion: float)
        (cancellationToken: CancellationToken)
        : Task<Result<QuantumPortfolioSolution, QuantumError>> =
        let config =
            { defaultConfig with
                NumShots = numShots
                RiskAversion = riskAversion
            }

        solveAsync backend assets constraints config cancellationToken
