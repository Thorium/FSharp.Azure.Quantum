namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.GroverSearch

module GroverOracleTests =

    // ========================================================================
    // ORACLE SPEC EVALUATION
    // ========================================================================

    [<Fact>]
    let ``isSolution with SingleTarget returns true for matching value`` () =
        Assert.True(Oracle.isSolution (Oracle.OracleSpec.SingleTarget 5) 5)

    [<Fact>]
    let ``isSolution with SingleTarget returns false for non-matching value`` () =
        Assert.False(Oracle.isSolution (Oracle.OracleSpec.SingleTarget 5) 3)

    [<Fact>]
    let ``isSolution with Solutions returns true for values in list`` () =
        let spec = Oracle.OracleSpec.Solutions [2; 5; 7]
        Assert.True(Oracle.isSolution spec 5)
        Assert.False(Oracle.isSolution spec 4)

    [<Fact>]
    let ``isSolution with Predicate delegates correctly`` () =
        let spec = Oracle.OracleSpec.Predicate (fun x -> x % 2 = 0)
        Assert.True(Oracle.isSolution spec 4)
        Assert.False(Oracle.isSolution spec 3)

    [<Fact>]
    let ``isSolution with And requires both conditions`` () =
        let spec = Oracle.OracleSpec.And(Oracle.OracleSpec.Predicate (fun x -> x > 2), Oracle.OracleSpec.Predicate (fun x -> x < 6))
        Assert.True(Oracle.isSolution spec 4)
        Assert.False(Oracle.isSolution spec 1)
        Assert.False(Oracle.isSolution spec 7)

    [<Fact>]
    let ``isSolution with Or requires either condition`` () =
        let spec = Oracle.OracleSpec.Or(Oracle.OracleSpec.SingleTarget 3, Oracle.OracleSpec.SingleTarget 5)
        Assert.True(Oracle.isSolution spec 3)
        Assert.True(Oracle.isSolution spec 5)
        Assert.False(Oracle.isSolution spec 4)

    [<Fact>]
    let ``isSolution with Not inverts condition`` () =
        let spec = Oracle.OracleSpec.Not(Oracle.OracleSpec.SingleTarget 3)
        Assert.False(Oracle.isSolution spec 3)
        Assert.True(Oracle.isSolution spec 4)

    // ========================================================================
    // COMPILE VALIDATION
    // ========================================================================

    [<Fact>]
    let ``compile rejects numQubits = 0`` () =
        let result = Oracle.compile (Oracle.OracleSpec.SingleTarget 0) 0
        match result with
        | Error _ -> ()
        | Ok _ -> failwith "Expected Error for numQubits = 0"

    [<Fact>]
    let ``compile rejects numQubits = 21`` () =
        let result = Oracle.compile (Oracle.OracleSpec.SingleTarget 0) 21
        match result with
        | Error _ -> ()
        | Ok _ -> failwith "Expected Error for numQubits = 21"

    [<Fact>]
    let ``compile succeeds for numQubits = 1`` () =
        let result = Oracle.compile (Oracle.OracleSpec.SingleTarget 0) 1
        match result with
        | Ok oracle ->
            Assert.Equal(1, oracle.NumQubits)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``compile succeeds for numQubits = 20`` () =
        let result = Oracle.compile (Oracle.OracleSpec.SingleTarget 0) 20
        match result with
        | Ok oracle ->
            Assert.Equal(20, oracle.NumQubits)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    // ========================================================================
    // FORVALUE / FORVALUES VALIDATION
    // ========================================================================

    [<Fact>]
    let ``forValue rejects target below 0`` () =
        let result = Oracle.forValue -1 3
        match result with
        | Error _ -> ()
        | Ok _ -> failwith "Expected Error for negative target"

    [<Fact>]
    let ``forValue rejects target >= 2^n`` () =
        let result = Oracle.forValue 8 3  // 2^3 = 8, so 8 is out of range
        match result with
        | Error _ -> ()
        | Ok _ -> failwith "Expected Error for target >= 2^n"

    [<Fact>]
    let ``forValue succeeds for valid target`` () =
        let result = Oracle.forValue 5 3  // 5 < 2^3 = 8
        match result with
        | Ok oracle ->
            Assert.Equal(3, oracle.NumQubits)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``forValues rejects empty list`` () =
        let result = Oracle.forValues [] 3
        match result with
        | Error _ -> ()
        | Ok _ -> failwith "Expected Error for empty list"

    [<Fact>]
    let ``forValues rejects out-of-range values`` () =
        let result = Oracle.forValues [1; 8] 3  // 8 >= 2^3
        match result with
        | Error _ -> ()
        | Ok _ -> failwith "Expected Error for out-of-range value"

    // ========================================================================
    // CONVENIENCE ORACLES
    // ========================================================================

    [<Fact>]
    let ``even oracle marks even numbers correctly`` () =
        match Oracle.even 3 with
        | Ok oracle ->
            Assert.True(Oracle.isSolution oracle.Spec 0)
            Assert.True(Oracle.isSolution oracle.Spec 2)
            Assert.True(Oracle.isSolution oracle.Spec 4)
            Assert.False(Oracle.isSolution oracle.Spec 1)
            Assert.False(Oracle.isSolution oracle.Spec 3)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``odd oracle marks odd numbers correctly`` () =
        match Oracle.odd 3 with
        | Ok oracle ->
            Assert.True(Oracle.isSolution oracle.Spec 1)
            Assert.True(Oracle.isSolution oracle.Spec 3)
            Assert.False(Oracle.isSolution oracle.Spec 0)
            Assert.False(Oracle.isSolution oracle.Spec 2)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``divisibleBy rejects divisor = 0`` () =
        let result = Oracle.divisibleBy 0 3
        match result with
        | Error _ -> ()
        | Ok _ -> failwith "Expected Error for divisor = 0"

    [<Fact>]
    let ``divisibleBy marks correct multiples`` () =
        match Oracle.divisibleBy 3 4 with
        | Ok oracle ->
            Assert.True(Oracle.isSolution oracle.Spec 0)
            Assert.True(Oracle.isSolution oracle.Spec 3)
            Assert.True(Oracle.isSolution oracle.Spec 6)
            Assert.True(Oracle.isSolution oracle.Spec 9)
            Assert.False(Oracle.isSolution oracle.Spec 1)
            Assert.False(Oracle.isSolution oracle.Spec 5)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``inRange rejects min > max`` () =
        let result = Oracle.inRange 5 2 3
        match result with
        | Error _ -> ()
        | Ok _ -> failwith "Expected Error for min > max"

    [<Fact>]
    let ``inRange marks values in range correctly`` () =
        match Oracle.inRange 2 5 4 with
        | Ok oracle ->
            Assert.True(Oracle.isSolution oracle.Spec 2)
            Assert.True(Oracle.isSolution oracle.Spec 3)
            Assert.True(Oracle.isSolution oracle.Spec 4)
            Assert.True(Oracle.isSolution oracle.Spec 5)
            Assert.False(Oracle.isSolution oracle.Spec 1)
            Assert.False(Oracle.isSolution oracle.Spec 6)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    // ========================================================================
    // ORACLE COMBINATORS
    // ========================================================================

    [<Fact>]
    let ``andOracle rejects mismatched qubit counts`` () =
        match Oracle.forValue 0 2, Oracle.forValue 0 3 with
        | Ok o1, Ok o2 ->
            match Oracle.andOracle o1 o2 with
            | Error _ -> ()
            | Ok _ -> failwith "Expected Error for mismatched qubit counts"
        | _ -> failwith "Setup failed"

    [<Fact>]
    let ``orOracle rejects mismatched qubit counts`` () =
        match Oracle.forValue 0 2, Oracle.forValue 0 3 with
        | Ok o1, Ok o2 ->
            match Oracle.orOracle o1 o2 with
            | Error _ -> ()
            | Ok _ -> failwith "Expected Error for mismatched qubit counts"
        | _ -> failwith "Setup failed"

    [<Fact>]
    let ``notOracle inverts solution set`` () =
        match Oracle.forValue 3 3 with
        | Ok oracle ->
            match Oracle.notOracle oracle with
            | Ok notted ->
                Assert.False(Oracle.isSolution notted.Spec 3)
                Assert.True(Oracle.isSolution notted.Spec 0)
            | Error e -> failwith $"notOracle failed: {e}"
        | Error e -> failwith $"Setup failed: {e}"

    // ========================================================================
    // VERIFY AND COUNT
    // ========================================================================

    [<Fact>]
    let ``verify confirms correct solutions`` () =
        match Oracle.forValues [2; 5] 3 with
        | Ok oracle ->
            Assert.True(Oracle.verify oracle [2; 5])
        | Error e -> failwith $"Setup failed: {e}"

    [<Fact>]
    let ``countSolutions returns correct count`` () =
        match Oracle.even 3 with
        | Ok oracle ->
            // 0, 2, 4, 6 are even in 3-qubit range [0..7]
            Assert.Equal(4, Oracle.countSolutions oracle)
        | Error e -> failwith $"Setup failed: {e}"

    [<Fact>]
    let ``listSolutions returns all solutions`` () =
        match Oracle.forValues [1; 3; 5] 3 with
        | Ok oracle ->
            let solutions = Oracle.listSolutions oracle |> List.sort
            Assert.Equal<int list>([1; 3; 5], solutions)
        | Error e -> failwith $"Setup failed: {e}"

    // ========================================================================
    // SAT ORACLES
    // ========================================================================

    [<Fact>]
    let ``satOracle rejects empty clauses`` () =
        let formula : Oracle.SatFormula = { NumVariables = 2; Clauses = [] }
        match Oracle.satOracle formula with
        | Error _ -> ()
        | Ok _ -> failwith "Expected Error for empty clauses"

    [<Fact>]
    let ``satOracle rejects too many variables`` () =
        let formula : Oracle.SatFormula = { NumVariables = 21; Clauses = [Oracle.clause [Oracle.var 0]] }
        match Oracle.satOracle formula with
        | Error _ -> ()
        | Ok _ -> failwith "Expected Error for NumVariables > 20"

    [<Fact>]
    let ``satOracle works for simple formula`` () =
        // (x0 OR x1) - satisfiable by 01, 10, 11
        let formula : Oracle.SatFormula = { NumVariables = 2; Clauses = [Oracle.clause [Oracle.var 0; Oracle.var 1]] }
        match Oracle.satOracle formula with
        | Ok oracle ->
            let count = Oracle.countSolutions oracle
            Assert.True(count >= 1, $"Expected at least 1 solution, got {count}")
        | Error e -> failwith $"satOracle failed: {e}"

    // ========================================================================
    // GRAPH COLORING ORACLES
    // ========================================================================

    [<Fact>]
    let ``graphColoringOracle rejects 0 vertices`` () =
        let config : Oracle.GraphColoringConfig = {
            Graph = Oracle.graph 0 []
            NumColors = 2
        }
        match Oracle.graphColoringOracle config with
        | Error _ -> ()
        | Ok _ -> failwith "Expected Error for 0 vertices"

    [<Fact>]
    let ``graphColoringOracle rejects too few colors`` () =
        let config : Oracle.GraphColoringConfig = {
            Graph = Oracle.graph 3 [(0,1); (1,2)]
            NumColors = 1
        }
        match Oracle.graphColoringOracle config with
        | Error _ -> ()
        | Ok _ -> failwith "Expected Error for NumColors < 2"

    [<Fact>]
    let ``graphColoringOracle succeeds for valid config`` () =
        let config : Oracle.GraphColoringConfig = {
            Graph = Oracle.graph 3 [(0,1); (1,2)]
            NumColors = 2
        }
        match Oracle.graphColoringOracle config with
        | Ok oracle ->
            let count = Oracle.countSolutions oracle
            Assert.True(count >= 1, $"Expected at least 1 valid coloring, got {count}")
        | Error e -> failwith $"graphColoringOracle failed: {e}"

    // ========================================================================
    // CLIQUE ORACLES
    // ========================================================================

    [<Fact>]
    let ``cliqueOracle rejects cliqueSize < 2`` () =
        let config : Oracle.CliqueConfig = {
            Graph = Oracle.graph 4 [(0,1); (1,2); (2,3)]
            CliqueSize = 1
        }
        match Oracle.cliqueOracle config with
        | Error _ -> ()
        | Ok _ -> failwith "Expected Error for CliqueSize < 2"

    [<Fact>]
    let ``cliqueOracle rejects cliqueSize > numVertices`` () =
        let config : Oracle.CliqueConfig = {
            Graph = Oracle.graph 3 [(0,1); (1,2)]
            CliqueSize = 4
        }
        match Oracle.cliqueOracle config with
        | Error _ -> ()
        | Ok _ -> failwith "Expected Error for CliqueSize > numVertices"
