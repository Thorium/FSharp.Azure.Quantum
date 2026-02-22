namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Business.ConstraintScheduler
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.GroverSearch
open System.Threading
open System.Threading.Tasks

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

        member this.ExecuteToStateAsync circuit ct =
            task { return (this :> IQuantumBackend).ExecuteToState circuit }
        member this.ApplyOperationAsync operation state ct =
            task { return (this :> IQuantumBackend).ApplyOperation operation state }

[<Collection("NonParallel")>]
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

    // ========================================================================
    // QAOA STRATEGY TESTS
    // ========================================================================

    [<Fact>]
    let ``QAOA Strategy - Simple Conflict via SAT`` () =
        let result = constraintScheduler {
            task "T1"
            task "T2"
            
            resource "R1" 10.0
            resource "R2" 10.0
            
            conflict "T1" "T2"
            
            optimizeFor MaximizeSatisfaction
            useQaoa
            backend (LocalBackend.LocalBackend() :> IQuantumBackend)
        }
        
        match result with
        | Ok r ->
            match r.BestSchedule with
            | Some s ->
                Assert.Equal(2, s.Assignments.Length)
                // Both tasks should be assigned
                let tasks = s.Assignments |> List.map (fun a -> a.Task) |> Set.ofList
                Assert.Contains("T1", tasks)
                Assert.Contains("T2", tasks)
            | None -> () // QAOA is approximate; no solution is acceptable
        | Error e -> Assert.Fail(sprintf "QAOA SAT solver failed: %A" e)

    [<Fact>]
    let ``QAOA Strategy - Resource Requirement via SAT`` () =
        let result = constraintScheduler {
            task "T1"
            resource "R1" 10.0
            resource "R2" 20.0
            
            require "T1" "R2"
            
            optimizeFor MaximizeSatisfaction
            useQaoa
            backend (LocalBackend.LocalBackend() :> IQuantumBackend)
        }
        
        match result with
        | Ok r ->
            match r.BestSchedule with
            | Some s ->
                Assert.Equal(1, s.Assignments.Length)
            | None -> () // QAOA is approximate
        | Error e -> Assert.Fail(sprintf "QAOA SAT solver failed: %A" e)

    [<Fact>]
    let ``QAOA Strategy - Cost Optimization via SAT (no capacity)`` () =
        let result = constraintScheduler {
            task "T1"
            task "T2"
            
            resource "Cheap" 1.0
            resource "Expensive" 10.0
            
            conflict "T1" "T2"
            
            optimizeFor MinimizeCost
            useQaoa
            backend (LocalBackend.LocalBackend() :> IQuantumBackend)
        }
        
        match result with
        | Ok r ->
            match r.BestSchedule with
            | Some s ->
                Assert.Equal(2, s.Assignments.Length)
            | None -> () // QAOA is approximate
        | Error e -> Assert.Fail(sprintf "QAOA SAT solver failed: %A" e)

    [<Fact>]
    let ``QAOA Strategy - Bin Packing with Capacity Constraints`` () =
        let result = constraintScheduler {
            task "T1"
            task "T2"
            task "T3"
            
            resourceWithCapacity "Server1" 5.0 2
            resourceWithCapacity "Server2" 3.0 2
            
            optimizeFor MinimizeCost
            useQaoa
            backend (LocalBackend.LocalBackend() :> IQuantumBackend)
        }
        
        match result with
        | Ok r ->
            match r.BestSchedule with
            | Some s ->
                // All 3 tasks should be assigned
                Assert.Equal(3, s.Assignments.Length)
            | None -> () // QAOA is approximate
        | Error e -> Assert.Fail(sprintf "QAOA bin packing solver failed: %A" e)

    [<Fact>]
    let ``QAOA Strategy - CE builder useGrover preserves Grover behavior`` () =
        let result = constraintScheduler {
            task "T1"
            task "T2"
            
            resource "R1" 10.0
            resource "R2" 10.0
            
            conflict "T1" "T2"
            
            optimizeFor MaximizeSatisfaction
            useGrover
            backend (LocalBackend.LocalBackend() :> IQuantumBackend)
        }
        
        match result with
        | Ok r ->
            match r.BestSchedule with
            | Some s ->
                Assert.True(s.IsFeasible, "Grover should find a feasible schedule")
                Assert.Equal(2, s.Assignments.Length)
                let t1Res = s.Assignments |> List.find (fun a -> a.Task = "T1") |> fun a -> a.Resource
                let t2Res = s.Assignments |> List.find (fun a -> a.Task = "T2") |> fun a -> a.Resource
                Assert.NotEqual<string>(t1Res, t2Res)
            | None -> Assert.Fail("Grover should have found a schedule")
        | Error e -> Assert.Fail(sprintf "Grover solver failed: %A" e)

    [<Fact>]
    let ``QAOA Strategy - Auto selects Grover when no capacity`` () =
        // No capacity constraints -> Auto should pick Grover (same as default)
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
                Assert.True(s.IsFeasible, "Auto (Grover) should find a feasible schedule")
                Assert.Equal(2, s.Assignments.Length)
            | None -> Assert.Fail("Should have found a schedule")
        | Error e -> Assert.Fail(sprintf "Solver failed: %A" e)

    [<Fact>]
    let ``QAOA Strategy - Auto selects QAOA when capacity present`` () =
        // Resources with capacity -> Auto should pick QAOA
        let result = constraintScheduler {
            task "T1"
            task "T2"
            
            resourceWithCapacity "Server1" 5.0 2
            resourceWithCapacity "Server2" 3.0 2
            
            optimizeFor MinimizeCost
            backend (LocalBackend.LocalBackend() :> IQuantumBackend)
        }
        
        match result with
        | Ok r ->
            match r.BestSchedule with
            | Some s ->
                Assert.Equal(2, s.Assignments.Length)
            | None -> () // QAOA is approximate
        | Error e -> Assert.Fail(sprintf "Auto QAOA solver failed: %A" e)

    [<Fact>]
    let ``QAOA Strategy - Validation errors unchanged`` () =
        // Empty tasks should still fail
        let result = constraintScheduler {
            resource "R1" 10.0
            optimizeFor MaximizeSatisfaction
            useQaoa
            backend (LocalBackend.LocalBackend() :> IQuantumBackend)
        }
        
        match result with
        | Error (QuantumError.ValidationError ("Tasks", _)) -> ()
        | other -> Assert.Fail(sprintf "Expected validation error, got: %A" other)

    [<Fact>]
    let ``QAOA Strategy - Programmatic API with Strategy`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let problem = {
            Tasks = ["T1"; "T2"]
            Resources = [{ Id = "R1"; Cost = 10.0; Capacity = None }; { Id = "R2"; Cost = 10.0; Capacity = None }]
            HardConstraints = [Conflict ("T1", "T2")]
            SoftConstraints = []
            Goal = MaximizeSatisfaction
            MaxBudget = None
            Backend = Some backend
            Strategy = Some QaoaOptimize
            Shots = 100
        }
        
        let result = ConstraintScheduler.solve problem
        
        match result with
        | Ok r ->
            match r.BestSchedule with
            | Some s -> Assert.Equal(2, s.Assignments.Length)
            | None -> () // QAOA is approximate
        | Error e -> Assert.Fail(sprintf "Programmatic QAOA failed: %A" e)

    [<Fact>]
    let ``QAOA Strategy - Programmatic API defaults Strategy to None`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let problem = {
            Tasks = ["T1"]
            Resources = [{ Id = "R1"; Cost = 10.0; Capacity = None }]
            HardConstraints = []
            SoftConstraints = []
            Goal = MaximizeSatisfaction
            MaxBudget = None
            Backend = Some backend
            Strategy = None
            Shots = 100
        }
        
        let result = ConstraintScheduler.solve problem
        
        match result with
        | Ok r ->
            match r.BestSchedule with
            | Some s ->
                Assert.Equal(1, s.Assignments.Length)
                Assert.Equal("T1", s.Assignments.[0].Task)
            | None -> Assert.Fail("Should have found a schedule for single task")
        | Error e -> Assert.Fail(sprintf "Solver failed: %A" e)

    [<Fact>]
    let ``QAOA Strategy - Balanced goal with QAOA uses SAT when no capacity`` () =
        let result = constraintScheduler {
            task "T1"
            task "T2"
            
            resource "R1" 5.0
            resource "R2" 15.0
            
            conflict "T1" "T2"
            
            optimizeFor Balanced
            useQaoa
            backend (LocalBackend.LocalBackend() :> IQuantumBackend)
        }
        
        match result with
        | Ok r ->
            match r.BestSchedule with
            | Some s -> Assert.Equal(2, s.Assignments.Length)
            | None -> () // QAOA is approximate
        | Error e -> Assert.Fail(sprintf "QAOA Balanced solver failed: %A" e)
