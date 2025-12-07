namespace FSharp.Azure.Quantum

open FSharp.Azure.Quantum.Core

// Open the TaskScheduling namespace to make all types available
open FSharp.Azure.Quantum.TaskScheduling.Types

/// Task Scheduling Domain Builder - F# Computation Expression API
///
/// DESIGN PHILOSOPHY:
/// This is a BUSINESS DOMAIN API for users who want to solve scheduling problems
/// with dependencies, resource constraints, and deadlines - without needing to
/// understand quantum computing internals (QAOA, QUBO, backends).
///
/// WHAT IS TASK SCHEDULING:
/// Assign tasks to resources over time while respecting:
/// - Precedence constraints (task A must complete before task B)
/// - Resource constraints (limited machines, workers, tools)
/// - Deadlines (tasks must complete by specific times)
/// - Optimization objectives (minimize makespan, cost, lateness)
///
/// USE CASES:
/// - Manufacturing: Production line scheduling with dependencies
/// - Cloud Computing: Container orchestration, batch job scheduling
/// - Project Management: Task allocation across team members
/// - Supply Chain: Order fulfillment with resource constraints
///
/// EXAMPLE USAGE:
///   open FSharp.Azure.Quantum.TaskScheduling
///   
///   let taskA = scheduledTask {
///       taskId "TaskA"
///       duration (hours 2.0)
///   }
///   
///   let taskB = scheduledTask {
///       taskId "TaskB"
///       duration (minutes 30.0)
///       after "TaskA"  // Dependency co-located!
///       deadline 180.0
///   }
///   
///   let problem = scheduling {
///       tasks [taskA; taskB]
///       objective MinimizeMakespan
///   }
///   
///   let! result = solve problem

// ============================================================================
// RE-EXPORT TYPES AND FUNCTIONS - Make everything available at FSharp.Azure.Quantum level
[<AutoOpen>]
module TaskSchedulingTypes =
    
    // Open Types module so all types and union cases are available
    open FSharp.Azure.Quantum.TaskScheduling.Types

    // Re-export builder functions
    let scheduledTask<'T> = FSharp.Azure.Quantum.TaskScheduling.Builders.scheduledTask<'T>
    let resource<'T> = FSharp.Azure.Quantum.TaskScheduling.Builders.resource<'T>
    let crew = FSharp.Azure.Quantum.TaskScheduling.Builders.crew
    let scheduling<'TTask, 'TResource> = FSharp.Azure.Quantum.TaskScheduling.Builders.scheduling<'TTask, 'TResource>
    
    // Re-export time helper functions (redundant but explicit)
    let minutes = minutes
    let hours = hours
    let days = days

    // Public API functions
    
    /// Solve scheduling problem and return optimized schedule (classical dependency-only)
    /// 
    /// Note: This solver handles dependencies but ignores resource capacity constraints.
    /// For resource-constrained scheduling, use solveQuantum with IQuantumBackend.
    let solve (problem: SchedulingProblem<'TTask, 'TResource>) : Async<QuantumResult<Solution>> =
        async {
            return FSharp.Azure.Quantum.TaskScheduling.ClassicalSolver.solve problem
        }

    /// Solve scheduling problem with resource constraints using quantum backend
    /// 
    /// RULE 1 COMPLIANCE:
    /// ✅ Requires IQuantumBackend parameter (explicit quantum execution)
    /// 
    /// Resource-constrained scheduling is solved via quantum optimization:
    /// 1. Encodes tasks, dependencies, and resource limits as QUBO problem
    /// 2. Uses QAOA or quantum annealing to find optimal schedule
    /// 3. Respects resource capacity constraints (unlike classical solver)
    /// 
    /// Use this when:
    /// - Tasks have resource requirements (workers, machines, budget)
    /// - Resources have limited capacity
    /// - Need optimal allocation under constraints
    /// 
    /// Example:
    ///   let backend = BackendAbstraction.createLocalBackend()
    ///   let! result = solveQuantum backend problem
    let solveQuantum 
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: SchedulingProblem<'TTask, 'TResource>) 
        : Async<QuantumResult<Solution>> =
        FSharp.Azure.Quantum.TaskScheduling.QuantumSolver.solveAsync backend problem
    
    /// Export schedule as Gantt chart to text file
    let exportGanttChart (solution: Solution) (filePath: string) : unit =
        FSharp.Azure.Quantum.TaskScheduling.Export.exportGanttChart solution filePath

// ============================================================================
// C# INTEROP - Types for easier C# consumption
// ============================================================================

/// Additional types and functions for C# compatibility (FluentAPI)
module Scheduling =

    /// Create a simple task (C# helper)
    let task (id: string) (value: 'T) (duration: float) : ScheduledTask<'T> =
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

    /// Create a task with resource requirements (C# helper)
    let taskWithRequirements (id: string) (value: 'T) (duration: float) (requirements: (string * float) list) : ScheduledTask<'T> =
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

    /// SchedulingBuilder for C# FluentAPI
    type SchedulingBuilder<'TTask, 'TResource> private (problem: SchedulingProblem<'TTask, 'TResource>) =
        static member Create() =
            SchedulingBuilder({
                Tasks = []
                Resources = []
                Dependencies = []
                Objective = MinimizeMakespan
                TimeHorizon = 1000.0
            })

        member _.Tasks(tasks: ScheduledTask<'TTask> list) =
            SchedulingBuilder({ problem with Tasks = tasks })

        member _.Resources(resources: Resource<'TResource> list) =
            SchedulingBuilder({ problem with Resources = resources })

        member _.AddDependency(dependency: Dependency) =
            SchedulingBuilder({ problem with Dependencies = dependency :: problem.Dependencies })

        member _.Objective(objective: Objective) =
            SchedulingBuilder({ problem with Objective = objective })

        member _.TimeHorizon(horizon: float) =
            SchedulingBuilder({ problem with TimeHorizon = horizon })

        member _.Build() = problem

    /// Scheduling objective enum for C#
    type SchedulingObjective =
        static member MinimizeMakespan = MinimizeMakespan
        static member MinimizeCost = MinimizeCost
        static member MaximizeResourceUtilization = MaximizeResourceUtilization
        static member MinimizeLateness = MinimizeLateness

    /// Solve scheduling problem (synchronous for C#)
    let solveClassical (problem: SchedulingProblem<'TTask, 'TResource>) : QuantumResult<Solution> =
        solve problem |> Async.RunSynchronously
