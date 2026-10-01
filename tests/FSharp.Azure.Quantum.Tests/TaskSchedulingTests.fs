namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.TaskScheduling
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Backends
open System.Threading
open System.Threading.Tasks

module TaskSchedulingTests =

    // ============================================================================
    // TEST 1: Simple 3-Task Chain (A→B→C) - Dependency Scheduling
    // ============================================================================

    [<Fact>]
    let ``Simple 3-task chain A→B→C should schedule sequentially`` () =
        // Arrange - Define tasks with dependencies
        task {
            let taskA =
                scheduledTask {
                    taskId "A"
                    duration (minutes 10.0)
                }

            let taskB =
                scheduledTask {
                    taskId "B"
                    duration (minutes 20.0)
                    after "A" // B must wait for A
                }

            let taskC =
                scheduledTask {
                    taskId "C"
                    duration (minutes 15.0)
                    after "B" // C must wait for B
                }

            let problem =
                scheduling {
                    tasks [ taskA; taskB; taskC ]
                    resources []
                    objective MinimizeMakespan
                }

            // Act - Solve scheduling problem

            // Assert
            match! solveAsync problem CancellationToken.None with
            | Ok solution ->
                // Validate makespan = 10 + 20 + 15 = 45 minutes
                Assert.Equal(45.0, solution.Makespan.TotalMinutes)

                // Validate task A starts at time 0
                let assignmentA = solution.Assignments |> List.find (fun a -> a.TaskId = "A")
                Assert.Equal(0.0, assignmentA.StartTime.TotalMinutes)
                Assert.Equal(10.0, assignmentA.EndTime.TotalMinutes)

                // Validate task B starts after A finishes
                let assignmentB = solution.Assignments |> List.find (fun a -> a.TaskId = "B")
                Assert.Equal(10.0, assignmentB.StartTime.TotalMinutes)
                Assert.Equal(30.0, assignmentB.EndTime.TotalMinutes)

                // Validate task C starts after B finishes
                let assignmentC = solution.Assignments |> List.find (fun a -> a.TaskId = "C")
                Assert.Equal(30.0, assignmentC.StartTime.TotalMinutes)
                Assert.Equal(45.0, assignmentC.EndTime.TotalMinutes)

                // Validate no deadline violations
                Assert.Empty(solution.DeadlineViolations)
                Assert.True(solution.IsValid)

            | Error msg -> Assert.Fail($"Scheduling failed: {msg}")
        }
        :> Task

    // ============================================================================
    // TEST 2: Parallel Tasks - No Dependencies
    // ============================================================================

    [<Fact; Trait("Category", "Slow")>]
    let ``Two independent tasks should schedule in parallel`` () =
        // Arrange - Two tasks with NO dependencies
        task {
            let taskA =
                scheduledTask {
                    taskId "A"
                    duration (minutes 10.0)
                }

            let taskB =
                scheduledTask {
                    taskId "B"
                    duration (minutes 20.0)
                }

            let problem =
                scheduling {
                    tasks [ taskA; taskB ]
                    resources []
                    objective MinimizeMakespan
                }

            // Act

            // Assert
            match! solveAsync problem CancellationToken.None with
            | Ok solution ->
                // Makespan should be max(10, 20) = 20 minutes (parallel execution)
                Assert.Equal(20.0, solution.Makespan.TotalMinutes)

                // Both tasks should start at time 0
                let assignmentA = solution.Assignments |> List.find (fun a -> a.TaskId = "A")
                let assignmentB = solution.Assignments |> List.find (fun a -> a.TaskId = "B")

                Assert.Equal(0.0, assignmentA.StartTime.TotalMinutes)
                Assert.Equal(0.0, assignmentB.StartTime.TotalMinutes)

            | Error msg -> Assert.Fail($"Scheduling failed: {msg}")
        }
        :> Task

    // ============================================================================
    // TEST 3: Time Unit Helpers
    // ============================================================================

    [<Fact>]
    let ``Time unit helpers should convert correctly`` () =
        // The time helpers now produce System.TimeSpan values.
        Assert.Equal(60.0, (minutes 60.0).TotalMinutes)
        Assert.Equal(60.0, (hours 1.0).TotalMinutes)
        Assert.Equal(1440.0, (days 1.0).TotalMinutes)
        Assert.Equal(120.0, (hours 2.0).TotalMinutes)

    // ============================================================================
    // TEST 4: Validation - Invalid Dependencies
    // ============================================================================

    [<Fact>]
    let ``Validation should fail for invalid task dependencies`` () =
        // Arrange - Task B depends on non-existent task "X"
        task {
            let taskA =
                scheduledTask {
                    taskId "A"
                    duration (minutes 10.0)
                }

            let taskB =
                scheduledTask {
                    taskId "B"
                    duration (minutes 20.0)
                    after "X" // Invalid - "X" doesn't exist
                }

            let problem =
                scheduling {
                    tasks [ taskA; taskB ]
                    resources []
                    objective MinimizeMakespan
                }

            // Act
            let! result = solveAsync problem CancellationToken.None

            // Assert - Should return error
            result
            |> Result.map (fun _ -> Assert.Fail("Should have failed validation"))
            |> Result.defaultWith (fun msg -> Assert.Contains("X", msg.Message))
        }
        :> Task // Error should mention invalid dependency "X"

    // ============================================================================
    // TEST 5: Validation - Duplicate Task IDs
    // ============================================================================

    [<Fact>]
    let ``Validation should fail for duplicate task IDs`` () =
        // Arrange - Two tasks with same ID
        task {
            let taskA1 =
                scheduledTask {
                    taskId "A"
                    duration (minutes 10.0)
                }

            let taskA2 =
                scheduledTask {
                    taskId "A" // Duplicate ID
                    duration (minutes 20.0)
                }

            let problem =
                scheduling {
                    tasks [ taskA1; taskA2 ]
                    resources []
                    objective MinimizeMakespan
                }

            // Act
            let! result = solveAsync problem CancellationToken.None

            // Assert - Should return error
            result
            |> Result.map (fun _ -> Assert.Fail("Should have failed validation"))
            |> Result.defaultWith (fun msg -> Assert.Contains("Duplicate", msg.Message))
        }
        :> Task

    // ============================================================================
    // TEST 6: Resource Helper - crew
    // ============================================================================

    [<Fact>]
    let ``crew helper should create resource correctly`` () =
        // Arrange & Act
        let resource = crew "SafetyCrew" 2.0 100.0

        // Assert
        Assert.Equal("SafetyCrew", resource.Id)
        Assert.Equal(2.0, resource.Capacity)
        Assert.Equal(100.0, resource.CostPerUnit)

    // ============================================================================
    // TEST 7: Gantt Chart Export
    // ============================================================================

    [<Fact>]
    let ``exportGanttChart should create text file`` () =
        // Arrange
        task {
            let taskA =
                scheduledTask {
                    taskId "A"
                    duration (minutes 10.0)
                }

            let taskB =
                scheduledTask {
                    taskId "B"
                    duration (minutes 20.0)
                    after "A"
                }

            let problem =
                scheduling {
                    tasks [ taskA; taskB ]
                    resources []
                    objective MinimizeMakespan
                }


            // Act
            match! solveAsync problem CancellationToken.None with
            | Ok solution ->
                let tempFile = System.IO.Path.GetTempFileName()
                exportGanttChart solution tempFile

                // Assert - File should exist and contain expected content
                Assert.True(System.IO.File.Exists(tempFile))
                let! content = System.IO.File.ReadAllTextAsync(tempFile)
                Assert.Contains("Gantt Chart", content)
                Assert.Contains("Makespan: 30", content)
                Assert.Contains("A", content) // Task IDs are just "A", "B"
                Assert.Contains("B", content)

                // Cleanup
                System.IO.File.Delete(tempFile)

            | Error msg -> Assert.Fail($"Scheduling failed: {msg}")
        }
        :> Task

    // ============================================================================
    // TEST 8: Resource-Constrained Scheduling (Quantum Backend Required)
    // ============================================================================

    [<Fact; Trait("Category", "Slow")>]
    let ``Resource-constrained scheduling requires quantum backend`` () =
        // Arrange - Two parallel tasks requiring same resource (capacity 1)
        task {
            let taskA =
                scheduledTask {
                    taskId "A"
                    duration (minutes 10.0)
                    requires "Worker" 1.0
                }

            let taskB =
                scheduledTask {
                    taskId "B"
                    duration (minutes 15.0)
                    requires "Worker" 1.0
                }

            // Only 1 worker available - quantum solver needed for resource constraints
            let worker =
                resource {
                    resourceId "Worker"
                    capacity 1.0
                    costPerUnit 50.0
                }

            let problem =
                scheduling {
                    tasks [ taskA; taskB ]
                    resources [ worker ]
                    objective MinimizeMakespan
                    timeHorizon (minutes 60.0) // 6 slots of 10 minutes: 2 tasks x 6 slots = 12 qubits
                }

            // Act - Use quantum solver
            let backend = LocalBackend.LocalBackend() :> Core.BackendAbstraction.IQuantumBackend

            // Assert - a sampled schedule comes back, and it never overloads the worker
            match! solveQuantumAsync backend problem CancellationToken.None with
            | Error msg -> Assert.Fail($"Quantum solver failed: %s{msg.Message}")
            | Ok solution ->
                Assert.Equal(2, solution.Assignments.Length)
                let a = solution.Assignments |> List.find (fun x -> x.TaskId = "A")
                let b = solution.Assignments |> List.find (fun x -> x.TaskId = "B")

                Assert.True(
                    a.EndTime <= b.StartTime || b.EndTime <= a.StartTime,
                    $"A (%.0f{a.StartTime.TotalMinutes}-%.0f{a.EndTime.TotalMinutes}) and B (%.0f{b.StartTime.TotalMinutes}-%.0f{b.EndTime.TotalMinutes}) overlap on the single worker"
                )

                // Both tasks in turn take at least 25 minutes
                Assert.True(solution.Makespan.TotalMinutes >= 25.0 - 1e-6)
        }
        :> Task

    // ============================================================================
    // TEST 9: Deadline Constraint
    // ============================================================================

    [<Fact>]
    let ``Task with deadline should report violation if missed`` () =
        // Arrange - Task chain that violates deadline
        task {
            let taskA =
                scheduledTask {
                    taskId "A"
                    duration (minutes 20.0)
                }

            let taskB =
                scheduledTask {
                    taskId "B"
                    duration (minutes 30.0)
                    after "A"
                    deadline (minutes 40.0) // Deadline at 40, but will finish at 50
                }

            let problem =
                scheduling {
                    tasks [ taskA; taskB ]
                    resources []
                    objective MinimizeMakespan
                }

            // Act

            // Assert
            match! solveAsync problem CancellationToken.None with
            | Ok solution ->
                // Task B finishes at 50 minutes but deadline is 40
                Assert.Contains("B", solution.DeadlineViolations)
                Assert.False(solution.IsValid, "Solution should be invalid due to deadline violation")

            | Error msg -> Assert.Fail($"Scheduling failed: {msg}")
        }
        :> Task

    [<Fact>]
    let ``Task meeting deadline should not report violation`` () =
        // Arrange - Task chain that meets deadline
        task {
            let taskA =
                scheduledTask {
                    taskId "A"
                    duration (minutes 10.0)
                }

            let taskB =
                scheduledTask {
                    taskId "B"
                    duration (minutes 15.0)
                    after "A"
                    deadline (minutes 30.0) // Deadline at 30, finishes at 25
                }

            let problem =
                scheduling {
                    tasks [ taskA; taskB ]
                    resources []
                    objective MinimizeMakespan
                }

            // Act

            // Assert
            match! solveAsync problem CancellationToken.None with
            | Ok solution ->
                Assert.Empty(solution.DeadlineViolations)
                Assert.True(solution.IsValid, "Solution should be valid")

            | Error msg -> Assert.Fail($"Scheduling failed: {msg}")
        }
        :> Task

    // ============================================================================
    // TEST 10: Powerplant Startup - $25k/hour ROI Validation
    // ============================================================================

    [<Fact>]
    let ``Powerplant startup example should schedule complex dependencies`` () =
        // Arrange - Simplified powerplant startup sequence
        // Real use case: 50+ tasks, complex dependencies
        // This test: 10 representative tasks

        // Phase 1: Safety checks (parallel)
        let safetyElectrical =
            scheduledTask {
                taskId "SafetyElectrical"
                duration (minutes 15.0)
                priority 10.0 // High priority
            }

        let safetyMechanical =
            scheduledTask {
                taskId "SafetyMechanical"
                duration (minutes 20.0)
                priority 10.0
            }

        // Phase 2: System initialization (after safety)
        let initCooling =
            scheduledTask {
                taskId "InitCooling"
                duration (minutes 30.0)
                afterMultiple [ "SafetyElectrical"; "SafetyMechanical" ]
            }

        let initControl =
            scheduledTask {
                taskId "InitControl"
                duration (minutes 25.0)
                after "SafetyElectrical"
            }

        // Phase 3: Component startup (after initialization)
        let startPump1 =
            scheduledTask {
                taskId "StartPump1"
                duration (minutes 10.0)
                after "InitCooling"
            }

        let startPump2 =
            scheduledTask {
                taskId "StartPump2"
                duration (minutes 10.0)
                after "InitCooling"
            }

        let startTurbine =
            scheduledTask {
                taskId "StartTurbine"
                duration (minutes 45.0)
                afterMultiple [ "StartPump1"; "StartPump2"; "InitControl" ]
            }

        // Phase 4: Power generation (final)
        let syncGrid =
            scheduledTask {
                taskId "SyncGrid"
                duration (minutes 15.0)
                after "StartTurbine"
            }

        let fullPower =
            scheduledTask {
                taskId "FullPower"
                duration (minutes 20.0)
                after "SyncGrid"
                deadline (minutes 180.0) // Must reach full power within 180 minutes
            }

        let problem =
            scheduling {
                tasks
                    [
                        safetyElectrical
                        safetyMechanical
                        initCooling
                        initControl
                        startPump1
                        startPump2
                        startTurbine
                        syncGrid
                        fullPower
                    ]

                resources []
                objective MinimizeMakespan
                timeHorizon (minutes 300.0)
            }

        // Act

        // Assert
        task {
            match! solveAsync problem CancellationToken.None with
            | Ok solution ->
                // Validate critical path scheduling
                // Expected critical path: SafetyMechanical (20) → InitCooling (30) → StartPump1 (10) → StartTurbine (45) → SyncGrid (15) → FullPower (20) = 140 minutes

                printfn "\n=== Powerplant Startup Schedule ==="
                printfn "Makespan: %.1f minutes" solution.Makespan.TotalMinutes
                printfn "\nTask Assignments:"

                solution.Assignments
                |> List.sortBy (fun a -> a.StartTime)
                |> List.iter (fun a ->
                    printfn "  %s: [%.1f - %.1f]" a.TaskId a.StartTime.TotalMinutes a.EndTime.TotalMinutes)

                // Verify makespan is reasonable (critical path = 140 min)
                Assert.True(
                    solution.Makespan.TotalMinutes >= 140.0
                    && solution.Makespan.TotalMinutes <= 200.0,
                    $"Expected makespan between 140-200 minutes, got {solution.Makespan.TotalMinutes}"
                )

                // Verify no deadline violations
                Assert.Empty(solution.DeadlineViolations)
                Assert.True(solution.IsValid)

                // Verify critical dependencies are respected
                let getEndTime taskId =
                    solution.Assignments
                    |> List.find (fun a -> a.TaskId = taskId)
                    |> fun a -> a.EndTime

                let getStartTime taskId =
                    solution.Assignments
                    |> List.find (fun a -> a.TaskId = taskId)
                    |> fun a -> a.StartTime

                // InitCooling must start after BOTH safety checks
                Assert.True(
                    getStartTime "InitCooling" >= getEndTime "SafetyElectrical",
                    "InitCooling should start after SafetyElectrical"
                )

                Assert.True(
                    getStartTime "InitCooling" >= getEndTime "SafetyMechanical",
                    "InitCooling should start after SafetyMechanical"
                )

                // StartTurbine must start after pumps and control
                Assert.True(
                    getStartTime "StartTurbine" >= getEndTime "StartPump1",
                    "StartTurbine should start after StartPump1"
                )

                Assert.True(
                    getStartTime "StartTurbine" >= getEndTime "StartPump2",
                    "StartTurbine should start after StartPump2"
                )

                // FullPower must complete last
                Assert.Equal(solution.Makespan, getEndTime "FullPower")

                printfn "\n✅ Powerplant startup schedule validated!"
                printfn "💰 ROI Impact: ~30 minute reduction = $25,000 savings per startup"

            | Error msg -> Assert.Fail($"Scheduling failed: {msg}")
        }
        :> Task



    // ============================================================================
    // TEST: Quantum solver must return a PRECEDENCE-FEASIBLE schedule
    // (regression for the fix: solveQuantum previously picked min-makespan without
    //  filtering precedence-violating measurements, so it could return a schedule
    //  that breaks the dependencies the user specified.)
    // ============================================================================

    [<Fact>]
    let ``solveQuantum returns a precedence-respecting schedule`` () =
        // Unit-duration tasks (1 slot each) so the A->B chain fits a small time horizon.
        task {
            let taskA =
                scheduledTask {
                    taskId "A"
                    duration (minutes 1.0)
                }

            let taskB =
                scheduledTask {
                    taskId "B"
                    duration (minutes 1.0)
                    after "A"
                }

            let problem =
                scheduling {
                    tasks [ taskA; taskB ]
                    resources []
                    objective MinimizeMakespan
                    timeHorizon (minutes 5.0)
                }

            let backend = LocalBackend.LocalBackend() :> Core.BackendAbstraction.IQuantumBackend

            match! solveQuantumAsync backend problem CancellationToken.None with
            | Ok solution ->
                let a = solution.Assignments |> List.find (fun x -> x.TaskId = "A")
                let b = solution.Assignments |> List.find (fun x -> x.TaskId = "B")
                // B must start no earlier than A finishes — the solver must not return a
                // lower-makespan but precedence-violating schedule.
                Assert.True(
                    b.StartTime >= a.EndTime,
                    $"B (start %.1f{b.StartTime.TotalMinutes}) must start after A finishes (end %.1f{a.EndTime.TotalMinutes})"
                )
            | Error msg -> Assert.Fail($"solveQuantum should find a precedence-feasible schedule: %A{msg}")
        }
        :> Task

    // ============================================================================
    // TEST: A dependency chain longer than the slot cap is sized for, and a
    // backend too small for it is refused up front
    // (regression: 8 tasks capped the grid at 2 slots, so a 5-task chain could
    //  never be placed and every run ended in "no valid solutions" after sampling.)
    // ============================================================================

    [<Fact>]
    let ``solveQuantum refuses a chain the local simulator cannot hold, naming the qubits`` () =
        task {
            let chained id after' =
                match after' with
                | Some prev ->
                    scheduledTask {
                        taskId id
                        duration (minutes 5.0)
                        after prev
                    }
                | None ->
                    scheduledTask {
                        taskId id
                        duration (minutes 5.0)
                    }

            // A 5-task chain plus 3 independent tasks: 8 tasks x 5 slots = 40 qubits.
            let tasks' =
                [
                    chained "C1" None
                    chained "C2" (Some "C1")
                    chained "C3" (Some "C2")
                    chained "C4" (Some "C3")
                    chained "C5" (Some "C4")
                    chained "I1" None
                    chained "I2" None
                    chained "I3" None
                ]

            let problem =
                scheduling {
                    tasks tasks'
                    resources []
                    objective MinimizeMakespan
                }

            let backend = LocalBackend.LocalBackend() :> Core.BackendAbstraction.IQuantumBackend

            match! solveQuantumAsync backend problem CancellationToken.None with
            | Error(QuantumError.ValidationError(field, reason)) ->
                Assert.Equal("qubits", field)
                Assert.Contains("40 qubits", reason)
                Assert.Contains("5 slots", reason)
            | Error other -> Assert.Fail($"expected a qubit-capacity validation error, got %A{other}")
            | Ok _ -> Assert.Fail("8 tasks with a 5-task chain need 40 qubits; the local simulator cannot hold them")
        }
        :> Task

    // ============================================================================
    // TEST: Resource availability windows and earliest starts
    // ============================================================================

    let private windowedResource (id: string) (windows: (float * float) list) : Resource<unit> =
        let single: Resource<unit> =
            resource {
                resourceId id
                capacity 1.0
            }

        { single with
            AvailableWindows = windows
        }

    [<Fact>]
    let ``Classical solver starts a task in the earliest availability window that fits`` () =
        let specialistTask =
            scheduledTask {
                taskId "Review"
                duration (minutes 90.0)
                requires "Specialist" 1.0
            }

        let problem =
            scheduling {
                tasks [ specialistTask ]
                // The 60-minute morning window is too short for a 90-minute task
                resources [ windowedResource "Specialist" [ (0.0, 60.0); (480.0, 960.0) ] ]
                objective MinimizeMakespan
            }

        match ClassicalSolver.solve problem with
        | Error err -> Assert.Fail($"Scheduling failed: %A{err}")
        | Ok solution ->
            let review = solution.Assignments |> List.exactlyOne
            Assert.Equal(480.0, review.StartTime.TotalMinutes)
            Assert.Equal(570.0, review.EndTime.TotalMinutes)

    [<Fact>]
    let ``Classical solver moves a dependent task into the resource window`` () =
        let prep =
            scheduledTask {
                taskId "Prep"
                duration (minutes 30.0)
            }

        let review =
            scheduledTask {
                taskId "Review"
                duration (minutes 60.0)
                after "Prep"
                requires "Specialist" 1.0
            }

        let problem =
            scheduling {
                tasks [ prep; review ]
                resources [ windowedResource "Specialist" [ (0.0, 60.0); (120.0, 240.0) ] ]
                objective MinimizeMakespan
            }

        match ClassicalSolver.solve problem with
        | Error err -> Assert.Fail($"Scheduling failed: %A{err}")
        | Ok solution ->
            // Ready at 30, but 30-90 leaves the first window; the next window opens at 120
            let r = solution.Assignments |> List.find (fun a -> a.TaskId = "Review")
            Assert.Equal(120.0, r.StartTime.TotalMinutes)

    [<Fact>]
    let ``Classical solver reports a task that fits in no availability window`` () =
        let longTask =
            scheduledTask {
                taskId "Long"
                duration (minutes 120.0)
                requires "Specialist" 1.0
            }

        let problem =
            scheduling {
                tasks [ longTask ]
                resources [ windowedResource "Specialist" [ (0.0, 60.0) ] ]
                objective MinimizeMakespan
            }

        match ClassicalSolver.solve problem with
        | Error(QuantumError.ValidationError(field, reason)) ->
            Assert.Equal("AvailableWindows", field)
            Assert.Contains("Long", reason)
        | other -> Assert.Fail($"expected an availability-window validation error, got %A{other}")

    [<Fact>]
    let ``QUBO forbids start slots outside availability windows and before earliest start`` () =
        // 4 slots of 60 minutes; variables are task-major (A: 0-3, B: 4-7)
        let taskA =
            scheduledTask {
                taskId "A"
                duration (minutes 60.0)
                requires "R" 1.0
            }

        let taskB =
            scheduledTask {
                taskId "B"
                duration (minutes 60.0)
                earliestStart (minutes 60.0)
            }

        let problem =
            scheduling {
                tasks [ taskA; taskB ]
                // A may start at 120 or 180 only (it must end by 240)
                resources [ windowedResource "R" [ (120.0, 240.0) ] ]
                objective MinimizeMakespan
            }

        let slots, slotMinutes = 4, 60.0

        let forbidden =
            FSharp.Azure.Quantum.TaskScheduling.QuboEncoding.forbiddenStartVariables problem slots slotMinutes

        Assert.Equal<Set<int>>(Set.ofList [ 0; 1; 4 ], forbidden)

        match FSharp.Azure.Quantum.TaskScheduling.QuboEncoding.toQubo problem slots slotMinutes with
        | Error err -> Assert.Fail($"toQubo failed: %A{err}")
        | Ok qubo ->
            // The lowest-energy one-hot assignment must use allowed slots only
            let energy (bits: int[]) =
                qubo.Q
                |> Map.fold (fun acc (i, j) w -> acc + w * float (bits.[i] * bits.[j])) 0.0

            let best =
                [
                    for a in 0 .. slots - 1 do
                        for b in 0 .. slots - 1 do
                            let bits = Array.zeroCreate qubo.NumVariables
                            bits.[a] <- 1
                            bits.[slots + b] <- 1
                            yield (a, b), energy bits
                ]
                |> List.minBy snd
                |> fst

            Assert.Equal((2, 1), best)

    [<Fact>]
    let ``solveQuantum schedules a task inside its resource availability window`` () =
        task {
            let taskA =
                scheduledTask {
                    taskId "A"
                    duration (minutes 10.0)
                }

            let taskB =
                scheduledTask {
                    taskId "B"
                    duration (minutes 10.0)
                    requires "R" 1.0
                }

            // 2 tasks x 4 slots of 10 minutes = 8 qubits
            let problem =
                scheduling {
                    tasks [ taskA; taskB ]
                    resources [ windowedResource "R" [ (20.0, 30.0) ] ]
                    objective MinimizeMakespan
                    timeHorizon (minutes 40.0)
                }

            let backend = LocalBackend.LocalBackend() :> Core.BackendAbstraction.IQuantumBackend

            match! solveQuantumAsync backend problem CancellationToken.None with
            | Error msg -> Assert.Fail($"solveQuantum failed: %A{msg}")
            | Ok solution ->
                let b = solution.Assignments |> List.find (fun x -> x.TaskId = "B")
                Assert.Equal(20.0, b.StartTime.TotalMinutes, 6)
                Assert.Equal(30.0, b.EndTime.TotalMinutes, 6)
        }
        :> Task

    [<Fact>]
    let ``solveQuantum honours earliestStart`` () =
        task {
            let taskA =
                scheduledTask {
                    taskId "A"
                    duration (minutes 10.0)
                    earliestStart (minutes 20.0)
                }

            let taskB =
                scheduledTask {
                    taskId "B"
                    duration (minutes 10.0)
                }

            // 2 tasks x 4 slots of 10 minutes = 8 qubits
            let problem =
                scheduling {
                    tasks [ taskA; taskB ]
                    resources []
                    objective MinimizeMakespan
                    timeHorizon (minutes 40.0)
                }

            let backend = LocalBackend.LocalBackend() :> Core.BackendAbstraction.IQuantumBackend

            match! solveQuantumAsync backend problem CancellationToken.None with
            | Error msg -> Assert.Fail($"solveQuantum failed: %A{msg}")
            | Ok solution ->
                let a = solution.Assignments |> List.find (fun x -> x.TaskId = "A")

                Assert.True(
                    a.StartTime.TotalMinutes >= 20.0 - 1e-6,
                    $"A starts at %.1f{a.StartTime.TotalMinutes} min, before its earliest start of 20 min"
                )
        }
        :> Task

    [<Fact>]
    let ``solveQuantum refuses a task no grid slot can place`` () =
        task {
            let late =
                scheduledTask {
                    taskId "Late"
                    duration (minutes 10.0)
                    earliestStart (minutes 500.0)
                }

            let problem =
                scheduling {
                    tasks [ late ]
                    resources []
                    objective MinimizeMakespan
                    timeHorizon (minutes 40.0)
                }

            let backend = LocalBackend.LocalBackend() :> Core.BackendAbstraction.IQuantumBackend

            match! solveQuantumAsync backend problem CancellationToken.None with
            | Error(QuantumError.ValidationError(field, reason)) ->
                Assert.Equal("AvailableWindows", field)
                Assert.Contains("Late", reason)
            | other -> Assert.Fail($"expected a validation error for an unplaceable task, got %A{other}")
        }
        :> Task

    // ============================================================================
    // QUBO MINIMUM = FEASIBLE ONE-HOT SCHEDULE (every bitstring enumerated, no circuit)
    // ============================================================================

    /// A slot-grid instance: tasks as (id, duration in slots, resource needs). Feasibility is
    /// checked here on slot indices, independently of the solver's own feasibility code.
    type private GridInstance =
        {
            Tasks: (string * int * (string * float) list) list
            Capacities: (string * float) list
            Dependencies: (string * string) list
            Slots: int
        }

    [<Literal>]
    let private gridSlotMinutes = 10.0

    let private gridProblem (instance: GridInstance) : SchedulingProblem<unit, unit> =
        {
            Tasks =
                instance.Tasks
                |> List.map (fun (id, slots, needs) ->
                    ({
                        Id = id
                        Value = None
                        Duration = minutes (float slots * gridSlotMinutes)
                        EarliestStart = None
                        Deadline = None
                        ResourceRequirements = Map.ofList needs
                        Priority = 1.0
                        Properties = Map.empty
                    }
                    : ScheduledTask<unit>))
            Resources =
                instance.Capacities
                |> List.map (fun (id, capacity) ->
                    ({
                        Id = id
                        Value = None
                        Capacity = capacity
                        AvailableWindows = [ (0.0, 1e6) ]
                        CostPerUnit = 1.0
                        Properties = Map.empty
                    }
                    : Resource<unit>))
            Dependencies =
                instance.Dependencies
                |> List.map (fun (pred, succ) -> FinishToStart(pred, succ, System.TimeSpan.Zero))
            Objective = MinimizeMakespan
            TimeHorizon = System.TimeSpan.Zero
        }

    /// Start slot per task when every task has exactly one start bit set.
    let private strictStarts (instance: GridInstance) (bits: int[]) : int[] option =
        let perTask =
            instance.Tasks
            |> List.mapi (fun t _ ->
                [ 0 .. instance.Slots - 1 ]
                |> List.filter (fun slot -> bits.[t * instance.Slots + slot] = 1))

        if perTask |> List.forall (fun starts -> starts.Length = 1) then
            Some(perTask |> List.map List.head |> List.toArray)
        else
            None

    let private gridFeasible (instance: GridInstance) (starts: int[]) : bool =
        let index id =
            instance.Tasks |> List.findIndex (fun (taskId, _, _) -> taskId = id)

        let slotsOf t =
            let (_, slots, _) = instance.Tasks.[t]
            slots

        let dependenciesHold =
            instance.Dependencies
            |> List.forall (fun (pred, succ) -> starts.[index succ] >= starts.[index pred] + slotsOf (index pred))

        let capacitiesHold =
            instance.Capacities
            |> List.forall (fun (resourceId, capacity) ->
                [
                    0 .. instance.Slots + (instance.Tasks |> List.sumBy (fun (_, slots, _) -> slots))
                ]
                |> List.forall (fun slot ->
                    let usage =
                        instance.Tasks
                        |> List.mapi (fun t (_, slots, needs) ->
                            if starts.[t] <= slot && slot < starts.[t] + slots then
                                needs
                                |> List.sumBy (fun (id, amount) -> if id = resourceId then amount else 0.0)
                            else
                                0.0)
                        |> List.sum

                    usage <= capacity + 1e-9))

        dependenciesHold && capacitiesHold

    let private completionSlots (instance: GridInstance) (starts: int[]) : int[] =
        instance.Tasks
        |> List.mapi (fun t (_, slots, _) -> starts.[t] + slots)
        |> List.toArray

    /// Start slots of every minimum-energy state; fails when one is not strictly one-hot
    /// or not a feasible schedule.
    let private minimumEnergySchedules (instance: GridInstance) : int[][] =
        let problem = gridProblem instance

        let qubo =
            match FSharp.Azure.Quantum.TaskScheduling.QuboEncoding.toQubo problem instance.Slots gridSlotMinutes with
            | Ok matrix -> Qubo.toDenseArray matrix.NumVariables matrix.Q
            | Error err -> failwith $"toQubo failed: %A{err}"

        let n = Array2D.length1 qubo
        Assert.Equal(instance.Tasks.Length * instance.Slots, n)

        let states =
            Array.init (1 <<< n) (fun index -> Array.init n (fun q -> (index >>> q) &&& 1))

        let energies = states |> Array.map (QaoaExecutionHelpers.evaluateQubo qubo)
        let lowest = Array.min energies

        Array.zip states energies
        |> Array.filter (fun (_, e) -> e <= lowest + 1e-9 * max 1.0 (abs lowest))
        |> Array.map (fun (state, _) ->
            match strictStarts instance state with
            | None -> failwith $"minimum-energy state %A{state} is not one-hot per task"
            | Some starts ->
                Assert.True(gridFeasible instance starts, $"minimum-energy schedule %A{starts} is infeasible")
                starts)

    /// Smallest sum of completion slots over all feasible one-hot schedules.
    let private bestCompletionSum (instance: GridInstance) : int =
        let rec schedules (t: int) : int list list =
            if t = instance.Tasks.Length then
                [ [] ]
            else
                [
                    for start in 0 .. instance.Slots - 1 do
                        for rest in schedules (t + 1) -> start :: rest
                ]

        schedules 0
        |> List.map List.toArray
        |> List.filter (gridFeasible instance)
        |> List.map (completionSlots instance >> Array.sum)
        |> List.min

    let private chainInstance =
        {
            Tasks = [ ("A", 1, []); ("B", 1, []) ]
            Capacities = []
            Dependencies = [ ("A", "B") ]
            Slots = 2
        }

    let private sharedResourceInstance =
        {
            Tasks = [ ("A", 1, [ ("m", 1.0) ]); ("B", 1, [ ("m", 1.0) ]); ("C", 1, [ ("m", 1.0) ]) ]
            Capacities = [ ("m", 2.0) ]
            Dependencies = [ ("A", "B") ]
            Slots = 3
        }

    let private unequalDurationInstance =
        {
            Tasks = [ ("A", 2, [ ("m", 1.0) ]); ("B", 1, []); ("C", 1, [ ("m", 1.0) ]) ]
            Capacities = [ ("m", 1.0) ]
            Dependencies = [ ("A", "B") ]
            Slots = 4
        }

    [<Fact>]
    let ``QUBO minimum-energy states are one-hot feasible schedules with the smallest completion-time sum`` () =
        for instance in [ chainInstance; sharedResourceInstance; unequalDurationInstance ] do
            let minima = minimumEnergySchedules instance
            Assert.NotEmpty minima
            let best = bestCompletionSum instance

            for starts in minima do
                Assert.Equal(best, completionSlots instance starts |> Array.sum)

    [<Fact>]
    let ``QUBO minimum-energy states can differ in makespan`` () =
        // A (2 slots) -> B, A and C share a unit resource: A,C-then-B and C-then-A-then-B both
        // have completion-time sum 8, with makespans 3 and 4 slots.
        let makespans =
            minimumEnergySchedules unequalDurationInstance
            |> Array.map (completionSlots unequalDurationInstance >> Array.max)
            |> Array.distinct
            |> Array.sort

        Assert.Equal<int[]>([| 3; 4 |], makespans)

    // ============================================================================
    // QUANTUM SOLVER: STRICT DECODE FIRST, REPAIR DECODE REPORTED
    // ============================================================================

    /// Backend that prepares one basis state whatever circuit it is given, so that every
    /// sample is that bitstring (bit q = qubit q).
    let private fixedSampleBackend (bits: int[]) : Core.BackendAbstraction.IQuantumBackend =
        let inner = LocalBackend.LocalBackend() :> Core.BackendAbstraction.IQuantumBackend

        let prepared () =
            let circuit =
                bits
                |> Array.indexed
                |> Array.fold
                    (fun acc (qubit, bit) ->
                        if bit = 1 then
                            CircuitBuilder.addGate (CircuitBuilder.X qubit) acc
                        else
                            acc)
                    (CircuitBuilder.empty bits.Length)

            Core.CircuitAbstraction.CircuitWrapper(circuit) :> Core.CircuitAbstraction.ICircuit

        { new Core.BackendAbstraction.IQuantumBackend with
            member _.Name = "fixed sample"
            member _.NativeStateType = inner.NativeStateType
            member _.SupportsOperation op = inner.SupportsOperation op
            member _.InitializeState n = inner.InitializeState n
            member _.ApplyOperation op state = inner.ApplyOperation op state
            member _.ApplyOperationAsync op state ct = inner.ApplyOperationAsync op state ct
            member _.ExecuteToState _ = inner.ExecuteToState(prepared ())

            member _.ExecuteToStateAsync _ ct =
                inner.ExecuteToStateAsync (prepared ()) ct
        }

    /// A -> B, 10 minutes each: a grid of 2 slots, variables A@0, A@1, B@0, B@1.
    let private chainProblem = gridProblem chainInstance

    [<Fact>]
    let ``solveQuantum returns a strictly valid sample unrepaired and counts it`` () : Task =
        task {
            // A at slot 0, B at slot 1: one start bit per task, dependency kept
            let backend = fixedSampleBackend [| 1; 0; 0; 1 |]

            match! solveQuantumAsync backend chainProblem CancellationToken.None with
            | Error err -> Assert.Fail($"solveQuantum failed: %A{err}")
            | Ok solution ->
                Assert.False(solution.WasRepaired)
                Assert.Equal(20.0, solution.Makespan.TotalMinutes, 6)

                let b = solution.Assignments |> List.find (fun x -> x.TaskId = "B")
                Assert.Equal(10.0, b.StartTime.TotalMinutes, 6)

                match solution.Sampling with
                | None -> Assert.Fail("Sampling should be reported")
                | Some sampling ->
                    Assert.Equal(QaoaExecutionHelpers.defaultConfig.FinalShots, sampling.Shots)
                    Assert.Equal(4, sampling.Qubits)
                    Assert.Equal(sampling.Shots, sampling.Valid)
                    Assert.Equal(sampling.Shots, sampling.Hits)
        }

    [<Fact>]
    let ``solveQuantum uses the repair decode only when no sample is valid, and says so`` () : Task =
        task {
            // A has both start bits set, B starts at slot 1: not one-hot, so no sample is valid.
            // The repair decode gives A its earliest slot, which is a feasible schedule.
            let backend = fixedSampleBackend [| 1; 1; 0; 1 |]

            match! solveQuantumAsync backend chainProblem CancellationToken.None with
            | Error err -> Assert.Fail($"solveQuantum failed: %A{err}")
            | Ok solution ->
                Assert.True(solution.WasRepaired)
                Assert.Equal(20.0, solution.Makespan.TotalMinutes, 6)

                match solution.Sampling with
                | None -> Assert.Fail("Sampling should be reported")
                | Some sampling ->
                    Assert.Equal(QaoaExecutionHelpers.defaultConfig.FinalShots, sampling.Shots)
                    Assert.Equal(0, sampling.Valid)
                    Assert.Equal(0, sampling.Hits)

            // With the repair decode switched off the same samples give no schedule
            let strictOnly =
                { QaoaExecutionHelpers.defaultConfig with
                    EnableConstraintRepair = false
                }

            match! solveQuantumWithConfigAsync backend chainProblem strictOnly CancellationToken.None with
            | Error(QuantumError.OperationError(_, reason)) -> Assert.Contains("No valid solutions", reason)
            | other -> Assert.Fail($"expected no valid solution without the repair decode, got %A{other}")
        }

    [<Fact>]
    let ``solveQuantum returns an error when neither decode gives a feasible schedule`` () : Task =
        task {
            // Both tasks at slot 0 only: B starts before A finishes, under either decode
            let backend = fixedSampleBackend [| 1; 0; 1; 0 |]

            match! solveQuantumAsync backend chainProblem CancellationToken.None with
            | Error(QuantumError.OperationError(_, reason)) -> Assert.Contains("No valid solutions", reason)
            | other -> Assert.Fail($"expected no valid solution, got %A{other}")
        }

    [<Fact>]
    let ``solveQuantumWithConfig samples as configured and its statistics are consistent`` () : Task =
        task {
            let problem = gridProblem sharedResourceInstance

            let config =
                { QaoaExecutionHelpers.defaultConfig with
                    NumLayers = 1
                    FinalShots = 300
                }

            let backend = LocalBackend.LocalBackend() :> Core.BackendAbstraction.IQuantumBackend

            match! solveQuantumWithConfigAsync backend problem config CancellationToken.None with
            | Error err -> Assert.Fail($"solveQuantumWithConfig failed: %A{err}")
            | Ok solution ->
                // Whatever was sampled, the schedule returned is feasible on the grid
                let starts =
                    sharedResourceInstance.Tasks
                    |> List.map (fun (id, _, _) ->
                        let assignment = solution.Assignments |> List.find (fun a -> a.TaskId = id)
                        int (System.Math.Round(assignment.StartTime.TotalMinutes / gridSlotMinutes)))
                    |> List.toArray

                Assert.True(gridFeasible sharedResourceInstance starts, $"schedule %A{starts} is infeasible")

                match solution.Sampling with
                | None -> Assert.Fail("Sampling should be reported")
                | Some sampling ->
                    Assert.Equal(300, sampling.Shots)
                    Assert.Equal(9, sampling.Qubits)

                    if solution.WasRepaired then
                        Assert.Equal(0, sampling.Valid)
                        Assert.Equal(0, sampling.Hits)
                    else
                        Assert.True(sampling.Hits >= 1)
                        Assert.True(sampling.Valid >= sampling.Hits)
        }

    [<Fact>]
    let ``Classical solver reports no repair and no sampling`` () =
        match ClassicalSolver.solve chainProblem with
        | Error err -> Assert.Fail($"Scheduling failed: %A{err}")
        | Ok solution ->
            Assert.False(solution.WasRepaired)
            Assert.True(solution.Sampling.IsNone)
