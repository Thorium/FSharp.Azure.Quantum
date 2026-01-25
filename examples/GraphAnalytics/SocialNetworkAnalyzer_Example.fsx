// ============================================================================
// Social Network Analyzer Example
// ============================================================================
//
// Demonstrates using quantum Grover's algorithm to find tight-knit communities
// (cliques) in social networks.
//
// Business Use Cases:
// - Marketing: Identify influencer groups for targeted campaigns
// - Security: Detect fraud rings through connection patterns
// - HR: Analyze team collaboration and communication networks
// - Healthcare: Track disease outbreak clusters
//
// This example shows how to:
// 1. Build a social network with people and connections
// 2. Use quantum acceleration to find communities
// 3. Compare with classical algorithm
// ============================================================================

// Use local build for development
#I "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0"
#r "FSharp.Azure.Quantum.dll"

// For published package, use instead:
// #r "nuget: FSharp.Azure.Quantum"

open System
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Business

// ============================================================================
// Example 1: Small Network - Classical Algorithm
// ============================================================================

printfn "=== Example 1: Small Network (Classical) ==="
printfn ""

let classicalResult = SocialNetworkAnalyzer.socialNetwork {
    // Define network of 4 people
    person "Alice"
    person "Bob"
    person "Carol"
    person "Dave"
    
    // Add connections (friendships)
    connection "Alice" "Bob"
    connection "Bob" "Carol"
    connection "Carol" "Alice"  // Triangle: tight-knit group
    connection "Dave" "Alice"   // Dave connected but not in core group
    
    // Find communities of at least 3 people
    findCommunities 3
    
    // No backend = classical algorithm
}

match classicalResult with
| Ok result ->
    printfn "✓ Classical Analysis Complete"
    printfn "  Total People: %d" result.TotalPeople
    printfn "  Total Connections: %d" result.TotalConnections
    printfn "  Communities Found: %d" result.Communities.Length
    printfn "  Message: %s" result.Message
    printfn ""
    
    for comm in result.Communities do
        printfn "  Community: %A" comm.Members
        printfn "    Strength: %.2f (%.0f%% connected)" comm.Strength (comm.Strength * 100.0)
        printfn "    Internal Connections: %d" comm.InternalConnections
        printfn ""

| Error err ->
    printfn "✗ Error: %A" err
    printfn ""

// ============================================================================
// Example 2: Larger Network - Quantum Algorithm
// ============================================================================

printfn "=== Example 2: Larger Network (Quantum with Local Simulation) ==="
printfn ""

// Create local quantum backend
let localBackend = LocalBackend() :> IQuantumBackend

let quantumResult = SocialNetworkAnalyzer.socialNetwork {
    // Define a larger network (6 people)
    people ["Alice"; "Bob"; "Carol"; "Dave"; "Eve"; "Frank"]
    
    // Create two separate cliques
    // Clique 1: Alice, Bob, Carol (triangle)
    connections [
        ("Alice", "Bob")
        ("Bob", "Carol")
        ("Carol", "Alice")
    ]
    
    // Clique 2: Dave, Eve, Frank (triangle)
    connections [
        ("Dave", "Eve")
        ("Eve", "Frank")
        ("Frank", "Dave")
    ]
    
    // Bridge connection
    connection "Carol" "Dave"
    
    // Find communities of at least 3 people
    findCommunities 3
    
    // Enable quantum acceleration
    backend localBackend
    shots 1000  // Use 1000 measurement shots for accuracy
}

match quantumResult with
| Ok result ->
    printfn "✓ Quantum Analysis Complete"
    printfn "  Total People: %d" result.TotalPeople
    printfn "  Total Connections: %d" result.TotalConnections
    printfn "  Communities Found: %d" result.Communities.Length
    printfn "  Message: %s" result.Message
    printfn ""
    
    if result.Communities.Length > 0 then
        for i, comm in result.Communities |> List.indexed do
            printfn "  Community %d: %A" (i + 1) comm.Members
            printfn "    Strength: %.2f (%.0f%% connected)" comm.Strength (comm.Strength * 100.0)
            printfn "    Internal Connections: %d" comm.InternalConnections
            printfn ""
    else
        printfn "  No communities of size %d found in this network." 
            (match quantumResult with Ok r -> 3 | Error _ -> 3)
        printfn "  Try a smaller minimum community size or add more connections."
        printfn ""

| Error err ->
    printfn "✗ Error: %A" err
    printfn ""

// ============================================================================
// Example 3: Business Scenario - Fraud Detection
// ============================================================================

printfn "=== Example 3: Fraud Detection Scenario ==="
printfn ""

let fraudResult = SocialNetworkAnalyzer.socialNetwork {
    // Suspicious transaction network
    people [
        "Account_1001"
        "Account_1002"
        "Account_1003"
        "Account_1004"
        "Account_1005"
    ]
    
    // Suspicious circular transactions (potential fraud ring)
    connections [
        ("Account_1001", "Account_1002")
        ("Account_1002", "Account_1003")
        ("Account_1003", "Account_1001")  // Circular pattern
        
        // Isolated transaction
        ("Account_1004", "Account_1005")
    ]
    
    // Detect fraud rings (min 3 accounts in circular pattern)
    findCommunities 3
    
    // Use quantum for faster detection in large transaction networks
    backend localBackend
    shots 2000  // Higher accuracy for fraud detection
}

match fraudResult with
| Ok result ->
    printfn "✓ Fraud Detection Complete"
    printfn "  Accounts Analyzed: %d" result.TotalPeople
    printfn "  Transactions: %d" result.TotalConnections
    printfn ""
    
    if result.Communities.Length > 0 then
        printfn "  ⚠️  ALERT: Potential fraud rings detected!"
        for i, comm in result.Communities |> List.indexed do
            printfn ""
            printfn "  Fraud Ring %d:" (i + 1)
            printfn "    Accounts: %A" comm.Members
            printfn "    Ring Strength: %.2f" comm.Strength
            printfn "    Circular Transactions: %d" comm.InternalConnections
    else
        printfn "  ✓ No suspicious circular patterns detected"
    printfn ""

| Error err ->
    printfn "✗ Error: %A" err
    printfn ""

// ============================================================================
// Performance Notes
// ============================================================================

printfn "=== Performance Notes ==="
printfn ""
printfn "Quantum Advantage:"
printfn "  - Classical clique finding: O(2^n) time complexity"
printfn "  - Grover's algorithm: O(√(2^n)) quadratic speedup"
printfn "  - Most beneficial for networks with 20+ people"
printfn ""
printfn "Backend Options:"
printfn "  - LocalBackend: Fast simulation for development/testing"
printfn "  - IonQ, Rigetti, Quantinuum: Real quantum hardware for production"
printfn ""
printfn "Accuracy Control:"
printfn "  - shots 100-1000: Fast testing"
printfn "  - shots 1000-5000: Standard production"
printfn "  - shots 5000-10000: High accuracy for critical decisions"
printfn ""
