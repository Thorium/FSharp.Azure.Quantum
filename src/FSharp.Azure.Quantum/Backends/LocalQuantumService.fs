namespace FSharp.Azure.Quantum.Backends

open System
open System.Collections.Concurrent
open System.Globalization
open System.IO
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open Azure.Core
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.Types
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.LocalSimulator

/// Provider input decoding, local execution and provider result encoding used by
/// LocalQuantumService. Internal: the service's HTTP surface is the public contract.
module internal LocalQuantumServiceFormats =

    // ============================================================================
    // PROGRAM MODEL
    // ============================================================================

    /// One step of a decoded provider program.
    type Instruction =
        /// Apply a unitary (or barrier) gate.
        | Apply of CircuitBuilder.Gate
        /// Measure a qubit, writing the outcome into a reported classical bit (None = not reported).
        | MeasureInto of qubit: int * bit: int option
        /// Reset a qubit to |0⟩.
        | ResetQubit of qubit: int
        /// Apply a gate when the last measurement of `qubit` read 1.
        | IfMeasured of qubit: int * gate: CircuitBuilder.Gate

    /// A provider program decoded into gates plus a classical readout layout.
    type DecodedProgram =
        {
            /// Qubits the program uses (highest index + 1, or the declared register size)
            NumQubits: int
            /// Width of the reported classical register (bit 0 = rightmost character of a result key)
            ClassicalBits: int
            /// Instructions in program order
            Instructions: Instruction list
            /// Leniencies applied while decoding (e.g. implicit measurement), reported on the job
            Warnings: string list
        }

    /// Azure Quantum gate providers the service emulates.
    type Provider =
        | IonQ
        | Rigetti
        | Quantinuum
        | Iqm
        | AtomComputing

    /// Provider for a target id ("ionq.simulator" → IonQ), by its provider prefix.
    let providerOfTarget (target: string) : Provider option =
        let prefix =
            match target.IndexOf '.' with
            | -1 -> target
            | i -> target.Substring(0, i)

        match prefix.ToLowerInvariant() with
        | "ionq" -> Some IonQ
        | "rigetti" -> Some Rigetti
        | "quantinuum"
        | "honeywell" -> Some Quantinuum
        | "iqm" -> Some Iqm
        | "atom-computing" -> Some AtomComputing
        | _ -> None

    /// Azure Quantum provider id reported on the job.
    let providerId (provider: Provider) : string =
        match provider with
        | IonQ -> "ionq"
        | Rigetti -> "rigetti"
        | Quantinuum -> "quantinuum"
        | Iqm -> "iqm"
        | AtomComputing -> "atom-computing"

    /// Error code the provider reports for a program it cannot decode or run
    /// (the codes the library's map*Error functions translate to ValidationError).
    let invalidProgramCode (provider: Provider) : string =
        match provider with
        | Rigetti -> "InvalidProgram"
        | _ -> "InvalidCircuit"

    /// Output data format reported on the job for the provider's result blob.
    let outputDataFormat (provider: Provider) : string =
        match provider with
        | IonQ -> "ionq.quantum-results.v1"
        | Rigetti -> "rigetti.quil-results.v1"
        | Quantinuum -> "honeywell.quantum-results.v1"
        | Iqm
        | AtomComputing -> "microsoft.quantum-results.v1"

    /// Terminal "measure every qubit into the same-numbered bit" readout: what an IonQ
    /// circuit means (its format has no measurement instruction; every qubit is read).
    let private measureAll (count: int) : Instruction list =
        List.init count (fun q -> MeasureInto(q, Some q))

    let private hasMeasurement (instructions: Instruction list) =
        instructions
        |> List.exists (function
            | MeasureInto _ -> true
            | _ -> false)

    /// Like the real providers, the service reads out only what the program measures: an
    /// unmeasured classical bit reads 0 on every shot. The warning on the job says so.
    let private noMeasurementWarning =
        "The program contains no measurement, so every classical bit reads 0 on every shot, as it "
        + "would on the real provider."

    // ============================================================================
    // IONQ JSON (ionq.circuit.v1)
    // ============================================================================

    /// Decode an IonQ `ionq.circuit.v1` JSON circuit ({"qubits": n, "circuit": [...]}).
    /// IonQ measures every qubit at the end of the circuit, so the readout is always
    /// the full register; explicit "measure" entries are accepted and ignored.
    let decodeIonQ (json: string) : Result<DecodedProgram, string> =
        let intList (element: JsonElement) : int list =
            match element.ValueKind with
            | JsonValueKind.Number -> [ element.GetInt32() ]
            | JsonValueKind.Array -> element.EnumerateArray() |> Seq.map (fun e -> e.GetInt32()) |> List.ofSeq
            | kind -> failwith $"expected a qubit index or an array of indices, got {kind}"

        let property (name: string) (element: JsonElement) =
            match element.TryGetProperty name with
            | true, value when value.ValueKind <> JsonValueKind.Null -> Some value
            | _ -> None

        let decodeGate (index: int) (gate: JsonElement) : Result<Instruction list, string> =
            match property "gate" gate with
            | None -> Error $"Gate #{index} has no 'gate' name"
            | Some nameElement ->
                let name = nameElement.GetString().ToLowerInvariant()

                let targets =
                    [ property "target" gate; property "targets" gate ]
                    |> List.choose id
                    |> List.collect intList

                let controls =
                    [ property "control" gate; property "controls" gate ]
                    |> List.choose id
                    |> List.collect intList

                let rotation = property "rotation" gate |> Option.map (fun r -> r.GetDouble())

                let perTarget (make: int -> CircuitBuilder.Gate) =
                    if targets.IsEmpty then
                        Error $"Gate #{index} ('{name}') has no target"
                    else
                        Ok(targets |> List.map (make >> Apply))

                let rotationGate (make: int * float -> CircuitBuilder.Gate) =
                    match rotation with
                    | None -> Error $"Gate #{index} ('{name}') has no 'rotation'"
                    | Some angle -> perTarget (fun q -> make (q, angle))

                let single (target: string) (make: int -> CircuitBuilder.Gate) =
                    if controls.IsEmpty then
                        perTarget make
                    else
                        Error $"Gate #{index}: controlled '{target}' is not supported by LocalQuantumService"

                match name, controls, targets with
                | "measure", _, _ -> Ok [] // IonQ always measures the whole register
                | ("x" | "not" | "cnot" | "cx"), [ c ], [ t ] -> Ok [ Apply(CircuitBuilder.CNOT(c, t)) ]
                | ("x" | "not" | "cnot" | "cx"), [ c1; c2 ], [ t ] -> Ok [ Apply(CircuitBuilder.CCX(c1, c2, t)) ]
                | ("z" | "cz"), [ c ], [ t ] -> Ok [ Apply(CircuitBuilder.CZ(c, t)) ]
                | "z", (_ :: _ :: _ as cs), [ t ] -> Ok [ Apply(CircuitBuilder.MCZ(cs, t)) ]
                | "swap", [], [ a; b ]
                | "swap", [ a ], [ b ] -> Ok [ Apply(CircuitBuilder.SWAP(a, b)) ]
                | "h", _, _ -> single name CircuitBuilder.H
                | ("x" | "not"), _, _ -> single name CircuitBuilder.X
                | "y", _, _ -> single name CircuitBuilder.Y
                | "z", _, _ -> single name CircuitBuilder.Z
                | "s", _, _ -> single name CircuitBuilder.S
                | "si", _, _ -> single name CircuitBuilder.SDG
                | "t", _, _ -> single name CircuitBuilder.T
                | "ti", _, _ -> single name CircuitBuilder.TDG
                // v = √X = RX(π/2) and vi = RX(-π/2), equal up to global phase
                | "v", _, _ -> single name (fun q -> CircuitBuilder.RX(q, Math.PI / 2.0))
                | "vi", _, _ -> single name (fun q -> CircuitBuilder.RX(q, -Math.PI / 2.0))
                | "rx", [], _ -> rotationGate CircuitBuilder.RX
                | "ry", [], _ -> rotationGate CircuitBuilder.RY
                | "rz", [], _ -> rotationGate CircuitBuilder.RZ
                | _ -> Error $"Gate #{index}: unsupported IonQ gate '{name}' with {controls.Length} control(s) and {targets.Length} target(s)"

        try
            use doc = JsonDocument.Parse json
            let root = doc.RootElement

            match property "gateset" root with
            | Some gateset when gateset.ValueKind = JsonValueKind.String && gateset.GetString() = "native" ->
                Error "IonQ native gateset (gpi/gpi2/ms) is not supported by LocalQuantumService"
            | _ ->
                match property "qubits" root, property "circuit" root with
                | None, _ -> Error "IonQ circuit JSON is missing 'qubits'"
                | _, None -> Error "IonQ circuit JSON is missing 'circuit'"
                | Some _, Some circuit when circuit.ValueKind <> JsonValueKind.Array ->
                    Error "IonQ 'circuit' must be an array of gates"
                | Some qubits, Some circuit ->
                    let numQubits = qubits.GetInt32()

                    let decoded =
                        circuit.EnumerateArray()
                        |> Seq.mapi decodeGate
                        |> Seq.fold
                            (fun acc next ->
                                match acc, next with
                                | Ok instructions, Ok more -> Ok(instructions @ more)
                                | (Error _ as e), _ -> e
                                | _, Error e -> Error e)
                            (Ok [])

                    decoded
                    |> Result.map (fun instructions ->
                        {
                            NumQubits = numQubits
                            ClassicalBits = numQubits
                            Instructions = instructions @ measureAll numQubits
                            Warnings = []
                        })
        with ex ->
            Error $"Invalid IonQ circuit JSON: {ex.Message}"

    // ============================================================================
    // RIGETTI QUIL
    // ============================================================================

    /// Evaluate a Quil parameter expression: numbers, `pi`, + - * / and parentheses.
    let private evaluateExpression (text: string) : Result<float, string> =
        let s = text.Replace(" ", "")
        let mutable pos = 0

        let peek () = if pos < s.Length then s.[pos] else '\000'

        let rec expr () =
            let mutable value = term ()

            while peek () = '+' || peek () = '-' do
                let op = peek ()
                pos <- pos + 1
                let rhs = term ()
                value <- if op = '+' then value + rhs else value - rhs

            value

        and term () =
            let mutable value = factor ()

            while peek () = '*' || peek () = '/' do
                let op = peek ()
                pos <- pos + 1
                let rhs = factor ()
                value <- if op = '*' then value * rhs else value / rhs

            value

        and factor () =
            match peek () with
            | '-' ->
                pos <- pos + 1
                -(factor ())
            | '+' ->
                pos <- pos + 1
                factor ()
            | '(' ->
                pos <- pos + 1
                let value = expr ()

                if peek () <> ')' then
                    failwith "missing ')'"

                pos <- pos + 1
                value
            | c when Char.IsLetter c ->
                let start = pos

                while Char.IsLetter(peek ()) do
                    pos <- pos + 1

                match s.Substring(start, pos - start).ToLowerInvariant() with
                | "pi" -> Math.PI
                | other -> failwith $"unknown identifier '{other}'"
            | _ ->
                let m = Regex.Match(s.Substring pos, @"^(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?")

                if not m.Success then
                    failwith $"unexpected character at '{s.Substring pos}'"

                pos <- pos + m.Length
                Double.Parse(m.Value, CultureInfo.InvariantCulture)

        try
            let value = expr ()

            if pos <> s.Length then
                Error $"Cannot evaluate parameter '{text}'"
            else
                Ok value
        with ex ->
            Error $"Cannot evaluate parameter '{text}': {ex.Message}"

    let private quilDeclare =
        Regex(@"^DECLARE\s+(\w+)\s+(BIT|OCTET|INTEGER|REAL)(?:\s*\[\s*(\d+)\s*\])?$", RegexOptions.IgnoreCase)

    let private quilMeasure =
        Regex(@"^MEASURE\s+(\d+)(?:\s+(\w+)(?:\s*\[\s*(\d+)\s*\])?)?$", RegexOptions.IgnoreCase)

    let private quilGate =
        Regex(@"^((?:DAGGER\s+)*)([A-Za-z][A-Za-z0-9_]*)(?:\s*\(([^)]*)\))?((?:\s+\d+)+)$")

    /// Decode a Rigetti Quil program (the text RigettiBackend.serializeProgram emits, plus
    /// common Quil: comments, PRAGMA, pi expressions, DAGGER, PHASE/CPHASE/CCNOT, RESET).
    /// The readout is the `ro` register: ro[i] holds the last MEASURE into ro[i] (0 when
    /// never written). A program with no MEASURE is measured qubit i → ro[i] at the end.
    let decodeQuil (quil: string) : Result<DecodedProgram, string> =
        let lines =
            quil.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.map (fun line ->
                match line.IndexOf '#' with
                | -1 -> line.Trim()
                | i -> line.Substring(0, i).Trim())
            |> Array.filter (fun line -> line <> "")

        let mutable roSize: int option = None
        let mutable error: string option = None
        let instructions = ResizeArray<Instruction>()
        let mutable maxQubit = -1

        let fail (lineNo: int) (message: string) =
            if error.IsNone then
                error <- Some $"Quil line {lineNo}: {message}"

        let noteQubits (qubits: int list) =
            for q in qubits do
                maxQubit <- max maxQubit q

        for lineIndex in 0 .. lines.Length - 1 do
            let line = lines.[lineIndex]
            let lineNo = lineIndex + 1
            let upper = line.ToUpperInvariant()

            if error.IsSome then
                ()
            elif upper.StartsWith "PRAGMA" || upper = "HALT" || upper = "NOP" || upper = "WAIT" then
                ()
            elif upper = "RESET" then
                if instructions.Count > 0 then
                    fail lineNo "a global RESET after other instructions is not supported by LocalQuantumService"
            elif quilDeclare.IsMatch line then
                let m = quilDeclare.Match line
                let size = if m.Groups.[3].Success then int m.Groups.[3].Value else 1

                if m.Groups.[1].Value = "ro" then
                    roSize <- Some size
            elif quilMeasure.IsMatch line then
                let m = quilMeasure.Match line
                let qubit = int m.Groups.[1].Value
                noteQubits [ qubit ]

                let bit =
                    if m.Groups.[2].Success && m.Groups.[2].Value = "ro" then
                        Some(if m.Groups.[3].Success then int m.Groups.[3].Value else 0)
                    else
                        None

                match bit, roSize with
                | Some _, None -> fail lineNo "MEASURE into 'ro' before 'DECLARE ro BIT[n]'"
                | Some b, Some size when b >= size -> fail lineNo $"ro[{b}] is outside the declared BIT[{size}]"
                | _ -> instructions.Add(MeasureInto(qubit, bit))
            elif upper.StartsWith "RESET " then
                match Int32.TryParse(line.Substring(6).Trim()) with
                | true, q ->
                    noteQubits [ q ]
                    instructions.Add(ResetQubit q)
                | _ -> fail lineNo $"malformed RESET: {line}"
            elif quilGate.IsMatch line then
                let m = quilGate.Match line
                let daggers = Regex.Matches(m.Groups.[1].Value, "DAGGER", RegexOptions.IgnoreCase).Count
                let inverse = daggers % 2 = 1
                let name = m.Groups.[2].Value.ToUpperInvariant()

                let qubits =
                    m.Groups.[4].Value.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)
                    |> Array.map int
                    |> List.ofArray

                let parameters =
                    if m.Groups.[3].Success then
                        m.Groups.[3].Value.Split(',') |> Array.map evaluateExpression |> List.ofArray
                    else
                        []

                noteQubits qubits
                let sign = if inverse then -1.0 else 1.0

                let gate: Result<CircuitBuilder.Gate option, string> =
                    match parameters |> List.tryPick (function Error e -> Some e | Ok _ -> None) with
                    | Some e -> Error e
                    | None ->
                        let angles = parameters |> List.map (function Ok v -> v | Error _ -> 0.0)

                        match name, angles, qubits with
                        | "I", [], [ _ ] -> Ok None
                        | "H", [], [ q ] -> Ok(Some(CircuitBuilder.H q))
                        | "X", [], [ q ] -> Ok(Some(CircuitBuilder.X q))
                        | "Y", [], [ q ] -> Ok(Some(CircuitBuilder.Y q))
                        | "Z", [], [ q ] -> Ok(Some(CircuitBuilder.Z q))
                        | "S", [], [ q ] -> Ok(Some(if inverse then CircuitBuilder.SDG q else CircuitBuilder.S q))
                        | "T", [], [ q ] -> Ok(Some(if inverse then CircuitBuilder.TDG q else CircuitBuilder.T q))
                        | "RX", [ a ], [ q ] -> Ok(Some(CircuitBuilder.RX(q, sign * a)))
                        | "RY", [ a ], [ q ] -> Ok(Some(CircuitBuilder.RY(q, sign * a)))
                        | "RZ", [ a ], [ q ] -> Ok(Some(CircuitBuilder.RZ(q, sign * a)))
                        | "PHASE", [ a ], [ q ] -> Ok(Some(CircuitBuilder.P(q, sign * a)))
                        | "CZ", [], [ c; t ] -> Ok(Some(CircuitBuilder.CZ(c, t)))
                        | "CNOT", [], [ c; t ] -> Ok(Some(CircuitBuilder.CNOT(c, t)))
                        | "SWAP", [], [ a; b ] -> Ok(Some(CircuitBuilder.SWAP(a, b)))
                        | "CPHASE", [ a ], [ c; t ] -> Ok(Some(CircuitBuilder.CP(c, t, sign * a)))
                        | "CCNOT", [], [ c1; c2; t ] -> Ok(Some(CircuitBuilder.CCX(c1, c2, t)))
                        | _ ->
                            Error
                                $"unsupported Quil gate '{name}' with {angles.Length} parameter(s) on {qubits.Length} qubit(s)"

                match gate with
                | Ok(Some g) -> instructions.Add(Apply g)
                | Ok None -> ()
                | Error e -> fail lineNo e
            else
                fail lineNo $"unsupported Quil instruction '{line}'"

        match error with
        | Some e -> Error e
        | None ->
            let numQubits = max 1 (maxQubit + 1)
            let classicalBits = roSize |> Option.defaultValue numQubits
            let program = List.ofSeq instructions

            if hasMeasurement program then
                Ok
                    {
                        NumQubits = numQubits
                        ClassicalBits = classicalBits
                        Instructions = program
                        Warnings = []
                    }
            else
                Ok
                    {
                        NumQubits = numQubits
                        ClassicalBits = classicalBits
                        Instructions = program
                        Warnings = [ noMeasurementWarning ]
                    }

    // ============================================================================
    // OPENQASM (Quantinuum, IQM, Atom Computing)
    // ============================================================================

    /// Decode OpenQASM 2.0/3.0 through the library's OpenQasmImport parser. The readout
    /// register spans every qubit (OpenQasmExport declares `creg c[n]` and measures
    /// q[i] -> c[i]); a program with no measurement reads all zeros.
    let decodeOpenQasm (qasm: string) : Result<DecodedProgram, string> =
        match OpenQasmImport.parse qasm with
        | Error e -> Error $"OpenQASM parse error: {e}"
        | Ok circuit ->
            let program =
                CircuitBuilder.getGates circuit
                |> List.map (fun gate ->
                    match gate with
                    | CircuitBuilder.Measure q -> MeasureInto(q, Some q)
                    | CircuitBuilder.Reset q -> ResetQubit q
                    | CircuitBuilder.Conditional(q, inner) -> IfMeasured(q, inner)
                    | g -> Apply g)

            let numQubits = circuit.QubitCount

            if hasMeasurement program then
                Ok
                    {
                        NumQubits = numQubits
                        ClassicalBits = numQubits
                        Instructions = program
                        Warnings = []
                    }
            else
                Ok
                    {
                        NumQubits = numQubits
                        ClassicalBits = numQubits
                        Instructions = program
                        Warnings = [ noMeasurementWarning ]
                    }

    /// Decode job input by its `inputDataFormat`.
    let decode (inputDataFormat: string) (inputData: string) : Result<DecodedProgram, string> =
        let format = inputDataFormat.ToLowerInvariant()

        if format.StartsWith "ionq.circuit" then
            decodeIonQ inputData
        elif format.Contains "quil" then
            decodeQuil inputData
        elif format.Contains "qasm" then
            decodeOpenQasm inputData
        else
            Error
                $"Input data format '{inputDataFormat}' is not supported by LocalQuantumService (supported: ionq.circuit.v1, Quil, OpenQASM; QIR bitcode cannot be executed locally)"

    // ============================================================================
    // EXECUTION
    // ============================================================================

    /// Check that every qubit and classical-bit index is inside the program's registers.
    let private validateIndices (program: DecodedProgram) : Result<unit, string> =
        let badQubit q = q < 0 || q >= program.NumQubits

        let bad =
            program.Instructions
            |> List.tryPick (fun instruction ->
                match instruction with
                | Apply g
                | IfMeasured(_, g) when CircuitBuilder.getAffectedQubits g |> List.exists badQubit ->
                    Some $"gate {CircuitBuilder.getGateName g} uses a qubit outside 0..{program.NumQubits - 1}"
                | MeasureInto(q, _)
                | ResetQubit q
                | IfMeasured(q, _) when badQubit q -> Some $"qubit {q} is outside 0..{program.NumQubits - 1}"
                | MeasureInto(_, Some b) when b < 0 || b >= program.ClassicalBits ->
                    Some $"classical bit {b} is outside 0..{program.ClassicalBits - 1}"
                | _ -> None)

        match bad with
        | Some message -> Error message
        | None -> Ok()

    /// True when measurement can be sampled from the final state: no reset or classical
    /// control, and no gate acts on a qubit after it was measured.
    let private isTerminalMeasurementOnly (instructions: Instruction list) : bool =
        let mutable touchedLater = Set.empty
        let mutable ok = true

        for instruction in List.rev instructions do
            match instruction with
            | Apply(CircuitBuilder.Barrier _) -> ()
            | Apply g -> touchedLater <- Set.union touchedLater (Set.ofList (CircuitBuilder.getAffectedQubits g))
            | MeasureInto(q, _) ->
                if touchedLater.Contains q then
                    ok <- false
            | ResetQubit _
            | IfMeasured _ -> ok <- false

        ok

    /// Run a decoded program `shots` times on the local simulator.
    /// Returns one classical readout per shot (array index = classical bit).
    let execute (rng: Random) (shots: int) (program: DecodedProgram) : Result<bool[][], string> =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        let sampleStatic () : Result<bool[][], string> =
            let gates =
                program.Instructions
                |> List.choose (function
                    | Apply g -> Some g
                    | _ -> None)

            let readout =
                program.Instructions
                |> List.choose (function
                    | MeasureInto(q, Some b) -> Some(q, b)
                    | _ -> None)
                |> Array.ofList

            let circuit: CircuitBuilder.Circuit =
                {
                    QubitCount = program.NumQubits
                    Gates = List.rev gates
                }

            match backend.ExecuteToState(wrapCircuit circuit) with
            | Error e -> Error e.Message
            | Ok(QuantumState.StateVector sv) ->
                let probabilities = Measurement.getProbabilityDistribution sv
                let cumulative = Array.scan (+) 0.0 probabilities |> Array.tail
                let total = cumulative.[cumulative.Length - 1]

                let sampleIndex () =
                    let r = rng.NextDouble() * total
                    let mutable lo = 0
                    let mutable hi = cumulative.Length - 1

                    while lo < hi do
                        let mid = (lo + hi) / 2

                        if cumulative.[mid] > r then hi <- mid else lo <- mid + 1

                    lo

                Ok(
                    Array.init shots (fun _ ->
                        let index = sampleIndex ()
                        let bits = Array.zeroCreate program.ClassicalBits

                        for (q, b) in readout do
                            bits.[b] <- (index >>> q) &&& 1 = 1

                        bits)
                )
            | Ok other -> Error $"Local simulator returned an unexpected state type: {other.GetType().Name}"

        let sampleDynamic () : Result<bool[][], string> =
            let runShot () =
                let mutable state = StateVector.init program.NumQubits
                let outcomes = Collections.Generic.Dictionary<int, int>()
                let bits = Array.zeroCreate program.ClassicalBits

                let apply (gate: CircuitBuilder.Gate) =
                    match backend.ApplyOperation (QuantumOperation.Gate gate) (QuantumState.StateVector state) with
                    | Ok(QuantumState.StateVector next) -> state <- next
                    | Ok _ -> failwith "local simulator returned a non state-vector state"
                    | Error e -> failwith e.Message

                let measure (q: int) =
                    let outcome = Measurement.measureSingleQubit rng q state
                    state <- Measurement.collapseAfterMeasurement q outcome state
                    outcomes.[q] <- outcome
                    outcome

                for instruction in program.Instructions do
                    match instruction with
                    | Apply g -> apply g
                    | MeasureInto(q, bit) ->
                        let outcome = measure q
                        bit |> Option.iter (fun b -> bits.[b] <- (outcome = 1))
                    | ResetQubit q ->
                        if measure q = 1 then
                            apply (CircuitBuilder.X q)
                    | IfMeasured(q, g) ->
                        match outcomes.TryGetValue q with
                        | true, 1 -> apply g
                        | true, _ -> ()
                        | false, _ -> failwith $"conditional on qubit {q} before it was measured"

                bits

            Ok(Array.init shots (fun _ -> runShot ()))

        try
            validateIndices program
            |> Result.bind (fun () ->
                if isTerminalMeasurementOnly program.Instructions then
                    sampleStatic ()
                else
                    sampleDynamic ())
        with ex ->
            Error ex.Message

    // ============================================================================
    // RESULT ENCODING
    // ============================================================================

    /// Result key for one readout: rightmost character = classical bit 0 (the Azure
    /// histogram convention CloudBackendHelpers.histogramToQuantumState reads).
    let bitstring (bits: bool[]) : string =
        let n = bits.Length
        String(Array.init n (fun i -> if bits.[n - 1 - i] then '1' else '0'))

    /// Count readouts per bitstring.
    let histogram (readouts: bool[][]) : Map<string, int> =
        readouts |> Array.countBy bitstring |> Map.ofArray

    /// Serialize with a Utf8JsonWriter into a UTF-8 string.
    let writeJson (write: Utf8JsonWriter -> unit) : string =
        use stream = new MemoryStream()

        do
            use writer = new Utf8JsonWriter(stream)
            write writer
            writer.Flush()

        Encoding.UTF8.GetString(stream.ToArray())

    let private writeCounts (writer: Utf8JsonWriter) (name: string) (counts: Map<string, int>) =
        writer.WriteStartObject name

        for KeyValue(key, count) in counts do
            writer.WriteNumber(key, count)

        writer.WriteEndObject()

    /// Encode readouts as the provider's result blob, in the shape the library's result
    /// parsers read:
    /// - IonQ: {"histogram": {"<decimal basis index>": probability}} (ionq.quantum-results.v1)
    /// - Rigetti: {"ro": [[ro0, ro1, ...] per shot]} (rigetti.quil-results.v1)
    /// - Quantinuum: {"c": ["<bitstring>" per shot]} (honeywell.quantum-results.v1)
    /// - IQM / Atom Computing: {"results": {"<bitstring>": count}}
    /// Rigetti and Quantinuum get only the provider's native per-shot registers, so the
    /// library's parsers are tested against the format the real service returns.
    let encodeResult (provider: Provider) (readouts: bool[][]) : string =
        let counts = histogram readouts
        let shots = readouts.Length

        writeJson (fun writer ->
            writer.WriteStartObject()

            match provider with
            | IonQ ->
                writer.WriteStartObject "histogram"

                for KeyValue(key, count) in counts do
                    let index = if key.Length = 0 then 0L else Convert.ToInt64(key, 2)
                    writer.WriteNumber(index.ToString(CultureInfo.InvariantCulture), float count / float shots)

                writer.WriteEndObject()
            | Rigetti ->
                writer.WriteStartArray "ro"

                for bits in readouts do
                    writer.WriteStartArray()

                    for bit in bits do
                        writer.WriteNumberValue(if bit then 1 else 0)

                    writer.WriteEndArray()

                writer.WriteEndArray()
            | Quantinuum ->
                writer.WriteStartArray "c"

                for bits in readouts do
                    writer.WriteStringValue(bitstring bits)

                writer.WriteEndArray()
            | Iqm
            | AtomComputing -> writeCounts writer "results" counts

            writer.WriteEndObject())

