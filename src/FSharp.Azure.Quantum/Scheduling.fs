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
