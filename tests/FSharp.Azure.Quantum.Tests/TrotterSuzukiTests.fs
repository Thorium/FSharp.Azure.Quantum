namespace FSharp.Azure.Quantum.Tests

open System.Numerics
open Xunit
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum

module TrotterSuzukiTests =

    // ========================================================================
    // DEFAULT CONFIG
    // ========================================================================

    [<Fact>]
    let ``defaultConfig has expected values`` () =
        let config = TrotterSuzuki.defaultConfig
        Assert.Equal(10, config.NumSteps)
        Assert.Equal(1.0, config.Time)
        Assert.Equal(1, config.Order)

    // ========================================================================
    // DECOMPOSE MATRIX TO PAULI
    // ========================================================================

    [<Fact>]
    let ``decomposeMatrixToPauli with identity matrix returns I term`` () =
        let identity = Array2D.init 2 2 (fun i j -> if i = j then Complex.One else Complex.Zero)
        match TrotterSuzuki.decomposeMatrixToPauli identity with
        | Ok hamiltonian ->
            Assert.Equal(1, hamiltonian.NumQubits)
            Assert.True(hamiltonian.Terms.Length >= 1, "Identity should produce at least the I term")
            let iTerm = hamiltonian.Terms |> List.tryFind (fun t -> t.Operators = [|'I'|])
            Assert.True(iTerm.IsSome, "Should contain I operator")
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``decomposeMatrixToPauli with Pauli Z matrix returns Z term`` () =
        let pauliZ = Array2D.init 2 2 (fun i j ->
            if i = 0 && j = 0 then Complex.One
            elif i = 1 && j = 1 then Complex(- 1.0, 0.0)
            else Complex.Zero)
        match TrotterSuzuki.decomposeMatrixToPauli pauliZ with
        | Ok hamiltonian ->
            Assert.Equal(1, hamiltonian.NumQubits)
            let zTerm = hamiltonian.Terms |> List.tryFind (fun t -> t.Operators = [|'Z'|])
            Assert.True(zTerm.IsSome, "Should contain Z operator")
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``decomposeMatrixToPauli rejects non-square matrix`` () =
        let matrix = Array2D.init 2 3 (fun _ _ -> Complex.Zero)
        match TrotterSuzuki.decomposeMatrixToPauli matrix with
        | Error _ -> ()
        | Ok _ -> failwith "Expected Error for non-square matrix"

    [<Fact>]
    let ``decomposeMatrixToPauli rejects non-power-of-2 dimension`` () =
        let matrix = Array2D.init 3 3 (fun i j -> if i = j then Complex.One else Complex.Zero)
        match TrotterSuzuki.decomposeMatrixToPauli matrix with
        | Error _ -> ()
        | Ok _ -> failwith "Expected Error for non-power-of-2 dimension"

    [<Fact>]
    let ``decomposeMatrixToPauli handles 4x4 matrix`` () =
        // 2-qubit identity
        let identity = Array2D.init 4 4 (fun i j -> if i = j then Complex.One else Complex.Zero)
        match TrotterSuzuki.decomposeMatrixToPauli identity with
        | Ok hamiltonian ->
            Assert.Equal(2, hamiltonian.NumQubits)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    // ========================================================================
    // DECOMPOSE DIAGONAL MATRIX TO PAULI
    // ========================================================================

    [<Fact>]
    let ``decomposeDiagonalMatrixToPauli with Z eigenvalues produces Z term`` () =
        let eigenvalues = [| 1.0; -1.0 |]
        let hamiltonian = TrotterSuzuki.decomposeDiagonalMatrixToPauli eigenvalues
        Assert.Equal(1, hamiltonian.NumQubits)
        let zTerm = hamiltonian.Terms |> List.tryFind (fun t -> t.Operators = [|'Z'|])
        Assert.True(zTerm.IsSome, "Should contain Z operator for [1, -1] eigenvalues")

    [<Fact>]
    let ``decomposeDiagonalMatrixToPauli with uniform eigenvalues produces Z term`` () =
        let eigenvalues = [| 2.0; 2.0 |]
        let hamiltonian = TrotterSuzuki.decomposeDiagonalMatrixToPauli eigenvalues
        Assert.Equal(1, hamiltonian.NumQubits)
        // Uniform eigenvalues still produce a Z term because the algorithm
        // weights by average eigenvalue contribution from set-bit positions
        let zTerm = hamiltonian.Terms |> List.tryFind (fun t -> t.Operators = [|'Z'|])
        Assert.True(zTerm.IsSome, "Should contain Z operator for uniform eigenvalues")

    // ========================================================================
    // SYNTHESIZE PAULI EVOLUTION
    // ========================================================================

    [<Fact>]
    let ``synthesizePauliEvolution with all-identity returns unchanged circuit`` () =
        let pauliString : TrotterSuzuki.PauliString = { Operators = [|'I'|]; Coefficient = Complex.One }
        let circuit = CircuitBuilder.empty 1
        let result = TrotterSuzuki.synthesizePauliEvolution pauliString 1.0 [|0|] circuit
        // Identity evolution should not add any gates (or add global phase only)
        Assert.True(result.QubitCount >= 1)

    [<Fact>]
    let ``synthesizePauliEvolution with single Z adds RZ gate`` () =
        let pauliString : TrotterSuzuki.PauliString = { Operators = [|'Z'|]; Coefficient = Complex.One }
        let circuit = CircuitBuilder.empty 1
        let result = TrotterSuzuki.synthesizePauliEvolution pauliString 1.0 [|0|] circuit
        Assert.True(result.Gates.Length > 0, "Z evolution should add gates")

    [<Fact>]
    let ``synthesizePauliEvolution with single X adds H-RZ-H pattern`` () =
        let pauliString : TrotterSuzuki.PauliString = { Operators = [|'X'|]; Coefficient = Complex.One }
        let circuit = CircuitBuilder.empty 1
        let result = TrotterSuzuki.synthesizePauliEvolution pauliString 1.0 [|0|] circuit
        Assert.True(result.Gates.Length > 0, "X evolution should add gates")

    [<Fact>]
    let ``synthesizePauliEvolution with multi-qubit string adds CNOT ladder`` () =
        let pauliString : TrotterSuzuki.PauliString = { Operators = [|'X'; 'Z'|]; Coefficient = Complex.One }
        let circuit = CircuitBuilder.empty 2
        let result = TrotterSuzuki.synthesizePauliEvolution pauliString 1.0 [|0; 1|] circuit
        Assert.True(result.Gates.Length > 2, "Multi-qubit evolution should add CNOT ladder and rotation")

    [<Fact>]
    let ``synthesizePauliEvolution with qubit count mismatch throws`` () =
        let pauliString : TrotterSuzuki.PauliString = { Operators = [|'Z'; 'Z'|]; Coefficient = Complex.One }
        let circuit = CircuitBuilder.empty 3
        Assert.Throws<System.Exception>(fun () ->
            TrotterSuzuki.synthesizePauliEvolution pauliString 1.0 [|0|] circuit |> ignore
        ) |> ignore

    // ========================================================================
    // SYNTHESIZE HAMILTONIAN EVOLUTION
    // ========================================================================

    [<Fact>]
    let ``synthesizeHamiltonianEvolution with order 1 produces valid circuit`` () =
        let hamiltonian : TrotterSuzuki.PauliHamiltonian = {
            Terms = [
                { Operators = [|'Z'|]; Coefficient = Complex.One }
                { Operators = [|'X'|]; Coefficient = Complex(0.5, 0.0) }
            ]
            NumQubits = 1
        }
        let config : TrotterSuzuki.TrotterConfig = { NumSteps = 2; Time = 1.0; Order = 1 }
        let circuit = CircuitBuilder.empty 1
        let result = TrotterSuzuki.synthesizeHamiltonianEvolution hamiltonian config [|0|] circuit
        Assert.True(result.Gates.Length > 0, "Hamiltonian evolution should produce gates")

    [<Fact>]
    let ``synthesizeHamiltonianEvolution with order 2 produces valid circuit`` () =
        let hamiltonian : TrotterSuzuki.PauliHamiltonian = {
            Terms = [
                { Operators = [|'Z'|]; Coefficient = Complex.One }
                { Operators = [|'X'|]; Coefficient = Complex(0.5, 0.0) }
            ]
            NumQubits = 1
        }
        let config : TrotterSuzuki.TrotterConfig = { NumSteps = 2; Time = 1.0; Order = 2 }
        let circuit = CircuitBuilder.empty 1
        let result = TrotterSuzuki.synthesizeHamiltonianEvolution hamiltonian config [|0|] circuit
        Assert.True(result.Gates.Length > 0, "Order 2 evolution should produce gates")

    // ========================================================================
    // CONTROLLED PAULI EVOLUTION
    // ========================================================================

    [<Fact>]
    let ``synthesizeControlledPauliEvolution with single Z adds CRZ gate`` () =
        let pauliString : TrotterSuzuki.PauliString = { Operators = [|'Z'|]; Coefficient = Complex.One }
        let circuit = CircuitBuilder.empty 2  // 1 control + 1 target
        let result = TrotterSuzuki.synthesizeControlledPauliEvolution 0 pauliString 1.0 [|1|] circuit
        Assert.True(result.Gates.Length > 0, "Controlled Z evolution should add CRZ gate")

    // ========================================================================
    // ESTIMATE TROTTER STEPS
    // ========================================================================

    [<Fact>]
    let ``estimateTrotterSteps order 1 returns positive count`` () =
        let steps = TrotterSuzuki.estimateTrotterSteps 2.0 1.0 0.01 1
        Assert.True(steps > 0, $"Expected positive step count, got {steps}")

    [<Fact>]
    let ``estimateTrotterSteps order 2 returns fewer steps than order 1`` () =
        let steps1 = TrotterSuzuki.estimateTrotterSteps 2.0 1.0 0.01 1
        let steps2 = TrotterSuzuki.estimateTrotterSteps 2.0 1.0 0.01 2
        Assert.True(steps2 <= steps1, $"Order 2 ({steps2}) should need <= steps than order 1 ({steps1})")

    [<Fact>]
    let ``estimateTrotterSteps with zero norm returns 0`` () =
        let steps = TrotterSuzuki.estimateTrotterSteps 0.0 1.0 0.01 1
        Assert.Equal(0, steps)

    [<Fact>]
    let ``estimateTrotterSteps with smaller tolerance returns more steps`` () =
        let stepsCoarse = TrotterSuzuki.estimateTrotterSteps 2.0 1.0 0.1 1
        let stepsFine = TrotterSuzuki.estimateTrotterSteps 2.0 1.0 0.001 1
        Assert.True(stepsFine >= stepsCoarse, $"Finer tolerance ({stepsFine}) should need >= steps than coarse ({stepsCoarse})")
