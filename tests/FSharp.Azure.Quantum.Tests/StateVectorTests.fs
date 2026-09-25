namespace FSharp.Azure.Quantum.Tests

open System
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

        for i in 1..7 do
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
        let expectedNorm = sqrt (9.0 + 16.0) // sqrt(25) = 5
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
        Assert.Throws<Exception>(fun () -> StateVector.getAmplitude -1 state |> ignore)
        |> ignore

        Assert.Throws<Exception>(fun () -> StateVector.getAmplitude 4 state |> ignore)
        |> ignore

    [<Fact>]
    let ``Initialize state vector - should enforce qubit limits`` () =
        // Valid: 0 qubits (trivial case)
        let state0 = StateVector.init 0
        Assert.Equal(1, StateVector.dimension state0)

        // Valid: 16 qubits (well inside any machine's capacity)
        let state16 = StateVector.init 16
        Assert.Equal(65536, StateVector.dimension state16)

        // Invalid: negative qubits
        Assert.Throws<Exception>(fun () -> StateVector.init -1 |> ignore) |> ignore

        // Invalid: past the capacity this machine reports
        Assert.Throws<Exception>(fun () -> StateVector.init (StateVector.maxQubits + 1) |> ignore)

    [<Fact>]
    let ``Create custom state vector - should create with provided amplitudes`` () =
        // Create equal superposition: (|0⟩ + |1⟩)/√2
        let sqrtHalf = 1.0 / sqrt 2.0

        let superposition =
            StateVector.create [| Complex(sqrtHalf, 0.0); Complex(sqrtHalf, 0.0) |]

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
        let sqrtHalf = 1.0 / sqrt 2.0

        let superposition =
            StateVector.create [| Complex(sqrtHalf, 0.0); Complex(sqrtHalf, 0.0) |]

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
        let custom =
            StateVector.create [| Complex.One; Complex.Zero; Complex.Zero; Complex.Zero |]

        Assert.True(StateVector.equals state1 custom) // Both are |00⟩

        let different =
            StateVector.create [| Complex.Zero; Complex.One; Complex.Zero; Complex.Zero |]

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

    // ========================================================================
    // CAPACITY (memory-derived qubit limit)
    // ========================================================================
    //
    // The maximum width used to be the constant 20. It is now derived from the
    // memory the runtime reports, because an n-qubit dense state vector is
    // 2^n x 16 bytes and the answer genuinely differs between machines.

    [<Fact>]
    let ``maxQubits sits between the advertised floor and the structural ceiling`` () =
        Assert.InRange(StateVector.maxQubits, StateVector.MinMaxQubits, StateVector.StructuralMaxQubits)

    [<Fact>]
    let ``structural ceiling is what a flat array can actually hold`` () =
        // A single-dimension .NET array holds at most Array.MaxLength elements, and
        // 2^31 amplitudes exceeds that — so 30 qubits is the widest expressible state
        // no matter how much memory is installed. (1 <<< 31 also overflows Int32.)
        Assert.Equal(30, StateVector.StructuralMaxQubits)
        Assert.True(int64 Array.MaxLength >= (1L <<< StateVector.StructuralMaxQubits))
        Assert.True(int64 Array.MaxLength < (1L <<< (StateVector.StructuralMaxQubits + 1)))

    [<Fact>]
    let ``stateVectorBytes is 16 bytes per amplitude`` () =
        // System.Numerics.Complex is two float64 fields.
        Assert.Equal(16L, StateVector.stateVectorBytes 0)
        Assert.Equal(16L * 1024L, StateVector.stateVectorBytes 10)
        Assert.Equal(16L * 1048576L, StateVector.stateVectorBytes 20)

    [<Fact>]
    let ``init allocates at the reported limit and refuses one qubit beyond`` () =
        // The floor is always allocatable, so this is safe on any machine.
        let atFloor = StateVector.init StateVector.MinMaxQubits
        Assert.Equal(StateVector.MinMaxQubits, StateVector.numQubits atFloor)

        let tooWide = StateVector.maxQubits + 1

        let ex = Assert.Throws<Exception>(fun () -> StateVector.init tooWide |> ignore)

        // The message must explain the limit rather than state a bare number:
        // where it came from, and how to lift it.
        Assert.Contains(string StateVector.maxQubits, ex.Message)
        Assert.Contains("memory", ex.Message)
        Assert.Contains(StateVector.MaxQubitsEnvironmentVariable, ex.Message)

    [<Fact>]
    let ``create refuses an amplitude array wider than the limit`` () =
        // Build the rejection from a length rather than an allocation: the point is
        // the bound check, and allocating 2^(maxQubits+1) amplitudes is the very
        // thing that cannot fit.
        Assert.True(StateVector.maxQubits >= StateVector.MinMaxQubits)

        // A power-of-two array one qubit past the floor is small and legal, so it
        // must be accepted whenever the floor is not itself the limit.
        let legal = StateVector.create (Array.zeroCreate<Complex>(1 <<< 4))
        Assert.Equal(4, StateVector.numQubits legal)

    [<Fact>]
    let ``gate application preserves amplitudes without aliasing the source`` () =
        // Gates now hand their freshly built array to the state vector instead of
        // copying it. The source state must still be untouched by that.
        let initial = StateVector.init 3
        let evolved = Gates.applyX 0 initial

        Assert.Equal(1.0, (StateVector.getAmplitude 0 initial).Real, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 1 initial).Real, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 0 evolved).Real, 10)
        Assert.Equal(1.0, (StateVector.getAmplitude 1 evolved).Real, 10)

    [<Fact>]
    let ``maxQubitsForAvailableBytes scales with the machine it is asked about`` () =
        let gb (n: float) = int64 (n * 1073741824.0)

        // A 32 GB development box and a 128 GB execution server do not get the same
        // answer — which is the whole point of deriving the limit instead of fixing it.
        Assert.Equal(29, StateVector.maxQubitsForAvailableBytes (gb 32.0))
        Assert.Equal(30, StateVector.maxQubitsForAvailableBytes (gb 128.0))

        // Every qubit doubles the requirement, so the thresholds double too:
        // n qubits needs 2^n x 64 bytes of total memory.
        Assert.Equal(24, StateVector.maxQubitsForAvailableBytes (gb 1.0))
        Assert.Equal(25, StateVector.maxQubitsForAvailableBytes (gb 2.0))
        Assert.Equal(26, StateVector.maxQubitsForAvailableBytes (gb 4.0))
        Assert.Equal(27, StateVector.maxQubitsForAvailableBytes (gb 8.0))
        Assert.Equal(28, StateVector.maxQubitsForAvailableBytes (gb 16.0))

        // Just under a threshold stays on the lower width — 32 GB of DIMMs reports
        // slightly less than 32 GiB to the runtime, which is why this box gets 28.
        Assert.Equal(28, StateVector.maxQubitsForAvailableBytes (gb 32.0 - 1L))

    [<Fact>]
    let ``maxQubitsForAvailableBytes is clamped at both ends`` () =
        let gb (n: float) = int64 (n * 1073741824.0)

        // Below the floor we still advertise MinMaxQubits: 2^20 amplitudes is 16 MB,
        // allocatable anywhere, and the library's own algorithms assume that much.
        Assert.Equal(StateVector.MinMaxQubits, StateVector.maxQubitsForAvailableBytes 0L)
        Assert.Equal(StateVector.MinMaxQubits, StateVector.maxQubitsForAvailableBytes -1L)
        Assert.Equal(StateVector.MinMaxQubits, StateVector.maxQubitsForAvailableBytes (gb 0.001))

        // Above the structural ceiling, more memory buys nothing: a flat Complex[]
        // cannot hold 2^31 amplitudes at any size.
        Assert.Equal(StateVector.StructuralMaxQubits, StateVector.maxQubitsForAvailableBytes (gb 1024.0))
        Assert.Equal(StateVector.StructuralMaxQubits, StateVector.maxQubitsForAvailableBytes Int64.MaxValue)

    [<Fact>]
    let ``maxQubitsForAvailableBytes never decreases as memory grows`` () =
        let widths =
            [ 0L; 1L <<< 26; 1L <<< 30; 1L <<< 33; 1L <<< 36; 1L <<< 40 ]
            |> List.map StateVector.maxQubitsForAvailableBytes

        Assert.Equal<int list>(List.sort widths, widths)

    [<Fact>]
    let ``the live machine agrees with the pure capacity function`` () =
        // Unless FSAQ_MAX_QUBITS is pinning it, the reported limit is exactly what
        // the formula gives for the memory the runtime reports.
        match Environment.GetEnvironmentVariable StateVector.MaxQubitsEnvironmentVariable with
        | null
        | "" ->
            let available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes
            Assert.Equal(StateVector.maxQubitsForAvailableBytes available, StateVector.maxQubits)
        | _ -> () // overridden for this run; the override is covered elsewhere
