// ==============================================================================
// Regenerates the bundled FCIDUMP integrals of the chemistry examples
// ==============================================================================
// Writes, next to this script (or into --out DIR), for every species:
//   <slug>.fcidump   active-space Hamiltonian, STO-3G, core energy as `0 0 0 0`
//   <slug>.xyz       the geometry it was computed at, in Angstrom
// plus manifest.json and README.md (method, per-file table, activation
// energies). README.md in this folder describes the method in prose.
//
// The quantum chemistry runs in Python: PySCF (RHF, gradients, Hessians,
// CASSCF, integrals), SciPy (BFGS minimisation) and geomeTRIC (transition-state
// searches). The script sends short PySCF snippets to one Python process per
// job and does everything else here: which geometry each species gets, the
// saddle-point escapes, stretched bonds, transition-state validation, the
// FCIDUMP/XYZ/JSON/Markdown writing and --check.
//
// Python: --python PATH, else `python` or `python3` on PATH when it imports
// pyscf and geometric, else ~/pyscf-venv/bin/python in WSL. A PATH starting
// with / or ~ runs inside WSL. Tested with PySCF 2.14.0, SciPy 1.18.1,
// geomeTRIC 1.1.1; other versions give slightly different numbers.
//
// Usage:
//   dotnet fsi generate-fcidumps.fsx                  all species, TS searches included
//   dotnet fsi generate-fcidumps.fsx -- --check       regenerate into a temporary
//                                                     folder, compare, write nothing here
//   dotnet fsi generate-fcidumps.fsx -- --only kinugasa-ts,water-cas-2-2
//   dotnet fsi generate-fcidumps.fsx -- --skip-ts     keep the bundled transition states
//   dotnet fsi generate-fcidumps.fsx -- --out DIR --python ~/pyscf-venv/bin/python
//
//   dotnet fsi generate-fcidumps.fsx -- --docs-only   README.md and manifest.json
//                                                     from the existing manifest, no Python
//
// --only and --skip-ts rewrite the selected files only; the manifest and
// README keep the other species' records from the existing manifest.json.
// A transition-state search is capped at 15 minutes (search and validation).
// A search that reaches the bundled TS's saddle point keeps the bundled
// geometry (validated again); a search that fails, runs out of time or does
// not validate falls back to validating the bundled geometry. A TS that does
// not validate either way is left out, never estimated.
//
// --check prints, per species, the largest differences from the bundled files
// and exits with 1 when an energy, geometry or manifest value differs. Active
// orbitals that differ at the same E(CAS) (flat or degenerate CASSCF optima)
// are listed but do not fail it.
// ==============================================================================

#nowarn "3886" // lists of one (name, value) pair

open System
open System.Collections.Generic
open System.Diagnostics
open System.Globalization
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading.Tasks

let inv = CultureInfo.InvariantCulture

/// Angstrom per bohr, as PySCF uses it.
[<Literal>]
let bohr = 0.52917721092

[<Literal>]
let basis = "sto-3g"

// ------------------------------------------------------------------------------
// Species
// ------------------------------------------------------------------------------

/// Starting orbitals of the CASSCF that defines a species' active space.
[<Struct>]
type Active =
    /// Canonical RHF orbitals around the Fermi level and MP2 natural orbitals.
    | Frontier of electrons: int * orbitals: int
    /// The sigma/sigma* pair of bond x-h (0-based atoms), CAS(2,2).
    | Bond of x: int * h: int
    /// Atoms [0, split) and [split, n) are two closed-shell monomers, CAS(4,4):
    /// frontier, MP2 natural and the complex orbitals matching each monomer's HOMO and LUMO.
    | Fragments of split: int

type Species =
    {
        Name: string
        /// Hand-built starting geometry, Angstrom.
        Atoms: string
        Active: Active
    }

type Stretched =
    {
        Name: string
        Source: string
        /// Bonded pair (X, H): the X-H distance is scaled.
        X: int
        H: int
        Factor: float
    }

type TransitionState =
    {
        /// "<route name> TS", so the slug is what AntibioticPrecursorSynthesis.fsx loads.
        Name: string
        /// Starting geometry, Angstrom.
        Start: (string * float[])[]
        /// Distances (i, j, Angstrom) held during the pre-relaxation.
        Constraints: (int * int * float) list
        Forming: (int * int) list
        Breaking: (int * int) list
        /// Distances recorded at both downhill ends.
        Watch: (int * int) list
        Reactants: string list
        Step: string
    }

let frontier22 = Frontier(2, 2)
let frontier44 = Frontier(4, 4)

let sp name atoms active =
    {
        Species.Name = name
        Atoms = atoms
        Active = active
    }

let species =
    [
        // shared small molecules
        sp "Hydrogen (H2) [CAS(2,2)]" "H 0 0 0; H 0 0 0.74" frontier22
        sp "Water [CAS(2,2)]" "O 0 0 0; H 0.757 0.586 0; H -0.757 0.586 0" frontier22
        // beta-lactam routes (AntibioticPrecursorSynthesis.fsx)
        sp "Ketene [CAS(2,2)]" "C 0 0 0; C 0 0 1.314; O 0 0 2.474; H 0 0.94 -0.55; H 0 -0.94 -0.55" frontier22
        sp
            "Methanimine [CAS(2,2)]"
            "C 0 0 0; N 1.273 0 0; H 1.622 0.959 0; H -0.545 0.944 0; H -0.545 -0.944 0"
            frontier22
        sp
            "Aziridine [CAS(2,2)]"
            "C -0.74 0 0; C 0.74 0 0; N 0 0 1.27; H 0 0.95 1.63; H -1.25 0.92 -0.3; H -1.25 -0.92 -0.3; H 1.25 0.92 -0.3; H 1.25 -0.92 -0.3"
            frontier22
        sp "Carbon monoxide [CAS(2,2)]" "C 0 0 0; O 0 0 1.128" frontier22
        sp
            "Formaldonitrone [CAS(2,2)]"
            "C 0 0 0; N 1.29 0 0; O 1.93 1.11 0; H 1.795 -0.875 0; H -0.54 0.94 0; H -0.54 -0.94 0"
            frontier22
        sp "Acetylene [CAS(2,2)]" "C 0 0 0; C 0 0 1.203; H 0 0 -1.063; H 0 0 2.266" frontier22
        sp
            "beta-Alanine [CAS(4,4)]"
            "N 0 0 0; C 1.25 0.85 0; C 2.50 0 0; C 3.75 0.85 0; O 3.75 2.06 0; O 4.95 0.20 0; H 5.70 0.80 0; H -0.45 -0.55 0.75; H -0.45 -0.55 -0.75; H 1.25 1.48 0.89; H 1.25 1.48 -0.89; H 2.50 -0.63 0.89; H 2.50 -0.63 -0.89"
            frontier44
        sp
            "2-Azetidinone [CAS(2,2)]"
            "N 0 0 0; C 1.37 0 0; C 1.37 1.53 0; C 0 1.55 0; O 2.10 -0.73 0; H -0.72 -0.72 0; H 1.87 1.98 0.88; H 1.87 1.98 -0.88; H -0.5 2.0 0.88; H -0.5 2.0 -0.88"
            frontier22
        sp
            "2-Azetidinone [CAS(4,4)]"
            "N 0 0 0; C 1.37 0 0; C 1.37 1.53 0; C 0 1.55 0; O 2.10 -0.73 0; H -0.72 -0.72 0; H 1.87 1.98 0.88; H 1.87 1.98 -0.88; H -0.5 2.0 0.88; H -0.5 2.0 -0.88"
            frontier44
        // redox couples (ElectronTransportChain.fsx)
        sp
            "Pyridine [CAS(2,2)]"
            "N 1.39 0 0; C 0.695 1.204 0; C -0.695 1.204 0; C -1.39 0 0; C -0.695 -1.204 0; C 0.695 -1.204 0; H 1.24 2.14 0; H -1.24 2.14 0; H -2.47 0 0; H -1.24 -2.14 0; H 1.24 -2.14 0"
            frontier22
        sp
            "1,4-Dihydropyridine [CAS(4,4)]"
            "N 1.39 0 0; C 0.695 1.204 0; C -0.695 1.204 0; C -1.45 0 0; C -0.695 -1.204 0; C 0.695 -1.204 0; H 2.40 0 0; H 1.24 2.14 0; H -1.24 2.14 0; H -2.10 0 0.88; H -2.10 0 -0.88; H -1.24 -2.14 0; H 1.24 -2.14 0"
            frontier44
        sp
            "Ethylene [CAS(2,2)]"
            "C -0.667 0 0; C 0.667 0 0; H -1.23 0.92 0; H -1.23 -0.92 0; H 1.23 0.92 0; H 1.23 -0.92 0"
            frontier22
        sp
            "Ethane [CAS(4,4)]"
            "C 0 0 0.765; C 0 0 -0.765; H 1.02 0 1.16; H -0.51 0.883 1.16; H -0.51 -0.883 1.16; H -1.02 0 -1.16; H 0.51 0.883 -1.16; H 0.51 -0.883 -1.16"
            frontier44
        sp
            "p-Benzoquinone [CAS(2,2)]"
            "C 1.43 0 0; C 0.67 1.26 0; C -0.67 1.26 0; C -1.43 0 0; C -0.67 -1.26 0; C 0.67 -1.26 0; O 2.65 0 0; O -2.65 0 0; H 1.23 2.18 0; H -1.23 2.18 0; H -1.23 -2.18 0; H 1.23 -2.18 0"
            frontier22
        sp
            "Hydroquinone [CAS(4,4)]"
            "C 1.39 0 0; C 0.695 1.204 0; C -0.695 1.204 0; C -1.39 0 0; C -0.695 -1.204 0; C 0.695 -1.204 0; O 2.75 0 0; H 3.07 0.91 0; O -2.75 0 0; H -3.07 -0.91 0; H 1.24 2.14 0; H -1.24 2.14 0; H -1.24 -2.14 0; H 1.24 -2.14 0"
            frontier44
        sp
            "Hydrogen peroxide [CAS(2,2)]"
            "O 0 0.734 -0.052; O 0 -0.734 -0.052; H 0.839 0.880 0.422; H -0.839 -0.880 0.422"
            frontier22
        // hydrogen-bonded complexes (BindingAffinity.fsx)
        sp "Hydrogen fluoride [CAS(2,2)]" "F 0 0 0; H 0.917 0 0" frontier22
        sp "Hydrogen sulfide [CAS(2,2)]" "S 0 0 0; H 0.960 0.927 0; H -0.960 0.927 0" frontier22
        sp "Hydrogen chloride [CAS(2,2)]" "Cl 0 0 0; H 0 0 1.275" frontier22
        sp "HF dimer (F-H...F) [CAS(4,4)]" "F 0 0 0; H 0.92 0 0; F 2.75 0 0; H 3.05 0.87 0" (Fragments 2)
        sp
            "HF-H2S (F-H...S) [CAS(4,4)]"
            "F 0 0 0; H 0.92 0 0; S 3.30 0 0; H 3.50 0.93 0.93; H 3.50 0.93 -0.93"
            (Fragments 2)
        sp "HF-HCl (F-H...Cl) [CAS(4,4)]" "F 0 0 0; H 0.92 0 0; Cl 3.20 0 0; H 3.50 1.23 0" (Fragments 2)
        sp
            "HF-H2O (F-H...O) [CAS(4,4)]"
            "F 0 0 0; H 0.92 0 0; O 2.65 0 0; H 3.24 0.757 0; H 3.24 -0.757 0"
            (Fragments 2)
        // bond dissociation (H2OWater.fsx): sigma/sigma* of the stretched bond
        sp "H2O (equilibrium)" "O 0 0 0; H 0.96 0 0; H -0.24 0.93 0" (Bond(0, 1))
        sp "HF (equilibrium)" "H 0 0 0; F 0.92 0 0" (Bond(1, 0))
        sp "LiH (equilibrium)" "Li 0 0 0; H 1.60 0 0" (Bond(0, 1))
    ]

