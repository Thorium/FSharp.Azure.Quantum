namespace FSharp.Azure.Quantum.Examples.DrugDiscovery.MolecularSimilarity

open System
open System.Diagnostics
open System.IO

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Data
open FSharp.Azure.Quantum.MachineLearning

open FSharp.Azure.Quantum.Examples.Common

type Compound = { CompoundId: string; Smiles: string }

module private Csv =
    let tryGetRequired (name: string) (row: Data.CsvRow) =
        row.Values |> Map.tryFind name |> Option.map (fun s -> s.Trim()) |> Option.filter (fun s -> s <> "")

    let readCompounds (path: string) : Compound list * (string list) =
        let rows, structuralErrors = Data.readCsvWithHeaderWithErrors path
        let compounds, bad =
            rows
            |> List.mapi (fun i row ->
                match tryGetRequired "compound_id" row, tryGetRequired "smiles" row with
                | Some id, Some smiles -> Ok { CompoundId = id; Smiles = smiles }
                | _ -> Error (sprintf "row=%d missing compound_id/smiles" (i + 2)))
            |> List.fold
                (fun (goods, bads) r ->
                    match r with
                    | Ok v -> (v :: goods, bads)
                    | Error e -> (goods, e :: bads))
                ([], [])

        (List.rev compounds, structuralErrors @ (List.rev bad))

module private Features =
    let fingerprintBitsDefault = 1024

    let extractForKernel (desc: MolecularData.MolecularDescriptors) : float array =
        // Keep feature engineering explicit and bounded for stable encoding.
        [|
            desc.MolecularWeight / 600.0 |> min 1.0
            (desc.LogP + 3.0) / 10.0 |> max 0.0 |> min 1.0
            float desc.HydrogenBondDonors / 5.0 |> min 1.0
            float desc.HydrogenBondAcceptors / 10.0 |> min 1.0
            desc.TPSA / 150.0 |> min 1.0
            float desc.RotatableBonds / 12.0 |> min 1.0
            desc.FractionCsp3 |> min 1.0
            float desc.AromaticRingCount / 4.0 |> min 1.0
        |]

type ParsedMolecule =
    { Compound: Compound
      Molecule: MolecularData.Molecule
      Descriptors: MolecularData.MolecularDescriptors
      Fingerprint: MolecularData.MolecularFingerprint
      KernelFeatures: float array }

module private Screening =
    let parseAndFeaturize (fingerprintBits: int) (compound: Compound) : Result<ParsedMolecule, string> =
        match MolecularData.parseSmiles compound.Smiles with
        | Ok mol ->
            let desc = MolecularData.calculateDescriptors mol
            let fp = MolecularData.generateFingerprint mol fingerprintBits
            let feats = Features.extractForKernel desc
            Ok
                { Compound = compound
                  Molecule = mol
                  Descriptors = desc
                  Fingerprint = fp
                  KernelFeatures = feats }
        | Error e -> Error (sprintf "%s (%s)" compound.CompoundId e.Message)

    let averageTanimoto (actives: ParsedMolecule array) (candidateFp: MolecularData.MolecularFingerprint) =
        actives
        |> Array.map (fun a -> MolecularData.tanimotoSimilarity candidateFp a.Fingerprint)
        |> Array.average

    let quantumKernelSimilarity
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (shots: int)
        (x1: float array)
        (x2: float array)
        =
        let data = [| x1; x2 |]
        match QuantumKernels.computeKernelMatrix backend featureMap data shots with
        | Ok m -> m.[0, 1]
        | Error _ -> 0.0

    let averageQuantum
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (shots: int)
        (actives: ParsedMolecule array)
        (candidate: ParsedMolecule)
        =
        actives
        |> Array.map (fun a -> quantumKernelSimilarity backend featureMap shots candidate.KernelFeatures a.KernelFeatures)
        |> Array.average

type Metrics =
    { run_id: string
      library_path: string
      actives_path: string
      library_sha256: string
      actives_sha256: string
      parsed_library: int
      parsed_actives: int
      failed_library: int
      failed_actives: int
      fingerprint_bits: int
      quantum_shots: int
      feature_map: string
      top_n: int
      similarity_threshold: float
      classical_hits: int
      quantum_hits: int
      elapsed_ms_total: int64
      elapsed_ms_classical: int64
      elapsed_ms_quantum: int64 }

