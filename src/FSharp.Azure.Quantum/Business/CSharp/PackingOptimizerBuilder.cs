using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using static FSharp.Azure.Quantum.Business.PackingOptimizer;
using static FSharp.Azure.Quantum.Core.BackendAbstraction;

namespace FSharp.Azure.Quantum.Business.CSharp
{
    /// <summary>
    /// C# Fluent API for Packing Optimization (Quantum Bin Packing).
    ///
    /// Assigns items to bins minimizing the total number of bins used via quantum QAOA optimization.
    /// Use this when you need to pack items into fixed-capacity containers.
    ///
    /// Example:
    /// <code>
    /// var result = new PackingOptimizerBuilder()
    ///     .SetBinCapacity(100.0)
    ///     .AddItem("Crate-A", 45.0)
    ///     .AddItem("Crate-B", 35.0)
    ///     .AddItem("Crate-C", 25.0)
    ///     .AddItem("Crate-D", 50.0)
    ///     .WithBackend(backend)
    ///     .Build();
    ///
    /// Console.WriteLine($"Bins used: {result.BinsUsed}");
    /// foreach (var a in result.Assignments)
    ///     Console.WriteLine($"  {a.ItemId} (size {a.ItemSize}) -> Bin {a.BinIndex}");
    /// </code>
    /// </summary>
    public class PackingOptimizerBuilder
    {
        private readonly List<(string Id, double Size)> _items = new();
        private double _binCapacity;
        private IQuantumBackend? _backend;
        private int _shots = 1000;

        /// <summary>
        /// Adds an item to be packed.
        /// </summary>
        /// <param name="id">Unique identifier for the item.</param>
        /// <param name="size">Size/weight of the item.</param>
        /// <returns>The builder instance for chaining.</returns>
        public PackingOptimizerBuilder AddItem(string id, double size)
        {
            ArgumentNullException.ThrowIfNull(id);
            _items.Add((id, size));
            return this;
        }

        /// <summary>
        /// Sets the capacity of each bin/container (all bins have the same capacity).
        /// </summary>
        /// <param name="capacity">Maximum capacity per bin.</param>
        /// <returns>The builder instance for chaining.</returns>
        public PackingOptimizerBuilder SetBinCapacity(double capacity)
        {
            _binCapacity = capacity;
            return this;
        }

        /// <summary>
        /// Sets the quantum backend used for execution.
        /// Required for all packing optimization.
        /// </summary>
        /// <param name="backend">Backend implementation to use.</param>
        /// <returns>The builder instance for chaining.</returns>
        public PackingOptimizerBuilder WithBackend(IQuantumBackend backend)
        {
            ArgumentNullException.ThrowIfNull(backend);
            _backend = backend;
            return this;
        }

        /// <summary>
        /// Sets the number of measurement shots for the quantum circuit.
        /// Default: 1000.
        /// </summary>
        /// <param name="shots">Number of shots.</param>
        /// <returns>The builder instance for chaining.</returns>
        public PackingOptimizerBuilder WithShots(int shots)
        {
            _shots = shots;
            return this;
        }

        /// <summary>
        /// Builds and executes the packing optimization.
        /// Returns a C#-native result with no F# types exposed.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if optimization fails or validation errors occur.</exception>
        /// <returns>A <see cref="PackingOptimizationResult"/> with the optimal bin assignments.</returns>
        public PackingOptimizationResult Build()
        {
            // Convert C# types to F# types internally
            var fsharpItems = _items.Select(i =>
                new PackingItem(i.Id, i.Size)).ToList();

            var problem = new PackingProblem(
                ListModule.OfSeq(fsharpItems),
                _binCapacity,
                _backend != null ? FSharpOption<IQuantumBackend>.Some(_backend) : FSharpOption<IQuantumBackend>.None,
                _shots);

            var result = PackingOptimizer.solve(problem);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Packing optimization failed: {result.ErrorValue.Message}");
            }

            return PackingResultWrapper.Convert(result.ResultValue);
        }
    }

    // ========================================================================
    // C#-NATIVE RESULT TYPES (no F# types exposed)
    // ========================================================================

    /// <summary>
    /// Result of packing optimization (no F# types exposed).
    /// Contains the item-to-bin assignments, bin count, and statistics.
    /// </summary>
    public class PackingOptimizationResult
    {
        /// <summary>Gets the item-to-bin assignments.</summary>
        public required BinAssignmentResult[] Assignments { get; init; }

        /// <summary>Gets the total number of bins used.</summary>
        public int BinsUsed { get; init; }

        /// <summary>Gets a value indicating whether all items are assigned and no bin exceeds capacity.</summary>
        public bool IsValid { get; init; }

        /// <summary>Gets the total number of items.</summary>
        public int TotalItems { get; init; }

        /// <summary>Gets the number of items successfully assigned to bins.</summary>
        public int ItemsAssigned { get; init; }

        /// <summary>Gets a human-readable execution message.</summary>
        public required string Message { get; init; }
    }

    /// <summary>
    /// A single item-to-bin assignment in the solution (no F# types exposed).
    /// </summary>
    public class BinAssignmentResult
    {
        /// <summary>Gets the unique identifier of the item.</summary>
        public required string ItemId { get; init; }

        /// <summary>Gets the size/weight of the item.</summary>
        public double ItemSize { get; init; }

        /// <summary>Gets the bin index this item was assigned to (0-based).</summary>
        public int BinIndex { get; init; }
    }

    // ========================================================================
    // INTERNAL WRAPPER - Converts F# types to C# types
    // ========================================================================
    internal static class PackingResultWrapper
    {
        public static PackingOptimizationResult Convert(PackingResult fsharpResult)
        {
            var assignments = fsharpResult.Assignments
                .Select(a => new BinAssignmentResult
                {
                    ItemId = a.Item.Id,
                    ItemSize = a.Item.Size,
                    BinIndex = a.BinIndex,
                })
                .ToArray();

            return new PackingOptimizationResult
            {
                Assignments = assignments,
                BinsUsed = fsharpResult.BinsUsed,
                IsValid = fsharpResult.IsValid,
                TotalItems = fsharpResult.TotalItems,
                ItemsAssigned = fsharpResult.ItemsAssigned,
                Message = fsharpResult.Message,
            };
        }
    }
}
