namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Visualization

module CircuitExtensionsTests =

    let private createBellCircuit () =
        empty 2
        |> addGate (H 0)
        |> addGate (CNOT(0, 1))

    let private createSingleQubitCircuit () =
        empty 1
        |> addGate (H 0)
        |> addGate (X 0)

    // ========================================================================
    // ToASCII
    // ========================================================================

    [<Fact>]
    let ``ToASCII returns non-empty string`` () =
        let circuit = createBellCircuit()
        let result = circuit.ToASCII()
        Assert.True(result.Length > 0, "ASCII output should not be empty")

    [<Fact>]
    let ``ToASCII contains qubit labels`` () =
        let circuit = createBellCircuit()
        let result = circuit.ToASCII()
        Assert.Contains("q_0", result)
        Assert.Contains("q_1", result)

    [<Fact>]
    let ``ToASCII contains gate labels`` () =
        let circuit = createSingleQubitCircuit()
        let result = circuit.ToASCII()
        Assert.Contains("H", result)
        Assert.Contains("X", result)

    [<Fact>]
    let ``ToASCII handles empty circuit`` () =
        let circuit = empty 2
        let result = circuit.ToASCII()
        Assert.Contains("q_0", result)
        Assert.Contains("q_1", result)

    // ========================================================================
    // ToASCIIWithConfig
    // ========================================================================

    [<Fact>]
    let ``ToASCIIWithConfig hides measurements`` () =
        let circuit =
            empty 1
            |> addGate (H 0)
            |> addGate (Measure 0)
        let config = { VisualizationConfig.defaultConfig with ShowMeasurements = false }
        let result = circuit.ToASCIIWithConfig(config)
        Assert.Contains("H", result)
        Assert.DoesNotContain("M", result)

    [<Fact>]
    let ``ToASCIIWithConfig with default config shows measurements`` () =
        let circuit =
            empty 1
            |> addGate (Measure 0)
        let config = VisualizationConfig.defaultConfig
        let result = circuit.ToASCIIWithConfig(config)
        Assert.Contains("M", result)

    // ========================================================================
    // ToMermaid
    // ========================================================================

    [<Fact>]
    let ``ToMermaid returns non-empty string`` () =
        let circuit = createBellCircuit()
        let result = circuit.ToMermaid()
        Assert.True(result.Length > 0, "Mermaid output should not be empty")

    [<Fact>]
    let ``ToMermaid contains sequenceDiagram`` () =
        let circuit = createBellCircuit()
        let result = circuit.ToMermaid()
        Assert.Contains("sequenceDiagram", result)

    // ========================================================================
    // ToMermaidFlowchart
    // ========================================================================

    [<Fact>]
    let ``ToMermaidFlowchart returns non-empty string`` () =
        let circuit = createBellCircuit()
        let result = circuit.ToMermaidFlowchart()
        Assert.True(result.Length > 0, "Mermaid flowchart output should not be empty")

    [<Fact>]
    let ``ToMermaidFlowchart contains graph keyword`` () =
        let circuit = createBellCircuit()
        let result = circuit.ToMermaidFlowchart()
        // Mermaid flowcharts start with "graph" or "flowchart"
        Assert.True(result.Contains("graph") || result.Contains("flowchart"),
            "Expected 'graph' or 'flowchart' keyword in output")