module Program =
    [<EntryPoint>]
    let main argv =
        let args = Cli.parse argv
        if Cli.hasFlag "help" args || Cli.hasFlag "h" args then
            printfn "MolecularSimilarity"
            printfn "  --library <path>   (CSV: compound_id,smiles)"
            printfn "  --actives <path>   (CSV: compound_id,smiles)"
            printfn "  --out <dir>        (output folder)"
            printfn "  --top <n>          (default: 5)"
            printfn "  --shots <n>        (default: 1000)"
            printfn "  --fingerprint-bits <n> (default: 1024)"
            printfn "  --threshold <x>    (default: 0.7)"
            0
        else
            let swTotal = Stopwatch.StartNew()

            let libraryPath = Cli.getOr "library" "examples/DrugDiscovery/_data/library_tiny.csv" args
            let activesPath = Cli.getOr "actives" "examples/DrugDiscovery/_data/actives_tiny.csv" args
            let outDir = Cli.getOr "out" (Path.Combine("runs", "drugdiscovery", "molsim")) args

            let topN = Cli.getIntOr "top" 5 args
            let shots = Cli.getIntOr "shots" 1000 args
            let fingerprintBits = Cli.getIntOr "fingerprint-bits" Features.fingerprintBitsDefault args
            let threshold = Cli.getFloatOr "threshold" 0.7 args
            let featureMap = FeatureMapType.ZZFeatureMap 2

            Data.ensureDirectory outDir

            let runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss")
            let runConfig =
                {| run_id = runId
                   utc = DateTimeOffset.UtcNow
                   library = libraryPath
                   actives = activesPath
                   out = outDir
                   top = topN
                   shots = shots
                   fingerprint_bits = fingerprintBits
                   threshold = threshold
                   feature_map = featureMap.ToString() |}

            Reporting.writeJson (Path.Combine(outDir, "run-config.json")) runConfig

            let librarySha = Data.fileSha256Hex libraryPath
            let activesSha = Data.fileSha256Hex activesPath

            let libraryCompounds, libraryBadSchema = Csv.readCompounds libraryPath
            let activesCompounds, activesBadSchema = Csv.readCompounds activesPath

            let parse compounds =
                compounds
                |> List.map (Screening.parseAndFeaturize fingerprintBits)
                |> List.fold
                    (fun (oks, errs) r ->
                        match r with
                        | Ok v -> (v :: oks, errs)
                        | Error e -> (oks, e :: errs))
                    ([], [])
                |> fun (oks, errs) -> (List.rev oks |> List.toArray, List.rev errs)

            let actives, activesParseErrors = parse activesCompounds
            let library, libraryParseErrors = parse libraryCompounds

            let badRows =
                (libraryBadSchema @ activesBadSchema)
                @ (activesParseErrors |> List.map (fun e -> "actives: " + e))
                @ (libraryParseErrors |> List.map (fun e -> "library: " + e))

            if not badRows.IsEmpty then
                Reporting.writeCsv
                    (Path.Combine(outDir, "bad_rows.csv"))
                    [ "error" ]
                    (badRows |> List.map (fun e -> [ e ]))

            if actives.Length = 0 || library.Length = 0 then
                Reporting.writeTextFile
                    (Path.Combine(outDir, "run-report.md"))
                    "# Molecular Similarity\n\nNo actives or no library molecules parsed; see bad_rows.csv.\n"
                2
            else
                let swClassical = Stopwatch.StartNew()
                let classicalResults =
                    library
                    |> Array.map (fun c ->
                        let s = Screening.averageTanimoto actives c.Fingerprint
                        (c, s))
                    |> Array.sortByDescending snd
                swClassical.Stop()

                let classicalHits = classicalResults |> Array.filter (fun (_, s) -> s >= threshold) |> Array.length

                let swQuantum = Stopwatch.StartNew()
                let backend = LocalBackend() :> IQuantumBackend
                let quantumResults =
                    library
                    |> Array.map (fun c ->
                        let s = Screening.averageQuantum backend featureMap shots actives c
                        (c, s))
                    |> Array.sortByDescending snd
                swQuantum.Stop()

                let quantumHits = quantumResults |> Array.filter (fun (_, s) -> s >= threshold) |> Array.length

                let toCsvRows (results: (ParsedMolecule * float) array) =
                    results
                    |> Array.truncate topN
                    |> Array.toList
                    |> List.map (fun (c, sim) ->
                        [ c.Compound.CompoundId
                          c.Compound.Smiles
                          sprintf "%.6f" sim
                          sprintf "%.2f" c.Descriptors.MolecularWeight
                          sprintf "%.3f" c.Descriptors.LogP
                          string c.Descriptors.HydrogenBondDonors
                          string c.Descriptors.HydrogenBondAcceptors
                          sprintf "%.2f" c.Descriptors.TPSA ])

                let header =
                    [ "compound_id"
                      "smiles"
                      "avg_similarity"
                      "mw"
                      "logp"
                      "hbd"
                      "hba"
                      "tpsa" ]

                Reporting.writeCsv (Path.Combine(outDir, "neighbors_classical.csv")) header (toCsvRows classicalResults)
                Reporting.writeCsv (Path.Combine(outDir, "neighbors_quantum.csv")) header (toCsvRows quantumResults)

                swTotal.Stop()

                let metrics: Metrics =
                    { run_id = runId
                      library_path = libraryPath
                      actives_path = activesPath
                      library_sha256 = librarySha
                      actives_sha256 = activesSha
                      parsed_library = library.Length
                      parsed_actives = actives.Length
                      failed_library = libraryParseErrors.Length + libraryBadSchema.Length
                      failed_actives = activesParseErrors.Length + activesBadSchema.Length
                      fingerprint_bits = fingerprintBits
                      quantum_shots = shots
                      feature_map = featureMap.ToString()
                      top_n = topN
                      similarity_threshold = threshold
                      classical_hits = classicalHits
                      quantum_hits = quantumHits
                      elapsed_ms_total = swTotal.ElapsedMilliseconds
                      elapsed_ms_classical = swClassical.ElapsedMilliseconds
                      elapsed_ms_quantum = swQuantum.ElapsedMilliseconds }

                Reporting.writeJson (Path.Combine(outDir, "metrics.json")) metrics

                let report =
                    $"""# Molecular Similarity Screening

This run screens a candidate library against a set of known actives using:

- Classical baseline: fingerprint + Tanimoto similarity
- Quantum method: kernel similarity over molecular descriptors (local backend)

## Inputs

- Library: `{libraryPath}` (sha256: `{librarySha}`)
- Actives: `{activesPath}` (sha256: `{activesSha}`)

## Parsing

- Parsed library: {library.Length}
- Parsed actives: {actives.Length}

## Results

- Classical hits (avg similarity >= {threshold:F2}): {classicalHits}
- Quantum hits (avg similarity >= {threshold:F2}): {quantumHits}

## Outputs

- `neighbors_classical.csv` (top {topN})
- `neighbors_quantum.csv` (top {topN})
- `metrics.json`
"""

                Reporting.writeTextFile (Path.Combine(outDir, "run-report.md")) report

                printfn "Wrote outputs to: %s" outDir
                0
