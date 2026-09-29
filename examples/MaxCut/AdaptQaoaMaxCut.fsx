/// ADAPT-QAOA — MaxCut with an adaptively-chosen mixer per layer
///
/// Standard QAOA repeats a fixed mixer (Σ Xᵢ). ADAPT-QAOA instead selects, at each new
/// layer, the mixer from a pool whose energy gradient is largest — yielding a shallower,
/// problem-tailored circuit. Each layer applies the cost evolution e^(-iγH) followed by
/// the chosen mixer e^(-iβA), starting from |+…+⟩.
///
/// Here we solve MaxCut on a frustrated triangle. The cost Hamiltonian H = Σ_edges ZᵢZⱼ
/// is minimised by anti-aligning qubits across edges; the triangle is frustrated, so the
/// best cut leaves one edge uncut → min ⟨H⟩ = -1 (cut value 2 of 3 edges).
///
/// Run with: dotnet fsi AdaptQaoaMaxCut.fsx
/// Add `--svg [path]` to also draw the run as an animated picture
/// (default: _images/adapt-qaoa-maxcut.svg next to this script).

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
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Algorithms
open SvgAnimation

let backend = LocalBackend.LocalBackend() :> IQuantumBackend

let pauli (ops: char[]) (c: float) : TrotterSuzuki.PauliString =
    {
        Operators = ops
        Coefficient = Complex(c, 0.0)
    }

// Triangle graph: edges (0,1), (1,2), (0,2). Cost H = Z0Z1 + Z1Z2 + Z0Z2.
let cost: TrotterSuzuki.PauliHamiltonian =
    {
        Terms =
            [
                pauli [| 'Z'; 'Z'; 'I' |] 1.0
                pauli [| 'I'; 'Z'; 'Z' |] 1.0
                pauli [| 'Z'; 'I'; 'Z' |] 1.0
            ]
        NumQubits = 3
    }

// Mixer pool: single-qubit X and Y rotations.
let pool =
    [
        pauli [| 'X'; 'I'; 'I' |] 1.0
        pauli [| 'I'; 'X'; 'I' |] 1.0
        pauli [| 'I'; 'I'; 'X' |] 1.0
        pauli [| 'Y'; 'I'; 'I' |] 1.0
        pauli [| 'I'; 'Y'; 'I' |] 1.0
        pauli [| 'I'; 'I'; 'Y' |] 1.0
    ]

// ============================================================================
// PICTURE (only with --svg): the state after every step of the run
// ============================================================================

let numQubits = cost.NumQubits
let qubits = [| 0 .. numQubits - 1 |]

/// The graph's edges, read from the cost terms: each ZZ term is one edge.
let edges =
    cost.Terms
    |> List.map (fun t ->
        let zs =
            t.Operators
            |> Array.indexed
            |> Array.filter (fun (_, p) -> p = 'Z')
            |> Array.map fst

        (zs.[0], zs.[1]))
    |> Array.ofList

/// A pool mixer as words, e.g. "X on qubit 1".
let describe (mixer: TrotterSuzuki.PauliString) =
    let q = mixer.Operators |> Array.findIndex (fun p -> p <> 'I')
    sprintf "%c on qubit %d" mixer.Operators.[q] q

/// Amplitudes (index bit q = qubit q) and ⟨H⟩ of the state a circuit prepares.
let measureCircuit (circuit: CircuitBuilder.Circuit) : Complex[] * float =
    let fail (e: QuantumError) =
        failwithf "state of a picture step: %s" e.Message

    match Primitives.getState backend circuit with
    | Ok(QuantumState.StateVector sv as state) ->
        let amps =
            Array.init (LocalSimulator.StateVector.dimension sv) (fun i -> LocalSimulator.StateVector.getAmplitude i sv)

        (amps, Primitives.expectation cost state |> Result.defaultWith fail)
    | Ok other -> failwithf "state of a picture step: expected a state vector, got %s" (other.GetType().Name)
    | Error e -> fail e

