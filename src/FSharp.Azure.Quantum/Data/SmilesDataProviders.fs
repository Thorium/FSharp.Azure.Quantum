namespace FSharp.Azure.Quantum.Data

open System
open FSharp.Azure.Quantum.Core

/// SMILES-based dataset providers for drug discovery workflows.
/// These providers wrap MolecularData's SMILES parsing with the unified
/// IMoleculeDatasetProvider interface from ChemistryDataProviders.
///
/// NOTE: SMILES molecules are topology-only (no 3D geometry).
/// For quantum chemistry calculations, combine with a geometry provider
/// or use XYZ files instead.
module SmilesDataProviders =

    open ChemistryDataProviders

    // ========================================================================
    // MOLECULE CONVERSION (MolecularData.Molecule → MoleculeInstance)
    // ========================================================================

    /// Convert a MolecularData.Molecule (SMILES-parsed) to a MoleculeInstance.
    /// Note: SMILES molecules have no 3D geometry - Geometry will be None.
    let fromSmilesMolecule (mol: MolecularData.Molecule) : MoleculeInstance =
        let topology: MoleculeTopology =
            { Atoms = mol.Atoms |> Array.map (fun a -> a.Element)
              Bonds = 
                mol.Bonds 
                |> Array.map (fun b -> (b.Atom1, b.Atom2, Some (float b.Order)))
              Charge = 
                mol.Atoms 
                |> Array.sumBy (fun a -> a.Charge)
                |> Some
              Multiplicity = Some 1  // Assume singlet for SMILES
              Metadata = 
                [ "smiles", mol.Smiles
                  "formula", mol.Formula ]
                |> Map.ofList }
        
        { Id = Some mol.Smiles
          Name = Some mol.Smiles
          Topology = topology
          Geometry = None }

    // ========================================================================
    // CSV+SMILES DATASET PROVIDER
    // ========================================================================

    /// Dataset provider that loads molecules from CSV files with a SMILES column.
    /// Wraps MolecularData.loadFromCsv with the unified provider interface.
    /// 
    /// Example:
    ///   let provider = CsvSmilesDatasetProvider("molecules.csv", "SMILES", Some "Activity")
    ///   match provider.Load All with
    ///   | Ok dataset -> printfn "Loaded %d molecules" dataset.Molecules.Length
    ///   | Error e -> printfn "Error: %A" e
    type CsvSmilesDatasetProvider(filePath: string, smilesColumn: string, ?labelColumn: string) =

        let loadDataset () : QuantumResult<MoleculeDataset> =
            MolecularData.loadFromCsv filePath smilesColumn labelColumn
            |> Result.map (fun dataset ->
                { Molecules = dataset.Molecules |> Array.map fromSmilesMolecule
                  Labels = dataset.Labels
                  LabelColumn = dataset.LabelColumn
                  Metadata = Map.ofList ["source", filePath; "format", "csv+smiles"] })

        interface IMoleculeDatasetProvider with
            member _.Describe() =
                $"CSV+SMILES dataset provider (file: {filePath}, column: {smilesColumn})"

            member _.Load(query: DatasetQuery) =
                match query with
                | All -> loadDataset ()
                | ByName name ->
                    loadDataset ()
                    |> Result.bind (fun ds ->
                        let matching = 
                            ds.Molecules 
                            |> Array.filter (fun m -> 
                                m.Name = Some name || m.Id = Some name)
                        if matching.Length > 0 then
                            Ok { ds with Molecules = matching }
                        else
                            Error (QuantumError.ValidationError("MoleculeNotFound", $"No molecule with SMILES '{name}' found")))
                | ByPath _ ->
                    Error (QuantumError.ValidationError("UnsupportedQuery", "CsvSmilesDatasetProvider does not support path queries"))
                | ByCategory _ ->
                    Error (QuantumError.ValidationError("UnsupportedQuery", "CsvSmilesDatasetProvider does not support category queries"))

            member _.ListNames() =
                match loadDataset () with
                | Ok ds -> 
                    ds.Molecules 
                    |> Array.choose (fun m -> m.Name)
                    |> Array.toList
                | Error _ -> []

    // ========================================================================
    // SMILES LIST DATASET PROVIDER
    // ========================================================================

    /// Dataset provider for in-memory SMILES lists.
    /// Useful for quick prototyping without file I/O.
    /// 
    /// Example:
    ///   let smiles = ["CCO"; "CC(=O)O"; "c1ccccc1"]  // Ethanol, acetic acid, benzene
    ///   let provider = SmilesListDatasetProvider(smiles)
    ///   match provider.Load All with
    ///   | Ok dataset -> 
    ///       for mol in dataset.Molecules do
    ///           printfn "%s: %d atoms" 
    ///               (mol.Name |> Option.defaultValue "?")
    ///               mol.Topology.Atoms.Length
    ///   | Error e -> printfn "Parse error: %A" e
    type SmilesListDatasetProvider(smilesList: string list) =

        let loadDataset () : QuantumResult<MoleculeDataset> =
            MolecularData.loadFromSmilesList smilesList
            |> Result.map (fun dataset ->
                { Molecules = dataset.Molecules |> Array.map fromSmilesMolecule
                  Labels = dataset.Labels
                  LabelColumn = dataset.LabelColumn
                  Metadata = Map.ofList ["format", "smiles_list"; "count", string smilesList.Length] })

        interface IMoleculeDatasetProvider with
            member _.Describe() =
                $"SMILES list provider ({smilesList.Length} molecules)"

            member _.Load(query: DatasetQuery) =
                match query with
                | All -> loadDataset ()
                | ByName name ->
                    if smilesList |> List.contains name then
                        MolecularData.parseSmiles name
                        |> Result.map (fun mol ->
                            { Molecules = [| fromSmilesMolecule mol |]
                              Labels = None
                              LabelColumn = None
                              Metadata = Map.ofList ["format", "smiles_list"] })
                    else
                        Error (QuantumError.ValidationError("MoleculeNotFound", $"SMILES '{name}' not in list"))
                | ByPath _ ->
                    Error (QuantumError.ValidationError("UnsupportedQuery", "SmilesListDatasetProvider does not support path queries"))
                | ByCategory _ ->
                    Error (QuantumError.ValidationError("UnsupportedQuery", "SmilesListDatasetProvider does not support category queries"))

            member _.ListNames() = smilesList

    // ========================================================================
    // COMPOSITE DATASET PROVIDER
    // ========================================================================

    /// Composite provider that queries multiple providers in sequence.
    /// Returns the first successful result.
    /// 
    /// Example:
    ///   // Try built-in library first, then fall back to CSV file
    ///   let provider = CompositeDatasetProvider [
    ///       defaultDatasetProvider
    ///       CsvSmilesDatasetProvider("custom_molecules.csv", "SMILES")
    ///   ]
    type CompositeDatasetProvider(providers: IMoleculeDatasetProvider list) =

        interface IMoleculeDatasetProvider with
            member _.Describe() =
                let names = 
                    providers 
                    |> List.map (fun p -> p.Describe()) 
                    |> String.concat "; "
                $"Composite: [{names}]"

            member _.Load(query: DatasetQuery) =
                let rec tryProviders (remaining: IMoleculeDatasetProvider list) =
                    match remaining with
                    | [] -> 
                        Error (QuantumError.ValidationError("NoProvider", "No provider could satisfy the query"))
                    | provider :: rest ->
                        match provider.Load query with
                        | Ok result when result.Molecules.Length > 0 -> Ok result
                        | Ok _ -> tryProviders rest  // Empty result - try next provider
                        | Error _ -> tryProviders rest
                tryProviders providers

            member _.ListNames() =
                providers
                |> List.collect (fun p -> p.ListNames())
                |> List.distinct

    // ========================================================================
    // CONVENIENCE FUNCTIONS
    // ========================================================================

    /// Parse a single SMILES string and return as MoleculeInstance.
    let parseSmiles (smiles: string) : QuantumResult<MoleculeInstance> =
        MolecularData.parseSmiles smiles
        |> Result.map fromSmilesMolecule

    /// Parse multiple SMILES strings, returning only successful parses.
    let parseSmilesMany (smilesList: string list) : MoleculeInstance list =
        smilesList
        |> List.choose (fun s ->
            match parseSmiles s with
            | Ok mol -> Some mol
            | Error _ -> None)

    /// Create a provider from a single SMILES string.
    /// Useful for quick one-off molecule creation.
    let fromSingleSmiles (smiles: string) : IMoleculeDatasetProvider =
        SmilesListDatasetProvider([smiles]) :> IMoleculeDatasetProvider
