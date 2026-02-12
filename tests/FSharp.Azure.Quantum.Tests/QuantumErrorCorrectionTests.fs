module QuantumErrorCorrectionTests

open Xunit
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Backends.DWaveBackend

let private createLocalBackend () : IQuantumBackend =
    LocalBackend.LocalBackend() :> IQuantumBackend

// ========================================================================
// CODE PARAMETERS
// ========================================================================

[<Fact>]
let ``CodeParameters BitFlipCode3 correct values`` () =
    let p = QuantumErrorCorrection.codeParameters QuantumErrorCorrection.BitFlipCode3
    Assert.Equal(3, p.PhysicalQubits)
    Assert.Equal(1, p.LogicalQubits)
    Assert.Equal(1, p.Distance)
    Assert.Equal(1, p.CorrectableErrors)

[<Fact>]
let ``CodeParameters PhaseFlipCode3 correct values`` () =
    let p = QuantumErrorCorrection.codeParameters QuantumErrorCorrection.PhaseFlipCode3
    Assert.Equal(3, p.PhysicalQubits)
    Assert.Equal(1, p.LogicalQubits)
    Assert.Equal(1, p.Distance)
    Assert.Equal(1, p.CorrectableErrors)

[<Fact>]
let ``CodeParameters ShorCode9 correct values`` () =
    let p = QuantumErrorCorrection.codeParameters QuantumErrorCorrection.ShorCode9
    Assert.Equal(9, p.PhysicalQubits)
    Assert.Equal(1, p.LogicalQubits)
    Assert.Equal(3, p.Distance)
    Assert.Equal(1, p.CorrectableErrors)

[<Fact>]
let ``CodeParameters SteaneCode7 correct values`` () =
    let p = QuantumErrorCorrection.codeParameters QuantumErrorCorrection.SteaneCode7
    Assert.Equal(7, p.PhysicalQubits)
    Assert.Equal(1, p.LogicalQubits)
    Assert.Equal(3, p.Distance)
    Assert.Equal(1, p.CorrectableErrors)

// ========================================================================
// BIT-FLIP CODE TESTS
// ========================================================================

[<Fact>]
let ``BitFlip Encode0 produces 000`` () =
    let backend = createLocalBackend ()
    match QuantumErrorCorrection.BitFlip.encode backend 0 with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        Assert.Equal(QuantumErrorCorrection.BitFlipCode3, result.Code)
        Assert.Equal(3, result.PhysicalQubits)
        // Measure encoded state: should be |00000> (all zeros including ancilla)
        let measurements = QuantumState.measure result.EncodedState 100
        for shot in measurements do
            Assert.Equal(0, shot.[0])
            Assert.Equal(0, shot.[1])
            Assert.Equal(0, shot.[2])

[<Fact>]
let ``BitFlip Encode1 produces 111`` () =
    let backend = createLocalBackend ()
    match QuantumErrorCorrection.BitFlip.encode backend 1 with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        Assert.Equal(QuantumErrorCorrection.BitFlipCode3, result.Code)
        // Measure encoded state: data qubits should be |111>
        let measurements = QuantumState.measure result.EncodedState 100
        for shot in measurements do
            Assert.Equal(1, shot.[0])
            Assert.Equal(1, shot.[1])
            Assert.Equal(1, shot.[2])

[<Fact>]
let ``BitFlip DetectsFlipOnQubit0 syndrome 10`` () =
    let backend = createLocalBackend ()
    match QuantumErrorCorrection.BitFlip.encode backend 0 with
    | Error err -> Assert.Fail($"Expected Ok from encode, got Error: {err}")
    | Ok encoded ->
        // Inject bit-flip on qubit 0: |000> -> |100>
        match QuantumErrorCorrection.injectBitFlip backend 0 encoded.EncodedState with
        | Error err -> Assert.Fail($"Expected Ok from inject, got Error: {err}")
        | Ok afterError ->
            match QuantumErrorCorrection.BitFlip.measureSyndrome backend afterError with
            | Error err -> Assert.Fail($"Expected Ok from syndrome, got Error: {err}")
            | Ok (syndrome, _) ->
                Assert.Equal<int list>([ 1; 0 ], syndrome.SyndromeBits)
                Assert.Equal(Some QuantumErrorCorrection.BitFlipError, syndrome.DetectedError)
                Assert.Equal(Some 0, syndrome.ErrorQubit)

