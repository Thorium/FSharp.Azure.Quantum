namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.OpenQasmVersion

/// OpenQASM versioned support tests.
/// Covers QasmVersion, QasmConfig, version detection, V3.0 export/import,
/// round-trip fidelity, and parseWithVersion strict enforcement.
module OpenQasmVersionTests =

    // ========================================================================
    // VERSION CONFIG CONSTRUCTION TESTS
    // ========================================================================

    [<Fact>]
    let ``configFor V1_0 returns qelib1 and QregCreg`` () =
        let config = configFor V1_0
        Assert.Equal("1.0", config.VersionString)
        Assert.Equal("qelib1.inc", config.IncludeFile)
        Assert.Equal(QregCreg, config.RegisterStyle)
        Assert.Equal(ArrowSyntax, config.MeasureStyle)

    [<Fact>]
    let ``configFor V2_0 returns qelib1 and QregCreg`` () =
        let config = configFor V2_0
        Assert.Equal("2.0", config.VersionString)
        Assert.Equal("qelib1.inc", config.IncludeFile)
        Assert.Equal(QregCreg, config.RegisterStyle)
        Assert.Equal(ArrowSyntax, config.MeasureStyle)

    [<Fact>]
    let ``configFor V3_0 returns stdgates and QubitBit`` () =
        let config = configFor V3_0
        Assert.Equal("3.0", config.VersionString)
        Assert.Equal("stdgates.inc", config.IncludeFile)
        Assert.Equal(QubitBit, config.RegisterStyle)
        Assert.Equal(AssignmentSyntax, config.MeasureStyle)

    [<Fact>]
    let ``V1_0 and V2_0 configs differ only in version string`` () =
        let c1 = configFor V1_0
        let c2 = configFor V2_0
        Assert.NotEqual<string>(c1.VersionString, c2.VersionString)
        Assert.Equal(c1.IncludeFile, c2.IncludeFile)
        Assert.Equal(c1.RegisterStyle, c2.RegisterStyle)
        Assert.Equal(c1.MeasureStyle, c2.MeasureStyle)

    // ========================================================================
    // VERSION DETECTION TESTS
    // ========================================================================

    [<Fact>]
    let ``detectVersion finds V2_0 from header`` () =
        let qasm = "OPENQASM 2.0;\ninclude \"qelib1.inc\";\nqreg q[1];\n"
        let result = detectVersion qasm
        Assert.Equal(Ok V2_0, result)

    [<Fact>]
    let ``detectVersion finds V3_0 from header`` () =
        let qasm = "OPENQASM 3.0;\ninclude \"stdgates.inc\";\nqubit[1] q;\n"
        let result = detectVersion qasm
        Assert.Equal(Ok V3_0, result)

    [<Fact>]
    let ``detectVersion finds V1_0 from header`` () =
        let qasm = "OPENQASM 1.0;\ninclude \"qelib1.inc\";\nqreg q[1];\n"
        let result = detectVersion qasm
        Assert.Equal(Ok V1_0, result)

    [<Fact>]
    let ``detectVersion returns error for missing header`` () =
        let qasm = "include \"qelib1.inc\";\nqreg q[1];\n"
        let result = detectVersion qasm
        Assert.True(Result.isError result)

    [<Fact>]
    let ``detectVersion returns error for unsupported version`` () =
        let qasm = "OPENQASM 4.0;\nqreg q[1];\n"
        let result = detectVersion qasm
        Assert.True(Result.isError result)
        match result with
        | Error msg -> Assert.Contains("4.0", msg)
        | Ok _ -> failwith "Expected error"

    [<Fact>]
    let ``detectVersion handles whitespace around header`` () =
        let qasm = "  OPENQASM  3.0 ;\nqubit[1] q;\n"
        let result = detectVersion qasm
        Assert.Equal(Ok V3_0, result)

    // ========================================================================
    // parseVersionString TESTS
    // ========================================================================

    [<Fact>]
    let ``parseVersionString accepts 1.0 2.0 and 3.0`` () =
        Assert.Equal(Ok V1_0, parseVersionString "1.0")
        Assert.Equal(Ok V2_0, parseVersionString "2.0")
        Assert.Equal(Ok V3_0, parseVersionString "3.0")

    [<Fact>]
    let ``parseVersionString accepts 3 without minor`` () =
        Assert.Equal(Ok V3_0, parseVersionString "3")

    [<Fact>]
    let ``parseVersionString rejects unknown versions`` () =
        Assert.True(Result.isError (parseVersionString "5.0"))
        Assert.True(Result.isError (parseVersionString "2.1"))
        Assert.True(Result.isError (parseVersionString "abc"))

    // ========================================================================
    // HEADER GENERATION TESTS
    // ========================================================================

    [<Fact>]
    let ``headerLine generates correct V2_0 header`` () =
        let config = configFor V2_0
        Assert.Equal("OPENQASM 2.0;", headerLine config)

    [<Fact>]
    let ``headerLine generates correct V3_0 header`` () =
        let config = configFor V3_0
        Assert.Equal("OPENQASM 3.0;", headerLine config)

    [<Fact>]
    let ``includeLine uses qelib1 for V2_0`` () =
        let config = configFor V2_0
        Assert.Equal("include \"qelib1.inc\";", includeLine config)

    [<Fact>]
    let ``includeLine uses stdgates for V3_0`` () =
        let config = configFor V3_0
        Assert.Equal("include \"stdgates.inc\";", includeLine config)

    [<Fact>]
    let ``qubitRegisterDecl uses qreg for V2_0`` () =
        let config = configFor V2_0
        Assert.Equal("qreg q[3];", qubitRegisterDecl config 3)

    [<Fact>]
    let ``qubitRegisterDecl uses qubit for V3_0`` () =
        let config = configFor V3_0
        Assert.Equal("qubit[3] q;", qubitRegisterDecl config 3)

    [<Fact>]
    let ``classicalRegisterDecl uses creg for V2_0`` () =
        let config = configFor V2_0
        Assert.Equal("creg c[2];", classicalRegisterDecl config 2)

    [<Fact>]
    let ``classicalRegisterDecl uses bit for V3_0`` () =
        let config = configFor V3_0
        Assert.Equal("bit[2] c;", classicalRegisterDecl config 2)

    [<Fact>]
    let ``measureInstruction uses arrow for V2_0`` () =
        let config = configFor V2_0
        Assert.Equal("measure q[0] -> c[0];", measureInstruction config 0)

    [<Fact>]
    let ``measureInstruction uses assignment for V3_0`` () =
        let config = configFor V3_0
        Assert.Equal("c[0] = measure q[0];", measureInstruction config 0)

    // ========================================================================
    // versionToString TESTS
    // ========================================================================

    [<Fact>]
    let ``versionToString returns correct strings`` () =
        Assert.Equal("1.0", versionToString V1_0)
        Assert.Equal("2.0", versionToString V2_0)
        Assert.Equal("3.0", versionToString V3_0)

    // ========================================================================
    // V3.0 EXPORT TESTS
    // ========================================================================

    [<Fact>]
    let ``exportV3 generates V3_0 header and qubit declaration`` () =
        let circuit = { QubitCount = 2; Gates = [H 0; CNOT (0, 1)] }
        let qasm = OpenQasm.exportV3 circuit
        Assert.Contains("OPENQASM 3.0;", qasm)
        Assert.Contains("include \"stdgates.inc\";", qasm)
        Assert.Contains("qubit[2] q;", qasm)
        Assert.Contains("h q[0];", qasm)
        Assert.Contains("cx q[0],q[1];", qasm)

    [<Fact>]
    let ``exportV3 does not contain V2_0 artifacts`` () =
        let circuit = { QubitCount = 1; Gates = [X 0] }
        let qasm = OpenQasm.exportV3 circuit
        Assert.DoesNotContain("OPENQASM 2.0;", qasm)
        Assert.DoesNotContain("qelib1.inc", qasm)
        Assert.DoesNotContain("qreg", qasm)

    [<Fact>]
    let ``exportV3 measurement uses assignment syntax`` () =
        let circuit = { QubitCount = 1; Gates = [H 0; Measure 0] }
        let qasm = OpenQasm.exportV3 circuit
        Assert.Contains("c[0] = measure q[0];", qasm)
        Assert.DoesNotContain("->", qasm)

    [<Fact>]
    let ``export default still uses V2_0 format`` () =
        let circuit = { QubitCount = 1; Gates = [H 0; Measure 0] }
        let qasm = OpenQasm.export circuit
        Assert.Contains("OPENQASM 2.0;", qasm)
        Assert.Contains("qelib1.inc", qasm)
        Assert.Contains("qreg q[1];", qasm)
        Assert.Contains("measure q[0] -> c[0];", qasm)

    [<Fact>]
    let ``exportWithConfig V1_0 produces 1.0 header`` () =
        let config = configFor V1_0
        let circuit = { QubitCount = 1; Gates = [X 0] }
        let qasm = OpenQasm.exportWithConfig config circuit
        Assert.Contains("OPENQASM 1.0;", qasm)
        Assert.Contains("qreg q[1];", qasm)

    [<Fact>]
    let ``exportV3 with rotation gates formats angles correctly`` () =
        let circuit = { QubitCount = 1; Gates = [RX (0, 1.5707963268)] }
        let qasm = OpenQasm.exportV3 circuit
        Assert.Contains("rx(", qasm)
        Assert.Contains("q[0];", qasm)

    [<Fact>]
    let ``exportV3 with multi-qubit gates`` () =
        let circuit = { QubitCount = 3; Gates = [H 0; CNOT (0, 1); CCX (0, 1, 2)] }
        let qasm = OpenQasm.exportV3 circuit
        Assert.Contains("qubit[3] q;", qasm)
        Assert.Contains("ccx q[0],q[1],q[2];", qasm)

    // ========================================================================
    // V3.0 IMPORT TESTS
    // ========================================================================

    [<Fact>]
    let ``parse V3_0 qubit declaration`` () =
        let qasm = """OPENQASM 3.0;
include "stdgates.inc";
qubit[2] q;
h q[0];
cx q[0],q[1];
"""
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            Assert.Equal(2, circuit.QubitCount)
            Assert.Equal(2, circuit.Gates.Length)
            Assert.Equal(H 0, circuit.Gates.[0])
            Assert.Equal(CNOT (0, 1), circuit.Gates.[1])
        | Error msg -> failwith $"Parse failed: {msg}"

    [<Fact>]
    let ``parse V3_0 assignment measurement`` () =
        let qasm = """OPENQASM 3.0;
include "stdgates.inc";
qubit[1] q;
bit[1] c;
h q[0];
c[0] = measure q[0];
"""
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            Assert.Equal(1, circuit.QubitCount)
            Assert.Equal(2, circuit.Gates.Length)
            Assert.Equal(H 0, circuit.Gates.[0])
            Assert.Equal(Measure 0, circuit.Gates.[1])
        | Error msg -> failwith $"Parse failed: {msg}"

    [<Fact>]
    let ``parse V3_0 ignores bit declaration`` () =
        let qasm = """OPENQASM 3.0;
include "stdgates.inc";
qubit[2] q;
bit[2] c;
x q[0];
"""
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            Assert.Equal(2, circuit.QubitCount)
            Assert.Equal(1, circuit.Gates.Length)
        | Error msg -> failwith $"Parse failed: {msg}"

    [<Fact>]
    let ``parse V3_0 multiple gates`` () =
        let qasm = """OPENQASM 3.0;
include "stdgates.inc";
qubit[3] q;
h q[0];
cx q[0],q[1];
ccx q[0],q[1],q[2];
c[0] = measure q[0];
c[1] = measure q[1];
"""
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            Assert.Equal(3, circuit.QubitCount)
            Assert.Equal(5, circuit.Gates.Length)
            Assert.Equal(H 0, circuit.Gates.[0])
            Assert.Equal(CNOT (0, 1), circuit.Gates.[1])
            Assert.Equal(CCX (0, 1, 2), circuit.Gates.[2])
            Assert.Equal(Measure 0, circuit.Gates.[3])
            Assert.Equal(Measure 1, circuit.Gates.[4])
        | Error msg -> failwith $"Parse failed: {msg}"

    [<Fact>]
    let ``parse V2_0 arrow measurement still works`` () =
        let qasm = """OPENQASM 2.0;
include "qelib1.inc";
qreg q[1];
creg c[1];
h q[0];
measure q[0] -> c[0];
"""
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            Assert.Equal(1, circuit.QubitCount)
            Assert.Equal(2, circuit.Gates.Length)
            Assert.Equal(Measure 0, circuit.Gates.[1])
        | Error msg -> failwith $"Parse failed: {msg}"

    // ========================================================================
    // ROUND-TRIP TESTS
    // ========================================================================

    [<Fact>]
    let ``V3_0 round-trip preserves Bell state circuit`` () =
        let original = { QubitCount = 2; Gates = [H 0; CNOT (0, 1)] }
        let exported = OpenQasm.exportV3 original
        let result = OpenQasmImport.parse exported
        match result with
        | Ok reimported ->
            Assert.Equal(original.QubitCount, reimported.QubitCount)
            Assert.Equal(original.Gates.Length, reimported.Gates.Length)
            Assert.Equal(original.Gates.[0], reimported.Gates.[0])
            Assert.Equal(original.Gates.[1], reimported.Gates.[1])
        | Error msg -> failwith $"Round-trip failed: {msg}"

    [<Fact>]
    let ``V2_0 round-trip preserves Bell state circuit`` () =
        let original = { QubitCount = 2; Gates = [H 0; CNOT (0, 1)] }
        let exported = OpenQasm.export original
        let result = OpenQasmImport.parse exported
        match result with
        | Ok reimported ->
            Assert.Equal(original.QubitCount, reimported.QubitCount)
            Assert.Equal(original.Gates.Length, reimported.Gates.Length)
            Assert.Equal(original.Gates.[0], reimported.Gates.[0])
            Assert.Equal(original.Gates.[1], reimported.Gates.[1])
        | Error msg -> failwith $"Round-trip failed: {msg}"

    [<Fact>]
    let ``V3_0 round-trip preserves single-qubit gates`` () =
        let original = { QubitCount = 1; Gates = [X 0; Y 0; Z 0; H 0; S 0; T 0] }
        let exported = OpenQasm.exportV3 original
        let result = OpenQasmImport.parse exported
        match result with
        | Ok reimported ->
            Assert.Equal(original.QubitCount, reimported.QubitCount)
            Assert.Equal<Gate list>(original.Gates, reimported.Gates)
        | Error msg -> failwith $"Round-trip failed: {msg}"

    [<Fact>]
    let ``V3_0 round-trip preserves measurement`` () =
        let original = { QubitCount = 2; Gates = [H 0; CNOT (0, 1); Measure 0; Measure 1] }
        let exported = OpenQasm.exportV3 original
        let result = OpenQasmImport.parse exported
        match result with
        | Ok reimported ->
            Assert.Equal(original.QubitCount, reimported.QubitCount)
            Assert.Equal(original.Gates.Length, reimported.Gates.Length)
            Assert.Equal(Measure 0, reimported.Gates.[2])
            Assert.Equal(Measure 1, reimported.Gates.[3])
        | Error msg -> failwith $"Round-trip failed: {msg}"

    [<Fact>]
    let ``V3_0 round-trip preserves multi-qubit circuit`` () =
        let original = { QubitCount = 3; Gates = [H 0; CNOT (0, 1); SWAP (1, 2); CZ (0, 2); CCX (0, 1, 2)] }
        let exported = OpenQasm.exportV3 original
        let result = OpenQasmImport.parse exported
        match result with
        | Ok reimported ->
            Assert.Equal(original.QubitCount, reimported.QubitCount)
            Assert.Equal<Gate list>(original.Gates, reimported.Gates)
        | Error msg -> failwith $"Round-trip failed: {msg}"

    // ========================================================================
    // parseWithVersion STRICT ENFORCEMENT TESTS
    // ========================================================================

    [<Fact>]
    let ``parseWithVersion accepts matching V2_0`` () =
        let qasm = """OPENQASM 2.0;
include "qelib1.inc";
qreg q[1];
x q[0];
"""
        let result = OpenQasmImport.parseWithVersion V2_0 qasm
        Assert.True(Result.isOk result)

    [<Fact>]
    let ``parseWithVersion accepts matching V3_0`` () =
        let qasm = """OPENQASM 3.0;
include "stdgates.inc";
qubit[1] q;
x q[0];
"""
        let result = OpenQasmImport.parseWithVersion V3_0 qasm
        Assert.True(Result.isOk result)

    [<Fact>]
    let ``parseWithVersion rejects V2_0 when V3_0 expected`` () =
        let qasm = """OPENQASM 2.0;
include "qelib1.inc";
qreg q[1];
x q[0];
"""
        let result = OpenQasmImport.parseWithVersion V3_0 qasm
        Assert.True(Result.isError result)
        match result with
        | Error msg ->
            Assert.Contains("mismatch", msg.ToLowerInvariant())
            Assert.Contains("3.0", msg)
            Assert.Contains("2.0", msg)
        | Ok _ -> failwith "Expected error"

    [<Fact>]
    let ``parseWithVersion rejects V3_0 when V2_0 expected`` () =
        let qasm = """OPENQASM 3.0;
include "stdgates.inc";
qubit[1] q;
x q[0];
"""
        let result = OpenQasmImport.parseWithVersion V2_0 qasm
        Assert.True(Result.isError result)
        match result with
        | Error msg ->
            Assert.Contains("mismatch", msg.ToLowerInvariant())
        | Ok _ -> failwith "Expected error"

    [<Fact>]
    let ``parseWithVersion rejects missing header`` () =
        let qasm = """include "qelib1.inc";
qreg q[1];
x q[0];
"""
        let result = OpenQasmImport.parseWithVersion V2_0 qasm
        Assert.True(Result.isError result)

    // ========================================================================
    // CROSS-VERSION IMPORT TESTS
    // ========================================================================

    [<Fact>]
    let ``auto-detect parse handles both V2_0 and V3_0 correctly`` () =
        let qasmV2 = """OPENQASM 2.0;
include "qelib1.inc";
qreg q[2];
h q[0];
cx q[0],q[1];
"""
        let qasmV3 = """OPENQASM 3.0;
include "stdgates.inc";
qubit[2] q;
h q[0];
cx q[0],q[1];
"""
        let result2 = OpenQasmImport.parse qasmV2
        let result3 = OpenQasmImport.parse qasmV3
        match result2, result3 with
        | Ok c2, Ok c3 ->
            Assert.Equal(c2.QubitCount, c3.QubitCount)
            Assert.Equal<Gate list>(c2.Gates, c3.Gates)
        | Error msg, _ -> failwith $"V2.0 parse failed: {msg}"
        | _, Error msg -> failwith $"V3.0 parse failed: {msg}"

    [<Fact>]
    let ``V1_0 export can be re-imported`` () =
        let config = configFor V1_0
        let circuit = { QubitCount = 2; Gates = [H 0; CNOT (0, 1)] }
        let exported = OpenQasm.exportWithConfig config circuit
        Assert.Contains("OPENQASM 1.0;", exported)
        let result = OpenQasmImport.parse exported
        match result with
        | Ok reimported ->
            Assert.Equal(circuit.QubitCount, reimported.QubitCount)
            Assert.Equal<Gate list>(circuit.Gates, reimported.Gates)
        | Error msg -> failwith $"V1.0 re-import failed: {msg}"

    // ========================================================================
    // CLASSICAL REGISTER DECLARATION EXPORT TESTS (Bug 2)
    // ========================================================================

    [<Fact>]
    let ``V2_0 export emits creg when circuit has measurements`` () =
        let circuit = { QubitCount = 2; Gates = [H 0; CNOT (0, 1); Measure 0; Measure 1] }
        let qasm = OpenQasm.export circuit
        Assert.Contains("creg c[2];", qasm)

    [<Fact>]
    let ``V3_0 export emits bit when circuit has measurements`` () =
        let circuit = { QubitCount = 2; Gates = [H 0; CNOT (0, 1); Measure 0; Measure 1] }
        let qasm = OpenQasm.exportV3 circuit
        Assert.Contains("bit[2] c;", qasm)

    [<Fact>]
    let ``export does not emit creg when circuit has no measurements`` () =
        let circuit = { QubitCount = 2; Gates = [H 0; CNOT (0, 1)] }
        let qasm = OpenQasm.export circuit
        Assert.DoesNotContain("creg", qasm)

    [<Fact>]
    let ``V3_0 export does not emit bit declaration when circuit has no measurements`` () =
        let circuit = { QubitCount = 2; Gates = [H 0; CNOT (0, 1)] }
        let qasm = OpenQasm.exportV3 circuit
        Assert.DoesNotContain("bit[2] c;", qasm)

    [<Fact>]
    let ``V2_0 export with single measurement emits creg c[1]`` () =
        let circuit = { QubitCount = 3; Gates = [H 0; Measure 2] }
        let qasm = OpenQasm.export circuit
        Assert.Contains("creg c[1];", qasm)

    [<Fact>]
    let ``V2_0 round-trip with measurement includes creg`` () =
        let circuit = { QubitCount = 2; Gates = [H 0; CNOT (0, 1); Measure 0; Measure 1] }
        let exported = OpenQasm.export circuit
        Assert.Contains("creg c[2];", exported)
        let result = OpenQasmImport.parse exported
        match result with
        | Ok reimported ->
            Assert.Equal(circuit.QubitCount, reimported.QubitCount)
            Assert.Equal<Gate list>(circuit.Gates, reimported.Gates)
        | Error msg -> failwith $"Round-trip failed: {msg}"

    [<Fact>]
    let ``V3_0 round-trip with measurement includes bit declaration`` () =
        let circuit = { QubitCount = 2; Gates = [H 0; CNOT (0, 1); Measure 0; Measure 1] }
        let exported = OpenQasm.exportV3 circuit
        Assert.Contains("bit[2] c;", exported)
        let result = OpenQasmImport.parse exported
        match result with
        | Ok reimported ->
            Assert.Equal(circuit.QubitCount, reimported.QubitCount)
            Assert.Equal<Gate list>(circuit.Gates, reimported.Gates)
        | Error msg -> failwith $"Round-trip failed: {msg}"

    // ========================================================================
    // NEGATIVE ANGLE AND PI EXPRESSION IMPORT TESTS (Gap 1 & 2)
    // ========================================================================

    [<Fact>]
    let ``parse negative angle literal rx(-1.5707963268)`` () =
        let qasm = """OPENQASM 2.0;
include "qelib1.inc";
qreg q[1];
rx(-1.5707963268) q[0];
"""
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            Assert.Equal(1, circuit.Gates.Length)
            match circuit.Gates.[0] with
            | RX (0, angle) -> Assert.True(abs (angle - (-1.5707963268)) < 1e-6)
            | _ -> failwith $"Expected RX, got {circuit.Gates.[0]}"
        | Error msg -> failwith $"Parse failed: {msg}"

    [<Fact>]
    let ``parse pi as angle in rx(pi)`` () =
        let qasm = """OPENQASM 2.0;
include "qelib1.inc";
qreg q[1];
rx(pi) q[0];
"""
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            Assert.Equal(1, circuit.Gates.Length)
            match circuit.Gates.[0] with
            | RX (0, angle) -> Assert.True(abs (angle - System.Math.PI) < 1e-10)
            | _ -> failwith $"Expected RX, got {circuit.Gates.[0]}"
        | Error msg -> failwith $"Parse failed: {msg}"

    [<Fact>]
    let ``parse pi/2 as angle in ry(pi/2)`` () =
        let qasm = """OPENQASM 2.0;
include "qelib1.inc";
qreg q[1];
ry(pi/2) q[0];
"""
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            match circuit.Gates.[0] with
            | RY (0, angle) -> Assert.True(abs (angle - System.Math.PI / 2.0) < 1e-10)
            | _ -> failwith $"Expected RY, got {circuit.Gates.[0]}"
        | Error msg -> failwith $"Parse failed: {msg}"

    [<Fact>]
    let ``parse pi/4 as angle in rz(pi/4)`` () =
        let qasm = """OPENQASM 2.0;
include "qelib1.inc";
qreg q[1];
rz(pi/4) q[0];
"""
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            match circuit.Gates.[0] with
            | RZ (0, angle) -> Assert.True(abs (angle - System.Math.PI / 4.0) < 1e-10)
            | _ -> failwith $"Expected RZ, got {circuit.Gates.[0]}"
        | Error msg -> failwith $"Parse failed: {msg}"

    [<Fact>]
    let ``parse 2*pi as angle`` () =
        let qasm = """OPENQASM 2.0;
include "qelib1.inc";
qreg q[1];
rx(2*pi) q[0];
"""
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            match circuit.Gates.[0] with
            | RX (0, angle) -> Assert.True(abs (angle - 2.0 * System.Math.PI) < 1e-10)
            | _ -> failwith $"Expected RX, got {circuit.Gates.[0]}"
        | Error msg -> failwith $"Parse failed: {msg}"

    [<Fact>]
    let ``parse -pi/4 as angle`` () =
        let qasm = """OPENQASM 2.0;
include "qelib1.inc";
qreg q[1];
rz(-pi/4) q[0];
"""
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            match circuit.Gates.[0] with
            | RZ (0, angle) -> Assert.True(abs (angle - (-System.Math.PI / 4.0)) < 1e-10)
            | _ -> failwith $"Expected RZ, got {circuit.Gates.[0]}"
        | Error msg -> failwith $"Parse failed: {msg}"

    [<Fact>]
    let ``parse 3*pi/4 as angle`` () =
        let qasm = """OPENQASM 2.0;
include "qelib1.inc";
qreg q[1];
rz(3*pi/4) q[0];
"""
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            match circuit.Gates.[0] with
            | RZ (0, angle) -> Assert.True(abs (angle - 3.0 * System.Math.PI / 4.0) < 1e-10)
            | _ -> failwith $"Expected RZ, got {circuit.Gates.[0]}"
        | Error msg -> failwith $"Parse failed: {msg}"

    [<Fact>]
    let ``parse u3 with pi expressions`` () =
        let qasm = """OPENQASM 2.0;
include "qelib1.inc";
qreg q[1];
u3(pi/2,0,pi) q[0];
"""
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            match circuit.Gates.[0] with
            | U3 (0, theta, phi, lambda) ->
                Assert.True(abs (theta - System.Math.PI / 2.0) < 1e-10)
                Assert.True(abs phi < 1e-10)
                Assert.True(abs (lambda - System.Math.PI) < 1e-10)
            | _ -> failwith $"Expected U3, got {circuit.Gates.[0]}"
        | Error msg -> failwith $"Parse failed: {msg}"

    [<Fact>]
    let ``parse cp with pi expression`` () =
        let qasm = """OPENQASM 2.0;
include "qelib1.inc";
qreg q[2];
cp(pi/4) q[0],q[1];
"""
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            match circuit.Gates.[0] with
            | CP (0, 1, angle) -> Assert.True(abs (angle - System.Math.PI / 4.0) < 1e-10)
            | _ -> failwith $"Expected CP, got {circuit.Gates.[0]}"
        | Error msg -> failwith $"Parse failed: {msg}"

    // ========================================================================
    // MULTIPLE NAMED REGISTERS IMPORT TESTS (Gap 3 & 4)
    // ========================================================================

    [<Fact>]
    let ``parse multiple qreg declarations`` () =
        let qasm = """OPENQASM 2.0;
include "qelib1.inc";
qreg a[2];
qreg b[3];
h a[0];
cx a[1],b[0];
x b[2];
"""
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            Assert.Equal(5, circuit.QubitCount)
            Assert.Equal(3, circuit.Gates.Length)
            // a[0] -> qubit 0, a[1] -> qubit 1, b[0] -> qubit 2, b[2] -> qubit 4
            Assert.Equal(H 0, circuit.Gates.[0])
            Assert.Equal(CNOT (1, 2), circuit.Gates.[1])
            Assert.Equal(X 4, circuit.Gates.[2])
        | Error msg -> failwith $"Parse failed: {msg}"

    [<Fact>]
    let ``parse V3 multiple qubit declarations`` () =
        let qasm = """OPENQASM 3.0;
include "stdgates.inc";
qubit[2] a;
qubit[1] b;
h a[0];
cx a[1],b[0];
"""
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            Assert.Equal(3, circuit.QubitCount)
            Assert.Equal(2, circuit.Gates.Length)
            Assert.Equal(H 0, circuit.Gates.[0])
            Assert.Equal(CNOT (1, 2), circuit.Gates.[1])
        | Error msg -> failwith $"Parse failed: {msg}"

    [<Fact>]
    let ``parse named register gate instructions`` () =
        let qasm = """OPENQASM 2.0;
include "qelib1.inc";
qreg data[2];
h data[0];
cx data[0],data[1];
"""
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            Assert.Equal(2, circuit.QubitCount)
            Assert.Equal(2, circuit.Gates.Length)
            Assert.Equal(H 0, circuit.Gates.[0])
            Assert.Equal(CNOT (0, 1), circuit.Gates.[1])
        | Error msg -> failwith $"Parse failed: {msg}"

    [<Fact>]
    let ``parse measurement with named register`` () =
        let qasm = """OPENQASM 2.0;
include "qelib1.inc";
qreg data[2];
creg meas[2];
h data[0];
measure data[0] -> meas[0];
"""
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            Assert.Equal(2, circuit.QubitCount)
            Assert.Equal(2, circuit.Gates.Length)
            Assert.Equal(H 0, circuit.Gates.[0])
            Assert.Equal(Measure 0, circuit.Gates.[1])
        | Error msg -> failwith $"Parse failed: {msg}"

    [<Fact>]
    let ``parse rotation gate with named register`` () =
        let qasm = """OPENQASM 2.0;
include "qelib1.inc";
qreg myq[1];
rx(1.5707963268) myq[0];
"""
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            Assert.Equal(1, circuit.QubitCount)
            Assert.Equal(1, circuit.Gates.Length)
            match circuit.Gates.[0] with
            | RX (0, _) -> ()
            | _ -> failwith $"Expected RX, got {circuit.Gates.[0]}"
        | Error msg -> failwith $"Parse failed: {msg}"

    // ========================================================================
    // EDGE CASES
    // ========================================================================

    [<Fact>]
    let ``export empty circuit in V3_0`` () =
        let circuit = { QubitCount = 1; Gates = [] }
        let qasm = OpenQasm.exportV3 circuit
        Assert.Contains("OPENQASM 3.0;", qasm)
        Assert.Contains("qubit[1] q;", qasm)

    [<Fact>]
    let ``parse V3_0 with only qubit declaration and no gates`` () =
        let qasm = """OPENQASM 3.0;
include "stdgates.inc";
qubit[1] q;
"""
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            Assert.Equal(1, circuit.QubitCount)
            Assert.Empty(circuit.Gates)
        | Error msg -> failwith $"Parse failed: {msg}"

    [<Fact>]
    let ``parse V3_0 rejects duplicate register name`` () =
        let qasm = """OPENQASM 3.0;
include "stdgates.inc";
qubit[2] q;
qubit[3] q;
h q[0];
"""
        let result = OpenQasmImport.parse qasm
        Assert.True(Result.isError result)
        match result with
        | Error msg -> Assert.Contains("Duplicate", msg)
        | Ok _ -> failwith "Expected error"

    [<Fact>]
    let ``parse rejects OPENQASM 99.0`` () =
        let qasm = """OPENQASM 99.0;
include "qelib1.inc";
qreg q[1];
x q[0];
"""
        let result = OpenQasmImport.parse qasm
        Assert.True(Result.isError result)
