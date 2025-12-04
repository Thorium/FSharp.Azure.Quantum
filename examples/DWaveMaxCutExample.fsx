/// End-to-End MaxCut Example with D-Wave Backend
///
/// This example demonstrates:
/// 1. Building a MaxCut problem (graph partitioning)
/// 2. Encoding as QAOA circuit
/// 3. Automatic backend selection (D-Wave for annealing)
/// 4. Execution and result interpretation
///
/// Run with: dotnet fsi examples/DWaveMaxCutExample.fsx

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open FSharp.Azure.Quantum.Core.QaoaCircuit
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Backends.DWaveBackend
open FSharp.Azure.Quantum.Backends.DWaveTypes
open FSharp.Azure.Quantum.Backends.BackendCapabilityDetection

printfn "╔══════════════════════════════════════════════════════════╗"
printfn "║  MaxCut Example with D-Wave Annealing Backend           ║"
printfn "╚══════════════════════════════════════════════════════════╝"
printfn ""

// ============================================================================
// STEP 1: Define the Graph
// ============================================================================

printfn "📊 Step 1: Define the Graph"
printfn "────────────────────────────"

// Simple triangle graph with weighted edges
// Vertices: 0, 1, 2
// Edges: (0,1,5.0), (1,2,3.0), (0,2,4.0)
//
//     0
//    /|\
//   5 | 4
//  /  |  \
// 1---2---2
//    3
//
// Goal: Partition vertices to maximize cut weight

let edges = [
    (0, 1, 5.0)  // Edge between vertices 0 and 1, weight 5
    (1, 2, 3.0)  // Edge between vertices 1 and 2, weight 3
    (0, 2, 4.0)  // Edge between vertices 0 and 2, weight 4
]

printfn "Graph:"
printfn "  Vertices: 3"
printfn "  Edges:"
for (u, v, w) in edges do
    printfn $"    ({u}, {v}) = {w}"
printfn ""

// ============================================================================
// STEP 2: Build MaxCut QAOA Circuit
// ============================================================================

printfn "🔧 Step 2: Build QAOA Circuit"
printfn "────────────────────────────"

// Convert MaxCut to QUBO (Quadratic Unconstrained Binary Optimization)
// For MaxCut, QUBO diagonal terms are edge weights, off-diagonal are -edge_weight
let buildMaxCutHamiltonian (numVertices: int) (edges: (int * int * float) list) : ProblemHamiltonian =
    // Calculate diagonal terms (sum of edge weights for each vertex)
    let diagonalTerms =
        [0 .. numVertices - 1]
        |> List.map (fun v ->
            let weight = 
                edges 
                |> List.filter (fun (u, w, _) -> u = v || w = v)
                |> List.sumBy (fun (_, _, w) -> w)
            { Coefficient = weight / 2.0; QubitsIndices = [| v |]; PauliOperators = [| PauliZ |] }
        )
    
    // Off-diagonal terms (negative edge weights for interaction)
    let offDiagonalTerms =
        edges
        |> List.map (fun (u, v, w) ->
            { Coefficient = -w / 4.0; QubitsIndices = [| u; v |]; PauliOperators = [| PauliZ; PauliZ |] }
        )
    
    {
        NumQubits = numVertices
        Terms = List.append diagonalTerms offDiagonalTerms |> List.toArray
    }

let numVertices = 3
let problemHamiltonian = buildMaxCutHamiltonian numVertices edges
let mixerHamiltonian = MixerHamiltonian.create numVertices

// QAOA parameters (gamma, beta) for p=1 level
let qaoaParameters = [| (0.5, 0.3) |]

let qaoaCircuit = QaoaCircuit.build problemHamiltonian mixerHamiltonian qaoaParameters
let circuit = QaoaCircuitWrapper(qaoaCircuit) :> ICircuit

printfn $"Circuit built: {circuit.NumQubits} qubits"
printfn $"QAOA depth: p={qaoaParameters.Length}"
printfn ""

// ============================================================================
// STEP 3: Automatic Backend Selection
// ============================================================================

printfn "🤖 Step 3: Automatic Backend Selection"
printfn "────────────────────────────────────────"

// Show backend recommendations
printBackendRecommendations circuit

// Automatically select best backend
let backends = createDefaultBackendPool()
match selectBestBackend backends circuit with
| Error e -> 
    printfn $"❌ Error: {e}"
    exit 1