// ================================================================================
// PUBLIC TYPES
// ================================================================================

/// Configuration for LocalQuantumService. Start from LocalQuantumService.defaultOptions.
type LocalQuantumServiceOptions =
    {
        /// TCP port on the loopback interface; 0 picks a free ephemeral port
        Port: int
        /// Subscription id in the emulated workspace URL
        SubscriptionId: string
        /// Resource group in the emulated workspace URL
        ResourceGroup: string
        /// Workspace name in the emulated workspace URL
        WorkspaceName: string
        /// Location used by CreateClientConfig (its data-plane host is routed to the service)
        Location: string
        /// Bearer token the service's Credential hands out and the workspace API accepts
        AccessToken: string
        /// When true, workspace requests without `Authorization: Bearer <AccessToken>` get 401
        RequireAuthentication: bool
        /// Widest program the service will simulate; wider jobs fail with TooManyQubits
        MaxQubits: int
        /// Shot count used when the job's inputParams carry none
        DefaultShots: int
        /// Seed for measurement sampling (job k uses seed + k); None = nondeterministic
        Seed: int option
        /// Status polls answered with Waiting/Executing before the final status is reported
        PollsBeforeCompletion: int
        /// Minimum time after submission before the final status is reported
        ExecutionDelay: TimeSpan
        /// Jobs per page on GET /jobs (further pages via nextLink)
        JobsPageSize: int
    }

