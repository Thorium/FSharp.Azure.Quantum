namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Visualization

module ASCIIRendererTests =

    // ========================================================================
    // BASIC RENDERING
    // ========================================================================

    [<Fact>]
    let ``render produces output for single H gate`` () =
        let gates = [CircuitGate (CircuitBuilder.H 0)]
        let result = ASCIIRenderer.render 1 gates
        Assert.True(result.Length > 0, "Output should not be empty")
        Assert.Contains("q_0", result)

    [<Fact>]
    let ``render shows qubit labels`` () =
        let gates = [CircuitGate (CircuitBuilder.H 0)]
        let result = ASCIIRenderer.render 2 gates
        Assert.Contains("q_0", result)
        Assert.Contains("q_1", result)

    [<Fact>]
    let ``render handles empty gate list`` () =
        let result = ASCIIRenderer.render 2 []
        Assert.Contains("q_0", result)
        Assert.Contains("q_1", result)

    // ========================================================================
    // SINGLE-QUBIT GATES
    // ========================================================================

    [<Fact>]
    let ``render shows X gate`` () =
        let gates = [CircuitGate (CircuitBuilder.X 0)]
        let result = ASCIIRenderer.render 1 gates
        Assert.Contains("X", result)

    [<Fact>]
    let ``render shows Y gate`` () =
        let gates = [CircuitGate (CircuitBuilder.Y 0)]
        let result = ASCIIRenderer.render 1 gates
        Assert.Contains("Y", result)

    [<Fact>]
    let ``render shows Z gate`` () =
        let gates = [CircuitGate (CircuitBuilder.Z 0)]
        let result = ASCIIRenderer.render 1 gates
        Assert.Contains("Z", result)

    [<Fact>]
    let ``render shows Measure gate as M`` () =
        let gates = [CircuitGate (CircuitBuilder.Measure 0)]
        let result = ASCIIRenderer.render 1 gates
        Assert.Contains("M", result)

    // ========================================================================
    // ROTATION GATES
    // ========================================================================

    [<Fact>]
    let ``render shows RX gate with angle`` () =
        let gates = [CircuitGate (CircuitBuilder.RX(0, 1.57))]
        let result = ASCIIRenderer.render 1 gates
        Assert.Contains("RX", result)

    [<Fact>]
    let ``render shows RY gate with angle`` () =
        let gates = [CircuitGate (CircuitBuilder.RY(0, 0.5))]
        let result = ASCIIRenderer.render 1 gates
        Assert.Contains("RY", result)

    [<Fact>]
    let ``render shows RZ gate with angle`` () =
        let gates = [CircuitGate (CircuitBuilder.RZ(0, 3.14))]
        let result = ASCIIRenderer.render 1 gates
        Assert.Contains("RZ", result)

    // ========================================================================
    // TWO-QUBIT GATES
    // ========================================================================

    [<Fact>]
    let ``render shows CNOT gate`` () =
        let gates = [CircuitGate (CircuitBuilder.CNOT(0, 1))]
        let result = ASCIIRenderer.render 2 gates
        Assert.Contains("X", result)

    [<Fact>]
    let ``render shows CZ gate`` () =
        let gates = [CircuitGate (CircuitBuilder.CZ(0, 1))]
        let result = ASCIIRenderer.render 2 gates
        Assert.Contains("Z", result)

    [<Fact>]
    let ``render shows SWAP gate`` () =
        let gates = [CircuitGate (CircuitBuilder.SWAP(0, 1))]
        let result = ASCIIRenderer.render 2 gates
        // SWAP should contain the cross symbol
        Assert.True(result.Length > 0)

    // ========================================================================
    // MULTI-GATE CIRCUITS
    // ========================================================================

    [<Fact>]
    let ``render handles multiple gates`` () =
        let gates = [
            CircuitGate (CircuitBuilder.H 0)
            CircuitGate (CircuitBuilder.CNOT(0, 1))
            CircuitGate (CircuitBuilder.Measure 0)
            CircuitGate (CircuitBuilder.Measure 1)
        ]
        let result = ASCIIRenderer.render 2 gates
        Assert.Contains("H", result)
        Assert.Contains("M", result)

    // ========================================================================
    // BARRIERS
    // ========================================================================

    [<Fact>]
    let ``render shows barriers`` () =
        let gates = [
            CircuitGate (CircuitBuilder.H 0)
            Barrier [0; 1]
            CircuitGate (CircuitBuilder.X 1)
        ]
        let result = ASCIIRenderer.render 2 gates
        Assert.True(result.Length > 0)

    // ========================================================================
    // RENDER WITH CONFIG
    // ========================================================================

    [<Fact>]
    let ``renderWithConfig hides measurements when configured`` () =
        let config = { VisualizationConfig.defaultConfig with ShowMeasurements = false }
        let gates = [
            CircuitGate (CircuitBuilder.H 0)
            CircuitGate (CircuitBuilder.Measure 0)
        ]
        let result = ASCIIRenderer.renderWithConfig config 1 gates
        Assert.Contains("H", result)
        // Measure gate should be filtered out
        Assert.DoesNotContain("M", result)

    [<Fact>]
    let ``renderWithConfig shows measurements by default`` () =
        let config = VisualizationConfig.defaultConfig
        let gates = [
            CircuitGate (CircuitBuilder.Measure 0)
        ]
        let result = ASCIIRenderer.renderWithConfig config 1 gates
        Assert.Contains("M", result)

    [<Fact>]
    let ``renderWithConfig hides barriers when configured`` () =
        let config = { VisualizationConfig.defaultConfig with ShowBarriers = false }
        let gates = [
            CircuitGate (CircuitBuilder.H 0)
            Barrier [0]
        ]
        let result = ASCIIRenderer.renderWithConfig config 1 gates
        Assert.Contains("H", result)

    [<Fact>]
    let ``VisualizationConfig defaultConfig has expected values`` () =
        let config = VisualizationConfig.defaultConfig
        Assert.True(config.ShowMeasurements)
        Assert.False(config.ShowBarriers)
        Assert.True(config.ShowInitialization)
        Assert.Equal(ASCII, config.Format)
