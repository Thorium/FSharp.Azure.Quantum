module FSharp.Azure.Quantum.Tests.QuantumKernelTests

open System.Threading
open System.Threading.Tasks
open Xunit
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.MachineLearning
open FSharp.Azure.Quantum.MachineLearning.QuantumKernels

// ============================================================================
// Test Setup
// ============================================================================

let private backend = LocalBackend.LocalBackend() :> IQuantumBackend

[<Literal>]
let private epsilon = 1e-6


// ============================================================================
// Kernel Computation Tests
// ============================================================================

[<Fact>]
let ``computeKernel - should return value between 0 and 1`` () : Task =
    task {
        let featureMap = AngleEncoding
        let x = [| 0.5; 0.3 |]
        let y = [| 0.7; 0.4 |]
        let shots = 1000

        match! computeKernelAsync backend featureMap x y shots CancellationToken.None with
        | Ok kernelValue ->
            Assert.True(
                kernelValue >= 0.0 && kernelValue <= 1.0,
                $"Kernel value should be in [0,1], got %f{kernelValue}"
            )
        | Error err -> Assert.True(false, $"Should not fail: %s{err.Message}")
    }

[<Fact>]
let ``computeKernel - identical vectors should give high kernel value`` () : Task =
    task {
        let featureMap = AngleEncoding
        let x = [| 0.5; 0.3 |]
        let shots = 1000

        // K(x, x) should be close to 1.0 (identical states)
        match! computeKernelAsync backend featureMap x x shots CancellationToken.None with
        | Ok kernelValue ->
            // Due to quantum noise, might not be exactly 1.0 but should be high
            Assert.True(kernelValue > 0.8, $"K(x,x) should be high (>0.8), got %f{kernelValue}")
        | Error err -> Assert.True(false, $"Should not fail: %s{err.Message}")
    }

[<Fact>]
let ``computeKernel - should reject empty feature vectors`` () : Task =
    task {
        let featureMap = AngleEncoding
        let x = [||]
        let y = [||]
        let shots = 1000

        let! result = computeKernelAsync backend featureMap x y shots CancellationToken.None

        result
        |> Result.map (fun _ -> Assert.True(false, "Should have rejected empty vectors"))
        |> Result.defaultWith (fun msg -> Assert.Contains("cannot be empty", msg.Message))
    }

[<Fact>]
let ``computeKernel - should reject mismatched vector lengths`` () : Task =
    task {
        let featureMap = AngleEncoding
        let x = [| 0.5; 0.3 |]
        let y = [| 0.7 |] // Different length
        let shots = 1000

        let! result = computeKernelAsync backend featureMap x y shots CancellationToken.None

        result
        |> Result.map (fun _ -> Assert.True(false, "Should have rejected mismatched lengths"))
        |> Result.defaultWith (fun msg -> Assert.Contains("same length", msg.Message))
    }

[<Fact>]
let ``computeKernel - should reject non-positive shots`` () : Task =
    task {
        let featureMap = AngleEncoding
        let x = [| 0.5; 0.3 |]
        let y = [| 0.7; 0.4 |]
        let shots = 0

        let! result = computeKernelAsync backend featureMap x y shots CancellationToken.None

        result
        |> Result.map (fun _ -> Assert.True(false, "Should have rejected zero shots"))
        |> Result.defaultWith (fun msg -> Assert.Contains("must be positive", msg.Message))
    }

[<Fact>]
let ``computeKernel - orthogonal states should give low kernel value`` () : Task =
    task {
        let featureMap = AngleEncoding
        // These should produce nearly orthogonal quantum states
        let x = [| 0.0; 0.0 |] // |00⟩
        let y = [| 1.0; 1.0 |] // After rotation, should be far from |00⟩
        let shots = 1000

        match! computeKernelAsync backend featureMap x y shots CancellationToken.None with
        | Ok kernelValue ->
            // Orthogonal states should have low overlap
            Assert.True(kernelValue < 0.8, $"K(x,y) for distant states should be lower, got %f{kernelValue}")
        | Error err -> Assert.True(false, $"Should not fail: %s{err.Message}")
    }

