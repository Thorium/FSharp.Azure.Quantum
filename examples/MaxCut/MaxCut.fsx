/// MaxCut Example - Circuit Design Wire Minimization
/// 
/// USE CASE: Partition circuit blocks to minimize wire crossings
/// 
/// PROBLEM: Given a circuit design with interconnected blocks,
/// partition them into two regions to minimize communication overhead.
/// 
/// This is THE canonical QAOA problem - MaxCut is a fundamental
/// graph partitioning problem with applications in:
/// - VLSI circuit design (minimize wire crossings)
/// - Social network analysis (detect communities)
/// - Load balancing (minimize inter-server communication)
/// - Image segmentation (foreground/background separation)

(*
===============================================================================
 Background Theory
===============================================================================

The Maximum Cut (MaxCut) problem is a fundamental combinatorial optimization
problem: given a weighted graph G = (V, E, w), partition the vertices into two
disjoint sets S and T such that the sum of edge weights crossing the partition
is maximized. MaxCut is NP-hard, meaning no known classical algorithm can solve
all instances efficiently. The best classical approximation algorithm (Goemans-
Williamson, 1995) achieves a 0.878 approximation ratio using semidefinite
programming, but exact solutions require exponential time in the worst case.

The Quantum Approximate Optimization Algorithm (QAOA), introduced by Farhi et al.
(2014), is a variational quantum algorithm specifically designed for combinatorial
optimization problems like MaxCut. QAOA encodes the problem Hamiltonian H_C (cost)
and a mixer Hamiltonian H_B (driver) into alternating quantum operations. At depth
p, the ansatz is: |ψ(γ,β)⟩ = Πₖ exp(-iβₖH_B)·exp(-iγₖH_C)|+⟩ⁿ. The parameters
(γ,β) are optimized classically to maximize ⟨H_C⟩.

Key Equations:
  - MaxCut cost function: C(z) = Σ_{(i,j)∈E} wᵢⱼ·½(1 - zᵢzⱼ)  where zᵢ ∈ {±1}
  - Problem Hamiltonian: H_C = Σ_{(i,j)∈E} wᵢⱼ·½(I - ZᵢZⱼ)
  - Mixer Hamiltonian: H_B = Σᵢ Xᵢ (induces transitions between configurations)
  - QAOA depth-p ansatz: |γ,β⟩ = Πₖ₌₁ᵖ e^{-iβₖH_B} e^{-iγₖH_C} |+⟩ⁿ
  - Expected cut value: ⟨γ,β|H_C|γ,β⟩ (maximized over parameters)

Quantum Advantage:
  QAOA provides a quantum-native approach to NP-hard optimization. At depth p→∞,
  QAOA provably finds the optimal solution. For finite depth, QAOA can outperform
  classical local search on certain graph instances. On NISQ devices, QAOA at
  depth p=1-3 often matches or exceeds classical heuristics for small graphs.
  The key advantage is parallel exploration of the solution space via quantum
  superposition and interference, potentially finding high-quality solutions
  faster than classical random sampling or greedy algorithms.

References:
  [1] Farhi, Goldstone, Gutmann, "A Quantum Approximate Optimization Algorithm",
      arXiv:1411.4028 (2014). https://arxiv.org/abs/1411.4028
  [2] Goemans & Williamson, "Improved approximation algorithms for maximum cut",
      J. ACM 42(6), 1115-1145 (1995). https://doi.org/10.1145/227683.227684
  [3] Zhou et al., "Quantum Approximate Optimization Algorithm: Performance,
      Mechanism, and Implementation", Phys. Rev. X 10, 021067 (2020).
      https://doi.org/10.1103/PhysRevX.10.021067
  [4] Wikipedia: Maximum_cut
      https://en.wikipedia.org/wiki/Maximum_cut
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"


open FSharp.Azure.Quantum
open System

printfn "======================================"
printfn "MaxCut - Circuit Wire Minimization"
printfn "======================================"
printfn ""

// ============================================================================
// EXAMPLE 1: Small Circuit Design (4 blocks)
// ============================================================================

printfn "Example 1: Small Circuit with 4 Blocks"
printfn "--------------------------------------"

// Define circuit blocks
let blocks = ["CPU"; "GPU"; "RAM"; "IO"]

// Define interconnections with communication weights
// Higher weight = more data transferred between blocks
let interconnects = [
    ("CPU", "GPU", 5.0)   // Heavy compute communication
    ("CPU", "RAM", 10.0)  // Very high memory bandwidth
    ("CPU", "IO", 2.0)    // Low I/O traffic
    ("GPU", "RAM", 7.0)   // GPU memory access
    ("GPU", "IO", 1.0)    // Minimal GPU I/O
    ("RAM", "IO", 3.0)    // DMA transfers
]

// Create MaxCut problem
let circuitProblem = MaxCut.createProblem blocks interconnects

printfn "Circuit Blocks: %A" blocks
printfn "Interconnects: %d edges, total weight: %.1f" 
    circuitProblem.EdgeCount 
    (interconnects |> List.sumBy (fun (_, _, w) -> w))
printfn ""

// Solve using quantum optimization (LocalBackend simulation)
printfn "Solving with quantum QAOA..."
match MaxCut.solve circuitProblem None with
| Ok solution ->
    printfn "✓ Quantum Solution Found!"
    printfn "  Partition A (Left side): %A" solution.PartitionS
    printfn "  Partition B (Right side): %A" solution.PartitionT
    printfn "  Cut Value: %.1f (communication overhead)" solution.CutValue
    printfn "  Cut Edges: %d wires crossing partition" solution.CutEdges.Length
    printfn "  Backend: %s" solution.BackendName
    printfn ""
    
    // Show which wires cross the partition
    printfn "  Wire Crossings:"
    for edge in solution.CutEdges do
        printfn "    %s <-> %s (weight: %.1f)" edge.Source edge.Target edge.Weight
    printfn ""

| Error err ->
    printfn "✗ Quantum solve failed: %s" err.Message
    printfn ""

// ============================================================================
// EXAMPLE 2: Helper Functions - Common Graph Structures
// ============================================================================

printfn ""
printfn "Example 2: Helper Functions for Common Graphs"
printfn "----------------------------------------------"

// Complete graph (K4) - all nodes connected
printfn "Complete Graph (K4):"
let completeGraph = MaxCut.completeGraph ["A"; "B"; "C"; "D"] 1.0
printfn "  Vertices: %d, Edges: %d" completeGraph.VertexCount completeGraph.EdgeCount

match MaxCut.solve completeGraph None with
| Ok sol ->
    printfn "  Max Cut: %.0f (optimal is %d for K4)" sol.CutValue 4
    printfn "  Partition: %A | %A" sol.PartitionS sol.PartitionT
| Error _ -> ()
printfn ""

// Cycle graph (C4) - nodes in a ring
printfn "Cycle Graph (C4):"
let cycleGraph = MaxCut.cycleGraph ["A"; "B"; "C"; "D"] 1.0
printfn "  Vertices: %d, Edges: %d" cycleGraph.VertexCount cycleGraph.EdgeCount

match MaxCut.solve cycleGraph None with
| Ok sol ->
    printfn "  Max Cut: %.0f (optimal is %d for C4)" sol.CutValue 4
    printfn "  Partition: %A | %A" sol.PartitionS sol.PartitionT
| Error _ -> ()
printfn ""

// Star graph - one central hub
printfn "Star Graph (1 center, 3 spokes):"
let starGraph = MaxCut.starGraph "Hub" ["S1"; "S2"; "S3"] 1.0
printfn "  Vertices: %d, Edges: %d" starGraph.VertexCount starGraph.EdgeCount

match MaxCut.solve starGraph None with
| Ok sol ->
    printfn "  Max Cut: %.0f (optimal is %d for star)" sol.CutValue 3
    printfn "  Partition: %A | %A" sol.PartitionS sol.PartitionT
| Error _ -> ()
printfn ""

// Grid graph - 2D lattice
printfn "Grid Graph (2x3):"
let gridGraph = MaxCut.gridGraph 2 3 1.0
printfn "  Vertices: %d, Edges: %d" gridGraph.VertexCount gridGraph.EdgeCount

match MaxCut.solve gridGraph None with
| Ok sol ->
    printfn "  Max Cut: %.0f" sol.CutValue
    printfn "  Partition: %A | %A" sol.PartitionS sol.PartitionT
| Error _ -> ()
printfn ""

// ============================================================================
// EXAMPLE 3: Social Network Community Detection
// ============================================================================

printfn ""
printfn "Example 3: Social Network Community Detection"
printfn "---------------------------------------------"

let socialNetwork = [
    ("Alice", "Bob", 5.0)     // Close friends
    ("Alice", "Charlie", 3.0)
    ("Bob", "Charlie", 4.0)   
    ("David", "Eve", 6.0)     // Different group
    ("David", "Frank", 5.0)
    ("Eve", "Frank", 4.0)
    ("Charlie", "David", 1.0) // Weak bridge between groups
]

let people = ["Alice"; "Bob"; "Charlie"; "David"; "Eve"; "Frank"]
let networkProblem = MaxCut.createProblem people socialNetwork

printfn "Social Network: %d people, %d connections" 
    networkProblem.VertexCount networkProblem.EdgeCount

match MaxCut.solve networkProblem None with
| Ok solution ->
    printfn "✓ Detected Communities:"
    printfn "  Community 1: %A" solution.PartitionS
    printfn "  Community 2: %A" solution.PartitionT
    printfn "  Polarization Score: %.1f" solution.CutValue
    printfn "  (Higher score = more polarized/distinct communities)"
    printfn ""
    
    printfn "  Weak connections between communities:"
    for edge in solution.CutEdges do
        printfn "    %s <-> %s (strength: %.1f)" edge.Source edge.Target edge.Weight
| Error err ->
    printfn "✗ Failed: %s" err.Message

printfn ""

// ============================================================================
// EXAMPLE 4: Direct API Usage
// ============================================================================

printfn ""
printfn "Example 4: Simple Triangle Graph"
printfn "---------------------------------"

// Solve triangle graph
let vertices = ["X"; "Y"; "Z"]
let edges = [("X", "Y", 2.0); ("Y", "Z", 3.0); ("Z", "X", 1.0)]
let triangleProblem = MaxCut.createProblem vertices edges

match MaxCut.solve triangleProblem None with
| Ok solution ->
    printfn "Triangle graph MaxCut: %.0f" solution.CutValue
    printfn "Partition: %A | %A" solution.PartitionS solution.PartitionT
| Error err ->
    printfn "✗ Failed: %s" err.Message

printfn ""

// ============================================================================
// EXAMPLE 5: Optimal Solution for Complete Graph
// ============================================================================

printfn ""
printfn "Example 5: Complete Graph K3"
printfn "-----------------------------"

// Create complete graph K3 (triangle)
let testProblem = MaxCut.completeGraph ["A"; "B"; "C"] 1.0

// Find optimal solution using quantum QAOA
match MaxCut.solve testProblem None with
| Ok optimal ->
    printfn "Complete graph K3:"
    printfn "  Vertices: 3, Edges: 3 (all connected)"
    printfn "  Optimal cut value: %.0f" optimal.CutValue
    printfn "  Partition: %A | %A" optimal.PartitionS optimal.PartitionT
    printfn "  Cut edges: %d" optimal.CutEdges.Length
    printfn ""
    printfn "For K3, optimal MaxCut = 2 (any 2 edges can be cut)"
| Error err ->
    printfn "✗ Failed: %s" err.Message

printfn ""
printfn "======================================"
printfn "MaxCut Examples Complete!"
printfn "======================================"
