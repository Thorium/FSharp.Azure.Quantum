namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.QuantumArithmeticOps

/// Unit tests for QuantumArithmeticBuilder
/// Tests the computation expression builder and QFT-based arithmetic operations
module QuantumArithmeticBuilderTests =
    
    // ========================================================================
    // BUILDER VALIDATION TESTS
    // ========================================================================
    
    // NOTE: The builder provides sensible defaults (0+0 with 8 qubits, Add operation)
    // This follows F# conventions for computation expressions and improves UX.
    // Tests focus on meaningful validation failures, not missing optional fields.
    
    [<Fact>]
    let ``quantumArithmetic builder rejects negative operands`` () =
        let result = quantumArithmetic {
            operands -5 10
            operation Add
            qubits 8
        }
        
        match result with
        | Error msg -> Assert.Contains("non-negative", msg)
        | Ok _ -> Assert.True(false, "Should have rejected negative operands")
    
    [<Fact>]
    let ``quantumArithmetic builder rejects insufficient qubits`` () =
        let result = quantumArithmetic {
            operands 42 17
            operation Add
            qubits 1
        }
        
        match result with
        | Error msg -> Assert.Contains("At least 2", msg)
        | Ok _ -> Assert.True(false, "Should have rejected insufficient qubits")
    
    [<Fact>]
    let ``quantumArithmetic builder rejects excessive qubits`` () =
        let result = quantumArithmetic {
            operands 42 17
            operation Add
            qubits 20
        }
        
        match result with
        | Error msg -> Assert.Contains("exceeds maximum", msg)
        | Ok _ -> Assert.True(false, "Should have rejected excessive qubits")
    
    [<Fact>]
    let ``quantumArithmetic builder requires modulus for modular operations`` () =
        let result = quantumArithmetic {
            operands 5 3
            operation ModularAdd
            qubits 8
        }
        
        match result with
        | Error msg -> Assert.Contains("Modulus is required", msg)
        | Ok _ -> Assert.True(false, "Should have required modulus")
    
    [<Fact>]
    let ``quantumArithmetic builder validates operands fit in qubits`` () =
        let result = quantumArithmetic {
            operands 300 17  // 300 doesn't fit in 8 qubits (max 255)
            operation Add
            qubits 8
        }
        
        match result with
        | Error msg -> Assert.Contains("requires more than", msg)
        | Ok _ -> Assert.True(false, "Should have rejected operand too large for qubits")
    
    [<Fact>]
    let ``quantumArithmetic builder validates operands smaller than modulus`` () =
        let result = quantumArithmetic {
            operands 50 30
            operation ModularAdd
            modulus 40
            qubits 8
        }
        
        match result with
        | Error msg -> Assert.Contains("smaller than modulus", msg)
        | Ok _ -> Assert.True(false, "Should have rejected operands >= modulus")
    
    [<Fact>]
    let ``quantumArithmetic builder accepts valid operation with explicit values`` () =
        // Test that a minimal valid operation works
        // Using explicit values: 0+0 with 8 qubits (trivial but valid)
        let result = quantumArithmetic {
            operands 0 0
            operation Add
            qubits 8
        }
        
        match result with
        | Ok op ->
            Assert.Equal(0, op.OperandA)
            Assert.Equal(0, op.OperandB)
            Assert.Equal(Add, op.Operation)
            Assert.Equal(8, op.Qubits)
        | Error msg -> Assert.True(false, $"Should have accepted valid operation: {msg}")
    
    // ========================================================================
    // ARITHMETIC CORRECTNESS TESTS
    // ========================================================================
    
    [<Fact>]
    let ``add operation produces correct result`` () =
        let result =
            quantumArithmetic {
                operands 42 17
                operation Add
                qubits 8
            }
            |> Result.bind execute
        
        match result with
        | Ok res ->
            Assert.Equal(59, res.Value)
            Assert.Equal(OperationType.Add, res.OperationType)
            Assert.False(res.IsModular)
        | Error msg -> Assert.True(false, $"Operation failed: {msg}")
    
    [<Fact>]
    let ``multiply operation produces correct result`` () =
        let result =
            quantumArithmetic {
                operands 7 6
                operation Multiply
                qubits 8
            }
            |> Result.bind execute
        
        match result with
        | Ok res ->
            Assert.Equal(42, res.Value)
            Assert.Equal(OperationType.Multiply, res.OperationType)
        | Error msg -> Assert.True(false, $"Operation failed: {msg}")
    
    [<Fact>]
    let ``modular add operation produces correct result`` () =
        let result =
            quantumArithmetic {
                operands 10 7
                operation ModularAdd
                modulus 12
                qubits 8
            }
            |> Result.bind execute
        
        match result with
        | Ok res ->
            Assert.Equal(5, res.Value)  // (10 + 7) mod 12 = 5
            Assert.True(res.IsModular)
        | Error msg -> Assert.True(false, $"Operation failed: {msg}")
    
    [<Fact>]
    let ``modular multiply operation produces correct result`` () =
        let result =
            quantumArithmetic {
                operands 7 5
                operation ModularMultiply
                modulus 11
                qubits 8
            }
            |> Result.bind execute
        
        match result with
        | Ok res ->
            Assert.Equal(2, res.Value)  // (7 * 5) mod 11 = 2
            Assert.True(res.IsModular)
        | Error msg -> Assert.True(false, $"Operation failed: {msg}")
    
    [<Fact>]
    let ``modular exponentiate operation produces correct result`` () =
        let result =
            quantumArithmetic {
                operands 3 4
                operation ModularExponentiate
                modulus 7
                qubits 8
            }
            |> Result.bind execute
        
        match result with
        | Ok res ->
            Assert.Equal(4, res.Value)  // (3^4) mod 7 = 81 mod 7 = 4
            Assert.True(res.IsModular)
        | Error msg -> Assert.True(false, $"Operation failed: {msg}")
    
    [<Fact>]
    let ``add convenience helper works correctly`` () =
        let result = 
            add 25 30 8
            |> execute
        
        match result with
        | Ok res ->
            Assert.Equal(55, res.Value)
            Assert.Equal(OperationType.Add, res.OperationType)
        | Error msg -> Assert.True(false, $"Operation failed: {msg}")
    
    [<Fact>]
    let ``modularAdd convenience helper works correctly`` () =
        let result = 
            modularAdd 15 20 25 8
            |> execute
        
        match result with
        | Ok res ->
            Assert.Equal(10, res.Value)  // (15 + 20) mod 25 = 10
            Assert.True(res.IsModular)
        | Error msg -> Assert.True(false, $"Operation failed: {msg}")
    
    [<Fact>]
    let ``modularMultiply convenience helper works correctly`` () =
        let result = 
            modularMultiply 6 8 13 8
            |> execute
        
        match result with
        | Ok res ->
            Assert.Equal(9, res.Value)  // (6 * 8) mod 13 = 48 mod 13 = 9
            Assert.True(res.IsModular)
        | Error msg -> Assert.True(false, $"Operation failed: {msg}")
    
    [<Fact>]
    let ``modularExponentiate convenience helper works correctly`` () =
        let result = 
            modularExponentiate 2 5 11 8
            |> execute
        
        match result with
        | Ok res ->
            Assert.Equal(10, res.Value)  // (2^5) mod 11 = 32 mod 11 = 10
            Assert.True(res.IsModular)
        | Error msg -> Assert.True(false, $"Operation failed: {msg}")
    
    // ========================================================================
    // CRYPTOGRAPHY-RELEVANT TESTS
    // ========================================================================
    
    [<Fact>]
    let ``modular exponentiation for RSA encryption`` () =
        // RSA Toy Example: Encrypt message m=7 with public key (e=5, n=33)
        // where n = p*q = 11*3, and e=5 is coprime to φ(n)=(11-1)*(3-1)=20
        // Ciphertext c = m^e mod n = 7^5 mod 33 = 16807 mod 33 = 10
        let result =
            quantumArithmetic {
                operands 7 5      // message=7, public_exponent=5
                operation ModularExponentiate
                modulus 33        // n=33 (p*q = 11*3)
                qubits 8
            }
            |> Result.bind execute
        
        match result with
        | Ok res -> 
            Assert.Equal(10, res.Value)  // Encrypted ciphertext
            Assert.True(res.IsModular)
            Assert.Equal(OperationType.ModularExponentiate, res.OperationType)
        | Error msg -> Assert.True(false, $"RSA encryption failed: {msg}")
    
    [<Fact>]
    let ``modular exponentiation for Shors algorithm period finding`` () =
        // Shor's Algorithm: Find period of a^x mod N
        // Example: a=2, N=15 (composite number to factor)
        // Testing 2^4 mod 15 = 16 mod 15 = 1 (period r=4)
        let result =
            quantumArithmetic {
                operands 2 4      // base=2, exponent=4
                operation ModularExponentiate
                modulus 15        // N=15 (to be factored into 3*5)
                qubits 8
            }
            |> Result.bind execute
        
        match result with
        | Ok res -> 
            Assert.Equal(1, res.Value)  // 2^4 mod 15 = 1 (found period!)
            Assert.True(res.IsModular)
        | Error msg -> Assert.True(false, $"Shor's algorithm test failed: {msg}")
    
    [<Fact>]
    let ``modular multiplication for discrete logarithm problem`` () =
        // Discrete Log Example: Compute g^a * g^b mod p
        // Using additive property: g^a * g^b = g^(a+b) mod p
        // Simplified: Just test modular multiplication for DLP building block
        let result =
            quantumArithmetic {
                operands 5 6      // Two group elements
                operation ModularMultiply
                modulus 23        // Prime modulus (order of group)
                qubits 8
            }
            |> Result.bind execute
        
        match result with
        | Ok res -> 
            Assert.Equal(7, res.Value)  // (5 * 6) mod 23 = 30 mod 23 = 7
            Assert.True(res.IsModular)
        | Error msg -> Assert.True(false, $"DLP multiplication failed: {msg}")
    
    // ========================================================================
    // EDGE CASES AND BOUNDARY TESTS
    // ========================================================================
    
    [<Fact>]
    let ``handles zero operands correctly`` () =
        let result =
            quantumArithmetic {
                operands 0 5
                operation Add
                qubits 8
            }
            |> Result.bind execute
        
        match result with
        | Ok res -> Assert.Equal(5, res.Value)
        | Error msg -> Assert.True(false, $"Operation failed: {msg}")
    
    [<Fact>]
    let ``handles identity operations`` () =
        let addZeroResult =
            quantumArithmetic {
                operands 42 0
                operation Add
                qubits 8
            }
            |> Result.bind execute
        
        let multiplyOneResult =
            quantumArithmetic {
                operands 42 1
                operation Multiply
                qubits 8
            }
            |> Result.bind execute
        
        match addZeroResult, multiplyOneResult with
        | Ok add, Ok mul ->
            Assert.Equal(42, add.Value)
            Assert.Equal(42, mul.Value)
        | _ -> Assert.True(false, "Operations failed")
    
    [<Fact>]
    let ``modular operation with result equal to modulus`` () =
        let result =
            quantumArithmetic {
                operands 8 7
                operation ModularAdd
                modulus 15
                qubits 8
            }
            |> Result.bind execute
        
        match result with
        | Ok res -> Assert.Equal(0, res.Value)  // (8 + 7) mod 15 = 15 mod 15 = 0
        | Error msg -> Assert.True(false, $"Operation failed: {msg}")
    
    // ========================================================================
    // RESULT METADATA TESTS
    // ========================================================================
    
    [<Fact>]
    let ``result includes correct metadata`` () =
        let result =
            quantumArithmetic {
                operands 10 5
                operation Multiply
                qubits 8
            }
            |> Result.bind execute
        
        match result with
        | Ok res ->
            Assert.Equal(50, res.Value)
            Assert.Equal(8, res.QubitsUsed)
            Assert.True(res.GateCount > 0)
            Assert.True(res.CircuitDepth > 0)
            Assert.NotEmpty(res.BackendName)
            Assert.False(res.IsModular)
        | Error msg -> Assert.True(false, $"Operation failed: {msg}")
    
    [<Fact>]
    let ``modular operation metadata includes modular flag`` () =
        let result =
            quantumArithmetic {
                operands 5 3
                operation ModularAdd
                modulus 7
                qubits 8
            }
            |> Result.bind execute
        
        match result with
        | Ok res ->
            Assert.True(res.IsModular)
            Assert.Equal(OperationType.ModularAdd, res.OperationType)
        | Error msg -> Assert.True(false, $"Operation failed: {msg}")