let stretched =
    [
        {
            Name = "H2O (O-H x2.0)"
            Source = "H2O (equilibrium)"
            X = 0
            H = 1
            Factor = 2.0
        }
        {
            Name = "HF (H-F x2.0)"
            Source = "HF (equilibrium)"
            X = 1
            H = 0
            Factor = 2.0
        }
        {
            Name = "LiH (Li-H x2.0)"
            Source = "LiH (equilibrium)"
            X = 0
            H = 1
            Factor = 2.0
        }
    ]

let parseAtoms (atoms: string) =
    atoms.Split ';'
    |> Array.map (fun entry ->
        let parts = entry.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)
        parts.[0], parts.[1..3] |> Array.map (fun v -> Double.Parse(v, inv)))

/// 2-azetidinone at the planar stationary point BFGS reaches from its hand-built
/// start (a saddle point; the ring puckers), Angstrom.
let azetidinonePlanar =
    "N -0.01149907 0.07232309 0; C 1.39747954 0.01470721 0; C 1.45413154 1.57165112 0; "
    + "C -0.10073414 1.53582705 0; O 2.18744082 -0.89977486 0; H -0.73765621 -0.64178575 0; "
    + "H 1.90422313 2.00474119 0.88926093; H 1.90422313 2.00474119 -0.88926093; "
    + "H -0.56880437 1.96378488 0.89052379; H -0.56880437 1.96378488 -0.89052379"

/// Formaldonitrone C0=N1-O2 at its RHF/STO-3G minimum with acetylene placed 2 A
/// above it, parallel to the C0...O2 axis and centred over it (C-C 1.24 A, C-H 1.06 A).
let nitroneBelowAcetylene =
    let nitrone =
        parseAtoms (
            "C -0.03248656 -0.00607415 0; N 1.27873652 0.05656399 0; O 2.05495736 1.12207915 0; "
            + "H 1.75029702 -0.88282653 0; H -0.59658527 0.91377933 0; H -0.51991906 -0.96852178 0"
        )

    let c, o = snd nitrone.[0], snd nitrone.[2]

    let norm (v: float[]) =
        sqrt (v.[0] * v.[0] + v.[1] * v.[1] + v.[2] * v.[2])

    let d = Array.map2 (-) o c
    let u = d |> Array.map (fun x -> x / norm d)
    let m = Array.map2 (fun a b -> (a + b) / 2.0) o c
    let z = [| 0.0; 0.0; 1.0 |]

    let carbon sign =
        Array.init 3 (fun k -> m.[k] + sign * 0.62 * u.[k] + 2.0 * z.[k])

    let hydrogen sign =
        let w = Array.init 3 (fun k -> sign * 0.9 * u.[k] + 0.44 * z.[k])
        let c = carbon sign
        Array.init 3 (fun k -> c.[k] + 1.06 * w.[k] / norm w)

    Array.append nitrone [| "C", carbon -1.0; "C", carbon 1.0; "H", hydrogen -1.0; "H", hydrogen 1.0 |]

let transitionStates =
    [
        // ketene C3=C2=O + methanimine C4=N1: N1-C2 and C3-C4 form (starts from 2-azetidinone)
        {
            Name = "Staudinger [2+2] TS"
            Start = parseAtoms azetidinonePlanar
            Constraints = [ 0, 1, 1.9; 2, 3, 2.6 ]
            Forming = [ 0, 1; 2, 3 ]
            Breaking = []
            Watch = [ 0, 1; 2, 3; 1, 4 ]
            Reactants = [ "Ketene [CAS(2,2)]"; "Methanimine [CAS(2,2)]" ]
            Step = "ketene + methanimine -> 2-azetidinone"
        }
        // 1,3-dipolar cycloaddition, formaldonitrone C0=N1-O2 + acetylene C6#C7 -> 4-isoxazoline:
        // C0-C6 and O2-C7 form. The isoxazoline -> beta-lactam rearrangement is a later step.
        {
            Name = "Kinugasa TS"
            Start = nitroneBelowAcetylene
            Constraints = [ 0, 6, 2.2; 2, 7, 2.2 ]
            Forming = [ 0, 6; 2, 7 ]
            Breaking = []
            Watch = [ 0, 6; 2, 7; 6, 7; 1, 2 ]
            Reactants = [ "Formaldonitrone [CAS(2,2)]"; "Acetylene [CAS(2,2)]" ]
            Step = "nitrone + acetylene -> 4-isoxazoline (first step; the rearrangement to the lactam follows)"
        }
        // beta-alanine -> 2-azetidinone + water in one four-centre step: N0-C1 and O10-H11 form,
        // C1-O10 and N0-H11 break (starts from 2-azetidinone + water)
        {
            Name = "beta-Amino Acid Cyclization TS"
            Start = parseAtoms (azetidinonePlanar + "; O 1.5 0.2 1.9; H 0.8 0.1 1.3; H 1.9 0.9 2.4")
            Constraints = [ 1, 10, 1.9; 0, 11, 1.3; 10, 11, 1.2; 0, 1, 1.7 ]
            Forming = [ 0, 1; 10, 11 ]
            Breaking = [ 1, 10; 0, 11 ]
            Watch = [ 0, 1; 1, 10; 0, 11; 10, 11 ]
            Reactants = [ "beta-Alanine [CAS(4,4)]" ]
            Step = "beta-alanine -> 2-azetidinone + water"
        }
    ]

/// Wall-clock cap per transition state: search and validation.
[<Literal>]
let tsSeconds = 900.0

/// Angstrom: a forming bond is made, a breaking bond still intact.
[<Literal>]
let tsBonded = 1.7

/// Angstrom: a forming bond not yet made, a breaking bond broken.
[<Literal>]
let tsApart = 1.8

let tsValidation =
    "exactly one imaginary frequency; minimising from the TS displaced 0.15 A both ways along that mode "
    + $"ends once with every forming bond below {tsBonded} A and every breaking bond above {tsApart} A "
    + "(product) and once the other way round (reactant)"

let private slugRegex = Regex "[^a-z0-9]+"

/// Same rule as ChemistryIntegrals.speciesSlug in examples/_common.
let slug (name: string) =
    slugRegex.Replace(name.ToLowerInvariant(), "-").Trim '-'

// ------------------------------------------------------------------------------
// Python-compatible number formatting and JSON
// ------------------------------------------------------------------------------

/// Sign, significant digits (no leading or trailing zeros) and decimal-point
/// position (value = 0.digits * 10^point) of a .NET-formatted number.
let decompose (text: string) =
    let negative = text.StartsWith "-"
    let text = text.TrimStart '-'

    let mantissa, exponent =
        match text.IndexOfAny [| 'E'; 'e' |] with
        | -1 -> text, 0
        | k -> text.Substring(0, k), int (text.Substring(k + 1))

    let whole, fraction =
        match mantissa.IndexOf '.' with
        | -1 -> mantissa, ""
        | k -> mantissa.Substring(0, k), mantissa.Substring(k + 1)

    let digits = whole + fraction
    let trimmed = digits.TrimStart '0'
    negative, trimmed.TrimEnd '0', whole.Length + exponent - (digits.Length - trimmed.Length)

let private exponentText (e: int) =
    (if e < 0 then "e-" else "e+") + (abs e).ToString("00", inv)

let private mantissaText (digits: string) =
    if digits.Length = 1 then
        digits
    else
        digits.Substring(0, 1) + "." + digits.Substring 1

let private fixedText (digits: string) (point: int) =
    if point <= 0 then
        "0." + String('0', -point) + digits
    elif point >= digits.Length then
        digits + String('0', point - digits.Length)
    else
        digits.Substring(0, point) + "." + digits.Substring point

/// Python's repr(float): shortest round-trip digits.
let pyRepr (x: float) =
    let negative, digits, point = decompose (x.ToString("R", inv))
    let sign = if negative then "-" else ""

    if digits = "" then
        sign + "0.0"
    elif point > 16 || point < -3 then
        sign + mantissaText digits + exponentText (point - 1)
    elif point >= digits.Length then
        sign + fixedText digits point + ".0"
    else
        sign + fixedText digits point

/// Python's "%.{precision}g".
let pyG (precision: int) (x: float) =
    let negative, digits, point =
        decompose (x.ToString("E" + string (precision - 1), inv))

    let sign = if negative then "-" else ""

    if digits = "" then
        sign + "0"
    elif point - 1 < -4 || point - 1 >= precision then
        sign + mantissaText digits + exponentText (point - 1)
    else
        sign + fixedText digits point

/// Python's "{x:.{decimals}e}".
let pyE (decimals: int) (x: float) =
    let text = x.ToString("E" + string decimals, inv)
    let k = text.IndexOf 'E'
    text.Substring(0, k) + exponentText (int (text.Substring(k + 1)))

