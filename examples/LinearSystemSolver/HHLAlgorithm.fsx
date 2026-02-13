// HHL Algorithm (Harrow-Hassidim-Lloyd) Example
// Quantum Linear System Solver: Ax = b
//
// BREAKTHROUGH: Exponential speedup for solving linear systems
// Classical: O(N log N) using conjugate gradient (sparse)
// Quantum HHL: O(log(N) x poly(kappa, log(epsilon)))
//
// WHERE IT MATTERS:
// - Quantum chemistry: Molecular ground state energies
// - Machine learning: Quantum SVM, least squares regression
// - Engineering: Finite element analysis, circuit simulation
// - Finance: Portfolio optimization with covariance matrices

(*
===============================================================================
 Background Theory
===============================================================================

The HHL algorithm (Harrow, Hassidim, Lloyd, 2009) solves linear systems Ax = b
with exponential speedup under specific conditions. For an NxN sparse Hermitian
matrix A with condition number kappa, classical algorithms require O(N*sqrt(kappa))
operations (conjugate gradient), while HHL runs in O(log(N) x poly(kappa, 1/epsilon))
time. This exponential speedup in N makes HHL foundational for quantum machine
learning, where linear algebra underlies most algorithms.

The algorithm works in three phases: (1) Quantum Phase Estimation (QPE) extracts
eigenvalues lambda_j of A into a register, decomposing |b> = sum_j beta_j|u_j>
in the eigenbasis. (2) Controlled rotation applies R_y(2 arcsin(C/lambda_j)) to
an ancilla, encoding 1/lambda_j in the amplitude. (3) Inverse QPE uncomputes the
eigenvalue register. Upon measuring the ancilla in |1>, the remaining state is
proportional to |x> = A^{-1}|b>.

Key Equations:
  - Linear system: A|x> = |b>  where A is NxN Hermitian, kappa = lambda_max/lambda_min
  - Eigendecomposition: |b> = sum_j beta_j|u_j> where A|u_j> = lambda_j|u_j>
  - Solution state: |x> = A^{-1}|b> = sum_j (beta_j/lambda_j)|u_j>
  - HHL complexity: O(log(N) x kappa^2 x s x poly(1/epsilon)) for s-sparse matrices
  - Success probability: P ~ ||x||^2 / ||A^{-1}||^2  (amplitude amplification helps)

Quantum Advantage:
  HHL achieves exponential speedup in matrix dimension N, but with caveats:
  (1) Input |b> must be efficiently preparable (qRAM or structured data)
  (2) Output is quantum state |x>, not classical vector (readout costs O(N))
  (3) Matrix A must be sparse and well-conditioned (kappa appears polynomially)
  (4) Useful when only expectation values <x|M|x> are needed, not full x
  Despite limitations, HHL enables quantum speedups for SVM classification,
  recommendation systems, and solving differential equations.

References:
  [1] Harrow, Hassidim, Lloyd, "Quantum Algorithm for Linear Systems of Equations",
      Phys. Rev. Lett. 103, 150502 (2009). https://doi.org/10.1103/PhysRevLett.103.150502
  [2] Childs, Kothari, Somma, "Quantum algorithm for systems of linear equations
      with exponentially improved dependence on precision", SIAM J. Comput. (2017).
      https://doi.org/10.1137/16M1087072
  [3] Aaronson, "Read the fine print", Nature Physics 11, 291-293 (2015).
      https://doi.org/10.1038/nphys3272
  [4] Wikipedia: Quantum_algorithm_for_linear_systems_of_equations
      https://en.wikipedia.org/wiki/Quantum_algorithm_for_linear_systems_of_equations
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open System.Numerics
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.QuantumLinearSystemSolver
open FSharp.Azure.Quantum.Algorithms.TrotterSuzuki
open FSharp.Azure.Quantum.Algorithms.MottonenStatePreparation
open FSharp.Azure.Quantum.Examples.Common

// ---------------------------------------------------------------------------
// CLI setup
// ---------------------------------------------------------------------------
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv
Cli.exitIfHelp "HHLAlgorithm.fsx" "Quantum linear system solver (Ax = b) via HHL algorithm"
    [ { Name = "example";   Description = "Scenario to run: 1|2|3|4|5|all"; Default = Some "all" }
      { Name = "precision"; Description = "QPE eigenvalue qubits";           Default = Some "4" }
      { Name = "output";    Description = "Write results to JSON file";      Default = None }
      { Name = "csv";       Description = "Write results to CSV file";       Default = None }
      { Name = "quiet";     Description = "Suppress informational output";   Default = None } ]
    args

let quiet      = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath    = Cli.tryGet "csv" args
let example    = Cli.getOr "example" "all" args
let cliPrecision = Cli.getIntOr "precision" 4 args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt

// Rule 1: explicit IQuantumBackend
let quantumBackend = LocalBackend() :> IQuantumBackend

// Collect results for JSON / CSV output
let results = ResizeArray<Map<string, string>>()

let shouldRun (n: string) = example = "all" || example = n

// ============================================================================
// SCENARIO 1: Simple 2x2 System (Educational)
// ============================================================================

if shouldRun "1" then
    pr "--------------------------------------------------------------------"
    pr "SCENARIO 1: Simple 2x2 Diagonal System"
    pr "--------------------------------------------------------------------"
    pr ""
    pr "BUSINESS PROBLEM:"
    pr "  Solve electrical circuit with 2 nodes:"
    pr "    2*V1 = 4  (node 1)"
    pr "    1*V2 = 2  (node 2)"
    pr ""
    pr "  Matrix A = [[2, 0], [0, 1]]"
    pr "  Vector b = [4, 2]"
    pr "  Expected solution: x = [2, 2] volts"
    pr ""

    let problem1 = linearSystemSolver {
        matrix [[2.0; 0.0]; [0.0; 1.0]]
        vector [4.0; 2.0]
        precision cliPrecision
        backend quantumBackend
    }

    pr "Running HHL algorithm on local simulator..."
    match problem1 with
    | Error err ->
        pr "Problem setup failed: %s" err.Message
    | Ok prob ->
        match solve prob with
        | Error err ->
            pr "Error: %s" err.Message
        | Ok result ->
            pr "SUCCESS!"
            pr ""
            pr "RESULTS:"
            pr "  Success Probability: %.4f" result.SuccessProbability
            pr "  Condition Number (kappa): %s" (
                match result.ConditionNumber with
                | Some k -> sprintf "%.2f" k
                | None -> "N/A"
            )
            pr "  Gates Used: %d" result.GateCount
            pr "  Backend: %s" result.BackendName
            pr ""
            pr "CLASSICAL VERIFICATION:"
            pr "  x1 = 4/2 = 2.0"
            pr "  x2 = 2/1 = 2.0"
            pr ""

            results.Add(Map.ofList [
                "scenario", "1_simple_2x2"
                "matrix", "diag(2,1)"
                "vector", "[4,2]"
                "success_probability", sprintf "%.6f" result.SuccessProbability
                "condition_number", (match result.ConditionNumber with Some k -> sprintf "%.2f" k | None -> "N/A")
                "gate_count", sprintf "%d" result.GateCount
                "backend", result.BackendName
            ])

// ============================================================================
// SCENARIO 2: Ill-Conditioned System (Stress Test)
// ============================================================================

if shouldRun "2" then
    pr "--------------------------------------------------------------------"
    pr "SCENARIO 2: Ill-Conditioned Matrix (kappa = 100)"
    pr "--------------------------------------------------------------------"
    pr ""
    pr "CHALLENGE:"
    pr "  High condition number kappa = lambda_max/lambda_min affects:"
    pr "  - Success probability: P_success ~ 1/kappa^2"
    pr "  - Accuracy of solution"
    pr ""
    pr "  Matrix: diag(100, 1)"
    pr "  Vector: [1, 1]"
    pr ""

    let problem2 = linearSystemSolver {
        diagonalMatrix [100.0; 1.0]
        vector [1.0; 1.0]
        precision 6
        minEigenvalue 0.001
        backend quantumBackend
    }

    pr "Running HHL..."
    match problem2 with
    | Error err ->
        pr "Problem setup failed: %s" err.Message
    | Ok prob ->
        match solve prob with
        | Error err ->
            pr "Error: %s" err.Message
        | Ok result ->
            pr "Result obtained"
            pr ""
            pr "CONDITION NUMBER ANALYSIS:"
            match result.ConditionNumber with
            | Some k ->
                pr "  kappa = %.2f (ill-conditioned!)" k
                pr "  Expected success rate: ~%.2f%%" (100.0 / (k * k))
            | None ->
                pr "  kappa not available"

            pr ""
            pr "MEASURED RESULTS:"
            pr "  Success Probability: %.4f" result.SuccessProbability
            pr "  Gates: %d" result.GateCount
            pr ""
            pr "KEY INSIGHT:"
            pr "  HHL works best with well-conditioned matrices (kappa < 100)"
            pr "  For ill-conditioned systems, use preconditioning!"
            pr ""

            results.Add(Map.ofList [
                "scenario", "2_ill_conditioned"
                "matrix", "diag(100,1)"
                "vector", "[1,1]"
                "success_probability", sprintf "%.6f" result.SuccessProbability
                "condition_number", (match result.ConditionNumber with Some k -> sprintf "%.2f" k | None -> "N/A")
                "gate_count", sprintf "%d" result.GateCount
                "backend", result.BackendName
            ])

// ============================================================================
// SCENARIO 3: Larger System (4x4)
// ============================================================================

if shouldRun "3" then
    pr "--------------------------------------------------------------------"
    pr "SCENARIO 3: 4x4 System (Finite Element Analysis)"
    pr "--------------------------------------------------------------------"
    pr ""
    pr "APPLICATION:"
    pr "  Structural analysis with 4 nodes"
    pr "  Stiffness matrix (diagonal approximation)"
    pr ""

    let problem3 = linearSystemSolver {
        diagonalMatrix [2.0; 3.0; 4.0; 5.0]
        vector [1.0; 0.0; 0.0; 0.0]
        precision 5
        backend quantumBackend
    }

    pr "Running HHL on 4x4 system..."
    pr "  This requires 5 + 2 + 1 = 8 qubits total"
    pr "  Clock: 5 qubits, Solution: 2 qubits, Ancilla: 1 qubit"
    pr ""

    match problem3 with
    | Error err ->
        pr "Problem setup failed: %s" err.Message
    | Ok prob ->
        match solve prob with
        | Error err ->
            pr "Error: %s" err.Message
        | Ok result ->
            pr "Solved 4x4 system!"
            pr "  Gates: %d" result.GateCount
            pr "  Success: %.4f" result.SuccessProbability
            pr ""

            results.Add(Map.ofList [
                "scenario", "3_4x4_system"
                "matrix", "diag(2,3,4,5)"
                "vector", "[1,0,0,0]"
                "success_probability", sprintf "%.6f" result.SuccessProbability
                "condition_number", (match result.ConditionNumber with Some k -> sprintf "%.2f" k | None -> "N/A")
                "gate_count", sprintf "%d" result.GateCount
                "backend", result.BackendName
            ])

// ============================================================================
// SCENARIO 4: Mottonen's Arbitrary State Preparation
// ============================================================================

if shouldRun "4" then
    pr "--------------------------------------------------------------------"
    pr "SCENARIO 4: Mottonen's Arbitrary State Preparation"
    pr "--------------------------------------------------------------------"
    pr ""
    pr "KEY INNOVATION:"
    pr "  Previous HHL limitation: Only encoded dominant component"
    pr "  Mottonen's method: Encodes FULL arbitrary quantum state!"
    pr ""
    pr "EXAMPLE: Encode superposition state"
    pr "  |psi> = 0.6|00> + 0.5|01> + 0.4|10> + 0.4|11>"
    pr ""

    let amplitudes = [| Complex(0.6, 0.0); Complex(0.5, 0.0)
                        Complex(0.4, 0.0); Complex(0.4, 0.0) |]

    try
        let state = normalizeState amplitudes
        pr "State normalized:"
        pr "  Dimension: 2^%d = %d" state.NumQubits state.Amplitudes.Length

        for i in 0 .. state.Amplitudes.Length - 1 do
            let prob = state.Amplitudes[i].Magnitude * state.Amplitudes[i].Magnitude
            if prob > 0.01 then
                pr "  |%s>: %.4f (prob: %.2f%%)"
                    (Convert.ToString(i, 2).PadLeft(state.NumQubits, '0'))
                    state.Amplitudes[i].Real
                    (prob * 100.0)

        pr ""
        pr "This enables HHL to solve Ax = b for ANY input vector b!"
        pr ""

        results.Add(Map.ofList [
            "scenario", "4_mottonen_state_prep"
            "num_qubits", sprintf "%d" state.NumQubits
            "dimension", sprintf "%d" state.Amplitudes.Length
            "status", "success"
        ])
    with
    | ex ->
        pr "Error: %s" ex.Message
        results.Add(Map.ofList [
            "scenario", "4_mottonen_state_prep"
            "status", sprintf "error: %s" ex.Message
        ])

// ============================================================================
// SCENARIO 5: Trotter-Suzuki Decomposition
// ============================================================================

if shouldRun "5" then
    pr "--------------------------------------------------------------------"
    pr "SCENARIO 5: Trotter-Suzuki for Non-Diagonal Matrices"
    pr "--------------------------------------------------------------------"
    pr ""
    pr "BREAKTHROUGH:"
    pr "  Previous HHL limitation: Only diagonal matrices"
    pr "  Trotter-Suzuki: Handles ANY Hermitian matrix via Pauli decomposition!"
    pr ""

    pr "EXAMPLE: Simple 2x2 matrix in Pauli basis"
    let eigenvalues = [| 2.0; 1.0 |]
    let pauliHamiltonian = decomposeDiagonalMatrixToPauli eigenvalues

    pr "  Matrix: diag(2, 1)"
    pr "  Pauli decomposition: H = sum_i c_i P_i"
    pr "  Number of terms: %d" pauliHamiltonian.Terms.Length
    pr "  Qubits: %d" pauliHamiltonian.NumQubits
    pr ""

    for term in pauliHamiltonian.Terms do
        let pauliStr = term.Operators |> String
        pr "    %s: coefficient = %.4f" pauliStr term.Coefficient.Real

    pr ""
    pr "Trotter-Suzuki Configuration:"
    let trotterConfig = {
        NumSteps = 10
        Time = 1.0
        Order = 1
    }
    pr "  Steps: %d" trotterConfig.NumSteps
    pr "  Time: %.1f" trotterConfig.Time
    pr "  Order: %d (first-order formula)" trotterConfig.Order
    pr ""

    let estimatedSteps = estimateTrotterSteps 2.0 1.0 0.01 1
    pr "  For ||H||=2, t=1, epsilon=0.01:"
    pr "  Required steps: %d" estimatedSteps
    pr ""

    results.Add(Map.ofList [
        "scenario", "5_trotter_suzuki"
        "pauli_terms", sprintf "%d" pauliHamiltonian.Terms.Length
        "num_qubits", sprintf "%d" pauliHamiltonian.NumQubits
        "trotter_steps", sprintf "%d" trotterConfig.NumSteps
        "estimated_steps", sprintf "%d" estimatedSteps
    ])

// ============================================================================
// PERFORMANCE COMPARISON (always shown unless quiet)
// ============================================================================

if shouldRun "all" || (example <> "1" && example <> "2" && example <> "3" && example <> "4" && example <> "5") then
    pr "--------------------------------------------------------------------"
    pr "QUANTUM ADVANTAGE: When HHL Beats Classical"
    pr "--------------------------------------------------------------------"
    pr ""
    pr "  N           kappa    Sparse   Classical     HHL Quantum"
    pr "  100         < 10     Yes      O(N log N)    O(log N)"
    pr "  1,000       < 100    Yes      ~10^6 ops     ~10^3 ops"
    pr "  1,000,000   < 100    Yes      ~10^12 ops    ~10^6 ops"
    pr ""
    pr "SPEEDUP FACTOR:"
    pr "  N = 1,000:     ~1,000x faster"
    pr "  N = 1,000,000: ~1,000,000x faster (EXPONENTIAL!)"
    pr ""
    pr "REQUIREMENTS FOR ADVANTAGE:"
    pr "  - Large system (N > 1000)"
    pr "  - Sparse matrix (few non-zero entries per row)"
    pr "  - Well-conditioned (kappa < 100)"
    pr "  - Quantum output acceptable (no full state tomography)"
    pr ""

    pr "--------------------------------------------------------------------"
    pr "REAL-WORLD APPLICATIONS"
    pr "--------------------------------------------------------------------"
    pr ""
    pr "1. QUANTUM CHEMISTRY"
    pr "   Problem: Compute molecular ground states"
    pr "   Matrix: Hamiltonian (sparse, Hermitian)"
    pr "   Speedup: Enables simulation of larger molecules"
    pr ""
    pr "2. MACHINE LEARNING"
    pr "   Problem: Quantum SVM, least squares regression"
    pr "   Matrix: Kernel matrix, covariance matrix"
    pr "   Speedup: Train on exponentially more data"
    pr ""
    pr "3. FINANCIAL MODELING"
    pr "   Problem: Portfolio optimization"
    pr "   Matrix: Covariance matrix of asset returns"
    pr "   Speedup: Analyze thousands of assets simultaneously"
    pr ""
    pr "4. ENGINEERING SIMULATION"
    pr "   Problem: Finite element analysis (FEA)"
    pr "   Matrix: Stiffness matrix (sparse)"
    pr "   Speedup: Simulate larger structures with finer meshes"
    pr ""

// ============================================================================
// Summary
// ============================================================================

pr "--------------------------------------------------------------------"
pr "SUMMARY: HHL Algorithm Capabilities"
pr "--------------------------------------------------------------------"
pr ""
pr "IMPLEMENTED:"
pr "  - Diagonal matrix solver (working today!)"
pr "  - Mottonen's arbitrary state preparation"
pr "  - Trotter-Suzuki non-diagonal decomposition"
pr "  - LocalBackend simulation (testing)"
pr "  - Cloud backend support (IonQ, Rigetti)"
pr ""
pr "QUANTUM ADVANTAGE:"
pr "  - Exponential speedup: O(log N) vs O(N)"
pr "  - Enables previously impossible calculations"
pr "  - Critical for quantum machine learning & chemistry"
pr ""

// ---------------------------------------------------------------------------
// Structured output
// ---------------------------------------------------------------------------

if outputPath.IsSome then
    let payload = {| script = "HHLAlgorithm.fsx"
                     timestamp = DateTime.UtcNow
                     precision = cliPrecision
                     example = example
                     results = results |> Seq.toArray |}
    Reporting.writeJson outputPath.Value payload
    pr "Results written to %s" outputPath.Value

if csvPath.IsSome then
    let header = ["scenario"; "matrix"; "vector"; "success_probability";
                  "condition_number"; "gate_count"; "backend"; "status"]
    let rows =
        results
        |> Seq.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
        |> Seq.toList
    Reporting.writeCsv csvPath.Value header rows
    pr "CSV written to %s" csvPath.Value

// Usage hints
if argv.Length = 0 && outputPath.IsNone && csvPath.IsNone then
    pr ""
    pr "Usage hints:"
    pr "  dotnet fsi HHLAlgorithm.fsx -- --example 1          # Run scenario 1 only"
    pr "  dotnet fsi HHLAlgorithm.fsx -- --precision 6         # Higher QPE precision"
    pr "  dotnet fsi HHLAlgorithm.fsx -- --quiet --output r.json"
    pr "  dotnet fsi HHLAlgorithm.fsx -- --help"
