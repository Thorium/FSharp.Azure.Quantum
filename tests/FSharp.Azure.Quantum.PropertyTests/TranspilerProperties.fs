namespace FSharp.Azure.Quantum.PropertyTests

open System
open System.Numerics
open Xunit
open FsCheck.Xunit
open FsCheck
open FsCheck.FSharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.LocalSimulator

/// What GateTranspiler promises: a transpiled circuit is the same unitary as
/// the original, up to a global phase, for every backend it knows and every
/// constraint set; it only emits gates the target supports; and transpiling
/// twice is transpiling once. The local simulator is the oracle: both circuits
/// are run from |0...0> and the two state vectors compared by fidelity.
module TranspilerProperties =

    // ========================================================================
    // Unitary circuits
    // ========================================================================

    /// A unitary gate on distinct qubits of an n-qubit register, MCZ included
    /// (it is what the transpiler decomposes most deeply).
    let private genUnitaryGate (n: int) : Gen<Gate> =
        let mcz =
            gen {
                let! k = Gen.choose (1, min 3 (n - 1))
                let! qs = Gen.shuffle [ 0 .. n - 1 ] |> Gen.map (Array.toList >> List.take (k + 1))
                return MCZ(List.tail qs, List.head qs)
            }

        Gen.frequency [ 8, Circuits.genUnitary n; (if n >= 2 then 1 else 0), mcz ]

    /// Two to five qubits and up to `size` gates, stored most-recent-first.
    let private genUnitaryCircuit: Gen<Circuit> =
        Gen.sized (fun size ->
            gen {
                let! n = Gen.choose (2, 5)
                let! count = Gen.choose (0, max 1 (size / 3))
                let! gates = Gen.listOfLength count (genUnitaryGate n)
                return { QubitCount = n; Gates = gates }
            })

    let private arbUnitaryCircuit: Arbitrary<Circuit> =
        Arb.fromGenShrink (genUnitaryCircuit, Circuits.shrinkCircuit)

    // ========================================================================
    // Simulation
    // ========================================================================

    /// One unitary gate on a state vector, as LocalBackend applies it.
    let private applyGate (gate: Gate) (state: StateVector.StateVector) : StateVector.StateVector =
        match gate with
        | X q -> Gates.applyX q state
        | Y q -> Gates.applyY q state
        | Z q -> Gates.applyZ q state
        | H q -> Gates.applyH q state
        | S q -> Gates.applyS q state
        | SDG q -> Gates.applySDG q state
        | T q -> Gates.applyT q state
        | TDG q -> Gates.applyTDG q state
        | P(q, a) -> Gates.applyP q a state
        | RX(q, a) -> Gates.applyRx q a state
        | RY(q, a) -> Gates.applyRy q a state
        | RZ(q, a) -> Gates.applyRz q a state
        | U3(q, theta, phi, lambda) -> state |> Gates.applyRz q lambda |> Gates.applyRy q theta |> Gates.applyRz q phi
        | CNOT(c, t) -> Gates.applyCNOT c t state
        | CZ(c, t) -> Gates.applyCZ c t state
        | SWAP(a, b) -> Gates.applySWAP a b state
        | RXX(a, b, t) -> Gates.applyRxx a b t state
        | RYY(a, b, t) -> Gates.applyRyy a b t state
        | RZZ(a, b, t) -> Gates.applyRzz a b t state
        | CP(c, t, a) -> Gates.applyCP c t a state
        | CRX(c, t, a) -> Gates.applyCRX c t a state
        | CRY(c, t, a) -> Gates.applyCRY c t a state
        | CRZ(c, t, a) -> Gates.applyCRZ c t a state
        | CCX(a, b, t) -> Gates.applyCCX a b t state
        | MCZ(controls, t) -> Gates.applyMultiControlledZ controls t state
        | Barrier _ -> state
        | Measure _
        | Reset _
        | Conditional _ -> failwith $"not a unitary gate: {gate}"

    /// The state a circuit leaves from |0...0>, applying its gates in program order.
    let private run (circuit: Circuit) : StateVector.StateVector =
        circuit.Gates
        |> List.rev
        |> List.fold (fun state gate -> applyGate gate state) (StateVector.init circuit.QubitCount)

    /// The state is normalised and its amplitudes finite.
    let private wellFormed (state: StateVector.StateVector) =
        let n = StateVector.dimension state
        let amplitudes = [ for i in 0 .. n - 1 -> StateVector.getAmplitude i state ]

        amplitudes
        |> List.forall (fun a -> Double.IsFinite a.Real && Double.IsFinite a.Imaginary)
        && abs (StateVector.norm state - 1.0) < 1e-9

    /// |<a|b>|: 1 when the states are the same up to a global phase.
    let private fidelity (a: StateVector.StateVector) (b: StateVector.StateVector) =
        Complex.Abs(StateVector.innerProduct a b)

    let private describe (circuit: Circuit) =
        $"{circuit.QubitCount} qubits, {List.rev circuit.Gates}"

    /// The named backends the transpiler knows, plus one it does not (which
    /// makes it decompose everything).
    let private backends =
        [
            "ionq.simulator"
            "rigetti.aspen"
            "quantinuum.h1"
            "atomcomputing.phoenix"
            "topological"
            "local.simulator"
            "somewhere.else"
        ]

    let private constraintSets () =
        [
            CircuitValidator.BackendConstraints.ionqSimulator ()
            CircuitValidator.BackendConstraints.ionqHardware ()
            CircuitValidator.BackendConstraints.rigettiAspenM3 ()
            CircuitValidator.BackendConstraints.localSimulator ()
        ]

    // ========================================================================
    // Properties
    // ========================================================================

    [<Property(MaxTest = 300)>]
    let ``a circuit transpiled for any backend is the same unitary up to a global phase`` () =
        Prop.forAll arbUnitaryCircuit (fun circuit ->
            let expected = run circuit

            backends
            |> List.map (fun backend ->
                let transpiled = GateTranspiler.transpileForBackend backend circuit
                let actual = run transpiled
                let f = fidelity expected actual

                (wellFormed actual && abs (f - 1.0) < 1e-9)
                |> Prop.label
                    $"{backend}: fidelity {f}\n{describe circuit}\n--- transpiled ---\n{List.rev transpiled.Gates}")
            |> List.reduce (.&.)
            |> Prop.classify
                (circuit.Gates
                 |> List.exists (function
                     | MCZ _ -> true
                     | _ -> false))
                "has MCZ")

    [<Property(MaxTest = 300)>]
    let ``a circuit transpiled under any constraint set is the same unitary and needs no further transpilation`` () =
        Prop.forAll arbUnitaryCircuit (fun circuit ->
            let expected = run circuit

            constraintSets ()
            |> List.map (fun constraints ->
                let transpiled = GateTranspiler.transpile constraints circuit
                let f = fidelity expected (run transpiled)

                let settled =
                    not (
                        GateTranspiler.needsTranspilation constraints transpiled
                        && GateTranspiler.needsTranspilation constraints circuit
                    )

                (abs (f - 1.0) < 1e-9 && settled)
                |> Prop.label
                    $"{constraints.Name}: fidelity {f}, still needs transpilation {GateTranspiler.needsTranspilation constraints transpiled}\n{describe circuit}\n--- transpiled ---\n{List.rev transpiled.Gates}")
            |> List.reduce (.&.))

    [<Property(MaxTest = 300)>]
    let ``transpiling twice is transpiling once`` () =
        Prop.forAll arbUnitaryCircuit (fun circuit ->
            backends
            |> List.map (fun backend ->
                let once = GateTranspiler.transpileForBackend backend circuit
                let twice = GateTranspiler.transpileForBackend backend once

                (twice = once)
                |> Prop.label
                    $"{backend}\n{describe circuit}\n--- once ---\n{List.rev once.Gates}\n--- twice ---\n{List.rev twice.Gates}")
            |> List.reduce (.&.))

    /// The gates each named backend is documented to run natively.
    let private native (backend: string) (gate: Gate) =
        let name = getGateName gate
        let basic = set [ "X"; "Y"; "Z"; "H"; "Rx"; "Ry"; "Rz"; "U3"; "CNOT"; "Barrier" ]

        let allowed =
            match backend with
            | "ionq.simulator" -> Set.union basic (set [ "SWAP" ])
            | "rigetti.aspen" -> Set.union basic (set [ "CZ"; "SWAP" ])
            | "quantinuum.h1"
            | "atomcomputing.phoenix" -> Set.union basic (set [ "S"; "SDG"; "T"; "TDG"; "P"; "CZ" ])
            | "topological" -> Set.union basic (set [ "S"; "SDG"; "T"; "TDG"; "P" ])
            | "somewhere.else" -> basic
            | _ -> Set.empty

        allowed.Contains name

    [<Property(MaxTest = 300)>]
    let ``a transpiled circuit holds only the target's native gates`` () =
        Prop.forAll arbUnitaryCircuit (fun circuit ->
            backends
            |> List.filter (fun b -> b <> "local.simulator")
            |> List.map (fun backend ->
                let transpiled = GateTranspiler.transpileForBackend backend circuit

                let foreign =
                    transpiled.Gates
                    |> List.filter (native backend >> not)
                    |> List.map getGateName
                    |> List.distinct

                (List.isEmpty foreign)
                |> Prop.label
                    $"{backend} was left {foreign}\n{describe circuit}\n--- transpiled ---\n{List.rev transpiled.Gates}")
            |> List.reduce (.&.))

    [<Property(MaxTest = 300)>]
    let ``the local simulator needs nothing transpiled`` () =
        Prop.forAll arbUnitaryCircuit (fun circuit ->
            let transpiled = GateTranspiler.transpileForBackend "local.simulator" circuit

            (transpiled.Gates
             |> List.forall (function
                 | MCZ _ -> false
                 | _ -> true)
             && transpiled.Gates.Length >= circuit.Gates.Length
             && (circuit.Gates
                 |> List.forall (function
                     | MCZ _ -> false
                     | _ -> true))
                 =
                 (transpiled = circuit))
            |> Prop.label $"{describe circuit}\n--- transpiled ---\n{List.rev transpiled.Gates}")

    [<Fact>]
    let ``the generator reaches every gate the transpiler decomposes`` () =
        let names =
            Gen.sampleWithSize 30 400 genUnitaryCircuit
            |> Array.collect (fun c -> c.Gates |> List.map getGateName |> Array.ofList)
            |> Set.ofArray

        let wanted =
            [
                S 0
                SDG 0
                T 0
                TDG 0
                P(0, 0.0)
                CZ(0, 1)
                CRX(0, 1, 0.0)
                CRY(0, 1, 0.0)
                CRZ(0, 1, 0.0)
                CP(0, 1, 0.0)
                RXX(0, 1, 0.0)
                RYY(0, 1, 0.0)
                RZZ(0, 1, 0.0)
                SWAP(0, 1)
                CCX(0, 1, 2)
                MCZ([ 0 ], 1)
            ]
            |> List.map getGateName
            |> Set.ofList

        let missing = Set.difference wanted names
        Assert.True(Set.isEmpty missing, $"never generated: {missing}")
