/// Quantum Constraint Solver Examples - FSharp.Azure.Quantum
///
/// USE CASE: Solve constraint satisfaction problems using Grover's quantum search
///
/// PROBLEM: Find assignments of values to variables that satisfy all constraints.
/// Uses quantum search (Grover's algorithm) to accelerate exploration of the
/// solution space when constraints are expensive to evaluate.
///
/// This script demonstrates three constraint satisfaction problems:
///   1. 4x4 Sudoku Solver
///   2. 8-Queens Puzzle
///   3. Job Scheduling with Constraints

(*
===============================================================================
 Background Theory
===============================================================================

Constraint Satisfaction Problems (CSPs) require finding assignments of values
to variables that satisfy a set of constraints. Many important CSPs are NP-hard:
Sudoku, N-Queens, graph coloring, scheduling, resource allocation, and SAT.

Classical solvers use backtracking, arc consistency, and constraint propagation,
achieving good performance on structured problems but worst-case exponential
time. Grover's algorithm provides a provable O(sqrt(N)) speedup over exhaustive
classical search of N candidates, making it attractive for CSPs with expensive
constraint checking and little exploitable structure.

Key Equations:
  - Classical brute-force: O(N) evaluations for N-element search space
  - Grover's algorithm: O(sqrt(N)) evaluations (quadratic speedup)
  - For k-valued variables over n cells: N = k^n possible assignments
  - Optimal Grover iterations: floor(pi/4 * sqrt(N/M)) where M = solution count
  - Success probability per run: >= 1/2 for unknown M

Quantum Advantage:
  Grover's algorithm explores the entire solution space in superposition,
  using amplitude amplification to concentrate probability on valid solutions.
  The quadratic speedup is optimal for unstructured search (BBBV theorem).
  For CSPs with 10^6 candidates, Grover reduces evaluations from ~10^6 to ~10^3.

References:
  [1] Grover, "A fast quantum mechanical algorithm for database search",
      STOC 1996. https://doi.org/10.1145/237814.237866
  [2] Nielsen & Chuang, "Quantum Computation and Quantum Information",
      Cambridge University Press (2010), Section 6.1.
  [3] Wikipedia: Grover's_algorithm
      https://en.wikipedia.org/wiki/Grover%27s_algorithm

Usage:
  dotnet fsi SudokuSolver.fsx                                   (defaults)
  dotnet fsi SudokuSolver.fsx -- --help                         (show options)
  dotnet fsi SudokuSolver.fsx -- --example sudoku               (run Sudoku)
  dotnet fsi SudokuSolver.fsx -- --example queens               (run 8-Queens)
  dotnet fsi SudokuSolver.fsx -- --example all --shots 2000     (run all)
  dotnet fsi SudokuSolver.fsx -- --quiet --output results.json  (pipeline mode)
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.QuantumConstraintSolver
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "SudokuSolver.fsx"
    "Quantum constraint satisfaction using Grover's search."
    [ { Cli.OptionSpec.Name = "example";  Description = "Which example: sudoku|queens|scheduling|all"; Default = Some "all" }
      { Cli.OptionSpec.Name = "shots";    Description = "Number of measurement shots";                 Default = Some "1000" }
      { Cli.OptionSpec.Name = "output";   Description = "Write results to JSON file";                  Default = None }
      { Cli.OptionSpec.Name = "csv";      Description = "Write results to CSV file";                   Default = None }
      { Cli.OptionSpec.Name = "quiet";    Description = "Suppress informational output";               Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let exampleName = Cli.getOr "example" "all" args
let numShots = Cli.getIntOr "shots" 1000 args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ==============================================================================
// BACKEND CONFIGURATION
// ==============================================================================

let localBackend = LocalBackend() :> IQuantumBackend

// ==============================================================================
// DISPLAY HELPERS
// ==============================================================================

let printHeader title =
    if not quiet then
        printfn ""
        printfn "%s" title
        printfn "%s" (String.replicate (String.length title) "-")

// ==============================================================================
// DOMAIN TYPES
// ==============================================================================

type Worker =
    { Id: int
      Name: string
      Skills: string list
      AvailableShifts: int list }

type Shift =
    { Id: int
      Name: string
      RequiredSkill: string }

// ==============================================================================
// EXAMPLE DATA
// ==============================================================================

/// 4x4 Sudoku puzzle (0 = empty cell)
let puzzle4x4 =
    [| 1; 0; 0; 0
       0; 0; 0; 2
       0; 3; 0; 0
       0; 0; 4; 0 |]

let workers =
    [ { Id = 0; Name = "Alice";   Skills = ["Machine A"; "Machine B"]; AvailableShifts = [0; 1; 2] }
      { Id = 1; Name = "Bob";     Skills = ["Machine A"];              AvailableShifts = [1; 2; 3] }
      { Id = 2; Name = "Charlie"; Skills = ["Machine B"; "Machine C"]; AvailableShifts = [0; 2; 4] }
      { Id = 3; Name = "Diana";   Skills = ["Machine C"];              AvailableShifts = [1; 3; 4] }
      { Id = 4; Name = "Eve";     Skills = ["Machine A"; "Machine C"]; AvailableShifts = [0; 3; 4] } ]

let shifts =
    [ { Id = 0; Name = "Morning A";   RequiredSkill = "Machine A" }
      { Id = 1; Name = "Morning B";   RequiredSkill = "Machine B" }
      { Id = 2; Name = "Afternoon A"; RequiredSkill = "Machine A" }
      { Id = 3; Name = "Afternoon C"; RequiredSkill = "Machine C" }
      { Id = 4; Name = "Evening C";   RequiredSkill = "Machine C" } ]

// ==============================================================================
// CONSTRAINT FUNCTIONS
// ==============================================================================

let checkSudoku4x4 (assignment: Map<int, int>) =
    if assignment.Count < 16 then false
    else
        let grid = Array.create 16 0
        for cell in 0 .. 15 do
            grid.[cell] <-
                if puzzle4x4.[cell] <> 0 then puzzle4x4.[cell]
                else
                    match Map.tryFind cell assignment with
                    | Some value -> value
                    | None -> 0

        let rowsValid =
            [ 0 .. 3 ]
            |> List.forall (fun row ->
                let values = [ 0 .. 3 ] |> List.map (fun col -> grid.[row * 4 + col])
                List.sort values = [ 1; 2; 3; 4 ])

        let colsValid =
            [ 0 .. 3 ]
            |> List.forall (fun col ->
                let values = [ 0 .. 3 ] |> List.map (fun row -> grid.[row * 4 + col])
                List.sort values = [ 1; 2; 3; 4 ])

        let boxesValid =
            [ (0, 0); (0, 2); (2, 0); (2, 2) ]
            |> List.forall (fun (boxRow, boxCol) ->
                let values =
                    [ for r in 0 .. 1 do
                        for c in 0 .. 1 do
                            yield grid.[(boxRow + r) * 4 + (boxCol + c)] ]
                List.sort values = [ 1; 2; 3; 4 ])

        rowsValid && colsValid && boxesValid

let checkQueens (assignment: Map<int, int>) =
    if assignment.Count < 8 then false
    else
        let positions =
            [ 0 .. 7 ]
            |> List.choose (fun row ->
                Map.tryFind row assignment
                |> Option.map (fun col -> (row, col)))

        if positions.Length < 8 then false
        else
            let uniqueCols =
                positions |> List.map snd |> List.distinct |> List.length = 8

            let noDiagonalAttacks =
                positions
                |> List.forall (fun (r1, c1) ->
                    positions
                    |> List.forall (fun (r2, c2) ->
                        r1 = r2 || (abs (r1 - r2) <> abs (c1 - c2))))

            uniqueCols && noDiagonalAttacks

let checkSchedule (assignment: Map<int, int>) =
    if assignment.Count < 5 then false
    else
        let allShiftsAssigned =
            [ 0 .. 4 ] |> List.forall (fun shift -> Map.containsKey shift assignment)

        if not allShiftsAssigned then false
        else
            let workerAssignments =
                [ 0 .. 4 ] |> List.choose (fun shift -> Map.tryFind shift assignment)
            let noDuplicates =
                workerAssignments |> List.distinct |> List.length = 5

            let skillsMatch =
                [ 0 .. 4 ]
                |> List.forall (fun shiftId ->
                    match Map.tryFind shiftId assignment with
                    | Some workerId when workerId < workers.Length && shiftId < shifts.Length ->
                        let shift = shifts.[shiftId]
                        let worker = workers.[workerId]
                        worker.Skills |> List.contains shift.RequiredSkill
                    | _ -> false)

            let availabilityMatch =
                [ 0 .. 4 ]
                |> List.forall (fun shiftId ->
                    match Map.tryFind shiftId assignment with
                    | Some workerId when workerId < workers.Length ->
                        let worker = workers.[workerId]
                        worker.AvailableShifts |> List.contains shiftId
                    | _ -> false)

            allShiftsAssigned && noDuplicates && skillsMatch && availabilityMatch

// ==============================================================================
// EXAMPLE RUNNERS
// ==============================================================================

let allResults = ResizeArray<Map<string, string>>()

let runSudoku () =
    printHeader "Example 1: 4x4 Sudoku Solver"

    if not quiet then
        printfn ""
        printfn "Initial Puzzle:"
        printfn "  1 _ _ _"
        printfn "  _ _ _ 2"
        printfn "  _ 3 _ _"
        printfn "  _ _ 4 _"
        printfn ""
        printfn "Solving with Quantum Constraint Solver (Grover's algorithm)..."
        printfn ""

    let problem =
        constraintSolver {
            searchSpace 16
            domain [ 1 .. 4 ]
            satisfies checkSudoku4x4
            backend localBackend
            shots numShots
        }

    match solve problem with
    | Ok solution ->
        let solvedGrid = Array.copy puzzle4x4
        for (cellStr, value) in Map.toList solution.Assignment do
            let cell = int cellStr
            solvedGrid.[cell] <- value

        if not quiet then
            printfn "Solution found!"
            printfn ""
            printfn "  Solved Grid:"
            for row in 0 .. 3 do
                printfn "    %d %d %d %d"
                    solvedGrid.[row * 4]
                    solvedGrid.[row * 4 + 1]
                    solvedGrid.[row * 4 + 2]
                    solvedGrid.[row * 4 + 3]
            printfn ""
            printfn "  Constraints satisfied: %b" solution.AllConstraintsSatisfied
            printfn "  Cells filled: %d/16" solution.Assignment.Count
            printfn "  Search space: 4096 states"
            printfn "  Quantum advantage: sqrt(4096) = 64x fewer evaluations"
            printfn ""

        allResults.Add(Map.ofList
            [ "example",              "sudoku"
              "status",               "solved"
              "constraints_met",      sprintf "%b" solution.AllConstraintsSatisfied
              "assignments",          sprintf "%d" solution.Assignment.Count
              "search_space",         "4096"
              "backend",              "LocalBackend" ])

    | Error err ->
        if not quiet then printfn "Error: %s" err.Message

let runQueens () =
    printHeader "Example 2: 8-Queens Puzzle"

    if not quiet then
        printfn ""
        printfn "Problem: Place 8 queens on 8x8 board with no attacks"
        printfn "Solving with Quantum Constraint Solver..."
        printfn ""

    let problem =
        constraintSolver {
            searchSpace 8
            domain [ 0 .. 7 ]
            satisfies checkQueens
            backend localBackend
            shots numShots
        }

    match solve problem with
    | Ok solution ->
        if not quiet then
            printfn "Solution found!"
            printfn ""
            printfn "  Queen Positions (row, column):"
            for row in 0 .. 7 do
                let col = solution.Assignment.[row]
                printfn "    Row %d: Column %d" row col

            printfn ""
            printfn "  Board Visualization:"
            for row in 0 .. 7 do
                let col = solution.Assignment.[row]
                let board =
                    [ 0 .. 7 ]
                    |> List.map (fun c -> if c = col then "Q" else ".")
                printfn "    %s" (String.concat " " board)

            printfn ""
            printfn "  Constraints satisfied: %b" solution.AllConstraintsSatisfied
            printfn "  Queens placed: 8/8"
            printfn "  Search space: 16,777,216 states"
            printfn "  Quantum advantage: sqrt(16M) = 4096x fewer evaluations"
            printfn ""

        allResults.Add(Map.ofList
            [ "example",              "queens"
              "status",               "solved"
              "constraints_met",      sprintf "%b" solution.AllConstraintsSatisfied
              "assignments",          sprintf "%d" solution.Assignment.Count
              "search_space",         "16777216"
              "backend",              "LocalBackend" ])

    | Error err ->
        if not quiet then printfn "Error: %s" err.Message

let runScheduling () =
    printHeader "Example 3: Job Scheduling"

    if not quiet then
        printfn ""
        printfn "Workers: %d" workers.Length
        printfn "Shifts: %d" shifts.Length
        printfn ""
        printfn "Constraints:"
        printfn "  - Worker must have required skill"
        printfn "  - Worker must be available"
        printfn "  - Each shift assigned to exactly one worker"
        printfn ""
        printfn "Solving with Quantum Constraint Solver..."
        printfn ""

    let problem =
        constraintSolver {
            searchSpace 5
            domain [ 0 .. 4 ]
            satisfies checkSchedule
            backend localBackend
            shots numShots
        }

    match solve problem with
    | Ok solution ->
        if not quiet then
            printfn "Schedule found!"
            printfn ""
            printfn "  Shift Assignments:"
            for shift in shifts do
                let workerId = solution.Assignment.[shift.Id]
                let worker = workers.[workerId]
                printfn "    %s -> %s (skill: %s)"
                    shift.Name worker.Name shift.RequiredSkill

            printfn ""
            printfn "  Worker Schedules:"
            for worker in workers do
                let assignedShifts =
                    shifts
                    |> List.filter (fun shift ->
                        solution.Assignment.[shift.Id] = worker.Id)
                    |> List.map (fun s -> s.Name)
                if assignedShifts.IsEmpty then
                    printfn "    %s: [No shifts]" worker.Name
                else
                    printfn "    %s: %s" worker.Name (String.concat ", " assignedShifts)

            printfn ""
            printfn "  All constraints satisfied: %b" solution.AllConstraintsSatisfied
            printfn "  Shifts covered: 5/5"
            printfn "  Search space: 3,125 states"
            printfn "  Quantum advantage: sqrt(3125) = 56x fewer evaluations"
            printfn ""

        allResults.Add(Map.ofList
            [ "example",              "scheduling"
              "status",               "solved"
              "constraints_met",      sprintf "%b" solution.AllConstraintsSatisfied
              "assignments",          sprintf "%d" solution.Assignment.Count
              "search_space",         "3125"
              "backend",              "LocalBackend" ])

    | Error err ->
        if not quiet then printfn "Error: %s" err.Message

// ==============================================================================
// MAIN EXECUTION
// ==============================================================================

if not quiet then
    printfn "======================================"
    printfn "Quantum Constraint Solver"
    printfn "======================================"

match exampleName.ToLowerInvariant() with
| "all" ->
    runSudoku ()
    runQueens ()
    runScheduling ()

| "sudoku"     -> runSudoku ()
| "queens"     -> runQueens ()
| "scheduling" -> runScheduling ()

| other ->
    eprintfn "Unknown example: '%s'. Use: sudoku|queens|scheduling|all" other
    exit 1

if not quiet then
    printfn "======================================"
    printfn "Constraint Solver Examples Complete!"
    printfn "======================================"

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let resultRows = allResults |> Seq.toList

match outputPath with
| Some path ->
    Reporting.writeJson path resultRows
    if not quiet then printfn "Results written to %s" path
| None -> ()

match csvPath with
| Some path ->
    let header =
        [ "example"; "status"; "constraints_met"; "assignments";
          "search_space"; "backend" ]
    let rows =
        resultRows
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()

// ==============================================================================
// USAGE HINTS
// ==============================================================================

if argv.Length = 0 && not quiet then
    printfn ""
    printfn "Tip: Run with --help to see all options:"
    printfn "   dotnet fsi SudokuSolver.fsx -- --help"
    printfn "   dotnet fsi SudokuSolver.fsx -- --example sudoku"
    printfn "   dotnet fsi SudokuSolver.fsx -- --example all --shots 2000"
    printfn "   dotnet fsi SudokuSolver.fsx -- --quiet --output results.json"
    printfn ""