/// The gradient screen AdaptQaoa.run applies before each layer: append the mixer with the
/// seed γ₀ and take the central difference of ⟨H⟩ in β around 0.
let slope (mixers: TrotterSuzuki.PauliString list) (parameters: float[]) (mixer: TrotterSuzuki.PauliString) =
    let config = AdaptQaoa.defaultConfig

    let energyAt beta =
        AdaptQaoa.buildAnsatz numQubits cost (mixers @ [ mixer ]) (Array.append parameters [| config.GammaInit; beta |])
        |> measureCircuit
        |> snd

    (energyAt config.FiniteDiffEps - energyAt -config.FiniteDiffEps)
    / (2.0 * config.FiniteDiffEps)

type StepKind =
    | Start
    | Screen
    | CostStep
    | MixerStep
    | Stop

type Step =
    {
        Kind: StepKind
        Layer: int
        Label: string
        Caption: string * string
        Amplitudes: Complex[]
        Energy: float
        /// |slope| per pool mixer from the latest screen (empty before the first).
        Slopes: float[]
        /// Pool index of the mixer this layer uses.
        Chosen: int option
    }

let steps (result: AdaptQaoa.AdaptQaoaResult) : Step[] =
    let poolIndex (m: TrotterSuzuki.PauliString) =
        pool |> List.findIndex (fun p -> p.Operators = m.Operators)

    let threshold = AdaptQaoa.defaultConfig.GradientThreshold

    let firstLayers k =
        List.take k result.SelectedMixers, result.Parameters.[0 .. 2 * k - 1]

    let screen mixers parameters =
        pool |> List.map (slope mixers parameters >> abs) |> Array.ofList

    let a0, e0 = measureCircuit (AdaptQaoa.buildAnsatz numQubits cost [] [||])

    [|
        {
            Kind = Start
            Layer = 0
            Label = "Start: the equal superposition |+++⟩"
            Caption = ("Each qubit is 0 and 1 at once,", "so all 8 splits are equally likely")
            Amplitudes = a0
            Energy = e0
            Slopes = [||]
            Chosen = None
        }

        for k, mixer in List.indexed result.SelectedMixers do
            let mixersBefore, parametersBefore = firstLayers k
            let before = AdaptQaoa.buildAnsatz numQubits cost mixersBefore parametersBefore
            let gamma, beta = result.Parameters.[2 * k], result.Parameters.[2 * k + 1]
            let slopes = screen mixersBefore parametersBefore
            let chosen = poolIndex mixer
            let best = Array.max slopes

            let tied =
                [
                    for i in 0 .. slopes.Length - 1 do
                        if best - slopes.[i] < 1e-9 then
                            i
                ]

            if not (List.contains chosen tied) then
                failwithf "layer %d: the recomputed screen does not rank %s first" (k + 1) (describe mixer)

            let letterOf i =
                pool.[i].Operators |> Array.find (fun p -> p <> 'I')

            let qubitOf i =
                pool.[i].Operators |> Array.findIndex (fun p -> p <> 'I')

            let screenWords =
                match tied, tied |> List.map letterOf |> List.distinct with
                | [ _ ], _ -> (sprintf "%s lowers ⟨H⟩ fastest" (describe mixer), $"(slope %.2f{best})")
                | _, [ letter ] ->
                    (sprintf
                        "%c on qubits %s tie at slope %.2f;"
                        letter
                        (tied |> List.map (qubitOf >> string) |> String.concat ", ")
                        best,
                     sprintf "the run took qubit %d" (qubitOf chosen))
                | _ -> ($"%d{tied.Length} mixers tie at slope %.2f{best};", sprintf "the run took %s" (describe mixer))

            let aB, eB = measureCircuit before

            {
                Kind = Screen
                Layer = k + 1
                Label = sprintf "Layer %d: pick the mixer that lowers ⟨H⟩ fastest" (k + 1)
                Caption = screenWords
                Amplitudes = aB
                Energy = eB
                Slopes = slopes
                Chosen = Some chosen
            }

            let afterCost =
                cost.Terms
                |> List.fold (fun c term -> TrotterSuzuki.synthesizePauliEvolution term gamma qubits c) before

            let aC, eC = measureCircuit afterCost

            {
                Kind = CostStep
                Layer = k + 1
                Label = sprintf "Layer %d: cost step e^(-iγH) on all %d edges, γ = %.2f" (k + 1) edges.Length gamma
                Caption = ("The cost step turns each answer's arrow", "by γ × its energy; the bars do not move")
                Amplitudes = aC
                Energy = eC
                Slopes = slopes
                Chosen = Some chosen
            }

            let aM, eM =
                measureCircuit (TrotterSuzuki.synthesizePauliEvolution mixer beta qubits afterCost)

            {
                Kind = MixerStep
                Layer = k + 1
                Label = sprintf "Layer %d: mixer %s, β = %.2f" (k + 1) (describe mixer) beta
                Caption =
                    (sprintf "%s, β = %.2f: the arrows" (describe mixer) beta, "add up or cancel, and the bars move")
                Amplitudes = aM
                Energy = eM
                Slopes = slopes
                Chosen = Some chosen
            }

        let aF, eF =
            measureCircuit (AdaptQaoa.buildAnsatz numQubits cost result.SelectedMixers result.Parameters)

        {
            Kind = Stop
            Layer = result.Layers
            Label =
                if result.Converged then
                    $"Done: every mixer's slope is below %g{threshold}"
                else
                    $"Done: stopped after %d{result.Layers} layers"
            // Each cut edge adds -1 to H and each uncut edge +1, so the expected cut is (edges - ⟨H⟩) / 2.
            Caption = (sprintf "Expected cut: %.2f of %d edges" ((float edges.Length - eF) / 2.0) edges.Length, "")
            Amplitudes = aF
            Energy = eF
            Slopes = screen result.SelectedMixers result.Parameters
            Chosen = None
        }
    |]

