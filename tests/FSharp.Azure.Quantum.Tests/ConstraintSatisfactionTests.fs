namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.ConstraintSatisfaction

/// TKT-92: Constraint Satisfaction Framework Tests
/// Following TDD approach with comprehensive coverage
module ConstraintSatisfactionTests =
    
    // ========================================================================
    // FR-1: VARIABLE DEFINITION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``variable creates variable with name and domain`` () =
        let var = variable "x" [1; 2; 3]
        Assert.Equal("x", var.Name)
        Assert.Equal<int list>([1; 2; 3], var.Domain)
        Assert.True(Map.isEmpty var.Properties)
    
    [<Fact>]
    let ``variables with same name and domain are equal`` () =
        let var1 = variable "x" [1; 2]
        let var2 = variable "x" [1; 2]
        Assert.Equal(var1, var2)
    
    [<Fact>]
    let ``variable with properties stores metadata`` () =
        let var = variableWithProps "x" [1; 2; 3] [("color", box "red")]
        Assert.Equal("red", var.Properties.["color"] :?> string)
    
    // ========================================================================
    // FR-2: CONSTRAINT TYPES TESTS
    // ========================================================================
    
    [<Fact>]
    let ``AllDifferent constraint defined`` () =
        let constr = AllDifferent ["x"; "y"; "z"]
        match constr with
        | AllDifferent vars -> Assert.Equal(3, vars.Length)
        | _ -> Assert.True(false, "Expected AllDifferent constraint")
    
    [<Fact>]
    let ``Binary constraint with predicate defined`` () =
        let constr = Binary ("x", "y", fun (x: int, y: int) -> x < y)
        match constr with
        | Binary (v1, v2, _) -> 
            Assert.Equal("x", v1)
            Assert.Equal("y", v2)
        | _ -> Assert.True(false, "Expected Binary constraint")
    
    [<Fact>]
    let ``Custom constraint with predicate defined`` () =
        let constr = Custom (fun (assignments: Map<string, int>) -> 
            assignments.Values |> Seq.sum > 10
        )
        match constr with
        | Custom _ -> Assert.True(true)
        | _ -> Assert.True(false, "Expected Custom constraint")
    
    // ========================================================================
    // FR-3: FLUENT BUILDER API TESTS
    // ========================================================================
    
    [<Fact>]
    let ``ConstraintSatisfactionBuilder creates empty problem`` () =
        let builder = ConstraintSatisfactionBuilder<int>()
        let problem = builder.Build()
        Assert.Empty(problem.Variables)
        Assert.Empty(problem.Constraints)
    
    [<Fact>]
    let ``Builder with variables sets problem variables`` () =
        let vars = [variable "x" [1; 2]; variable "y" [1; 2]]
        let problem = 
            ConstraintSatisfactionBuilder<int>()
                .Variables(vars)
                .Build()
        Assert.Equal(2, problem.Variables.Length)
    
    [<Fact>]
    let ``Builder with constraints adds all constraints`` () =
        let vars = [variable "x" [1; 2]; variable "y" [1; 2]]
        let problem =
            ConstraintSatisfactionBuilder<int>()
                .Variables(vars)
                .AddConstraint(AllDifferent ["x"; "y"])
                .AddConstraint(Binary ("x", "y", fun (x, y) -> x < y))
                .Build()
        Assert.Equal(2, problem.Constraints.Length)
    
    [<Fact>]
    let ``Builder fluent API chains methods`` () =
        let problem =
            ConstraintSatisfactionBuilder<int>()
                .Variables([variable "x" [1; 2; 3]])
                .AddConstraint(AllDifferent ["x"])
                .Build()
        Assert.Single(problem.Variables) |> ignore
        Assert.Single(problem.Constraints) |> ignore
    
    // ========================================================================
    // FR-4: QUBO ENCODING TESTS
    // ========================================================================
    
    [<Fact>]
    let ``toQubo encodes simple CSP to QUBO`` () =
        let problem =
            ConstraintSatisfactionBuilder<int>()
                .Variables([
                    variable "x" [0; 1]
                    variable "y" [0; 1]
                ])
                .Build()
        
        let qubo = toQubo problem
        // 2 variables * 2 domain values = 4 binary variables
        Assert.Equal(4, qubo.NumVariables)
    
    [<Fact>]
    let ``toQubo encodes AllDifferent constraint`` () =
        let problem =
            ConstraintSatisfactionBuilder<int>()
                .Variables([
                    variable "x" [0; 1; 2]
                    variable "y" [0; 1; 2]
                ])
                .AddConstraint(AllDifferent ["x"; "y"])
                .Build()
        
        let qubo = toQubo problem
        // Should have penalty terms for same value assignments
        Assert.True(qubo.Q.Count > 0)
    
    [<Fact>]
    let ``toQubo encodes Binary constraint`` () =
        let problem =
            ConstraintSatisfactionBuilder<int>()
                .Variables([
                    variable "x" [1; 2]
                    variable "y" [1; 2]
                ])
                .AddConstraint(Binary ("x", "y", fun (x, y) -> x < y))
                .Build()
        
        let qubo = toQubo problem
        Assert.True(qubo.Q.Count > 0)
    
    // ========================================================================
    // FR-5: SOLUTION DECODING TESTS
    // ========================================================================
    
    [<Fact>]
    let ``decodeSolution extracts variable assignments`` () =
        let problem =
            ConstraintSatisfactionBuilder<int>()
                .Variables([
                    variable "x" [0; 1]
                    variable "y" [0; 1]
                ])
                .Build()
        
        // QUBO solution: [1, 0, 0, 1] means x=0, y=1
        let solution = decodeSolution problem [1; 0; 0; 1]
        
        Assert.True(solution.Assignments.IsSome)
        Assert.Equal(0, solution.Assignments.Value.["x"])
        Assert.Equal(1, solution.Assignments.Value.["y"])
    
    [<Fact>]
    let ``solution reports feasibility`` () =
        let problem =
            ConstraintSatisfactionBuilder<int>()
                .Variables([variable "x" [1; 2]])
                .Build()
        
        let solution = decodeSolution problem [1; 0]
        Assert.True(solution.IsFeasible)
    
    [<Fact>]
    let ``solution validates AllDifferent constraint`` () =
        let problem =
            ConstraintSatisfactionBuilder<int>()
                .Variables([
                    variable "x" [1; 2]
                    variable "y" [1; 2]
                ])
                .AddConstraint(AllDifferent ["x"; "y"])
                .Build()
        
        // Valid: x=1, y=2 (different)
        let validSolution = decodeSolution problem [1; 0; 0; 1]
        Assert.True(validSolution.IsFeasible)
        Assert.Empty(validSolution.Violations)
        
        // Invalid: x=1, y=1 (same)
        let invalidSolution = decodeSolution problem [1; 0; 1; 0]
        Assert.False(invalidSolution.IsFeasible)
        Assert.NotEmpty(invalidSolution.Violations)
    
    // ========================================================================
    // FR-6: CLASSICAL SOLVER TESTS
    // ========================================================================
    
    [<Fact>]
    let ``solveClassical uses backtracking for simple CSP`` () =
        let problem =
            ConstraintSatisfactionBuilder<int>()
                .Variables([
                    variable "x" [1; 2; 3]
                    variable "y" [1; 2; 3]
                ])
                .AddConstraint(AllDifferent ["x"; "y"])
                .Build()
        
        let solution = solveClassical problem
        Assert.True(solution.IsFeasible)
        Assert.NotEqual(solution.Assignments.Value.["x"], solution.Assignments.Value.["y"])
    
    // ========================================================================
    // INTEGRATION TESTS: N-QUEENS
    // ========================================================================
    
    [<Fact>]
    let ``N-Queens 4x4 solves correctly`` () =
        let queens = [0..3] |> List.map (fun i -> variable $"Q{i}" [0; 1; 2; 3])
        
        let noDiagonalAttacks (assignments: Map<string, int>) =
            let positions = assignments |> Map.toList |> List.map snd
            let rows = [0..3]
            
            // Check no two queens attack diagonally
            rows
            |> List.allPairs rows
            |> List.filter (fun (r1, r2) -> r1 < r2)
            |> List.forall (fun (r1, r2) ->
                let c1 = positions.[r1]
                let c2 = positions.[r2]
                abs (r1 - r2) <> abs (c1 - c2)
            )
        
        let problem =
            ConstraintSatisfactionBuilder<int>()
                .Variables(queens)
                .AddConstraint(AllDifferent ["Q0"; "Q1"; "Q2"; "Q3"])
                .AddConstraint(Custom noDiagonalAttacks)
                .Build()
        
        let solution = solveClassical problem
        Assert.True(solution.IsFeasible, "N-Queens 4x4 should have a solution")
        Assert.Equal(4, solution.Assignments.Value.Count)
    
    // ========================================================================
    // INTEGRATION TESTS: MAP COLORING
    // ========================================================================
    
    [<Fact>]
    let ``Map coloring with 3 colors solves correctly`` () =
        // Simple map: A-B-C (linear)
        let regions = ["A"; "B"; "C"] |> List.map (fun r -> variable r [0; 1; 2])
        
        let problem =
            ConstraintSatisfactionBuilder<int>()
                .Variables(regions)
                .AddConstraint(Binary ("A", "B", fun (a, b) -> a <> b))
                .AddConstraint(Binary ("B", "C", fun (b, c) -> b <> c))
                .Build()
        
        let solution = solveClassical problem
        Assert.True(solution.IsFeasible)
        Assert.NotEqual(solution.Assignments.Value.["A"], solution.Assignments.Value.["B"])
        Assert.NotEqual(solution.Assignments.Value.["B"], solution.Assignments.Value.["C"])
