(*
    Basic Fusion Example - Topological Quantum Computing
    ======================================================

    Demonstrates fundamental fusion rules of Ising anyons:
      sigma x sigma = 1 (vacuum) + psi (fermion)

    These are the building blocks of Microsoft's topological quantum
    computer using Majorana zero modes.

    Run with: dotnet fsi BasicFusion.fsx
              dotnet fsi BasicFusion.fsx -- --example 3 --trials 500
              dotnet fsi BasicFusion.fsx -- --quiet --output r.json --csv r.csv
*)

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
// The library comes from NuGet; `dotnet fsi --define:LOCAL_BUILD <script>` uses the repo's Debug build.
#if LOCAL_BUILD
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "../../src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"
#else
#r "nuget: FSharp.Azure.Quantum"
#r "nuget: FSharp.Azure.Quantum.Topological"
#endif
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"
#load "../_common/SvgAnimation.fsx"

open System
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Examples.Common
open SvgAnimation

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "BasicFusion.fsx"
    "Ising anyon fusion rules and measurement statistics"
    [
        {
            Name = "example"
            Description = "Which example: 1-4|all"
            Default = Some "all"
        }
        {
            Name = "trials"
            Description = "Fusion statistics trials"
            Default = Some "1000"
        }
        {
            Name = "output"
            Description = "Write results to JSON file"
            Default = None
        }
        {
            Name = "csv"
            Description = "Write results to CSV file"
            Default = None
        }
        {
            Name = "quiet"
            Description = "Suppress console output"
            Default = None
        }
    ]
    args

let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args
let exChoice = Cli.getOr "example" "all" args
let cliTrials = Cli.getIntOr "trials" 1000 args

let pr fmt =
    Printf.ksprintf
        (fun s ->
            if not quiet then
                printfn "%s" s)
        fmt

let shouldRun ex =
    exChoice = "all" || exChoice = string ex

let separator () = pr "%s" (String.replicate 60 "-")

// ---------------------------------------------------------------------------
// Quantum backend - topological IQuantumBackend
// ---------------------------------------------------------------------------
let quantumBackend = TopologicalUnifiedBackendFactory.createIsing 10

/// Helper: create a raw 2-sigma-anyon FusionTree.State.
/// This is the minimal "sigma x sigma" system for demonstrating
/// the Ising fusion rule: sigma x sigma = 1 (vacuum) + psi.
let createTwoSigmaState () =
    let tree =
        FusionTree.fuse
            (FusionTree.leaf AnyonSpecies.Particle.Sigma)
            (FusionTree.leaf AnyonSpecies.Particle.Sigma)
            AnyonSpecies.Particle.Vacuum // initial channel

    FusionTree.create tree AnyonSpecies.AnyonType.Ising

/// Two sigma pairs, each made from the vacuum, and one anyon from each pair
/// fused: the probabilities of vacuum and psi. F-moves change the basis until
/// the middle two anyons share a fusion node; measureFusion on that node,
/// weighted by |amplitude|^2, gives each outcome's probability.
let crossFusionProbabilities () =
    let sigma = AnyonSpecies.Particle.Sigma
    let vacuum = AnyonSpecies.Particle.Vacuum

    let pairFromNothing () =
        FusionTree.fuse (FusionTree.leaf sigma) (FusionTree.leaf sigma) vacuum

    let twoPairs =
        FusionTree.create
            (FusionTree.fuse (pairFromNothing ()) (pairFromNothing ()) vacuum)
            AnyonSpecies.AnyonType.Ising

    // ((s0 s1) (s2 s3)) -> s0 ((s1 s2) s3): the middle two anyons share a node.
    let middleFirst =
        (TopologicalOperations.fMove TopologicalOperations.LeftToRight 0 twoPairs).Terms
        |> List.collect (fun (a, st) ->
            (TopologicalOperations.fMove TopologicalOperations.RightToLeft 1 st).Terms
            |> List.map (fun (b, st2) -> (a * b, st2)))

    let probability particle =
        middleFirst
        |> List.sumBy (fun (amp, st) ->
            match TopologicalOperations.measureFusion 1 st with
            | Ok outcomes ->
                outcomes
                |> List.sumBy (fun (p, r) ->
                    if r.ClassicalOutcome = Some particle then
                        p * amp.Magnitude * amp.Magnitude
                    else
                        0.0)
            | Error _ -> 0.0)

    (probability vacuum, probability AnyonSpecies.Particle.Psi)

