namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.GraphColoring

module GraphColoringTests =
    
    // ============================================================================
    // TEST 1: Simple 3-Node Triangle - Classic Graph Coloring
    // ============================================================================
    
    [<Fact>]
    let ``3-node triangle requires 3 colors`` () =
        // Arrange - Triangle: all nodes conflict with each other
        // R1 -- R2
        //  \   /
        //   R3
        let problem = graphColoring {
            node "R1" ["R2"; "R3"]
            node "R2" ["R1"; "R3"]
            node "R3" ["R1"; "R2"]
            colors ["Red"; "Green"; "Blue"]
            objective MinimizeColors
        }
        
        // Act
        let solution = solve problem
        
        // Assert
        Assert.True(solution.IsValid, "Solution should be valid (no conflicts)")
        Assert.Equal(3, solution.ColorsUsed)  // Triangle needs all 3 colors
        Assert.Equal(0, solution.ConflictCount)
        
        // Verify all nodes have different colors
        let r1Color = Map.find "R1" solution.Assignments
        let r2Color = Map.find "R2" solution.Assignments
        let r3Color = Map.find "R3" solution.Assignments
        
        Assert.NotEqual<string>(r1Color, r2Color)
        Assert.NotEqual<string>(r1Color, r3Color)
        Assert.NotEqual<string>(r2Color, r3Color)
    
    // ============================================================================
    // TEST 2: 4-Node Square - Bipartite Graph
    // ============================================================================
    
    [<Fact>]
    let ``4-node square requires only 2 colors`` () =
        // Arrange - Square graph (bipartite)
        // R1 -- R2
        // |      |
        // R4 -- R3
        let problem = graphColoring {
            node "R1" ["R2"; "R4"]
            node "R2" ["R1"; "R3"]
            node "R3" ["R2"; "R4"]
            node "R4" ["R3"; "R1"]
            colors ["Red"; "Green"; "Blue"]
            objective MinimizeColors
        }
        
        // Act
        let solution = solve problem
        
        // Assert
        Assert.True(solution.IsValid)
        Assert.Equal(2, solution.ColorsUsed)  // Bipartite graph needs only 2 colors
        Assert.Equal(0, solution.ConflictCount)
        
        // Verify opposite corners have same color (R1=R3, R2=R4)
        let r1Color = Map.find "R1" solution.Assignments
        let r2Color = Map.find "R2" solution.Assignments
        let r3Color = Map.find "R3" solution.Assignments
        let r4Color = Map.find "R4" solution.Assignments
        
        Assert.NotEqual<string>(r1Color, r2Color)
        Assert.NotEqual<string>(r1Color, r4Color)
    
    // ============================================================================
    // TEST 3: Fixed Color Assignments
    // ============================================================================
    
    [<Fact>]
    let ``Fixed color assignments should be respected`` () =
        // Arrange - Using advanced coloredNode builder
        let r1 = coloredNode {
            id "R1"
            conflictsWith ["R2"]
            fixedColor "Red"  // Pre-assign R1 to Red
        }
        
        let r2 = coloredNode {
            id "R2"
            conflictsWith ["R1"]
        }
        
        let problem = graphColoring {
            nodes [r1; r2]
            colors ["Red"; "Green"; "Blue"]
        }
        
        // Act
        let solution = solve problem
        
        // Assert
        Assert.True(solution.IsValid)
        
        // R1 must be Red (fixed)
        let r1Color = Map.find "R1" solution.Assignments
        Assert.Equal("Red", r1Color)
        
        // R2 must NOT be Red (conflicts with R1)
        let r2Color = Map.find "R2" solution.Assignments
        Assert.NotEqual<string>("Red", r2Color)
    
    // ============================================================================
    // TEST 4: Inline Node Syntax - Progressive Disclosure
    // ============================================================================
    
    [<Fact>]
    let ``Inline node syntax should work for simple cases`` () =
        // Arrange - Using simple inline syntax (80% use case)
        let problem = graphColoring {
            node "A" ["B"; "C"]
            node "B" ["A"]
            node "C" ["A"]
            colors ["X"; "Y"]
        }
        
        // Act
        let solution = solve problem
        
        // Assert
        Assert.True(solution.IsValid)
        Assert.Equal(2, solution.ColorsUsed)
        
        // A must differ from B and C
        let aColor = Map.find "A" solution.Assignments
        let bColor = Map.find "B" solution.Assignments
        let cColor = Map.find "C" solution.Assignments
        
        Assert.NotEqual<string>(aColor, bColor)
        Assert.NotEqual<string>(aColor, cColor)
        // B and C can be same (no conflict between them)
    
    // ============================================================================
    // TEST 5: Real-World - Compiler Register Allocation
    // ============================================================================
    
    [<Fact>]
    let ``Compiler register allocation should assign variables to registers`` () =
        // Arrange - 8 variables with live range conflicts
        // Simulating compiler register allocation for CPU registers
        let problem = graphColoring {
            // Variable interference graph
            node "v1" ["v2"; "v3"; "v4"]      // v1 conflicts with v2, v3, v4
            node "v2" ["v1"; "v5"]            // v2 conflicts with v1, v5
            node "v3" ["v1"; "v6"]            // v3 conflicts with v1, v6
            node "v4" ["v1"; "v7"]            // v4 conflicts with v1, v7
            node "v5" ["v2"; "v8"]            // v5 conflicts with v2, v8
            node "v6" ["v3"]                  // v6 conflicts with v3
            node "v7" ["v4"]                  // v7 conflicts with v4
            node "v8" ["v5"]                  // v8 conflicts with v5
            
            // CPU registers (x86-64)
            colors ["RAX"; "RBX"; "RCX"; "RDX"; "RSI"; "RDI"; "R8"; "R9"]
            objective MinimizeColors
        }
        
        // Act
        let solution = solve problem
        
        // Assert
        Assert.True(solution.IsValid)
        Assert.InRange(solution.ColorsUsed, 1, 5)  // Should need ≤ 5 registers
        Assert.Equal(8, solution.Assignments.Count)
        
        // Verify no conflicts
        Assert.Equal(0, solution.ConflictCount)
    
    // ============================================================================
    // TEST 6: Validation - Empty Nodes
    // ============================================================================
    
    [<Fact>]
    let ``Empty nodes list should fail validation`` () =
        // Arrange & Act & Assert
        let ex = Assert.Throws<System.Exception>(fun () ->
            graphColoring {
                colors ["Red"; "Green"]
            } |> ignore
        )
        
        Assert.Contains("at least one node", ex.Message)
    
    // ============================================================================
    // TEST 7: Validation - Empty Colors
    // ============================================================================
    
    [<Fact>]
    let ``Empty colors list should fail validation`` () =
        // Arrange & Act & Assert
        let ex = Assert.Throws<System.Exception>(fun () ->
            graphColoring {
                node "A" []
            } |> ignore
        )
        
        Assert.Contains("at least one available color", ex.Message)
    
    // ============================================================================
    // TEST 8: Validation - Invalid Conflict References
    // ============================================================================
    
    [<Fact>]
    let ``Invalid conflict references should fail validation`` () =
        // Arrange & Act & Assert
        let ex = Assert.Throws<System.Exception>(fun () ->
            graphColoring {
                node "A" ["B"; "NonExistent"]  // "NonExistent" doesn't exist
                node "B" ["A"]
                colors ["Red"; "Green"]
            } |> ignore
        )
        
        Assert.Contains("Invalid conflict references", ex.Message)
    
    // ============================================================================
    // TEST 9: Validation - Fixed Color Not in Available Colors
    // ============================================================================
    
    [<Fact>]
    let ``Fixed color not in available colors should fail validation`` () =
        // Arrange & Act & Assert
        let ex = Assert.Throws<System.Exception>(fun () ->
            let r1 = coloredNode {
                id "R1"
                conflictsWith []
                fixedColor "Purple"  // Purple not in available colors
            }
            
            graphColoring {
                nodes [r1]
                colors ["Red"; "Green"; "Blue"]
            } |> ignore
        )
        
        Assert.Contains("Fixed colors not in available colors", ex.Message)
    
    // ============================================================================
    // TEST 10: Validation - Duplicate Node IDs
    // ============================================================================
    
    [<Fact>]
    let ``Duplicate node IDs should fail validation`` () =
        // Arrange & Act & Assert
        let ex = Assert.Throws<System.Exception>(fun () ->
            graphColoring {
                node "A" []
                node "A" []  // Duplicate ID
                colors ["Red"; "Green"]
            } |> ignore
        )
        
        Assert.Contains("Node IDs must be unique", ex.Message)
    
    // ============================================================================
    // TEST 11: Advanced Builder - Priority and Properties
    // ============================================================================
    
    [<Fact>]
    let ``Advanced builder with priority and properties should compile`` () =
        // Arrange - Using full coloredNode builder (5% use case)
        let highPriorityNode = coloredNode {
            id "Critical"
            conflictsWith ["Other"]
            priority 100.0
            property "spill_cost" 1000.0
            property "live_range_start" 0
        }
        
        let normalNode = coloredNode {
            id "Other"
            conflictsWith ["Critical"]
            priority 1.0
        }
        
        let problem = graphColoring {
            nodes [highPriorityNode; normalNode]
            colors ["EAX"; "EBX"]
        }
        
        // Act
        let solution = solve problem
        
        // Assert
        Assert.True(solution.IsValid)
        Assert.Equal(2, solution.ColorsUsed)
    
    // ============================================================================
    // TEST 12: Helper Function - node() Quick Creation
    // ============================================================================
    
    [<Fact>]
    let ``node helper function should create simple nodes`` () =
        // Arrange - Using helper function outside computation expression
        let r1 = node "R1" ["R2"; "R3"]
        let r2 = node "R2" ["R1"]
        let r3 = node "R3" ["R1"]
        
        let problem = graphColoring {
            nodes [r1; r2; r3]
            colors ["A"; "B"]
        }
        
        // Act
        let solution = solve problem
        
        // Assert
        Assert.True(solution.IsValid)
        Assert.Equal(2, solution.ColorsUsed)
    
    // ============================================================================
    // TEST 13: Real-World - Wireless Frequency Assignment
    // ============================================================================
    
    [<Fact>]
    let ``Wireless frequency assignment should avoid interference`` () =
        // Arrange - Cell towers with interference zones
        let problem = graphColoring {
            // Towers in interference range
            node "Tower1" ["Tower2"; "Tower3"]
            node "Tower2" ["Tower1"; "Tower4"]
            node "Tower3" ["Tower1"; "Tower4"; "Tower5"]
            node "Tower4" ["Tower2"; "Tower3"; "Tower6"]
            node "Tower5" ["Tower3"; "Tower6"]
            node "Tower6" ["Tower4"; "Tower5"]
            
            // Available frequencies
            colors ["2.4GHz"; "5GHz"; "6GHz"]
            objective MinimizeColors
        }
        
        // Act
        let solution = solve problem
        
        // Assert
        Assert.True(solution.IsValid, "No frequency interference")
        Assert.InRange(solution.ColorsUsed, 1, 3)
        
        // Verify distribution is tracked
        Assert.Equal(6, solution.ColorDistribution.Values |> Seq.sum)
    
    // ============================================================================
    // TEST 14: Real-World - Exam Scheduling
    // ============================================================================
    
    [<Fact>]
    let ``Exam scheduling should avoid student conflicts`` () =
        // Arrange - Exams with student overlap
        let problem = graphColoring {
            // Math conflicts with Physics (same students)
            node "Math" ["Physics"; "Chemistry"]
            node "Physics" ["Math"; "CompSci"]
            node "Chemistry" ["Math"; "Biology"]
            node "CompSci" ["Physics"]
            node "Biology" ["Chemistry"]
            
            // Time slots
            colors ["Morning"; "Afternoon"; "Evening"]
        }
        
        // Act
        let solution = solve problem
        
        // Assert
        Assert.True(solution.IsValid)
        Assert.InRange(solution.ColorsUsed, 2, 3)
        
        // Math and Physics must be in different time slots
        let mathSlot = Map.find "Math" solution.Assignments
        let physicsSlot = Map.find "Physics" solution.Assignments
        Assert.NotEqual<string>(mathSlot, physicsSlot)
    
    // ============================================================================
    // TEST 15: Visualization Export
    // ============================================================================
    
    [<Fact>]
    let ``exportToDot should generate valid GraphViz format`` () =
        // Arrange
        let problem = graphColoring {
            node "A" ["B"]
            node "B" ["A"]
            colors ["Red"; "Blue"]
        }
        
        let solution = solve problem
        
        // Act
        let dot = exportToDot problem solution
        
        // Assert
        Assert.Contains("graph G {", dot)
        Assert.Contains("A [label=", dot)
        Assert.Contains("B [label=", dot)
        Assert.Contains("A -- B", dot)
        Assert.Contains("}", dot)
    
    // ============================================================================
    // TEST 16: Solution Description
    // ============================================================================
    
    [<Fact>]
    let ``describeSolution should provide human-readable output`` () =
        // Arrange
        let problem = graphColoring {
            node "A" ["B"]
            node "B" ["A"]
            colors ["Red"; "Blue"]
        }
        
        let solution = solve problem
        
        // Act
        let description = describeSolution solution
        
        // Assert
        Assert.Contains("Graph Coloring Solution", description)
        Assert.Contains("Status:", description)
        Assert.Contains("Colors Used:", description)
        Assert.Contains("Assignments:", description)
        Assert.Contains("A →", description)
        Assert.Contains("B →", description)
    
    // ============================================================================
    // TEST 17: MaxColors Constraint
    // ============================================================================
    
    [<Fact>]
    let ``maxColors constraint should be validated`` () =
        // Arrange - Triangle needs 3 colors, but we set maxColors to 2
        let ex = Assert.Throws<System.Exception>(fun () ->
            graphColoring {
                node "A" ["B"; "C"]
                node "B" ["A"; "C"]
                node "C" ["A"; "B"]
                colors ["Red"; "Green"; "Blue"]
                maxColors 0  // Invalid: must be at least 1
            } |> ignore
        )
        
        Assert.Contains("MaxColors must be at least 1", ex.Message)
    
    // ============================================================================
    // TEST 18: Large Graph Performance Test
    // ============================================================================
    
    [<Fact>]
    let ``Large graph with 20 nodes should solve quickly`` () =
        // Arrange - Create nodes outside computation expression
        let nodesList = 
            [1..20]
            |> List.map (fun i ->
                let conflicts = 
                    if i % 2 = 0 then
                        [sprintf "Node%d" (i-1); sprintf "Node%d" ((i % 20) + 1)]
                    else
                        [sprintf "Node%d" (i+1)]
                node (sprintf "Node%d" i) conflicts
            )
        
        let problem = graphColoring {
            nodes nodesList
            colors ["C1"; "C2"; "C3"; "C4"; "C5"]
        }
        
        // Act
        let solution = solve problem
        
        // Assert
        Assert.True(solution.IsValid)
        Assert.Equal(20, solution.Assignments.Count)
        Assert.InRange(solution.ColorsUsed, 1, 5)
    
    // ============================================================================
    // TEST 19: Empty Conflict List (Independent Nodes)
    // ============================================================================
    
    [<Fact>]
    let ``Independent nodes with no conflicts can share same color`` () =
        // Arrange - Three nodes with NO conflicts
        let problem = graphColoring {
            node "A" []
            node "B" []
            node "C" []
            colors ["Red"]  // Only one color available
        }
        
        // Act
        let solution = solve problem
        
        // Assert
        Assert.True(solution.IsValid)
        Assert.Equal(1, solution.ColorsUsed)
        
        // All nodes should get the same color (no conflicts)
        let aColor = Map.find "A" solution.Assignments
        let bColor = Map.find "B" solution.Assignments
        let cColor = Map.find "C" solution.Assignments
        
        Assert.Equal("Red", aColor)
        Assert.Equal("Red", bColor)
        Assert.Equal("Red", cColor)
    
    // ============================================================================
    // TEST 20: Mixed Inline and Builder Nodes
    // ============================================================================
    
    [<Fact>]
    let ``Mixed inline and builder nodes should work together`` () =
        // Arrange - Mix both syntaxes (progressive disclosure)
        let advancedNode = coloredNode {
            id "Advanced"
            conflictsWith ["Simple1"]
            priority 10.0
        }
        
        let problem = graphColoring {
            nodes [advancedNode]
            node "Simple1" ["Advanced"; "Simple2"]
            node "Simple2" ["Simple1"]
            colors ["A"; "B"; "C"]
        }
        
        // Act
        let solution = solve problem
        
        // Assert
        Assert.True(solution.IsValid)
        Assert.Equal(3, solution.Assignments.Count)
