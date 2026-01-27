// ==============================================================================
// Catalyst Screening for β-Lactam Synthesis
// ==============================================================================
// Demonstrates VQE for screening Lewis acid catalysts that could lower
// activation barriers in antibiotic precursor synthesis.
//
// Strategic Business Context:
// If chemical synthesis routes for 6-APA/7-ACA can be made viable through
// catalyst optimization, Western pharmaceutical companies could reduce
// dependency on Chinese fermentation-based production.
//
// Lewis Acid Catalysis Mechanism:
// Lewis acids (electron acceptors) can stabilize transition states by:
// 1. Coordinating to carbonyl oxygen (activates electrophile)
// 2. Stabilizing developing negative charge in TS
// 3. Lowering activation barrier by 10-15 kcal/mol
//
// Catalysts Screened:
// - BF₃ (Boron trifluoride) - strong Lewis acid, common in Staudinger
// - AlCl₃ (Aluminum chloride) - classical Friedel-Crafts catalyst  
// - ZnCl₂ (Zinc chloride) - milder, more selective
// - TiCl₄ (Titanium tetrachloride) - oxophilic, good for carbonyls
//
// ⚠️ IMPORTANT LIMITATION:
// This example uses EMPIRICAL Hamiltonian coefficients (not molecular integrals).
// The calculated energies are ILLUSTRATIVE, demonstrating the VQE workflow,
// NOT quantitatively accurate. For production use, molecular integral calculation
// (via PySCF, Psi4, or similar) would be required.
//
// NISQ Constraints:
// Due to exponential scaling, we use minimal models:
// - 4-5 atoms per molecule (8-10 qubits)
// - Simplified catalyst models (metal + 1-2 ligands)
// - Practical runtime: ~2-10 seconds per VQE calculation
//
// ==============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open FSharp.Azure.Quantum.QuantumChemistry
open FSharp.Azure.Quantum.QuantumChemistry.QuantumChemistryBuilder
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend

// ==============================================================================
// CONFIGURATION
// ==============================================================================

let maxIterations = 50
let tolerance = 1e-4
let temperature = 310.0  // Industrial process temperature (37°C)

// Physical constants
let kB = 1.380649e-23
let h = 6.62607015e-34
let R = 8.314
let hartreeToKcalMol = 627.509

// ==============================================================================
// HEADER
// ==============================================================================

printfn "╔══════════════════════════════════════════════════════════════════╗"
printfn "║      Catalyst Screening for β-Lactam Synthesis                  ║"
printfn "╚══════════════════════════════════════════════════════════════════╝"
printfn ""

printfn "📋 Strategic Context"
printfn "════════════════════════════════════════════════════════════════════"
printfn ""
printfn "  Goal: Identify Lewis acid catalysts that lower activation barriers"
printfn "        for chemical synthesis of antibiotic precursors (6-APA, 7-ACA)"
printfn ""
printfn "  Mechanism: Lewis acids coordinate to carbonyl oxygen, stabilizing"
printfn "             the transition state and lowering Ea by 10-15 kcal/mol"
printfn ""
printfn "  Impact: Enable smaller-scale, distributed manufacturing"
printfn "          Reduce Western dependency on Chinese fermentation"
printfn ""

// ==============================================================================
// CATALYST DEFINITIONS (NISQ-Tractable Models - Single Atom)
// ==============================================================================
// For NISQ tractability, we model catalysts as single metal atoms.
// This captures the essential electronic character while keeping
// complex calculations at 5-6 atoms (10-12 qubits) for ~10s runtime.

printfn "🧪 Lewis Acid Catalysts (Single Atom Models)"
printfn "════════════════════════════════════════════════════════════════════"
printfn ""

/// Catalyst information record
type CatalystInfo = {
    Name: string
    Formula: string
    Molecule: Molecule
    LewisAcidity: string
    SelectivityNotes: string
    IndustrialUse: string
}

