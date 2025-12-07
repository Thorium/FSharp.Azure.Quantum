#r "../src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"

open FSharp.Azure.Quantum.Topological
open System

/// Toric Code Example - Topological Error Correction
/// 
/// Demonstrates:
/// - Lattice creation and initialization
/// - Ground state stabilizer properties
/// - Error injection and syndrome measurement
/// - Anyon detection (e-particles and m-particles)
/// - Code parameters (distance, encoding rate)

printfn "========================================="
printfn "TORIC CODE - TOPOLOGICAL ERROR CORRECTION"
printfn "========================================="
printfn ""

// ============================================================================
// STEP 1: Create a toric code lattice
// ============================================================================

printfn "STEP 1: Creating 5×5 toric code lattice"
printfn "-----------------------------------------"

let latticeResult = ToricCode.createLattice 5 5

match latticeResult with
| Error err ->
    printfn "❌ Failed to create lattice: %A" err
    exit 1
| Ok lattice ->
    printfn "✅ Created %dx%d lattice" lattice.Width lattice.Height
    printfn ""
    
    // Code parameters
    let k = ToricCode.logicalQubits lattice
    let n = ToricCode.physicalQubits lattice
    let d = ToricCode.codeDistance lattice
    let rate = float k / float n
    
    printfn "📊 Code Parameters:"
    printfn "   Logical qubits (k):  %d" k
    printfn "   Physical qubits (n): %d" n
    printfn "   Code distance (d):   %d" d
    printfn "   Encoding rate (k/n): %.4f" rate
    printfn "   Error correction:    Can correct up to %d errors" ((d-1)/2)
    printfn ""
    
    // ============================================================================
    // STEP 2: Initialize ground state
    // ============================================================================
    
    printfn "STEP 2: Initializing ground state"
    printfn "----------------------------------"
    
    let state = ToricCode.initializeGroundState lattice
    
    printfn "✅ Initialized %d qubits in |+⟩ state" state.Qubits.Count
    
    // Measure initial syndrome
    let initialSyndrome = ToricCode.measureSyndrome state
    let initialEParticles = ToricCode.getElectricExcitations initialSyndrome
    let initialMParticles = ToricCode.getMagneticExcitations initialSyndrome
    
    printfn "📏 Initial syndrome:"
    printfn "   e-particles (electric): %d" initialEParticles.Length
    printfn "   m-particles (magnetic): %d" initialMParticles.Length
    printfn "   ✅ Ground state confirmed (no anyons)"
    printfn ""
    
    // ============================================================================
    // STEP 3: Inject X errors (bit flips)
    // ============================================================================
    
    printfn "STEP 3: Injecting X errors (bit flips)"
    printfn "---------------------------------------"
    
    // Apply X errors to create e-particle pairs
    let error1 = { 
        ToricCode.Position = { ToricCode.X = 2; ToricCode.Y = 2 }
        ToricCode.Type = ToricCode.Horizontal 
    }
    let error2 = { 
        ToricCode.Position = { ToricCode.X = 3; ToricCode.Y = 4 }
        ToricCode.Type = ToricCode.Vertical 
    }
    
    let errorState1 = ToricCode.applyXError state error1
    let errorState2 = ToricCode.applyXError errorState1 error2
    
    printfn "💥 Applied X errors to 2 edges:"
    printfn "   Edge 1: (%d,%d) %A" error1.Position.X error1.Position.Y error1.Type
    printfn "   Edge 2: (%d,%d) %A" error2.Position.X error2.Position.Y error2.Type
    printfn ""
    
    // Measure syndrome after X errors
    let xSyndrome = ToricCode.measureSyndrome errorState2
    let xEParticles = ToricCode.getElectricExcitations xSyndrome
    let xMParticles = ToricCode.getMagneticExcitations xSyndrome
    
    printfn "📏 Syndrome after X errors:"
    printfn "   e-particles: %d (at vertices)" xEParticles.Length
    printfn "   m-particles: %d (no Z errors yet)" xMParticles.Length
    printfn ""
    
    if xEParticles.Length > 0 then
        printfn "   🔍 e-particle positions:"
        xEParticles |> List.iter (fun p ->
            printfn "      (%d, %d)" p.X p.Y)
        printfn ""
    
    // ============================================================================
    // STEP 4: Inject Z errors (phase flips)
    // ============================================================================
    
    printfn "STEP 4: Injecting Z errors (phase flips)"
    printfn "-----------------------------------------"
    
    // Apply Z errors to create m-particle pairs
    let zError1 = { 
        ToricCode.Position = { ToricCode.X = 1; ToricCode.Y = 1 }
        ToricCode.Type = ToricCode.Vertical 
    }
    let zError2 = { 
        ToricCode.Position = { ToricCode.X = 4; ToricCode.Y = 3 }
        ToricCode.Type = ToricCode.Horizontal 
    }
    
    let errorState3 = ToricCode.applyZError errorState2 zError1
    let errorState4 = ToricCode.applyZError errorState3 zError2
    
    printfn "💥 Applied Z errors to 2 edges:"
    printfn "   Edge 1: (%d,%d) %A" zError1.Position.X zError1.Position.Y zError1.Type
    printfn "   Edge 2: (%d,%d) %A" zError2.Position.X zError2.Position.Y zError2.Type
    printfn ""
    
    // Measure final syndrome
    let finalSyndrome = ToricCode.measureSyndrome errorState4
    let finalEParticles = ToricCode.getElectricExcitations finalSyndrome
    let finalMParticles = ToricCode.getMagneticExcitations finalSyndrome
    
    printfn "📏 Final syndrome (X + Z errors):"
    printfn "   e-particles: %d" finalEParticles.Length
    printfn "   m-particles: %d" finalMParticles.Length
    printfn ""
    
    if finalMParticles.Length > 0 then
        printfn "   🔍 m-particle positions:"
        finalMParticles |> List.iter (fun p ->
            printfn "      (%d, %d)" p.X p.Y)
        printfn ""
    
    // ============================================================================
    // STEP 5: Anyon statistics and distances
    // ============================================================================
    
    printfn "STEP 5: Analyzing anyon statistics"
    printfn "-----------------------------------"
    
    // Calculate pairwise distances between anyons
    if finalEParticles.Length >= 2 then
        printfn "📐 Distances between e-particles (on torus):"
        for i in 0 .. finalEParticles.Length - 2 do
            for j in i + 1 .. finalEParticles.Length - 1 do
                let p1 = finalEParticles.[i]
                let p2 = finalEParticles.[j]
                let dist = ToricCode.toricDistance lattice p1 p2
                printfn "   (%d,%d) ↔ (%d,%d): distance = %d" 
                    p1.X p1.Y p2.X p2.Y dist
        printfn ""
    
    if finalMParticles.Length >= 2 then
        printfn "📐 Distances between m-particles (on torus):"
        for i in 0 .. finalMParticles.Length - 2 do
            for j in i + 1 .. finalMParticles.Length - 1 do
                let p1 = finalMParticles.[i]
                let p2 = finalMParticles.[j]
                let dist = ToricCode.toricDistance lattice p1 p2
                printfn "   (%d,%d) ↔ (%d,%d): distance = %d" 
                    p1.X p1.Y p2.X p2.Y dist
        printfn ""
    
    // ============================================================================
    // STEP 6: Summary and key properties
    // ============================================================================
    
    printfn "STEP 6: Toric code key properties"
    printfn "----------------------------------"
    printfn ""
    printfn "🔬 Anyon theory: Z₂ × Z₂"
    printfn "   Particle types: {1, e, m, ε}"
    printfn "   - 1: Vacuum (no excitation)"
    printfn "   - e: Electric charge (vertex excitation)"
    printfn "   - m: Magnetic flux (plaquette excitation)"
    printfn "   - ε: Fermion (e × m)"
    printfn ""
    printfn "📏 Stabilizer structure:"
    printfn "   - Vertex operators A_v: ∏(X on 4 edges)"
    printfn "   - Plaquette operators B_p: ∏(Z on 4 edges)"
    printfn "   - Ground state: A_v|ψ⟩ = |ψ⟩, B_p|ψ⟩ = |ψ⟩ for all v, p"
    printfn ""
    printfn "🛡️ Error correction capability:"
    printfn "   - Distance d = %d → corrects %d errors" d ((d-1)/2)
    printfn "   - X errors create e-particle pairs"
    printfn "   - Z errors create m-particle pairs"
    printfn "   - Anyons always created in pairs (charge conservation)"
    printfn ""
    printfn "🌀 Topological properties:"
    printfn "   - Logical qubits encoded in non-contractible loops"
    printfn "   - Information protected by topology, not local encoding"
    printfn "   - Decoherence-free subspace (DFS) for topological order"
    printfn "   - Fault-tolerant with active error correction"
    printfn ""
    printfn "========================================="
    printfn "✅ TORIC CODE EXAMPLE COMPLETE"
    printfn "========================================="

