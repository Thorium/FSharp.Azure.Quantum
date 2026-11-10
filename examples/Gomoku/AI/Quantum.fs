namespace FSharp.Azure.Quantum.Examples.Gomoku.AI

open FSharp.Azure.Quantum.Examples.Gomoku
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.GroverSearch
open System
open System.Collections.Generic

/// Quantum Gomoku AI: every decision is a Grover search. It never calls the classical AI;
/// both read the same game knowledge (Evaluation.fs) and differ only in how they search it.
///
/// It takes the steps of Evaluation.decide with two quantum searches:
/// - Best cell: Dürr–Høyer maximum finding over the candidate cells (cell score from the
///   pattern table), repeatedly Grover-searching for a cell scoring more than the best seen.
/// - Forcing win: Grover search for a sequence of d moves that each make a four, so every
///   opponent reply is forced, ending in five or in two fours at once. Each sequence is one
///   basis state of a q·d-qubit register (q qubits pick one of K forcing candidates per
///   move), and the oracle marks the winning ones.
///
/// Classical.selectForcingMove takes the same steps with classical searches. The forcing
/// search is where the look-ahead comes from: d of its own moves deep, 2d plies, which the
/// one-move classical scan does not see.
///
/// Cost is counted in oracle queries (Grover iterations × shots, over every circuit run),
/// which is what a quantum device spends. Grover needs about √(K^d) of them where exhaustive
/// enumeration of the same sequences needs K^d evaluations: in measured games about 1,500
/// queries a move against 34,000. Two things it does not show: it is not faster on this
/// simulator, which evaluates the oracle on every basis state to build it; and a classical
/// depth-first search that drops a sequence at its first non-forcing move needs only about
/// 300 evaluations a move. Grover's speedup is over unstructured search; the quantum
/// counterpart of the pruned search is quantum backtracking, which this library lacks.
module QuantumSearch =

    /// The forcing-sequence register uses at most this many qubits. The local simulator runs
    /// 20, but building the oracle visits every basis state, and 2^16 is quick enough per move.
    [<Literal>]
    let MaxSequenceQubits = 16

    /// Forcing candidates per side: 5 qubits per move, so 3 moves deep at 32 candidates
    [<Literal>]
    let ForcingCandidateLimit = 32

    /// Positional candidates for step 1: 7 qubits
    [<Literal>]
    let PositionalCandidateLimit = 128

    /// Longest forcing sequence searched, in own moves
    [<Literal>]
    let MaxForcingDepth = 5

    /// Measurements per Grover circuit. One measured marked state is enough to act on.
    [<Literal>]
    let Shots = 2

    let qubitsFor (n: int) =
        if n <= 1 then 1 else int (ceil (Math.Log2(float n)))

    /// Forcing-sequence depth for `count` candidates within `qubitBudget` qubits
    let depthFor (qubitBudget: int) (count: int) : int =
        max 1 (min MaxForcingDepth (qubitBudget / qubitsFor count))

    /// What one move's search cost.
    type Metrics =
        {
            Candidates: int
            ForcingCandidates: int
            ForcingDepth: int
            /// Widest register used
            Qubits: int
            GroverRuns: int
            /// Grover iterations × shots over every circuit run: the oracle queries a quantum
            /// device would make
            OracleQueries: int
            /// Evaluations an exhaustive classical enumeration of the same spaces makes
            ClassicalEvaluations: int
            /// Which step chose the move
            Decision: string
            ElapsedMs: float
        }

    /// Grover search with the number of marked states unknown: try the iteration counts of
    /// the BBHT schedule (optimal for N/2, N/4, ... 1 marked states) until a run measures a
    /// marked state. Counts every run's iterations × shots as oracle queries.
    type private Searcher(backend: IQuantumBackend) =
        let mutable runs = 0
        let mutable queries = 0
        let mutable widest = 0

        member _.Runs = runs
        member _.Queries = queries
        member _.Widest = widest

        /// Marked states measured, or [] when the search finds none.
        member _.Find (numQubits: int) (marked: int -> bool) : int list =
            widest <- max widest numQubits

            // The simulator evaluates the oracle on every basis state of every circuit it
            // builds; remember the answers across the runs of this search.
            let known = Dictionary<int, bool>()

            let markedOnce index =
                match known.TryGetValue index with
                | true, answer -> answer
                | _ ->
                    let answer = marked index
                    known.[index] <- answer
                    answer

            let schedule =
                (1 <<< numQubits) / 2
                |> List.unfold (fun m ->
                    if m >= 1 then
                        Some(Grover.calculateOptimalIterations numQubits m, m / 2)
                    else
                        None)
                |> List.map (max 1)
                |> List.distinct

            let count k =
                runs <- runs + 1
                queries <- queries + k * Shots

            let rec tryRuns counts =
                match counts with
                | [] -> []
                | k :: rest ->
                    count k

                    let config =
                        { Grover.defaultConfig with
                            Iterations = Some k
                            Shots = Shots
                        }

                    match Grover.searchWhere markedOnce numQubits backend config with
                    | Ok result when not result.Solutions.IsEmpty -> result.Solutions
                    | Ok _ -> tryRuns rest
                    | Error(QuantumError.ValidationError("Oracle", _)) ->
                        // The simulator sees that nothing is marked before running anything.
                        // A device would run the rest of the schedule and measure nothing, so
                        // count those runs too.
                        List.iter count rest
                        []
                    | Error err -> failwith $"Grover search failed: {err.Message}"

            tryRuns schedule

    let private rng = Random()

    /// Dürr–Høyer: the index in 0 .. count-1 with the highest `value`, by Grover-searching for
    /// a higher one than the highest seen until there is none.
    let private findMaximum (searcher: Searcher) (count: int) (value: int -> int) : int * int =
        let rec climb best =
            let bestValue = value best

            match searcher.Find (qubitsFor count) (fun i -> i < count && value i > bestValue) with
            | [] -> (best, bestValue)
            | found -> climb (found |> List.maxBy value)

        climb (rng.Next count)

    /// Grover search for a forcing win of `player`: the moves of a winning sequence of at most
    /// `depth` moves from `candidates`, if one is found.
    let private findForcingWin
        (searcher: Searcher)
        (candidates: Position[])
        (depth: int)
        (board: Board)
        (player: Cell)
        : Position list option =
        let q = qubitsFor candidates.Length
        let mask = (1 <<< q) - 1

        // Sequence index: move i is candidate (index >>> q·i) &&& mask
        let wins index =
            let rec go board i =
                if i = depth then
                    false
                else
                    let pick = (index >>> (q * i)) &&& mask

                    pick < candidates.Length
                    && match Evaluation.forcingStep board player candidates.[pick] with
                       | Evaluation.Wins -> true
                       | Evaluation.Blocked next -> go next (i + 1)
                       | Evaluation.Fails -> false

            go board 0

        match searcher.Find (q * depth) wins with
        | [] -> None
        | foundHead :: _ ->
            // The moves up to the winning one; picks after it are arbitrary
            let index = foundHead

            [ for i in 0 .. depth - 1 -> (index >>> (q * i)) &&& mask ]
            |> List.takeWhile (fun pick -> pick < candidates.Length)
            |> List.map (fun pick -> candidates.[pick])
            |> Some

    /// Choose a move by Grover search on `backend`.
    let selectMove (board: Board) (backend: IQuantumBackend) : Position option * Metrics option =
        let stopwatch = Diagnostics.Stopwatch.StartNew()

        let runnable =
            UnifiedBackend.getRunnableQubits backend
            |> Option.defaultValue MaxSequenceQubits

        let budget = min runnable MaxSequenceQubits
        let positionalLimit = min PositionalCandidateLimit (1 <<< budget)

        match Evaluation.candidateMoves positionalLimit board with
        | [] -> (None, None)
        | [ only ] -> (Some only, None)
        | candidateList ->
            let candidates = Array.ofList candidateList
            let searcher = Searcher backend
            let mutable classical = candidates.Length
            let mutable forcingCount = 0
            let mutable forcingDepth = 0

            let bestCell () =
                let scores = candidates |> Array.map (Evaluation.scoreCell board)
                let best, score = findMaximum searcher candidates.Length (fun i -> scores.[i])
                (candidates.[best], score)

            let forcingWin board player =
                // Even one move of the sequence register must fit the backend
                let forcing =
                    Evaluation.forcingCandidates (min ForcingCandidateLimit (1 <<< budget)) board player
                    |> Array.ofList

                if forcing.Length = 0 then
                    None
                else
                    let depth = depthFor budget forcing.Length
                    forcingCount <- max forcingCount forcing.Length
                    forcingDepth <- max forcingDepth depth
                    classical <- classical + pown forcing.Length depth
                    findForcingWin searcher forcing depth board player

            let move, decision = Evaluation.decide bestCell forcingWin board

            let metrics =
                {
                    Candidates = candidates.Length
                    ForcingCandidates = forcingCount
                    ForcingDepth = forcingDepth
                    Qubits = searcher.Widest
                    GroverRuns = searcher.Runs
                    OracleQueries = searcher.Queries
                    ClassicalEvaluations = classical
                    Decision = decision
                    ElapsedMs = stopwatch.Elapsed.TotalMilliseconds
                }

            (Some move, Some metrics)

    /// One line on what a move's search cost
    let describe (metrics: Metrics) : string =
        $"Grover: {metrics.Decision}; {metrics.Candidates} cells, "
        + $"{metrics.ForcingCandidates} forcing candidates {metrics.ForcingDepth} deep, "
        + $"{metrics.Qubits} qubits, {metrics.GroverRuns} circuits, {metrics.OracleQueries} oracle queries "
        + $"vs {metrics.ClassicalEvaluations} by exhaustive enumeration, {metrics.ElapsedMs:F0} ms"

