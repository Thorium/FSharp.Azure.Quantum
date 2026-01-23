namespace FSharp.Azure.Quantum.Examples.Fraud.TransactionFraudBatchScoring

open System

module Metrics =
    type private AuprcState =
        { Tp: int
          Fp: int
          PrevRecall: float
          Area: float }

    // PR-AUC via step-wise integration on precision-recall curve.
    let auprc (scores: (float * int) array) : float =
        let sorted = scores |> Array.sortByDescending fst

        let totalPos = sorted |> Array.sumBy (fun (_, y) -> if y = 1 then 1 else 0)
        if totalPos = 0 then 0.0
        else
            let finalState =
                sorted
                |> Array.fold
                    (fun s (_, y) ->
                        let tp, fp = if y = 1 then s.Tp + 1, s.Fp else s.Tp, s.Fp + 1
                        let precision = float tp / float (tp + fp)
                        let recall = float tp / float totalPos
                        let deltaRecall = recall - s.PrevRecall

                        { Tp = tp
                          Fp = fp
                          PrevRecall = recall
                          Area = s.Area + precision * deltaRecall })
                    { Tp = 0; Fp = 0; PrevRecall = 0.0; Area = 0.0 }

            finalState.Area

    // Population Stability Index (PSI) for a score distribution.
    // Bins are created from expected sample quantiles.
    let psi (expectedScores: float array) (actualScores: float array) (bins: int) : float =
        if expectedScores.Length = 0 || actualScores.Length = 0 then 0.0
        else
            let eps = 1e-12
            let sorted = expectedScores |> Array.sort

            let quantile (q: float) =
                let idx = int (Math.Round(q * float (sorted.Length - 1))) |> max 0 |> min (sorted.Length - 1)
                sorted.[idx]

            let cuts = [| for i in 1 .. bins - 1 -> quantile (float i / float bins) |]

            let binIndex (x: float) =
                let rec loop i =
                    if i >= cuts.Length then cuts.Length
                    elif x <= cuts.[i] then i
                    else loop (i + 1)
                loop 0

            let counts (xs: float array) =
                xs
                |> Array.countBy binIndex
                |> Map.ofArray
                |> fun m -> Array.init bins (fun i -> m |> Map.tryFind i |> Option.defaultValue 0)

            let eCounts = counts expectedScores
            let aCounts = counts actualScores

            let eTotal = float expectedScores.Length
            let aTotal = float actualScores.Length

            Array.init bins id
            |> Array.sumBy (fun i ->
                let e = (float eCounts.[i] / eTotal) |> max eps
                let a = (float aCounts.[i] / aTotal) |> max eps
                (a - e) * log (a / e))
