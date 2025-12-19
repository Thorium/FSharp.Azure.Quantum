namespace FSharp.Azure.Quantum.Tests

open Xunit

[<CollectionDefinition("NonParallel", DisableParallelization = true)>]
type NonParallelCollection() = class end
