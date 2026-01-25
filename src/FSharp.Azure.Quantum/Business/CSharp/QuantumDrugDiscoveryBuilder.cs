namespace FSharp.Azure.Quantum.Business.CSharp;

using FSharp.Azure.Quantum.Core;
using Microsoft.FSharp.Core;
using static FSharp.Azure.Quantum.Business.QuantumDrugDiscoveryDSL;
using static FSharp.Azure.Quantum.Core.BackendAbstraction;

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
    private int _vqcLayers = 2;
    private int _vqcMaxEpochs = 50;
    private double _selectionBudget = 10.0;
    private double _diversityWeight = 0.5;

    /// <summary>
    /// Sets the target protein structure from a PDB file path.
    /// </summary>
    /// <param name="path">Path to a PDB file describing the target protein.</param>
    /// <returns>The current builder instance.</returns>
    public QuantumDrugDiscoveryBuilder TargetProteinFromPdb(string path)
    {
        _targetPdbPath = path;
        return this;
    }

    /// <summary>
    /// Loads screening candidates from a file (implementation-defined format).
    /// </summary>
    /// <param name="path">Path to the candidate molecules file.</param>
    /// <returns>The current builder instance.</returns>
    public QuantumDrugDiscoveryBuilder LoadCandidatesFromFile(string path)
    {
        _candidatesPath = path;
        return this;
    }

    /// <summary>
    /// Selects the screening method used for virtual screening.
    /// </summary>
    /// <param name="method">Screening method to use.</param>
    /// <returns>The current builder instance.</returns>
    public QuantumDrugDiscoveryBuilder UseMethod(ScreeningMethod method)
    {
        _method = method;
        return this;
    }

    /// <summary>
    /// Selects the feature map used in quantum kernels / embeddings.
    /// </summary>
    /// <param name="featureMap">Feature map to use.</param>
    /// <returns>The current builder instance.</returns>
    public QuantumDrugDiscoveryBuilder UseFeatureMap(FeatureMap featureMap)
    {
        _featureMap = featureMap;
        return this;
    }

    /// <summary>
    /// Sets the number of candidates evaluated per batch.
    /// </summary>
    /// <param name="size">Batch size.</param>
    /// <returns>The current builder instance.</returns>
    public QuantumDrugDiscoveryBuilder SetBatchSize(int size)
    {
        _batchSize = size;
        return this;
    }

    /// <summary>
    /// Sets the fingerprint size used to represent candidates.
    /// </summary>
    /// <param name="fingerprintSize">Fingerprint size (e.g., number of bits / features).</param>
    /// <returns>The current builder instance.</returns>
    public QuantumDrugDiscoveryBuilder SetFingerprintSize(int fingerprintSize)
    {
        _fingerprintSize = fingerprintSize;
        return this;
    }

    /// <summary>
    /// Sets the number of shots used when executing quantum circuits.
    /// </summary>
    /// <param name="shots">Number of shots.</param>
    /// <returns>The current builder instance.</returns>
    public QuantumDrugDiscoveryBuilder WithShots(int shots)
    {
        _shots = shots;
        return this;
    }

    /// <summary>
    /// Sets the quantum backend used for execution.
    /// </summary>
    /// <param name="backend">Backend implementation to use.</param>
    /// <returns>The current builder instance.</returns>
    public QuantumDrugDiscoveryBuilder WithBackend(IQuantumBackend backend)
    {
        _backend = backend;
        return this;
    }

    /// <summary>
    /// Sets the number of VQC ansatz layers (for VQCClassifier method).
    /// </summary>
    /// <param name="layers">Number of layers (default: 2).</param>
    /// <returns>The current builder instance.</returns>
    public QuantumDrugDiscoveryBuilder WithVQCLayers(int layers)
    {
        _vqcLayers = layers;
        return this;
    }

    /// <summary>
    /// Sets the maximum training epochs for VQC (for VQCClassifier method).
    /// </summary>
    /// <param name="epochs">Maximum epochs (default: 50).</param>
    /// <returns>The current builder instance.</returns>
    public QuantumDrugDiscoveryBuilder WithVQCMaxEpochs(int epochs)
    {
        _vqcMaxEpochs = epochs;
        return this;
    }

    /// <summary>
    /// Sets the budget constraint for diverse selection (for QAOADiverseSelection method).
    /// </summary>
    /// <param name="budget">Budget constraint (default: 10.0).</param>
    /// <returns>The current builder instance.</returns>
    public QuantumDrugDiscoveryBuilder WithSelectionBudget(double budget)
    {
        _selectionBudget = budget;
        return this;
    }

    /// <summary>
    /// Sets the diversity weight for QAOA selection (for QAOADiverseSelection method).
    /// </summary>
    /// <param name="weight">Diversity weight (default: 0.5).</param>
    /// <returns>The current builder instance.</returns>
    public QuantumDrugDiscoveryBuilder WithDiversityWeight(double weight)
    {
        _diversityWeight = weight;
        return this;
    }

    /// <summary>
    /// Builds the configuration and runs the drug discovery workflow.
    /// </summary>
    /// <returns>
    /// A result containing the <see cref="ScreeningResult"/> on success, or a <see cref="QuantumError"/> on failure.
    /// </returns>
    public FSharpResult<ScreeningResult, QuantumError> Run()
    {
        // Build CandidateSource from _candidatesPath if provided
        var candidateSource = _candidatesPath == null
            ? FSharpOption<CandidateSource>.None
            : FSharpOption<CandidateSource>.Some(CandidateSource.NewFilePath(_candidatesPath));

        var config = new DrugDiscoveryConfiguration(
            _targetPdbPath == null ? FSharpOption<string>.None : FSharpOption<string>.Some(_targetPdbPath),
            _candidatesPath == null ? FSharpOption<string>.None : FSharpOption<string>.Some(_candidatesPath),
            candidateSource,
            _method,
            _featureMap,
            _batchSize,
            _fingerprintSize,
            _shots,
            _backend == null ? FSharpOption<IQuantumBackend>.None : FSharpOption<IQuantumBackend>.Some(_backend),
            _vqcLayers,
            _vqcMaxEpochs,
            _selectionBudget,
            _diversityWeight);

        return drugDiscovery.Run(FSharpFunc<Unit, DrugDiscoveryConfiguration>.FromConverter(_ => config));
    }
}