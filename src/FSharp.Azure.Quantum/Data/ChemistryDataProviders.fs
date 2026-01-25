namespace FSharp.Azure.Quantum.Data

open System
open System.Globalization
open System.IO
open System.Text.RegularExpressions
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

    // ========================================================================
    // SDF/MOL FILE PARSER AND PROVIDER
    // ========================================================================

    /// Module for parsing MDL MOL and SDF (Structure-Data File) formats.
    /// 
    /// Supports:
    /// - MOL V2000 format (the most common version)
    /// - SDF files (multiple MOL records with associated data)
    /// 
    /// The parser extracts:
    /// - 3D atomic coordinates (in Angstroms)
    /// - Bond connectivity and types
    /// - Associated data fields (for SDF files)
    /// - Charge and other properties from the M  CHG lines
    module SdfMolParser =

        open System.Text.RegularExpressions

        /// Bond type from MOL file
        type MolBondType =
            | Single = 1
            | Double = 2
            | Triple = 3
            | Aromatic = 4
            | SingleOrDouble = 5
            | SingleOrAromatic = 6
            | DoubleOrAromatic = 7
            | Any = 8

        /// Parsed atom from MOL file
        type MolAtom = {
            X: float
            Y: float
            Z: float
            Symbol: string
            MassDifference: int
            Charge: int
            StereoParity: int
        }

        /// Parsed bond from MOL file
        type MolBond = {
            Atom1: int  // 1-indexed
            Atom2: int  // 1-indexed
            BondType: int
            Stereo: int
        }

        /// Parsed MOL record
        type MolRecord = {
            Name: string
            Comment: string
            Atoms: MolAtom array
            Bonds: MolBond array
            Charges: (int * int) list  // (atom index 1-based, charge)
            Properties: Map<string, string>  // Associated data from SDF
        }

        /// Parse a single MOL block (V2000 format)
        let parseMolBlock (lines: string array) : Result<MolRecord * int, string> =
            try
                if lines.Length < 4 then
                    Error "MOL block too short (need at least 4 lines for header)"
                else
                    // Line 1: Molecule name (can be empty)
                    let name = if lines.Length > 0 then lines.[0].Trim() else ""
                    
                    // Line 2: Program/timestamp (ignored)
                    // Line 3: Comment (can be empty)
                    let comment = if lines.Length > 2 then lines.[2].Trim() else ""
                    
                    // Line 4: Counts line - "aaabbblllfff...V2000"
                    let countsLine = lines.[3]
                    if countsLine.Length < 6 then
                        Error $"Counts line too short: '{countsLine}'"
                    else
                        // Parse atom and bond counts (first 6 characters, 3 each)
                        let atomCountStr = countsLine.Substring(0, 3).Trim()
                        let bondCountStr = countsLine.Substring(3, 3).Trim()
                        
                        match Int32.TryParse atomCountStr, Int32.TryParse bondCountStr with
                        | (false, _), _ -> Error $"Cannot parse atom count: '{atomCountStr}'"
                        | _, (false, _) -> Error $"Cannot parse bond count: '{bondCountStr}'"
                        | (true, atomCount), (true, bondCount) ->
                            
                            let atomStartLine = 4
                            let bondStartLine = atomStartLine + atomCount
                            let endLine = bondStartLine + bondCount
                            
                            if lines.Length < endLine then
                                Error $"MOL block too short: need {endLine} lines, got {lines.Length}"
                            else
                                // Parse atoms
                                let atoms = 
                                    [| for i in 0 .. atomCount - 1 do
                                        let line = lines.[atomStartLine + i]
                                        // Format: xxxxx.xxxxyyyyy.yyyyzzzzz.zzzz aaaddcccssshhhbbbvvvHHHrrriiimmmnnneee
                                        // x,y,z: 10.4 format each (but often space-separated)
                                        // aaa: atom symbol (3 chars)
                                        // dd: mass difference
                                        // ccc: charge (0=uncharged, 1=+3, 2=+2, 3=+1, 4=doublet radical, 5=-1, 6=-2, 7=-3)
                                        
                                        let parts = line.Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
                                        if parts.Length >= 4 then
                                            let x = Double.Parse(parts.[0], CultureInfo.InvariantCulture)
                                            let y = Double.Parse(parts.[1], CultureInfo.InvariantCulture)
                                            let z = Double.Parse(parts.[2], CultureInfo.InvariantCulture)
                                            let symbol = parts.[3].Trim()
                                            
                                            // Parse optional fields
                                            let massDiff = if parts.Length > 4 then Int32.Parse parts.[4] else 0
                                            let chargeCode = if parts.Length > 5 then Int32.Parse parts.[5] else 0
                                            
                                            // Convert charge code to actual charge
                                            let charge = 
                                                match chargeCode with
                                                | 0 -> 0
                                                | 1 -> 3
                                                | 2 -> 2
                                                | 3 -> 1
                                                | 4 -> 0  // doublet radical
                                                | 5 -> -1
                                                | 6 -> -2
                                                | 7 -> -3
                                                | _ -> 0
                                            
                                            { X = x; Y = y; Z = z
                                              Symbol = symbol
                                              MassDifference = massDiff
                                              Charge = charge
                                              StereoParity = 0 }
                                        else
                                            // Try fixed-width parsing as fallback
                                            let x = Double.Parse(line.Substring(0, 10).Trim(), CultureInfo.InvariantCulture)
                                            let y = Double.Parse(line.Substring(10, 10).Trim(), CultureInfo.InvariantCulture)
                                            let z = Double.Parse(line.Substring(20, 10).Trim(), CultureInfo.InvariantCulture)
                                            let symbol = line.Substring(31, 3).Trim()
                                            
                                            { X = x; Y = y; Z = z
                                              Symbol = symbol
                                              MassDifference = 0
                                              Charge = 0
                                              StereoParity = 0 }
                                    |]
                                
                                // Parse bonds
                                let bonds =
                                    [| for i in 0 .. bondCount - 1 do
                                        let line = lines.[bondStartLine + i]
                                        // Format: 111222tttsssxxxrrrccc
                                        // 111: first atom (3 chars)
                                        // 222: second atom (3 chars)
                                        // ttt: bond type
                                        // sss: stereo
                                        
                                        let parts = line.Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
                                        if parts.Length >= 3 then
                                            let atom1 = Int32.Parse parts.[0]
                                            let atom2 = Int32.Parse parts.[1]
                                            let bondType = Int32.Parse parts.[2]
                                            let stereo = if parts.Length > 3 then Int32.Parse parts.[3] else 0
                                            
                                            { Atom1 = atom1
                                              Atom2 = atom2
                                              BondType = bondType
                                              Stereo = stereo }
                                        else
                                            // Fixed-width fallback
                                            let atom1 = Int32.Parse(line.Substring(0, 3).Trim())
                                            let atom2 = Int32.Parse(line.Substring(3, 3).Trim())
                                            let bondType = Int32.Parse(line.Substring(6, 3).Trim())
                                            
                                            { Atom1 = atom1
                                              Atom2 = atom2
                                              BondType = bondType
                                              Stereo = 0 }
                                    |]
                                
                                // Parse properties block (M  CHG, etc.) and find M  END
                                let mutable charges = []
                                let mutable currentLine = endLine
                                let mutable foundEnd = false
                                
                                while currentLine < lines.Length && not foundEnd do
                                    let line = lines.[currentLine]
                                    if line.StartsWith("M  END") then
                                        foundEnd <- true
                                    elif line.StartsWith("M  CHG") then
                                        // Format: M  CHG  n  aaa vvv  aaa vvv ...
                                        let parts = line.Substring(6).Trim().Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
                                        if parts.Length >= 3 then
                                            let count = Int32.Parse parts.[0]
                                            for i in 0 .. count - 1 do
                                                if parts.Length > 1 + i * 2 + 1 then
                                                    let atomIdx = Int32.Parse parts.[1 + i * 2]
                                                    let charge = Int32.Parse parts.[2 + i * 2]
                                                    charges <- (atomIdx, charge) :: charges
                                    currentLine <- currentLine + 1
                                
                                let linesConsumed = if foundEnd then currentLine else endLine
                                
                                Ok ({ Name = name
                                      Comment = comment
                                      Atoms = atoms
                                      Bonds = bonds
                                      Charges = charges |> List.rev
                                      Properties = Map.empty }, linesConsumed)
            with
            | ex -> Error $"Failed to parse MOL block: {ex.Message}"

        /// Parse associated data fields from SDF format
        let parseDataFields (lines: string array) (startLine: int) : Map<string, string> * int =
            let mutable properties = Map.empty
            let mutable currentLine = startLine
            let mutable currentField = None
            let mutable currentValue = System.Text.StringBuilder()
            
            while currentLine < lines.Length && not (lines.[currentLine].StartsWith("$$$$")) do
                let line = lines.[currentLine]
                
                if line.StartsWith("> ") || line.StartsWith(">  ") then
                    // Save previous field if exists
                    match currentField with
                    | Some fieldName ->
                        properties <- properties.Add(fieldName, currentValue.ToString().Trim())
                    | None -> ()
                    
                    // Parse new field name: >  <FieldName>
                    let fieldMatch = Regex.Match(line, @">\s*<([^>]+)>")
                    if fieldMatch.Success then
                        currentField <- Some fieldMatch.Groups.[1].Value
                        currentValue <- System.Text.StringBuilder()
                    else
                        currentField <- None
                elif currentField.IsSome && line.Trim().Length > 0 then
                    if currentValue.Length > 0 then
                        currentValue.AppendLine() |> ignore
                    currentValue.Append(line.Trim()) |> ignore
                
                currentLine <- currentLine + 1
            
            // Save last field
            match currentField with
            | Some fieldName ->
                properties <- properties.Add(fieldName, currentValue.ToString().Trim())
            | None -> ()
            
            // Skip the $$$$ delimiter if present
            if currentLine < lines.Length && lines.[currentLine].StartsWith("$$$$") then
                currentLine <- currentLine + 1
            
            (properties, currentLine)

        /// Parse a complete SDF file (multiple MOL records with data)
        let parseSdfFile (content: string) : Result<MolRecord array, string> =
            let lines = content.Replace("\r\n", "\n").Split('\n')
            let mutable records = []
            let mutable currentLine = 0
            let mutable errors = []
            
            while currentLine < lines.Length do
                // Skip empty lines
                while currentLine < lines.Length && lines.[currentLine].Trim().Length = 0 do
                    currentLine <- currentLine + 1
                
                if currentLine < lines.Length then
                    // Try to parse a MOL block
                    let remainingLines = lines.[currentLine..]
                    match parseMolBlock remainingLines with
                    | Ok (record, linesConsumed) ->
                        let nextLine = currentLine + linesConsumed
                        
                        // Parse associated data fields
                        let (properties, finalLine) = parseDataFields lines nextLine
                        let recordWithProps = { record with Properties = properties }
                        
                        records <- recordWithProps :: records
                        currentLine <- finalLine
                    | Error e ->
                        // Skip to next $$$$ or end
                        errors <- e :: errors
                        while currentLine < lines.Length && not (lines.[currentLine].StartsWith("$$$$")) do
                            currentLine <- currentLine + 1
                        if currentLine < lines.Length then
                            currentLine <- currentLine + 1
            
            if records.IsEmpty && not errors.IsEmpty then
                Error (String.concat "; " errors)
            else
                Ok (records |> List.rev |> List.toArray)

        /// Parse a single MOL file
        let parseMolFile (content: string) : Result<MolRecord, string> =
            match parseSdfFile content with
            | Ok records when records.Length > 0 -> Ok records.[0]
            | Ok _ -> Error "No molecule found in MOL file"
            | Error e -> Error e

        /// Convert MolRecord to MoleculeInstance
        let toMoleculeInstance (record: MolRecord) : MoleculeInstance =
            // Apply M  CHG charges to atoms
            let chargeMap = record.Charges |> List.map (fun (idx, chg) -> (idx, chg)) |> Map.ofList
            
            let topology: MoleculeTopology =
                { Atoms = record.Atoms |> Array.map (fun a -> a.Symbol)
                  Bonds = 
                    record.Bonds 
                    |> Array.map (fun b -> 
                        // Convert 1-indexed to 0-indexed
                        let bondOrder = float b.BondType
                        (b.Atom1 - 1, b.Atom2 - 1, Some bondOrder))
                  Charge = 
                    // Sum of inline charges + M  CHG charges
                    let inlineCharge = record.Atoms |> Array.sumBy (fun a -> a.Charge)
                    let mChgCharge = record.Charges |> List.sumBy snd
                    Some (inlineCharge + mChgCharge)
                  Multiplicity = Some 1  // Not specified in MOL format
                  Metadata = 
                    record.Properties
                    |> Map.toList
                    |> List.map (fun (k, v) -> (k, v))
                    |> Map.ofList }
            
            let geometry: MoleculeGeometry =
                { Coordinates = 
                    record.Atoms 
                    |> Array.map (fun a -> { X = a.X; Y = a.Y; Z = a.Z })
                  Units = "angstrom" }
            
            let name = 
                if String.IsNullOrWhiteSpace record.Name then
                    record.Properties.TryFind "Name" 
                    |> Option.orElse (record.Properties.TryFind "PUBCHEM_IUPAC_NAME")
                    |> Option.orElse (record.Properties.TryFind "PUBCHEM_COMPOUND_CID" |> Option.map (fun cid -> $"CID_{cid}"))
                else
                    Some record.Name
            
            { Id = name
              Name = name
              Topology = topology
              Geometry = Some geometry }

    /// Dataset provider that loads molecules from SDF files.
    /// 
    /// SDF (Structure-Data File) is a common format for molecular structures,
    /// especially for datasets from PubChem, ChEMBL, and other databases.
    /// 
    /// Example:
    ///   let provider = SdfFileDatasetProvider("molecules.sdf")
    ///   match provider.Load All with
    ///   | Ok dataset -> printfn "Loaded %d molecules" dataset.Molecules.Length
    ///   | Error e -> printfn "Error: %A" e
    type SdfFileDatasetProvider(filePath: string) =

        let loadDataset () : QuantumResult<MoleculeDataset> =
            try
                if not (File.Exists filePath) then
                    Error (QuantumError.ValidationError("FilePath", $"File not found: {filePath}"))
                else
                    let content = File.ReadAllText(filePath)
                    match SdfMolParser.parseSdfFile content with
                    | Ok records ->
                        let molecules = 
                            records 
                            |> Array.map SdfMolParser.toMoleculeInstance
                        Ok { Molecules = molecules
                             Labels = None
                             LabelColumn = None
                             Metadata = Map.ofList ["source", filePath; "format", "sdf"] }
                    | Error e ->
                        Error (QuantumError.ValidationError("SdfParsing", e))
            with
            | ex -> Error (QuantumError.OperationError("SdfLoading", $"Failed to load SDF file: {ex.Message}"))

        interface IMoleculeDatasetProvider with
            member _.Describe() =
                $"SDF file provider (file: {filePath})"

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
                            Error (QuantumError.ValidationError("MoleculeNotFound", $"No molecule named '{name}' found in SDF file")))
                | ByPath _ ->
                    Error (QuantumError.ValidationError("UnsupportedQuery", "SdfFileDatasetProvider does not support path queries"))
                | ByCategory _ ->
                    Error (QuantumError.ValidationError("UnsupportedQuery", "SdfFileDatasetProvider does not support category queries"))

            member _.ListNames() =
                match loadDataset () with
                | Ok ds -> 
                    ds.Molecules 
                    |> Array.choose (fun m -> m.Name)
                    |> Array.toList
                | Error _ -> []

    /// Dataset provider that loads molecules from a directory of MOL/SDF files.
    /// 
    /// Example:
    ///   let provider = MolDirectoryDatasetProvider("/path/to/molecules")
    ///   match provider.Load (ByName "aspirin") with
    ///   | Ok dataset -> printfn "Found aspirin"
    ///   | Error e -> printfn "Not found"
    type MolDirectoryDatasetProvider(directoryPath: string, ?searchPattern: string) =
        let pattern = defaultArg searchPattern "*.mol"

        let loadMolFile (path: string) : MoleculeInstance option =
            try
                let content = File.ReadAllText(path)
                match SdfMolParser.parseMolFile content with
                | Ok record -> Some (SdfMolParser.toMoleculeInstance record)
                | Error _ -> None
            with
            | _ -> None

        interface IMoleculeDatasetProvider with
            member _.Describe() =
                $"MOL directory provider (dir: {directoryPath}, pattern: {pattern})"

            member _.Load(query: DatasetQuery) =
                if not (Directory.Exists directoryPath) then
                    Error (QuantumError.ValidationError("DirectoryPath", $"Directory not found: {directoryPath}"))
                else
                    match query with
                    | All ->
                        let files = Directory.GetFiles(directoryPath, pattern)
                        let sdfFiles = Directory.GetFiles(directoryPath, "*.sdf")
                        
                        let molMolecules = 
                            files |> Array.choose loadMolFile
                        
                        let sdfMolecules =
                            sdfFiles
                            |> Array.collect (fun path ->
                                let content = File.ReadAllText(path)
                                match SdfMolParser.parseSdfFile content with
                                | Ok records -> records |> Array.map SdfMolParser.toMoleculeInstance
                                | Error _ -> [||])
                        
                        let allMolecules = Array.append molMolecules sdfMolecules
                        
                        if allMolecules.Length = 0 then
                            Error (QuantumError.ValidationError("NoMolecules", "No valid MOL/SDF files found in directory"))
                        else
                            Ok { Molecules = allMolecules
                                 Labels = None
                                 LabelColumn = None
                                 Metadata = Map.ofList ["source", directoryPath; "format", "mol_directory"] }
                    
                    | ByName name ->
                        // Try to find file by name
                        let molPath = Path.Combine(directoryPath, $"{name}.mol")
                        let sdfPath = Path.Combine(directoryPath, $"{name}.sdf")
                        
                        if File.Exists molPath then
                            match loadMolFile molPath with
                            | Some mol -> 
                                Ok { Molecules = [| mol |]
                                     Labels = None
                                     LabelColumn = None
                                     Metadata = Map.ofList ["source", molPath; "format", "mol"] }
                            | None ->
                                Error (QuantumError.ValidationError("ParseError", $"Failed to parse {molPath}"))
                        elif File.Exists sdfPath then
                            let content = File.ReadAllText(sdfPath)
                            match SdfMolParser.parseSdfFile content with
                            | Ok records when records.Length > 0 ->
                                Ok { Molecules = records |> Array.map SdfMolParser.toMoleculeInstance
                                     Labels = None
                                     LabelColumn = None
                                     Metadata = Map.ofList ["source", sdfPath; "format", "sdf"] }
                            | Ok _ ->
                                Error (QuantumError.ValidationError("EmptyFile", $"No molecules in {sdfPath}"))
                            | Error e ->
                                Error (QuantumError.ValidationError("ParseError", e))
                        else
                            Error (QuantumError.ValidationError("MoleculeNotFound", $"No file named '{name}.mol' or '{name}.sdf' found"))
                    
                    | ByPath subPath ->
                        let fullPath = Path.Combine(directoryPath, subPath)
                        if File.Exists fullPath then
                            match loadMolFile fullPath with
                            | Some mol ->
                                Ok { Molecules = [| mol |]
                                     Labels = None
                                     LabelColumn = None
                                     Metadata = Map.ofList ["source", fullPath; "format", "mol"] }
                            | None ->
                                Error (QuantumError.ValidationError("ParseError", $"Failed to parse {fullPath}"))
                        else
                            Error (QuantumError.ValidationError("FileNotFound", $"File not found: {fullPath}"))
                    
                    | ByCategory _ ->
                        Error (QuantumError.ValidationError("UnsupportedQuery", "MolDirectoryDatasetProvider does not support category queries"))

            member _.ListNames() =
                if Directory.Exists directoryPath then
                    let molFiles = Directory.GetFiles(directoryPath, pattern)
                    let sdfFiles = Directory.GetFiles(directoryPath, "*.sdf")
                    
                    let molNames = molFiles |> Array.map (fun p -> Path.GetFileNameWithoutExtension p)
                    let sdfNames = sdfFiles |> Array.map (fun p -> Path.GetFileNameWithoutExtension p)
                    
                    Array.append molNames sdfNames |> Array.distinct |> Array.toList
                else
                    []

    // ========================================================================
    // FCIDUMP PARSER MODULE
    // ========================================================================

    /// Parser for FCIDump (Full Configuration Interaction Dump) files.
    /// 
    /// FCIDump is a standard format for molecular orbital integrals used by
    /// quantum chemistry programs. It contains:
    /// - Header: NORB (orbitals), NELEC (electrons), MS2 (2*spin)
    /// - Body: one/two-electron integrals (not parsed here)
    /// 
    /// This parser extracts header metadata only. Full integral parsing
    /// requires specialized quantum chemistry software.
    /// 
    /// NOTE: FCIDump doesn't contain molecular geometry, so MoleculeInstance
    /// created from FCIDump will have Geometry = None and placeholder atoms.
    module FciDumpParser =
        open System.Text.RegularExpressions

        /// Parsed FCIDump header information.
        type FciDumpHeader =
            { /// Number of molecular orbitals
              NumOrbitals: int
              /// Number of electrons
              NumElectrons: int
              /// 2 * total spin (MS2); multiplicity = MS2 + 1
              MS2: int option
              /// Orbital symmetries (if present)
              OrbSym: int array option
              /// Number of irreducible representations (if present)
              NumIrrep: int option
              /// Raw header line for reference
              RawHeader: string }

        /// Parse FCIDump header from file content.
        let parseHeader (content: string) : Result<FciDumpHeader, string> =
            let lines = content.Replace("\r\n", "\n").Split('\n')
            
            // Find header start line (&FCI or $FCI)
            let headerStartIdx = 
                lines 
                |> Array.tryFindIndex (fun line -> 
                    let trimmed = line.Trim().ToUpperInvariant()
                    trimmed.StartsWith("&FCI") || trimmed.StartsWith("$FCI"))
            
            match headerStartIdx with
            | None -> Error "No FCIDump header found (&FCI line)"
            | Some startIdx ->
                // Collect all header lines until we find '/' or '&END' or '$END'
                let headerLines = 
                    lines.[startIdx..]
                    |> Array.takeWhile (fun line ->
                        let trimmed = line.Trim()
                        // Stop at end of header markers, but include lines containing the marker
                        not (trimmed = "/" || trimmed = "&END" || trimmed = "$END" || 
                             trimmed = "&" || trimmed = "$"))
                    |> Array.toList
                
                // Join all header lines into one string for regex matching
                let header = String.concat " " headerLines
                
                // Extract integer parameter from header
                let extractInt name =
                    let pattern = $"{name}\\s*=\\s*(\\d+)"
                    let m = Regex.Match(header, pattern, RegexOptions.IgnoreCase)
                    if m.Success then Some (int m.Groups.[1].Value) else None
                
                // Extract array parameter (e.g., ORBSYM=1,1,1,...)
                let extractIntArray name =
                    let pattern = $"{name}\\s*=\\s*([\\d,\\s]+)"
                    let m = Regex.Match(header, pattern, RegexOptions.IgnoreCase)
                    if m.Success then
                        let values = 
                            m.Groups.[1].Value.Split([|','; ' '|], StringSplitOptions.RemoveEmptyEntries)
                            |> Array.choose (fun s -> 
                                match Int32.TryParse(s.Trim()) with
                                | true, v -> Some v
                                | false, _ -> None)
                        if values.Length > 0 then Some values else None
                    else
                        None
                
                match extractInt "NORB", extractInt "NELEC" with
                | None, _ -> Error "NORB (number of orbitals) not found in FCIDump header"
                | _, None -> Error "NELEC (number of electrons) not found in FCIDump header"
                | Some norb, Some nelec ->
                    Ok { NumOrbitals = norb
                         NumElectrons = nelec
                         MS2 = extractInt "MS2"
                         OrbSym = extractIntArray "ORBSYM"
                         NumIrrep = extractInt "NIRREP"
                         RawHeader = header }

        /// Parse FCIDump file and return header information.
        let parseFile (filePath: string) : Result<FciDumpHeader, string> =
            try
                if not (File.Exists filePath) then
                    Error $"File not found: {filePath}"
                else
                    let content = File.ReadAllText(filePath)
                    parseHeader content
            with
            | ex -> Error $"Failed to read FCIDump file: {ex.Message}"

        /// Convert FCIDump header to MoleculeInstance.
        /// 
        /// NOTE: FCIDump doesn't contain geometry, so we create placeholder
        /// atoms based on electron count. This is useful for tracking
        /// electronic structure information but NOT for geometric calculations.
        let toMoleculeInstance (header: FciDumpHeader) (sourcePath: string option) : MoleculeInstance =
            let multiplicity = match header.MS2 with | Some ms2 -> ms2 + 1 | None -> 1
            
            // Create placeholder topology (no real atoms, just electron count)
            let topology: MoleculeTopology =
                { Atoms = 
                    // Use "X" as placeholder for unknown atoms
                    // One "atom" per pair of electrons (very rough approximation)
                    Array.init (max 1 (header.NumElectrons / 2)) (fun _ -> "X")
                  Bonds = [||]  // No bonds known
                  Charge = Some (header.NumOrbitals - header.NumElectrons)  // Rough estimate
                  Multiplicity = Some multiplicity
                  Metadata = 
                    [ "format", "fcidump"
                      "norb", string header.NumOrbitals
                      "nelec", string header.NumElectrons
                      yield! match header.MS2 with | Some ms2 -> ["ms2", string ms2] | None -> []
                      yield! match header.NumIrrep with | Some n -> ["nirrep", string n] | None -> []
                      yield! match sourcePath with | Some p -> ["source", p] | None -> [] ]
                    |> Map.ofList }
            
            { Id = sourcePath |> Option.map Path.GetFileNameWithoutExtension
              Name = sourcePath |> Option.map (fun p -> $"FCIDump: {Path.GetFileName p}")
              Topology = topology
              Geometry = None  // FCIDump doesn't contain geometry!
            }

    /// Dataset provider for FCIDump files.
    /// 
    /// FCIDump (Full Configuration Interaction Dump) is a format for molecular
    /// orbital integrals used by quantum chemistry programs like PySCF, Psi4, etc.
    /// 
    /// IMPORTANT: FCIDump files do NOT contain molecular geometry. The provider
    /// extracts electronic structure metadata (orbitals, electrons, spin) but
    /// the resulting MoleculeInstance will have `Geometry = None`.
    /// 
    /// For quantum chemistry workflows, FCIDump is typically used AFTER
    /// geometry optimization, providing pre-computed integrals for correlation
    /// energy calculations.
    /// 
    /// Example:
    ///   let provider = FciDumpFileDatasetProvider("h2o.fcidump")
    ///   match provider.Load All with
    ///   | Ok dataset ->
    ///       let mol = dataset.Molecules.[0]
    ///       let norb = mol.Topology.Metadata.["norb"]
    ///       printfn "FCIDump has %s orbitals" norb
    ///   | Error e -> printfn "Error: %A" e
    type FciDumpFileDatasetProvider(filePath: string) =

        let loadDataset () : QuantumResult<MoleculeDataset> =
            match FciDumpParser.parseFile filePath with
            | Ok header ->
                let molecule = FciDumpParser.toMoleculeInstance header (Some filePath)
                Ok { Molecules = [| molecule |]
                     Labels = None
                     LabelColumn = None
                     Metadata = Map.ofList ["source", filePath; "format", "fcidump"] }
            | Error msg ->
                Error (QuantumError.ValidationError("FciDumpParsing", msg))

        interface IMoleculeDatasetProvider with
            member _.Describe() =
                $"FCIDump file provider (file: {filePath})"

            member _.Load(query: DatasetQuery) =
                match query with
                | All -> loadDataset ()
                | ByName _ -> loadDataset ()  // Only one molecule per FCIDump
                | ByPath _ ->
                    Error (QuantumError.ValidationError("UnsupportedQuery", "FciDumpFileDatasetProvider does not support path queries"))
                | ByCategory _ ->
                    Error (QuantumError.ValidationError("UnsupportedQuery", "FciDumpFileDatasetProvider does not support category queries"))

            member _.ListNames() =
                match loadDataset () with
                | Ok ds -> 
                    ds.Molecules 
                    |> Array.choose (fun m -> m.Name)
                    |> Array.toList
                | Error _ -> []

    /// Dataset provider that loads FCIDump files from a directory.
    /// 
    /// Example:
    ///   let provider = FciDumpDirectoryDatasetProvider("/path/to/fcidumps")
    ///   match provider.Load All with
    ///   | Ok dataset -> printfn "Found %d FCIDump files" dataset.Molecules.Length
    ///   | Error e -> printfn "Error: %A" e
    type FciDumpDirectoryDatasetProvider(directoryPath: string, ?searchPattern: string) =
        let pattern = defaultArg searchPattern "*.fcidump"

        let loadFciDump (path: string) : MoleculeInstance option =
            match FciDumpParser.parseFile path with
            | Ok header -> Some (FciDumpParser.toMoleculeInstance header (Some path))
            | Error _ -> None

        interface IMoleculeDatasetProvider with
            member _.Describe() =
                $"FCIDump directory provider (dir: {directoryPath}, pattern: {pattern})"

            member _.Load(query: DatasetQuery) =
                if not (Directory.Exists directoryPath) then
                    Error (QuantumError.ValidationError("DirectoryPath", $"Directory not found: {directoryPath}"))
                else
                    match query with
                    | All ->
                        let files = Directory.GetFiles(directoryPath, pattern)
                        let molecules = files |> Array.choose loadFciDump
                        
                        if molecules.Length = 0 then
                            Error (QuantumError.ValidationError("NoFciDumps", "No valid FCIDump files found in directory"))
                        else
                            Ok { Molecules = molecules
                                 Labels = None
                                 LabelColumn = None
                                 Metadata = Map.ofList ["source", directoryPath; "format", "fcidump_directory"] }
                    
                    | ByName name ->
                        let fcidumpPath = Path.Combine(directoryPath, $"{name}.fcidump")
                        let altPath = Path.Combine(directoryPath, name)  // Try without extension
                        
                        let tryPath path =
                            if File.Exists path then
                                match loadFciDump path with
                                | Some mol -> 
                                    Some (Ok { Molecules = [| mol |]
                                               Labels = None
                                               LabelColumn = None
                                               Metadata = Map.ofList ["source", path; "format", "fcidump"] })
                                | None ->
                                    Some (Error (QuantumError.ValidationError("ParseError", $"Failed to parse {path}")))
                            else
                                None
                        
                        match tryPath fcidumpPath with
                        | Some result -> result
                        | None ->
                            match tryPath altPath with
                            | Some result -> result
                            | None ->
                                Error (QuantumError.ValidationError("FileNotFound", $"No FCIDump file named '{name}' found"))
                    
                    | ByPath subPath ->
                        let fullPath = Path.Combine(directoryPath, subPath)
                        if File.Exists fullPath then
                            match loadFciDump fullPath with
                            | Some mol ->
                                Ok { Molecules = [| mol |]
                                     Labels = None
                                     LabelColumn = None
                                     Metadata = Map.ofList ["source", fullPath; "format", "fcidump"] }
                            | None ->
                                Error (QuantumError.ValidationError("ParseError", $"Failed to parse {fullPath}"))
                        else
                            Error (QuantumError.ValidationError("FileNotFound", $"File not found: {fullPath}"))
                    
                    | ByCategory _ ->
                        Error (QuantumError.ValidationError("UnsupportedQuery", "FciDumpDirectoryDatasetProvider does not support category queries"))

            member _.ListNames() =
                if Directory.Exists directoryPath then
                    Directory.GetFiles(directoryPath, pattern)
                    |> Array.map Path.GetFileNameWithoutExtension
                    |> Array.toList
                else
                    []

    // ========================================================================
    // PDB PARSER MODULE
    // ========================================================================

    /// Parser for PDB (Protein Data Bank) format files.
    /// 
    /// PDB is the standard format for macromolecular structures. This parser
    /// focuses on extracting small molecule ligands (HETATM records) which are
    /// relevant for quantum chemistry calculations.
    /// 
    /// Key features:
    /// - Extracts HETATM records (ligands, cofactors, small molecules)
    /// - Optionally extracts ATOM records (protein/nucleic acid)
    /// - Groups atoms by residue name and chain
    /// - Supports multiple models (NMR ensembles)
    /// 
    /// PDB Format Reference: https://www.wwpdb.org/documentation/file-format
    module PdbParser =
        
        /// A single atom from a PDB file.
        type PdbAtom =
            { /// Atom serial number (columns 7-11)
              Serial: int
              /// Atom name (columns 13-16)
              Name: string
              /// Alternate location indicator (column 17)
              AltLoc: char option
              /// Residue name (columns 18-20)
              ResName: string
              /// Chain identifier (column 22)
              ChainId: char option
              /// Residue sequence number (columns 23-26)
              ResSeq: int
              /// Insertion code (column 27)
              ICode: char option
              /// X coordinate in Angstroms (columns 31-38)
              X: float
              /// Y coordinate in Angstroms (columns 39-46)
              Y: float
              /// Z coordinate in Angstroms (columns 47-54)
              Z: float
              /// Occupancy (columns 55-60)
              Occupancy: float option
              /// Temperature factor (columns 61-66)
              TempFactor: float option
              /// Element symbol (columns 77-78)
              Element: string
              /// Charge (columns 79-80)
              Charge: string option
              /// True if HETATM, false if ATOM
              IsHetAtom: bool }

        /// A group of atoms forming a residue/molecule.
        type PdbResidue =
            { /// Residue name (3-letter code)
              Name: string
              /// Chain identifier
              ChainId: char option
              /// Residue sequence number
              ResSeq: int
              /// Atoms in this residue
              Atoms: PdbAtom array
              /// True if this is a heteroatom residue (ligand)
              IsHet: bool }

        /// Parsed PDB structure.
        type PdbStructure =
            { /// PDB ID (from HEADER or filename)
              PdbId: string option
              /// Title (from TITLE record)
              Title: string option
              /// All residues in the structure
              Residues: PdbResidue array
              /// Model number (for NMR ensembles)
              ModelNumber: int option
              /// Source file path
              SourcePath: string option }

        /// Parse a single ATOM or HETATM line.
        let private parseAtomLine (line: string) (isHetatm: bool) : PdbAtom option =
            if line.Length < 54 then None
            else
                try
                    // PDB format is fixed-column, 1-indexed in documentation
                    // We use 0-indexed substrings
                    let serial = line.[6..10].Trim() |> int
                    let name = line.[12..15].Trim()
                    let altLoc = 
                        let c = line.[16]
                        if c = ' ' then None else Some c
                    let resName = line.[17..19].Trim()
                    let chainId = 
                        let c = line.[21]
                        if c = ' ' then None else Some c
                    let resSeq = line.[22..25].Trim() |> int
                    let iCode = 
                        if line.Length > 26 then
                            let c = line.[26]
                            if c = ' ' then None else Some c
                        else None
                    let x = line.[30..37].Trim() |> Double.Parse
                    let y = line.[38..45].Trim() |> Double.Parse
                    let z = line.[46..53].Trim() |> Double.Parse
                    
                    let occupancy = 
                        if line.Length >= 60 then
                            match Double.TryParse(line.[54..59].Trim()) with
                            | true, v -> Some v
                            | _ -> None
                        else None
                    
                    let tempFactor =
                        if line.Length >= 66 then
                            match Double.TryParse(line.[60..65].Trim()) with
                            | true, v -> Some v
                            | _ -> None
                        else None
                    
                    let element =
                        if line.Length >= 78 then
                            line.[76..77].Trim()
                        else
                            // Infer from atom name
                            name.TrimStart([|'0'..'9'|]).[0..0]
                    
                    let charge =
                        if line.Length >= 80 then
                            let ch = line.[78..79].Trim()
                            if String.IsNullOrEmpty ch then None else Some ch
                        else None
                    
                    Some {
                        Serial = serial
                        Name = name
                        AltLoc = altLoc
                        ResName = resName
                        ChainId = chainId
                        ResSeq = resSeq
                        ICode = iCode
                        X = x
                        Y = y
                        Z = z
                        Occupancy = occupancy
                        TempFactor = tempFactor
                        Element = element
                        Charge = charge
                        IsHetAtom = isHetatm
                    }
                with
                | _ -> None

        /// Options for parsing PDB files.
        type PdbParseOptions =
            { /// Include ATOM records (protein/nucleic acid atoms)
              IncludeAtom: bool
              /// Include HETATM records (ligands, water, etc.)
              IncludeHetatm: bool
              /// Exclude water molecules (HOH, WAT, H2O)
              ExcludeWater: bool
              /// Only include specific residue names (empty = all)
              ResidueFilter: string list
              /// Only include first model (for NMR ensembles)
              FirstModelOnly: bool }

        /// Default parse options: HETATM only, no water.
        let defaultOptions =
            { IncludeAtom = false
              IncludeHetatm = true
              ExcludeWater = true
              ResidueFilter = []
              FirstModelOnly = true }

        /// Parse PDB content with options.
        let parseWithOptions (options: PdbParseOptions) (content: string) : Result<PdbStructure, string> =
            let lines = content.Replace("\r\n", "\n").Split('\n')
            let mutable atoms: PdbAtom list = []
            let mutable pdbId = None
            let mutable title = None
            let mutable inModel = false
            let mutable modelNumber = None
            let mutable firstModelEnded = false
            
            let waterNames = set ["HOH"; "WAT"; "H2O"; "DOD"; "DIS"]
            
            for line in lines do
                if line.Length >= 6 then
                    let recordType = line.[0..5].TrimEnd()
                    
                    match recordType with
                    | "HEADER" when line.Length >= 66 ->
                        pdbId <- Some (line.[62..65].Trim())
                    | "TITLE" when line.Length > 10 ->
                        let titlePart = line.[10..].Trim()
                        title <- match title with
                                 | Some t -> Some (t + " " + titlePart)
                                 | None -> Some titlePart
                    | "MODEL" ->
                        if not inModel then
                            inModel <- true
                            modelNumber <- 
                                if line.Length >= 14 then
                                    match Int32.TryParse(line.[10..13].Trim()) with
                                    | true, n -> Some n
                                    | _ -> Some 1
                                else Some 1
                    | "ENDMDL" ->
                        inModel <- false
                        if options.FirstModelOnly then
                            firstModelEnded <- true
                    | "ATOM" when options.IncludeAtom && not firstModelEnded ->
                        match parseAtomLine line false with
                        | Some atom -> atoms <- atom :: atoms
                        | None -> ()
                    | "HETATM" when options.IncludeHetatm && not firstModelEnded ->
                        match parseAtomLine line true with
                        | Some atom ->
                            let shouldInclude =
                                // Check water exclusion
                                (not options.ExcludeWater || not (waterNames.Contains atom.ResName)) &&
                                // Check residue filter
                                (options.ResidueFilter.IsEmpty || 
                                 options.ResidueFilter |> List.exists (fun r -> 
                                     r.Equals(atom.ResName, StringComparison.OrdinalIgnoreCase)))
                            if shouldInclude then
                                atoms <- atom :: atoms
                        | None -> ()
                    | _ -> ()
            
            // Group atoms into residues
            let residues =
                atoms
                |> List.rev
                |> List.groupBy (fun a -> (a.ResName, a.ChainId, a.ResSeq))
                |> List.map (fun ((resName, chainId, resSeq), resAtoms) ->
                    { Name = resName
                      ChainId = chainId
                      ResSeq = resSeq
                      Atoms = resAtoms |> List.toArray
                      IsHet = resAtoms |> List.exists (fun a -> a.IsHetAtom) })
                |> List.toArray
            
            Ok {
                PdbId = pdbId
                Title = title
                Residues = residues
                ModelNumber = modelNumber
                SourcePath = None
            }

        /// Parse PDB content with default options (HETATM only, no water).
        let parse (content: string) : Result<PdbStructure, string> =
            parseWithOptions defaultOptions content

        /// Parse PDB file.
        let parseFile (filePath: string) : Result<PdbStructure, string> =
            try
                if not (File.Exists filePath) then
                    Error $"File not found: {filePath}"
                else
                    let content = File.ReadAllText(filePath)
                    match parse content with
                    | Ok structure -> 
                        Ok { structure with SourcePath = Some filePath }
                    | Error e -> Error e
            with
            | ex -> Error $"Failed to read PDB file: {ex.Message}"

        /// Convert a PDB residue to MoleculeInstance.
        let residueToMoleculeInstance (residue: PdbResidue) (pdbId: string option) : MoleculeInstance =
            let topology: MoleculeTopology =
                { Atoms = residue.Atoms |> Array.map (fun a -> a.Element)
                  Bonds = [||]  // PDB doesn't include bond information for HETATM
                  Charge = None  // Would need to sum individual charges
                  Multiplicity = Some 1
                  Metadata = 
                    [ "format", "pdb"
                      "residue_name", residue.Name
                      "residue_seq", string residue.ResSeq
                      yield! match residue.ChainId with | Some c -> ["chain_id", string c] | None -> []
                      yield! match pdbId with | Some id -> ["pdb_id", id] | None -> [] ]
                    |> Map.ofList }
            
            let geometry: MoleculeGeometry =
                { Coordinates = 
                    residue.Atoms 
                    |> Array.map (fun a -> { X = a.X; Y = a.Y; Z = a.Z })
                  Units = "angstrom" }
            
            let name = 
                match pdbId with
                | Some id -> $"{id}_{residue.Name}_{residue.ResSeq}"
                | None -> $"{residue.Name}_{residue.ResSeq}"
            
            { Id = Some name
              Name = Some name
              Topology = topology
              Geometry = Some geometry }

        /// Convert all non-water HETATM residues to MoleculeInstances.
        let structureToMoleculeInstances (structure: PdbStructure) : MoleculeInstance array =
            structure.Residues
            |> Array.filter (fun r -> r.IsHet)
            |> Array.map (fun r -> residueToMoleculeInstance r structure.PdbId)

    /// Dataset provider for PDB files, focusing on ligand extraction.
    /// 
    /// PDB files typically contain protein structures with bound ligands.
    /// This provider extracts the ligand molecules (HETATM records) which
    /// are suitable for quantum chemistry calculations.
    /// 
    /// Example:
    ///   let provider = PdbLigandDatasetProvider("1ATP.pdb")
    ///   match provider.Load All with
    ///   | Ok dataset ->
    ///       for mol in dataset.Molecules do
    ///           printfn "Ligand: %s (%d atoms)" 
    ///               (mol.Name |> Option.defaultValue "?")
    ///               mol.Topology.Atoms.Length
    ///   | Error e -> printfn "Error: %A" e
    type PdbLigandDatasetProvider(filePath: string, ?options: PdbParser.PdbParseOptions) =
        let parseOptions = defaultArg options PdbParser.defaultOptions

        let loadDataset () : QuantumResult<MoleculeDataset> =
            match PdbParser.parseFile filePath with
            | Ok structure ->
                let structure = { structure with SourcePath = Some filePath }
                let molecules = PdbParser.structureToMoleculeInstances structure
                
                if molecules.Length = 0 then
                    Error (QuantumError.ValidationError("NoLigands", "No ligand molecules found in PDB file"))
                else
                    Ok { Molecules = molecules
                         Labels = None
                         LabelColumn = None
                         Metadata = 
                            [ "source", filePath
                              "format", "pdb"
                              yield! match structure.PdbId with | Some id -> ["pdb_id", id] | None -> []
                              yield! match structure.Title with | Some t -> ["title", t] | None -> [] ]
                            |> Map.ofList }
            | Error msg ->
                Error (QuantumError.ValidationError("PdbParsing", msg))

        interface IMoleculeDatasetProvider with
            member _.Describe() =
                $"PDB ligand provider (file: {filePath})"

            member _.Load(query: DatasetQuery) =
                match query with
                | All -> loadDataset ()
                | ByName name ->
                    loadDataset ()
                    |> Result.bind (fun ds ->
                        let matching = 
                            ds.Molecules 
                            |> Array.filter (fun m -> 
                                m.Name = Some name || 
                                m.Id = Some name ||
                                // Also match by residue name
                                m.Topology.Metadata.TryFind "residue_name" = Some name)
                        if matching.Length > 0 then
                            Ok { ds with Molecules = matching }
                        else
                            Error (QuantumError.ValidationError("LigandNotFound", $"No ligand named '{name}' found in PDB file")))
                | ByPath _ ->
                    Error (QuantumError.ValidationError("UnsupportedQuery", "PdbLigandDatasetProvider does not support path queries"))
                | ByCategory _ ->
                    Error (QuantumError.ValidationError("UnsupportedQuery", "PdbLigandDatasetProvider does not support category queries"))

            member _.ListNames() =
                match loadDataset () with
                | Ok ds -> 
                    ds.Molecules 
                    |> Array.choose (fun m -> m.Name)
                    |> Array.toList
                | Error _ -> []

    /// Dataset provider that loads ligands from a directory of PDB files.
    /// 
    /// Example:
    ///   let provider = PdbDirectoryDatasetProvider("/path/to/pdb_files")
    ///   match provider.Load All with
    ///   | Ok dataset -> printfn "Found %d ligands" dataset.Molecules.Length
    ///   | Error e -> printfn "Error: %A" e
    type PdbDirectoryDatasetProvider(directoryPath: string, ?searchPattern: string, ?options: PdbParser.PdbParseOptions) =
        let pattern = defaultArg searchPattern "*.pdb"
        let parseOptions = defaultArg options PdbParser.defaultOptions

        let loadPdbFile (path: string) : MoleculeInstance array =
            match PdbParser.parseFile path with
            | Ok structure ->
                let structure = { structure with SourcePath = Some path }
                PdbParser.structureToMoleculeInstances structure
            | Error _ -> [||]

        interface IMoleculeDatasetProvider with
            member _.Describe() =
                $"PDB directory provider (dir: {directoryPath}, pattern: {pattern})"

            member _.Load(query: DatasetQuery) =
                if not (Directory.Exists directoryPath) then
                    Error (QuantumError.ValidationError("DirectoryPath", $"Directory not found: {directoryPath}"))
                else
                    match query with
                    | All ->
                        let files = Directory.GetFiles(directoryPath, pattern)
                        let molecules = files |> Array.collect loadPdbFile
                        
                        if molecules.Length = 0 then
                            Error (QuantumError.ValidationError("NoLigands", "No ligands found in PDB files"))
                        else
                            Ok { Molecules = molecules
                                 Labels = None
                                 LabelColumn = None
                                 Metadata = Map.ofList ["source", directoryPath; "format", "pdb_directory"] }
                    
                    | ByName name ->
                        // First try to find a PDB file with that name
                        let pdbPath = Path.Combine(directoryPath, $"{name}.pdb")
                        if File.Exists pdbPath then
                            let molecules = loadPdbFile pdbPath
                            if molecules.Length > 0 then
                                Ok { Molecules = molecules
                                     Labels = None
                                     LabelColumn = None
                                     Metadata = Map.ofList ["source", pdbPath; "format", "pdb"] }
                            else
                                Error (QuantumError.ValidationError("NoLigands", $"No ligands found in {pdbPath}"))
                        else
                            // Search all PDB files for a ligand with that name
                            let files = Directory.GetFiles(directoryPath, pattern)
                            let matching =
                                files
                                |> Array.collect loadPdbFile
                                |> Array.filter (fun m ->
                                    m.Name = Some name ||
                                    m.Id = Some name ||
                                    m.Topology.Metadata.TryFind "residue_name" = Some name)
                            
                            if matching.Length > 0 then
                                Ok { Molecules = matching
                                     Labels = None
                                     LabelColumn = None
                                     Metadata = Map.ofList ["source", directoryPath; "format", "pdb_directory"] }
                            else
                                Error (QuantumError.ValidationError("LigandNotFound", $"No ligand named '{name}' found"))
                    
                    | ByPath subPath ->
                        let fullPath = Path.Combine(directoryPath, subPath)
                        if File.Exists fullPath then
                            let molecules = loadPdbFile fullPath
                            if molecules.Length > 0 then
                                Ok { Molecules = molecules
                                     Labels = None
                                     LabelColumn = None
                                     Metadata = Map.ofList ["source", fullPath; "format", "pdb"] }
                            else
                                Error (QuantumError.ValidationError("NoLigands", $"No ligands found in {fullPath}"))
                        else
                            Error (QuantumError.ValidationError("FileNotFound", $"File not found: {fullPath}"))
                    
                    | ByCategory _ ->
                        Error (QuantumError.ValidationError("UnsupportedQuery", "PdbDirectoryDatasetProvider does not support category queries"))

            member _.ListNames() =
                if Directory.Exists directoryPath then
                    let files = Directory.GetFiles(directoryPath, pattern)
                    files
                    |> Array.collect loadPdbFile
                    |> Array.choose (fun m -> m.Name)
                    |> Array.distinct
                    |> Array.toList
                else
                    []
