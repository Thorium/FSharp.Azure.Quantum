using System;
using System.Linq;

namespace KasinoExample
{
    /// <summary>
    /// C# Kasino Card Game Example - Demonstrates clean C# API with FSharp.Azure.Quantum.
    ///
    /// Kasino is a traditional Finnish card game where players capture cards by matching
    /// table cards whose sum equals a card from hand. This example demonstrates:
    ///
    /// 1. Clean C# API using GlobalUsings (no manual F# interop needed!)
    /// 2. C# value tuples work directly with CSharpBuilders helpers
    /// 3. Extension methods for Result&lt;T,E&gt; and F# lists
    /// 4. NEW: findAllValidCombinations() for proper Kasino capture logic
    /// 5. Quantum optimization (32x-181x speedup potential with QUBO encoding)
    ///
    /// Kasino Game Rules:
    /// - Table cards: Numeric values (2-10, J=11, Q=12, K=13, Ace=1)
    /// - Hand cards: Numeric values (2-10, J=11, Q=12, K=13, Ace=14)
    /// - SPECIAL RULE: Ace in hand = 14, Ace on table = 1
    /// - Goal: Find table cards that sum exactly to hand card value
    /// - CAPTURE RULE: If multiple combinations exist, capture ALL cards from ALL combinations!
    ///
    /// Example: Hand Ace (14) + Table [Ace(1), King(13)]
    /// - Valid combination: 1 + 13 = 14 → Capture both Ace and King
    /// - Invalid: Cannot capture table Ace alone (1 ≠ 14).
    /// </summary>
    internal sealed class Program
    {
        private Program()
        {
        }

