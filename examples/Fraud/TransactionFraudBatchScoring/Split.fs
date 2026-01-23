namespace FSharp.Azure.Quantum.Examples.Fraud.TransactionFraudBatchScoring

open System

module Split =
    let private shuffleInPlace (rng: Random) (arr: 'T array) =
        for i = arr.Length - 1 downto 1 do
            let j = rng.Next(i + 1)
            let tmp = arr.[i]
            arr.[i] <- arr.[j]
            arr.[j] <- tmp

    let stratifiedHoldout (seed: int) (testFraction: float) (xs: Transaction array) : Transaction array * Transaction array =
        let rng = Random(seed)

        let byLabel =
            xs
            |> Array.groupBy Transaction.labelOrZero
            |> Array.map (fun (_, group) ->
                let copy = Array.copy group
                shuffleInPlace rng copy
                let testCount = int (Math.Round(testFraction * float copy.Length)) |> max 1 |> min (copy.Length - 1)
                let test = copy |> Array.take testCount
                let train = copy |> Array.skip testCount
                (train, test))

        let train = byLabel |> Array.collect fst
        let test = byLabel |> Array.collect snd
        shuffleInPlace rng train
        shuffleInPlace rng test
        (train, test)
