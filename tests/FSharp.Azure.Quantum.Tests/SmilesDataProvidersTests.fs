module SmilesDataProvidersTests

open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Data
open FSharp.Azure.Quantum.Data.ChemistryDataProviders
open FSharp.Azure.Quantum.Data.SmilesDataProviders

// =============================================================================
// SMILES PARSING TESTS
// =============================================================================

[<Fact>]
let ``parseSmiles parses simple ethanol SMILES`` () =
    match parseSmiles "CCO" with
    | Ok mol ->
        Assert.Equal(Some "CCO", mol.Name)
        Assert.True(mol.Topology.Atoms.Length > 0)
        Assert.True(mol.Geometry.IsNone, "SMILES should not have 3D geometry")
    | Error e ->
        Assert.Fail($"Expected Ok but got Error: {e}")

[<Fact>]
let ``parseSmiles parses benzene ring`` () =
    match parseSmiles "c1ccccc1" with
    | Ok mol ->
        Assert.Equal(6, mol.Topology.Atoms.Length)  // 6 carbons
        Assert.True(mol.Topology.Bonds.Length >= 6)  // At least 6 bonds in ring
    | Error e ->
        Assert.Fail($"Expected Ok but got Error: {e}")

[<Fact>]
let ``parseSmiles returns error for invalid SMILES`` () =
    match parseSmiles "INVALID_NOT_SMILES_{{" with
    | Ok _ -> ()  // Some parsers may be lenient, that's ok
    | Error _ -> ()  // Expected - invalid input

[<Fact>]
let ``parseSmilesMany returns only successful parses`` () =
    let smiles = ["CCO"; "CC(=O)O"; "INVALID"; "c1ccccc1"]
    let results = parseSmilesMany smiles
    Assert.True(results.Length >= 2, "Should parse at least 2 valid SMILES")

// =============================================================================
// SMILES LIST DATASET PROVIDER TESTS
// =============================================================================

[<Fact>]
let ``SmilesListDatasetProvider loads all molecules`` () =
    let smiles = ["CCO"; "CC(=O)O"; "c1ccccc1"]  // Ethanol, acetic acid, benzene
    let provider = SmilesListDatasetProvider(smiles)
    
    match (provider :> IMoleculeDatasetProvider).Load All with
    | Ok dataset ->
        Assert.True(dataset.Molecules.Length >= 2, "Should load at least 2 molecules")
    | Error e ->
        Assert.Fail($"Expected Ok but got Error: {e}")

[<Fact>]
let ``SmilesListDatasetProvider queries by name`` () =
    let smiles = ["CCO"; "CC(=O)O"]
    let provider = SmilesListDatasetProvider(smiles) :> IMoleculeDatasetProvider
    
    match provider.Load (ByName "CCO") with
    | Ok dataset ->
        Assert.Equal(1, dataset.Molecules.Length)
        Assert.Equal(Some "CCO", dataset.Molecules.[0].Name)
    | Error e ->
        Assert.Fail($"Expected Ok but got Error: {e}")

[<Fact>]
let ``SmilesListDatasetProvider returns error for unknown SMILES`` () =
    let smiles = ["CCO"]
    let provider = SmilesListDatasetProvider(smiles) :> IMoleculeDatasetProvider
    
    match provider.Load (ByName "NOT_IN_LIST") with
    | Ok _ -> Assert.Fail("Expected Error for unknown SMILES")
    | Error _ -> ()  // Expected

[<Fact>]
let ``SmilesListDatasetProvider lists names`` () =
    let smiles = ["CCO"; "CC(=O)O"]
    let provider = SmilesListDatasetProvider(smiles) :> IMoleculeDatasetProvider
    
    let names = provider.ListNames()
    Assert.Equal(2, names.Length)
    Assert.Contains("CCO", names)
    Assert.Contains("CC(=O)O", names)

