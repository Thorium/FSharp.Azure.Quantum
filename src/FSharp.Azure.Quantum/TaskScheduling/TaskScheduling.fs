namespace FSharp.Azure.Quantum

open System
open System.Threading
open System.Threading.Tasks
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
///   let! result = solveQuantumAsync backend problem CancellationToken.None

// ============================================================================
// RE-EXPORT TYPES AND FUNCTIONS - Make everything available at FSharp.Azure.Quantum level
[<AutoOpen>]
module TaskSchedulingTypes =

    // Open Types module so all types and union cases are available
    open FSharp.Azure.Quantum.TaskScheduling.Types

    // Re-export builder functions
    let scheduledTask<'T> = TaskScheduling.Builders.scheduledTask<'T>

    let resource<'T> = TaskScheduling.Builders.resource<'T>
    let crew = TaskScheduling.Builders.crew

    let scheduling<'TTask, 'TResource> =
        TaskScheduling.Builders.scheduling<'TTask, 'TResource>

    // Re-export time helper functions (redundant but explicit)
    let minutes = minutes
    let hours = hours
    let days = days

    // Public API functions

    /// Solve scheduling problem and return optimized schedule (classical dependency-only)
    ///
    /// Note: This solver handles dependencies but ignores resource capacity constraints.
    /// For resource-constrained scheduling, use solveQuantumAsync with IQuantumBackend.
    /// Internal: classical dependency-only solver. Not part of the public quantum-first API.
    /// Public callers must use solveQuantumAsync with an IQuantumBackend (local simulator or cloud).
    let internal solveAsync
        (problem: SchedulingProblem<'TTask, 'TResource>)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Solution>> =
        task {
            cancellationToken.ThrowIfCancellationRequested()
            return TaskScheduling.ClassicalSolver.solve problem
        }

    /// Solve scheduling problem with resource constraints using quantum backend
    ///
    /// RULE 1 COMPLIANCE:
    /// ✅ Requires IQuantumBackend parameter (explicit quantum execution)
    ///
    /// Resource-constrained scheduling is solved via quantum optimization:
    /// 1. Encodes tasks, dependencies, and resource limits as QUBO problem
    /// 2. Runs QAOA with the shared default configuration (2 layers, optimised angles,
    ///    1000 final shots) and returns the best sample that is a feasible schedule
    /// 3. Respects resource capacity constraints (unlike classical solver)
    ///
    /// See TaskScheduling.QuantumSolver.solveWithConfigAsync for the decoding rules and
    /// for Solution.WasRepaired and Solution.Sampling.
    ///
    /// Use this when:
    /// - Tasks have resource requirements (workers, machines, budget)
    /// - Resources have limited capacity
    /// - Need optimal allocation under constraints
    ///
    /// Example:
    ///   let backend = LocalBackend() :> IQuantumBackend
    ///   let! result = solveQuantumAsync backend problem CancellationToken.None
    let solveQuantumAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: SchedulingProblem<'TTask, 'TResource>)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Solution>> =
        TaskScheduling.QuantumSolver.solveAsync backend problem cancellationToken

    /// solveQuantumAsync with an explicit QAOA configuration (layers, angle optimisation,
    /// shots, and whether the one-hot repair decode may be used when no sample is valid).
    ///
    /// Example:
    ///   let config = { QaoaExecutionHelpers.defaultConfig with NumLayers = 3; FinalShots = 2000 }
    ///   let! result = solveQuantumWithConfigAsync backend problem config CancellationToken.None
    let solveQuantumWithConfigAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: SchedulingProblem<'TTask, 'TResource>)
        (config: QaoaExecutionHelpers.QaoaSolverConfig)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Solution>> =
        TaskScheduling.QuantumSolver.solveWithConfigAsync backend problem config cancellationToken

    /// Export schedule as Gantt chart to text file
    let exportGanttChart (solution: Solution) (filePath: string) : unit =
        TaskScheduling.Export.exportGanttChart solution filePath

// ============================================================================
// C# INTEROP - Types for easier C# consumption
// ============================================================================

/// Additional types and functions for C# compatibility (FluentAPI)
module Scheduling =

    /// Create a simple task (C# helper)
    let task (id: string) (value: 'T) (duration: TimeSpan) : ScheduledTask<'T> =
        {
            Id = id
            Value = Some value
            Duration = duration
            EarliestStart = None
            Deadline = None
            ResourceRequirements = Map.empty
            Priority = 0.0
            Properties = Map.empty
        }

    /// Create a task with resource requirements (C# helper)
    let taskWithRequirements
        (id: string)
        (value: 'T)
        (duration: TimeSpan)
        (requirements: (string * float) list)
        : ScheduledTask<'T> =
        {
            Id = id
            Value = Some value
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
            SchedulingBuilder(
                {
                    Tasks = []
                    Resources = []
                    Dependencies = []
                    Objective = MinimizeMakespan
                    TimeHorizon = TimeSpan.FromMinutes 1000.0
                }
            )

        member _.Tasks(tasks: ScheduledTask<'TTask> list) =
            SchedulingBuilder({ problem with Tasks = tasks })

        member _.Resources(resources: Resource<'TResource> list) =
            SchedulingBuilder({ problem with Resources = resources })

        member _.AddDependency(dependency: Dependency) =
            SchedulingBuilder(
                { problem with
                    Dependencies = dependency :: problem.Dependencies
                }
            )

        member _.Objective(objective: Objective) =
            SchedulingBuilder({ problem with Objective = objective })

        member _.TimeHorizon(horizon: TimeSpan) =
            SchedulingBuilder({ problem with TimeHorizon = horizon })

        member _.Build() = problem

    /// Scheduling objective enum for C#
    type SchedulingObjective =
        static member MinimizeMakespan = MinimizeMakespan
        static member MinimizeCost = MinimizeCost
        static member MaximizeResourceUtilization = MaximizeResourceUtilization
        static member MinimizeLateness = MinimizeLateness