[<Fact>]
let ``BitFlip DetectsFlipOnQubit1 syndrome 11`` () =
    let backend = createLocalBackend ()
    match QuantumErrorCorrection.BitFlip.encode backend 0 with
    | Error err -> Assert.Fail($"Expected Ok from encode, got Error: {err}")
    | Ok encoded ->
        match QuantumErrorCorrection.injectBitFlip backend 1 encoded.EncodedState with
        | Error err -> Assert.Fail($"Expected Ok from inject, got Error: {err}")
        | Ok afterError ->
            match QuantumErrorCorrection.BitFlip.measureSyndrome backend afterError with
            | Error err -> Assert.Fail($"Expected Ok from syndrome, got Error: {err}")
            | Ok (syndrome, _) ->
                Assert.Equal<int list>([ 1; 1 ], syndrome.SyndromeBits)
                Assert.Equal(Some QuantumErrorCorrection.BitFlipError, syndrome.DetectedError)
                Assert.Equal(Some 1, syndrome.ErrorQubit)

[<Fact>]
let ``BitFlip DetectsFlipOnQubit2 syndrome 01`` () =
    let backend = createLocalBackend ()
    match QuantumErrorCorrection.BitFlip.encode backend 0 with
    | Error err -> Assert.Fail($"Expected Ok from encode, got Error: {err}")
    | Ok encoded ->
        match QuantumErrorCorrection.injectBitFlip backend 2 encoded.EncodedState with
        | Error err -> Assert.Fail($"Expected Ok from inject, got Error: {err}")
        | Ok afterError ->
            match QuantumErrorCorrection.BitFlip.measureSyndrome backend afterError with
            | Error err -> Assert.Fail($"Expected Ok from syndrome, got Error: {err}")
            | Ok (syndrome, _) ->
                Assert.Equal<int list>([ 0; 1 ], syndrome.SyndromeBits)
                Assert.Equal(Some QuantumErrorCorrection.BitFlipError, syndrome.DetectedError)
                Assert.Equal(Some 2, syndrome.ErrorQubit)

[<Fact>]
let ``BitFlip NoError syndrome 00`` () =
    let backend = createLocalBackend ()
    match QuantumErrorCorrection.BitFlip.encode backend 0 with
    | Error err -> Assert.Fail($"Expected Ok from encode, got Error: {err}")
    | Ok encoded ->
        match QuantumErrorCorrection.BitFlip.measureSyndrome backend encoded.EncodedState with
        | Error err -> Assert.Fail($"Expected Ok from syndrome, got Error: {err}")
        | Ok (syndrome, _) ->
            Assert.Equal<int list>([ 0; 0 ], syndrome.SyndromeBits)
            Assert.Equal(None, syndrome.DetectedError)
            Assert.Equal(None, syndrome.ErrorQubit)

[<Fact>]
let ``BitFlip RoundTrip corrects bit-flip on qubit 0`` () =
    let backend = createLocalBackend ()
    match QuantumErrorCorrection.BitFlip.roundTrip backend 0 (Some 0) with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        Assert.True(result.Success, $"Expected round-trip success, decoded={result.DecodedBit}")
        Assert.Equal(0, result.DecodedBit)
        Assert.True(result.CorrectionApplied, "Expected correction to be applied")

[<Fact>]
let ``BitFlip RoundTrip corrects bit-flip encoding 1`` () =
    let backend = createLocalBackend ()
    match QuantumErrorCorrection.BitFlip.roundTrip backend 1 (Some 1) with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        Assert.True(result.Success, $"Expected round-trip success, decoded={result.DecodedBit}")
        Assert.Equal(1, result.DecodedBit)
        Assert.True(result.CorrectionApplied, "Expected correction to be applied")

