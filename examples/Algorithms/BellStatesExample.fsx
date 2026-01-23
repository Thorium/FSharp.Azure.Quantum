/// Bell States (EPR Pairs) Example
/// 
/// Demonstrates creation of maximally entangled two-qubit states.
/// 
/// **Production Use Cases**:
/// - Quantum Error Correction (surface codes, toric codes)
/// - Quantum Key Distribution (BB84, E91 protocols)
/// - Quantum Teleportation (requires pre-shared Bell pair)
/// - Quantum Networking (entanglement swapping)
/// 
/// **Real Deployments**:
/// - ID Quantique commercial QKD systems
/// - Micius satellite quantum communication
/// - IBM Quantum, IonQ, Rigetti platforms

(*
===============================================================================
 Background Theory
===============================================================================

Bell states are the four maximally entangled two-qubit states, forming an
orthonormal basis for the two-qubit Hilbert space. Named after physicist John
Bell, these states exhibit "spooky action at a distance" (Einstein's phrase):
measuring one qubit instantaneously determines the other's state, regardless of
spatial separation. Bell states are the fundamental resource for quantum
communication, teleportation, superdense coding, and entanglement-based protocols.

The four Bell states are created from |00⟩ using Hadamard and CNOT gates, with
optional X and Z gates for the variants. In each state, the two qubits are
perfectly correlated (|Φ⟩ states) or anti-correlated (|Ψ⟩ states), and measuring
either qubit in the computational basis yields a uniformly random outcome. The
"entanglement" means the joint state cannot be written as a product of individual
qubit states: |Φ⁺⟩ ≠ |ψ₁⟩ ⊗ |ψ₂⟩ for any single-qubit states.

Key Equations:
  - |Φ⁺⟩ = (|00⟩ + |11⟩) / √2  (correlated, same phase)
  - |Φ⁻⟩ = (|00⟩ - |11⟩) / √2  (correlated, opposite phase)
  - |Ψ⁺⟩ = (|01⟩ + |10⟩) / √2  (anti-correlated, same phase)
  - |Ψ⁻⟩ = (|01⟩ - |10⟩) / √2  (anti-correlated, opposite phase / singlet)
  - Creation circuit: |Φ⁺⟩ = CNOT(H|0⟩ ⊗ |0⟩)
  - CHSH inequality: |S| ≤ 2 classically, |S| ≤ 2√2 ≈ 2.83 quantum (Bell violation)

Quantum Advantage:
  Bell states enable quantum advantages impossible classically: (1) Superdense
  coding: send 2 classical bits using 1 qubit + 1 ebit. (2) Teleportation:
  transfer quantum state using 2 cbits + 1 ebit. (3) Device-independent QKD:
  Bell inequality violations certify security. (4) Entanglement swapping:
  create entanglement between particles that never interacted. Bell's theorem
  (1964) proved no local hidden variable theory can reproduce quantum predictions,
  experimentally confirmed (Nobel Prize 2022: Aspect, Clauser, Zeilinger).

References:
  [1] Bell, "On the Einstein Podolsky Rosen Paradox", Physics 1, 195-200 (1964).
      https://doi.org/10.1103/PhysicsPhysiqueFizika.1.195
  [2] Aspect, Dalibard, Roger, "Experimental Test of Bell's Inequalities Using
      Time-Varying Analyzers", Phys. Rev. Lett. 49, 1804 (1982).
      https://doi.org/10.1103/PhysRevLett.49.1804
  [3] Nielsen & Chuang, "Quantum Computation and Quantum Information",
      Cambridge University Press (2010), Section 1.3.6.
  [4] Wikipedia: Bell_state
      https://en.wikipedia.org/wiki/Bell_state
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open FSharp.Azure.Quantum.Algorithms.BellStates
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core.BackendAbstraction

printfn "=== Bell States (EPR Pairs) Demo ==="
printfn ""

// Create quantum backend (local simulator)
let backend = LocalBackend() :> IQuantumBackend

printfn "🔬 Creating All Four Bell States"
printfn "================================"
printfn ""

// Create |Φ⁺⟩ = (|00⟩ + |11⟩) / √2
printfn "1. Creating |Φ⁺⟩ (Phi Plus) - Most common Bell state"
printfn "   Circuit: H(0), CNOT(0,1)"
printfn "   Used in: Teleportation, Superdense Coding, QKD"
match createPhiPlus backend with
| Ok result ->
    printfn "%s" (formatResult result)
    printfn "   ✅ Success - Entangled state created!"
| Error err ->
    printfn "   ❌ Error: %A" err

printfn ""

// Create |Φ⁻⟩ = (|00⟩ - |11⟩) / √2
printfn "2. Creating |Φ⁻⟩ (Phi Minus)"
printfn "   Circuit: H(0), CNOT(0,1), Z(0)"
match createPhiMinus backend with
| Ok result ->
    printfn "%s" (formatResult result)
    printfn "   ✅ Success!"
| Error err ->
    printfn "   ❌ Error: %A" err

printfn ""

// Create |Ψ⁺⟩ = (|01⟩ + |10⟩) / √2
printfn "3. Creating |Ψ⁺⟩ (Psi Plus)"
printfn "   Circuit: H(0), CNOT(0,1), X(1)"
match createPsiPlus backend with
| Ok result ->
    printfn "%s" (formatResult result)
    printfn "   ✅ Success!"
| Error err ->
    printfn "   ❌ Error: %A" err

printfn ""

// Create |Ψ⁻⟩ = (|01⟩ - |10⟩) / √2
printfn "4. Creating |Ψ⁻⟩ (Psi Minus)"
printfn "   Circuit: H(0), CNOT(0,1), X(1), Z(0)"
match createPsiMinus backend with
| Ok result ->
    printfn "%s" (formatResult result)
    printfn "   ✅ Success!"
| Error err ->
    printfn "   ❌ Error: %A" err

printfn ""
printfn "================================"
printfn ""

// Verify entanglement
printfn "🔍 Verifying Entanglement"
printfn "========================="
printfn ""

match createPhiPlus backend with
| Ok phiPlus ->
    printfn "Created |Φ⁺⟩ - verifying entanglement..."
    match verifyEntanglement phiPlus backend 100 with
    | Ok correlation ->
        printfn "Correlation coefficient: %.2f" correlation
        if abs correlation > 0.9 then
            printfn "✅ Strong entanglement verified! (|correlation| > 0.9)"
        else
            printfn "⚠️  Weak correlation - check NISQ noise"
    | Error err ->
        printfn "❌ Verification error: %A" err
| Error err ->
    printfn "❌ Creation error: %A" err

printfn ""
printfn "================================"
printfn ""

printfn "📚 Production Applications:"
printfn "  • Quantum Error Correction: Bell pairs detect/correct errors"
printfn "  • Quantum Key Distribution: Secure communication (ID Quantique, Micius)"
printfn "  • Quantum Teleportation: Transfer quantum states"
printfn "  • Quantum Networks: Entanglement swapping for quantum internet"
printfn ""
printfn "🌐 Real-World Status:"
printfn "  ✅ Commercially deployed (QKD systems)"
printfn "  ✅ Satellite quantum communication (Micius, 2016+)"
printfn "  ✅ Every quantum platform supports Bell states"
printfn "  🔮 Future: Quantum internet backbone (2030+)"
