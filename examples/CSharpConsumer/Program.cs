using System;
using FSharp.Azure.Quantum.Business;

namespace CSharpConsumer
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("C# Consumer for Quantum DSLs");

            // 1. Quantum Risk Engine
            Console.WriteLine("\n--- Testing QuantumRiskEngine ---");

            var report = new FSharp.Azure.Quantum.Business.CSharp.QuantumRiskEngineBuilder()
                .SetConfidenceLevel(0.99)
                .SetSimulationPaths(1000)
                .CalculateMetric(RiskMetric.ValueAtRisk)
                .BuildAndRun();

            Console.WriteLine($"Confidence Level: {report.ConfidenceLevel}");
            Console.WriteLine($"Method: {report.Method}");
            Console.WriteLine($"VaR Calculated: {report.VaR.IsSome}");

            // 2. Quantum Drug Discovery
            Console.WriteLine("\n--- Testing QuantumDrugDiscovery ---");
            
            var drugResult = new FSharp.Azure.Quantum.Business.CSharp.QuantumDrugDiscoveryBuilder()
                .TargetProteinFromPdb("test.pdb")
                .UseMethod(ScreeningMethod.QuantumKernelSVM)
                .Run();

            if (drugResult.IsError)
            {
                // Expected error
                var error = drugResult.ErrorValue;
                Console.WriteLine($"Expected Validation Error: {error}");
            }
            else
            {
                Console.WriteLine("Unexpected Success (files don't exist?)");
            }
        }
    }
}