/// Python's "{x:.{decimals}f}".
let pyF (decimals: int) (x: float) = x.ToString("F" + string decimals, inv)

/// Python's round(x, decimals).
let pyRound (decimals: int) (x: float) = Double.Parse(pyF decimals x, inv)

type Json =
    | JNum of float
    | JInt of int
    | JStr of string
    | JBool of bool
    | JList of Json list
    | JObj of (string * Json) list

let private quote (s: string) =
    let b = StringBuilder("\"")

    for c in s do
        match c with
        | '"' -> b.Append "\\\"" |> ignore
        | '\\' -> b.Append "\\\\" |> ignore
        | '\n' -> b.Append "\\n" |> ignore
        | '\r' -> b.Append "\\r" |> ignore
        | '\t' -> b.Append "\\t" |> ignore
        | c when c < ' ' || int c > 127 -> b.AppendFormat(inv, "\\u{0:x4}", int c) |> ignore
        | c -> b.Append c |> ignore

    b.Append('"').ToString()

/// Python's json.dumps(value, indent=2); with no indent, one line.
let rec pyJson (indent: int option) (value: Json) =
    let items (opening: string) (closing: string) (parts: string list) =
        match indent with
        | _ when parts.IsEmpty -> opening + closing
        | None -> opening + String.Join(", ", parts) + closing
        | Some n ->
            let pad = String(' ', 2 * (n + 1))

            opening
            + "\n"
            + String.Join(",\n", parts |> List.map (fun p -> pad + p))
            + "\n"
            + String(' ', 2 * n)
            + closing

    let inner = indent |> Option.map ((+) 1)

    match value with
    | JNum x -> pyRepr x
    | JInt i -> string i
    | JStr s -> quote s
    | JBool b -> if b then "true" else "false"
    | JList xs -> xs |> List.map (pyJson inner) |> items "[" "]"
    | JObj fields ->
        fields
        |> List.map (fun (k, v) -> quote k + ": " + pyJson inner v)
        |> items "{" "}"

/// A JSON value read back, keeping integers (no '.' or exponent) apart from floats.
let rec ofElement (e: JsonElement) =
    match e.ValueKind with
    | JsonValueKind.Number ->
        let raw = e.GetRawText()

        if raw.IndexOfAny [| '.'; 'e'; 'E' |] >= 0 then
            JNum(e.GetDouble())
        else
            JInt(e.GetInt32())
    | JsonValueKind.String -> JStr(e.GetString())
    | JsonValueKind.True -> JBool true
    | JsonValueKind.False -> JBool false
    | JsonValueKind.Array -> JList [ for x in e.EnumerateArray() -> ofElement x ]
    | JsonValueKind.Object -> JObj [ for p in e.EnumerateObject() -> p.Name, ofElement p.Value ]
    | kind -> failwithf "unexpected JSON %A" kind

let field (name: string) (record: (string * Json) list) =
    record |> List.tryPick (fun (k, v) -> if k = name then Some v else None)

let str name record =
    match field name record with
    | Some(JStr s) -> s
    | _ -> failwithf "no string %s" name

let num name record =
    match field name record with
    | Some(JNum x) -> x
    | Some(JInt i) -> float i
    | _ -> failwithf "no number %s" name

let ofFloats (xs: float seq) = JList [ for x in xs -> JNum x ]
let ofMatrix (m: float[][]) = JList [ for row in m -> ofFloats row ]

// ------------------------------------------------------------------------------
// Python engine: one process, one JSON request per line, one JSON reply per line
// ------------------------------------------------------------------------------

/// Everything PySCF, SciPy and geomeTRIC do. Replies go to the saved stdout; the
/// libraries' own output goes to stderr. A request with "seconds" ends the
/// process (exit 124) when it runs longer than that.
let engineCode =
    """
import json, os, sys, tempfile, threading

reply = os.fdopen(os.dup(1), "w")
os.dup2(2, 1)
sys.stdout = sys.stderr

import numpy as np
import geometric, pyscf, scipy
from pyscf import ao2mo, gto, mcscf, mp, scf
from pyscf.geomopt.geometric_solver import kernel as geometric_kernel
from pyscf.hessian import thermo
from scipy.optimize import minimize

BOHR = 0.52917721092


def mol_of(symbols, coords, unit="Bohr"):
    atoms = [(s, tuple(c)) for s, c in zip(symbols, coords)]
    return gto.M(atom=atoms, basis="sto-3g", unit=unit, charge=0, spin=0, verbose=0)


def rhf(mol, conv=1e-11, dm0=None):
    mf = scf.RHF(mol)
    mf.conv_tol = conv
    mf.max_cycle = 200
    mf.kernel(dm0=dm0)
    if not mf.converged:
        raise RuntimeError("RHF did not converge")
    return mf


def versions():
    return {"pyscf": pyscf.__version__, "scipy": scipy.__version__, "geometric": geometric.__version__}


def minimum(symbols, coords):
    # RHF minimum from analytic gradients and SciPy BFGS, bohr
    state = {"dm": None}

    def energy_and_gradient(x):
        mf = rhf(mol_of(symbols, x.reshape(-1, 3)), dm0=state["dm"])
        state["dm"] = mf.make_rdm1()
        return mf.e_tot, mf.nuc_grad_method().kernel().ravel()

    options = {"gtol": 1e-5, "maxiter": 1000}
    result = minimize(energy_and_gradient, np.asarray(coords).ravel(), jac=True, method="BFGS", options=options)
    gradient = float(np.abs(result.jac).max())
    if gradient > 1e-4:
        raise RuntimeError(f"geometry optimisation stopped with max gradient {gradient:.2e}")
    return {"coords": result.x.reshape(-1, 3).tolist(), "gradient": gradient}


def frequencies(symbols, coords):
    # harmonic wavenumbers (cm-1, imaginary ones negative) and normal modes from the analytic Hessian
    mol = mol_of(symbols, coords)
    mf = rhf(mol)
    analysis = thermo.harmonic_analysis(mol, mf.Hessian().kernel())
    wavenumbers = [float(-f.imag if abs(f.imag) > 0 else f.real) for f in np.atleast_1d(analysis["freq_wavenumber"])]
    return {"wavenumbers": wavenumbers, "modes": np.asarray(analysis["norm_mode"]).tolist()}


def frontier_start(mf, nelec, norb):
    nocc = mf.mol.nelectron // 2
    return list(range(nocc - nelec // 2, nocc)) + list(range(nocc, nocc + norb - nelec // 2))


def mp2_natural_start(mf, nelec, norb):
    occupation, coeff = mcscf.addons.make_natural_orbitals(mp.MP2(mf).run())
    order = np.argsort(-occupation)
    nocc = mf.mol.nelectron // 2
    occupied, virtual = list(order[:nocc]), list(order[nocc:])
    core, active_occ = occupied[: nocc - nelec // 2], occupied[nocc - nelec // 2 :]
    active_vir, rest = virtual[: norb - nelec // 2], virtual[norb - nelec // 2 :]
    return coeff[:, core + active_occ + active_vir + rest]


def bond_start(mf, h):
    mol = mf.mol
    h_ao = [i for i, label in enumerate(mol.ao_labels()) if label.split()[0] == str(h)]
    weight = (mf.mo_coeff[h_ao, :] ** 2).sum(axis=0)
    nocc = mol.nelectron // 2
    return [int(np.argmax(weight[:nocc])), nocc + int(np.argmax(weight[nocc:]))]


def fragment_start(mf, symbols, coords, split):
    mol = mf.mol
    s = mol.intor("int1e_ovlp")
    ao_ranges = mol.aoslice_by_atom()[:, 2:4]
    nocc = mol.nelectron // 2
    chosen = []
    for atoms in (list(range(0, split)), list(range(split, len(symbols)))):
        frag = rhf(mol_of([symbols[a] for a in atoms], coords[atoms]))
        frag_nocc = frag.mol.nelectron // 2
        ao = np.concatenate([np.arange(*ao_ranges[a]) for a in atoms])
        for frag_index, occupied in ((frag_nocc - 1, True), (frag_nocc, False)):
            v = np.zeros(mol.nao)
            v[ao] = frag.mo_coeff[:, frag_index]
            overlap = np.abs(v @ s @ mf.mo_coeff)
            candidates = range(0, nocc) if occupied else range(nocc, mol.nao)
            chosen.append(max((k for k in candidates if k not in chosen), key=lambda k: overlap[k]))
    return sorted(chosen)


def export(symbols, coords, kind, electrons, orbitals, h=0, split=0):
    # lowest CASSCF over the starts, then the CASCI active-space Hamiltonian on its orbitals
    coords = np.asarray(coords)
    mf = rhf(mol_of(symbols, coords))
    if kind == "bond":
        starts = [bond_start(mf, h)]
    else:
        starts = [frontier_start(mf, electrons, orbitals), mp2_natural_start(mf, electrons, orbitals)]
        if kind == "fragments":
            starts.append(fragment_start(mf, symbols, coords, split))
    best = None
    for start in starts:
        mc = mcscf.CASSCF(mf, orbitals, electrons)
        mc.conv_tol = 1e-10
        mc.max_cycle_macro = 200
        mc.kernel(mc.sort_mo([k + 1 for k in start]) if isinstance(start, list) else start)
        if mc.converged and (best is None or mc.e_tot < best.e_tot - 1e-8):
            best = mc
    if best is None:
        raise RuntimeError(f"CASSCF({electrons},{orbitals}) did not converge from any start")
    casci = mcscf.CASCI(mf, orbitals, electrons)
    casci.mo_coeff = best.mo_coeff
    e_cas = casci.kernel()[0]
    h1, ecore = casci.get_h1eff()
    h2 = ao2mo.restore(4, casci.get_h2eff(), orbitals)
    return {"e_rhf": mf.e_tot, "e_cas": e_cas, "ecore": float(ecore), "h1": h1.tolist(), "h2": h2.tolist()}


def geometric_run(symbols, coords, unit, require=True, **options):
    mf = rhf(mol_of(symbols, coords, unit), 1e-10)
    converged, mol = geometric_kernel(mf, **options)
    if require and not converged:
        raise RuntimeError(f"geomeTRIC did not converge in {options['maxsteps']} steps")
    return mol


def ts_search(symbols, start, constraints):
    # constrained pre-relaxation, then geomeTRIC's saddle-point search from the analytic Hessian, Angstrom
    guess = np.asarray(start)
    if constraints:
        with tempfile.TemporaryDirectory() as work:
            path = os.path.join(work, "constraints.txt")
            with open(path, "w") as f:
                f.write("$set\n" + "".join(f"distance {i + 1} {j + 1} {d}\n" for i, j, d in constraints))
            relaxed = geometric_run(symbols, guess, "Angstrom", require=False, constraints=path, maxsteps=300)
            guess = relaxed.atom_coords(unit="Angstrom")
    ts = geometric_run(symbols, guess, "Angstrom", transition=True, hessian="first", maxsteps=300)
    return {"coords": ts.atom_coords(unit="Angstrom").tolist()}


def downhill(symbols, coords, mode):
    # minimise from the TS displaced 0.15 A (largest atom) both ways along the mode, bohr
    step = (0.15 / BOHR) * np.asarray(mode) / np.abs(mode).max()
    ends = []
    for sign in (1, -1):
        mol = geometric_run(symbols, np.asarray(coords) + sign * step, "Bohr", maxsteps=500)
        ends.append({"coords": mol.atom_coords(unit="Bohr").tolist(), "e_rhf": rhf(mol, 1e-10).e_tot})
    return {"ends": ends}


def fci(h1, h2, ecore, orbitals, electrons):
    # lowest eigenvalues of an active-space Hamiltonian (for --check)
    from pyscf.fci import direct_spin1

    eri = ao2mo.restore(1, np.asarray(h2), orbitals)
    e, _ = direct_spin1.FCI().kernel(np.asarray(h1), eri, orbitals, electrons, ecore=ecore, nroots=4)
    return {"energies": [float(x) for x in np.atleast_1d(e)]}


OPS = {f.__name__: f for f in (versions, minimum, frequencies, export, ts_search, downhill, fci)}

for line in sys.stdin:
    request = json.loads(line)
    timer = None
    if request.get("seconds"):
        timer = threading.Timer(request["seconds"], lambda: os._exit(124))
        timer.daemon = True
        timer.start()
    try:
        answer = {"ok": True, **OPS[request["op"]](**request.get("args", {}))}
    except Exception as error:
        answer = {"ok": False, "error": f"{type(error).__name__}: {error}"}
    if timer:
        timer.cancel()
    reply.write(json.dumps(answer) + "\n")
    reply.flush()
"""

