(*
    Bell State Creation - Topological Quantum Computing
    =====================================================

    Creates entangled Bell state via braiding operations instead
    of quantum gates. Braiding creates entanglement geometrically
    and is topologically protected (immune to local noise).

    Run with: dotnet fsi BellState.fsx
              dotnet fsi BellState.fsx -- --example 2 --trials 200
              dotnet fsi BellState.fsx -- --quiet --output r.json --csv r.csv
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
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Examples.Common
open SvgAnimation

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "BellState.fsx"
    "Bell state creation via topological braiding operations"
    [
        {
            Name = "example"
            Description = "Which example: 1-3|all"
            Default = Some "all"
        }
        {
            Name = "trials"
            Description = "Correlation test trials"
            Default = Some "100"
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
let cliTrials = Cli.getIntOr "trials" 100 args

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
// Quantum backend (Rule 1) - topological IQuantumBackend
// ---------------------------------------------------------------------------
let quantumBackend = TopologicalUnifiedBackendFactory.createIsing 10

// Results accumulators
let mutable jsonResults: (string * obj) list = []
let mutable csvRows: string list list = []

// ---------------------------------------------------------------------------
// Example 1 - Create Bell state via braiding
// ---------------------------------------------------------------------------
// Two qubits are six sigma anyons in three pairs: qubit 0, qubit 1, and a pair
// that keeps the total parity. Braids 0 and 2 exchange the two anyons inside a
// qubit's pair and only turn a phase; braid 1 exchanges anyons of two
// different pairs, and that one entangles the qubits.
let bellBraids = [ 0; 2; 1 ]

/// The state after the braids, or why it could not be made.
let bellState () =
    bellBraids
    |> List.fold
        (fun state b -> state |> Result.bind (quantumBackend.ApplyOperation(QuantumOperation.Braid b)))
        (quantumBackend.InitializeState 2)
    |> Result.mapError (fun e -> e.Message)
    |> Result.bind (fun state ->
        match state with
        | FSharp.Azure.Quantum.Core.QuantumState.FusionSuperposition fs ->
            match TopologicalOperations.fromInterface fs with
            | Some superposition -> Ok superposition
            | None -> Error "the backend returned a state that is not a fusion superposition"
        | other -> Error $"unexpected state type %A{other}")

let bellBitstrings = [ [| 0; 0 |]; [| 0; 1 |]; [| 1; 0 |]; [| 1; 1 |] ]

let bitText (bits: int[]) =
    bits |> Array.map string |> String.concat ""

if shouldRun 1 then
    separator ()
    pr "EXAMPLE 1: Create Bell state via topological braiding"
    separator ()

    match bellState () with
    | Ok state ->
        pr "Bell state created via braiding"
        pr "  1. Initialise 2 qubits: 6 sigma anyons in 3 pairs"
        pr "  2. Braid 0 (inside qubit 0's pair): turns a phase"
        pr "  3. Braid 2 (inside qubit 1's pair): turns a phase"
        pr "  4. Braid 1 (across the two pairs): entangles"
        pr "  Probabilities:"

        let probabilities =
            bellBitstrings
            |> List.map (fun bits -> (bitText bits, TopologicalOperations.probabilityOfBitstring bits state))

        for bits, p in probabilities do
            pr "    |%s>  %.4f" bits p

        pr "  Result: (|00> + |11>)/sqrt(2) up to phases: the two bits always agree"

        jsonResults <-
            ("1_bell_state",
             box
                 {|
                     status = "ok"
                     qubits = 2
                     braids = List.toArray bellBraids
                     probabilities = probabilities |> List.map (fun (b, p) -> {| bits = b; probability = p |})
                 |})
            :: jsonResults

        csvRows <-
            [
                "1_bell_state"
                "ok"
                "braids 0,2,1"
                probabilities |> List.map (fun (b, p) -> $"%s{b}=%.4f{p}") |> String.concat " "
            ]
            :: csvRows
    | Error err -> pr "Failed: %s" err

// ---------------------------------------------------------------------------
// Example 2 - Entanglement correlation test
// ---------------------------------------------------------------------------
if shouldRun 2 then
    separator ()
    pr "EXAMPLE 2: Entanglement correlation test (%d trials)" cliTrials
    separator ()

    match bellState () with
    | Error err -> pr "Failed: %s" err
    | Ok state ->
        // Each shot fuses every qubit's pair and reads both bits.
        let shots = TopologicalOperations.measureAll state cliTrials

        let correlatedCount =
            shots
            |> Array.filter (fun bits -> bits.Length = 2 && bits.[0] = bits.[1])
            |> Array.length

        let corrPct = float correlatedCount / float cliTrials * 100.0

        for bits in bellBitstrings do
            pr "  |%s>: %d" (bitText bits) (shots |> Array.filter ((=) bits) |> Array.length)

        pr "Correlated:   %d (%.1f%%)" correlatedCount corrPct
        pr "Uncorrelated: %d (%.1f%%)" (cliTrials - correlatedCount) (100.0 - corrPct)
        pr ""

        if corrPct > 75.0 then
            pr "Strong correlation - entanglement verified"
        else
            pr "Correlation weaker than expected"

        jsonResults <-
            ("2_correlation",
             box
                 {|
                     trials = cliTrials
                     correlated = correlatedCount
                     correlationPct = corrPct
                 |})
            :: jsonResults

        csvRows <-
            [ "2_correlation"; string cliTrials; string correlatedCount; $"%.1f{corrPct}" ]
            :: csvRows

// ---------------------------------------------------------------------------
// Example 3 - Gate-based vs topological comparison
// ---------------------------------------------------------------------------
if shouldRun 3 then
    separator ()
    pr "EXAMPLE 3: Gate-based vs topological comparison"
    separator ()
    pr "Gate-based:                          Topological:"
    pr "  Initial: |00>                        Initial: 6 sigma anyons, 3 pairs"
    pr "  H(q0) -> superposition               Braid(0), Braid(2) -> phases"
    pr "  CNOT(0,1) -> entangle                Braid(1) across pairs -> entangle"
    pr "  Result: (|00>+|11>)/sqrt(2)          Result: 00 and 11, 50%% each"
    pr ""
    pr "Advantage: topological protection (immune to local perturbations)"
    pr ""
    pr "Braiding worldline (qubit 0, qubit 1, parity pair):"
    pr "  Time"
    pr "    |  s  s  s  s  s  s"
    pr "    |   \\/   |  |  |  |    Braid(0): a phase"
    pr "    |   /\\   |  |  |  |"
    pr "    |  |  |   \\/   |  |    Braid(2): a phase"
    pr "    |  |  |   /\\   |  |"
    pr "    |  |   \\/   |  |  |    Braid(1): entangles"
    pr "    |  |   /\\   |  |  |"
    pr "    v                       (the two bits always agree)"

    jsonResults <-
        ("3_comparison",
         box
             {|
                 gateOps = "H, CNOT"
                 topoOps = "Braid(0), Braid(2), Braid(1)"
             |})
        :: jsonResults

    csvRows <- [ "3_comparison"; "H+CNOT"; "Braid(0)+Braid(2)+Braid(1)" ] :: csvRows

// ---------------------------------------------------------------------------
// Output
// ---------------------------------------------------------------------------
match outputPath with
| Some outputPathValue ->
    let payload =
        {|
            script = "BellState.fsx"
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
    let header = [ "example"; "detail1"; "detail2"; "detail3" ]
    Reporting.writeCsv v header (csvRows |> List.rev)
| None -> ()

// ---------------------------------------------------------------------------
// Picture (--svg [path]): the braids as worldlines, then the measured shots
// ---------------------------------------------------------------------------
// Two qubits are six sigma anyons in three pairs: qubit 0, qubit 1, and a pair
// that keeps the total parity. Braids 0 and 2 are the ones Example 1 runs;
// braid 1 exchanges anyons of two different pairs, and that one entangles.
// Every state, probability, phase and shot drawn comes from quantumBackend.
match svgPath (IO.Path.Combine(__SOURCE_DIRECTORY__, "_images", "bell-state.svg")) with
| None -> ()
| Some path ->
    let braids = [| 0; 2; 1 |]

    let superpositionOf state =
        match state with
        | FSharp.Azure.Quantum.Core.QuantumState.FusionSuperposition fs -> TopologicalOperations.fromInterface fs
        | _ -> None

    let states =
        braids
        |> Array.scan
            (fun state b -> state |> Result.bind (quantumBackend.ApplyOperation(QuantumOperation.Braid b)))
            (quantumBackend.InitializeState 2)
        |> Array.map (Result.toOption >> Option.bind superpositionOf)

    if states |> Array.exists Option.isNone then
        printfn "The picture could not run the braids on the backend."
    else
        let states = states |> Array.map Option.get
        let bitstrings = [| [| 0; 0 |]; [| 0; 1 |]; [| 1; 0 |]; [| 1; 1 |] |]

        let amplitude (sup: TopologicalOperations.Superposition) (bits: int[]) =
            sup.Terms
            |> List.fold
                (fun acc (amp, st: FusionTree.State) ->
                    if List.toArray (FusionTree.toComputationalBasis st.Tree) = bits then
                        acc + amp
                    else
                        acc)
                Numerics.Complex.Zero

        let n = max 1 cliTrials
        let shots = TopologicalOperations.measureAll states.[braids.Length] n

        let checkpoints =
            [| 1; 2; 3; max 4 (n / 4); n |]
            |> Array.filter (fun c -> c <= n)
            |> Array.distinct

        let countsAt c =
            bitstrings
            |> Array.map (fun bits -> shots.[.. c - 1] |> Array.filter ((=) bits) |> Array.length)

        let firstShot = braids.Length + 1
        let frames = firstShot + checkpoints.Length
        let stateAt k = states.[min k braids.Length]
        let checkpointAt k = checkpoints.[k - firstShot]
        let isShot k = k >= firstShot

        let bitText (bits: int[]) =
            bits |> Array.map string |> String.concat ""

        let pic =
            Picture(760.0, 470.0, frames, 2.0 * float frames, "Bell state by braiding", hold = 0.65)

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

        pic.Text(20.0, 30.0, "A Bell state made by braiding anyons", size = 18.0, bold = true)

        pic.Text(
            20.0,
            50.0,
            "Swapping two anyons is the gate. A swap inside a pair only turns a phase; a swap across pairs entangles.",
            size = 12.5,
            fill = grey
        )

        // The 00 phase after each braid, in degrees turned.
        let phaseTurn k =
            let before = (amplitude states.[k - 1] bitstrings.[0]).Phase
            let after = (amplitude states.[k] bitstrings.[0]).Phase
            abs (after - before) * 180.0 / Math.PI

        let pct (p: float) = sprintf "%.0f%%" (100.0 * p)

        let times c =
            if c = 1 then "1 time" else $"%d{c} times"

        let final = states.[braids.Length]

        let pBell =
            TopologicalOperations.probabilityOfBitstring bitstrings.[0] final,
            TopologicalOperations.probabilityOfBitstring bitstrings.[3] final

        let pOdd =
            TopologicalOperations.probabilityOfBitstring bitstrings.[1] final
            + TopologicalOperations.probabilityOfBitstring bitstrings.[2] final

        let steps =
            Array.init frames (fun k ->
                if k = 0 then
                    "Start: six anyons, made as three pairs from empty space"
                elif k <= braids.Length then
                    let b = braids.[k - 1]

                    let where =
                        if b % 2 = 0 then
                            sprintf "inside qubit %d's pair" (b / 2)
                        else
                            "across two pairs"

                    sprintf "Braid %d: swap the anyons at places %d and %d, %s" b b (b + 1) where
                else
                    let c = checkpointAt k
                    sprintf "Fuse each pair to read the bits: shot %d of %d reads %s" c n (bitText shots.[c - 1]))

        let details =
            Array.init frames (fun k ->
                if k = 0 then
                    "A pair that fuses to 1 reads bit 0, to ψ reads bit 1. Right now both qubits read 00."
                elif k < braids.Length then
                    sprintf "The chances stay the same. Only the phase turns, by %.1f degrees." (phaseTurn k)
                elif k = braids.Length then
                    sprintf
                        "Now 00 has %s, 11 has %s, and 01 or 10 has %s: an entangled Bell state."
                        (pct (fst pBell))
                        (pct (snd pBell))
                        (pct pOdd)
                else
                    let c = checkpointAt k
                    let counts = countsAt c
                    let disagree = counts.[1] + counts.[2]

                    if disagree = 0 then
                        sprintf
                            "The two bits agree every time: %s 00 and %s 11 so far."
                            (times counts.[0])
                            (times counts.[3])
                    else
                        sprintf
                            "So far %s 00, %s 11, %s 01 or 10."
                            (times counts.[0])
                            (times counts.[3])
                            (times disagree))

        pic.FrameText(20.0, 80.0, steps, size = 14.0, bold = true)
        pic.FrameText(20.0, 99.0, details, size = 12.0, fill = grey)

        // Left: the worldlines, time running down.
        pic.Rect(20.0, 115.0, 345.0, 300.0, fill = panel, stroke = frameColour, rx = 6.0)
        pic.Text(35.0, 137.0, "Worldlines: time runs down", size = 12.0, fill = grey)
        let xs = Array.init 6 (fun i -> 50.0 + 48.0 * float i)
        let ys = Array.init (braids.Length + 1) (fun k -> 198.0 + 48.0 * float k)

        let pairColour a =
            [| colour 3; colour 5; colour 8 |].[a / 2]

        for (q, label) in [ (0, "qubit 0"); (1, "qubit 1"); (2, "parity") ] do
            let x1, x2 = xs.[2 * q], xs.[2 * q + 1]
            pic.Text((x1 + x2) / 2.0, 160.0, label, size = 11.5, anchor = "middle", fill = pairColour (2 * q))
            pic.Line(x1 - 8.0, 166.0, x2 + 8.0, 166.0, stroke = pairColour (2 * q), width = 1.5)

        for i in 0..5 do
            pic.Text(xs.[i], 182.0, string i, size = 10.0, fill = grey, anchor = "middle")

        // perms.[k].[p] is the anyon at place p after k braids.
        let perms =
            braids
            |> Array.scan
                (fun (p: int[]) b ->
                    let q = Array.copy p
                    q.[b] <- p.[b + 1]
                    q.[b + 1] <- p.[b]
                    q)
                [| 0..5 |]

        braids
        |> Array.iteri (fun s b ->
            let shown = Array.init frames (fun k -> if k > s then 1.0 else 0.0)
            let y1, y2 = ys.[s], ys.[s + 1]

            let segment p =
                xs.[p],
                xs
                    .[if p = b then b + 1
                      elif p = b + 1 then b
                      else p]

            for p in 0..5 do
                if p <> b && p <> b + 1 then
                    let x1, x2 = segment p

                    pic.Line(
                        x1,
                        y1,
                        x2,
                        y2,
                        stroke = pairColour perms.[s].[p],
                        width = 3.5,
                        animate = [ ("opacity", shown) ]
                    )

            // The strand moving left passes under, the one moving right over.
            let x1, x2 = segment (b + 1)

            pic.Line(
                x1,
                y1,
                x2,
                y2,
                stroke = pairColour perms.[s].[b + 1],
                width = 3.5,
                animate = [ ("opacity", shown) ]
            )

            let x1, x2 = segment b
            pic.Line(x1, y1, x2, y2, stroke = panel, width = 10.0, animate = [ ("opacity", shown) ])
            pic.Line(x1, y1, x2, y2, stroke = pairColour perms.[s].[b], width = 3.5, animate = [ ("opacity", shown) ])

            pic.Text(
                xs.[5] + 18.0,
                (y1 + y2) / 2.0 + 4.0,
                $"braid %d{b}",
                size = 11.0,
                fill = grey,
                animate = [ ("opacity", shown) ]
            ))

        // Measuring: each qubit pair fuses at the bottom and shows 1 or ψ.
        let capY = ys.[braids.Length]
        let bubbleY = capY + 26.0
        let shotShown = Array.init frames (fun k -> if isShot k then 1.0 else 0.0)

        for q in 0..1 do
            let x1, x2 = xs.[2 * q], xs.[2 * q + 1]
            let mid = (x1 + x2) / 2.0

            pic.Element(
                "path",
                [
                    "d",
                    sprintf
                        "M %s %s C %s %s %s %s %s %s"
                        (num x1)
                        (num capY)
                        (num x1)
                        (num (capY + 30.0))
                        (num x2)
                        (num (capY + 30.0))
                        (num x2)
                        (num capY)
                    "stroke", grey
                    "stroke-width", "2"
                    "fill", "none"
                ],
                animate = [ ("opacity", shotShown |> Array.map num) ]
            )

            let bit k =
                if isShot k then
                    Some shots.[checkpointAt k - 1].[q]
                else
                    None

            pic.Element(
                "circle",
                [
                    "cx", num mid
                    "cy", num bubbleY
                    "r", "14"
                    "stroke", "white"
                    "stroke-width", "2"
                ],
                animate =
                    [
                        "opacity", shotShown |> Array.map num
                        "fill",
                        Array.init frames (fun k ->
                            match bit k with
                            | Some 1 -> colour 1
                            | _ -> colour 0)
                    ]
            )

            frameLabels
                mid
                (bubbleY + 6.0)
                (Array.init frames (fun k ->
                    match bit k with
                    | Some 1 -> "ψ"
                    | Some _ -> "1"
                    | None -> ""))
                16.0
                "white"
                true
                "middle"

            frameLabels
                mid
                (bubbleY + 34.0)
                (Array.init frames (fun k ->
                    match bit k with
                    | Some b -> $"bit %d{b}"
                    | None -> ""))
                11.5
                ink
                false
                "middle"

        pic.Text(
            (xs.[4] + xs.[5]) / 2.0,
            bubbleY + 6.0,
            "not read",
            size = 11.0,
            fill = grey,
            anchor = "middle",
            animate = [ ("opacity", shotShown) ]
        )

        // The anyons ride down their worldlines; qubit pairs fuse when read.
        for a in 0..5 do
            let at k =
                let s = min k braids.Length
                let place = Array.IndexOf(perms.[s], a)

                if isShot k && place < 4 then
                    (xs.[place - place % 2] + xs.[place - place % 2 + 1]) / 2.0, bubbleY, 0.0
                else
                    xs.[place], ys.[s], 1.0

            let cx = Array.init frames (fun k -> let x, _, _ = at k in x)
            let cy = Array.init frames (fun k -> let _, y, _ = at k in y)
            let shown = Array.init frames (fun k -> let _, _, o = at k in o)

            pic.Circle(
                cx.[0],
                cy.[0],
                9.0,
                fill = pairColour a,
                stroke = "white",
                width = 1.5,
                animate = [ "cx", cx; "cy", cy; "opacity", shown ]
            )

        // Right: chance and phase of each two-bit result, then the shots.
        pic.Rect(380.0, 115.0, 360.0, 300.0, fill = panel, stroke = frameColour, rx = 6.0)
        pic.Text(395.0, 137.0, "Chance of each result", size = 12.0, fill = grey)
        pic.Text(715.0, 137.0, "phase", size = 12.0, fill = grey, anchor = "middle")
        let barX, barW, dialX, dialR = 425.0, 180.0, 715.0, 11.0

        let bar (y: float) (label: string) (fill: string) (widths: float[]) (labels: string[]) =
            pic.Text(395.0, y + 5.0, label, size = 13.0, bold = true)
            pic.Rect(barX, y - 8.0, barW, 16.0, fill = light, rx = 3.0)
            pic.Rect(barX, y - 8.0, 0.0, 16.0, fill = fill, rx = 3.0, animate = [ ("width", widths) ])
            frameLabels 672.0 (y + 5.0) labels 12.5 ink false "end"

        bitstrings
        |> Array.iteri (fun r bits ->
            let y = 162.0 + 26.0 * float r

            let chances =
                Array.init frames (fun k -> TopologicalOperations.probabilityOfBitstring bits (stateAt k))

            bar y (bitText bits) (colour 0) (chances |> Array.map ((*) barW)) (chances |> Array.map pct)
            let amps = Array.init frames (fun k -> amplitude (stateAt k) bits)
            pic.Circle(dialX, y, dialR, fill = "white", stroke = frameColour, width = 1.5)

            pic.Line(
                dialX,
                y,
                dialX + dialR,
                y,
                stroke = ink,
                width = 2.0,
                animate =
                    [
                        "x2", amps |> Array.map (fun a -> dialX + dialR * a.Magnitude * cos a.Phase)
                        "y2", amps |> Array.map (fun a -> y - dialR * a.Magnitude * sin a.Phase)
                        "opacity", amps |> Array.map (fun a -> if a.Magnitude > 1e-9 then 1.0 else 0.0)
                    ]
            ))

        pic.FrameText(
            395.0,
            282.0,
            Array.init frames (fun k ->
                if isShot k then
                    sprintf "Measured: %d of %d shots" (checkpointAt k) n
                else
                    "Measured: no shots yet"),
            size = 12.0,
            fill = grey
        )

        bitstrings
        |> Array.iteri (fun r bits ->
            let y = 306.0 + 26.0 * float r

            let tally =
                Array.init frames (fun k ->
                    if isShot k then
                        let c = checkpointAt k
                        Some((countsAt c).[r], c)
                    else
                        None)

            bar
                y
                (bitText bits)
                (colour 4)
                (tally
                 |> Array.map (Option.map (fun (c, t) -> barW * float c / float t) >> Option.defaultValue 0.0))
                (tally |> Array.map (Option.map (fst >> string) >> Option.defaultValue "")))

        pic.Progress(20.0, 432.0, 720.0)

        pic.Text(
            20.0,
            458.0,
            $"Computed in this run of BellState.fsx on the Ising backend: 2 qubits, braids 0, 2 and 1, then %d{n} shots.",
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
    pr "  dotnet fsi BellState.fsx -- --example 2 --trials 200"
    pr "  dotnet fsi BellState.fsx -- --quiet --output r.json --csv r.csv"
    pr "  dotnet fsi BellState.fsx -- --help"
