namespace FSharp.Azure.Quantum.TaskScheduling

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core
open Types

/// Quantum solver for resource-constrained scheduling
module QuantumSolver =

    /// Solve scheduling problem with resource constraints using quantum backend
    /// 
    /// RULE 1 COMPLIANCE:
    /// ✅ Requires IQuantumBackend parameter (explicit quantum execution)
    /// 
    /// Resource-constrained scheduling is solved via quantum optimization:
    /// 1. Encodes tasks, dependencies, and resource limits as QUBO problem
    /// 2. Uses QAOA or quantum annealing to find optimal schedule
    /// 3. Respects resource capacity constraints (unlike classical solver)
    /// 
    /// Use this when:
    /// - Tasks have resource requirements (workers, machines, budget)
    /// - Resources have limited capacity
    /// - Need optimal allocation under constraints
    /// 
    /// Example:
    ///   let backend = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
    ///   let! result = solveQuantum backend problem
    let solveAsync 
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: SchedulingProblem<'TTask, 'TResource>) 
        : Async<QuantumResult<Solution>> =
        async {
            // Validate problem first
            match Validation.validateProblem problem with
            | Error err -> return Error err
            | Ok () ->
            
            // Discretise the real-time problem into a BOUNDED grid of time slots. Real durations
            // (minutes/hours/days) are mapped onto integer slots via slotMinutes, so the QUBO never
            // conflates real time with slot indices, and the qubit count (numTasks × timeHorizon)
            // stays within the local simulator's reach.
            let numTasks = problem.Tasks.Length
            let totalWorkMin = problem.Tasks |> List.sumBy (fun t -> t.Duration.TotalMinutes)
            let horizonMin =
                let h = problem.TimeHorizon.TotalMinutes
                if h > 0.0 then max h totalWorkMin else totalWorkMin
            let minDurMin =
                problem.Tasks
                |> List.choose (fun t -> if t.Duration.TotalMinutes > 0.0 then Some t.Duration.TotalMinutes else None)
                |> function [] -> 1.0 | xs -> List.min xs
            // Cap slots so numTasks × timeHorizon stays modest (~18 qubits).
            let maxSlots = max 2 (min 10 (18 / max 1 numTasks))
            let timeHorizon = max 2 (min maxSlots (int (ceil (horizonMin / minDurMin))))
            let slotMinutes = if timeHorizon > 0 then horizonMin / float timeHorizon else 1.0

            // Encode problem as QUBO
            match QuboEncoding.toQubo problem timeHorizon slotMinutes with
            | Error err -> return Error err
            | Ok quboMatrix ->
            
            // Convert sparse QUBO to dense array for QAOA
            let quboArray = Array2D.zeroCreate quboMatrix.NumVariables quboMatrix.NumVariables
            for KeyValue((i, j), value) in quboMatrix.Q do
                quboArray.[i, j] <- value
            
            // Create QAOA problem and mixer Hamiltonians
            let problemHam = QaoaCircuit.ProblemHamiltonian.fromQubo quboArray
            let mixerHam = QaoaCircuit.MixerHamiltonian.create quboMatrix.NumVariables
            
            // Build QAOA circuit with initial parameters
            let gamma, beta = 0.5, 0.5  // Initial parameters
            let qaoaCircuit = QaoaCircuit.QaoaCircuit.build problemHam mixerHam [| (gamma, beta) |]
            
            // Wrap QAOA circuit for backend execution
            let circuitWrapper = 
                CircuitAbstraction.QaoaCircuitWrapper(qaoaCircuit) 
                :> CircuitAbstraction.ICircuit
            
            // Execute on quantum backend to get state
            let numShots = 1000
            match backend.ExecuteToState circuitWrapper with
            | Error err -> return Error err
            | Ok state ->
            
            // Perform measurements on quantum state
            let measurements = QuantumState.measure state numShots
            
            // Decode measurements to find best schedule
            // Reuse variable mapping function
            let (_, reverseMapping, _) = QuboEncoding.createVariableMappings problem.Tasks timeHorizon
            
            // Decode each measurement and find best feasible solution
            // A schedule respects precedence iff every finish-to-start dependency holds:
            // the successor starts no earlier than the predecessor finishes (+ lag).
            let respectsDependencies (assignments: TaskAssignment list) =
                problem.Dependencies
                |> List.forall (fun dep ->
                    match dep with
                    | FinishToStart(predId, succId, lag) ->
                        match assignments |> List.tryFind (fun a -> a.TaskId = predId),
                              assignments |> List.tryFind (fun a -> a.TaskId = succId) with
                        | Some pred, Some succ -> succ.StartTime >= pred.EndTime + lag
                        | _ -> true)

            // A schedule respects resource limits iff at every moment the combined usage of all
            // concurrently running tasks stays within each resource's capacity. Usage only changes
            // when a task starts, so checking at each assignment's start time is sufficient.
            let respectsResources (assignments: TaskAssignment list) =
                problem.Resources
                |> List.forall (fun resource ->
                    assignments
                    |> List.forall (fun a ->
                        let usageAtStart =
                            assignments
                            |> List.sumBy (fun b ->
                                if b.StartTime <= a.StartTime && a.StartTime < b.EndTime then
                                    b.AssignedResources |> Map.tryFind resource.Id |> Option.defaultValue 0.0
                                else 0.0)
                        usageAtStart <= resource.Capacity + 1e-9))

            let solutions =
                measurements
                |> Array.choose (fun bitstring ->
                    let taskStarts = QuboEncoding.decodeBitstring bitstring reverseMapping

                    // One-hot multiplicity is enforced here: decodeBitstring only yields a start
                    // for tasks with EXACTLY ONE set bit, and buildSolutionFromStarts returns None
                    // unless every task has a start.
                    match QuboEncoding.buildSolutionFromStarts problem.Tasks taskStarts slotMinutes with
                    // Keep only fully feasible measurements (precedence AND resource capacity).
                    // The QUBO penalties bias QAOA sampling toward these, but the final
                    // min-makespan selection must not pick a lower-makespan measurement that
                    // VIOLATES the constraints the user specified — otherwise the returned
                    // "solution" would silently break dependencies or overload resources.
                    | Some assignments when respectsDependencies assignments && respectsResources assignments ->
                        let makespan = ScheduleMetrics.calculateMakespan assignments
                        Some (makespan, assignments)
                    | _ -> None
                )
            
            if Array.isEmpty solutions then
                return Error (QuantumError.OperationError ("Quantum scheduling", "No valid solutions found from quantum measurements. Try increasing numShots or adjusting QAOA parameters."))
            else
                // Select best solution (minimum makespan)
                let (bestMakespan, bestAssignments) = solutions |> Array.minBy fst
                
                // Score the quantum-decoded schedule with the shared ScheduleMetrics helpers
                // (pure metric calculation — no classical solving in the quantum path)
                let totalCost = ScheduleMetrics.calculateTotalCost bestAssignments problem.Resources
                let completionTimes = bestAssignments |> List.map (fun a -> a.TaskId, a.EndTime) |> Map.ofList
                let violations = ScheduleMetrics.findDeadlineViolations problem.Tasks completionTimes
                let resourceUtil = ScheduleMetrics.calculateResourceUtilization bestAssignments problem.Resources bestMakespan
                
                let solution = {
                    Assignments = bestAssignments
                    Makespan = bestMakespan
                    TotalCost = totalCost
                    ResourceUtilization = resourceUtil
                    DeadlineViolations = violations
                    IsValid = List.isEmpty violations
                }
                
                return Ok solution
        }
