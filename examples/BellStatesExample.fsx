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

//#r "nuget: FSharp.Azure.Quantum"
#r "../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

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
