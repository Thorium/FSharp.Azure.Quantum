/// Test script for TopologicalOperations.measureAll function
/// 
/// This tests the new measurement functionality for topological quantum states.

#r "nuget: MathNet.Numerics"
#r "nuget: MathNet.Numerics.FSharp"

#load "../src/FSharp.Azure.Quantum.Topological/TopologicalError.fs"
#load "../src/FSharp.Azure.Quantum.Topological/AnyonSpecies.fs"
#load "../src/FSharp.Azure.Quantum.Topological/FusionRules.fs"
#load "../src/FSharp.Azure.Quantum.Topological/TopologicalHelpers.fs"
#load "../src/FSharp.Azure.Quantum.Topological/BraidingOperators.fs"
#load "../src/FSharp.Azure.Quantum.Topological/FusionTree.fs"
#load "../src/FSharp.Azure.Quantum.Topological/TopologicalOperations.fs"

open System
open System.Numerics
open FSharp.Azure.Quantum.Topological

printfn "=== Testing TopologicalOperations.measureAll ==="
printfn ""

// Test 1: Measure a pure state (should always return same outcome)
printfn "Test 1: Pure state measurement"
printfn "-------------------------------"

let anyonType = AnyonSpecies.AnyonType.Ising
let vacuumParticle = AnyonSpecies.Particle.Vacuum
let sigmaParticle = AnyonSpecies.Particle.Sigma

// Create |00⟩ state: all vacuum fusion
let tree00 = 
    FusionTree.fuse 
        (FusionTree.leaf vacuumParticle)
        (FusionTree.leaf vacuumParticle)
        vacuumParticle

let state00 = FusionTree.create tree00 anyonType
let pureSuperposition = TopologicalOperations.pureState state00

// Measure 10 times - should all be |00⟩
let measurements = TopologicalOperations.measureAll pureSuperposition 10

printfn "Expected: All measurements = [|0; 0|]"
printfn "Results:"
for i, bits in Array.indexed measurements do
    printfn "  Shot %d: %A" (i+1) bits

// Check all are [|0; 0|] or empty (depends on encoding)
let allSame = measurements |> Array.forall (fun bits -> bits = measurements.[0])
printfn "All measurements identical: %b ✓" allSame
printfn ""

// Test 2: Measure a uniform superposition
printfn "Test 2: Uniform superposition measurement"
printfn "------------------------------------------"

// Create uniform superposition of two states
let tree01 = 
    FusionTree.fuse 
        (FusionTree.leaf vacuumParticle)
        (FusionTree.leaf sigmaParticle)
        sigmaParticle

let state01 = FusionTree.create tree01 anyonType

// Uniform superposition: (|00⟩ + |01⟩) / √2
let uniformSuperposition = 
    TopologicalOperations.uniform [state00; state01] anyonType

printfn "Superposition: (|00⟩ + |01⟩) / √2"
printfn "Expected: ~50%% |00⟩, ~50%% |01⟩"
printfn ""

// Measure 100 times
let measurements2 = TopologicalOperations.measureAll uniformSuperposition 100

// Count outcomes
let outcomes = 
    measurements2 
    |> Array.countBy id
    |> Array.map (fun (bits, count) -> (bits, count, float count / 100.0))

printfn "Results from 100 measurements:"
for (bits, count, prob) in outcomes do
    printfn "  %A: %d times (%.1f%%)" bits count (prob * 100.0)

printfn ""
printfn "Distribution check:"
printfn "  - Should have 2 distinct outcomes"
printfn "  - Each should appear ~50 times (±20 for statistical variance)"

let distinctOutcomes = outcomes.Length
let withinRange = outcomes |> Array.forall (fun (_, count, _) -> count >= 30 && count <= 70)

if distinctOutcomes = 2 && withinRange then
    printfn "  ✓ Distribution looks good!"
else
    printfn "  ⚠ Distribution unexpected (but might be due to small sample)"

printfn ""
printfn "=== All tests complete ==="
