---
layout: default
title: Local Azure Quantum Service
---

# Local Azure Quantum Service

**Run the real Azure Quantum code path on your own machine.** `LocalQuantumService` is an optional, in-process emulator of the Azure Quantum workspace REST API. The cloud backends (IonQ, Rigetti, Quantinuum, IQM, Atom Computing) submit jobs to it over real HTTP exactly as they would to Azure, and it runs each job on the library's local simulator. No request leaves the machine, no Azure account or credential is needed, and nothing is billed.

Use it to:

- test code that uses `IonQCloudBackend`, `RigettiCloudBackend` and the other cloud backends before spending money;
- run the whole job lifecycle (serialize → `PUT` job → poll status → download the result blob → provider result parser → `QuantumState`) in unit tests;
- inject failures, slow jobs and rejected submissions to test error handling and `JobBudget` limits.

Nothing starts unless you call `LocalQuantumService.start`. If you only need simulation results, use `LocalBackend` directly (see [Local Simulation](local-simulation)); the service is for exercising the Azure path.

## Quick Start

```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Backends.CloudBackends

// Listens on http://127.0.0.1:<free port>/ (localhost on Windows) until disposed
use service = LocalQuantumService.start LocalQuantumService.defaultOptions

// An HttpClient with the library's bearer-token and throttling handlers, a dummy
// token, and routing that keeps every request on the local service
use http = service.CreateHttpClient()

// Any cloud backend, pointed at the local workspace URL
let backend =
    IonQCloudBackend(http, service.WorkspaceUrl, "ionq.simulator", 1000) :> IQuantumBackend

let bell =
    CircuitBuilder.empty 2
    |> CircuitBuilder.addGate (CircuitBuilder.H 0)
    |> CircuitBuilder.addGate (CircuitBuilder.CNOT(0, 1))
    |> wrapCircuit

match backend.ExecuteToState bell with
| Ok _ ->
    let job = service.Jobs |> List.exactlyOne
    printfn "Job %s: %A, counts %A" job.JobId job.Status job.Histogram // only "00" and "11"
| Error err -> printfn "Failed: %s" err.Message
```

Swap in `RigettiCloudBackend(http, service.WorkspaceUrl, "rigetti.sim.qvm", 1000)`, `QuantinuumCloudBackend(..., "quantinuum.sim.h1-1e", ...)`, `IqmCloudBackend(..., "iqm.sim", ...)` or `AtomComputingCloudBackend(..., "atom-computing.sim", ...)`; every algorithm, solver and ML builder that accepts an `IQuantumBackend` works the same way. The `CloudBackendFactory.create*` functions take the same `http` and `service.WorkspaceUrl`.

## What the Service Does