/// A job accepted by LocalQuantumService (a snapshot; read LocalQuantumService.Jobs again for updates).
type LocalQuantumJob =
    {
        /// Client-chosen job id (the PUT /jobs/{id} path segment)
        JobId: string
        /// Job name from the submission
        Name: string
        /// Target id, e.g. "ionq.simulator"
        Target: string
        /// Provider id derived from the target, e.g. "ionq"
        ProviderId: string
        /// inputDataFormat from the submission
        InputDataFormat: string
        /// The submitted program text (IonQ JSON, Quil or OpenQASM)
        InputData: string
        /// Shot count the job ran with
        Shots: int
        /// Status most recently reported to a client
        Status: JobStatus
        /// Status the job ends in once the polling schedule allows it to be reported
        FinalStatus: JobStatus
        /// Distinct statuses reported so far, in order (starts with Waiting)
        StatusHistory: JobStatus list
        /// GET /jobs/{id} requests answered for this job
        StatusPolls: int
        /// Measured classical bitstrings (rightmost = bit 0) → counts; empty unless it succeeded
        Histogram: Map<string, int>
        /// Result blob served at the job's outputDataUri
        OutputData: string option
        /// Leniencies applied while running the job (e.g. implicit measurement)
        Warnings: string list
        /// When the service accepted the job
        CreationTime: DateTimeOffset
    }

