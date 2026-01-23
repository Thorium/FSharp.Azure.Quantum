namespace FSharp.Azure.Quantum.Examples.Fraud.TransactionFraudBatchScoring

open System

open FSharp.Azure.Quantum.Examples.Common

module Parsing =
    type ParseError = string

    let private tryGet (k: string) (row: Data.CsvRow) =
        row.Values |> Map.tryFind k |> Option.map (fun s -> s.Trim())

    let private required (rowNum: int) (name: string) (row: Data.CsvRow) : Result<string, ParseError> =
        match tryGet name row with
        | Some v when v <> "" -> Ok v
        | _ -> Error (sprintf "row=%d missing %s" rowNum name)

    let private parseFloat (rowNum: int) (name: string) (s: string) : Result<float, ParseError> =
        match Double.TryParse s with
        | true, x -> Ok x
        | false, _ -> Error (sprintf "row=%d invalid %s" rowNum name)

    let private parseIntOpt (rowNum: int) (name: string) (row: Data.CsvRow) : Result<int option, ParseError> =
        match tryGet name row with
        | None
        | Some "" -> Ok None
        | Some s ->
            match Int32.TryParse s with
            | true, x -> Ok (Some x)
            | false, _ -> Error (sprintf "row=%d invalid %s" rowNum name)

    let private parseRow (rowNum: int) (row: Data.CsvRow) : Result<Transaction, ParseError> =
        match required rowNum "transaction_id" row with
        | Error e -> Error e
        | Ok txId ->
            match required rowNum "amount" row |> Result.bind (parseFloat rowNum "amount") with
            | Error e -> Error e
            | Ok amount ->
                match required rowNum "hour" row |> Result.bind (parseFloat rowNum "hour") with
                | Error e -> Error e
                | Ok hour ->
                    match required rowNum "merchant_category" row |> Result.bind (parseFloat rowNum "merchant_category") with
                    | Error e -> Error e
                    | Ok cat ->
                        match required rowNum "distance_km" row |> Result.bind (parseFloat rowNum "distance_km") with
                        | Error e -> Error e
                        | Ok dist ->
                            match required rowNum "txn_count_24h" row |> Result.bind (parseFloat rowNum "txn_count_24h") with
                            | Error e -> Error e
                            | Ok cnt ->
                                match parseIntOpt rowNum "label" row with
                                | Error e -> Error e
                                | Ok label ->
                                    Ok
                                        { TransactionId = txId
                                          Amount = amount
                                          Hour = hour
                                          MerchantCategory = cat
                                          DistanceKm = dist
                                          TxnCount24h = cnt
                                          Label = label }

    let readTransactions (path: string) : Transaction list * ParseError list =
        let rows, structuralErrors = Data.readCsvWithHeaderWithErrors path

        let parsed, rowErrors =
            rows
            |> List.mapi (fun i row -> parseRow (i + 2) row)
            |> List.fold
                (fun (oks, errs) r ->
                    match r with
                    | Ok v -> (v :: oks, errs)
                    | Error e -> (oks, e :: errs))
                ([], [])

        (List.rev parsed, structuralErrors @ (List.rev rowErrors))