// Results accumulators
let mutable jsonResults: (string * obj) list = []
let mutable csvRows: string list list = []

// What the picture (--svg) draws from Examples 2, 3 and 4.
let mutable measuredOutcomes: (AnyonSpecies.Particle * float) list = []
let mutable crossSamples: AnyonSpecies.Particle[] option = None
let mutable qubitAnyons: int option = None

// ---------------------------------------------------------------------------
// Example 1 - Initialise Ising anyons
// ---------------------------------------------------------------------------
if shouldRun 1 then
    separator ()
    pr "EXAMPLE 1: Initialise 2 Ising anyons (sigma particles)"
    separator ()

    let state = createTwoSigmaState ()
    let superposition = TopologicalOperations.pureState state

    pr "Initialised 2 sigma anyons"
    pr "  Terms in superposition: %d" superposition.Terms.Length

    for (amp, fusionState) in superposition.Terms do
        pr "  Amplitude: %A   Tree: %A" amp fusionState.Tree

    jsonResults <-
        ("1_init",
         box
             {|
                 anyons = 2
                 terms = superposition.Terms.Length
             |})
        :: jsonResults

    csvRows <- [ "1_init"; "2"; string superposition.Terms.Length ] :: csvRows

// ---------------------------------------------------------------------------
// Example 2 - Fusion measurement (sigma x sigma = 1 + psi)
// ---------------------------------------------------------------------------
if shouldRun 2 then
    separator ()
    pr "EXAMPLE 2: Measure fusion of two sigma anyons"
    separator ()

    let state = createTwoSigmaState ()
    let result = TopologicalOperations.measureFusion 0 state

    match result with
    | Ok outcomes ->
        // measureFusion returns all possible outcomes with probabilities
        for (probability, opResult) in outcomes do
            match opResult.ClassicalOutcome with
            | Some outcome ->
                let outName =
                    match outcome with
                    | AnyonSpecies.Particle.Vacuum -> "vacuum (trivial)"
                    | AnyonSpecies.Particle.Psi -> "psi (fermion)"
                    | _ -> $"%A{outcome}"

                pr "Outcome: %s   (probability: %.4f)" outName probability
                measuredOutcomes <- measuredOutcomes @ [ (outcome, probability) ]

                jsonResults <-
                    ("2_measure",
                     box
                         {|
                             outcome = $"%A{outcome}"
                             probability = probability
                         |})
                    :: jsonResults

                csvRows <- [ "2_measure"; $"%A{outcome}"; $"%.4f{probability}" ] :: csvRows
            | None -> pr "Outcome: (no classical outcome)   (probability: %.4f)" probability

        pr ""
        pr "A pair made from the vacuum always fuses back to the vacuum. sigma x sigma"
        pr "= 1 + psi means either outcome is possible when the pair's total charge is"
        pr "not fixed: fusing one anyon from each of two pairs (Example 3)."
    | Error err -> pr "Measurement failed: %s" err.Message

// ---------------------------------------------------------------------------
// Example 3 - Fusion statistics
// ---------------------------------------------------------------------------
if shouldRun 3 then
    separator ()
    pr "EXAMPLE 3: Fusion statistics, one anyon from each of two pairs (%d trials)" cliTrials
    separator ()

    // Two pairs, each made from the vacuum; fusing one anyon from each pair
    // leaves the outcome open. measureFusion gives the probabilities (Born rule
    // from quantum dimensions); the trials sample from them.
    let pVacuum, pPsi = crossFusionProbabilities ()
    pr "Probabilities: vacuum %.4f, psi %.4f" pVacuum pPsi

    if pVacuum + pPsi <= 0.0 then
        pr "Measurement setup failed: no fusion outcome"
    else
        let rng = Random()

        let samples =
            Array.init cliTrials (fun _ ->
                if rng.NextDouble() * (pVacuum + pPsi) < pVacuum then
                    AnyonSpecies.Particle.Vacuum
                else
                    AnyonSpecies.Particle.Psi)

        let vacCount =
            samples |> Array.filter ((=) AnyonSpecies.Particle.Vacuum) |> Array.length

        let psiCount = cliTrials - vacCount
        let vacPct = float vacCount / float cliTrials * 100.0
        let psiPct = float psiCount / float cliTrials * 100.0
        crossSamples <- Some samples
        pr "Vacuum (1): %d times (%.1f%%)" vacCount vacPct
        pr "Psi    (ψ): %d times (%.1f%%)" psiCount psiPct
        pr ""
        pr "Expected: ~50%% vacuum, ~50%% psi  (from sigma x sigma = 1 + psi)"

        jsonResults <-
            ("3_stats",
             box
                 {|
                     trials = cliTrials
                     vacuum = vacCount
                     psi = psiCount
                     vacuumPct = vacPct
                     psiPct = psiPct
                 |})
            :: jsonResults

        csvRows <-
            [
                "3_stats"
                string vacCount
                string psiCount
                $"%.1f{vacPct}"
                $"%.1f{psiPct}"
            ]
            :: csvRows

