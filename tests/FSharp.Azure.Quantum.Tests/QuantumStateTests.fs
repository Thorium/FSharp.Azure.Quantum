namespace FSharp.Azure.Quantum.Tests

open System
open System.Numerics
open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.LocalSimulator
open FSharp.Azure.Quantum.Backends.DWaveTypes
open FSharp.Azure.Quantum.Core.BackendAbstraction

module QuantumStateTests =
    
    // ============================================================================
    // HELPER FUNCTIONS
    // ============================================================================
    
    let createBellState () =
        // Create |00⟩ + |11⟩ Bell state (EPR pair) using backend abstraction
        let sv = StateVector.init 2
        // We simulate gate application by manually updating the state vector for testing purposes
        // H on qubit 0
        // |00> -> (|00> + |10>)/sqrt(2)
        let sqrt2inv = 1.0 / Math.Sqrt(2.0)
        let amps1 = Array.create 4 Complex.Zero
        amps1.[0] <- Complex(sqrt2inv, 0.0)
        amps1.[2] <- Complex(sqrt2inv, 0.0) // qubit 0 is least significant bit if big endian, but let's check convention
        // Actually, F# Azure Quantum seems to use Little Endian for qubits usually?
        // Let's assume standard Q# convention: qubit 0 is lowest index.
        // If state is |q1 q0>, index is q1*2 + q0.
        // H on q0:
        // |00> -> (|00> + |01>)/sqrt(2)  (if q0 is least significant bit)
        // Let's stick to the manual construction that matches the expected test outcomes
        
        // Let's construct the expected Bell state directly: (|00> + |11>)/sqrt(2)
        // |00> is index 0
        // |11> is index 3
        let bellAmps = Array.create 4 Complex.Zero
        bellAmps.[0] <- Complex(sqrt2inv, 0.0)
        bellAmps.[3] <- Complex(sqrt2inv, 0.0)
        
        QuantumState.StateVector (StateVector.create bellAmps)
    
    let createIsingProblem () : IsingProblem =
        // Simple 3-variable Ising problem: minimize h₀s₀ + h₁s₁ + h₂s₂ + J₀₁s₀s₁
        {
            LinearCoeffs = Map.ofList [(0, -1.0); (1, -0.5); (2, -0.3)]
            QuadraticCoeffs = Map.ofList [((0, 1), 0.5)]
            Offset = 0.0
        }
    
    let createDWaveSolutions () : DWaveSolution list =
        [
            // Best solution: all spins down (s = -1)
            {
                Spins = Map.ofList [(0, -1); (1, -1); (2, -1)]
                Energy = -1.8
                NumOccurrences = 70
                ChainBreakFraction = 0.0
            }
            // Second best: s₀ = +1, rest down
            {
                Spins = Map.ofList [(0, 1); (1, -1); (2, -1)]
                Energy = -0.8
                NumOccurrences = 25
                ChainBreakFraction = 0.0
            }
            // Third: s₁ = +1, rest down
            {
                Spins = Map.ofList [(0, -1); (1, 1); (2, -1)]
                Energy = -1.3
                NumOccurrences = 5
                ChainBreakFraction = 0.0
            }
        ]
    
    // ============================================================================
    // STATEVECTOR TESTS
    // ============================================================================
    
    [<Fact>]
    let ``StateVector - numQubits should return correct qubit count`` () =
        let state1 = QuantumState.StateVector (StateVector.init 1)
        let state3 = QuantumState.StateVector (StateVector.init 3)
        let state5 = QuantumState.StateVector (StateVector.init 5)
        
        Assert.Equal(1, QuantumState.numQubits state1)
        Assert.Equal(3, QuantumState.numQubits state3)
        Assert.Equal(5, QuantumState.numQubits state5)
    
    [<Fact>]
    let ``StateVector - stateType should return GateBased`` () =
        let state = QuantumState.StateVector (StateVector.init 2)
        Assert.Equal(GateBased, QuantumState.stateType state)
    
    [<Fact>]
    let ``StateVector - isPure should return true`` () =
        let state = QuantumState.StateVector (StateVector.init 2)
        Assert.True(QuantumState.isPure state)
    
    [<Fact>]
    let ``StateVector - dimension should return 2^n`` () =
        let state2 = QuantumState.StateVector (StateVector.init 2)
        let state4 = QuantumState.StateVector (StateVector.init 4)
        
        Assert.Equal(4, QuantumState.dimension state2)    // 2^2
        Assert.Equal(16, QuantumState.dimension state4)   // 2^4
    
    [<Fact>]
    let ``StateVector - measure should return valid bitstrings`` () =
        let bellState = createBellState ()
        let measurements = QuantumState.measure bellState 100
        
        // All measurements should be length 2
        Assert.All(measurements, fun bits -> Assert.Equal(2, bits.Length))
        
        // All bits should be 0 or 1
        Assert.All(measurements, fun bits ->
            Assert.All(bits, fun bit -> Assert.True(bit = 0 || bit = 1)))
        
        // Bell state should only give |00⟩ or |11⟩
        Assert.All(measurements, fun bits ->
            Assert.True((bits.[0] = 0 && bits.[1] = 0) || (bits.[0] = 1 && bits.[1] = 1)))
    
    [<Fact>]
    let ``StateVector - probability should calculate correct probabilities`` () =
        let bellState = createBellState ()
        
        // Bell state: |00⟩ and |11⟩ each have probability ~0.5
        let prob00 = QuantumState.probability [|0; 0|] bellState
        let prob11 = QuantumState.probability [|1; 1|] bellState
        let prob01 = QuantumState.probability [|0; 1|] bellState
        let prob10 = QuantumState.probability [|1; 0|] bellState
        
        Assert.Equal(0.5, prob00, 10)
        Assert.Equal(0.5, prob11, 10)
        Assert.Equal(0.0, prob01, 10)
        Assert.Equal(0.0, prob10, 10)
    
    [<Fact>]
    let ``StateVector - isNormalized should verify normalization`` () =
        let normalizedState = QuantumState.StateVector (StateVector.init 2)
        Assert.True(QuantumState.isNormalized normalizedState)
        
        let bellState = createBellState ()
        Assert.True(QuantumState.isNormalized bellState)
    
    [<Fact>]
    let ``StateVector - toString should provide readable representation`` () =
        let state = QuantumState.StateVector (StateVector.init 2)
        let str = QuantumState.toString state
        
        Assert.Contains("StateVector", str)
        Assert.Contains("2 qubits", str)
        Assert.Contains("4 dimensions", str)
    
    // ============================================================================
    // ISINGSAMPLES TESTS
    // ============================================================================
    
    [<Fact>]
    let ``IsingSamples - numQubits should return variable count from problem`` () =
        let problem = createIsingProblem ()
        let solutions = createDWaveSolutions ()
        let state = QuantumState.IsingSamples (problem :> obj, solutions :> obj)
        
        // Problem has 3 variables (indices 0, 1, 2)
        Assert.Equal(3, QuantumState.numQubits state)
    
    [<Fact>]
    let ``IsingSamples - numQubits should handle empty problem`` () =
        let emptyProblem : IsingProblem = {
            LinearCoeffs = Map.empty
            QuadraticCoeffs = Map.empty
            Offset = 0.0
        }
        let state = QuantumState.IsingSamples (emptyProblem :> obj, [] :> obj)
        
        Assert.Equal(0, QuantumState.numQubits state)
    
    [<Fact>]
    let ``IsingSamples - stateType should return Annealing`` () =
        let problem = createIsingProblem ()
        let solutions = createDWaveSolutions ()
        let state = QuantumState.IsingSamples (problem :> obj, solutions :> obj)
        
        Assert.Equal(Annealing, QuantumState.stateType state)
    
    [<Fact>]
    let ``IsingSamples - isPure should return false`` () =
        let problem = createIsingProblem ()
        let solutions = createDWaveSolutions ()
        let state = QuantumState.IsingSamples (problem :> obj, solutions :> obj)
        
        // Annealing samples are classical (collapsed), not pure quantum states
        Assert.False(QuantumState.isPure state)
    
    [<Fact>]
    let ``IsingSamples - isNormalized should return true`` () =
        let problem = createIsingProblem ()
        let solutions = createDWaveSolutions ()
        let state = QuantumState.IsingSamples (problem :> obj, solutions :> obj)
        
        // Classical samples are always "normalized"
        Assert.True(QuantumState.isNormalized state)
    
    [<Fact>]
    let ``IsingSamples - measure should convert spins to bits correctly`` () =
        let problem = createIsingProblem ()
        let solutions = createDWaveSolutions ()
        let state = QuantumState.IsingSamples (problem :> obj, solutions :> obj)
        
        let measurements = QuantumState.measure state 100
        
        // All measurements should be length 3
        Assert.All(measurements, fun bits -> Assert.Equal(3, bits.Length))
        
        // All bits should be 0 or 1
        Assert.All(measurements, fun bits ->
            Assert.All(bits, fun bit -> Assert.True(bit = 0 || bit = 1)))
        
        // Should only see the three solutions from DWaveSolutions
        // Solution 1: spins [-1, -1, -1] → bits [0, 0, 0]
        // Solution 2: spins [+1, -1, -1] → bits [1, 0, 0]
        // Solution 3: spins [-1, +1, -1] → bits [0, 1, 0]
        let uniqueBitstrings =
            measurements
            |> Array.map (fun bits -> (bits.[0], bits.[1], bits.[2]))
            |> Array.distinct
        
        Assert.True(uniqueBitstrings.Length <= 3)
        Assert.All(uniqueBitstrings, fun (b0, b1, b2) ->
            Assert.True(
                (b0 = 0 && b1 = 0 && b2 = 0) ||  // Solution 1
                (b0 = 1 && b1 = 0 && b2 = 0) ||  // Solution 2
                (b0 = 0 && b1 = 1 && b2 = 0)     // Solution 3
            ))
    
    [<Fact>]
    let ``IsingSamples - measure should respect NumOccurrences weighting`` () =
        let problem = createIsingProblem ()
        let solutions = createDWaveSolutions ()
        let state = QuantumState.IsingSamples (problem :> obj, solutions :> obj)
        
        let measurements = QuantumState.measure state 1000
        
        // Count occurrences of each bitstring
        let counts =
            measurements
            |> Array.countBy id
            |> Map.ofArray
        
        // Best solution (70% of samples): [0, 0, 0]
        let count000 = Map.tryFind [|0; 0; 0|] counts |> Option.defaultValue 0
        
        // Second best (25% of samples): [1, 0, 0]
        let count100 = Map.tryFind [|1; 0; 0|] counts |> Option.defaultValue 0
        
        // Third (5% of samples): [0, 1, 0]
        let count010 = Map.tryFind [|0; 1; 0|] counts |> Option.defaultValue 0
        
        // Should see roughly 70%, 25%, 5% distribution (with statistical variance)
        Assert.True(count000 > count100, "Best solution should appear most frequently")
        Assert.True(count100 > count010, "Second best should appear more than third")
        
        // Rough bounds: 70% ± 10% = [600, 800], 25% ± 10% = [150, 350], 5% ± 5% = [0, 100]
        Assert.InRange(count000, 600, 800)
        Assert.InRange(count100, 150, 350)
        Assert.InRange(count010, 0, 100)
    
    [<Fact>]
    let ``IsingSamples - measure should handle empty solutions`` () =
        let problem = createIsingProblem ()
        let emptySolutions : DWaveSolution list = []
        let state = QuantumState.IsingSamples (problem :> obj, emptySolutions :> obj)
        
        let measurements = QuantumState.measure state 10
        
        // Should return all zeros when no solutions available
        Assert.All(measurements, fun bits ->
            Assert.All(bits, fun bit -> Assert.Equal(0, bit)))
    
    [<Fact>]
    let ``IsingSamples - probability should calculate empirical probability`` () =
        let problem = createIsingProblem ()
        let solutions = createDWaveSolutions ()
        let state = QuantumState.IsingSamples (problem :> obj, solutions :> obj)
        
        // Total occurrences: 70 + 25 + 5 = 100
        
        // P([0, 0, 0]) = 70 / 100 = 0.70
        let prob000 = QuantumState.probability [|0; 0; 0|] state
        Assert.Equal(0.70, prob000, 10)
        
        // P([1, 0, 0]) = 25 / 100 = 0.25
        let prob100 = QuantumState.probability [|1; 0; 0|] state
        Assert.Equal(0.25, prob100, 10)
        
        // P([0, 1, 0]) = 5 / 100 = 0.05
        let prob010 = QuantumState.probability [|0; 1; 0|] state
        Assert.Equal(0.05, prob010, 10)
        
        // P([1, 1, 1]) = 0 / 100 = 0.0 (not in solutions)
        let prob111 = QuantumState.probability [|1; 1; 1|] state
        Assert.Equal(0.0, prob111, 10)
    
    [<Fact>]
    let ``IsingSamples - probability should handle empty solutions`` () =
        let problem = createIsingProblem ()
        let emptySolutions : DWaveSolution list = []
        let state = QuantumState.IsingSamples (problem :> obj, emptySolutions :> obj)
        
        let prob = QuantumState.probability [|0; 0; 0|] state
        Assert.Equal(0.0, prob, 10)
    
    [<Fact>]
    let ``IsingSamples - toString should show solution summary`` () =
        let problem = createIsingProblem ()
        let solutions = createDWaveSolutions ()
        let state = QuantumState.IsingSamples (problem :> obj, solutions :> obj)
        
        let str = QuantumState.toString state
        
        Assert.Contains("IsingSamples", str)
        Assert.Contains("3 variables", str)
        Assert.Contains("3 unique solutions", str)
        Assert.Contains("100 samples", str)  // 70 + 25 + 5
        Assert.Contains("-1.8", str)  // Best energy
    
    [<Fact>]
    let ``IsingSamples - toString should handle empty solutions`` () =
        let problem = createIsingProblem ()
        let emptySolutions : DWaveSolution list = []
        let state = QuantumState.IsingSamples (problem :> obj, emptySolutions :> obj)
        
        let str = QuantumState.toString state
        
        Assert.Contains("IsingSamples", str)
        Assert.Contains("no solutions", str)
    
    // ============================================================================
    // EDGE CASES & INTEGRATION TESTS
    // ============================================================================
    
    [<Fact>]
    let ``IsingSamples - should handle missing spin indices gracefully`` () =
        let problem = createIsingProblem ()
        // Solution with only partial spin assignments
        let sparseSolutions = [
            {
                Spins = Map.ofList [(0, 1); (2, -1)]  // Missing index 1
                Energy = -0.5
                NumOccurrences = 10
                ChainBreakFraction = 0.0
            }
        ]
        let state = QuantumState.IsingSamples (problem :> obj, sparseSolutions :> obj)
        
        let measurements = QuantumState.measure state 20
        
        // Missing spins should default to 0 (spin = -1 → bit = 0)
        Assert.All(measurements, fun bits ->
            Assert.Equal(1, bits.[0])  // Index 0: spin = +1 → bit = 1
            Assert.Equal(0, bits.[1])  // Index 1: missing → default 0
            Assert.Equal(0, bits.[2])  // Index 2: spin = -1 → bit = 0
        )
    
    [<Fact>]
    let ``IsingSamples - probability validation should reject mismatched bitstring length`` () =
        let problem = createIsingProblem ()
        let solutions = createDWaveSolutions ()
        let state = QuantumState.IsingSamples (problem :> obj, solutions :> obj)
        
        // Should throw for wrong bitstring length
        Assert.Throws<ArgumentException>(fun () ->
            QuantumState.probability [|0; 0|] state |> ignore  // Too short
        ) |> ignore
        
        Assert.Throws<ArgumentException>(fun () ->
            QuantumState.probability [|0; 0; 0; 0|] state |> ignore  // Too long
        ) |> ignore
    
    [<Fact>]
    let ``All probabilities should sum to 1.0 for IsingSamples`` () =
        let problem = createIsingProblem ()
        let solutions = createDWaveSolutions ()
        let state = QuantumState.IsingSamples (problem :> obj, solutions :> obj)
        
        // Generate all possible 3-bit bitstrings: 2^3 = 8
        let allBitstrings = 
            [| for i in 0 .. 7 ->
                [| (i >>> 2) &&& 1; (i >>> 1) &&& 1; i &&& 1 |]
            |]
        
        let totalProb = 
            allBitstrings
            |> Array.sumBy (fun bits -> QuantumState.probability bits state)
        
        Assert.Equal(1.0, totalProb, 10)
