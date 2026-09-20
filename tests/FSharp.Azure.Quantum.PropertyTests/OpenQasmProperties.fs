namespace FSharp.Azure.Quantum.PropertyTests

open System
open Xunit
open FsCheck.Xunit
open FsCheck
open FsCheck.FSharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.OpenQasmVersion

/// What OpenQASM export and import promise each other, over random circuits:
/// a circuit survives export and import in every version, the text itself is
/// a fixed point of that round trip, comments never change a parse, a circuit
/// that validation rejects is rejected by the importer too, angle expressions
/// mean what they say, and damaged text is answered with an Error, never an
/// exception.
module OpenQasmProperties =

    let private versions = [ V1_0; V2_0; V3_0 ]

    let private describe (circuit: Circuit) =
        $"{circuit.QubitCount} qubits, gates {List.rev circuit.Gates}"

    /// Every version, on circuits it can express.
    let private arbFor (version: QasmVersion) =
        match version with
        | V3_0 -> Circuits.arbCircuitV3
        | V1_0
        | V2_0 -> Circuits.arbCircuit

    let private sameCircuit (a: Circuit) (b: Circuit) =
        a.QubitCount = b.QubitCount
        && a.Gates.Length = b.Gates.Length
        && List.forall2 Circuits.gateClose a.Gates b.Gates

    let private roundTrip (version: QasmVersion) =
        let config = configFor version

        Prop.forAll (arbFor version) (fun circuit ->
            let qasm = OpenQasmExport.exportWithConfig config circuit

            match OpenQasmImport.parse qasm, OpenQasmImport.parseWithVersion version qasm, detectVersion qasm with
            | Ok parsed, Ok parsedV, Ok detected ->
                (sameCircuit circuit parsed && sameCircuit circuit parsedV && detected = version)
                |> Prop.label $"{describe circuit} came back as {List.rev parsed.Gates} (detected {detected})\n{qasm}"
            | parsed, parsedV, detected ->
                false
                |> Prop.label $"{describe circuit}: {parsed} / {parsedV} / {detected}\n{qasm}")
        |> Prop.classify true (versionToString version)

    [<Property(MaxTest = 300)>]
    let ``a circuit survives export and import in OpenQASM 1.0`` () = roundTrip V1_0

    [<Property(MaxTest = 300)>]
    let ``a circuit survives export and import in OpenQASM 2.0`` () = roundTrip V2_0

    [<Property(MaxTest = 300)>]
    let ``a circuit survives export and import in OpenQASM 3.0`` () = roundTrip V3_0

    [<Property(MaxTest = 300)>]
    let ``the exported text is a fixed point of import then export`` () =
        Prop.forAll (Gen.elements versions |> Arb.fromGen) (fun version ->
            let config = configFor version

            Prop.forAll (arbFor version) (fun circuit ->
                let qasm = OpenQasmExport.exportWithConfig config circuit

                match OpenQasmImport.parse qasm with
                | Ok parsed ->
                    let again = OpenQasmExport.exportWithConfig config parsed

                    (again = qasm)
                    |> Prop.label $"{versionToString version}\n{qasm}\n--- became ---\n{again}"
                | Error msg -> false |> Prop.label $"{describe circuit}: {msg}\n{qasm}"))

    [<Property(MaxTest = 300)>]
    let ``every generated circuit passes validation, and its export in 3.0 imports into one that does too`` () =
        Prop.forAll Circuits.arbCircuitV3 (fun circuit ->
            let qasm = OpenQasmExport.exportWithConfig (configFor V3_0) circuit
            let ownVerdict = OpenQasmExport.validate circuit

            let importedVerdict =
                OpenQasmImport.parse qasm |> Result.bind OpenQasmExport.validate

            (ownVerdict = Ok() && importedVerdict = Ok())
            |> Prop.label $"{describe circuit}: {ownVerdict} / {importedVerdict}")

    /// One gate of the circuit moved past the end of the register.
    let private genOutOfRange: Gen<Circuit * int> =
        gen {
            let! circuit = Circuits.genCircuit true

            if List.isEmpty circuit.Gates then
                return { QubitCount = 1; Gates = [ X 1 ] }, 0
            else
                let! i = Gen.choose (0, circuit.Gates.Length - 1)
                let moved = Circuits.mapQubits (fun q -> q + circuit.QubitCount) circuit.Gates[i]

                return
                    { circuit with
                        Gates = List.updateAt i moved circuit.Gates
                    },
                    i
        }

    [<Property(MaxTest = 300)>]
    let ``a gate on a qubit outside the register fails validation and is refused by the importer`` () =
        Prop.forAll (Arb.fromGen genOutOfRange) (fun (circuit, i) ->
            let qasm = OpenQasmExport.exportWithConfig (configFor V3_0) circuit
            let verdict = OpenQasmExport.validate circuit
            let imported = OpenQasmImport.parse qasm

            (Result.isError verdict && Result.isError imported)
            |> Prop.label $"{describe circuit} (gate {i} moved): validate {verdict}, import {imported}\n{qasm}")

    /// Line comments at line ends, block comments and blank lines between lines.
    let private genCommented (qasm: string) : Gen<string> =
        let lines = qasm.Split '\n'

        gen {
            let! decorated =
                lines
                |> Array.map (fun line ->
                    Gen.frequency
                        [
                            3, Gen.constant line
                            1, Gen.constant $"{line} // a remark; with semicolons"
                            1, Gen.constant $"{line}\n"
                            1, Gen.constant $"/* one-line block */ {line}"
                            1, Gen.constant $"{line}\n/* a block\n   over two lines */"
                        ])
                |> Array.toList
                |> Gen.sequenceToList

            return String.Join("\n", decorated)
        }

    [<Property(MaxTest = 300)>]
    let ``comments and blank lines do not change what is parsed`` () =
        Prop.forAll (Gen.elements versions |> Arb.fromGen) (fun version ->
            Prop.forAll (arbFor version) (fun circuit ->
                let qasm = OpenQasmExport.exportWithConfig (configFor version) circuit

                Prop.forAll (Arb.fromGen (genCommented qasm)) (fun commented ->
                    let plain = OpenQasmImport.parse qasm
                    let withComments = OpenQasmImport.parse commented

                    (plain = withComments)
                    |> Prop.label $"{plain}\n--- with comments ---\n{withComments}\n{commented}")))

    /// An angle as OpenQASM text together with its value.
    let private genAngleText: Gen<string * float> =
        Gen.frequency
            [
                2,
                gen {
                    let! k = Gen.choose (-9, 9)
                    let! d = Gen.choose (1, 9)

                    let! form =
                        Gen.elements
                            [
                                (fun k d -> $"{k}*pi/{d}")
                                (fun k d -> if d = 1 then $"{k}*pi" else $"{k}*pi/{d}")
                                (fun k d ->
                                    if abs k = 1 then
                                        (if k < 0 then $"-pi/{d}" else $"pi/{d}")
                                    else
                                        $"{k}*pi/{d}")
                            ]

                    return form k d, float k * Math.PI / float d
                }
                1,
                Gen.elements
                    [
                        "pi", Math.PI
                        "-pi", -Math.PI
                        "2*pi", 2.0 * Math.PI
                        "pi/2", Math.PI / 2.0
                        "0", 0.0
                    ]
                2,
                Gen.choose (-3141592, 3141592)
                |> Gen.map (fun n ->
                    let value = float n / 1000000.0
                    value.ToString("0.000000", Globalization.CultureInfo.InvariantCulture), value)
                1, Gen.choose (-9, 9) |> Gen.map (fun n -> $"{n}e-2", float n / 100.0)
            ]

    [<Property(MaxTest = 300)>]
    let ``an angle expression parses to its value`` () =
        Prop.forAll
            (Arb.fromGen (Gen.zip genAngleText (Gen.elements [ "rx"; "ry"; "rz"; "p" ])))
            (fun ((text, value), name) ->
                let qasm =
                    $"OPENQASM 2.0;\ninclude \"qelib1.inc\";\nqreg q[1];\n{name}({text}) q[0];"

                match OpenQasmImport.parse qasm with
                | Ok { Gates = [ RX(0, a) ] }
                | Ok { Gates = [ RY(0, a) ] }
                | Ok { Gates = [ RZ(0, a) ] }
                | Ok { Gates = [ P(0, a) ] } ->
                    (abs (a - value) < 1e-9)
                    |> Prop.label $"{name}({text}) parsed as {a}, expected {value}"
                | other -> false |> Prop.label $"{name}({text}): {other}")

    /// Damage to the text: a character deleted, inserted, repeated many
    /// times, or a line dropped or doubled.
    type Damage =
        | DeleteAt of int
        | InsertAt of n: int * c: char
        | RepeatAt of n: int * times: int
        | DropLine of int
        | DoubleLine of int

    let private applyDamage (text: string) (damage: Damage) : string =
        let at n =
            if text.Length = 0 then 0 else n % text.Length

        match damage with
        | DeleteAt n when text.Length > 0 -> text.Remove(at n, 1)
        | DeleteAt _ -> text
        | InsertAt(n, c) -> text.Insert(at n, string c)
        | RepeatAt(n, times) when text.Length > 0 -> text.Insert(at n, String(text.[at n], times))
        | RepeatAt _ -> text
        | DropLine n ->
            let lines = text.Split '\n'
            String.Join("\n", Array.removeAt (n % lines.Length) lines)
        | DoubleLine n ->
            let lines = text.Split '\n'
            String.Join("\n", Array.insertAt (n % lines.Length) lines[n % lines.Length] lines)

    let private genDamage: Gen<Damage> =
        Gen.frequency
            [
                3, Gen.choose (0, 9999) |> Gen.map DeleteAt
                3,
                Gen.zip
                    (Gen.choose (0, 9999))
                    (Gen.elements
                        [
                            '['
                            ']'
                            '('
                            ')'
                            ','
                            ';'
                            '-'
                            '.'
                            '9'
                            'q'
                            ' '
                            '\n'
                            '{'
                            '}'
                            '/'
                            '*'
                        ])
                |> Gen.map InsertAt
                2, Gen.zip (Gen.choose (0, 9999)) (Gen.choose (2, 24)) |> Gen.map RepeatAt
                1, Gen.choose (0, 9999) |> Gen.map DropLine
                1, Gen.choose (0, 9999) |> Gen.map DoubleLine
            ]

    /// One to three damages: enough to break any one line, few enough that
    /// the parser still gets past the header and reads the damaged line.
    let private genDamages: Gen<Damage list> =
        Gen.choose (1, 3) |> Gen.bind (fun n -> Gen.listOfLength n genDamage)

    [<Property(MaxTest = 500)>]
    let ``damaged text is answered with a Result, never an exception`` () =
        Prop.forAll Circuits.arbCircuitV3 (fun circuit ->
            let qasm = OpenQasmExport.exportWithConfig (configFor V3_0) circuit

            Prop.forAll (Arb.fromGen genDamages) (fun damages ->
                let damaged = damages |> List.fold applyDamage qasm

                let outcome =
                    try
                        match OpenQasmImport.parse damaged with
                        | Ok _ -> Ok "parsed"
                        | Error _ -> Ok "refused"
                    with ex ->
                        Error $"{ex.GetType().Name}: {ex.Message}"

                (Result.isOk outcome)
                |> Prop.label $"{outcome}\n{damaged}"
                |> Prop.classify (outcome = Ok "parsed") "still parsed"))

    [<Fact>]
    let ``the generator reaches every gate kind the exporter writes`` () =
        let circuits = Gen.sampleWithSize 20 300 (Circuits.genCircuit true)

        let names =
            circuits
            |> Array.collect (fun c -> c.Gates |> List.map getGateName |> Array.ofList)
            |> Array.distinct
            |> Set.ofArray

        let expected =
            [
                X 0
                Y 0
                Z 0
                H 0
                S 0
                SDG 0
                T 0
                TDG 0
                P(0, 0.0)
                RX(0, 0.0)
                RY(0, 0.0)
                RZ(0, 0.0)
                U3(0, 0.0, 0.0, 0.0)
                CNOT(0, 1)
                CZ(0, 1)
                CP(0, 1, 0.0)
                CRX(0, 1, 0.0)
                CRY(0, 1, 0.0)
                CRZ(0, 1, 0.0)
                SWAP(0, 1)
                RXX(0, 1, 0.0)
                RYY(0, 1, 0.0)
                RZZ(0, 1, 0.0)
                CCX(0, 1, 2)
                Measure 0
                Reset 0
                Barrier [ 0 ]
                Conditional(0, X 0)
            ]
            |> List.map getGateName
            |> Set.ofList

        let missing = Set.difference expected names
        Assert.True(Set.isEmpty missing, $"never generated: {missing}")
