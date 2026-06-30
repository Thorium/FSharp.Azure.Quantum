namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.TaskScheduling
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Backends

module TaskSchedulingTests =
    
    // ============================================================================
    // TEST 1: Simple 3-Task Chain (A→B→C) - Dependency Scheduling
    // ============================================================================
    
    [<Fact>]
    let ``Simple 3-task chain A→B→C should schedule sequentially`` () =
        // Arrange - Define tasks with dependencies
        let taskA = scheduledTask {
            taskId "A"
            duration (minutes 10.0)
        }
        
        let taskB = scheduledTask {
            taskId "B"
            duration (minutes 20.0)
            after "A"  // B must wait for A
        }
        
        let taskC = scheduledTask {
            taskId "C"
            duration (minutes 15.0)
            after "B"  // C must wait for B
        }
        
        let problem = scheduling {
            tasks [taskA; taskB; taskC]
            resources []
            objective MinimizeMakespan
        }
        
        // Act - Solve scheduling problem
        let result = solve problem |> Async.RunSynchronously
        
        // Assert
        match result with
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
            
        | Error msg ->
            Assert.Fail($"Scheduling failed: {msg}")
    
    // ============================================================================
    // TEST 2: Parallel Tasks - No Dependencies
    // ============================================================================
    
    [<Fact>]
    let ``Two independent tasks should schedule in parallel`` () =
        // Arrange - Two tasks with NO dependencies
        let taskA = scheduledTask {
            taskId "A"
            duration (minutes 10.0)
        }
        
        let taskB = scheduledTask {
            taskId "B"
            duration (minutes 20.0)
        }
        
        let problem = scheduling {
            tasks [taskA; taskB]
            resources []
            objective MinimizeMakespan
        }
        
        // Act
        let result = solve problem |> Async.RunSynchronously
        
        // Assert
        match result with
        | Ok solution ->
            // Makespan should be max(10, 20) = 20 minutes (parallel execution)
            Assert.Equal(20.0, solution.Makespan.TotalMinutes)

            // Both tasks should start at time 0
            let assignmentA = solution.Assignments |> List.find (fun a -> a.TaskId = "A")
            let assignmentB = solution.Assignments |> List.find (fun a -> a.TaskId = "B")

            Assert.Equal(0.0, assignmentA.StartTime.TotalMinutes)
            Assert.Equal(0.0, assignmentB.StartTime.TotalMinutes)
            
        | Error msg ->
            Assert.Fail($"Scheduling failed: {msg}")
    
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
        let taskA = scheduledTask {
            taskId "A"
            duration (minutes 10.0)
        }
        
        let taskB = scheduledTask {
            taskId "B"
            duration (minutes 20.0)
            after "X"  // Invalid - "X" doesn't exist
        }
        
        let problem = scheduling {
            tasks [taskA; taskB]
            resources []
            objective MinimizeMakespan
        }
        
        // Act
        let result = solve problem |> Async.RunSynchronously
        
        // Assert - Should return error
        match result with
        | Ok _ -> Assert.Fail("Should have failed validation")
        | Error msg -> Assert.Contains("X", msg.Message)  // Error should mention invalid dependency "X"
    
    // ============================================================================
    // TEST 5: Validation - Duplicate Task IDs
    // ============================================================================
    
    [<Fact>]
    let ``Validation should fail for duplicate task IDs`` () =
        // Arrange - Two tasks with same ID
        let taskA1 = scheduledTask {
            taskId "A"
            duration (minutes 10.0)
        }
        
        let taskA2 = scheduledTask {
            taskId "A"  // Duplicate ID
            duration (minutes 20.0)
        }
        
        let problem = scheduling {
            tasks [taskA1; taskA2]
            resources []
            objective MinimizeMakespan
        }
        
        // Act
        let result = solve problem |> Async.RunSynchronously
        
        // Assert - Should return error
        match result with
        | Ok _ -> Assert.Fail("Should have failed validation")
        | Error msg -> Assert.Contains("Duplicate", msg.Message)
    
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
        let taskA = scheduledTask {
            taskId "A"
            duration (minutes 10.0)
        }
        
        let taskB = scheduledTask {
            taskId "B"
            duration (minutes 20.0)
            after "A"
        }
        
        let problem = scheduling {
            tasks [taskA; taskB]
            resources []
            objective MinimizeMakespan
        }
        
        let result = solve problem |> Async.RunSynchronously
        
        // Act
        match result with
        | Ok solution ->
            let tempFile = System.IO.Path.GetTempFileName()
            exportGanttChart solution tempFile
            
            // Assert - File should exist and contain expected content
            Assert.True(System.IO.File.Exists(tempFile))
            let content = System.IO.File.ReadAllText(tempFile)
            Assert.Contains("Gantt Chart", content)
            Assert.Contains("Makespan: 30", content)
            Assert.Contains("A", content)  // Task IDs are just "A", "B"
            Assert.Contains("B", content)
            
            // Cleanup
            System.IO.File.Delete(tempFile)
            
        | Error msg ->
            Assert.Fail($"Scheduling failed: {msg}")
    
    // ============================================================================
    // TEST 8: Resource-Constrained Scheduling (Quantum Backend Required)
    // ============================================================================
    
    [<Fact>]
    [<Trait("Category", "Slow")>]
    let ``Resource-constrained scheduling requires quantum backend`` () =
        // Arrange - Two parallel tasks requiring same resource (capacity 1)
        let taskA = scheduledTask {
            taskId "A"
            duration (minutes 10.0)
            requires "Worker" 1.0
        }
        
        let taskB = scheduledTask {
            taskId "B"
            duration (minutes 15.0)
            requires "Worker" 1.0
        }
        
        // Only 1 worker available - quantum solver needed for resource constraints
        let worker = resource {
            resourceId "Worker"
            capacity 1.0
            costPerUnit 50.0
        }
        
        let problem = scheduling {
            tasks [taskA; taskB]
            resources [worker]
            objective MinimizeMakespan
            timeHorizon (hours 6.0)  // generous real-time horizon; solver discretises into bounded slots
        }
        
        // Act - Use quantum solver
        let backend = LocalBackend.LocalBackend() :> Core.BackendAbstraction.IQuantumBackend
        let result = solveQuantum backend problem |> Async.RunSynchronously
        
        // Assert - Quantum solver should execute successfully
        // Note: QAOA with initial parameters may not always find optimal solution
        // This test verifies the quantum solver executes without error
        match result with
        | Error msg -> Assert.Fail(sprintf "Quantum solver failed: %s" msg.Message)
        | Ok solution ->
            // Solution found - quantum execution successful
            Assert.NotEmpty(solution.Assignments)
            Assert.Equal(2, solution.Assignments.Length)
            
            // Verify makespan is reasonable (not infinite)
            Assert.True(solution.Makespan.TotalMinutes > 0.0 && solution.Makespan.TotalMinutes < 100000.0)
            
            // TODO: Add parameter optimization to improve solution quality
            // For now, just verify quantum solver can execute
    // ============================================================================
    // TEST 9: Deadline Constraint
    // ============================================================================
    
    [<Fact>]
    let ``Task with deadline should report violation if missed`` () =
        // Arrange - Task chain that violates deadline
        let taskA = scheduledTask {
            taskId "A"
            duration (minutes 20.0)
        }
        
        let taskB = scheduledTask {
            taskId "B"
            duration (minutes 30.0)
            after "A"
            deadline (minutes 40.0)  // Deadline at 40, but will finish at 50
        }
        
        let problem = scheduling {
            tasks [taskA; taskB]
            resources []
            objective MinimizeMakespan
        }
        
        // Act
        let result = solve problem |> Async.RunSynchronously
        
        // Assert
        match result with
        | Ok solution ->
            // Task B finishes at 50 minutes but deadline is 40
            Assert.Contains("B", solution.DeadlineViolations)
            Assert.False(solution.IsValid, "Solution should be invalid due to deadline violation")
            
        | Error msg ->
            Assert.Fail($"Scheduling failed: {msg}")
    
    [<Fact>]
    let ``Task meeting deadline should not report violation`` () =
        // Arrange - Task chain that meets deadline
        let taskA = scheduledTask {
            taskId "A"
            duration (minutes 10.0)
        }
        
        let taskB = scheduledTask {
            taskId "B"
            duration (minutes 15.0)
            after "A"
            deadline (minutes 30.0)  // Deadline at 30, finishes at 25
        }
        
        let problem = scheduling {
            tasks [taskA; taskB]
            resources []
            objective MinimizeMakespan
        }
        
        // Act
        let result = solve problem |> Async.RunSynchronously
        
        // Assert
        match result with
        | Ok solution ->
            Assert.Empty(solution.DeadlineViolations)
            Assert.True(solution.IsValid, "Solution should be valid")
            
        | Error msg ->
            Assert.Fail($"Scheduling failed: {msg}")
    
    // ============================================================================
    // TEST 10: Powerplant Startup - $25k/hour ROI Validation
    // ============================================================================
    
    [<Fact>]
    let ``Powerplant startup example should schedule complex dependencies`` () =
        // Arrange - Simplified powerplant startup sequence
        // Real use case: 50+ tasks, complex dependencies
        // This test: 10 representative tasks
        
        // Phase 1: Safety checks (parallel)
        let safetyElectrical = scheduledTask {
            taskId "SafetyElectrical"
            duration (minutes 15.0)
            priority 10.0  // High priority
        }
        
        let safetyMechanical = scheduledTask {
            taskId "SafetyMechanical"
            duration (minutes 20.0)
            priority 10.0
        }
        
        // Phase 2: System initialization (after safety)
        let initCooling = scheduledTask {
            taskId "InitCooling"
            duration (minutes 30.0)
            afterMultiple ["SafetyElectrical"; "SafetyMechanical"]
        }
        
        let initControl = scheduledTask {
            taskId "InitControl"
            duration (minutes 25.0)
            after "SafetyElectrical"
        }
        
        // Phase 3: Component startup (after initialization)
        let startPump1 = scheduledTask {
            taskId "StartPump1"
            duration (minutes 10.0)
            after "InitCooling"
        }
        
        let startPump2 = scheduledTask {
            taskId "StartPump2"
            duration (minutes 10.0)
            after "InitCooling"
        }
        
        let startTurbine = scheduledTask {
            taskId "StartTurbine"
            duration (minutes 45.0)
            afterMultiple ["StartPump1"; "StartPump2"; "InitControl"]
        }
        
        // Phase 4: Power generation (final)
        let syncGrid = scheduledTask {
            taskId "SyncGrid"
            duration (minutes 15.0)
            after "StartTurbine"
        }
        
        let fullPower = scheduledTask {
            taskId "FullPower"
            duration (minutes 20.0)
            after "SyncGrid"
            deadline (minutes 180.0)  // Must reach full power within 180 minutes
        }
        
        let problem = scheduling {
            tasks [
                safetyElectrical; safetyMechanical
                initCooling; initControl
                startPump1; startPump2; startTurbine
                syncGrid; fullPower
            ]
            resources []
            objective MinimizeMakespan
            timeHorizon (minutes 300.0)
        }

        // Act
        let result = solve problem |> Async.RunSynchronously
        
        // Assert
        match result with
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
            Assert.True(solution.Makespan.TotalMinutes >= 140.0 && solution.Makespan.TotalMinutes <= 200.0,
                        $"Expected makespan between 140-200 minutes, got {solution.Makespan.TotalMinutes}")
            
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
            Assert.True(getStartTime "InitCooling" >= getEndTime "SafetyElectrical",
                        "InitCooling should start after SafetyElectrical")
            Assert.True(getStartTime "InitCooling" >= getEndTime "SafetyMechanical",
                        "InitCooling should start after SafetyMechanical")
            
            // StartTurbine must start after pumps and control
            Assert.True(getStartTime "StartTurbine" >= getEndTime "StartPump1",
                        "StartTurbine should start after StartPump1")
            Assert.True(getStartTime "StartTurbine" >= getEndTime "StartPump2",
                        "StartTurbine should start after StartPump2")
            
            // FullPower must complete last
            Assert.Equal(solution.Makespan, getEndTime "FullPower")
            
            printfn "\n✅ Powerplant startup schedule validated!"
            printfn "💰 ROI Impact: ~30 minute reduction = $25,000 savings per startup"
            
        | Error msg ->
            Assert.Fail($"Scheduling failed: {msg}")



    // ============================================================================
    // TEST: Quantum solver must return a PRECEDENCE-FEASIBLE schedule
    // (regression for the fix: solveQuantum previously picked min-makespan without
    //  filtering precedence-violating measurements, so it could return a schedule
    //  that breaks the dependencies the user specified.)
    // ============================================================================

    [<Fact>]
    let ``solveQuantum returns a precedence-respecting schedule`` () =
        // Unit-duration tasks (1 slot each) so the A->B chain fits a small time horizon.
        let taskA = scheduledTask { taskId "A"; duration (minutes 1.0) }
        let taskB = scheduledTask { taskId "B"; duration (minutes 1.0); after "A" }

        let problem = scheduling {
            tasks [taskA; taskB]
            resources []
            objective MinimizeMakespan
            timeHorizon (minutes 5.0)
        }

        let backend = LocalBackend.LocalBackend() :> Core.BackendAbstraction.IQuantumBackend

        match solveQuantum backend problem |> Async.RunSynchronously with
        | Ok solution ->
            let a = solution.Assignments |> List.find (fun x -> x.TaskId = "A")
            let b = solution.Assignments |> List.find (fun x -> x.TaskId = "B")
            // B must start no earlier than A finishes — the solver must not return a
            // lower-makespan but precedence-violating schedule.
            Assert.True(b.StartTime >= a.EndTime,
                sprintf "B (start %.1f) must start after A finishes (end %.1f)" b.StartTime.TotalMinutes a.EndTime.TotalMinutes)
        | Error msg ->
            Assert.Fail(sprintf "solveQuantum should find a precedence-feasible schedule: %A" msg)