| Ok backend ->
    printfn $"✅ Selected: {backend.Name}"
    printfn ""
    
    // ============================================================================
    // STEP 4: Execute on D-Wave Backend
    // ============================================================================
    
    printfn "⚡ Step 4: Execute on Backend"
    printfn "────────────────────────────"
    
    let numShots = 1000
    printfn $"Executing {numShots} shots..."
    
    match backend.Execute circuit numShots with
    | Error e ->
        printfn $"❌ Execution error: {e}"
        exit 1
    | Ok execResult ->
        printfn $"✅ Execution complete: {execResult.NumShots} shots"
        printfn $"   Backend: {execResult.BackendName}"
        printfn ""
        
        // ============================================================================
        // STEP 5: Analyze Results
        // ============================================================================
        
        printfn "📈 Step 5: Analyze Results"
        printfn "───────────────────────────"
        
        // Count occurrence of each bitstring
        let counts =
            execResult.Measurements
            |> Array.countBy id
            |> Array.sortByDescending snd
        
        printfn "Top 5 solutions:"
        printfn "  Bitstring  | Count | Cut Value | Partition"
        printfn "  -----------|-------|-----------|----------"
        
        for i in 0 .. min 4 (counts.Length - 1) do
            let (bitstring, count) = counts.[i]
            
            // Calculate cut value for this partition
            let partition = bitstring
            let cutValue =
                edges
                |> List.filter (fun (u, v, _) -> partition.[u] <> partition.[v])
                |> List.sumBy (fun (_, _, w) -> w)
            
            let bitstringStr = System.String.Join("", bitstring)
            let partitionStr = 
                [0 .. numVertices - 1]
                |> List.map (fun v -> if partition.[v] = 0 then $"{v}" else $"[{v}]")
                |> String.concat " "
            
            printfn $"  {bitstringStr}       | {count,5} | {cutValue,9:F1} | {partitionStr}"
        
        printfn ""
        
        // Find best cut
        let bestSolution =
            counts
            |> Array.map (fun (bitstring, count) ->
                let cutValue =
                    edges
                    |> List.filter (fun (u, v, _) -> bitstring.[u] <> bitstring.[v])
                    |> List.sumBy (fun (_, _, w) -> w)
                (bitstring, count, cutValue)
            )
            |> Array.maxBy (fun (_, _, cutValue) -> cutValue)
        
        let (bestBitstring, bestCount, bestCut) = bestSolution
        
        printfn "╔══════════════════════════════════════════════════════════╗"
        printfn "║  Best Solution Found                                     ║"
        printfn "╚══════════════════════════════════════════════════════════╝"
        printfn ""
        let partitionStr = System.String.Join("", bestBitstring)
        printfn $"  Partition: {partitionStr}"
        printfn $"  Cut Value: {bestCut:F1} (max possible: 12.0)"
        printfn $"  Occurrences: {bestCount}/{numShots} ({100.0 * float bestCount / float numShots:F1}%%)"
        printfn ""
        
        // Show partition visually
        let set0 = [0 .. numVertices - 1] |> List.filter (fun v -> bestBitstring.[v] = 0)
        let set1 = [0 .. numVertices - 1] |> List.filter (fun v -> bestBitstring.[v] = 1)
        
        printfn "  Partition Sets:"
        let set0Str = String.concat ", " (set0 |> List.map string)
        let set1Str = String.concat ", " (set1 |> List.map string)
        printfn $"    Set 0: {{{set0Str}}}"
        printfn $"    Set 1: {{{set1Str}}}"
        printfn ""
        
        // Show cut edges
        let cutEdges = edges |> List.filter (fun (u, v, _) -> bestBitstring.[u] <> bestBitstring.[v])
        printfn "  Edges in cut:"
        for (u, v, w) in cutEdges do
            printfn $"    ({u}, {v}) weight = {w}"
        printfn ""
        
        printfn "✨ MaxCut example complete!"
        printfn ""

// ============================================================================
// STEP 6: Alternative - Manual Backend Selection
// ============================================================================

printfn "💡 Alternative: Manual Backend Selection"
printfn "──────────────────────────────────────────"
printfn ""
printfn "You can also manually create and use a specific D-Wave backend:"
printfn ""
printfn "  // Create specific D-Wave solver"
printfn "  let dwaveBackend = createMockDWaveBackend Advantage_System6_1 (Some 42)"
printfn "  "
printfn "  // Execute directly"
printfn "  let result = dwaveBackend.Execute circuit 1000"
printfn ""
printfn "Available D-Wave solvers:"
printfn "  - Advantage_System6_1:  5640 qubits (Pegasus topology)"
printfn "  - Advantage_System4_1:  5000 qubits (Pegasus topology)"
printfn "  - Advantage_System1_1:  5000 qubits (Pegasus topology)"
printfn "  - Advantage2_Prototype: 1200 qubits (Zephyr topology, next-gen)"
printfn "  - DW_2000Q_6:           2048 qubits (Chimera topology, legacy)"
printfn ""