/// Reads the rest of the process's first stdin line as the program.
[<Literal>]
let bootstrap = "import sys,json;exec(json.loads(sys.stdin.readline()))"

type Python =
    {
        Command: string
        InWsl: bool
    }

    member p.Describe = if p.InWsl then $"WSL {p.Command}" else p.Command

/// One thread per process: PySCF's threaded reductions make CASSCF on degenerate
/// orbitals land on different solutions from run to run, and these systems are too
/// small to gain from threads.
let startInfo (python: Python) (pythonArgs: string list) =
    let psi = ProcessStartInfo()

    if python.InWsl then
        let args =
            pythonArgs
            |> List.map (fun a -> "'" + a.Replace("'", "'\\''") + "'")
            |> String.concat " "

        psi.FileName <- "wsl.exe"

        for a in
            [
                "-e"
                "bash"
                "-lc"
                $"cd /tmp && OMP_NUM_THREADS=1 OPENBLAS_NUM_THREADS=1 MKL_NUM_THREADS=1 exec {python.Command} {args}"
            ] do
            psi.ArgumentList.Add a
    else
        psi.FileName <- python.Command
        psi.WorkingDirectory <- Path.GetTempPath()

        for v in [ "OMP_NUM_THREADS"; "OPENBLAS_NUM_THREADS"; "MKL_NUM_THREADS" ] do
            psi.Environment.[v] <- "1"

        for a in pythonArgs do
            psi.ArgumentList.Add a

    psi.UseShellExecute <- false
    psi.RedirectStandardInput <- true
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.StandardInputEncoding <- UTF8Encoding false
    psi.StandardOutputEncoding <- UTF8Encoding false
    psi.CreateNoWindow <- true
    psi

exception EngineTimeout of string

let describe (error: exn) =
    match error with
    | EngineTimeout message -> message
    | error -> error.Message

/// A Python process running engineCode.
type Engine(python: Python, label: string) =
    let proc = Process.Start(startInfo python [ "-u"; "-c"; bootstrap ])
    let tail = Queue<string>()

    do
        proc.ErrorDataReceived.Add(fun e ->
            if not (isNull e.Data) then
                lock tail (fun () ->
                    tail.Enqueue e.Data

                    if tail.Count > 30 then
                        tail.Dequeue() |> ignore))

        proc.BeginErrorReadLine()
        proc.StandardInput.NewLine <- "\n"
        proc.StandardInput.WriteLine(JsonSerializer.Serialize engineCode)
        proc.StandardInput.Flush()

    /// Runs one operation; seconds caps it (the Python side exits, this side kills as a backstop).
    member _.Call(op: string, args: (string * Json) list, ?seconds: float) : JsonElement =
        let request =
            JObj(
                [ "op", JStr op; "args", JObj args ]
                @ (seconds |> Option.map (fun s -> [ "seconds", JNum s ]) |> Option.defaultValue [])
            )

        proc.StandardInput.WriteLine(pyJson None request)
        proc.StandardInput.Flush()
        let pending = proc.StandardOutput.ReadLineAsync()

        let finished =
            match seconds with
            | Some s -> pending.Wait(TimeSpan.FromSeconds(s + 60.0))
            | None -> pending.Wait System.Threading.Timeout.InfiniteTimeSpan

        if not finished then
            proc.Kill true
            raise (EngineTimeout $"{label}: {op} ran out of time")

        match pending.Result with
        | null ->
            proc.WaitForExit()

            if proc.ExitCode = 124 then
                raise (EngineTimeout $"{label}: {op} ran out of time ({seconds |> Option.defaultValue 0.0:F0} s)")

            let log = lock tail (fun () -> String.Join("\n  ", tail))
            failwithf "%s: Python exited with code %d during %s:\n  %s" label proc.ExitCode op log
        | line ->
            let reply = JsonDocument.Parse(line).RootElement

            if reply.GetProperty("ok").GetBoolean() then
                reply
            else
                failwithf "%s: %s" op (reply.GetProperty("error").GetString())

    interface IDisposable with
        member _.Dispose() =
            try
                if not proc.HasExited then
                    proc.StandardInput.Close()

                    if not (proc.WaitForExit 10000) then
                        proc.Kill true
            with _ ->
                ()

            proc.Dispose()

let canImport (python: Python) =
    try
        use p = Process.Start(startInfo python [ "-c"; "import pyscf, geometric, scipy" ])
        p.StandardInput.Close()
        p.StandardOutput.ReadToEnd() |> ignore
        p.StandardError.ReadToEnd() |> ignore
        p.WaitForExit 120000 && p.ExitCode = 0
    with _ ->
        false

let findPython (requested: string option) =
    let ofPath (path: string) =
        {
            Command = path
            InWsl = path.StartsWith "/" || path.StartsWith "~"
        }

    let candidates =
        match requested with
        | Some path -> [ ofPath path ]
        | None ->
            [
                ofPath "python"
                ofPath "python3"
                {
                    Command = "~/pyscf-venv/bin/python"
                    InWsl = true
                }
            ]

    match candidates |> List.tryFind canImport with
    | Some python -> python
    | None ->
        failwithf
            "No Python that imports pyscf, scipy and geometric (tried %s). Regenerating the data needs them: install with `python -m pip install pyscf geometric scipy` (Linux, macOS or WSL; PySCF has no native Windows build) and pass --python PATH if that Python isn't on the PATH. The examples themselves only read the bundled files and need none of this."
            (candidates |> List.map (fun p -> p.Describe) |> String.concat ", ")

// ------------------------------------------------------------------------------
// Geometry helpers (bohr, as PySCF works)
// ------------------------------------------------------------------------------

type Geometry =
    {
        Symbols: string[]
        /// Cartesian coordinates, bohr.
        Coords: float[][]
    }

let toBohr (atoms: (string * float[])[]) =
    {
        Symbols = atoms |> Array.map fst
        Coords = atoms |> Array.map (snd >> Array.map (fun v -> v / bohr))
    }

let xyzLine (symbol: string) (c: float[]) =
    sprintf "%-2s %14s %14s %14s" symbol (pyF 8 (c.[0] * bohr)) (pyF 8 (c.[1] * bohr)) (pyF 8 (c.[2] * bohr))

/// The geometry as its .xyz file records it (8 decimals in Angstrom).
let asWritten (g: Geometry) =
    { g with
        Coords =
            g.Coords
            |> Array.map (Array.map (fun v -> Double.Parse(pyF 8 (v * bohr), inv) / bohr))
    }

let readXyz (path: string) =
    File.ReadAllLines path
    |> Array.skip 2
    |> Array.filter (fun l -> l.Trim() <> "")
    |> Array.map (fun l ->
        let parts = l.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)
        parts.[0], parts.[1..3] |> Array.map (fun v -> Double.Parse(v, inv)))
    |> toBohr

let distance (coords: float[][]) (i: int) (j: int) =
    let d = Array.map2 (-) coords.[i] coords.[j]
    sqrt (d |> Array.sumBy (fun v -> v * v)) * bohr

let floats (e: JsonElement) =
    [| for x in e.EnumerateArray() -> x.GetDouble() |]

let matrix (e: JsonElement) =
    [| for row in e.EnumerateArray() -> floats row |]

let geometryArgs (g: Geometry) =
    [
        "symbols", JList [ for s in g.Symbols -> JStr s ]
        "coords", ofMatrix g.Coords
    ]

// ------------------------------------------------------------------------------
// Pipeline
// ------------------------------------------------------------------------------

type Outcome =
    {
        Record: (string * Json) list
        Fcidump: string
        Xyz: string
        /// How a transition state was obtained, for the report.
        Path: string option
    }