let drawPicture (result: AdaptQaoa.AdaptQaoaResult) (path: string) =
    let steps = steps result
    let n = steps.Length
    let dim = 1 <<< numQubits
    let per (f: Step -> 'T) = Array.map f steps

    let bit (i: int) (q: int) = (i >>> q) &&& 1

    let bits (i: int) =
        String [| for q in qubits -> if bit i q = 1 then '1' else '0' |]

    let cutOf (i: int) =
        edges |> Array.filter (fun (a, b) -> bit i a <> bit i b) |> Array.length

    let bestCut = Array.init dim cutOf |> Array.max

    let prob (s: Step) (i: int) =
        let a = s.Amplitudes.[i] in a.Real * a.Real + a.Imaginary * a.Imaginary

    let pOne (s: Step) (q: int) =
        Seq.sum
            [
                for i in 0 .. dim - 1 do
                    if bit i q = 1 then
                        prob s i
            ]

    let pCut (s: Step) (a, b) =
        Seq.sum
            [
                for i in 0 .. dim - 1 do
                    if bit i a <> bit i b then
                        prob s i
            ]

    let pct (p: float) =
        sprintf "%g%%" (Math.Round(100.0 * p, 1))

    let picW, picH = 760.0, 546.0

    let accent, zeroColour, oneColour, bestColour =
        colour 3, colour 0, colour 4, colour 2

    let pic =
        Picture(
            picW,
            picH,
            frames = n,
            durationS = (float n * 3.6 |> max 15.0 |> min 20.0),
            title = "ADAPT-QAOA MaxCut",
            hold = 0.7
        )

    let shownIn (i: int) =
        Array.init n (fun k -> if k = i then "visible" else "hidden")

    // Text whose words change per frame while its y follows `ys` smoothly.
    let ridingText (x: float) (ys: float[]) (labels: string[]) (attrs: (string * string) list) =
        for i, label in Array.indexed labels do
            pic.Element(
                "text",
                [ "x", num x; "text-anchor", "middle" ] @ attrs,
                animate = [ "y", Array.map num ys; "visibility", shownIn i ],
                text = label
            )

    // Header.
    pic.Text(16.0, 30.0, "ADAPT-QAOA solves MaxCut on a triangle", size = 20.0, bold = true)

    pic.Text(
        16.0,
        50.0,
        "The algorithm grows a quantum circuit one layer at a time until all the chance sits on the best ways to cut the triangle.",
        size = 12.0,
        fill = grey
    )

    pic.FrameText(16.0, 78.0, per (fun s -> s.Label), size = 14.0, bold = true, fill = accent)

    // Left: the graph.
    let gx = 164.0
    pic.Rect(16.0, 92.0, 296.0, 234.0, fill = panel, stroke = frameColour, rx = 6.0)
    pic.Text(28.0, 110.0, "The triangle: one qubit per node", size = 12.0, bold = true)
    pic.Text(28.0, 124.0, "fill = chance the qubit reads 1 (blue 0%, orange 100%)", size = 10.5, fill = grey)

    let pos = [| (gx, 160.0); (gx - 80.0, 258.0); (gx + 80.0, 258.0) |]
    let centroid = (Array.averageBy fst pos, Array.averageBy snd pos)

    for (a, b) as edge in edges do
        let (x1, y1), (x2, y2) = pos.[a], pos.[b]
        let cutWidths = per (fun s -> 2.0 + 12.0 * pCut s edge)
        pic.Line(x1, y1, x2, y2, stroke = tint 0.45 ink, width = 2.0, animate = [ ("stroke-width", cutWidths) ])

        pic.Line(
            x1,
            y1,
            x2,
            y2,
            stroke = accent,
            width = 4.0,
            animate = [ ("opacity", per (fun s -> if s.Kind = CostStep then 1.0 else 0.0)) ]
        )

        let mx, my = (x1 + x2) / 2.0, (y1 + y2) / 2.0
        let dx, dy = mx - fst centroid, my - snd centroid
        let len = sqrt (dx * dx + dy * dy)
        let lx, ly = mx + 20.0 * dx / len, my + 20.0 * dy / len + 4.0

        let anchor =
            if dx / len > 0.3 then "start"
            elif dx / len < -0.3 then "end"
            else "middle"

        pic.FrameText(lx, ly, per (fun s -> "cut " + pct (pCut s edge)), size = 10.5, fill = grey, anchor = anchor)

    for q in qubits do
        let x, y = pos.[q]

        let mixerHere (s: Step) =
            match s.Kind, s.Chosen with
            | MixerStep, Some c -> pool.[c].Operators.[q] <> 'I'
            | _ -> false

        pic.Circle(
            x,
            y,
            31.0,
            stroke = accent,
            width = 3.0,
            animate = [ ("opacity", per (fun s -> if mixerHere s then 1.0 else 0.0)) ]
        )

        pic.Circle(x, y, 23.0, fill = tint 0.55 zeroColour)
        pic.Circle(x, y, 23.0, fill = oneColour, animate = [ ("opacity", per (fun s -> pOne s q)) ])
        pic.Circle(x, y, 23.0, stroke = ink, width = 1.5)
        pic.Text(x, y - 1.0, $"q%d{q}", size = 13.0, bold = true, anchor = "middle")
        pic.FrameText(x, y + 12.0, per (fun s -> pct (pOne s q)), size = 9.5, anchor = "middle")

    pic.FrameText(gx, 304.0, per (fun s -> fst s.Caption), size = 11.0, anchor = "middle")
    pic.FrameText(gx, 318.0, per (fun s -> snd s.Caption), size = 11.0, anchor = "middle")

    // Left, below: the mixer pool and each mixer's slope from the latest screen.
    pic.Rect(16.0, 334.0, 296.0, 84.0, fill = panel, stroke = frameColour, rx = 6.0)
    pic.Text(28.0, 351.0, "Mixer pool: how fast each lowers ⟨H⟩", size = 12.0, bold = true)
    let maxSlope = steps |> Array.collect (fun s -> s.Slopes) |> Array.fold max 1e-12

    for i, mixer in List.indexed pool do
        let col, row = i / numQubits, i % numQubits
        let x, y = 28.0 + 148.0 * float col, 368.0 + 16.0 * float row

        let width (s: Step) =
            if s.Slopes.Length = 0 then
                0.0
            else
                62.0 * s.Slopes.[i] / maxSlope

        let chosenHere (s: Step) = if s.Chosen = Some i then 1.0 else 0.0
        let letter = mixer.Operators |> Array.find (fun p -> p <> 'I')
        let q = mixer.Operators |> Array.findIndex (fun p -> p <> 'I')
        pic.Text(x, y, $"%c{letter} q%d{q}", size = 10.5)
        pic.Rect(x + 32.0, y - 8.0, 62.0, 9.0, fill = light, rx = 2.0)
        pic.Rect(x + 32.0, y - 8.0, 0.0, 9.0, fill = tint 0.3 grey, rx = 2.0, animate = [ ("width", per width) ])

        pic.Rect(
            x + 32.0,
            y - 8.0,
            0.0,
            9.0,
            fill = accent,
            rx = 2.0,
            animate = [ "width", per width; "opacity", per chosenHere ]
        )

        let value (s: Step) =
            if s.Slopes.Length = 0 then
                ""
            elif s.Slopes.[i] < AdaptQaoa.defaultConfig.GradientThreshold then
                $"<%g{AdaptQaoa.defaultConfig.GradientThreshold}"
            else
                sprintf "%.2f" s.Slopes.[i]

        pic.FrameText(x + 98.0, y, per value, size = 10.0, fill = grey)

    // Right: the chance of each answer, and its amplitude as an arrow.
    let rx0, rw = 324.0, 420.0
    pic.Rect(rx0, 92.0, rw, 326.0, fill = panel, stroke = frameColour, rx = 6.0)
    pic.Text(rx0 + 12.0, 110.0, "Chance of each answer", size = 12.0, bold = true)

    pic.Text(
        rx0 + 12.0,
        124.0,
        $"bits = q0 q1 q2; green = a best cut (%d{bestCut} of %d{edges.Length} edges)",
        size = 10.5,
        fill = grey
    )

    let bestShare (s: Step) =
        Seq.sum
            [
                for i in 0 .. dim - 1 do
                    if cutOf i = bestCut then
                        prob s i
            ]

    pic.FrameText(
        rx0 + rw - 12.0,
        110.0,
        per (fun s -> "best cuts: " + pct (bestShare s)),
        size = 12.0,
        bold = true,
        fill = bestColour,
        anchor = "end"
    )

    let maxP = steps |> Array.collect (prob >> Array.init dim) |> Array.max
    let top = ceil (maxP * 10.0 + 1e-9) / 10.0
    let baseY, barArea = 296.0, 150.0
    let yOf (p: float) = baseY - barArea * p / top
    let x0, slot = rx0 + 42.0, (rw - 54.0) / float dim

    for t in 1 .. int (round (top * 10.0)) do
        let p = float t / 10.0
        pic.Line(x0, yOf p, rx0 + rw - 12.0, yOf p, stroke = light, width = 1.0)
        pic.Text(x0 - 6.0, yOf p + 4.0, pct p, size = 10.0, fill = grey, anchor = "end")

    pic.Line(x0, baseY, rx0 + rw - 12.0, baseY, stroke = grey, width = 1.0)
    let order = Array.init dim id |> Array.sortBy bits

    let maxAmp =
        steps
        |> Array.collect (fun s -> s.Amplitudes |> Array.map (fun a -> a.Magnitude))
        |> Array.max

    for slotIndex, i in Array.indexed order do
        let cx = x0 + slot * (float slotIndex + 0.5)
        let best = cutOf i = bestCut
        let tops = per (fun s -> yOf (prob s i))
        let fill = if best then bestColour else tint 0.35 grey

        pic.Rect(
            cx - 14.0,
            baseY,
            28.0,
            0.0,
            fill = fill,
            animate = [ "y", tops; "height", per (fun s -> barArea * prob s i / top) ]
        )

        ridingText
            cx
            (tops |> Array.map (fun y -> y - 4.0))
            (per (fun s -> pct (prob s i)))
            [ "font-size", "9.5"; "fill", ink ]

        pic.Text(cx, baseY + 16.0, bits i, size = 12.0, anchor = "middle")

        pic.Text(
            cx,
            baseY + 29.0,
            sprintf "cut %d" (cutOf i),
            size = 9.5,
            fill = (if best then bestColour else grey),
            anchor = "middle"
        )

        let dialY, r = baseY + 54.0, 15.0
        pic.Circle(cx, dialY, r, fill = "white", stroke = frameColour)
        let tipX = per (fun s -> cx + r * s.Amplitudes.[i].Real / maxAmp)
        let tipY = per (fun s -> dialY - r * s.Amplitudes.[i].Imaginary / maxAmp)
        pic.Line(cx, dialY, cx, dialY, stroke = ink, width = 2.0, animate = [ "x2", tipX; "y2", tipY ])
        pic.Circle(cx, dialY, 1.8, fill = ink)

    pic.Text(
        rx0 + 12.0,
        392.0,
        "Arrows: each answer's amplitude. The cost step only turns them;",
        size = 10.5,
        fill = grey
    )

    pic.Text(
        rx0 + 12.0,
        406.0,
        "the mixer makes them add up or cancel, and that moves the bars.",
        size = 10.5,
        fill = grey
    )

    // Bottom: ⟨H⟩ after each step.
    pic.Rect(16.0, 426.0, picW - 32.0, 98.0, fill = panel, stroke = frameColour, rx = 6.0)
    pic.Text(28.0, 443.0, "Energy ⟨H⟩ after each step (lower is better)", size = 12.0, bold = true)

    pic.FrameText(
        picW - 28.0,
        443.0,
        per (fun s -> sprintf "⟨H⟩ = %.3f" (if abs s.Energy < 5e-4 then 0.0 else s.Energy)),
        size = 12.0,
        bold = true,
        anchor = "end"
    )

    let energies = per (fun s -> s.Energy)
    let optimum = float (edges.Length - 2 * bestCut)

    let eHi, eLo =
        max 0.0 (Array.max energies) + 0.15, min optimum (Array.min energies) - 0.15

    let ex0, ex1, ey0, ey1 = 150.0, picW - 60.0, 456.0, 500.0

    let exOf (k: int) =
        ex0 + (ex1 - ex0) * float k / float (max 1 (n - 1))

    let eyOf (e: float) =
        ey0 + (ey1 - ey0) * (eHi - e) / (eHi - eLo)

    pic.Line(ex0, eyOf 0.0, ex1, eyOf 0.0, stroke = light, width = 1.0)
    pic.Text(ex0 - 8.0, eyOf 0.0 + 4.0, "0", size = 10.0, fill = grey, anchor = "end")
    pic.Line(ex0, eyOf optimum, ex1, eyOf optimum, stroke = bestColour, width = 1.5, dash = "5 4")

    pic.Text(
        ex0 - 8.0,
        eyOf optimum + 4.0,
        $"best possible %g{optimum}",
        size = 10.0,
        fill = bestColour,
        anchor = "end"
    )

    let pathTo (k: int) =
        String.Join(
            " ",
            [
                for j in 0 .. n - 1 ->
                    sprintf
                        "%s%s,%s"
                        (if j = 0 then "M" else "L")
                        (num (exOf (min j k)))
                        (num (eyOf energies.[min j k]))
            ]
        )

    pic.Path(pathTo 0, stroke = accent, width = 2.0, shapes = Array.init n pathTo)

    pic.Circle(
        exOf 0,
        eyOf energies.[0],
        5.0,
        fill = accent,
        animate = [ "cx", Array.init n exOf; "cy", Array.map eyOf energies ]
    )

    let tick (s: Step) =
        let layer = if result.Layers > 1 then $" %d{s.Layer}" else ""

        match s.Kind with
        | Start -> "start"
        | Screen -> "pick" + layer
        | CostStep -> "cost" + layer
        | MixerStep -> "mixer" + layer
        | Stop -> "stop"

    for k in 0 .. n - 1 do
        pic.Text(exOf k, 516.0, tick steps.[k], size = 10.0, fill = grey, anchor = "middle")

    pic.Progress(16.0, 534.0, picW - 32.0, fill = accent)
    pic.Save path

printfn "ADAPT-QAOA — MaxCut on a frustrated triangle (3 nodes)\n"

match AdaptQaoa.run backend cost pool 3 AdaptQaoa.defaultConfig with
| Error e ->
    eprintfn "ADAPT-QAOA failed: %s" e.Message
    exit 1
| Ok result ->
    printfn "Converged      : %b" result.Converged
    printfn "Layers added   : %d (each = cost e^(-iγH) + one selected mixer e^(-iβA))" result.Layers

    result.SelectedMixers
    |> List.iteri (fun i m -> printfn "  layer %d mixer : %s" (i + 1) (String m.Operators))

    printfn
        "Min ⟨H⟩        : %.6f  (optimal = -1 → cut value %.0f of 3 edges)"
        result.Energy
        ((3.0 - result.Energy) / 2.0)

    printfn "Energy/layer   : %s" (result.EnergyHistory |> List.map (sprintf "%.4f") |> String.concat "  →  ")

    match svgPath (Path.Combine(__SOURCE_DIRECTORY__, "_images", "adapt-qaoa-maxcut.svg")) with
    | Some path -> drawPicture result path
    | None -> ()
