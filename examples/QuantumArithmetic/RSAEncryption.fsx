// RSA Encryption Example - Quantum Arithmetic for Cryptography
// Demonstrates modular exponentiation using quantum circuits (QFT-based)

// Reference local build (use this for development/testing)
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

// Or use published NuGet package (uncomment when package is published):
// #r "nuget: FSharp.Azure.Quantum"

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.QuantumArithmeticOps

printfn "=== RSA Encryption Demo (Toy Example) ==="
printfn ""
printfn "BUSINESS SCENARIO:"
printfn "A secure messaging application needs to encrypt messages using RSA."
printfn "This demonstrates the core RSA operation: m^e mod n"
printfn ""

// ============================================================================
// RSA KEY SETUP (Toy Example - Real RSA uses 2048+ bit keys)
// ============================================================================

printfn "--- Step 1: RSA Key Setup ---"
let p = 3  // Prime 1 (toy example)
let q = 11 // Prime 2 (toy example)
let n = p * q  // RSA modulus n = 33
let phi = (p - 1) * (q - 1)  // φ(n) = 20

// Public key exponent (commonly e = 65537 in real RSA, but we use e = 3)
let e = 3

printfn "RSA Modulus (n):        %d (public)" n
printfn "Public Exponent (e):    %d (public)" e
printfn "Factorization (p, q):   %d, %d (private - must stay secret!)" p q
printfn ""

// ============================================================================
// ENCRYPT MESSAGE USING QUANTUM ARITHMETIC
// ============================================================================

printfn "--- Step 2: Encrypt Message ---"

let message = 5  // Message to encrypt (plaintext)
printfn "Original Message (m):   %d" message
printfn "Encryption Formula:     c = m^e mod n"
printfn "                        c = %d^%d mod %d" message e n
printfn ""

// Use quantum arithmetic builder for modular exponentiation
let encryptOperation = quantumArithmetic {
    operands message e      // base = message, exponent = e
    operation ModularExponentiate
    modulus n               // RSA modulus
    qubits 8                // 8 qubits sufficient for n = 33
}

printfn "Executing quantum circuit..."
match encryptOperation with
| Ok op ->
    match execute op with
    | Ok result ->
        let ciphertext = result.Value
        printfn "✅ Encrypted Message (c): %d" ciphertext
        printfn ""
        printfn "CIRCUIT STATISTICS:"
        printfn "  Qubits Used:     %d" result.QubitsUsed
        printfn "  Gate Count:      %d" result.GateCount
        printfn "  Circuit Depth:   %d" result.CircuitDepth
        printfn ""
        
        // ============================================================================
        // DECRYPT MESSAGE (Classical for comparison)
        // ============================================================================
        
        printfn "--- Step 3: Decrypt Message (Classical) ---"
        
        // Calculate private exponent d where (e * d) mod φ(n) = 1
        let d = 7  // Precomputed: 3 * 7 = 21 ≡ 1 (mod 20)
        
        printfn "Private Exponent (d):   %d (secret)" d
        printfn "Decryption Formula:     m = c^d mod n"
        printfn "                        m = %d^%d mod %d" ciphertext d n
        
        // Classical modular exponentiation for decryption
        let decrypted = 
            let mutable result = 1
            for _ in 1..d do
                result <- (result * ciphertext) % n
            result
        
        printfn "Decrypted Message:      %d" decrypted
        printfn ""
        
        if decrypted = message then
            printfn "✅ SUCCESS: Decryption matches original message!"
        else
            printfn "❌ ERROR: Decryption failed!"
        
        printfn ""
        printfn "=== Key Takeaways ==="
        printfn "• RSA security relies on difficulty of factoring n = p × q"
        printfn "• Quantum computers (Shor's algorithm) can break RSA efficiently"
        printfn "• This example uses quantum circuits for the encryption operation m^e mod n"
        printfn "• Real RSA uses 2048+ bit keys (requires ~4096+ qubits on quantum hardware)"
        printfn "• Current NISQ hardware limited to ~100 qubits, so only toy examples work"

    | Error err ->
        printfn "❌ Execution Error: %s" err.Message

| Error err ->
    printfn "❌ Builder Error: %s" err.Message