| Request | Behaviour |
|---------|-----------|
| `PUT {workspace}/jobs/{id}` | Accepts the job (`201 Created`, status `Waiting`), decodes the program and runs it on the local simulator with the requested shots (`shots` or Rigetti's `count`). |
| `GET {workspace}/jobs/{id}` | Reports `Waiting` / `Executing` according to the configured schedule, then `Succeeded` with an `outputDataUri`, or `Failed` with `errorData`. |
| `GET {base}/blobs/...?sig=...` | Serves the result blob. Like Azure Storage, it accepts only SAS requests *without* a bearer token. |
| `DELETE {workspace}/jobs/{id}`, `POST .../cancel` | Cancels a job that has not yet reported a final status. |
| `GET {workspace}/jobs` | Lists jobs with `nextLink` paging. |
| `POST {workspace}/storage/sasUri` | Hands out SAS URIs for the local blob store (`inputDataUri` pointing there is also accepted). |

Programs are decoded from the formats the library emits, and results are written in the shapes the library's parsers read:

| Provider | Input decoded | Result blob |
|----------|---------------|-------------|
| IonQ | `ionq.circuit.v1` JSON | `{"histogram": {"<decimal basis index>": probability}}` |
| Rigetti | Quil (`DECLARE`, gates, `MEASURE q ro[i]`, `DAGGER`, `pi` expressions) | `{"ro": [[...] per shot]}` (`rigetti.quil-results.v1`) |
| Quantinuum | OpenQASM 2.0/3.0 via `OpenQasmImport` | `{"c": ["<bits>" per shot]}` (`honeywell.quantum-results.v1`) |
| IQM, Atom Computing | OpenQASM 2.0/3.0 via `OpenQasmImport` | `{"results": {"<bits>": count}}` |

Bitstrings put classical bit 0 on the right, the convention `CloudBackendHelpers.histogramToQuantumState` reads. A program with measurements only at the end is sampled from its final state vector; mid-circuit measurement, `reset` and classically controlled gates are simulated shot by shot.

A job the service cannot run ends `Failed`, never with made-up counts: an undecodable program (`InvalidCircuit`, or `InvalidProgram` for Rigetti), QIR input, the IonQ native gate set, or a program wider than `MaxQubits` (`TooManyQubits`). A target from any other provider is rejected at submission with `400 InvalidTarget`.

Differences from the real service to keep in mind:

- Like the real providers, the service reads out only what a program measures: an unmeasured classical bit reads 0 on every shot, and the job carries a warning saying so. The Rigetti, Quantinuum, IQM and Atom Computing cloud backends measure every qubit at the end of a circuit that measures none (`CloudBackendHelpers.withTerminalMeasurements`); an IonQ circuit measures every qubit by definition.
- The Rigetti and Quantinuum result blobs carry only the provider's per-shot register (`ro`, `c`), which `RigettiBackend.parseRigettiResults` and `QuantinuumBackend.parseQuantinuumResult` count into a histogram.
- Results are noiseless simulator samples, not hardware results.

## Options

Start from `LocalQuantumService.defaultOptions` and override what you need:

```fsharp
open System

let options =
    { LocalQuantumService.defaultOptions with
        Seed = Some 42                             // reproducible sampling
        PollsBeforeCompletion = 2                  // Waiting, then Executing, then the final status
        ExecutionDelay = TimeSpan.FromSeconds 1.0  // or: final status only after this long
        MaxQubits = 16 }

use service = LocalQuantumService.start options
```

| Option | Default | Meaning |
|--------|---------|---------|
| `Port` | `0` | Loopback port; `0` picks a free one |
| `RequireAuthentication` / `AccessToken` | `true` / dummy | Workspace requests need `Authorization: Bearer <AccessToken>` (401 otherwise) |
| `MaxQubits` | 20 (or less if memory is short) | Wider programs fail with `TooManyQubits` |
| `DefaultShots` | 500 | Shots when a job names none |
| `Seed` | `None` | Seed for measurement sampling |
| `PollsBeforeCompletion` | 0 | Status polls answered `Waiting`/`Executing` before the final status |
| `ExecutionDelay` | 0 | Minimum time before the final status is reported |
| `JobsPageSize` | 100 | Page size of `GET /jobs` |
| `SubscriptionId`, `ResourceGroup`, `WorkspaceName`, `Location` | local placeholders | Used to build `WorkspaceUrl` and `CreateClientConfig` |

The library polls every 2 s, doubling up to 30 s, so each extra poll adds seconds to a job; keep `PollsBeforeCompletion` small in tests.

## Testing Failure Paths

```fsharp
open System.Net

service.FailNextJobs 1                     // next job ends Failed ("BackendUnavailable" by default)
service.FailNextJobs(1, "InvalidCircuit", "rejected by test")
service.RejectNextSubmissions(1, HttpStatusCode.ServiceUnavailable) // next PUT answers 503

let budget = CloudBackendHelpers.JobBudget.Limit 2  // the backends refuse the third job
```

Inspect what happened with `service.SubmittedJobCount`, `service.Jobs` (each job's status history, poll count, histogram, result blob and warnings), `service.TryGetJob id` and `service.Requests` (every HTTP request, including whether it carried a bearer token).

A test with xUnit:

```fsharp
open Xunit

[<Fact>]
let ``Bell state through IonQ returns only 00 and 11`` () =
    use service = LocalQuantumService.start { LocalQuantumService.defaultOptions with Seed = Some 1 }
    use http = service.CreateHttpClient()
    let backend = IonQCloudBackend(http, service.WorkspaceUrl, "ionq.simulator", 1000) :> IQuantumBackend

    match backend.ExecuteToState bell with
    | Ok _ ->
        let job = service.Jobs |> List.exactlyOne
        Assert.Equal(1000, job.Histogram |> Map.toSeq |> Seq.sumBy snd)
        Assert.True(job.Histogram |> Map.forall (fun bits _ -> bits = "00" || bits = "11"))
    | Error err -> Assert.Fail err.Message
```

## Other Clients

- **`JobLifecycle` and the provider modules** (`IonQBackend.submitAndWaitForResultsAsync` and the others) take the same `http` and `service.WorkspaceUrl`.
- **`Client.QuantumClient`** builds its URLs from the workspace location (`https://{location}.quantum.azure.com/...`). `service.CreateClientConfig http` returns a config for the local workspace, and the client from `CreateHttpClient` routes that host to the service:

  ```fsharp
  let client = Client.QuantumClient(service.CreateClientConfig http)
  ```

- **Your own HTTP pipeline**: `service.Credential` is a `TokenCredential` that hands out the dummy token without contacting Azure AD.

The client from `CreateHttpClient` refuses every host other than the service and `*.quantum.azure.com` (which it routes to the service), so a misconfigured URL fails instead of reaching Azure. `AzureQuantumWorkspace.QuantumWorkspace` (quotas and provider status through the Microsoft SDK) is not emulated.
