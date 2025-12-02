namespace FSharp.Azure.Quantum.TaskScheduling

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
        | FinishToStart of predecessorId: string * successorId: string * lag: float

    /// Time duration helper type
    type Duration = Duration of float

    /// Scheduled task with duration, dependencies, and constraints
    type ScheduledTask<'T> = {
        /// Task identifier (must be unique)
        Id: string
        
        /// Task payload/value
        Value: 'T
        
        /// Task duration in time units (minutes)
        Duration: float
        
        /// Earliest allowed start time
        EarliestStart: float option
        
        /// Latest allowed completion time (deadline)
        Deadline: float option
        
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
        
        /// Resource payload/value
        Value: 'T
        
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
        
        /// Maximum time horizon to consider
        TimeHorizon: float
    }

    /// Task assignment in schedule
    type TaskAssignment = {
        /// Task identifier
        TaskId: string
        
        /// Scheduled start time
        StartTime: float
        
        /// Scheduled end time
        EndTime: float
        
        /// Assigned resources (resource ID -> quantity allocated)
        AssignedResources: Map<string, float>
    }

    /// Complete schedule solution
    type Solution = {
        /// Task assignments
        Assignments: TaskAssignment list
        
        /// Total completion time (max end time)
        Makespan: float
        
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

    /// Convert minutes to duration
    let minutes (value: float) : Duration = Duration value

    /// Convert hours to duration (1 hour = 60 minutes)
    let hours (value: float) : Duration = Duration (value * 60.0)

    /// Convert days to duration (1 day = 1440 minutes)
    let days (value: float) : Duration = Duration (value * 1440.0)
