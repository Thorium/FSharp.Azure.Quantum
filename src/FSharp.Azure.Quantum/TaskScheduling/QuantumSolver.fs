namespace FSharp.Azure.Quantum.TaskScheduling

open FSharp.Azure.Quantum
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
    ///   let backend = BackendAbstraction.createLocalBackend()
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
            
            // Determine time horizon (max possible makespan)
            let totalDuration = problem.Tasks |> List.sumBy (fun t -> t.Duration)
            let timeHorizon = 
                if problem.TimeHorizon > 0.0 then
                    int (ceil problem.TimeHorizon)
                else
                    int (ceil totalDuration)  // Conservative estimate if not specified
            
            // Encode problem as QUBO
            match QuboEncoding.toQubo problem timeHorizon with
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
            
            // Execute on quantum backend
            let numShots = 1000
            let! execResult = backend.ExecuteAsync circuitWrapper numShots
            match execResult with
            | Error err -> return Error err
            | Ok execResult ->
            
            // Decode measurements to find best schedule
            // Reuse variable mapping function
            let (_, reverseMapping, _) = QuboEncoding.createVariableMappings problem.Tasks timeHorizon
            
            // Decode each measurement and find best feasible solution
            let solutions =
                execResult.Measurements
                |> Array.choose (fun bitstring ->
                    let taskStarts = QuboEncoding.decodeBitstring bitstring reverseMapping
                    
                    match QuboEncoding.buildSolutionFromStarts problem.Tasks taskStarts with
                    | Some assignments ->
                        let makespan = ClassicalSolver.calculateMakespan assignments
                        Some (makespan, assignments)
                    | None -> None
                )
            
            if Array.isEmpty solutions then
                return Error (QuantumError.OperationError ("Quantum scheduling", "No valid solutions found from quantum measurements. Try increasing numShots or adjusting QAOA parameters."))
            else
                // Select best solution (minimum makespan)
                let (bestMakespan, bestAssignments) = solutions |> Array.minBy fst
                
                // Calculate metrics using helper functions from ClassicalSolver
                let totalCost = ClassicalSolver.calculateTotalCost bestAssignments problem.Resources
                let completionTimes = bestAssignments |> List.map (fun a -> a.TaskId, a.EndTime) |> Map.ofList
                let violations = ClassicalSolver.findDeadlineViolations problem.Tasks completionTimes
                let resourceUtil = ClassicalSolver.calculateResourceUtilization bestAssignments problem.Resources bestMakespan
                
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
