namespace FSharp.Azure.Quantum.Core

open System

/// Complete cost estimation module for Azure Quantum backends
/// ALL cost estimation code consolidated in this single file for AI context optimization
/// Supports: IonQ, Quantinuum, Rigetti backends with per-gate and per-shot cost calculations
module CostEstimation =
    
    // ============================================================================
    // UNITS OF MEASURE
    // ============================================================================
    
    /// United States Dollar
    [<Measure>] type USD
    
    /// Quantinuum Hardware Credits
    [<Measure>] type HQC
    
    /// Number of shots (circuit executions)
    [<Measure>] type shot
    
    /// Number of quantum gates
    [<Measure>] type gate
    
    /// Number of qubits
    [<Measure>] type qubit
    
    /// Milliseconds
    [<Measure>] type ms
    
    /// Microseconds  
    [<Measure>] type us
    
    // ============================================================================
    // CIRCUIT COST PROFILE (Simplified for cost estimation)
    // ============================================================================
    
    /// Quantum circuit cost profile with gate counts for cost estimation
    /// Note: This is a simplified representation used only for cost calculation.
    /// For full circuit representation, see CircuitBuilder.Circuit
    type CircuitCostProfile = {
        /// Number of single-qubit gates (H, X, Y, Z, etc.)
        SingleQubitGates: int<gate>
        
        /// Number of two-qubit gates (CNOT, CZ, etc.)
        TwoQubitGates: int<gate>
        
        /// Number of measurement operations
        Measurements: int<gate>
        
        /// Total number of qubits used
        QubitCount: int<qubit>
    }
    
    module CircuitCostProfile =
        /// Create empty circuit cost profile
        let empty = {
            SingleQubitGates = 0<gate>
            TwoQubitGates = 0<gate>
            Measurements = 0<gate>
            QubitCount = 0<qubit>
        }
        
        /// Calculate total gate count
        let totalGates (circuit: CircuitCostProfile) : int<gate> =
            circuit.SingleQubitGates + circuit.TwoQubitGates + circuit.Measurements
        
        /// Calculate circuit depth (simplified - actual depth requires gate scheduling)
        let depth (circuit: CircuitCostProfile) : int<gate> =
            totalGates circuit  // Conservative estimate: assume no parallelism
    
    // ============================================================================
    // BACKEND PRICING MODELS
    // ============================================================================
    
    /// IonQ backend pricing configuration
    type IonQPricing = {
        /// Minimum cost with error mitigation enabled (USD)
        MinimumCostWithErrorMitigation: decimal<USD>
        
        /// Minimum cost without error mitigation (USD)
        MinimumCostWithoutErrorMitigation: decimal<USD>
        
        /// Cost per single-qubit gate operation
        SingleQubitGateCost: decimal
        
        /// Cost per two-qubit gate operation
        TwoQubitGateCost: decimal
    }
    
    module IonQPricing =
        /// Default IonQ pricing (as of 2025-11)
        /// Source: Azure Quantum pricing documentation
        let Default = {
            MinimumCostWithErrorMitigation = 97.50M<USD>
            MinimumCostWithoutErrorMitigation = 12.42M<USD>
            SingleQubitGateCost = 0.000220M
            TwoQubitGateCost = 0.000975M
        }
    
    /// Quantinuum backend pricing configuration (HQC-based subscription model)
    type QuantinuumPricing = {
        /// Minimum HQC cost per job
        MinimumCostHQC: int<HQC>
        
        /// Weight factor for single-qubit gates
        SingleQubitGateWeight: float
        
        /// Weight factor for two-qubit gates  
        TwoQubitGateWeight: float
        
        /// Weight factor for measurement operations
        MeasurementWeight: float
        
        /// Shot divisor for HQC calculation
        ShotDivisor: int
    }
    
    module QuantinuumPricing =
        /// Default Quantinuum pricing (HQC model)
        /// Source: Azure Quantum pricing documentation
        let Default = {
            MinimumCostHQC = 5<HQC>
            SingleQubitGateWeight = 1.0
            TwoQubitGateWeight = 10.0
            MeasurementWeight = 5.0
            ShotDivisor = 5000
        }
        
        /// Subscription plans (for reference)
        let StandardPlan = {| MonthlyCost = 135000.0M<USD>; MonthlyQuota = 10000<HQC> |}
        let PremiumPlan = {| MonthlyCost = 185000.0M<USD>; MonthlyQuota = 17000<HQC> |}
    
    /// Rigetti backend pricing configuration (time-based)
    type RigettiPricing = {
        /// Cost per 10 milliseconds of execution time
        CostPerTenMs: decimal<USD>
    }
    
    module RigettiPricing =
        /// Default Rigetti pricing
        /// Source: Azure Quantum pricing documentation  
        let Default = {
            CostPerTenMs = 0.02M<USD>
        }
    
    /// Gate timing estimates for execution time calculation
    type GateTiming = {
        /// Single-qubit gate execution time
        SingleQubitGateTime: float<us>
        
        /// Two-qubit gate execution time
        TwoQubitGateTime: float<us>
    }
    
    module GateTiming =
        /// Default Rigetti gate timing estimates
        let RigettiDefault = {
            SingleQubitGateTime = 0.05<us>    // ~50 ns
            TwoQubitGateTime = 0.20<us>       // ~200 ns
        }
    
    // ============================================================================
    // COST BACKEND TYPES (for cost calculation)
    // ============================================================================
    
    /// Supported quantum backends for cost estimation
    /// Note: This is a simplified backend model used only for pricing.
    /// For full backend representation, see Types.Backend
    type CostBackend =
        | IonQ of useErrorMitigation: bool
        | Quantinuum
        | Rigetti
    
    module CostBackend =
        /// Get backend display name
        let name = function
            | IonQ true -> "IonQ (with error mitigation)"
            | IonQ false -> "IonQ (without error mitigation)"
            | Quantinuum -> "Quantinuum"
            | Rigetti -> "Rigetti"
    
    /// Detailed cost breakdown
    type CostBreakdown = {
        /// Base cost (minimum charge)
        BaseCost: decimal<USD>
        
        /// Cost from single-qubit gates
        SingleQubitGateCost: decimal<USD>
        
        /// Cost from two-qubit gates
        TwoQubitGateCost: decimal<USD>
        
        /// Cost from shots/measurements
        ShotCost: decimal<USD>
        
        /// Total estimated cost
        TotalCost: decimal<USD>
    }
    
    /// Complete cost estimate with range and warnings
    type CostEstimate = {
        /// Target backend
        Backend: CostBackend
        
        /// Minimum possible cost
        MinimumCost: decimal<USD>
        
        /// Maximum possible cost
        MaximumCost: decimal<USD>
        
        /// Expected cost (conservative estimate)
        ExpectedCost: decimal<USD>
        
        /// Currency code
        Currency: string
        
        /// Detailed cost breakdown
        Breakdown: CostBreakdown option
        
        /// Warning messages
        Warnings: string list
    }
    
    // ============================================================================
    // COST CALCULATION FUNCTIONS
    // ============================================================================
    
    /// Calculate IonQ cost based on gate counts and shots
    let calculateIonQCost 
        (pricing: IonQPricing) 
        (circuit: CircuitCostProfile) 
        (shots: int<shot>) 
        (useErrorMitigation: bool) 
        : CostEstimate =
        
        let baseCost = 
            if useErrorMitigation then 
                pricing.MinimumCostWithErrorMitigation 
            else 
                pricing.MinimumCostWithoutErrorMitigation
        
        let singleQubitCost = 
            decimal (int circuit.SingleQubitGates) * pricing.SingleQubitGateCost * decimal (int shots) * 1.0M<USD>
        
        let twoQubitCost = 
            decimal (int circuit.TwoQubitGates) * pricing.TwoQubitGateCost * decimal (int shots) * 1.0M<USD>
        
        let totalCost = baseCost + singleQubitCost + twoQubitCost
        
        let warnings = 
            if totalCost > 200.0M<USD> then
                [sprintf "Estimated cost $%.2f exceeds $200. Consider using simulator first." (float (totalCost / 1.0M<USD>))]
            else
                []
        
        {
            Backend = IonQ useErrorMitigation
            MinimumCost = baseCost
            MaximumCost = totalCost
            ExpectedCost = totalCost  // Conservative: use maximum
            Currency = "USD"
            Breakdown = Some {
                BaseCost = baseCost
                SingleQubitGateCost = singleQubitCost
                TwoQubitGateCost = twoQubitCost
                ShotCost = 0.0M<USD>  // Included in gate costs
                TotalCost = totalCost
            }
            Warnings = warnings
        }
    
    /// Calculate Quantinuum cost in HQC (Hardware Quantum Credits)
    let calculateQuantinuumHQC 
        (pricing: QuantinuumPricing) 
        (circuit: CircuitCostProfile) 
        (shots: int<shot>) 
        : int<HQC> =
        
        let gateCount1Q = float (int circuit.SingleQubitGates)
        let gateCount2Q = float (int circuit.TwoQubitGates)
        let measurementCount = float (int circuit.Measurements)
        let shotCount = int shots
        
        let operationCost = 
            (gateCount1Q * pricing.SingleQubitGateWeight) +
            (gateCount2Q * pricing.TwoQubitGateWeight) +
            (measurementCount * pricing.MeasurementWeight)
        
        let totalHQC = 
            pricing.MinimumCostHQC + 
            int ((operationCost * float shotCount) / float pricing.ShotDivisor) * 1<HQC>
        
        totalHQC
    
    /// Calculate Quantinuum cost estimate
    let calculateQuantinuumCost 
        (pricing: QuantinuumPricing) 
        (circuit: CircuitCostProfile) 
        (shots: int<shot>) 
        : CostEstimate =
        
        let hqc = calculateQuantinuumHQC pricing circuit shots
        
        let warnings = [
            sprintf "Will consume %d HQC from subscription quota" (int hqc)
            "Quantinuum uses subscription model: Standard ($135k/mo, 10k HQC) or Premium ($185k/mo, 17k HQC)"
        ]
        
        {
            Backend = Quantinuum
            MinimumCost = 0.0M<USD>  // Subscription model
            MaximumCost = 0.0M<USD>  // Subscription model
            ExpectedCost = 0.0M<USD>  // Charged via HQC quota
            Currency = "HQC"
            Breakdown = None  // HQC model doesn't use USD breakdown
            Warnings = warnings
        }
    
    /// Estimate circuit execution time on Rigetti hardware
    let estimateRigettiExecutionTime 
        (timing: GateTiming) 
        (circuit: CircuitCostProfile) 
        : float<ms> =
        
        let gateCount1Q = float (int circuit.SingleQubitGates)
        let gateCount2Q = float (int circuit.TwoQubitGates)
        
        let totalTimeUs = 
            (gateCount1Q * timing.SingleQubitGateTime) +
            (gateCount2Q * timing.TwoQubitGateTime)
        
        totalTimeUs / 1000.0<us/ms>
    
    /// Calculate Rigetti cost based on execution time
    let calculateRigettiCost 
        (pricing: RigettiPricing) 
        (timing: GateTiming) 
        (circuit: CircuitCostProfile) 
        (shots: int<shot>) 
        : CostEstimate =
        
        let execTime = estimateRigettiExecutionTime timing circuit
        let totalExecTime = execTime * float (int shots)
        let tensOfMs = ceil (totalExecTime / 10.0<ms>)
        let cost = decimal tensOfMs * pricing.CostPerTenMs
        
        let warnings = 
            if cost > 200.0M<USD> then
                [sprintf "Estimated cost $%.2f exceeds $200. Consider reducing shots or using simulator." (float (cost / 1.0M<USD>))]
            else
                []
        
        {
            Backend = Rigetti
            MinimumCost = cost
            MaximumCost = cost
            ExpectedCost = cost
            Currency = "USD"
            Breakdown = Some {
                BaseCost = 0.0M<USD>
                SingleQubitGateCost = 0.0M<USD>  // Time-based pricing
                TwoQubitGateCost = 0.0M<USD>     // Time-based pricing
                ShotCost = cost                   // All cost is execution time
                TotalCost = cost
            }
            Warnings = warnings
        }
    
    // ============================================================================
    // UNIFIED COST ESTIMATION API
    // ============================================================================
    
    /// Estimate cost for a circuit on specified backend
    let estimateCost 
        (backend: CostBackend) 
        (circuit: CircuitCostProfile) 
        (shots: int<shot>) 
        : Result<CostEstimate, string> =
        
        if int shots < 1 then
            Error "Shot count must be at least 1"
        elif int circuit.QubitCount < 1 then
            Error "Circuit must have at least 1 qubit"
        else
            try
                let estimate = 
                    match backend with
                    | IonQ useErrorMitigation ->
                        calculateIonQCost IonQPricing.Default circuit shots useErrorMitigation
                    
                    | Quantinuum ->
                        calculateQuantinuumCost QuantinuumPricing.Default circuit shots
                    
                    | Rigetti ->
                        calculateRigettiCost 
                            RigettiPricing.Default 
                            GateTiming.RigettiDefault 
                            circuit 
                            shots
                
                Ok estimate
            with
            | ex -> Error (sprintf "Cost estimation failed: %s" ex.Message)
    
    /// Compare costs across multiple backends
    let compareCosts 
        (backends: CostBackend list) 
        (circuit: CircuitCostProfile) 
        (shots: int<shot>) 
        : Result<CostEstimate list, string> =
        
        backends
        |> List.map (fun backend -> estimateCost backend circuit shots)
        |> List.fold (fun acc result ->
            match acc, result with
            | Ok estimates, Ok estimate -> Ok (estimate :: estimates)
            | Error msg, _ -> Error msg
            | _, Error msg -> Error msg
        ) (Ok [])
        |> Result.map List.rev
    
    /// Find the cheapest backend from a list of options
    let findCheapestBackend 
        (backends: CostBackend list) 
        (circuit: CircuitCostProfile) 
        (shots: int<shot>) 
        : Result<CostBackend * CostEstimate, string> =
        
        if backends.IsEmpty then
            Error "No backends provided"
        else
            compareCosts backends circuit shots
            |> Result.map (fun estimates ->
                estimates 
                |> List.minBy (fun est -> est.ExpectedCost)
                |> fun cheapestEstimate -> (cheapestEstimate.Backend, cheapestEstimate))
    
    // ============================================================================
    // COST OPTIMIZATION RECOMMENDATIONS
    // ============================================================================
    
    /// Cost optimization recommendation
    type CostRecommendation = {
        /// Current backend being used
        CurrentBackend: CostBackend
        
        /// Recommended cheaper backend
        RecommendedBackend: CostBackend
        
        /// Potential cost savings (USD)
        PotentialSavings: decimal<USD>
        
        /// Human-readable reasoning
        Reasoning: string
        
        /// Current backend cost estimate
        CurrentCost: CostEstimate
        
        /// Recommended backend cost estimate
        RecommendedCost: CostEstimate
    }
    
    /// Format backend name for display
    let private formatBackendName (backend: CostBackend) : string =
        match backend with
        | IonQ true -> "IonQ (with error mitigation)"
        | IonQ false -> "IonQ (without error mitigation)"
        | Quantinuum -> "Quantinuum"
        | Rigetti -> "Rigetti"
    
    /// Generate cost optimization recommendation
    /// Returns Some recommendation if savings >= 20%, None if current is optimal
    let recommendCostOptimization
        (currentBackend: CostBackend)
        (availableBackends: CostBackend list)
        (circuit: CircuitCostProfile)
        (shots: int<shot>)
        : Result<CostRecommendation option, string> =
        
        match estimateCost currentBackend circuit shots with
        | Error msg -> Error msg
        | Ok currentEstimate ->
            match findCheapestBackend availableBackends circuit shots with
            | Error msg -> Error msg
            | Ok (cheapest, cheapestEstimate) ->
                let savings = currentEstimate.ExpectedCost - cheapestEstimate.ExpectedCost
                let savingsPercent = (float (savings / currentEstimate.ExpectedCost)) * 100.0
                
                // Only recommend if savings >= 20%
                if savings > 0.0M<USD> && savingsPercent >= 20.0 && cheapest <> currentBackend then
                    let recommendation = {
                        CurrentBackend = currentBackend
                        RecommendedBackend = cheapest
                        PotentialSavings = savings
                        Reasoning = sprintf "Save $%.2f (%.0f%% reduction) by switching from %s to %s"
                            (float (savings / 1.0M<USD>))
                            savingsPercent
                            (formatBackendName currentBackend)
                            (formatBackendName cheapest)
                        CurrentCost = currentEstimate
                        RecommendedCost = cheapestEstimate
                    }
                    Ok (Some recommendation)
                else
                    Ok None
    
    // ============================================================================
    // BUDGET ENFORCEMENT
    // ============================================================================
    
    /// Budget policy configuration
    type BudgetPolicy = {
        /// Daily spending limit (USD)
        DailyLimit: decimal<USD> option
        
        /// Monthly spending limit (USD)
        MonthlyLimit: decimal<USD> option
        
        /// Per-job spending limit (USD)
        PerJobLimit: decimal<USD> option
        
        /// Warn when reaching this percentage of budget
        WarnAtPercent: float
    }
    
    module BudgetPolicy =
        /// Default budget policy for development environment
        let Development = {
            DailyLimit = Some 50.0M<USD>
            MonthlyLimit = Some 500.0M<USD>
            PerJobLimit = Some 20.0M<USD>
            WarnAtPercent = 80.0
        }
        
        /// Default budget policy for production environment
        let Production = {
            DailyLimit = Some 500.0M<USD>
            MonthlyLimit = Some 10000.0M<USD>
            PerJobLimit = Some 200.0M<USD>
            WarnAtPercent = 80.0
        }
    
    /// Budget check result
    type BudgetCheckResult =
        | Approved
        | Warning of message: string
        | Denied of reason: string
    
    /// Check if job cost is within budget limits
    let checkBudget 
        (policy: BudgetPolicy) 
        (cost: CostEstimate) 
        (dailySpent: decimal<USD>) 
        (monthlySpent: decimal<USD>) 
        : BudgetCheckResult =
        
        // Check per-job limit
        match policy.PerJobLimit with
        | Some limit when cost.ExpectedCost > limit ->
            Denied (sprintf "Job cost $%.2f exceeds per-job limit $%.2f" 
                (float (cost.ExpectedCost / 1.0M<USD>)) (float (limit / 1.0M<USD>)))
        | _ ->
            // Check daily limit
            match policy.DailyLimit with
            | Some limit when dailySpent + cost.ExpectedCost > limit ->
                let remaining = limit - dailySpent
                Denied (sprintf "Job cost $%.2f would exceed daily limit (remaining: $%.2f)" 
                    (float (cost.ExpectedCost / 1.0M<USD>)) (float (remaining / 1.0M<USD>)))
            | Some limit when (dailySpent + cost.ExpectedCost) / limit * 100.0M > decimal policy.WarnAtPercent ->
                let percentUsed = (dailySpent + cost.ExpectedCost) / limit * 100.0M
                Warning (sprintf "Job will use %.1f%% of daily budget" (float percentUsed))
            | _ ->
                // Check monthly limit
                match policy.MonthlyLimit with
                | Some limit when monthlySpent + cost.ExpectedCost > limit ->
                    let remaining = limit - monthlySpent
                    Denied (sprintf "Job cost $%.2f would exceed monthly limit (remaining: $%.2f)" 
                        (float (cost.ExpectedCost / 1.0M<USD>)) (float (remaining / 1.0M<USD>)))
                | Some limit when (monthlySpent + cost.ExpectedCost) / limit * 100.0M > decimal policy.WarnAtPercent ->
                    let percentUsed = (monthlySpent + cost.ExpectedCost) / limit * 100.0M
                    Warning (sprintf "Job will use %.1f%% of monthly budget" (float percentUsed))
                | _ ->
                    Approved
    
    // ============================================================================
    // COST TRACKING
    // ============================================================================
    
    /// Cost tracking record for completed jobs
    type CostTrackingRecord = {
        /// Job ID
        JobId: string
        
        /// Backend used
        Backend: CostBackend
        
        /// Estimated cost before execution
        EstimatedCost: decimal<USD>
        
        /// Actual cost charged (if available)
        ActualCost: decimal<USD> option
        
        /// Timestamp of job completion
        Timestamp: DateTimeOffset
        
        /// Circuit characteristics
        Circuit: CircuitCostProfile
        
        /// Shots executed
        Shots: int<shot>
    }
    
    /// Cost tracker state
    type CostTracker = {
        /// All tracked cost records
        Records: CostTrackingRecord list
        
        /// Daily spending total (USD)
        DailySpent: decimal<USD>
        
        /// Monthly spending total (USD)
        MonthlySpent: decimal<USD>
        
        /// Last reset timestamp for daily tracking
        LastDailyReset: DateTimeOffset
        
        /// Last reset timestamp for monthly tracking
        LastMonthlyReset: DateTimeOffset
    }
    
    module CostTracker =
        /// Create new cost tracker
        let create () : CostTracker = {
            Records = []
            DailySpent = 0.0M<USD>
            MonthlySpent = 0.0M<USD>
            LastDailyReset = DateTimeOffset.UtcNow
            LastMonthlyReset = DateTimeOffset.UtcNow
        }
        
        /// Check if daily reset is needed
        let needsDailyReset (tracker: CostTracker) : bool =
            let now = DateTimeOffset.UtcNow
            now.Date > tracker.LastDailyReset.Date
        
        /// Check if monthly reset is needed
        let needsMonthlyReset (tracker: CostTracker) : bool =
            let now = DateTimeOffset.UtcNow
            now.Month <> tracker.LastMonthlyReset.Month || now.Year <> tracker.LastMonthlyReset.Year
        
        /// Reset daily spending if needed
        let resetDaily (tracker: CostTracker) : CostTracker =
            if needsDailyReset tracker then
                { tracker with 
                    DailySpent = 0.0M<USD>
                    LastDailyReset = DateTimeOffset.UtcNow }
            else
                tracker
        
        /// Reset monthly spending if needed
        let resetMonthly (tracker: CostTracker) : CostTracker =
            if needsMonthlyReset tracker then
                { tracker with 
                    MonthlySpent = 0.0M<USD>
                    LastMonthlyReset = DateTimeOffset.UtcNow }
            else
                tracker
        
        /// Add cost tracking record
        let addRecord (record: CostTrackingRecord) (tracker: CostTracker) : CostTracker =
            let tracker = tracker |> resetDaily |> resetMonthly
            
            let cost = record.ActualCost |> Option.defaultValue record.EstimatedCost
            
            {
                Records = record :: tracker.Records
                DailySpent = tracker.DailySpent + cost
                MonthlySpent = tracker.MonthlySpent + cost
                LastDailyReset = tracker.LastDailyReset
                LastMonthlyReset = tracker.LastMonthlyReset
            }
        
        /// Get total spending for a time period
        let getTotalSpending (since: DateTimeOffset) (tracker: CostTracker) : decimal<USD> =
            tracker.Records
            |> List.filter (fun r -> r.Timestamp >= since)
            |> List.sumBy (fun r -> r.ActualCost |> Option.defaultValue r.EstimatedCost)
        
        /// Get spending by backend
        let getSpendingByBackend (tracker: CostTracker) : Map<CostBackend, decimal<USD>> =
            tracker.Records
            |> List.groupBy (fun r -> r.Backend)
            |> List.map (fun (backend, records) ->
                let total = 
                    records 
                    |> List.sumBy (fun r -> r.ActualCost |> Option.defaultValue r.EstimatedCost)
                backend, total)
            |> Map.ofList
    
    // ============================================================================
    // CLI COST DASHBOARD
    // ============================================================================
    
    /// Display cost dashboard to console
    let displayCostDashboard (records: CostTrackingRecord list) : unit =
        printfn "\n=== Cost Dashboard ==="
        
        if records.IsEmpty then
            printfn "\nNo cost records to display."
        else
            // Helper to convert USD to float for display
            let usdFloat (cost: decimal<USD>) = float (cost / 1.0M<USD>)
            
            // Today's spending
            let today = DateTimeOffset.UtcNow.Date
            let todayRecords = 
                records 
                |> List.filter (fun r -> r.Timestamp.Date = today)
            
            let todaySpend = 
                todayRecords
                |> List.sumBy (fun r -> r.ActualCost |> Option.defaultValue r.EstimatedCost)
            
            printfn "\n📅 Today: $%.2f (%d jobs)" (usdFloat todaySpend) todayRecords.Length
            
            // This month's spending
            let thisMonth = DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month
            let monthlyRecords =
                records
                |> List.filter (fun r -> (r.Timestamp.Year, r.Timestamp.Month) = thisMonth)
            
            let monthlySpend =
                monthlyRecords
                |> List.sumBy (fun r -> r.ActualCost |> Option.defaultValue r.EstimatedCost)
            
            printfn "📆 This Month: $%.2f (%d jobs)" (usdFloat monthlySpend) monthlyRecords.Length
            
            // Total spending
            let totalSpend = 
                records 
                |> List.sumBy (fun r -> r.ActualCost |> Option.defaultValue r.EstimatedCost)
            
            printfn "💰 Total: $%.2f (%d jobs)" (usdFloat totalSpend) records.Length
            
            // Breakdown by backend
            printfn "\n📊 Spending by Backend:"
            records
            |> List.groupBy (fun r -> r.Backend)
            |> List.sortByDescending (fun (_, recs) -> 
                recs |> List.sumBy (fun r -> r.ActualCost |> Option.defaultValue r.EstimatedCost))
            |> List.iter (fun (backend, recs) ->
                let total = recs |> List.sumBy (fun r -> r.ActualCost |> Option.defaultValue r.EstimatedCost)
                printfn "  %s: $%.2f (%d jobs)" (formatBackendName backend) (usdFloat total) recs.Length)
            
            // Estimate accuracy
            let recordsWithActual = 
                records 
                |> List.filter (fun r -> r.ActualCost.IsSome)
            
            if not recordsWithActual.IsEmpty then
                let accuracyErrors =
                    recordsWithActual
                    |> List.map (fun r -> 
                        let actual = r.ActualCost.Value
                        let estimated = r.EstimatedCost
                        abs(float ((actual - estimated) / actual)))
                
                let avgError = (List.average accuracyErrors) * 100.0
                printfn "\n📈 Estimate Accuracy: %.1f%% average error" avgError
    
    // ============================================================================
    // AZURE QUANTUM METADATA PARSING (for Azure job cost info)
    // ============================================================================
    
    /// Actual cost information from Azure Quantum job metadata
    type CostInfo = {
        /// Job ID
        JobId: string
        
        /// Actual cost charged
        ActualCost: decimal<USD> option
        
        /// Currency code
        Currency: string
        
        /// Billing status
        BillingStatus: string option
    }
    
    // ============================================================================
    // SIMPLIFIED LEGACY API (for backward compatibility with Client.fs)
    // ============================================================================
    
    /// Simplified cost estimate (compatible with old Cost.fs API)
    type SimpleCostEstimate = {
        /// Target backend ID
        Target: string
        
        /// Minimum estimated cost
        MinimumCost: decimal<USD>
        
        /// Maximum estimated cost
        MaximumCost: decimal<USD>
        
        /// Expected cost (conservative estimate)
        ExpectedCost: decimal<USD>
        
        /// Currency code
        Currency: string
        
        /// Warning messages
        Warnings: string list
    }
    
    /// Simplified cost estimation based on target string and shot count
    /// This is a compatibility wrapper for Client.fs (legacy Cost.fs API)
    let estimateCostSimple (target: string) (shots: int) : Result<SimpleCostEstimate, string> =
        if shots < 1 then
            Error "Shot count must be at least 1"
        elif String.IsNullOrWhiteSpace(target) then
            Error "Target backend cannot be empty"
        else
            // Detect backend type from target string
            let isSimulator = target.ToLowerInvariant().Contains("simulator")
            
            if isSimulator then
                // Simulators are free
                Ok {
                    Target = target
                    MinimumCost = 0.0M<USD>
                    MaximumCost = 0.0M<USD>
                    ExpectedCost = 0.0M<USD>
                    Currency = "USD"
                    Warnings = []
                }
            else
                // QPU - use simple heuristic (conservative estimate)
                let baseCost = 100.0M<USD>
                let perShotCost = 0.01M<USD>
                let minCost = baseCost
                let maxCost = baseCost + (decimal shots * perShotCost)
                
                let warnings =
                    if maxCost > 200.0M<USD> then
                        [ sprintf "Estimated cost $%.2f exceeds $200. Consider using simulator first."
                              (float (maxCost / 1.0M<USD>)) ]
                    else
                        []
                
                Ok {
                    Target = target
                    MinimumCost = minCost
                    MaximumCost = maxCost
                    ExpectedCost = maxCost  // Conservative: use max
                    Currency = "USD"
                    Warnings = warnings
                }
    
    /// Parse cost information from Azure Quantum job metadata JSON
    /// Returns cost information extracted from Azure's costData field
    let parseCostFromMetadata (costDataJson: string option) : CostInfo option =
        match costDataJson with
        | None -> None
        | Some json when String.IsNullOrWhiteSpace(json) -> None
        | Some json ->
            try
                // Azure Quantum returns cost in "costData" field
                // Example: {"estimated": 135.50, "currency": "USD"}
                use doc = System.Text.Json.JsonDocument.Parse(json)
                let root = doc.RootElement
                
                let mutable element = Unchecked.defaultof<System.Text.Json.JsonElement>
                
                let cost =
                    if root.TryGetProperty("estimated", &element) then
                        Some(decimal (element.GetDouble()) * 1.0M<USD>)
                    elif root.TryGetProperty("actual", &element) then
                        Some(decimal (element.GetDouble()) * 1.0M<USD>)
                    else
                        None
                
                let currency =
                    if root.TryGetProperty("currency", &element) then
                        element.GetString()
                    else
                        "USD"
                
                let billingStatus =
                    if root.TryGetProperty("billingStatus", &element) then
                        Some(element.GetString())
                    else
                        None
                
                Some {
                    JobId = ""  // Will be set by caller
                    ActualCost = cost
                    Currency = currency
                    BillingStatus = billingStatus
                }
            with _ ->
                None
    
    // ============================================================================
    // CSV PERSISTENCE (TKT-48)
    // ============================================================================
    
    /// CSV field escaping - handles commas, quotes, and newlines in field values
    let private escapeCsvField (field: string) : string =
        if field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r") then
            // Escape quotes by doubling them, then wrap in quotes
            "\"" + field.Replace("\"", "\"\"") + "\""
        else
            field
    
    /// Convert backend to CSV-friendly string representation
    let private backendToCsvString (backend: CostBackend) : string =
        match backend with
        | IonQ true -> "IonQ-EM"
        | IonQ false -> "IonQ-NoEM"
        | Quantinuum -> "Quantinuum"
        | Rigetti -> "Rigetti"
    
    /// Parse backend from CSV string representation
    let private backendFromCsvString (str: string) : Result<CostBackend, string> =
        match str with
        | "IonQ-EM" -> Ok (IonQ true)
        | "IonQ-NoEM" -> Ok (IonQ false)
        | "Quantinuum" -> Ok Quantinuum
        | "Rigetti" -> Ok Rigetti
        | _ -> Error (sprintf "Unknown backend: %s" str)
    
    /// Save cost tracking record to CSV file (appends if file exists)
    let saveCostRecordToCsv (filePath: string) (record: CostTrackingRecord) : Result<unit, string> =
        try
            // CSV format: JobId,Backend,EstimatedCost,ActualCost,Timestamp,SingleQubitGates,TwoQubitGates,Measurements,QubitCount,Shots
            let actualCostStr = 
                match record.ActualCost with
                | Some cost -> string (cost / 1.0M<USD>)
                | None -> ""
            
            let csvLine = 
                [
                    escapeCsvField record.JobId
                    backendToCsvString record.Backend
                    string (record.EstimatedCost / 1.0M<USD>)
                    actualCostStr
                    record.Timestamp.ToString("o")  // ISO 8601 format
                    string (int record.Circuit.SingleQubitGates)
                    string (int record.Circuit.TwoQubitGates)
                    string (int record.Circuit.Measurements)
                    string (int record.Circuit.QubitCount)
                    string (int record.Shots)
                ]
                |> String.concat ","
            
            // Append to file (create if doesn't exist)
            System.IO.File.AppendAllLines(filePath, [csvLine])
            Ok ()
        with ex ->
            Error (sprintf "Failed to save cost record to CSV: %s" ex.Message)
    
    /// Load cost history from CSV file
    let loadCostHistoryFromCsv (filePath: string) : Result<CostTrackingRecord list, string> =
        try
            if not (System.IO.File.Exists(filePath)) then
                // Return empty list for non-existent file (not an error)
                Ok []
            else
                let lines = System.IO.File.ReadAllLines(filePath)
                
                let records =
                    lines
                    |> Array.toList
                    |> List.choose (fun line ->
                        if String.IsNullOrWhiteSpace(line) then
                            None
                        else
                            try
                                // Parse CSV line (handle quoted fields with commas)
                                let fields = 
                                    let rec parseFields pos inQuotes currentField fields =
                                        if pos >= line.Length then
                                            // Add final field
                                            List.rev (currentField :: fields)
                                        else
                                            let c = line.[pos]
                                            match c with
                                            | '"' -> 
                                                if inQuotes && pos + 1 < line.Length && line.[pos + 1] = '"' then
                                                    // Escaped quote - add single quote and skip next char
                                                    parseFields (pos + 2) inQuotes (currentField + "\"") fields
                                                else
                                                    // Toggle quote mode
                                                    parseFields (pos + 1) (not inQuotes) currentField fields
                                            | ',' when not inQuotes ->
                                                // Field delimiter
                                                parseFields (pos + 1) inQuotes "" (currentField :: fields)
                                            | _ ->
                                                parseFields (pos + 1) inQuotes (currentField + string c) fields
                                    
                                    parseFields 0 false "" []
                                
                                if fields.Length <> 10 then
                                    None
                                else
                                    match backendFromCsvString fields.[1] with
                                    | Error _ -> None
                                    | Ok backend ->
                                        let actualCost = 
                                            if String.IsNullOrWhiteSpace(fields.[3]) then 
                                                None 
                                            else 
                                                Some (decimal fields.[3] * 1.0M<USD>)
                                        
                                        Some {
                                            JobId = fields.[0]
                                            Backend = backend
                                            EstimatedCost = decimal fields.[2] * 1.0M<USD>
                                            ActualCost = actualCost
                                            Timestamp = DateTimeOffset.Parse(fields.[4])
                                            Circuit = {
                                                SingleQubitGates = int fields.[5] * 1<gate>
                                                TwoQubitGates = int fields.[6] * 1<gate>
                                                Measurements = int fields.[7] * 1<gate>
                                                QubitCount = int fields.[8] * 1<qubit>
                                            }
                                            Shots = int fields.[9] * 1<shot>
                                        }
                            with _ ->
                                None  // Skip malformed lines
                    )
                
                Ok records
        with ex ->
            Error (sprintf "Failed to load cost history from CSV: %s" ex.Message)
