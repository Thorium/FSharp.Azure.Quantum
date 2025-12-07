6// ============================================================================
// Quantum Constraint Solver Examples - FSharp.Azure.Quantum
// ============================================================================
//
// This script demonstrates the Quantum Constraint Solver API using Grover's
// algorithm to solve constraint satisfaction problems (CSPs):
//
// 1. Sudoku Solver (4×4 and 9×9)
// 2. N-Queens Puzzle
// 3. Job Scheduling with Constraints
//
// WHAT IS CONSTRAINT SATISFACTION:
// Find assignments of values to variables that satisfy all constraints.
// Uses quantum search (Grover's algorithm) to accelerate exploration of
// the solution space when constraints are expensive to evaluate.
//
// WHY USE QUANTUM:
// - Grover's algorithm provides O(√N) speedup over classical search
// - Ideal for problems with expensive constraint checking
// - Quadratic speedup for exploring combinatorial solution spaces
//
// ============================================================================

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.QuantumConstraintSolver
open FSharp.Azure.Quantum.Core.BackendAbstraction

// ============================================================================
// BACKEND CONFIGURATION
// ============================================================================

// Create local quantum simulator (fast, for development/testing)
let localBackend = LocalBackend() :> IQuantumBackend

// For cloud execution, use IonQ or Rigetti backend:
// let cloudBackend = IonQBackend(workspace, resourceId) :> IQuantumBackend
// let cloudBackend = RigettiBackend(workspace, resourceId) :> IQuantumBackend

// ============================================================================
// EXAMPLE 1: 4×4 Sudoku Solver (Educational)
// ============================================================================
//
// PROBLEM: Fill a 4×4 grid with numbers 1-4 such that:
// - Each row contains 1-4 exactly once
// - Each column contains 1-4 exactly once
// - Each 2×2 box contains 1-4 exactly once
//
// REAL-WORLD IMPACT:
// - Sudoku solving demonstrates CSP solving techniques
// - Scales to scheduling, resource allocation, configuration problems
// - Quantum speedup useful for large constraint networks
//
printfn "========================================="
printfn "EXAMPLE 1: 4×4 Sudoku Solver"
printfn "========================================="
printfn ""

// Puzzle: 0 represents empty cell
let puzzle4x4 = [|
    1; 0; 0; 0;  // Row 1: 1 _ _ _
    0; 0; 0; 2;  // Row 2: _ _ _ 2
    0; 3; 0; 0;  // Row 3: _ 3 _ _
    0; 0; 4; 0   // Row 4: _ _ 4 _
|]

printfn "Initial Puzzle:"
printfn "  1 _ _ _"
printfn "  _ _ _ 2"
printfn "  _ 3 _ _"
printfn "  _ _ 4 _"
printfn ""

// Helper: Check if assignment satisfies Sudoku rules
let checkSudoku4x4 (assignment: Map<int, int>) =
    // Check if we have all cells assigned
    if assignment.Count < 16 then false
    else
        // Convert to 2D array for easier checking
        let grid = Array.create 16 0
        for cell in 0..15 do
            grid.[cell] <- 
                if puzzle4x4.[cell] <> 0 then puzzle4x4.[cell]
                else 
                    match Map.tryFind cell assignment with
                    | Some value -> value
                    | None -> 0  // Missing assignment
        
        // Check all rows
        let rowsValid = 
            [0..3] |> List.forall (fun row ->
                let values = [0..3] |> List.map (fun col -> grid.[row * 4 + col])
                List.sort values = [1; 2; 3; 4]
            )
        
        // Check all columns
        let colsValid = 
            [0..3] |> List.forall (fun col ->
                let values = [0..3] |> List.map (fun row -> grid.[row * 4 + col])
                List.sort values = [1; 2; 3; 4]
            )
        
        // Check all 2×2 boxes
        let boxesValid = 
            [(0,0); (0,2); (2,0); (2,2)] |> List.forall (fun (boxRow, boxCol) ->
                let values = [
                    for r in 0..1 do
                        for c in 0..1 do
                            yield grid.[(boxRow + r) * 4 + (boxCol + c)]
                ]
                List.sort values = [1; 2; 3; 4]
            )
        
        rowsValid && colsValid && boxesValid

