namespace FSharp.Azure.Quantum.Examples.Common

open System
open System.Collections.Generic
open System.Globalization
open System.IO
open System.Text.RegularExpressions
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.QuantumChemistry

/// Where the chemistry examples get molecular integrals: FCIDUMP files (one per species,
/// by default the bundled set in examples/_data/chemistry/fcidump), the library's STO-3G
/// integrals for molecules of H and He atoms, or, only on request, the empirical prototype
/// Hamiltonian.
module ChemistryIntegrals =

    /// Bundled FCIDUMP files and geometries (see the README.md there for provenance).
    let bundledDirectory =
        Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "_data", "chemistry", "fcidump"))

    let private speciesSlugRegex = Regex "[^a-z0-9]+"
    /// File-name stem of a species' FCIDUMP: lowercase letters and digits, other runs as '-'.
    let speciesSlug (name: string) : string =
        speciesSlugRegex.Replace(name.ToLowerInvariant(), "-").Trim('-')

    /// FCIDUMP file name expected for a species.
    let fcidumpFileName (molecule: Molecule) : string = speciesSlug molecule.Name + ".fcidump"

    /// A closed-shell species named `name` at the geometry of the bundled `<slug>.xyz`.
    let loadSpecies (name: string) : Molecule =
        let path = Path.Combine(bundledDirectory, speciesSlug name + ".xyz")

        if not (File.Exists path) then
            failwith $"No bundled geometry {path} for '{name}'; see {bundledDirectory}/README.md"

        let atoms =
            File.ReadAllLines path
            |> Array.skip 2
            |> Array.filter (fun line -> line.Trim() <> "")
            |> Array.map (fun line ->
                let parts = line.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)

                let coordinate i =
                    Double.Parse(parts.[i], CultureInfo.InvariantCulture)

                {
                    Element = parts.[0]
                    Position = (coordinate 1, coordinate 2, coordinate 3)
                })
            |> List.ofArray

        {
            Name = name
            Atoms = atoms
            Bonds = []
            Charge = 0
            Multiplicity = 1
        }

    /// True when the library computes this molecule's integrals itself (H and He atoms only).
    let hasLibraryIntegrals (molecule: Molecule) : bool =
        molecule.Atoms
        |> List.forall (fun a ->
            let e = a.Element.ToUpperInvariant()
            e = "H" || e = "HE")

    /// Integral provider reading `path`, refusing Hamiltonians wider than `maxQubits`.
    let fcidumpProvider (maxQubits: int) (path: string) : IntegralProvider =
        fun _ ->
            FciDumpIntegrals.readFile path
            |> Result.mapError (fun err -> err.Message)
            |> Result.bind (fun integrals ->
                if 2 * integrals.NumOrbitals > maxQubits then
                    Error
                        $"{Path.GetFileName path}: {integrals.NumOrbitals} orbitals need {2 * integrals.NumOrbitals} qubits; the example caps VQE at {maxQubits} (use a smaller active space)"
                else
                    Ok integrals)

    /// Provider for the species' FCIDUMP in `directory`, when that file exists.
    let tryFcidump (maxQubits: int) (directory: string option) (molecule: Molecule) : IntegralProvider option =
        directory
        |> Option.map (fun dir -> Path.Combine(dir, fcidumpFileName molecule))
        |> Option.filter File.Exists
        |> Option.map (fcidumpProvider maxQubits)

    /// Integral provider for a species: its FCIDUMP in `directory` when present; None for the
    /// library's STO-3G integrals (H/He species). Without a directory (the explicit empirical
    /// mode), other species also get None (the empirical prototype Hamiltonian) except those
    /// the library refuses without integrals (H2O, LiH). With a directory, a missing file is
    /// an Error naming it, so one comparison never mixes integral-based and empirical energies.
    let providerFor
        (maxQubits: int)
        (directory: string option)
        (molecule: Molecule)
        : Result<IntegralProvider option, string> =
        let file = fcidumpFileName molecule

        match tryFcidump maxQubits directory molecule, directory with
        | Some provider, _ -> Ok(Some provider)
        | None, _ when hasLibraryIntegrals molecule -> Ok None
        | None, Some dir -> Error $"needs {file} in {dir}"
        | None, None when (MoleculeIdentification.identify molecule).IsSome -> Error $"needs --fcidump-dir with {file}"
        | None, None -> Ok None

    /// FCIDUMP folder from the command line: `--fcidump-dir DIR`, else the bundled set;
    /// None only with `--empirical`.
    let integralDirectory (fcidumpDir: string option) (empirical: bool) : string option =
        match fcidumpDir, empirical with
        | _, true -> None
        | Some dir, false -> Some dir
        | None, false -> Some bundledDirectory

    /// Short description of the Hamiltonian behind an energy.
    let describeSource (source: EnergySource) : string =
        match source with
        | ProviderIntegrals -> "FCIDUMP integrals"
        | ComputedSto3gIntegrals -> "STO-3G integrals (library)"
        | Computed631gIntegrals -> "6-31G integrals (library)"
        | EmpiricalHamiltonian -> "EMPIRICAL (illustrative)"
        | QpeTrotterEvolution -> "QPE of the Trotterised evolution"
        | TabulatedReference -> "tabulated (no circuit)"

    /// Line describing where this run's integrals come from.
    let describeDirectory (directory: string option) : string =
        match directory with
        | Some dir when Path.GetFullPath dir = bundledDirectory ->
            $"bundled FCIDUMP files ({dir}; STO-3G, PySCF, see README.md there)"
        | Some dir -> $"FCIDUMP files in {dir}"
        | None -> "none - EMPIRICAL prototype Hamiltonian (--empirical; illustrative energies only)"

    /// Note printed under a results table that contains empirical energies.
    let empiricalNote =
        [
            "  EMPIRICAL energies come from the library's prototype Hamiltonian, not from molecular"
            "  integrals: they illustrate the workflow and are not chemistry results. Drop --empirical"
            "  to use the bundled FCIDUMP integrals, or pass --fcidump-dir with your own files."
        ]

    /// Ground-state energy of one species.
    type SpeciesEnergy =
        {
            Energy: float
            Source: EnergySource
            Converged: bool
            Iterations: int
            Seconds: float
            /// Returned from the per-run cache (Seconds is the original computation's)
            Reused: bool
        }

    /// VQE per species name, computed once per run.
    type EnergyCache
        (backend: IQuantumBackend, maxIterations: int, tolerance: float, maxQubits: int, directory: string option) =
        let cache = Dictionary<string, Result<SpeciesEnergy, string>>()

        member _.Energy(molecule: Molecule) : Result<SpeciesEnergy, string> =
            match cache.TryGetValue molecule.Name with
            | true, cached -> cached |> Result.map (fun e -> { e with Reused = true })
            | _ ->
                let started = DateTime.Now

                let result =
                    providerFor maxQubits directory molecule
                    |> Result.bind (fun provider ->
                        // Without a provider the library's own Hamiltonian (STO-3G for H/He,
                        // empirical otherwise) uses 2 qubits per atom.
                        let qubits = 2 * molecule.Atoms.Length

                        if provider.IsNone && qubits > maxQubits then
                            Error
                                $"{molecule.Name}: the library Hamiltonian needs {qubits} qubits; the example caps VQE at {maxQubits}"
                        else
                            Ok provider)
                    |> Result.bind (fun provider ->
                        let config =
                            {
                                Method = GroundStateMethod.VQE
                                Backend = Some backend
                                MaxIterations = maxIterations
                                Tolerance = tolerance
                                InitialParameters = None
                                ProgressReporter = None
                                ErrorMitigation = None
                                IntegralProvider = provider
                            }

                        GroundStateEnergy.estimateEnergy molecule config
                        |> Async.RunSynchronously
                        |> Result.mapError (fun err -> err.Message))
                    |> Result.map (fun r ->
                        {
                            Energy = r.Energy
                            Source = r.Source
                            Converged = r.Converged
                            Iterations = r.Iterations
                            Seconds = (DateTime.Now - started).TotalSeconds
                            Reused = false
                        })

                cache.[molecule.Name] <- result
                result
