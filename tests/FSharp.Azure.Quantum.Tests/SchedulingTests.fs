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