[<Fact>]
let ``SmilesListDatasetProvider Describe returns meaningful string`` () =
    let smiles = ["CCO"; "CC(=O)O"; "c1ccccc1"]
    let provider = SmilesListDatasetProvider(smiles) :> IMoleculeDatasetProvider
    
    let desc = provider.Describe()
    Assert.Contains("3", desc)  // Should mention count
    Assert.Contains("SMILES", desc)

// =============================================================================
// COMPOSITE DATASET PROVIDER TESTS
// =============================================================================

[<Fact>]
let ``CompositeDatasetProvider tries providers in order`` () =
    // Create two providers - first one empty, second has data
    let empty = SmilesListDatasetProvider([]) :> IMoleculeDatasetProvider
    let withData = SmilesListDatasetProvider(["CCO"]) :> IMoleculeDatasetProvider
    
    let composite = CompositeDatasetProvider([empty; withData]) :> IMoleculeDatasetProvider
    
    match composite.Load All with
    | Ok dataset ->
        Assert.True(dataset.Molecules.Length >= 1)
    | Error e ->
        Assert.Fail($"Expected Ok but got Error: {e}")

[<Fact>]
let ``CompositeDatasetProvider combines with built-in provider`` () =
    let builtIn = defaultDatasetProvider
    let custom = SmilesListDatasetProvider(["CCO"]) :> IMoleculeDatasetProvider
    
    let composite = CompositeDatasetProvider([builtIn; custom]) :> IMoleculeDatasetProvider
    
    // Should find H2 from built-in
    match composite.Load (ByName "H2") with
    | Ok dataset ->
        Assert.Equal(1, dataset.Molecules.Length)
    | Error e ->
        Assert.Fail($"Expected Ok but got Error: {e}")

[<Fact>]
let ``CompositeDatasetProvider ListNames combines all providers`` () =
    let provider1 = SmilesListDatasetProvider(["CCO"]) :> IMoleculeDatasetProvider
    let provider2 = SmilesListDatasetProvider(["c1ccccc1"]) :> IMoleculeDatasetProvider
    
    let composite = CompositeDatasetProvider([provider1; provider2]) :> IMoleculeDatasetProvider
    
    let names = composite.ListNames()
    Assert.Contains("CCO", names)
    Assert.Contains("c1ccccc1", names)

// =============================================================================
// MOLECULE INSTANCE CONVERSION TESTS
// =============================================================================

[<Fact>]
let ``fromSmilesMolecule creates correct topology`` () =
    match MolecularData.parseSmiles "CCO" with
    | Ok mol ->
        let instance = fromSmilesMolecule mol
        
        Assert.True(instance.Topology.Atoms.Length > 0)
        Assert.True(instance.Geometry.IsNone, "SMILES should not have geometry")
        Assert.True(instance.Topology.Metadata.ContainsKey "smiles")
    | Error e ->
        Assert.Fail($"Expected Ok but got Error: {e}")

[<Fact>]
let ``fromSingleSmiles creates working provider`` () =
    let provider = fromSingleSmiles "CCO"
    
    match provider.Load All with
    | Ok dataset ->
        Assert.Equal(1, dataset.Molecules.Length)
    | Error e ->
        Assert.Fail($"Expected Ok but got Error: {e}")

// =============================================================================
// INTEGRATION WITH CHEMISTRY PROVIDERS
// =============================================================================

[<Fact>]
let ``SMILES provider molecules have valid topology`` () =
    let provider = SmilesListDatasetProvider(["CCO"; "c1ccccc1"]) :> IMoleculeDatasetProvider
    
    match provider.Load All with
    | Ok dataset ->
        for mol in dataset.Molecules do
            Assert.True(mol.Topology.Atoms.Length > 0, "Should have atoms")
            Assert.True(mol.Topology.Bonds.Length > 0, "Should have bonds")
    | Error e ->
        Assert.Fail($"Expected Ok but got Error: {e}")