// ============================================================================
// Kernel Matrix Tests
// ============================================================================

[<Fact>]
let ``computeKernelMatrix - should be square and symmetric`` () : Task =
    task {
        let featureMap = AngleEncoding
        let data = [| [| 0.1; 0.2 |]; [| 0.3; 0.4 |]; [| 0.5; 0.6 |] |]
        let shots = 500

        let! result =
            computeKernelMatrixAsync backend featureMap data shots CancellationToken.None

        match result with
        | Ok matrix ->
            // Should be 3x3
            Assert.Equal(3, Array2D.length1 matrix)
            Assert.Equal(3, Array2D.length2 matrix)

            // Should be symmetric: K[i,j] ≈ K[j,i]
            for i in 0..2 do
                for j in i + 1 .. 2 do
                    Assert.True(
                        abs (matrix.[i, j] - matrix.[j, i]) < 0.1,
                        sprintf "Matrix should be symmetric at (%d,%d): %f vs %f" i j matrix.[i, j] matrix.[j, i]
                    )
        | Error err -> Assert.True(false, $"Should not fail: %s{err.Message}")
    }

[<Fact>]
let ``computeKernelMatrix - diagonal should be close to 1`` () : Task =
    task {
        let featureMap = AngleEncoding
        let data = [| [| 0.1; 0.2 |]; [| 0.3; 0.4 |] |]
        let shots = 1000

        let! result =
            computeKernelMatrixAsync backend featureMap data shots CancellationToken.None

        match result with
        | Ok matrix ->
            // Diagonal elements K(x,x) should be close to 1
            for i in 0..1 do
                Assert.True(matrix.[i, i] > 0.8, sprintf "K(%d,%d) should be high, got %f" i i matrix.[i, i])
        | Error err -> Assert.True(false, $"Should not fail: %s{err.Message}")
    }

[<Fact>]
let ``computeKernelMatrix - should reject empty dataset`` () : Task =
    task {
        let featureMap = AngleEncoding
        let data = [||]
        let shots = 1000

        let! result =
            computeKernelMatrixAsync backend featureMap data shots CancellationToken.None

        result
        |> Result.map (fun _ -> Assert.True(false, "Should have rejected empty dataset"))
        |> Result.defaultWith (fun msg -> Assert.Contains("cannot be empty", msg.Message))
    }

[<Fact>]
let ``computeKernelMatrix - all values should be in range 0 to 1`` () : Task =
    task {
        let featureMap = AngleEncoding
        let data = [| [| 0.1; 0.2 |]; [| 0.8; 0.9 |]; [| 0.4; 0.5 |] |]
        let shots = 500

        let! result =
            computeKernelMatrixAsync backend featureMap data shots CancellationToken.None

        match result with
        | Ok matrix ->
            for i in 0..2 do
                for j in 0..2 do
                    Assert.True(
                        matrix.[i, j] >= 0.0 && matrix.[i, j] <= 1.0,
                        sprintf "K[%d,%d]=%f should be in [0,1]" i j matrix.[i, j]
                    )
        | Error err -> Assert.True(false, $"Should not fail: %s{err.Message}")
    }

// ============================================================================
// Train/Test Kernel Matrix Tests
// ============================================================================

[<Fact>]
let ``computeKernelMatrixTrainTest - should have correct dimensions`` () : Task =
    task {
        let featureMap = AngleEncoding
        let trainData = [| [| 0.1; 0.2 |]; [| 0.3; 0.4 |]; [| 0.5; 0.6 |] |]
        let testData = [| [| 0.7; 0.8 |]; [| 0.9; 1.0 |] |]
        let shots = 500

        let! result =
            computeKernelMatrixTrainTestAsync backend featureMap trainData testData shots CancellationToken.None

        match result with
        | Ok matrix ->
            // Should be 2 (test) × 3 (train)
            Assert.Equal(2, Array2D.length1 matrix)
            Assert.Equal(3, Array2D.length2 matrix)
        | Error err -> Assert.True(false, $"Should not fail: %s{err.Message}")
    }

