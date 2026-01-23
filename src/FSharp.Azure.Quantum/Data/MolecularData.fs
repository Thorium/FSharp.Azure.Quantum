namespace FSharp.Azure.Quantum.Data

/// Molecular Data Infrastructure for Drug Discovery Applications
///
/// Provides SMILES parsing, molecular descriptor calculation, and data loading
/// utilities for quantum machine learning in pharmaceutical applications.
///
/// SMILES (Simplified Molecular-Input Line-Entry System) is a standard text
/// notation for representing chemical structures.
///
/// Reference: Weininger, D. "SMILES, a Chemical Language and Information System"
/// Journal of Chemical Information and Computer Sciences, 1988.

open System
open System.Text.RegularExpressions
open FSharp.Azure.Quantum.Core

module MolecularData =

    type private SmilesParseState =
        { LastAtomIndex: int
          CurrentBondOrder: int
          ErrorsRev: string list }
    
    // ========================================================================
    // CORE TYPES
    // ========================================================================
    
    /// Represents an atom in a molecule
    type Atom = {
        /// Element symbol (e.g., "C", "N", "O", "S")
        Element: string
        
        /// Atom index in the molecule (0-based)
        Index: int
        
        /// Formal charge (e.g., -1, 0, +1)
        Charge: int
        
        /// Is aromatic (lowercase in SMILES)
        IsAromatic: bool
        
        /// Number of explicit hydrogens
        ExplicitHydrogens: int
        
        /// Atomic mass (if specified, for isotopes)
        Mass: int option
    }
    
    /// Represents a bond between atoms
    type Bond = {
        /// Index of first atom
        Atom1: int
        
        /// Index of second atom
        Atom2: int
        
        /// Bond order (1=single, 2=double, 3=triple, 4=aromatic)
        Order: int
    }
    
    /// Parsed molecular structure
    type Molecule = {
        /// Original SMILES string
        Smiles: string
        
        /// Atoms in the molecule
        Atoms: Atom array
        
        /// Bonds in the molecule
        Bonds: Bond array
        
        /// Molecular formula (calculated)
        Formula: string
        
        /// Parse errors (if any)
        ParseErrors: string list
    }
    
    /// Molecular descriptors for ML features
    type MolecularDescriptors = {
        /// Molecular weight (sum of atomic masses)
        MolecularWeight: float
        
        /// Partition coefficient (lipophilicity estimate)
        LogP: float
        
        /// Number of hydrogen bond donors
        HydrogenBondDonors: int
        
        /// Number of hydrogen bond acceptors
        HydrogenBondAcceptors: int
        
        /// Number of rotatable bonds
        RotatableBonds: int
        
        /// Topological polar surface area (estimate)
        TPSA: float
        
        /// Number of heavy atoms (non-hydrogen)
        HeavyAtomCount: int
        
        /// Number of aromatic rings
        AromaticRingCount: int
        
        /// Number of rings total
        RingCount: int
        
        /// Fraction of sp3 carbons
        FractionCsp3: float
    }
    
    /// Binary fingerprint for similarity calculations
    type MolecularFingerprint = {
        /// Fingerprint type identifier
        Type: string
        
        /// Bit vector (true = bit set)
        Bits: bool array
        
        /// Number of set bits
        BitCount: int
    }
    
    /// Result of molecular data loading
    type MolecularDataset = {
        /// Parsed molecules
        Molecules: Molecule array
        
        /// Computed descriptors (parallel to Molecules)
        Descriptors: MolecularDescriptors array option
        
        /// Computed fingerprints (parallel to Molecules)
        Fingerprints: MolecularFingerprint array option
        
        /// Labels (if provided)
        Labels: int array option
        
        /// Label column name (if applicable)
        LabelColumn: string option
    }
    
    // ========================================================================
    // ATOMIC DATA (Reference: IUPAC)
    // ========================================================================
    
    /// Atomic masses for common elements
    let private atomicMasses = 
        Map.ofList [
            ("H", 1.008)
            ("C", 12.011)
            ("N", 14.007)
            ("O", 15.999)
            ("F", 18.998)
            ("P", 30.974)
            ("S", 32.065)
            ("Cl", 35.453)
            ("Br", 79.904)
            ("I", 126.904)
            ("B", 10.811)
            ("Si", 28.086)
            ("Se", 78.96)
            ("Na", 22.990)
            ("K", 39.098)
            ("Ca", 40.078)
            ("Mg", 24.305)
            ("Zn", 65.38)
            ("Fe", 55.845)
            ("Cu", 63.546)
        ]
    
    /// LogP contributions (Wildman-Crippen)
    let private logPContributions =
        Map.ofList [
            ("C", 0.1441)
            ("N", -0.7566)
            ("O", -0.2893)
            ("S", 0.6482)
            ("F", 0.4118)
            ("Cl", 0.6895)
            ("Br", 0.8456)
            ("I", 1.1410)
            ("H", 0.1230)
            ("P", 0.8740)
        ]
    
    /// TPSA contributions (Ertl)
    let private tpsaContributions =
        Map.ofList [
            ("N", 26.02)   // Primary amine
            ("NH", 26.02)  // Secondary amine  
            ("NH2", 26.02) // Primary amine
            ("O", 9.23)    // Ether oxygen
            ("OH", 20.23)  // Hydroxyl
            ("S", 25.30)   // Thioether
            ("SH", 28.24)  // Thiol
        ]
    
    // ========================================================================
    // SMILES PARSER
    // ========================================================================
    
    /// Parse atom from SMILES notation
    let private parseAtom (smilesFragment: string) (index: int) : Atom option =
        if String.IsNullOrEmpty(smilesFragment) then
            None
        else
            // Organic subset: B, C, N, O, P, S, F, Cl, Br, I
            // Lowercase = aromatic: b, c, n, o, p, s
            let organicSubset = Regex(@"^([BCNOPSFIbcnops]|Cl|Br)")
            let bracketAtom = Regex(@"^\[(\d*)([A-Z][a-z]?)([H]?)(\d*)([+-]?\d*)\]")
            
            let matchOrganic = organicSubset.Match(smilesFragment)
            if matchOrganic.Success then
                let element = matchOrganic.Value.ToUpper()
                let isAromatic = Char.IsLower(smilesFragment.[0])
                Some {
                    Element = element
                    Index = index
                    Charge = 0
                    IsAromatic = isAromatic
                    ExplicitHydrogens = 0
                    Mass = None
                }
            else
                let matchBracket = bracketAtom.Match(smilesFragment)
                if matchBracket.Success then
                    let mass = 
                        if String.IsNullOrEmpty(matchBracket.Groups.[1].Value) then None
                        else Some (Int32.Parse(matchBracket.Groups.[1].Value))
                    let element = matchBracket.Groups.[2].Value
                    let hasH = not (String.IsNullOrEmpty(matchBracket.Groups.[3].Value))
                    let hCount = 
                        if hasH && String.IsNullOrEmpty(matchBracket.Groups.[4].Value) then 1
                        elif hasH then Int32.Parse(matchBracket.Groups.[4].Value)
                        else 0
                    let charge = 
                        if String.IsNullOrEmpty(matchBracket.Groups.[5].Value) then 0
                        elif matchBracket.Groups.[5].Value = "+" then 1
                        elif matchBracket.Groups.[5].Value = "-" then -1
                        else Int32.Parse(matchBracket.Groups.[5].Value)
                    
                    Some {
                        Element = element
                        Index = index
                        Charge = charge
                        IsAromatic = Char.IsLower(element.[0])
                        ExplicitHydrogens = hCount
                        Mass = mass
                    }
                else
                    None
    
    /// Simple SMILES tokenizer
    let private tokenizeSmiles (smiles: string) : string list =
        let organic = Regex(@"^(Cl|Br|[BCNOPSFIbcnops])")
        let bracket = Regex(@"^\[[^\]]+\]")
        let bond = Regex(@"^[-=#:$]")
        let ring = Regex(@"^%?\d+")
        let branch = Regex(@"^[()]")
        let dot = Regex(@"^\.")
        
        let rec tokenize (remaining: string) (acc: string list) =
            if String.IsNullOrEmpty(remaining) then
                List.rev acc
            else
                // Try patterns in order
                let tryMatch (pattern: Regex) =
                    let m = pattern.Match(remaining)
                    if m.Success then Some m.Value else None
                
                match tryMatch bracket with
                | Some token -> tokenize (remaining.Substring(token.Length)) (token :: acc)
                | None ->
                    match tryMatch organic with
                    | Some token -> tokenize (remaining.Substring(token.Length)) (token :: acc)
                    | None ->
                        match tryMatch bond with
                        | Some token -> tokenize (remaining.Substring(token.Length)) (token :: acc)
                        | None ->
                            match tryMatch ring with
                            | Some token -> tokenize (remaining.Substring(token.Length)) (token :: acc)
                            | None ->
                                match tryMatch branch with
                                | Some token -> tokenize (remaining.Substring(token.Length)) (token :: acc)
                                | None ->
                                    match tryMatch dot with
                                    | Some token -> tokenize (remaining.Substring(token.Length)) (token :: acc)
                                    | None ->
                                        // Unknown character, skip with warning
                                        tokenize (remaining.Substring(1)) acc
        
        tokenize smiles []
    
    /// Parse SMILES string into a Molecule
    let parseSmiles (smiles: string) : QuantumResult<Molecule> =
        if String.IsNullOrWhiteSpace(smiles) then
            Error (QuantumError.ValidationError ("smiles", "SMILES string cannot be empty"))
        else
            try
                let tokens = tokenizeSmiles smiles
                let atoms = ResizeArray<Atom>()
                let bonds = ResizeArray<Bond>()
                let ringClosures = System.Collections.Generic.Dictionary<int, int>()
                let branchStack = System.Collections.Generic.Stack<int>()

                let initialState: SmilesParseState = { LastAtomIndex = -1; CurrentBondOrder = 1; ErrorsRev = [] }

                let step state token =
                    match token with
                    // Bond characters
                    | "-" -> { state with CurrentBondOrder = 1 }
                    | "=" -> { state with CurrentBondOrder = 2 }
                    | "#" -> { state with CurrentBondOrder = 3 }
                    | ":" -> { state with CurrentBondOrder = 4 } // Aromatic

                    // Branch start
                    | "(" ->
                        if state.LastAtomIndex >= 0 then
                            branchStack.Push(state.LastAtomIndex)
                        state

                    // Branch end
                    | ")" ->
                        if branchStack.Count > 0 then
                            { state with LastAtomIndex = branchStack.Pop() }
                        else
                            state

                    // Disconnected fragment
                    | "." -> { state with LastAtomIndex = -1 }

                    // Ring closure
                    | _ when Regex.IsMatch(token, @"^%?\d+$") ->
                        let ringNum =
                            if token.StartsWith("%") then Int32.Parse(token.Substring(1))
                            else Int32.Parse(token)

                        if ringClosures.ContainsKey(ringNum) then
                            let startAtom = ringClosures.[ringNum]
                            bonds.Add({ Atom1 = startAtom; Atom2 = state.LastAtomIndex; Order = state.CurrentBondOrder })
                            ringClosures.Remove(ringNum) |> ignore
                        else
                            ringClosures.[ringNum] <- state.LastAtomIndex

                        { state with CurrentBondOrder = 1 }

                    | _ ->
                        match parseAtom token atoms.Count with
                        | Some atom ->
                            atoms.Add(atom)

                            if state.LastAtomIndex >= 0 then
                                bonds.Add({ Atom1 = state.LastAtomIndex; Atom2 = atom.Index; Order = state.CurrentBondOrder })

                            { state with
                                LastAtomIndex = atom.Index
                                CurrentBondOrder = 1 }
                        | None ->
                            { state with ErrorsRev = sprintf "Unknown token: %s" token :: state.ErrorsRev }

                let finalState =
                    tokens |> List.fold step initialState
                
                // Calculate molecular formula
                let formula =
                    atoms
                    |> Seq.groupBy (fun a -> a.Element)
                    |> Seq.sortBy (fun (elem, _) -> 
                        // Hill system: C first, then H, then alphabetical
                        match elem with
                        | "C" -> "0C"
                        | "H" -> "1H"
                        | _ -> "2" + elem)
                    |> Seq.map (fun (elem, group) ->
                        let count = Seq.length group
                        if count = 1 then elem
                        else sprintf "%s%d" elem count)
                    |> String.concat ""
                
                Ok {
                    Smiles = smiles
                    Atoms = atoms.ToArray()
                    Bonds = bonds.ToArray()
                    Formula = formula
                    ParseErrors = List.rev finalState.ErrorsRev
                }
            with ex ->
                Error (QuantumError.Other (sprintf "SMILES parse error: %s" ex.Message))
    
    // ========================================================================
    // MOLECULAR DESCRIPTORS
    // ========================================================================
    
    /// Calculate molecular descriptors from parsed molecule
    let calculateDescriptors (molecule: Molecule) : MolecularDescriptors =
        let atoms = molecule.Atoms
        let bonds = molecule.Bonds
        
        // Molecular weight
        let mw = 
            atoms
            |> Array.sumBy (fun a ->
                match atomicMasses.TryFind(a.Element) with
                | Some mass -> mass + float a.ExplicitHydrogens * 1.008
                | None -> 0.0)
        
        // LogP (Wildman-Crippen estimate)
        let logP =
            atoms
            |> Array.sumBy (fun a ->
                match logPContributions.TryFind(a.Element) with
                | Some contrib -> contrib
                | None -> 0.0)
        
        // Hydrogen bond donors (N-H, O-H)
        let hbdCount =
            atoms
            |> Array.filter (fun a ->
                (a.Element = "N" || a.Element = "O") && a.ExplicitHydrogens > 0)
            |> Array.length
        
        // Hydrogen bond acceptors (N, O)
        let hbaCount =
            atoms
            |> Array.filter (fun a -> a.Element = "N" || a.Element = "O")
            |> Array.length
        
        // Heavy atom count (non-hydrogen)
        let heavyCount = 
            atoms
            |> Array.filter (fun a -> a.Element <> "H")
            |> Array.length
        
        // Rotatable bonds (single bonds between non-terminal heavy atoms)
        let rotatableBonds =
            bonds
            |> Array.filter (fun b ->
                b.Order = 1 &&
                atoms.[b.Atom1].Element <> "H" &&
                atoms.[b.Atom2].Element <> "H")
            |> Array.length
        
        // Aromatic atoms count
        let aromaticCount =
            atoms
            |> Array.filter (fun a -> a.IsAromatic)
            |> Array.length
        
        // Estimate aromatic rings (simple heuristic: aromatic atoms / 6)
        let aromaticRingCount = aromaticCount / 6
        
        // Count sp3 carbons (4 single bonds)
        let carbonCount = atoms |> Array.filter (fun a -> a.Element = "C") |> Array.length
        let sp3Carbons = 
            atoms
            |> Array.filter (fun a -> 
                a.Element = "C" && not a.IsAromatic)
            |> Array.length
        let fractionCsp3 = 
            if carbonCount = 0 then 0.0
            else float sp3Carbons / float carbonCount
        
        // TPSA (simplified estimate)
        let tpsa =
            atoms
            |> Array.sumBy (fun a ->
                match a.Element with
                | "N" -> 26.02
                | "O" -> 9.23 + (if a.ExplicitHydrogens > 0 then 11.0 else 0.0)
                | "S" -> 25.30
                | _ -> 0.0)
        
        // Ring count (Euler formula approximation: rings = edges - vertices + 1)
        let ringCount = max 0 (bonds.Length - atoms.Length + 1)
        
        {
            MolecularWeight = mw
            LogP = logP
            HydrogenBondDonors = hbdCount
            HydrogenBondAcceptors = hbaCount
            RotatableBonds = rotatableBonds
            TPSA = tpsa
            HeavyAtomCount = heavyCount
            AromaticRingCount = aromaticRingCount
            RingCount = ringCount
            FractionCsp3 = fractionCsp3
        }
    
    // ========================================================================
    // MOLECULAR FINGERPRINTS
    // ========================================================================
    
    /// Generate a simple path-based fingerprint (similar to RDKit fingerprints)
    /// 
    /// This is a simplified implementation for educational/prototyping purposes.
    /// Production use should integrate with RDKit or similar.
    let generateFingerprint (molecule: Molecule) (nBits: int) : MolecularFingerprint =
        let setBitIndices : Set<int> =
            seq {
                // Hash atom types
                for atom in molecule.Atoms do
                    let hash = atom.Element.GetHashCode() ^^^ (if atom.IsAromatic then 0x1234 else 0)
                    yield abs (hash) % nBits

                // Hash bond patterns (atom1-bond-atom2)
                for bond in molecule.Bonds do
                    let atom1 = molecule.Atoms.[bond.Atom1]
                    let atom2 = molecule.Atoms.[bond.Atom2]
                    let hash = atom1.Element.GetHashCode() ^^^ (bond.Order * 0x5678) ^^^ atom2.Element.GetHashCode()
                    yield abs (hash) % nBits

                // Hash 2-bond paths
                for bond1 in molecule.Bonds do
                    for bond2 in molecule.Bonds do
                        if bond1.Atom2 = bond2.Atom1 then
                            let a1 = molecule.Atoms.[bond1.Atom1]
                            let a2 = molecule.Atoms.[bond1.Atom2]
                            let a3 = molecule.Atoms.[bond2.Atom2]
                            let hash =
                                a1.Element.GetHashCode() ^^^
                                (bond1.Order * 0x1111) ^^^
                                a2.Element.GetHashCode() ^^^
                                (bond2.Order * 0x2222) ^^^
                                a3.Element.GetHashCode()
                            yield abs (hash) % nBits
            }
            |> Seq.fold (fun s i -> Set.add i s) Set.empty

        let bits = Array.init nBits (fun i -> setBitIndices |> Set.contains i)
        let bitCount = setBitIndices.Count

        { Type = "PathFingerprint"
          Bits = bits
          BitCount = bitCount }
    
    // ========================================================================
    // SIMILARITY CALCULATIONS
    // ========================================================================
    
    /// Calculate Tanimoto similarity between two fingerprints
    let tanimotoSimilarity (fp1: MolecularFingerprint) (fp2: MolecularFingerprint) : float =
        if fp1.Bits.Length <> fp2.Bits.Length then
            failwith "Fingerprints must have same length"
        
        let intersection = 
            Array.zip fp1.Bits fp2.Bits
            |> Array.filter (fun (a, b) -> a && b)
            |> Array.length
        
        let union =
            Array.zip fp1.Bits fp2.Bits
            |> Array.filter (fun (a, b) -> a || b)
            |> Array.length
        
        if union = 0 then 0.0
        else float intersection / float union
    
    /// Calculate Dice similarity between two fingerprints
    let diceSimilarity (fp1: MolecularFingerprint) (fp2: MolecularFingerprint) : float =
        if fp1.Bits.Length <> fp2.Bits.Length then
            failwith "Fingerprints must have same length"
        
        let intersection = 
            Array.zip fp1.Bits fp2.Bits
            |> Array.filter (fun (a, b) -> a && b)
            |> Array.length
        
        if fp1.BitCount + fp2.BitCount = 0 then 0.0
        else 2.0 * float intersection / float (fp1.BitCount + fp2.BitCount)
    
    // ========================================================================
    // DATA LOADING
    // ========================================================================
    
    /// Load molecules from a list of SMILES strings
    let loadFromSmilesList (smilesList: string list) : QuantumResult<MolecularDataset> =
        let results = smilesList |> List.map parseSmiles
        
        let molecules = 
            results 
            |> List.choose (function Ok m -> Some m | Error _ -> None)
            |> List.toArray
        
        let errors =
            results
            |> List.choose (function Error e -> Some e | Ok _ -> None)
        
        if errors.Length > 0 && molecules.Length = 0 then
            Error (QuantumError.ValidationError ("smiles", sprintf "All SMILES failed to parse: %A" errors))
        else
            Ok {
                Molecules = molecules
                Descriptors = None
                Fingerprints = None
                Labels = None
                LabelColumn = None
            }
    
    /// Load molecules from CSV file with SMILES column
    let loadFromCsv 
        (filePath: string) 
        (smilesColumn: string) 
        (labelColumn: string option)
        : QuantumResult<MolecularDataset> =
        
        try
            let lines = System.IO.File.ReadAllLines(filePath)
            if lines.Length < 2 then
                Error (QuantumError.ValidationError ("file", "CSV must have header and at least one data row"))
            else
                let headers = lines.[0].Split(',') |> Array.map (fun s -> s.Trim().Trim('"'))
                
                let smilesIdx = headers |> Array.tryFindIndex (fun h -> h = smilesColumn)
                let labelIdx = labelColumn |> Option.bind (fun col -> headers |> Array.tryFindIndex (fun h -> h = col))
                
                match smilesIdx with
                | None -> Error (QuantumError.ValidationError ("smilesColumn", sprintf "Column '%s' not found" smilesColumn))
                | Some sIdx ->
                    let dataLines = lines.[1..]
                    
                    let molecules = ResizeArray<Molecule>()
                    let labels = ResizeArray<int>()
                    
                    for line in dataLines do
                        let fields = line.Split(',') |> Array.map (fun s -> s.Trim().Trim('"'))
                        if fields.Length > sIdx then
                            match parseSmiles fields.[sIdx] with
                            | Ok mol -> 
                                molecules.Add(mol)
                                match labelIdx with
                                | Some lIdx when fields.Length > lIdx ->
                                    match Int32.TryParse(fields.[lIdx]) with
                                    | true, v -> labels.Add(v)
                                    | false, _ -> labels.Add(0)
                                | _ -> ()
                            | Error _ -> ()
                    
                    Ok {
                        Molecules = molecules.ToArray()
                        Descriptors = None
                        Fingerprints = None
                        Labels = if labels.Count > 0 then Some (labels.ToArray()) else None
                        LabelColumn = labelColumn
                    }
        with ex ->
            Error (QuantumError.Other (sprintf "Failed to read CSV: %s" ex.Message))
    
    // ========================================================================
    // FEATURE EXTRACTION (for ML)
    // ========================================================================
    
    /// Convert descriptors to feature array
    let descriptorsToFeatures (desc: MolecularDescriptors) : float array =
        [|
            desc.MolecularWeight
            desc.LogP
            float desc.HydrogenBondDonors
            float desc.HydrogenBondAcceptors
            float desc.RotatableBonds
            desc.TPSA
            float desc.HeavyAtomCount
            float desc.AromaticRingCount
            float desc.RingCount
            desc.FractionCsp3
        |]
    
    /// Convert fingerprint to feature array (0.0/1.0 encoding)
    let fingerprintToFeatures (fp: MolecularFingerprint) : float array =
        fp.Bits |> Array.map (fun b -> if b then 1.0 else 0.0)
    
    /// Add computed descriptors to dataset
    let withDescriptors (dataset: MolecularDataset) : MolecularDataset =
        let descriptors = dataset.Molecules |> Array.map calculateDescriptors
        { dataset with Descriptors = Some descriptors }
    
    /// Add computed fingerprints to dataset (default 1024 bits)
    let withFingerprints (nBits: int) (dataset: MolecularDataset) : MolecularDataset =
        let fingerprints = dataset.Molecules |> Array.map (fun m -> generateFingerprint m nBits)
        { dataset with Fingerprints = Some fingerprints }
    
    /// Convert dataset to feature matrix for ML training
    let toFeatureMatrix (useDescriptors: bool) (useFingerprints: bool) (dataset: MolecularDataset) 
        : QuantumResult<float array array * int array option> =
        
        if not useDescriptors && not useFingerprints then
            Error (QuantumError.ValidationError ("features", "Must enable descriptors and/or fingerprints"))
        else
            let nMolecules = dataset.Molecules.Length
            
            // Get descriptor features
            let descFeatures =
                if useDescriptors then
                    match dataset.Descriptors with
                    | Some descs -> descs |> Array.map descriptorsToFeatures
                    | None -> 
                        // Calculate on the fly
                        dataset.Molecules 
                        |> Array.map (fun m -> calculateDescriptors m |> descriptorsToFeatures)
                else
                    Array.init nMolecules (fun _ -> [||])
            
            // Get fingerprint features
            let fpFeatures =
                if useFingerprints then
                    match dataset.Fingerprints with
                    | Some fps -> fps |> Array.map fingerprintToFeatures
                    | None ->
                        // Calculate on the fly (1024 bits default)
                        dataset.Molecules
                        |> Array.map (fun m -> generateFingerprint m 1024 |> fingerprintToFeatures)
                else
                    Array.init nMolecules (fun _ -> [||])
            
            // Concatenate features
            let features =
                Array.zip descFeatures fpFeatures
                |> Array.map (fun (d, f) -> Array.concat [d; f])
            
            Ok (features, dataset.Labels)
