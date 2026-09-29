/// Quantum Fourier Transform (QFT) — educational example
///
/// The QFT is the quantum analogue of the discrete Fourier transform and the
/// engine behind phase estimation, Shor's algorithm and more.
///
/// This example:
///   1. Applies the QFT to |000⟩ and shows the result is a *uniform* superposition
///      over all basis states (the Fourier transform of a delta is flat).
///   2. Applies the inverse QFT to undo it, recovering |000⟩.
///
/// Runs on the local state-vector simulator (LocalBackend) only; nothing is sent to
/// Azure Quantum. Step 2 applies the inverse QFT to the state step 1 returned, and
/// the picture reads amplitudes: both need state-vector access, which a cloud
/// backend does not give. On a cloud backend the library runs a QFT only from |0⟩,
/// as one submitted circuit, and returns sampled counts.
///
/// Run with: dotnet fsi QuantumFourierTransform.fsx
/// Add `--svg [path]` to also draw a 4-qubit QFT of the number 7, gate by gate,
/// as an animated picture (default: _images/quantum-fourier-transform.svg next to this script).

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "nuget: MathNet.Numerics, 5.0.0"
// The library comes from NuGet; `dotnet fsi --define:LOCAL_BUILD <script>` uses the repo's Debug build.
#if LOCAL_BUILD
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#else
#r "nuget: FSharp.Azure.Quantum"
#endif
#load "../_common/SvgAnimation.fsx"

open System
open System.IO
open System.Numerics
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open SvgAnimation

let backend = LocalBackend.LocalBackend() :> IQuantumBackend

[<Literal>]
let numQubits = 3

[<Literal>]
let shots = 4000

let histogram state =
    UnifiedBackend.measureState state shots
    |> Array.map (Array.map string >> String.concat "")
    |> Array.countBy id
    |> Array.sortByDescending snd

// ============================================================================
// PICTURE (only with --svg): a number going through the QFT, gate by gate
// ============================================================================

/// The picture's own register and input number.
[<Literal>]
let pictureQubits = 4

[<Literal>]
let pictureInput = 7

/// Amplitudes of a state; index bit q is qubit q.
let amplitudesOf (state: QuantumState) : Complex[] =
    match state with
    | QuantumState.StateVector sv ->
        Array.init (LocalSimulator.StateVector.dimension sv) (fun i -> LocalSimulator.StateVector.getAmplitude i sv)
    | other -> failwithf "picture: expected a state vector, got %s" (other.GetType().Name)

/// One qubit on its own: the chance it reads 1, and the phase of its 1 against its 0
/// as a hand of length 2|ρ₁₀| (1 = an even mix, 0 = a plain 0 or 1) at an angle in turns.
[<Struct>]
type Dial =
    {
        POne: float
        Length: float
        Turn: float
    }

let dialOf (amps: Complex[]) (q: int) : Dial =
    let mask = 1 <<< q
    let mutable pOne = 0.0
    let mutable rho10 = Complex.Zero

    for i in 0 .. amps.Length - 1 do
        if i &&& mask <> 0 then
            pOne <- pOne + amps.[i].Magnitude * amps.[i].Magnitude
            rho10 <- rho10 + amps.[i] * Complex.Conjugate amps.[i ^^^ mask]

    let length = 2.0 * rho10.Magnitude
    let turn = ((rho10.Phase / (2.0 * Math.PI)) % 1.0 + 1.0) % 1.0

    {
        POne = pOne
        Length = length
        Turn = (if length < 1e-9 || turn > 1.0 - 1e-9 then 0.0 else turn)
    }

/// The QFT here reads qubit 0 as the highest bit: a reading's number from a state index
/// and back (reversing the bits is its own inverse).
let readingOf (i: int) =
    Seq.sum
        [
            for q in 0 .. pictureQubits - 1 -> ((i >>> q) &&& 1) <<< (pictureQubits - 1 - q)
        ]

/// A turn as a fraction of 1/2^pictureQubits, e.g. "7/16", "3/4", "0".
let turnWords (t: float) =
    let parts = 1 <<< pictureQubits
    let whole = Math.Round(t * float parts)

    if abs (t * float parts - whole) > 1e-6 then
        $"%.3f{t}"
    else
        let rec gcd a b = if b = 0 then a else gcd b (a % b)
        let m = int whole % parts
        let g = gcd m parts

        if m = 0 then "0" else sprintf "%d/%d" (m / g) (parts / g)

