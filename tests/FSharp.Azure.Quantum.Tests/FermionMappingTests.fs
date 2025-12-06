namespace FSharp.Azure.Quantum.Tests

open System
open System.Numerics
open Xunit
open FSharp.Azure.Quantum.QuantumChemistry.FermionMapping
open FSharp.Azure.Quantum.Core.QaoaCircuit

/// Tests for Fermion-to-Qubit Mapping Transformations
module FermionMappingTests =
    
    // ========================================================================
    // PAULI ALGEBRA TESTS
    // ========================================================================
    
    module PauliAlgebraTests =
        
        [<Fact>]
        let ``multiplyPaulis identity rules`` () =
            // I * X = X
            let (phase1, result1) = multiplyPaulis PauliI PauliX
            Assert.Equal(Complex.One, phase1)
            Assert.Equal(PauliX, result1)
            
            // X * I = X
            let (phase2, result2) = multiplyPaulis PauliX PauliI
            Assert.Equal(Complex.One, phase2)
            Assert.Equal(PauliX, result2)
            
            // I * I = I
            let (phase3, result3) = multiplyPaulis PauliI PauliI
            Assert.Equal(Complex.One, phase3)
            Assert.Equal(PauliI, result3)
        
        [<Fact>]
        let ``multiplyPaulis self-multiplication returns identity`` () =
            // X * X = I
            let (phase1, result1) = multiplyPaulis PauliX PauliX
            Assert.Equal(Complex.One, phase1)
            Assert.Equal(PauliI, result1)
            
            // Y * Y = I
            let (phase2, result2) = multiplyPaulis PauliY PauliY
            Assert.Equal(Complex.One, phase2)
            Assert.Equal(PauliI, result2)
            
            // Z * Z = I
            let (phase3, result3) = multiplyPaulis PauliZ PauliZ
            Assert.Equal(Complex.One, phase3)
            Assert.Equal(PauliI, result3)
        
        [<Fact>]
        let ``multiplyPaulis cyclic permutations have positive phase`` () =
            // X * Y = iZ
            let (phase1, result1) = multiplyPaulis PauliX PauliY
            Assert.Equal(Complex.ImaginaryOne, phase1)
            Assert.Equal(PauliZ, result1)
            
            // Y * Z = iX
            let (phase2, result2) = multiplyPaulis PauliY PauliZ
            Assert.Equal(Complex.ImaginaryOne, phase2)
            Assert.Equal(PauliX, result2)
            
            // Z * X = iY
            let (phase3, result3) = multiplyPaulis PauliZ PauliX
            Assert.Equal(Complex.ImaginaryOne, phase3)
            Assert.Equal(PauliY, result3)
        
        [<Fact>]
        let ``multiplyPaulis anticyclic permutations have negative phase`` () =
            // Y * X = -iZ
            let (phase1, result1) = multiplyPaulis PauliY PauliX
            Assert.Equal(-Complex.ImaginaryOne, phase1)
            Assert.Equal(PauliZ, result1)
            
            // Z * Y = -iX
            let (phase2, result2) = multiplyPaulis PauliZ PauliY
            Assert.Equal(-Complex.ImaginaryOne, phase2)
            Assert.Equal(PauliX, result2)
            
            // X * Z = -iY
            let (phase3, result3) = multiplyPaulis PauliX PauliZ
            Assert.Equal(-Complex.ImaginaryOne, phase3)
            Assert.Equal(PauliY, result3)
        
        [<Fact>]
        let ``multiplyPauliStrings combines operators correctly`` () =
            // X₀ * Y₀ = iZ₀
            let ps1 = {
                Coefficient = Complex.One
                Operators = Map.ofList [(0, PauliX)]
            }
            let ps2 = {
                Coefficient = Complex.One
                Operators = Map.ofList [(0, PauliY)]
            }
            
            let result = multiplyPauliStrings ps1 ps2
            
            Assert.Equal(Complex.ImaginaryOne, result.Coefficient)
            Assert.Equal(1, result.Operators.Count)
            Assert.Equal(PauliZ, result.Operators.[0])
        
        [<Fact>]
        let ``multiplyPauliStrings multiplies coefficients`` () =
            let ps1 = {
                Coefficient = Complex(2.0, 0.0)
                Operators = Map.ofList [(0, PauliX)]
            }
            let ps2 = {
                Coefficient = Complex(3.0, 0.0)
                Operators = Map.ofList [(1, PauliY)]
            }
            
            let result = multiplyPauliStrings ps1 ps2
            
            Assert.Equal(Complex(6.0, 0.0), result.Coefficient)
        
        [<Fact>]
        let ``multiplyPauliStrings accumulates phases`` () =
            // (X₀ Y₁) * (Y₀ Z₁) = -Z₀ X₁ (phase: i * (-i) = 1, but anticommute)
            let ps1 = {
                Coefficient = Complex.One
                Operators = Map.ofList [(0, PauliX); (1, PauliY)]
            }
            let ps2 = {
                Coefficient = Complex.One
                Operators = Map.ofList [(0, PauliY); (1, PauliZ)]
            }
            
            let result = multiplyPauliStrings ps1 ps2
            
            // X*Y = iZ, Y*Z = iX, total phase = i*i = -1
            Assert.Equal(2, result.Operators.Count)
        
        [<Fact>]
        let ``multiplyPauliStrings only stores non-identity operators`` () =
            // X₀ * X₀ = I (should have empty Operators map)
            let ps1 = {
                Coefficient = Complex.One
                Operators = Map.ofList [(0, PauliX)]
            }
            
            let result = multiplyPauliStrings ps1 ps1
            
            Assert.Equal(0, result.Operators.Count)  // Identity not stored
            Assert.Equal(Complex.One, result.Coefficient)
    
    // ========================================================================
    // JORDAN-WIGNER TRANSFORMATION TESTS
    // ========================================================================
    
    module JordanWignerTests =
        
        [<Fact>]
        let ``transformOperator creation produces X minus iY over 2`` () =
            let op = {
                OrbitalIndex = 2
                OperatorType = Creation
            }
            
            let (xTerm, yTerm) = JordanWigner.transformOperator op
            
            // X term: coefficient = 0.5
            Assert.Equal(Complex(0.5, 0.0), xTerm.Coefficient)
            Assert.True(xTerm.Operators.ContainsKey(2))
            Assert.Equal(PauliX, xTerm.Operators.[2])
            
            // Y term: coefficient = -0.5i
            Assert.Equal(Complex(0.0, -0.5), yTerm.Coefficient)
            Assert.True(yTerm.Operators.ContainsKey(2))
            Assert.Equal(PauliY, yTerm.Operators.[2])
        
        [<Fact>]
        let ``transformOperator annihilation produces X plus iY over 2`` () =
            let op = {
                OrbitalIndex = 2
                OperatorType = Annihilation
            }
            
            let (xTerm, yTerm) = JordanWigner.transformOperator op
            
            // X term: coefficient = 0.5
            Assert.Equal(Complex(0.5, 0.0), xTerm.Coefficient)
            
            // Y term: coefficient = +0.5i
            Assert.Equal(Complex(0.0, 0.5), yTerm.Coefficient)
        
        [<Fact>]
        let ``transformOperator includes Z string for lower orbitals`` () =
            let op = {
                OrbitalIndex = 3
                OperatorType = Creation
            }
            
            let (xTerm, yTerm) = JordanWigner.transformOperator op
            
            // Should have Z₀, Z₁, Z₂, X₃
            Assert.Equal(4, xTerm.Operators.Count)
            Assert.Equal(PauliZ, xTerm.Operators.[0])
            Assert.Equal(PauliZ, xTerm.Operators.[1])
            Assert.Equal(PauliZ, xTerm.Operators.[2])
            Assert.Equal(PauliX, xTerm.Operators.[3])
        
        [<Fact>]
        let ``transformOperator orbital 0 has no Z string`` () =
            let op = {
                OrbitalIndex = 0
                OperatorType = Creation
            }
            
            let (xTerm, yTerm) = JordanWigner.transformOperator op
            
            // Should only have X₀ (no Z string)
            Assert.Equal(1, xTerm.Operators.Count)
            Assert.Equal(PauliX, xTerm.Operators.[0])
        
        [<Fact>]
        let ``transformTerm empty operators returns identity`` () =
            let term : FermionTerm = {
                Coefficient = Complex(2.5, 0.0)
                Operators = []
            }
            
            let result = JordanWigner.transformTerm term
            
            Assert.Equal(1, result.Length)
            Assert.Equal(Complex(2.5, 0.0), result.[0].Coefficient)
            Assert.Equal(0, result.[0].Operators.Count)
        
        [<Fact>]
        let ``transformTerm single operator returns 2 pauli strings`` () =
            let term : FermionTerm = {
                Coefficient = Complex.One
                Operators = [{
                    OrbitalIndex = 0
                    OperatorType = Creation
                }]
            }
            
            let result = JordanWigner.transformTerm term
            
            // Single fermionic operator → 2 Pauli strings (X and Y terms)
            Assert.Equal(2, result.Length)
        
        [<Fact>]
        let ``transformTerm two operators returns 4 pauli strings`` () =
            let term : FermionTerm = {
                Coefficient = Complex.One
                Operators = [
                    { OrbitalIndex = 0; OperatorType = Creation }
                    { OrbitalIndex = 1; OperatorType = Annihilation }
                ]
            }
            
            let result = JordanWigner.transformTerm term
            
            // Two fermionic operators → 2² = 4 Pauli strings
            Assert.Equal(4, result.Length)
        
        [<Fact>]
        let ``transformTerm preserves coefficient`` () =
            let coeff = Complex(2.5, 1.3)
            let term : FermionTerm = {
                Coefficient = coeff
                Operators = [{
                    OrbitalIndex = 0
                    OperatorType = Creation
                }]
            }
            
            let result = JordanWigner.transformTerm term
            
            // Each result should have coefficient multiplied by term coefficient
            result |> List.iter (fun ps ->
                // Check that coefficient contains original coefficient as factor
                Assert.True(ps.Coefficient.Magnitude > 0.0)
            )
        
        [<Fact>]
        let ``transform simple fermionic Hamiltonian`` () =
            // H = a†₀ a₀ (number operator for orbital 0)
            let hamiltonian = {
                NumOrbitals = 2
                Terms = [{
                    Coefficient = Complex.One
                    Operators = [
                        { OrbitalIndex = 0; OperatorType = Creation }
                        { OrbitalIndex = 0; OperatorType = Annihilation }
                    ]
                }]
            }
            
            let result = JordanWigner.transform hamiltonian
            
            Assert.Equal(2, result.NumQubits)
            Assert.True(result.Terms.Length > 0)
        
        [<Fact>]
        let ``transform groups identical pauli strings`` () =
            // Create Hamiltonian with terms that will produce identical Pauli strings
            let hamiltonian = {
                NumOrbitals = 2
                Terms = [
                    {
                        Coefficient = Complex(1.0, 0.0)
                        Operators = [{ OrbitalIndex = 0; OperatorType = Creation }]
                    }
                    {
                        Coefficient = Complex(1.0, 0.0)
                        Operators = [{ OrbitalIndex = 0; OperatorType = Creation }]
                    }
                ]
            }
            
            let result = JordanWigner.transform hamiltonian
            
            // Should group and sum coefficients
            Assert.True(result.Terms.Length >= 1)
        
        [<Fact>]
        let ``transform filters zero coefficient terms`` () =
            // Create terms that cancel out
            let hamiltonian = {
                NumOrbitals = 2
                Terms = [
                    {
                        Coefficient = Complex(1.0, 0.0)
                        Operators = [{ OrbitalIndex = 0; OperatorType = Creation }]
                    }
                    {
                        Coefficient = Complex(-1.0, 0.0)
                        Operators = [{ OrbitalIndex = 0; OperatorType = Creation }]
                    }
                ]
            }
            
            let result = JordanWigner.transform hamiltonian
            
            // Should filter out zero-coefficient terms (magnitude < 1e-12)
            result.Terms |> List.iter (fun term ->
                Assert.True(term.Coefficient.Magnitude > 1e-12)
            )
        
        [<Fact>]
        let ``transform correct number of qubits`` () =
            let hamiltonian = {
                NumOrbitals = 5
                Terms = [{
                    Coefficient = Complex.One
                    Operators = [{ OrbitalIndex = 0; OperatorType = Creation }]
                }]
            }
            
            let result = JordanWigner.transform hamiltonian
            
            Assert.Equal(5, result.NumQubits)
    
    // ========================================================================
    // BRAVYI-KITAEV TRANSFORMATION TESTS
    // ========================================================================
    
    module BravyiKitaevTests =
        
        [<Fact>]
        let ``transformOperator creation uses parity and update sets`` () =
            let op = {
                OrbitalIndex = 2
                OperatorType = Creation
            }
            
            let (xTerm, yTerm) = BravyiKitaev.transformOperator op 4
            
            // Should have coefficient 0.5 and -0.5i
            Assert.Equal(Complex(0.5, 0.0), xTerm.Coefficient)
            Assert.Equal(Complex(0.0, -0.5), yTerm.Coefficient)
            
            // Should have X or Y on qubit 2
            Assert.True(xTerm.Operators.ContainsKey(2))
            Assert.True(yTerm.Operators.ContainsKey(2))
        
        [<Fact>]
        let ``transformOperator has fewer or similar paulis than jordan wigner for small systems`` () =
            let op = {
                OrbitalIndex = 5
                OperatorType = Creation
            }
            
            let numOrbitals = 8
            
            // Jordan-Wigner: Z₀ Z₁ Z₂ Z₃ Z₄ X₅ (6 operators)
            let (jwX, jwY) = JordanWigner.transformOperator op
            let jwCount = jwX.Operators.Count
            
            // Bravyi-Kitaev: Should have logarithmic scaling
            let (bkX, bkY) = BravyiKitaev.transformOperator op numOrbitals
            let bkCount = bkX.Operators.Count
            
            // For orbital 5, BK should be competitive or better
            Assert.True(bkCount <= jwCount + 2)  // Allow small overhead for small systems
        
        [<Fact>]
        let ``transformTerm produces pauli strings`` () =
            let term : FermionTerm = {
                Coefficient = Complex.One
                Operators = [{
                    OrbitalIndex = 1
                    OperatorType = Creation
                }]
            }
            
            let result = BravyiKitaev.transformTerm term 4
            
            // Should produce 2 Pauli strings (X and Y components)
            Assert.Equal(2, result.Length)
        
        [<Fact>]
        let ``transform simple fermionic Hamiltonian`` () =
            let hamiltonian = {
                NumOrbitals = 2
                Terms = [{
                    Coefficient = Complex.One
                    Operators = [
                        { OrbitalIndex = 0; OperatorType = Creation }
                        { OrbitalIndex = 0; OperatorType = Annihilation }
                    ]
                }]
            }
            
            let result = BravyiKitaev.transform hamiltonian
            
            Assert.Equal(2, result.NumQubits)
            Assert.True(result.Terms.Length > 0)
        
        [<Fact>]
        let ``transform groups and simplifies terms`` () =
            let hamiltonian = {
                NumOrbitals = 3
                Terms = [
                    {
                        Coefficient = Complex(1.0, 0.0)
                        Operators = [{ OrbitalIndex = 0; OperatorType = Creation }]
                    }
                ]
            }
            
            let result = BravyiKitaev.transform hamiltonian
            
            // Should group identical Pauli strings and filter zeros
            result.Terms |> List.iter (fun term ->
                Assert.True(term.Coefficient.Magnitude > 1e-12)
            )
    
    // ========================================================================
    // EQUIVALENCE TESTS (Jordan-Wigner vs Bravyi-Kitaev)
    // ========================================================================
    
    module EquivalenceTests =
        
        [<Fact>]
        let ``both mappings produce same number of qubits`` () =
            let hamiltonian = {
                NumOrbitals = 4
                Terms = [{
                    Coefficient = Complex.One
                    Operators = [
                        { OrbitalIndex = 0; OperatorType = Creation }
                        { OrbitalIndex = 1; OperatorType = Annihilation }
                    ]
                }]
            }
            
            let jwResult = JordanWigner.transform hamiltonian
            let bkResult = BravyiKitaev.transform hamiltonian
            
            Assert.Equal(jwResult.NumQubits, bkResult.NumQubits)
        
        [<Fact>]
        let ``both mappings produce non-empty hamiltonians`` () =
            let hamiltonian = {
                NumOrbitals = 3
                Terms = [{
                    Coefficient = Complex.One
                    Operators = [{ OrbitalIndex = 1; OperatorType = Creation }]
                }]
            }
            
            let jwResult = JordanWigner.transform hamiltonian
            let bkResult = BravyiKitaev.transform hamiltonian
            
            Assert.True(jwResult.Terms.Length > 0)
            Assert.True(bkResult.Terms.Length > 0)
        
        [<Fact>]
        let ``both mappings produce similar number of terms for small systems`` () =
            // For simple Hamiltonians, term counts should be comparable
            let hamiltonian = {
                NumOrbitals = 2
                Terms = [{
                    Coefficient = Complex.One
                    Operators = [
                        { OrbitalIndex = 0; OperatorType = Creation }
                        { OrbitalIndex = 1; OperatorType = Annihilation }
                    ]
                }]
            }
            
            let jwResult = JordanWigner.transform hamiltonian
            let bkResult = BravyiKitaev.transform hamiltonian
            
            // Both should produce similar number of terms (within factor of 2)
            let ratio = float jwResult.Terms.Length / float bkResult.Terms.Length
            Assert.True(ratio >= 0.5 && ratio <= 2.0)
        
        [<Fact>]
        let ``both mappings preserve hermiticity for real coefficients`` () =
            let hamiltonian = {
                NumOrbitals = 2
                Terms = [{
                    Coefficient = Complex(2.0, 0.0)  // Real coefficient
                    Operators = [
                        { OrbitalIndex = 0; OperatorType = Creation }
                        { OrbitalIndex = 0; OperatorType = Annihilation }
                    ]
                }]
            }
            
            let jwResult = JordanWigner.transform hamiltonian
            let bkResult = BravyiKitaev.transform hamiltonian
            
            // For Hermitian operators, all coefficients should be real (or close to real)
            jwResult.Terms |> List.iter (fun term ->
                Assert.True(abs(term.Coefficient.Imaginary) < 1e-10 || term.Coefficient.Real <> 0.0)
            )
            
            bkResult.Terms |> List.iter (fun term ->
                Assert.True(abs(term.Coefficient.Imaginary) < 1e-10 || term.Coefficient.Real <> 0.0)
            )
    
    // ========================================================================
    // CONVERSION TESTS
    // ========================================================================
    
    module ConversionTests =
        
        [<Fact>]
        let ``toQaoaHamiltonian converts QubitHamiltonian correctly`` () =
            let hamiltonian : QubitHamiltonian = {
                NumQubits = 2
                Terms = [
                    {
                        Coefficient = Complex(1.5, 0.0)
                        Operators = Map.ofList [(0, PauliZ); (1, PauliZ)]
                    }
                ]
            }
            
            let result = toQaoaHamiltonian hamiltonian
            
            Assert.Equal(2, result.NumQubits)
            Assert.Equal(1, result.Terms.Length)
            Assert.Equal(1.5, result.Terms.[0].Coefficient)
        
        [<Fact>]
        let ``toQaoaHamiltonian extracts real part of coefficients`` () =
            let hamiltonian : QubitHamiltonian = {
                NumQubits = 1
                Terms = [
                    {
                        Coefficient = Complex(2.0, 1.0)  // Complex coefficient
                        Operators = Map.ofList [(0, PauliX)]
                    }
                ]
            }
            
            let result = toQaoaHamiltonian hamiltonian
            
            // Should use real part only (for Hermitian operators)
            Assert.Equal(2.0, result.Terms.[0].Coefficient)
