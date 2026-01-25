// ============================================================================
// Constraint Scheduler Example
// ============================================================================
//
// Demonstrates using quantum optimization to solve scheduling and resource
// allocation problems with constraints.
//
// Business Use Cases:
// - Workforce Management: Schedule shifts respecting availability and skills
// - Cloud Computing: Allocate VMs to minimize costs while meeting SLAs
// - Manufacturing: Assign tasks to machines with capacity constraints
// - Logistics: Route deliveries respecting time windows and capacity
//
// This example shows how to:
// 1. Define tasks and resources with constraints
// 2. Use quantum optimization to find optimal assignments
// 3. Handle hard constraints (must satisfy) and soft constraints (preferences)
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
// Example 1: Simple Task Assignment - Classical
// ============================================================================

printfn "=== Example 1: Simple Task Assignment (Classical) ==="
printfn ""

let simpleResult = ConstraintScheduler.constraintScheduler {
    // Define 2 tasks
    task "Task1"
    task "Task2"
    
    // Define 2 resources with costs
    resource "ResourceA" 5.0
    resource "ResourceB" 3.0
    
    // Soft constraints (preferences)
    prefer "Task1" "ResourceA" 1.0
    prefer "Task2" "ResourceB" 1.0
    
    // Optimization goal
    optimizeFor ConstraintScheduler.MinimizeCost
}

match simpleResult with
| Ok result ->
    printfn "✓ Classical Scheduling Complete"
    printfn "  Message: %s" result.Message
    printfn ""
    
    match result.BestSchedule with
    | Some schedule ->
        printfn "  Optimal Assignment:"
        for assignment in schedule.Assignments do
            printfn "    %s → %s ($%.2f)" assignment.Task assignment.Resource assignment.Cost
        printfn ""
        printfn "  Total Cost: $%.2f" schedule.TotalCost
        printfn "  Feasible: %b" schedule.IsFeasible
    | None ->
        printfn "  No feasible schedule found with current constraints."
        printfn "  Try adjusting constraints or increasing budget."
    printfn ""

| Error err ->
    printfn "✗ Error: %A" err
    printfn ""

// ============================================================================
// Example 2: Workforce Scheduling - Classical
// ============================================================================

printfn "=== Example 2: Workforce Scheduling (Classical) ==="
printfn ""

let workforceResult = ConstraintScheduler.constraintScheduler {
    // Define shifts
    task "Morning"
    task "Afternoon"
    task "Evening"
    task "Night"
    
    // Available workers with hourly rates
    resource "Alice" 25.0  // Senior
    resource "Bob" 15.0    // Junior
    resource "Carol" 20.0  // Mid-level
    resource "Dave" 15.0   // Junior
    
    // Hard constraints (conflicts)
    conflict "Morning" "Afternoon"
    conflict "Afternoon" "Evening"
    conflict "Evening" "Night"
    
    // Soft constraints (preferences)
    prefer "Morning" "Alice" 10.0
    prefer "Afternoon" "Carol" 8.0
    prefer "Night" "Dave" 9.0
    
    // Optimization
    optimizeFor ConstraintScheduler.MinimizeCost
    maxBudget 100.0
}

match workforceResult with
| Ok result ->
    printfn "✓ Workforce Scheduling Complete"
    printfn "  Message: %s" result.Message
    printfn ""
    
    match result.BestSchedule with
    | Some schedule ->
        printfn "  Shift Assignments:"
        for assignment in schedule.Assignments do
            printfn "    %s → %s ($%.2f/hour)" assignment.Task assignment.Resource assignment.Cost
        printfn ""
        printfn "  Total Cost: $%.2f" schedule.TotalCost
        printfn "  Constraints Satisfied: %d / %d hard, %d / %d soft" 
            schedule.HardConstraintsSatisfied 
            schedule.TotalHardConstraints
            schedule.SoftConstraintsSatisfied 
            schedule.TotalSoftConstraints
        printfn "  Feasible: %b" schedule.IsFeasible
    | None ->
        printfn "  No feasible schedule found - constraints may be too restrictive."
    printfn ""

| Error err ->
    printfn "✗ Error: %A" err
    printfn ""

// ============================================================================
// Example 3: Cloud Resource Allocation - Quantum
// ============================================================================

printfn "=== Example 3: Cloud Resource Allocation (Quantum) ==="
printfn ""

// Create local quantum backend
let localBackend = LocalBackend() :> IQuantumBackend

