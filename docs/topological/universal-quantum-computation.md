# Universal Quantum Computation with Ising Anyons

This guide demonstrates how to achieve **universal quantum computation** using Ising anyons (Majorana zero modes), which naturally support only Clifford operations.

## The Challenge

**Ising anyons can only perform Clifford operations natively** through braiding:
- ✅ Hadamard gate (H)
- ✅ CNOT gate
- ✅ Phase gate (S)
- ✅ Pauli gates (X, Y, Z)

**But Clifford gates alone are NOT universal!** You cannot implement arbitrary quantum algorithms with just Clifford operations.

## The Solution: Magic State Distillation

To achieve universality, we add **non-Clifford gates** (specifically T-gates) via **magic state distillation**:

1. **Prepare noisy magic states** |T⟩ = (|0⟩ + e^(iπ/4)|1⟩) / √2
2. **Distill to high fidelity** using error detection codes
3. **Inject purified magic states** to implement T-gates via gate teleportation
4. **Combine with native Clifford ops** → Universal computation!

**Gate Set**: Clifford + T = Universal quantum computation ✓

### What the library models

`MagicStateDistillation` is a **fidelity and resource model** of this protocol, not a circuit-level simulation of it:

- `distill15to1` computes the output fidelity from the standard formula p_out ≈ 35p³ and draws 14 random syndrome bits, each set with probability p (the batch's average input error rate). A batch therefore passes the syndrome check with probability **(1 − p)^14**: about 0.49 at p = 5% and 0.23 at p = 10%. `Accepted` reports the outcome. An accepted batch returns the first input state's qubit with the distilled fidelity; a rejected batch keeps the average input fidelity. `AcceptanceProbability` is not that pass rate: it holds 1 − 35p for an accepted batch and 0 for a rejected one.
- `distillIterative` keeps only the accepted batches of each round and returns an `Error` when too few pass to finish the remaining rounds, so it needs more than 15^rounds input states in practice (see [Iterative Distillation](#iterative-distillation)).
- `applyTGateToQubit` applies the exact T rotation to one qubit of a superposition, through `TopologicalOperations.tGate`; `applyTGate` does the same for a single basis state. Both draw a random correction flag and report the magic state's fidelity as `GateFidelity`. The magic state's imperfection is reported only as that number; it is not applied to the amplitudes.

Both these functions and the topological backend apply the T phase directly to the amplitudes; the backend does not compile T into braids, because no Ising braid is a T gate.

## Quick Start Example

```fsharp
open FSharp.Azure.Quantum.Topological

// 1. Prepare noisy magic states (15 per distillation round)
let random = System.Random()
let noisyErrorRate = 0.05  // 5% error

let prepareBatch () =
    [1..15]
    |> List.map (fun _ -> 
        MagicStateDistillation.prepareNoisyMagicState noisyErrorRate AnyonSpecies.AnyonType.Ising
    )
    |> List.choose Result.toOption

// 2. Distill to a high-fidelity magic state. A batch passes the syndrome check with
//    probability (1 - p)^14, about 0.49 at p = 5%; a rejected batch is discarded,
//    so retry with fresh states until one passes.
let rec distillUntilAccepted attempts =
    match MagicStateDistillation.distill15to1 random (prepareBatch ()) with
    | Ok result when result.Accepted -> result, attempts
    | Ok _ -> distillUntilAccepted (attempts + 1)
    | Error err -> failwith err.Message

let distillResult, attempts = distillUntilAccepted 1
let purifiedState = distillResult.PurifiedState

printfn "Input fidelity:  %.4f" (1.0 - noisyErrorRate)
printfn "Output fidelity: %.6f (batches tried: %d)" purifiedState.Fidelity attempts
printfn "Error suppression: %.1fx" (noisyErrorRate / (1.0 - purifiedState.Fidelity))

// 3. Create a topological qubit (|0⟩ state)
let sigma = AnyonSpecies.Particle.Sigma
let vacuum = AnyonSpecies.Particle.Vacuum

let dataQubit = 
    let left = FusionTree.leaf sigma
    let right = FusionTree.leaf sigma
    let tree = FusionTree.fuse left right vacuum
    FusionTree.create tree AnyonSpecies.AnyonType.Ising

// 4. Apply a T gate by magic state injection. T|0⟩ = |0⟩, so the output is the
//    same basis state; GateFidelity reports the magic state's fidelity.
let tGateResult = 
    MagicStateDistillation.applyTGate random dataQubit purifiedState
    |> Result.defaultWith (fun err -> failwith err.Message)

printfn "\nT gate applied by injection"
printfn "Output terms: %d" tGateResult.OutputState.Terms.Length
printfn "Gate fidelity: %.6f" tGateResult.GateFidelity
printfn "S correction needed: %b" tGateResult.CorrectionApplied
```

**Output** (the number of batches tried and the correction flag are random):
```
Input fidelity:  0.9500
Output fidelity: 0.995625 (batches tried: 2)
Error suppression: 11.4x

T gate applied by injection
Output terms: 1
Gate fidelity: 0.995625
S correction needed: false
```

`applyTGate` refuses magic states below 99% fidelity, so undistilled 95% states are rejected with an error.

## How Magic State Distillation Works

### The 15-to-1 Protocol (Bravyi-Kitaev 2005)

**Input:** 15 noisy magic states with error rate `p`  
**Output:** 1 purified magic state with error rate `p_out ≈ 35p³`

**Key property:** **Cubic error suppression**
- 10% error → 3.5% error (2.9× improvement)
- 5% error → 0.44% error (11.4× improvement)  
- 1% error → 0.0035% error (286× improvement)

The formula is only meaningful for small p: at p ≈ 17% it reaches p_out = p, and above that a round makes things worse.

### Iterative Distillation

Apply 15-to-1 multiple times for doubly exponential error suppression (the exponent triples each round):

```fsharp
// Two rounds need at least 15^2 = 225 states, and more to absorb rejected batches.
// At p = 10% a round-1 batch passes with probability 0.9^14 ≈ 0.23; round-2 batches
// (p ≈ 3.5%) pass with 0.965^14 ≈ 0.61, and round 2 uses only full batches of 15
// accepted states. With 1000 round-1 batches (15,000 states) the run fails with
// probability about 1.5e-6 under this model.
let round1States =
    [1..15000]
    |> List.map (fun _ -> MagicStateDistillation.prepareNoisyMagicState 0.10 AnyonSpecies.AnyonType.Ising)
    |> List.choose Result.toOption

// 2 rounds of distillation: p → 35p³ → 35(35p³)³ = 35⁴p⁹
let finalState = 
    MagicStateDistillation.distillIterative random 2 round1States
    |> Result.defaultWith (fun err -> failwith err.Message)

printfn "Input error:  %.4f" 0.10
printfn "Output error: %.7f" (1.0 - finalState.Fidelity)
printfn "Suppression:  %.1fx" (0.10 / (1.0 - finalState.Fidelity))
```

**Output:**
```
Input error:  0.1000
Output error: 0.0015006
Suppression:  66.6x
```

`distillIterative` accepts 1 to 5 rounds and needs at least 15^rounds input states. Each round distils every full batch of 15, keeps only the batches that pass the syndrome check, and leaves unused any states after the last full batch. If fewer states pass than the remaining rounds need (15^(rounds left)), it returns an `Error`.

How many states are enough follows from the pass rate (1 − p)^14. For two rounds, the chance that a run fails:

| Input error | 225 states | 3,000 states | 15,000 states |
|-------------|-----------|--------------|---------------|
| 5%  | ≈ 1 | ≈ 1.3 × 10⁻⁷ | < 10⁻¹¹ |
| 10% | ≈ 1 | ≈ 0.10 | ≈ 1.5 × 10⁻⁶ |

## Resource Estimation

Estimate how many noisy states you need for a target fidelity:

```fsharp
let targetFidelity = 0.9999  // 99.99% fidelity
let noisyFidelity = 0.95     // Start with 95% fidelity

let estimate = 
    MagicStateDistillation.estimateResources targetFidelity noisyFidelity

printfn "%s" (MagicStateDistillation.displayResourceEstimate estimate)
```

**Output:**
```
Resource Estimate for 99.99% fidelity:
  Distillation Rounds: 2
  Noisy States Required: 225
  Output Fidelity: 99.9997%
  Overhead Factor: 225x
```

`estimateResources` stops at 5 rounds even if the target is not reached; check `OutputFidelity` against your target. `NoisyStatesRequired` is 15^rounds, the count if every batch passes the syndrome check; because rejected batches are discarded, feed `distillIterative` more (see the table under [Iterative Distillation](#iterative-distillation)).

## Example: Implementing Toffoli Gate

The **Toffoli gate** (CCNOT) can be decomposed into Clifford + T gates. The standard decomposition (Nielsen & Chuang, Fig. 4.9) uses:
- **Clifford operations**: 6 CNOTs, 2 H and 1 S (native via braiding)
- **7 T or T† gates** (via magic state injection); 7 is the minimum T-count for Toffoli

```fsharp
// For 99.99% fidelity Toffoli gate
let tGatesNeeded = 7
let estimate = MagicStateDistillation.estimateResources 0.9999 0.95

let totalNoisyStates = tGatesNeeded * estimate.NoisyStatesRequired

printfn "Toffoli Gate Resource Requirements:"
printfn "  T-gates needed: %d" tGatesNeeded
printfn "  Rounds per T-gate: %d" estimate.DistillationRounds
printfn "  Noisy states per T-gate: %d" estimate.NoisyStatesRequired
printfn "  Total noisy states: %d" totalNoisyStates
printfn "  Gate fidelity: %.4f%%" (estimate.OutputFidelity * 100.0)
```

**Output:**
```
Toffoli Gate Resource Requirements:
  T-gates needed: 7
  Rounds per T-gate: 2
  Noisy states per T-gate: 225
  Total noisy states: 1575
  Gate fidelity: 99.9997%
```

(The "gate fidelity" printed here is the fidelity of each distilled magic state; the Toffoli's overall fidelity is lower, since its seven T gates compound.)

## Complete Algorithm Workflow

Here's a complete example: plan and model the magic states for one T gate, then run H·T·H on one qubit in the fusion-tree simulator:

```fsharp
open FSharp.Azure.Quantum.Topological

let runQuantumAlgorithm () =
    let random = System.Random()
    
    // Step 1: Resource planning
    printfn "=== Step 1: Resource Planning ==="
    let targetFidelity = 0.999
    let noisyErrorRate = 0.05
    
    let estimate = 
        MagicStateDistillation.estimateResources targetFidelity (1.0 - noisyErrorRate)
    
    printfn "One T gate requires: %d noisy magic states if every batch passes (%d rounds)" estimate.NoisyStatesRequired estimate.DistillationRounds
    
    // Step 2: Prepare and distill magic states.
    // Batches pass the syndrome check with probability (1 - p)^14: about 0.49 in round 1
    // (p = 5%) and 0.94 in round 2 (p ≈ 0.44%). With 200 round-1 batches (3000 states)
    // both rounds succeed except with probability about 1.3e-7 under this model.
    printfn "\n=== Step 2: Magic State Preparation ==="
    let noisyStateCount = 3000

    let noisyStates = 
        [1..noisyStateCount]
        |> List.map (fun _ -> 
            MagicStateDistillation.prepareNoisyMagicState noisyErrorRate AnyonSpecies.AnyonType.Ising
        )
        |> List.choose Result.toOption
    
    let purifiedState = 
        MagicStateDistillation.distillIterative random estimate.DistillationRounds noisyStates
        |> Result.defaultWith (fun err -> failwith err.Message)
    
    printfn "Distilled magic state fidelity: %.6f" purifiedState.Fidelity
    
    // Step 3: Run H · T · H on one qubit (|0> = 4 sigma anyons: qubit pair + parity pair)
    printfn "\n=== Step 3: Run the Circuit ==="
    let circuitResult =
        topologicalResult {
            let! tree = FusionTree.fromComputationalBasis [ 0 ] AnyonSpecies.AnyonType.Ising
            let initial = TopologicalOperations.pureState (FusionTree.create tree AnyonSpecies.AnyonType.Ising)
            let! afterH = TopologicalOperations.hadamard 0 initial   // Clifford
            // T by magic state injection: exact T phase on the amplitudes
            let! injected = MagicStateDistillation.applyTGateToQubit random 0 afterH purifiedState
            let! finalState = TopologicalOperations.hadamard 0 injected.OutputState
            return finalState, injected.GateFidelity
        }
    
    // Step 4: Measurement probabilities
    printfn "\n=== Step 4: Measurement ==="
    match circuitResult with
    | Ok (finalState, gateFidelity) ->
        let p1 = TopologicalOperations.probabilityOfBitstring [| 1 |] finalState
        printfn "P(1) after H·T·H: %.4f (ideal sin²(π/8) = 0.1464)" p1
        printfn "Injected T gate fidelity (from the magic state): %.6f" gateFidelity
    | Error err -> printfn "Error: %s" err.Message

// Run it!
runQuantumAlgorithm()
```

## Performance Characteristics

### Error Suppression

| Input Fidelity | Output Fidelity (1 round) | Improvement |
|----------------|---------------------------|-------------|
| 90% (10% error) | 96.5% (3.5% error) | 2.9× |
| 95% (5% error) | 99.56% (0.44% error) | 11.4× |
| 99% (1% error) | 99.9965% (0.0035% error) | 286× |

### Resource Overhead

| Target Fidelity | Rounds | Noisy States | Overhead |
|-----------------|--------|--------------|----------|
| 99% | 1 | 15 | 15× |
| 99.9% | 1-2 | 15-225 | 15-225× |
| 99.99% | 2 | 225 | 225× |
| 99.999% | 2-3 | 225-3,375 | 225-3,375× |

(Ranges depend on the input fidelity; from 95% input, one round gives 99.56% and two give 99.9997%.)

**Rule of thumb:** Each extra round multiplies the cost by 15 and roughly cubes the error, so a round buys several "9"s once the input error is small.

## Integration with Topological Error Correction

Magic state distillation works synergistically with topological protection:

1. **Topological protection** (from braiding):
   - Protects against local perturbations
   - In theory, errors are suppressed exponentially with anyon separation
   - Handles Clifford operations

2. **Magic state distillation** (for T-gates):
   - Error detection on encoded states
   - Polynomial (cubic) error suppression per round
   - Handles non-Clifford operations

**Combined**: the standard route to fault-tolerant universal quantum computation with Ising anyons. This library models both parts on a classical computer; it does not demonstrate them on hardware.

## API Reference

### Core Functions

All of these live in the `MagicStateDistillation` module:

```fsharp
// Prepare noisy magic state
val prepareNoisyMagicState : 
    errorRate:float -> 
    anyonType:AnyonSpecies.AnyonType -> 
    TopologicalResult<MagicState>

// Single round of 15-to-1 distillation
val distill15to1 : 
    random:Random -> 
    inputStates:MagicState list -> 
    TopologicalResult<DistillationResult>

// Iterative distillation (multiple rounds; rejected batches are discarded)
val distillIterative : 
    random:Random -> 
    rounds:int -> 
    initialStates:MagicState list -> 
    TopologicalResult<MagicState>

// Apply T to one qubit of a superposition via magic state injection
val applyTGateToQubit : 
    random:Random -> 
    qubitIndex:int -> 
    data:TopologicalOperations.Superposition -> 
    magicState:MagicState -> 
    TopologicalResult<TGateResult>

// Apply T to a basis state (qubit 0) via magic state injection
val applyTGate : 
    random:Random -> 
    dataQubit:FusionTree.State -> 
    magicState:MagicState -> 
    TopologicalResult<TGateResult>

// Estimate resources for target fidelity (at most 5 rounds)
val estimateResources : 
    targetFidelity:float -> 
    noisyStateFidelity:float -> 
    ResourceEstimate

// Output fidelity of one 15-to-1 round: 1 - 35 (1 - f)^3
val calculateDistilledFidelity : inputFidelity:float -> float

// Text summaries
val displayMagicState : state:MagicState -> string
val displayDistillationResult : result:DistillationResult -> string
val displayResourceEstimate : estimate:ResourceEstimate -> string
```

### Types

```fsharp
type MagicState = {
    QubitState: FusionTree.State
    Fidelity: float
    ErrorRate: float
}

type DistillationResult = {
    PurifiedState: MagicState          // input fidelity when the batch was rejected
    Accepted: bool                     // syndrome check passed
    AcceptanceProbability: float       // 1 - 35p when accepted, 0 when rejected
    InputStatesConsumed: int
    Syndromes: bool list
}

type TGateResult = {
    OutputState: TopologicalOperations.Superposition   // exact T rotation of the input
    CorrectionApplied: bool
    GateFidelity: float                                // the magic state's fidelity
}

type ResourceEstimate = {
    TargetFidelity: float
    DistillationRounds: int
    NoisyStatesRequired: int
    OutputFidelity: float
    OverheadFactor: int
}
```

## Further Reading

- **Bravyi & Kitaev (2005)**: "Universal quantum computation with ideal Clifford gates and noisy ancillas"
- **Simon, "Topological Quantum" (2023)**: Chapters on magic state distillation
- [Topological Documentation Index](./index.md)
- [Topological Error Correction](./developer-deep-dive.md#toric-code-topological-error-correction)

## See Also

- [Ising Anyon Braiding](./developer-deep-dive.md#braiding-operations---quantum-gates-as-geometry)
- [Fusion Trees](./developer-deep-dive.md#fusion-trees-the-core-data-structure)
- [Topological Error Correction](./developer-deep-dive.md#toric-code-topological-error-correction)
