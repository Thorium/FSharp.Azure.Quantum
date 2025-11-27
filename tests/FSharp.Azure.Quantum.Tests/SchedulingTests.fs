namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum

module SchedulingTests =

    [<Fact>]
    let ``Task creation with basic fields (id, value, duration)`` () =
        // Arrange & Act
        let task = Scheduling.task "T1" "Design Phase" 5.0
        
        // Assert
        Assert.Equal("T1", task.Id)
        Assert.Equal("Design Phase", task.Value)
        Assert.Equal(5.0, task.Duration)
        Assert.Equal(None, task.EarliestStart)
        Assert.Equal(None, task.Deadline)
        Assert.True(Map.isEmpty task.ResourceRequirements)
        Assert.Equal(0.0, task.Priority)
        Assert.True(Map.isEmpty task.Properties)
    
    [<Fact>]
    let ``Dependency types represent task relationships`` () =
        // Arrange & Act
        let finishToStart = Scheduling.FinishToStart("T1", "T2", 0.0)
        let startToStart = Scheduling.StartToStart("T1", "T2", 1.0)
        let finishToFinish = Scheduling.FinishToFinish("T1", "T2", 2.0)
        let startToFinish = Scheduling.StartToFinish("T1", "T2", 3.0)
        
        // Assert - verify dependency types exist and hold correct values
        match finishToStart with
        | Scheduling.FinishToStart(t1, t2, lag) ->
            Assert.Equal("T1", t1)
            Assert.Equal("T2", t2)
            Assert.Equal(0.0, lag)
        | _ -> Assert.Fail("Expected FinishToStart")
        
        match startToStart with
        | Scheduling.StartToStart(t1, t2, lag) ->
            Assert.Equal(1.0, lag)
        | _ -> Assert.Fail("Expected StartToStart")
    
    [<Fact>]
    let ``SchedulingConstraint types define problem constraints`` () =
        // Arrange & Act
        let deps = Scheduling.TaskDependencies([Scheduling.FinishToStart("T1", "T2", 0.0)])
        let capacity = Scheduling.ResourceCapacity("R1", 10.0)
        let noOverlap = Scheduling.NoOverlap(["T1"; "T2"; "T3"])
        let timeWindow = Scheduling.TimeWindow("T1", 0.0, 24.0)
        let maxMakespan = Scheduling.MaxMakespan(48.0)
        
        // Assert - verify constraint types exist
        match deps with
        | Scheduling.TaskDependencies(depList) ->
            Assert.Equal(1, List.length depList)
        | _ -> Assert.Fail("Expected TaskDependencies")
        
        match capacity with
        | Scheduling.ResourceCapacity(resource, max) ->
            Assert.Equal("R1", resource)
            Assert.Equal(10.0, max)
        | _ -> Assert.Fail("Expected ResourceCapacity")
    
    [<Fact>]
    let ``SchedulingObjective types define optimization goals`` () =
        // Arrange & Act
        let makespan = Scheduling.MinimizeMakespan
        let tardiness = Scheduling.MinimizeTardiness
        let cost = Scheduling.MinimizeCost
        let utilization = Scheduling.MaximizeResourceUtilization
        let idle = Scheduling.MinimizeIdleTime
        
        // Assert - verify objective types exist
        match makespan with
        | Scheduling.MinimizeMakespan -> Assert.True(true)
        | _ -> Assert.Fail("Expected MinimizeMakespan")
    
    [<Fact>]
    let ``SchedulingBuilder fluent API composes with method chaining`` () =
        // Arrange
        let task1 = Scheduling.task "T1" "Design" 5.0
        let task2 = Scheduling.task "T2" "Implement" 10.0
        let task3 = Scheduling.task "T3" "Test" 3.0
        
        let resource1 : Scheduling.Resource<string> = {
            Id = "R1"
            Value = "Engineer"
            Capacity = 1.0
            AvailableWindows = [(0.0, 24.0)]
            CostPerUnit = 100.0
            Properties = Map.empty
        }
        
        // Act - fluent builder API (immutable record pattern)
        let problem =
            Scheduling.SchedulingBuilder.Create()
                .Tasks([task1; task2; task3])
                .Resources([resource1])
                .AddDependency(Scheduling.FinishToStart("T1", "T2", 0.0))
                .AddDependency(Scheduling.FinishToStart("T2", "T3", 0.0))
                .AddConstraint(Scheduling.NoOverlap(["T1"; "T2"; "T3"]))
                .Objective(Scheduling.MinimizeMakespan)
                .TimeHorizon(24.0)
                .Build()
        
        // Assert - verify problem structure
        Assert.Equal(3, problem.Tasks.Length)
        Assert.Equal(1, problem.Resources.Length)
        Assert.Equal(2, problem.Dependencies.Length)
        Assert.Equal(1, problem.Constraints.Length)
        Assert.Equal(24.0, problem.TimeHorizon)
        
        match problem.Objective with
        | Scheduling.MinimizeMakespan -> Assert.True(true)
        | _ -> Assert.Fail("Expected MinimizeMakespan")
    
    [<Fact>]
    let ``Classical List Scheduling solves simple problem with dependencies`` () =
        // Arrange - Simple 3-task problem with dependencies
        let task1 = Scheduling.task "T1" "Design" 5.0
        let task2 = Scheduling.task "T2" "Implement" 10.0
        let task3 = Scheduling.task "T3" "Test" 3.0
        
        let resource1 : Scheduling.Resource<string> = {
            Id = "R1"
            Value = "Engineer"
            Capacity = 1.0
            AvailableWindows = [(0.0, 100.0)]
            CostPerUnit = 100.0
            Properties = Map.empty
        }
        
        let problem =
            Scheduling.SchedulingBuilder.Create()
                .Tasks([task1; task2; task3])
                .Resources([resource1])
                .AddDependency(Scheduling.FinishToStart("T1", "T2", 0.0))
                .AddDependency(Scheduling.FinishToStart("T2", "T3", 0.0))
                .Objective(Scheduling.MinimizeMakespan)
                .TimeHorizon(100.0)
                .Build()
        
        // Act - solve with classical solver
        let result = Scheduling.solveClassical problem
        
        // Assert
        match result with
        | Ok schedule ->
            // Verify all tasks are scheduled
            Assert.Equal(3, schedule.TaskAssignments.Count)
            
            // Verify dependencies are respected
            let t1 = schedule.TaskAssignments.["T1"]
            let t2 = schedule.TaskAssignments.["T2"]
            let t3 = schedule.TaskAssignments.["T3"]
            
            // T1 must finish before T2 starts
            Assert.True(t1.EndTime <= t2.StartTime, "T1 must finish before T2 starts")
            
            // T2 must finish before T3 starts
            Assert.True(t2.EndTime <= t3.StartTime, "T2 must finish before T3 starts")
            
            // Verify durations are correct
            Assert.Equal(5.0, t1.EndTime - t1.StartTime)
            Assert.Equal(10.0, t2.EndTime - t2.StartTime)
            Assert.Equal(3.0, t3.EndTime - t3.StartTime)
            
            // Verify makespan
            Assert.Equal(18.0, schedule.Makespan)  // 5 + 10 + 3 = 18 hours
            
        | Error msg ->
            Assert.Fail($"Solver failed: {msg}")
