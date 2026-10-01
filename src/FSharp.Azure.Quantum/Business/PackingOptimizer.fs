namespace FSharp.Azure.Quantum.Business

open System
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Quantum

/// <summary>
/// High-level packing/placement optimization using quantum bin packing.
/// </summary>
/// <remarks>
/// **Business Use Cases:**
/// - Container Loading: Pack cargo into containers minimizing container count
/// - VM Placement: Place virtual machines onto servers respecting memory/CPU limits
/// - Warehouse: Assign products to storage bins by size constraints
/// - Cloud Computing: Schedule batch jobs into time windows with capacity limits
/// - Manufacturing: Cut stock into pieces minimizing waste
///
/// **Quantum Advantage:**
/// Uses QAOA-based bin packing optimization to assign items to bins
/// minimizing the total number of bins used, with approximate optimization
/// via QUBO formulation.
///
/// **Example:**
/// ```fsharp
/// // The builder returns a Task<QuantumResult<PackingResult>>: await it inside task { }
/// let! result = packingOptimizer {
///     containerCapacity 100.0
///
///     item "Crate-A" 45.0
///     item "Crate-B" 35.0
///     item "Crate-C" 25.0
///     item "Crate-D" 50.0
///
///     backend myBackend
/// }
/// ```
/// </remarks>
module PackingOptimizer =

    // ========================================================================
    // TYPES
    // ========================================================================

    /// An item to be packed
    type PackingItem =
        {
            /// Unique identifier
            Id: string
            /// Size/weight of the item
            Size: float
        }

    /// Bin/container assignment
    type BinAssignment =
        {
            /// Item that was assigned
            Item: PackingItem
            /// Bin index (0-based)
            BinIndex: int
        }

    /// Packing optimization problem
    type PackingProblem =
        {
            /// Items to pack
            Items: PackingItem list
            /// Capacity of each bin/container (all bins have same capacity)
            BinCapacity: float
            /// Quantum backend (None = error, Some = quantum optimization)
            Backend: IQuantumBackend option
            /// Number of measurement shots (default: 1000)
            Shots: int
        }

    /// Packing solution
    type PackingResult =
        {
            /// Item-to-bin assignments
            Assignments: BinAssignment list
            /// Number of bins used
            BinsUsed: int
            /// Whether all items are assigned and no bin exceeds capacity
            IsValid: bool
            /// Total items
            TotalItems: int
            /// Items successfully assigned
            ItemsAssigned: int
            /// Execution message
            Message: string
        }

    // ========================================================================
    // CONVERSION & SOLVING
    // ========================================================================

    /// Convert PackingProblem to QuantumBinPackingSolver.Problem
    let private toBinPackingProblem (problem: PackingProblem) : QuantumBinPackingSolver.Problem =
        let items =
            problem.Items
            |> List.map (fun pi -> ({ Id = pi.Id; Size = pi.Size }: QuantumBinPackingSolver.Item))

        {
            Items = items
            BinCapacity = problem.BinCapacity
        }

    /// Decode a QuantumBinPackingSolver.Solution to PackingResult
    let private decodeSolution (problem: PackingProblem) (solution: QuantumBinPackingSolver.Solution) : PackingResult =
        let assignments =
            solution.Assignments
            |> List.choose (fun (item, binIdx) ->
                problem.Items
                |> List.tryFind (fun pi -> pi.Id = item.Id)
                |> Option.map (fun pi -> { Item = pi; BinIndex = binIdx }))

        {
            Assignments = assignments
            BinsUsed = solution.BinsUsed
            IsValid = solution.IsValid
            TotalItems = problem.Items.Length
            ItemsAssigned = assignments.Length
            Message =
                if solution.IsValid then
                    $"Packed {assignments.Length} items into {solution.BinsUsed} bins"
                else
                    $"Partial packing: {assignments.Length}/{problem.Items.Length} items assigned to {solution.BinsUsed} bins"
        }

    /// Execute packing optimization without blocking the calling thread
    let solveAsync
        (problem: PackingProblem)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<PackingResult>> =
        quantumResultTask {
            if problem.Items.IsEmpty then
                return! Error(QuantumError.ValidationError("Items", "must have at least one item"))
            elif problem.BinCapacity <= 0.0 then
                return! Error(QuantumError.ValidationError("BinCapacity", "bin capacity must be positive"))
            elif problem.Items |> List.exists (fun i -> i.Size <= 0.0) then
                return! Error(QuantumError.ValidationError("ItemSize", "all item sizes must be positive"))
            elif problem.Items |> List.exists (fun i -> i.Size > problem.BinCapacity) then
                return! Error(QuantumError.ValidationError("ItemSize", "item size exceeds bin capacity"))
            else
                // Quantum-first: run on the caller's backend, or default to the local simulator
                // (a real quantum backend) when none was supplied.
                let backend =
                    problem.Backend
                    |> Option.defaultWith (fun () ->
                        FSharp.Azure.Quantum.Backends.LocalBackend.LocalBackend() :> IQuantumBackend)

                let binProblem = toBinPackingProblem problem

                let! solution =
                    QuantumBinPackingSolver.solveWithConfigAsync
                        backend
                        binProblem
                        { QuantumBinPackingSolver.defaultConfig with
                            FinalShots = problem.Shots
                        }
                        cancellationToken

                return decodeSolution problem solution
        }

    // ========================================================================
    // COMPUTATION EXPRESSION BUILDER
    // ========================================================================

    /// Fluent builder for packing optimization.
    type PackingOptimizerBuilder() =

        let defaultProblem =
            {
                Items = []
                BinCapacity = 0.0
                Backend = None
                Shots = 1000
            }

        member _.Yield(_) = defaultProblem
        member _.Delay(f: unit -> PackingProblem) = f

        /// Execute the optimization. The result is a task, so F# callers write
        /// `let! result = packingOptimizer { ... }` inside `task { }`.
        member _.Run(f: unit -> PackingProblem) : Task<QuantumResult<PackingResult>> =
            let problem = f ()
            solveAsync problem CancellationToken.None

        member _.Combine(p1: PackingProblem, p2: PackingProblem) = p2
        member _.Zero() = defaultProblem

        /// <summary>Add an item to pack.</summary>
        /// <param name="id">Unique identifier</param>
        /// <param name="size">Size/weight of the item</param>
        [<CustomOperation("item")>]
        member _.Item(problem: PackingProblem, id: string, size: float) : PackingProblem =
            let item = { Id = id; Size = size }

            { problem with
                Items = item :: problem.Items
            }

        /// <summary>Set the bin/container capacity.</summary>
        /// <param name="capacity">Maximum capacity per bin</param>
        [<CustomOperation("containerCapacity")>]
        member _.ContainerCapacity(problem: PackingProblem, capacity: float) : PackingProblem =
            { problem with BinCapacity = capacity }

        /// <summary>Set the quantum backend.</summary>
        [<CustomOperation("backend")>]
        member _.Backend(problem: PackingProblem, backend: IQuantumBackend) : PackingProblem =
            { problem with Backend = Some backend }

        /// <summary>Set the number of measurement shots.</summary>
        [<CustomOperation("shots")>]
        member _.Shots(problem: PackingProblem, shots: int) : PackingProblem = { problem with Shots = shots }

    /// Create a packing optimizer builder.
    let packingOptimizer = PackingOptimizerBuilder()
