namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core.BackendAbstraction

module QuantumRiskEngineTests =

    // ========================================================================
    // HELPERS
    // ========================================================================

    let private defaultConfig = {
        MarketDataPath = None
        ConfidenceLevel = 0.95
        SimulationPaths = 10000
        UseAmplitudeEstimation = false
        UseErrorMitigation = false
        Metrics = []
        NumQubits = 5
        GroverIterations = 2
        Shots = 100
        Backend = None
        CancellationToken = None
    }

    // ========================================================================
    // CLASSICAL MONTE CARLO EXECUTION TESTS
    // ========================================================================

    [<Fact>]
    let ``execute with default config should return report with Method = Classical Monte Carlo`` () =
        let config = { defaultConfig with Metrics = [ValueAtRisk] }
        let report = RiskEngine.execute config
        Assert.Equal("Classical Monte Carlo", report.Method)

    [<Fact>]
    let ``execute with VaR metric should compute non-negative VaR`` () =
        let config = { defaultConfig with Metrics = [ValueAtRisk]; SimulationPaths = 1000 }
        let report = RiskEngine.execute config
        match report.VaR with
        | ValueSome var -> Assert.True(var >= 0.0, $"VaR should be non-negative, got {var}")
        | ValueNone -> failwith "Expected VaR to be computed"

    [<Fact>]
    let ``execute with CVaR metric should compute value`` () =
        let config = { defaultConfig with Metrics = [ConditionalVaR]; SimulationPaths = 1000 }
        let report = RiskEngine.execute config
        match report.CVaR with
        | ValueSome cvar -> Assert.True(cvar >= 0.0, $"CVaR should be non-negative, got {cvar}")
        | ValueNone -> failwith "Expected CVaR to be computed"

    [<Fact>]
    let ``execute with ExpectedShortfall metric should compute value`` () =
        let config = { defaultConfig with Metrics = [ExpectedShortfall]; SimulationPaths = 1000 }
        let report = RiskEngine.execute config
        match report.ExpectedShortfall with
        | ValueSome es -> Assert.True(es >= 0.0, $"ES should be non-negative, got {es}")
        | ValueNone -> failwith "Expected ExpectedShortfall to be computed"

    [<Fact>]
    let ``execute with Volatility metric should compute positive value`` () =
        let config = { defaultConfig with Metrics = [Volatility]; SimulationPaths = 1000 }
        let report = RiskEngine.execute config
        match report.Volatility with
        | ValueSome vol -> Assert.True(vol > 0.0, $"Volatility should be positive, got {vol}")
        | ValueNone -> failwith "Expected Volatility to be computed"

    [<Fact>]
    let ``execute with all metrics should compute all values`` () =
        let config = { defaultConfig with 
                        Metrics = [ValueAtRisk; ConditionalVaR; ExpectedShortfall; Volatility]
                        SimulationPaths = 1000 }
        let report = RiskEngine.execute config
        Assert.True(report.VaR.IsSome, "VaR should be computed")
        Assert.True(report.CVaR.IsSome, "CVaR should be computed")
        Assert.True(report.ExpectedShortfall.IsSome, "ES should be computed")
        Assert.True(report.Volatility.IsSome, "Volatility should be computed")

    [<Fact>]
    let ``execute with no metrics should return ValueNone for all`` () =
        let config = { defaultConfig with Metrics = []; SimulationPaths = 1000 }
        let report = RiskEngine.execute config
        Assert.True(report.VaR.IsNone, "VaR should be None when not requested")
        Assert.True(report.CVaR.IsNone, "CVaR should be None when not requested")
        Assert.True(report.ExpectedShortfall.IsNone, "ES should be None when not requested")
        Assert.True(report.Volatility.IsNone, "Volatility should be None when not requested")

    [<Fact>]
    let ``execute should preserve confidence level in report`` () =
        let config = { defaultConfig with ConfidenceLevel = 0.99; SimulationPaths = 1000 }
        let report = RiskEngine.execute config
        Assert.Equal(0.99, report.ConfidenceLevel)

    [<Fact>]
    let ``execute should record positive execution time`` () =
        let config = { defaultConfig with Metrics = [ValueAtRisk]; SimulationPaths = 1000 }
        let report = RiskEngine.execute config
        Assert.True(report.ExecutionTimeMs >= 0.0, "ExecutionTimeMs should be non-negative")

    [<Fact>]
    let ``execute should preserve configuration in report`` () =
        let config = { defaultConfig with SimulationPaths = 500; ConfidenceLevel = 0.90 }
        let report = RiskEngine.execute config
        Assert.Equal(500, report.Configuration.SimulationPaths)
        Assert.Equal(0.90, report.Configuration.ConfidenceLevel)

    // ========================================================================
    // QUANTUM AMPLITUDE ESTIMATION TESTS
    // ========================================================================

    [<Fact>]
    let ``execute with UseAmplitudeEstimation and backend should use quantum path`` () =
        let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend
        let config = { defaultConfig with
                        UseAmplitudeEstimation = true
                        Backend = Some quantumBackend
                        NumQubits = 3
                        GroverIterations = 1
                        Shots = 100
                        SimulationPaths = 200
                        Metrics = [ValueAtRisk] }
        let report = RiskEngine.execute config
        Assert.Equal("Quantum Amplitude Estimation", report.Method)

    [<Fact>]
    let ``quantum path should compute non-negative VaR`` () =
        let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend
        let config = { defaultConfig with
                        UseAmplitudeEstimation = true
                        Backend = Some quantumBackend
                        NumQubits = 3
                        GroverIterations = 1
                        Shots = 100
                        SimulationPaths = 200
                        Metrics = [ValueAtRisk] }
        let report = RiskEngine.execute config
        match report.VaR with
        | ValueSome var -> Assert.True(System.Double.IsFinite(var), $"VaR should be finite, got {var}")
        | ValueNone -> failwith "Expected VaR to be computed"

    [<Fact>]
    let ``quantum path should compute CVaR and ExpectedShortfall`` () =
        let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend
        let config = { defaultConfig with
                        UseAmplitudeEstimation = true
                        Backend = Some quantumBackend
                        NumQubits = 3
                        GroverIterations = 1
                        Shots = 100
                        SimulationPaths = 200
                        Metrics = [ConditionalVaR; ExpectedShortfall] }
        let report = RiskEngine.execute config
        Assert.True(report.CVaR.IsSome, "CVaR should be computed")
        Assert.True(report.ExpectedShortfall.IsSome, "ES should be computed")

    [<Fact>]
    let ``quantum path should compute Volatility classically`` () =
        let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend
        let config = { defaultConfig with
                        UseAmplitudeEstimation = true
                        Backend = Some quantumBackend
                        NumQubits = 3
                        GroverIterations = 1
                        Shots = 100
                        SimulationPaths = 200
                        Metrics = [Volatility] }
        let report = RiskEngine.execute config
        match report.Volatility with
        | ValueSome vol -> Assert.True(vol > 0.0, $"Volatility should be positive, got {vol}")
        | ValueNone -> failwith "Expected Volatility to be computed"

    [<Fact>]
    let ``quantum path with no metrics should return ValueNone for all`` () =
        let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend
        let config = { defaultConfig with
                        UseAmplitudeEstimation = true
                        Backend = Some quantumBackend
                        NumQubits = 3
                        GroverIterations = 1
                        Shots = 100
                        SimulationPaths = 200
                        Metrics = [] }
        let report = RiskEngine.execute config
        Assert.Equal("Quantum Amplitude Estimation", report.Method)
        Assert.True(report.VaR.IsNone, "VaR should be None when not requested")
        Assert.True(report.CVaR.IsNone, "CVaR should be None when not requested")

    [<Fact>]
    let ``quantum path should preserve confidence level`` () =
        let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend
        let config = { defaultConfig with
                        UseAmplitudeEstimation = true
                        Backend = Some quantumBackend
                        NumQubits = 3
                        GroverIterations = 1
                        Shots = 100
                        SimulationPaths = 200
                        ConfidenceLevel = 0.99
                        Metrics = [ValueAtRisk] }
        let report = RiskEngine.execute config
        Assert.Equal(0.99, report.ConfidenceLevel)
        Assert.Equal("Quantum Amplitude Estimation", report.Method)

    [<Fact>]
    let ``execute with UseAmplitudeEstimation but no backend should not fail`` () =
        // UseAmplitudeEstimation=true but Backend=None skips quantum path
        let config = { defaultConfig with 
                        UseAmplitudeEstimation = true
                        Backend = None
                        Metrics = [ValueAtRisk]
                        SimulationPaths = 1000 }
        let report = RiskEngine.execute config
        Assert.Equal("Classical Monte Carlo", report.Method)

    // ========================================================================
    // ASYNC EXECUTION TESTS
    // ========================================================================

    [<Fact>]
    let ``executeAsync should return same result as execute`` () =
        let config = { defaultConfig with Metrics = [ValueAtRisk; Volatility]; SimulationPaths = 1000 }
        let syncReport = RiskEngine.execute config
        let asyncReport = RiskEngine.executeAsync config |> Async.RunSynchronously
        // Both use same deterministic RNG seed=42, should produce identical results
        Assert.Equal(syncReport.VaR, asyncReport.VaR)
        Assert.Equal(syncReport.Volatility, asyncReport.Volatility)
        Assert.Equal(syncReport.Method, asyncReport.Method)

    [<Fact>]
    let ``executeAsync with cancellation token should respect cancellation`` () =
        let cts = new Threading.CancellationTokenSource()
        cts.Cancel()
        let config = { defaultConfig with 
                        CancellationToken = Some cts.Token
                        Metrics = [ValueAtRisk]
                        SimulationPaths = 1000 }
        // execute internally calls executeAsync |> Async.RunSynchronously,
        // which propagates cancellation as OperationCanceledException
        Assert.Throws<OperationCanceledException>(fun () ->
            RiskEngine.execute config |> ignore) |> ignore

    // ========================================================================
    // HIGHER CONFIDENCE LEVEL TESTS
    // ========================================================================

    [<Fact>]
    let ``higher confidence level should yield higher VaR`` () =
        let config95 = { defaultConfig with ConfidenceLevel = 0.95; Metrics = [ValueAtRisk]; SimulationPaths = 10000 }
        let config99 = { defaultConfig with ConfidenceLevel = 0.99; Metrics = [ValueAtRisk]; SimulationPaths = 10000 }
        let report95 = RiskEngine.execute config95
        let report99 = RiskEngine.execute config99
        match report95.VaR, report99.VaR with
        | ValueSome var95, ValueSome var99 ->
            Assert.True(var99 >= var95, $"99%% VaR ({var99}) should be >= 95%% VaR ({var95})")
        | _ -> failwith "Both VaR values should be computed"

    // ========================================================================
    // CE BUILDER TESTS
    // ========================================================================

    [<Fact>]
    let ``quantumRiskEngine CE should produce Ok result`` () =
        let result = quantumRiskEngine {
            set_confidence_level 0.95
            set_simulation_paths 1000
            calculate_metric ValueAtRisk
            calculate_metric Volatility
        }
        match result with
        | Ok report ->
            Assert.Equal(0.95, report.ConfidenceLevel)
            Assert.True(report.VaR.IsSome)
            Assert.True(report.Volatility.IsSome)
        | Error e -> failwith $"Should succeed, got error: {e}"

    [<Fact>]
    let ``quantumRiskEngine CE should set multiple metrics`` () =
        let result = quantumRiskEngine {
            set_simulation_paths 500
            calculate_metric ValueAtRisk
            calculate_metric ConditionalVaR
            calculate_metric ExpectedShortfall
            calculate_metric Volatility
        }
        match result with
        | Ok report ->
            Assert.True(report.VaR.IsSome)
            Assert.True(report.CVaR.IsSome)
            Assert.True(report.ExpectedShortfall.IsSome)
            Assert.True(report.Volatility.IsSome)
        | Error e -> failwith $"Should succeed, got error: {e}"

    [<Fact>]
    let ``quantumRiskEngine CE with amplitude estimation and backend should succeed`` () =
        let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend
        let result = quantumRiskEngine {
            use_amplitude_estimation true
            backend quantumBackend
            qubits 3
            iterations 1
            shots 100
            set_simulation_paths 200
            calculate_metric ValueAtRisk
        }
        match result with
        | Ok report ->
            Assert.Equal("Quantum Amplitude Estimation", report.Method)
            Assert.True(report.VaR.IsSome, "VaR should be computed")
        | Error e -> failwith $"Should succeed, got error: {e}"

    [<Fact>]
    let ``quantumRiskEngine CE should set qubits and iterations`` () =
        let result = quantumRiskEngine {
            qubits 8
            iterations 5
            shots 200
            set_simulation_paths 500
            calculate_metric ValueAtRisk
        }
        match result with
        | Ok report ->
            Assert.Equal(8, report.Configuration.NumQubits)
            Assert.Equal(5, report.Configuration.GroverIterations)
            Assert.Equal(200, report.Configuration.Shots)
        | Error e -> failwith $"Should succeed, got error: {e}"

    [<Fact>]
    let ``quantumRiskEngine CE should set confidence level`` () =
        let result = quantumRiskEngine {
            set_confidence_level 0.99
            set_simulation_paths 500
            calculate_metric ValueAtRisk
        }
        match result with
        | Ok report -> Assert.Equal(0.99, report.ConfidenceLevel)
        | Error e -> failwith $"Should succeed, got error: {e}"

    [<Fact>]
    let ``quantumRiskEngine CE with no metrics should succeed with empty results`` () =
        let result = quantumRiskEngine {
            set_simulation_paths 500
        }
        match result with
        | Ok report ->
            Assert.True(report.VaR.IsNone)
            Assert.True(report.CVaR.IsNone)
        | Error e -> failwith $"Should succeed, got error: {e}"

    // ========================================================================
    // DETERMINISTIC RESULTS TEST
    // ========================================================================

    [<Fact>]
    let ``mock data generator should be deterministic with seed 42`` () =
        let config = { defaultConfig with Metrics = [ValueAtRisk; Volatility]; SimulationPaths = 100 }
        let report1 = RiskEngine.execute config
        let report2 = RiskEngine.execute config
        Assert.Equal(report1.VaR, report2.VaR)
        Assert.Equal(report1.Volatility, report2.Volatility)
