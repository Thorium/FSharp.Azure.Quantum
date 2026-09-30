namespace FSharp.Azure.Quantum.Tests

open System.Net.Http
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Backends.CloudBackends
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.GroverSearch

/// Algorithms run end to end through the real cloud backend classes against
/// LocalQuantumService: each job is transpiled to the provider's gates, serialised in the
/// provider's format (IonQ JSON, Quil, OpenQASM), submitted over HTTP, polled, and its result
/// blob downloaded and parsed with the provider's own parser — the path a job to Azure takes,
/// with the circuit run on the local simulator instead of hardware. The tests pin the answer
/// and the number of jobs each algorithm submits, since every job is billed on Azure.
[<Collection("NonParallel")>]
module CloudEndToEndTests =

    let private options = { LocalQuantumService.defaultOptions with Seed = Some 7 }

    /// Cloud backend class for `provider` pointed at `service`, measuring `shots` per job.
    let private cloud
        (service: LocalQuantumService)
        (http: HttpClient)
        (budget: CloudBackendHelpers.JobBudget)
        (provider: string)
        (shots: int)
        : IQuantumBackend =
        let url = service.WorkspaceUrl

        match provider with
        | "ionq" -> IonQCloudBackend(http, url, "ionq.simulator", shots, jobBudget = budget) :> IQuantumBackend
        | "rigetti" -> RigettiCloudBackend(http, url, "rigetti.sim.qvm", shots, jobBudget = budget) :> IQuantumBackend
        | "quantinuum" ->
            QuantinuumCloudBackend(http, url, "quantinuum.sim.h1-1e", shots, jobBudget = budget) :> IQuantumBackend
        | "iqm" -> IqmCloudBackend(http, url, "iqm.sim", shots, jobBudget = budget) :> IQuantumBackend
        | "atom" ->
            AtomComputingCloudBackend(http, url, "atom-computing.sim", shots, jobBudget = budget) :> IQuantumBackend
        | other -> failwith $"unknown provider {other}"

    /// Run `test` against a fresh service and a backend for `provider`.
    let private withCloud (provider: string) (shots: int) (test: LocalQuantumService -> IQuantumBackend -> unit) =
        use service = LocalQuantumService.start options
        use http = service.CreateHttpClient()
        test service (cloud service http (CloudBackendHelpers.JobBudget()) provider shots)

    let private expectOk (result: Result<'T, QuantumError>) : 'T =
        match result with
        | Ok value -> value
        | Error e -> failwith $"expected Ok, got {e.Message}"

    // ========================================================================
    // ORACLE PROTOCOLS — one job each, on every provider
    // ========================================================================

    [<Theory>]
    [<InlineData("ionq")>]
    [<InlineData("rigetti")>]
    [<InlineData("quantinuum")>]
    [<InlineData("iqm")>]
    [<InlineData("atom")>]
    let ``Oracle protocols recover their answers with one job each`` (provider: string) =
        withCloud provider 200 (fun service backend ->
            let bv = BernsteinVazirani.runWithSecret [| 1; 0; 1; 1 |] backend 200 |> expectOk
            Assert.Equal<int[]>([| 1; 0; 1; 1 |], bv.RecoveredSecret)
            Assert.Equal(1, service.SubmittedJobCount)

            let dj = DeutschJozsa.runBalancedParity 3 backend 200 |> expectOk
            Assert.Equal(DeutschJozsa.Balanced, dj.OracleType)
            Assert.Equal(2, service.SubmittedJobCount)

            let constant = DeutschJozsa.runConstantZero 3 backend 200 |> expectOk
            Assert.Equal(DeutschJozsa.Constant, constant.OracleType)
            Assert.Equal(3, service.SubmittedJobCount)

            let simon = Simon.runWithSecret [| 1; 1; 0 |] backend 200 |> expectOk
            Assert.Equal<int[]>([| 1; 1; 0 |], simon.RecoveredSecret)
            Assert.Equal(4, service.SubmittedJobCount)

            let superdense = SuperdenseCoding.send11 backend |> expectOk
            Assert.True(superdense.Success, $"sent 11, received %A{superdense.ReceivedMessage}")
            Assert.Equal(5, service.SubmittedJobCount))

    // ========================================================================
    // GROVER AND SHOR
    // ========================================================================

    [<Fact>]
    let ``Grover finds the marked item and reports the shots the job measured`` () =
        withCloud "ionq" 500 (fun service backend ->
            let oracle = Oracle.forValue 5 3 |> expectOk

            let result =
                Grover.search oracle backend { Grover.defaultConfig with Shots = 1000 } |> expectOk

            Assert.Equal<int list>([ 5 ], result.Solutions)
            // 500 shots were measured, so 500 are reported, whatever the config asked for.
            Assert.Equal(500, result.Measurements |> Map.toSeq |> Seq.sumBy snd)
            Assert.Equal(1, service.SubmittedJobCount))

    [<Fact>]
    let ``Shor finds the period of 7 mod 15 from one Rigetti job`` () =
        withCloud "rigetti" 100 (fun service backend ->
            let result = Shor.findPeriodQuantum 7 15 3 backend |> expectOk
            Assert.Equal(4, result.Period)
            // Retries read further recorded shots of the same job.
            Assert.Equal(1, service.SubmittedJobCount))

    [<Fact>]
    let ``Shor on a one-shot backend submits a new job per retry`` () =
        withCloud "quantinuum" 1 (fun service backend ->
            let result = Shor.findPeriodQuantum 7 15 3 backend |> expectOk
            Assert.Equal(4, result.Period)
            Assert.Equal(result.Attempts, service.SubmittedJobCount))

    // ========================================================================
    // QKD AND TELEPORTATION
    // ========================================================================

    [<Fact>]
    let ``BB84 makes a 200-bit key from one job and detects Eve in two`` () =
        withCloud "quantinuum" 100 (fun service backend ->
            let result = QuantumKeyDistribution.runBB84 200 backend 0.15 0.11 (Some 5) |> expectOk
            Assert.Equal(0.0, result.EavesdropCheck.ErrorRate)
            Assert.True(result.Success)
            Assert.Equal(1, service.SubmittedJobCount))

        withCloud "quantinuum" 100 (fun service backend ->
            let result =
                QuantumKeyDistribution.runBB84WithEve 200 backend 0.15 0.11 (Some 5) |> expectOk

            Assert.True(result.EavesdropCheck.EavesdropDetected, $"QBER {result.EavesdropCheck.ErrorRate}")
            Assert.True(service.SubmittedJobCount <= 2, $"{service.SubmittedJobCount} jobs"))

    [<Fact>]
    let ``E91 violates CHSH over 600 pairs from two jobs, and not with Eve`` () =
        withCloud "ionq" 100 (fun service backend ->
            let result = EkertQKD.run backend 600 (Some 7) |> expectOk
            Assert.True(result.CHSHTest.S > 2.2, $"S = {result.CHSHTest.S}")
            Assert.True(result.IsSecure)
            Assert.Equal(2, service.SubmittedJobCount))

        withCloud "ionq" 100 (fun service backend ->
            let result = EkertQKD.runWithEve backend 600 (Some 7) |> expectOk
            Assert.True(abs result.CHSHTest.S < 2.0, $"S = {result.CHSHTest.S}")
            Assert.False(result.IsSecure)
            Assert.True(service.SubmittedJobCount <= 9, $"{service.SubmittedJobCount} jobs"))

    [<Fact>]
    let ``Teleportation reaches full fidelity with one job per call`` () =
        withCloud "atom" 2000 (fun service backend ->
            for name, teleport in
                [
                    "one", QuantumTeleportation.teleportOne
                    "plus", QuantumTeleportation.teleportPlus
                    "minus", QuantumTeleportation.teleportMinus
                ] do
                let result = teleport backend |> expectOk
                Assert.True(result.Fidelity > 0.95, $"{name}: fidelity {result.Fidelity}")

            Assert.Equal(3, service.SubmittedJobCount)
            // Three copies of the 3-qubit protocol side by side.
            Assert.True(service.Jobs |> List.forall (fun job -> job.InputData.Contains "qreg q[9];"))

    // ========================================================================
    // QRNG, PRIMITIVES AND JOB BUDGETS
    // ========================================================================

    [<Fact>]
    let ``QRNG takes its bits from one one-shot job per call`` () =
        withCloud "ionq" 1 (fun service backend ->
            let result = QRNG.generateWithBackend 16 backend |> Async.RunSynchronously |> expectOk
            Assert.Equal(16, result.Bits.Length)
            Assert.Equal(1, service.SubmittedJobCount))

    [<Fact>]
    let ``A job's state yields exactly the shots the service measured`` () =
        withCloud "rigetti" 300 (fun service backend ->
            let bell =
                CircuitBuilder.empty 2
                |> CircuitBuilder.addGate (CircuitBuilder.H 0)
                |> CircuitBuilder.addGate (CircuitBuilder.CNOT(0, 1))

            let counts = Primitives.sample backend bell 300 |> expectOk
            Assert.Equal(300, counts |> Map.toSeq |> Seq.sumBy snd)
            Assert.True(counts |> Map.forall (fun key _ -> key = "00" || key = "11"))

            let state = backend.ExecuteToState(CircuitAbstraction.CircuitWrapper bell) |> expectOk
            let shots = UnifiedBackend.measureState state 10000
            Assert.Equal(300, shots.Length)

            // The job's own histogram on the service, shot for shot.
            let job = service.Jobs |> List.last

            Assert.Equal(
                job.Histogram |> Map.tryFind "11" |> Option.defaultValue 0,
                shots |> Array.filter (fun s -> s = [| 1; 1 |]) |> Array.length
            ))

    [<Fact>]
    let ``A job budget stops an algorithm before the job it cannot afford`` () =
        use service = LocalQuantumService.start options
        use http = service.CreateHttpClient()
        let budget = CloudBackendHelpers.JobBudget.Limit 1
        let backend = cloud service http budget "iqm" 100

        // BB84 with Eve needs two jobs here: the second is refused, not submitted.
        match QuantumKeyDistribution.runBB84WithEve 200 backend 0.15 0.11 (Some 5) with
        | Ok _ -> Assert.Fail("the second job exceeds the budget")
        | Error(QuantumError.AzureError(AzureQuantumError.QuotaExceeded _)) -> ()
        | Error e -> Assert.Fail($"expected QuotaExceeded, got {e.Message}")

        Assert.Equal(1, service.SubmittedJobCount)
        Assert.Equal(1, budget.Submitted)