[<Fact>]
let ``computeKernelMatrixTrainTest - all values should be in range`` () : Task =
    task {
        let featureMap = AngleEncoding
        let trainData = [| [| 0.1; 0.2 |]; [| 0.3; 0.4 |] |]
        let testData = [| [| 0.5; 0.6 |] |]
        let shots = 500

        let! result =
            computeKernelMatrixTrainTestAsync backend featureMap trainData testData shots CancellationToken.None

        match result with
        | Ok matrix ->
            for i in 0..0 do
                for j in 0..1 do
                    Assert.True(
                        matrix.[i, j] >= 0.0 && matrix.[i, j] <= 1.0,
                        sprintf "K[%d,%d]=%f should be in [0,1]" i j matrix.[i, j]
                    )
        | Error err -> Assert.True(false, $"Should not fail: %s{err.Message}")
    }

[<Fact>]
let ``computeKernelMatrixTrainTest - should reject empty train data`` () : Task =
    task {
        let featureMap = AngleEncoding
        let trainData = [||]
        let testData = [| [| 0.5; 0.6 |] |]
        let shots = 500

        let! result =
            computeKernelMatrixTrainTestAsync backend featureMap trainData testData shots CancellationToken.None

        result
        |> Result.map (fun _ -> Assert.True(false, "Should have rejected empty train data"))
        |> Result.defaultWith (fun msg -> Assert.Contains("Training dataset cannot be empty", msg.Message))
    }

[<Fact>]
let ``computeKernelMatrixTrainTest - should reject empty test data`` () : Task =
    task {
        let featureMap = AngleEncoding
        let trainData = [| [| 0.1; 0.2 |] |]
        let testData = [||]
        let shots = 500

        let! result =
            computeKernelMatrixTrainTestAsync backend featureMap trainData testData shots CancellationToken.None

        result
        |> Result.map (fun _ -> Assert.True(false, "Should have rejected empty test data"))
        |> Result.defaultWith (fun msg -> Assert.Contains("Test dataset cannot be empty", msg.Message))
    }

// ============================================================================
// Kernel Properties Tests
// ============================================================================

[<Fact>]
let ``isSymmetric - should detect symmetric matrix`` () =
    let matrix = array2D [ [ 1.0; 0.5 ]; [ 0.5; 1.0 ] ]
    let tolerance = 1e-6

    let result = isSymmetric matrix tolerance

    Assert.True(result, "Matrix should be detected as symmetric")

[<Fact>]
let ``isSymmetric - should detect non-symmetric matrix`` () =
    let matrix = array2D [ [ 1.0; 0.5 ]; [ 0.3; 1.0 ] ] // Not symmetric
    let tolerance = 1e-6

    let result = isSymmetric matrix tolerance

    Assert.False(result, "Matrix should be detected as non-symmetric")

[<Fact>]
let ``isSymmetric - should reject non-square matrix`` () =
    let matrix = array2D [ [ 1.0; 0.5; 0.3 ] ] // 1x3 matrix
    let tolerance = 1e-6

    let result = isSymmetric matrix tolerance

    Assert.False(result, "Non-square matrix cannot be symmetric")

[<Fact>]
let ``isPositiveSemiDefinite - should accept matrix with non-negative diagonal`` () =
    let matrix = array2D [ [ 1.0; 0.5 ]; [ 0.5; 0.8 ] ]

    let result = isPositiveSemiDefinite matrix

    Assert.True(result, "Matrix with positive diagonal should pass")

