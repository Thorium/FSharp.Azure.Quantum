namespace FSharp.Azure.Quantum.Tests

open System
open System.Threading
open System.Threading.Tasks
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

    let private defaultConfig =
        {
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

    /// Await the Result-returning executeAsync and unwrap it, as the deprecated sync wrapper did.
    let private run config =
        task {
            match! RiskEngine.executeAsync config CancellationToken.None with
            | Ok report -> return report
            | Error e -> return failwith $"Expected Ok, got Error: {e}"
        }

    // ========================================================================
    // CLASSICAL MONTE CARLO EXECUTION TESTS
    // ========================================================================

    [<Fact>]
    let ``execute with default config should return report with Method = Classical Monte Carlo`` () =
        task {
            let config =
                { defaultConfig with
                    Metrics = [ ValueAtRisk ]
                }

            let! report = run config
            Assert.Equal("Classical Monte Carlo", report.Method)
        }
        :> Task

    [<Fact>]
    let ``execute with VaR metric should compute non-negative VaR`` () =
        task {
            let config =
                { defaultConfig with
                    Metrics = [ ValueAtRisk ]
                    SimulationPaths = 1000
                }

            let! report = run config

            match report.VaR with
            | ValueSome var -> Assert.True(var >= 0.0, $"VaR should be non-negative, got {var}")
            | ValueNone -> failwith "Expected VaR to be computed"
        }
        :> Task

    [<Fact>]
    let ``execute with CVaR metric should compute value`` () =
        task {
            let config =
                { defaultConfig with
                    Metrics = [ ConditionalVaR ]
                    SimulationPaths = 1000
                }

            let! report = run config

            match report.CVaR with
            | ValueSome cvar -> Assert.True(cvar >= 0.0, $"CVaR should be non-negative, got {cvar}")
            | ValueNone -> failwith "Expected CVaR to be computed"
        }
        :> Task

    [<Fact>]
    let ``execute with ExpectedShortfall metric should compute value`` () =
        task {
            let config =
                { defaultConfig with
                    Metrics = [ ExpectedShortfall ]
                    SimulationPaths = 1000
                }

            let! report = run config

            match report.ExpectedShortfall with
            | ValueSome es -> Assert.True(es >= 0.0, $"ES should be non-negative, got {es}")
            | ValueNone -> failwith "Expected ExpectedShortfall to be computed"
        }
        :> Task

    [<Fact>]
    let ``execute with Volatility metric should compute positive value`` () =
        task {
            let config =
                { defaultConfig with
                    Metrics = [ Volatility ]
                    SimulationPaths = 1000
                }

            let! report = run config

            match report.Volatility with
            | ValueSome vol -> Assert.True(vol > 0.0, $"Volatility should be positive, got {vol}")
            | ValueNone -> failwith "Expected Volatility to be computed"
        }
        :> Task

    [<Fact>]
    let ``execute with all metrics should compute all values`` () =
        task {
            let config =
                { defaultConfig with
                    Metrics = [ ValueAtRisk; ConditionalVaR; ExpectedShortfall; Volatility ]
                    SimulationPaths = 1000
                }

            let! report = run config
            Assert.True(report.VaR.IsSome, "VaR should be computed")
            Assert.True(report.CVaR.IsSome, "CVaR should be computed")
            Assert.True(report.ExpectedShortfall.IsSome, "ES should be computed")
            Assert.True(report.Volatility.IsSome, "Volatility should be computed")
        }
        :> Task

    [<Fact>]
    let ``execute with no metrics should return ValueNone for all`` () =
        task {
            let config =
                { defaultConfig with
                    Metrics = []
                    SimulationPaths = 1000
                }

            let! report = run config
            Assert.True(report.VaR.IsNone, "VaR should be None when not requested")
            Assert.True(report.CVaR.IsNone, "CVaR should be None when not requested")
            Assert.True(report.ExpectedShortfall.IsNone, "ES should be None when not requested")
            Assert.True(report.Volatility.IsNone, "Volatility should be None when not requested")
        }
        :> Task

    [<Fact>]
    let ``execute should preserve confidence level in report`` () =
        task {
            let config =
                { defaultConfig with
                    ConfidenceLevel = 0.99
                    SimulationPaths = 1000
                }

            let! report = run config
            Assert.Equal(0.99, report.ConfidenceLevel)
        }
        :> Task

    [<Fact>]
    let ``execute should record positive execution time`` () =
        task {
            let config =
                { defaultConfig with
                    Metrics = [ ValueAtRisk ]
                    SimulationPaths = 1000
                }

            let! report = run config
            Assert.True(report.ExecutionTimeMs >= 0.0, "ExecutionTimeMs should be non-negative")
        }
        :> Task

    [<Fact>]
    let ``execute should preserve configuration in report`` () =
        task {
            let config =
                { defaultConfig with
                    SimulationPaths = 500
                    ConfidenceLevel = 0.90
                }

            let! report = run config
            Assert.Equal(500, report.Configuration.SimulationPaths)
            Assert.Equal(0.90, report.Configuration.ConfidenceLevel)
        }
        :> Task

    // ========================================================================
    // QUANTUM AMPLITUDE ESTIMATION TESTS
    // ========================================================================

    [<Fact>]
    let ``execute with UseAmplitudeEstimation and backend should use quantum path`` () =
        task {
            let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend

            let config =
                { defaultConfig with
                    UseAmplitudeEstimation = true
                    Backend = Some quantumBackend
                    NumQubits = 3
                    GroverIterations = 1
                    Shots = 100
                    SimulationPaths = 200
                    Metrics = [ ValueAtRisk ]
                }

            let! report = run config
            Assert.Equal("Quantum Amplitude Estimation", report.Method)
        }
        :> Task

    [<Fact>]
    let ``quantum path should compute non-negative VaR`` () =
        task {
            let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend

            let config =
                { defaultConfig with
                    UseAmplitudeEstimation = true
                    Backend = Some quantumBackend
                    NumQubits = 3
                    GroverIterations = 1
                    Shots = 100
                    SimulationPaths = 200
                    Metrics = [ ValueAtRisk ]
                }

            let! report = run config

            match report.VaR with
            | ValueSome var -> Assert.True(Double.IsFinite(var), $"VaR should be finite, got {var}")
            | ValueNone -> failwith "Expected VaR to be computed"
        }
        :> Task

    [<Fact>]
    let ``quantum path should compute CVaR and ExpectedShortfall`` () =
        task {
            let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend

            let config =
                { defaultConfig with
                    UseAmplitudeEstimation = true
                    Backend = Some quantumBackend
                    NumQubits = 3
                    GroverIterations = 1
                    Shots = 100
                    SimulationPaths = 200
                    Metrics = [ ConditionalVaR; ExpectedShortfall ]
                }

            let! report = run config
            Assert.True(report.CVaR.IsSome, "CVaR should be computed")
            Assert.True(report.ExpectedShortfall.IsSome, "ES should be computed")
        }
        :> Task

    [<Fact>]
    let ``quantum path should compute Volatility`` () =
        task {
            let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend

            let config =
                { defaultConfig with
                    UseAmplitudeEstimation = true
                    Backend = Some quantumBackend
                    NumQubits = 3
                    GroverIterations = 1
                    Shots = 100
                    SimulationPaths = 200
                    Metrics = [ Volatility ]
                }

            let! report = run config

            match report.Volatility with
            | ValueSome vol -> Assert.True(vol > 0.0, $"Volatility should be positive, got {vol}")
            | ValueNone -> failwith "Expected Volatility to be computed"
        }
        :> Task

    [<Fact>]
    let ``quantum path with no metrics should return ValueNone for all`` () =
        task {
            let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend

            let config =
                { defaultConfig with
                    UseAmplitudeEstimation = true
                    Backend = Some quantumBackend
                    NumQubits = 3
                    GroverIterations = 1
                    Shots = 100
                    SimulationPaths = 200
                    Metrics = []
                }

            let! report = run config
            Assert.Equal("Quantum Amplitude Estimation", report.Method)
            Assert.True(report.VaR.IsNone, "VaR should be None when not requested")
            Assert.True(report.CVaR.IsNone, "CVaR should be None when not requested")
        }
        :> Task

    [<Fact>]
    let ``quantum path should preserve confidence level`` () =
        task {
            let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend

            let config =
                { defaultConfig with
                    UseAmplitudeEstimation = true
                    Backend = Some quantumBackend
                    NumQubits = 3
                    GroverIterations = 1
                    Shots = 100
                    SimulationPaths = 200
                    ConfidenceLevel = 0.99
                    Metrics = [ ValueAtRisk ]
                }

            let! report = run config
            Assert.Equal(0.99, report.ConfidenceLevel)
            Assert.Equal("Quantum Amplitude Estimation", report.Method)
        }
        :> Task

    [<Fact>]
    let ``execute with UseAmplitudeEstimation but no backend should not fail`` () =
        task {
            // UseAmplitudeEstimation=true but Backend=None skips quantum path
            let config =
                { defaultConfig with
                    UseAmplitudeEstimation = true
                    Backend = None
                    Metrics = [ ValueAtRisk ]
                    SimulationPaths = 1000
                }

            let! report = run config
            Assert.Equal("Classical Monte Carlo", report.Method)
        }
        :> Task

    // ========================================================================
    // ASYNC EXECUTION TESTS
    // ========================================================================

    [<Fact; Trait("Category", "Slow")>]
    let ``executeAsync with cancellation token should respect cancellation`` () =
        task {
            use cts = new CancellationTokenSource()
            do! cts.CancelAsync()

            // The builder runs executeAsync with the configured token, so a cancelled
            // token cancels the analysis and awaiting it raises OperationCanceledException
            let! _ =
                Assert.ThrowsAnyAsync<OperationCanceledException>(fun () ->
                    quantumRiskEngine {
                        cancellation_token cts.Token
                        calculate_metric ValueAtRisk
                        set_simulation_paths 1000
                    }
                    :> Task)

            ()
        }
        :> Task

    [<Fact>]
    let ``executeAsync is cancelled by the configuration's token as well as by the caller's`` () =
        task {
            use configured = new CancellationTokenSource()
            use callers = new CancellationTokenSource()

            let config =
                { defaultConfig with
                    Metrics = [ ValueAtRisk ]
                    SimulationPaths = 1000
                    CancellationToken = Some configured.Token
                }

            // Neither token cancelled: the two are linked and the analysis runs
            let! report = RiskEngine.executeAsync config callers.Token
            Assert.True(Result.isOk report, $"Expected Ok but got {report}")

            // The configuration's token alone cancels it
            do! configured.CancelAsync()

            let! _ =
                Assert.ThrowsAnyAsync<OperationCanceledException>(fun () ->
                    RiskEngine.executeAsync config callers.Token :> Task)

            // And so does the caller's token alone
            do! callers.CancelAsync()

            let! _ =
                Assert.ThrowsAnyAsync<OperationCanceledException>(fun () ->
                    RiskEngine.executeAsync
                        { config with
                            CancellationToken = Some CancellationToken.None
                        }
                        callers.Token
                    :> Task)

            ()
        }
        :> Task

    // ========================================================================
    // HIGHER CONFIDENCE LEVEL TESTS
    // ========================================================================

    [<Fact>]
    let ``higher confidence level should yield higher VaR`` () =
        task {
            let config95 =
                { defaultConfig with
                    ConfidenceLevel = 0.95
                    Metrics = [ ValueAtRisk ]
                    SimulationPaths = 10000
                }

            let config99 =
                { defaultConfig with
                    ConfidenceLevel = 0.99
                    Metrics = [ ValueAtRisk ]
                    SimulationPaths = 10000
                }

            let! report95 = run config95
            let! report99 = run config99

            match report95.VaR, report99.VaR with
            | ValueSome var95, ValueSome var99 ->
                Assert.True(var99 >= var95, $"99%% VaR ({var99}) should be >= 95%% VaR ({var95})")
            | _ -> failwith "Both VaR values should be computed"
        }
        :> Task

    // ========================================================================
    // CE BUILDER TESTS
    // ========================================================================

    [<Fact>]
    let ``quantumRiskEngine CE should produce Ok result`` () =
        task {
            let! result =
                quantumRiskEngine {
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
        }
        :> Task

    [<Fact>]
    let ``quantumRiskEngine CE should set multiple metrics`` () =
        task {
            let! result =
                quantumRiskEngine {
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
        }
        :> Task

    [<Fact>]
    let ``quantumRiskEngine CE with amplitude estimation and backend should succeed`` () =
        task {
            let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend

            let! result =
                quantumRiskEngine {
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
        }
        :> Task

    [<Fact>]
    let ``quantumRiskEngine CE should set qubits and iterations`` () =
        task {
            let! result =
                quantumRiskEngine {
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
        }
        :> Task

    [<Fact>]
    let ``quantumRiskEngine CE should set confidence level`` () =
        task {
            let! result =
                quantumRiskEngine {
                    set_confidence_level 0.99
                    set_simulation_paths 500
                    calculate_metric ValueAtRisk
                }

            result
            |> Result.map (fun report -> Assert.Equal(0.99, report.ConfidenceLevel))
            |> Result.defaultWith (fun e -> failwith $"Should succeed, got error: {e}")
        }
        :> Task

    [<Fact>]
    let ``quantumRiskEngine CE with no metrics should succeed with empty results`` () =
        task {
            match! quantumRiskEngine { set_simulation_paths 500 } with
            | Ok report ->
                Assert.True(report.VaR.IsNone)
                Assert.True(report.CVaR.IsNone)
            | Error e -> failwith $"Should succeed, got error: {e}"
        }
        :> Task

    // ========================================================================
    // DETERMINISTIC RESULTS TEST
    // ========================================================================

    [<Fact>]
    let ``mock data generator should be deterministic with seed 42`` () =
        task {
            let config =
                { defaultConfig with
                    Metrics = [ ValueAtRisk; Volatility ]
                    SimulationPaths = 100
                }

            let! report1 = run config
            let! report2 = run config
            Assert.Equal(report1.VaR, report2.VaR)
            Assert.Equal(report1.Volatility, report2.Volatility)
            Assert.Equal(report1.Method, report2.Method)
        }
        :> Task

    // ========================================================================
    // ROUTES: every quantum metric from amplitude estimation
    // ========================================================================

    let private quantumConfig (backend: IQuantumBackend) =
        { defaultConfig with
            UseAmplitudeEstimation = true
            Backend = Some backend
            NumQubits = 4
            GroverIterations = 2
            Shots = 1000
            SimulationPaths = 2000
            Metrics = [ ValueAtRisk; ConditionalVaR; Volatility ]
        }

    [<Fact>]
    let ``quantum volatility matches the returns' standard deviation on the local simulator`` () =
        task {
            let! report = run (quantumConfig (LocalBackend.LocalBackend()))

            let! classical =
                run
                    { quantumConfig (LocalBackend.LocalBackend()) with
                        UseAmplitudeEstimation = false
                    }

            match report.Volatility, classical.Volatility with
            | ValueSome q, ValueSome c ->
                // Bin midpoints on 16 bins: a few per cent of discretisation error, no more.
                Assert.True(abs (q - c) / c < 0.05, $"quantum {q} vs sample {c}")
            | _ -> failwith "Expected both volatilities"
        }
        :> Task

    [<Fact>]
    let ``quantum VaR and CVaR on a whole-circuit sampling backend agree with the exact local estimates`` () =
        task {
            let backend = SampledWholeCircuit.Backend(8000, 17)
            let! local = run (quantumConfig (LocalBackend.LocalBackend()))
            let! sampled = run (quantumConfig backend)

            Assert.Equal("Quantum Amplitude Estimation", local.Method)
            Assert.Contains("whole circuits sampled at 8000 shots", sampled.Method)
            Assert.True(backend.Executed > 0)

            match local.VaR, sampled.VaR, local.CVaR, sampled.CVaR, sampled.Volatility with
            | ValueSome lv, ValueSome sv, ValueSome lc, ValueSome sc, ValueSome vol ->
                // Bisection on a sampled CDF may stop one bin away; one bin is 1/16 of the range.
                let binWidth = 0.02 * 8.0 / 16.0
                Assert.True(abs (lv - sv) <= binWidth + 1e-9, $"VaR local {lv} vs sampled {sv}")
                Assert.True(abs (lc - sc) <= binWidth, $"CVaR local {lc} vs sampled {sc}")
                Assert.True(vol > 0.0)
            | other -> failwith $"Expected every metric, got {other}"
        }
        :> Task