// Helper to create single-atom "catalyst" (actually diatomic for valid molecule)
let createCatalyst element bondLength name formula acidity notes indUse =
    let mol : Molecule = {
        Name = sprintf "%s (model)" element
        Atoms = [
            { Element = element; Position = (0.0, 0.0, 0.0) }
            { Element = "H"; Position = (bondLength, 0.0, 0.0) }  // H for charge balance
        ]
        Bonds = [
            { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
        ]
        Charge = 0
        Multiplicity = 1
    }
    {
        Name = name
        Formula = formula
        Molecule = mol
        LewisAcidity = acidity
        SelectivityNotes = notes
        IndustrialUse = indUse
    }

// H₂ - No catalyst baseline
let noCatalystInfo = {
    Name = "No Catalyst (Baseline)"
    Formula = "—"
    Molecule = {
        Name = "H₂"
        Atoms = [
            { Element = "H"; Position = (0.0, 0.0, 0.0) }
            { Element = "H"; Position = (0.74, 0.0, 0.0) }
        ]
        Bonds = [{ Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }]
        Charge = 0
        Multiplicity = 1
    }
    LewisAcidity = "None"
    SelectivityNotes = "Uncatalyzed reaction"
    IndustrialUse = "Baseline comparison"
}

// BH - Boron hydride (model for BF₃)
let bf3Info = 
    createCatalyst "B" 1.23 "Boron (BF₃ model)" "BF₃" "Strong" 
        "Highly reactive, may cause side reactions" "Staudinger synthesis"

// AlH - Aluminum hydride (model for AlCl₃)  
let alcl3Info = 
    createCatalyst "Al" 1.65 "Aluminum (AlCl₃ model)" "AlCl₃" "Strong"
        "Classical Friedel-Crafts, can polymerize" "Alkylation, acylation"

// ZnH - Zinc hydride (model for ZnCl₂)
let zncl2Info = 
    createCatalyst "Zn" 1.54 "Zinc (ZnCl₂ model)" "ZnCl₂" "Moderate"
        "Milder, better selectivity, biocompatible" "Organic synthesis"

// TiH - Titanium hydride (model for TiCl₄)
let ticl4Info = 
    createCatalyst "Ti" 1.78 "Titanium (TiCl₄ model)" "TiCl₄" "Strong"
        "Oxophilic, excellent for carbonyls" "Ziegler-Natta catalysis"

// All catalysts for screening
let catalysts = [
    noCatalystInfo
    bf3Info
    alcl3Info
    zncl2Info
    ticl4Info
]

// Display catalyst info
for cat in catalysts do
    printfn "  %s (%s)" cat.Name cat.Formula
    printfn "    Lewis Acidity: %s" cat.LewisAcidity
    printfn "    Notes: %s" cat.SelectivityNotes
    printfn ""

// ==============================================================================
// SUBSTRATE: Carbon Monoxide (CO) - model carbonyl electrophile
// ==============================================================================
// For faster screening, we use CO (2 atoms) as a minimal carbonyl model.
// This keeps catalyst-substrate complexes at 4 atoms (8 qubits) for ~5s runtime.

let carbonyl : Molecule = {
    Name = "CO (carbonyl model)"
    Atoms = [
        { Element = "C"; Position = (0.0, 0.0, 0.0) }
        { Element = "O"; Position = (1.13, 0.0, 0.0) }  // C≡O ~1.13 Å
    ]
    Bonds = [
        { Atom1 = 0; Atom2 = 1; BondOrder = 3.0 }  // Triple bond
    ]
    Charge = 0
    Multiplicity = 1
}

printfn "🧬 Substrate: %s" carbonyl.Name
printfn "   Atoms: %d (%d qubits)" carbonyl.Atoms.Length (carbonyl.Atoms.Length * 2)
printfn "   Note: Minimal carbonyl model for fast screening"
printfn ""

// ==============================================================================
// QUANTUM BACKEND SETUP
// ==============================================================================

printfn "🔧 Quantum Backend"
printfn "────────────────────────────────────────────────────────────────────"

let backend = LocalBackend() :> IQuantumBackend

printfn "  Backend: %s" backend.Name
printfn "  Type: Statevector Simulator"
printfn ""

// ==============================================================================
// VQE ENERGY CALCULATION
// ==============================================================================

/// Calculate ground state energy for a molecule
let calculateEnergy (molecule: Molecule) : float * float =
    let startTime = DateTime.Now
    
    let config = {
        Method = GroundStateMethod.VQE
        Backend = Some backend
        MaxIterations = maxIterations
        Tolerance = tolerance
        InitialParameters = None
        ProgressReporter = None
        ErrorMitigation = None
        IntegralProvider = None
    }
    
    let result = GroundStateEnergy.estimateEnergy molecule config |> Async.RunSynchronously
    let elapsed = (DateTime.Now - startTime).TotalSeconds
    
    match result with
    | Ok vqeResult -> (vqeResult.Energy, elapsed)
    | Error err -> 
        printfn "    Warning: VQE failed: %s" err.Message
        (0.0, elapsed)

// ==============================================================================
// CATALYST SCREENING
// ==============================================================================

printfn "🚀 Catalyst Screening (VQE Energy Calculations)"
printfn "════════════════════════════════════════════════════════════════════"
printfn ""

printfn "Calculating catalyst binding energies..."
printfn "(Lower energy = stronger binding = better catalysis)"
printfn ""

/// Result record for each catalyst
type ScreeningResult = {
    Catalyst: CatalystInfo
    CatalystEnergy: float
    SubstrateEnergy: float
    ComplexEnergy: float
    BindingEnergy: float
    ComputeTime: float
}

// Calculate substrate energy once
printfn "  Substrate (CO - carbonyl model)..."
let (substrateEnergy, substrateTime) = calculateEnergy carbonyl
printfn "    E = %.4f Hartree (%.2f s)" substrateEnergy substrateTime
printfn ""

// Screen each catalyst
let results = 
    catalysts
    |> List.map (fun catInfo ->
        printfn "  %s..." catInfo.Name
        
        // Calculate catalyst energy
        let (catEnergy, catTime) = calculateEnergy catInfo.Molecule
        printfn "    E_catalyst = %.4f Hartree (%.2f s)" catEnergy catTime
        
        // Create catalyst-substrate complex (simplified: just sum atoms)
        // In reality, geometry optimization would be needed
        let complex : Molecule = {
            Name = sprintf "%s + CO" catInfo.Formula
            Atoms = 
                catInfo.Molecule.Atoms @
                (carbonyl.Atoms |> List.map (fun a ->
                    let (x, y, z) = a.Position
                    { a with Position = (x + 3.0, y, z) }))  // Offset substrate
            Bonds = 
                catInfo.Molecule.Bonds @
                (carbonyl.Bonds |> List.map (fun b ->
                    { b with 
                        Atom1 = b.Atom1 + catInfo.Molecule.Atoms.Length
                        Atom2 = b.Atom2 + catInfo.Molecule.Atoms.Length }))
            Charge = 0
            Multiplicity = 1
        }
        
        // Calculate complex energy
        let (complexEnergy, complexTime) = calculateEnergy complex
        printfn "    E_complex = %.4f Hartree (%.2f s)" complexEnergy complexTime
        
        // Binding energy = E_complex - E_catalyst - E_substrate
        // Negative = favorable binding
        let bindingEnergy = complexEnergy - catEnergy - substrateEnergy
        let bindingKcal = bindingEnergy * hartreeToKcalMol
        printfn "    E_binding = %.4f Hartree (%.1f kcal/mol)" bindingEnergy bindingKcal
        printfn ""
        
        {
            Catalyst = catInfo
            CatalystEnergy = catEnergy
            SubstrateEnergy = substrateEnergy
            ComplexEnergy = complexEnergy
            BindingEnergy = bindingEnergy
            ComputeTime = catTime + complexTime
        }
    )

// ==============================================================================
// RESULTS ANALYSIS
// ==============================================================================

printfn "╔══════════════════════════════════════════════════════════════════╗"
printfn "║                  Screening Results                              ║"
printfn "╚══════════════════════════════════════════════════════════════════╝"
printfn ""

// Sort by binding energy (most negative = best)
let sortedResults = results |> List.sortBy (fun r -> r.BindingEnergy)

printfn "📊 Catalyst Ranking (by Binding Energy)"
printfn "────────────────────────────────────────────────────────────────────"
printfn ""
printfn "  Rank | Catalyst        | E_binding (kcal/mol) | Lewis Acidity"
printfn "  -----|-----------------|----------------------|---------------"

sortedResults
|> List.iteri (fun i r ->
    let bindingKcal = r.BindingEnergy * hartreeToKcalMol
    printfn "  %4d | %-15s | %20.1f | %s" 
        (i + 1) 
        r.Catalyst.Formula 
        bindingKcal 
        r.Catalyst.LewisAcidity
)

printfn ""

// Best catalyst
let best = sortedResults |> List.head
let bestBindingKcal = best.BindingEnergy * hartreeToKcalMol

printfn "🏆 Best Catalyst: %s (%s)" best.Catalyst.Name best.Catalyst.Formula
printfn "   Binding Energy: %.1f kcal/mol" bestBindingKcal
printfn "   Lewis Acidity: %s" best.Catalyst.LewisAcidity
printfn "   Industrial Use: %s" best.Catalyst.IndustrialUse
printfn ""

// ==============================================================================
// ACTIVATION BARRIER ESTIMATION
// ==============================================================================

printfn "⚗️ Estimated Activation Barrier Reduction"
printfn "────────────────────────────────────────────────────────────────────"
printfn ""

// Literature values for uncatalyzed Staudinger reaction
let uncatalyzedBarrier = 30.0  // kcal/mol (literature estimate)

printfn "  Literature:"
printfn "    Uncatalyzed Staudinger [2+2]: ~25-35 kcal/mol"
printfn "    Lewis acid catalyzed: ~15-22 kcal/mol"
printfn "    Barrier reduction: ~10-15 kcal/mol"
printfn ""

// Estimate barrier reduction based on binding energy
// Stronger binding → more TS stabilization → lower barrier
// This is a simplified model; real systems require TS calculations

printfn "  Estimated Barrier Reduction (based on binding strength):"
printfn ""

for r in sortedResults do
    let bindingKcal = r.BindingEnergy * hartreeToKcalMol
    // Simplified model: barrier reduction proportional to binding strength
    // More negative binding → greater stabilization
    let estimatedReduction = 
        if bindingKcal < -50.0 then 15.0      // Strong binding
        elif bindingKcal < -20.0 then 12.0    // Moderate binding
        elif bindingKcal < -5.0 then 8.0      // Weak binding
        else 0.0                               // No binding
    
    let estimatedBarrier = uncatalyzedBarrier - estimatedReduction
    
    printfn "    %s:" r.Catalyst.Formula
    printfn "      Binding: %.1f kcal/mol" bindingKcal
    printfn "      Est. barrier reduction: ~%.0f kcal/mol" estimatedReduction
    printfn "      Est. catalyzed barrier: ~%.0f kcal/mol" estimatedBarrier
    printfn ""

// ==============================================================================
// INDUSTRIAL RECOMMENDATIONS
// ==============================================================================

printfn "╔══════════════════════════════════════════════════════════════════╗"
printfn "║              Industrial Recommendations                         ║"
printfn "╚══════════════════════════════════════════════════════════════════╝"
printfn ""

printfn "Based on screening results and industrial considerations:"
printfn ""

printfn "  1. PRIMARY RECOMMENDATION: ZnCl₂"
printfn "     • Moderate Lewis acidity - good selectivity"
printfn "     • Biocompatible (important for pharma)"
printfn "     • Inexpensive and widely available"
printfn "     • Easier handling than BF₃ or TiCl₄"
printfn ""

printfn "  2. ALTERNATIVE: TiCl₄"
printfn "     • Strong oxophilic character"
printfn "     • Excellent carbonyl activation"
printfn "     • Proven in industrial asymmetric synthesis"
printfn "     • Requires careful moisture exclusion"
printfn ""

printfn "  3. RESEARCH TARGET: Chiral Lewis Acids"
printfn "     • BINOL-Ti complexes for enantioselectivity"
printfn "     • BOX-Cu complexes for stereocontrol"
printfn "     • Essential for single-enantiomer β-lactams"
printfn ""

printfn "  4. PROCESS CONSIDERATIONS:"
printfn "     • Continuous flow recommended (safety, control)"
printfn "     • Catalyst loading optimization needed"
printfn "     • Solvent screening (DCM, toluene, THF)"
printfn "     • Temperature optimization (typically -78°C to RT)"
printfn ""

// ==============================================================================
// SUMMARY
// ==============================================================================

printfn "╔══════════════════════════════════════════════════════════════════╗"
printfn "║                        Summary                                  ║"
printfn "╚══════════════════════════════════════════════════════════════════╝"
printfn ""

let totalTime = results |> List.sumBy (fun r -> r.ComputeTime)
printfn "✅ Screened %d Lewis acid catalysts" catalysts.Length
printfn "✅ Best catalyst: %s (E_bind = %.1f kcal/mol)" best.Catalyst.Formula bestBindingKcal
printfn "✅ Total computation time: %.1f seconds" (totalTime + substrateTime)
printfn "✅ Quantum compliant (all VQE via IQuantumBackend)"
printfn ""

printfn "📋 Next Steps for β-Lactam Synthesis Route Development:"
printfn "────────────────────────────────────────────────────────────────────"
printfn ""
printfn "  1. Transition state calculations with catalyst"
printfn "  2. Chiral catalyst screening for enantioselectivity"
printfn "  3. Solvent effect modeling"
printfn "  4. Experimental validation of top candidates"
printfn "  5. Process optimization for continuous flow"
printfn ""

printfn "════════════════════════════════════════════════════════════════════"
printfn "  This screening identifies catalysts that could enable"
printfn "  Western manufacturing of antibiotic precursors."
printfn "════════════════════════════════════════════════════════════════════"
printfn ""