[<Fact>]
let ``isPositiveSemiDefinite - should reject matrix with negative diagonal`` () =
    let matrix = array2D [ [ 1.0; 0.5 ]; [ 0.5; -0.1 ] ] // Negative on diagonal

    let result = isPositiveSemiDefinite matrix

    Assert.False(result, "Matrix with negative diagonal should fail")

// ============================================================================
// Normalization Tests
// ============================================================================

[<Fact>]
let ``normalizeKernelMatrix - should normalize diagonal to 1`` () =
    let matrix = array2D [ [ 2.0; 1.0 ]; [ 1.0; 3.0 ] ]

    let result = normalizeKernelMatrix matrix

    match result with
    | Ok normalized ->
        // Diagonal should be 1.0
        Assert.True(
            abs (normalized.[0, 0] - 1.0) < epsilon,
            sprintf "K_norm[0,0] should be 1.0, got %f" normalized.[0, 0]
        )

        Assert.True(
            abs (normalized.[1, 1] - 1.0) < epsilon,
            sprintf "K_norm[1,1] should be 1.0, got %f" normalized.[1, 1]
        )

        // Off-diagonal: K_norm[0,1] = 1.0 / sqrt(2.0 * 3.0) ≈ 0.408
        let expected = 1.0 / sqrt (2.0 * 3.0)

        Assert.True(
            abs (normalized.[0, 1] - expected) < 0.01,
            sprintf "K_norm[0,1] should be %f, got %f" expected normalized.[0, 1]
        )
    | Error err -> Assert.True(false, $"Should not fail: %s{err.Message}")

[<Fact>]
let ``normalizeKernelMatrix - should reject non-square matrix`` () =
    let matrix = array2D [ [ 1.0; 0.5; 0.3 ] ] // 1x3

    let result = normalizeKernelMatrix matrix

    result
    |> Result.map (fun _ -> Assert.True(false, "Should have rejected non-square matrix"))
    |> Result.defaultWith (fun msg -> Assert.Contains("must be square", msg.Message))

[<Fact>]
let ``normalizeKernelMatrix - should reject matrix with zero diagonal`` () =
    let matrix = array2D [ [ 0.0; 0.5 ]; [ 0.5; 1.0 ] ] // Zero on diagonal

    let result = normalizeKernelMatrix matrix

    result
    |> Result.map (fun _ -> Assert.True(false, "Should have rejected zero diagonal"))
    |> Result.defaultWith (fun msg -> Assert.Contains("must be positive", msg.Message))

// ============================================================================
// Helper Functions Tests
// ============================================================================

[<Fact>]
let ``getDiagonal - should extract diagonal elements`` () =
    let matrix = array2D [ [ 1.0; 2.0 ]; [ 3.0; 4.0 ] ]

    let diagonal = getDiagonal matrix

    Assert.Equal(2, diagonal.Length)
    Assert.Equal(1.0, diagonal.[0])
    Assert.Equal(4.0, diagonal.[1])

[<Fact>]
let ``getDiagonal - should work with non-square matrix`` () =
    let matrix = array2D [ [ 1.0; 2.0; 3.0 ]; [ 4.0; 5.0; 6.0 ] ] // 2x3

    let diagonal = getDiagonal matrix

    // Should get min(2,3) = 2 diagonal elements
    Assert.Equal(2, diagonal.Length)
    Assert.Equal(1.0, diagonal.[0])
    Assert.Equal(5.0, diagonal.[1])

[<Fact>]
let ``computeStats - should compute correct statistics`` () =
    let matrix = array2D [ [ 1.0; 0.5 ]; [ 0.5; 1.0 ] ]

    let stats = computeStats matrix

    // Mean = (1.0 + 0.5 + 0.5 + 1.0) / 4 = 0.75
    Assert.True(abs (stats.Mean - 0.75) < epsilon, $"Mean should be 0.75, got %f{stats.Mean}")

    // Min = 0.5, Max = 1.0
    Assert.Equal(0.5, stats.Min)
    Assert.Equal(1.0, stats.Max)

    // Diagonal mean = (1.0 + 1.0) / 2 = 1.0
    Assert.Equal(1.0, stats.DiagonalMean)

    // Should detect symmetry
    Assert.True(stats.IsSymmetric, "Should detect symmetric matrix")

    // Should detect positive semi-definite
    Assert.True(stats.IsPositiveSemiDefinite, "Should detect PSD matrix")

