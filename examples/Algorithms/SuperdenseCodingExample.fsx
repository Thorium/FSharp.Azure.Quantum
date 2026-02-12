/// Superdense Coding Protocol Example
/// 
/// Demonstrates the superdense coding quantum communication protocol:
/// Send 2 classical bits by transmitting only 1 qubit, using a pre-shared
/// entangled Bell pair.
/// 
/// **Key Insight**: Superdense coding is the dual of quantum teleportation.
/// - Teleportation: 1 qubit of quantum info via 2 classical bits + 1 ebit
/// - Superdense:    2 classical bits via 1 qubit + 1 ebit
/// 
/// **Production Use Cases**:
/// - Quantum Networks (efficient classical communication over quantum channels)
/// - Quantum Communication (double channel capacity for classical bits)
/// - Quantum Dense Coding in quantum internet protocols

(*
===============================================================================
 Background Theory
===============================================================================

Superdense coding (also called "dense coding") was proposed by Bennett and
Wiesner in 1992 and experimentally demonstrated by Mattle et al. in 1996.

The protocol allows Alice to send 2 classical bits of information to Bob by
transmitting only 1 qubit, provided they share a pre-existing entangled Bell
pair |Phi+> = (|00> + |11>) / sqrt(2).

Protocol Steps:
  1. Preparation: Alice and Bob share a Bell pair |Phi+>
  2. Encoding: Alice applies one of 4 operations to her qubit:
     - 00 -> I (identity):   |Phi+> -> |Phi+> = (|00> + |11>) / sqrt(2)
     - 01 -> X (bit flip):   |Phi+> -> |Psi+> = (|01> + |10>) / sqrt(2)
     - 10 -> Z (phase flip): |Phi+> -> |Phi-> = (|00> - |11>) / sqrt(2)
     - 11 -> ZX (both):      |Phi+> -> |Psi-> = (|01> - |10>) / sqrt(2)
  3. Transmission: Alice sends her qubit to Bob (1 qubit carries 2 bits!)
  4. Decoding: Bob performs Bell measurement (CNOT -> H -> Measure)
  5. Result: Bob recovers the 2 classical bits

Key Equations:
  - Holevo bound: 1 qubit can carry at most 1 classical bit (without entanglement)
  - With pre-shared entanglement: 1 qubit can carry 2 classical bits
  - This does NOT violate Holevo's theorem (entanglement is pre-shared resource)

References:
  [1] Bennett, Wiesner, "Communication via one- and two-particle operators on
      Einstein-Podolsky-Rosen states", Phys. Rev. Lett. 69, 2881 (1992).
  [2] Mattle et al., "Dense Coding in Experimental Quantum Communication",
      Phys. Rev. Lett. 76, 4656 (1996).
  [3] Nielsen & Chuang, "Quantum Computation and Quantum Information",
      Cambridge University Press (2010), Section 2.3.
  [4] Wikipedia: Superdense_coding
      https://en.wikipedia.org/wiki/Superdense_coding
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open FSharp.Azure.Quantum.Algorithms.SuperdenseCoding
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core.BackendAbstraction

printfn "================================================================"
printfn "  Superdense Coding Protocol - Quantum Communication Demo"
printfn "================================================================"
printfn ""

// Create a local quantum backend (noise-free simulator)
let backend : IQuantumBackend = LocalBackend() :> IQuantumBackend

printfn "Backend: %s" backend.Name
printfn ""

// ============================================================================
// DEMO 1: Send all 4 possible 2-bit messages
// ============================================================================

printfn "--- Demo 1: All 4 Message Encodings ---"
printfn ""

let messages : ClassicalMessage list = [
    { Bit1 = 0; Bit2 = 0 }
    { Bit1 = 0; Bit2 = 1 }
    { Bit1 = 1; Bit2 = 0 }
    { Bit1 = 1; Bit2 = 1 }
]

for msg in messages do
    match send backend msg with
    | Error err -> printfn "  ERROR sending %d%d: %A" msg.Bit1 msg.Bit2 err
    | Ok result ->
        let encoding =
            match (msg.Bit1, msg.Bit2) with
            | (0, 0) -> "I (identity)"
            | (0, 1) -> "X (bit flip)"
            | (1, 0) -> "Z (phase flip)"
            | (1, 1) -> "ZX (both)"
            | _ -> "?"
        let status = if result.Success then "OK" else "FAIL"
        printfn "  Send %d%d [%s] -> Received %d%d  [%s]"
            msg.Bit1 msg.Bit2 encoding
            result.ReceivedMessage.Bit1 result.ReceivedMessage.Bit2
            status

printfn ""

// ============================================================================
// DEMO 2: Convenience functions
// ============================================================================

printfn "--- Demo 2: Convenience Functions ---"
printfn ""

let demos = [
    ("send00", send00 backend)
    ("send01", send01 backend)
    ("send10", send10 backend)
    ("send11", send11 backend)
]

for (name, result) in demos do
    match result with
    | Error err -> printfn "  %s: ERROR %A" name err
    | Ok r -> printfn "  %s: sent %d%d -> received %d%d (success=%b)"
                name r.SentMessage.Bit1 r.SentMessage.Bit2
                r.ReceivedMessage.Bit1 r.ReceivedMessage.Bit2 r.Success

printfn ""

// ============================================================================
// DEMO 3: Statistical verification
// ============================================================================

printfn "--- Demo 3: Statistical Verification (100 trials) ---"
printfn ""

let testMessage : ClassicalMessage = { Bit1 = 1; Bit2 = 1 }

match runStatistics backend testMessage 100 with
| Error err -> printfn "  ERROR: %A" err
| Ok stats ->
    printfn "%s" (formatStatistics stats)

printfn ""

// ============================================================================
// DEMO 4: Formatted output
// ============================================================================

printfn "--- Demo 4: Formatted Result ---"
printfn ""

match send backend { Bit1 = 1; Bit2 = 0 } with
| Error err -> printfn "  ERROR: %A" err
| Ok result -> printfn "%s" (formatResult result)

printfn ""
printfn "================================================================"
printfn "  Superdense Coding Demo Complete"
printfn "================================================================"