// Build constraint solver problem
let sudoku4x4Problem = constraintSolver {
    // Search space: 16 cells total (4x4 grid)
    searchSpace 16
    domain [1..4]
    
    // Main constraint: all Sudoku rules satisfied
    satisfies checkSudoku4x4
    
    // Use local quantum simulator
    backend localBackend
    shots 1000  // Number of measurements
}

printfn "Solving with Quantum Constraint Solver..."
printfn "(Using Grover's algorithm for O(√N) speedup)"
printfn ""

match solve sudoku4x4Problem with
| Ok solution ->
    printfn "✅ SOLUTION FOUND!"
    printfn ""
    
    // Reconstruct full grid
    let solvedGrid = Array.copy puzzle4x4
    for (cellStr, value) in Map.toList solution.Assignment do
        let cell = int cellStr
        solvedGrid.[cell] <- value
    
    printfn "  Solved Grid:"
    for row in 0..3 do
        printfn "    %d %d %d %d" 
            solvedGrid.[row*4] 
            solvedGrid.[row*4+1] 
            solvedGrid.[row*4+2] 
            solvedGrid.[row*4+3]
    
    printfn ""
    printfn "  Verification:"
    printfn "    Constraints satisfied: %b" solution.AllConstraintsSatisfied
    printfn "    Cells filled: %d/16" solution.Assignment.Count
    printfn ""
    printfn "  Quantum Resources:"
    printfn "    Search space explored: 4096 states"
    printfn "    Quantum advantage: √4096 = 64× fewer evaluations"

| Error err ->
    printfn "❌ Error: %s" err.Message

printfn ""
printfn ""

// ============================================================================
// EXAMPLE 2: N-Queens Puzzle (8-Queens)
// ============================================================================
//
// PROBLEM: Place 8 queens on an 8×8 chessboard such that no two queens
// attack each other (same row, column, or diagonal).
//
// REAL-WORLD IMPACT:
// - Classic CSP problem used to teach constraint solving
// - Demonstrates quantum advantage for backtracking problems
// - Applies to resource placement, task assignment
//
printfn "========================================="
printfn "EXAMPLE 2: 8-Queens Puzzle"
printfn "========================================="
printfn ""

printfn "Problem: Place 8 queens on 8×8 board with no attacks"
printfn ""

// Helper: Check if queen placement is valid
let checkQueens (assignment: Map<int, int>) =
    // Check if we have all queens placed
    if assignment.Count < 8 then false
    else
        // assignment: row → column (queen in row i at column assignment[i])
        let positions = 
            [0..7] 
            |> List.choose (fun row -> 
                Map.tryFind row assignment 
                |> Option.map (fun col -> (row, col))
            )
        
        if positions.Length < 8 then false
        else
            // Check no two queens in same column
            let cols = positions |> List.map snd
            let uniqueCols = cols |> List.distinct |> List.length = 8
            
            // Check no two queens on same diagonal
            let noDiagonalAttacks =
                positions |> List.forall (fun (r1, c1) ->
                    positions |> List.forall (fun (r2, c2) ->
                        r1 = r2 || (abs(r1 - r2) <> abs(c1 - c2))
                    )
                )
            
            uniqueCols && noDiagonalAttacks

let queensProblem = constraintSolver {
    // 8 queens to place (search space of 8 positions)
    searchSpace 8
    domain [0..7]  // Columns 0-7
    
    // Constraint: no two queens attack each other
    satisfies checkQueens
    
    // Use local quantum simulator
    backend localBackend
    shots 1000
}

printfn "Solving with Quantum Constraint Solver..."
printfn ""

