namespace FSharp.Azure.Quantum.TaskScheduling
open System
open FSharp.Azure.Quantum.Core

/// Task Scheduling Domain Types
///
/// Core types for defining scheduling problems with dependencies,
/// resource constraints, and deadlines.
[<AutoOpen>]
module Types =

    // ============================================================================
    // DOMAIN TYPES
    // ============================================================================

    /// Scheduling objective functions
    type Objective =
        | MinimizeMakespan          // Minimize total completion time
        | MinimizeCost              // Minimize total resource cost
        | MaximizeResourceUtilization // Maximize resource usage
        | MinimizeLateness          // Minimize deadline violations

    /// Dependency relationship between tasks
    type Dependency =
        | FinishToStart of predecessorId: string * successorId: string * lag: TimeSpan

    /// Scheduled task with duration, dependencies, and constraints
    type ScheduledTask<'T> = {
        /// Task identifier (must be unique)
        Id: string

        /// Optional task payload/value (None when created via CE builder without explicit value)
        Value: 'T option

        /// Task duration (a real-time span; build with minutes/hours/days)
        Duration: TimeSpan

        /// Earliest allowed start time (offset from schedule start)
        EarliestStart: TimeSpan option

        /// Latest allowed completion time (deadline, offset from schedule start)
        Deadline: TimeSpan option
        
        /// Resource requirements (resource ID -> quantity needed)
        ResourceRequirements: Map<string, float>
        
        /// Task priority for tie-breaking (higher = more important)
        Priority: float
        
        /// Custom properties for extensibility
        Properties: Map<string, obj>
    }

    /// Resource with capacity and cost constraints
    type Resource<'T> = {
        /// Resource identifier
        Id: string
        
        /// Optional resource payload/value (None when created via CE builder without explicit value)
        Value: 'T option
        
        /// Maximum units available
        Capacity: float
        
        /// Time windows when available (start, end)
        AvailableWindows: (float * float) list
        
        /// Cost per unit per time unit
        CostPerUnit: float
        
        /// Custom properties for extensibility
        Properties: Map<string, obj>
    }

    /// Complete scheduling problem definition
    type SchedulingProblem<'TTask, 'TResource> = {
        /// Tasks to schedule
        Tasks: ScheduledTask<'TTask> list
        
        /// Available resources
        Resources: Resource<'TResource> list
        
        /// Task dependencies (finish-to-start relationships)
        Dependencies: Dependency list
        
        /// Optimization objective
        Objective: Objective

        /// Maximum time horizon to consider (real-time span)
        TimeHorizon: TimeSpan
    }

    /// Task assignment in schedule
    type TaskAssignment = {
        /// Task identifier
        TaskId: string

        /// Scheduled start time (offset from schedule start)
        StartTime: TimeSpan

        /// Scheduled end time (offset from schedule start)
        EndTime: TimeSpan

        /// Assigned resources (resource ID -> quantity allocated)
        AssignedResources: Map<string, float>
    }

    /// Complete schedule solution
    type Solution = {
        /// Task assignments
        Assignments: TaskAssignment list
        
        /// Total completion time (max end time)
        Makespan: TimeSpan

        /// Total resource usage cost
        TotalCost: float
        
        /// Resource utilization per resource (0.0-1.0)
        ResourceUtilization: Map<string, float>
        
        /// Task IDs that missed deadlines
        DeadlineViolations: string list
        
        /// True if no deadline violations
        IsValid: bool
    }

    // ============================================================================
    // TIME UNIT HELPERS
    // ============================================================================

    /// A duration of the given number of minutes.
    let minutes (value: float) : TimeSpan = TimeSpan.FromMinutes value

    /// A duration of the given number of hours.
    let hours (value: float) : TimeSpan = TimeSpan.FromHours value

    /// A duration of the given number of days.
    let days (value: float) : TimeSpan = TimeSpan.FromDays value
