# QuantumResult Computation Expression Builder

## Overview

The `quantumResult` computation expression builder eliminates nested `match` clauses when working with `QuantumResult<T>`, making error handling code cleaner and more maintainable.

## Before: Nested Match Clauses 

```fsharp
let processQuantumWorkflow (input: float array) (backend: IQuantumBackend) : QuantumResult<Solution> =
 match validateInput input with
 | Error err -> Error err
 | Ok validatedData ->
 match encodeToQubo validatedData with
 | Error err -> Error err
 | Ok quboMatrix ->
 match executeQuantum quboMatrix backend with
 | Error err -> Error err
 | Ok quantumResult ->
 match decodeResult quantumResult with
 | Error err -> Error err
 | Ok solution ->
 Ok solution
```

**Problems:**
- levels of nesting
- Repetitive error propagation (`| Error err -> Error err`)
- Hard to read and maintain
- Easy to make mistakes

## After: Computation Expression 

```fsharp
let processQuantumWorkflow (input: float array) (backend: IQuantumBackend) : QuantumResult<Solution> =
 quantumResult {
 let! validatedData = validateInput input
 let! quboMatrix = encodeToQubo validatedData
 let! quantumResult = executeQuantum quboMatrix backend
 let! solution = decodeResult quantumResult
 return solution
 }
```

**Benefits:**
- Flat, linear structure
- Automatic error propagation
- Reads like imperative code
- Much easier to maintain

## Real-World Examples

### Example : TSP Solver with Validation

**Before:**
```fsharp
let solveTsp (cities: City list) (backend: IQuantumBackend) : QuantumResult<Tour> =
 match validateCities cities with
 | Error err -> Error err
 | Ok validCities ->
 match buildDistanceMatrix validCities with
 | Error err -> Error err
 | Ok distances ->
 match QuantumTspSolver.solve backend distances defaultConfig with
 | Error err -> Error err
 | Ok quantumResult ->
 match validateTour quantumResult.Tour cities.Length with
 | Error err -> Error err
 | Ok validTour ->
 Ok { Cities = getCityNames validCities validTour
 TotalDistance = quantumResult.TourLength
 IsValid = true }
```

**After:**
```fsharp
let solveTsp (cities: City list) (backend: IQuantumBackend) : QuantumResult<Tour> =
 quantumResult {
 let! validCities = validateCities cities
 let! distances = buildDistanceMatrix validCities
 let! quantumResult = QuantumTspSolver.solve backend distances defaultConfig
 let! validTour = validateTour quantumResult.Tour cities.Length
 
 return { 
 Cities = getCityNames validCities validTour
 TotalDistance = quantumResult.TourLength
 IsValid = true 
 }
 }
```

### Example : ML Training Pipeline

**Before:**
```fsharp
let trainQuantumModel (data: TrainingData) (backend: IQuantumBackend) : QuantumResult<Model> =
 match validateTrainingData data with
 | Error err -> Error err
 | Ok validData ->
 match preprocessFeatures validData.Features with
 | Error err -> Error err
 | Ok processedFeatures ->
 match encodeToQuantumCircuit processedFeatures with
 | Error err -> Error err
 | Ok circuit ->
 match trainVQC circuit validData.Labels backend with
 | Error err -> Error err
 | Ok trainedParams ->
 match serializeModel trainedParams with
 | Error err -> Error err
 | Ok model ->
 Ok model
```

**After:**
```fsharp
let trainQuantumModel (data: TrainingData) (backend: IQuantumBackend) : QuantumResult<Model> =
 quantumResult {
 let! validData = validateTrainingData data
 let! processedFeatures = preprocessFeatures validData.Features
 let! circuit = encodeToQuantumCircuit processedFeatures
 let! trainedParams = trainVQC circuit validData.Labels backend
 let! model = serializeModel trainedParams
 return model
 }
```

### Example : Sequential Processing with Intermediate Values

```fsharp
let optimizePortfolio (stocks: Stock list) (budget: float) : QuantumResult<PortfolioAllocation> =
 quantumResult {
 // Validate inputs
 let! validStocks = validateStocks stocks
 let! validBudget = validateBudget budget
 
 // Analyze risk
 let! riskProfile = calculateRiskProfile validStocks
 
 // Encode as QUBO
 let! quboMatrix = encodePortfolioQubo validStocks validBudget riskProfile
 
 // Solve with quantum backend
 let backend = LocalBackendFactory.createUnified()
 let! solution = solveQubo quboMatrix backend
 
 // Decode and validate
 let! allocation = decodeSolution solution validStocks
 let! validatedAllocation = validateAllocation allocation budget
 
 return validatedAllocation
 }
```