/// One HTTP request the service answered (for asserting on the wire traffic in tests).
type LocalServiceRequest =
    {
        /// HTTP method
        Method: string
        /// Path and query as received
        PathAndQuery: string
        /// Whether the request carried an Authorization: Bearer header
        HadBearerToken: bool
        /// Status code the service answered with
        StatusCode: int
    }

/// TokenCredential that hands out a fixed dummy bearer token without contacting Azure AD.
/// Use it with Authentication.createAuthenticatedClient or anything else that takes a TokenCredential.
type LocalQuantumServiceCredential(token: string) =
    inherit TokenCredential()

    /// The token handed out.
    member _.Token = token

    override _.GetToken(_requestContext: TokenRequestContext, _cancellationToken: CancellationToken) =
        AccessToken(token, DateTimeOffset.UtcNow.AddHours 1.0)

    override _.GetTokenAsync(_requestContext: TokenRequestContext, _cancellationToken: CancellationToken) =
        ValueTask<AccessToken>(AccessToken(token, DateTimeOffset.UtcNow.AddHours 1.0))

/// HTTP handler that keeps a client on the local service: Azure Quantum data-plane
/// hosts (*.quantum.azure.com) are rewritten to the service's base URI, requests to the
/// service pass through, and any other host is refused, so nothing reaches the network.
type internal LocalServiceRoutingHandler(baseUri: Uri, inner: HttpMessageHandler) =
    inherit DelegatingHandler(inner)

    override _.SendAsync(request: HttpRequestMessage, cancellationToken: CancellationToken) =
        let uri = request.RequestUri

        if isNull uri then
            Task.FromException<HttpResponseMessage>(HttpRequestException "Request has no URI")
        elif uri.IsLoopback && uri.Port = baseUri.Port then
            base.SendAsync(request, cancellationToken)
        elif uri.Host.EndsWith(".quantum.azure.com", StringComparison.OrdinalIgnoreCase) then
            let builder = UriBuilder(uri)
            builder.Scheme <- baseUri.Scheme
            builder.Host <- baseUri.Host
            builder.Port <- baseUri.Port
            request.RequestUri <- builder.Uri
            base.SendAsync(request, cancellationToken)
        else
            Task.FromException<HttpResponseMessage>(
                HttpRequestException(
                    $"LocalQuantumService client refused to contact external host '{uri.Host}': only the local service and *.quantum.azure.com (routed locally) are reachable"
                )
            )

