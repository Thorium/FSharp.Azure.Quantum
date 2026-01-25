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

    public QuantumRiskEngineBuilder LoadMarketData(string path)
    {
        _marketDataPath = path;
        return this;
    }

    public QuantumRiskEngineBuilder SetConfidenceLevel(double level)
    {
        _confidenceLevel = level;
        return this;
    }

    public QuantumRiskEngineBuilder SetSimulationPaths(int paths)
    {
        _simulationPaths = paths;
        return this;
    }

    public QuantumRiskEngineBuilder UseAmplitudeEstimation(bool enable)
    {
        _useAmplitudeEstimation = enable;
        return this;
    }

    public QuantumRiskEngineBuilder UseErrorMitigation(bool enable)
    {
        _useErrorMitigation = enable;
        return this;
    }

    public QuantumRiskEngineBuilder CalculateMetric(RiskMetric metric)
    {
        _metrics.Add(metric);
        return this;
    }

    public QuantumRiskEngineBuilder WithQubits(int numQubits)
    {
        _numQubits = numQubits;
        return this;
    }

    public QuantumRiskEngineBuilder WithIterations(int groverIterations)
    {
        _groverIterations = groverIterations;
        return this;
    }

    public QuantumRiskEngineBuilder WithShots(int shots)
    {
        _shots = shots;
        return this;
    }

    public QuantumRiskEngineBuilder WithBackend(IQuantumBackend backend)
    {
        _backend = backend;
        return this;
    }

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
