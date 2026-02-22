namespace FSharp.Azure.Quantum.Topological.Tests

open System
open System.Threading
open System.Threading.Tasks
open Xunit
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Topological.TopologicalBuilderExtensions

module VisualizationTests =
    
    open FSharp.Azure.Quantum.Core
    open FSharp.Azure.Quantum.Core.BackendAbstraction
    
    // Mock backend for testing visualization without full simulation overhead
    // Implements unified IQuantumBackend interface
    type MockBackend() =
        interface IQuantumBackend with
            member _.Name = "Mock Visualization Backend"
            
            member _.NativeStateType = QuantumStateType.TopologicalBraiding
            
            member _.SupportsOperation _ = true
            
            member _.ExecuteToState _ = 
                Error (QuantumError.NotImplemented ("ExecuteToState", Some "not implemented in mock"))

            member _.InitializeState (numQubits: int) =
                // Return dummy state with correct number of anyons
                // numQubits maps to anyons: usually 2*N or similar.
                // For visualization, we just need a non-empty state.
                let anyonCount = numQubits // Simplified mapping for mock
                
                // Create a chain of anyons
                let rec createTree count =
                    if count <= 1 then
                        FusionTree.leaf AnyonSpecies.Particle.Vacuum
                    else
                        let left = FusionTree.leaf AnyonSpecies.Particle.Vacuum
                        let right = createTree (count - 1)
                        // Correctly use 'fuse' helper or constructor
                        FusionTree.fuse left right AnyonSpecies.Particle.Vacuum

                let tree = createTree anyonCount
                let state = FusionTree.create tree AnyonSpecies.AnyonType.Ising
                let sup = TopologicalOperations.pureState state
                let fs = QuantumState.FusionSuperposition (TopologicalOperations.toInterface sup)
                Ok fs
                
            member _.ApplyOperation (op: QuantumOperation) (state: QuantumState) =
                match state with
                | QuantumState.FusionSuperposition fs ->
                    match op with
                    | QuantumOperation.Braid _ -> Ok state
                    | QuantumOperation.Measure _ -> Ok state
                    | _ -> Ok state
                | _ -> Error (QuantumError.ValidationError ("state", "Mock requires FusionSuperposition"))

            member this.ExecuteToStateAsync (circuit: FSharp.Azure.Quantum.Core.CircuitAbstraction.ICircuit) (_ct: CancellationToken) : Task<Result<QuantumState, QuantumError>> =
                task { return (this :> IQuantumBackend).ExecuteToState circuit }

            member this.ApplyOperationAsync (operation: QuantumOperation) (state: QuantumState) (_ct: CancellationToken) : Task<Result<QuantumState, QuantumError>> =
                task { return (this :> IQuantumBackend).ApplyOperation operation state }

    // Helper to ignore Result value for 'do!' bindings in builder
    let ignoreResult (task: System.Threading.Tasks.Task<Result<TopologicalBuilder.BuilderContext, QuantumError>>) =
        task

    [<Fact>]
    let ``Circuit visualization produces valid Mermaid diagram`` () =
        task {
            let backend = MockBackend()
            
            // Construct the program function manually to match the expected signature
            // Builder.Run expects: BuilderContext -> Task<Result<'a * BuilderContext, QuantumError>>
            // The computation expression returns exactly that.
            let program = topological backend {
                 do! TopologicalBuilder.initialize AnyonSpecies.AnyonType.Ising 4
                 do! TopologicalBuilder.braid 0
                 do! TopologicalBuilder.braid 1
                 do! TopologicalBuilder.braid 2
                 
                 let! _ = TopologicalBuilder.measure 0
                 return ()
            }
            
            // Execute
            let! result = TopologicalBuilder.executeWithContext backend program
            
            match result with
            | Ok ((), context) ->
                // Generate diagram
                let mermaid = context.ToMermaid()
                
                // Verify output
                Assert.Contains("sequenceDiagram", mermaid)
                Assert.Contains("Initialize 4 Ising Anyons", mermaid)
                Assert.Contains("Braid σ0", mermaid)
                Assert.Contains("Braid σ1", mermaid)
                Assert.Contains("Braid σ2", mermaid)
                Assert.Contains("Measure Fusion", mermaid)
                
                // Print for manual inspection (visible in test output)
                printfn "%s" mermaid
                
            | Error err ->
                Assert.Fail(sprintf "Program execution failed: %s" err.Message)
        } :> Task