// ============================================================================
// Integration Tests with Different Feature Maps
// ============================================================================

[<Fact>]
let ``computeKernel - should work with ZZFeatureMap`` () : Task =
    task {
        let featureMap = ZZFeatureMap 1
        let x = [| 0.5; 0.3 |]
        let y = [| 0.7; 0.4 |]
        let shots = 1000

        match! computeKernelAsync backend featureMap x y shots CancellationToken.None with
        | Ok kernelValue ->
            Assert.True(
                kernelValue >= 0.0 && kernelValue <= 1.0,
                $"Kernel value should be in [0,1], got %f{kernelValue}"
            )
        | Error err -> Assert.True(false, $"Should not fail: %s{err.Message}")
    }

[<Fact>]
let ``computeKernelMatrix - should work with ZZFeatureMap`` () : Task =
    task {
        let featureMap = ZZFeatureMap 1
        let data = [| [| 0.1; 0.2 |]; [| 0.3; 0.4 |] |]
        let shots = 500

        let! result =
            computeKernelMatrixAsync backend featureMap data shots CancellationToken.None

        match result with
        | Ok matrix ->
            Assert.Equal(2, Array2D.length1 matrix)
            Assert.Equal(2, Array2D.length2 matrix)

            // Diagonal should be high
            Assert.True(matrix.[0, 0] > 0.7, sprintf "K[0,0] should be high, got %f" matrix.[0, 0])
            Assert.True(matrix.[1, 1] > 0.7, sprintf "K[1,1] should be high, got %f" matrix.[1, 1])
        | Error err -> Assert.True(false, $"Should not fail: %s{err.Message}")
    }

[<Fact>]
let ``computeKernelMatrix - properties should hold for real quantum kernel`` () : Task =
    task {
        let featureMap = AngleEncoding
        let data = [| [| 0.1; 0.2 |]; [| 0.5; 0.6 |]; [| 0.9; 1.0 |] |]
        let shots = 1000

        let! result =
            computeKernelMatrixAsync backend featureMap data shots CancellationToken.None

        match result with
        | Ok matrix ->
            let stats = computeStats matrix

            // All values in valid range
            Assert.True(
                stats.Min >= 0.0 && stats.Max <= 1.0,
                $"All kernel values should be in [0,1]: min=%f{stats.Min}, max=%f{stats.Max}"
            )

            // Should be symmetric (within quantum noise)
            Assert.True(isSymmetric matrix 0.2, "Kernel matrix should be approximately symmetric")

            // Diagonal mean should be high (self-similarity)
            Assert.True(stats.DiagonalMean > 0.8, $"Diagonal mean should be high, got %f{stats.DiagonalMean}")
        | Error err -> Assert.True(false, $"Should not fail: %s{err.Message}")
    }

// ============================================================================
// Async Kernel Computation Tests
// ============================================================================

[<Fact>]
let ``computeKernelAsync - should return value between 0 and 1`` () : Task =
    task {
        let featureMap = AngleEncoding
        let x = [| 0.5; 0.3 |]
        let y = [| 0.7; 0.4 |]
        let shots = 1000


        match! computeKernelAsync backend featureMap x y shots CancellationToken.None with
        | Ok kernelValue ->
            Assert.True(
                kernelValue >= 0.0 && kernelValue <= 1.0,
                $"Kernel value should be in [0,1], got %f{kernelValue}"
            )
        | Error err -> Assert.True(false, $"Should not fail: %s{err.Message}")
    }