/// Mutable server-side state of one job.
type internal LocalJobEntry =
    {
        Sequence: int
        Id: string
        Name: string
        Target: string
        ProviderId: string
        InputDataFormat: string
        InputData: string
        InputParamsJson: string
        MetadataJson: string
        Shots: int
        Created: DateTimeOffset
        Final: JobStatus
        OutputData: string option
        OutputDataFormat: string
        Histogram: Map<string, int>
        Warnings: string list
        mutable Polls: int
        mutable Reported: JobStatus
        mutable History: JobStatus list // most recent first
        mutable BeginTime: DateTimeOffset option
        mutable EndTime: DateTimeOffset option
        mutable CancelTime: DateTimeOffset option
    }

/// Reply produced by the service's router.
type internal LocalServiceReply =
    {
        Status: int
        Body: string
        RetryAfterSeconds: int option
    }

/// Local emulator of the Azure Quantum workspace REST API that runs jobs on the library's
/// local simulator. Nothing starts until LocalQuantumService.start is called; the service
/// listens on the loopback interface only and never contacts the network.
///
/// Emulated API (paths under WorkspaceUrl, api-version accepted and ignored):
///   PUT    {workspace}/jobs/{id}          submit (201 Created, status Waiting)
///   GET    {workspace}/jobs/{id}          status (Waiting → Executing → Succeeded/Failed)
///   DELETE {workspace}/jobs/{id}          cancel (204)
///   POST   {workspace}/jobs/{id}/cancel   cancel (200)
///   GET    {workspace}/jobs               list ({"value": [...], "nextLink": ...})
///   POST   {workspace}/storage/sasUri     SAS URI for a container/blob in the local blob store
///   GET/PUT {base}/blobs/{container}/{blob}?...sig=...   SAS blob download/upload (no bearer token)
type LocalQuantumService internal (options: LocalQuantumServiceOptions, listener: HttpListener, baseUri: Uri) =

    static let workspacePrefix =
        @"(?:/subscriptions/[^/]+/resourceGroups/[^/]+/providers/Microsoft\.Quantum/workspaces/[^/]+)?"

    static let jobsRoute =
        Regex(
            "^(?<ws>" + workspacePrefix + @")/jobs(?:/(?<id>[^/]+)(?<cancel>/cancel)?)?/?$",
            RegexOptions.IgnoreCase ||| RegexOptions.Compiled
        )

    static let sasRoute =
        Regex("^" + workspacePrefix + @"/storage/sasUri/?$", RegexOptions.IgnoreCase ||| RegexOptions.Compiled)

    static let blobRoute =
        Regex(@"^/blobs/(?<container>[^/]+)(?:/(?<blob>.+))?$", RegexOptions.Compiled)

    let credential = LocalQuantumServiceCredential(options.AccessToken)

    let workspacePath =
        $"/subscriptions/%s{options.SubscriptionId}/resourceGroups/%s{options.ResourceGroup}/providers/Microsoft.Quantum/Workspaces/%s{options.WorkspaceName}"

    let baseText = baseUri.AbsoluteUri.TrimEnd('/')
    let workspaceUrl = baseText + workspacePath

    let jobs = ConcurrentDictionary<string, LocalJobEntry>(StringComparer.Ordinal)
    let blobs = ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal)
    let requests = ConcurrentQueue<LocalServiceRequest>()
    let injectionLock = obj ()
    let mutable failNext: (string * string) list = []
    let mutable rejectNext: HttpStatusCode list = []
    let sequence = ref 0
    let acceptSequence = ref 0
    let mutable disposed = false

    // ------------------------------------------------------------------------
    // JSON helpers
    // ------------------------------------------------------------------------

    let writeJson = LocalQuantumServiceFormats.writeJson

    let reply (status: int) (body: string) =
        {
            Status = status
            Body = body
            RetryAfterSeconds = None
        }

    let errorReply (status: int) (code: string) (message: string) =
        reply
            status
            (writeJson (fun w ->
                w.WriteStartObject()
                w.WriteStartObject "error"
                w.WriteString("code", code)
                w.WriteString("message", message)
                w.WriteEndObject()
                w.WriteEndObject()))

    let statusText (status: JobStatus) =
        match status with
        | JobStatus.Waiting -> "Waiting"
        | JobStatus.Executing -> "Executing"
        | JobStatus.Succeeded -> "Succeeded"
        | JobStatus.Failed _ -> "Failed"
        | JobStatus.Cancelled -> "Cancelled"

    let isTerminal (status: JobStatus) =
        match status with
        | JobStatus.Succeeded
        | JobStatus.Failed _
        | JobStatus.Cancelled -> true
        | JobStatus.Waiting
        | JobStatus.Executing -> false

    let timestamp (t: DateTimeOffset) =
        t.ToString("o", CultureInfo.InvariantCulture)

    let containerName (jobId: string) = $"job-{jobId}"

    let sasQuery = "sv=2019-02-02&sr=b&sp=r&sig=local-quantum-service"

    let blobUri (container: string) (blob: string) =
        $"{baseText}/blobs/{Uri.EscapeDataString container}/{Uri.EscapeDataString blob}?{sasQuery}"

    let writeJobJson (w: Utf8JsonWriter) (job: LocalJobEntry) =
        w.WriteStartObject()
        w.WriteString("id", job.Id)
        w.WriteString("name", job.Name)
        w.WriteString("providerId", job.ProviderId)
        w.WriteString("target", job.Target)
        w.WriteString("itemType", "Job")
        w.WriteString("jobType", "QuantumComputing")
        w.WriteString("containerUri", $"{baseText}/blobs/{Uri.EscapeDataString(containerName job.Id)}?{sasQuery}")
        w.WriteString("inputDataUri", blobUri (containerName job.Id) "inputData")
        w.WriteString("inputDataFormat", job.InputDataFormat)
        w.WritePropertyName "inputParams"
        w.WriteRawValue job.InputParamsJson
        w.WritePropertyName "metadata"
        w.WriteRawValue job.MetadataJson
        w.WriteString("outputDataFormat", job.OutputDataFormat)

        if job.Reported = JobStatus.Succeeded then
            w.WriteString("outputDataUri", blobUri (containerName job.Id) "rawOutputData")

        w.WriteString("status", statusText job.Reported)
        w.WriteString("creationTime", timestamp job.Created)
        job.BeginTime |> Option.iter (fun t -> w.WriteString("beginExecutionTime", timestamp t))
        job.EndTime |> Option.iter (fun t -> w.WriteString("endExecutionTime", timestamp t))
        job.CancelTime |> Option.iter (fun t -> w.WriteString("cancellationTime", timestamp t))

        match job.Reported with
        | JobStatus.Failed(code, message) ->
            w.WriteStartObject "errorData"
            w.WriteString("code", code)
            w.WriteString("message", message)
            w.WriteEndObject()
        | _ -> ()

        w.WriteEndObject()

    let jobJson (job: LocalJobEntry) = writeJson (fun w -> writeJobJson w job)

    let snapshot (job: LocalJobEntry) : LocalQuantumJob =
        lock job (fun () ->
            {
                JobId = job.Id
                Name = job.Name
                Target = job.Target
                ProviderId = job.ProviderId
                InputDataFormat = job.InputDataFormat
                InputData = job.InputData
                Shots = job.Shots
                Status = job.Reported
                FinalStatus = job.Final
                StatusHistory = List.rev job.History
                StatusPolls = job.Polls
                Histogram = job.Histogram
                OutputData = job.OutputData
                Warnings = job.Warnings
                CreationTime = job.Created
            })

    let report (job: LocalJobEntry) (status: JobStatus) (now: DateTimeOffset) =
        if status <> job.Reported then
            job.History <- status :: job.History

        if status <> JobStatus.Waiting && job.BeginTime.IsNone && status <> JobStatus.Cancelled then
            job.BeginTime <- Some now

        if isTerminal status && job.EndTime.IsNone && status <> JobStatus.Cancelled then
            job.EndTime <- Some now

        job.Reported <- status

    /// Answer one status poll, advancing the job along its reporting schedule.
    let poll (job: LocalJobEntry) : string =
        lock job (fun () ->
            job.Polls <- job.Polls + 1

            if not (isTerminal job.Reported) then
                let now = DateTimeOffset.UtcNow

                let ready =
                    job.Polls > options.PollsBeforeCompletion
                    && now - job.Created >= options.ExecutionDelay

                let next =
                    if ready then job.Final
                    elif job.Polls <= 1 && options.PollsBeforeCompletion >= 2 then JobStatus.Waiting
                    else JobStatus.Executing

                report job next now

            jobJson job)

    let cancel (job: LocalJobEntry) : Result<string, string> =
        lock job (fun () ->
            if isTerminal job.Reported then
                Error $"Job {job.Id} is already {statusText job.Reported}"
            else
                let now = DateTimeOffset.UtcNow
                job.CancelTime <- Some now
                report job JobStatus.Cancelled now
                Ok(jobJson job))

    // ------------------------------------------------------------------------
    // Submission
    // ------------------------------------------------------------------------

    let tryProperty (name: string) (element: JsonElement) =
        match element.TryGetProperty name with
        | true, value when value.ValueKind <> JsonValueKind.Null -> Some value
        | _ -> None

    let readShots (inputParams: JsonElement option) : Result<int, string> =
        let fromElement (e: JsonElement) =
            match e.ValueKind with
            | JsonValueKind.Number ->
                match e.TryGetInt32() with
                | true, v -> Some v
                | _ -> None
            | JsonValueKind.String ->
                match Int32.TryParse(e.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture) with
                | true, v -> Some v
                | _ -> None
            | _ -> None

        let requested =
            match inputParams with
            | Some p when p.ValueKind = JsonValueKind.Object ->
                [ "shots"; "count"; "shotCount" ]
                |> List.tryPick (fun name -> tryProperty name p |> Option.bind fromElement)
            | _ -> None

        let shots = requested |> Option.defaultValue options.DefaultShots

        if shots < 1 || shots > 1_000_000 then
            Error $"Shot count must be between 1 and 1000000, got {shots}"
        else
            Ok shots

    /// Local blob key for a URI the service handed out, if it points at this service.
    let localBlobKey (uriText: string) : string option =
        match Uri.TryCreate(uriText, UriKind.Absolute) with
        | true, uri when uri.IsLoopback && uri.Port = baseUri.Port ->
            let m = blobRoute.Match uri.AbsolutePath

            if m.Success && m.Groups.["blob"].Success then
                Some(Uri.UnescapeDataString m.Groups.["container"].Value + "/" + Uri.UnescapeDataString m.Groups.["blob"].Value)
            else
                None
        | _ -> None

    let readInputData (payload: JsonElement) : Result<string, string> =
        match tryProperty "inputData" payload, tryProperty "inputDataUri" payload with
        | Some data, _ when data.ValueKind = JsonValueKind.String -> Ok(data.GetString())
        | Some data, _ when data.ValueKind = JsonValueKind.Object || data.ValueKind = JsonValueKind.Array ->
            Ok(data.GetRawText())
        | Some data, _ -> Error $"inputData must be a string or JSON object, got {data.ValueKind}"
        | None, Some uri when uri.ValueKind = JsonValueKind.String ->
            match localBlobKey (uri.GetString()) with
            | Some key ->
                match blobs.TryGetValue key with
                | true, bytes -> Ok(Encoding.UTF8.GetString bytes)
                | _ -> Error $"inputDataUri blob '{key}' has not been uploaded"
            | None -> Error "inputDataUri must point at this LocalQuantumService's blob store (external URIs are never fetched)"
        | _ -> Error "Job has neither inputData nor inputDataUri"

    /// Decode and run a submission, producing (final status, output, histogram, warnings).
    let run (provider: LocalQuantumServiceFormats.Provider) (format: string) (input: string) (shots: int) =
        let injected =
            lock injectionLock (fun () ->
                match failNext with
                | next :: rest ->
                    failNext <- rest
                    Some next
                | [] -> None)

        match injected with
        | Some(code, message) -> (JobStatus.Failed(code, message), None, Map.empty, [])
        | None ->
            let invalid = LocalQuantumServiceFormats.invalidProgramCode provider

            match LocalQuantumServiceFormats.decode format input with
            | Error message -> (JobStatus.Failed(invalid, message), None, Map.empty, [])
            | Ok program when program.NumQubits > options.MaxQubits ->
                let message =
                    $"Program uses {program.NumQubits} qubits; LocalQuantumService is configured for at most {options.MaxQubits}"

                (JobStatus.Failed("TooManyQubits", message), None, Map.empty, program.Warnings)
            | Ok program ->
                let seq = Interlocked.Increment(&sequence.contents)

                let rng =
                    match options.Seed with
                    | Some seed -> Random(seed + seq)
                    | None -> Random()

                match LocalQuantumServiceFormats.execute rng shots program with
                | Error message ->
                    (JobStatus.Failed(invalid, $"Execution failed: {message}"), None, Map.empty, program.Warnings)
                | Ok readouts ->
                    let output = LocalQuantumServiceFormats.encodeResult provider readouts
                    (JobStatus.Succeeded, Some output, LocalQuantumServiceFormats.histogram readouts, program.Warnings)

    let submit (urlJobId: string) (body: string) : LocalServiceReply =
        let parsed =
            try
                use doc = JsonDocument.Parse body
                Ok(doc.RootElement.Clone())
            with ex ->
                Error ex.Message

        match parsed with
        | Error message -> errorReply 400 "InvalidJobDefinition" $"Request body is not valid JSON: {message}"
        | Ok payload when payload.ValueKind <> JsonValueKind.Object ->
            errorReply 400 "InvalidJobDefinition" "Request body must be a JSON object"
        | Ok payload ->
            let bodyId = tryProperty "id" payload |> Option.map (fun e -> e.GetString())
            let target = tryProperty "target" payload |> Option.map (fun e -> e.GetString())

            let rejection =
                lock injectionLock (fun () ->
                    match rejectNext with
                    | next :: rest ->
                        rejectNext <- rest
                        Some next
                    | [] -> None)

            match rejection with
            | Some status ->
                { errorReply (int status) "InjectedRejection" "LocalQuantumService rejected this submission (injected fault)" with
                    RetryAfterSeconds = Some 1
                }
            | None ->
                match bodyId, target with
                | Some id, _ when id <> urlJobId ->
                    errorReply 400 "InvalidJobDefinition" $"Body id '{id}' does not match the URL job id '{urlJobId}'"
                | _, None -> errorReply 400 "InvalidJobDefinition" "Job has no 'target'"
                | _, Some target ->
                    match LocalQuantumServiceFormats.providerOfTarget target with
                    | None ->
                        errorReply
                            400
                            "InvalidTarget"
                            $"Target '{target}' is not supported by LocalQuantumService (providers: ionq, rigetti, quantinuum, iqm, atom-computing)"
                    | Some provider ->
                        let inputParams = tryProperty "inputParams" payload

                        match readInputData payload, readShots inputParams with
                        | Error message, _
                        | _, Error message -> errorReply 400 "InvalidJobDefinition" message
                        | Ok input, Ok shots ->
                            let format =
                                tryProperty "inputDataFormat" payload
                                |> Option.map (fun e -> e.GetString())
                                |> Option.defaultValue ""

                            let rawObject (element: JsonElement option) =
                                match element with
                                | Some e when e.ValueKind = JsonValueKind.Object -> e.GetRawText()
                                | _ -> "{}"

                            let final, output, counts, warnings = run provider format input shots

                            let entry =
                                {
                                    Sequence = Interlocked.Increment(&acceptSequence.contents)
                                    Id = urlJobId
                                    Name =
                                        tryProperty "name" payload
                                        |> Option.map (fun e -> e.GetString())
                                        |> Option.defaultValue urlJobId
                                    Target = target
                                    ProviderId = LocalQuantumServiceFormats.providerId provider
                                    InputDataFormat = format
                                    InputData = input
                                    InputParamsJson = rawObject inputParams
                                    MetadataJson = rawObject (tryProperty "metadata" payload)
                                    Shots = shots
                                    Created = DateTimeOffset.UtcNow
                                    Final = final
                                    OutputData = output
                                    OutputDataFormat = LocalQuantumServiceFormats.outputDataFormat provider
                                    Histogram = counts
                                    Warnings = warnings
                                    Polls = 0
                                    Reported = JobStatus.Waiting
                                    History = [ JobStatus.Waiting ]
                                    BeginTime = None
                                    EndTime = None
                                    CancelTime = None
                                }

                            if jobs.TryAdd(urlJobId, entry) then
                                let container = containerName urlJobId
                                blobs.[container + "/inputData"] <- Encoding.UTF8.GetBytes input

                                output
                                |> Option.iter (fun data -> blobs.[container + "/rawOutputData"] <- Encoding.UTF8.GetBytes data)

                                reply 201 (jobJson entry)
                            else
                                errorReply 409 "Conflict" $"Job '{urlJobId}' already exists"

    // ------------------------------------------------------------------------
    // Routing
    // ------------------------------------------------------------------------

    let listJobs (wsPrefix: string) (query: string) : LocalServiceReply =
        let parameters = Web.HttpUtility.ParseQueryString query

        let skip =
            match Int32.TryParse(parameters.["skipToken"]) with
            | true, v when v > 0 -> v
            | _ -> 0

        let ordered = jobs.Values |> Seq.sortBy (fun j -> j.Sequence) |> Array.ofSeq
        let pageSize = max 1 options.JobsPageSize
        let page = ordered |> Array.skip (min skip ordered.Length) |> Array.truncate pageSize
        let next = skip + page.Length

        reply
            200
            (writeJson (fun w ->
                w.WriteStartObject()
                w.WriteStartArray "value"

                for job in page do
                    lock job (fun () -> writeJobJson w job)

                w.WriteEndArray()

                if next < ordered.Length then
                    w.WriteString(
                        "nextLink",
                        $"{baseText}{wsPrefix}/jobs?api-version=2022-09-12-preview&skipToken={next}"
                    )
                else
                    w.WriteNull "nextLink"

                w.WriteEndObject()))

    let sasUri (body: string) : LocalServiceReply =
        try
            use doc = JsonDocument.Parse(if String.IsNullOrWhiteSpace body then "{}" else body)
            let root = doc.RootElement

            match tryProperty "containerName" root |> Option.map (fun e -> e.GetString()) with
            | None -> errorReply 400 "InvalidRequest" "containerName is required"
            | Some container ->
                let uri =
                    match tryProperty "blobName" root |> Option.map (fun e -> e.GetString()) with
                    | Some blob -> blobUri container blob
                    | None -> $"{baseText}/blobs/{Uri.EscapeDataString container}?{sasQuery}"

                reply 200 (writeJson (fun w -> w.WriteStartObject(); w.WriteString("sasUri", uri); w.WriteEndObject()))
        with ex ->
            errorReply 400 "InvalidRequest" ex.Message

    let blobRequest (httpMethod: string) (url: Uri) (hasBearer: bool) (bodyBytes: byte[]) : LocalServiceReply =
        let m = blobRoute.Match url.AbsolutePath
        let parameters = Web.HttpUtility.ParseQueryString url.Query

        if hasBearer then
            // Azure Storage rejects a SAS request that also carries a bearer token meant
            // for the workspace; the library must mark these requests NoAuth.
            errorReply 401 "InvalidAuthenticationInfo" "Blob requests are authorised by their SAS query; a bearer token must not be sent"
        elif String.IsNullOrEmpty parameters.["sig"] then
            errorReply 403 "AuthenticationFailed" "Blob request has no SAS signature"
        else
            let container = Uri.UnescapeDataString m.Groups.["container"].Value

            match httpMethod, m.Groups.["blob"].Success with
            | "PUT", false -> reply 201 "" // create container
            | "PUT", true ->
                blobs.[container + "/" + Uri.UnescapeDataString m.Groups.["blob"].Value] <- bodyBytes
                reply 201 ""
            | "GET", true ->
                match blobs.TryGetValue(container + "/" + Uri.UnescapeDataString m.Groups.["blob"].Value) with
                | true, bytes -> reply 200 (Encoding.UTF8.GetString bytes)
                | _ -> errorReply 404 "BlobNotFound" "The specified blob does not exist"
            | _ -> errorReply 405 "UnsupportedHttpVerb" $"{httpMethod} is not supported on blobs"

    let authorized (authorization: string) =
        not options.RequireAuthentication
        || String.Equals(authorization, $"Bearer {options.AccessToken}", StringComparison.Ordinal)

    let route (httpMethod: string) (url: Uri) (authorization: string) (bodyBytes: byte[]) : LocalServiceReply =
        let path = url.AbsolutePath
        let hasBearer = not (String.IsNullOrEmpty authorization)
        let body = Encoding.UTF8.GetString bodyBytes

        if blobRoute.IsMatch path then
            blobRequest httpMethod url hasBearer bodyBytes
        elif not (authorized authorization) then
            errorReply 401 "Unauthorized" "Missing or invalid bearer token for the Azure Quantum workspace API"
        elif sasRoute.IsMatch path then
            if httpMethod = "POST" then
                sasUri body
            else
                errorReply 405 "MethodNotAllowed" $"{httpMethod} is not supported on storage/sasUri"
        else
            let m = jobsRoute.Match path

            if not m.Success then
                errorReply 404 "NotFound" $"LocalQuantumService has no route for {httpMethod} {path}"
            else
                let wsPrefix = m.Groups.["ws"].Value
                let isCancel = m.Groups.["cancel"].Success

                match m.Groups.["id"].Success with
                | false ->
                    if httpMethod = "GET" then
                        listJobs wsPrefix url.Query
                    else
                        errorReply 405 "MethodNotAllowed" $"{httpMethod} is not supported on the jobs collection"
                | true ->
                    let jobId = Uri.UnescapeDataString m.Groups.["id"].Value

                    match httpMethod, isCancel with
                    | "PUT", false -> submit jobId body
                    | "GET", false ->
                        match jobs.TryGetValue jobId with
                        | true, job -> reply 200 (poll job)
                        | _ -> errorReply 404 "JobNotFound" $"Job '{jobId}' not found"
                    | "DELETE", false
                    | "POST", true ->
                        match jobs.TryGetValue jobId with
                        | true, job ->
                            match cancel job with
                            | Ok json -> if isCancel then reply 200 json else reply 204 ""
                            | Error message -> errorReply 409 "Conflict" message
                        | _ -> errorReply 404 "JobNotFound" $"Job '{jobId}' not found"
                    | _ -> errorReply 405 "MethodNotAllowed" $"{httpMethod} is not supported on {path}"

    let handle (context: HttpListenerContext) : Task =
        task {
            let request = context.Request
            let response = context.Response

            try
                try
                    use buffer = new MemoryStream()

                    if request.HasEntityBody then
                        do! request.InputStream.CopyToAsync buffer

                    let authorization = request.Headers.["Authorization"]
                    let authorization = if isNull authorization then "" else authorization

                    let result =
                        try
                            route request.HttpMethod request.Url authorization (buffer.ToArray())
                        with ex ->
                            errorReply 500 "InternalError" ex.Message

                    requests.Enqueue
                        {
                            Method = request.HttpMethod
                            PathAndQuery = request.Url.PathAndQuery
                            HadBearerToken = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                            StatusCode = result.Status
                        }

                    response.StatusCode <- result.Status
                    result.RetryAfterSeconds |> Option.iter (fun s -> response.AddHeader("Retry-After", string s))

                    if result.Body <> "" then
                        let bytes = Encoding.UTF8.GetBytes result.Body
                        response.ContentType <- "application/json; charset=utf-8"
                        response.ContentLength64 <- int64 bytes.Length
                        do! response.OutputStream.WriteAsync(bytes, 0, bytes.Length)
                with _ ->
                    try
                        response.StatusCode <- 500
                    with _ ->
                        ()
            finally
                try
                    response.Close()
                with _ ->
                    ()
        }

    let acceptLoop () : Task =
        task {
            let mutable running = true

            while running do
                try
                    let! context = listener.GetContextAsync()
                    Task.Run(Func<Task>(fun () -> handle context)) |> ignore
                with _ ->
                    if disposed || not listener.IsListening then
                        running <- false
        }

    do acceptLoop () |> ignore

    /// Root URI the service listens on, e.g. http://127.0.0.1:52341/
    member _.BaseUri = baseUri

    /// Workspace URL to pass as `workspaceUrl` to the cloud backends and JobLifecycle functions.
    member _.WorkspaceUrl = workspaceUrl

    /// The options the service was started with.
    member _.Options = options

    /// Credential that hands out the service's dummy bearer token (never contacts Azure AD).
    member _.Credential: TokenCredential = credential :> TokenCredential

    /// Create an HttpClient wired like Authentication.createAuthenticatedClient (bearer-token
    /// AuthenticationHandler + ThrottlingHandler) but with the dummy credential, no proxy, and
    /// routing that sends *.quantum.azure.com to this service and refuses every other external
    /// host. The caller owns (and disposes) the client.
    member _.CreateHttpClient() : HttpClient =
        let sockets = new SocketsHttpHandler(UseProxy = false)
        let routing = new LocalServiceRoutingHandler(baseUri, sockets)
        let throttling = new RateLimiting.ThrottlingHandler(routing)

        let auth =
            new Authentication.AuthenticationHandler(
                new Authentication.TokenManager(credential),
                InnerHandler = throttling
            )

        new HttpClient(auth, true)

    /// A Client.QuantumClientConfig for this workspace. Its data-plane URL
    /// (https://{Location}.quantum.azure.com/...) is routed to the service by `httpClient`,
    /// which must come from CreateHttpClient.
    member _.CreateClientConfig(httpClient: HttpClient) : Client.QuantumClientConfig =
        Client.createConfig options.SubscriptionId options.ResourceGroup options.WorkspaceName options.Location httpClient

    /// Snapshots of every accepted job, in submission order.
    member _.Jobs: LocalQuantumJob list =
        jobs.Values |> Seq.sortBy (fun j -> j.Sequence) |> Seq.map snapshot |> List.ofSeq

    /// Snapshot of one job, if the service accepted it.
    member _.TryGetJob(jobId: string) : LocalQuantumJob option =
        match jobs.TryGetValue jobId with
        | true, job -> Some(snapshot job)
        | _ -> None

    /// Number of job submissions the service accepted (201 Created).
    member _.SubmittedJobCount = jobs.Count

    /// Every HTTP request answered so far, in arrival order.
    member _.Requests: LocalServiceRequest list = requests |> List.ofSeq

    /// Make the next `count` accepted jobs end in Failed with the given error code and message
    /// (default "BackendUnavailable", which the provider error mappers treat as a transient outage).
    member _.FailNextJobs(count: int, ?errorCode: string, ?errorMessage: string) =
        let code = defaultArg errorCode "BackendUnavailable"
        let message = defaultArg errorMessage "Injected failure from LocalQuantumService"

        lock injectionLock (fun () -> failNext <- failNext @ List.replicate (max 0 count) (code, message))

    /// Answer the next `count` job submissions with `statusCode` (e.g. 503) and Retry-After: 1,
    /// without creating a job.
    member _.RejectNextSubmissions(count: int, statusCode: HttpStatusCode) =
        lock injectionLock (fun () -> rejectNext <- rejectNext @ List.replicate (max 0 count) statusCode)

    /// Stop listening and release the port.
    member _.Stop() =
        if not disposed then
            disposed <- true

            try
                listener.Stop()
                listener.Close()
            with _ ->
                ()

    interface IDisposable with
        member this.Dispose() = this.Stop()

