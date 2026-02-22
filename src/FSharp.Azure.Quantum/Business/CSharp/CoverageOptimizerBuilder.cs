using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using static FSharp.Azure.Quantum.Business.CoverageOptimizer;
using static FSharp.Azure.Quantum.Core.BackendAbstraction;

namespace FSharp.Azure.Quantum.Business.CSharp
{
    /// <summary>
    /// C# Fluent API for Coverage Optimization (Quantum Set Cover).
    ///
    /// Finds minimum-cost subsets that cover all required elements using quantum QAOA optimization.
    /// Use this when you need to select the cheapest combination of options that satisfies all requirements.
    ///
    /// Example:
    /// <code>
    /// var result = new CoverageOptimizerBuilder()
    ///     .SetUniverseSize(3)
    ///     .AddOption("MorningShift", new[] { 0, 1 }, 25.0)
    ///     .AddOption("AfternoonShift", new[] { 1, 2 }, 20.0)
    ///     .AddOption("FullDay", new[] { 0, 1, 2 }, 40.0)
    ///     .WithBackend(backend)
    ///     .Build();
    ///
    /// Console.WriteLine($"Total cost: ${result.TotalCost}");
    /// foreach (var opt in result.SelectedOptions)
    ///     Console.WriteLine($"  Selected: {opt.Id} (cost ${opt.Cost})");
    /// </code>
    /// </summary>
    public class CoverageOptimizerBuilder
    {
        private int _universeSize;
        private readonly List<(string Id, int[] Elements, double Cost)> _options = new();
        private IQuantumBackend? _backend;
        private int _shots = 1000;

        /// <summary>
        /// Sets the total number of elements that need to be covered.
        /// </summary>
        /// <param name="size">Total number of elements in the universe (0-indexed).</param>
        /// <returns>The builder instance for chaining.</returns>
        public CoverageOptimizerBuilder SetUniverseSize(int size)
        {
            _universeSize = size;
            return this;
        }

        /// <summary>
        /// Adds an element to the universe (expands UniverseSize if needed).
        /// </summary>
        /// <param name="elementIndex">0-based index of the element.</param>
        /// <returns>The builder instance for chaining.</returns>
        public CoverageOptimizerBuilder AddElement(int elementIndex)
        {
            if (elementIndex + 1 > _universeSize)
                _universeSize = elementIndex + 1;
            return this;
        }

        /// <summary>
        /// Adds a coverage option (shift, facility, service package, etc.).
        /// </summary>
        /// <param name="id">Unique identifier for this option.</param>
        /// <param name="coveredElements">Array of element indices this option covers (0-based).</param>
        /// <param name="cost">Cost of selecting this option.</param>
        /// <returns>The builder instance for chaining.</returns>
        public CoverageOptimizerBuilder AddOption(string id, int[] coveredElements, double cost)
        {
            ArgumentNullException.ThrowIfNull(id);
            ArgumentNullException.ThrowIfNull(coveredElements);
            _options.Add((id, coveredElements, cost));
            return this;
        }

        /// <summary>
        /// Sets the quantum backend used for execution.
        /// Required for all coverage optimization.
        /// </summary>
        /// <param name="backend">Backend implementation to use.</param>
        /// <returns>The builder instance for chaining.</returns>
        public CoverageOptimizerBuilder WithBackend(IQuantumBackend backend)
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
        public CoverageOptimizerBuilder WithShots(int shots)
        {
            _shots = shots;
            return this;
        }

        /// <summary>
        /// Builds and executes the coverage optimization.
        /// Returns a C#-native result with no F# types exposed.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if optimization fails or validation errors occur.</exception>
        /// <returns>A <see cref="CoverageOptimizationResult"/> with the optimal coverage solution.</returns>
        public CoverageOptimizationResult Build()
        {
            // Convert C# types to F# types internally
            var fsharpOptions = _options.Select(o =>
                new CoverageOption(o.Id, ListModule.OfSeq(o.Elements), o.Cost)).ToList();

            var problem = new CoverageProblem(
                _universeSize,
                ListModule.OfSeq(fsharpOptions),
                _backend != null ? FSharpOption<IQuantumBackend>.Some(_backend) : FSharpOption<IQuantumBackend>.None,
                _shots);

            var result = CoverageOptimizer.solve(problem);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Coverage optimization failed: {result.ErrorValue.Message}");
            }

            return CoverageResultWrapper.Convert(result.ResultValue);
        }
    }

    // ========================================================================
    // C#-NATIVE RESULT TYPES (no F# types exposed)
    // ========================================================================

    /// <summary>
    /// Result of coverage optimization (no F# types exposed).
    /// Contains the selected options, total cost, and coverage statistics.
    /// </summary>
    public class CoverageOptimizationResult
    {
        /// <summary>Gets the selected coverage options.</summary>
        public required SelectedCoverageOption[] SelectedOptions { get; init; }

        /// <summary>Gets the total cost of all selected options.</summary>
        public double TotalCost { get; init; }

        /// <summary>Gets the number of elements covered by the selected options.</summary>
        public int ElementsCovered { get; init; }

        /// <summary>Gets the total number of elements that needed coverage.</summary>
        public int TotalElements { get; init; }

        /// <summary>Gets a value indicating whether all elements are covered.</summary>
        public bool IsComplete { get; init; }

        /// <summary>Gets a human-readable execution message.</summary>
        public required string Message { get; init; }
    }

    /// <summary>
    /// A selected coverage option in the solution (no F# types exposed).
    /// </summary>
    public class SelectedCoverageOption
    {
        /// <summary>Gets the unique identifier of the option.</summary>
        public required string Id { get; init; }

        /// <summary>Gets the element indices covered by this option.</summary>
        public required int[] CoveredElements { get; init; }

        /// <summary>Gets the cost of this option.</summary>
        public double Cost { get; init; }
    }

    // ========================================================================
    // INTERNAL WRAPPER - Converts F# types to C# types
    // ========================================================================
    internal static class CoverageResultWrapper
    {
        public static CoverageOptimizationResult Convert(CoverageResult fsharpResult)
        {
            var options = fsharpResult.SelectedOptions
                .Select(o => new SelectedCoverageOption
                {
                    Id = o.Id,
                    CoveredElements = o.CoveredElements.ToArray(),
                    Cost = o.Cost,
                })
                .ToArray();

            return new CoverageOptimizationResult
            {
                SelectedOptions = options,
                TotalCost = fsharpResult.TotalCost,
                ElementsCovered = fsharpResult.ElementsCovered,
                TotalElements = fsharpResult.TotalElements,
                IsComplete = fsharpResult.IsComplete,
                Message = fsharpResult.Message,
            };
        }
    }
}
