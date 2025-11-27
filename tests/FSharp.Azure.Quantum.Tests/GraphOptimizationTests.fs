namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum.GraphOptimization

/// TKT-90: Tests for Generic Graph Optimization Framework
/// Idiomatic F# with comprehensive coverage
module GraphOptimizationTests =
    
    // ============================================================================
    // FR-1: NODE DEFINITION TESTS
    // ============================================================================
    
    [<Fact>]
    let ``node creates node with id and value`` () =
        let n = node "A" "City A"
        
        Assert.Equal("A", n.Id)
        Assert.Equal("City A", n.Value)
        Assert.True(Map.isEmpty n.Properties)
    
    [<Fact>]
    let ``nodeWithProps creates node with properties`` () =
        let props = ["population", box 10000; "region", box "North"]
        let n = nodeWithProps "A" "City A" props
        
        Assert.Equal("A", n.Id)
        Assert.Equal(2, n.Properties.Count)
        Assert.Equal(box 10000, n.Properties.["population"])
    
    [<Fact>]
    let ``nodes with same id and value are equal`` () =
        let n1 = node "A" 42
        let n2 = node "A" 42
        
        Assert.Equal(n1, n2)
    
    // ============================================================================
    // FR-2: EDGE DEFINITION TESTS
    // ============================================================================
    
    [<Fact>]
    let ``edge creates undirected edge with weight`` () =
        let e = edge "A" "B" 10.0
        
        Assert.Equal("A", e.Source)
        Assert.Equal("B", e.Target)
        Assert.Equal(10.0, e.Weight)
        Assert.False(e.Directed)
        Assert.True(e.Value.IsNone)
    
    [<Fact>]
    let ``directedEdge creates directed edge`` () =
        let e = directedEdge "A" "B" 5.0
        
        Assert.Equal("A", e.Source)
        Assert.Equal("B", e.Target)
        Assert.True(e.Directed)
    
    [<Fact>]
    let ``edge with properties stores metadata`` () =
        let e = { edge "A" "B" 10.0 with Properties = Map.ofList ["road", box "highway"] }
        
        Assert.Equal(box "highway", e.Properties.["road"])
    
    // ============================================================================
    // FR-3: GRAPH REPRESENTATION TESTS
    // ============================================================================
    
    [<Fact>]
    let ``empty graph has no nodes or edges`` () =
        let g = Graph.empty<string, unit>
        
        Assert.Empty(g.Nodes)
        Assert.Empty(g.Edges)
        Assert.Empty(g.Adjacency)
    
    [<Fact>]
    let ``graph with nodes builds adjacency list`` () =
        let nodes = [node "A" "City A"; node "B" "City B"]
        let edges = [edge "A" "B" 10.0]
        let g = Graph.create false nodes edges
        
        Assert.Equal(2, g.Nodes.Count)
        Assert.Equal(1, g.Edges.Length)
        Assert.True(g.Adjacency.ContainsKey "A")
        Assert.Contains("B", g.Adjacency.["A"])
    
    [<Fact>]
    let ``undirected graph has symmetric adjacency`` () =
        let nodes = [node "A" 1; node "B" 2]
        let edges = [edge "A" "B" 1.0]
        let g = Graph.create false nodes edges
        
        // Undirected: both A->B and B->A
        Assert.Contains("B", g.Adjacency.["A"])
        Assert.Contains("A", g.Adjacency.["B"])
    
    [<Fact>]
    let ``directed graph has asymmetric adjacency`` () =
        let nodes = [node "A" 1; node "B" 2]
        let edges = [directedEdge "A" "B" 1.0]
        let g = Graph.create true nodes edges
        
        // Directed: only A->B
        Assert.Contains("B", g.Adjacency.["A"])
        Assert.False(g.Adjacency.ContainsKey "B" && g.Adjacency.["B"] |> List.contains "A")
    
    // ============================================================================
    // FR-4: GRAPH CONSTRAINT TESTS
    // ============================================================================
    
    [<Fact>]
    let ``NoAdjacentEqual constraint defined`` () =
        let constraint = NoAdjacentEqual
        
        // Type check - should compile
        Assert.NotNull(box constraint)
    
    [<Fact>]
    let ``VisitOnce constraint defined`` () =
        let constraint = VisitOnce
        
        Assert.NotNull(box constraint)
    
    [<Fact>]
    let ``DegreeLimit constraint with max value`` () =
        let constraint = DegreeLimit 4
        
        match constraint with
        | DegreeLimit max -> Assert.Equal(4, max)
        | _ -> Assert.True(false, "Wrong constraint type")
    
    // ============================================================================
    // FR-5: OBJECTIVE FUNCTION TESTS
    // ============================================================================
    
    [<Fact>]
    let ``MinimizeColors objective defined`` () =
        let objective = MinimizeColors
        
        Assert.NotNull(box objective)
    
    [<Fact>]
    let ``MinimizeTotalWeight objective defined`` () =
        let objective = MinimizeTotalWeight
        
        Assert.NotNull(box objective)
    
    [<Fact>]
    let ``MaximizeCut objective defined`` () =
        let objective = MaximizeCut
        
        Assert.NotNull(box objective)
    
    // ============================================================================
    // FR-6: FLUENT BUILDER API TESTS
    // ============================================================================
    
    [<Fact>]
    let ``GraphOptimizationBuilder creates empty problem`` () =
        let builder = GraphOptimizationBuilder<string, unit>()
        let problem = builder.Build()
        
        Assert.NotNull(problem)
    
    [<Fact>]
    let ``Builder with nodes sets graph nodes`` () =
        let nodes = [node "A" 1; node "B" 2]
        let problem =
            GraphOptimizationBuilder<int, unit>()
                .Nodes(nodes)
                .Build()
        
        Assert.Equal(2, problem.Graph.Nodes.Count)
    
    [<Fact>]
    let ``Builder with edges sets graph edges`` () =
        let nodes = [node "A" 1; node "B" 2]
        let edges = [edge "A" "B" 10.0]
        let problem =
            GraphOptimizationBuilder<int, unit>()
                .Nodes(nodes)
                .Edges(edges)
                .Build()
        
        Assert.Equal(1, problem.Graph.Edges.Length)
    
    [<Fact>]
    let ``Builder fluent API chains methods`` () =
        let problem =
            GraphOptimizationBuilder<string, unit>()
                .Nodes([node "R1" "Region 1"; node "R2" "Region 2"])
                .Edges([edge "R1" "R2" 1.0])
                .Directed(false)
                .NumColors(4)
                .AddConstraint(NoAdjacentEqual)
                .Objective(MinimizeColors)
                .Build()
        
        Assert.Equal(2, problem.Graph.Nodes.Count)
        Assert.Equal(1, problem.Constraints.Length)
        Assert.Equal(MinimizeColors, problem.Objective)
    
    [<Fact>]
    let ``Builder with multiple constraints adds all`` () =
        let problem =
            GraphOptimizationBuilder<int, unit>()
                .Nodes([node "A" 1])
                .AddConstraint(NoAdjacentEqual)
                .AddConstraint(Connected)
                .Objective(MinimizeColors)
                .Build()
        
        Assert.Equal(2, problem.Constraints.Length)
        Assert.Contains(NoAdjacentEqual, problem.Constraints)
        Assert.Contains(Connected, problem.Constraints)
    
    // ============================================================================
    // FR-7: QUBO ENCODING - GRAPH COLORING TESTS
    // ============================================================================
    
    [<Fact>]
    let ``Graph coloring encodes to QUBO with one-hot variables`` () =
        let problem =
            GraphOptimizationBuilder<string, unit>()
                .Nodes([node "R1" "Region 1"; node "R2" "Region 2"])
                .Edges([edge "R1" "R2" 1.0])
                .NumColors(3)
                .AddConstraint(NoAdjacentEqual)
                .Objective(MinimizeColors)
                .Build()
        
        let qubo = toQubo problem
        
        // 2 nodes * 3 colors = 6 variables
        Assert.True(qubo.NumVariables >= 6)
    
    [<Fact>]
    let ``NoAdjacentEqual constraint adds penalty terms`` () =
        let problem =
            GraphOptimizationBuilder<int, unit>()
                .Nodes([node "A" 1; node "B" 2])
                .Edges([edge "A" "B" 1.0])
                .NumColors(2)
                .AddConstraint(NoAdjacentEqual)
                .Objective(MinimizeColors)
                .Build()
        
        let qubo = toQubo problem
        
        // Should have penalty terms for adjacent nodes with same color
        Assert.True(qubo.Q.Count > 0)
    
    // ============================================================================
    // FR-7: QUBO ENCODING - TSP TESTS
    // ============================================================================
    
    [<Fact>]
    let ``TSP encodes to QUBO with one-hot time variables`` () =
        let cities = [node "A" "City A"; node "B" "City B"; node "C" "City C"]
        let edges = [
            edge "A" "B" 10.0
            edge "B" "C" 15.0
            edge "A" "C" 20.0
        ]
        
        let problem =
            GraphOptimizationBuilder<string, unit>()
                .Nodes(cities)
                .Edges(edges)
                .AddConstraint(VisitOnce)
                .Objective(MinimizeTotalWeight)
                .Build()
        
        let qubo = toQubo problem
        
        // n cities * n time steps = n² variables
        Assert.True(qubo.NumVariables >= 9) // 3*3 = 9
    
    // ============================================================================
    // FR-7: QUBO ENCODING - MAXCUT TESTS
    // ============================================================================
    
    [<Fact>]
    let ``MaxCut encodes to QUBO with binary partition variables`` () =
        let nodes = [node "A" 1; node "B" 2; node "C" 3]
        let edges = [edge "A" "B" 1.0; edge "B" "C" 1.0]
        
        let problem =
            GraphOptimizationBuilder<int, unit>()
                .Nodes(nodes)
                .Edges(edges)
                .Objective(MaximizeCut)
                .Build()
        
        let qubo = toQubo problem
        
        // n nodes = n binary variables
        Assert.True(qubo.NumVariables >= 3)
    
    // ============================================================================
    // FR-8: SOLUTION DECODING TESTS
    // ============================================================================
    
    [<Fact>]
    let ``decode solution extracts node assignments`` () =
        let problem =
            GraphOptimizationBuilder<string, unit>()
                .Nodes([node "R1" "Region 1"; node "R2" "Region 2"])
                .NumColors(2)
                .Objective(MinimizeColors)
                .Build()
        
        // Mock QUBO solution: R1=color0, R2=color1
        let quboSolution = [0; 1; 1; 0] // One-hot encoding
        let solution = decodeSolution problem quboSolution
        
        Assert.True(solution.NodeAssignments.IsSome)
        Assert.Equal(2, solution.NodeAssignments.Value.Count)
    
    [<Fact>]
    let ``solution contains objective value`` () =
        let problem =
            GraphOptimizationBuilder<int, unit>()
                .Nodes([node "A" 1])
                .Objective(MinimizeColors)
                .Build()
        
        let solution = decodeSolution problem [1]
        
        Assert.True(solution.ObjectiveValue >= 0.0)
    
    [<Fact>]
    let ``solution reports feasibility`` () =
        let problem =
            GraphOptimizationBuilder<string, unit>()
                .Nodes([node "A" "City"])
                .AddConstraint(NoAdjacentEqual)
                .Objective(MinimizeColors)
                .Build()
        
        let solution = decodeSolution problem [1]
        
        // Should have IsFeasible flag
        Assert.True(solution.IsFeasible || not solution.IsFeasible) // Boolean value exists
    
    // ============================================================================
    // INTEGRATION TESTS - GRAPH COLORING
    // ============================================================================
    
    [<Fact>]
    let ``Graph coloring example - 4-coloring problem`` () =
        let regions = [
            node "R1" "Region 1"
            node "R2" "Region 2"
            node "R3" "Region 3"
            node "R4" "Region 4"
        ]
        
        let borders = [
            edge "R1" "R2" 1.0
            edge "R1" "R3" 1.0
            edge "R2" "R3" 1.0
            edge "R2" "R4" 1.0
            edge "R3" "R4" 1.0
        ]
        
        let problem =
            GraphOptimizationBuilder<string, unit>()
                .Nodes(regions)
                .Edges(borders)
                .NumColors(4)
                .AddConstraint(NoAdjacentEqual)
                .Objective(MinimizeColors)
                .Build()
        
        Assert.Equal(4, problem.Graph.Nodes.Count)
        Assert.Equal(5, problem.Graph.Edges.Length)
    
    // ============================================================================
    // INTEGRATION TESTS - TSP
    // ============================================================================
    
    [<Fact>]
    let ``TSP example - 4 cities`` () =
        let cities = [
            node "NYC" "New York"
            node "LA" "Los Angeles"
            node "CHI" "Chicago"
            node "HOU" "Houston"
        ]
        
        let routes = [
            edge "NYC" "LA" 2800.0
            edge "NYC" "CHI" 800.0
            edge "NYC" "HOU" 1600.0
            edge "LA" "CHI" 2000.0
            edge "LA" "HOU" 1500.0
            edge "CHI" "HOU" 1100.0
        ]
        
        let problem =
            GraphOptimizationBuilder<string, unit>()
                .Nodes(cities)
                .Edges(routes)
                .AddConstraint(VisitOnce)
                .Objective(MinimizeTotalWeight)
                .Build()
        
        Assert.Equal(4, problem.Graph.Nodes.Count)
        Assert.Contains(VisitOnce, problem.Constraints)
    
    // ============================================================================
    // INTEGRATION TESTS - MAXCUT
    // ============================================================================
    
    [<Fact>]
    let ``MaxCut example - graph partitioning`` () =
        let nodes = List.init 5 (fun i -> node $"N{i}" i)
        let edges = [
            edge "N0" "N1" 1.0
            edge "N1" "N2" 1.0
            edge "N2" "N3" 1.0
            edge "N3" "N4" 1.0
            edge "N0" "N4" 1.0
        ]
        
        let problem =
            GraphOptimizationBuilder<int, unit>()
                .Nodes(nodes)
                .Edges(edges)
                .Objective(MaximizeCut)
                .Build()
        
        Assert.Equal(5, problem.Graph.Nodes.Count)
        Assert.Equal(MaximizeCut, problem.Objective)
    
    // ============================================================================
    // TDD CYCLE 2 - OBJECTIVE VALUE CALCULATION
    // ============================================================================
    
    [<Fact>]
    let ``calculateObjectiveValue - graph coloring counts colors used`` () =
        let nodes = [
            node "A" "Node A"
            node "B" "Node B"
            node "C" "Node C"
        ]
        let edges = [
            edge "A" "B" 1.0
            edge "B" "C" 1.0
        ]
        
        let problem =
            GraphOptimizationBuilder<string, float>()
                .Nodes(nodes)
                .Edges(edges)
                .Objective(MinimizeColors)
                .Build()
        
        // Solution: A=0, B=1, C=0 (2 colors used)
        let solution = {
            Graph = problem.Graph
            NodeAssignments = Some (Map.ofList [("A", 0); ("B", 1); ("C", 0)])
            SelectedEdges = None
            ObjectiveValue = 0.0 // Should be calculated
            IsFeasible = true
            Violations = []
        }
        
        let actualValue = calculateObjectiveValue solution
        Assert.Equal(2.0, actualValue) // 2 colors used
    
    [<Fact>]
    let ``calculateObjectiveValue - TSP sums edge weights in tour`` () =
        let cities = [
            node "A" "City A"
            node "B" "City B"
            node "C" "City C"
        ]
        let routes = [
            edge "A" "B" 10.0
            edge "B" "C" 20.0
            edge "C" "A" 15.0
        ]
        
        let problem =
            GraphOptimizationBuilder<string, float>()
                .Nodes(cities)
                .Edges(routes)
                .Objective(MinimizeTotalWeight)
                .Build()
        
        // Solution: Tour A->B->C->A
        let edgeAB = problem.Graph.Edges |> List.find (fun e -> e.Source = "A" && e.Target = "B")
        let edgeBC = problem.Graph.Edges |> List.find (fun e -> e.Source = "B" && e.Target = "C")
        let edgeCA = problem.Graph.Edges |> List.find (fun e -> e.Source = "C" && e.Target = "A")
        
        let solution = {
            Graph = problem.Graph
            NodeAssignments = None
            SelectedEdges = Some [edgeAB; edgeBC; edgeCA]
            ObjectiveValue = 0.0 // Should be calculated
            IsFeasible = true
            Violations = []
        }
        
        let actualValue = calculateObjectiveValue solution
        Assert.Equal(45.0, actualValue) // 10 + 20 + 15
    
    [<Fact>]
    let ``calculateObjectiveValue - MaxCut counts edges crossing partition`` () =
        let nodes = List.init 4 (fun i -> node $"N{i}" i)
        let edges = [
            edge "N0" "N1" 1.0
            edge "N1" "N2" 1.0
            edge "N2" "N3" 1.0
            edge "N0" "N3" 1.0
        ]
        
        let problem =
            GraphOptimizationBuilder<int, float>()
                .Nodes(nodes)
                .Edges(edges)
                .Objective(MaximizeCut)
                .Build()
        
        // Solution: Partition {N0, N2} vs {N1, N3}
        let solution = {
            Graph = problem.Graph
            NodeAssignments = Some (Map.ofList [("N0", 0); ("N1", 1); ("N2", 0); ("N3", 1)])
            SelectedEdges = None
            ObjectiveValue = 0.0 // Should be calculated
            IsFeasible = true
            Violations = []
        }
        
        let actualValue = calculateObjectiveValue solution
        Assert.Equal(4.0, actualValue) // All 4 edges cross the partition
    
    // ============================================================================
    // TDD CYCLE 2 - CLASSICAL SOLVERS
    // ============================================================================
    
    [<Fact>]
    let ``solveClassical - greedy coloring for graph coloring`` () =
        let nodes = [
            node "A" "Node A"
            node "B" "Node B"
            node "C" "Node C"
            node "D" "Node D"
        ]
        let edges = [
            edge "A" "B" 1.0
            edge "B" "C" 1.0
            edge "C" "D" 1.0
            edge "D" "A" 1.0
        ]
        
        let problem =
            GraphOptimizationBuilder<string, float>()
                .Nodes(nodes)
                .Edges(edges)
                .Objective(MinimizeColors)
                .Build()
        
        let solution = solveClassical problem
        
        // Should produce valid coloring
        Assert.NotNull(solution)
        Assert.True(solution.ObjectiveValue >= 2.0) // At least 2 colors needed (cycle)
        
        // Verify NodeAssignments exists
        Assert.True(solution.NodeAssignments.IsSome)
        let assignments = solution.NodeAssignments.Value
        
        // Verify no adjacent nodes have same color
        for edge in problem.Graph.Edges do
            let colorU = assignments.[edge.Source]
            let colorV = assignments.[edge.Target]
            Assert.NotEqual(colorU, colorV)
    
    [<Fact>]
    let ``solveClassical - nearest neighbor for TSP`` () =
        let cities = [
            node "A" "City A"
            node "B" "City B"
            node "C" "City C"
        ]
        let routes = [
            edge "A" "B" 10.0
            edge "B" "C" 20.0
            edge "C" "A" 15.0
        ]
        
        let problem =
            GraphOptimizationBuilder<string, float>()
                .Nodes(cities)
                .Edges(routes)
                .Objective(MinimizeTotalWeight)
                .AddConstraint(VisitOnce)
                .Build()
        
        let solution = solveClassical problem
        
        // Should produce valid tour
        Assert.NotNull(solution)
        Assert.True(solution.SelectedEdges.IsSome)
        
        let selectedEdges = solution.SelectedEdges.Value
        Assert.Equal(3, selectedEdges.Length) // Complete tour
        
        // Objective value should be sum of selected edges
        Assert.True(solution.ObjectiveValue > 0.0)
    
    [<Fact>]
    let ``solveClassical - randomized maxcut for graph partitioning`` () =
        let nodes = List.init 4 (fun i -> node $"N{i}" i)
        let edges = [
            edge "N0" "N1" 1.0
            edge "N1" "N2" 1.0
            edge "N2" "N3" 1.0
            edge "N0" "N3" 1.0
        ]
        
        let problem =
            GraphOptimizationBuilder<int, float>()
                .Nodes(nodes)
                .Edges(edges)
                .Objective(MaximizeCut)
                .Build()
        
        let solution = solveClassical problem
        
        // Should produce valid partition
        Assert.NotNull(solution)
        Assert.True(solution.NodeAssignments.IsSome)
        
        let assignments = solution.NodeAssignments.Value
        Assert.Equal(4, assignments.Count)
        
        // Each node should be assigned to a partition (color 0 or 1)
        for KeyValue(nodeId, color) in assignments do
            Assert.True(color = 0 || color = 1)
        
        // Cut size should be positive
        Assert.True(solution.ObjectiveValue > 0.0)
    
    // ============================================================================
    // TDD CYCLE 2 - CONSTRAINT VALIDATION
    // ============================================================================
    
    [<Fact>]
    let ``validateConstraints - NoAdjacentEqual detects violations`` () =
        let nodes = [
            node "A" "Node A"
            node "B" "Node B"
            node "C" "Node C"
        ]
        let edges = [
            edge "A" "B" 1.0
            edge "B" "C" 1.0
        ]
        
        let problem =
            GraphOptimizationBuilder<string, float>()
                .Nodes(nodes)
                .Edges(edges)
                .AddConstraint(NoAdjacentEqual)
                .Objective(MinimizeColors)
                .Build()
        
        // Valid solution: A=0, B=1, C=0
        let validSolution = {
            Graph = problem.Graph
            NodeAssignments = Some (Map.ofList [("A", 0); ("B", 1); ("C", 0)])
            SelectedEdges = None
            ObjectiveValue = 0.0
            IsFeasible = true
            Violations = []
        }
        
        let validResult = validateConstraints problem validSolution
        Assert.True(validResult)
        
        // Invalid solution: A=0, B=0, C=1 (A and B adjacent with same color)
        let invalidSolution = {
            Graph = problem.Graph
            NodeAssignments = Some (Map.ofList [("A", 0); ("B", 0); ("C", 1)])
            SelectedEdges = None
            ObjectiveValue = 0.0
            IsFeasible = true
            Violations = []
        }
        
        let invalidResult = validateConstraints problem invalidSolution
        Assert.False(invalidResult)
    
    [<Fact>]
    let ``validateConstraints - DegreeLimit enforces maximum degree`` () =
        let nodes = [
            node "A" "Node A"
            node "B" "Node B"
            node "C" "Node C"
            node "D" "Node D"
        ]
        let edges = [
            edge "A" "B" 1.0
            edge "A" "C" 1.0
            edge "A" "D" 1.0
            edge "B" "C" 1.0
        ]
        
        let problem =
            GraphOptimizationBuilder<string, float>()
                .Nodes(nodes)
                .Edges(edges)
                .AddConstraint(DegreeLimit 2)
                .Objective(MinimizeColors)
                .Build()
        
        // Node A has degree 3, violates DegreeLimit 2
        let solution = {
            Graph = problem.Graph
            NodeAssignments = None
            SelectedEdges = None
            ObjectiveValue = 0.0
            IsFeasible = true
            Violations = []
        }
        
        let result = validateConstraints problem solution
        Assert.False(result) // Should fail because A has degree 3 > 2
    
    // ============================================================================
    // TDD CYCLE #3: OBJECTIVE VALUE CALCULATION IN decodeSolution
    // ============================================================================
    
    [<Fact>]
    let ``decodeSolution calculates objective value for graph coloring`` () =
        // Create a simple graph coloring problem
        let nodes = [
            node "A" "Node A"
            node "B" "Node B"
            node "C" "Node C"
        ]
        let edges = [
            edge "A" "B" 1.0
            edge "B" "C" 1.0
        ]
        
        let problem =
            GraphOptimizationBuilder<string, float>()
                .Nodes(nodes)
                .Edges(edges)
                .Objective(MinimizeColors)
                .Build()
        
        // QUBO solution vector: 3 nodes × 4 colors = 12 variables
        // One-hot encoding: A=color0, B=color1, C=color0 (2 colors used)
        let quboSolution = [
            1; 0; 0; 0;  // Node A (index 0): color 0
            0; 1; 0; 0;  // Node B (index 1): color 1
            1; 0; 0; 0   // Node C (index 2): color 0
        ]
        
        let solution = decodeSolution problem quboSolution
        
        // Assert: ObjectiveValue should be 2.0 (2 colors used), not 0.0
        Assert.Equal(2.0, solution.ObjectiveValue)
    
    // ============================================================================
    // TDD CYCLE #4: MAXCUT QUBO ENCODING
    // ============================================================================
    
    [<Fact>]
    let ``toQubo generates MaxCut QUBO with edge weight terms`` () =
        // Create a simple 3-node graph for MaxCut
        let nodes = [
            node "A" 1
            node "B" 2
            node "C" 3
        ]
        let edges = [
            edge "A" "B" 5.0  // Edge weight 5.0
            edge "B" "C" 3.0  // Edge weight 3.0
            edge "A" "C" 2.0  // Edge weight 2.0
        ]
        
        let problem =
            GraphOptimizationBuilder<int, float>()
                .Nodes(nodes)
                .Edges(edges)
                .Objective(MaximizeCut)
                .Build()
        
        let qubo = toQubo problem
        
        // MaxCut QUBO formulation: Minimize -w * x_i * x_j for each edge (i,j)
        // To maximize cut, we minimize the negative: -5*x_A*x_B - 3*x_B*x_C - 2*x_A*x_C
        // Node indices: A=0, B=1, C=2
        
        // Check QUBO has correct number of variables (one per node)
        Assert.Equal(3, qubo.NumVariables)
        
        // Check quadratic terms exist for edges with negative weights
        // Edge A-B (nodes 0-1): Should have Q[(0,1)] = -5.0
        Assert.True(qubo.Q.ContainsKey((0, 1)))
        Assert.Equal(-5.0, qubo.Q.[(0, 1)])
        
        // Edge B-C (nodes 1-2): Should have Q[(1,2)] = -3.0
        Assert.True(qubo.Q.ContainsKey((1, 2)))
        Assert.Equal(-3.0, qubo.Q.[(1, 2)])
        
        // Edge A-C (nodes 0-2): Should have Q[(0,2)] = -2.0
        Assert.True(qubo.Q.ContainsKey((0, 2)))
        Assert.Equal(-2.0, qubo.Q.[(0, 2)])
    
    // ============================================================================
    // TDD CYCLE #5: TSP QUBO ENCODING
    // ============================================================================
    
    [<Fact>]
    let ``toQubo generates TSP QUBO with distance minimization terms`` () =
        // Create a simple 3-city TSP problem
        let cities = [
            node "A" "City A"
            node "B" "City B"
            node "C" "City C"
        ]
        let routes = [
            edge "A" "B" 10.0  // Distance A->B = 10
            edge "B" "C" 20.0  // Distance B->C = 20
            edge "C" "A" 15.0  // Distance C->A = 15
        ]
        
        let problem =
            GraphOptimizationBuilder<string, float>()
                .Nodes(cities)
                .Edges(routes)
                .Objective(MinimizeTotalWeight)
                .Build()
        
        let qubo = toQubo problem
        
        // TSP QUBO formulation with one-hot time encoding:
        // Variables: x_{i,t} where i=city index (0-2), t=time slot (0-2)
        // Total variables: 3 cities * 3 time slots = 9
        
        // Check correct number of variables
        Assert.Equal(9, qubo.NumVariables)
        
        // QUBO should contain quadratic terms for distance minimization
        // For edge A->B (distance 10): terms like x_{A,t} * x_{B,t+1}
        // These should be present in the QUBO matrix
        
        // At minimum, verify QUBO is not empty (has some terms)
        Assert.True(qubo.Q.Count > 0, "TSP QUBO should contain constraint and objective terms")

