using System;
using FSharp.Azure.Quantum.Business;

namespace CSharpConsumer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("C# Consumer for Quantum DSLs");

            // 1. Quantum Risk Engine
            Console.WriteLine("\n--- Testing QuantumRiskEngine ---");

            var report = await new FSharp.Azure.Quantum.Business.CSharp.QuantumRiskEngineBuilder()
                .SetConfidenceLevel(0.99)
                .WithMarketReturns(new[] { 0.012, -0.031, 0.004, -0.008, 0.021, -0.015, 0.007, -0.002, 0.016, -0.024 })
                .CalculateMetric(RiskMetric.ValueAtRisk)
                .BuildAndRunAsync();

            Console.WriteLine($"Confidence Level: {report.ConfidenceLevel}");
            Console.WriteLine($"Method: {report.Method}");
            Console.WriteLine($"VaR Calculated: {report.VaR.IsSome}");

            // 2. Quantum Drug Discovery
            Console.WriteLine("\n--- Testing QuantumDrugDiscovery ---");
            
            var drugResult = await new FSharp.Azure.Quantum.Business.CSharp.QuantumDrugDiscoveryBuilder()
                .TargetProteinFromPdb("test.pdb")
                .UseMethod(ScreeningMethod.QuantumKernelSVM)
                .RunAsync();

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
