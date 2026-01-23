/// Quantum Teleportation Protocol Example
/// 
/// Demonstrates the canonical quantum teleportation protocol:
/// - Transfer quantum state from Alice to Bob using entanglement
/// - Uses pre-shared Bell pair + 2 classical bits
/// - Original state destroyed (no-cloning theorem)
/// 
/// **Textbook References**:
/// - Nielsen & Chuang "Quantum Computation and Quantum Information" - Section 1.3.7
/// - "Learn Quantum Computing with Python and Q#" (Kaiser, 2021) - Chapter 8
/// - "Quantum Programming in Depth" (Manning, 2024) - Chapter 10
/// 
/// **Production Use Cases**:
/// - Quantum Networks (transfer states between nodes)
/// - Quantum Repeaters (extend communication range)
/// - Distributed Quantum Computing (move data between processors)
/// 
/// **Real-World Deployments**:
/// - Micius satellite: 1400 km teleportation (2017)
/// - USTC China: 143 km fiber teleportation (2012)
/// - Delft quantum network experiments (2022)

(*
===============================================================================
 Background Theory
===============================================================================

Quantum teleportation (Bennett et al., 1993) transfers an unknown quantum state
|ψ⟩ = α|0⟩ + β|1⟩ from Alice to Bob using shared entanglement and classical
communication. Crucially, the protocol does NOT transmit the physical qubit or
violate relativity (classical bits must be sent). Instead, it "disassembles"
the quantum information at Alice's location and "reassembles" it at Bob's,
consuming one Bell pair and two classical bits per teleported qubit.

The protocol proceeds in three steps: (1) Alice and Bob pre-share a Bell state
|Φ⁺⟩ = (|00⟩ + |11⟩)/√2. (2) Alice performs a Bell-basis measurement on her
qubit (the state to teleport) and her half of the Bell pair, obtaining 2 classical
bits (m₁, m₂). This measurement collapses Bob's qubit into one of four states,
each related to |ψ⟩ by a known Pauli operation. (3) Alice sends (m₁, m₂) to Bob,
who applies the corresponding correction: I, X, Z, or XZ. Bob now has |ψ⟩.

Key Equations:
  - Initial state: |ψ⟩_A ⊗ |Φ⁺⟩_AB = (α|0⟩ + β|1⟩) ⊗ (|00⟩ + |11⟩)/√2
  - Bell measurement outcomes and Bob's state:
    |Φ⁺⟩: Bob has |ψ⟩ (apply I)
    |Φ⁻⟩: Bob has Z|ψ⟩ (apply Z)
    |Ψ⁺⟩: Bob has X|ψ⟩ (apply X)
    |Ψ⁻⟩: Bob has XZ|ψ⟩ (apply XZ)
  - Resource cost: 1 ebit (Bell pair) + 2 cbits → 1 teleported qubit
  - Fidelity: F = |⟨ψ_ideal|ψ_actual⟩|² (ideally 1.0, limited by noise)

Quantum Advantage:
  Teleportation is foundational for quantum networking and distributed quantum
  computing. It enables: (1) Quantum repeaters that extend entanglement over
  long distances via entanglement swapping. (2) Gate teleportation for fault-
  tolerant quantum computing (teleporting through a gate). (3) Quantum internet
  protocols where qubits cannot be directly transmitted. The no-cloning theorem
  ensures the original state is destroyed, preserving unitarity.

References:
  [1] Bennett et al., "Teleporting an unknown quantum state via dual classical
      and Einstein-Podolsky-Rosen channels", Phys. Rev. Lett. 70, 1895 (1993).
      https://doi.org/10.1103/PhysRevLett.70.1895
  [2] Bouwmeester et al., "Experimental quantum teleportation", Nature 390,
      575-579 (1997). https://doi.org/10.1038/37539
  [3] Ren et al., "Ground-to-satellite quantum teleportation", Nature 549,
      70-73 (2017). https://doi.org/10.1038/nature23675
  [4] Wikipedia: Quantum_teleportation
      https://en.wikipedia.org/wiki/Quantum_teleportation
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Algorithms.QuantumTeleportation
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core.BackendAbstraction

printfn "╔══════════════════════════════════════════════════════════╗"
printfn "║       Quantum Teleportation Protocol Demo               ║"
printfn "╚══════════════════════════════════════════════════════════╝"
printfn ""

// Create quantum backend (local simulator)
let backend = LocalBackend() :> IQuantumBackend

printfn "🌐 Protocol Overview"
printfn "═══════════════════════════════════════════════════════════"
printfn "Alice wants to send quantum state to Bob:"
printfn "  1. Alice & Bob share entangled Bell pair"
printfn "  2. Alice entangles her state with her Bell qubit"
printfn "  3. Alice measures her qubits → 2 classical bits"
printfn "  4. Alice sends classical bits to Bob"
printfn "  5. Bob applies corrections based on classical bits"
printfn "  6. Bob now has original state (Alice's destroyed)"
printfn ""
printfn "Resources:"
printfn "  - 3 qubits (Alice input, Alice Bell, Bob Bell)"
printfn "  - 1 pre-shared Bell pair"
printfn "  - 2 classical communication bits"
printfn "  - ~4 quantum gates"
printfn ""

// ============================================================================
// Test 1: Teleport |0⟩ State (Trivial Case)
// ============================================================================

printfn "Test 1: Teleporting |0⟩ State"
printfn "─────────────────────────────────────────────────────────"
printfn "Input:  Alice has |0⟩ on her qubit"
printfn "Output: Bob should receive |0⟩"
printfn ""

match teleportZero backend with
| Ok result ->
    printfn "%s" (formatResult result)
    printfn ""
    printfn "✅ Success - Bob received |0⟩"
| Error err ->
    printfn "❌ Error: %A" err

printfn ""
printfn ""

// ============================================================================
// Test 2: Teleport |1⟩ State
// ============================================================================

printfn "Test 2: Teleporting |1⟩ State"
printfn "─────────────────────────────────────────────────────────"
printfn "Input:  Alice has |1⟩ on her qubit"
printfn "Output: Bob should receive |1⟩"
printfn ""

match teleportOne backend with
| Ok result ->
    printfn "%s" (formatResult result)
    printfn ""
    printfn "✅ Success - Bob received |1⟩"
| Error err ->
    printfn "❌ Error: %A" err

printfn ""
printfn ""

// ============================================================================
// Test 3: Teleport |+⟩ State (Superposition)
// ============================================================================

printfn "Test 3: Teleporting |+⟩ State (Superposition)"
printfn "─────────────────────────────────────────────────────────"
printfn "Input:  Alice has |+⟩ = (|0⟩ + |1⟩)/√2"
printfn "Output: Bob should receive |+⟩"
printfn ""
printfn "This tests teleportation of superposition states!"
printfn ""

match teleportPlus backend with
| Ok result ->
    printfn "%s" (formatResult result)
    printfn ""
    printfn "✅ Success - Superposition teleported!"
| Error err ->
    printfn "❌ Error: %A" err

printfn ""
printfn ""

// ============================================================================
// Test 4: Teleport |-⟩ State (Superposition with Phase)
// ============================================================================

printfn "Test 4: Teleporting |-⟩ State (Phase)"
printfn "─────────────────────────────────────────────────────────"
printfn "Input:  Alice has |-⟩ = (|0⟩ - |1⟩)/√2"
printfn "Output: Bob should receive |-⟩"
printfn ""
printfn "This tests teleportation preserves relative phase!"
printfn ""

match teleportMinus backend with
| Ok result ->
    printfn "%s" (formatResult result)
    printfn ""
    printfn "✅ Success - Phase information preserved!"
| Error err ->
    printfn "❌ Error: %A" err

printfn ""
printfn ""

// ============================================================================
// Statistics Test: Multiple Teleportation Runs
// ============================================================================

printfn "Test 5: Statistical Analysis (Multiple Runs)"
printfn "─────────────────────────────────────────────────────────"
printfn "Running teleportation 20 times to analyze measurement distribution"
printfn ""

let prepareInputState (backend: IQuantumBackend) =
    result {
        let! state = backend.InitializeState 3
        // Prepare |+⟩ state
        return! backend.ApplyOperation (QuantumOperation.Gate (H 0)) state
    }

match runStatistics prepareInputState backend 20 with
| Ok results ->
    printfn "%s" (analyzeStatistics results)
    printfn ""
    printfn "✅ Statistics collected successfully"
| Error err ->
    printfn "❌ Error: %A" err

printfn ""
printfn ""

// ============================================================================
// Summary
// ============================================================================

printfn "╔══════════════════════════════════════════════════════════╗"
printfn "║                   Key Takeaways                          ║"
printfn "╚══════════════════════════════════════════════════════════╝"
printfn ""
printfn "📚 Quantum Teleportation Properties:"
printfn "  • Transfers quantum state (not matter/energy)"
printfn "  • Requires pre-shared entanglement (Bell pair)"
printfn "  • Requires 2 classical communication bits"
printfn "  • Original state destroyed (no-cloning theorem)"
printfn "  • Does NOT violate speed of light"
printfn ""
printfn "🎯 Production Applications:"
printfn "  • Quantum Networks (2030+ target)"
printfn "  • Quantum Repeaters (extend communication)"
printfn "  • Distributed Quantum Computing"
printfn "  • Enhanced QKD protocols"
printfn ""
printfn "🌐 Real-World Status:"
printfn "  ✅ Demonstrated: 1997 (first experiment)"
printfn "  ✅ Long-distance: 1400 km (Micius satellite, 2017)"
printfn "  ✅ Fiber optics: 143 km (USTC China, 2012)"
printfn "  🔮 Quantum Internet: Research phase (2030+ deployment)"
printfn ""
printfn "⚙️  Technical Details:"
printfn "  • 3 qubits required (Alice input, Alice Bell, Bob Bell)"
printfn "  • 4 possible measurement outcomes (00, 01, 10, 11)"
printfn "  • 4 possible corrections (None, X, Z, ZX)"
printfn "  • Theoretical fidelity: 100%%"
printfn "  • NISQ fidelity: 95-99%% (depends on Bell pair quality)"
printfn ""
printfn "🔬 Why This Matters:"
printfn "  Quantum teleportation is a fundamental protocol for"
printfn "  future quantum internet infrastructure, enabling:"
printfn "  - Secure quantum communication networks"
printfn "  - Distributed quantum computation"
printfn "  - Long-distance quantum entanglement distribution"
printfn ""
