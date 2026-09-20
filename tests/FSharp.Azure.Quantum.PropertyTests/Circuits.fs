namespace FSharp.Azure.Quantum.PropertyTests

open System
open FsCheck
open FsCheck.FSharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder

/// Random circuits for the OpenQASM properties: every gate the exporter can
/// write, on distinct qubits inside the register, with angles both at exact
/// multiples of pi and anywhere in [-2pi, 2pi]. MCZ is left out on purpose:
/// the exporter refuses it until GateTranspiler has decomposed it.
module Circuits =

    /// A gate-shaped view used to compare circuits after a round trip: the
    /// exporter writes angles with ten decimals, so two gates are the same
    /// when their qubits agree and their angles agree to that precision.
    let private angleClose (a: float) (b: float) = abs (a - b) < 1e-9

    let rec gateClose (a: Gate) (b: Gate) : bool =
        match a, b with
        | RX(q, t), RX(q', t')
        | RY(q, t), RY(q', t')
        | RZ(q, t), RZ(q', t')
        | P(q, t), P(q', t') -> q = q' && angleClose t t'
        | CP(c, g, t), CP(c', g', t')
        | CRX(c, g, t), CRX(c', g', t')
        | CRY(c, g, t), CRY(c', g', t')
        | CRZ(c, g, t), CRZ(c', g', t')
        | RXX(c, g, t), RXX(c', g', t')
        | RYY(c, g, t), RYY(c', g', t')
        | RZZ(c, g, t), RZZ(c', g', t') -> c = c' && g = g' && angleClose t t'
        | U3(q, t, p, l), U3(q', t', p', l') -> q = q' && angleClose t t' && angleClose p p' && angleClose l l'
        | Conditional(q, inner), Conditional(q', inner') -> q = q' && gateClose inner inner'
        | _ -> a = b

    /// Apply a function to every qubit index a gate names.
    let rec mapQubits (f: int -> int) (gate: Gate) : Gate =
        match gate with
        | X q -> X(f q)
        | Y q -> Y(f q)
        | Z q -> Z(f q)
        | H q -> H(f q)
        | S q -> S(f q)
        | SDG q -> SDG(f q)
        | T q -> T(f q)
        | TDG q -> TDG(f q)
        | P(q, t) -> P(f q, t)
        | RX(q, t) -> RX(f q, t)
        | RY(q, t) -> RY(f q, t)
        | RZ(q, t) -> RZ(f q, t)
        | U3(q, t, p, l) -> U3(f q, t, p, l)
        | CNOT(c, t) -> CNOT(f c, f t)
        | CZ(c, t) -> CZ(f c, f t)
        | CP(c, t, a) -> CP(f c, f t, a)
        | CRX(c, t, a) -> CRX(f c, f t, a)
        | CRY(c, t, a) -> CRY(f c, f t, a)
        | CRZ(c, t, a) -> CRZ(f c, f t, a)
        | SWAP(a, b) -> SWAP(f a, f b)
        | RXX(a, b, t) -> RXX(f a, f b, t)
        | RYY(a, b, t) -> RYY(f a, f b, t)
        | RZZ(a, b, t) -> RZZ(f a, f b, t)
        | CCX(a, b, c) -> CCX(f a, f b, f c)
        | MCZ(controls, target) -> MCZ(List.map f controls, f target)
        | Measure q -> Measure(f q)
        | Reset q -> Reset(f q)
        | Barrier qs -> Barrier(List.map f qs)
        | Conditional(q, inner) -> Conditional(f q, mapQubits f inner)

    /// Exact multiples of pi one in four times, otherwise anywhere in [-2pi, 2pi].
    let genAngle: Gen<float> =
        Gen.frequency
            [
                1, Gen.elements [ 0.0; Math.PI; Math.PI / 2.0; -Math.PI / 4.0; 3.0 * Math.PI / 4.0; -Math.PI ]
                3,
                Gen.choose (-1000000, 1000000)
                |> Gen.map (fun n -> float n * 2.0 * Math.PI / 1000000.0)
            ]

    /// `k` distinct qubits of a register of `n`.
    let private distinctQubits (n: int) (k: int) : Gen<int list> =
        Gen.shuffle [ 0 .. n - 1 ] |> Gen.map (Array.toList >> List.take k)

    let private genOneQubit (n: int) : Gen<Gate> =
        gen {
            let! q = Gen.choose (0, n - 1)
            let! angle = genAngle
            let! theta = genAngle
            let! phi = genAngle
            let! lambda = genAngle

            return!
                Gen.elements
                    [
                        X q
                        Y q
                        Z q
                        H q
                        S q
                        SDG q
                        T q
                        TDG q
                        RX(q, angle)
                        RY(q, angle)
                        RZ(q, angle)
                        P(q, angle)
                        U3(q, theta, phi, lambda)
                    ]
        }

    let private genTwoQubit (n: int) : Gen<Gate> =
        gen {
            let! qubits = distinctQubits n 2
            let a, b = qubits[0], qubits[1]
            let! angle = genAngle

            return!
                Gen.elements
                    [
                        CNOT(a, b)
                        CZ(a, b)
                        SWAP(a, b)
                        CP(a, b, angle)
                        CRX(a, b, angle)
                        CRY(a, b, angle)
                        CRZ(a, b, angle)
                        RXX(a, b, angle)
                        RYY(a, b, angle)
                        RZZ(a, b, angle)
                    ]
        }

    let private genThreeQubit (n: int) : Gen<Gate> =
        distinctQubits n 3 |> Gen.map (fun qs -> CCX(qs[0], qs[1], qs[2]))

    /// A plain unitary gate: what a Conditional may wrap.
    let genUnitary (n: int) : Gen<Gate> =
        Gen.frequency
            [
                3, genOneQubit n
                (if n >= 2 then 3 else 0), genTwoQubit n
                (if n >= 3 then 1 else 0), genThreeQubit n
            ]

    let private genBarrier (n: int) : Gen<Gate> =
        gen {
            let! k = Gen.choose (1, n)
            let! qs = distinctQubits n k
            return Barrier qs
        }

    /// Any gate the exporter writes. Conditionals only exist in OpenQASM 3.0
    /// output, so they are generated only when asked for.
    let genGate (n: int) (withConditional: bool) : Gen<Gate> =
        Gen.frequency
            [
                7, genUnitary n
                1, Gen.choose (0, n - 1) |> Gen.map Measure
                1, Gen.choose (0, n - 1) |> Gen.map Reset
                1, genBarrier n
                (if withConditional then 2 else 0),
                gen {
                    let! q = Gen.choose (0, n - 1)
                    let! inner = genUnitary n
                    return Conditional(q, inner)
                }
            ]

    /// A circuit of one to six qubits (an empty register now and then) with
    /// up to `size` gates, stored most-recent-first as CircuitBuilder keeps them.
    let genCircuit (withConditional: bool) : Gen<Circuit> =
        Gen.sized (fun size ->
            gen {
                let! n = Gen.frequency [ 1, Gen.constant 0; 12, Gen.choose (1, 6) ]

                if n = 0 then
                    return { QubitCount = 0; Gates = [] }
                else
                    let! count = Gen.choose (0, max 1 size)
                    let! gates = Gen.listOfLength count (genGate n withConditional)
                    return { QubitCount = n; Gates = gates }
            })

    /// Drop one gate at a time.
    let shrinkCircuit (circuit: Circuit) : seq<Circuit> =
        seq {
            for i in 0 .. circuit.Gates.Length - 1 do
                yield
                    { circuit with
                        Gates = List.removeAt i circuit.Gates
                    }
        }

    /// Circuits for OpenQASM 1.0 and 2.0: no conditionals.
    let arbCircuit: Arbitrary<Circuit> =
        Arb.fromGenShrink (genCircuit false, shrinkCircuit)

    /// Circuits for OpenQASM 3.0: conditionals included.
    let arbCircuitV3: Arbitrary<Circuit> =
        Arb.fromGenShrink (genCircuit true, shrinkCircuit)