// ---------------------------------------------------------------------------
// Example 4 - Four anyons (2-qubit equivalent)
// ---------------------------------------------------------------------------
if shouldRun 4 then
    separator ()
    pr "EXAMPLE 4: Four Ising anyons (2-qubit equivalent)"
    separator ()

    // Use the unified backend: InitializeState 2 creates a 2-qubit
    // topological state encoded in Ising sigma-pairs (+ parity ancilla).
    match quantumBackend.InitializeState 2 with
    | Ok(QuantumState.FusionSuperposition fs) ->
        match TopologicalOperations.fromInterface fs with
        | Some superposition ->
            let numAnyons =
                match superposition.Terms with
                | (_, firstState) :: _ -> FusionTree.leaves firstState.Tree |> List.length
                | [] -> 0

            pr "Initialised 2-qubit state (%d sigma anyons in encoding)" numAnyons
            qubitAnyons <- Some numAnyons
            pr "  Terms in superposition: %d" superposition.Terms.Length

            for (amp, _) in superposition.Terms do
                pr "  Amplitude: %A" amp

            jsonResults <-
                ("4_four_anyons",
                 box
                     {|
                         qubits = 2
                         anyons = numAnyons
                         terms = superposition.Terms.Length
                     |})
                :: jsonResults

            csvRows <-
                [ "4_four_anyons"; string numAnyons; string superposition.Terms.Length ]
                :: csvRows
        | None -> pr "Failed: could not unwrap FusionSuperposition"
    | Ok other -> pr "Unexpected state type: %A" other
    | Error err -> pr "Failed: %s" err.Message

// ---------------------------------------------------------------------------
// Output
// ---------------------------------------------------------------------------
match outputPath with
| Some outputPathValue ->
    let payload =
        {|
            script = "BasicFusion.fsx"
            backend = "Topological (Ising)"
            timestamp = DateTime.UtcNow.ToString("o")
            example = exChoice
            trials = cliTrials
            results = jsonResults |> List.rev |> List.map (fun (k, v) -> {| key = k; value = v |})
        |}

    Reporting.writeJson outputPathValue payload
| None -> ()

match csvPath with
| Some v ->
    let header = [ "example"; "detail1"; "detail2"; "detail3"; "detail4" ]
    Reporting.writeCsv v header (csvRows |> List.rev)
| None -> ()

