namespace FSharp.Azure.Quantum.Tests

open System.Numerics
open Xunit
open FSharp.Azure.Quantum.LocalSimulator

module StateVectorTests =
    
    [<Fact>]
    let ``Initialize state vector - should create |0⟩^⊗n state correctly`` () =
        // Test multiple qubit counts (anti-gaming pattern: mixed cases)
        
        // 1 qubit: |0⟩ state
        let state1 = StateVector.init 1
        Assert.Equal(2, StateVector.dimension state1)
        Assert.Equal(1.0, (StateVector.getAmplitude 0 state1).Real, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 0 state1).Imaginary, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 1 state1).Real, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 1 state1).Imaginary, 10)
        
        // 2 qubits: |00⟩ state
        let state2 = StateVector.init 2
        Assert.Equal(4, StateVector.dimension state2)
        Assert.Equal(1.0, (StateVector.getAmplitude 0 state2).Real, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 1 state2).Real, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 2 state2).Real, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 3 state2).Real, 10)
        
        // 3 qubits: |000⟩ state (verify dimensions scale correctly)
        let state3 = StateVector.init 3
        Assert.Equal(8, StateVector.dimension state3)
        Assert.Equal(1.0, (StateVector.getAmplitude 0 state3).Real, 10)
        for i in 1 .. 7 do
            Assert.Equal(0.0, (StateVector.getAmplitude i state3).Real, 10)
    
    [<Fact>]
    let ``Normalize state vector - should properly normalize arbitrary states`` () =
        // Create unnormalized state and verify normalization
        let unnormalized = StateVector.create [| Complex(3.0, 0.0); Complex(4.0, 0.0) |]
        let normalized = StateVector.normalize unnormalized
        
        // After normalization: |ψ⟩ = 0.6|0⟩ + 0.8|1⟩
        Assert.Equal(0.6, (StateVector.getAmplitude 0 normalized).Real, 10)
        Assert.Equal(0.8, (StateVector.getAmplitude 1 normalized).Real, 10)
        
        // Verify norm = 1 (sum of squared magnitudes)
        Assert.Equal(1.0, StateVector.norm normalized, 10)
        
        // Test with complex amplitudes
        let complexState = StateVector.create [| Complex(1.0, 1.0); Complex(1.0, -1.0) |]
        let normalizedComplex = StateVector.normalize complexState
        let norm = StateVector.norm normalizedComplex
        Assert.Equal(1.0, norm, 10)
    
    [<Fact>]
    let ``State vector norm - should calculate correct norm`` () =
        // Test |0⟩ state (norm = 1)
        let state1 = StateVector.init 1
        Assert.Equal(1.0, StateVector.norm state1, 10)
        
        // Test unnormalized state
        let unnormalized = StateVector.create [| Complex(3.0, 0.0); Complex(4.0, 0.0) |]
        let expectedNorm = sqrt(9.0 + 16.0)  // sqrt(25) = 5
        Assert.Equal(expectedNorm, StateVector.norm unnormalized, 10)
        
        // Test complex state: (1+i)|0⟩ + (1-i)|1⟩
        // Norm = sqrt(|1+i|^2 + |1-i|^2) = sqrt(2 + 2) = 2
        let complexState = StateVector.create [| Complex(1.0, 1.0); Complex(1.0, -1.0) |]
        Assert.Equal(2.0, StateVector.norm complexState, 10)
    
    [<Fact>]
    let ``Inner product - should calculate correct inner product`` () =
        // Test <0|0> = 1
        let state0 = StateVector.init 1
        let innerProduct = StateVector.innerProduct state0 state0
        Assert.Equal(1.0, innerProduct.Real, 10)
        Assert.Equal(0.0, innerProduct.Imaginary, 10)
        
        // Test orthogonal states: <0|1> = 0
        let state1 = StateVector.create [| Complex.Zero; Complex.One |]
        let orthogonalProduct = StateVector.innerProduct state0 state1
        Assert.Equal(0.0, orthogonalProduct.Real, 10)
        Assert.Equal(0.0, orthogonalProduct.Imaginary, 10)
        
        // Test general case: <ψ|φ> where ψ = (1+i)|0⟩, φ = (1-i)|0⟩
        let psi = StateVector.create [| Complex(1.0, 1.0); Complex.Zero |]
        let phi = StateVector.create [| Complex(1.0, -1.0); Complex.Zero |]
        let product = StateVector.innerProduct psi phi
        // <ψ|φ> = (1-i)*(1-i) = 1 - 2i + i^2 = 1 - 2i - 1 = -2i
        Assert.Equal(0.0, product.Real, 10)
        Assert.Equal(-2.0, product.Imaginary, 10)
    
    [<Fact>]
    let ``Get amplitude - should validate index bounds`` () =
        let state = StateVector.init 2
        
        // Valid indices
        let amp0 = StateVector.getAmplitude 0 state
        let amp3 = StateVector.getAmplitude 3 state
        Assert.Equal(1.0, amp0.Real, 10)
        Assert.Equal(0.0, amp3.Real, 10)
        
        // Invalid indices should throw
        Assert.Throws<System.Exception>(fun () -> 
            StateVector.getAmplitude -1 state |> ignore
        ) |> ignore
        Assert.Throws<System.Exception>(fun () -> 
            StateVector.getAmplitude 4 state |> ignore
        ) |> ignore
    
    [<Fact>]
    let ``Initialize state vector - should enforce qubit limits`` () =
        // Valid: 0 qubits (trivial case)
        let state0 = StateVector.init 0
        Assert.Equal(1, StateVector.dimension state0)
        
        // Valid: 16 qubits (maximum)
        let state16 = StateVector.init 16
        Assert.Equal(65536, StateVector.dimension state16)
        
        // Invalid: negative qubits
        Assert.Throws<System.Exception>(fun () -> 
            StateVector.init -1 |> ignore
        ) |> ignore
        
        // Invalid: > 16 qubits
        Assert.Throws<System.Exception>(fun () -> 
            StateVector.init 17 |> ignore
        )
    
    [<Fact>]
    let ``Create custom state vector - should create with provided amplitudes`` () =
        // Create equal superposition: (|0⟩ + |1⟩)/√2
        let sqrtHalf = 1.0 / sqrt(2.0)
        let superposition = StateVector.create [| Complex(sqrtHalf, 0.0); Complex(sqrtHalf, 0.0) |]
        
        Assert.Equal(2, StateVector.dimension superposition)
        Assert.Equal(sqrtHalf, (StateVector.getAmplitude 0 superposition).Real, 10)
        Assert.Equal(sqrtHalf, (StateVector.getAmplitude 1 superposition).Real, 10)
        Assert.Equal(1.0, StateVector.norm superposition, 10)
    
    [<Fact>]
    let ``Probability of basis state - should calculate correct measurement probabilities`` () =
        // |0⟩ state: P(|0⟩) = 1, P(|1⟩) = 0
        let state0 = StateVector.init 1
        Assert.Equal(1.0, StateVector.probability 0 state0, 10)
        Assert.Equal(0.0, StateVector.probability 1 state0, 10)
        
        // Equal superposition: P(|0⟩) = P(|1⟩) = 0.5
        let sqrtHalf = 1.0 / sqrt(2.0)
        let superposition = StateVector.create [| Complex(sqrtHalf, 0.0); Complex(sqrtHalf, 0.0) |]
        Assert.Equal(0.5, StateVector.probability 0 superposition, 10)
        Assert.Equal(0.5, StateVector.probability 1 superposition, 10)
        
        // Complex superposition: (1+i)|0⟩ (unnormalized)
        let complexState = StateVector.create [| Complex(1.0, 1.0); Complex.Zero |]
        let prob = StateVector.probability 0 complexState
        // |1+i|² = (1² + 1²) = 2 (for unnormalized state)
        Assert.Equal(2.0, prob, 10)
    
    [<Fact>]
    let ``State vector equality - should compare states correctly`` () =
        let state1 = StateVector.init 2
        let state2 = StateVector.init 2
        let state3 = StateVector.init 3
        
        // Same states should be equal
        Assert.True(StateVector.equals state1 state2)
        
        // Different dimensions should not be equal
        Assert.False(StateVector.equals state1 state3)
        
        // Different amplitudes should not be equal
        let custom = StateVector.create [| Complex.One; Complex.Zero; Complex.Zero; Complex.Zero |]
        Assert.True(StateVector.equals state1 custom)  // Both are |00⟩
        
        let different = StateVector.create [| Complex.Zero; Complex.One; Complex.Zero; Complex.Zero |]
        Assert.False(StateVector.equals state1 different)
    
    [<Fact>]
    let ``Tensor product - should compute correct product state`` () =
        // |0⟩ ⊗ |0⟩ = |00⟩
        let state0 = StateVector.create [| Complex.One; Complex.Zero |]
        let product00 = StateVector.tensorProduct state0 state0
        Assert.Equal(4, StateVector.dimension product00)
        Assert.Equal(1.0, (StateVector.getAmplitude 0 product00).Real, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 1 product00).Real, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 2 product00).Real, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 3 product00).Real, 10)
        
        // |0⟩ ⊗ |1⟩ = |01⟩ (index 1 in 2-qubit basis)
        let state1 = StateVector.create [| Complex.Zero; Complex.One |]
        let product01 = StateVector.tensorProduct state0 state1
        Assert.Equal(0.0, (StateVector.getAmplitude 0 product01).Real, 10)
        Assert.Equal(1.0, (StateVector.getAmplitude 1 product01).Real, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 2 product01).Real, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 3 product01).Real, 10)
        
        // |1⟩ ⊗ |0⟩ = |10⟩ (index 2 in 2-qubit basis)
        let product10 = StateVector.tensorProduct state1 state0
        Assert.Equal(0.0, (StateVector.getAmplitude 0 product10).Real, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 1 product10).Real, 10)
        Assert.Equal(1.0, (StateVector.getAmplitude 2 product10).Real, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 3 product10).Real, 10)
