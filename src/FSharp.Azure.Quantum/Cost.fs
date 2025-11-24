namespace FSharp.Azure.Quantum.Core

open System

module Cost =

    /// Cost information with unit of measure
    [<Measure>]
    type USD

    /// Cost estimate for a quantum job
    type CostEstimate =
        {
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

    /// Actual cost information from Azure
    type CostInfo =
        {
            /// Job ID
            JobId: string

            /// Actual cost charged
            ActualCost: decimal<USD> option

            /// Currency code
            Currency: string

            /// Billing status
            BillingStatus: string option
        }

    /// Estimate cost for a job submission
    /// Note: This is a simplified implementation - real cost depends on:
    /// - Backend type (IonQ, Quantinuum, Rigetti, PASQAL)
    /// - Circuit complexity (gate count, qubit count)
    /// - Shots requested
    /// - Error mitigation options
    let estimateCost (target: string) (shots: int) : Result<CostEstimate, string> =
        if shots < 1 then
            Error "Shot count must be at least 1"
        elif String.IsNullOrWhiteSpace(target) then
            Error "Target backend cannot be empty"
        else
            // Simple heuristic-based estimation
            // Simulators are free, QPUs have base cost + shot cost
            let isSimulator = target.ToLowerInvariant().Contains("simulator")

            if isSimulator then
                // Simulators are typically free
                Ok
                    { Target = target
                      MinimumCost = 0.0M<USD>
                      MaximumCost = 0.0M<USD>
                      ExpectedCost = 0.0M<USD>
                      Currency = "USD"
                      Warnings = [] }
            else
                // QPU estimation - simplified model
                // IonQ typical: ~$12-100 base + per-shot/gate costs
                // Conservative estimate: assume ~$100 base + $0.01 per shot
                let baseCost = 100.0M<USD>
                let perShotCost = 0.01M<USD>
                let minCost = baseCost
                let maxCost = baseCost + (decimal shots * perShotCost)

                let warnings =
                    if maxCost > 200.0M<USD> then
                        [ sprintf
                              "Estimated cost $%.2f exceeds $200. Consider using simulator first."
                              (float (maxCost / 1.0M<USD>)) ]
                    else
                        []

                Ok
                    { Target = target
                      MinimumCost = minCost
                      MaximumCost = maxCost
                      ExpectedCost = maxCost // Conservative: use max
                      Currency = "USD"
                      Warnings = warnings }

    /// Parse cost information from Azure job metadata
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

                Some
                    { JobId = "" // Will be set by caller
                      ActualCost = cost
                      Currency = currency
                      BillingStatus = billingStatus }
            with _ ->
                None
