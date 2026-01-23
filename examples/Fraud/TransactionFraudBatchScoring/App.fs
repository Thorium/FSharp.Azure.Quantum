namespace FSharp.Azure.Quantum.Examples.Fraud.TransactionFraudBatchScoring

open System
open System.Diagnostics
open System.IO

open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Business.BinaryClassifier

open FSharp.Azure.Quantum.Examples.Common

type RunMetrics =
    { run_id: string
      train_path: string
      train_sha256: string
      arch: string
      shots: int
      seed: int
      test_fraction: float
      train_rows: int
      train_pos_rate: float
      test_rows: int
      test_pos_rate: float
      accuracy: float
      precision: float
      recall: float
      f1: float
      auprc: float
      psi_score_train_vs_test: float
      elapsed_ms_total: int64
      elapsed_ms_train: int64
      elapsed_ms_eval: int64
      elapsed_ms_score: int64 }

module App =
    let private defaultTrainPath =
        Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "_data", "transactions_tiny.csv"))

    let private printHelp () =
        printfn "TransactionFraudBatchScoring"
        printfn "  --train <path>        (CSV: transaction_id,amount,hour,merchant_category,distance_km,txn_count_24h,label)"
        printfn "  --score <path>        (optional CSV without label; or with label for scoring metrics)"
        printfn "  --out <dir>           (output folder)"
        printfn "  --arch quantum|hybrid (default: hybrid)"
        printfn "  --shots <n>           (default: 1000)"
        printfn "  --seed <n>            (default: 42)"
        printfn "  --test-fraction <x>   (default: 0.2)"

    let private parseArch (raw: string) =
        match raw.Trim().ToLowerInvariant() with
        | "quantum" -> Architecture.Quantum
        | _ -> Architecture.Hybrid

    let private posRate (ys: int array) =
        if ys.Length = 0 then 0.0
        else float (ys |> Array.sumBy id) / float ys.Length

    let run (argv: string array) : int =
        let args = Cli.parse argv

        if Cli.hasFlag "help" args || Cli.hasFlag "h" args then
            printHelp ()
            0
        else
            let swTotal = Stopwatch.StartNew()

            let trainPath = Cli.getOr "train" defaultTrainPath args
            let scorePathOpt = Cli.tryGet "score" args
            let outDir = Cli.getOr "out" (Path.Combine("runs", "fraud", "tx")) args
            let numShots = Cli.getIntOr "shots" 1000 args
            let seed = Cli.getIntOr "seed" 42 args
            let testFraction = Cli.getFloatOr "test-fraction" 0.2 args |> max 0.05 |> min 0.95
            let arch = Cli.getOr "arch" "hybrid" args |> parseArch

            Data.ensureDirectory outDir

            let runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss")
            Reporting.writeJson
                (Path.Combine(outDir, "run-config.json"))
                {| run_id = runId
                   utc = DateTimeOffset.UtcNow
                   train = trainPath
                   score = scorePathOpt
                   out = outDir
                   arch = arch.ToString()
                   shots = numShots
                   seed = seed
                   test_fraction = testFraction |}

            let trainSha = Data.fileSha256Hex trainPath

            let trainRows, trainErrors = Parsing.readTransactions trainPath

            let scoreRowsOpt, scoreErrors =
                match scorePathOpt with
                | None -> None, []
                | Some p ->
                    let rows, errs = Parsing.readTransactions p
                    Some rows, errs

            let allErrors = trainErrors @ scoreErrors
            if not allErrors.IsEmpty then
                Reporting.writeCsv (Path.Combine(outDir, "bad_rows.csv")) [ "error" ] (allErrors |> List.map (fun e -> [ e ]))

            let labeledTrain = trainRows |> List.choose (fun t -> t.Label |> Option.map (fun _ -> t)) |> List.toArray
            if labeledTrain.Length = 0 then
                Reporting.writeTextFile
                    (Path.Combine(outDir, "run-report.md"))
                    "# Transaction Fraud\n\nNo labeled training rows found (label column missing/empty).\n"
                2
            else
                let trainSet, testSet = Split.stratifiedHoldout seed testFraction labeledTrain

                let trainX = trainSet |> Array.map Transaction.toVector
                let trainY = trainSet |> Array.map Transaction.labelOrZero
                let testX = testSet |> Array.map Transaction.toVector
                let testY = testSet |> Array.map Transaction.labelOrZero

                let swTrain = Stopwatch.StartNew()
                let trained =
                    binaryClassification {
                        trainWith trainX trainY
                        architecture arch
                        shots numShots
                        maxEpochs 50
                        convergenceThreshold 0.001
                    }
                swTrain.Stop()

                let swEval = Stopwatch.StartNew()

                let evalResult, testScores, trainScores =
                    match trained with
                    | Error e -> Error e, [||], [||]
                    | Ok model ->
                        let metrics = BinaryClassifier.evaluate testX testY model

                        let toScores (xs: float array array) (ys: int array) =
                            xs
                            |> Array.mapi (fun i x ->
                                match BinaryClassifier.predict x model with
                                | Ok p -> p.Confidence, ys.[i]
                                | Error _ -> 0.0, ys.[i])

                        metrics, toScores testX testY, toScores trainX trainY

                swEval.Stop()

                let swScore = Stopwatch.StartNew()

                let scoresCsvRows, psiTrainVsScore =
                    match trained, scoreRowsOpt with
                    | Ok model, Some scoreRows ->
                        let rows =
                            scoreRows
                            |> List.map (fun t ->
                                match BinaryClassifier.predict (Transaction.toVector t) model with
                                | Error _ -> [ t.TransactionId; "0"; "0.0"; "ALLOW" ]
                                | Ok p ->
                                    let recText = Recommendation.ofPrediction p.IsPositive p.Confidence |> Recommendation.toString
                                    [ t.TransactionId; string p.Label; sprintf "%.6f" p.Confidence; recText ])

                        let expected = trainScores |> Array.map fst
                        let actual =
                            scoreRows
                            |> List.toArray
                            |> Array.choose (fun t ->
                                match BinaryClassifier.predict (Transaction.toVector t) model with
                                | Ok p -> Some p.Confidence
                                | Error _ -> None)

                        rows, Metrics.psi expected actual 10
                    | _ -> [], 0.0

                if not scoresCsvRows.IsEmpty then
                    Reporting.writeCsv
                        (Path.Combine(outDir, "scores.csv"))
                        [ "transaction_id"; "pred_label"; "confidence"; "recommendation" ]
                        scoresCsvRows

                swScore.Stop()

                match trained, evalResult with
                | Error e, _
                | _, Error e ->
                    Reporting.writeTextFile
                        (Path.Combine(outDir, "run-report.md"))
                        ("# Transaction Fraud\n\nTraining/evaluation failed: " + e.Message + "\n")
                    3
                | Ok _, Ok m ->
                    let auprc = Metrics.auprc testScores
                    let psiTrainVsTest = Metrics.psi (trainScores |> Array.map fst) (testScores |> Array.map fst) 10

                    swTotal.Stop()

                    let metrics: RunMetrics =
                        { run_id = runId
                          train_path = trainPath
                          train_sha256 = trainSha
                          arch = arch.ToString()
                          shots = numShots
                          seed = seed
                          test_fraction = testFraction
                          train_rows = trainSet.Length
                          train_pos_rate = posRate trainY
                          test_rows = testSet.Length
                          test_pos_rate = posRate testY
                          accuracy = m.Accuracy
                          precision = m.Precision
                          recall = m.Recall
                          f1 = m.F1Score
                          auprc = auprc
                          psi_score_train_vs_test = psiTrainVsTest
                          elapsed_ms_total = swTotal.ElapsedMilliseconds
                          elapsed_ms_train = swTrain.ElapsedMilliseconds
                          elapsed_ms_eval = swEval.ElapsedMilliseconds
                          elapsed_ms_score = swScore.ElapsedMilliseconds }

                    Reporting.writeJson (Path.Combine(outDir, "metrics.json")) metrics

                    let report =
                        $"""# Transaction Fraud Batch Scoring

This run trains a binary classifier and evaluates it on a holdout set.

## Inputs

- Train: `{trainPath}` (sha256: `{trainSha}`)

## Evaluation

- Architecture: {arch}
- Shots: {numShots}

Metrics:

- Accuracy:  {m.Accuracy:F4}
- Precision: {m.Precision:F4}
- Recall:    {m.Recall:F4}
- F1:        {m.F1Score:F4}
- AUPRC:     {auprc:F4}

Stability (PSI-style):

- PSI(score train vs test): {psiTrainVsTest:F4}
"""

                    Reporting.writeTextFile (Path.Combine(outDir, "run-report.md")) report

                    if scorePathOpt.IsSome then
                        Reporting.writeTextFile
                            (Path.Combine(outDir, "stability.md"))
                            ($"# Stability\n\nPSI(score train vs score batch): {psiTrainVsScore:F4}\n")

                    printfn "Wrote outputs to: %s" outDir
                    0
