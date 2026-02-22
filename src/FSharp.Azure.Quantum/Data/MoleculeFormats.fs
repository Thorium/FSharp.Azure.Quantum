namespace FSharp.Azure.Quantum.Data

open System
open System.Globalization
open System.IO
open System.Text
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum.Core

/// Unified molecule file format parsing and writing.
///
/// This module consolidates all chemistry file format handling into
/// a single, idiomatic F# API. Each format has its own submodule with
/// consistent function signatures:
///
/// - `parse`: Pure function, content string → Result
/// - `readAsync`: File path → Async Result (thin I/O wrapper)
/// - `format`: Pure function, molecule → string
/// - `writeAsync`: File path → molecule → Async Result
///
/// Design philosophy: QC workloads are computation-bound, not I/O-bound.
/// These parsers prioritize correctness and simplicity over micro-optimization.
[<RequireQualifiedAccess>]
module MoleculeFormats =

    // ========================================================================
    // SHARED TYPES (re-exported from ChemistryDataProviders for convenience)
    // ========================================================================

    /// 3D coordinates for a single atom.
    type AtomCoord = { X: float; Y: float; Z: float }

    /// Molecular topology (atoms + bonds graph).
    type Topology =
        { Atoms: string array
          Bonds: (int * int * float option) array
          Charge: int option
          Multiplicity: int option
          Metadata: Map<string, string> }

    /// 3D geometry for a molecule.
    type Geometry =
        { Coordinates: AtomCoord array
          Units: string }

    /// A molecule instance with optional geometry.
    type MoleculeData =
        { Id: string option
          Name: string option
          Topology: Topology
          Geometry: Geometry option }

    // ========================================================================
    // SHARED HELPERS
    // ========================================================================

    /// Try parse a float using invariant culture
    let private tryParseFloat (s: string) =
        match Double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture) with
        | true, v -> Some v
        | false, _ -> None

    /// Try parse an int
    let private tryParseInt (s: string) =
        match Int32.TryParse(s.Trim()) with
        | true, v -> Some v
        | false, _ -> None

    /// Split line into whitespace-separated parts
    let private splitLine (line: string) =
        line.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)

    /// Infer bonds from geometry using covalent radii
    let private inferBonds (atoms: (string * AtomCoord) array) : (int * int * float option) array =
        [| for i in 0 .. atoms.Length - 2 do
               for j in i + 1 .. atoms.Length - 1 do
                   let (elem1, c1), (elem2, c2) = atoms.[i], atoms.[j]
                   let dx, dy, dz = c2.X - c1.X, c2.Y - c1.Y, c2.Z - c1.Z
                   let distance = sqrt (dx * dx + dy * dy + dz * dz)

                   match PeriodicTable.estimateBondLength elem1 elem2 with
                   | Some expected when distance <= expected * 1.25 -> yield (i, j, Some 1.0)
                   | None when distance < 3.0 -> yield (i, j, Some 1.0)
                   | _ -> () |]

    // ========================================================================
    // XYZ FORMAT
    // ========================================================================

    /// XYZ file format parser and writer.
    ///
    /// XYZ is a simple plain-text format for molecular geometry:
    /// - Line 1: Number of atoms
    /// - Line 2: Comment/title
    /// - Lines 3+: Element X Y Z (coordinates in Angstroms)
    ///
    /// Example:
    ///   3
    ///   Water molecule
    ///   O   0.000000   0.000000   0.117300
    ///   H   0.000000   0.757200  -0.469200
    ///   H   0.000000  -0.757200  -0.469200
    module Xyz =

        /// Parse a single atom line: "Element X Y Z"
        let private parseAtomLine (lineNum: int) (line: string) : QuantumResult<string * AtomCoord> =
            let parts = splitLine line

            if parts.Length < 4 then
                Error(QuantumError.ValidationError("XyzLine", $"Line {lineNum}: Expected 'Element X Y Z', got '{line}'"))
            else
                let element = parts.[0]

                match tryParseFloat parts.[1], tryParseFloat parts.[2], tryParseFloat parts.[3] with
                | Some x, Some y, Some z ->
                    if PeriodicTable.isValidSymbol element then
                        Ok(element, { X = x; Y = y; Z = z })
                    else
                        Error(QuantumError.ValidationError("Element", $"Line {lineNum}: Unknown element '{element}'"))
                | _ -> Error(QuantumError.ValidationError("XyzLine", $"Line {lineNum}: Invalid coordinates"))

        /// Parse XYZ content string into a MoleculeData.
        let parse (content: string) : QuantumResult<MoleculeData> =
            let lines =
                content.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
                |> Array.map (fun s -> s.Trim())
                |> Array.filter (String.IsNullOrWhiteSpace >> not)

            result {
                do!
                    if lines.Length < 3 then
                        Error(QuantumError.ValidationError("XyzFile", "XYZ file must have at least 3 lines"))
                    else
                        Ok()

                let! atomCount =
                    match tryParseInt lines.[0] with
                    | Some n when n > 0 -> Ok n
                    | Some _ -> Error(QuantumError.ValidationError("XyzFile", "Atom count must be positive"))
                    | None -> Error(QuantumError.ValidationError("XyzFile", "First line must be atom count"))

                do!
                    if lines.Length < 2 + atomCount then
                        Error(
                            QuantumError.ValidationError(
                                "XyzFile",
                                $"Expected {atomCount} atoms but file has only {lines.Length - 2} atom lines"
                            )
                        )
                    else
                        Ok()

                let name = lines.[1]

                // Parse all atom lines, collecting first error if any
                let! atomsWithCoords =
                    lines.[2 .. 1 + atomCount]
                    |> Array.mapi (fun i line -> parseAtomLine (i + 3) line)
                    |> Array.fold
                        (fun acc r ->
                            match acc, r with
                            | Error e, _ -> Error e
                            | _, Error e -> Error e
                            | Ok list, Ok atom -> Ok(atom :: list))
                        (Ok [])
                    |> Result.map (List.rev >> List.toArray)

                let bonds = inferBonds atomsWithCoords

                return
                    { Id = if String.IsNullOrWhiteSpace name then None else Some name
                      Name = if String.IsNullOrWhiteSpace name then None else Some name
                      Topology =
                        { Atoms = atomsWithCoords |> Array.map fst
                          Bonds = bonds
                          Charge = None
                          Multiplicity = None
                          Metadata = Map.ofList [ "source_format", "xyz" ] }
                      Geometry =
                        Some
                            { Coordinates = atomsWithCoords |> Array.map snd
                              Units = "angstrom" } }
            }

        /// Read XYZ file asynchronously (task-based).
        let readAsync (path: string) (cancellationToken: CancellationToken) : Task<QuantumResult<MoleculeData>> =
            task {
                try
                    if not (File.Exists path) then
                        return Error(QuantumError.IOError("ReadXYZ", path, "File not found"))
                    else
                        let! content = File.ReadAllTextAsync(path, cancellationToken)
                        return parse content
                with ex ->
                    return Error(QuantumError.OperationError("XyzRead", ex.Message))
            }

        /// Format MoleculeData as XYZ string.
        let format (mol: MoleculeData) : QuantumResult<string> =
            match mol.Geometry with
            | None -> Error(QuantumError.ValidationError("XyzFormat", "Cannot format molecule without geometry"))
            | Some geom when geom.Coordinates.Length <> mol.Topology.Atoms.Length ->
                Error(
                    QuantumError.ValidationError(
                        "XyzFormat",
                        $"Atom count ({mol.Topology.Atoms.Length}) doesn't match coordinate count ({geom.Coordinates.Length})"
                    )
                )
            | Some geom ->
                let sb = StringBuilder()
                sb.AppendLine(string mol.Topology.Atoms.Length) |> ignore
                sb.AppendLine(mol.Name |> Option.defaultValue "Molecule") |> ignore

                for i, elem in mol.Topology.Atoms |> Array.indexed do
                    let c = geom.Coordinates.[i]

                    sb.AppendLine(String.Format(CultureInfo.InvariantCulture, "{0,-2}  {1,12:F6}  {2,12:F6}  {3,12:F6}", elem, c.X, c.Y, c.Z))
                    |> ignore

                Ok(sb.ToString())

        /// Write MoleculeData to XYZ file asynchronously (task-based).
        let writeAsync (path: string) (mol: MoleculeData) (cancellationToken: CancellationToken) : Task<QuantumResult<unit>> =
            task {
                match format mol with
                | Error e -> return Error e
                | Ok content ->
                    try
                        do! File.WriteAllTextAsync(path, content, cancellationToken)
                        return Ok()
                    with ex ->
                        return Error(QuantumError.IOError("WriteXYZ", path, ex.Message))
            }

    // ========================================================================
    // FCIDUMP FORMAT
    // ========================================================================

    /// FCIDump (Full Configuration Interaction Dump) format parser.
    ///
    /// FCIDump is a standard format for molecular orbital integrals used by
    /// quantum chemistry programs (PySCF, Psi4, etc.). This parser extracts
    /// header metadata only - full integral parsing requires specialized software.
    ///
    /// IMPORTANT: FCIDump does NOT contain molecular geometry. The resulting
    /// MoleculeData will have Geometry = None.
    ///
    /// Header format:
    ///   &FCI NORB=10,NELEC=8,MS2=0,
    ///   ORBSYM=1,1,1,1,1,1,1,1,1,1,
    ///   &END
    module FciDump =

        /// Parsed FCIDump header information.
        type Header =
            { NumOrbitals: int
              NumElectrons: int
              MS2: int option
              OrbSym: int array option
              NumIrrep: int option
              RawHeader: string }

        /// Parse FCIDump header from content string.
        let parseHeader (content: string) : QuantumResult<Header> =
            let lines = content.Replace("\r\n", "\n").Split('\n')

            let headerStartIdx =
                lines
                |> Array.tryFindIndex (fun line ->
                    let t = line.Trim().ToUpperInvariant()
                    t.StartsWith("&FCI") || t.StartsWith("$FCI"))

            match headerStartIdx with
            | None -> Error(QuantumError.ValidationError("FciDump", "No FCIDump header found (&FCI line)"))
            | Some startIdx ->
                // Collect header lines until end marker
                let headerLines =
                    lines.[startIdx..]
                    |> Array.takeWhile (fun line ->
                        let t = line.Trim()
                        not (t = "/" || t = "&END" || t = "$END" || t = "&" || t = "$"))

                let header = String.concat " " headerLines

                let extractInt name =
                    let pattern = $"{name}\\s*=\\s*(\\d+)"
                    let m = Regex.Match(header, pattern, RegexOptions.IgnoreCase)
                    if m.Success then Some(int m.Groups.[1].Value) else None

                let extractIntArray name =
                    let pattern = $"{name}\\s*=\\s*([\\d,\\s]+)"
                    let m = Regex.Match(header, pattern, RegexOptions.IgnoreCase)

                    if m.Success then
                        let values =
                            m.Groups.[1].Value.Split([| ','; ' ' |], StringSplitOptions.RemoveEmptyEntries)
                            |> Array.choose (fun s ->
                                match Int32.TryParse(s.Trim()) with
                                | true, v -> Some v
                                | false, _ -> None)

                        if values.Length > 0 then Some values else None
                    else
                        None

                match extractInt "NORB", extractInt "NELEC" with
                | None, _ -> Error(QuantumError.ValidationError("FciDump", "NORB not found in header"))
                | _, None -> Error(QuantumError.ValidationError("FciDump", "NELEC not found in header"))
                | Some norb, Some nelec ->
                    Ok
                        { NumOrbitals = norb
                          NumElectrons = nelec
                          MS2 = extractInt "MS2"
                          OrbSym = extractIntArray "ORBSYM"
                          NumIrrep = extractInt "NIRREP"
                          RawHeader = header }

        /// Convert FCIDump header to MoleculeData.
        /// Note: Geometry will be None since FCIDump doesn't contain coordinates.
        let toMoleculeData (header: Header) (sourcePath: string option) : MoleculeData =
            let multiplicity =
                match header.MS2 with
                | Some ms2 -> ms2 + 1
                | None -> 1

            { Id = sourcePath |> Option.map Path.GetFileNameWithoutExtension
              Name = sourcePath |> Option.map (fun p -> $"FCIDump: {Path.GetFileName p}")
              Topology =
                { Atoms = Array.init (max 1 (header.NumElectrons / 2)) (fun _ -> "X")
                  Bonds = [||]
                  Charge = Some(header.NumOrbitals - header.NumElectrons)
                  Multiplicity = Some multiplicity
                  Metadata =
                    [ "format", "fcidump"
                      "norb", string header.NumOrbitals
                      "nelec", string header.NumElectrons
                      yield! header.MS2 |> Option.map (fun ms2 -> "ms2", string ms2) |> Option.toList
                      yield! header.NumIrrep |> Option.map (fun n -> "nirrep", string n) |> Option.toList
                      yield! sourcePath |> Option.map (fun p -> "source", p) |> Option.toList ]
                    |> Map.ofList }
              Geometry = None }

        /// Parse FCIDump content and return MoleculeData.
        let parse (content: string) : QuantumResult<MoleculeData> =
            parseHeader content |> Result.map (fun h -> toMoleculeData h None)

        /// Read FCIDump file asynchronously (task-based).
        let readAsync (path: string) (cancellationToken: CancellationToken) : Task<QuantumResult<MoleculeData>> =
            task {
                try
                    if not (File.Exists path) then
                        return Error(QuantumError.IOError("ReadFciDump", path, "File not found"))
                    else
                        let! content = File.ReadAllTextAsync(path, cancellationToken)

                        return
                            parseHeader content
                            |> Result.map (fun h -> toMoleculeData h (Some path))
                with ex ->
                    return Error(QuantumError.OperationError("FciDumpRead", ex.Message))
            }

    // ========================================================================
    // SDF/MOL FORMAT
    // ========================================================================

    /// SDF (Structure-Data File) and MOL format parser.
    ///
    /// MOL V2000 is a widely-used format for molecular structures containing:
    /// - Atom coordinates (3D geometry)
    /// - Bond connectivity and types
    /// - Charges and other properties
    ///
    /// SDF extends MOL by allowing multiple molecules with associated data fields.
    module Sdf =

        /// Parsed atom from MOL block
        type private MolAtom =
            { X: float
              Y: float
              Z: float
              Symbol: string
              Charge: int }

        /// Parsed bond from MOL block
        type private MolBond =
            { Atom1: int // 1-indexed
              Atom2: int // 1-indexed
              BondType: int }

        /// Parsed MOL record
        type private MolRecord =
            { Name: string
              Atoms: MolAtom array
              Bonds: MolBond array
              Charges: (int * int) list
              Properties: Map<string, string> }

        /// Parse atom line from MOL block
        let private parseAtomLine (line: string) : MolAtom option =
            let parts = splitLine line

            if parts.Length >= 4 then
                match tryParseFloat parts.[0], tryParseFloat parts.[1], tryParseFloat parts.[2] with
                | Some x, Some y, Some z ->
                    let symbol = parts.[3].Trim()
                    let chargeCode = if parts.Length > 5 then tryParseInt parts.[5] |> Option.defaultValue 0 else 0

                    let charge =
                        match chargeCode with
                        | 1 -> 3
                        | 2 -> 2
                        | 3 -> 1
                        | 5 -> -1
                        | 6 -> -2
                        | 7 -> -3
                        | _ -> 0

                    Some { X = x; Y = y; Z = z; Symbol = symbol; Charge = charge }
                | _ -> None
            else
                None

        /// Parse bond line from MOL block
        let private parseBondLine (line: string) : MolBond option =
            let parts = splitLine line

            if parts.Length >= 3 then
                match tryParseInt parts.[0], tryParseInt parts.[1], tryParseInt parts.[2] with
                | Some a1, Some a2, Some bt -> Some { Atom1 = a1; Atom2 = a2; BondType = bt }
                | _ -> None
            else
                None

        /// Parse M  CHG entries from a single M  CHG line
        let private parseChargeEntries (parts: string array) : (int * int) list =
            match tryParseInt parts.[0] with
            | Some count ->
                [ for i in 0 .. count - 1 do
                    if parts.Length > 1 + i * 2 + 1 then
                        match tryParseInt parts.[1 + i * 2], tryParseInt parts.[2 + i * 2] with
                        | Some idx, Some chg -> yield (idx, chg)
                        | _ -> () ]
            | None -> []

        /// Parse properties block (M  CHG lines) until M  END using tail recursion
        let private parsePropertiesBlock (lines: string array) (startLine: int) : (int * int) list * int =
            let rec loop lineNum chargesAcc =
                if lineNum >= lines.Length then
                    (List.rev chargesAcc, lineNum)
                else
                    let line = lines.[lineNum]
                    if line.StartsWith("M  END") then
                        (List.rev chargesAcc, lineNum + 1)
                    elif line.StartsWith("M  CHG") then
                        let parts = splitLine (line.Substring(6))
                        let newCharges =
                            if parts.Length >= 3 then parseChargeEntries parts
                            else []
                        loop (lineNum + 1) (List.append (List.rev newCharges) chargesAcc)
                    else
                        loop (lineNum + 1) chargesAcc
            loop startLine []

        /// Parse a single MOL block
        let private parseMolBlock (lines: string array) : Result<MolRecord * int, string> =
            if lines.Length < 4 then
                Error "MOL block too short"
            else
                let name = lines.[0].Trim()
                let countsLine = lines.[3]

                if countsLine.Length < 6 then
                    Error $"Invalid counts line: '{countsLine}'"
                else
                    let atomCountStr = countsLine.Substring(0, 3).Trim()
                    let bondCountStr = countsLine.Substring(3, 3).Trim()

                    match tryParseInt atomCountStr, tryParseInt bondCountStr with
                    | Some atomCount, Some bondCount ->
                        let atomStart = 4
                        let bondStart = atomStart + atomCount

                        if lines.Length < bondStart + bondCount then
                            Error "MOL block truncated"
                        else
                            let atoms =
                                lines.[atomStart .. atomStart + atomCount - 1]
                                |> Array.choose parseAtomLine

                            let bonds =
                                lines.[bondStart .. bondStart + bondCount - 1]
                                |> Array.choose parseBondLine

                            // Parse properties block using tail recursion
                            let (charges, linesConsumed) = parsePropertiesBlock lines (bondStart + bondCount)

                            Ok(
                                { Name = name
                                  Atoms = atoms
                                  Bonds = bonds
                                  Charges = charges
                                  Properties = Map.empty },
                                linesConsumed
                            )
                    | _ -> Error $"Cannot parse atom/bond counts from '{countsLine}'"

        /// Parse data fields from SDF format using tail recursion
        let private parseDataFields (lines: string array) (startLine: int) : Map<string, string> * int =
            let rec loop lineNum currentField (valueBuilder: StringBuilder) properties =
                if lineNum >= lines.Length || lines.[lineNum].StartsWith("$$$$") then
                    // Finalize any pending field
                    let finalProps =
                        match currentField with
                        | Some fieldName -> properties |> Map.add fieldName (valueBuilder.ToString().Trim())
                        | None -> properties
                    // Skip the $$$$ delimiter if present
                    let nextLine = if lineNum < lines.Length && lines.[lineNum].StartsWith("$$$$") then lineNum + 1 else lineNum
                    (finalProps, nextLine)
                else
                    let line = lines.[lineNum]
                    
                    if line.StartsWith("> ") || line.StartsWith(">  ") then
                        // Save previous field if exists
                        let updatedProps =
                            match currentField with
                            | Some fieldName -> properties |> Map.add fieldName (valueBuilder.ToString().Trim())
                            | None -> properties
                        
                        // Parse new field name
                        let fieldMatch = Regex.Match(line, @">\s*<([^>]+)>")
                        if fieldMatch.Success then
                            loop (lineNum + 1) (Some fieldMatch.Groups.[1].Value) (StringBuilder()) updatedProps
                        else
                            loop (lineNum + 1) None (StringBuilder()) updatedProps
                    elif currentField.IsSome && line.Trim().Length > 0 then
                        if valueBuilder.Length > 0 then
                            valueBuilder.AppendLine() |> ignore
                        valueBuilder.Append(line.Trim()) |> ignore
                        loop (lineNum + 1) currentField valueBuilder properties
                    else
                        loop (lineNum + 1) currentField valueBuilder properties
            
            loop startLine None (StringBuilder()) Map.empty

        /// Convert MolRecord to MoleculeData
        let private toMoleculeData (record: MolRecord) : MoleculeData =
            let chargeMap = record.Charges |> Map.ofList

            let totalCharge =
                let inlineCharge = record.Atoms |> Array.sumBy (fun a -> a.Charge)
                let mChgCharge = record.Charges |> List.sumBy snd
                inlineCharge + mChgCharge

            { Id = if String.IsNullOrWhiteSpace record.Name then None else Some record.Name
              Name =
                if String.IsNullOrWhiteSpace record.Name then
                    record.Properties.TryFind "Name"
                    |> Option.orElse (record.Properties.TryFind "PUBCHEM_IUPAC_NAME")
                else
                    Some record.Name
              Topology =
                { Atoms = record.Atoms |> Array.map (fun a -> a.Symbol)
                  Bonds = record.Bonds |> Array.map (fun b -> (b.Atom1 - 1, b.Atom2 - 1, Some(float b.BondType)))
                  Charge = Some totalCharge
                  Multiplicity = Some 1
                  Metadata =
                    record.Properties
                    |> Map.toList
                    |> List.append [ "source_format", "sdf" ]
                    |> Map.ofList }
              Geometry =
                Some
                    { Coordinates = record.Atoms |> Array.map (fun a -> { X = a.X; Y = a.Y; Z = a.Z })
                      Units = "angstrom" } }

        /// Parse SDF content (may contain multiple molecules) using tail recursion.
        let parseAll (content: string) : QuantumResult<MoleculeData array> =
            let lines = content.Replace("\r\n", "\n").Split('\n')
            
            /// Skip empty lines and return next non-empty line index
            let rec skipEmpty lineNum =
                if lineNum >= lines.Length then lineNum
                elif String.IsNullOrWhiteSpace lines.[lineNum] then skipEmpty (lineNum + 1)
                else lineNum
            
            /// Skip to next $$$$ delimiter and return line after it
            let rec skipToDelimiter lineNum =
                if lineNum >= lines.Length then lineNum
                elif lines.[lineNum].StartsWith("$$$$") then lineNum + 1
                else skipToDelimiter (lineNum + 1)
            
            /// Main parsing loop
            let rec parseLoop lineNum recordsAcc errorsAcc =
                let startLine = skipEmpty lineNum
                if startLine >= lines.Length then
                    (List.rev recordsAcc, List.rev errorsAcc)
                else
                    match parseMolBlock lines.[startLine..] with
                    | Ok(record, linesConsumed) ->
                        let nextLine = startLine + linesConsumed
                        let (properties, finalLine) = parseDataFields lines nextLine
                        let recordWithProps = { record with Properties = properties }
                        parseLoop finalLine (recordWithProps :: recordsAcc) errorsAcc
                    | Error e ->
                        let nextLine = skipToDelimiter startLine
                        parseLoop nextLine recordsAcc (e :: errorsAcc)
            
            let (records, errors) = parseLoop 0 [] []
            
            if records.IsEmpty && not errors.IsEmpty then
                Error(QuantumError.ValidationError("SdfParsing", String.concat "; " errors))
            else
                Ok(records |> List.map toMoleculeData |> List.toArray)

        /// Parse single MOL file content.
        let parse (content: string) : QuantumResult<MoleculeData> =
            parseAll content
            |> Result.bind (fun arr ->
                if arr.Length > 0 then
                    Ok arr.[0]
                else
                    Error(QuantumError.ValidationError("MolParsing", "No molecule found")))

        /// Read SDF file asynchronously (returns all molecules, task-based).
        let readAllAsync (path: string) (cancellationToken: CancellationToken) : Task<QuantumResult<MoleculeData array>> =
            task {
                try
                    if not (File.Exists path) then
                        return Error(QuantumError.IOError("ReadSdf", path, "File not found"))
                    else
                        let! content = File.ReadAllTextAsync(path, cancellationToken)
                        return parseAll content
                with ex ->
                    return Error(QuantumError.OperationError("SdfRead", ex.Message))
            }

        /// Read single MOL file asynchronously (task-based).
        let readAsync (path: string) (cancellationToken: CancellationToken) : Task<QuantumResult<MoleculeData>> =
            task {
                let! result = readAllAsync path cancellationToken
                return result |> Result.bind (fun arr ->
                    if arr.Length > 0 then Ok arr.[0]
                    else Error(QuantumError.ValidationError("MolParsing", "No molecule found")))
            }

    // ========================================================================
    // PDB FORMAT (LIGAND EXTRACTION)
    // ========================================================================

    /// PDB (Protein Data Bank) format parser for ligand extraction.
    ///
    /// PDB is the standard format for macromolecular structures. This parser
    /// focuses on extracting small molecule ligands (HETATM records) which
    /// are relevant for quantum chemistry calculations.
    ///
    /// For full protein structures, use specialized tools like BioPython.
    module Pdb =

        /// Parsed atom from PDB file
        type private PdbAtom =
            { Serial: int
              Name: string
              ResName: string
              ChainId: char option
              ResSeq: int
              X: float
              Y: float
              Z: float
              Element: string }

        /// Parse HETATM/ATOM line from PDB
        let private parseAtomLine (line: string) : PdbAtom option =
            if line.Length < 54 then
                None
            else
                try
                    let serial = line.Substring(6, 5).Trim() |> int
                    let name = line.Substring(12, 4).Trim()
                    let resName = line.Substring(17, 3).Trim()
                    let chainId = if line.Length > 21 && line.[21] <> ' ' then Some line.[21] else None
                    let resSeq = line.Substring(22, 4).Trim() |> int
                    let x = line.Substring(30, 8).Trim() |> Double.Parse
                    let y = line.Substring(38, 8).Trim() |> Double.Parse
                    let z = line.Substring(46, 8).Trim() |> Double.Parse

                    // Element is in columns 77-78, or infer from atom name
                    let element =
                        if line.Length >= 78 then
                            let elem = line.Substring(76, 2).Trim()
                            if String.IsNullOrEmpty elem then name.Substring(0, min 2 name.Length).Trim()
                            else elem
                        else
                            name.Substring(0, min 2 name.Length).Trim()

                    Some
                        { Serial = serial
                          Name = name
                          ResName = resName
                          ChainId = chainId
                          ResSeq = resSeq
                          X = x
                          Y = y
                          Z = z
                          Element = element }
                with _ ->
                    None

        /// Standard water residue names to exclude
        let private waterResidues = set [ "HOH"; "WAT"; "H2O"; "DOD"; "D2O" ]

        /// Common ion residue names to exclude
        let private ionResidues =
            set [ "NA"; "CL"; "K"; "MG"; "CA"; "ZN"; "FE"; "MN"; "CO"; "NI"; "CU"; "CD"; "HG"; "PB" ]

        /// Parse PDB content and extract ligands (HETATM records, excluding water/ions).
        let parseLigands (content: string) : QuantumResult<MoleculeData array> =
            let lines = content.Replace("\r\n", "\n").Split('\n')

            // Collect HETATM records
            let hetatoms =
                lines
                |> Array.filter (fun line -> line.StartsWith("HETATM"))
                |> Array.choose parseAtomLine
                |> Array.filter (fun a -> not (waterResidues.Contains a.ResName) && not (ionResidues.Contains a.ResName))

            // Group by residue (resName + chainId + resSeq)
            let ligandGroups =
                hetatoms
                |> Array.groupBy (fun a -> (a.ResName, a.ChainId, a.ResSeq))
                |> Array.map snd

            let molecules =
                ligandGroups
                |> Array.map (fun atoms ->
                    let resName = atoms.[0].ResName
                    let chainId = atoms.[0].ChainId |> Option.map string |> Option.defaultValue ""
                    let resSeq = atoms.[0].ResSeq

                    { Id = Some $"{resName}_{chainId}{resSeq}"
                      Name = Some resName
                      Topology =
                        { Atoms = atoms |> Array.map (fun a -> a.Element)
                          Bonds = [||] // PDB doesn't include bond info for HETATM
                          Charge = None
                          Multiplicity = None
                          Metadata = Map.ofList [ "source_format", "pdb"; "residue", resName; "chain", chainId ] }
                      Geometry =
                        Some
                            { Coordinates = atoms |> Array.map (fun a -> { X = a.X; Y = a.Y; Z = a.Z })
                              Units = "angstrom" } })

            if molecules.Length = 0 then
                Error(QuantumError.ValidationError("PdbParsing", "No ligands found (HETATM records excluding water/ions)"))
            else
                Ok molecules

        /// Read PDB file and extract ligands asynchronously (task-based).
        let readLigandsAsync (path: string) (cancellationToken: CancellationToken) : Task<QuantumResult<MoleculeData array>> =
            task {
                try
                    if not (File.Exists path) then
                        return Error(QuantumError.IOError("ReadPdb", path, "File not found"))
                    else
                        let! content = File.ReadAllTextAsync(path, cancellationToken)
                        return parseLigands content
                with ex ->
                    return Error(QuantumError.OperationError("PdbRead", ex.Message))
            }

    // ========================================================================
    // FORMAT DETECTION
    // ========================================================================

    /// Auto-detect format and read molecule(s) from file.
    module FormatDetection =

        /// Detect format from file extension
        let (|XyzFile|SdfFile|MolFile|PdbFile|FciDumpFile|UnknownFile|) (path: string) =
            match Path.GetExtension(path).ToLowerInvariant() with
            | ".xyz" -> XyzFile
            | ".sdf" -> SdfFile
            | ".mol" -> MolFile
            | ".pdb"
            | ".ent" -> PdbFile
            | ".fcidump" -> FciDumpFile
            | ext -> UnknownFile ext

        /// Read molecule(s) from file, auto-detecting format (task-based).
        let readAutoAsync (path: string) (cancellationToken: CancellationToken) : Task<QuantumResult<MoleculeData array>> =
            task {
                match path with
                | XyzFile ->
                    let! result = Xyz.readAsync path cancellationToken
                    return result |> Result.map Array.singleton
                | SdfFile
                | MolFile -> return! Sdf.readAllAsync path cancellationToken
                | PdbFile -> return! Pdb.readLigandsAsync path cancellationToken
                | FciDumpFile ->
                    let! result = FciDump.readAsync path cancellationToken
                    return result |> Result.map Array.singleton
                | UnknownFile ext ->
                    return Error(QuantumError.ValidationError("FileFormat", $"Unknown file extension: {ext}"))
            }
