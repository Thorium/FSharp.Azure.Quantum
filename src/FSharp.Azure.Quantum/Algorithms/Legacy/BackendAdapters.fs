// DEPRECATED ADAPTER STUBS
namespace FSharp.Azure.Quantum.GroverSearch
open FSharp.Azure.Quantum.Core
[<System.Obsolete("Deprecated")>]
module BackendAdapter =
    type AdaptedBackend = { Execute: unit -> Result<int[][], QuantumError> }
    let createAdapter _ : AdaptedBackend = failwith "Deprecated"
    let executeGroverWithBackend _ _ _ _ : Async<Result<int[][], QuantumError>> = async { return failwith "Deprecated" }

namespace FSharp.Azure.Quantum.Algorithms
[<System.Obsolete("Deprecated")>]
module QFTBackendAdapter =
    open FSharp.Azure.Quantum.CircuitBuilder
    let qftToCircuit _ : Result<Circuit, string> = failwith "Deprecated"
[<System.Obsolete("Deprecated")>]
module QPEBackendAdapter =
    let executeWithBackend _ _ _ : Async<Result<float, string>> = async { return failwith "Deprecated" }
    let extractPhaseFromHistogram _ : float = failwith "Deprecated"
[<System.Obsolete("Deprecated")>]
module HHLBackendAdapter =
    let executeWithBackend _ _ : Async<Result<float[], string>> = async { return failwith "Deprecated" }
    let extractSolutionFromMeasurements _ : float[] = failwith "Deprecated"
    let calculateSuccessRate _ : float = failwith "Deprecated"
    let hhlToCircuit _ : Result<unit, string> = failwith "Deprecated"
[<System.Obsolete("Deprecated")>]
module ShorsBackendAdapter =
    let executeWithBackend _ _ _ : Async<Result<int, string>> = async { return failwith "Deprecated" }
    let executeShorsWithBackend _ _ _ : Async<Result<int, string>> = async { return failwith "Deprecated" }