[<Fact>]
let ``computeKernelAsync - identical vectors should give high kernel value`` () : Task =
    task {
        let featureMap = AngleEncoding
        let x = [| 0.5; 0.3 |]
        let shots = 1000


        match! computeKernelAsync backend featureMap x x shots CancellationToken.None with
        | Ok kernelValue -> Assert.True(kernelValue > 0.8, $"K(x,x) should be high (>0.8), got %f{kernelValue}")
        | Error err -> Assert.True(false, $"Should not fail: %s{err.Message}")
    }

// ============================================================================
// Async Kernel Matrix Tests
// ============================================================================

[<Fact>]
let ``computeKernelMatrixAsync - should be square with correct dimensions`` () : Task =
    task {
        let featureMap = AngleEncoding
        let data = [| [| 0.1; 0.2 |]; [| 0.3; 0.4 |]; [| 0.5; 0.6 |] |]
        let shots = 500


        match! computeKernelMatrixAsync backend featureMap data shots CancellationToken.None with
        | Ok matrix ->
            Assert.Equal(3, Array2D.length1 matrix)
            Assert.Equal(3, Array2D.length2 matrix)

            // Should be approximately symmetric
            for i in 0..2 do
                for j in i + 1 .. 2 do
                    Assert.True(
                        abs (matrix.[i, j] - matrix.[j, i]) < 0.1,
                        sprintf "Matrix should be symmetric at (%d,%d): %f vs %f" i j matrix.[i, j] matrix.[j, i]
                    )
        | Error err -> Assert.True(false, $"Should not fail: %s{err.Message}")
    }

[<Fact>]
let ``computeKernelMatrixAsync - diagonal should be close to 1`` () : Task =
    task {
        let featureMap = AngleEncoding
        let data = [| [| 0.1; 0.2 |]; [| 0.3; 0.4 |] |]
        let shots = 1000


        match! computeKernelMatrixAsync backend featureMap data shots CancellationToken.None with
        | Ok matrix ->
            for i in 0..1 do
                Assert.True(matrix.[i, i] > 0.8, sprintf "K(%d,%d) should be high, got %f" i i matrix.[i, i])
        | Error err -> Assert.True(false, $"Should not fail: %s{err.Message}")
    }

[<Fact>]
let ``computeKernelMatrixAsync - all values should be in range 0 to 1`` () : Task =
    task {
        let featureMap = AngleEncoding
        let data = [| [| 0.1; 0.2 |]; [| 0.8; 0.9 |]; [| 0.4; 0.5 |] |]
        let shots = 500


        match! computeKernelMatrixAsync backend featureMap data shots CancellationToken.None with
        | Ok matrix ->
            for i in 0..2 do
                for j in 0..2 do
                    Assert.True(
                        matrix.[i, j] >= 0.0 && matrix.[i, j] <= 1.0,
                        sprintf "K[%d,%d]=%f should be in [0,1]" i j matrix.[i, j]
                    )
        | Error err -> Assert.True(false, $"Should not fail: %s{err.Message}")
    }

// ============================================================================
// Async Train/Test Kernel Matrix Tests
// ============================================================================

[<Fact>]
let ``computeKernelMatrixTrainTestAsync - should have correct dimensions`` () : Task =
    task {
        let featureMap = AngleEncoding
        let trainData = [| [| 0.1; 0.2 |]; [| 0.3; 0.4 |]; [| 0.5; 0.6 |] |]
        let testData = [| [| 0.7; 0.8 |]; [| 0.9; 1.0 |] |]
        let shots = 500


        match! computeKernelMatrixTrainTestAsync backend featureMap trainData testData shots CancellationToken.None with
        | Ok matrix ->
            // Should be 2 (test) x 3 (train)
            Assert.Equal(2, Array2D.length1 matrix)
            Assert.Equal(3, Array2D.length2 matrix)
        | Error err -> Assert.True(false, $"Should not fail: %s{err.Message}")
    }