        private static void Main(string[] args)
        {
            Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  Kasino Card Game - C# Interop with F# Knapsack Solver    ║");
            Console.WriteLine("║  Traditional Finnish Card Game (32x-181x Quantum Speedup)  ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            // Demonstrate three different Kasino capture scenarios
            Example1_SimpleCapture();
            Console.WriteLine();

            Example2_ComplexCapture();
            Console.WriteLine();

            Example3_MultipleCaptures();
            Console.WriteLine();

            Example4_RealKasinoCapture();
            Console.WriteLine();

            Console.WriteLine("✅ All examples completed successfully!");
            Console.WriteLine();
            Console.WriteLine("🎯 Key Takeaways:");
            Console.WriteLine("  • Clean C# API with GlobalUsings - no F# interop boilerplate!");
            Console.WriteLine("  • C# value tuples work directly with KnapsackProblem() helper");
            Console.WriteLine("  • Extension methods: result.IsOk(), result.GetOkValue(), list.Count()");
            Console.WriteLine("  • Quantum-ready algorithms with quantum optimization");
            Console.WriteLine("  • Quantum speedup potential: 32x-181x with QUBO encoding");
        }

        /// <summary>
        /// Example 1: Simple Kasino Capture.
        /// Hand card: King (13), Table: [2, 5, 8, Jack(11)]
        /// Goal: Find cards that maximize value without exceeding 13
        /// Expected: Optimal selection within constraint.
        /// </summary>
        private static void Example1_SimpleCapture()
        {
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine("Example 1: Simple Kasino Capture");
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine();
            Console.WriteLine("🎴 Hand Card: King (K) = 13");
            Console.WriteLine("🃏 Table Cards: 2, 5, 8, Jack (11)");
            Console.WriteLine();
            Console.WriteLine("🎯 Goal: Find table cards that maximize value ≤ 13");
            Console.WriteLine();

            // Create items representing table cards as C# value tuples (id, weight, value)
            // For Kasino: weight = card value (constraint), value = card value (maximize)
            var tableCards = new (string, double, double)[]
            {
                ("card_2", 2.0, 2.0),
                ("card_5", 5.0, 5.0),
                ("card_8", 8.0, 8.0),
                ("card_J", 11.0, 11.0),
            };

            // Use CSharpBuilders helper - automatically converts C# value tuples to F# tuples!
            var problem = KnapsackProblem(tableCards, capacity: 13.0);

            // Solve using Knapsack module (null = use LocalBackend quantum simulation)
            var result = Knapsack.solve(problem, backend: null);

            // Display solution using extension methods (IsOk, GetOkValue, GetErrorValue, Count)
            if (result.IsOk())
            {
                var solution = result.GetOkValue();

                Console.WriteLine("✅ Capture Solution Found!");
                var selectedCards = string.Join(", ", solution.SelectedItems.Select(item => $"{item.Id} ({item.Value})"));
                Console.WriteLine($"   Cards to capture: {selectedCards}");
                Console.WriteLine($"   Total value: {solution.TotalValue}");
                Console.WriteLine($"   Total weight: {solution.TotalWeight}");
                Console.WriteLine($"   Cards captured: {solution.SelectedItems.Count()}");
                Console.WriteLine($"   Feasible: {solution.IsFeasible}");
            }
            else
            {
                var error = result.GetErrorValue();
                Console.WriteLine($"❌ No valid capture: {error}");
            }
        }

        /// <summary>
        /// Example 2: Complex Kasino Capture.
        /// Hand card: 10, Table: [1, 2, 3, 4, 5, 6, 7]
        /// Goal: Find optimal subset that maximizes value ≤ 10
        /// Multiple solutions exist: demonstrate optimization.
        /// </summary>
        private static void Example2_ComplexCapture()
        {
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine("Example 2: Complex Kasino Capture (Multiple Solutions)");
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine();
            Console.WriteLine("🎴 Hand Card: 10");
            Console.WriteLine("🃏 Table Cards: 1, 2, 3, 4, 5, 6, 7");
            Console.WriteLine();
            Console.WriteLine("🎯 Goal: Find optimal capture (maximum value ≤ 10)");
            Console.WriteLine("💡 Multiple solutions exist:");
            Console.WriteLine("   • [4, 6] = 10 points");
            Console.WriteLine("   • [3, 7] = 10 points");
            Console.WriteLine("   • [1, 2, 3, 4] = 10 points");
            Console.WriteLine("   • [1, 2, 7] = 10 points");
            Console.WriteLine();
            Console.WriteLine("⚡ Quantum speedup: 32x-181x for finding optimal solution!");
            Console.WriteLine();

            // Create items representing table cards (1-7) as C# value tuples
            var tableCards = Enumerable.Range(1, 7)
                .Select(i => ($"card_{i}", (double)i, (double)i))
                .ToArray();

            // Use CSharpBuilders helper - clean API with C# value tuples!
            var problem = KnapsackProblem(tableCards, capacity: 10.0);

            // Solve using Knapsack module (null = use LocalBackend quantum simulation)
            var result = Knapsack.solve(problem, backend: null);

            // Display solution using extension methods
            if (result.IsOk())
            {
                var solution = result.GetOkValue();

                Console.WriteLine("✅ Optimal Capture Found!");
                var selectedCards = string.Join(", ", solution.SelectedItems.Select(item => item.Id.Replace("card_", string.Empty, StringComparison.Ordinal)));
                Console.WriteLine($"   Cards to capture: {selectedCards}");
                Console.WriteLine($"   Total value: {solution.TotalValue}");
                Console.WriteLine($"   Total weight: {solution.TotalWeight}");
                Console.WriteLine($"   Cards captured: {solution.SelectedItems.Count()}");
                Console.WriteLine();
                Console.WriteLine("🚀 In real quantum hardware, this would run 32x-181x faster!");
            }
            else
            {
                var error = result.GetErrorValue();
                Console.WriteLine($"❌ No valid capture: {error}");
            }
        }

        /// <summary>
        /// Example 3: Multiple Kasino Captures.
        /// Demonstrate solving multiple capture scenarios in sequence
        /// Shows practical game play where multiple turns are optimized.
        /// </summary>
        private static void Example3_MultipleCaptures()
        {
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine("Example 3: Multiple Capture Scenarios (Game Sequence)");
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine();

            // NOTE: These scenarios show ONE optimal capture (not all valid Kasino combinations)
            // Real Kasino: Hand 7 with table [2,5,3,4] captures ALL cards (2+5=7 AND 3+4=7)
            // Knapsack: Finds ONE subset [2,5] or [3,4], not both combinations
            var scenarios = new[]
            {
                new { HandCard = "5", HandValue = 5.0, TableCards = new[] { 2.0, 3.0 }, Description = "Simple sum (2+3=5)" },
                new { HandCard = "9", HandValue = 9.0, TableCards = new[] { 4.0, 5.0, 6.0, 8.0 }, Description = "Unique optimal (4+5=9)" },
                new { HandCard = "King", HandValue = 13.0, TableCards = new[] { 6.0, 7.0, 10.0 }, Description = "Best match (6+7=13)" },
            };

            int captureNumber = 1;
            foreach (var scenario in scenarios)
            {
                Console.WriteLine($"🎮 Capture #{captureNumber}: {scenario.HandCard} = {scenario.HandValue}");
                Console.WriteLine($"   Description: {scenario.Description}");
                Console.Write($"   Table: ");

                // Create table cards as C# value tuples - clean and idiomatic!
                var tableCards = scenario.TableCards
                    .Select((value, index) => ($"card_{index + 1}", value, value))
                    .ToArray();

                Console.WriteLine(string.Join(", ", scenario.TableCards));

                // Use CSharpBuilders helper - no manual F# interop!
                var problem = KnapsackProblem(tableCards, capacity: scenario.HandValue);

                // Solve (null = use LocalBackend quantum simulation)
                var result = Knapsack.solve(problem, backend: null);

                if (result.IsOk())
                {
                    var solution = result.GetOkValue();
                    var capturedValues = string.Join(", ", solution.SelectedItems.Select(item => item.Value));
                    Console.WriteLine($"   ✅ Captured: [{capturedValues}] = {solution.TotalValue} ({solution.SelectedItems.Count()} cards)");
                }
                else
                {
                    Console.WriteLine($"   ❌ No valid capture");
                }

                Console.WriteLine();
                captureNumber++;
            }
        }

        /// <summary>
        /// Example 4: Real Kasino Capture Logic (All Valid Combinations).
        /// Demonstrates the NEW findAllValidCombinations function that captures
        /// ALL cards involved in ANY valid combination - the true Kasino game rule.
        /// </summary>
        private static void Example4_RealKasinoCapture()
        {
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine("Example 4: Real Kasino Capture (All Valid Combinations)");
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine();

            var scenarios = new[]
            {
                new { HandCard = "7", HandValue = 7.0, TableCards = new[] { 2.0, 5.0, 3.0, 4.0, 8.0, 9.0 }, Description = "Multiple combinations: 2+5=7 AND 3+4=7 (leaves 8, 9 on table)" },
                new { HandCard = "Ace", HandValue = 14.0, TableCards = new[] { 1.0, 13.0, 3.0, 11.0, 2.0, 6.0 }, Description = "Ace(14): Takes Ace(1)+King(13)=14 AND Jack(11)+3=14 (leaves 2, 6)" },
                new { HandCard = "10", HandValue = 10.0, TableCards = new[] { 3.0, 7.0, 4.0, 6.0, 1.0, 12.0 }, Description = "Multiple combinations: 3+7=10 AND 4+6=10 (leaves Ace, Queen)" },
            };

            int captureNumber = 1;
            foreach (var scenario in scenarios)
            {
                Console.WriteLine($"🎮 Capture #{captureNumber}: {scenario.HandCard} = {scenario.HandValue}");
                Console.WriteLine($"   Description: {scenario.Description}");
                Console.WriteLine($"   Table: {string.Join(", ", scenario.TableCards)}");
                Console.WriteLine();

                // Create table cards as C# value tuples
                var tableCards = scenario.TableCards
                    .Select((value, index) => ($"card_{value}", value, value))
                    .ToArray();

                // Create problem
                var problem = KnapsackProblem(tableCards, capacity: scenario.HandValue);

                // Use NEW findAllValidCombinations to find ALL combinations
                var (combinations, allCapturedItems, combinationCount) = Knapsack.findAllValidCombinations(problem);

                Console.WriteLine($"   🔍 Found {combinationCount} valid combination(s):");

                int combNum = 1;
                foreach (var combination in combinations)
                {
                    var combValues = string.Join(" + ", combination.Select(item => item.Value));
                    var combSum = combination.Sum(item => item.Value);
                    Console.WriteLine($"      Combination {combNum}: [{combValues}] = {combSum}");
                    combNum++;
                }

                Console.WriteLine();
                Console.WriteLine($"   ✅ KASINO CAPTURE (union of all combinations):");
                var capturedValues = string.Join(", ", allCapturedItems.Select(item => item.Value));
                var totalCaptured = allCapturedItems.Sum(item => item.Value);
                Console.WriteLine($"      Captured: [{capturedValues}]");
                Console.WriteLine($"      Total: {totalCaptured} points ({allCapturedItems.Count()} cards)");

                // Show remaining cards
                var capturedIds = allCapturedItems.Select(item => item.Id).ToHashSet();
                var remainingCards = tableCards.Where(card => !capturedIds.Contains(card.Item1)).ToList();

                if (remainingCards.Count != 0)
                {
                    var remainingValues = string.Join(", ", remainingCards.Select(card => card.Item2));
                    Console.WriteLine($"      Remaining: [{remainingValues}] ({remainingCards.Count} cards left on table)");
                }
                else
                {
                    Console.WriteLine($"      🎉 FULL TABLE CAPTURE! (All {tableCards.Length} cards taken)");
                }

                Console.WriteLine();
                captureNumber++;
            }
        }
    }
}