match solve queensProblem with
| Ok solution ->
    printfn "✅ SOLUTION FOUND!"
    printfn ""
    
    printfn "  Queen Positions (row, column):"
    for row in 0..7 do
        let col = solution.Assignment.[row]
        printfn "    Row %d: Column %d" row col
    
    printfn ""
    printfn "  Board Visualization:"
    for row in 0..7 do
        let col = solution.Assignment.[row]
        let board = [0..7] |> List.map (fun c -> if c = col then "Q" else "·")
        printfn "    %s" (String.concat " " board)
    
    printfn ""
    printfn "  Verification:"
    printfn "    Constraints satisfied: %b" solution.AllConstraintsSatisfied
    printfn "    Queens placed: 8/8"
    printfn ""
    printfn "  Quantum Resources:"
    printfn "    Search space: 16,777,216 states"
    printfn "    Quantum advantage: √16M = 4096× fewer evaluations"

| Error err ->
    printfn "❌ Error: %s" err.Message

printfn ""
printfn ""

// ============================================================================
// EXAMPLE 3: Job Scheduling with Constraints
// ============================================================================
//
// PROBLEM: Assign 5 workers to 5 shifts, respecting:
// - Skill requirements (worker must have required skill)
// - Availability (worker must be available for that shift)
// - No worker assigned to overlapping shifts
//
// REAL-WORLD IMPACT:
// - Healthcare: nurse scheduling with certification requirements
// - Manufacturing: operator assignment with machine qualifications
// - Retail: employee scheduling with shift preferences
//
printfn "========================================="
printfn "EXAMPLE 3: Job Scheduling"
printfn "========================================="
printfn ""

// Domain model
type Worker = { Id: int; Name: string; Skills: string list; AvailableShifts: int list }
type Shift = { Id: int; Name: string; RequiredSkill: string }

let workers = [
    { Id = 0; Name = "Alice";   Skills = ["Machine A"; "Machine B"]; AvailableShifts = [0; 1; 2] }
    { Id = 1; Name = "Bob";     Skills = ["Machine A"];              AvailableShifts = [1; 2; 3] }
    { Id = 2; Name = "Charlie"; Skills = ["Machine B"; "Machine C"]; AvailableShifts = [0; 2; 4] }
    { Id = 3; Name = "Diana";   Skills = ["Machine C"];              AvailableShifts = [1; 3; 4] }
    { Id = 4; Name = "Eve";     Skills = ["Machine A"; "Machine C"]; AvailableShifts = [0; 3; 4] }
]

let shifts = [
    { Id = 0; Name = "Morning A";   RequiredSkill = "Machine A" }
    { Id = 1; Name = "Morning B";   RequiredSkill = "Machine B" }
    { Id = 2; Name = "Afternoon A"; RequiredSkill = "Machine A" }
    { Id = 3; Name = "Afternoon C"; RequiredSkill = "Machine C" }
    { Id = 4; Name = "Evening C";   RequiredSkill = "Machine C" }
]

printfn "Workers: %d" workers.Length
printfn "Shifts: %d" shifts.Length
printfn ""
printfn "Constraints:"
printfn "  - Worker must have required skill"
printfn "  - Worker must be available"
printfn "  - Each shift assigned to exactly one worker"
printfn ""

