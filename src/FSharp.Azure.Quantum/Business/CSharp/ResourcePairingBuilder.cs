using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using static FSharp.Azure.Quantum.Business.ResourcePairing;
using static FSharp.Azure.Quantum.Core.BackendAbstraction;

namespace FSharp.Azure.Quantum.Business.CSharp
{
    /// <summary>
    /// C# Fluent API for Resource Pairing Optimization (Quantum Maximum Weight Matching).
    ///
    /// Finds optimal 1:1 pairings between participants that maximize total compatibility.
    /// Use this when you need to match people, resources, or entities in pairs.
    ///
    /// Example:
    /// <code>
    /// var result = new ResourcePairingBuilder()
    ///     .AddParticipant("Alice")
    ///     .AddParticipant("Bob")
    ///     .AddParticipant("Carol")
    ///     .AddCompatibility("Alice", "Bob", 0.9)
    ///     .AddCompatibility("Alice", "Carol", 0.5)
    ///     .AddCompatibility("Bob", "Carol", 0.7)
    ///     .WithBackend(backend)
    ///     .Build();
    ///
    /// Console.WriteLine($"Total score: {result.TotalScore}");
    /// foreach (var pair in result.Pairings)
    ///     Console.WriteLine($"  {pair.Participant1} &lt;-&gt; {pair.Participant2} (score {pair.Score})");
    /// </code>
    /// </summary>
    public class ResourcePairingBuilder
    {
        private readonly List<string> _participants = new();
        private readonly List<(string P1, string P2, double Weight)> _compatibilities = new();
        private IQuantumBackend? _backend;
        private int _shots = 1000;

        /// <summary>
        /// Adds a participant to the pairing problem.
        /// </summary>
        /// <param name="participantId">Unique identifier for the participant.</param>
        /// <returns>The builder instance for chaining.</returns>
        public ResourcePairingBuilder AddParticipant(string participantId)
        {
            ArgumentNullException.ThrowIfNull(participantId);
            _participants.Add(participantId);
            return this;
        }

        /// <summary>
        /// Adds multiple participants at once.
        /// </summary>
        /// <param name="participants">Array of participant identifiers.</param>
        /// <returns>The builder instance for chaining.</returns>
        public ResourcePairingBuilder AddParticipants(params string[] participants)
        {
            ArgumentNullException.ThrowIfNull(participants);
            _participants.AddRange(participants);
            return this;
        }

        /// <summary>
        /// Adds a compatibility score between two participants.
        /// </summary>
        /// <param name="participant1">First participant.</param>
        /// <param name="participant2">Second participant.</param>
        /// <param name="weight">Compatibility score (higher = better match).</param>
        /// <returns>The builder instance for chaining.</returns>
        public ResourcePairingBuilder AddCompatibility(string participant1, string participant2, double weight)
        {
            ArgumentNullException.ThrowIfNull(participant1);
            ArgumentNullException.ThrowIfNull(participant2);
            _compatibilities.Add((participant1, participant2, weight));
            return this;
        }

        /// <summary>
        /// Sets the quantum backend used for execution.
        /// Required for all pairing optimization.
        /// </summary>
        /// <param name="backend">Backend implementation to use.</param>
        /// <returns>The builder instance for chaining.</returns>
        public ResourcePairingBuilder WithBackend(IQuantumBackend backend)
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
        public ResourcePairingBuilder WithShots(int shots)
        {
            _shots = shots;
            return this;
        }

        /// <summary>
        /// Builds and executes the pairing optimization.
        /// Returns a C#-native result with no F# types exposed.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if optimization fails or validation errors occur.</exception>
        /// <returns>A <see cref="PairingOptimizationResult"/> with the optimal pairings.</returns>
        public PairingOptimizationResult Build()
        {
            // Convert C# types to F# types internally
            var fsharpCompats = _compatibilities.Select(c =>
                new Compatibility(c.P1, c.P2, c.Weight)).ToList();

            var problem = new PairingProblem(
                ListModule.OfSeq(_participants),
                ListModule.OfSeq(fsharpCompats),
                _backend != null ? FSharpOption<IQuantumBackend>.Some(_backend) : FSharpOption<IQuantumBackend>.None,
                _shots);

            var result = ResourcePairing.solve(problem);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Pairing optimization failed: {result.ErrorValue.Message}");
            }

            return PairingResultWrapper.Convert(result.ResultValue);
        }
    }

    // ========================================================================
    // C#-NATIVE RESULT TYPES (no F# types exposed)
    // ========================================================================

    /// <summary>
    /// Result of pairing optimization (no F# types exposed).
    /// Contains the optimal pairings, total score, and statistics.
    /// </summary>
    public class PairingOptimizationResult
    {
        /// <summary>Gets the optimal pairings found.</summary>
        public required OptimalPairing[] Pairings { get; init; }

        /// <summary>Gets the total compatibility score of all pairings.</summary>
        public double TotalScore { get; init; }

        /// <summary>Gets the number of participants that were paired.</summary>
        public int ParticipantsPaired { get; init; }

        /// <summary>Gets the total number of participants.</summary>
        public int TotalParticipants { get; init; }

        /// <summary>Gets a value indicating whether the matching is valid (no participant in multiple pairs).</summary>
        public bool IsValid { get; init; }

        /// <summary>Gets a human-readable execution message.</summary>
        public required string Message { get; init; }
    }

    /// <summary>
    /// A single pairing in the solution (no F# types exposed).
    /// </summary>
    public class OptimalPairing
    {
        /// <summary>Gets the first participant in the pair.</summary>
        public required string Participant1 { get; init; }

        /// <summary>Gets the second participant in the pair.</summary>
        public required string Participant2 { get; init; }

        /// <summary>Gets the compatibility score for this pairing.</summary>
        public double Score { get; init; }
    }

    // ========================================================================
    // INTERNAL WRAPPER - Converts F# types to C# types
    // ========================================================================
    internal static class PairingResultWrapper
    {
        public static PairingOptimizationResult Convert(PairingResult fsharpResult)
        {
            var pairings = fsharpResult.Pairings
                .Select(p => new OptimalPairing
                {
                    Participant1 = p.Participant1,
                    Participant2 = p.Participant2,
                    Score = p.Weight,
                })
                .ToArray();

            return new PairingOptimizationResult
            {
                Pairings = pairings,
                TotalScore = fsharpResult.TotalScore,
                ParticipantsPaired = fsharpResult.ParticipantsPaired,
                TotalParticipants = fsharpResult.TotalParticipants,
                IsValid = fsharpResult.IsValid,
                Message = fsharpResult.Message,
            };
        }
    }
}
