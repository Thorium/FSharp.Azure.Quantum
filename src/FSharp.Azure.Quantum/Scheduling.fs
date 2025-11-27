namespace FSharp.Azure.Quantum

/// Generic Scheduling Framework for task scheduling, resource allocation, and job shop problems.
/// 
/// Provides fluent builder API for composing scheduling problems with tasks, resources,
/// dependencies, and constraints. Supports both quantum (QAOA) and classical (List Scheduling) solvers.
module Scheduling =
    
    // ============================================================================
    // CORE TYPES - Domain Model
    // ============================================================================
    
    /// Represents a task to be scheduled with duration, constraints, and resource requirements.
    type Task<'T when 'T : equality> = {
        /// Unique task identifier
        Id: string
        
        /// Task value (business data)
        Value: 'T
        
        /// Task duration in time units
        Duration: float
        
        /// Earliest allowed start time (optional)
        EarliestStart: float option
        
        /// Latest allowed completion time (deadline, optional)
        Deadline: float option
        
        /// Resource requirements (resource ID -> quantity needed)
        ResourceRequirements: Map<string, float>
        
        /// Task priority for tie-breaking (higher = more important)
        Priority: float
        
        /// Custom properties for domain-specific metadata
        Properties: Map<string, obj>
    }
    
    /// Represents a resource with capacity and availability constraints.
    type Resource<'T> = {
        /// Unique resource identifier
        Id: string
        
        /// Resource value (business data)
        Value: 'T
        
        /// Maximum capacity (units available)
        Capacity: float
        
        /// Time windows when resource is available (startTime, endTime)
        AvailableWindows: (float * float) list
        
        /// Cost per unit of resource usage
        CostPerUnit: float
        
        /// Custom properties for domain-specific metadata
        Properties: Map<string, obj>
    }
    
    // ============================================================================
    // DEPENDENCY TYPES - Task Relationships
    // ============================================================================
    
    /// Task dependency types defining temporal relationships between tasks.
    /// 
    /// Lag is the minimum time delay between the related events (can be 0 or positive).
    type Dependency =
        /// Task2 cannot start until Task1 finishes (most common)
        | FinishToStart of task1: string * task2: string * lag: float
        
        /// Task2 cannot start until Task1 starts
        | StartToStart of task1: string * task2: string * lag: float
        
        /// Task2 cannot finish until Task1 finishes
        | FinishToFinish of task1: string * task2: string * lag: float
        
        /// Task2 cannot finish until Task1 starts (rare)
        | StartToFinish of task1: string * task2: string * lag: float
    
    // ============================================================================
    // SOLUTION TYPES - Schedule Output (must be before constraints for forward ref)
    // ============================================================================
    
    /// Task assignment details in schedule.
    type TaskAssignment = {
        /// Task identifier
        TaskId: string
        
        /// Start time
        StartTime: float
        
        /// End time
        EndTime: float
        
        /// Assigned resources (resource ID -> quantity)
        AssignedResources: Map<string, float>
    }
    
    /// Resource allocation over time.
    type ResourceAllocation = {
        /// Resource identifier
        ResourceId: string
        
        /// Allocated task ID
        TaskId: string
        
        /// Allocation start time
        StartTime: float
        
        /// Allocation end time
        EndTime: float
        
        /// Quantity allocated
        Quantity: float
    }
    
    /// Complete schedule solution with task assignments and metrics.
    type Schedule = {
        /// Task assignments (task ID -> assignment details)
        TaskAssignments: Map<string, TaskAssignment>
        
        /// Resource allocations (resource ID -> time-based allocations)
        ResourceAllocations: Map<string, ResourceAllocation list>
        
        /// Total completion time (max task end time)
        Makespan: float
        
        /// Total cost of schedule
        TotalCost: float
    }
    
    // ============================================================================
    // CONSTRAINT TYPES - Problem Constraints
    // ============================================================================
    
    /// Scheduling constraints defining hard/soft rules for valid schedules.
    type SchedulingConstraint =
        /// Temporal dependencies between tasks
        | TaskDependencies of Dependency list
        
        /// Resource cannot exceed maximum capacity at any time
        | ResourceCapacity of resource: string * max: float
        
        /// Tasks must not overlap in time (e.g., same machine)
        | NoOverlap of tasks: string list
        
        /// Task must start/finish within time window
        | TimeWindow of task: string * earliest: float * latest: float
        
        /// Maximum total completion time (makespan)
        | MaxMakespan of duration: float
        
        /// Custom constraint function (for domain-specific rules)
        | Custom of (Schedule -> bool)
    
    // ============================================================================
    // OBJECTIVE TYPES - Optimization Goals
    // ============================================================================
    
    /// Scheduling objectives defining optimization criteria.
    type SchedulingObjective =
        /// Minimize total completion time (finish all tasks ASAP)
        | MinimizeMakespan
        
        /// Minimize total tardiness (delay past deadlines)
        | MinimizeTardiness
        
        /// Minimize total resource usage cost
        | MinimizeCost
        
        /// Maximize average resource utilization (keep resources busy)
        | MaximizeResourceUtilization
        
        /// Minimize total idle time across all resources
        | MinimizeIdleTime
        
        /// Custom objective function (for domain-specific goals)
        | Custom of (Schedule -> float)
    
    // ============================================================================
    // HELPER FUNCTIONS - Task and Resource Creation
    // ============================================================================
    
    /// Create a task with minimal fields (id, value, duration).
    /// All optional fields default to None/empty.
    let task (id: string) (value: 'T) (duration: float) : Task<'T> =
        {
            Id = id
            Value = value
            Duration = duration
            EarliestStart = None
            Deadline = None
            ResourceRequirements = Map.empty
            Priority = 0.0
            Properties = Map.empty
        }
    
    /// Create a task with resource requirements.
    let taskWithRequirements (id: string) (value: 'T) (duration: float) (requirements: (string * float) list) : Task<'T> =
        {
            Id = id
            Value = value
            Duration = duration
            EarliestStart = None
            Deadline = None
            ResourceRequirements = Map.ofList requirements
            Priority = 0.0
            Properties = Map.empty
        }