let fcidumpText (norb: int) (nelec: int) (h1: float[][]) (h2: float[][]) (ecore: float) =
    let b = StringBuilder()

    let line (value: float) (indices: string) =
        b.Append(" " + pyG 16 value + indices + "\n") |> ignore

    b.Append($" &FCI NORB=%4d{norb},NELEC=%2d{nelec},MS2=%d{0},\n") |> ignore
    b.Append("  ORBSYM=" + String.replicate norb "1," + "\n") |> ignore
    b.Append("  ISYM=1,\n &END\n") |> ignore

    let pairs =
        [
            for i in 0 .. norb - 1 do
                for j in 0..i -> i, j
        ]

    pairs
    |> List.iteri (fun ij (i, j) ->
        pairs
        |> List.iteri (fun kl (k, l) ->
            if abs h2.[ij].[kl] > 1e-15 then
                line h2.[ij].[kl] (sprintf " %4d %4d %4d %4d" (i + 1) (j + 1) (k + 1) (l + 1))))

    for i, j in pairs do
        if abs h1.[i].[j] > 1e-15 then
            line h1.[i].[j] (sprintf " %4d %4d  0  0" (i + 1) (j + 1))

    line ecore "  0  0  0  0"
    b.ToString()

type Versions =
    {
        Pyscf: string
        Scipy: string
        Geometric: string
    }

/// Active-space Hamiltonian and outputs of one species, computed at the geometry
/// exactly as its .xyz file records it.
let export (engine: Engine) (versions: Versions) name (g: Geometry) active note extra path =
    let g = asWritten g

    let kind, electrons, orbitals, rest, label =
        match active with
        | Frontier(e, o) -> "frontier", e, o, [], $"lowest CASSCF({e},{o}) from frontier and MP2 natural orbitals"
        | Fragments split ->
            "fragments",
            4,
            4,
            [ "split", JInt split ],
            "lowest CASSCF(4,4) from frontier, MP2 natural and monomer-matched orbitals"
        | Bond(x, h) ->
            "bond",
            2,
            2,
            [ "h", JInt h ],
            $"CASSCF(2,2) from the sigma/sigma* orbitals of bond {g.Symbols.[x]}{x + 1}-{g.Symbols.[h]}{h + 1}"

    let reply =
        engine.Call(
            "export",
            geometryArgs g
            @ [ "kind", JStr kind; "electrons", JInt electrons; "orbitals", JInt orbitals ]
            @ rest
        )

    let eRhf, eCas =
        reply.GetProperty("e_rhf").GetDouble(), reply.GetProperty("e_cas").GetDouble()

    let s = slug name

    let xyz =
        $"{g.Symbols.Length}\n{name} | RHF/STO-3G {note} | PySCF {versions.Pyscf}\n"
        + String.Join("", Array.map2 (fun sym c -> xyzLine sym c + "\n") g.Symbols g.Coords)

    printfn "%-40s RHF %.8f  CAS %.8f  (%de,%do)" name eRhf eCas electrons orbitals

    {
        Record =
            [
                "name", JStr name
                "file", JStr(s + ".fcidump")
                "geometry", JStr(s + ".xyz")
                "geometry_source", JStr note
                "basis", JStr basis
                "active_electrons", JInt electrons
                "active_orbitals", JInt orbitals
                "active_space", JStr label
                "qubits", JInt(2 * orbitals)
                "e_rhf", JNum eRhf
                "e_cas", JNum eCas
            ]
            @ extra
        Fcidump =
            fcidumpText
                orbitals
                electrons
                (matrix (reply.GetProperty "h1"))
                (matrix (reply.GetProperty "h2"))
                (reply.GetProperty("ecore").GetDouble())
        Xyz = xyz
        Path = path
    }

let frequencies (engine: Engine) (g: Geometry) =
    let reply = engine.Call("frequencies", geometryArgs g)
    floats (reply.GetProperty "wavenumbers"), [| for m in reply.GetProperty("modes").EnumerateArray() -> matrix m |]

let lowestIndex (w: float[]) =
    w |> Array.mapi (fun i v -> v, i) |> Array.min |> snd

/// RHF/STO-3G minimum with its geometry note and lowest frequency. BFGS from the
/// hand-built start; the Hessian at the written geometry checks it. While a
/// frequency is imaginary beyond 30i cm-1, step 0.1 A along that mode and minimise again.
let minimumOf (engine: Engine) (sp: Species) =
    let start = toBohr (parseAtoms sp.Atoms)

    let bfgs (g: Geometry) =
        let reply = engine.Call("minimum", geometryArgs g)

        { g with
            Coords = matrix (reply.GetProperty "coords")
        },
        reply.GetProperty("gradient").GetDouble()

    let first, gradient = bfgs start
    let w, modes = frequencies engine (asWritten first)

    if w.Length = 0 || Array.min w > -30.0 then
        first, $"optimised (max gradient {pyE 1 gradient} Ha/bohr)", Array.tryHead (Array.sort w)
    else
        let rec escape (g: Geometry) (w: float[]) (modes: float[][][]) attempt =
            if Array.min w > -30.0 then
                g, $"optimised, minimum verified (lowest frequency {pyF 0 (Array.min w)} cm-1)", Some(Array.min w)
            elif attempt = 4 then
                failwithf "%s: no minimum after four saddle-point escapes" sp.Name
            else
                let mode = modes.[lowestIndex w]
                let largest = mode |> Array.collect id |> Array.map abs |> Array.max

                let displaced =
                    { g with
                        Coords = Array.map2 (Array.map2 (fun c m -> c + (0.1 / bohr) * m / largest)) g.Coords mode
                    }

                let next, _ = bfgs displaced
                let w, modes = frequencies engine next
                escape next w modes (attempt + 1)

        escape (asWritten first) w modes 0

let basin (ts: TransitionState) (coords: float[][]) =
    let d (i, j) = distance coords i j

    let made =
        (ts.Forming |> List.forall (fun b -> d b < tsBonded))
        && (ts.Breaking |> List.forall (fun b -> d b > tsApart))

    let undone =
        (ts.Forming |> List.forall (fun b -> d b > tsApart))
        && (ts.Breaking |> List.forall (fun b -> d b < tsBonded))

    if made then "product"
    elif undone then "reactant"
    else "intermediate"

/// Largest difference between the interatomic distances of two geometries, Angstrom:
/// blind to translation, rotation and mirror images.
let shapeDeviation (a: Geometry) (b: Geometry) =
    if a.Symbols <> b.Symbols then
        infinity
    else
        let n = a.Symbols.Length

        seq {
            for i in 0 .. n - 1 do
                for j in i + 1 .. n - 1 -> abs (distance a.Coords i j - distance b.Coords i j)
        }
        |> Seq.fold max 0.0

/// Two TS geometries within this (Angstrom, every interatomic distance) are the
/// same saddle point: geomeTRIC's default convergence leaves about 2e-3 A, soft
/// modes up to 0.02 A; a different saddle point differs by tenths of an Angstrom.
[<Literal>]
let tsSameSaddle = 0.05

/// Constrained pre-relaxation from the start, then geomeTRIC's saddle-point search.
let searchTs (engine: Engine) (ts: TransitionState) (deadline: DateTime) =
    let reply =
        engine.Call(
            "ts_search",
            [
                "symbols", JList [ for s, _ in ts.Start -> JStr s ]
                "start", JList [ for _, c in ts.Start -> ofFloats c ]
                "constraints", JList [ for i, j, d in ts.Constraints -> JList [ JInt i; JInt j; JNum d ] ]
            ],
            max 1.0 (deadline - DateTime.UtcNow).TotalSeconds
        )

    toBohr (Array.map2 (fun (s, _) c -> s, c) ts.Start (matrix (reply.GetProperty "coords")))

/// Exactly one imaginary frequency, and minimising 0.15 A both ways along its mode
/// ends once in the product and once in the reactant basin (product end first).
let validateTs (engine: Engine) (ts: TransitionState) (g: Geometry) (deadline: DateTime) =
    let w, modes = frequencies engine g
    let imaginary = w |> Array.filter (fun v -> v < 0.0)

    if imaginary.Length <> 1 then
        failwithf "%d imaginary frequencies at the stationary point" imaginary.Length

    let ends =
        engine.Call(
            "downhill",
            geometryArgs g @ [ "mode", ofMatrix modes.[lowestIndex w] ],
            max 1.0 (deadline - DateTime.UtcNow).TotalSeconds
        )
        |> fun r ->
            [
                for e in r.GetProperty("ends").EnumerateArray() ->
                    let coords = matrix (e.GetProperty "coords")
                    basin ts coords, coords, e.GetProperty("e_rhf").GetDouble()
            ]
        |> List.sortBy (fun (kind, _, _) -> kind)

    match ends |> List.map (fun (kind, _, _) -> kind) with
    | [ "product"; "reactant" ] -> ()
    | kinds -> failwithf "downhill from the TS reaches %A, not the reactant and product basins" kinds

    let sorted = Array.sort w

    let downhill =
        [
            for _, coords, e in ends ->
                JObj
                    [
                        "e_rhf", JNum e
                        "distances_angstrom",
                        JObj [ for i, j in ts.Watch -> $"{i}-{j}", JNum(pyRound 3 (distance coords i j)) ]
                    ]
        ]

    sorted.[0], sorted.[1], downhill

// ------------------------------------------------------------------------------
// README
// ------------------------------------------------------------------------------

