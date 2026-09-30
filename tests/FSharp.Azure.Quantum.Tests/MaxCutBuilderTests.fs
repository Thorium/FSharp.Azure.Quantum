namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum

/// Tests for the MaxCut graph construction helpers.
module MaxCutBuilderTests =

    /// Undirected edge key, independent of endpoint order.
    let private edgeKey (e: GraphOptimization.Edge<float>) =
        if e.Source < e.Target then
            (e.Source, e.Target)
        else
            (e.Target, e.Source)

    [<Theory>]
    [<InlineData(1, 1)>]
    [<InlineData(1, 4)>]
    [<InlineData(4, 1)>]
    [<InlineData(2, 3)>]
    [<InlineData(3, 3)>]
    [<InlineData(3, 5)>]
    let ``gridGraph has rows*(cols-1) + (rows-1)*cols distinct edges`` (rows: int, cols: int) =
        let grid = MaxCut.gridGraph rows cols 1.0
        let expected = rows * (cols - 1) + (rows - 1) * cols

        Assert.Equal(rows * cols, grid.VertexCount)
        Assert.Equal(expected, grid.EdgeCount)
        Assert.Equal(expected, grid.Edges.Length)
        Assert.Equal(expected, grid.Edges |> List.map edgeKey |> List.distinct |> List.length)

    [<Fact>]
    let ``gridGraph 2x3 has 7 edges and 3x3 has 12`` () =
        Assert.Equal(7, (MaxCut.gridGraph 2 3 1.0).EdgeCount)
        Assert.Equal(12, (MaxCut.gridGraph 3 3 1.0).EdgeCount)

    [<Fact>]
    let ``gridGraph connects only orthogonal neighbours`` () =
        let grid = MaxCut.gridGraph 3 3 1.0
        let vertices = set grid.Vertices

        let coords (name: string) =
            let c = name.IndexOf 'C'
            int name.[1 .. c - 1], int name.[c + 1 ..]

        for e in grid.Edges do
            Assert.Contains(e.Source, vertices)
            Assert.Contains(e.Target, vertices)
            let (r1, c1), (r2, c2) = coords e.Source, coords e.Target
            Assert.Equal(1, abs (r1 - r2) + abs (c1 - c2))
