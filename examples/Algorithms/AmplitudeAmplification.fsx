/// Amplitude Amplification — educational example
///
/// Amplitude amplification is the generalisation of Grover's search: given a
/// state-preparation `A` and an oracle that marks "good" states, it boosts the
/// probability of measuring a good state quadratically faster than classical
/// sampling. Grover's algorithm is the special case where `A = H^⊗n` (uniform
/// superposition).
///
/// This example amplifies a single marked basis state (value 5 = |101⟩) in a
/// 3-qubit space, starting from the uniform superposition, and shows the marked
/// state's probability rising from 1/8 (12.5%) toward ~100%.
///
/// Executed on the unified IQuantumBackend (local simulator here). Swap in any
/// cloud backend to run the same code on hardware.
///
/// Run with: dotnet fsi AmplitudeAmplification.fsx
///
/// `--svg [path]` also draws the run as an animated picture: one bar per answer
/// showing its amplitude, each oracle and each diffusion step in turn, and the
/// marked answer's chance per round (default path: _images/amplitude-amplification.svg).

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "nuget: MathNet.Numerics, 5.0.0"
// The library comes from NuGet; `dotnet fsi --define:LOCAL_BUILD <script>` uses the repo's Debug build.
#if LOCAL_BUILD
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#else
#r "nuget: FSharp.Azure.Quantum"
#endif
#load "../_common/SvgAnimation.fsx"

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.GroverSearch
open FSharp.Azure.Quantum.Backends
open SvgAnimation

[<Literal>]
let numQubits = 3

/// |101⟩
[<Literal>]
let markedValue = 5

let searchSpace = 1 <<< numQubits

[<Literal>]
let shots = 4000

// State preparation A = H^⊗n (uniform superposition) as a gate circuit.
let uniformPrep =
    let empty = CircuitBuilder.empty numQubits

    List.init (max 0 numQubits) CircuitBuilder.H
    |> List.fold (fun c g -> CircuitBuilder.addGate g c) empty

let backend = LocalBackend.LocalBackend() :> IQuantumBackend

// ============================================================================
// PICTURE (only with --svg)
// ============================================================================

/// One state of the run: the round it belongs to, the step that produced it
/// ("start", "oracle" or "diffusion") and its amplitudes (index bit q = qubit q).
type Step =
    {
        Round: int
        Kind: string
        Amplitudes: float[]
    }

/// The run replayed one operation at a time, through the plan
/// `AmplitudeAmplification.Unified.execute` follows, so every oracle and every
/// diffusion leaves a state of its own.
let amplificationSteps (intent: AmplitudeAmplification.Unified.AmplitudeAmplificationIntent) (rounds: int) =
    let fail (e: Core.QuantumError) =
        failwithf "state of a picture step: %s" e.Message

    let amplitudes (state: Core.QuantumState) =
        match state with
        | Core.QuantumState.StateVector sv ->
            let amps =
                Array.init (LocalSimulator.StateVector.dimension sv) (fun i ->
                    LocalSimulator.StateVector.getAmplitude i sv)

            // Oracle and diffusion keep amplitudes real; the bars draw the real part.
            if amps |> Array.exists (fun a -> abs a.Imaginary > 1e-9) then
                failwith "state of a picture step: expected real amplitudes"

            amps |> Array.map (fun a -> a.Real)
        | other -> failwithf "state of a picture step: expected a state vector, got %s" (other.GetType().Name)

    match AmplitudeAmplification.Unified.plan backend intent with
    | Error e -> fail e
    | Ok(AmplitudeAmplification.Unified.AmplitudeAmplificationPlan.ExecuteViaGroverIntents(prepOps,
                                                                                           oracleOp,
                                                                                           diffusionOp,
                                                                                           _,
                                                                                           _)) ->
        let prepared =
            backend.InitializeState intent.NumQubits
            |> Result.bind (UnifiedBackend.applySequence backend prepOps)
            |> Result.defaultWith fail

        let apply op state =
            backend.ApplyOperation op state |> Result.defaultWith fail

        let rec go round state acc =
            if round > rounds then
                List.rev acc
            else
                let afterOracle = apply oracleOp state
                let afterDiffusion = apply diffusionOp afterOracle

                go
                    (round + 1)
                    afterDiffusion
                    ({
                        Round = round
                        Kind = "diffusion"
                        Amplitudes = amplitudes afterDiffusion
                     }
                     :: {
                            Round = round
                            Kind = "oracle"
                            Amplitudes = amplitudes afterOracle
                        }
                     :: acc)

        go
            1
            prepared
            [
                {
                    Round = 0
                    Kind = "start"
                    Amplitudes = amplitudes prepared
                }
            ]
        |> Array.ofList
    | Ok _ ->
        failwith
            $"the picture replays oracle and diffusion one at a time; this backend runs them as lowered gates, calling amplificationSteps with rounds: {rounds}"