let readme (versions: Versions) (records: (string * Json) list list) =
    let byName = records |> List.map (fun r -> str "name" r, r) |> Map.ofList
    let kcal = 627.509

    let header =
        [
            "# Bundled FCIDUMP integrals for the chemistry examples"
            ""
            "Molecular integrals the chemistry examples (`AntibioticPrecursorSynthesis.fsx`,"
            "`BindingAffinity.fsx`, `ElectronTransportChain.fsx`, `H2OWater.fsx`) run VQE on by default."
            "Each species has `<slug>.fcidump` (active-space Hamiltonian, STO-3G, core energy included"
            "as the `0 0 0 0` line) and `<slug>.xyz` (the geometry it was computed at, in Angstrom)."
            "The slug is the species name lower-cased with every run of other characters replaced by"
            "`-` (`ChemistryIntegrals.speciesSlug` in `examples/_common`). `manifest.json` lists every"
            "file with its geometry, active space and energies."
            ""
            "## Regenerating"
            ""
            "`generate-fcidumps.fsx` in this folder regenerates every file here, this README and"
            "`manifest.json` included, by the method below:"
            ""
            "    dotnet fsi generate-fcidumps.fsx                  # everything, TS searches included (~10 min)"
            "    dotnet fsi generate-fcidumps.fsx -- --check       # regenerate into a temporary folder and compare"
            "    dotnet fsi generate-fcidumps.fsx -- --only kinugasa-ts --out DIR"
            "    dotnet fsi generate-fcidumps.fsx -- --docs-only   # this README and the manifest only"
            ""
            "It needs a Python with PySCF, SciPy and geomeTRIC (`--python PATH`; otherwise `python` or"
            "`python3` on the PATH, then `~/pyscf-venv/bin/python` in WSL). The quantum chemistry runs"
            "there as short PySCF snippets, one thread per process; the script itself does the rest:"
            "the steps below, the file formats, the transition-state validation, this README and the"
            "manifest. The species, their starting geometries and active spaces, and the"
            "transition-state starts and constraints are listed at the top of the script."
            ""
            "`--check` compares every regenerated file with the bundled one: the integrals as written"
            "and after the best choice of orbital signs, the four lowest eigenvalues of the two"
            "active-space Hamiltonians (unchanged by any rotation of the active orbitals), the geometry"
            "and every manifest value. With the versions below, E(RHF) and E(CAS) reproduce to about"
            "1e-9 Ha and most integrals to 1e-8. Where the CASSCF optimum is flat or its orbitals"
            "degenerate (linear or symmetric molecules, the hydrogen-bonded complexes), another run"
            "can settle on other active orbitals with the same E(CAS), and threaded PySCF does so from"
            "run to run; the script therefore runs one thread per process. `--check` names each file"
            "a rerun changes. Other PySCF versions give slightly different numbers."
            ""
            "## How they were made"
            ""
            $"All files were computed with PySCF {versions.Pyscf} (SciPy {versions.Scipy} for the"
            $"minimisations, geomeTRIC {versions.Geometric} for the transition states) in the STO-3G"
            "basis on a closed-shell RHF reference, SCF converged to 1e-11 Ha (1e-10 Ha inside the"
            "geomeTRIC optimisations)."
            ""
            "1. Geometry. Each species is a minimum of the RHF/STO-3G energy, found from PySCF's"
            "   analytic nuclear gradients with SciPy's BFGS (gradient tolerance 1e-5 Ha/bohr; a"
            "   result with any gradient component above 1e-4 Ha/bohr is rejected), starting from"
            "   hand-built structures. The analytic RHF Hessian (`pyscf.hessian.thermo`) then"
            "   checks that each is a minimum. Where a frequency was imaginary beyond 30i cm-1 (a"
            "   saddle point: the planar 2-azetidinone and 1,4-dihydropyridine rings, which pucker,"
            "   and the planar HF-H2O complex), the structure was displaced 0.1 A along that mode"
            "   and minimised again until no such frequency remained (which way along the mode is the"
            "   sign LAPACK gives the eigenvector, so a rerun elsewhere may pucker a ring the other"
            "   way: a mirror image with the same energies). The table gives each"
            "   geometry's lowest frequency. Stretched species take the equilibrium geometry with"
            "   one X-H bond scaled and are not re-optimised, so they are not stationary points."
            "2. Active space. CASSCF(n,n) in PySCF (`mcscf.CASSCF`, convergence 1e-10 Ha, up to 200"
            "   macro-iterations) is run from several starting orbital sets and the lowest converged"
            "   energy is kept. The starts are the canonical RHF frontier orbitals (the top n/2"
            "   occupied and the bottom n/2 virtual), the MP2 natural orbitals with the occupations"
            "   furthest from 2 and 0 (the n/2 least-occupied occupied-type and the n/2 most-occupied"
            "   virtual-type), and, for the hydrogen-bonded complexes, the complex orbitals that"
            "   overlap most with each monomer's HOMO and LUMO. Bond-dissociation species start only"
            "   from the sigma/sigma* pair of the bond (the occupied and the virtual canonical orbital"
            "   with the largest hydrogen 1s weight), so both geometries of a molecule share the same"
            "   active orbitals. Orbital optimisation also removes the arbitrary rotation inside"
            "   degenerate shells (the pi orbitals of linear molecules)."
            "3. Balance. Every reaction in the examples keeps the same total active space on both"
            "   sides: two CAS(2,2) reactants form a CAS(4,4) product, a CAS(4,4) reactant forms two"
            "   CAS(2,2) products, and a transition state has the reactants' total."
            "4. Export. A CASCI on the optimised CASSCF orbitals gives the active-space Hamiltonian:"
            "   the one-electron integrals with the doubly occupied core folded in and the"
            "   two-electron integrals of the active orbitals. They are written in the layout of"
            "   `pyscf.tools.fcidump.from_integrals` (4-fold symmetry, values above 1e-15), with"
            "   MS2 = 0 and the core energy (nuclear repulsion plus the frozen-core electronic"
            "   energy) as the `0 0 0 0` line."
            ""
            "`E(CAS)` is the exact ground-state energy of the active-space Hamiltonian (the CASSCF"
            "energy), which a converged VQE reproduces. `E(CAS) - E(RHF)` is the energy the active"
            "space adds: correlation plus orbital relaxation."
            ""
            "## Transition states and activation energies"
            ""
            "`AntibioticPrecursorSynthesis.fsx` reports `Ea = E(TS) - sum E(reactants)`, from VQE on"
            "every species, for each route whose transition state has an FCIDUMP in the integral"
            "folder (this one or `--fcidump-dir`): the TS species is named `<route name> TS`, so the"
            "file is `<route-slug>-ts.fcidump` (e.g. `kinugasa-ts.fcidump`). Routes without one report"
            "their reaction energy only. The bundled transition states were made as follows, and the"
            "same steps (a new entry in the script's transition-state list) add one for another route"
            "or for your own reaction:"
            ""
            "1. Guess. Start from the product (plus any leaving molecule) or from the reactants placed"
            "   face to face, and minimise at RHF/STO-3G with the forming and breaking bond lengths"
            "   held near their expected TS values (geomeTRIC distance constraints: a `$set` block"
            "   passed as `constraints=` to `pyscf.geomopt.geometric_solver.optimize`)."
            "2. Saddle point. `optimize(mf, transition=True, hessian=\"first\")` runs geomeTRIC's"
            "   transition-state optimiser from that guess, starting from PySCF's analytic Hessian."
            "3. Validation. The analytic RHF Hessian at the result has exactly one imaginary"
            "   frequency, and that mode is the reaction coordinate of the step: minimising from the"
            "   TS displaced 0.15 A (largest atomic displacement) either way along it ends once in the"
            $"   product basin (every forming bond below {tsBonded} A, every breaking bond above {tsApart} A) and"
            "   once in the reactant basin (the other way round). A TS whose downhill path ends in an"
            "   intermediate belongs to another step and is rejected. `manifest.json` records both"
            "   downhill ends."
            "4. FCIDUMP. The active space is chosen and exported as above, with the reactants' total"
            "   (electrons, orbitals): CAS(4,4) for two CAS(2,2) reactants or one CAS(4,4) reactant."
            "   Ea then compares like with like."
            ""
            "Each search is limited to 15 minutes of wall-clock time, search and validation together."
            "geomeTRIC's default convergence leaves a saddle point uncertain by about 2e-3 A, so a"
            "rerun lands on the same saddle point but not the same digits. A search that reaches the"
            $"bundled TS's saddle point (every interatomic distance within {tsSameSaddle} A) keeps the bundled"
            "geometry, validated again, so its files reproduce; a new saddle point replaces it once"
            "validated. When a search fails or runs out of time, the bundled geometry is validated"
            "instead, and the script says which path each TS took. A route whose TS validates"
            "neither way has no TS file. The ring expansion (aziridine + CO) has none: its"
            "CO-insertion search did not converge. No TS energy is estimated or interpolated."
            ""
            "| Route TS | Elementary step | Imaginary frequency | Ea from E(RHF) / kcal/mol | Ea from E(CAS) / kcal/mol |"
            "|---|---|---|---|---|"
        ]

    let routes =
        [
            for ts in transitionStates do
                match byName.TryFind ts.Name with
                | Some r when ts.Reactants |> List.forall byName.ContainsKey ->
                    let ea key =
                        (num key r - (ts.Reactants |> List.sumBy (fun n -> num key byName.[n]))) * kcal

                    $"""| `{str "file" r}` | {ts.Step} | {pyF 0 (num "imaginary_frequency_cm1" r)}i cm-1 | {pyF 1 (ea "e_rhf")} | {pyF 1 (ea "e_cas")} |"""
                | _ -> ()
        ]

    let middle =
        [
            ""
            "Barriers from a minimal basis without dynamic correlation are too high, typically by"
            "tens of kcal/mol, and the steps are uncatalysed and in the gas phase (Kinugasa uses Cu,"
            "carbonylation Co or Rh, lactamisation an activating agent). Read them as a qualitative"
            "ordering of these model steps."
            ""
            "## What an active-space energy difference means"
            ""
            "Every energy here is an STO-3G total energy: mean-field inactive orbitals plus exact"
            "correlation inside a small active space. Differences between species are therefore"
            "close to RHF/STO-3G reaction or binding energies, plus the correlation of the active"
            "orbitals. Because of the balance rule, the correlation added is balanced in size,"
            "though not necessarily in character when bonds change, and CASSCF can settle in a local"
            "minimum (ketene's CAS(2,2) adds little). Compare `E(CAS) - E(RHF)` below to see how much"
            "of an energy difference comes from the active space. A minimal basis and a small active"
            "space give qualitative trends: signs and orderings, not kcal/mol accuracy. Errors of"
            "tens of kcal/mol against experiment are expected, and hydrogen-bond energies carry a"
            "large basis-set superposition error."
            ""
            "## Files"
            ""
            "| Species | File | Geometry | Active space | Qubits | E(RHF) / Ha | E(CAS) / Ha | E(CAS) - E(RHF) / Ha |"
            "|---|---|---|---|---|---|---|---|"
        ]

    let files =
        [
            for r in records ->
                let source = str "geometry_source" r

                let geometry =
                    match field "lowest_frequency_cm1" r with
                    | Some(JNum f) when not (source.Contains "lowest frequency") ->
                        $"{source}; lowest frequency {pyF 0 f} cm-1"
                    | _ -> source

                let eRhf, eCas = num "e_rhf" r, num "e_cas" r

                $"""| {str "name" r} | `{str "file" r}` | {geometry} | {str "active_space" r} | {num "qubits" r} | """
                + $"{pyF 8 eRhf} | {pyF 8 eCas} | {pyF 6 (eCas - eRhf)} |"
        ]

    let footer =
        [
            ""
            "## Licence"
            ""
            "These files are original computed output of the PySCF calculations described above,"
            "released with the rest of this repository under the Unlicense."
            ""
        ]

    String.Join("\n", header @ routes @ middle @ files @ footer)

