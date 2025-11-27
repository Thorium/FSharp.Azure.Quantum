namespace FSharp.Azure.Quantum.GroverSearch

open System.Numerics

/// Oracle Module for Grover's Search Algorithm
/// 
/// An oracle is a quantum operation that marks solution states by flipping their phase.
/// This module provides backend-agnostic oracle specifications that work with both
/// local simulation and Azure Quantum backends.
/// 
/// Design Philosophy:
/// - Pure functions (no mutable state)
/// - Backend-agnostic (works with Local, IonQ, Rigetti)
/// - Idiomatic F# (modules, not classes)
/// - Follows existing codebase patterns
/// 
/// ALL GROVER ORACLE CODE IN SINGLE FILE
module Oracle =
    
    open FSharp.Azure.Quantum.LocalSimulator
    
    // ============================================================================
    // TYPES - Pure data structures
    // ============================================================================
    
    /// Oracle specification - defines which states to mark as solutions
    /// 
    /// Oracles work by flipping the phase of solution states:
    /// |x⟩ → -|x⟩ if x is a solution, |x⟩ otherwise
    type OracleSpec =
        /// Mark states where predicate returns true
        | Predicate of (int -> bool)
        
        /// Mark specific solution indices
        | Solutions of int list
        
        /// Mark a single target value
        | SingleTarget of int
        
        /// Combine oracles with AND logic
        | And of OracleSpec * OracleSpec
        
        /// Combine oracles with OR logic
        | Or of OracleSpec * OracleSpec
        
        /// Negate oracle (mark non-solutions)
        | Not of OracleSpec
    
    /// Oracle result after compilation
    /// Contains both local and backend representations
    type CompiledOracle = {
        /// Oracle specification
        Spec: OracleSpec
        
        /// Number of qubits in search space
        NumQubits: int
        
        /// Local simulation function (for testing and small problems)
        LocalSimulation: StateVector.StateVector -> StateVector.StateVector
        
        /// Expected number of solutions (if known)
        ExpectedSolutions: int option
    }
    
    // ============================================================================
    // ORACLE EVALUATION - Pure functions
    // ============================================================================
    
    /// Evaluate if an index is a solution according to oracle spec
    /// Pure function - no side effects
    let rec isSolution (spec: OracleSpec) (index: int) : bool =
        match spec with
        | Predicate pred -> pred index
        | Solutions solList -> List.contains index solList
        | SingleTarget target -> index = target
        | And (spec1, spec2) -> isSolution spec1 index && isSolution spec2 index
        | Or (spec1, spec2) -> isSolution spec1 index || isSolution spec2 index
        | Not innerSpec -> not (isSolution innerSpec index)
    
    /// Count expected solutions for an oracle spec
    /// Returns None if count cannot be determined statically
    let rec countExpectedSolutions (spec: OracleSpec) (searchSpaceSize: int) : int option =
        match spec with
        | Predicate _ -> None  // Cannot statically count predicate results
        | Solutions solList -> Some (List.length solList)
        | SingleTarget _ -> Some 1
        | And (spec1, spec2) ->
            // AND: potentially fewer solutions than either spec alone
            // Conservative: return None unless we can compute exactly
            None
        | Or (spec1, spec2) ->
            // OR: potentially more solutions than either spec alone
            // For now, return None (would need set union logic)
            None
        | Not innerSpec ->
            // NOT: inverts the count
            match countExpectedSolutions innerSpec searchSpaceSize with
            | Some count -> Some (searchSpaceSize - count)
            | None -> None
    
    // ============================================================================
    // LOCAL SIMULATION - For testing and small problems
    // ============================================================================
    
    /// Apply oracle to quantum state (local simulation only)
    /// 
    /// Flips phase of solution states: |x⟩ → -|x⟩
    /// This is a pure function - creates new state, doesn't modify input
    let applyLocal (spec: OracleSpec) (state: StateVector.StateVector) : StateVector.StateVector =
        let dimension = StateVector.dimension state
        
        // Create new amplitude array with phase flips
        let newAmplitudes =
            [| 0 .. dimension - 1 |]
            |> Array.map (fun i ->
                let amp = StateVector.getAmplitude i state
                if isSolution spec i then
                    -amp  // Flip phase (multiply by -1)
                else
                    amp   // Keep original amplitude
            )
        
        StateVector.create newAmplitudes
    
    // ============================================================================
    // ORACLE COMPILATION - Backend-agnostic
    // ============================================================================
    
    /// Compile oracle specification into executable oracle
    /// 
    /// This is the main entry point for creating oracles.
    /// Returns CompiledOracle that works with both local and Azure backends.
    let compile (spec: OracleSpec) (numQubits: int) : Result<CompiledOracle, string> =
        if numQubits < 1 || numQubits > 20 then
            Error $"Number of qubits must be between 1 and 20, got {numQubits}"
        else
            let searchSpaceSize = 1 <<< numQubits  // 2^numQubits
            
            Ok {
                Spec = spec
                NumQubits = numQubits
                LocalSimulation = applyLocal spec
                ExpectedSolutions = countExpectedSolutions spec searchSpaceSize
            }
    
    // ============================================================================
    // ORACLE BUILDERS - Convenient creation functions
    // ============================================================================
    
    /// Create oracle that marks a single target value
    let forValue (target: int) (numQubits: int) : Result<CompiledOracle, string> =
        let searchSpaceSize = 1 <<< numQubits
        
        if target < 0 then
            Error $"Target must be non-negative, got {target}"
        elif target >= searchSpaceSize then
            Error $"Target {target} exceeds search space size {searchSpaceSize} for {numQubits} qubits"
        else
            compile (SingleTarget target) numQubits
    
    /// Create oracle that marks multiple solution values
    let forValues (solutions: int list) (numQubits: int) : Result<CompiledOracle, string> =
        let searchSpaceSize = 1 <<< numQubits
        
        if solutions.IsEmpty then
            Error "Solutions list cannot be empty"
        else
            let invalidSolutions = solutions |> List.filter (fun s -> s < 0 || s >= searchSpaceSize)
            
            if not invalidSolutions.IsEmpty then
                Error $"Solutions {invalidSolutions} are outside valid range [0, {searchSpaceSize - 1}] for {numQubits} qubits"
            else
                compile (Solutions solutions) numQubits
    
    /// Create oracle from predicate function
    let fromPredicate (predicate: int -> bool) (numQubits: int) : Result<CompiledOracle, string> =
        compile (Predicate predicate) numQubits
    
    // ============================================================================
    // ORACLE COMBINATORS - Pure functional composition
    // ============================================================================
    
    /// Combine two oracle specs with AND logic
    let andSpec (spec1: OracleSpec) (spec2: OracleSpec) : OracleSpec =
        And (spec1, spec2)
    
    /// Combine two oracle specs with OR logic
    let orSpec (spec1: OracleSpec) (spec2: OracleSpec) : OracleSpec =
        Or (spec1, spec2)
    
    /// Negate oracle spec (mark non-solutions)
    let notSpec (spec: OracleSpec) : OracleSpec =
        Not spec
    
    /// Combine two compiled oracles with AND logic
    let andOracle (oracle1: CompiledOracle) (oracle2: CompiledOracle) : Result<CompiledOracle, string> =
        if oracle1.NumQubits <> oracle2.NumQubits then
            Error $"Cannot combine oracles with different qubit counts ({oracle1.NumQubits} vs {oracle2.NumQubits})"
        else
            let combinedSpec = andSpec oracle1.Spec oracle2.Spec
            compile combinedSpec oracle1.NumQubits
    
    /// Combine two compiled oracles with OR logic
    let orOracle (oracle1: CompiledOracle) (oracle2: CompiledOracle) : Result<CompiledOracle, string> =
        if oracle1.NumQubits <> oracle2.NumQubits then
            Error $"Cannot combine oracles with different qubit counts ({oracle1.NumQubits} vs {oracle2.NumQubits})"
        else
            let combinedSpec = orSpec oracle1.Spec oracle2.Spec
            compile combinedSpec oracle1.NumQubits
    
    /// Negate compiled oracle
    let notOracle (oracle: CompiledOracle) : Result<CompiledOracle, string> =
        let negatedSpec = notSpec oracle.Spec
        compile negatedSpec oracle.NumQubits
    
    // ============================================================================
    // COMMON ORACLE PATTERNS - Reusable predicates
    // ============================================================================
    
    /// Oracle that marks even numbers
    let even (numQubits: int) : Result<CompiledOracle, string> =
        fromPredicate (fun x -> x % 2 = 0) numQubits
    
    /// Oracle that marks odd numbers
    let odd (numQubits: int) : Result<CompiledOracle, string> =
        fromPredicate (fun x -> x % 2 = 1) numQubits
    
    /// Oracle that marks numbers divisible by n
    let divisibleBy (n: int) (numQubits: int) : Result<CompiledOracle, string> =
        if n = 0 then
            Error "Divisor cannot be zero"
        else
            fromPredicate (fun x -> x % n = 0) numQubits
    
    /// Oracle that marks numbers in range [min, max] (inclusive)
    let inRange (min: int) (max: int) (numQubits: int) : Result<CompiledOracle, string> =
        if min > max then
            Error $"Invalid range: min ({min}) > max ({max})"
        else
            fromPredicate (fun x -> x >= min && x <= max) numQubits
    
    /// Oracle that marks numbers greater than threshold
    let greaterThan (threshold: int) (numQubits: int) : Result<CompiledOracle, string> =
        fromPredicate (fun x -> x > threshold) numQubits
    
    /// Oracle that marks numbers less than threshold
    let lessThan (threshold: int) (numQubits: int) : Result<CompiledOracle, string> =
        fromPredicate (fun x -> x < threshold) numQubits
    
    // ============================================================================
    // ORACLE VERIFICATION - Pure analysis functions
    // ============================================================================
    
    /// Verify oracle correctly marks solutions (local simulation only)
    /// Returns true if all expected solutions have flipped phase
    let verify (oracle: CompiledOracle) (expectedSolutions: int list) : bool =
        // Create uniform superposition
        let state = StateVector.init oracle.NumQubits
        
        // Apply Hadamard to all qubits to create uniform superposition
        let uniformState =
            [0 .. oracle.NumQubits - 1]
            |> List.fold (fun s qubitIdx -> Gates.applyH qubitIdx s) state
        
        // Apply oracle
        let markedState = oracle.LocalSimulation uniformState
        
        // Check that all expected solutions have negative phase
        let dimension = StateVector.dimension markedState
        
        expectedSolutions
        |> List.forall (fun sol ->
            if sol < dimension then
                let originalAmp = StateVector.getAmplitude sol uniformState
                let markedAmp = StateVector.getAmplitude sol markedState
                
                // Check phase flip: markedAmp ≈ -originalAmp
                let diff = Complex.Abs(markedAmp + originalAmp)
                diff < 1e-10  // Tolerance for floating-point comparison
            else
                false  // Solution index out of range
        )
    
    /// Count actual number of solutions marked by oracle
    /// Uses local simulation - expensive for large search spaces
    let countSolutions (oracle: CompiledOracle) : int =
        let searchSpaceSize = 1 <<< oracle.NumQubits
        
        [0 .. searchSpaceSize - 1]
        |> List.filter (fun i -> isSolution oracle.Spec i)
        |> List.length
    
    /// List all solutions marked by oracle
    /// Uses local evaluation - expensive for large search spaces
    let listSolutions (oracle: CompiledOracle) : int list =
        let searchSpaceSize = 1 <<< oracle.NumQubits
        
        [0 .. searchSpaceSize - 1]
        |> List.filter (fun i -> isSolution oracle.Spec i)
    
    // ============================================================================
    // ORACLE EXAMPLES - For documentation and testing
    // ============================================================================
    
    module Examples =
        
        /// Example: Search for specific value in 4-qubit space (0-15)
        let searchForValue (target: int) : Result<CompiledOracle, string> =
            forValue target 4
        
        /// Example: Search for multiple specific values
        let searchForMultiple (solutions: int list) : Result<CompiledOracle, string> =
            forValues solutions 4
        
        /// Example: Search for even numbers in 4-qubit space
        let searchEven : Result<CompiledOracle, string> =
            even 4
        
        /// Example: Search for numbers divisible by 3
        let searchDivisibleBy3 : Result<CompiledOracle, string> =
            divisibleBy 3 4
        
        /// Example: Search for numbers in range 5-10
        let searchRange5to10 : Result<CompiledOracle, string> =
            inRange 5 10 4
        
        /// Example: Complex query - even AND in range 4-12
        let searchEvenInRange : Result<CompiledOracle, string> =
            match even 4, inRange 4 12 4 with
            | Ok evenOracle, Ok rangeOracle -> andOracle evenOracle rangeOracle
            | Error msg, _ -> Error msg
            | _, Error msg -> Error msg
