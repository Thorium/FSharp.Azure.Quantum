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

//#r "nuget: FSharp.Azure.Quantum"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "nuget: MathNet.Numerics, 5.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System.Numerics
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Algorithms

let backend = LocalBackend.LocalBackend() :> IQuantumBackend

let pauli (ops: char[]) (c: float) : TrotterSuzuki.PauliString =
    { Operators = ops; Coefficient = Complex(c, 0.0) }

// Triangle graph: edges (0,1), (1,2), (0,2). Cost H = Z0Z1 + Z1Z2 + Z0Z2.
let cost : TrotterSuzuki.PauliHamiltonian =
    { Terms = [ pauli [| 'Z'; 'Z'; 'I' |] 1.0
                pauli [| 'I'; 'Z'; 'Z' |] 1.0
                pauli [| 'Z'; 'I'; 'Z' |] 1.0 ]
      NumQubits = 3 }

// Mixer pool: single-qubit X and Y rotations.
let pool =
    [ pauli [| 'X'; 'I'; 'I' |] 1.0; pauli [| 'I'; 'X'; 'I' |] 1.0; pauli [| 'I'; 'I'; 'X' |] 1.0
      pauli [| 'Y'; 'I'; 'I' |] 1.0; pauli [| 'I'; 'Y'; 'I' |] 1.0; pauli [| 'I'; 'I'; 'Y' |] 1.0 ]

printfn "ADAPT-QAOA — MaxCut on a frustrated triangle (3 nodes)\n"

match AdaptQaoa.run backend cost pool 3 AdaptQaoa.defaultConfig with
| Error e -> eprintfn "ADAPT-QAOA failed: %s" e.Message; exit 1
| Ok result ->
    printfn "Converged      : %b" result.Converged
    printfn "Layers added   : %d (each = cost e^(-iγH) + one selected mixer e^(-iβA))" result.Layers
    result.SelectedMixers
    |> List.iteri (fun i m -> printfn "  layer %d mixer : %s" (i + 1) (System.String m.Operators))
    printfn "Min ⟨H⟩        : %.6f  (optimal = -1 → cut value %.0f of 3 edges)" result.Energy ((3.0 - result.Energy) / 2.0)
    printfn "Energy/layer   : %s" (result.EnergyHistory |> List.map (sprintf "%.4f") |> String.concat "  →  ")