/// Draws the steps: amplitude bars with their mean on the left, the marked
/// answer's chance per round on the right.
let drawPicture (steps: Step[]) (marked: int -> bool) (optimal: int) (path: string) =
    let frames = steps.Length
    let n = steps.[0].Amplitudes.Length
    let qubits = int (System.Math.Round(System.Math.Log(float n, 2.0)))
    let rounds = steps |> Array.map (fun s -> s.Round) |> Array.max
    let markedIndices = [| 0 .. n - 1 |] |> Array.filter marked

    let bits (i: int) =
        System.String [| for q in qubits - 1 .. -1 .. 0 -> if (i >>> q) &&& 1 = 1 then '1' else '0' |]

    let chance (s: Step) =
        markedIndices |> Array.sumBy (fun i -> s.Amplitudes.[i] * s.Amplitudes.[i])

    let mean (s: Step) = Array.average s.Amplitudes
    let markedColour, otherColour = colour 1, colour 0

    let pic =
        Picture(
            760.0,
            452.0,
            frames,
            2.0 * float frames,
            $"Amplitude amplification: finding the marked answer among %d{n}",
            hold = 0.6
        )

    pic.Text(24.0, 30.0, $"Amplitude amplification: finding the marked answer among %d{n}", size = 18.0, bold = true)

    pic.Text(
        24.0,
        50.0,
        "Each round flips the right answer's bar, then mirrors every bar about their average: the right one grows.",
        size = 12.5,
        fill = grey
    )

    // Step label.
    let label (s: Step) =
        let p = 100.0 * chance s

        match s.Kind with
        | "start" -> $"start: all %d{n} answers equally likely (%.1f{p}%% each)"
        | "oracle" -> $"round %d{s.Round}: the oracle flips the marked answer (its chance stays %.1f{p}%%)"
        | _ when s.Round = optimal ->
            sprintf
                "round %d: mirror every bar about the mean: the marked answer reaches %.1f%%, the best stop"
                s.Round
                p
        | _ when s.Round > optimal ->
            sprintf
                "round %d: mirror again, past the best stop: the marked answer falls to %.1f%% (overshoot)"
                s.Round
                p
        | _ -> $"round %d{s.Round}: mirror every bar about the mean: the marked answer grows to %.1f{p}%%"

    pic.FrameText(24.0, 78.0, steps |> Array.map label, size = 13.5, bold = true)

    // Bars panel: amplitude −1 … 1.
    let panelTop, panelBottom = 92.0, 404.0
    pic.Rect(24.0, panelTop, 464.0, panelBottom - panelTop, fill = panel, stroke = frameColour, rx = 4.0)
    let zero, scale = 236.0, 125.0
    let yOf (a: float) = zero - a * scale
    let left, right = 78.0, 440.0

    for tick in [ -1.0; -0.5; 0.0; 0.5; 1.0 ] do
        pic.Line(left - 4.0, yOf tick, right, yOf tick, stroke = (if tick = 0.0 then grey else light), width = 1.0)

        pic.Text(
            left - 8.0,
            yOf tick + 4.0,
            (if tick = 0.0 then "0" else $"%+.1f{tick}"),
            size = 10.0,
            fill = grey,
            anchor = "end"
        )

    pic.Text(
        36.0,
        zero,
        "amplitude (signed bar height)",
        size = 11.0,
        fill = grey,
        anchor = "middle",
        transform = sprintf "rotate(-90 36 %s)" (num zero)
    )

    let slot = (right - left) / float n
    let barWidth = min 34.0 (slot * 0.62)

    for i in 0 .. n - 1 do
        let x0 = left + slot * (float i + 0.5) - barWidth / 2.0
        let x1 = x0 + barWidth

        let shape (s: Step) =
            let top = yOf s.Amplitudes.[i]

            sprintf
                "M %s %s L %s %s L %s %s L %s %s Z"
                (num x0)
                (num zero)
                (num x0)
                (num top)
                (num x1)
                (num top)
                (num x1)
                (num zero)

        let shapes = steps |> Array.map shape
        let fill = if marked i then markedColour else otherColour
        pic.Path(shapes.[0], stroke = "none", width = 0.0, fill = fill, shapes = shapes)

        pic.Text(
            (x0 + x1) / 2.0,
            panelBottom - 12.0,
            bits i,
            size = 11.0,
            fill = (if marked i then markedColour else grey),
            bold = marked i,
            anchor = "middle"
        )

    // The mean, which the diffusion mirrors every bar about.
    let meanY = steps |> Array.map (mean >> yOf)

    pic.Line(
        left - 4.0,
        meanY.[0],
        right,
        meanY.[0],
        stroke = ink,
        width = 1.5,
        dash = "6 4",
        animate = [ ("y1", meanY); ("y2", meanY) ]
    )

    pic.Text(
        right + 4.0,
        meanY.[0] + 4.0,
        "mean",
        size = 10.5,
        fill = ink,
        animate = [ ("y", meanY |> Array.map (fun y -> y + 4.0)) ]
    )

    // Chance of the marked answer per round.
    let cLeft, cRight, cTop, cBottom = 540.0, 720.0, 150.0, 350.0
    pic.Rect(500.0, panelTop, 236.0, panelBottom - panelTop, fill = panel, stroke = frameColour, rx = 4.0)
    pic.Text(510.0, 112.0, "chance of reading the marked answer", size = 12.0, bold = true)

    pic.FrameText(
        510.0,
        132.0,
        steps |> Array.map (fun s -> sprintf "now: %.1f%%" (100.0 * chance s)),
        size = 12.0,
        fill = markedColour,
        bold = true
    )

    let cx (r: float) =
        cLeft + r * (cRight - cLeft) / float rounds

    let cy (p: float) = cBottom - p * (cBottom - cTop)

    for p in [ 0.0; 0.5; 1.0 ] do
        pic.Line(cLeft, cy p, cRight, cy p, stroke = (if p = 0.0 then grey else light), width = 1.0)
        pic.Text(cLeft - 6.0, cy p + 4.0, sprintf "%.0f%%" (100.0 * p), size = 10.0, fill = grey, anchor = "end")

    for r in 0..rounds do
        pic.Text(cx (float r), cBottom + 15.0, string r, size = 10.0, fill = grey, anchor = "middle")

    pic.Text((cLeft + cRight) / 2.0, cBottom + 32.0, "rounds", size = 11.0, fill = grey, anchor = "middle")

    pic.Line(
        cx (float optimal),
        cTop - 4.0,
        cx (float optimal),
        cBottom,
        stroke = markedColour,
        width = 1.0,
        dash = "3 3",
        opacity = 0.7
    )

    pic.Text(cx (float optimal) + 4.0, cBottom - 8.0, "best stop", size = 10.0, fill = markedColour)

    let perRound =
        steps
        |> Array.filter (fun s -> s.Kind <> "oracle")
        |> Array.map (fun s -> (float s.Round, chance s))

    let curve =
        perRound
        |> Array.mapi (fun k (r, p) -> sprintf "%s %s %s" (if k = 0 then "M" else "L") (num (cx r)) (num (cy p)))
        |> String.concat " "

    pic.Path(curve, stroke = grey, width = 1.5)

    for r, p in perRound do
        pic.Circle(cx r, cy p, 2.5, fill = grey)

    // The marker: an oracle step sits halfway into its round, at an unchanged chance.
    let markerX =
        steps
        |> Array.map (fun s ->
            cx (
                if s.Kind = "oracle" then
                    float s.Round - 0.5
                else
                    float s.Round
            ))

    let markerY = steps |> Array.map (chance >> cy)

    pic.Circle(
        markerX.[0],
        markerY.[0],
        6.0,
        fill = markedColour,
        stroke = "white",
        width = 1.5,
        animate = [ ("cx", markerX); ("cy", markerY) ]
    )

    // Legend.
    let legendY = 424.0
    pic.Rect(24.0, legendY - 9.0, 10.0, 10.0, fill = markedColour)
    let markedText = markedIndices |> Array.map bits |> String.concat ", "
    pic.Text(40.0, legendY, $"marked answer (%s{markedText})", size = 11.0)
    pic.Rect(170.0, legendY - 9.0, 10.0, 10.0, fill = otherColour)
    pic.Text(186.0, legendY, sprintf "the other %d answers" (n - markedIndices.Length), size = 11.0)
    pic.Line(318.0, legendY - 4.0, 342.0, legendY - 4.0, stroke = ink, width = 1.5, dash = "6 4")
    pic.Text(348.0, legendY, "mean of all bars", size = 11.0)

    pic.Text(
        736.0,
        legendY,
        "a bar's height squared = its chance of being read",
        size = 11.0,
        fill = grey,
        anchor = "end"
    )

    pic.Progress(24.0, 440.0, 712.0)
    pic.Save path