/// Start and configure the local Azure Quantum REST emulator.
///
/// Example:
///   use service = LocalQuantumService.start LocalQuantumService.defaultOptions
///   use http = service.CreateHttpClient()
///   let backend = CloudBackends.IonQCloudBackend(http, service.WorkspaceUrl, "ionq.simulator", 1000)
[<RequireQualifiedAccess>]
module LocalQuantumService =

    /// Defaults: ephemeral port, authentication required with a dummy token, up to 20 qubits,
    /// 500 shots when the job names none, nondeterministic sampling, final status on first poll.
    let defaultOptions: LocalQuantumServiceOptions =
        {
            Port = 0
            SubscriptionId = "00000000-0000-0000-0000-000000000000"
            ResourceGroup = "local-rg"
            WorkspaceName = "local-workspace"
            Location = "local"
            AccessToken = "local-quantum-service-token"
            RequireAuthentication = true
            MaxQubits = min 20 StateVector.maxQubits
            DefaultShots = 500
            Seed = None
            PollsBeforeCompletion = 0
            ExecutionDelay = TimeSpan.Zero
            JobsPageSize = 100
        }

    let private freePort () =
        let probe = new TcpListener(IPAddress.Loopback, 0)
        probe.Start()
        let port = (probe.LocalEndpoint :?> IPEndPoint).Port
        probe.Stop()
        port

    /// Start the service on the loopback interface (127.0.0.1; localhost on Windows), or return why it could not bind.
    let tryStart (options: LocalQuantumServiceOptions) : Result<LocalQuantumService, QuantumError> =
        let rec attempt remaining =
            let port = if options.Port > 0 then options.Port else freePort ()
            // Windows' http.sys lets non-administrators listen on "localhost" but not on
            // "127.0.0.1"; elsewhere bind the IPv4 loopback address explicitly.
            let host = if OperatingSystem.IsWindows() then "localhost" else "127.0.0.1"
            let prefix = $"http://{host}:{port}/"
            let listener = new HttpListener()
            listener.Prefixes.Add prefix

            try
                listener.Start()
                Ok(new LocalQuantumService(options, listener, Uri prefix))
            with ex ->
                (listener :> IDisposable).Dispose()

                if options.Port = 0 && remaining > 0 then
                    attempt (remaining - 1)
                else
                    Error(QuantumError.OperationError("LocalQuantumService", $"Cannot listen on {prefix}: {ex.Message}"))

        attempt 5

    /// Start the service on the loopback interface. Raises InvalidOperationException when no port can be bound.
    let start (options: LocalQuantumServiceOptions) : LocalQuantumService =
        match tryStart options with
        | Ok service -> service
        | Error err -> raise (InvalidOperationException err.Message)

    /// Start the service with defaultOptions.
    let startDefault () : LocalQuantumService = start defaultOptions
