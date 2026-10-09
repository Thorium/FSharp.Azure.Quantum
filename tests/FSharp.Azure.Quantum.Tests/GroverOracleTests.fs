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
        let spec = Oracle.OracleSpec.Solutions [ 2; 5; 7 ]
        Assert.True(Oracle.isSolution spec 5)
        Assert.False(Oracle.isSolution spec 4)

    [<Fact>]
    let ``isSolution with Predicate delegates correctly`` () =
        let spec = Oracle.OracleSpec.Predicate(fun x -> x % 2 = 0)
        Assert.True(Oracle.isSolution spec 4)
        Assert.False(Oracle.isSolution spec 3)

    [<Fact>]
    let ``isSolution with And requires both conditions`` () =
        let spec =
            Oracle.OracleSpec.And(
                Oracle.OracleSpec.Predicate(fun x -> x > 2),
                Oracle.OracleSpec.Predicate(fun x -> x < 6)
            )

        Assert.True(Oracle.isSolution spec 4)
        Assert.False(Oracle.isSolution spec 1)
        Assert.False(Oracle.isSolution spec 7)

    [<Fact>]
    let ``isSolution with Or requires either condition`` () =
        let spec =
            Oracle.OracleSpec.Or(Oracle.OracleSpec.SingleTarget 3, Oracle.OracleSpec.SingleTarget 5)

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
        result |> Result.iter (fun _ -> failwith "Expected Error for numQubits = 0")

    [<Fact>]
    let ``compile rejects numQubits beyond the Int32 index range`` () =
        let result =
            Oracle.compile (Oracle.OracleSpec.SingleTarget 0) (Oracle.MaxIndexQubits + 1)

        result
        |> Result.iter (fun _ -> failwith $"Expected Error for numQubits = {Oracle.MaxIndexQubits + 1}")

    [<Fact>]
    let ``compile succeeds for numQubits = 1`` () =
        let result = Oracle.compile (Oracle.OracleSpec.SingleTarget 0) 1

        result
        |> Result.map (fun oracle -> Assert.Equal(1, oracle.NumQubits))
        |> Result.defaultWith (fun e -> failwith $"Expected Ok, got Error: {e}")

    // Compiling allocates no state, so widths past any simulator are safe to compile here;
    // the backend's limit is Grover.search's to check.
    [<Theory; InlineData(20); InlineData(21); InlineData(30)>]
    let ``compile succeeds for any width up to MaxIndexQubits`` (numQubits: int) =
        let result = Oracle.compile (Oracle.OracleSpec.SingleTarget 0) numQubits

        result
        |> Result.map (fun oracle -> Assert.Equal(numQubits, oracle.NumQubits))
        |> Result.defaultWith (fun e -> failwith $"Expected Ok, got Error: {e}")

    // ========================================================================
    // FORVALUE / FORVALUES VALIDATION
    // ========================================================================

    [<Fact>]
    let ``forValue rejects target below 0`` () =
        let result = Oracle.forValue -1 3
        result |> Result.iter (fun _ -> failwith "Expected Error for negative target")

    [<Fact>]
    let ``forValue rejects target >= 2^n`` () =
        let result = Oracle.forValue 8 3 // 2^3 = 8, so 8 is out of range
        result |> Result.iter (fun _ -> failwith "Expected Error for target >= 2^n")

    [<Fact>]
    let ``forValue succeeds for valid target`` () =
        let result = Oracle.forValue 5 3 // 5 < 2^3 = 8

        result
        |> Result.map (fun oracle -> Assert.Equal(3, oracle.NumQubits))
        |> Result.defaultWith (fun e -> failwith $"Expected Ok, got Error: {e}")

    [<Fact>]
    let ``forValues rejects empty list`` () =
        let result = Oracle.forValues [] 3
        result |> Result.iter (fun _ -> failwith "Expected Error for empty list")

    [<Fact>]
    let ``forValues rejects out-of-range values`` () =
        let result = Oracle.forValues [ 1; 8 ] 3 // 8 >= 2^3

        result
        |> Result.iter (fun _ -> failwith "Expected Error for out-of-range value")

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
        result |> Result.iter (fun _ -> failwith "Expected Error for divisor = 0")

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
        result |> Result.iter (fun _ -> failwith "Expected Error for min > max")

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
            (Oracle.andOracle o1 o2)
            |> Result.iter (fun _ -> failwith "Expected Error for mismatched qubit counts")
        | _ -> failwith "Setup failed"

    [<Fact>]
    let ``orOracle rejects mismatched qubit counts`` () =
        match Oracle.forValue 0 2, Oracle.forValue 0 3 with
        | Ok o1, Ok o2 ->
            (Oracle.orOracle o1 o2)
            |> Result.iter (fun _ -> failwith "Expected Error for mismatched qubit counts")
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
        (Oracle.forValues [ 2; 5 ] 3)
        |> Result.map (fun oracle -> Assert.True(Oracle.verify oracle [ 2; 5 ]))
        |> Result.defaultWith (fun e -> failwith $"Setup failed: {e}")

    [<Fact>]
    let ``countSolutions returns correct count`` () =
        match Oracle.even 3 with
        | Ok oracle ->
            // 0, 2, 4, 6 are even in 3-qubit range [0..7]
            Assert.Equal(4, Oracle.countSolutions oracle)
        | Error e -> failwith $"Setup failed: {e}"

    [<Fact>]
    let ``listSolutions returns all solutions`` () =
        match Oracle.forValues [ 1; 3; 5 ] 3 with
        | Ok oracle ->
            let solutions = Oracle.listSolutions oracle |> List.sort
            Assert.Equal<int list>([ 1; 3; 5 ], solutions)
        | Error e -> failwith $"Setup failed: {e}"

    // ========================================================================
    // SAT ORACLES
    // ========================================================================

    [<Fact>]
    let ``satOracle rejects empty clauses`` () =
        let formula: Oracle.SatFormula = { NumVariables = 2; Clauses = [] }

        (Oracle.satOracle formula)
        |> Result.iter (fun _ -> failwith "Expected Error for empty clauses")

    [<Fact>]
    let ``satOracle rejects more variables than an oracle indexes`` () =
        let formula: Oracle.SatFormula =
            {
                NumVariables = Oracle.MaxIndexQubits + 1
                Clauses = [ Oracle.clause [ Oracle.var 0 ] ]
            }

        (Oracle.satOracle formula)
        |> Result.iter (fun _ -> failwith "Expected Error for NumVariables > MaxIndexQubits")

    [<Fact>]
    let ``satOracle accepts 21 variables`` () =
        let formula: Oracle.SatFormula =
            {
                NumVariables = 21
                Clauses = [ Oracle.clause [ Oracle.var 0 ] ]
            }

        match Oracle.satOracle formula with
        | Ok oracle -> Assert.Equal(21, oracle.NumQubits)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``satOracle works for simple formula`` () =
        // (x0 OR x1) - satisfiable by 01, 10, 11
        let formula: Oracle.SatFormula =
            {
                NumVariables = 2
                Clauses = [ Oracle.clause [ Oracle.var 0; Oracle.var 1 ] ]
            }

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
        let config: Oracle.GraphColoringConfig =
            {
                Graph = Oracle.graph 0 []
                NumColors = 2
            }

        (Oracle.graphColoringOracle config)
        |> Result.iter (fun _ -> failwith "Expected Error for 0 vertices")

    [<Fact>]
    let ``graphColoringOracle rejects too few colors`` () =
        let config: Oracle.GraphColoringConfig =
            {
                Graph = Oracle.graph 3 [ (0, 1); (1, 2) ]
                NumColors = 1
            }

        (Oracle.graphColoringOracle config)
        |> Result.iter (fun _ -> failwith "Expected Error for NumColors < 2")

    [<Fact>]
    let ``graphColoringOracle accepts over 10 vertices and over 8 colors when the qubits fit an index`` () =
        // 12 vertices x 1 qubit and 6 vertices x 4 qubits (9 colors): 12 and 24 qubits
        let wide: Oracle.GraphColoringConfig =
            {
                Graph = Oracle.graph 12 [ (0, 1) ]
                NumColors = 2
            }

        let manyColors: Oracle.GraphColoringConfig =
            {
                Graph = Oracle.graph 6 [ (0, 1) ]
                NumColors = 9
            }

        match Oracle.graphColoringOracle wide, Oracle.graphColoringOracle manyColors with
        | Ok a, Ok b ->
            Assert.Equal(12, a.NumQubits)
            Assert.Equal(24, b.NumQubits)
        | a, b -> failwith $"Expected Ok for both, got {a} and {b}"

    [<Fact>]
    let ``graphColoringOracle rejects more qubits than an oracle indexes`` () =
        // 16 vertices x 2 qubits (3 colors) = 32 qubits
        let config: Oracle.GraphColoringConfig =
            {
                Graph = Oracle.graph 16 [ (0, 1) ]
                NumColors = 3
            }

        match Oracle.graphColoringOracle config with
        | Error(QuantumError.ValidationError("NumQubits", msg)) -> Assert.Contains("32 qubits", msg)
        | other -> failwith $"Expected a NumQubits ValidationError, got {other}"

    [<Fact>]
    let ``graphColoringOracle succeeds for valid config`` () =
        let config: Oracle.GraphColoringConfig =
            {
                Graph = Oracle.graph 3 [ (0, 1); (1, 2) ]
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
        let config: Oracle.CliqueConfig =
            {
                Graph = Oracle.graph 4 [ (0, 1); (1, 2); (2, 3) ]
                CliqueSize = 1
            }

        (Oracle.cliqueOracle config)
        |> Result.iter (fun _ -> failwith "Expected Error for CliqueSize < 2")

    [<Fact>]
    let ``cliqueOracle rejects cliqueSize > numVertices`` () =
        let config: Oracle.CliqueConfig =
            {
                Graph = Oracle.graph 3 [ (0, 1); (1, 2) ]
                CliqueSize = 4
            }

        (Oracle.cliqueOracle config)
        |> Result.iter (fun _ -> failwith "Expected Error for CliqueSize > numVertices")

    [<Fact>]
    let ``cliqueOracle accepts 25 vertices and rejects more than an oracle indexes`` () =
        let config (n: int) : Oracle.CliqueConfig =
            {
                Graph = Oracle.graph n [ (0, 1) ]
                CliqueSize = 2
            }

        match Oracle.cliqueOracle (config 25) with
        | Ok oracle -> Assert.Equal(25, oracle.NumQubits)
        | Error e -> failwith $"Expected Ok for 25 vertices, got {e}"

        (Oracle.cliqueOracle (config (Oracle.MaxIndexQubits + 1)))
        |> Result.iter (fun _ -> failwith "Expected Error for NumVertices > MaxIndexQubits")
