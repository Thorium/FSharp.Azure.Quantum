namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Business.ConstraintScheduler
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.GroverSearch

// Mock backend that simulates Grover search results
// This avoids running the actual quantum simulation which can be slow
type MockGroverBackend(solutions: int list) =
    interface IQuantumBackend with
        member _.Name = "MockGroverBackend"
        member _.NativeStateType = QuantumStateType.GateBased
        
        member _.SupportsOperation(op) = true
        member _.InitializeState(n) = 
            // Just return dummy state, we don't use it
            Ok (QuantumState.StateVector (LocalSimulator.StateVector.init n))
            
        member _.ApplyOperation op state = Ok state
        member _.ExecuteToState _ = 
             Ok (QuantumState.StateVector (LocalSimulator.StateVector.init 1))

module ConstraintSchedulerTests =

    // Helper to extract solution bits for testing
    let runTest (problem: SchedulingProblem) (solutionBits: int) =
        // Manually trigger the decoding logic by mocking the backend/search result
        // Since we can't easily inject the mock into the internal private functions,
        // we'll test the public API with a LocalBackend and hope it finds the solution.
        // For deterministic testing, we should probably expose the decoding logic internally
        // or use reflection, but for now let's try an integration test approach with LocalBackend.
        
        // Since we can't easily mock the internal Grover search call without dependency injection
        // on the module functions, we'll use LocalBackend which implements the actual algorithm.
        // This makes these integration tests rather than unit tests.
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let problemWithBackend = { problem with Backend = Some backend; Shots = 100 }
        
        ConstraintScheduler.solve problemWithBackend

    [<Fact>]
    let ``Constraint Scheduler - Simple Conflict`` () =
        let result = constraintScheduler {
            task "T1"
            task "T2"
            
            resource "R1" 10.0
            resource "R2" 10.0
            
            conflict "T1" "T2"
            
            optimizeFor MaximizeSatisfaction
            backend (LocalBackend.LocalBackend() :> IQuantumBackend)
        }
        
        match result with
        | Ok r ->
            match r.BestSchedule with
            | Some s ->
                Assert.True(s.IsFeasible, "Schedule should be feasible")
                Assert.Equal(2, s.Assignments.Length)
                
                let t1Res = s.Assignments |> List.find (fun a -> a.Task = "T1") |> fun a -> a.Resource
                let t2Res = s.Assignments |> List.find (fun a -> a.Task = "T2") |> fun a -> a.Resource
                
                Assert.NotEqual<string>(t1Res, t2Res) // Conflict constraint
            | None -> Assert.Fail("Should have found a schedule")
        | Error e -> Assert.Fail(sprintf "Solver failed: %A" e)

    [<Fact>]
    let ``Constraint Scheduler - Resource Requirement`` () =
        let result = constraintScheduler {
            task "T1"
            resource "R1" 10.0
            resource "R2" 20.0
            
            require "T1" "R2"
            
            optimizeFor MaximizeSatisfaction
            backend (LocalBackend.LocalBackend() :> IQuantumBackend)
        }
        
        match result with
        | Ok r ->
            match r.BestSchedule with
            | Some s ->
                Assert.True(s.IsFeasible)
                let t1Res = s.Assignments |> List.find (fun a -> a.Task = "T1") |> fun a -> a.Resource
                Assert.Equal("R2", t1Res)
            | None -> Assert.Fail("Should have found a schedule")
        | Error e -> Assert.Fail(sprintf "Solver failed: %A" e)

    [<Fact>]
    let ``Constraint Scheduler - Weighted Coloring (Cost Optimization)`` () =
        let result = constraintScheduler {
            task "T1"
            task "T2"
            
            resource "Cheap" 1.0
            resource "Expensive" 10.0
            
            conflict "T1" "T2" // Must be different resources
            
            optimizeFor MinimizeCost
            backend (LocalBackend.LocalBackend() :> IQuantumBackend)
        }
        
        match result with
        | Ok r ->
            match r.BestSchedule with
            | Some s ->
                Assert.True(s.IsFeasible)
                // Optimal: One task on Cheap, one on Expensive (since conflict forces different)
                // Total cost should be 11.0
                Assert.Equal(11.0, s.TotalCost)
            | None -> Assert.Fail("Should have found a schedule")
        | Error e -> Assert.Fail(sprintf "Solver failed: %A" e)
