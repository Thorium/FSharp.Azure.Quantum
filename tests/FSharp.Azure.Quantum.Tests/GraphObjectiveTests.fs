namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.GraphOptimization

/// Tests for the edge-selection QUBO objectives (MaximizeEdges / MinimizeEdges)
module GraphObjectiveTests =

    let private edgeSelectionProblem objective =
        let nodes = [node "A" 1; node "B" 2; node "C" 3]
        let edges = [edge "A" "B" 5.0; edge "B" "C" 3.0; edge "A" "C" 2.0]
        GraphOptimizationBuilder<int, float>()
            .Nodes(nodes)
            .Edges(edges)
            .Objective(objective)
            .Build()

    [<Fact>]
    let ``toQubo encodes MaximizeEdges as negated edge count`` () =
        let qubo = toQubo (edgeSelectionProblem MaximizeEdges)

        // One variable per edge, diagonal -1 (maximize count = minimize negation)
        Assert.Equal(3, qubo.NumVariables)
        [0 .. 2] |> List.iter (fun e -> Assert.Equal(-1.0, qubo.Q.[(e, e)]))

    [<Fact>]
    let ``toQubo encodes MinimizeEdges as positive edge count`` () =
        let qubo = toQubo (edgeSelectionProblem MinimizeEdges)

        Assert.Equal(3, qubo.NumVariables)
        [0 .. 2] |> List.iter (fun e -> Assert.Equal(1.0, qubo.Q.[(e, e)]))

    [<Fact>]
    let ``decodeSolution decodes edge selection bits`` () =
        let problem = edgeSelectionProblem MaximizeEdges

        // Select first and third edges
        let solution = decodeSolution problem [1; 0; 1]

        match solution.SelectedEdges with
        | None -> Assert.Fail("Expected SelectedEdges to be populated")
        | Some selected ->
            Assert.Equal(2, selected.Length)
            Assert.Equal<float list>([5.0; 2.0], selected |> List.map (fun e -> e.Weight))

    [<Fact>]
    let ``toQubo still rejects unimplemented spanning tree objective`` () =
        Assert.Throws<System.InvalidOperationException>(fun () ->
            toQubo (edgeSelectionProblem MinimizeSpanningTree) |> ignore
        ) |> ignore