type QftStep =
    {
        Label: string
        Caption: string
        Amplitudes: Complex[]
        Target: int option
        Controls: int list
        /// Words under the chance bars.
        BarsCaption: string
    }

let qftSteps () : QftStep[] =
    let n = pictureQubits

    let orFail (r: Result<'T, QuantumError>) =
        r |> Result.defaultWith (fun e -> failwithf "picture: %s" e.Message)

    let apply gates state =
        UnifiedBackend.applySequence backend (gates |> List.map QuantumOperation.Gate) state
        |> orFail

    let bitOfInput q = (pictureInput >>> (n - 1 - q)) &&& 1

    let binary =
        String [| for q in 0 .. n - 1 -> if bitOfInput q = 1 then '1' else '0' |]

    let readsOne (amps: Complex[]) (q: int) = (dialOf amps q).POne > 0.5
    let steps = ResizeArray<QftStep>()

    let add label caption state target controls barsCaption =
        steps.Add
            {
                Label = label
                Caption = caption
                Amplitudes = amplitudesOf state
                Target = target
                Controls = controls
                BarsCaption = barsCaption
            }

    let start = backend.InitializeState n |> orFail

    let loaded =
        apply
            [
                for q in 0 .. n - 1 do
                    if bitOfInput q = 1 then
                        CircuitBuilder.X q
            ]
            start

    let place =
        [ for q in 0 .. n - 1 -> sprintf "%d×%d" (bitOfInput q) (1 <<< (n - 1 - q)) ]
        |> String.concat " + "

    add
        $"Load the number %d{pictureInput} = %s{binary} (q0 is the highest bit)"
        $"%s{place} = %d{pictureInput}. Plain 0s and 1s have no hands yet."
        loaded
        None
        []
        $"One reading is certain: %s{binary} = %d{pictureInput}."

    let mutable state = loaded

    // Per qubit: one frame for its H, one for all the controlled turns onto it.
    for j in 0 .. n - 1 do
        let wasOne = readsOne (amplitudesOf state) j
        state <- apply [ CircuitBuilder.H j ] state

        add
            $"q%d{j}: H makes it 0 and 1 at once"
            (if wasOne then
                 $"q%d{j} read 1, so its hand starts at 1/2 turn."
             else
                 $"q%d{j} read 0, so its hand starts at 0 (12 o'clock).")
            state
            (Some j)
            []
            "H splits each reading in two; the arrows carry the phases."

        if j < n - 1 then

            let turns: (int * int * bool) list =
                [
                    for k in j + 1 .. n - 1 do
                        let power = k - j + 1
                        let controlIsOne = readsOne (amplitudesOf state) k
                        state <- apply [ CircuitBuilder.CP(k, j, 2.0 * Math.PI / float (1 <<< power)) ] state
                        (k, power, controlIsOne)
                ]

            let turns = turns
            let turned = turns |> List.filter (fun (_, _, one) -> one)

            let fractions =
                turned
                |> List.map (fun (_, power, _) -> $"1/%d{1 <<< power}")
                |> String.concat " + "

            let names = turned |> List.map (fun (k, _, _) -> $"q%d{k}") |> String.concat ", "

            let halves =
                turns
                |> List.map (fun (_, power, _) -> $"1/%d{1 <<< power}")
                |> String.concat ", "

            let from = turns |> List.map (fun (k, _, _) -> $"q%d{k}") |> String.concat ", "

            add
                $"q%d{j}: halving turns of %s{halves} from %s{from}"
                (if turned.IsEmpty then
                     $"No later qubit reads 1, so q%d{j} does not turn."
                 else
                     $"%s{names} read 1, so q%d{j} turns a further %s{fractions}: now at %s{turnWords (dialOf (amplitudesOf state) j).Turn} turn.")
                state
                (Some j)
                (turns |> List.map (fun (k, _, _) -> k))
                "The chances stay put; only the arrows turn."

    // The transform ends by reversing the qubit order.
    state <- apply [ for i in 0 .. n / 2 - 1 -> CircuitBuilder.SWAP(i, n - 1 - i) ] state

    let pairs =
        [ for i in 0 .. n / 2 - 1 -> sprintf "q%d ↔ q%d" i (n - 1 - i) ]
        |> String.concat ", "

    // The library's own QFT of the loaded state must give the same state as the gates above.
    let library = QFT.executeOnState loaded backend QFT.defaultConfig |> orFail
    let libraryAmps = amplitudesOf library.FinalState

    let gap =
        Array.map2 (fun (a: Complex) (b: Complex) -> (a - b).Magnitude) libraryAmps (amplitudesOf state)
        |> Array.max

    if gap > 1e-9 then
        failwithf "picture: the gate-by-gate QFT differs from QFT.executeOnState by %g" gap

    // Dial q ends at input / 2^(q+1) of a turn, whole turns dropped.
    let ends =
        [
            for q in 0 .. n - 1 ->
                let expected = (float pictureInput / float (1 <<< (q + 1))) % 1.0
                let dial = dialOf libraryAmps q

                if abs (dial.Turn - expected) > 1e-9 || abs (dial.Length - 1.0) > 1e-9 then
                    failwithf "picture: q%d ends at %g turn, not %g" q dial.Turn expected

                let whole = sprintf "%d/%d" pictureInput (1 <<< (q + 1))
                let reduced = turnWords expected

                if whole = reduced then
                    $"q%d{q} %s{whole}"
                else
                    $"q%d{q} %s{whole} → %s{reduced}"
        ]

    // Neighbouring readings' arrows differ by input/2^n of a turn.
    let step = float pictureInput / float (1 <<< n)

    for k in 1 .. (1 <<< n) - 1 do
        let turnOf (r: int) =
            libraryAmps.[readingOf r].Phase / (2.0 * Math.PI)

        let d = ((turnOf k - turnOf (k - 1) - step) % 1.0 + 1.5) % 1.0 - 0.5

        if abs d > 1e-9 then
            failwithf "picture: reading %d's arrow is not %g turn past reading %d's" k step (k - 1)

    add
        $"Swap %s{pairs}: now dial q stands at %d{pictureInput} ÷ 2^(q+1) of a turn"
        ((ends |> String.concat ",  ") + " (whole turns drop out)")
        library.FinalState
        None
        []
        $"All %d{1 <<< n} readings are equally likely, and each arrow is %s{turnWords step} turn past the one before it."

    let back =
        QFT.executeOnState
            library.FinalState
            backend
            { QFT.defaultConfig with
                Inverse = true
            }
        |> orFail

    if
        (readingOf (Array.findIndex (fun (a: Complex) -> a.Magnitude > 0.5) (amplitudesOf back.FinalState)))
        <> pictureInput
    then
        failwith "picture: the inverse QFT did not return the input"

    add
        $"Inverse QFT: the dials fold back into %s{binary} = %d{pictureInput}"
        "The angles held the number: undoing the transform reads it back."
        back.FinalState
        None
        []
        $"One reading is certain again: %s{binary} = %d{pictureInput}."

    steps.ToArray()

let drawPicture (path: string) =
    let steps = qftSteps ()
    let n = steps.Length
    let q = pictureQubits
    let dim = 1 <<< q
    let per (f: QftStep -> 'T) = Array.map f steps
    let picW, picH = 760.0, 566.0
    let accent, zeroColour, oneColour = colour 3, colour 0, colour 4

    let pic =
        Picture(picW, picH, frames = n, durationS = 2.0 * float n, title = "Quantum Fourier transform", hold = 0.55)

    // Header.
    pic.Text(16.0, 30.0, "The quantum Fourier transform turns a number into dial angles", size = 20.0, bold = true)

    pic.Text(
        16.0,
        50.0,
        $"Four qubits hold the number %d{pictureInput}. Gate by gate the transform turns each qubit's dial, until the dials stand at %d{pictureInput}/2, %d{pictureInput}/4, %d{pictureInput}/8 and %d{pictureInput}/16 of a turn.",
        size = 11.5,
        fill = grey
    )

    pic.FrameText(16.0, 78.0, per (fun s -> s.Label), size = 14.0, bold = true, fill = accent)

    // Top: one dial per qubit.
    pic.Rect(16.0, 92.0, picW - 32.0, 236.0, fill = panel, stroke = frameColour, rx = 6.0)
    pic.Text(28.0, 110.0, "Each qubit as a dial", size = 12.0, bold = true)

    pic.Text(
        28.0,
        124.0,
        "hand = phase of the qubit's 1 against its 0, clockwise from 12 · no hand = a plain 0 or 1 · face: blue reads 0, orange reads 1",
        size = 10.5,
        fill = grey
    )

    let r = 46.0
    let dialY = 196.0

    for qubit in 0 .. q - 1 do
        let cx = 110.0 + 180.0 * float qubit
        let dials = per (fun s -> dialOf s.Amplitudes qubit)
        let isTarget = per (fun s -> if s.Target = Some qubit then 1.0 else 0.0)
        let isControl = per (fun s -> if List.contains qubit s.Controls then 1.0 else 0.0)

        pic.Circle(cx, dialY, r + 7.0, stroke = accent, width = 3.0, animate = [ ("opacity", isTarget) ])

        pic.Element(
            "circle",
            [
                "cx", num cx
                "cy", num dialY
                "r", num (r + 7.0)
                "fill", "none"
                "stroke", accent
                "stroke-width", "2.5"
                "stroke-dasharray", "5 4"
            ],
            animate = [ ("opacity", Array.map num isControl) ]
        )

        pic.Circle(cx, dialY, r, fill = tint 0.75 zeroColour)

        pic.Circle(
            cx,
            dialY,
            r,
            fill = tint 0.45 oneColour,
            animate = [ ("opacity", dials |> Array.map (fun d -> d.POne)) ]
        )

        pic.Circle(cx, dialY, r, stroke = ink, width = 1.2)

        for t, label, dx, dy, anchor in
            [
                (0.0, "0", 0.0, -2.0, "middle")
                (0.25, "1/4", 3.0, 4.0, "start")
                (0.5, "1/2", 0.0, 11.0, "middle")
                (0.75, "3/4", -3.0, 4.0, "end")
            ] do
            let a = 2.0 * Math.PI * t
            let sx, sy = sin a, -(cos a)

            pic.Line(
                cx + (r - 6.0) * sx,
                dialY + (r - 6.0) * sy,
                cx + r * sx,
                dialY + r * sy,
                stroke = grey,
                width = 1.0
            )

            pic.Text(
                cx + (r + 9.0) * sx + dx,
                dialY + (r + 9.0) * sy + dy,
                label,
                size = 9.0,
                fill = grey,
                anchor = anchor
            )

        // The angle swept from 12 o'clock, as a slice with a fixed point count so it morphs.
        let slice (d: Dial) =
            let len = r * d.Length * 0.92

            let points =
                [
                    for k in 0..24 ->
                        let a = 2.0 * Math.PI * d.Turn * float k / 24.0
                        sprintf "L%s,%s" (num (cx + len * sin a)) (num (dialY - len * cos a))
                ]

            sprintf "M%s,%s %s Z" (num cx) (num dialY) (String.concat " " points)

        let slices = dials |> Array.map slice
        pic.Path(slices.[0], stroke = "none", width = 0.0, fill = tint 0.55 accent, opacity = 0.7, shapes = slices)

        let tipX =
            dials
            |> Array.map (fun d -> cx + r * 0.92 * d.Length * sin (2.0 * Math.PI * d.Turn))

        let tipY =
            dials
            |> Array.map (fun d -> dialY - r * 0.92 * d.Length * cos (2.0 * Math.PI * d.Turn))

        pic.Line(cx, dialY, cx, dialY, stroke = ink, width = 3.0, animate = [ "x2", tipX; "y2", tipY ])
        pic.Circle(cx, dialY, 3.0, fill = ink)

        let role =
            if qubit = 0 then " (highest bit)"
            elif qubit = q - 1 then " (lowest bit)"
            else ""

        pic.Text(cx, dialY + r + 32.0, $"q%d{qubit}%s{role}", size = 12.0, bold = true, anchor = "middle")

        let words (d: Dial) =
            if d.Length > 0.5 then
                sprintf "turned %s" (turnWords d.Turn)
            elif d.POne > 0.5 then
                "reads 1"
            else
                "reads 0"

        pic.FrameText(cx, dialY + r + 47.0, dials |> Array.map words, size = 11.0, anchor = "middle")

    pic.FrameText(picW / 2.0, 318.0, per (fun s -> s.Caption), size = 11.5, anchor = "middle")

    // Bottom: the chance of each reading, and its amplitude as an arrow.
    pic.Rect(16.0, 336.0, picW - 32.0, 206.0, fill = panel, stroke = frameColour, rx = 6.0)
    pic.Text(28.0, 354.0, "Chance of each reading, with its amplitude as an arrow", size = 12.0, bold = true)

    pic.Text(
        28.0,
        368.0,
        "bits = q0 q1 q2 q3, read as a binary number · arrow length is relative to the largest in that step",
        size = 10.5,
        fill = grey
    )

    let prob (s: QftStep) (i: int) =
        let a = s.Amplitudes.[i] in a.Real * a.Real + a.Imaginary * a.Imaginary

    let baseY, barArea = 452.0, 66.0
    let x0 = 60.0
    let slot = (picW - 28.0 - x0) / float dim
    let yOf (p: float) = baseY - barArea * p

    for p, label in [ (0.5, "50%"); (1.0, "100%") ] do
        pic.Line(x0, yOf p, picW - 28.0, yOf p, stroke = light, width = 1.0)
        pic.Text(x0 - 6.0, yOf p + 4.0, label, size = 10.0, fill = grey, anchor = "end")

    pic.Line(x0, baseY, picW - 28.0, baseY, stroke = grey, width = 1.0)
    pic.Text(x0 - 6.0, baseY + 4.0, "0%", size = 10.0, fill = grey, anchor = "end")

    for reading in 0 .. dim - 1 do
        let i = readingOf reading
        let cx = x0 + slot * (float reading + 0.5)
        let tops = per (fun s -> yOf (prob s i))

        pic.Rect(
            cx - 13.0,
            baseY,
            26.0,
            0.0,
            fill = tint 0.3 zeroColour,
            animate = [ "y", tops; "height", per (fun s -> barArea * prob s i) ]
        )

        let pct (s: QftStep) =
            let p = prob s i

            if p < 1e-9 then
                ""
            else
                sprintf "%g%%" (Math.Round(100.0 * p, 2))

        pic.FrameText(cx, baseY + 27.0, per pct, size = 9.5, fill = grey, anchor = "middle")

        let bits =
            String
                [|
                    for b in q - 1 .. -1 .. 0 -> if (reading >>> b) &&& 1 = 1 then '1' else '0'
                |]

        pic.Text(cx, baseY + 14.0, bits, size = 10.0, anchor = "middle")

        let ay, ar = baseY + 52.0, 13.0
        pic.Circle(cx, ay, ar, fill = "white", stroke = frameColour)
        pic.Line(cx, ay - ar, cx, ay - ar + 3.0, stroke = grey, width = 1.0)

        let largest (s: QftStep) =
            s.Amplitudes |> Array.map (fun a -> a.Magnitude) |> Array.max

        let tip (f: float -> float) (s: QftStep) =
            let a = s.Amplitudes.[i] in f a.Phase * ar * a.Magnitude / largest s

        pic.Line(
            cx,
            ay,
            cx,
            ay,
            stroke = accent,
            width = 2.0,
            animate = [ "x2", per (fun s -> cx + tip sin s); "y2", per (fun s -> ay - tip cos s) ]
        )

        pic.Circle(cx, ay, 1.6, fill = accent)

    pic.FrameText(picW / 2.0, 532.0, per (fun s -> s.BarsCaption), size = 11.5, anchor = "middle")
    pic.Progress(16.0, 552.0, picW - 32.0, fill = accent)
    pic.Save path

printfn "Quantum Fourier Transform (%d qubits)\n" numQubits

// 1. Forward QFT on |000⟩  ->  uniform superposition
match QFT.execute numQubits backend QFT.defaultConfig with
| Error err ->
    eprintfn "QFT failed: %s" err.Message
    exit 1
| Ok qft ->
    printfn "Forward QFT applied: %d gates, %.2f ms" qft.GateCount qft.ExecutionTimeMs
    printfn "Measured distribution (expect ~uniform, ~%.1f%% each):" (100.0 / float (1 <<< numQubits))

    for (bitstring, count) in histogram qft.FinalState do
        printfn "  |%s⟩ : %5.1f%%" bitstring (100.0 * float count / float shots)

    // 2. Inverse QFT undoes it, recovering |000⟩
    match
        QFT.executeOnState
            qft.FinalState
            backend
            { QFT.defaultConfig with
                Inverse = true
            }
    with
    | Error err ->
        eprintfn "Inverse QFT failed: %s" err.Message
        exit 1
    | Ok inv ->
        printfn "\nInverse QFT applied — state should collapse back to |000⟩:"

        for (bitstring, count) in histogram inv.FinalState do
            printfn "  |%s⟩ : %5.1f%%" bitstring (100.0 * float count / float shots)

match svgPath (Path.Combine(__SOURCE_DIRECTORY__, "_images", "quantum-fourier-transform.svg")) with
| Some path -> drawPicture path
| None -> ()
