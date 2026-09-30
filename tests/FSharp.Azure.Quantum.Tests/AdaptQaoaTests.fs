namespace FSharp.Azure.Quantum.Tests

open System.Numerics
open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Algorithms

/// Tests for ADAPT-QAOA (adaptive mixer selection).
module AdaptQaoaTests =

    let private backend () : IQuantumBackend =
        LocalBackend.LocalBackend() :> IQuantumBackend

    let private ps (ops: char[]) (c: float) : TrotterSuzuki.PauliString =
        {
            Operators = ops
            Coefficient = Complex(c, 0.0)
        }

    [<Fact>]
    let ``ADAPT-QAOA solves a 2-qubit MaxCut (H = Z0 Z1, ground -1)`` () =
        // Minimising ⟨Z₀Z₁⟩ anti-aligns the qubits — the max cut of a single edge.
        let h: TrotterSuzuki.PauliHamiltonian =
            {
                Terms = [ ps [| 'Z'; 'Z' |] 1.0 ]
                NumQubits = 2
            }

        let pool =
            [
                ps [| 'X'; 'I' |] 1.0
                ps [| 'I'; 'X' |] 1.0
                ps [| 'Y'; 'I' |] 1.0
                ps [| 'I'; 'Y' |] 1.0
            ]

        match AdaptQaoa.run (backend ()) h pool 2 AdaptQaoa.defaultConfig with
        | Error e -> failwith $"ADAPT-QAOA failed: {e.Message}"
        | Ok result ->
            Assert.Equal(-1.0, result.Energy, 3)
            Assert.True(result.Converged)

    [<Fact>]
    let ``ADAPT-QAOA solves the frustrated triangle (ground -1)`` () =
        // Triangle MaxCut is frustrated: min Σ ZᵢZⱼ = -1 (two edges cut).
        let h: TrotterSuzuki.PauliHamiltonian =
            {
                Terms =
                    [
                        ps [| 'Z'; 'Z'; 'I' |] 1.0
                        ps [| 'I'; 'Z'; 'Z' |] 1.0
                        ps [| 'Z'; 'I'; 'Z' |] 1.0
                    ]
                NumQubits = 3
            }

        let pool =
            [
                ps [| 'X'; 'I'; 'I' |] 1.0
                ps [| 'I'; 'X'; 'I' |] 1.0
                ps [| 'I'; 'I'; 'X' |] 1.0
                ps [| 'Y'; 'I'; 'I' |] 1.0
                ps [| 'I'; 'Y'; 'I' |] 1.0
                ps [| 'I'; 'I'; 'Y' |] 1.0
            ]

        match AdaptQaoa.run (backend ()) h pool 3 AdaptQaoa.defaultConfig with
        | Error e -> failwith $"ADAPT-QAOA failed: {e.Message}"
        | Ok result ->
            Assert.Equal(-1.0, result.Energy, 3)
            // Energy never increases as layers are added.
            List.pairwise result.EnergyHistory
            |> List.iter (fun (a, b) -> Assert.True(b <= a + 1e-6, $"energy increased: {a} -> {b}"))

    [<Fact>]
    let ``ADAPT-QAOA rejects a mixer whose width mismatches the problem`` () =
        let h: TrotterSuzuki.PauliHamiltonian =
            {
                Terms = [ ps [| 'Z'; 'Z' |] 1.0 ]
                NumQubits = 2
            }

        match AdaptQaoa.run (backend ()) h [ ps [| 'X' |] 1.0 ] 2 AdaptQaoa.defaultConfig with
        | Error(QuantumError.ValidationError("pool", _)) -> ()
        | other -> failwith $"expected a pool ValidationError, got: {other}"

    [<Fact>]
    let ``ADAPT-QAOA rejects an empty mixer pool`` () =
        let h: TrotterSuzuki.PauliHamiltonian =
            {
                Terms = [ ps [| 'Z'; 'Z' |] 1.0 ]
                NumQubits = 2
            }

        match AdaptQaoa.run (backend ()) h [] 2 AdaptQaoa.defaultConfig with
        | Error(QuantumError.ValidationError("pool", _)) -> ()
        | other -> failwith $"expected a pool ValidationError, got: {other}"

    [<Fact>]
    let ``solveQubo rejects a QUBO key outside [0, numQubits) with an Error, not an exception`` () =
        // Variable index 2 with only 2 qubits — must surface as Error, not IndexOutOfRangeException.
        let qubo = Map.ofList [ ((2, 2), 1.0) ]

        match AdaptQaoa.solveQubo (backend ()) 2 qubo AdaptQaoa.defaultConfig with
        | Error(QuantumError.ValidationError("quboMap", _)) -> ()
        | other -> failwith $"expected a quboMap ValidationError, got: {other}"

    [<Fact>]
    let ``solveQubo minimises a small QUBO`` () =
        // Q = [[-1, 2], [0, -1]]: min over x∈{0,1}² of -x0 - x1 + 2 x0 x1 is -1 at (1,0) or (0,1).
        let qubo = Map.ofList [ ((0, 0), -1.0); ((1, 1), -1.0); ((0, 1), 2.0) ]

        match AdaptQaoa.solveQubo (backend ()) 2 qubo AdaptQaoa.defaultConfig with
        | Error e -> failwith $"solveQubo failed: {e.Message}"
        | Ok solution ->
            Assert.Equal(-1.0, solution.QuboCost, 3)
            Assert.Equal(1, solution.Assignment.[0] + solution.Assignment.[1]) // exactly one variable set

    [<Fact>]
    let ``MaxCut.solveWithAdaptQaoa finds the max cut of a triangle`` () =
        // Triangle MaxCut = 2 (odd cycle: one edge can't be cut).
        let triangle =
            FSharp.Azure.Quantum.MaxCut.createProblem
                [ "A"; "B"; "C" ]
                [ ("A", "B", 1.0); ("B", "C", 1.0); ("A", "C", 1.0) ]

        match FSharp.Azure.Quantum.MaxCut.solveWithAdaptQaoa triangle None with
        | Error e -> failwith $"solveWithAdaptQaoa failed: {e.Message}"
        | Ok solution ->
            Assert.Equal(2.0, solution.CutValue, 3)
            Assert.True(solution.IsQuantum)

    [<Fact>]
    let ``MaxCut.solveWithAdaptQaoa finds the max cut of a 4-cycle`` () =
        // A 4-cycle is bipartite: MaxCut cuts all 4 edges.
        let square =
            FSharp.Azure.Quantum.MaxCut.createProblem
                [ "A"; "B"; "C"; "D" ]
                [ ("A", "B", 1.0); ("B", "C", 1.0); ("C", "D", 1.0); ("D", "A", 1.0) ]

        (FSharp.Azure.Quantum.MaxCut.solveWithAdaptQaoa square None)
        |> Result.map (fun solution -> Assert.Equal(4.0, solution.CutValue, 3))
        |> Result.defaultWith (fun e -> failwith $"solveWithAdaptQaoa failed: {e.Message}")

    // ========================================================================
    // Shot-sampling (cloud) backends: measured energies, parameter-shift gradients
    // ========================================================================

    let private triangle: TrotterSuzuki.PauliHamiltonian =
        {
            Terms =
                [
                    ps [| 'Z'; 'Z'; 'I' |] 1.0
                    ps [| 'I'; 'Z'; 'Z' |] 1.0
                    ps [| 'Z'; 'I'; 'Z' |] 1.0
                ]
            NumQubits = 3
        }

    let private triangleMixers =
        [
            ps [| 'X'; 'I'; 'I' |] 1.0
            ps [| 'I'; 'X'; 'I' |] 1.0
            ps [| 'I'; 'I'; 'X' |] 1.0
            ps [| 'Y'; 'I'; 'I' |] 1.0
            ps [| 'I'; 'Y'; 'I' |] 1.0
            ps [| 'I'; 'I'; 'Y' |] 1.0
        ]

    [<Fact>]
    let ``ADAPT-QAOA on a shot-sampling backend reaches the frustrated triangle's ground energy`` () =
        let cloud = CloudStyleBackends.ShotSamplingCloud(4000, 21)

        match AdaptQaoa.run cloud triangle triangleMixers 3 AdaptQaoa.defaultConfig with
        | Error e -> failwith $"ADAPT-QAOA failed: {e.Message}"
        | Ok result ->
            Assert.True(abs (result.Energy - -1.0) < 0.05, $"sampled energy {result.Energy}")
            Assert.True(result.Layers >= 1)
            Assert.True(cloud.Jobs > 0)
            Assert.Equal(0, cloud.ApplyOperationCalls)

    [<Fact>]
    let ``solveQubo on a shot-sampling backend samples with the backend's own shots`` () =
        // Triangle MaxCut as a QUBO: cut value 2 is the optimum (cost -2).
        let qubo =
            Map.ofList
                [
                    (0, 0), -2.0
                    (1, 1), -2.0
                    (2, 2), -2.0
                    (0, 1), 2.0
                    (1, 2), 2.0
                    (0, 2), 2.0
                ]

        let cloud = CloudStyleBackends.ShotSamplingCloud(1000, 8)

        match AdaptQaoa.solveQubo cloud 3 qubo AdaptQaoa.defaultConfig with
        | Error e -> failwith $"solveQubo failed: {e.Message}"
        | Ok solution ->
            Assert.Equal(-2.0, solution.QuboCost)
            // The final sample is the last job, measured with the backend's 1000 shots.
            Assert.Equal(1000, cloud.Histograms |> List.last |> Map.toSeq |> Seq.sumBy snd)

    // ========================================================================
    // Cloud job cap and the sampled energy's standard error
    // ========================================================================

    [<Fact>]
    let ``ADAPT-QAOA job estimate matches the jobs a capped-off run submits`` () =
        // Triangle: 1 measurement group, 3 cost terms, 6 mixers → per layer L: 13 + 320·L.
        Assert.Equal(1 + 333, AdaptQaoa.estimateCloudJobs triangle triangleMixers.Length 1)
        Assert.Equal(1 + 333 + 653, AdaptQaoa.estimateCloudJobs triangle triangleMixers.Length 2)

    [<Fact>]
    let ``ADAPT-QAOA stops at the default cap with the best ansatz so far`` () =
        // Transverse-field Ising chain: 2 measurement groups (Z, X), 5 cost terms. Uncapped,
        // this run submits 9,730 jobs over 4 layers (10 layers could need 53,062); a second
        // layer needs 1,946 jobs on top of the first 988, over the default 2,000.
        let tfim: TrotterSuzuki.PauliHamiltonian =
            {
                Terms =
                    [
                        ps [| 'Z'; 'Z'; 'I' |] 1.0
                        ps [| 'I'; 'Z'; 'Z' |] 1.0
                        ps [| 'X'; 'I'; 'I' |] 0.5
                        ps [| 'I'; 'X'; 'I' |] 0.7
                        ps [| 'I'; 'I'; 'X' |] 0.3
                    ]
                NumQubits = 3
            }

        Assert.Equal(988, AdaptQaoa.estimateCloudJobs tfim triangleMixers.Length 1)
        Assert.Equal(2934, AdaptQaoa.estimateCloudJobs tfim triangleMixers.Length 2)
        let cloud = CloudStyleBackends.ShotSamplingCloud(4000, 21)

        match AdaptQaoa.run cloud tfim triangleMixers 3 AdaptQaoa.defaultConfig with
        | Ok result ->
            Assert.True(result.JobCapReached)
            Assert.False(result.Converged)
            Assert.Equal(1, result.Layers)
            Assert.Equal(988, result.CloudJobs)
            Assert.Equal(988, cloud.Jobs)

            // Not an eigenstate: the X-group outcomes vary shot to shot.
            match result.EnergyStandardError with
            | Some sigma -> Assert.InRange(sigma, 0.005, 0.05)
            | None -> failwith "a sampled energy must report its standard error"
        | Error e -> failwith $"ADAPT-QAOA failed: {e.Message}"

    [<Fact>]
    let ``ADAPT-QAOA refuses up front, before any job, when the first layer cannot fit under MaxCloudJobs`` () =
        let cloud = CloudStyleBackends.ShotSamplingCloud(4000, 21)

        let config =
            { AdaptQaoa.defaultConfig with
                MaxCloudJobs = Some 100
            }

        match AdaptQaoa.run cloud triangle triangleMixers 3 config with
        | Error(QuantumError.ValidationError("MaxCloudJobs", message)) ->
            Assert.Contains("334", message)
            Assert.Equal(0, cloud.Jobs)
        | other -> failwith $"expected a MaxCloudJobs refusal, got {other}"

    [<Fact>]
    let ``ADAPT-QAOA on an exact backend reports no standard error and no cloud jobs`` () =
        match AdaptQaoa.run (backend ()) triangle triangleMixers 3 AdaptQaoa.defaultConfig with
        | Error e -> failwith $"ADAPT-QAOA failed: {e.Message}"
        | Ok result ->
            Assert.Equal(None, result.EnergyStandardError)
            Assert.Equal(0, result.CloudJobs)
