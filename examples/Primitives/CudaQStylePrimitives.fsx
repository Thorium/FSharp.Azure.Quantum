/// CUDA-Q-style execution primitives — sample / observe / run / getState
///
/// This library exposes a `Primitives` module that mirrors the CUDA-Q surface
/// (`cudaq.sample` / `cudaq.observe` / `cudaq.run` / `cudaq.get_state`), so code —
/// or an agent — written against that mental model maps directly onto any
/// `IQuantumBackend` here (local simulator or a real cloud QPU).
///
///   CUDA-Q                 FSharp.Azure.Quantum
///   cudaq.sample       →   Primitives.sample     (bitstring histogram)
///   cudaq.run          →   Primitives.run        (raw per-shot outcomes)
///   cudaq.observe      →   Primitives.observe    (expectation ⟨H⟩)
///   cudaq.get_state    →   Primitives.getState   (statevector, simulator)
///   *_async            →   Primitives.*Async
///
/// The "kernel" is a `CircuitBuilder.Circuit`. Here we use a Bell state
/// |Φ⁺⟩ = (|00⟩ + |11⟩)/√2 and confirm ⟨Z₀Z₁⟩ = +1 and ⟨X₀⟩ = 0.
///
/// Run with: dotnet fsi CudaQStylePrimitives.fsx

//#r "nuget: FSharp.Azure.Quantum"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "nuget: MathNet.Numerics, 5.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System.Numerics
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Algorithms

// Any IQuantumBackend — local simulator here, a cloud QPU for hardware.
let backend = LocalBackend.LocalBackend() :> IQuantumBackend

// The "kernel": a Bell-state circuit.
let bell =
    CircuitBuilder.empty 2
    |> CircuitBuilder.addGate (CircuitBuilder.H 0)
    |> CircuitBuilder.addGate (CircuitBuilder.CNOT (0, 1))

printfn "CUDA-Q-style primitives on a Bell state\n"

// cudaq.sample → histogram of measured bitstrings
match Primitives.sample backend bell 1000 with
| Ok histogram ->
    printfn "sample (1000 shots):"
    histogram |> Map.iter (fun bitstring count -> printfn "  |%s⟩ : %d" bitstring count)
| Error e -> eprintfn "sample failed: %s" e.Message

// cudaq.observe → expectation value ⟨H⟩ of a Pauli Hamiltonian
let zz : TrotterSuzuki.PauliHamiltonian =
    { Terms = [ { Operators = [| 'Z'; 'Z' |]; Coefficient = Complex(1.0, 0.0) } ]; NumQubits = 2 }
let x0 : TrotterSuzuki.PauliHamiltonian =
    { Terms = [ { Operators = [| 'X'; 'I' |]; Coefficient = Complex(1.0, 0.0) } ]; NumQubits = 2 }

match Primitives.observe backend bell zz, Primitives.observe backend bell x0 with
| Ok ezz, Ok ex0 ->
    printfn "\nobserve ⟨Z₀Z₁⟩ = %+.4f  (expect +1 for a Bell state)" ezz
    printfn "observe ⟨X₀⟩   = %+.4f  (expect  0)" ex0
| _ -> eprintfn "observe failed"

// cudaq.run → raw per-shot outcomes; cudaq.get_state → full statevector
match Primitives.run backend bell 5 with
| Ok shots -> printfn "\nrun: %d shots × %d qubits (first shot: %A)" shots.Length shots.[0].Length shots.[0]
| Error e -> eprintfn "run failed: %s" e.Message

match Primitives.getState backend bell with
| Ok _ -> printfn "getState: returned the full quantum state (simulator)"
| Error e -> eprintfn "getState failed: %s" e.Message
