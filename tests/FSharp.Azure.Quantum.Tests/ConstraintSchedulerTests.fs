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
            Ok(QuantumState.StateVector(LocalSimulator.StateVector.init n))

        member _.ApplyOperation op state = Ok state

        member _.ExecuteToState _ =
            Ok(QuantumState.StateVector(LocalSimulator.StateVector.init 1))

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

        let problemWithBackend =
            { problem with
                Backend = Some backend
                Shots = 100
            }

        ConstraintScheduler.solveAsync problemWithBackend CancellationToken.None

    [<Fact>]
    let ``Constraint Scheduler - Simple Conflict`` () =
        task {
            let! result =
                constraintScheduler {
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

                    let t1Res =
                        s.Assignments |> List.find (fun a -> a.Task = "T1") |> fun a -> a.Resource

                    let t2Res =
                        s.Assignments |> List.find (fun a -> a.Task = "T2") |> fun a -> a.Resource

                    Assert.NotEqual<string>(t1Res, t2Res) // Conflict constraint
                | None -> Assert.Fail("Should have found a schedule")
            | Error e -> Assert.Fail($"Solver failed: %A{e}")
        }
        :> Task

    [<Fact>]
    let ``Constraint Scheduler - Resource Requirement`` () =
        task {
            let! result =
                constraintScheduler {
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

                    let t1Res =
                        s.Assignments |> List.find (fun a -> a.Task = "T1") |> fun a -> a.Resource

                    Assert.Equal("R2", t1Res)
                | None -> Assert.Fail("Should have found a schedule")
            | Error e -> Assert.Fail($"Solver failed: %A{e}")
        }
        :> Task

    [<Fact>]
    let ``Constraint Scheduler - Weighted Coloring (Cost Optimization)`` () =
        task {
            let! result =
                constraintScheduler {
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
            | Error e -> Assert.Fail($"Solver failed: %A{e}")
        }
        :> Task

    // ========================================================================
    // QAOA STRATEGY TESTS
    // ========================================================================

    [<Fact>]
    let ``QAOA Strategy - Simple Conflict via SAT`` () =
        task {
            let! result =
                constraintScheduler {
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
            | Error e -> Assert.Fail($"QAOA SAT solver failed: %A{e}")
        }
        :> Task

    [<Fact>]
    let ``QAOA Strategy - Resource Requirement via SAT`` () =
        task {
            let! result =
                constraintScheduler {
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
                | Some s -> Assert.Equal(1, s.Assignments.Length)
                | None -> () // QAOA is approximate
            | Error e -> Assert.Fail($"QAOA SAT solver failed: %A{e}")
        }
        :> Task

    [<Fact>]
    let ``QAOA Strategy - Cost Optimization via SAT (no capacity)`` () =
        task {
            let! result =
                constraintScheduler {
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
                | Some s -> Assert.Equal(2, s.Assignments.Length)
                | None -> () // QAOA is approximate
            | Error e -> Assert.Fail($"QAOA SAT solver failed: %A{e}")
        }
        :> Task

    /// What IsFeasible = true promises, read from the assignment list alone: every task on
    /// exactly one resource and no resource over its capacity.
    let private assertFeasibleMeansWithinCapacity (tasks: string list) (capacities: (string * int) list) (s: Schedule) =
        if s.IsFeasible then
            Assert.Equal<string list>(List.sort tasks, s.Assignments |> List.map (fun a -> a.Task) |> List.sort)

            for resource, capacity in capacities do
                let load =
                    s.Assignments |> List.filter (fun a -> a.Resource = resource) |> List.length

                Assert.True(load <= capacity, $"{resource} carries {load} tasks, capacity {capacity}")

    [<Fact>]
    let ``QAOA Strategy - Capacity QUBO with Capacity Constraints`` () =
        task {
            let! result =
                constraintScheduler {
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
                    assertFeasibleMeansWithinCapacity [ "T1"; "T2"; "T3" ] [ "Server1", 2; "Server2", 2 ] s
                    // The cheapest feasible schedule puts two tasks on Server2: 3 + 3 + 5
                    if s.IsFeasible then
                        Assert.True(s.TotalCost >= 11.0, $"cost {s.TotalCost} is below the cheapest feasible schedule")
                | None -> () // QAOA is approximate
            | Error e -> Assert.Fail($"QAOA capacity solver failed: %A{e}")
        }
        :> Task

    [<Fact>]
    let ``QAOA Strategy - CE builder useGrover preserves Grover behavior`` () =
        task {
            let! result =
                constraintScheduler {
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

                    let t1Res =
                        s.Assignments |> List.find (fun a -> a.Task = "T1") |> fun a -> a.Resource

                    let t2Res =
                        s.Assignments |> List.find (fun a -> a.Task = "T2") |> fun a -> a.Resource

                    Assert.NotEqual<string>(t1Res, t2Res)
                | None -> Assert.Fail("Grover should have found a schedule")
            | Error e -> Assert.Fail($"Grover solver failed: %A{e}")
        }
        :> Task

    [<Fact>]
    let ``QAOA Strategy - Auto selects Grover when no capacity`` () =
        task {
            // No capacity constraints -> Auto should pick Grover (same as default)
            let! result =
                constraintScheduler {
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
            | Error e -> Assert.Fail($"Solver failed: %A{e}")
        }
        :> Task

    [<Fact>]
    let ``QAOA Strategy - Auto selects QAOA when capacity present`` () =
        task {
            // Resources with capacity -> Auto should pick QAOA
            let! result =
                constraintScheduler {
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
                | Some s -> assertFeasibleMeansWithinCapacity [ "T1"; "T2" ] [ "Server1", 2; "Server2", 2 ] s
                | None -> () // QAOA is approximate
            | Error e -> Assert.Fail($"Auto QAOA solver failed: %A{e}")
        }
        :> Task

    [<Fact>]
    let ``QAOA Strategy - Validation errors unchanged`` () =
        task {
            // Empty tasks should still fail
            let! result =
                constraintScheduler {
                    resource "R1" 10.0
                    optimizeFor MaximizeSatisfaction
                    useQaoa
                    backend (LocalBackend.LocalBackend() :> IQuantumBackend)
                }

            match result with
            | Error(QuantumError.ValidationError("Tasks", _)) -> ()
            | other -> Assert.Fail($"Expected validation error, got: %A{other}")
        }
        :> Task

    [<Fact>]
    let ``QAOA Strategy - Programmatic API with Strategy`` () =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend

            let problem =
                {
                    Tasks = [ "T1"; "T2" ]
                    Resources =
                        [
                            {
                                Id = "R1"
                                Cost = 10.0
                                Capacity = None
                            }
                            {
                                Id = "R2"
                                Cost = 10.0
                                Capacity = None
                            }
                        ]
                    HardConstraints = [ Conflict("T1", "T2") ]
                    SoftConstraints = []
                    Goal = MaximizeSatisfaction
                    MaxBudget = None
                    Backend = Some backend
                    Strategy = Some QaoaOptimize
                    Shots = 100
                }

            match! ConstraintScheduler.solveAsync problem CancellationToken.None with
            | Ok r ->
                match r.BestSchedule with
                | Some s -> Assert.Equal(2, s.Assignments.Length)
                | None -> () // QAOA is approximate
            | Error e -> Assert.Fail($"Programmatic QAOA failed: %A{e}")
        }
        :> Task

    [<Fact>]
    let ``QAOA Strategy - Programmatic API defaults Strategy to None`` () =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend

            let problem =
                {
                    Tasks = [ "T1" ]
                    Resources =
                        [
                            {
                                Id = "R1"
                                Cost = 10.0
                                Capacity = None
                            }
                        ]
                    HardConstraints = []
                    SoftConstraints = []
                    Goal = MaximizeSatisfaction
                    MaxBudget = None
                    Backend = Some backend
                    Strategy = None
                    Shots = 100
                }

            match! ConstraintScheduler.solveAsync problem CancellationToken.None with
            | Ok r ->
                match r.BestSchedule with
                | Some s ->
                    Assert.Equal(1, s.Assignments.Length)
                    Assert.Equal("T1", s.Assignments.[0].Task)
                | None -> Assert.Fail("Should have found a schedule for single task")
            | Error e -> Assert.Fail($"Solver failed: %A{e}")
        }
        :> Task

    [<Fact>]
    let ``Cost goal routes to cost-aware coloring even when QAOA is requested`` () =
        task {
            // A cost goal (Balanced/MinimizeCost) without capacity constraints is routed to
            // the weighted graph-colouring formulation, which is the only encoding that
            // genuinely carries resource costs — regardless of the QAOA strategy hint.
            let! result =
                constraintScheduler {
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
                | None -> () // quantum search is approximate
            | Error e -> Assert.Fail($"Cost-goal solver failed: %A{e}")
        }
        :> Task

    [<Fact>]
    let ``Precedence constraints are rejected honestly rather than silently ignored`` () =
        task {
            // The resource-assignment scheduler has no time dimension, so precedence
            // (temporal ordering) cannot be honoured. solve must surface this as an error
            // instead of returning a schedule that quietly ignores the constraint.
            let! result =
                constraintScheduler {
                    task "T1"
                    task "T2"

                    resource "R1" 10.0
                    resource "R2" 10.0

                    precedence "T1" "T2"

                    optimizeFor MaximizeSatisfaction
                    backend (LocalBackend.LocalBackend() :> IQuantumBackend)
                }

            match result with
            | Error(QuantumError.NotImplemented(feature, _)) -> Assert.Contains("Precedence", feature)
            | Error e -> Assert.Fail($"Expected NotImplemented for precedence, got: %A{e}")
            | Ok _ -> Assert.Fail("Precedence constraint should be rejected, not silently ignored")
        }
        :> Task

    // ========================================================================
    // CAPACITY AND FEASIBILITY (energy-only brute force, no circuit)
    // ========================================================================

    let private problemOf
        (goal: OptimizationGoal)
        (tasks: string list)
        (resources: (string * float * int option) list)
        (hard: HardConstraint list)
        : SchedulingProblem =
        {
            Tasks = tasks
            Resources =
                resources
                |> List.map (fun (id, cost, capacity) ->
                    {
                        Id = id
                        Cost = cost
                        Capacity = capacity
                    })
            HardConstraints = hard
            SoftConstraints = []
            Goal = goal
            MaxBudget = None
            Backend = None
            Strategy = None
            Shots = 200
        }

    let private bitsOf (numQubits: int) (index: int) : int[] =
        Array.init numQubits (fun q -> (index >>> q) &&& 1)

    /// The resources each task is on, read from the task-resource bits x[t, r] at t * R + r.
    let private resourcesPerTask (problem: SchedulingProblem) (bits: int[]) : int list list =
        let numResources = problem.Resources.Length

        problem.Tasks
        |> List.mapi (fun t _ ->
            [ 0 .. numResources - 1 ]
            |> List.filter (fun r -> bits.[t * numResources + r] = 1))

    /// Some cost when the bits are a feasible schedule: every task on exactly one resource,
    /// every resource within its own capacity, every Conflict and RequiresResource satisfied.
    let private feasibleCost (problem: SchedulingProblem) (bits: int[]) : float option =
        let resources = problem.Resources |> List.toArray
        let perTask = resourcesPerTask problem bits

        if perTask |> List.forall (fun rs -> rs.Length = 1) then
            let resourceOf = perTask |> List.map List.head |> List.toArray

            let taskIndex task =
                problem.Tasks |> List.findIndex ((=) task)

            let withinCapacity =
                resources
                |> Array.mapi (fun r res ->
                    let load = resourceOf |> Array.filter ((=) r) |> Array.length
                    res.Capacity |> Option.forall (fun capacity -> load <= capacity))
                |> Array.forall id

            let hardSatisfied =
                problem.HardConstraints
                |> List.forall (function
                    | Conflict(a, b) -> resourceOf.[taskIndex a] <> resourceOf.[taskIndex b]
                    | RequiresResource(task, resource) -> resources.[resourceOf.[taskIndex task]].Id = resource
                    | Precedence _ -> true)

            if withinCapacity && hardSatisfied then
                Some(resourceOf |> Array.sumBy (fun r -> resources.[r].Cost))
            else
                None
        else
            None

    /// Minimum-energy bitstrings of a QUBO, by enumeration.
    let private groundStates (qubo: float[,]) : int[] list =
        let numQubits = qubo.GetLength 0
        Assert.True(numQubits <= 16, $"{numQubits} qubits is too many to enumerate")

        let energies =
            Array.init (1 <<< numQubits) (fun index -> QaoaExecutionHelpers.evaluateQubo qubo (bitsOf numQubits index))

        let minimum = Array.min energies

        [ 0 .. energies.Length - 1 ]
        |> List.filter (fun index -> energies.[index] <= minimum + 1e-9)
        |> List.map (bitsOf numQubits)

    [<Fact>]
    let ``every minimum-energy bitstring of the capacity QUBO is a feasible schedule of least cost`` () =
        let tasks = [ "T1"; "T2"; "T3" ]

        let problems =
            [
                // own capacities 2 and 1: both resources full
                problemOf Balanced tasks [ "Alice", 25.0, Some 2; "Bob", 15.0, Some 1 ] []
                // hard constraints in the QUBO
                problemOf
                    Balanced
                    tasks
                    [ "Alice", 25.0, Some 2; "Bob", 15.0, Some 2 ]
                    [ Conflict("T1", "T2"); RequiresResource("T3", "Bob") ]
                // a resource without capacity next to limited ones
                problemOf MinimizeCost tasks [ "A", 5.0, Some 1; "B", 3.0, Some 2; "C", 10.0, None ] []
                // no cost terms for the satisfaction goal
                problemOf
                    MaximizeSatisfaction
                    tasks
                    [ "Alice", 25.0, Some 2; "Bob", 15.0, Some 2 ]
                    [ Conflict("T1", "T2") ]
                // a negative cost and a resource that takes nothing
                problemOf MinimizeCost [ "T1"; "T2" ] [ "A", -5.0, Some 1; "B", 3.0, None; "C", 1.0, Some 0 ] []
            ]

        for problem in problems do
            match ConstraintScheduler.toCapacityQubo problem with
            | Error err -> Assert.Fail($"toCapacityQubo failed: {err}")
            | Ok qubo ->
                let assignmentBits = problem.Tasks.Length * problem.Resources.Length

                let leastCost =
                    [ 0 .. (1 <<< assignmentBits) - 1 ]
                    |> List.choose (bitsOf assignmentBits >> feasibleCost problem)
                    |> List.min

                for bits in groundStates qubo do
                    let text = bits |> Array.map string |> String.concat ""

                    match feasibleCost problem bits with
                    | None -> Assert.Fail($"%A{problem.Resources}: ground state %s{text} is not a feasible schedule")
                    | Some cost ->
                        if problem.Goal <> MaximizeSatisfaction then
                            Assert.Equal(leastCost, cost, 9)

    [<Fact>]
    let ``capacity QUBO has task-resource bits plus slack bits of the limited resources`` () =
        let tasks = [ "T1"; "T2"; "T3" ]

        let qubits problem =
            match ConstraintScheduler.toCapacityQubo problem with
            | Ok qubo -> qubo.GetLength 0
            | Error err -> failwith $"{err}"

        // capacities 2 + 1 = 3 tasks: both loads are fixed, no slack
        Assert.Equal(6, qubits (problemOf Balanced tasks [ "Alice", 25.0, Some 2; "Bob", 15.0, Some 1 ] []))
        // capacities 2 and 2: each resource carries 1 or 2 tasks, one slack bit each
        Assert.Equal(8, qubits (problemOf Balanced tasks [ "Alice", 25.0, Some 2; "Bob", 15.0, Some 2 ] []))
        // a capacity of at least the task count adds no term
        Assert.Equal(6, qubits (problemOf Balanced tasks [ "Alice", 25.0, Some 3; "Bob", 15.0, None ] []))

    [<Fact>]
    let ``capacities below the task count are rejected`` () =
        task {
            let problem =
                problemOf Balanced [ "T1"; "T2"; "T3" ] [ "Alice", 25.0, Some 1; "Bob", 15.0, Some 1 ] []

            match! ConstraintScheduler.solveAsync problem CancellationToken.None with
            | Error(QuantumError.ValidationError("Resources", _)) -> ()
            | other -> Assert.Fail($"Expected a Resources validation error, got: %A{other}")
        }
        :> Task

    [<Fact>]
    let ``IsFeasible requires one resource per task, capacity, hard constraints and the budget`` () =
        let problem =
            problemOf
                Balanced
                [ "T1"; "T2"; "T3" ]
                [ "Alice", 25.0, Some 2; "Bob", 15.0, Some 1 ]
                [ Conflict("T1", "T2") ]

        let schedule (pairs: (string * string) list) =
            pairs
            |> List.map (fun (task, resource) ->
                {
                    Task = task
                    Resource = resource
                    Cost = 1.0
                })
            |> ConstraintScheduler.createSchedule problem

        Assert.True((schedule [ "T1", "Alice"; "T2", "Bob"; "T3", "Alice" ]).IsFeasible)
        // Bob carries two tasks, capacity 1
        Assert.False((schedule [ "T1", "Alice"; "T2", "Bob"; "T3", "Bob" ]).IsFeasible)
        // T1 and T2 share Alice
        Assert.False((schedule [ "T1", "Alice"; "T2", "Alice"; "T3", "Bob" ]).IsFeasible)
        // T3 is on two resources
        Assert.False((schedule [ "T1", "Alice"; "T2", "Bob"; "T3", "Alice"; "T3", "Bob" ]).IsFeasible)
        // total cost 3.0 against a budget of 2.5
        let overBudget =
            [ "T1", "Alice"; "T2", "Bob"; "T3", "Alice" ]
            |> List.map (fun (task, resource) ->
                {
                    Task = task
                    Resource = resource
                    Cost = 1.0
                })

        Assert.False((ConstraintScheduler.createSchedule { problem with MaxBudget = Some 2.5 } overBudget).IsFeasible)
        Assert.True((ConstraintScheduler.createSchedule { problem with MaxBudget = Some 3.0 } overBudget).IsFeasible)

        // T3 is unassigned
        Assert.False((schedule [ "T1", "Alice"; "T2", "Bob" ]).IsFeasible)

        // A conflict is judged on every resource a task is on
        let doubled = schedule [ "T1", "Alice"; "T1", "Bob"; "T2", "Alice"; "T3", "Alice" ]
        Assert.Equal(0, doubled.HardConstraintsSatisfied)

    [<Fact>]
    let ``capacity schedules reported feasible respect each resource's own capacity`` () =
        task {
            let tasks = [ "T1"; "T2"; "T3" ]

            let problem =
                { problemOf Balanced tasks [ "Alice", 25.0, Some 2; "Bob", 15.0, Some 1 ] [] with
                    Backend = Some(LocalBackend.LocalBackend() :> IQuantumBackend)
                }

            match! ConstraintScheduler.solveAsync problem CancellationToken.None with
            | Error e -> Assert.Fail($"Solver failed: %A{e}")
            | Ok r ->
                match r.BestSchedule with
                | None -> Assert.Fail("The capacity path returns the decoded sample")
                | Some s ->
                    assertFeasibleMeansWithinCapacity tasks [ "Alice", 2; "Bob", 1 ] s
                    // Feasible means two tasks on Alice and one on Bob
                    if s.IsFeasible then
                        Assert.Equal(65.0, s.TotalCost)
        }
        :> Task

    [<Fact>]
    let ``every minimum-energy bitstring of the QAOA SAT encoding assigns each task once`` () =
        let overConstrained =
            [
                // one task required on two resources
                problemOf
                    MaximizeSatisfaction
                    [ "T1" ]
                    [ "R1", 1.0, None; "R2", 1.0, None ]
                    [ RequiresResource("T1", "R1"); RequiresResource("T1", "R2") ]
                // two conflicting tasks, one resource
                problemOf MaximizeSatisfaction [ "T1"; "T2" ] [ "R1", 1.0, None ] [ Conflict("T1", "T2") ]
                // one task required on each of three resources, the second requirement listed twice
                problemOf
                    MaximizeSatisfaction
                    [ "T1" ]
                    [ "R1", 1.0, None; "R2", 1.0, None; "R3", 1.0, None ]
                    [
                        RequiresResource("T1", "R1")
                        RequiresResource("T1", "R2")
                        RequiresResource("T1", "R2")
                        RequiresResource("T1", "R3")
                    ]
            ]

        for problem in overConstrained do
            let satProblem = ConstraintScheduler.toQaoaSatProblem problem

            match FSharp.Azure.Quantum.Quantum.QuantumSatSolver.toQubo satProblem with
            | Error err -> Assert.Fail($"toQubo failed: {err}")
            | Ok qubo ->
                for bits in groundStates qubo do
                    let perTask = resourcesPerTask problem bits

                    Assert.True(
                        perTask |> List.forall (fun rs -> rs.Length = 1),
                        $"%A{problem.HardConstraints}: a ground state puts the tasks on %A{perTask}"
                    )