/// Hybrid AI: the classical AI for the opening, where the board is nearly empty and there
/// is nothing to force yet, and the quantum search after it.
module LocalHybrid =

    /// Moves played before the quantum search takes over
    [<Literal>]
    let OpeningMoves = 6

    type Strategy =
        | Classical of reason: string
        | Quantum of reason: string

    type MoveMetrics =
        {
            Strategy: Strategy
            Move: Position option
            Quantum: QuantumSearch.Metrics option
        }

    let selectBestMove (board: Board) (backend: IQuantumBackend) : MoveMetrics =
        if board.MoveHistory.Length < OpeningMoves then
            {
                Strategy = Classical "opening: nothing to force yet"
                Move = Classical.selectBestMove board
                Quantum = None
            }
        else
            let move, metrics = QuantumSearch.selectMove board backend

            {
                Strategy = Quantum "Grover search"
                Move = move
                Quantum = metrics
            }

    let explainStrategy (metrics: MoveMetrics) : string =
        match metrics.Strategy, metrics.Quantum with
        | Classical reason, _ -> $"Strategy: classical pattern scan ({reason})"
        | Quantum reason, Some quantum -> $"Strategy: quantum ({reason})\n{QuantumSearch.describe quantum}"
        | Quantum reason, None -> $"Strategy: quantum ({reason})"
