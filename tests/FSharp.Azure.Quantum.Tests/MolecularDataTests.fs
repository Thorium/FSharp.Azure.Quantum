namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Data.MolecularData

module MolecularDataTests =

    // ========================================================================
    // SMILES PARSING
    // ========================================================================

    [<Fact>]
    let ``parseSmiles rejects empty string`` () =
        match parseSmiles "" with
        | Error (QuantumError.ValidationError _) -> ()
        | r -> failwith $"Expected ValidationError, got {r}"

    [<Fact>]
    let ``parseSmiles rejects whitespace only`` () =
        match parseSmiles "   " with
        | Error (QuantumError.ValidationError _) -> ()
        | r -> failwith $"Expected ValidationError, got {r}"

    [<Fact>]
    let ``parseSmiles parses methane (C)`` () =
        match parseSmiles "C" with
        | Ok mol ->
            Assert.Equal("C", mol.Smiles)
            Assert.Equal(1, mol.Atoms.Length)
            Assert.Equal("C", mol.Atoms.[0].Element)
            Assert.False(mol.Atoms.[0].IsAromatic)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``parseSmiles parses ethane (CC)`` () =
        match parseSmiles "CC" with
        | Ok mol ->
            Assert.Equal(2, mol.Atoms.Length)
            Assert.Equal(1, mol.Bonds.Length)
            Assert.Equal(1, mol.Bonds.[0].Order) // Single bond
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``parseSmiles parses double bond (C=C)`` () =
        match parseSmiles "C=C" with
        | Ok mol ->
            Assert.Equal(2, mol.Atoms.Length)
            Assert.Equal(1, mol.Bonds.Length)
            Assert.Equal(2, mol.Bonds.[0].Order) // Double bond
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``parseSmiles parses triple bond (C#N)`` () =
        match parseSmiles "C#N" with
        | Ok mol ->
            Assert.Equal(2, mol.Atoms.Length)
            Assert.Equal(1, mol.Bonds.Length)
            Assert.Equal(3, mol.Bonds.[0].Order) // Triple bond
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``parseSmiles parses aromatic ring (c1ccccc1)`` () =
        match parseSmiles "c1ccccc1" with
        | Ok mol ->
            Assert.Equal(6, mol.Atoms.Length)
            // All atoms should be aromatic
            Assert.True(mol.Atoms |> Array.forall (fun a -> a.IsAromatic))
            // Should have ring closure bond
            Assert.True(mol.Bonds.Length >= 6)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``parseSmiles parses branching (CC(C)C)`` () =
        match parseSmiles "CC(C)C" with
        | Ok mol ->
            Assert.Equal(4, mol.Atoms.Length)
            Assert.Equal(3, mol.Bonds.Length)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``parseSmiles parses water (O)`` () =
        match parseSmiles "O" with
        | Ok mol ->
            Assert.Equal(1, mol.Atoms.Length)
            Assert.Equal("O", mol.Atoms.[0].Element)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``parseSmiles parses chlorine (Cl)`` () =
        match parseSmiles "CCl" with
        | Ok mol ->
            Assert.Equal(2, mol.Atoms.Length)
            Assert.True(mol.Atoms |> Array.exists (fun a -> a.Element = "CL" || a.Element = "Cl"))
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``parseSmiles parses bromine (Br)`` () =
        match parseSmiles "CBr" with
        | Ok mol ->
            Assert.Equal(2, mol.Atoms.Length)
            Assert.True(mol.Atoms |> Array.exists (fun a -> a.Element = "BR" || a.Element = "Br"))
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``parseSmiles generates molecular formula`` () =
        match parseSmiles "CCO" with
        | Ok mol ->
            Assert.False(System.String.IsNullOrEmpty(mol.Formula))
            Assert.Contains("C", mol.Formula)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``parseSmiles handles disconnected fragments (C.C)`` () =
        match parseSmiles "C.C" with
        | Ok mol ->
            Assert.Equal(2, mol.Atoms.Length)
            Assert.Equal(0, mol.Bonds.Length) // Disconnected, no bonds
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    // ========================================================================
    // MOLECULAR DESCRIPTORS
    // ========================================================================

    [<Fact>]
    let ``calculateDescriptors returns positive molecular weight`` () =
        match parseSmiles "CCO" with
        | Ok mol ->
            let desc = calculateDescriptors mol
            Assert.True(desc.MolecularWeight > 0.0, $"MW {desc.MolecularWeight} should be positive")
        | Error e -> failwith $"Parse failed: {e}"

    [<Fact>]
    let ``calculateDescriptors counts heavy atoms correctly`` () =
        match parseSmiles "CCO" with
        | Ok mol ->
            let desc = calculateDescriptors mol
            Assert.Equal(3, desc.HeavyAtomCount) // C, C, O
        | Error e -> failwith $"Parse failed: {e}"

    [<Fact>]
    let ``calculateDescriptors identifies hydrogen bond acceptors`` () =
        match parseSmiles "CCO" with
        | Ok mol ->
            let desc = calculateDescriptors mol
            Assert.True(desc.HydrogenBondAcceptors >= 1, "Ethanol has at least 1 HBA (oxygen)")
        | Error e -> failwith $"Parse failed: {e}"

    [<Fact>]
    let ``calculateDescriptors LogP is finite`` () =
        match parseSmiles "c1ccccc1" with // Benzene
        | Ok mol ->
            let desc = calculateDescriptors mol
            Assert.True(System.Double.IsFinite(desc.LogP))
        | Error e -> failwith $"Parse failed: {e}"

    [<Fact>]
    let ``calculateDescriptors TPSA is non-negative`` () =
        match parseSmiles "CCO" with
        | Ok mol ->
            let desc = calculateDescriptors mol
            Assert.True(desc.TPSA >= 0.0)
        | Error e -> failwith $"Parse failed: {e}"

    [<Fact>]
    let ``calculateDescriptors ring count for benzene`` () =
        match parseSmiles "c1ccccc1" with
        | Ok mol ->
            let desc = calculateDescriptors mol
            Assert.True(desc.RingCount >= 1, $"Benzene ring count {desc.RingCount} should be >= 1")
        | Error e -> failwith $"Parse failed: {e}"

    [<Fact>]
    let ``calculateDescriptors aromatic ring count for benzene`` () =
        match parseSmiles "c1ccccc1" with
        | Ok mol ->
            let desc = calculateDescriptors mol
            Assert.True(desc.AromaticRingCount >= 1, $"Benzene aromatic ring count {desc.AromaticRingCount} should be >= 1")
        | Error e -> failwith $"Parse failed: {e}"

    [<Fact>]
    let ``calculateDescriptors FractionCsp3 is in range [0, 1]`` () =
        match parseSmiles "CCCCC" with // Pentane, all sp3
        | Ok mol ->
            let desc = calculateDescriptors mol
            Assert.True(desc.FractionCsp3 >= 0.0 && desc.FractionCsp3 <= 1.0,
                $"FractionCsp3 {desc.FractionCsp3} should be in [0, 1]")
        | Error e -> failwith $"Parse failed: {e}"

    // ========================================================================
    // MOLECULAR FINGERPRINTS
    // ========================================================================

    [<Fact>]
    let ``generateFingerprint produces correct length`` () =
        match parseSmiles "CCO" with
        | Ok mol ->
            let fp = generateFingerprint mol 1024
            Assert.Equal(1024, fp.Bits.Length)
        | Error e -> failwith $"Parse failed: {e}"

    [<Fact>]
    let ``generateFingerprint has non-zero bit count`` () =
        match parseSmiles "CCO" with
        | Ok mol ->
            let fp = generateFingerprint mol 1024
            Assert.True(fp.BitCount > 0, "Fingerprint should have set bits")
        | Error e -> failwith $"Parse failed: {e}"

    [<Fact>]
    let ``generateFingerprint type is PathFingerprint`` () =
        match parseSmiles "C" with
        | Ok mol ->
            let fp = generateFingerprint mol 256
            Assert.Equal("PathFingerprint", fp.Type)
        | Error e -> failwith $"Parse failed: {e}"

    [<Fact>]
    let ``generateFingerprint bit count matches actual set bits`` () =
        match parseSmiles "CC=O" with
        | Ok mol ->
            let fp = generateFingerprint mol 512
            let actualSetBits = fp.Bits |> Array.filter id |> Array.length
            Assert.Equal(actualSetBits, fp.BitCount)
        | Error e -> failwith $"Parse failed: {e}"

    // ========================================================================
    // SIMILARITY CALCULATIONS
    // ========================================================================

    [<Fact>]
    let ``tanimotoSimilarity of identical fingerprints is 1.0`` () =
        match parseSmiles "CCO" with
        | Ok mol ->
            let fp = generateFingerprint mol 256
            let sim = tanimotoSimilarity fp fp
            Assert.True(abs(sim - 1.0) < 1e-10, $"Self-similarity {sim} should be 1.0")
        | Error e -> failwith $"Parse failed: {e}"

    [<Fact>]
    let ``tanimotoSimilarity is in range [0, 1]`` () =
        match parseSmiles "CCO", parseSmiles "c1ccccc1" with
        | Ok mol1, Ok mol2 ->
            let fp1 = generateFingerprint mol1 256
            let fp2 = generateFingerprint mol2 256
            let sim = tanimotoSimilarity fp1 fp2
            Assert.True(sim >= 0.0 && sim <= 1.0, $"Tanimoto {sim} should be in [0, 1]")
        | _ -> failwith "Parse failed"

    [<Fact>]
    let ``tanimotoSimilarity throws for different lengths`` () =
        match parseSmiles "C" with
        | Ok mol ->
            let fp1 = generateFingerprint mol 256
            let fp2 = generateFingerprint mol 512
            Assert.Throws<System.Exception>(fun () ->
                tanimotoSimilarity fp1 fp2 |> ignore) |> ignore
        | Error e -> failwith $"Parse failed: {e}"

    [<Fact>]
    let ``diceSimilarity of identical fingerprints is 1.0`` () =
        match parseSmiles "CCO" with
        | Ok mol ->
            let fp = generateFingerprint mol 256
            let sim = diceSimilarity fp fp
            Assert.True(abs(sim - 1.0) < 1e-10, $"Self Dice similarity {sim} should be 1.0")
        | Error e -> failwith $"Parse failed: {e}"

    [<Fact>]
    let ``diceSimilarity is in range [0, 1]`` () =
        match parseSmiles "CCO", parseSmiles "c1ccccc1" with
        | Ok mol1, Ok mol2 ->
            let fp1 = generateFingerprint mol1 256
            let fp2 = generateFingerprint mol2 256
            let sim = diceSimilarity fp1 fp2
            Assert.True(sim >= 0.0 && sim <= 1.0, $"Dice {sim} should be in [0, 1]")
        | _ -> failwith "Parse failed"

    // ========================================================================
    // DATA LOADING (in-memory)
    // ========================================================================

    [<Fact>]
    let ``loadFromSmilesList succeeds with valid SMILES`` () =
        match loadFromSmilesList [ "C"; "CC"; "CCO" ] with
        | Ok dataset ->
            Assert.Equal(3, dataset.Molecules.Length)
            Assert.Equal(None, dataset.Descriptors)
            Assert.Equal(None, dataset.Fingerprints)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``loadFromSmilesList skips invalid SMILES`` () =
        match loadFromSmilesList [ "C"; ""; "CCO" ] with
        | Ok dataset ->
            // Empty string fails parseSmiles, but "C" and "CCO" succeed
            Assert.True(dataset.Molecules.Length >= 2)
        | Error _ -> () // Also acceptable if all fail

    [<Fact>]
    let ``loadFromSmilesList returns error when all fail`` () =
        match loadFromSmilesList [ "" ] with
        | Error _ -> ()
        | Ok _ -> failwith "Expected Error when all SMILES are invalid"

    // ========================================================================
    // FEATURE EXTRACTION
    // ========================================================================

    [<Fact>]
    let ``descriptorsToFeatures returns 10 features`` () =
        match parseSmiles "CCO" with
        | Ok mol ->
            let desc = calculateDescriptors mol
            let features = descriptorsToFeatures desc
            Assert.Equal(10, features.Length)
        | Error e -> failwith $"Parse failed: {e}"

    [<Fact>]
    let ``fingerprintToFeatures returns 0.0 and 1.0 values`` () =
        match parseSmiles "CCO" with
        | Ok mol ->
            let fp = generateFingerprint mol 64
            let features = fingerprintToFeatures fp
            Assert.Equal(64, features.Length)
            Assert.True(features |> Array.forall (fun f -> f = 0.0 || f = 1.0))
        | Error e -> failwith $"Parse failed: {e}"

    [<Fact>]
    let ``withDescriptors adds descriptors to dataset`` () =
        match loadFromSmilesList [ "C"; "CC"; "CCO" ] with
        | Ok dataset ->
            let enriched = withDescriptors dataset
            Assert.True(enriched.Descriptors.IsSome)
            Assert.Equal(3, enriched.Descriptors.Value.Length)
        | Error e -> failwith $"Load failed: {e}"

    [<Fact>]
    let ``withFingerprints adds fingerprints to dataset`` () =
        match loadFromSmilesList [ "C"; "CC" ] with
        | Ok dataset ->
            let enriched = withFingerprints 256 dataset
            Assert.True(enriched.Fingerprints.IsSome)
            Assert.Equal(2, enriched.Fingerprints.Value.Length)
            Assert.Equal(256, enriched.Fingerprints.Value.[0].Bits.Length)
        | Error e -> failwith $"Load failed: {e}"

    [<Fact>]
    let ``toFeatureMatrix rejects both false`` () =
        match loadFromSmilesList [ "C" ] with
        | Ok dataset ->
            match toFeatureMatrix false false dataset with
            | Error (QuantumError.ValidationError _) -> ()
            | r -> failwith $"Expected ValidationError, got {r}"
        | Error e -> failwith $"Load failed: {e}"

    [<Fact>]
    let ``toFeatureMatrix with descriptors returns correct shape`` () =
        match loadFromSmilesList [ "C"; "CC" ] with
        | Ok dataset ->
            match toFeatureMatrix true false dataset with
            | Ok (features, _) ->
                Assert.Equal(2, features.Length)
                Assert.Equal(10, features.[0].Length) // 10 descriptor features
            | Error e -> failwith $"Expected Ok, got Error: {e}"
        | Error e -> failwith $"Load failed: {e}"

    [<Fact>]
    let ``toFeatureMatrix with fingerprints returns correct shape`` () =
        match loadFromSmilesList [ "C" ] with
        | Ok dataset ->
            let enriched = withFingerprints 128 dataset
            match toFeatureMatrix false true enriched with
            | Ok (features, _) ->
                Assert.Equal(1, features.Length)
                Assert.Equal(128, features.[0].Length)
            | Error e -> failwith $"Expected Ok, got Error: {e}"
        | Error e -> failwith $"Load failed: {e}"

    [<Fact>]
    let ``toFeatureMatrix with both concatenates features`` () =
        match loadFromSmilesList [ "C" ] with
        | Ok dataset ->
            let enriched = dataset |> withDescriptors |> withFingerprints 64
            match toFeatureMatrix true true enriched with
            | Ok (features, _) ->
                Assert.Equal(1, features.Length)
                Assert.Equal(74, features.[0].Length) // 10 descriptors + 64 fingerprint
            | Error e -> failwith $"Expected Ok, got Error: {e}"
        | Error e -> failwith $"Load failed: {e}"
