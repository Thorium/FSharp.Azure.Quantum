/// ADAPT-VQE — adaptive ansatz growth for ground-state energy
///
/// Unlike a fixed variational form, ADAPT-VQE *grows* the ansatz one operator at a time:
/// each round it screens an operator pool by the energy gradient, appends the
/// highest-gradient operator as a new e^(-iθP) block, re-optimises all angles, and stops
/// when no pool operator has a meaningful gradient left. The result is a compact,
/// problem-tailored ansatz.
///
/// Here we minimise a small transverse-field-Ising-style Hamiltonian
///   H = X₀ + X₁ + ½ Z₀Z₁
/// on the local simulator (a state-vector backend, required for exact expectation values).
///
/// Run with: dotnet fsi AdaptVqe.fsx

//#r "nuget: FSharp.Azure.Quantum"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "nuget: MathNet.Numerics, 5.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System.Numerics
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Algorithms

let backend = LocalBackend.LocalBackend() :> IQuantumBackend

// Helper: a unit-coefficient Pauli string, e.g. term [| 'X'; 'I' |] = X₀.
let term (ops: char[]) coeff : TrotterSuzuki.PauliString =
    { Operators = ops; Coefficient = Complex(coeff, 0.0) }

// H = X₀ + X₁ + ½ Z₀Z₁
let hamiltonian : TrotterSuzuki.PauliHamiltonian =
    { Terms =
        [ term [| 'X'; 'I' |] 1.0
          term [| 'I'; 'X' |] 1.0
          term [| 'Z'; 'Z' |] 0.5 ]
      NumQubits = 2 }

// Operator pool: single-qubit Y rotations plus a two-qubit entangler.
let pool =
    [ term [| 'Y'; 'I' |] 1.0
      term [| 'I'; 'Y' |] 1.0
      term [| 'Y'; 'X' |] 1.0
      term [| 'X'; 'Y' |] 1.0 ]

printfn "ADAPT-VQE — H = X₀ + X₁ + ½ Z₀Z₁ (2 qubits)\n"

match AdaptVqe.run backend hamiltonian pool 2 AdaptVqe.defaultConfig with
| Error e -> eprintfn "ADAPT-VQE failed: %s" e.Message; exit 1
| Ok result ->
    printfn "Converged        : %b" result.Converged
    printfn "Operators added  : %d" result.SelectedOperators.Length
    result.SelectedOperators
    |> List.iteri (fun i op -> printfn "  #%d : %s" (i + 1) (System.String op.Operators))
    printfn "Ground energy    : %.6f" result.Energy
    printfn "\nEnergy per step  : %s"
        (result.EnergyHistory |> List.map (sprintf "%.4f") |> String.concat "  →  ")