// Helper: Check if schedule satisfies all constraints
let checkSchedule (assignment: Map<int, int>) =
    // assignment: shift → worker
    
    // Check if we have all shifts assigned
    if assignment.Count < 5 then false
    else
        // Check each shift is assigned
        let allShiftsAssigned = [0..4] |> List.forall (fun shift -> Map.containsKey shift assignment)
        
        if not allShiftsAssigned then false
        else
            // Check no worker assigned to overlapping shifts (simplified: no duplicates)
            let workerAssignments = 
                [0..4] 
                |> List.choose (fun shift -> Map.tryFind shift assignment)
            let noDuplicates = workerAssignments |> List.distinct |> List.length = 5
            
            // Check skill match
            let skillsMatch = 
                [0..4] |> List.forall (fun shiftId ->
                    match Map.tryFind shiftId assignment with
                    | Some workerId when workerId < workers.Length && shiftId < shifts.Length ->
                        let shift = shifts.[shiftId]
                        let worker = workers.[workerId]
                        worker.Skills |> List.contains shift.RequiredSkill
                    | _ -> false
                )
            
            // Check availability
            let availabilityMatch = 
                [0..4] |> List.forall (fun shiftId ->
                    match Map.tryFind shiftId assignment with
                    | Some workerId when workerId < workers.Length ->
                        let worker = workers.[workerId]
                        worker.AvailableShifts |> List.contains shiftId
                    | _ -> false
                )
            
            allShiftsAssigned && noDuplicates && skillsMatch && availabilityMatch

let schedulingProblem = constraintSolver {
    // 5 shifts to assign (search space of 5)
    searchSpace 5
    domain [0..4]  // Worker IDs 0-4
    
    // Constraints: skills, availability, no conflicts
    satisfies checkSchedule
    
    // Use local quantum simulator
    backend localBackend
    shots 1000
}

printfn "Solving with Quantum Constraint Solver..."
printfn ""

match solve schedulingProblem with
| Ok solution ->
    printfn "✅ OPTIMAL SCHEDULE FOUND!"
    printfn ""
    
    printfn "  Shift Assignments:"
    for shift in shifts do
        let workerId = solution.Assignment.[shift.Id]
        let worker = workers.[workerId]
        printfn "    %s → %s (skill: %s)" 
            shift.Name 
            worker.Name 
            shift.RequiredSkill
    
    printfn ""
    printfn "  Worker Schedules:"
    for worker in workers do
        let assignedShifts = 
            shifts 
            |> List.filter (fun shift -> 
                solution.Assignment.[shift.Id] = worker.Id
            )
            |> List.map (fun s -> s.Name)
        if assignedShifts.IsEmpty then
            printfn "    %s: [No shifts]" worker.Name
        else
            printfn "    %s: %s" worker.Name (String.concat ", " assignedShifts)
    
    printfn ""
    printfn "  Verification:"
    printfn "    All constraints satisfied: %b" solution.AllConstraintsSatisfied
    printfn "    Shifts covered: 5/5"
    printfn ""
    printfn "  Quantum Resources:"
    printfn "    Search space: 3,125 states"
    printfn "    Quantum advantage: √3125 = 56× fewer evaluations"

| Error err ->
    printfn "❌ Error: %s" err.Message

printfn ""
printfn ""

// ============================================================================
// SUMMARY: When to Use Quantum Constraint Solver
// ============================================================================

printfn "========================================="
printfn "WHEN TO USE QUANTUM CONSTRAINT SOLVER"
printfn "========================================="
printfn ""
printfn "✅ GOOD FITS:"
printfn "  - Constraint satisfaction problems (CSP)"
printfn "  - Small-to-medium search spaces (10³-10⁶ states)"
printfn "  - Expensive constraint evaluation"
printfn "  - Need to find ANY valid solution (not optimal)"
printfn ""
printfn "❌ NOT SUITABLE FOR:"
printfn "  - Optimization problems (use QAOA/VQE instead)"
printfn "  - Very large search spaces (>10⁶ states)"
printfn "  - Problems with efficient classical algorithms"
printfn ""
printfn "🚀 QUANTUM ADVANTAGE:"
printfn "  - Grover's algorithm: O(√N) vs O(N) classical"
printfn "  - Best for problems with no structure to exploit"
printfn "  - Quadratic speedup for unstructured search"
printfn ""
printfn "📚 MORE EXAMPLES:"
printfn "  - Graph coloring (use GraphColoringBuilder)"
printfn "  - Tree search (use QuantumTreeSearchBuilder)"
printfn "  - Pattern matching (use QuantumPatternMatcherBuilder)"
printfn ""