[<Fact>]
let ``computeKernelMatrixTrainTestAsync - all values should be in range`` () : Task =
    task {
        let featureMap = AngleEncoding
        let trainData = [| [| 0.1; 0.2 |]; [| 0.3; 0.4 |] |]
        let testData = [| [| 0.5; 0.6 |] |]
        let shots = 500


        match! computeKernelMatrixTrainTestAsync backend featureMap trainData testData shots CancellationToken.None with
        | Ok matrix ->
            for i in 0..0 do
                for j in 0..1 do
                    Assert.True(
                        matrix.[i, j] >= 0.0 && matrix.[i, j] <= 1.0,
                        sprintf "K[%d,%d]=%f should be in [0,1]" i j matrix.[i, j]
                    )
        | Error err -> Assert.True(false, $"Should not fail: %s{err.Message}")
    }

// ============================================================================
// Async Cancellation Tests
// ============================================================================

[<Fact>]
let ``computeKernelAsync - accepts cancellation token`` () : Task =
    task {
        let featureMap = AngleEncoding
        let x = [| 0.5; 0.3 |]
        let y = [| 0.7; 0.4 |]
        let shots = 100

        // The token is never cancelled: passing it must not change the outcome.
        use cts = new CancellationTokenSource()

        match! computeKernelAsync backend featureMap x y shots cts.Token with
        | Ok kernelValue -> Assert.InRange(kernelValue, 0.0, 1.0)
        | Error e -> Assert.Fail($"an uncancelled token must not fail the kernel: {e.Message}")
    }

// ============================================================================
// Job fan-out: a sampling (cloud) backend gets a bounded number of circuits at once
// ============================================================================

/// Local simulator whose ExecuteToStateAsync takes 20 ms and records the most calls in flight.
type private InFlightProbe() =
    let inner = LocalBackend.LocalBackend() :> IQuantumBackend
    let mutable inFlight = 0
    let mutable maxInFlight = 0

    member _.MaxInFlight = maxInFlight

    interface IQuantumBackend with
        member _.ExecuteToState circuit = inner.ExecuteToState circuit

        member _.ExecuteToStateAsync circuit _ =
            task {
                let now = Interlocked.Increment(&inFlight)

                lock inner (fun () -> maxInFlight <- max maxInFlight now)

                try
                    do! Task.Delay 20
                    return inner.ExecuteToState circuit
                finally
                    Interlocked.Decrement(&inFlight) |> ignore
            }

        member _.NativeStateType = inner.NativeStateType
        member _.ApplyOperation op state = inner.ApplyOperation op state
        member _.ApplyOperationAsync op state ct = inner.ApplyOperationAsync op state ct
        member _.SupportsOperation op = inner.SupportsOperation op
        member _.Name = "in-flight probe"
        member _.InitializeState n = inner.InitializeState n

/// The same probe reporting shots, as cloud backends do.
type private SamplingInFlightProbe() =
    inherit InFlightProbe()

    interface IShotSamplingBackend with
        member _.Shots = 1000

let private fanOutData =
    Array.init 7 (fun i -> [| 0.1 * float i; 0.3 - 0.05 * float i |])

[<Fact>]
let ``computeKernelMatrixAsync keeps a sampling backend to MaxConcurrentSampledJobs circuits at once`` () =
    task {
        let probe = SamplingInFlightProbe()

        let! result =
            computeKernelMatrixAsync probe AngleEncoding fanOutData 1000 CancellationToken.None

        match result with
        | Ok matrix ->
            // 28 upper-triangle circuits, never more than the limit in flight.
            Assert.InRange(probe.MaxInFlight, 2, MaxConcurrentSampledJobs)
            Assert.Equal(matrix.[1, 3], matrix.[3, 1])
        | Error e -> Assert.Fail e.Message
    }