// ========================================================================
// PHASE-FLIP CODE TESTS
// ========================================================================

[<Fact>]
let ``PhaseFlip Encode0 produces plus-plus-plus`` () =
    let backend = createLocalBackend ()
    match QuantumErrorCorrection.PhaseFlip.encode backend 0 with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        Assert.Equal(QuantumErrorCorrection.PhaseFlipCode3, result.Code)
        // |+++> is a superposition - measure many times, should see mix of 0 and 1
        let measurements = QuantumState.measure result.EncodedState 200
        let onesQ0 = measurements |> Array.sumBy (fun shot -> shot.[0])
        // Each qubit should have roughly 50% chance of 0 or 1
        // Allow wide tolerance for statistical test
        Assert.True(onesQ0 > 20 && onesQ0 < 180,
            $"Expected roughly 50%% ones on qubit 0, got {onesQ0}/200")

[<Fact>]
let ``PhaseFlip DetectsPhaseFlip on qubit 0`` () =
    let backend = createLocalBackend ()
    match QuantumErrorCorrection.PhaseFlip.encode backend 0 with
    | Error err -> Assert.Fail($"Expected Ok from encode, got Error: {err}")
    | Ok encoded ->
        match QuantumErrorCorrection.injectPhaseFlip backend 0 encoded.EncodedState with
        | Error err -> Assert.Fail($"Expected Ok from inject, got Error: {err}")
        | Ok afterError ->
            match QuantumErrorCorrection.PhaseFlip.measureSyndrome backend afterError with
            | Error err -> Assert.Fail($"Expected Ok from syndrome, got Error: {err}")
            | Ok (syndrome, _) ->
                Assert.Equal(Some QuantumErrorCorrection.PhaseFlipError, syndrome.DetectedError)
                Assert.Equal(Some 0, syndrome.ErrorQubit)

[<Fact>]
let ``PhaseFlip RoundTrip corrects phase-flip`` () =
    let backend = createLocalBackend ()
    match QuantumErrorCorrection.PhaseFlip.roundTrip backend 0 (Some 0) with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        Assert.True(result.Success, $"Expected round-trip success, decoded={result.DecodedBit}")
        Assert.Equal(0, result.DecodedBit)
        Assert.True(result.CorrectionApplied, "Expected correction to be applied")

[<Fact>]
let ``PhaseFlip RoundTrip corrects phase-flip encoding 1`` () =
    let backend = createLocalBackend ()
    match QuantumErrorCorrection.PhaseFlip.roundTrip backend 1 (Some 2) with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        Assert.True(result.Success, $"Expected round-trip success, decoded={result.DecodedBit}")
        Assert.Equal(1, result.DecodedBit)
        Assert.True(result.CorrectionApplied, "Expected correction to be applied")

// ========================================================================
// SHOR CODE TESTS
// ========================================================================

[<Fact>]
let ``Shor RoundTrip corrects bit-flip`` () =
    let backend = createLocalBackend ()
    // Inject bit-flip on qubit 1 (within block 0)
    match QuantumErrorCorrection.Shor.roundTrip backend 0 QuantumErrorCorrection.BitFlipError 1 with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        Assert.True(result.Success, $"Expected round-trip success, decoded={result.DecodedBit}")
        Assert.Equal(0, result.DecodedBit)
        Assert.True(result.CorrectionApplied, "Expected correction to be applied")

[<Fact>]
let ``Shor RoundTrip corrects phase-flip`` () =
    let backend = createLocalBackend ()
    // Inject phase-flip on qubit 0 (block 0 leader)
    match QuantumErrorCorrection.Shor.roundTrip backend 0 QuantumErrorCorrection.PhaseFlipError 0 with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        Assert.True(result.Success, $"Expected round-trip success, decoded={result.DecodedBit}")
        Assert.Equal(0, result.DecodedBit)
        Assert.True(result.CorrectionApplied, "Expected correction to be applied")

