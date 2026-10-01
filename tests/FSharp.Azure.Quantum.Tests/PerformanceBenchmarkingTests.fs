module FSharp.Azure.Quantum.Tests.PerformanceBenchmarkingTests

// The classical benchmarks time the classical solvers: a result labelled "Classical" comes
// from TspSolver / PortfolioSolver, at sizes no simulator could run as a QUBO.

open System.Threading
open System.Threading.Tasks
open Xunit
open FSharp.Azure.Quantum

[<Fact>]
let ``benchmarkClassicalTSPAsync times the classical solver at 40 cities`` () : Task =
    task {
        let cities = PerformanceBenchmarking.generateRandomCities 40 (Some 7)

        let! result =
            PerformanceBenchmarking.benchmarkClassicalTSPAsync cities 2 None CancellationToken.None

        Assert.Equal("TSP", result.ProblemType)
        Assert.Equal("Classical", result.Solver)
        Assert.Equal(40, result.ProblemSize)
        Assert.True(result.SolutionQuality > 0.0, "a closed tour through 40 distinct cities has positive length")
        Assert.Equal(0.0, result.Cost)
    }

[<Fact>]
let ``benchmarkClassicalPortfolioAsync times the classical solver at 40 assets`` () : Task =
    task {
        let assets = PerformanceBenchmarking.generateRandomAssets 40 (Some 7)

        let! result =
            PerformanceBenchmarking.benchmarkClassicalPortfolioAsync assets 10000.0 2 None CancellationToken.None

        Assert.Equal("Portfolio", result.ProblemType)
        Assert.Equal("Classical", result.Solver)
        Assert.Equal(40, result.ProblemSize)
        Assert.True(result.SolutionQuality > 0.0, "the greedy portfolio has a positive expected return")
    }

[<Fact>]
let ``the TSP benchmark suite reports one classical result per problem size`` () : Task =
    task {
        let config: PerformanceBenchmarking.BenchmarkConfig =
            {
                ProblemSizes = [ 5; 12; 30 ]
                Repetitions = 1
                Backends = [ "Classical" ]
                OutputPath = ""
            }

        let! results =
            PerformanceBenchmarking.runTSPBenchmarkSuiteAsync config CancellationToken.None

        Assert.Equal<int list>([ 5; 12; 30 ], results |> List.map (fun r -> r.ProblemSize))
        Assert.All(results, (fun r -> Assert.Equal("Classical", r.Solver)))
    }
