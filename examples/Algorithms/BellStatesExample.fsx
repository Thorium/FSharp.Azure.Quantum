// Bell States (EPR Pairs) Example
// Creates and verifies maximally entangled two-qubit states across backends
//
// Usage:
//   dotnet fsi BellStatesExample.fsx
//   dotnet fsi BellStatesExample.fsx -- --help
//   dotnet fsi BellStatesExample.fsx -- --backend local --state phiplus
//   dotnet fsi BellStatesExample.fsx -- --verification-shots 500
//   dotnet fsi BellStatesExample.fsx -- --quiet --output results.json --csv results.csv

(*
===============================================================================
 Background Theory
===============================================================================

Bell states are the four maximally entangled two-qubit states, forming an
orthonormal basis for the two-qubit Hilbert space. Named after physicist John
Bell, these states exhibit "spooky action at a distance": measuring one qubit
instantaneously determines the other's state, regardless of separation.

The four Bell states:
  |Phi+> = (|00> + |11>) / sqrt(2)  (correlated, same phase)
  |Phi-> = (|00> - |11>) / sqrt(2)  (correlated, opposite phase)
  |Psi+> = (|01> + |10>) / sqrt(2)  (anti-correlated, same phase)
  |Psi-> = (|01> - |10>) / sqrt(2)  (anti-correlated, opposite phase / singlet)

Creation circuit: |Phi+> = CNOT(H|0> x |0>)
CHSH inequality: |S| <= 2 classically, |S| <= 2*sqrt(2) ~ 2.83 quantum

Production Use Cases:
  - Quantum Error Correction (surface codes, toric codes)
  - Quantum Key Distribution (BB84, E91 protocols)
  - Quantum Teleportation (requires pre-shared Bell pair)
  - Quantum Networking (entanglement swapping)

Real Deployments:
  - ID Quantique commercial QKD systems
  - Micius satellite quantum communication (2016+)
  - IBM Quantum, IonQ, Rigetti platforms

Unified Backend Architecture:
  Same Bell state algorithms work across backends via IQuantumBackend:
  - LocalBackend: Gate-based simulation (state vectors)
  - TopologicalBackend: Braiding-based computation (Ising anyons)

References:
  [1] Bell, "On the Einstein Podolsky Rosen Paradox", Physics 1, 195 (1964).
  [2] Aspect, Dalibard, Roger, "Experimental Test of Bell's Inequalities",
      Phys. Rev. Lett. 49, 1804 (1982).
  [3] Nielsen & Chuang, "Quantum Computation and Quantum Information",
      Cambridge University Press (2010), Section 1.3.6.
  [4] Wikipedia: Bell_state https://en.wikipedia.org/wiki/Bell_state
*)

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "../../src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open FSharp.Azure.Quantum.Algorithms.BellStates
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Examples.Common

// ============================================================================
// CLI Setup
// ============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "BellStatesExample.fsx" "Create and verify Bell states (EPR pairs) on gate-based and topological backends." [
    { Name = "backend"; Description = "Backend to use (local/topological/both)"; Default = Some "both" }
    { Name = "state"; Description = "Bell state (phiplus/phiminus/psiplus/psiminus/all)"; Default = Some "all" }
    { Name = "verification-shots"; Description = "Shots for entanglement verification"; Default = Some "100" }
    { Name = "output"; Description = "Write results to JSON file"; Default = None }
    { Name = "csv"; Description = "Write results to CSV file"; Default = None }
    { Name = "quiet"; Description = "Suppress informational output"; Default = None }
] args

let backendArg = Cli.getOr "backend" "both" args
let stateArg = Cli.getOr "state" "all" args
let verificationShots = Cli.getIntOr "verification-shots" 100 args
let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ============================================================================
// Quantum Backends
// ============================================================================

let localBackend = LocalBackend() :> IQuantumBackend
let topoBackend = TopologicalUnifiedBackendFactory.createIsing 8

let backendsToTest =
    match backendArg with
    | "local" -> [ ("local", localBackend) ]
    | "topological" | "topo" -> [ ("topological", topoBackend) ]
    | _ -> [ ("local", localBackend); ("topological", topoBackend) ]

// ============================================================================
// Result Collection
// ============================================================================

let results = System.Collections.Generic.List<Map<string, string>>()

// ============================================================================
// Bell State Definitions
// ============================================================================

type BellStateSpec = {
    Key: string
    Label: string
    Notation: string
    Circuit: string
    Create: IQuantumBackend -> Result<BellStateResult, FSharp.Azure.Quantum.Core.QuantumError>
}

let allStates = [
    { Key = "phiplus"; Label = "Phi Plus"; Notation = "|Phi+> = (|00> + |11>) / sqrt(2)"
      Circuit = "H(0), CNOT(0,1)"; Create = createPhiPlus }
    { Key = "phiminus"; Label = "Phi Minus"; Notation = "|Phi-> = (|00> - |11>) / sqrt(2)"
      Circuit = "H(0), CNOT(0,1), Z(0)"; Create = createPhiMinus }
    { Key = "psiplus"; Label = "Psi Plus"; Notation = "|Psi+> = (|01> + |10>) / sqrt(2)"
      Circuit = "H(0), CNOT(0,1), X(1)"; Create = createPsiPlus }
    { Key = "psiminus"; Label = "Psi Minus"; Notation = "|Psi-> = (|01> - |10>) / sqrt(2)"
      Circuit = "H(0), CNOT(0,1), X(1), Z(0)"; Create = createPsiMinus }
]