[<Fact>]
let ``Shor RoundTrip corrects combined error`` () =
    let backend = createLocalBackend ()
    // Inject combined (Y) error on qubit 4 (block 1, middle)
    match QuantumErrorCorrection.Shor.roundTrip backend 1 QuantumErrorCorrection.CombinedError 4 with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        Assert.True(result.Success, $"Expected round-trip success, decoded={result.DecodedBit}")
        Assert.Equal(1, result.DecodedBit)
        Assert.True(result.CorrectionApplied, "Expected correction to be applied")

// ========================================================================
// STEANE CODE TESTS
// ========================================================================

[<Fact>]
let ``Steane RoundTrip corrects bit-flip`` () =
    let backend = createLocalBackend ()
    // Inject X error on qubit 3
    match QuantumErrorCorrection.Steane.roundTrip backend 0 QuantumErrorCorrection.BitFlipError 3 with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        Assert.True(result.Success, $"Expected round-trip success, decoded={result.DecodedBit}")
        Assert.Equal(0, result.DecodedBit)
        Assert.True(result.CorrectionApplied, "Expected correction to be applied")

[<Fact>]
let ``Steane RoundTrip corrects phase-flip`` () =
    let backend = createLocalBackend ()
    // Inject Z error on qubit 5
    match QuantumErrorCorrection.Steane.roundTrip backend 0 QuantumErrorCorrection.PhaseFlipError 5 with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        Assert.True(result.Success, $"Expected round-trip success, decoded={result.DecodedBit}")
        Assert.Equal(0, result.DecodedBit)
        Assert.True(result.CorrectionApplied, "Expected correction to be applied")

[<Fact>]
let ``Steane RoundTrip corrects combined error`` () =
    let backend = createLocalBackend ()
    // Inject Y error on qubit 2
    match QuantumErrorCorrection.Steane.roundTrip backend 1 QuantumErrorCorrection.CombinedError 2 with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        Assert.True(result.Success, $"Expected round-trip success, decoded={result.DecodedBit}")
        Assert.Equal(1, result.DecodedBit)
        Assert.True(result.CorrectionApplied, "Expected correction to be applied")

// ========================================================================
// ANNEALING BACKEND REJECTION
// ========================================================================

[<Fact>]
let ``AllCodes reject annealing backend`` () =
    // DWaveBackend is an annealing backend
    let annealingBackend =
        createDefaultMockBackend () :> IQuantumBackend

    // BitFlip.encode should fail
    match QuantumErrorCorrection.BitFlip.encode annealingBackend 0 with
    | Ok _ -> Assert.Fail("Expected Error for annealing backend, got Ok")
    | Error _ -> () // Expected

    // PhaseFlip.encode should fail
    match QuantumErrorCorrection.PhaseFlip.encode annealingBackend 0 with
    | Ok _ -> Assert.Fail("Expected Error for annealing backend, got Ok")
    | Error _ -> () // Expected

    // Shor.encode should fail
    match QuantumErrorCorrection.Shor.encode annealingBackend 0 with
    | Ok _ -> Assert.Fail("Expected Error for annealing backend, got Ok")
    | Error _ -> () // Expected

    // Steane.encode should fail
    match QuantumErrorCorrection.Steane.encode annealingBackend 0 with
    | Ok _ -> Assert.Fail("Expected Error for annealing backend, got Ok")
    | Error _ -> () // Expected

// ========================================================================
// FORMATTING TESTS
// ========================================================================

[<Fact>]
let ``formatCodeParameters produces correct output`` () =
    let output = QuantumErrorCorrection.formatCodeParameters QuantumErrorCorrection.ShorCode9
    Assert.Contains("Shor 9-Qubit Code", output)
    Assert.Contains("[[9,1,3]]", output)
    Assert.Contains("9 physical qubits", output)

[<Fact>]
let ``formatRoundTrip produces correct output`` () =
    let backend = createLocalBackend ()
    match QuantumErrorCorrection.BitFlip.roundTrip backend 0 (Some 0) with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        let output = QuantumErrorCorrection.formatRoundTrip result
        Assert.Contains("Bit-Flip [[3,1,1]]", output)
        Assert.Contains("Success: true", output)
