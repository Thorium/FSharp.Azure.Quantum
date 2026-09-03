namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder

/// Tests for ResourceEstimation — logical counts + surface-code physical estimate.
module ResourceEstimationTests =

    let private sample () =
        empty 3
        |> addGate (H 0)
        |> addGate (T 0)
        |> addGate (T 1)
        |> addGate (TDG 2)   // T-dagger must also count toward T-count
        |> addGate (CNOT(0, 1))

    [<Fact>]
    let ``logical resources count qubits, gates, and T-count`` () =
        let l = ResourceEstimation.estimateLogical (sample ())
        Assert.Equal(3, l.LogicalQubits)
        Assert.Equal(5, l.TotalGates)
        Assert.Equal(3, l.TCount)          // two T + one T-dagger
        Assert.Equal(1, l.TwoQubitGates)   // one CNOT

    [<Fact>]
    let ``physical estimate uses an odd code distance >= 3 and 2 d^2 qubits per logical`` () =
        let est = ResourceEstimation.estimateDefault (sample ())
        let p = est.Physical
        Assert.True(p.CodeDistance >= 3, "distance must be at least 3")
        Assert.True(p.CodeDistance % 2 = 1, "surface-code distance must be odd")
        Assert.Equal(2 * p.CodeDistance * p.CodeDistance, p.PhysicalQubitsPerLogical)
        Assert.Equal(est.Logical.LogicalQubits * p.PhysicalQubitsPerLogical, p.TotalPhysicalQubits)
        Assert.True(p.EstimatedRuntimeSeconds > 0.0)

    [<Fact>]
    let ``lower physical error rate never needs a larger code distance`` () =
        let l = ResourceEstimation.estimateLogical (sample ())
        let noisy = ResourceEstimation.estimatePhysical { ResourceEstimation.defaultFaultToleranceParams with PhysicalErrorRate = 5e-3 } l
        let clean = ResourceEstimation.estimatePhysical { ResourceEstimation.defaultFaultToleranceParams with PhysicalErrorRate = 1e-4 } l
        Assert.True(clean.CodeDistance <= noisy.CodeDistance,
            $"clean d=%d{clean.CodeDistance} should be <= noisy d=%d{noisy.CodeDistance}")

    [<Fact>]
    let ``more logical qubits require more physical qubits`` () =
        let small = ResourceEstimation.estimateDefault (empty 2 |> addGate (CNOT(0, 1)))
        let big = ResourceEstimation.estimateDefault (empty 8 |> addGate (CNOT(0, 1)))
        Assert.True(big.Physical.TotalPhysicalQubits > small.Physical.TotalPhysicalQubits)