/// Species records by name from a folder's manifest.json (empty when there is none).
let readManifest (dir: string) =
    let path = Path.Combine(dir, "manifest.json")

    if File.Exists path then
        match ofElement (JsonDocument.Parse(File.ReadAllText path).RootElement) with
        | JObj fields ->
            match field "species" fields with
            | Some(JList records) ->
                records
                |> List.choose (function
                    | JObj r -> Some(str "name" r, r)
                    | _ -> None)
                |> Map.ofList
            | _ -> Map.empty
        | _ -> Map.empty
    else
        Map.empty

/// manifest.json and README.md, species in the script's order.
let writeDocs (dir: string) (versions: Versions) (records: (string * Json) list list) =
    let manifest =
        JObj
            [
                "pyscf", JStr versions.Pyscf
                "scipy", JStr versions.Scipy
                "basis", JStr basis
                "species", JList(records |> List.map JObj)
                "geometric", JStr versions.Geometric
            ]

    File.WriteAllText(Path.Combine(dir, "manifest.json"), pyJson (Some 0) manifest)
    File.WriteAllText(Path.Combine(dir, "README.md"), readme versions records)

// ------------------------------------------------------------------------------
// --check: compare regenerated files with the bundled ones
// ------------------------------------------------------------------------------

/// FCIDUMP header value and integrals keyed by their four indices; the core
/// energy is (0, 0, 0, 0).
let readFcidump (path: string) =
    let lines = File.ReadAllLines path

    let body =
        lines |> Array.skipWhile (fun l -> not (l.Contains "&END")) |> Array.skip 1

    let header (key: string) =
        Regex.Match(String.Join(" ", lines), key + @"\s*=\s*(\d+)").Groups.[1].Value
        |> int

    header "NORB",
    header "NELEC",
    body
    |> Array.filter (fun l -> l.Trim() <> "")
    |> Array.map (fun l ->
        let p = l.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)
        (int p.[1], int p.[2], int p.[3], int p.[4]), Double.Parse(p.[0], inv))
    |> dict

/// Largest deviation between two FCIDUMPs, raw and after the best choice of
/// orbital signs (an MO's sign is arbitrary: h_ij picks up s_i s_j, (ij|kl) s_i s_j s_k s_l).
let integralDeviation (a: string) (b: string) =
    let norb, _, x = readFcidump a
    let _, _, y = readFcidump b
    let keys = Seq.append x.Keys y.Keys |> Seq.distinct |> Seq.toArray

    let value (d: IDictionary<_, float>) k =
        match d.TryGetValue k with
        | true, value -> value
        | false, _ -> 0.0

    let deviation (signs: int[]) =
        let s i =
            if i = 0 then 1.0 else float signs.[i - 1]

        keys
        |> Array.map (fun ((i, j, k, l) as key) -> abs (value x key - s i * s j * s k * s l * value y key))
        |> Array.fold max 0.0

    let raw = deviation (Array.create norb 1)

    let best =
        List.init (FSharp.Core.Operators.max 0 (1 <<< (norb - 1))) (fun bits ->
            deviation (Array.init norb (fun i -> if i > 0 && (bits >>> (i - 1)) &&& 1 = 1 then -1 else 1)))
        |> List.min

    raw, best

/// The four lowest eigenvalues (Sz = 0) of an FCIDUMP's Hamiltonian: unchanged
/// by any rotation among the active orbitals, signs included.
let spectrum (engine: Engine) (path: string) =
    let norb, nelec, x = readFcidump path

    let value key =
        match x.TryGetValue key with
        | true, value -> value
        | false, _ -> 0.0

    let pairs =
        [|
            for i in 1..norb do
                for j in 1..i -> i, j
        |]

    let h1 =
        Array.init norb (fun i -> Array.init norb (fun j -> value (max i j + 1, min i j + 1, 0, 0)))

    // (ij|kl) = (kl|ij): a file may list only one of the two
    let h2 =
        pairs
        |> Array.map (fun (i, j) ->
            pairs
            |> Array.map (fun (k, l) ->
                if x.ContainsKey(i, j, k, l) then
                    x.[(i, j, k, l)]
                else
                    value (k, l, i, j)))

    engine.Call(
        "fci",
        [
            "h1", ofMatrix h1
            "h2", ofMatrix h2
            "ecore", JNum(value (0, 0, 0, 0))
            "orbitals", JInt norb
            "electrons", JInt nelec
        ]
    )
    |> fun r -> floats (r.GetProperty "energies")

/// Largest coordinate difference and largest interatomic-distance difference, Angstrom.
let xyzDeviation (a: string) (b: string) =
    let ga, gb = readXyz a, readXyz b

    if ga.Symbols <> gb.Symbols then
        infinity, infinity
    else
        Array.map2 (Array.map2 (fun u v -> abs (u - v) * bohr)) ga.Coords gb.Coords
        |> Array.collect id
        |> Array.fold max 0.0,
        shapeDeviation ga gb

let compareRecords (fresh: (string * Json) list) (bundled: (string * Json) list) =
    let tolerance =
        function
        | "e_rhf"
        | "e_cas" -> 1e-7
        | "lowest_frequency_cm1"
        | "imaginary_frequency_cm1"
        | "second_frequency_cm1" -> 0.15
        | _ -> 1e-6

    let rec diff (path: string) (a: Json) (b: Json) =
        match a, b with
        | JNum x, JNum y ->
            let key = path.Split('.') |> Array.last

            let tol =
                if path.Contains "distances_angstrom" then
                    0.0015
                else
                    tolerance key

            if abs (x - y) > tol then
                [ $"{path}: {pyRepr x} vs {pyRepr y}" ]
            else
                []
        | JObj xs, JObj ys ->
            let keys = (List.map fst xs @ List.map fst ys) |> List.distinct

            [
                for k in keys do
                    match field k xs, field k ys with
                    | Some u, Some v -> yield! diff $"{path}.{k}" u v
                    | u, _ -> $"""{path}.{k}: only in {(if u.IsSome then "regenerated" else "bundled")}"""
            ]
        | JList xs, JList ys when xs.Length = ys.Length ->
            // downhill ends compare by energy, whichever end is listed first
            let order =
                if path.EndsWith "downhill_ends" then
                    List.sortBy (function
                        | JObj e -> num "e_rhf" e
                        | _ -> 0.0)
                else
                    id

            List.zip (order xs) (order ys)
            |> List.mapi (fun i (u, v) -> diff $"{path}[{i}]" u v)
            |> List.concat
        | _ when a = b -> []
        | _ -> [ $"{path}: {pyJson None a} vs {pyJson None b}" ]

    diff (str "name" fresh) (JObj fresh) (JObj bundled)

// ------------------------------------------------------------------------------
// Main
// ------------------------------------------------------------------------------

let args = fsi.CommandLineArgs |> Array.skip 1 |> Array.toList

[<TailCall>]
let rec option name =
    function
    | flag :: value :: _ when flag = name -> Some value
    | _ :: rest -> option name rest
    | [] -> None

let flag name = List.contains name args

if flag "--help" || flag "-h" then
    printfn
        "dotnet fsi generate-fcidumps.fsx [-- --check] [--only slug,...] [--skip-ts] [--docs-only] [--out DIR] [--python PATH]"

    exit 0

let known =
    set
        [
            "--check"
            "--only"
            "--skip-ts"
            "--docs-only"
            "--out"
            "--python"
            "--help"
            "-h"
        ]

for a in args do
    if a.StartsWith "--" && not (known.Contains a) then
        failwithf "unknown option %s (--help lists them)" a

let here = __SOURCE_DIRECTORY__
let check = flag "--check"
let skipTs = flag "--skip-ts"

let allNames =
    (species |> List.map (fun s -> s.Name))
    @ (stretched |> List.map (fun s -> s.Name))
    @ (transitionStates |> List.map (fun t -> t.Name))

let only =
    option "--only" args
    |> Option.map (fun s ->
        s.Split(',', StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun x -> x.Trim())
        |> set)

match only with
| Some slugs ->
    let unknown = slugs - set (allNames |> List.map slug)

    if not unknown.IsEmpty then
        failwithf
            "unknown slug(s) %s; known: %s"
            (String.Join(", ", unknown))
            (String.Join(", ", allNames |> List.map slug))
| None -> ()

let wanted name =
    only |> Option.forall (fun s -> s.Contains(slug name))

let outDir =
    if check then
        Path.Combine(Path.GetTempPath(), "fcidump-check-" + Guid.NewGuid().ToString("N").Substring(0, 8))
    else
        option "--out" args |> Option.map Path.GetFullPath |> Option.defaultValue here

Directory.CreateDirectory outDir |> ignore

// --docs-only: README.md and manifest.json from the existing manifest, no Python.
if flag "--docs-only" then
    let manifestPath = Path.Combine(outDir, "manifest.json")

    match ofElement (JsonDocument.Parse(File.ReadAllText manifestPath).RootElement) with
    | JObj top ->
        let existing = readManifest outDir

        let versions =
            {
                Pyscf = str "pyscf" top
                Scipy = str "scipy" top
                Geometric = str "geometric" top
            }

        writeDocs outDir versions (allNames |> List.choose existing.TryFind)
        printfn "README.md and manifest.json rewritten from %s" manifestPath
        exit 0
    | _ -> failwithf "%s is not a JSON object" manifestPath

let python = findPython (option "--python" args)
let clock = Stopwatch.StartNew()

let tsJobs =
    if skipTs then
        []
    else
        transitionStates |> List.filter (fun t -> wanted t.Name)

printfn "Python: %s (one thread per process)" python.Describe
printfn "Output: %s%s" outDir (if check then " (temporary, --check)" else "")

// The bundled TS geometries, read before anything is written: the fallback start.
let bundledTs =
    transitionStates
    |> List.choose (fun t ->
        let path = Path.Combine(here, slug t.Name + ".xyz")

        if File.Exists path then
            Some(t.Name, readXyz path)
        else
            None)
    |> Map.ofList

let engineVersions (engine: Engine) =
    let v = engine.Call("versions", [])

    {
        Pyscf = v.GetProperty("pyscf").GetString()
        Scipy = v.GetProperty("scipy").GetString()
        Geometric = v.GetProperty("geometric").GetString()
    }