// ---------------------------------------------------------------------------
// Picture (--svg [path]): fusing one pair, then one anyon from each of two pairs
// ---------------------------------------------------------------------------
// Example 2 supplies the one-pair outcome, Example 3 the two-pair fusion's
// trials and Example 4 the anyon count of two qubits; the one-pair repeats are
// sampled here from Example 2's probabilities.
match svgPath (IO.Path.Combine(__SOURCE_DIRECTORY__, "_images", "basic-fusion.svg")) with
| None -> ()
| Some path ->
    match measuredOutcomes, crossSamples, qubitAnyons with
    | [], _, _
    | _, None, _
    | _, _, None -> printfn "The picture needs Examples 2, 3 and 4: run with --example all."
    | _, Some samples, Some numAnyons ->
        let vacuum = AnyonSpecies.Particle.Vacuum
        let psi = AnyonSpecies.Particle.Psi
        let crossVac, crossPsi = crossFusionProbabilities ()

        let onePair particle =
            measuredOutcomes |> List.sumBy (fun (o, p) -> if o = particle then p else 0.0)

        let oneVac, onePsi = onePair vacuum, onePair psi

        // The one-pair repeats, sampled from Example 2's probabilities; the
        // two-pair ones are Example 3's trials.
        let n = max 1 samples.Length
        let rng = Random()

        let vacCount =
            Seq.init n (fun _ -> rng.NextDouble() * (oneVac + onePsi) < oneVac)
            |> Seq.filter id
            |> Seq.length

        let psiCount = n - vacCount

        let checkpoints = [| max 1 (n / 100); max 1 (n / 10); n |]

        let countsAt c =
            let v = samples.[.. c - 1] |> Array.filter ((=) vacuum) |> Array.length
            (v, c - v)

        let name o = if o = vacuum then "1" else "ψ"

        let outcomeColour o =
            if o = vacuum then colour 0 else colour 1

        let pct (p: float) = sprintf "%.0f%%" (100.0 * p)

        let times c =
            if c = 1 then "1 time" else $"%d{c} times"

        // Frames: 0-2 one pair, 3-4 two pairs, 5-7 repeated fusions, 8 two qubits.
        let firstRepeat = 5
        let frames = firstRepeat + checkpoints.Length + 1

        let pic =
            Picture(760.0, 400.0, frames, 2.0 * float frames, "Anyon fusion", hold = 0.65)

        let isRepeat k =
            k >= firstRepeat && k < firstRepeat + checkpoints.Length

        let fusedShown k =
            k = 1 || k = 2 || (k >= 4 && k < frames - 1)

        // Like pic.FrameText, but an empty label draws no element at all.
        let frameLabels
            (x: float)
            (y: float)
            (labels: string[])
            (size: float)
            (fill: string)
            (bold: bool)
            (anchor: string)
            =
            labels
            |> Array.iteri (fun i label ->
                if label <> "" then
                    pic.Element(
                        "text",
                        [
                            "x", num x
                            "y", num y
                            "font-size", num size
                            "fill", fill
                            "font-weight", (if bold then "bold" else "normal")
                            "text-anchor", anchor
                        ],
                        animate =
                            [
                                ("visibility", Array.init frames (fun k -> if k = i then "visible" else "hidden"))
                            ],
                        text = label
                    ))

        pic.Text(20.0, 30.0, "Anyon fusion: 1 or ψ", size = 18.0, bold = true)

        pic.Text(
            20.0,
            50.0,
            "Two anyons fuse into 1 (nothing) or ψ (a fermion). Which one depends on how they were paired.",
            size = 12.5,
            fill = grey
        )

        let steps =
            Array.concat
                [
                    [|
                        "1. Make a pair of σ anyons from empty space"
                        "2. Bring the two back together and fuse them"
                        $"3. Repeat that %d{n} times"
                        "4. Now make two pairs from empty space"
                        "5. Fuse one anyon from each pair"
                    |]
                    checkpoints |> Array.map (fun c -> $"6. Repeat: fusion %d{c} of %d{n}")
                    [| $"7. Two qubits use %d{numAnyons} anyons" |]
                ]

        let details =
            Array.concat
                [
                    [|
                        "A pair made from nothing carries total charge 1, which means nothing."
                        sprintf
                            "It fuses back into 1 with probability %s, and into ψ with %s."
                            (pct oneVac)
                            (pct onePsi)
                        sprintf
                            "%s 1 and %s ψ: the pair remembers that it came from nothing."
                            (times vacCount)
                            (times psiCount)
                        "Each pair still carries total charge 1 on its own."
                        sprintf
                            "These two share no history: 1 with probability %s, ψ with %s."
                            (pct crossVac)
                            (pct crossPsi)
                    |]
                    checkpoints
                    |> Array.map (fun c ->
                        let v, p = countsAt c
                        sprintf "Fusion %d gave %s. So far %s 1 and %s ψ." c (name samples.[c - 1]) (times v) (times p))
                    [|
                        "One pair per qubit, plus one pair that keeps the total charge even. How a pair fuses is its bit."
                    |]
                ]

        pic.FrameText(20.0, 80.0, steps, size = 14.0, bold = true)
        pic.FrameText(20.0, 99.0, details, size = 12.0, fill = grey)

        // Left: the anyons on a line.
        let lineY = 200.0
        let centre = 210.0
        pic.Rect(20.0, 115.0, 370.0, 220.0, fill = panel, stroke = frameColour, rx = 6.0)
        pic.Text(35.0, 137.0, "Anyons on a line", size = 12.0, fill = grey)
        pic.Line(40.0, lineY, 370.0, lineY, stroke = light, width = 2.0)
        pic.Text(35.0, 318.0, "σ: one anyon.  1: nothing is left.  ψ: one fermion is left.", size = 11.0, fill = grey)

        let posA = [| 150.0; 270.0 |]
        let posB = [| 70.0; 160.0; 260.0; 350.0 |]
        let posC = [| 55.0; 110.0; 175.0; 230.0; 295.0; 350.0 |]

        // Where dot i sits in frame k, and whether it shows.
        let dotAt i k =
            match k with
            | 0 ->
                if i < 2 then posA.[i], 1.0
                elif i < 4 then posB.[i], 0.0
                else posC.[i], 0.0
            | 1
            | 2 ->
                if i < 2 then centre, 0.0
                elif i < 4 then posB.[i], 0.0
                else posC.[i], 0.0
            | 3 -> if i < 4 then posB.[i], 1.0 else posC.[i], 0.0
            | k when k < frames - 1 ->
                if i = 1 || i = 2 then centre, 0.0
                elif i < 4 then posB.[i], 1.0
                else posC.[i], 0.0
            | _ -> posC.[i], 1.0

        let arcPath (x1: float) (x2: float) =
            sprintf
                "M %s %s Q %s %s %s %s"
                (num x1)
                (num (lineY + 18.0))
                (num ((x1 + x2) / 2.0))
                (num (lineY + 50.0))
                (num x2)
                (num (lineY + 18.0))

        let shownIn (ks: int list) =
            Array.init frames (fun k -> if List.contains k ks then 1.0 else 0.0)

        let pairArc (x1: float) (x2: float) (label: string) (shown: float[]) =
            pic.Element(
                "path",
                [
                    "d", arcPath x1 x2
                    "stroke", grey
                    "stroke-width", "1.5"
                    "fill", "none"
                    "stroke-linecap", "round"
                ],
                animate = [ ("opacity", shown |> Array.map num) ]
            )

            pic.Text(
                (x1 + x2) / 2.0,
                lineY + 56.0,
                label,
                size = 11.0,
                fill = grey,
                anchor = "middle",
                animate = [ ("opacity", shown) ]
            )

        let repeats = [ firstRepeat .. firstRepeat + checkpoints.Length - 1 ]
        pairArc posA.[0] posA.[1] "a pair from nothing" (shownIn [ 0 ])

        let twoPairFrames =
            Array.init frames (fun k ->
                if k = 3 then 1.0
                elif k = 4 || List.contains k repeats then 0.45
                else 0.0)

        pairArc posB.[0] posB.[1] "a pair from nothing" twoPairFrames
        pairArc posB.[2] posB.[3] "a pair from nothing" twoPairFrames

        let last = frames - 1
        pairArc posC.[0] posC.[1] "qubit 0" (shownIn [ last ])
        pairArc posC.[2] posC.[3] "qubit 1" (shownIn [ last ])
        pairArc posC.[4] posC.[5] "parity" (shownIn [ last ])

        let dotColour i =
            [| colour 3; colour 5; colour 8 |].[i / 2]

        for i in 0..5 do
            let xs = Array.init frames (dotAt i >> fst)
            let shown = Array.init frames (dotAt i >> snd)
            pic.Circle(xs.[0], lineY, 12.0, fill = dotColour i, animate = [ "cx", xs; "opacity", shown ])

            pic.Text(
                xs.[0],
                lineY + 4.5,
                "σ",
                size = 13.0,
                fill = "white",
                bold = true,
                anchor = "middle",
                animate = [ "x", xs; "opacity", shown ]
            )

        // The fusion result, where the fused anyons met.
        let bubbleOutcome k =
            if k = 1 || k = 2 then
                Some(fst (measuredOutcomes |> List.maxBy snd))
            elif isRepeat k then
                Some samples.[checkpoints.[k - firstRepeat] - 1]
            else
                None

        pic.Text(
            centre,
            lineY - 26.0,
            "fused",
            size = 11.0,
            fill = grey,
            anchor = "middle",
            animate = [ ("opacity", Array.init frames (fun k -> if fusedShown k then 1.0 else 0.0)) ]
        )

        pic.Element(
            "circle",
            [
                "cx", num centre
                "cy", num lineY
                "r", "17"
                "stroke", "white"
                "stroke-width", "2"
            ],
            animate =
                [
                    "opacity", Array.init frames (fun k -> if fusedShown k then "1" else "0")
                    "fill",
                    Array.init frames (fun k ->
                        match bubbleOutcome k with
                        | Some o -> outcomeColour o
                        | None -> grey)
                ]
        )

        frameLabels
            centre
            (lineY + 6.0)
            (Array.init frames (fun k ->
                match bubbleOutcome k with
                | Some o -> name o
                | None when fusedShown k -> "?"
                | None -> ""))
            17.0
            "white"
            true
            "middle"

        // Right: the chance of each outcome, and the outcomes counted so far.
        pic.Rect(405.0, 115.0, 335.0, 220.0, fill = panel, stroke = frameColour, rx = 6.0)
        pic.Text(420.0, 137.0, "Chance of each result", size = 12.0, fill = grey)
        let barX, barW = 500.0, 170.0
        let rows = [| (vacuum, "1  nothing"); (psi, "ψ  fermion") |]

        let chance k o =
            if k = 1 || k = 2 then
                Some(if o = vacuum then oneVac else onePsi)
            elif k >= 4 && k < last then
                Some(if o = vacuum then crossVac else crossPsi)
            else
                None

        let counts k =
            if k = 2 then
                Some(vacCount, psiCount, vacCount + psiCount)
            elif isRepeat k then
                let c = checkpoints.[k - firstRepeat]
                let v, p = countsAt c
                Some(v, p, c)
            else
                None

        let bar (y: float) (fill: string) (widths: float[]) (labels: string[]) (label: string) =
            pic.Text(420.0, y + 5.0, label, size = 12.5)
            pic.Rect(barX, y - 8.0, barW, 16.0, fill = light, rx = 3.0)
            pic.Rect(barX, y - 8.0, 0.0, 16.0, fill = fill, rx = 3.0, animate = [ ("width", widths) ])
            frameLabels 728.0 (y + 5.0) labels 12.5 ink false "end"

        rows
        |> Array.iteri (fun r (o, label) ->
            let chances = Array.init frames (fun k -> chance k o)

            bar
                (162.0 + 28.0 * float r)
                (outcomeColour o)
                (chances |> Array.map (fun p -> barW * defaultArg p 0.0))
                (chances |> Array.map (Option.map pct >> Option.defaultValue ""))
                label)

        pic.FrameText(
            420.0,
            237.0,
            Array.init frames (fun k ->
                match counts k with
                | Some(_, _, total) -> $"Results of %d{total} fusions"
                | None -> "Results of repeated fusions"),
            size = 12.0,
            fill = grey
        )

        rows
        |> Array.iteri (fun r (o, label) ->
            let tally =
                Array.init frames (fun k ->
                    counts k
                    |> Option.map (fun (v, p, total) -> ((if o = vacuum then v else p), total)))

            bar
                (262.0 + 28.0 * float r)
                (tint 0.35 (outcomeColour o))
                (tally
                 |> Array.map (Option.map (fun (c, t) -> barW * float c / float t) >> Option.defaultValue 0.0))
                (tally |> Array.map (Option.map (fst >> string) >> Option.defaultValue ""))
                label)

        pic.Progress(20.0, 352.0, 720.0)

        pic.Text(
            20.0,
            382.0,
            "Computed in this run of BasicFusion.fsx (Ising anyons): Examples 2 to 4, plus the two-pair fusion via F-moves.",
            size = 11.0,
            fill = grey
        )

        pic.Save path

// ---------------------------------------------------------------------------
// Usage hints
// ---------------------------------------------------------------------------
if not quiet && argv.Length = 0 then
    pr ""
    pr "Usage hints:"
    pr "  dotnet fsi BasicFusion.fsx -- --example 3 --trials 500"
    pr "  dotnet fsi BasicFusion.fsx -- --quiet --output r.json --csv r.csv"
    pr "  dotnet fsi BasicFusion.fsx -- --help"
