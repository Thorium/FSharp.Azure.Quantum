namespace FSharp.Azure.Quantum.TaskScheduling
open FSharp.Azure.Quantum.Core

open Types

/// Export and visualization utilities
module Export =

    /// Export schedule as Gantt chart to text file
    let exportGanttChart (solution: Solution) (filePath: string) : unit =
        use writer = new System.IO.StreamWriter(filePath)
        
        writer.WriteLine("# Gantt Chart - Task Schedule")
        writer.WriteLine("")
        writer.WriteLine(sprintf "Makespan: %.1f time units" solution.Makespan)
        writer.WriteLine(sprintf "Total Cost: $%.2f" solution.TotalCost)
        writer.WriteLine(sprintf "Valid: %b" solution.IsValid)
        writer.WriteLine("")
        
        writer.WriteLine("Task Assignments:")
        writer.WriteLine("----------------")
        
        for assignment in solution.Assignments |> List.sortBy (fun a -> a.StartTime) do
            let barLength = int (assignment.EndTime - assignment.StartTime)
            let bar = System.String('█', barLength)
            writer.WriteLine(sprintf "%-12s [%6.1f - %6.1f] %s"
                (if assignment.TaskId.Length > 12 then assignment.TaskId.Substring(0, 12) else assignment.TaskId)
                assignment.StartTime
                assignment.EndTime
                bar)
        
        if not (List.isEmpty solution.DeadlineViolations) then
            writer.WriteLine("")
            writer.WriteLine("Deadline Violations:")
            writer.WriteLine("-------------------")
            for taskId in solution.DeadlineViolations do
                writer.WriteLine(sprintf "  - %s" taskId)