[<Fact>]
let ``computeKernelMatrixAsync leaves a simulator unthrottled`` () =
    task {
        let probe = InFlightProbe()

        let! result =
            computeKernelMatrixAsync probe AngleEncoding fanOutData 1000 CancellationToken.None

        match result with
        | Ok _ -> Assert.True(probe.MaxInFlight > MaxConcurrentSampledJobs, $"max in flight {probe.MaxInFlight}")
        | Error e -> Assert.Fail e.Message
    }

[<Fact>]
let ``computeKernelMatrixTrainTestAsync keeps a sampling backend to MaxConcurrentSampledJobs circuits at once`` () =
    task {
        let probe = SamplingInFlightProbe()

        let! result =
            computeKernelMatrixTrainTestAsync
                probe
                AngleEncoding
                fanOutData
                fanOutData.[0..3]
                1000
                CancellationToken.None

        match result with
        | Ok matrix ->
            Assert.InRange(probe.MaxInFlight, 2, MaxConcurrentSampledJobs)
            Assert.Equal(4, Array2D.length1 matrix)
        | Error e -> Assert.Fail e.Message
    }

// ============================================================================
// Shots on shot-sampling (cloud) backends
// ============================================================================

[<Fact>]
let ``computeKernelAsync refuses a shot count other than the sampling backend's, naming both`` () =
    task {
        let cloud = CloudStyleBackends.ShotSamplingCloud(1000, 3)

        let! result =
            computeKernelAsync cloud AngleEncoding [| 0.5; 0.3 |] [| 0.2; 0.9 |] 250 CancellationToken.None

        match result with
        | Error(FSharp.Azure.Quantum.Core.QuantumError.ValidationError("shots", message)) ->
            Assert.Contains("1000", message)
            Assert.Contains("250", message)
            Assert.Equal(0, cloud.Jobs)
        | other -> Assert.Fail $"expected a shots ValidationError, got {other}"
    }

[<Fact>]
let ``computeKernelAsync on a sampling backend reads its own shots when they are requested`` () =
    task {
        let cloud = CloudStyleBackends.ShotSamplingCloud(1000, 3)

        let! result =
            computeKernelAsync cloud AngleEncoding [| 0.5; 0.3 |] [| 0.5; 0.3 |] 1000 CancellationToken.None

        match result with
        | Ok k ->
            // K(x, x) = 1: every one of the 1,000 measured shots is |00⟩.
            Assert.Equal(1.0, k, 9)
            Assert.Equal(1, cloud.Jobs)
        | Error e -> Assert.Fail e.Message
    }

[<Fact>]
let ``kernel matrices refuse a mismatched shot count before submitting any job`` () =
    task {
        let cloud = CloudStyleBackends.ShotSamplingCloud(1000, 3)
        let data = [| [| 0.1; 0.2 |]; [| 0.3; 0.4 |]; [| 0.5; 0.6 |] |]

        let! square =
            computeKernelMatrixAsync cloud AngleEncoding data 2000 CancellationToken.None

        let! trainTest =
            computeKernelMatrixTrainTestAsync cloud AngleEncoding data data.[0..1] 2000 CancellationToken.None

        for result in [ square; trainTest ] do
            match result with
            | Error(FSharp.Azure.Quantum.Core.QuantumError.ValidationError("shots", message)) ->
                Assert.Contains("2000", message)
            | other -> Assert.Fail $"expected a shots ValidationError, got {other}"

        Assert.Equal(0, cloud.Jobs)
    }

[<Fact>]
let ``computeKernelAsync on an exact simulator samples the requested shots`` () =
    task {
        // Orthogonal-ish pair: with 7 shots every estimate is a multiple of 1/7.
        let! result =
            computeKernelAsync backend AngleEncoding [| 0.5; 0.3 |] [| 2.0; 1.4 |] 7 CancellationToken.None

        match result with
        | Ok k -> Assert.Equal(0.0, (k * 7.0) - round (k * 7.0), 9)
        | Error e -> Assert.Fail e.Message
    }
