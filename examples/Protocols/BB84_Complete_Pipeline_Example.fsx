/// Quick test of complete BB84 QKD pipeline
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open FSharp.Azure.Quantum.Algorithms.QuantumKeyDistribution
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core.BackendAbstraction

let backend = LocalBackend() :> IQuantumBackend

printfn "Testing Complete BB84 QKD Pipeline..."
printfn ""

match runCompleteQKD 256 backend true 128 None with
| Ok result ->
    printfn "%s" (formatCompleteQKDResult result)
    printfn ""
    printfn "✅ Test PASSED"
| Error err ->
    printfn "❌ Test FAILED: %A" err
