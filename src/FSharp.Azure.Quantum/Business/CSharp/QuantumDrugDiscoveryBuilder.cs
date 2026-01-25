namespace FSharp.Azure.Quantum.Business.CSharp;

using System;
using System.Collections.Generic;
using FSharp.Azure.Quantum.Core.BackendAbstraction;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using static FSharp.Azure.Quantum.Business.QuantumDrugDiscoveryDSL;

/// <summary>
/// C# Builder for Quantum Drug Discovery.
/// Provides a fluent API for virtual screening without F# syntax.
/// </summary>
public class QuantumDrugDiscoveryBuilder
{
    private string? _targetPdbPath;
    private string? _candidatesPath;
    private ScreeningMethod _method = ScreeningMethod.QuantumKernelSVM;
    private FeatureMap _featureMap = FeatureMap.ZZFeatureMap;
    private int _batchSize = 10;
    private int _fingerprintSize = 8;
    private int _shots = 100;
    private IQuantumBackend? _backend;

    public QuantumDrugDiscoveryBuilder TargetProteinFromPdb(string path)
    {
        _targetPdbPath = path;
        return this;
    }

    public QuantumDrugDiscoveryBuilder LoadCandidatesFromFile(string path)
    {
        _candidatesPath = path;
        return this;
    }

    public QuantumDrugDiscoveryBuilder UseMethod(ScreeningMethod method)
    {
        _method = method;
        return this;
    }

    public QuantumDrugDiscoveryBuilder UseFeatureMap(FeatureMap featureMap)
    {
        _featureMap = featureMap;
        return this;
    }

    public QuantumDrugDiscoveryBuilder SetBatchSize(int size)
    {
        _batchSize = size;
        return this;
    }

    public QuantumDrugDiscoveryBuilder SetFingerprintSize(int fingerprintSize)
    {
        _fingerprintSize = fingerprintSize;
        return this;
    }

    public QuantumDrugDiscoveryBuilder WithShots(int shots)
    {
        _shots = shots;
        return this;
    }

    public QuantumDrugDiscoveryBuilder WithBackend(IQuantumBackend backend)
    {
        _backend = backend;
        return this;
    }

    public QuantumResult<ScreeningResult> Run()
    {
        var config = new DrugDiscoveryConfiguration(
            _targetPdbPath == null ? FSharpOption<string>.None : FSharpOption<string>.Some(_targetPdbPath),
            _candidatesPath == null ? FSharpOption<string>.None : FSharpOption<string>.Some(_candidatesPath),
            _method,
            _featureMap,
            _batchSize,
            _fingerprintSize,
            _shots,
            _backend == null ? FSharpOption<IQuantumBackend>.None : FSharpOption<IQuantumBackend>.Some(_backend)
        );

        return drugDiscovery.Run(_ => config);
    }
}
