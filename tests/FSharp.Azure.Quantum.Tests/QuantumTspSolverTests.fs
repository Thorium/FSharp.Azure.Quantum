namespace FSharp.Azure.Quantum.Tests

open System.Numerics
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Classical
open FSharp.Azure.Quantum.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.LocalSimulator
open System.Threading
open System.Threading.Tasks

// The QUBO and the decode are checked exhaustively (energy only, no circuit). Solver runs
// assert only what holds for every sampling outcome: a run either returns a tour that was
// measured as a permutation matrix, or reports that no measurement was a valid tour.
module QuantumTspSolverTests =

    // Helper to create local backend for tests
    let private createLocalBackend () : BackendAbstraction.IQuantumBackend =
        LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend

    /// A single one-layer circuit at the fastConfig angles with the given shot count.
    let private solveWithShots backend distances shots =
        QuantumTspSolver.solveAsync
            backend
            distances
            { QuantumTspSolver.fastConfig with
                FinalShots = shots
            }
            CancellationToken.None

    let private solveWithDefaults distances =
        QuantumTspSolver.solveAsync
            (createLocalBackend ())
            distances
            QuantumTspSolver.defaultConfig
            CancellationToken.None

    // ========================================================================
    // Helper Functions
    // ========================================================================

    /// Create simple 3-city TSP distance matrix (triangle); its only cycle has length 4.5
    let create3CityProblem () =
        array2D [ [ 0.0; 1.0; 2.0 ]; [ 1.0; 0.0; 1.5 ]; [ 2.0; 1.5; 0.0 ] ]

    /// Create simple 4-city TSP distance matrix (square)
    let create4CityProblem () =
        array2D
            [
                [ 0.0; 1.0; 4.0; 2.0 ]
                [ 1.0; 0.0; 2.0; 3.0 ]
                [ 4.0; 2.0; 0.0; 1.0 ]
                [ 2.0; 3.0; 1.0; 0.0 ]
            ]

    /// Symmetric matrix from its upper-triangle entries.
    let private symmetric (n: int) (entries: ((int * int) * float) list) =
        let d = Array2D.zeroCreate n n

        for ((i, j), v) in entries do
            d.[i, j] <- v
            d.[j, i] <- v

        d

    /// 4 cities whose only optimal cycle is 0-2-1-3 (length 4); the index order 0-1-2-3 costs 12.
    let private offIndexSquare () =
        symmetric 4 [ (0, 2), 1.0; (1, 2), 1.0; (1, 3), 1.0; (0, 3), 1.0; (0, 1), 5.0; (2, 3), 5.0 ]

    /// 3 cities with direction-dependent distances: 0→1→2→0 costs 3, the reverse costs 30.
    let private oneWayTriangle () =
        array2D [ [ 0.0; 1.0; 10.0 ]; [ 10.0; 0.0; 1.0 ]; [ 1.0; 10.0; 0.0 ] ]

    let private scaled (factor: float) (distances: float[,]) = distances |> Array2D.map ((*) factor)

    let rec private orderings (items: int list) : int list list =
        match items with
        | [] -> [ [] ]
        | _ ->
            items
            |> List.collect (fun x -> orderings (List.filter ((<>) x) items) |> List.map (fun rest -> x :: rest))

    /// The city in each time slot when the bits (variable i·n + t) are a permutation matrix.
    let private permutationOf (n: int) (bits: int[]) : int[] option =
        let rowSum i =
            Seq.sum (seq { for t in 0 .. n - 1 -> bits.[i * n + t] })

        let columnSum t =
            Seq.sum (seq { for i in 0 .. n - 1 -> bits.[i * n + t] })

        if
            Seq.forall (fun i -> rowSum i = 1) (seq { 0 .. n - 1 })
            && Seq.forall (fun t -> columnSum t = 1) (seq { 0 .. n - 1 })
        then
            Some(Array.init n (fun t -> Seq.find (fun i -> bits.[i * n + t] = 1) (seq { 0 .. n - 1 })))
        else
            None

    /// The permutation matrix (variable i·n + t) of the cities in time-slot order.
    let private bitsOfOrder (order: int[]) : int[] =
        let n = order.Length
        let bits = Array.zeroCreate (n * n)

        order |> Array.iteri (fun t city -> bits.[city * n + t] <- 1)
        bits

    /// Bit q of the index is variable q.
    let private bitsOfIndex (numQubits: int) (index: int) =
        Array.init numQubits (fun q -> (index >>> q) &&& 1)

    let private indexOfBits (bits: int[]) =
        bits |> Array.mapi (fun q bit -> bit <<< q) |> Array.sum

    /// The tour visits every city once, starts at city 0 and has the stated closed length.
    let private assertConsistentTour (distances: float[,]) (tour: int[]) (length: float) =
        let n = distances.GetLength 0
        Assert.Equal<int[]>([| 0 .. n - 1 |], Array.sort tour)
        Assert.Equal(0, tour.[0])
        Assert.Equal(TspSolver.calculateTourLength distances tour, length, 9)

    /// What every returned solution satisfies, whatever was sampled.
    let private assertSolutionInvariants (distances: float[,]) (solution: QuantumTspSolver.QuantumTspSolution) =
        let n = distances.GetLength 0
        assertConsistentTour distances solution.Tour solution.TourLength
        Assert.Equal(solution.TourLength, solution.BestEnergy)

        Assert.InRange(solution.TopSolutions.Length, 1, 5)

        for (tour, length, frequency) in solution.TopSolutions do
            assertConsistentTour distances tour length
            Assert.True(frequency > 0)

        let tours =
            solution.TopSolutions |> List.map (fun (tour, _, _) -> List.ofArray tour)

        Assert.Equal(tours.Length, (List.distinct tours).Length)

        let lengths = solution.TopSolutions |> List.map (fun (_, length, _) -> length)
        Assert.Equal<float list>(List.sort lengths, lengths)

        let (bestTour, bestLength, bestFrequency) = solution.TopSolutions.[0]
        Assert.Equal<int[]>(solution.Tour, bestTour)
        Assert.Equal(solution.TourLength, bestLength)

        match solution.Sampling with
        | None -> Assert.Fail "no sampling statistics"
        | Some stats ->
            Assert.Equal(solution.NumShots, stats.Shots)
            Assert.Equal(n * n, stats.Qubits)
            Assert.Equal(bestFrequency, stats.Hits)
            Assert.InRange(stats.Hits, 1, stats.Valid)
            Assert.InRange(stats.Valid, 1, stats.Shots)

            Assert.True(
                (solution.TopSolutions |> List.sumBy (fun (_, _, frequency) -> frequency))
                <= stats.Valid
            )

    /// A run returns a solution with the invariants above, or the error that names the
    /// shots taken when no measurement was a valid tour. `check` holds for every solution.
    let private assertTourOrNoValidTour
        (distances: float[,])
        (shots: int)
        (check: QuantumTspSolver.QuantumTspSolution -> unit)
        (result: Result<QuantumTspSolver.QuantumTspSolution, QuantumError>)
        =
        match result with
        | Ok solution ->
            Assert.Equal(shots, solution.NumShots)
            assertSolutionInvariants distances solution
            check solution
        | Error err -> Assert.Contains($"No valid tour in {shots} shots", err.Message)

    // ========================================================================
    // QUBO ground states (energy only, no circuit)
    // ========================================================================

    /// Every minimum-energy bitstring of the solver's QUBO is a permutation matrix whose
    /// closed tour has the brute-force optimal length, and every optimal ordering of the
    /// cities is a minimum-energy bitstring.
    let private assertGroundStatesAreOptimalTours (label: string) (distances: float[,]) =
        let n = distances.GetLength 0
        let numQubits = n * n
        let qubo = QuantumTspSolver.toQubo distances

        let entries =
            [|
                for i in 0 .. numQubits - 1 do
                    for j in 0 .. numQubits - 1 do
                        if qubo.[i, j] <> 0.0 then
                            yield struct (i, j, qubo.[i, j])
            |]

        let energies =
            Array.init (1 <<< numQubits) (fun index ->
                let mutable total = 0.0

                for struct (i, j, v) in entries do
                    if (index >>> i) &&& 1 = 1 && (index >>> j) &&& 1 = 1 then
                        total <- total + v

                total)

        let minimum = Array.min energies
        let tolerance = 1e-9 * max 1.0 (abs minimum)

        let groundStates =
            energies
            |> Array.indexed
            |> Array.filter (fun (_, e) -> e <= minimum + tolerance)
            |> Array.map (fun (index, _) -> bitsOfIndex numQubits index)

        let lengths =
            orderings [ 0 .. n - 1 ]
            |> List.map (Array.ofList >> TspSolver.calculateTourLength distances)

        let optimum = List.min lengths

        let optimalOrderings =
            lengths
            |> List.filter (fun l -> abs (l - optimum) <= 1e-9 * max 1.0 optimum)
            |> List.length

        for bits in groundStates do
            let text = bits |> Array.map string |> String.concat ""

            match permutationOf n bits with
            | None -> Assert.Fail $"{label}: minimum-energy bitstring {text} is not a permutation matrix"
            | Some tour ->
                let length = TspSolver.calculateTourLength distances tour

                Assert.True(
                    abs (length - optimum) <= 1e-9 * max 1.0 optimum,
                    $"%s{label}: minimum-energy tour %A{tour} has length {length}, the optimum is {optimum}"
                )

        Assert.Equal(optimalOrderings, groundStates.Length)

    [<Fact>]
    let ``QUBO ground states are the optimal tours for 3 cities with one-way distances`` () =
        assertGroundStatesAreOptimalTours "one-way triangle" (oneWayTriangle ())

    [<Fact>]
    let ``QUBO ground states are the optimal tours for 3 cities at distance scale 100`` () =
        assertGroundStatesAreOptimalTours "triangle x100" (scaled 100.0 (create3CityProblem ()))

    [<Fact>]
    let ``QUBO ground states are the optimal tours for 4 cities whose index order is not optimal`` () =
        assertGroundStatesAreOptimalTours "off-index square" (offIndexSquare ())

    [<Fact>]
    let ``QUBO ground states are the optimal tours for 4 cities at distance scale 100`` () =
        assertGroundStatesAreOptimalTours "off-index square x100" (scaled 100.0 (offIndexSquare ()))

    [<Fact>]
    let ``QUBO ground states are the optimal tours at small and zero distances`` () =
        assertGroundStatesAreOptimalTours "one-way triangle x0.001" (scaled 0.001 (oneWayTriangle ()))
        assertGroundStatesAreOptimalTours "all distances zero" (Array2D.zeroCreate 3 3)
        assertGroundStatesAreOptimalTours "2 cities" (array2D [ [ 0.0; 3.0 ]; [ 5.0; 0.0 ] ])

    // ========================================================================
    // Decode (exhaustive, no circuit)
    // ========================================================================

    [<Fact>]
    let ``tryDecodeTour accepts exactly the permutation matrices`` () =
        for distances in [ create3CityProblem (); oneWayTriangle (); create4CityProblem () ] do
            let n = distances.GetLength 0

            let decoded =
                Array.init (1 <<< (n * n)) (fun index ->
                    let bits = bitsOfIndex (n * n) index
                    bits, QuantumTspSolver.tryDecodeTour distances bits)

            for (bits, tour) in decoded do
                match permutationOf n bits, tour with
                | None, None -> ()
                | Some order, Some tour ->
                    // The decoded tour is the measured cycle: same closed length, every city once
                    Assert.Equal<int[]>([| 0 .. n - 1 |], Array.sort tour)
                    Assert.Equal(0, tour.[0])

                    Assert.Equal(
                        TspSolver.calculateTourLength distances order,
                        TspSolver.calculateTourLength distances tour,
                        9
                    )
                | expected, actual ->
                    let text = bits |> Array.map string |> String.concat ""
                    Assert.Fail $"%s{text}: permutation %A{expected}, decoded %A{actual}"

            let factorial = List.fold (*) 1 [ 1..n ]
            Assert.Equal(factorial, decoded |> Array.filter (fun (_, tour) -> tour.IsSome) |> Array.length)

    [<Fact>]
    let ``tryDecodeTour rejects wrong lengths, empty slots and repeated cities`` () =
        let distances = create3CityProblem ()
        // City 0 in slots 0 and 1, city 1 in slot 2: city 2 is missing
        Assert.Equal(None, QuantumTspSolver.tryDecodeTour distances [| 1; 1; 0; 0; 0; 1; 0; 0; 0 |])
        // Cities 0 and 1 in slots 0 and 1, slot 2 empty
        Assert.Equal(None, QuantumTspSolver.tryDecodeTour distances [| 1; 0; 0; 0; 1; 0; 0; 0; 0 |])
        // A permutation with one extra city in slot 0
        Assert.Equal(None, QuantumTspSolver.tryDecodeTour distances [| 1; 0; 0; 1; 1; 0; 0; 0; 1 |])
        Assert.Equal(None, QuantumTspSolver.tryDecodeTour distances (Array.zeroCreate 9))
        Assert.Equal(None, QuantumTspSolver.tryDecodeTour distances (Array.create 9 1))
        Assert.Equal(None, QuantumTspSolver.tryDecodeTour distances [| 1; 0; 0; 0; 1; 0; 0; 0 |])

    [<Fact>]
    let ``tryDecodeTour gives one tour for the rotations and directions of a symmetric cycle`` () =
        let distances = create4CityProblem ()

        let byTour =
            orderings [ 0..3 ]
            |> List.map (Array.ofList >> bitsOfOrder >> QuantumTspSolver.tryDecodeTour distances)
            |> List.countBy id

        // 4! = 24 permutation matrices: 3 distinct cycles, each measured as 2n = 8 bitstrings
        Assert.Equal(3, byTour.Length)

        for (tour, count) in byTour do
            Assert.True(tour.IsSome)
            Assert.Equal(8, count)
            Assert.True(tour.Value.[1] < tour.Value.[3])

        Assert.Equal<int list option>(
            Some [ 0; 2; 1; 3 ],
            QuantumTspSolver.tryDecodeTour (offIndexSquare ()) (bitsOfOrder [| 1; 2; 0; 3 |])
            |> Option.map List.ofArray
        )

    [<Fact>]
    let ``tryDecodeTour keeps the direction when distances depend on it`` () =
        let distances = oneWayTriangle ()

        let byTour =
            orderings [ 0..2 ]
            |> List.map (fun order ->
                QuantumTspSolver.tryDecodeTour distances (bitsOfOrder (Array.ofList order))
                |> Option.map List.ofArray)
            |> List.countBy id
            |> List.sort

        // 3! = 6 permutation matrices: two directed cycles, each measured as n = 3 bitstrings
        Assert.Equal<(int list option * int) list>([ Some [ 0; 1; 2 ], 3; Some [ 0; 2; 1 ], 3 ], byTour)

    // ========================================================================
    // Solver on measurements known in advance
    // ========================================================================

    /// Backend whose every circuit ends in the given state, so that the measurements are
    /// known in advance: a basis state measures to the same bitstring on every shot.
    type private FixedStateBackend(amplitudes: Complex[]) =
        let inner = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend

        let state () =
            QuantumState.StateVector(StateVector.create amplitudes)

        interface BackendAbstraction.IQuantumBackend with
            member _.Name = "Fixed State"
            member _.NativeStateType = inner.NativeStateType
            member _.SupportsOperation op = inner.SupportsOperation op
            member _.InitializeState n = inner.InitializeState n
            member _.ApplyOperation op state = inner.ApplyOperation op state
            member _.ExecuteToState _ = Ok(state ())
            member _.ExecuteToStateAsync _ _ = Task.FromResult(Ok(state ()))
            member _.ApplyOperationAsync op state ct = inner.ApplyOperationAsync op state ct

    /// Backend that measures the given bitstrings with equal probability.
    let private measuring (bitstrings: int[] list) : BackendAbstraction.IQuantumBackend =
        let amplitudes = Array.create (1 <<< bitstrings.Head.Length) Complex.Zero
        let amplitude = Complex(1.0 / sqrt (float bitstrings.Length), 0.0)

        for bits in bitstrings do
            amplitudes.[indexOfBits bits] <- amplitude

        FixedStateBackend amplitudes :> BackendAbstraction.IQuantumBackend

    let private ok (result: Result<'T, QuantumError>) : 'T =
        result |> Result.defaultWith (fun err -> failwith err.Message)

    /// TopSolutions with the tours as lists.
    let private topSolutions (solution: QuantumTspSolver.QuantumTspSolution) =
        solution.TopSolutions
        |> List.map (fun (tour, length, frequency) -> (List.ofArray tour, length, frequency))

    [<Fact>]
    let ``solve returns the measured permutation with the return leg charged`` () : Task =
        task {
            let distances = oneWayTriangle ()
            // Slots hold cities 2, 1, 0: the cycle 0→2→1→0, which costs 10 + 10 + 10
            let! result = solveWithShots (measuring [ bitsOfOrder [| 2; 1; 0 |] ]) distances 20
            let solution = ok result

            assertSolutionInvariants distances solution
            Assert.Equal<int[]>([| 0; 2; 1 |], solution.Tour)
            Assert.Equal(30.0, solution.TourLength)
            Assert.Equal<(int list * float * int) list>([ [ 0; 2; 1 ], 30.0, 20 ], topSolutions solution)
            Assert.Equal(20, solution.Sampling.Value.Hits)
            Assert.Equal(20, solution.Sampling.Value.Valid)
            Assert.Equal("Fixed State", solution.BackendName)
        }

    [<Fact>]
    let ``solve reports an error when no measurement is a permutation matrix`` () : Task =
        task {
            let distances = create3CityProblem ()

            let notTours =
                [
                    Array.zeroCreate 9
                    // Two cities placed, the third missing
                    [| 1; 0; 0; 0; 1; 0; 0; 0; 0 |]
                    // City 0 twice, city 2 missing
                    [| 1; 1; 0; 0; 0; 1; 0; 0; 0 |]
                    // The index-order permutation with city 1 also in slot 0
                    [| 1; 0; 0; 1; 1; 0; 0; 0; 1 |]
                ]

            for bits in notTours do
                match! solveWithShots (measuring [ bits ]) distances 20 with
                | Ok solution -> Assert.Fail $"decoded %A{bits} to %A{solution.Tour}"
                | Error err ->
                    Assert.Contains("No valid tour in 20 shots", err.Message)
                    Assert.Contains("FinalShots", err.Message)
                    Assert.Contains("NumLayers", err.Message)

            match! solveWithShots (measuring notTours) distances 20 with
            | Ok solution -> Assert.Fail $"decoded to %A{solution.Tour}"
            | Error err -> Assert.Contains("No valid tour in 20 shots", err.Message)
        }

    [<Fact>]
    let ``solve counts the bitstrings of one symmetric cycle as one solution`` () : Task =
        task {
            let distances = create3CityProblem ()
            let permutations = orderings [ 0..2 ] |> List.map (Array.ofList >> bitsOfOrder)

            let! result = solveWithShots (measuring permutations) distances 40
            let solution = ok result

            assertSolutionInvariants distances solution
            Assert.Equal<(int list * float * int) list>([ [ 0; 1; 2 ], 4.5, 40 ], topSolutions solution)
            Assert.Equal(40, solution.Sampling.Value.Hits)
            Assert.Equal(40, solution.Sampling.Value.Valid)
        }

    [<Fact>]
    let ``solve returns the shortest measured tour and counts only permutation matrices as valid`` () : Task =
        task {
            let distances = oneWayTriangle ()
            let permutations = orderings [ 0..2 ] |> List.map (Array.ofList >> bitsOfOrder)

            // Every sample is one of the two directed cycles
            let! result = solveWithShots (measuring permutations) distances 40
            let solution = ok result
            assertSolutionInvariants distances solution
            Assert.Equal(40, solution.Sampling.Value.Valid)
            Assert.Equal(40, solution.TopSolutions |> List.sumBy (fun (_, _, frequency) -> frequency))

            for (tour, length, _) in solution.TopSolutions do
                Assert.Contains((List.ofArray tour, length), [ [ 0; 1; 2 ], 3.0; [ 0; 2; 1 ], 30.0 ])

            // Half of the probability is on bitstrings that are not tours
            let mixed = measuring (bitsOfOrder [| 1; 2; 0 |] :: [ Array.zeroCreate 9 ])

            let! result = solveWithShots mixed distances 40

            result
            |> assertTourOrNoValidTour distances 40 (fun solution ->
                Assert.Equal<int[]>([| 0; 1; 2 |], solution.Tour)
                Assert.Equal(3.0, solution.TourLength)
                Assert.Equal(solution.Sampling.Value.Valid, solution.Sampling.Value.Hits))
        }

    [<Fact>]
    let ``solve reports the angles of every layer`` () : Task =
        task {
            let distances = create3CityProblem ()
            let backend = measuring [ bitsOfOrder [| 0; 1; 2 |] ]

            let! fixedAngles =
                QuantumTspSolver.solveAsync
                    backend
                    distances
                    { QuantumTspSolver.fastConfig with
                        NumLayers = 3
                        InitialParameters = (0.3, 0.2)
                        FinalShots = 10
                    }
                    CancellationToken.None

            let solution = ok fixedAngles
            Assert.Equal<(float * float)[]>(Array.create 3 (0.3, 0.2), solution.LayerParameters)
            Assert.Equal(None, solution.OptimizedParameters)
            Assert.Equal(None, solution.OptimizationConverged)
            Assert.Equal(None, solution.OptimizationIterations)

            let! optimised =
                QuantumTspSolver.solveAsync
                    backend
                    distances
                    { QuantumTspSolver.defaultConfig with
                        FinalShots = 10
                    }
                    CancellationToken.None

            let solution = ok optimised
            Assert.Equal(QuantumTspSolver.defaultConfig.NumLayers, solution.LayerParameters.Length)
            Assert.Equal(2, solution.LayerParameters.Length)
            Assert.Equal(Some solution.LayerParameters.[0], solution.OptimizedParameters)
            Assert.True(solution.OptimizationConverged.IsSome)
            Assert.True(solution.OptimizationIterations.IsSome)
        }

    // ========================================================================
    // Input Validation Tests
    // ========================================================================

    [<Fact>]
    let ``solve should reject single city`` () : Task =
        task {
            let backend = createLocalBackend ()
            let distances = array2D [ [ 0.0 ] ]

            let! result = solveWithShots backend distances 100

            result
            |> Result.map (fun _ -> Assert.True(false, "Should reject single city"))
            |> Result.defaultWith (fun err -> Assert.Contains("at least 2 cities", err.Message))
        }

    [<Fact>]
    let ``solve should reject negative shots`` () : Task =
        task {
            let backend = createLocalBackend ()
            let distances = create3CityProblem ()

            let! result = solveWithShots backend distances -10

            result
            |> Result.map (fun _ -> Assert.True(false, "Should reject negative shots"))
            |> Result.defaultWith (fun err -> Assert.Contains("positive", err.Message))
        }

    [<Fact>]
    let ``solve should reject zero shots`` () : Task =
        task {
            let backend = createLocalBackend ()
            let distances = create3CityProblem ()

            let! result = solveWithShots backend distances 0

            result
            |> Result.map (fun _ -> Assert.True(false, "Should reject zero shots"))
            |> Result.defaultWith (fun err -> Assert.Contains("positive", err.Message))
        }

    [<Fact>]
    let ``solve should reject zero layers`` () : Task =
        task {
            let! result =
                QuantumTspSolver.solveAsync
                    (createLocalBackend ())
                    (create3CityProblem ())
                    { QuantumTspSolver.fastConfig with
                        NumLayers = 0
                    }
                    CancellationToken.None

            match result with
            | Ok _ -> Assert.Fail "Should reject zero layers"
            | Error err -> Assert.Contains("NumLayers", err.Message)
        }

    [<Fact>]
    let ``solve should reject negative, non-finite and non-square distances`` () : Task =
        task {
            let backend = createLocalBackend ()

            let invalid =
                [
                    array2D [ [ 0.0; 1.0; 2.0 ]; [ 1.0; 0.0; -1.5 ]; [ 2.0; 1.5; 0.0 ] ]
                    array2D [ [ 0.0; 1.0; 2.0 ]; [ 1.0; 0.0; nan ]; [ 2.0; 1.5; 0.0 ] ]
                    array2D [ [ 0.0; infinity; 2.0 ]; [ 1.0; 0.0; 1.5 ]; [ 2.0; 1.5; 0.0 ] ]
                    array2D [ [ 0.0; 1.0 ]; [ 1.0; 0.0 ]; [ 2.0; 1.5 ] ]
                ]

            for distances in invalid do
                match! solveWithShots backend distances 100 with
                | Ok _ -> Assert.Fail $"Should reject %A{distances}"
                | Error err -> Assert.Contains("'distances'", err.Message)
        }

    [<Fact>]
    let ``solve should reject problem too large for backend`` () : Task =
        task {
            let backend: BackendAbstraction.IQuantumBackend = createLocalBackend () // Max 16 qubits
            // 5 cities requires 5*5 = 25 qubits (exceeds limit)
            let distances = Array2D.zeroCreate 6 6

            match! solveWithShots backend distances 100 with
            | Error err ->
                Assert.Contains("qubits", err.Message)
                // The solver checks backend capacity up front, so the message names the
                // backend and the required size rather than surfacing a bare limit error
                // from deep inside circuit execution.
                Assert.Contains(backend.Name, err.Message)
                Assert.Contains("36", err.Message) // 6 cities → 6² qubits
            | Ok _ -> Assert.Fail("Should reject problem too large")
        }

    [<Fact>]
    let ``solve should reject a problem that fits in memory but not in time`` () : Task =
        task {
            // 5 cities → 25 qubits. That FITS: the memory-derived capacity is 28 on a 32GB box
            // and 30 on a large one. It does not FINISH — the solver drives a 25-qubit state
            // through its whole optimisation budget, which is hours.
            //
            // Checking capacity alone admitted this, which is why the admission check asks
            // getRunnableQubits (capacity and wall-clock together) rather than getMaxQubits.
            // The 6-city case above cannot catch it: 36 qubits exceeds capacity too, so it is
            // refused either way.
            let backend: BackendAbstraction.IQuantumBackend = createLocalBackend ()
            let distances = Array2D.zeroCreate 5 5

            match! solveWithShots backend distances 100 with
            | Error err ->
                Assert.Contains("qubits", err.Message)
                Assert.Contains("25", err.Message)
            | Ok _ ->
                Assert.Fail(
                    "Should refuse 25 qubits: it fits the memory budget but not the wall-clock one, "
                    + "so accepting it trades a fast refusal for an hours-long run"
                )
        }

    // ========================================================================
    // Execution on the local simulator
    // ========================================================================

    [<Fact>]
    let ``solve on the local backend returns a measured tour or reports that none was measured`` () : Task =
        task {
            let backend = createLocalBackend ()
            let distances = create3CityProblem ()

            for shots in [ 10; 50; 200 ] do
                let! result = solveWithShots backend distances shots

                result
                |> assertTourOrNoValidTour distances shots (fun solution ->
                    Assert.Equal("Local Simulator", solution.BackendName)
                    Assert.True(solution.ElapsedMs >= 0.0)
                    // A symmetric triangle has one cycle, so every valid sample is the returned tour
                    Assert.Equal<int[]>([| 0; 1; 2 |], solution.Tour)
                    Assert.Equal(4.5, solution.TourLength, 9)
                    Assert.Equal(1, solution.TopSolutions.Length)
                    Assert.Equal(solution.Sampling.Value.Valid, solution.Sampling.Value.Hits)
                    Assert.Equal<(float * float)[]>([| (0.5, 0.5) |], solution.LayerParameters))
        }

    [<Fact>]
    let ``solve with one-way distances returns one of the two directed cycles`` () : Task =
        task {
            let distances = oneWayTriangle ()
            let! result = solveWithShots (createLocalBackend ()) distances 200

            result
            |> assertTourOrNoValidTour distances 200 (fun solution ->
                Assert.Contains(
                    (List.ofArray solution.Tour, solution.TourLength),
                    [ [ 0; 1; 2 ], 3.0; [ 0; 2; 1 ], 30.0 ]
                )

                Assert.Equal(
                    solution.Sampling.Value.Valid,
                    solution.TopSolutions |> List.sumBy (fun (_, _, frequency) -> frequency)
                ))
        }

    [<Fact>]
    let ``solve for 4 cities returns a measured tour or reports that none was measured`` () : Task =
        task {
            // 4 cities = 16 qubits, one circuit
            let distances = offIndexSquare ()
            let! result = solveWithShots (createLocalBackend ()) distances 300

            result
            |> assertTourOrNoValidTour distances 300 (fun solution ->
                Assert.Contains(
                    (List.ofArray solution.Tour, solution.TourLength),
                    [ [ 0; 2; 1; 3 ], 4.0; [ 0; 1; 2; 3 ], 12.0; [ 0; 1; 3; 2 ], 12.0 ]
                ))
        }

    // ========================================================================
    // Default Parameters Test
    // ========================================================================

    [<Fact>]
    let ``defaultConfig optimises two layers and fastConfig runs one fixed layer`` () =
        Assert.Equal(2, QuantumTspSolver.defaultConfig.NumLayers)
        Assert.True(QuantumTspSolver.defaultConfig.EnableOptimization)
        Assert.Equal(1000, QuantumTspSolver.defaultConfig.FinalShots)
        Assert.Equal(1, QuantumTspSolver.fastConfig.NumLayers)
        Assert.False(QuantumTspSolver.fastConfig.EnableOptimization)

    [<Fact>]
    let ``solveWithDefaults should use 1000 shots on the local backend`` () : Task =
        task {
            let distances = create3CityProblem ()
            let! result = solveWithDefaults distances

            result
            |> assertTourOrNoValidTour distances 1000 (fun solution ->
                Assert.Equal("Local Simulator", solution.BackendName)
                Assert.Equal<int[]>([| 0; 1; 2 |], solution.Tour)
                Assert.Equal(4.5, solution.TourLength, 9)
                Assert.Equal(2, solution.LayerParameters.Length)
                Assert.Equal(Some solution.LayerParameters.[0], solution.OptimizedParameters)
                Assert.True(solution.OptimizationConverged.IsSome))
        }