let statesToTest =
    match stateArg with
    | "all" -> allStates
    | key -> allStates |> List.filter (fun s -> s.Key = key)

// ============================================================================
// Run Bell State Creation + Verification
// ============================================================================

if not quiet then
    printfn "=== Bell States (EPR Pairs) ==="
    printfn ""
    printfn "BUSINESS SCENARIO:"
    printfn "Create maximally entangled qubit pairs — the fundamental"
    printfn "resource for quantum communication, teleportation, and QKD."
    printfn ""

for (backendKey, backend) in backendsToTest do
    if not quiet then
        printfn "--- Backend: %s (%s) ---" backend.Name backendKey
        printfn "  Native state type: %A" backend.NativeStateType
        printfn ""
        printfn "  Creating Bell States:"
        printfn ""

    for (idx, spec) in statesToTest |> List.mapi (fun i s -> (i + 1, s)) do
        match spec.Create backend with
        | Ok result ->
            if not quiet then
                printfn "  %d. %s" idx spec.Label
                printfn "     %s" spec.Notation
                printfn "     Circuit: %s" spec.Circuit
                printfn "%s" (formatResult result)
                printfn ""

            // Verify entanglement
            let correlationStr =
                match verifyEntanglement result backend verificationShots with
                | Ok correlation ->
                    if not quiet then
                        let strength = if abs correlation > 0.9 then "STRONG" elif abs correlation > 0.5 then "moderate" else "weak"
                        printfn "     Entanglement: correlation = %.4f [%s]" correlation strength

                    sprintf "%.4f" correlation
                | Error _ ->
                    if not quiet then
                        printfn "     Entanglement: verification unavailable"
                    "N/A"

            if not quiet then printfn ""

            results.Add(
                [ "backend", backendKey
                  "backend_name", backend.Name
                  "state_key", spec.Key
                  "state_label", spec.Label
                  "notation", spec.Notation
                  "circuit", spec.Circuit
                  "qubits", string result.NumQubits
                  "correlation", correlationStr
                  "success", "true" ]
                |> Map.ofList)

        | Error err ->
            if not quiet then
                printfn "  %d. %s - ERROR: %A" idx spec.Label err
                printfn ""

            results.Add(
                [ "backend", backendKey
                  "state_key", spec.Key
                  "state_label", spec.Label
                  "error", sprintf "%A" err
                  "success", "false" ]
                |> Map.ofList)

// ============================================================================
// Summary
// ============================================================================

if not quiet then
    printfn "--- Applications ---"
    printfn ""
    printfn "  Bell states in production:"
    printfn "    Quantum Error Correction:  Bell pairs detect/correct errors"
    printfn "    Quantum Key Distribution:  Secure communication (ID Quantique, Micius)"
    printfn "    Quantum Teleportation:     Transfer quantum states"
    printfn "    Quantum Networks:          Entanglement swapping for quantum internet"
    printfn ""

    if backendsToTest.Length > 1 then
        printfn "--- Unified Architecture ---"
        printfn ""
        printfn "  Same Bell state code works on BOTH backends:"
        printfn "    LocalBackend:        Gate-based simulation (state vectors)"
        printfn "    TopologicalBackend:  Braiding-based (Ising anyons, fault-tolerant)"
        printfn ""
        printfn "  Topological advantage: inherent error protection via non-Abelian braiding"
        printfn "  (Microsoft's approach to scalable quantum computing)"
        printfn ""

// ============================================================================
// Structured Output
// ============================================================================

let resultsList = results |> Seq.toList

match outputPath with
| Some path -> Reporting.writeJson path resultsList
| None -> ()

match csvPath with
| Some path ->
    let allKeys =
        resultsList
        |> List.collect (fun m -> m |> Map.toList |> List.map fst)
        |> List.distinct
    let rows =
        resultsList
        |> List.map (fun m -> allKeys |> List.map (fun k -> m |> Map.tryFind k |> Option.defaultValue ""))
    Reporting.writeCsv path allKeys rows
| None -> ()

// ============================================================================
// Usage Hints
// ============================================================================

if not quiet && outputPath.IsNone && csvPath.IsNone && argv.Length = 0 then
    printfn "Hint: Customize this run with CLI options:"
    printfn "  dotnet fsi BellStatesExample.fsx -- --backend local --state phiplus"
    printfn "  dotnet fsi BellStatesExample.fsx -- --verification-shots 500"
    printfn "  dotnet fsi BellStatesExample.fsx -- --quiet --output results.json --csv results.csv"
    printfn "  dotnet fsi BellStatesExample.fsx -- --help"