## Advanced Features

### Exception Handling with try-with

```fsharp
let safeQuantumExecution (circuit: ICircuit) (backend: IQuantumBackend) : QuantumResult<ExecutionResult> =
 quantumResult {
 try
 let! validated = validateCircuit circuit
 let! result = backend.ExecuteAsync validated |> Async.RunSynchronously
 return result
 with
 | :? TimeoutException as ex ->
 return! Error (QuantumError.OperationError ("Execution", $"Timeout: {ex.Message}"))
 | ex ->
 return! Error (QuantumError.OperationError ("Execution", $"Failed: {ex.Message}"))
 }
```

### Loops and Iteration

```fsharp
let validateMultipleCircuits (circuits: ICircuit list) : QuantumResult<unit> =
 quantumResult {
 for circuit in circuits do
 let! _ = validateCircuit circuit
 ()
 return ()
 }
```

### Combining Results

```fsharp
let processMultipleInputs (inputs: float array list) : QuantumResult<float list> =
 quantumResult {
 let results = ResizeArray<float>()
 
 for input in inputs do
 let! validated = validateInput input
 let! processed = processData validated
 results.Add(processed)
 
 return List.ofSeq results
 }
```

## Migration Guide

### Step : Identify Nested Matches

Look for patterns like:
```fsharp
match expr with
| Error err -> Error err
| Ok val ->
 match expr with
 | Error err -> Error err
 | Ok val ->
 ...
```

### Step : Convert to Computation Expression

Replace with:
```fsharp
quantumResult {
 let! val = expr
 let! val = expr
 ...
}
```

### Step : Handle Special Cases

**Early return on error:**
```fsharp
quantumResult {
 let! data = getData()
 
 // Early validation
 if data.Length = then
 return! Error (QuantumError.ValidationError ("Data", "Cannot be empty"))
 
 let! result = processData data
 return result
}
```

**Conditional logic:**
```fsharp
quantumResult {
 let! analysis = analyzeData data
 
 let! solution = 
 if analysis.RecommendQuantum then
 solveWithQuantum data backend
 else
 solveClassically data
 
 return solution
}
```

## Best Practices

### DO

. **Use for sequential operations with error handling**
 ```fsharp
 quantumResult {
 let! a = stepA()
 let! b = stepB a
 let! c = stepC b
 return c
 }
 ```

. **Combine with regular let bindings**
 ```fsharp
 quantumResult {
 let! validated = validate input
 let transformed = transform validated // No error possible
 let! result = executeQuantum transformed
 return result
 }
 ```

. **Use return! for returning QuantumResult directly**
 ```fsharp
 quantumResult {
 let! data = getData()
 return! processAndReturn data // processAndReturn returns QuantumResult
 }
 ```

### DON'T

. **Use for simple single operations**
 ```fsharp
 // Bad - unnecessary
 quantumResult {
 return! validate input
 }
 
 // Good - direct call
 validate input
 ```

. **Nest computation expressions**
 ```fsharp
 // Bad - defeats the purpose
 quantumResult {
 let! outer = quantumResult {
 let! inner = getInner()
 return inner
 }
 return outer
 }
 
 // Good - flatten
 quantumResult {
 let! inner = getInner()
 return inner
 }
 ```

## Comparison with Other Patterns

### vs Result.bind

**Result.bind chain:**
```fsharp
validateInput input
|> Result.bind encodeToQubo
|> Result.bind (fun qubo -> executeQuantum qubo backend)
|> Result.bind decodeResult
```

**Computation expression:**
```fsharp
quantumResult {
 let! validated = validateInput input
 let! qubo = encodeToQubo validated
 let! result = executeQuantum qubo backend
 let! decoded = decodeResult result
 return decoded
}
```

**When to use each:**
- Use `Result.bind` for simple linear chains
- Use `quantumResult` when you need intermediate values or branching logic

### vs Railway-Oriented Programming

The `quantumResult` builder IS railway-oriented programming, just with nicer syntax!

```
 validate ──→ encode ──→ execute ──→ decode ──→ Success
 │ │ │ │
 ↓ Error ↓ Error ↓ Error ↓ Error
```

The computation expression automatically handles the "switch to error track" logic.

## Summary

The `quantumResult` computation expression builder:

 **Eliminates** nested match clauses 
 **Simplifies** error handling 
 **Improves** code readability 
 **Reduces** boilerplate 
 **Maintains** type safety 
 **Supports** advanced features (loops, try-with, etc.) 

Use it whenever you have + sequential operations that return `QuantumResult<T>`!