/// Validates a TS geometry and exports it.
let tsOutcome (ts: TransitionState) (g: Geometry) (deadline: DateTime) (path: string) =
    use engine = new Engine(python, ts.Name)
    let versions = engineVersions engine
    let imaginary, second, downhill = validateTs engine ts g deadline

    let note =
        $"TS optimised with geomeTRIC, imaginary frequency {pyF 0 (abs imaginary)}i cm-1"

    let extra =
        [
            "transition_state", JBool true
            "imaginary_frequency_cm1", JNum(pyRound 1 (abs imaginary))
            "second_frequency_cm1", JNum(pyRound 1 second)
            "forming_bonds", JList [ for i, j in ts.Forming -> JList [ JInt i; JInt j ] ]
            "breaking_bonds", JList [ for i, j in ts.Breaking -> JList [ JInt i; JInt j ] ]
            "validation", JStr tsValidation
            "downhill_ends", JList downhill
        ]

    export engine versions ts.Name g frontier44 note extra (Some path)

/// Every TS is searched (15 minutes for search and validation). A search that reaches
/// the bundled TS's saddle point keeps the bundled geometry, so a rerun reproduces its
/// files; a different saddle point replaces it once validated. When the search fails or
/// runs out of time, or its result does not validate, the bundled geometry is validated
/// instead (15 more minutes).
let tsTasks =
    tsJobs
    |> List.map (fun ts ->
        Task.Run(fun () ->
            let started = Stopwatch.StartNew()
            let deadline = DateTime.UtcNow.AddSeconds tsSeconds
            let bundled = bundledTs.TryFind ts.Name

            let searched =
                try
                    use engine = new Engine(python, ts.Name)
                    Ok(searchTs engine ts deadline)
                with error ->
                    Error(describe error)

            let took = $"searched in {started.Elapsed.TotalMinutes:F1} min"
            let fallback why = $"{why}: bundled geometry revalidated"

            let candidates =
                match searched, bundled with
                | Ok g, Some b when shapeDeviation g b <= tsSameSaddle ->
                    [
                        b,
                        deadline,
                        $"{took}, the bundled TS's saddle point (distances within {pyF 4 (shapeDeviation g b)} A): bundled geometry revalidated"
                    ]
                | Ok g, Some b ->
                    [
                        g,
                        deadline,
                        $"{took}, a new saddle point (distances differ up to {pyF 3 (shapeDeviation g b)} A)"
                        b, DateTime.UtcNow.AddSeconds tsSeconds, fallback $"{took}, new saddle point did not validate"
                    ]
                | Ok g, None -> [ g, deadline, took ]
                | Error message, Some b ->
                    [
                        b, DateTime.UtcNow.AddSeconds tsSeconds, fallback $"search failed ({message})"
                    ]
                | Error message, None ->
                    printfn "%-40s search failed: %s" ts.Name message
                    []

            let rec first candidates =
                match candidates with
                | [] -> None
                | (g, deadline, path) :: rest ->
                    try
                        Some(tsOutcome ts g deadline path)
                    with error ->
                        printfn "%-40s not validated (%s): %s" ts.Name path (describe error)
                        first rest

            first candidates))

// Species: one engine, in order; a geometry shared by two active spaces is computed once.
let outcomes, versions =
    use engine = new Engine(python, "species")
    let versions = engineVersions engine
    let geometries = Dictionary<string, Geometry * string * float option>()

    let geometryOf (sp: Species) =
        match geometries.TryGetValue sp.Atoms with
        | true, known -> known
        | _ ->
            let known = minimumOf engine sp
            geometries.[sp.Atoms] <- known
            known

    let needed =
        species
        |> List.filter (fun sp ->
            wanted sp.Name
            || stretched |> List.exists (fun s -> s.Source = sp.Name && wanted s.Name))

    let byName = species |> List.map (fun s -> s.Name, s) |> Map.ofList

    let main =
        [
            for sp in needed do
                let g, note, lowest = geometryOf sp

                if wanted sp.Name then
                    let extra =
                        lowest
                        |> Option.map (fun f -> [ "lowest_frequency_cm1", JNum(pyRound 1 f) ])
                        |> Option.defaultValue []

                    export engine versions sp.Name g sp.Active note extra None

            for st in stretched do
                if wanted st.Name then
                    let source = byName.[st.Source]
                    let g = asWritten (let g, _, _ = geometryOf source in g)
                    let x, h = g.Coords.[st.X], g.Coords.[st.H]

                    let coords =
                        g.Coords
                        |> Array.mapi (fun i c ->
                            if i = st.H then
                                Array.map2 (fun xv hv -> xv + st.Factor * (hv - xv)) x h
                            else
                                c)

                    let note =
                        $"{st.Source} geometry with the {g.Symbols.[st.X]}-{g.Symbols.[st.H]} bond scaled x{pyRepr st.Factor}"

                    export engine versions st.Name { g with Coords = coords } source.Active note [] None
        ]

    main, versions

let tsOutcomes = tsTasks |> List.map (fun t -> t.Result)

let fresh =
    outcomes @ (tsOutcomes |> List.choose id)
    |> List.map (fun o -> str "name" o.Record, o)
    |> Map.ofList

for KeyValue(name, o) in fresh do
    let s = slug name
    File.WriteAllText(Path.Combine(outDir, s + ".fcidump"), o.Fcidump)
    File.WriteAllText(Path.Combine(outDir, s + ".xyz"), o.Xyz)

// A transition state that validated neither way loses its old files (not in --check).
if not check then
    for ts, outcome in List.zip tsJobs tsOutcomes do
        if outcome.IsNone then
            for ext in [ ".fcidump"; ".xyz" ] do
                File.Delete(Path.Combine(outDir, slug ts.Name + ext))


// Records not regenerated this run come from the existing manifest.
let previous = readManifest (if check then here else outDir)
let failedTs = tsJobs |> List.map (fun t -> t.Name) |> set

let records =
    allNames
    |> List.choose (fun name ->
        match fresh.TryFind name with
        | Some o -> Some o.Record
        | None when failedTs.Contains name && not check -> None
        | None -> previous.TryFind name)

writeDocs outDir versions records

printfn ""

for ts, outcome in List.zip tsJobs tsOutcomes do
    match outcome with
    | Some o -> printfn "%-40s %s" ts.Name (o.Path |> Option.defaultValue "")
    | None -> printfn "%-40s not validated, left out" ts.Name

if check then
    let bundled = readManifest here
    use engine = new Engine(python, "check")
    printfn ""
    printfn "Largest absolute differences, regenerated - bundled (Ha; geometry in Angstrom):"
    printfn "  integrals   every FCIDUMP value, as written"
    printfn "  signs       the same after the best choice of orbital signs"
    printfn "  spectrum    the four lowest eigenvalues of the two Hamiltonians (any orbital rotation)"
    printfn "  xyz         coordinates; shape: interatomic distances (any rotation or mirror image)"
    printfn ""

    printfn "%-34s %9s %9s %9s %9s %9s %9s %9s" "species" "integrals" "signs" "spectrum" "xyz" "shape" "E(RHF)" "E(CAS)"

    let mutable problems = 0
    let mutable rotated = 0

    for ts, outcome in List.zip tsJobs tsOutcomes do
        if outcome.IsNone then
            printfn "%-34s not validated, no files" ts.Name
            problems <- problems + 1

    for name in allNames do
        match fresh.TryFind name with
        | None -> ()
        | Some o ->
            let s = slug name

            let mine, theirs =
                Path.Combine(outDir, s + ".fcidump"), Path.Combine(here, s + ".fcidump")

            let raw, signs = integralDeviation mine theirs

            let roots =
                Array.map2 (fun a b -> abs (a - b)) (spectrum engine mine) (spectrum engine theirs)
                |> Array.max

            let dx, shape =
                xyzDeviation (Path.Combine(outDir, s + ".xyz")) (Path.Combine(here, s + ".xyz"))

            let comment (file: string) = (File.ReadAllLines file).[1]

            let dE key =
                match bundled.TryFind name with
                | Some b -> num key o.Record - num key b
                | None -> nan

            let e (x: float) = x.ToString("0.0E+0", inv)

            printfn
                "%-34s %9s %9s %9s %9s %9s %9s %9s"
                (if name.Length > 34 then name.Substring(0, 34) else name)
                (e raw)
                (e signs)
                (e roots)
                (e dx)
                (e shape)
                (e (dE "e_rhf"))
                (e (dE "e_cas"))

            // The same Hamiltonian up to orbital signs (1e-8) or rotations (spectrum, 1e-7)
            let sameHamiltonian = signs <= 1e-8 || roots <= 1e-7

            let issues =
                [
                    // BFGS stops at 1e-5 Ha/bohr, so soft coordinates may move ~1e-5 A
                    if shape > 1e-4 then
                        "geometry differs"
                    if
                        comment (Path.Combine(outDir, s + ".xyz"))
                        <> comment (Path.Combine(here, s + ".xyz"))
                    then
                        "xyz comment line differs"
                    match bundled.TryFind name with
                    | Some b -> yield! compareRecords o.Record b
                    | None -> "not in the bundled manifest"
                ]

            if signs > 1e-8 && roots <= 1e-7 then
                printfn "    note: other active orbitals, same Hamiltonian spectrum"

            if not sameHamiltonian && issues.IsEmpty then
                printfn "    other active orbitals at the same E(CAS): the CASSCF optimum is flat or degenerate"
                rotated <- rotated + 1

            if dx > 1e-4 && shape <= 1e-4 then
                printfn "    note: same shape, other orientation or mirror image"

            for issue in issues do
                printfn "    %s" issue

            problems <- problems + issues.Length

    if only.IsNone && not skipTs then
        let fresh = File.ReadAllLines(Path.Combine(outDir, "README.md"))
        let old = File.ReadAllLines(Path.Combine(here, "README.md"))

        let differing =
            (Set.ofArray fresh - Set.ofArray old).Count
            + (Set.ofArray old - Set.ofArray fresh).Count

        printfn
            "README.md: %s"
            (if differing = 0 then
                 "identical"
             else
                 $"{differing} line(s) differ")

    printfn
        "%d issue(s), %d species with other active orbitals at the same energies; regenerated files in %s"
        problems
        rotated
        outDir

    if problems > 0 then
        printfn "Done in %.1f min" clock.Elapsed.TotalMinutes
        exit 1

printfn "Done in %.1f min" clock.Elapsed.TotalMinutes
