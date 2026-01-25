namespace FSharp.Azure.Quantum.Data

open System
open System.Globalization
open System.IO
open FSharp.Azure.Quantum.Core

/// Chemistry-related data providers and importers.
///
/// This file implements the 4-layer provider architecture for chemistry data:
/// 1. Element Provider - symbol → atomic properties
/// 2. Topology Parser - SMILES/SDF → atoms/bonds graph  
/// 3. Geometry Provider - topology → 3D coordinates
/// 4. Dataset Provider - collections of molecules with labels/metadata
///
/// NOTE: This file lives in the Data layer to avoid circular dependencies
/// with solver modules (e.g., QuantumChemistry).
module ChemistryDataProviders =

    // ========================================================================
    // LAYER 1: ELEMENT PROVIDER
    // ========================================================================

    /// Element properties for quantum chemistry calculations.
    type ElementProperties =
        { AtomicNumber: int
          Symbol: string
          Name: string
          AtomicMass: float
          CovalentRadius: float option
          Electronegativity: float option }

    /// Provider interface for element data (atomic properties).
    /// Replaces hardcoded atomic mass/number tables scattered across codebase.
    type IElementProvider =
        /// Get element by symbol (case-insensitive)
        abstract TryGetBySymbol: symbol: string -> ElementProperties option
        /// Get element by atomic number
        abstract TryGetByAtomicNumber: atomicNumber: int -> ElementProperties option
        /// Check if symbol is valid
        abstract IsValidSymbol: symbol: string -> bool
        /// Estimate bond length between two elements (sum of covalent radii)
        abstract EstimateBondLength: symbol1: string -> symbol2: string -> float option

    /// Default element provider backed by PeriodicTable.
    /// This is the "batteries included" default.
    type PeriodicTableElementProvider() =
        interface IElementProvider with
            member _.TryGetBySymbol(symbol: string) =
                PeriodicTable.tryBySymbol symbol
                |> Option.map (fun e ->
                    { AtomicNumber = e.AtomicNumber
                      Symbol = e.Symbol
                      Name = e.Name
                      AtomicMass = e.AtomicMass
                      CovalentRadius = e.CovalentRadius
                      Electronegativity = e.Electronegativity })

            member _.TryGetByAtomicNumber(atomicNumber: int) =
                PeriodicTable.tryByNumber atomicNumber
                |> Option.map (fun e ->
                    { AtomicNumber = e.AtomicNumber
                      Symbol = e.Symbol
                      Name = e.Name
                      AtomicMass = e.AtomicMass
                      CovalentRadius = e.CovalentRadius
                      Electronegativity = e.Electronegativity })

            member _.IsValidSymbol(symbol: string) =
                PeriodicTable.isValidSymbol symbol

            member _.EstimateBondLength symbol1 symbol2 =
                PeriodicTable.estimateBondLength symbol1 symbol2

    /// Singleton instance for convenience
    let defaultElementProvider = PeriodicTableElementProvider() :> IElementProvider

    // ========================================================================
    // LAYER 2: TOPOLOGY PARSER
    // ========================================================================

    /// Molecular topology (atoms + bonds graph, no 3D geometry required).
    /// This is the output of SMILES/SDF parsing before geometry generation.
    type MoleculeTopology =
        { /// Element symbols for each atom
          Atoms: string array
          /// Bonds as (atom1Index, atom2Index, bondOrder option)
          Bonds: (int * int * float option) array
          /// Total molecular charge
          Charge: int option
          /// Spin multiplicity (2S+1)
          Multiplicity: int option
          /// Additional metadata (e.g., stereochemistry hints, source format)
          Metadata: Map<string, string> }

    /// Interface for parsing molecular topology from string formats.
    type IMoleculeTopologyParser =
        /// List of supported format identifiers (e.g., ["smiles"; "sdf"; "mol"])
        abstract SupportedFormats: string list
        /// Parse input string into molecular topology
        abstract Parse: input: string -> QuantumResult<MoleculeTopology>
        /// Parse with explicit format hint
        abstract ParseWithFormat: format: string -> input: string -> QuantumResult<MoleculeTopology>

    // ========================================================================
    // LAYER 3: GEOMETRY PROVIDER (THE MISSING KEY FOR REAL QC)
    // ========================================================================

    /// 3D coordinates for a single atom.
    type AtomGeometry =
        { X: float
          Y: float
          Z: float }

    /// Full 3D geometry for a molecule.
    type MoleculeGeometry =
        { /// 3D coordinates for each atom (same order as topology atoms)
          Coordinates: AtomGeometry array
          /// Coordinate units ("angstrom" or "bohr")
          Units: string }

    /// Provider interface for generating/retrieving 3D molecular geometry.
    /// This is the CRITICAL piece for making QC usable with real molecules.
    type IGeometryProvider =
        /// Get 3D geometry for a molecular topology.
        /// Returns None if geometry cannot be determined (topology-only molecule).
        /// Returns Error for actual failures (invalid topology, provider error).
        abstract TryGetGeometry: topology: MoleculeTopology -> QuantumResult<MoleculeGeometry option>

    /// Async variant for geometry providers that do expensive computation
    /// (e.g., conformer generation via RDKit/OpenBabel).
    type IGeometryProviderAsync =
        /// Get 3D geometry asynchronously
        abstract TryGetGeometryAsync: topology: MoleculeTopology -> Async<QuantumResult<MoleculeGeometry option>>

    /// A "no-op" geometry provider that always returns None.
    /// Useful for topology-only workflows (drug discovery ML).
    type NoGeometryProvider() =
        interface IGeometryProvider with
            member _.TryGetGeometry(_topology) = Ok None

    // ========================================================================
    // LAYER 4: DATASET PROVIDER
    // ========================================================================

    /// A molecule with optional geometry (the unified instance type).
    type MoleculeInstance =
        { /// Unique identifier (optional)
          Id: string option
          /// Human-readable name
          Name: string option
          /// Molecular topology (atoms/bonds)
          Topology: MoleculeTopology
          /// 3D geometry (optional - may be None for topology-only)
          Geometry: MoleculeGeometry option }

    /// A dataset of molecules with optional labels and metadata.
    type MoleculeDataset =
        { /// All molecules in the dataset
          Molecules: MoleculeInstance array
          /// Optional integer labels (e.g., activity class)
          Labels: int array option
          /// Name of the label column (for provenance)
          LabelColumn: string option
          /// Dataset-level metadata
          Metadata: Map<string, string> }

    /// Query type for dataset lookups.
    type DatasetQuery =
        /// Query by molecule name/identifier
        | ByName of string
        /// Query by file path
        | ByPath of string
        /// Query by category (e.g., "diatomic", "aromatic")
        | ByCategory of string
        /// Load all molecules
        | All

    /// Provider interface for molecule datasets.
    type IMoleculeDatasetProvider =
        /// Human-readable description of this provider
        abstract Describe: unit -> string
        /// Load molecules matching the query
        abstract Load: query: DatasetQuery -> QuantumResult<MoleculeDataset>
        /// List available molecule names/identifiers
        abstract ListNames: unit -> string list

    /// Async variant for providers that do I/O.
    type IMoleculeDatasetProviderAsync =
        /// Human-readable description
        abstract Describe: unit -> string
        /// Load molecules asynchronously
        abstract LoadAsync: query: DatasetQuery -> Async<QuantumResult<MoleculeDataset>>
        /// List available names
        abstract ListNamesAsync: unit -> Async<string list>

    // ========================================================================
    // LEGACY COMPATIBILITY TYPE (for existing code)
    // ========================================================================

    /// Legacy molecule entry type for backward compatibility.
    /// Maps to MoleculeLibrary's internal representation.
    type MoleculeEntry =
        { Name: string
          Atoms: MoleculeLibrary.Atom list
          Bonds: MoleculeLibrary.Bond list
          Charge: int
          Multiplicity: int }

    // ========================================================================
    // BUILT-IN PROVIDERS
    // ========================================================================

    /// Built-in dataset provider that wraps `MoleculeLibrary`.
    /// Provides the "batteries included" curated molecule set.
    type BuiltInMoleculeDatasetProvider() =

        let toTopology (atoms: MoleculeLibrary.Atom list) (bonds: MoleculeLibrary.Bond list) (charge: int) (mult: int) : MoleculeTopology =
            { Atoms = atoms |> List.map (fun a -> a.Element) |> List.toArray
              Bonds = bonds |> List.map (fun b -> (b.Atom1, b.Atom2, Some b.BondOrder)) |> List.toArray
              Charge = Some charge
              Multiplicity = Some mult
              Metadata = Map.empty }

        let toGeometry (atoms: MoleculeLibrary.Atom list) : MoleculeGeometry =
            { Coordinates =
                atoms
                |> List.map (fun a ->
                    let (x, y, z) = a.Position
                    { X = x; Y = y; Z = z })
                |> List.toArray
              Units = "angstrom" }

        let toInstance (m: MoleculeLibrary.Molecule) : MoleculeInstance =
            { Id = Some m.Name
              Name = Some m.Name
              Topology = toTopology m.Atoms m.Bonds m.Charge m.Multiplicity
              Geometry = Some (toGeometry m.Atoms) }

        interface IMoleculeDatasetProvider with
            member _.Describe() =
                "Built-in curated molecule library (MoleculeLibrary)"

            member _.Load(query: DatasetQuery) =
                match query with
                | ByName name ->
                    match MoleculeLibrary.tryGet name with
                    | Some m ->
                        Ok { Molecules = [| toInstance m |]
                             Labels = None
                             LabelColumn = None
                             Metadata = Map.empty }
                    | None ->
                        Error (QuantumError.ValidationError("MoleculeNotFound", $"Molecule '{name}' not found in library"))

                | ByCategory category ->
                    let molecules =
                        MoleculeLibrary.byCategory category
                        |> Array.map toInstance
                    if molecules.Length = 0 then
                        Error (QuantumError.ValidationError("CategoryNotFound", $"No molecules in category '{category}'"))
                    else
                        Ok { Molecules = molecules
                             Labels = None
                             LabelColumn = None
                             Metadata = Map.ofList ["category", category] }

                | All ->
                    let molecules =
                        MoleculeLibrary.all()
                        |> Array.map toInstance
                    Ok { Molecules = molecules
                         Labels = None
                         LabelColumn = None
                         Metadata = Map.empty }

                | ByPath _ ->
                    Error (QuantumError.ValidationError("UnsupportedQuery", "BuiltInMoleculeDatasetProvider does not support file paths"))

            member _.ListNames() =
                MoleculeLibrary.all()
                |> Array.map (fun m -> m.Name)
                |> Array.toList

        /// Legacy interface for backward compatibility
        member this.TryGet(name: string) : MoleculeEntry option =
            MoleculeLibrary.tryGet name
            |> Option.map (fun m ->
                { Name = m.Name
                  Atoms = m.Atoms
                  Bonds = m.Bonds
                  Charge = m.Charge
                  Multiplicity = m.Multiplicity })

    /// Singleton instance for convenience
    let defaultDatasetProvider = BuiltInMoleculeDatasetProvider() :> IMoleculeDatasetProvider

    // ========================================================================
    // XYZ IMPORTER
    // ========================================================================

    /// Represents a parsed XYZ file.
    type XyzMolecule =
        { Name: string
          Atoms: MoleculeLibrary.Atom list
          Bonds: MoleculeLibrary.Bond list }

    module XyzImporter =

        let private tryParseFloat (s: string) : float option =
            match Double.TryParse(s, NumberStyles.Float ||| NumberStyles.AllowThousands, CultureInfo.InvariantCulture) with
            | true, v -> Some v
            | false, _ -> None

        let private inferBonds (atoms: MoleculeLibrary.Atom list) : MoleculeLibrary.Bond list =
            let atomArray = atoms |> List.toArray
            let atomCount = atomArray.Length

            [
                for i in 0 .. atomCount - 2 do
                    for j in i + 1 .. atomCount - 1 do
                        let atom1 = atomArray.[i]
                        let atom2 = atomArray.[j]

                        let (x1, y1, z1) = atom1.Position
                        let (x2, y2, z2) = atom2.Position
                        let dx = x2 - x1
                        let dy = y2 - y1
                        let dz = z2 - z1
                        let distance = sqrt (dx * dx + dy * dy + dz * dz)

                        match PeriodicTable.estimateBondLength atom1.Element atom2.Element with
                        | Some expectedLength ->
                            // Allow fairly generous tolerance because XYZ may be unoptimized
                            let tolerance = 0.25
                            let maxBondLength = expectedLength * (1.0 + tolerance)

                            if distance <= maxBondLength then
                                yield { Atom1 = i; Atom2 = j; BondOrder = 1.0 }
                        | None ->
                            // Fallback heuristic for elements without covalent radii
                            if distance < 3.0 then
                                yield { Atom1 = i; Atom2 = j; BondOrder = 1.0 }
            ]

        /// Parse molecule geometry from an XYZ file.
        ///
        /// This importer only reads geometry; charge/multiplicity are not part of XYZ
        /// and must be provided by the caller if needed.
        let fromFileAsync (filePath: string) : Async<QuantumResult<XyzMolecule>> =
            async {
                try
                    if not (File.Exists filePath) then
                        return Error (QuantumError.IOError("ReadXYZ", filePath, "File not found"))
                    else
                        let! lines = File.ReadAllLinesAsync(filePath) |> Async.AwaitTask

                        let lines =
                            lines
                            |> Array.map (fun l -> l.Trim())
                            |> Array.filter (fun l -> not (String.IsNullOrWhiteSpace l))

                        if lines.Length < 3 then
                            return Error (QuantumError.ValidationError("XYZFile", "XYZ file must have at least 3 non-empty lines"))
                        else
                            return
                                result {
                                    let! atomCount =
                                        match Int32.TryParse(lines[0]) with
                                        | true, n -> Ok n
                                        | false, _ -> Error (QuantumError.ValidationError("XYZFile", "First line must be atom count"))

                                    do!
                                        if atomCount < 1 then
                                            Error (QuantumError.ValidationError("AtomCount", "Atom count must be positive"))
                                        elif lines.Length < 2 + atomCount then
                                            Error (
                                                QuantumError.ValidationError(
                                                    "XYZFile",
                                                    $"File has {lines.Length} lines but needs {2 + atomCount} for {atomCount} atoms"
                                                )
                                            )
                                        else
                                            Ok ()

                                    let name = lines[1]

                                    let! atoms =
                                        lines[2 .. 1 + atomCount]
                                        |> Array.mapi (fun idx line ->
                                            let parts =
                                                line.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)

                                            if parts.Length < 4 then
                                                Error (
                                                    QuantumError.ValidationError(
                                                        "XYZLine",
                                                        $"Line {idx + 3}: Expected 'Element X Y Z', got '{line}'"
                                                    )
                                                )
                                            else
                                                let element = parts[0]

                                                match tryParseFloat parts[1], tryParseFloat parts[2], tryParseFloat parts[3] with
                                                | Some x, Some y, Some z ->
                                                    if PeriodicTable.isValidSymbol element then
                                                        Ok { MoleculeLibrary.Atom.Element = element; MoleculeLibrary.Atom.Position = (x, y, z) }
                                                    else
                                                        Error (
                                                            QuantumError.ValidationError(
                                                                "Element",
                                                                $"Line {idx + 3}: Unknown element symbol '{element}'"
                                                            )
                                                        )
                                                | _ ->
                                                    Error (
                                                        QuantumError.ValidationError(
                                                            "XYZLine",
                                                            $"Line {idx + 3}: Could not parse coordinates from '{line}'"
                                                        )
                                                    ))
                                        |> Array.fold (fun acc next ->
                                            match acc, next with
                                            | Error e, _
                                            | _, Error e -> Error e
                                            | Ok atomsAcc, Ok atom -> Ok (atom :: atomsAcc)) (Ok [])
                                        |> Result.map List.rev

                                    let moleculeName =
                                        if String.IsNullOrWhiteSpace name then
                                            "Molecule"
                                        else
                                            name

                                    let bonds = inferBonds atoms

                                    return { Name = moleculeName; Atoms = atoms; Bonds = bonds }
                                }
                with ex ->
                    return Error (QuantumError.OperationError("XYZParsing", ex.Message))
            }

        /// Produce XYZ content for an `XyzMolecule`.
        let toXyz (molecule: XyzMolecule) : string =
            let header = string molecule.Atoms.Length

            let body =
                molecule.Atoms
                |> List.map (fun atom ->
                    let (x, y, z) = atom.Position
                    String.Format(
                        CultureInfo.InvariantCulture,
                        "{0,-2}  {1,10:0.000000}  {2,10:0.000000}  {3,10:0.000000}",
                        atom.Element,
                        x,
                        y,
                        z
                    ))

            String.concat Environment.NewLine ([ header; molecule.Name ] @ body) + Environment.NewLine

        /// Save `XyzMolecule` to a file.
        let saveToFileAsync (filePath: string) (molecule: XyzMolecule) : Async<QuantumResult<unit>> =
            async {
                try
                    let content = toXyz molecule
                    do! File.WriteAllTextAsync(filePath, content) |> Async.AwaitTask
                    return Ok ()
                with ex ->
                    return Error (QuantumError.IOError("WriteXYZ", filePath, ex.Message))
            }

        /// Convert XyzMolecule to the new MoleculeInstance type.
        let toMoleculeInstance (xyz: XyzMolecule) (charge: int option) (multiplicity: int option) : MoleculeInstance =
            let topology =
                { Atoms = xyz.Atoms |> List.map (fun a -> a.Element) |> List.toArray
                  Bonds = xyz.Bonds |> List.map (fun b -> (b.Atom1, b.Atom2, Some b.BondOrder)) |> List.toArray
                  Charge = charge
                  Multiplicity = multiplicity
                  Metadata = Map.ofList ["source", "xyz"] }

            let geometry =
                { Coordinates =
                    xyz.Atoms
                    |> List.map (fun a ->
                        let (x, y, z) = a.Position
                        { X = x; Y = y; Z = z })
                    |> List.toArray
                  Units = "angstrom" }

            { Id = Some xyz.Name
              Name = Some xyz.Name
              Topology = topology
              Geometry = Some geometry }

        /// Load XYZ file and return as MoleculeInstance.
        let loadAsMoleculeInstanceAsync (filePath: string) (charge: int option) (multiplicity: int option) : Async<QuantumResult<MoleculeInstance>> =
            async {
                let! result = fromFileAsync filePath
                return result |> Result.map (fun xyz -> toMoleculeInstance xyz charge multiplicity)
            }

    // ========================================================================
    // XYZ FILE DATASET PROVIDER
    // ========================================================================

    /// Dataset provider that loads molecules from XYZ files in a directory.
    type XyzFileDatasetProvider(directory: string) =

        let loadFile (filePath: string) : Async<QuantumResult<MoleculeInstance>> =
            XyzImporter.loadAsMoleculeInstanceAsync filePath None None

        interface IMoleculeDatasetProviderAsync with
            member _.Describe() =
                $"XYZ file dataset provider (directory: {directory})"

            member _.LoadAsync(query: DatasetQuery) =
                async {
                    match query with
                    | ByName name ->
                        let filePath = Path.Combine(directory, name + ".xyz")
                        if File.Exists filePath then
                            let! result = loadFile filePath
                            return result |> Result.map (fun m ->
                                { Molecules = [| m |]
                                  Labels = None
                                  LabelColumn = None
                                  Metadata = Map.ofList ["source", filePath] })
                        else
                            return Error (QuantumError.IOError("LoadXYZ", filePath, "File not found"))

                    | ByPath path ->
                        if File.Exists path then
                            let! result = loadFile path
                            return result |> Result.map (fun m ->
                                { Molecules = [| m |]
                                  Labels = None
                                  LabelColumn = None
                                  Metadata = Map.ofList ["source", path] })
                        else
                            return Error (QuantumError.IOError("LoadXYZ", path, "File not found"))

                    | All ->
                        if Directory.Exists directory then
                            let files = Directory.GetFiles(directory, "*.xyz")
                            let! results =
                                files
                                |> Array.map loadFile
                                |> Async.Parallel

                            let molecules =
                                results
                                |> Array.choose (function Ok m -> Some m | Error _ -> None)

                            return Ok { Molecules = molecules
                                        Labels = None
                                        LabelColumn = None
                                        Metadata = Map.ofList ["source", directory; "file_count", string files.Length] }
                        else
                            return Error (QuantumError.IOError("LoadXYZ", directory, "Directory not found"))

                    | ByCategory _ ->
                        return Error (QuantumError.ValidationError("UnsupportedQuery", "XyzFileDatasetProvider does not support category queries"))
                }

            member _.ListNamesAsync() =
                async {
                    if Directory.Exists directory then
                        return
                            Directory.GetFiles(directory, "*.xyz")
                            |> Array.map (fun f -> Path.GetFileNameWithoutExtension f)
                            |> Array.toList
                    else
                        return []
                }

    // ========================================================================
    // CACHING WRAPPER
    // ========================================================================

    /// Caching wrapper for any dataset provider.
    /// Caches loaded datasets by query to avoid repeated I/O.
    type CachingDatasetProvider(inner: IMoleculeDatasetProvider) =
        let cache = System.Collections.Concurrent.ConcurrentDictionary<string, MoleculeDataset>()

        let queryToKey (query: DatasetQuery) =
            match query with
            | ByName name -> $"name:{name}"
            | ByPath path -> $"path:{path}"
            | ByCategory cat -> $"category:{cat}"
            | All -> "all"

        interface IMoleculeDatasetProvider with
            member _.Describe() =
                $"Caching wrapper for: {inner.Describe()}"

            member _.Load(query: DatasetQuery) =
                let key = queryToKey query
                match cache.TryGetValue key with
                | true, dataset -> Ok dataset
                | false, _ ->
                    match inner.Load query with
                    | Ok dataset ->
                        cache.TryAdd(key, dataset) |> ignore
                        Ok dataset
                    | Error e -> Error e

            member _.ListNames() =
                inner.ListNames()

        /// Clear the cache
        member _.ClearCache() = cache.Clear()

        /// Get cache statistics
        member _.CacheCount = cache.Count

    // ========================================================================
    // CONVERSION UTILITIES
    // ========================================================================

    /// Module for converting between provider types and legacy types.
    module Conversions =

        /// Convert MoleculeInstance to legacy MoleculeEntry (loses geometry).
        let toMoleculeEntry (instance: MoleculeInstance) : MoleculeEntry option =
            instance.Geometry |> Option.map (fun geom ->
                let atoms =
                    Array.zip instance.Topology.Atoms geom.Coordinates
                    |> Array.map (fun (element, coord) ->
                        { MoleculeLibrary.Atom.Element = element
                          MoleculeLibrary.Atom.Position = (coord.X, coord.Y, coord.Z) })
                    |> Array.toList

                let bonds =
                    instance.Topology.Bonds
                    |> Array.map (fun (a1, a2, order) ->
                        { MoleculeLibrary.Bond.Atom1 = a1
                          MoleculeLibrary.Bond.Atom2 = a2
                          MoleculeLibrary.Bond.BondOrder = order |> Option.defaultValue 1.0 })
                    |> Array.toList

                { MoleculeEntry.Name = instance.Name |> Option.defaultValue "Unknown"
                  Atoms = atoms
                  Bonds = bonds
                  Charge = instance.Topology.Charge |> Option.defaultValue 0
                  Multiplicity = instance.Topology.Multiplicity |> Option.defaultValue 1 })

        /// Convert MoleculeEntry to MoleculeInstance.
        let fromMoleculeEntry (entry: MoleculeEntry) : MoleculeInstance =
            let topology =
                { Atoms = entry.Atoms |> List.map (fun a -> a.Element) |> List.toArray
                  Bonds = entry.Bonds |> List.map (fun b -> (b.Atom1, b.Atom2, Some b.BondOrder)) |> List.toArray
                  Charge = Some entry.Charge
                  Multiplicity = Some entry.Multiplicity
                  Metadata = Map.empty }

            let geometry =
                { Coordinates =
                    entry.Atoms
                    |> List.map (fun a ->
                        let (x, y, z) = a.Position
                        { X = x; Y = y; Z = z })
                    |> List.toArray
                  Units = "angstrom" }

            { Id = Some entry.Name
              Name = Some entry.Name
              Topology = topology
              Geometry = Some geometry }
