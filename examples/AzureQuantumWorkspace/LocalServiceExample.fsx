#!/usr/bin/env dotnet fsi
// ============================================================================
// Azure Quantum cloud backends against the local REST emulator
// ============================================================================
//
// Starts LocalQuantumService on 127.0.0.1 and runs a Bell circuit through the
// IonQ, Rigetti and Quantinuum cloud backends. Every job goes through the real
// Azure job lifecycle over HTTP (submit, poll, download the result blob, parse
// the provider's result format) and runs on the local simulator. Nothing is
// sent to Azure and no credential is needed.
//
// Usage: dotnet fsi LocalServiceExample.fsx
//
// ============================================================================

#r "nuget: Azure.Identity, 1.21.0"
#r "nuget: Microsoft.Extensions.Logging, 10.0.10"
// The library comes from NuGet; `dotnet fsi --define:LOCAL_BUILD <script>` uses the repo's Debug build.
#if LOCAL_BUILD
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#else
#r "nuget: FSharp.Azure.Quantum"
#endif

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Backends.CloudBackends

let bell =
    CircuitBuilder.empty 2
    |> CircuitBuilder.addGate (CircuitBuilder.H 0)
    |> CircuitBuilder.addGate (CircuitBuilder.CNOT(0, 1))
    |> wrapCircuit

let service =
    LocalQuantumService.start
        { LocalQuantumService.defaultOptions with
            Seed = Some 2024
            PollsBeforeCompletion = 1 // report Executing once before the final status
        }

let http = service.CreateHttpClient()
printfn "LocalQuantumService listening at %s" service.WorkspaceUrl

let backends: IQuantumBackend list =
    [
        IonQCloudBackend(http, service.WorkspaceUrl, "ionq.simulator", 1000)
        RigettiCloudBackend(http, service.WorkspaceUrl, "rigetti.sim.qvm", 1000)
        QuantinuumCloudBackend(http, service.WorkspaceUrl, "quantinuum.sim.h1-1e", 1000)
    ]

for backend in backends do
    match backend.ExecuteToState bell with
    | Ok _ ->
        let job = service.Jobs |> List.last
        printfn "%-35s %A  statuses %A  counts %A" backend.Name job.Status job.StatusHistory job.Histogram
    | Error err -> printfn "%-35s failed: %s" backend.Name err.Message

printfn "Jobs submitted: %d" service.SubmittedJobCount

http.Dispose()
(service :> System.IDisposable).Dispose()
