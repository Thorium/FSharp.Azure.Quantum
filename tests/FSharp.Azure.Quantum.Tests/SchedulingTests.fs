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