printfn "Amplitude Amplification — boosting the marked state |101⟩ (value %d)\n" markedValue

match Oracle.forValue markedValue numQubits with
| Error err ->
    eprintfn "Oracle build failed: %s" err.Message
    exit 1
| Ok oracle ->
    // Optimal number of amplification rounds for one marked item in 8 states.
    let iterations =
        AmplitudeAmplification.optimalIterations searchSpace 1 (1.0 / float searchSpace)

    printfn "Search space: %d states, marked: 1, optimal iterations: %d\n" searchSpace iterations

    let intent: AmplitudeAmplification.Unified.AmplitudeAmplificationIntent =
        {
            NumQubits = numQubits
            StatePreparation = uniformPrep
            Oracle = oracle
            Iterations = iterations
            Exactness = AmplitudeAmplification.Unified.Exact
        }

    match AmplitudeAmplification.Unified.execute backend intent with
    | Error err ->
        eprintfn "Amplification failed: %s" err.Message
        exit 1
    | Ok finalState ->
        let hist =
            UnifiedBackend.measureState finalState shots
            |> Array.map (Array.map string >> String.concat "")
            |> Array.countBy id
            |> Array.sortByDescending snd

        printfn "Measured distribution after amplification:"

        for (bitstring, count) in hist do
            printfn "  |%s⟩ : %5.1f%%" bitstring (100.0 * float count / float shots)

        printfn
            "\nStarting probability of the marked state was 1/%d = %.1f%%; amplification concentrates it."
            searchSpace
            (100.0 / float searchSpace)

        match svgPath (System.IO.Path.Combine(__SOURCE_DIRECTORY__, "_images", "amplitude-amplification.svg")) with
        | None -> ()
        | Some path ->
            // Two rounds past the best stop show the chance falling again.
            let steps = amplificationSteps intent (iterations + 2)

            // The replay agrees with the run: its state after `iterations` rounds is the final state.
            let replayed =
                steps |> Array.find (fun s -> s.Round = iterations && s.Kind <> "oracle")

            match finalState with
            | Core.QuantumState.StateVector sv ->
                for i in 0 .. searchSpace - 1 do
                    let a = LocalSimulator.StateVector.getAmplitude i sv

                    if abs (a.Real - replayed.Amplitudes.[i]) > 1e-9 || abs a.Imaginary > 1e-9 then
                        failwithf "picture replay differs from the run at |%d⟩" i
            | _ -> failwith "picture replay: expected a state vector"

            drawPicture steps (Oracle.isSolution oracle.Spec) iterations path
