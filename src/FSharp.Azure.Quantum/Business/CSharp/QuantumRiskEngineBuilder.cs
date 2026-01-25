namespace FSharp.Azure.Quantum.Business.CSharp;

using System.Collections.Generic;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using static FSharp.Azure.Quantum.Business.QuantumRiskEngineDSL;
using static FSharp.Azure.Quantum.Core.BackendAbstraction;

/// <summary>
/// C# Builder for Quantum Risk Engine.
/// Provides a fluent API for risk analysis without F# syntax.
/// </summary>
public class QuantumRiskEngineBuilder
{
    private string? _marketDataPath;
    private double _confidenceLevel = 0.95;
    private int _simulationPaths = 10000;
    private bool _useAmplitudeEstimation = false;
    private bool _useErrorMitigation = false;
    private int _numQubits = 5;
    private int _groverIterations = 2;
    private int _shots = 100;
    private IQuantumBackend? _backend;
    private readonly List<RiskMetric> _metrics = new();

    /// <summary>
    /// Sets the market data input path used by the risk engine.
    /// </summary>
    /// <param name="path">Path to the market data file.</param>
    /// <returns>The current builder instance.</returns>
    public QuantumRiskEngineBuilder LoadMarketData(string path)
    {
        _marketDataPath = path;
        return this;
    }

    /// <summary>
    /// Sets the confidence level used for risk metrics (e.g., VaR/CVaR).
    /// </summary>
    /// <param name="level">Confidence level (typically between 0 and 1).</param>
    /// <returns>The current builder instance.</returns>
    public QuantumRiskEngineBuilder SetConfidenceLevel(double level)
    {
        _confidenceLevel = level;
        return this;
    }

    /// <summary>
    /// Sets the number of Monte Carlo simulation paths.
    /// </summary>
    /// <param name="paths">Number of simulation paths.</param>
    /// <returns>The current builder instance.</returns>
    public QuantumRiskEngineBuilder SetSimulationPaths(int paths)
    {
        _simulationPaths = paths;
        return this;
    }

    /// <summary>
    /// Enables or disables amplitude estimation (when supported by the workflow).
    /// </summary>
    /// <param name="enable">Whether to enable amplitude estimation.</param>
    /// <returns>The current builder instance.</returns>
    public QuantumRiskEngineBuilder UseAmplitudeEstimation(bool enable)
    {
        _useAmplitudeEstimation = enable;
        return this;
    }

    /// <summary>
    /// Enables or disables error mitigation strategies (when supported by the workflow).
    /// </summary>
    /// <param name="enable">Whether to enable error mitigation.</param>
    /// <returns>The current builder instance.</returns>
    public QuantumRiskEngineBuilder UseErrorMitigation(bool enable)
    {
        _useErrorMitigation = enable;
        return this;
    }

    /// <summary>
    /// Adds a risk metric to compute in the report.
    /// </summary>
    /// <param name="metric">Metric to calculate.</param>
    /// <returns>The current builder instance.</returns>
    public QuantumRiskEngineBuilder CalculateMetric(RiskMetric metric)
    {
        _metrics.Add(metric);
        return this;
    }

    /// <summary>
    /// Sets the number of qubits allocated for the computation.
    /// </summary>
    /// <param name="numQubits">Number of qubits.</param>
    /// <returns>The current builder instance.</returns>
    public QuantumRiskEngineBuilder WithQubits(int numQubits)
    {
        _numQubits = numQubits;
        return this;
    }

    /// <summary>
    /// Sets the number of Grover iterations used by quantum subroutines.
    /// </summary>
    /// <param name="groverIterations">Number of Grover iterations.</param>
    /// <returns>The current builder instance.</returns>
    public QuantumRiskEngineBuilder WithIterations(int groverIterations)
    {
        _groverIterations = groverIterations;
        return this;
    }

    /// <summary>
    /// Sets the number of shots used when executing quantum circuits.
    /// </summary>
    /// <param name="shots">Number of shots.</param>
    /// <returns>The current builder instance.</returns>
    public QuantumRiskEngineBuilder WithShots(int shots)
    {
        _shots = shots;
        return this;
    }

    /// <summary>
    /// Sets the quantum backend used for execution.
    /// </summary>
    /// <param name="backend">Backend implementation to use.</param>
    /// <returns>The current builder instance.</returns>
    public QuantumRiskEngineBuilder WithBackend(IQuantumBackend backend)
    {
        _backend = backend;
        return this;
    }

    /// <summary>
    /// Builds a risk configuration from the current settings and executes the risk engine.
    /// </summary>
    /// <returns>A computed <see cref="RiskReport"/>.</returns>
    public RiskReport BuildAndRun()
    {
        var config = new RiskConfiguration(
            _marketDataPath == null ? FSharpOption<string>.None : FSharpOption<string>.Some(_marketDataPath),
            _confidenceLevel,
            _simulationPaths,
            _useAmplitudeEstimation,
            _useErrorMitigation,
            ListModule.OfSeq(_metrics),
            _numQubits,
            _groverIterations,
            _shots,
            _backend == null ? FSharpOption<IQuantumBackend>.None : FSharpOption<IQuantumBackend>.Some(_backend));

        return RiskEngine.execute(config);
    }

}
