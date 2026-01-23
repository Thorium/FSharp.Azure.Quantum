namespace FSharp.Azure.Quantum.Examples.Fraud.TransactionFraudBatchScoring

type Transaction =
    { TransactionId: string
      Amount: float
      Hour: float
      MerchantCategory: float
      DistanceKm: float
      TxnCount24h: float
      Label: int option }

type Recommendation =
    | Allow
    | Review
    | Block

module Transaction =
    let toVector (t: Transaction) : float array =
        [| t.Amount; t.Hour; t.MerchantCategory; t.DistanceKm; t.TxnCount24h |]

    let labelOrZero (t: Transaction) =
        t.Label |> Option.defaultValue 0

module Recommendation =
    let ofPrediction (isPositive: bool) (confidence: float) : Recommendation =
        if isPositive && confidence >= 0.8 then Block
        elif isPositive && confidence >= 0.5 then Review
        else Allow

    let toString = function
        | Allow -> "ALLOW"
        | Review -> "REVIEW"
        | Block -> "BLOCK"