let cloudResult = ConstraintScheduler.constraintScheduler {
    // VMs to allocate
    task "WebServer1"
    task "WebServer2"
    task "DatabasePrimary"
    task "DatabaseReplica"
    task "CacheNode"
    
    // Physical servers with costs
    resource "Server_A" 10.0
    resource "Server_B" 15.0
    resource "Server_C" 8.0
    
    // High availability constraints
    conflict "WebServer1" "WebServer2"
    conflict "DatabasePrimary" "DatabaseReplica"
    conflict "CacheNode" "DatabasePrimary"
    
    // Performance preferences
    prefer "WebServer1" "Server_A" 15.0
    prefer "WebServer2" "Server_A" 15.0
    prefer "DatabasePrimary" "Server_B" 20.0
    prefer "DatabaseReplica" "Server_B" 18.0
    
    // Optimization
    optimizeFor ConstraintScheduler.Balanced
    maxBudget 50.0
    
    // Quantum backend
    backend localBackend
    shots 1500
}

match cloudResult with
| Ok result ->
    printfn "✓ Quantum Optimization Complete"
    printfn "  Message: %s" result.Message
    printfn ""
    
    match result.BestSchedule with
    | Some schedule ->
        printfn "  VM Assignment:"
        
        // Group by server
        let byServer = 
            schedule.Assignments 
            |> List.groupBy (fun a -> a.Resource)
            |> List.sortBy fst
        
        for server, assignments in byServer do
            printfn "    %s:" server
            for assignment in assignments do
                printfn "      - %s ($%.2f)" assignment.Task assignment.Cost
        printfn ""
        printfn "  Total Cost: $%.2f" schedule.TotalCost
        printfn "  Feasible: %b" schedule.IsFeasible
    | None ->
        printfn "  Quantum optimization did not converge - try increasing shots or adjusting constraints."
    printfn ""

| Error err ->
    printfn "✗ Error: %A" err
    printfn ""

// ============================================================================
// Example 4: Manufacturing - Quantum with High Accuracy
// ============================================================================

printfn "=== Example 4: Manufacturing Task Assignment (Quantum, High Accuracy) ==="
printfn ""

let manufacturingResult = ConstraintScheduler.constraintScheduler {
    // Production tasks
    task "Welding_Job1"
    task "Welding_Job2"
    task "Assembly_Job1"
    task "Painting_Job1"
    
    // Stations with costs
    resource "WeldingStation_A" 50.0
    resource "WeldingStation_B" 30.0
    resource "AssemblyLine_1" 40.0
    resource "PaintingBooth" 35.0
    
    // Hard constraints (required capabilities)
    require "Welding_Job1" "WeldingStation_A"
    require "Welding_Job2" "WeldingStation_B"
    require "Assembly_Job1" "AssemblyLine_1"
    require "Painting_Job1" "PaintingBooth"
    
    // Precedence (painting after welding/assembly)
    precedence "Welding_Job1" "Painting_Job1"
    precedence "Assembly_Job1" "Painting_Job1"
    
    // Preferences
    prefer "Welding_Job1" "WeldingStation_A" 10.0
    
    // Optimization
    optimizeFor ConstraintScheduler.MaximizeSatisfaction
    maxBudget 200.0
    
    // Quantum with high accuracy
    backend localBackend
    shots 5000
}

match manufacturingResult with
| Ok result ->
    printfn "✓ Manufacturing Schedule Optimized"
    printfn "  Message: %s" result.Message
    printfn ""
    
    match result.BestSchedule with
    | Some schedule ->
        printfn "  Production Schedule:"
        for assignment in schedule.Assignments do
            printfn "    %s → %s ($%.2f)" assignment.Task assignment.Resource assignment.Cost
        printfn ""
        printfn "  Total Cost: $%.2f" schedule.TotalCost
        printfn "  Feasible: %b (all hard constraints satisfied)" schedule.IsFeasible
    | None ->
        printfn "  Quantum optimization did not find valid assignment - constraints may be contradictory."
    printfn ""

| Error err ->
    printfn "✗ Error: %A" err
    printfn ""

// ============================================================================
// Performance Notes
// ============================================================================

printfn "=== Performance Notes ==="
printfn ""
printfn "Quantum Optimization Advantage:"
printfn "  - Classical: NP-hard (exponential time)"
printfn "  - Quantum: Quadratic speedup with Grover search"
printfn "  - Best for 10+ tasks with complex constraints"
printfn ""
printfn "Optimization Goals:"
printfn "  - MinimizeCost: Weighted Graph Coloring oracle"
printfn "  - MaximizeSatisfaction: Max-SAT oracle"
printfn "  - Balanced: Both criteria combined"
printfn ""
printfn "Accuracy Control:"
printfn "  - shots 100-500: Fast testing"
printfn "  - shots 1000-2000: Standard production"
printfn "  - shots 5000-10000: High accuracy/critical decisions"
printfn ""
printfn "Real-World Applications:"
printfn "  - Workforce scheduling with shift constraints"
printfn "  - Cloud VM allocation with HA requirements"
printfn "  - Manufacturing production scheduling"
printfn "  - Logistics routing and delivery optimization"
printfn ""
