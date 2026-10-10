/// Drone Swarm Choreography Example (4 Drones)
///
/// Quantum-optimized formation planning for drone light shows using QAOA
/// to solve the Quadratic Assignment Problem (QAP).
///
/// Uses 4 drones (16 qubits) which fits within LocalBackend's 20-qubit limit,
/// so every assignment is a QAOA sample run on the backend.
///
/// QUANTUM OPTIMIZATION:
/// - 4 drones = 16 QUBO variables = 16 qubits (fits LocalBackend)
/// - QAOA solver via IQuantumBackend (RULE 1 compliant)
/// - No classical fallback: a transition with no valid, safe QAOA sample fails the show
///
/// CRAZYFLIE EXPORT:
/// - Use --export to generate Python scripts for real drone automation
/// - Supports Crazyflie 2.1 with Lighthouse/Loco positioning
namespace FSharp.Azure.Quantum.Examples.Drones.SwarmChoreography

open System
open System.Diagnostics
open System.IO

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Topological

open FSharp.Azure.Quantum.Examples.Common
open FSharp.Azure.Quantum.Examples.Drones
open FSharp.Azure.Quantum.Examples.Drones.Domain

// =============================================================================
// DOMAIN TYPES (same as full version)
// =============================================================================

/// 3D position in meters relative to ground origin
[<Struct>]
type Position3D =
    {
        X: float // meters, positive = right
        Y: float // meters, positive = forward
        Z: float // meters, positive = up (altitude)
    }

/// Assignment of drones to formation positions
[<Struct>]
type Assignment =
    {
        DroneId: int
        TargetPositionIndex: int
    }

/// A formation is a set of target positions for drones
type Formation =
    {
        Name: string
        Positions: Position3D[]
        /// Light colour of each position, shown by whichever drone is assigned to
        /// it: the formation is a picture. None keeps each drone its own colour.
        Colors: CrazyflieExport.LedColor[] option
        /// The drones fly through it without holding: a step of one continuous move
        Flow: bool
    }

/// Result of formation transition optimization
type TransitionResult =
    {
        FromFormation: string
        ToFormation: string
        Assignments: Assignment[]
        TotalDistance: float
        Method: string
    }

// =============================================================================
// FORMATION DEFINITIONS (4 drones only)
// =============================================================================

module Formations =

    let private rgb r g b : CrazyflieExport.LedColor = { R = r; G = g; B = b }

    /// Ground formation: 4 drones in a line at the origin
    let ground: Formation =
        {
            Name = "Ground (Line)"
            Positions =
                [|
                    { X = -6.0; Y = 0.0; Z = 0.0 } // Drone 0: left
                    { X = -2.0; Y = 0.0; Z = 0.0 } // Drone 1: center-left
                    { X = 2.0; Y = 0.0; Z = 0.0 } // Drone 2: center-right
                    { X = 6.0; Y = 0.0; Z = 0.0 } // Drone 3: right
                |]
            // White for take-off and landing
            Colors = Some(Array.create 4 (rgb 255 255 255))
            Flow = false
        }

    /// Diamond formation (4 points)
    ///       0        <- Top
    ///     1   2      <- Sides
    ///       3        <- Bottom
    let diamond: Formation =
        {
            Name = "Diamond"
            Positions =
                [|
                    { X = 0.0; Y = 0.0; Z = 25.0 } // Top
                    { X = -8.0; Y = 0.0; Z = 15.0 } // Left
                    { X = 8.0; Y = 0.0; Z = 15.0 } // Right
                    { X = 0.0; Y = 0.0; Z = 5.0 } // Bottom
                |]
            // A gem: white top, cyan sides, blue bottom
            Colors = Some [| rgb 255 255 255; rgb 0 255 255; rgb 0 255 255; rgb 0 0 255 |]
            Flow = false
        }

    /// Square formation (2x2 grid)
    /// 0   1    <- Top row
    /// 2   3    <- Bottom row
    let square: Formation =
        {
            Name = "Square"
            Positions =
                [|
                    { X = -5.0; Y = 0.0; Z = 20.0 } // Top-left
                    { X = 5.0; Y = 0.0; Z = 20.0 } // Top-right
                    { X = -5.0; Y = 0.0; Z = 10.0 } // Bottom-left
                    { X = 5.0; Y = 0.0; Z = 10.0 } // Bottom-right
                |]
            // Yellow top row, green bottom row
            Colors = Some [| rgb 255 255 0; rgb 255 255 0; rgb 0 255 0; rgb 0 255 0 |]
            Flow = false
        }

    /// Vertical line formation (ascending)
    ///   0   <- Top
    ///   1
    ///   2
    ///   3   <- Bottom
    let vertical: Formation =
        {
            Name = "Vertical Line"
            Positions =
                [|
                    { X = 0.0; Y = 0.0; Z = 30.0 } // Top
                    { X = 0.0; Y = 0.0; Z = 22.0 }
                    { X = 0.0; Y = 0.0; Z = 14.0 }
                    { X = 0.0; Y = 0.0; Z = 6.0 } // Bottom
                |]
            // A rainbow, red at the top
            Colors = Some [| rgb 255 0 0; rgb 255 128 0; rgb 0 255 0; rgb 0 0 255 |]
            Flow = false
        }

// =============================================================================
// DISTANCE CALCULATIONS
// =============================================================================

module Geometry =

    /// Calculate Euclidean distance between two 3D positions
    let distance (p1: Position3D) (p2: Position3D) : float =
        let dx = p2.X - p1.X
        let dy = p2.Y - p1.Y
        let dz = p2.Z - p1.Z
        sqrt (dx * dx + dy * dy + dz * dz)

    /// Smallest distance between two slots of the same formation, over all
    /// formations (ground slots included: the drones take off from them).
    let minSlotSpacing (formations: Formation[]) : float =
        formations
        |> Array.collect (fun f ->
            [|
                for i in 0 .. f.Positions.Length - 1 do
                    for j in i + 1 .. f.Positions.Length - 1 do
                        distance f.Positions.[i] f.Positions.[j]
            |])
        |> Array.min

    /// Scale at which `spacing` (in formation units) becomes `margin` times the
    /// minimum separation. The formations are drawn in abstract units; flying
    /// them at an indoor scale outdoors puts drones centimetres apart.
    let scaleFor (minSeparationM: float) (margin: float) (spacing: float) : float = minSeparationM * margin / spacing

    /// Build distance matrix from current positions to target formation
    let buildDistanceMatrix (currentPositions: Position3D[]) (targetFormation: Formation) : float[,] =
        let n = currentPositions.Length
        Array2D.init n n (fun i j -> distance currentPositions.[i] targetFormation.Positions.[j])

// =============================================================================
// QUBO FORMULATION FOR QUADRATIC ASSIGNMENT PROBLEM
// =============================================================================

module QapQubo =

    /// Build QUBO matrix for the Quadratic Assignment Problem
    /// Variables: x[i,j] = 1 if drone i is assigned to position j
    /// (BlockAssignment.assignmentQubo, which the block search uses on every block)
    let buildQubo (distanceMatrix: float[,]) (penaltyWeight: float) : float[,] =
        BlockAssignment.assignmentQubo penaltyWeight distanceMatrix

    /// Decode QUBO solution to assignment
    let decodeAssignment (solution: int[]) (n: int) : Assignment[] =
        [|
            for i in 0 .. n - 1 do
                for j in 0 .. n - 1 do
                    if solution.[i * n + j] = 1 then
                        yield { DroneId = i; TargetPositionIndex = j }
        |]

    /// Validate assignment (each drone and position used exactly once)
    let validateAssignment (assignments: Assignment[]) (n: int) : bool =
        let dronesUsed = assignments |> Array.map (fun a -> a.DroneId) |> Array.distinct

        let positionsUsed =
            assignments |> Array.map (fun a -> a.TargetPositionIndex) |> Array.distinct

        // Exactly n assignments: distinct counts alone let a drone take two
        // slots while two others share one.
        assignments.Length = n && dronesUsed.Length = n && positionsUsed.Length = n

    /// Calculate total distance for an assignment
    let calculateTotalDistance (distanceMatrix: float[,]) (assignments: Assignment[]) : float =
        assignments
        |> Array.sumBy (fun a -> distanceMatrix.[a.DroneId, a.TargetPositionIndex])

// =============================================================================
// TRANSITION SAFETY
// =============================================================================

/// Separation during a synchronised transition: every drone leaves together and
/// arrives together along a straight line, which is how the exports fly it.
/// Any assignment is a valid one-to-one mapping, but not every one is safe: a
/// drone sent from the bottom of a column to its top flies through the others.
module TransitionSafety =

    /// Closest two drones come, one moving in a straight line from `si` to `ei` and the
    /// other from `sj` to `ej` in the same time (exact: the relative motion is linear).
    let separationOf (si: Position3D) (ei: Position3D) (sj: Position3D) (ej: Position3D) : float =
        let a = [| si.X - sj.X; si.Y - sj.Y; si.Z - sj.Z |]
        let b = [| ei.X - ej.X; ei.Y - ej.Y; ei.Z - ej.Z |]
        let w = Array.map2 (-) b a
        let ww = Array.sumBy (fun x -> x * x) w

        let t =
            if ww = 0.0 then
                0.0
            else
                -(Array.map2 (*) a w |> Array.sum) / ww |> max 0.0 |> min 1.0

        Array.map2 (fun ai wi -> ai + t * wi) a w |> Array.sumBy (fun x -> x * x) |> sqrt

    /// Closest drones i and j come, both moving in straight lines from `starts` to `ends`
    /// in the same time.
    let private pairSeparation (starts: Position3D[]) (ends: Position3D[]) (i: int) (j: int) : float =
        separationOf starts.[i] ends.[i] starts.[j] ends.[j]

    /// Closest two drones come, all moving in straight lines from `starts` to
    /// `ends` in the same time.
    let minSeparation (starts: Position3D[]) (ends: Position3D[]) : float =
        [
            for i in 0 .. starts.Length - 1 do
                for j in i + 1 .. starts.Length - 1 do
                    pairSeparation starts ends i j
        ]
        |> List.fold min Double.PositiveInfinity

    /// The pairs of drones that come closer than `bound` in flight, closest first.
    let unsafePairs (starts: Position3D[]) (ends: Position3D[]) (bound: float) : (int * int)[] =
        [|
            for i in 0 .. starts.Length - 1 do
                for j in i + 1 .. starts.Length - 1 do
                    let separation = pairSeparation starts ends i j

                    if separation < bound then
                        yield separation, (i, j)
        |]
        |> Array.sortBy fst
        |> Array.map snd

    let private spacing (ps: Position3D[]) =
        [
            for i in 0 .. ps.Length - 1 do
                for j in i + 1 .. ps.Length - 1 do
                    Geometry.distance ps.[i] ps.[j]
        ]
        |> List.fold min Double.PositiveInfinity

    /// What a transition can be held to. With the assignment minimising the sum
    /// of SQUARED distances, synchronised straight lines keep every pair at
    /// least min(start spacing, end spacing) / sqrt 2 apart (Turpin, Michael &
    /// Kumar, "CAPT", 2014), so every transition has an assignment meeting this
    /// bound. Minimising plain distance gives no such bound: the QAOA samples are
    /// checked against it.
    let bound (starts: Position3D[]) (ends: Position3D[]) =
        min (spacing starts) (spacing ends) / sqrt 2.0

    /// The cost of one head-on pair in the block search's objective: `multiple` × the
    /// largest squared distance from any drone to any slot, the largest single term of the
    /// sum of squared distances, so at multiple 1 no saving of one drone's distance buys a
    /// conflict; 0 leaves the conflicts to the gate alone.
    let conflictWeight (multiple: float) (starts: Position3D[]) (targets: Position3D[]) : float =
        if multiple = 0.0 then
            0.0
        else
            multiple
            * ([|
                   for s in starts do
                       for t in targets do
                           Geometry.distance s t ** 2.0
               |]
               |> Array.max)

    /// The quadratic term of the assignment (BlockAssignment.neighbourhoods): what drones
    /// i and j flying to slots a and b in the same transition cost when they would come
    /// closer than `bound`: `weight` × the shortfall as a share of the bound, so a head-on
    /// pair costs the full weight and a pair just inside the bound almost nothing, which
    /// gives the block search a slope toward the gate; zero for a pair that keeps the bound.
    let conflictCost
        (starts: Position3D[])
        (targets: Position3D[])
        (bound: float)
        (weight: float)
        (i: int)
        (a: int)
        (j: int)
        (b: int)
        : float =
        if weight = 0.0 then
            0.0
        else
            let separation = separationOf starts.[i] targets.[a] starts.[j] targets.[b]

            if separation < bound then
                weight * (1.0 - separation / bound)
            else
                0.0

    let ends (target: Formation) (assignments: Assignment[]) (n: int) =
        Array.init n (fun d ->
            assignments
            |> Array.tryFind (fun a -> a.DroneId = d)
            |> Option.map (fun a -> target.Positions.[a.TargetPositionIndex])
            |> Option.defaultValue target.Positions.[d])

// =============================================================================
// INDOOR LAYOUT (Crazyflie)
// =============================================================================

/// The show laid out for a room: the formations are drawn in a vertical plane
/// in abstract units, but a room has a 2 m ceiling (crazyflie_show.py's
/// MAX_HEIGHT) and the indoor emergency action is to land every drone where it
/// is. Stacked slots cannot survive that: the Diamond's top lands on its
/// bottom, and the Vertical Line needs about 15 m of height to be separated at
/// all. So every airborne formation is rotated from the vertical plane into the
/// horizontal one (the picture is seen from above) and flown at one height,
/// and the ground formations are flown at take-off height. Each formation is
/// then scaled to the smallest size that keeps its closest slots, and every
/// synchronised transit, at 1.2 x the indoor limit.
module IndoorLayout =

    /// Indoor separation limit, horizontally (m). A Crazyflie 2.1 is about
    /// 0.13 m across its propellers; on-board position estimates and tracking
    /// in motion are good to roughly 0.1 m each (Lighthouse better, Loco
    /// worse), so two drones can drift 0.2 m towards each other: 0.13 + 0.2 is
    /// 0.33 m, rounded up to 0.5 m.
    [<Literal>]
    let limitM = 0.5

    /// Vertical distances count half: 1.0 m straight below another drone is as
    /// close as 0.5 m beside it. The downwash is the reason: hover thrust of a
    /// 27 g drone (0.27 N) through about 0.0064 m2 of rotor disc gives an
    /// induced velocity near 4 m/s, a jet that stays strong for several rotor
    /// diameters below and can upset another 27 g aircraft.
    let zWeight = 0.5

    /// The layout aims at 1.2 x the limit, like the outdoor scale.
    let margin = 1.2

    /// crazyflie_show.py's take-off height; the ground formations are flown here.
    [<Literal>]
    let hoverM = 0.5

    /// The height of every airborne formation: 0.2 m under the 2.0 m MAX_HEIGHT,
    /// and 1.3 m above take-off height (0.65 m weighted), so a drone left
    /// hovering at take-off height is clear of the show above it.
    [<Literal>]
    let showHeightM = 1.8

    /// The weighted space in which indoor distances are measured.
    let weigh (p: Position3D) : Position3D = { p with Z = p.Z * zWeight }

    let isGround (f: Formation) =
        f.Positions |> Array.forall (fun p -> p.Z <= 0.0)

    /// One formation at scale `s`: a ground formation at take-off height, an
    /// airborne one rotated flat (its height becomes the north offset from its
    /// middle) at the show height.
    let place (s: float) (f: Formation) : Formation =
        if isGround f then
            { f with
                Positions = f.Positions |> Array.map (fun p -> { X = p.X * s; Y = p.Y * s; Z = hoverM })
            }
        else
            let zs = f.Positions |> Array.map (fun p -> p.Z)
            let middle = (Array.min zs + Array.max zs) / 2.0

            { f with
                Positions =
                    f.Positions
                    |> Array.map (fun p ->
                        {
                            X = p.X * s
                            Y = (p.Y + p.Z - middle) * s
                            Z = showHeightM
                        })
            }

    let closestPair (ps: Position3D[]) =
        [
            for i in 0 .. ps.Length - 1 do
                for j in i + 1 .. ps.Length - 1 do
                    Geometry.distance (weigh ps.[i]) (weigh ps.[j])
        ]
        |> List.fold min Double.PositiveInfinity

    type Plan =
        {
            /// Laid out, in metres (X east, Y north, Z up), slot order.
            Formations: Formation[]
            /// The optimiser's transitions, each re-checked in this layout.
            Transitions: TransitionResult[]
            Scales: float[]
            /// Closest weighted distance of any synchronised transit.
            TransitMin: float
            /// Closest weighted distance of the slots of any formation.
            SlotMin: float
            Automatic: bool
        }

    /// Re-solves one transition in the weighted space: QAOA on the weighted
    /// distances from the starts to the targets, taking only an assignment that
    /// passes the safety gate there; the assignment and how it was found, or why
    /// none was.
    type Resolver = Position3D[] -> Position3D[] -> Result<Assignment[] * string, string>

    /// Lay the show out at the given per-formation scales and pass every
    /// transition through the safety gate again, in this layout and in the
    /// weighted space: an assignment that is safe in the drawn plane need not
    /// be safe once the formations are flat and scaled differently. A QAOA
    /// assignment that fails it is re-solved by `resolve` (QAOA again, on the
    /// weighted distances); the closing move onto the drones' own slots is fixed.
    /// Transitions that still fail are returned as failures; no classical
    /// assignment stands in.
    let private lay (resolve: Resolver) (formations: Formation[]) (transitions: TransitionResult[]) (scales: float[]) =
        let laid0 = Array.map2 place scales formations
        let n = laid0.[0].Positions.Length
        let last = formations.Length - 1

        // The show closes on the ground: every drone flies to above its OWN
        // start slot at show height (the overhead formation), then all descend
        // together. A drone that never got past take-off (lost radio) still
        // hovers on its slot, and one that dropped out has landed there; a
        // diagonal descent into the ground row would pass low beside them.
        let closes = last > 0 && isGround formations.[last]

        let laid, transitions, closing =
            if closes then
                let overhead =
                    { laid0.[last] with
                        Name = formations.[last].Name + " (overhead)"
                        Positions = laid0.[last].Positions |> Array.map (fun p -> { p with Z = showHeightM })
                    }

                let t = transitions.[transitions.Length - 1]

                (Array.append (Array.take last laid0) [| overhead; laid0.[last] |],
                 Array.append
                     (Array.take (transitions.Length - 1) transitions)
                     [|
                         { t with ToFormation = overhead.Name }
                         { t with FromFormation = overhead.Name }
                     |],
                 Set.ofList [ transitions.Length - 1; transitions.Length ])
            else
                (laid0, transitions, Set.empty)

        let _, gated, transit, failures =
            ((laid.[0].Positions, [], Double.PositiveInfinity, []), Array.indexed transitions)
            ||> Array.fold (fun (current, acc, transit, failures) (k, t) ->
                let target = laid.[k + 1]
                let weighted = Array.map weigh current
                let targetWeighted = target.Positions |> Array.map weigh

                let separation assignments =
                    TransitionSafety.minSeparation
                        weighted
                        (TransitionSafety.ends target assignments n |> Array.map weigh)

                let passes assignments =
                    separation assignments
                    >= TransitionSafety.bound weighted targetWeighted * (1.0 - 1e-6)

                // Closing: every drone to its own start slot (above it, then on it).
                let checkedT =
                    if closing.Contains k then
                        let own = Array.init n (fun d -> { DroneId = d; TargetPositionIndex = d })

                        if passes own then
                            Ok(own, t.Method + ", closing on own slots")
                        else
                            Error(
                                sprintf
                                    "%s → %s: closing on own slots comes %.2f m close (weighted), under the gate's %.2f m"
                                    t.FromFormation
                                    t.ToFormation
                                    (separation own)
                                    (TransitionSafety.bound weighted targetWeighted)
                            )
                    elif passes t.Assignments then
                        Ok(t.Assignments, t.Method)
                    else
                        resolve weighted targetWeighted
                        |> Result.map (fun (a, how) ->
                            a, t.Method + $", re-solved by QAOA on downwash-weighted distances (%s{how})")
                        |> Result.mapError (sprintf "%s → %s: %s" t.FromFormation t.ToFormation)

                // A failed transition keeps its assignment only so the layout can go on
                // to the next one; its transit is not counted and the plan is refused.
                let assignments, how, failures =
                    match checkedT with
                    | Ok(a, how) -> a, how, failures
                    | Error msg -> t.Assignments, t.Method, msg :: failures

                let ends = TransitionSafety.ends target assignments n

                let gatedT =
                    { t with
                        Assignments = assignments
                        Method = how
                        TotalDistance = Array.map2 Geometry.distance current ends |> Array.sum
                    }

                let transit =
                    if Result.isOk checkedT then
                        min transit (separation assignments)
                    else
                        transit

                (ends, gatedT :: acc, transit, failures))

        (laid, gated |> List.rev |> Array.ofList, transit, List.rev failures)

    /// Most rounds the automatic indoor scale grows.
    [<Literal>]
    let MaxGrowthRounds = 10

    /// The indoor plan: with `--scale`, every formation at that scale; without,
    /// each at the smallest scale that keeps its slots 1.2 x the limit apart,
    /// all grown together until every synchronised transit is too, and every
    /// transition has a QAOA assignment passing the gate (a failure grows the
    /// scale by 1.25 and re-solves). (A gated transit keeps at least slot
    /// spacing / sqrt 2, so growing by sqrt 2 suffices for the spacing.) An
    /// Error when a transition still fails at `--scale` or after
    /// MaxGrowthRounds rounds.
    let plan
        (resolve: Resolver)
        (formations: Formation[])
        (transitions: TransitionResult[])
        (explicitScale: float option)
        : Result<Plan, string> =
        let need = limitM * margin

        let build automatic scales =
            let laid, gated, transit, failures = lay resolve formations transitions scales

            {
                Formations = laid
                Transitions = gated
                Scales = scales
                TransitMin = transit
                SlotMin = laid |> Array.map (fun f -> closestPair f.Positions) |> Array.min
                Automatic = automatic
            },
            failures

        let refuse (failures: string list) (where: string) =
            Error(sprintf "%s: %s" where (String.concat "; " failures))

        match explicitScale with
        | Some s ->
            match build false (Array.create formations.Length s) with
            | p, [] -> Ok p
            | _, failures -> refuse failures (sprintf "at --scale %.3f" s)
        | None ->
            let baseScales =
                formations |> Array.map (fun f -> need / closestPair (place 1.0 f).Positions)

            let rec grow factor round =
                let p, failures = build true (baseScales |> Array.map ((*) factor))

                match failures with
                | [] when p.TransitMin >= need - 1e-9 || round >= MaxGrowthRounds -> Ok p
                | [] -> grow (max (factor * 1.01) (factor * need / p.TransitMin)) (round + 1)
                | _ when round >= MaxGrowthRounds ->
                    refuse failures (sprintf "after %d rounds of scale growth (x%.2f)" round factor)
                | _ -> grow (factor * 1.25) (round + 1)

            grow 1.0 0

// =============================================================================
// SOLVERS
// =============================================================================

module Solver =

    /// Most sweeps of a block search over a swarm wider than one circuit. The search ends
    /// itself after a sweep that improves nothing; this bounds one that keeps improving.
    /// Measured on the local simulator with blocks of 4 and 100 drones through a ground
    /// grid, a grid aloft, a ring and a sphere, three runs each: at a cap of 12 the gate
    /// passed 10 of 12 transitions, the failures at the cap with one pair still inside the
    /// bound; at 24 it passed 11 of 12, the ring to the sphere in 12-17 sweeps, and the one
    /// failure stopped itself on a sweep that improved nothing. 50 drones converged in 4-6
    /// sweeps.
    [<Literal>]
    let BlockSweeps = 24

    /// Assign the drones in blocks (BlockAssignment.solve): from the pairing of `starts`
    /// and `targets` in Morton order, blocks of `blockDrones` drones, each a QAOA circuit
    /// of blockDrones² qubits, through the same gate. The blocks lower the sum of SQUARED
    /// distances, the objective under which an assignment meeting the gate's bound exists
    /// (CAPT, see TransitionSafety.bound), plus the path-conflict cost of every pair of
    /// drones that would come closer than the gate's bound (TransitionSafety.conflictCost,
    /// weighted by `conflict` × the largest squared distance; 0 leaves conflicts to the gate
    /// alone), and the pairs the gate names are put in a block first; the plain distance is
    /// what the show reports.
    let solveInBlocks
        (backend: IQuantumBackend)
        (shots: int)
        (blockDrones: int)
        (conflict: float)
        (accept: Assignment[] -> bool)
        (starts: Position3D[])
        (targets: Position3D[])
        (distanceMatrix: float[,])
        : Result<Assignment[] * string, string> =
        let toPoint (p: Position3D) : BlockAssignment.Point = { X = p.X; Y = p.Y; Z = p.Z }

        let toAssignments (assignment: int[]) =
            assignment |> Array.mapi (fun d j -> { DroneId = d; TargetPositionIndex = j })

        let bound = TransitionSafety.bound starts targets

        let unsafePairs (assignment: int[]) =
            TransitionSafety.unsafePairs starts (assignment |> Array.map (fun j -> targets.[j])) bound

        BlockAssignment.solve
            backend
            shots
            blockDrones
            BlockSweeps
            (toAssignments >> accept)
            unsafePairs
            (fun drone slot -> distanceMatrix.[drone, slot] ** 2.0)
            (TransitionSafety.conflictCost starts targets bound (TransitionSafety.conflictWeight conflict starts targets))
            ValueNone
            (Array.map toPoint starts)
            (Array.map toPoint targets)
        |> Result.map (fun (assignment, how) -> toAssignments assignment, how)

    /// Quantum QAOA solver using IQuantumBackend
    /// RULE 1 COMPLIANT: Requires IQuantumBackend parameter.
    ///
    /// Returns the lowest-distance sampled assignment that is valid and that `accept` takes
    /// (the transition safety gate), and the attempt that found it (QaoaAttempts: more
    /// shots, then other angles). A swarm whose n² qubits are more than the backend runs is
    /// assigned in blocks of ⌊√limit⌋ drones instead (solveInBlocks), as is any swarm when
    /// `blockDrones` asks for blocks of that many. No accepted sample is an Error; no
    /// classical solver stands in.
    let solve
        (backend: IQuantumBackend)
        (shots: int)
        (blockDrones: int option)
        (conflict: float)
        (accept: Assignment[] -> bool)
        (starts: Position3D[])
        (targets: Position3D[])
        (distanceMatrix: float[,])
        : Result<Assignment[] * string, string> =

        let n = Array2D.length1 distanceMatrix
        let numVars = n * n

        match blockDrones, UnifiedBackend.getRunnableQubits backend with
        | Some k, _ -> solveInBlocks backend shots k conflict accept starts targets distanceMatrix
        | None, Some limit when numVars > limit ->
            match BlockAssignment.widestBlock backend with
            | Some k when k >= 2 -> solveInBlocks backend shots k conflict accept starts targets distanceMatrix
            | _ ->
                Error
                    $"%d{n} drones need %d{numVars} qubits (n²); backend '%s{backend.Name}' runs at most %d{limit}, under the 4 a block of 2 drones needs"
        | _ ->

            // Calculate penalty weight (Lucas rule)
            let maxDistance =
                [|
                    for i in 0 .. n - 1 do
                        for j in 0 .. n - 1 do
                            yield distanceMatrix.[i, j]
                |]
                |> Array.max

            let penaltyWeight = maxDistance * float n * 2.0

            // Build QUBO matrix
            let qubo = QapQubo.buildQubo distanceMatrix penaltyWeight

            // Convert to Problem Hamiltonian, normalised so the fixed angles below are on the
            // same scale the library's solvers use
            let problemHam =
                QaoaCircuit.ProblemHamiltonian.fromQubo qubo
                |> QaoaCircuit.ProblemHamiltonian.normalize

            let mixerHam = QaoaCircuit.MixerHamiltonian.create numVars

            // QAOA parameters (p=1 layer)
            let gamma = 0.5
            let beta = 0.3
            let parameters = [| (gamma, beta) |]

            let attempt (attemptShots: int) (angles: (float * float)[]) =
                // Build QAOA circuit
                let qaoaCircuit = QaoaCircuit.QaoaCircuit.build problemHam mixerHam angles

                let circuit =
                    CircuitAbstraction.QaoaCircuitWrapper(qaoaCircuit) :> CircuitAbstraction.ICircuit

                // Execute on provided backend
                match backend.ExecuteToState circuit with
                | Error err -> Error err.Message
                | Ok state ->
                    // Lowest-distance valid, accepted sample
                    QuantumState.measure state attemptShots
                    |> Array.map (fun bits -> QapQubo.decodeAssignment bits n)
                    |> Array.filter (fun assignments -> QapQubo.validateAssignment assignments n && accept assignments)
                    |> Array.sortBy (QapQubo.calculateTotalDistance distanceMatrix)
                    |> Array.tryHead
                    |> Ok

            QaoaAttempts.firstSolved shots parameters attempt

// =============================================================================
// SCALE RUN (more drones than one circuit holds)
// =============================================================================

/// A show of n drones through generated formations, every transition assigned in blocks
/// (Solver.solveInBlocks) through the same safety gate as the 4-drone show. It measures
/// the block search at scale: the distance of the spatial start pairing against the
/// searched one, the circuit runs and the time, per transition. Nothing is exported and no
/// evidence pack is built; a transition with no accepted assignment is reported as failed
/// and the drones hold where they are for the next one. No classical solver stands in.
module ScaleShow =

    /// Closest two slots come in a flown formation: 3 m, the upper end of the 2-3 m
    /// commercial drone shows fly at (operators' published venue guides, 2025-2026).
    [<Literal>]
    let SpacingM = 3.0

    /// Spacing of the launch grid: 2.5 m, above the 1.5-2.2 m the same guides give for a
    /// launch site, because the climb off the grid is the show's closest moment: its gate
    /// bound is the spacing / √2, and flown in ArduPilot SITL in GUIDED mode two drones'
    /// tracking errors (95th percentile 0.33 m each) come off it; at 2 m the 100-drone show
    /// came to 0.80 m against 1.43 m planned.
    [<Literal>]
    let LaunchSpacingM = 2.5

    /// Formations of n slots at least SpacingM apart: a ground grid, then a grid aloft, a
    /// ring and a sphere (Fibonacci) in turn until `count` formations, closing on the
    /// ground grid again.
    let formations (n: int) (count: int) : Formation[] =
        let side = int (ceil (sqrt (float n)))

        let grid (name: string) (spacing: float) (z: float) (turned: bool) : Formation =
            let half = float (side - 1) * spacing / 2.0

            {
                Name = name
                Positions =
                    Array.init n (fun i ->
                        let row = float (i / side) * spacing - half
                        let column = float (i % side) * spacing - half

                        if turned then
                            { X = row; Y = column; Z = z }
                        else
                            { X = column; Y = row; Z = z })
                Colors = None
                Flow = false
            }

        let ring: Formation =
            let radius = max SpacingM (float n * SpacingM / (2.0 * Math.PI))

            {
                Name = "Ring"
                Positions =
                    Array.init n (fun i ->
                        let angle = 2.0 * Math.PI * float i / float n

                        {
                            X = radius * cos angle
                            Y = radius * sin angle
                            Z = 20.0
                        })
                Colors = None
                Flow = false
            }

        let sphere: Formation =
            // area per slot ≈ spacing²: 4πR²/n = spacing²
            let radius = max SpacingM (SpacingM * sqrt (float n / (4.0 * Math.PI)))
            let golden = Math.PI * (3.0 - sqrt 5.0)

            {
                Name = "Sphere"
                Positions =
                    Array.init n (fun i ->
                        let y = 1.0 - 2.0 * (float i + 0.5) / float n
                        let r = sqrt (1.0 - y * y)
                        let angle = golden * float i

                        {
                            X = radius * r * cos angle
                            Y = radius * r * sin angle
                            Z = radius + 5.0 + radius * y
                        })
                Colors = None
                Flow = false
            }

        let middle = [| grid "Grid aloft" SpacingM 25.0 true; ring; sphere |]
        let ground = grid "Ground grid" LaunchSpacingM 0.0 false

        [|
            yield ground

            for i in 0 .. max 0 (count - 2) - 1 do
                yield middle.[i % middle.Length]

            if count >= 2 then
                yield ground
        |]

    /// Steps the globe's lands turn through a full turn, each a formation of its own with
    /// the same slots: a change of lights only, which the picture plays as a short step.
    [<Literal>]
    let GlobeSteps = 24

    /// Steps the cube turns through a full turn about a body diagonal, flown through: 10°
    /// each, so a step moves the farthest drone about 5 m, near the slot spacing, and
    /// following its own slot is each drone's shortest way.
    [<Literal>]
    let CubeSteps = 36

    /// Steps the mug turns through a full turn about the vertical, flown through: 15°
    /// each, about 5 m for the handle's farthest drone, for the same reason.
    [<Literal>]
    let MugSteps = 24

    /// Height of the lowest slot of every lit shape, so the show is clearly flown above
    /// the ground grid rather than brushing it.
    [<Literal>]
    let LowestM = 10.0

    /// Lit formations of n slots, the pictures of a light show: a globe (sea, land, ice)
    /// shown GlobeSteps times with its lands turning a step further each time while the
    /// drones hold, a diamond drawn along its edges, a star (a gold outline around a dark
    /// inner star), a mug turning a full turn about the vertical in MugSteps steps, a
    /// doughnut, and a cube drawn along its edges turning a full turn about a body diagonal
    /// in CubeSteps steps, the turns flown through without a hold, between the ground grid
    /// in white. (The mug and the doughnut share a parametrisation, but the straight
    /// deformation between them brought slots to 0.29 m, so they are two formations and the
    /// move between them a planned transition.) Every formation is scaled so its closest
    /// slots are SpacingM apart (the globe 2.5 × that, so it fills the picture; the mug and
    /// the doughnut 1.25 ×, 10 m higher) and flown at least LowestM up. `count` formations are
    /// flown, in this order.
    let litFormations (n: int) (count: int) : Formation[] =
        let rgb r g b : CrazyflieExport.LedColor = { R = r; G = g; B = b }
        let white = rgb 255 255 255
        let black = rgb 0 0 0

        // Points of a unit sphere spread evenly (Fibonacci)
        let sphere =
            let golden = Math.PI * (3.0 - sqrt 5.0)

            Array.init n (fun i ->
                let z = 1.0 - 2.0 * (float i + 0.5) / float n
                let r = sqrt (1.0 - z * z)
                let angle = golden * float i
                r * cos angle, r * sin angle, z)

        // A shape scaled so its closest two points are `closest` × SpacingM apart, lifted to LowestM
        let spaced
            (name: string)
            (closest: float)
            (points: (float * float * float)[])
            (light: float * float * float -> CrazyflieExport.LedColor)
            =
            let nearest =
                [
                    for i in 0 .. n - 1 do
                        for j in i + 1 .. n - 1 do
                            let (ax, ay, az), (bx, by, bz) = points.[i], points.[j]
                            sqrt ((ax - bx) ** 2.0 + (ay - by) ** 2.0 + (az - bz) ** 2.0)
                ]
                |> List.fold min Double.PositiveInfinity

            let scale =
                if nearest > 0.0 && nearest < Double.PositiveInfinity then
                    closest * SpacingM / nearest
                else
                    1.0

            let lowest = points |> Array.map (fun (_, _, z) -> z * scale) |> Array.min

            {
                Name = name
                Positions =
                    points
                    |> Array.map (fun (x, y, z) ->
                        {
                            X = x * scale
                            Y = y * scale
                            Z = z * scale - lowest + LowestM
                        })
                Colors = Some(points |> Array.map light)
                Flow = false
            }

        // Land where a few smooth waves add up; ice near the poles
        let land (x: float, y: float, z: float) =
            sin (3.1 * x + 1.0) * cos (2.3 * z) + sin (4.0 * y - 0.5) * cos (1.7 * x) > 0.45

        let ice (_, _, z: float) = abs z > 0.82

        // The globe's lights with its lands turned `turn` radians about the vertical axis
        let day (turn: float) (x: float, y: float, z: float) =
            let p = x * cos turn - y * sin turn, x * sin turn + y * cos turn, z

            if ice p then white
            elif land p then rgb 40 190 70
            else rgb 20 90 230

        // n points along the edges of a wire shape: every vertex, and the rest spread over
        // the edges as evenly as they divide
        let wire (vertices: (float * float * float)[]) (edges: (int * int)[]) =
            let inner = n - vertices.Length
            let perEdge = inner / edges.Length
            let extra = inner % edges.Length

            Array.append
                vertices
                (edges
                 |> Array.mapi (fun e (a, b) ->
                     let count = perEdge + (if e < extra then 1 else 0)
                     let (ax, ay, az), (bx, by, bz) = vertices.[a], vertices.[b]

                     Array.init count (fun k ->
                         let t = float (k + 1) / float (count + 1)
                         ax + t * (bx - ax), ay + t * (by - ay), az + t * (bz - az)))
                 |> Array.concat)

        // An octahedron on its point: six corners, twelve edges
        let octahedron =
            wire
                [| 1.0, 0.0, 0.0; -1.0, 0.0, 0.0; 0.0, 1.0, 0.0; 0.0, -1.0, 0.0; 0.0, 0.0, 1.0; 0.0, 0.0, -1.0 |]
                [| 0, 2; 2, 1; 1, 3; 3, 0; 4, 0; 4, 1; 4, 2; 4, 3; 5, 0; 5, 1; 5, 2; 5, 3 |]

        // Upper edges white, lower edges blue, the top and bottom corners cyan
        let diamondLight (_, _, z) =
            if abs z > 0.999 then rgb 0 255 255
            elif z >= 0.0 then white
            else rgb 0 110 255

        // A cube: eight corners, twelve edges
        let cube =
            wire
                [|
                    for x in [ -1.0; 1.0 ] do
                        for y in [ -1.0; 1.0 ] do
                            for z in [ -1.0; 1.0 ] do
                                x, y, z
                |]
                [| 0, 1; 2, 3; 4, 5; 6, 7; 0, 2; 1, 3; 4, 6; 5, 7; 0, 4; 1, 5; 2, 6; 3, 7 |]

        let cubeLight (_: float * float * float) = rgb 255 60 220

        // A five-point star in the east-up plane: the outline lit, an inner star dark
        let star =
            let vertices (radiusOuter: float) (radiusInner: float) =
                Array.init 10 (fun i ->
                    let angle = Math.PI / 2.0 + float i * Math.PI / 5.0
                    let r = if i % 2 = 0 then radiusOuter else radiusInner
                    r * cos angle, r * sin angle)

            let along (ring: (float * float)[]) (count: int) =
                let edges = Array.init 10 (fun i -> ring.[i], ring.[(i + 1) % 10])

                let lengths =
                    edges |> Array.map (fun ((ax, ay), (bx, by)) -> sqrt ((ax - bx) ** 2.0 + (ay - by) ** 2.0))

                let total = Array.sum lengths

                Array.init count (fun k ->
                    let mutable s = total * float k / float count
                    let mutable e = 0

                    while e < 9 && s > lengths.[e] do
                        s <- s - lengths.[e]
                        e <- e + 1

                    let (ax, ay), (bx, by) = edges.[e]
                    let t = s / lengths.[e]
                    ax + t * (bx - ax), 0.0, ay + t * (by - ay))

            let outline = max 10 (n * 3 / 5)
            let inner = n - outline

            Array.append
                (along (vertices 1.0 0.42) outline |> Array.map (fun (x, y, z) -> x, y, z, true))
                (along (vertices 0.55 0.23) inner |> Array.map (fun (x, y, z) -> x, y, z, false))

        let starPoints = star |> Array.map (fun (x, y, z, _) -> x, y, z)
        let starLit = star |> Array.map (fun (_, _, _, lit) -> lit)

        let starLight (p: float * float * float) =
            if starLit.[Array.findIndex ((=) p) starPoints] then rgb 255 190 0 else black

        // A mug and a doughnut on one parametrisation, slot i = column i / rows, row
        // i % rows: on the doughnut the column goes round the ring and the row round the
        // tube; on the mug the first columns go round the body (the row its height) and
        // the last along the handle (the row round its tube), so the same slot is the same
        // place on the surface in both and the morph between them is a deformation, not a
        // jump. Rows are 5 at 20 slots or more.
        let rows = max 1 (min 5 (n / 4))
        let cols = (n + rows - 1) / rows
        let handleCols = if cols >= 2 then max 1 (min (cols - 1) (cols * 3 / 10)) else 0
        let bodyCols = cols - handleCols

        let torusPoints =
            Array.init n (fun i ->
                let u = 2.0 * Math.PI * float (i / rows) / float cols
                let v = 2.0 * Math.PI * float (i % rows) / float rows
                (1.0 + 0.35 * cos v) * cos u, (1.0 + 0.35 * cos v) * sin u, 0.35 * sin v)

        let mugPoints =
            let height = 1.6
            let arm = 0.72
            let tube = 0.35

            Array.init n (fun i ->
                let c, r = i / rows, i % rows

                if c < bodyCols then
                    let angle = 2.0 * Math.PI * (float c + 0.5) / float bodyCols
                    let z = if rows > 1 then height * (float r / float (rows - 1) - 0.5) else 0.0
                    cos angle, sin angle, z
                else
                    // The handle: a half ring from the top of the body wall to its bottom,
                    // bulging east, with a tube round it
                    let s = float (c - bodyCols + 1) / float (handleCols + 1)
                    let phi = Math.PI / 2.0 - Math.PI * s
                    let psi = 2.0 * Math.PI * float r / float rows
                    let nx, nz = cos phi, sin phi
                    1.0 + arm * nx + tube * cos psi * nx, tube * sin psi, arm * nz + tube * cos psi * nz)

        // Cream china, coffee along the rim
        let mugLight (p: float * float * float) =
            let i = Array.findIndex ((=) p) mugPoints
            if i / rows < bodyCols && i % rows = rows - 1 then rgb 90 50 20 else rgb 255 235 190

        // Pink icing on top with sprinkles, dough below
        let doughnutLight (p: float * float * float) =
            let i = Array.findIndex ((=) p) torusPoints
            let (_, _, z) = p

            if z > 0.1 then
                match i % 7 with
                | 0 -> rgb 255 255 0
                | 3 -> rgb 0 230 255
                | _ -> rgb 255 105 180
            else
                rgb 170 100 40

        // Lifted `metres` above where `spaced` puts it
        let raised (metres: float) (f: Formation) =
            { f with
                Positions = f.Positions |> Array.map (fun p -> { p with Z = p.Z + metres })
            }

        let mug = spaced "Mug" 1.25 mugPoints mugLight |> raised 10.0
        let doughnut = spaced "Doughnut" 1.25 torusPoints doughnutLight |> raised 10.0

        // A formation turning a full turn about `axis` through its centre in `steps` steps
        // flown through (Rodrigues' rotation), all lifted alike so the lowest slot of any
        // step clears LowestM: the shape, then the steps, then the shape again. A turn is
        // rigid, so every step keeps the slots' spacing.
        let turning (steps: int) (axis: float * float * float) (shape: Formation) : Formation[] =
            let centre (axisOf: Position3D -> float) = shape.Positions |> Array.averageBy axisOf
            let cx, cy, cz = centre (fun p -> p.X), centre (fun p -> p.Y), centre (fun p -> p.Z)

            let ax, ay, az =
                let x, y, z = axis
                let l = sqrt (x * x + y * y + z * z)
                x / l, y / l, z / l

            let turned (angle: float) (p: Position3D) =
                let vx, vy, vz = p.X - cx, p.Y - cy, p.Z - cz
                let c, s = cos angle, sin angle
                let along = (ax * vx + ay * vy + az * vz) * (1.0 - c)
                let kx, ky, kz = ay * vz - az * vy, az * vx - ax * vz, ax * vy - ay * vx

                {
                    X = cx + vx * c + kx * s + ax * along
                    Y = cy + vy * c + ky * s + ay * along
                    Z = cz + vz * c + kz * s + az * along
                }

            let steps' =
                Array.init (steps + 1) (fun k ->
                    shape.Positions |> Array.map (turned (2.0 * Math.PI * float k / float steps)))

            let lift =
                max 0.0 (LowestM - (steps' |> Array.collect id |> Array.map (fun p -> p.Z) |> Array.min))

            Array.init (steps + 1) (fun k ->
                { shape with
                    Name =
                        if k = 0 then shape.Name
                        elif k = steps then shape.Name + ", turned round"
                        else $"{shape.Name}, turned %d{k}/%d{steps}"
                    Positions = steps'.[k] |> Array.map (fun p -> { p with Z = p.Z + lift })
                    Flow = k > 0 && k < steps
                })

        let ground =
            { (formations n 1).[0] with
                Colors = Some(Array.create n white)
                Flow = false
            }

        let step = 2.0 * Math.PI / float GlobeSteps

        let show =
            [|
                ground
                spaced "Globe" 2.5 sphere (day 0.0)

                for k in 1 .. GlobeSteps - 1 do
                    spaced $"Globe, turned %d{k}/%d{GlobeSteps}" 2.5 sphere (day (float k * step))

                spaced "Diamond" 1.0 octahedron diamondLight
                spaced "Star" 1.0 starPoints starLight
                yield! turning MugSteps (0.0, 0.0, 1.0) mug
                doughnut
                yield! turning CubeSteps (1.0, 1.0, 1.0) (spaced "Cube" 1.0 cube cubeLight)
                ground
            |]

        if count >= show.Length then show
        else Array.append (Array.truncate (max 1 (count - 1)) show) [| ground |]

    /// One transition of the scale run.
    type Transition =
        {
            From: string
            To: string
            /// Total distance of the Morton-order start pairing
            StartDistance: float
            /// Total distance of the assignment found; the start distance when none was
            Distance: float
            /// Pairs of drones the start pairing flies closer than the gate's bound
            StartUnsafePairs: int
            /// Fewest such pairs in any assignment the gate checked; 0 when one passed
            FewestUnsafePairs: int
            /// How it was found, or why it was not
            Method: string
            Solved: bool
            ElapsedMs: int64
        }

    /// A formation as flown: each drone's position and the light of the slot it holds
    /// (white where the formation has no lights), for LightShowPicture.
    let private frame (formation: Formation) (assignment: int[]) : LightShowPicture.Frame =
        {
            Name = formation.Name
            Positions =
                assignment
                |> Array.map (fun slot ->
                    let p = formation.Positions.[slot]
                    p.X, p.Y, p.Z)
            Lights =
                assignment
                |> Array.map (fun slot ->
                    match formation.Colors with
                    | Some colours -> colours.[slot].R, colours.[slot].G, colours.[slot].B
                    | None -> 255, 255, 255)
            Flow = formation.Flow
        }

    /// Fly `formations` with `n` drones: every transition through the gate, in blocks of
    /// `blockDrones` drones with `sweeps` sweeps at most (Solver.BlockSweeps when 0), the
    /// path conflicts weighted by `conflict` (Solver.solveInBlocks). The transitions, and
    /// every formation as flown (a failed transition holds the drones in the formation
    /// before it).
    let run
        (backend: IQuantumBackend)
        (shots: int)
        (blockDrones: int)
        (conflict: float)
        (sweeps: int)
        (formations: Formation[])
        : Transition[] * LightShowPicture.Frame[] =
        let toPoint (p: Position3D) : BlockAssignment.Point = { X = p.X; Y = p.Y; Z = p.Z }
        let n = formations.[0].Positions.Length
        let first = frame formations.[0] (Array.init n id)

        // The fold carries the drones' positions and the slots they hold, which is the start
        // pairing of a step of a deformation or a turn (BlockAssignment.startAssignment)
        let _, _, transitions, frames =
            ((formations.[0].Positions, Array.init n id, [], [ first ]), Array.pairwise formations)
            ||> Array.fold (fun (current, held, flown, frames) (fromFormation, toFormation) ->
                let sw = Stopwatch.StartNew()
                let targets = toFormation.Positions
                let distance (drone: int) (slot: int) = Geometry.distance current.[drone] targets.[slot]
                let bound = TransitionSafety.bound current targets
                let ends (assignment: int[]) = assignment |> Array.map (fun j -> targets.[j])

                let unsafePairs (assignment: int[]) =
                    TransitionSafety.unsafePairs current (ends assignment) bound

                // The gate, counting how close the search came for the report of a failure
                let mutable fewest = Int32.MaxValue

                let safe (assignment: int[]) =
                    let inside = (unsafePairs assignment).Length
                    fewest <- min fewest inside
                    inside = 0

                let total (assignment: int[]) = assignment |> Array.mapi distance |> Array.sum

                let start =
                    BlockAssignment.startAssignment (ValueSome held) (Array.map toPoint current) (Array.map toPoint targets)

                let startUnsafe = (unsafePairs start).Length
                let weight = TransitionSafety.conflictWeight conflict current targets

                printfn "═══════════════════════════════════════════════════"
                printfn "TRANSITION %d: %s → %s" (List.length flown + 1) fromFormation.Name toFormation.Name

                printfn
                    "  start pairing: %.2f m in all, %d pairs inside the gate's %.2f m; a head-on pair costs %.0f"
                    (total start)
                    startUnsafe
                    bound
                    weight

                // Squared distances plus path conflicts, the objective of Solver.solveInBlocks
                let found =
                    BlockAssignment.solve
                        backend
                        shots
                        blockDrones
                        (if sweeps > 0 then sweeps else Solver.BlockSweeps)
                        safe
                        unsafePairs
                        (fun drone slot -> distance drone slot ** 2.0)
                        (TransitionSafety.conflictCost current targets bound weight)
                        (ValueSome held)
                        (Array.map toPoint current)
                        (Array.map toPoint targets)

                sw.Stop()

                let next, nextHeld, flownFrame, transition =
                    match found with
                    | Ok(assignment, how) ->
                        printfn "  QAOA %s (%d ms)" how sw.ElapsedMilliseconds

                        assignment |> Array.map (fun j -> targets.[j]),
                        assignment,
                        frame toFormation assignment,
                        {
                            From = fromFormation.Name
                            To = toFormation.Name
                            StartDistance = total start
                            Distance = total assignment
                            StartUnsafePairs = startUnsafe
                            FewestUnsafePairs = 0
                            Method = how
                            Solved = true
                            ElapsedMs = sw.ElapsedMilliseconds
                        }
                    | Error msg ->
                        let closest = if fewest = Int32.MaxValue then startUnsafe else fewest

                        eprintfn
                            "  FAILED: %s; the closest assignment the gate checked had %d pairs inside the bound (%d ms); the drones hold"
                            msg
                            closest
                            sw.ElapsedMilliseconds

                        current,
                        held,
                        { List.head frames with Name = fromFormation.Name + " (held)" },
                        {
                            From = fromFormation.Name
                            To = toFormation.Name
                            StartDistance = total start
                            Distance = total start
                            StartUnsafePairs = startUnsafe
                            FewestUnsafePairs = closest
                            Method = msg
                            Solved = false
                            ElapsedMs = sw.ElapsedMilliseconds
                        }

                next, nextHeld, transition :: flown, flownFrame :: frames)

        transitions |> List.rev |> List.toArray, frames |> List.rev |> List.toArray

// =============================================================================
// SCALE RUN MISSIONS (the show as every drone flies it)
// =============================================================================

/// The MAVLink missions of a scale run, written by the 4-drone show's exporter
/// (MAVLinkExport.exportShow): every drone's own mission through the formations as flown,
/// at the outdoor scale that keeps the plan as flown at least Margin × the minimum
/// separation apart (the show's rule), its light at every formation as a CSV beside it, a
/// README, and the lot zipped for download. Nothing here plans; the assignments are the
/// scale run's.
module ScaleMissions =

    /// Margin over the minimum separation the flown plan keeps, the 4-drone show's.
    [<Literal>]
    let Margin = 1.2

    /// Cruise speed of the missions, m/s, the 4-drone show's.
    [<Literal>]
    let CruiseMs = 3.0

    /// Name of the zip a scale run writes.
    [<Literal>]
    let ZipName = "swarm-light-show-missions.zip"

    /// The formations as flown, by drone, from the picture's frames
    let private flownFormations (frames: LightShowPicture.Frame[]) =
        frames
        |> Array.map (fun f ->
            {|
                Name = f.Name
                Positions = f.Positions |> Array.map (fun (x, y, z) -> {| X = x; Y = y; Z = z |})
            |})

    /// The scale at which the plan as flown (take-off and every transition with S-curve
    /// timing) keeps Margin × the minimum separation, grown from the scale at which the
    /// closest slots and straight-line transits do, and the closest approach as flown at
    /// it
    let private outdoorScale
        (formations: {| Name: string; Positions: {| X: float; Y: float; Z: float |}[] |}[])
        (n: int)
        : float * float =
        let positionsOf k =
            formations.[k].Positions |> Array.map (fun p -> { X = p.X; Y = p.Y; Z = p.Z }: Position3D)

        let closestSlots =
            [
                for k in 0 .. formations.Length - 1 do
                    let ps = positionsOf k

                    for i in 0 .. n - 1 do
                        for j in i + 1 .. n - 1 do
                            Geometry.distance ps.[i] ps.[j]
            ]
            |> List.fold min Double.PositiveInfinity

        let closestTransit =
            [
                for k in 0 .. formations.Length - 2 do
                    if formations.[k + 1].Positions |> Array.exists (fun p -> p.Z > 0.0) then
                        TransitionSafety.minSeparation (positionsOf k) (positionsOf (k + 1))
            ]
            |> List.fold min Double.PositiveInfinity

        let target = Safety.minSwarmSeparationMeters * Margin
        let flownAt s = MAVLinkExport.showSeparation s CruiseMs formations n

        if min closestSlots closestTransit <= 1e-9 then
            invalidArg "frames" "two drones share a slot or their straight paths meet; no scale separates them"

        // No scale separates paths that meet: a closest approach of 0 ends the search
        let rec grow s round =
            let flown = flownAt s

            if flown >= target || round >= 8 || flown <= 1e-9 then
                (s, flown)
            else
                grow (s * max 1.01 (target / flown)) (round + 1)

        grow (Geometry.scaleFor Safety.minSwarmSeparationMeters Margin (min closestSlots closestTransit)) 0

    let private readme
        (title: string)
        (drones: int)
        (formations: int)
        (scale: float)
        (flownSpacing: float)
        (home: MavlinkMission.GeoCoordinate)
        =
        String.Join(
            "\n",
            [
                $"# {title}: the flight instructions"
                ""
                $"Every drone's own ArduPilot mission through the show, {drones} drones and {formations} formations, as planned by the SwarmChoreography example of FSharp.Azure.Quantum (https://github.com/thorium/fsharp.azure.quantum): every change of formation was assigned to the drones by the QAOA block search on 16-qubit circuits and passed the in-flight separation gate, and these files are that plan as flown."
                ""
                "## Files"
                ""
                "- `DroneN_mission.plan`: the mission of drone N for QGroundControl."
                "- `DroneN.waypoints`: the same mission as a MAVLink waypoint file (Mission Planner, MAVProxy)."
                "- `DroneN.param`: the ArduPilot 4.7 parameters the mission assumes; load them before flight."
                "- `lights/DroneN.csv`: the light drone N shows at every formation, in show order (formation index, name, colour). MAVLink missions have no light command: a show controller or an onboard script sets the light when the drone reaches the formation."
                "- `mavlink_show.fsx`: starts every mission together (`dotnet fsi mavlink_show.fsx --dry-run` lists what it would do). The missions are synchronised, so they are flown together or not at all."
                ""
                "## Figures"
                ""
                $"- Home: {home.Latitude:F6}, {home.Longitude:F6}, {home.Altitude:F1} m. It is the site the plan was written for; each drone arms on its own slot of the ground grid around it. Plan your own site by running the example with `--home-lat` and `--home-lon`."
                $"- Scale: the planned positions × {scale:F2}, the scale at which the plan as flown keeps at least {Margin} × the {Safety.minSwarmSeparationMeters:F0} m minimum separation; closest approach as flown {flownSpacing:F1} m."
                $"- Cruise speed {CruiseMs:F1} m/s; every drone holds {MAVLinkExport.Autopilot.formationHoldS:F0} s at a formation, longer where it waits for the slowest. A formation whose only change is the lights is a hold at the same waypoint."
                "- Each drone lands on the slot it took off from."
                ""
                "## Before anything flies"
                ""
                "A plan is not a permission. The missions were checked in ArduPilot's own simulator, not on aircraft; the operator's authorisation, the airspace, the weather limits and the separation the aircraft can actually hold are the operator's to establish. Nothing in these files replaces that."
                ""
                "MIT licence, as the repository."
                ""
            ]
        )

    /// Writes the missions of `frames` under `outDir/missions` and zips them to
    /// `outDir/ZipName`: the zip's path, the scale flown and the closest approach as flown.
    let write
        (outDir: string)
        (title: string)
        (home: MavlinkMission.GeoCoordinate)
        (frames: LightShowPicture.Frame[])
        : string * float * float =
        let formations = flownFormations frames
        let n = frames.[0].Positions.Length
        let scale, flownSpacing = outdoorScale formations n
        let dir = Path.Combine(outDir, "missions")

        if Directory.Exists dir then
            Directory.Delete(dir, true)

        let swarm =
            MAVLinkExport.exportShow
                dir
                title
                home
                scale
                CruiseMs
                formations
                n
                (Safety.minSwarmSeparationMeters * Margin)
                "Quantum (QAOA block search)"

        let lightsDir = Path.Combine(dir, "lights")
        Directory.CreateDirectory lightsDir |> ignore

        for d in 0 .. n - 1 do
            let name =
                (swarm.Missions |> List.find (fun m -> m.Drone.SystemId = d + 1)).Drone.Name

            File.WriteAllLines(
                Path.Combine(lightsDir, name + ".csv"),
                Array.append
                    [| "formation,name,light" |]
                    (frames
                     |> Array.mapi (fun k f ->
                         let r, g, b = f.Lights.[d]
                         sprintf "%d,\"%s\",#%02x%02x%02x" k f.Name r g b))
            )

        File.WriteAllText(
            Path.Combine(dir, "README.md"),
            readme title n frames.Length scale flownSpacing home
        )

        let zip = Path.Combine(outDir, ZipName)

        if File.Exists zip then
            File.Delete zip

        System.IO.Compression.ZipFile.CreateFromDirectory(dir, zip)
        zip, scale, flownSpacing

// =============================================================================
// SCALE RUN TRAJECTORIES (the show as a show controller flies it)
// =============================================================================

/// The show of a scale run as timed trajectories: for every drone, when it leaves each
/// formation, when it arrives at the next, its position there and its light. Between two
/// keyframes every drone moves in a straight line with the same progress law (a smoothstep
/// in time), so at every moment the drones' relative positions are those of the straight
/// synchronised lines the separation gate checked: the planned closest approach is the
/// flown one. A show controller streams these as position setpoints (MAVLink guided mode,
/// a Crazyflie's high-level commander). They are not waypoint missions, which fly legs of
/// different lengths apart in time and cannot keep a plan like this (see ScaleMissions).
/// Written as one zip.
module ScaleTrajectories =

    /// Seconds a formation is held, as a show holds a picture.
    [<Literal>]
    let HoldS = 3.0

    /// Seconds between two steps that change only the lights.
    [<Literal>]
    let LightStepS = 0.5

    /// Horizontal cruise speed, m/s, the MAVLink missions' as well.
    [<Literal>]
    let CruiseMs = 3.0

    /// Name of the zip a scale run writes.
    [<Literal>]
    let ZipName = "swarm-light-show-trajectories.zip"

    /// Progress through a transition at share `f` of its time: the smoothstep 3f² − 2f³,
    /// at rest at both ends; its peak speed is 1.5 × the average and its peak
    /// acceleration 6 × the leg / T².
    let progress (f: float) : float = f * f * (3.0 - 2.0 * f)

    /// A transition's seconds, the same for every drone so they move together: long
    /// enough that the longest leg's peak speed stays within the autopilot's cruise,
    /// climb and descent speeds and its peak acceleration within the autopilot's
    /// acceleration limit (MAVLinkExport.Autopilot).
    let transitionSeconds (fromP: (float * float * float)[]) (toP: (float * float * float)[]) : float =
        let mutable bySpeed = 0.0
        let mutable longest = 0.0

        for d in 0 .. fromP.Length - 1 do
            let (ax, ay, az), (bx, by, bz) = fromP.[d], toP.[d]
            let h = sqrt ((bx - ax) ** 2.0 + (by - ay) ** 2.0)
            let dz = bz - az
            longest <- max longest (sqrt (h * h + dz * dz))

            let vertical =
                if dz >= 0.0 then
                    dz / MAVLinkExport.Autopilot.climbSpeedMs
                else
                    -dz / MAVLinkExport.Autopilot.descentSpeedMs

            bySpeed <- max bySpeed (max (h / CruiseMs) vertical)

        let accel = min MAVLinkExport.Autopilot.accelMss MAVLinkExport.Autopilot.accelZMss
        max (1.5 * bySpeed) (sqrt (6.0 * longest / accel))

    /// When every frame is arrived at and left: frame 0 at 0; a frame with the previous
    /// frame's positions is a light step; a flown frame is arrived at transitionSeconds
    /// after the previous one is left and held HoldS, nothing when flown through, a light
    /// step when the next frame only changes the lights; the last is held HoldS.
    let timeline (frames: LightShowPicture.Frame[]) : (float * float)[] =
        let n = frames.Length
        let times = Array.zeroCreate<float * float> n
        let mutable t = 0.0

        for k in 0 .. n - 1 do
            let flown = k > 0 && frames.[k].Positions <> frames.[k - 1].Positions
            let nextFlown = k < n - 1 && frames.[k + 1].Positions <> frames.[k].Positions

            let arrive =
                if flown then
                    t + transitionSeconds frames.[k - 1].Positions frames.[k].Positions
                else
                    t

            let hold =
                if k = n - 1 then HoldS
                elif frames.[k].Flow && nextFlown then 0.0
                elif nextFlown && (k = 0 || flown) then HoldS
                else LightStepS

            times.[k] <- (arrive, arrive + hold)
            t <- arrive + hold

        times

    /// Closest two drones come in the whole show on the straight synchronised lines (the
    /// gate's figure, exact under the common progress law), and in which transition.
    let closestApproach (frames: LightShowPicture.Frame[]) : float * string =
        let toP (x, y, z) : Position3D = { X = x; Y = y; Z = z }

        [
            for k in 1 .. frames.Length - 1 do
                if frames.[k].Positions <> frames.[k - 1].Positions then
                    TransitionSafety.minSeparation
                        (Array.map toP frames.[k - 1].Positions)
                        (Array.map toP frames.[k].Positions),
                    sprintf "%s → %s" frames.[k - 1].Name frames.[k].Name
        ]
        |> List.minBy fst

    let private hex (r: int, g: int, b: int) = sprintf "#%02x%02x%02x" r g b

    let private readme
        (title: string)
        (drones: int)
        (frames: int)
        (durationS: float)
        (closest: float)
        (closestIn: string)
        (home: MavlinkMission.GeoCoordinate option)
        =
        String.Join(
            "\n",
            [
                $"# {title}: the flight plan"
                ""
                $"Timed trajectories for {drones} drones through {frames} formations, {durationS:F0} s of show, as planned by the SwarmChoreography example of FSharp.Azure.Quantum (https://github.com/thorium/fsharp.azure.quantum): every change of formation was assigned to the drones by the QAOA block search on 16-qubit circuits and passed the in-flight separation gate."
                ""
                "## Files"
                ""
                "- `trajectories/DroneN.csv`: drone N's keyframes, one row when it arrives at a formation and one when it leaves: `t_s,x_m,y_m,z_m,light,formation`. x is east, y north, z up, in metres from the show's origin on the ground. The light is the colour the drone shows from that moment: at a departure it is already the light of the formation it flies to, as the drones set it."
                "- `show.json`: the formations with their arrival and departure times, the progress law, the speed limits and the origin."
                ""
                "## How the drones move"
                ""
                "Between a departure at t0 and the next arrival at t1, every drone moves in a straight line from its departure point to its arrival point, at the same share of its own leg as every other drone: share = 3f² − 2f³ with f = (t − t0) / (t1 − t0). The share is what keeps the plan's separation: the gate checked the straight lines flown together, and under a common law the drones' relative positions at any moment are those lines at that share. A show controller streams these positions at 10 Hz or more (MAVLink: SET_POSITION_TARGET_LOCAL_NED in GUIDED, north = y, east = x, down = −z) and sets the light at each departure."
                ""
                $"Each transition takes long enough that the longest leg peaks at {CruiseMs:F1} m/s horizontally, {MAVLinkExport.Autopilot.climbSpeedMs:F1} m/s climbing, {MAVLinkExport.Autopilot.descentSpeedMs:F1} m/s descending, and at {min MAVLinkExport.Autopilot.accelMss MAVLinkExport.Autopilot.accelZMss:F1} m/s² of acceleration; shorter legs are slower in proportion. A formation is held {HoldS:F0} s, a step that only changes the lights {LightStepS:F1} s, a step of a deformation or a turn is flown through."
                ""
                "## Figures"
                ""
                $"- Closest two drones come: {closest:F2} m, in {closestIn}; the gate's bound is the closest slot spacing / √2 of each transition."
                (match home with
                 | Some h -> $"- Origin: {h.Latitude:F6}, {h.Longitude:F6}, {h.Altitude:F1} m, the site the plan was given; the launch grid stands around it."
                 | None -> "- Origin: not fixed; run the example with `--home-lat` and `--home-lon` to put the show on a site.")
                ""
                "## What this is not"
                ""
                "These are not waypoint missions. A waypoint mission flies each leg at its own speed, at least 0.5 m/s, and holds the time a short leg saves, so drones with legs of different lengths come apart in time and the plan's separation is lost; the example measured 0.25 m where the plan keeps 2.9 m. A plan is not a permission either: the operator's authorisation, the airspace, the weather limits and the separation the aircraft can hold are the operator's to establish."
                ""
                "MIT licence, as the repository."
                ""
            ]
        )

    /// Writes the trajectories of `frames` under `outDir/trajectories` and zips them to
    /// `outDir/ZipName`: the zip's path, the show's seconds, and the closest approach with
    /// the transition it is in.
    let write
        (outDir: string)
        (title: string)
        (home: MavlinkMission.GeoCoordinate option)
        (frames: LightShowPicture.Frame[])
        : string * float * float * string =
        let n = frames.[0].Positions.Length
        let times = timeline frames
        let durationS = snd times.[times.Length - 1]
        let closest, closestIn = closestApproach frames
        let dir = Path.Combine(outDir, "trajectories")

        if Directory.Exists dir then
            Directory.Delete(dir, true)

        let perDrone = Path.Combine(dir, "trajectories")
        Directory.CreateDirectory perDrone |> ignore
        let num (v: float) = v.ToString("0.###", Globalization.CultureInfo.InvariantCulture)

        for d in 0 .. n - 1 do
            let rows =
                [|
                    yield "t_s,x_m,y_m,z_m,light,formation"

                    for k in 0 .. frames.Length - 1 do
                        let arrive, depart = times.[k]
                        let x, y, z = frames.[k].Positions.[d]
                        let place = sprintf "%s,%s,%s" (num x) (num y) (num z)
                        yield sprintf "%s,%s,%s,\"%s\"" (num arrive) place (hex frames.[k].Lights.[d]) frames.[k].Name

                        if depart > arrive then
                            let next = if k < frames.Length - 1 then frames.[k + 1].Lights.[d] else frames.[k].Lights.[d]
                            yield sprintf "%s,%s,%s,\"%s\"" (num depart) place (hex next) frames.[k].Name
                |]

            File.WriteAllLines(Path.Combine(perDrone, sprintf "Drone%d.csv" d), rows)

        Reporting.writeJson
            (Path.Combine(dir, "show.json"))
            {|
                title = title
                drones = n
                duration_s = durationS
                closest_approach_m = closest
                closest_approach_in = closestIn
                progress_law = "share = 3f^2 - 2f^3 of each drone's straight leg, f = (t - depart) / (arrive - depart), all drones together"
                limits =
                    {|
                        cruise_ms = CruiseMs
                        climb_ms = MAVLinkExport.Autopilot.climbSpeedMs
                        descent_ms = MAVLinkExport.Autopilot.descentSpeedMs
                        acceleration_mss = min MAVLinkExport.Autopilot.accelMss MAVLinkExport.Autopilot.accelZMss
                        hold_s = HoldS
                        light_step_s = LightStepS
                    |}
                axes = "x east, y north, z up, metres from the origin on the ground"
                origin =
                    match home with
                    | Some h -> [| h.Latitude; h.Longitude; h.Altitude |]
                    | None -> [||]
                formations =
                    frames
                    |> Array.mapi (fun k f ->
                        {|
                            name = f.Name
                            flow = f.Flow
                            arrive_s = fst times.[k]
                            depart_s = snd times.[k]
                        |})
            |}

        File.WriteAllText(Path.Combine(dir, "README.md"), readme title n frames.Length durationS closest closestIn home)
        ShowController.write dir

        // One simulator per drone at its slot, when the show has a site
        home
        |> Option.iter (fun site ->
            ShowController.writeSitlStart
                dir
                (frames.[0].Positions
                 |> Array.map (fun (x, y, _) ->
                     let g = MAVLinkExport.position3DToMissionPoint site x y 0.0 1.0
                     g.Latitude, g.Longitude, site.Altitude)))

        let zip = Path.Combine(outDir, ZipName)

        if File.Exists zip then
            File.Delete zip

        System.IO.Compression.ZipFile.CreateFromDirectory(dir, zip)
        zip, durationS, closest, closestIn

// =============================================================================
// VISUALIZATION
// =============================================================================

module Visualization =

    /// Generate ASCII art for a formation (front view)
    let renderFormation (formation: Formation) : string =
        let width = 40
        let height = 12
        let grid = Array2D.create height width ' '

        // Scale positions to grid
        let scaleX x =
            int ((x + 15.0) / 30.0 * float (width - 1))

        let scaleZ z =
            int ((35.0 - z) / 40.0 * float (height - 1))

        // Plot drones
        for i, pos in Array.indexed formation.Positions do
            let gx = scaleX pos.X |> max 0 |> min (width - 1)
            let gz = scaleZ pos.Z |> max 0 |> min (height - 1)
            grid.[gz, gx] <- char (48 + i) // '0' to '3'

        // Build string
        let sb = System.Text.StringBuilder()
        sb.AppendLine($"Formation: {formation.Name}") |> ignore
        sb.AppendLine(String.replicate width "-") |> ignore

        for row in 0 .. height - 1 do
            for col in 0 .. width - 1 do
                sb.Append(grid.[row, col]) |> ignore

            sb.AppendLine() |> ignore

        sb.AppendLine(String.replicate width "-") |> ignore
        sb.ToString()

    /// Print transition result
    let printTransition (result: TransitionResult) =
        printfn ""
        printfn "╔══════════════════════════════════════════════════╗"
        printfn "║  FORMATION TRANSITION                            ║"
        printfn "╠══════════════════════════════════════════════════╣"
        printfn "║  From: %-40s ║" result.FromFormation
        printfn "║  To:   %-40s ║" result.ToFormation
        printfn "║  Method: %-38s ║" result.Method
        printfn "║  Total Distance: %8.2f meters                ║" result.TotalDistance
        printfn "╠══════════════════════════════════════════════════╣"
        printfn "║  ASSIGNMENTS:                                    ║"

        for a in result.Assignments |> Array.sortBy (fun x -> x.DroneId) do
            printfn "║    Drone %d → Position %d                           ║" a.DroneId a.TargetPositionIndex

        printfn "╚══════════════════════════════════════════════════╝"

// =============================================================================
// 1:N PERMISSION EVIDENCE
// =============================================================================

/// Evidence for a one-pilot-to-many-drones (1:N) permission, computed on the
/// geometry that is actually flown. Every track is rebuilt from the EXPORTED
/// waypoints (the MAVLink mission items or the Crazyflie show), not from the
/// optimiser's intent, so a mismatch between the two - an export bug - shows up
/// here rather than in the air.
module Evidence =

    module Ev = FSharp.Azure.Quantum.Examples.Drones.PermissionEvidence
    module Dyn = DynamicBehavior

    /// What is flown: the outdoor MAVLink missions when they were exported,
    /// otherwise the indoor Crazyflie show.
    type Flown =
        /// `dir` holds the exported files; the parameter files are read back from it.
        | Outdoor of swarm: MAVLinkExport.SwarmMission * scale: float * dir: string
        | Indoor of show: CrazyflieExport.ShowDefinition * layout: IndoorLayout.Plan * roomXM: float * roomYM: float

    /// Nominal endurance when --endurance-min is not given. DroneDomain has no
    /// endurance figure for these aircraft, so both are declared assumptions:
    /// 20 min for a small ArduPilot quad; 7 min is Bitcraze's stated flight time
    /// of a Crazyflie 2.1 on its stock battery without decks.
    let defaultEnduranceMin =
        function
        | Outdoor _ -> 20.0
        | Indoor _ -> 7.0

    /// Sampling step for counting drones airborne together (closestApproach is exact and ignores it).
    [<Literal>]
    let private stepS = 0.1

    /// Below this height an aircraft counts as parked on the ground.
    [<Literal>]
    let private groundZ = 0.1

    [<Literal>]
    let private c2BandMhz = 2400.0

    [<Literal>]
    let private c2FadeMarginDb = 10.0

    /// "Everyone reacts at once" contingencies are triggered this often.
    [<Literal>]
    let private triggerEveryS = 1.0

    /// How far a drone dropping out of the outdoor show steps out of the
    /// formation plane before landing: 1.2 x the outdoor minimum separation.
    let private stepOutM = Safety.minSwarmSeparationMeters * 1.2

    // Constants written into crazyflie_show.py by CrazyflieExport.toPythonScript.
    [<Literal>]
    let private cfTakeoffHeightM = 0.5

    [<Literal>]
    let private cfTakeoffS = 2.0

    [<Literal>]
    let private cfLandingS = 2.0

    [<Literal>]
    let private cfPauseS = 0.5

    [<Literal>]
    let private cfMinHeightM = 0.2

    [<Literal>]
    let private cfMaxHeightM = 2.0

    let private p3 x y z : Ev.P3 = { X = x; Y = y; Z = z }

    let private name (drone: int) = $"Drone%d{drone}"

    type private Step =
        /// An ArduPilot leg to a point at this horizontal speed (m/s), flown with
        /// the S-curve timing of MAVLinkExport.legSamples.
        | Leg of target: Ev.P3 * speedMs: float
        /// Straight line in a fixed time (s): a Crazyflie go_to, or a descent at
        /// a fixed rate.
        | Within of target: Ev.P3 * seconds: float
        | Wait of seconds: float

    let private local (p: Ev.P3) : MAVLinkExport.LocalPoint = { X = p.X; Y = p.Y; Z = p.Z }

    let private fly (id: string) (start: Ev.P3) (steps: Step list) : Ev.Track =
        // A Leg is flown with ArduPilot's S-curve timing, sampled every 0.1 s:
        // the track is piecewise straight between samples, on the leg's line.
        let samples, _, _ =
            (([ (0.0, start) ], 0.0, start), steps)
            ||> List.fold (fun (acc, t, here) step ->
                match step with
                | Leg(p, v) ->
                    let points =
                        MAVLinkExport.legSamples 0.1 v (local here) (local p)
                        |> Array.filter (fun (dt, _) -> dt > 0.0)
                        |> Array.map (fun (dt, q) -> (t + dt, p3 q.X q.Y q.Z))
                        |> List.ofArray

                    match List.tryLast points with
                    | Some(tEnd, _) -> (List.rev points @ acc, tEnd, p)
                    | None -> (acc, t, p)
                | Within(p, s) -> ((t + s, p) :: acc, t + s, p)
                | Wait s -> ((t + s, here) :: acc, t + s, here))

        {
            AircraftId = id
            Samples = samples |> List.rev |> Array.ofList
        }
        : Ev.Track

    let private endTime (track: Ev.Track) = fst (Array.last track.Samples)

    /// A landed aircraft stays where it landed. Extending its track keeps it in
    /// the comparison, so another one descending onto the same spot is caught.
    let private parked (tracks: Ev.Track list) =
        let tEnd = tracks |> List.map endTime |> List.max

        tracks
        |> List.map (fun tr ->
            let t, p = Array.last tr.Samples

            if t < tEnd then
                { tr with
                    Samples = Array.append tr.Samples [| (tEnd, p) |]
                }
            else
                tr)

    /// Closest approach with vertical distances weighted by `zWeight`: 1
    /// outdoors, 0.5 indoors (downwash, see IndoorLayout). Scaling one axis
    /// keeps straight lines straight, so the exact closest approach stays exact;
    /// the position reported is converted back to metres.
    let private closest (zWeight: float) (tracks: Ev.Track list) =
        let weigh (tr: Ev.Track) =
            { tr with
                Samples = tr.Samples |> Array.map (fun (t, p) -> (t, { p with Z = p.Z * zWeight }))
            }

        Ev.closestApproach stepS (groundZ * zWeight) (parked tracks |> List.map weigh)
        |> Option.map (fun c -> { c with Where.Z = c.Where.Z / zWeight })

    /// The same tracks from `t0` on, so a contingency is judged on what it
    /// adds, not on a conflict the normal show already had before it began.
    let private after (t0: float) (tracks: Ev.Track list) =
        tracks
        |> List.map (fun tr ->
            match Ev.positionAt tr t0 with
            | Some p ->
                { tr with
                    Samples = Array.append [| (t0, p) |] (tr.Samples |> Array.filter (fun (t, _) -> t > t0))
                }
            // Every track starts at 0, so this one has already landed: it stays parked.
            | None ->
                { tr with
                    Samples = [| (t0, snd (Array.last tr.Samples)) |]
                })

    let private shift (dt: float) (tr: Ev.Track) =
        { tr with
            Samples = tr.Samples |> Array.map (fun (t, p) -> (t + dt, p))
        }

    let private distanceOf (c: Ev.Closest option) =
        c
        |> Option.map (fun c -> c.Distance)
        |> Option.defaultValue Double.PositiveInfinity

    let private describe (c: Ev.Closest option) =
        match c with
        | Some c ->
            sprintf
                "%.2f m (%s / %s at t=%.1f s, near (%.2f, %.2f, %.2f) m)"
                c.Distance
                c.A
                c.B
                c.TimeS
                c.Where.X
                c.Where.Y
                c.Where.Z
        | None -> "fewer than two aircraft airborne"

    /// ArduPilot's landing: descend at WPNAV_SPEED_DN down to LAND_ALT_LOW, then
    /// at LAND_SPEED to the ground (RTL_ALT_FINAL 0 lands).
    let private landSteps (at: Ev.P3) =
        let low = min at.Z MAVLinkExport.Autopilot.landAltLowM

        [
            Leg(p3 at.X at.Y low, MAVLinkExport.Autopilot.minLegSpeedMs)
            Within(p3 at.X at.Y 0.0, low / MAVLinkExport.Autopilot.landSpeedMs)
        ]

    /// One drone's RTL as its parameters set it (RTL_ALT_M, RTL_SPEED_MS,
    /// RTL_LOIT_TIME; RTL_CONE_SLOPE 0).
    [<Struct>]
    type private RtlConfig =
        {
            AltM: float
            SpeedMs: float
            LoiterS: float
        }

    /// ArduPilot RTL: climb vertically to RTL_ALT (or stay higher), fly straight
    /// home at that height, loiter RTL_LOIT_TIME, land. With RTL_CONE_SLOPE 0
    /// (exported) the climb is not lowered near home.
    let private rtl (cfg: RtlConfig) (home: Ev.P3) (from: Ev.P3) =
        let cruise = max from.Z cfg.AltM

        [
            Leg(p3 from.X from.Y cruise, cfg.SpeedMs)
            Leg(p3 home.X home.Y cruise, cfg.SpeedMs)
            Wait cfg.LoiterS
        ]
        @ landSteps (p3 home.X home.Y cruise)

    /// One operation in flown metres (X east, Y north, Z up above the take-off
    /// ground), drone-indexed.
    type private Flight =
        {
            /// Per point: the formation it belongs to.
            Names: string[]
            /// Per drone: its formation points in show order, read from the export.
            Points: Ev.P3[][]
            /// Per drone: where it stands before take-off.
            Parking: Ev.P3[]
            /// Per point: all slots of that formation, for re-assignment after a drop-out.
            Slots: Ev.P3[][]
            /// Per drone, as exported: take-off, one segment per point (leg and
            /// hold), and the ending.
            Segments: Step list[][]
            /// One transition flown together by a group of drones, (from, to)
            /// per drone, as the export would plan it: leg and hold per drone.
            Transition: (Ev.P3 * Ev.P3)[] -> Step list[]
            /// How a drone's flight ends normally, from its last point.
            Finish: int -> Ev.P3 -> Step list
            /// What a drone dropping out does, from where it is.
            Depart: int -> Ev.P3 -> Step list
            /// What every drone does when all are told to stop at once.
            Abort: int -> Ev.P3 -> Step list
            /// How long the others hold where they are while a drone drops out
            /// (DynamicBehavior's Hold), before their re-planned transitions.
            DepartureHoldS: float
            /// The last this many points are each drone's own slot (kept for it in a re-plan too).
            OwnSlotPoints: int
            /// Weight of vertical distances in the separation measure.
            ZWeight: float
        }

    /// The exported flight of one drone up to the end of its hold at point k.
    let private prefix (f: Flight) (drone: int) (k: int) =
        f.Segments.[drone] |> Array.take (k + 2) |> List.concat

    let private showTrack (f: Flight) (drone: int) =
        fly (name drone) f.Parking.[drone] (f.Segments.[drone] |> List.concat)

    /// ArduPilot's own RTL_ALT_M default, used when a drone has no parameter file.
    [<Literal>]
    let private ardupilotDefaultRtlAltM = 15.0

    /// A drone's RTL as its exported parameter file sets it; ArduPilot's
    /// defaults where the file or a value is missing (the failsafe check fails then).
    let private rtlConfigOf (cruise: float) (parms: Map<string, float> option) : RtlConfig =
        let get name fallback =
            parms |> Option.bind (Map.tryFind name) |> Option.defaultValue fallback

        {
            AltM = get "RTL_ALT_M" ardupilotDefaultRtlAltM
            SpeedMs = get "RTL_SPEED_MS" cruise
            LoiterS = get "RTL_LOIT_TIME" (MAVLinkExport.Autopilot.rtlLoiterS * 1000.0) / 1000.0
        }

    /// The exported MAVLink missions, read back item by item, with each drone's
    /// exported parameter file.
    let private outdoor
        (swarm: MAVLinkExport.SwarmMission)
        (parms: Map<string, float> option[])
        (names: string[])
        (slots: Ev.P3[][])
        =
        let missions = swarm.Missions |> Array.ofList
        let cruise = swarm.Metadata.CruiseSpeedMs
        let origin = swarm.Metadata.ShowOrigin

        let toLocal (lat: float) (lon: float) (altAboveHome: float) =
            let l =
                MavlinkMission.geoToLocal
                    origin
                    { origin with
                        Latitude = lat
                        Longitude = lon
                    }

            p3 l.East l.North altAboveHome

        // GlobalRelativeAlt frame: the autopilot flies the written altitude
        // above home, whatever --home-alt was.
        let pointOf (item: MavlinkMission.MissionItem) =
            toLocal item.Latitude item.Longitude item.Altitude

        // ArduPilot's home is where the drone armed; the export declares it per
        // drone (plannedHomePosition, .waypoints row 0).
        let homes =
            missions
            |> Array.map (fun m -> toLocal m.HomePosition.Latitude m.HomePosition.Longitude 0.0)

        let rtlOf = parms |> Array.map (rtlConfigOf cruise)

        // Replay each mission: DO_CHANGE_SPEED sets the speed of the legs that
        // follow, NAV_WAYPOINT flies a leg and holds, the last item ends the flight.
        let replay (d: int) (m: MavlinkMission.DroneMission) =
            let start = ref (p3 0.0 0.0 0.0)
            let here = ref (p3 0.0 0.0 0.0)
            let speed = ref cruise
            let segments = ResizeArray<Step list>()
            let current = ResizeArray<Step>()
            let points = ResizeArray<Ev.P3>()
            let ending = ref (fun (_: Ev.P3) -> ([]: Step list))

            for item in m.Items do
                match item.Command with
                | MavlinkMission.NavTakeoff ->
                    // ArduPilot ignores NAV_TAKEOFF's lat/lon and climbs where the
                    // drone was armed; the export writes that arming point there.
                    let p = pointOf item
                    start.Value <- { p with Z = 0.0 }
                    here.Value <- p
                    current.Add(Leg(p, speed.Value))
                | MavlinkMission.DoChangeSpeed -> speed.Value <- item.Param2
                | MavlinkMission.NavWaypoint ->
                    segments.Add(List.ofSeq current)
                    current.Clear()
                    let p = pointOf item
                    current.Add(Leg(p, speed.Value))
                    // ArduPilot stores a waypoint's hold (param1) as whole seconds
                    // (AP_Mission: cmd.p1 is a uint16): the fraction is dropped.
                    current.Add(Wait(Math.Floor item.Param1))
                    points.Add p
                    here.Value <- p
                // NAV_DELAY keeps its seconds as a float: the hold's fraction.
                | MavlinkMission.NavDelay -> current.Add(Wait item.Param1)
                | MavlinkMission.NavLand ->
                    // Fly to the landing point at the current height, then land.
                    let target = pointOf item
                    let v = speed.Value

                    ending.Value <-
                        fun (p: Ev.P3) ->
                            let above = p3 target.X target.Y p.Z
                            Leg(above, v) :: landSteps above
                | MavlinkMission.NavReturnToLaunch -> ending.Value <- rtl rtlOf.[d] homes.[d]
                | _ -> ()

            segments.Add(List.ofSeq current)
            segments.Add(ending.Value here.Value)
            (start.Value, Array.ofSeq points, Array.ofSeq segments, ending.Value)

        let replays = missions |> Array.mapi replay

        let flight =
            {
                Names = names
                Points = replays |> Array.map (fun (_, ps, _, _) -> ps)
                Parking = replays |> Array.map (fun (s, _, _, _) -> s)
                Slots = slots
                Segments = replays |> Array.map (fun (_, _, segs, _) -> segs)
                Transition =
                    fun legs ->
                        MAVLinkExport.syncTransition
                            cruise
                            MAVLinkExport.Autopilot.formationHoldS
                            (legs |> Array.map (fun (a, b) -> (local a, local b)))
                        |> Array.mapi (fun i (v, hold) -> [ Leg(snd legs.[i], v); Wait hold ])
                Finish = fun d -> replays.[d] |> fun (_, _, _, ending) -> ending
                // A drone dropping out steps out of the formation plane (every
                // formation lies in the east-up plane at Y = 0) to the north,
                // at its height, and lands there: no vertical climb or descent
                // through the formation. A lone RTL would first climb straight
                // up, through any drone stacked above it.
                Depart =
                    fun _ p ->
                        let out = p3 p.X (p.Y + stepOutM) p.Z
                        Leg(out, cruise) :: landSteps out
                // Commanded for everyone at once (show abort): RTL as each
                // drone's parameters make it, to its declared home.
                Abort = fun d p -> rtl rtlOf.[d] homes.[d] p
                // Outdoors the leaving drone steps out of the plane at once.
                DepartureHoldS = 0.0
                OwnSlotPoints = 0
                ZWeight = 1.0
            }

        (flight, cruise, homes, rtlOf)

    /// The Crazyflie show as crazyflie_show.py flies it: take-off, then the
    /// on-board trajectory (a move and a pause per formation after the start
    /// one), then land.
    let private indoor
        (show: CrazyflieExport.ShowDefinition)
        (names: string[])
        (slots: Ev.P3[][])
        (ownSlotPoints: int)
        =
        // Crazyflie frame (X forward, Y left) back to X east, Y north. Nothing
        // is clamped: the script refuses a show outside [MIN_HEIGHT, MAX_HEIGHT].
        let ofCf (p: CrazyflieExport.CrazyfliePosition) = p3 (-p.Y) p.X p.Z

        // The waypoints become a Python dict literal keyed by drone id: a later
        // entry for the same drone wins, and a drone without one keeps its place.
        let points =
            show.Drones
            |> Array.map (fun drone ->
                show.Formations
                |> Array.scan
                    (fun prev fw ->
                        fw.Waypoints
                        |> Array.filter (fun w -> w.DroneId = drone.Id)
                        |> Array.tryLast
                        |> Option.map (fun w -> ofCf w.Position)
                        |> Option.orElse prev)
                    None
                |> Array.tail
                |> Array.choose id)

        let duration =
            show.Formations
            |> Array.collect (fun f -> f.Waypoints)
            |> Array.tryHead
            |> Option.map (fun w -> w.Duration)
            |> Option.defaultValue 0.0

        // The script's checklist: drones start on the ground at their start positions.
        let parking = points |> Array.map (fun ps -> p3 ps.[0].X ps.[0].Y 0.0)

        let landHere (p: Ev.P3) =
            [ Within({ p with Z = 0.0 }, cfLandingS) ]

        let goTo (p: Ev.P3) = [ Within(p, duration); Wait cfPauseS ]

        let flight =
            {
                Names = names
                Points = points
                Parking = parking
                Slots = slots
                Segments =
                    points
                    |> Array.mapi (fun d ps ->
                        [|
                            // HL take-off, then the script's 0.5 s wait
                            [
                                Within(
                                    { parking.[d] with
                                        Z = cfTakeoffHeightM
                                    },
                                    cfTakeoffS
                                )
                                Wait cfPauseS
                            ]
                            // The start formation is where take-off leaves the drone.
                            []
                            yield! ps |> Array.tail |> Array.map goTo
                            landHere (Array.last ps)
                        |])
                Transition = Array.map (snd >> goTo)
                Finish = fun _ p -> landHere p
                // Drop-out procedure: straight down to take-off height (the
                // others hold meanwhile, so nobody is above it), then home under
                // the show (1.3 m below it) to its own start slot, and land
                // there: the closing formation keeps that slot for it.
                Depart =
                    fun d p ->
                        let down = { p with Z = IndoorLayout.hoverM }

                        let home =
                            { parking.[d] with
                                Z = IndoorLayout.hoverM
                            }

                        [
                            Within(down, cfLandingS)
                            Wait cfPauseS
                            Within(home, duration)
                            Wait cfPauseS
                        ]
                        @ landHere home
                Abort = fun _ p -> landHere p
                // The others hold (DynamicBehavior's Hold) while the leaving drone
                // descends: nobody flies over a drone on its way down.
                DepartureHoldS = cfLandingS + cfPauseS
                OwnSlotPoints = ownSlotPoints
                ZWeight = IndoorLayout.zWeight
            }

        (flight, duration)

    /// Every drone reacts at the same moment (all lose the link, or the pilot
    /// hits emergency stop), triggered every `triggerEveryS` along the show.
    /// Drones on the ground at the trigger stay there.
    let private allAtOnce (zWeight: float) (show: Ev.Track list) (react: int -> Ev.P3 -> Step list) =
        let tEnd = show |> List.map endTime |> List.max

        [ 0.0 .. triggerEveryS .. tEnd ]
        |> List.choose (fun t0 ->
            let here =
                show
                |> List.map (fun tr -> Ev.positionAt tr t0 |> Option.defaultValue (snd (Array.last tr.Samples)))

            if here |> List.forall (fun p -> p.Z < groundZ) then
                None
            else
                let tracks =
                    List.zip show here
                    |> List.mapi (fun d (tr, p) -> fly tr.AircraftId p (if p.Z < groundZ then [] else react d p))
                    |> List.map (shift t0)

                Some(t0, closest zWeight tracks, tracks))

    let private allAtOnceCheck (label: string) (claim: string) (method: string) (limit: float) scenarios =
        let below = scenarios |> List.filter (fun (_, c, _) -> distanceOf c < limit)

        let worst = scenarios |> List.sortBy (fun (_, c, _) -> distanceOf c)

        Ev.Checks.contingency
            claim
            (sprintf
                "%s; triggered every %.0f s along the show (%d triggers), limit %.1f m"
                method
                triggerEveryS
                scenarios.Length
                limit)
            (match worst with
             | (t0, c, _) :: _ -> sprintf "%s, worst trigger t=%.0f s: closest %s" label t0 (describe c)
             | [] -> label + ": no airborne moment")
            (if below.IsEmpty then Ev.Pass else Ev.Fail)
            [
                if not below.IsEmpty then
                    $"%d{below.Length} of %d{scenarios.Length} triggers bring two aircraft below %.1f{limit} m"
                for t0, c, _ in worst |> List.truncate 5 do
                    sprintf "trigger t=%.0f s: %s" t0 (describe c)
            ]

    /// `gone` leaves after holding at point k; the others are re-assigned to
    /// every later formation by DynamicBehavior (QAOA on the backend).
    let private dropOut backend shots profile (f: Flight) (gone: int) (k: int) =
        let n = f.Points.Length
        let stay = [ 0 .. n - 1 ] |> List.filter ((<>) gone)

        let toDyn (p: Ev.P3) : Dyn.Position = { X = p.X; Y = p.Y; Z = p.Z }

        let baseState =
            let s = Dyn.SwarmState.create n (List.replicate n profile)

            { s with
                DroneStates =
                    s.DroneStates
                    |> Map.add gone (Dyn.Departed(Dyn.Standard Dyn.ReturnToHome, DateTime.UtcNow))
            }

        // Measured in the weighted space (vertical x ZWeight), like everything else.
        let toP (p: Ev.P3) : Position3D =
            {
                X = p.X
                Y = p.Y
                Z = p.Z * f.ZWeight
            }

        // Per later point: where DynamicBehavior sends each remaining drone. Where the show
        // closes on each drone's own slot, the re-plan does too: the leaving drone has landed
        // on its own slot, which nobody else takes. Every other point is a QAOA re-plan that
        // passes the same transition safety gate as the show: only a sampled assignment whose
        // synchronised legs keep the CAPT bound is taken. None found is a failed re-plan; no
        // classical assignment stands in.
        let adapted, _ =
            [ k + 1 .. f.Slots.Length - 1 ]
            |> List.mapFold
                (fun (hereR: Result<Map<int, Ev.P3>, string>) j ->
                    let step =
                        hereR
                        |> Result.bind (fun here ->
                            if j >= f.Slots.Length - f.OwnSlotPoints then
                                Ok(
                                    Map.ofList [ for d in stay -> d, f.Points.[d].[j] ],
                                    "own start slots (the closing formation)"
                                )
                            else
                                let formation: Dyn.Formation =
                                    {
                                        Name = f.Names.[j]
                                        Positions = f.Slots.[j] |> Array.map toDyn
                                    }

                                let state =
                                    { baseState with
                                        DronePositions = here |> Map.map (fun _ p -> toDyn p)
                                    }

                                let proposedBy (assignments: Map<int, int>) =
                                    here
                                    |> Map.map (fun d p ->
                                        assignments
                                        |> Map.tryFind d
                                        |> Option.map (fun slot -> f.Slots.[j].[slot])
                                        |> Option.defaultValue p)

                                let ids = here |> Map.toArray |> Array.map fst
                                let starts = ids |> Array.map (fun d -> toP here.[d])

                                let safe (assignments: Map<int, int>) =
                                    let proposed = proposedBy assignments
                                    let ends = ids |> Array.map (fun d -> toP proposed.[d])

                                    TransitionSafety.minSeparation starts ends
                                    >= TransitionSafety.bound starts ends - 1e-9

                                Dyn.SwarmAdaptation.adaptFormationWith backend shots safe state formation 1000L
                                |> Result.map (fun r -> proposedBy r.Assignments, r.Method)
                                |> Result.mapError (fun msg -> sprintf "re-plan to %s failed: %s" f.Names.[j] msg))

                    (step, step |> Result.map fst))
                (Ok(Map.ofList [ for d in stay -> d, f.Points.[d].[k] ]))

        let adapted =
            adapted
            |> List.fold
                (fun acc step ->
                    match acc, step with
                    | Error e, _ -> Error e
                    | _, Error e -> Error e
                    | Ok steps, Ok s -> Ok(steps @ [ s ]))
                (Ok [])

        let departing =
            fly (name gone) f.Parking.[gone] (prefix f gone k @ f.Depart gone f.Points.[gone].[k])

        // The moment it leaves: the end of its hold at point k.
        let tLeave = endTime (fly (name gone) f.Parking.[gone] (prefix f gone k))

        // The remaining drones fly the re-planned transitions together, as the
        // export would plan them, then end as they normally do.
        let stayArr = Array.ofList stay

        let replanned =
            adapted
            |> Result.map (fun adapted ->
                let positions =
                    (Map.ofList [ for d in stay -> d, f.Points.[d].[k] ])
                    :: (adapted |> List.map fst)

                let legs =
                    positions
                    |> List.pairwise
                    |> List.map (fun (a, b) -> stayArr |> Array.map (fun d -> (a.[d], b.[d])) |> f.Transition)

                stayArr
                |> Array.mapi (fun i d ->
                    let steps =
                        Wait f.DepartureHoldS :: (legs |> List.collect (fun perDrone -> perDrone.[i]))

                    let last = (List.last positions).[d]
                    fly (name d) f.Parking.[d] (prefix f d k @ steps @ f.Finish d last))
                |> List.ofArray)

        let unchanged = stay |> List.map (showTrack f)

        {|
            LeavesAt = tLeave
            // Closest approach of the re-planned flight, or why no re-plan was found
            Replanned =
                replanned
                |> Result.map (fun tracks -> closest f.ZWeight (after tLeave (departing :: tracks)))
            Unchanged = closest f.ZWeight (after tLeave (departing :: unchanged))
            Methods =
                match adapted with
                | Ok steps -> steps |> List.map snd |> List.distinct
                | Error msg -> [ msg ]
            Tracks =
                match replanned with
                | Ok tracks -> departing :: tracks
                | Error _ -> departing :: unchanged
        |}

    let build
        (backend: IQuantumBackend)
        (shots: int)
        (pilots: int)
        (enduranceMin: float)
        (formations: Formation[])
        (flown: Flown)
        : Ev.Pack =

        let names = formations |> Array.map (fun f -> f.Name)

        let slotsAt (scale: float) (clampZ: float -> float) =
            formations
            |> Array.map (fun f ->
                f.Positions
                |> Array.map (fun p -> p3 (p.X * scale) (p.Y * scale) (clampZ (p.Z * scale))))

        let usableMin = enduranceMin * (1.0 - Battery.reserveBatteryPercent / 100.0)

        let f, limit, profile, operation, source, station, allAtOnceText, failsafe, assumptions =
            match flown with
            | Outdoor(swarm, scale, dir) ->
                let picked = swarm.Metadata.FormationIndices |> Array.ofList
                let slots = slotsAt scale id

                // Read the parameter files back from disk: evidence of what was
                // exported, not of what the exporter meant to write.
                let parms =
                    swarm.Missions
                    |> List.map (fun m ->
                        let path = Path.Combine(dir, MavlinkMission.ParamFile.fileName m)

                        if File.Exists path then
                            Some(MavlinkMission.ParamFile.parse (File.ReadAllText path))
                        else
                            None)
                    |> Array.ofList

                let f, cruise, homes, rtlOf =
                    outdoor
                        swarm
                        parms
                        (picked |> Array.map (fun i -> names.[i]))
                        (picked |> Array.map (fun i -> slots.[i]))

                let origin = swarm.Metadata.ShowOrigin

                // The tracks checked here, for the launcher's --check against
                // a flight's telemetry: T0 is MISSION_START, so every track
                // begins with the spool-up before the climb.
                Reporting.writeCsv
                    (Path.Combine(dir, "plan_tracks.csv"))
                    [ "mission"; "t_s"; "lat"; "lon"; "rel_alt_m" ]
                    [
                        for d, m in List.indexed swarm.Missions do
                            for t, p in (showTrack f d).Samples do
                                let g = MavlinkMission.localToGeo origin { North = p.Y; East = p.X; Down = 0.0 }

                                [
                                    m.Drone.Name
                                    sprintf "%.2f" (t + MavlinkMission.Flight.spoolUpS)
                                    $"%.7f{g.Latitude}"
                                    $"%.7f{g.Longitude}"
                                    $"%.2f{p.Z}"
                                ]
                    ]

                let showTop =
                    f.Points |> Array.collect id |> Array.map (fun p -> p.Z) |> Array.fold max 0.0

                // The loiter the read-back missions need: nobody may start its
                // descent before every drone has finished its RTL climb and transit.
                let neededLoiterS =
                    MAVLinkExport.rtlLoiterSeconds
                        cruise
                        (Array.map2
                            (fun (park: Ev.P3) (pts: Ev.P3[]) ->
                                Array.append
                                    [|
                                        { park with
                                            Z = MAVLinkExport.Autopilot.takeoffAltitudeM
                                        }
                                    |]
                                    pts
                                |> Array.map local)
                            f.Parking
                            f.Points)
                        (homes |> Array.map local)
                        (rtlOf |> Array.map (fun r -> r.AltM))

                let problems =
                    [
                        for d in 0 .. parms.Length - 1 do
                            match parms.[d] with
                            | None -> sprintf "%s.parm missing" (name d)
                            | Some p ->
                                for key, expected in MAVLinkExport.parameters cruise rtlOf.[d].AltM neededLoiterS do
                                    match Map.tryFind key p with
                                    | None -> sprintf "%s.parm: %s missing" (name d) key
                                    | Some v when abs (v - expected) > 1e-6 ->
                                        sprintf "%s.parm: %s = %g, the model assumes %g" (name d) key v expected
                                    | Some _ -> ()

                        let alts = rtlOf |> Array.map (fun r -> r.AltM) |> Array.sort

                        for lo, hi in Array.pairwise alts do
                            if hi - lo < Safety.minSwarmSeparationMeters then
                                sprintf
                                    "RTL altitudes %.0f m and %.0f m are less than %.0f m apart"
                                    lo
                                    hi
                                    Safety.minSwarmSeparationMeters

                        if alts.Length > 0 && alts.[0] < showTop + Safety.minSwarmSeparationMeters then
                            sprintf
                                "lowest RTL altitude %.0f m is not %.0f m above the show's top %.1f m"
                                alts.[0]
                                Safety.minSwarmSeparationMeters
                                showTop
                    ]

                let failsafe: Ev.Check =
                    {
                        Area = Ev.Contingency
                        Claim = "Each vehicle's exported parameters make its failsafes act as modelled here"
                        Method =
                            "every <DroneName>.parm read back from the export and compared with the values the model uses: link loss in AUTO continues the mission (FS_OPTIONS 11, so it flies the deconflicted show above), low battery only warns (the pilot runs the drop-out procedure), RTL at the drone's own RTL_ALT with RTL_CONE_SLOPE 0, RTL_LOIT_TIME long enough for every drone's climb and transit, RTL_SPEED and the climb/descent/land speeds; RTL altitudes checked apart and above the show"
                        Measured =
                            sprintf
                                "%d of %d parameter files read back; RTL_ALT %s m"
                                (parms |> Array.filter Option.isSome |> Array.length)
                                parms.Length
                                (rtlOf |> Array.map (fun r -> $"%.0f{r.AltM}") |> String.concat " / ")
                        Limit =
                            sprintf
                                "values as modelled; RTL altitudes >= %.0f m apart, above the show"
                                Safety.minSwarmSeparationMeters
                        Status = if problems.IsEmpty then Ev.Pass else Ev.Fail
                        Details = problems
                    }

                let homesApart =
                    [
                        for a in 0 .. homes.Length - 1 do
                            for b in a + 1 .. homes.Length - 1 do
                                Ev.dist3 homes.[a] homes.[b]
                    ]
                    |> List.fold min Double.PositiveInfinity

                (f,
                 Safety.minSwarmSeparationMeters,
                 Dyn.DroneProfile.standard,
                 sprintf
                     "Outdoor show: %d ArduPilot drones flying the exported AUTO missions (mavlink/*.plan) at scale %.2f, up to %.1f m/s, from %.5f, %.5f"
                     f.Points.Length
                     scale
                     cruise
                     origin.Latitude
                     origin.Longitude,
                 "the exported MAVLink mission items",
                 "the pilot station at the show origin",
                 ("All RTL at once (abort)",
                  "All drones commanded to RTL at once (show abort, or link lost outside AUTO) stay deconflicted",
                  sprintf
                      "each drone RTLs from where it is, at its own exported RTL_ALT, to its own declared home (its arming slot; homes %.1f m apart at the closest)"
                      homesApart),
                 failsafe,
                 [
                     "Geometry is that of the exported .plan/.waypoints missions flown in AUTO mode, read back item by item, with each drone's .parm file read back from disk."
                     sprintf
                         "The missions are time-synchronised: before each leg a DO_CHANGE_SPEED sets the drone's horizontal speed so every drone's leg takes the same time (the slowest leg at %.1f m/s, or its climb at WPNAV_SPEED_UP %.1f m/s / descent at WPNAV_SPEED_DN %.1f m/s); a leg too short to fly that slowly (%.1f m/s minimum) is flown at the minimum and the drone holds longer, so all leave each waypoint together after the %.0f s formation hold. Every leg starts and ends at rest and is flown as a jerk-limited S-curve with WPNAV_ACCEL %.1f m/s^2, WPNAV_ACCEL_Z %.1f m/s^2, WPNAV_JERK %.1f m/s^3 and PSC_JERK_Z %.1f m/s^3 (pinned in the .parm files), a symmetric approximation of ArduPilot's SCurve; drones on legs of different length accelerate differently, which the tracks include. A waypoint's hold is exported as whole seconds (ArduPilot keeps param1 as an integer) and its fraction as a NAV_DELAY (float seconds)."
                         cruise
                         MAVLinkExport.Autopilot.climbSpeedMs
                         MAVLinkExport.Autopilot.descentSpeedMs
                         MAVLinkExport.Autopilot.minLegSpeedMs
                         MAVLinkExport.Autopilot.formationHoldS
                         MAVLinkExport.Autopilot.accelMss
                         MAVLinkExport.Autopilot.accelZMss
                         MAVLinkExport.Autopilot.jerkMsss
                         MAVLinkExport.Autopilot.jerkZMsss
                     sprintf
                         "All missions are started together. Each drone is armed on its own ground slot, which the export declares as its home; NAV_TAKEOFF climbs vertically there to %.0f m (ArduPilot ignores its lat/lon). After the last formation each drone flies to above its own slot at its current height (NAV_LAND with a position) and lands: descent at WPNAV_SPEED_DN to LAND_ALT_LOW %.0f m, then LAND_SPEED %.1f m/s."
                         MAVLinkExport.Autopilot.takeoffAltitudeM
                         MAVLinkExport.Autopilot.landAltLowM
                         MAVLinkExport.Autopilot.landSpeedMs
                     "RTL (the abort for everyone at once, as the .parm files set it): climb vertically to max(current, the drone's RTL_ALT), straight to its home at that height, loiter RTL_LOIT_TIME (long enough that nobody descends before every drone has finished its climb and transit), descend and land. Staggered RTL_ALTs keep simultaneous returns at different heights; the RTL order puts a drone that is ever stacked under another below it. In AUTO a lost link does not RTL: FS_OPTIONS makes the drone continue its mission, which is the show checked above."
                     "mavlink_show.fsx (exported with the missions) sets and reads back every parameter from the .parm files, uploads and reads back every mission, checks each vehicle stands within 2 m of its declared home (its own slot), refuses to start on any difference, then arms all and starts all missions together. It has been dry-run, not flown: exercise it against ArduPilot SITL before any aircraft."
                     sprintf
                         "Separation limit %.0f m is Safety.minSwarmSeparationMeters (outdoor), checked exactly on the planned straight-line paths (closed-form closest approach between breakpoints); GPS and wind errors come on top. The automatic outdoor scale puts the closest slots and the closest synchronised transit at 1.2 x this limit."
                         Safety.minSwarmSeparationMeters
                     sprintf
                         "Endurance %.0f min (--endurance-min; DroneDomain has no figure for these aircraft) with the %.0f%% reserve."
                         enduranceMin
                         Battery.reserveBatteryPercent
                     "C2 is checked for the domain's generic radio at 2.4 GHz with a 10 dB fade margin, the pilot station standing at the show origin."
                     sprintf
                         "Drop-out procedure (the pilot's, on a low-battery warning or any fault short of a crash): the drone steps %.0f m north out of the formation plane at its height (every formation lies in the east-up plane) and lands there, on clear ground north of the show line; the others are re-assigned to each later formation by DynamicBehavior.SwarmAdaptation.adaptFormation (passed through the same transition safety gate as the show) and fly synchronised legs as the export would plan them. The exported missions cannot re-plan in flight: that needs the ground station to upload new missions. The result with the missions left unchanged is shown next to it. A lone RTL is not the drop-out procedure: it climbs straight up, through any drone stacked above."
                         stepOutM
                 ])
            | Indoor(show, layout, roomX, roomY) ->
                let slots =
                    layout.Formations
                    |> Array.map (fun lf -> lf.Positions |> Array.map (fun p -> p3 p.X p.Y p.Z))

                let layoutNames = layout.Formations |> Array.map (fun lf -> lf.Name)

                let ownSlotPoints =
                    if
                        layoutNames.Length >= 2
                        && layoutNames.[layoutNames.Length - 2].EndsWith "(overhead)"
                    then
                        2
                    else
                        0

                let f, duration = indoor show layoutNames slots ownSlotPoints

                (f,
                 IndoorLayout.limitM,
                 Dyn.DroneProfile.crazyflie,
                 sprintf
                     "Indoor show (collision-safety case; no airspace permission is needed indoors): %d Crazyflie 2.1 drones flying crazyflie_show.py's on-board trajectories in a %.1f x %.1f m room, %.1f s per transition"
                     f.Points.Length
                     roomX
                     roomY
                     duration,
                 "the Crazyflie show waypoints",
                 "the Crazyradio at the room centre",
                 ("Emergency stop, all land in place",
                  "Emergency stop (Ctrl+C) lands every drone without collision",
                  "the script sets EMERGENCY_STOP and lands every drone vertically where it is"),
                 // Replaced below by the measured lost-radio check.
                 Ev.Checks.notEvidenced Ev.Contingency "-" "-",
                 [
                     "Indoors there is no airspace, so no 1:N permission is needed; the checks are the collision-safety case a venue or operator needs. The verdict logic is the same as outdoors."
                     sprintf
                         "Layout: every airborne formation is rotated from the vertical plane into the horizontal one (seen from above) and flown at %.1f m; ground formations are flown at the %.1f m take-off height. Indoors the emergency action is to land in place, and a stacked formation would land drones on each other; the Vertical Line would also need about 15 m of height. Each formation is scaled to the smallest size that keeps its closest slots and every synchronised transit at %.1f x the limit (scales %s); each transition was re-checked by the safety gate in this layout."
                         IndoorLayout.showHeightM
                         IndoorLayout.hoverM
                         IndoorLayout.margin
                         (layout.Scales |> Array.map (sprintf "%.3f") |> String.concat " / ")
                     sprintf
                         "Separation: distance sqrt(dx^2 + dy^2 + (%.1f dz)^2) >= %.1f m, i.e. %.1f m beside or %.1f m straight below. A Crazyflie 2.1 is about 0.13 m across its propellers and each drone's position is good to about 0.1 m, so 0.33 m rounded up to 0.5 m; vertically, hover thrust 0.27 N through about 0.0064 m2 of rotor disc drives a downwash near 4 m/s that can upset a 27 g drone for several rotor diameters below, so vertical distance counts half. This replaces the generic 2 m planning default of CollisionAvoidance.PlanningConstraints, which no 4-drone show fits in a room; no external standard is claimed."
                         IndoorLayout.zWeight
                         IndoorLayout.limitM
                         IndoorLayout.limitM
                         (IndoorLayout.limitM / IndoorLayout.zWeight)
                     sprintf
                         "Geometry is that of crazyflie_show.py: high-level take-off to %.1f m in %.0f s and a %.1f s wait, then each drone's on-board trajectory (uploaded before take-off): a %.1f s move to every formation after the start one and a %.1f s pause, then land in %.0f s. Moves are 7th-order smooth steps with the same time profile for every drone, so straight synchronised lines have the same relative geometry; linear interpolation is used here."
                         cfTakeoffHeightM
                         cfTakeoffS
                         cfPauseS
                         duration
                         cfPauseS
                         cfLandingS
                     "Drones start on the ground at their start positions (the script's checklist), with the room centre at the origin. The script refuses to fly a waypoint outside MIN_HEIGHT-MAX_HEIGHT (0.2-2.0 m); nothing is clamped in flight."
                     "Lost radio: a started on-board trajectory runs without the radio, so the drone flies the rest of the show and then holds above its final slot (the land command cannot reach it); before the start it holds where the take-off left it. This relies on the Crazyflie high-level commander executing a started trajectory and holding its last setpoint without radio traffic: verify it on the firmware used."
                     sprintf
                         "Endurance %.0f min (--endurance-min; default is Bitcraze's stock-battery figure without decks, which positioning and LED decks reduce) with the %.0f%% reserve."
                         enduranceMin
                         Battery.reserveBatteryPercent
                     "C2 is checked for the domain's generic 2.4 GHz radio, not the Crazyradio PA; at room distances it is a formality."
                     sprintf
                         "Drop-out: the drone lands vertically where it is (the script's land()); the others hold where they are for %.1f s (DynamicBehavior's Hold) until it is down, then are re-assigned to each later formation by DynamicBehavior.SwarmAdaptation.adaptFormation through the same safety gate. The script cannot re-plan in flight (it would have to stop the trajectories and upload new ones). The result with the show left unchanged is shown next to it."
                         f.DepartureHoldS
                 ])

        let n = f.Points.Length
        let m = f.Names.Length
        let show = [ for d in 0 .. n - 1 -> showTrack f d ]

        // --- Deconfliction --------------------------------------------------
        let shared =
            [
                for j in 0 .. m - 1 do
                    for a in 0 .. n - 1 do
                        for b in a + 1 .. n - 1 do
                            let p = f.Points.[a].[j]

                            if Ev.dist3 p f.Points.[b].[j] < 1e-3 then
                                let slots = f.Slots.[j]

                                // Two slots can merge in the flown geometry (the
                                // Crazyflie height clamp); otherwise the optimiser
                                // handed one slot to two drones.
                                let merged =
                                    slots
                                    |> Array.filter (fun s -> Ev.dist3 s p < 1e-3)
                                    |> Array.length
                                    |> fun c -> c > 1

                                sprintf
                                    "%s: %s and %s both sent to (%.2f, %.2f, %.2f) m (%s)"
                                    f.Names.[j]
                                    (name a)
                                    (name b)
                                    p.X
                                    p.Y
                                    p.Z
                                    (if merged then
                                         "two formation slots coincide in the flown geometry"
                                     else
                                         "the optimiser's assignment gave this slot to both")
            ]

        let slotCheck: Ev.Check =
            {
                Area = Ev.Deconfliction
                Claim = "Every drone is sent to its own formation slot"
                Method = $"per formation, the waypoints of every pair of drones in %s{source} compared"
                Measured =
                    if shared.IsEmpty then
                        "no shared slots"
                    else
                        $"%d{shared.Length} drone pair(s) sent to the same slot"
                Limit = "no two drones share a slot"
                Status = if shared.IsEmpty then Ev.Pass else Ev.Fail
                Details = shared
            }

        // Can this show be flown separated at all in its height band? Every
        // formation scales together: the scale must spread the closest slots to
        // the limit and still keep the highest slot under the ceiling.
        let fitCheck: Ev.Check =
            match flown with
            | Indoor(_, layout, _, _) ->
                let need = IndoorLayout.limitM * IndoorLayout.margin

                {
                    Area = Ev.Deconfliction
                    Claim = "The room layout keeps every formation and every synchronised transit apart"
                    Method =
                        sprintf
                            "%s layout (airborne formations flat at %.1f m, ground formations at %.1f m), weighted distance of the closest slots of any formation and of the closest synchronised transit, after the safety gate"
                            (if layout.Automatic then "automatic" else "--scale")
                            IndoorLayout.showHeightM
                            IndoorLayout.hoverM
                    Measured =
                        sprintf
                            "closest slots %.2f, closest transit %.2f; scales %s"
                            layout.SlotMin
                            layout.TransitMin
                            (layout.Scales |> Array.map (sprintf "%.3f") |> String.concat " / ")
                    Limit = $">= %.2f{need} (%.1f{IndoorLayout.margin} x the %.1f{IndoorLayout.limitM} m limit)"
                    Status =
                        if min layout.SlotMin layout.TransitMin >= need - 1e-9 then
                            Ev.Pass
                        else
                            Ev.Fail
                    Details = []
                }
            | Outdoor(_, scale, _) ->
                let ceilingM, what = (Regulations.maxAltitudeAglMeters, "the altitude ceiling")

                let spacing = Geometry.minSlotSpacing formations

                let tallest =
                    formations
                    |> Array.maxBy (fun f -> f.Positions |> Array.map (fun p -> p.Z) |> Array.max)

                let top = tallest.Positions |> Array.map (fun p -> p.Z) |> Array.max
                let needed = limit / spacing
                let allowed = ceilingM / top

                {
                    Area = Ev.Deconfliction
                    Claim = "The formations can be flown at a scale that keeps the minimum separation under the ceiling"
                    Method =
                        sprintf
                            "closest slots of any formation (%.1f units) scaled to the limit vs. the highest slot (%s, %.0f units) scaled to %s"
                            spacing
                            tallest.Name
                            top
                            what
                    Measured =
                        sprintf
                            "separation needs scale >= %.3f; the %.1f m ceiling allows <= %.3f; flown at %.3f"
                            needed
                            ceilingM
                            allowed
                            scale
                    Limit = "a scale that satisfies both exists"
                    Status = if needed <= allowed then Ev.Pass else Ev.Fail
                    Details =
                        if needed <= allowed then
                            []
                        else
                            [
                                sprintf
                                    "no scale works: spreading the closest slots to %.1f m puts %s's top at %.1f m, and the ceiling is %.1f m. Fly it in a taller space (raise MAX_HEIGHT) or re-draw the formations."
                                    limit
                                    tallest.Name
                                    (top * needed)
                                    ceilingM
                            ]
                }

        let pairLines =
            [
                for a in 0 .. n - 1 do
                    for b in a + 1 .. n - 1 do
                        closest f.ZWeight [ show.[a]; show.[b] ]
            ]
            |> List.sortBy distanceOf
            |> List.map (fun c -> "pair " + describe c)

        let separation =
            let c =
                Ev.Checks.separation
                    limit
                    (sprintf
                        "per-drone tracks rebuilt from %s, from take-off to landing, exact closest approach of every pair of straight segments; pairs both below %.1f m AGL (parked) ignored"
                        source
                        groundZ)
                    (closest f.ZWeight show)

            { c with Details = pairLines }

        // --- Contingency ----------------------------------------------------
        let departures =
            [ 0 .. m - 1 ]
            |> List.filter (fun k -> f.Points |> Array.forall (fun ps -> ps.[k].Z >= groundZ))

        let drops =
            [
                for gone in 0 .. n - 1 do
                    let results =
                        departures |> List.map (fun k -> (k, dropOut backend shots profile f gone k))

                    // Neither export re-plans in flight: the MAVLink missions and
                    // the Crazyflie's on-board trajectories fly on unchanged unless
                    // the ground station uploads new ones. So a drop-out passes only
                    // if what flies anyway (show unchanged) AND the re-plan are clear;
                    // a re-plan QAOA could not find fails it.
                    // Per departure: the worse of the two, and which one it was.
                    let failedReplans =
                        results
                        |> List.choose (fun (k, r) ->
                            match r.Replanned with
                            | Error msg -> Some(k, msg)
                            | Ok _ -> None)

                    let worstOf =
                        results
                        |> List.map (fun (k, r) ->
                            match r.Replanned with
                            | Ok replanned when distanceOf r.Unchanged > distanceOf replanned ->
                                (k, "re-planned", replanned)
                            | _ -> (k, "show unchanged", r.Unchanged))

                    let worst = worstOf |> List.sortBy (fun (_, _, c) -> distanceOf c)

                    let ok =
                        results
                        |> List.forall (fun (_, r) ->
                            distanceOf r.Unchanged >= limit
                            && (match r.Replanned with
                                | Ok replanned -> distanceOf replanned >= limit
                                | Error _ -> false))

                    let check =
                        Ev.Checks.contingency
                            (sprintf "%s dropping out leaves the others deconflicted" (name gone))
                            (sprintf
                                "%s leaves by the drop-out procedure after its hold at each airborne formation (%s); the other %d either fly on unchanged (what the exports do) or are re-assigned by DynamicBehavior (a ground-station re-plan); both must stay clear; closest approach of all %d tracks from the moment it leaves, limit %.1f m"
                                (name gone)
                                (departures |> List.map (fun k -> f.Names.[k]) |> String.concat ", ")
                                (n - 1)
                                n
                                limit)
                            (match failedReplans, worst with
                             | (k, msg) :: _, _ -> sprintf "%s leaving at %s: %s" (name gone) f.Names.[k] msg
                             | [], (k, how, c) :: _ ->
                                 sprintf "%s leaving at %s (%s): closest %s" (name gone) f.Names.[k] how (describe c)
                             | [], [] -> sprintf "%s: no airborne formation to leave from" (name gone))
                            (if ok then Ev.Pass else Ev.Fail)
                            [
                                for k, r in results do
                                    sprintf
                                        "leaving at %s (t=%.1f s): show unchanged %s; %s"
                                        f.Names.[k]
                                        r.LeavesAt
                                        (describe r.Unchanged)
                                        (match r.Replanned with
                                         | Ok replanned ->
                                             sprintf
                                                 "re-planned %.2f m by %s"
                                                 (distanceOf replanned)
                                                 (String.Join("; ", r.Methods))
                                         | Error msg -> sprintf "no re-plan (%s)" msg)
                            ]

                    // Workload: the leaving drone is the pilot's (drop-out procedure);
                    // the rest re-assign themselves.
                    let scenarios =
                        results
                        |> List.map (fun (k, r) ->
                            let what = sprintf "%s drops out at %s" (name gone) f.Names.[k]

                            (what,
                             [
                                 {
                                     Ev.Event = what
                                     Ev.Affected = 1
                                     Ev.StartS = r.LeavesAt
                                     Ev.DurationS = Ev.decisionTimeS
                                     Ev.Handling = Ev.PilotDecision
                                     Ev.Response =
                                         "pilot runs the drop-out procedure (step out of the formation plane, land)"
                                 }
                                 {
                                     Ev.Event = "the others re-assign"
                                     Ev.Affected = n - 1
                                     Ev.StartS = r.LeavesAt
                                     Ev.DurationS = Ev.decisionTimeS
                                     Ev.Handling = Ev.Automatic
                                     Ev.Response = "DynamicBehavior re-assigns the remaining drones"
                                 }
                             ]))

                    (check, results |> List.collect (fun (_, r) -> r.Tracks), scenarios)
            ]

        let everyoneAtOnce =
            let label, claim, method = allAtOnceText
            let scenarios = allAtOnce f.ZWeight show f.Abort
            [ (allAtOnceCheck label claim method limit scenarios, scenarios) ]

        // --- Lost radio (indoor) ---------------------------------------------
        // A Crazyflie flies its uploaded trajectory on board. Lost after the
        // start, the radio changes nothing until the end: the drone holds above
        // its final slot instead of landing. Lost during take-off, it holds where
        // the take-off left it. A trigger at any moment after the start gives the
        // same track, so the two cases cover the show. The others fly on.
        let lostRadio, lostRadioTracks =
            match flown with
            | Outdoor _ -> (None, [])
            | Indoor _ ->
                let tStart = cfTakeoffS + cfPauseS

                let results =
                    [
                        for d in 0 .. n - 1 do
                            let others = show |> List.filter (fun tr -> tr.AircraftId <> name d)
                            let segments = f.Segments.[d]

                            let early = fly (name d) f.Parking.[d] segments.[0]

                            let late =
                                fly (name d) f.Parking.[d] (segments |> Array.take (segments.Length - 1) |> List.concat)

                            for what, t0, lost in
                                [
                                    (sprintf "%s loses the radio during take-off" (name d), 0.0, early)
                                    (sprintf "%s loses the radio after the start" (name d), tStart, late)
                                ] do
                                (what, closest f.ZWeight (after t0 (lost :: others)), lost)
                    ]

                let sorted = results |> List.sortBy (fun (_, c, _) -> distanceOf c)
                let ok = results |> List.forall (fun (_, c, _) -> distanceOf c >= limit)

                (Some(
                    Ev.Checks.contingency
                        "A Crazyflie that loses the radio does not come within the limit of the others"
                        "every drone in turn loses the radio: after the start it flies the rest of its on-board trajectory and holds above its final slot instead of landing; during take-off it holds where the take-off left it; the others fly the show; closest approach from the loss on"
                        (match sorted with
                         | (what, c, _) :: _ -> sprintf "worst: %s, closest %s" what (describe c)
                         | [] -> "no drones")
                        (if ok then Ev.Pass else Ev.Fail)
                        [ for what, c, _ in sorted -> sprintf "%s: %s" what (describe c) ]
                 ),
                 results |> List.map (fun (_, _, lost) -> lost))

        let failsafe = lostRadio |> Option.defaultValue failsafe

        let contingencyTracks =
            (drops |> List.collect (fun (_, tracks, _) -> tracks))
            @ (everyoneAtOnce
               |> List.collect (fun (_, scenarios) -> scenarios |> List.collect (fun (_, _, tracks) -> tracks)))
            @ lostRadioTracks

        // --- Endurance, altitude, C2 ----------------------------------------
        let endurance =
            show
            |> List.map (fun tr -> (tr.AircraftId, endTime tr / 60.0, usableMin))
            |> Ev.Checks.endurance
                "min"
                (sprintf
                    "flight time of each drone's track (take-off to landed, holds included) vs. %.0f min nominal endurance minus reserve"
                    enduranceMin)

        let perAircraft (what: string) (measure: Ev.P3 -> float) (tracks: Ev.Track list) =
            tracks
            |> List.map (fun tr -> (tr.AircraftId, tr.Samples |> Array.map (snd >> measure) |> Array.max))
            |> List.groupBy fst
            |> List.map (fun (id, xs) -> ($"%s{id} %s{what}", xs |> List.map snd |> List.max))

        // Outdoors the altitude ceiling; indoors the room (walls and ceiling):
        // tracks are straight lines, so their breakpoints bound them.
        let altitude =
            match flown with
            | Outdoor _ ->
                perAircraft "(show)" (fun p -> p.Z) show
                @ perAircraft "(contingencies)" (fun p -> p.Z) contingencyTracks
                |> Ev.Checks.altitude
            | Indoor(_, _, roomX, roomY) ->
                let wall = IndoorLayout.limitM
                let maxX, maxY = roomX / 2.0 - wall, roomY / 2.0 - wall

                let points =
                    (show @ contingencyTracks)
                    |> List.collect (fun tr ->
                        tr.Samples |> Array.map (fun (_, p) -> (tr.AircraftId, p)) |> List.ofArray)

                let outside =
                    points
                    |> List.filter (fun (_, p) ->
                        abs p.X > maxX + 1e-9 || abs p.Y > maxY + 1e-9 || p.Z > cfMaxHeightM + 1e-9)

                let farthest measure =
                    points |> List.map (snd >> measure) |> List.fold max 0.0

                {
                    Area = Ev.AltitudeCeiling
                    Claim =
                        "Every point of the show and its contingencies stays inside the room, clear of the walls and under MAX_HEIGHT"
                    Method =
                        sprintf
                            "every track breakpoint vs. the %.1f x %.1f m room (--room-x / --room-y, centred on the origin) less %.1f m from each wall, and crazyflie_show.py's %.1f m MAX_HEIGHT"
                            roomX
                            roomY
                            wall
                            cfMaxHeightM
                    Measured =
                        sprintf
                            "farthest %.2f m east-west and %.2f m north-south of centre, highest %.2f m"
                            (farthest (fun p -> abs p.X))
                            (farthest (fun p -> abs p.Y))
                            (farthest (fun p -> p.Z))
                    Limit = $"|x| <= %.2f{maxX} m, |y| <= %.2f{maxY} m, z <= %.1f{cfMaxHeightM} m"
                    Status = if outside.IsEmpty then Ev.Pass else Ev.Fail
                    Details =
                        outside
                        |> List.distinctBy fst
                        |> List.map (fun (id, p) -> $"%s{id} at (%.2f{p.X}, %.2f{p.Y}, %.2f{p.Z}) m")
                }

        let horizontalKm (p: Ev.P3) =
            Math.Sqrt(p.X * p.X + p.Y * p.Y) / 1000.0

        let c2 =
            perAircraft "(show and contingencies)" horizontalKm (show @ contingencyTracks)
            |> Ev.Checks.c2Link station c2BandMhz c2FadeMarginDb

        // --- Supervisor workload ---------------------------------------------
        let peak = Ev.peakAirborne stepS show

        let event what affected handling response =
            {
                Ev.Event = what
                Ev.Affected = affected
                Ev.StartS = 0.0
                Ev.DurationS = Ev.decisionTimeS
                Ev.Handling = handling
                Ev.Response = response
            }

        let everyone =
            let label, _, _ = allAtOnceText

            (label,
             [
                 event "abort called" 0 Ev.PilotDecision "the pilot calls the abort"
                 event label n Ev.Automatic "every drone flies its own abort track, deconflicted as checked above"
             ])

        let lostLink =
            match flown with
            | Outdoor _ ->
                ("one drone loses its C2 link",
                 [
                     event
                         "one drone loses its C2 link"
                         1
                         Ev.Automatic
                         "FS_OPTIONS: continues its mission, the deconflicted show"
                 ])
            | Indoor _ ->
                ("one drone loses the radio",
                 [
                     (match lostRadio with
                      | Some c when c.Status = Ev.Pass ->
                          event
                              "one drone loses the radio"
                              1
                              Ev.Automatic
                              "its on-board trajectory flies on; it holds above its final slot, clear of the others as checked above"
                      | _ ->
                          event
                              "one drone loses the radio"
                              1
                              Ev.PilotDecision
                              "the lost-radio check fails: the pilot must stop the show")
                 ])

        let workload =
            Ev.Checks.workload pilots ((drops |> List.collect (fun (_, _, s) -> s)) @ [ everyone; lostLink ])


        {
            Example = "SwarmChoreography"
            Operation = operation
            Pilots = pilots
            Aircraft = n
            PeakAirborne = peak
            Checks =
                [ slotCheck; fitCheck; separation; endurance; c2; altitude ]
                @ (drops |> List.map (fun (c, _, _) -> c))
                @ (everyoneAtOnce |> List.map fst)
                @ [ failsafe; Ev.Checks.pilotRatio pilots n peak; workload ]
            Assumptions = assumptions
        }

// =============================================================================
// METRICS
// =============================================================================

type Metrics =
    {
        run_id: string
        num_drones: int
        num_qubits: int
        num_formations: int
        solver: string // Always "Quantum (QAOA)" - RULE 1 compliant
        shots: int
        total_show_distance: float
        transitions:
            {|
                from_formation: string
                to_formation: string
                distance: float
                solver_used: string
            |}[]
        elapsed_ms: int64
    }

// =============================================================================
// MAIN PROGRAM
// =============================================================================

module Program =

    /// The backend --backend names: the local simulator (default), or the topological one
    /// with --anyons.
    let private createBackend (args: Cli.ParsedArgs) : IQuantumBackend =
        match (Cli.getOr "backend" "local" args).ToLower() with
        | "topological"
        | "topo" ->
            // Ising encoding: 16 qubits needs 2*(16+1) = 34 anyons
            let maxAnyons = Cli.getOr "anyons" "34" args |> int
            printfn "Backend: Topological (Ising, %d anyons)" maxAnyons
            TopologicalUnifiedBackendFactory.createIsing maxAnyons
        | _ ->
            printfn "Backend: LocalBackend (gate-based)"
            LocalBackend() :> IQuantumBackend

    /// --block: blocks of that many drones per circuit, when given.
    let private blockOption (args: Cli.ParsedArgs) : int option =
        match Cli.tryInt "block" args with
        | Ok block -> block
        | Error msg ->
            eprintfn "%s" msg
            exit 2

    /// --conflict: the weight of the path-conflict cost in a block search, as a multiple of
    /// the largest squared distance (TransitionSafety.conflictWeight). 0 unless given,
    /// leaving the conflicts to the gate and the CAPT objective: measured on 100 drones
    /// through 5 formations, three runs each, the gate passed 10 of 12 transitions at 0,
    /// 8 at 0.1, 7 at 0.3 and 6 at 1 and at 3, so the term is an experiment, not a default.
    let private conflictOption (args: Cli.ParsedArgs) : float =
        match Cli.tryFloat "conflict" args with
        | Ok(Some weight) when weight >= 0.0 -> weight
        | Ok(Some weight) ->
            eprintfn "--conflict: %g is below 0; the weight is 0 or more" weight
            exit 2
        | Ok None -> 0.0
        | Error msg ->
            eprintfn "%s" msg
            exit 2

    /// The scale run (--drones n, n other than 4): see ScaleShow. Exit code 1 when a
    /// transition found no accepted assignment.
    let private scaleMain (args: Cli.ParsedArgs) : int =
        let sw = Stopwatch.StartNew()
        let outDir = Cli.getOr "out" (Path.Combine("runs", "drone", "swarm-scale")) args
        let shots = Cli.getOr "shots" "2000" args |> int
        let droneCount = Cli.getOr "drones" "4" args |> int
        let lights = Cli.hasFlag "lights" args
        let formationCount =
            Cli.getOr
                "formations"
                (if lights then
                     string (ScaleShow.GlobeSteps + ScaleShow.MugSteps + ScaleShow.CubeSteps + 7)
                 else
                     "5")
                args
            |> int
        let sweeps = Cli.getOr "sweeps" "0" args |> int
        let conflict = conflictOption args

        printfn ""
        printfn "╔══════════════════════════════════════════════════╗"
        printfn "║  DRONE SWARM CHOREOGRAPHY: SCALE RUN             ║"
        printfn "║  FSharp.Azure.Quantum Example                    ║"
        printfn "╚══════════════════════════════════════════════════╝"
        printfn ""

        let backend = createBackend args

        let blockDrones =
            match blockOption args, BlockAssignment.widestBlock backend with
            | Some k, _ -> k
            | None, Some k when k >= 2 -> k
            | None, Some limit ->
                eprintfn "backend '%s' runs %d qubits, under the 4 a block of 2 drones needs" backend.Name limit
                exit 2
            | None, None ->
                eprintfn "backend '%s' reports no qubit limit: give --block <k>" backend.Name
                exit 2

        let formations =
            if lights then
                ScaleShow.litFormations droneCount formationCount
            else
                ScaleShow.formations droneCount formationCount

        printfn
            "%d drones, %d formations%s, blocks of %d drones (%d qubits a circuit), %s sweeps, %d shots, path conflicts weighted %g"
            droneCount
            formations.Length
            (if lights then " (a light show)" else "")
            blockDrones
            (blockDrones * blockDrones)
            (if sweeps > 0 then string sweeps else $"up to {Solver.BlockSweeps}")
            shots
            conflict

        // --home-lat/--home-lon: the site, the origin of the trajectories and of the
        // simulators; --mavlink (every drone's waypoint mission, zipped) needs it
        let home: MavlinkMission.GeoCoordinate option =
            match Cli.tryFloat "home-lat" args, Cli.tryFloat "home-lon" args with
            | Ok(Some lat), Ok(Some lon) ->
                Some
                    {
                        Latitude = lat
                        Longitude = lon
                        Altitude = Cli.getFloatOr "home-alt" 0.0 args
                    }
            | Error msg, _
            | _, Error msg ->
                eprintfn "%s" msg
                exit 2
            | _ when Cli.hasFlag "mavlink" args ->
                eprintfn "--mavlink needs --home-lat <deg> and --home-lon <deg>, the site the show is flown at"
                exit 2
            | _ -> None

        let mavlink = Cli.hasFlag "mavlink" args

        printfn
            "A scale run measures the block search, draws the show and writes every drone's trajectory%s."
            (if mavlink then
                 "; with --mavlink also every drone's waypoint mission"
             else
                 "")

        printfn ""

        let transitions, frames =
            ScaleShow.run backend shots blockDrones conflict sweeps formations
        sw.Stop()

        Data.ensureDirectory outDir

        LightShowPicture.write
            (Path.Combine(outDir, "show.svg"))
            (sprintf "%d drones, %d formations: the lights the drones have on" droneCount formations.Length)
            3.0
            frames

        // The show as flown, by drone: every formation's positions and lights
        Reporting.writeJson
            (Path.Combine(outDir, "show-frames.json"))
            (frames
             |> Array.map (fun f ->
                 {|
                     name = f.Name
                     flow = f.Flow
                     positions = f.Positions |> Array.map (fun (x, y, z) -> [| x; y; z |])
                     lights = f.Lights |> Array.map (fun (r, g, b) -> [| r; g; b |])
                 |}))

        // The show as a show controller flies it: every drone's timed keyframes, one zip
        let trajectories, showSeconds, closest, closestIn =
            ScaleTrajectories.write
                outDir
                (sprintf "%d drones, %d formations" droneCount formations.Length)
                home
                frames

        printfn ""

        printfn
            "Trajectories: %s (%d KB; %.0f s of show; closest approach %.2f m in %s)"
            trajectories
            (FileInfo(trajectories).Length / 1024L)
            showSeconds
            closest
            closestIn

        let missions =
            (if mavlink then home else None)
            |> Option.map (fun site ->
                let zip, scale, flownSpacing =
                    ScaleMissions.write
                        outDir
                        (sprintf "%d drones, %d formations" droneCount formations.Length)
                        site
                        frames

                printfn ""

                printfn
                    "Missions: %s (%d KB; positions × %.2f, closest approach as flown %.1f m)"
                    zip
                    (FileInfo(zip).Length / 1024L)
                    scale
                    flownSpacing

                zip)

        let solved = transitions |> Array.filter (fun t -> t.Solved) |> Array.length
        let startDistance = transitions |> Array.sumBy (fun t -> t.StartDistance)
        let distance = transitions |> Array.sumBy (fun t -> t.Distance)

        printfn ""
        printfn "╔══════════════════════════════════════════════════╗"
        printfn "║  SCALE RUN SUMMARY                               ║"
        printfn "╠══════════════════════════════════════════════════╣"
        printfn "║  Drones: %-40d║" droneCount
        printfn "║  Transitions solved: %d of %-23d║" solved transitions.Length
        printfn "║  Distance, start pairing: %-23s║" (sprintf "%.1f m" startDistance)
        printfn "║  Distance, block search:  %-23s║" (sprintf "%.1f m" distance)
        printfn "║  Elapsed: %-39s║" (sprintf "%d ms" sw.ElapsedMilliseconds)
        printfn "╚══════════════════════════════════════════════════╝"

        Reporting.writeJson
            (Path.Combine(outDir, "scale-metrics.json"))
            {|
                run_id = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss")
                num_drones = droneCount
                block_drones = blockDrones
                block_qubits = blockDrones * blockDrones
                shots = shots
                conflict_weight = conflict
                trajectories_zip = Path.GetFileName trajectories
                show_seconds = showSeconds
                closest_approach_m = closest
                missions_zip = (missions |> Option.map Path.GetFileName |> Option.defaultValue "")
                backend = backend.Name
                transitions_solved = solved
                start_pairing_distance = startDistance
                total_distance = distance
                elapsed_ms = sw.ElapsedMilliseconds
                transitions =
                    transitions
                    |> Array.map (fun t ->
                        {|
                            from_formation = t.From
                            to_formation = t.To
                            start_pairing_distance = t.StartDistance
                            distance = t.Distance
                            start_unsafe_pairs = t.StartUnsafePairs
                            fewest_unsafe_pairs = t.FewestUnsafePairs
                            solved = t.Solved
                            method = t.Method
                            elapsed_ms = t.ElapsedMs
                        |})
            |}

        if solved = transitions.Length then 0 else 1

    [<EntryPoint>]
    let main argv =
        let args = Cli.parse argv

        if Cli.hasFlag "help" args || Cli.hasFlag "h" args then
            printfn "╔══════════════════════════════════════════════════╗"
            printfn "║  DRONE SWARM CHOREOGRAPHY                        ║"
            printfn "║  Quantum Formation Optimization                  ║"
            printfn "╠══════════════════════════════════════════════════╣"
            printfn "║  4 drones × 4 positions = 16 qubits              ║"
            printfn "║  Fits within LocalBackend 20-qubit limit         ║"
            printfn "╠══════════════════════════════════════════════════╣"
            printfn "║  SHOW SEQUENCE:                                  ║"
            printfn "║    Ground → Diamond → Square → Vertical → Ground ║"
            printfn "╠══════════════════════════════════════════════════╣"
            printfn "║  OPTIONS:                                        ║"
            printfn "║    --out <dir>       Output directory            ║"
            printfn "║    --shots <n>       Number of measurements      ║"
            printfn "║    --export          Export Crazyflie Python     ║"
            printfn "║    --mavlink         Export MAVLink (ArduPilot)  ║"
            printfn "║    --scale <f>       Scale factor (default:      ║"
            printfn "║                      automatic, both exports)    ║"
            printfn "║    --duration <s>    Transition time (default 3) ║"
            printfn "║    --home-lat <deg>  Home latitude (MAVLink)     ║"
            printfn "║    --home-lon <deg>  Home longitude (MAVLink)    ║"
            printfn "║    --home-alt <m>    Home altitude (MAVLink)     ║"
            printfn "║    --backend <type>  local or topological        ║"
            printfn "║    --anyons <n>      Max anyons (topological)    ║"
            printfn "║    --pilots <n>      Pilots for 1:N (default 1)  ║"
            printfn "║    --endurance-min <m> Nominal endurance         ║"
            printfn "║                      (20 MAVLink, 7 Crazyflie)   ║"
            printfn "║    --room-x <m>      Room east-west (default 4)  ║"
            printfn "║    --room-y <m>      Room north-south (default 4)║"
            printfn "║    --block <k>       Assign in blocks of k drones║"
            printfn "║    --drones <n>      Scale run with n drones     ║"
            printfn "║    --formations <m>  Scale run formations (5, 91)║"
            printfn "║    --lights          Scale run as a light show   ║"
            printfn "║    --sweeps <s>      Scale run sweeps (up to 24) ║"
            printfn "║    --conflict <w>    Path-conflict weight (0)    ║"
            printfn "║    --help            Show this help              ║"
            printfn "╚══════════════════════════════════════════════════╝"
            printfn ""
            printfn "QUANTUM EXECUTION (RULE 1 COMPLIANT):"
            printfn "  All optimization uses QAOA via IQuantumBackend."
            printfn "  No classical fallback: a transition QAOA cannot solve fails the show."
            printfn "  Indoors, a transition failing the downwash-weighted gate is re-solved"
            printfn "  by QAOA on weighted distances, then at a larger scale, then fails."
            printfn ""
            printfn "EXPORT EXAMPLES:"
            printfn "  Crazyflie (indoor): dotnet run -- --export"
            printfn "  MAVLink (outdoor):  dotnet run -- --mavlink --home-lat 49.84 --home-lon 24.03"
            printfn ""
            printfn "  Crazyflie generates:"
            printfn "    - crazyflie_show.json  (waypoint data)"
            printfn "    - crazyflie_show.py    (executable script)"
            printfn ""
            printfn "  MAVLink generates:"
            printfn "    - DroneN_mission.plan  (QGroundControl plans)"
            printfn "    - DroneN.waypoints     (MAVLink waypoint files)"
            printfn "    - mavlink_show.fsx     (launcher: checks, flies and draws the show)"
            printfn ""
            printfn "  Every run writes permission-evidence.md/.json: 1:N permission"
            printfn "  evidence for the MAVLink missions (with --mavlink) or else the"
            printfn "  Crazyflie show."
            printfn ""
            printfn "MORE DRONES THAN ONE CIRCUIT HOLDS:"
            printfn "  A swarm whose n² qubits exceed the backend is assigned in blocks of"
            printfn "  ⌊√limit⌋ drones (BlockAssignment): --block <k> asks for blocks of k."
            printfn "  Scale run: dotnet run -- --drones 200 --formations 5 [--sweeps 12]"
            printfn "  Blocks lower squared distances plus a cost for every pair of paths"
            printfn "  inside the gate's bound: --conflict <w> weights it (0 unless given)."
            printfn "  (generated formations, every transition in blocks; no missions exported,"
            printfn "  show.svg animates the drones in the colours of their lights). With"
            printfn "  --lights the formations are a globe whose lands turn, a diamond, a star"
            printfn "  with its inner lights off, a turning mug, a doughnut and a turning cube:"
            printfn "  dotnet run -- --drones 100 --lights"
            printfn "  A scale run writes every drone's timed keyframes for a show controller,"
            printfn "  zipped as swarm-light-show-trajectories.zip (--home-lat/--home-lon put"
            printfn "  the origin on a site). With --mavlink and the site it also writes"
            printfn "  ArduPilot waypoint missions, which cannot fly legs of different lengths"
            printfn "  together: see the README before using them."
            0
        elif (Cli.getOr "drones" "4" args |> int) <> 4 then
            scaleMain args
        else
            let sw = Stopwatch.StartNew()

            let outDir = Cli.getOr "out" (Path.Combine("runs", "drone", "swarm")) args
            let shots = Cli.getOr "shots" "2000" args |> int
            let blockDrones = blockOption args
            let conflict = conflictOption args

            Data.ensureDirectory outDir
            let runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss")

            printfn ""
            printfn "╔══════════════════════════════════════════════════╗"
            printfn "║  DRONE SWARM CHOREOGRAPHY                        ║"
            printfn "║  FSharp.Azure.Quantum Example                    ║"
            printfn "╚══════════════════════════════════════════════════╝"
            printfn ""
            printfn "4-Drone Light Show (Quantum-Ready: 16 qubits)"
            printfn "Show Sequence: Ground → Diamond → Square → Vertical → Ground"
            printfn "Solver: Quantum QAOA (RULE 1 Compliant) | Shots: %d" shots
            printfn ""

            // Create quantum backend (RULE 1 COMPLIANT)
            let backend = createBackend args

            // Define show sequence
            let formations =
                [|
                    Formations.ground
                    Formations.diamond
                    Formations.square
                    Formations.vertical
                    Formations.ground // Return to base
                |]

            // Print initial formation
            printfn "INITIAL FORMATION:"
            printfn "%s" (Visualization.renderFormation Formations.ground)

            // Run transitions
            let transitions = ResizeArray<TransitionResult>()
            let mutable currentPositions = Formations.ground.Positions

            for i in 0 .. formations.Length - 2 do
                let fromFormation = formations.[i]
                let toFormation = formations.[i + 1]

                printfn "═══════════════════════════════════════════════════"
                printfn "TRANSITION %d: %s → %s" (i + 1) fromFormation.Name toFormation.Name
                printfn "═══════════════════════════════════════════════════"

                // Build distance matrix
                let distMatrix = Geometry.buildDistanceMatrix currentPositions toFormation

                // Print distance matrix
                printfn "Distance Matrix (meters):"
                printfn "         Pos0    Pos1    Pos2    Pos3"

                for d in 0..3 do
                    printf "Drone%d " d

                    for p in 0..3 do
                        printf "%7.1f " distMatrix.[d, p]

                    printfn ""

                printfn ""

                // Solve with QAOA (RULE 1: quantum solver via IQuantumBackend). Safety gate:
                // the drones fly this transition together in straight lines, so only a sampled
                // assignment keeping every pair at least the CAPT bound apart is taken. No
                // such sample, after the retries, fails the show: no classical solver stands in.
                let assignments, methodUsed =
                    printfn "Running QAOA with %d shots..." shots
                    let n = currentPositions.Length
                    let bound = TransitionSafety.bound currentPositions toFormation.Positions

                    let safe (candidate: Assignment[]) =
                        TransitionSafety.minSeparation currentPositions (TransitionSafety.ends toFormation candidate n)
                        >= bound - 1e-9

                    match Solver.solve backend shots blockDrones conflict safe currentPositions toFormation.Positions distMatrix with
                    | Ok(a, how) ->
                        printfn "  QAOA %s" how
                        (a, "Quantum (QAOA)")
                    | Error msg ->
                        eprintfn ""
                        eprintfn "TRANSITION %d FAILED: %s → %s" (i + 1) fromFormation.Name toFormation.Name
                        eprintfn "  %s (valid assignments at least %.2f apart in flight)." msg bound
                        eprintfn "  No show is exported: every transition must be solved by QAOA."
                        exit 1

                let totalDist = QapQubo.calculateTotalDistance distMatrix assignments

                let result =
                    {
                        FromFormation = fromFormation.Name
                        ToFormation = toFormation.Name
                        Assignments = assignments
                        TotalDistance = totalDist
                        Method = methodUsed
                    }

                transitions.Add result
                Visualization.printTransition result

                // Update current positions (ensure valid assignments)
                // Filter to get one assignment per drone (deduplicate if invalid)
                let validAssignments =
                    assignments
                    |> Array.groupBy (fun a -> a.DroneId)
                    |> Array.map (fun (droneId, assigns) ->
                        // If multiple assignments for same drone, take first one
                        assigns.[0])
                    |> Array.sortBy (fun a -> a.DroneId)

                // Ensure we have exactly 4 drones
                currentPositions <-
                    [|
                        for i in 0..3 do
                            match validAssignments |> Array.tryFind (fun a -> a.DroneId = i) with
                            | Some a -> yield toFormation.Positions.[a.TargetPositionIndex]
                            | None -> yield toFormation.Positions.[i] // Default: same position
                    |]

                // Print formation
                printfn ""
                printfn "%s" (Visualization.renderFormation toFormation)

            sw.Stop()

            // Summary
            let totalShowDistance = transitions |> Seq.sumBy (fun t -> t.TotalDistance)

            let quantumSolved =
                transitions |> Seq.filter (fun t -> t.Method.Contains "Quantum") |> Seq.length

            printfn ""
            printfn "╔══════════════════════════════════════════════════╗"
            printfn "║  SHOW SUMMARY                                    ║"
            printfn "╠══════════════════════════════════════════════════╣"
            printfn "║  Drones: 4 (16 qubits)                           ║"
            printfn "║  Transitions: %d                                  ║" transitions.Count
            printfn "║  Total Flight Distance: %8.2f meters          ║" totalShowDistance
            printfn "║  Elapsed Time: %d ms                             ║" sw.ElapsedMilliseconds
            printfn "╠══════════════════════════════════════════════════╣"
            printfn "║  RULE 1 COMPLIANT: Quantum solver via IBackend  ║"
            printfn "║  Quantum solved: %d of %d                          ║" quantumSolved transitions.Count
            printfn "╚══════════════════════════════════════════════════╝"

            // Write metrics
            let metrics: Metrics =
                {
                    run_id = runId
                    num_drones = 4
                    num_qubits = 16
                    num_formations = formations.Length
                    solver = "Quantum (QAOA)"
                    shots = shots
                    total_show_distance = totalShowDistance
                    transitions =
                        transitions.ToArray()
                        |> Array.map (fun t ->
                            {|
                                from_formation = t.FromFormation
                                to_formation = t.ToFormation
                                distance = t.TotalDistance
                                solver_used = t.Method
                            |})
                    elapsed_ms = sw.ElapsedMilliseconds
                }

            Reporting.writeJson (Path.Combine(outDir, "metrics.json")) metrics

            // Write report
            let transitionTable =
                transitions.ToArray()
                |> Array.mapi (fun i t ->
                    sprintf
                        "| %d | %s | %s | %.2f m | %s |"
                        (i + 1)
                        t.FromFormation
                        t.ToFormation
                        t.TotalDistance
                        t.Method)
                |> String.concat "\n"

            let report =
                $"""# Drone Swarm Choreography Results

## Summary

- **Run ID**: {runId}
- **Drones**: 4
- **QUBO Variables**: 16 (fits LocalBackend)
- **Formations**: Ground → Diamond → Square → Vertical → Ground
- **Solver**: Quantum QAOA (RULE 1 Compliant)
- **Shots**: {shots}
- **Total Flight Distance**: {totalShowDistance:F2} meters
- **Elapsed Time**: {sw.ElapsedMilliseconds} ms

## Transition Results

| # | From | To | Distance | Solver |
|---|------|----|----------|--------|
{transitionTable}

## RULE 1 Compliance

This example is **RULE 1 compliant**:
- All optimization uses QAOA via `IQuantumBackend`
- A transition with no valid, safe QAOA sample fails the run; no classical fallback
- No standalone classical solver exposed in public API

## Files Generated

- `metrics.json` - Performance metrics
- `permission-evidence.md` / `permission-evidence.json` - 1:N (one pilot, many drones) permission evidence for the flown geometry
"""

            Reporting.writeTextFile (Path.Combine(outDir, "run-report.md")) report

            // Scale: --scale wins for both exports. Otherwise the Crazyflie show
            // keeps its indoor 0.05, and the MAVLink show is scaled (below) so
            // nothing it flies brings two drones closer than 1.2 x the outdoor
            // minimum separation.
            let explicitScale = Cli.tryGet "scale" args |> Option.map float
            // (The indoor scale is chosen by IndoorLayout.plan below.)

            let outdoorScaleMargin = 1.2

            let transitionDuration = Cli.getOr "duration" "3.0" args |> float

            // Convert formations to anonymous record format for export
            let formationsForExport =
                formations
                |> Array.map (fun f ->
                    {|
                        Name = f.Name
                        Positions = f.Positions |> Array.map (fun p -> {| X = p.X; Y = p.Y; Z = p.Z |})
                    |})

            // Convert transitions to anonymous record format for export
            let transitionsForExport =
                transitions.ToArray()
                |> Array.map (fun t ->
                    {|
                        FromFormation = t.FromFormation
                        ToFormation = t.ToFormation
                        Assignments =
                            t.Assignments
                            |> Array.map (fun a ->
                                {|
                                    DroneId = a.DroneId
                                    TargetPositionIndex = a.TargetPositionIndex
                                |})
                        TotalDistance = t.TotalDistance
                        Method = t.Method
                    |})

            // Formations indexed by drone: Positions.[i] is where the optimiser
            // sent drone i. The MAVLink missions are built per drone from these;
            // the raw formations list slots in their own order, not drone order.
            let formationsFlown =
                MAVLinkExport.assignedFormations transitionsForExport formationsForExport

            // What the MAVLink show flies, in formation units: the slots of every
            // formation (they take off from the ground slots) and the synchronised
            // transitions into each airborne formation. Landing is not a straight
            // transition: each drone flies to its own slot at its own height first.
            let outdoorSpacing, transitSpacing =
                let positionsOf k =
                    formationsFlown.[k].Positions
                    |> Array.map (fun p -> { X = p.X; Y = p.Y; Z = p.Z }: Position3D)

                let transit =
                    [
                        for k in 0 .. formations.Length - 2 do
                            if formations.[k + 1].Positions |> Array.exists (fun p -> p.Z > 0.0) then
                                TransitionSafety.minSeparation (positionsOf k) (positionsOf (k + 1))
                    ]
                    |> List.fold min Double.PositiveInfinity

                (min (Geometry.minSlotSpacing formations) transit, transit)

            // MAVLink cruise (top horizontal) speed, m/s.
            let mavlinkCruiseMs = 3.0

            // The flown transits are not the optimiser's straight lines at
            // constant speed: every leg is an S-curve (acceleration and jerk
            // limits), and drones on legs of different lengths accelerate
            // differently. Grow the scale until the plan as flown (take-off and
            // every transition, S-curve timing) keeps the same 1.2 x margin.
            let outdoorScale, flownSpacing =
                let target = Safety.minSwarmSeparationMeters * outdoorScaleMargin

                let flownAt s =
                    MAVLinkExport.showSeparation s mavlinkCruiseMs formationsFlown formationsFlown.[0].Positions.Length

                match explicitScale with
                | Some s -> (s, flownAt s)
                | None ->
                    // No scale separates paths that meet: a closest approach of 0 ends it
                    let rec grow s round =
                        let flown = flownAt s

                        if flown >= target || round >= 8 || flown <= 1e-9 then
                            (s, flown)
                        else
                            grow (s * max 1.01 (target / flown)) (round + 1)

                    grow (Geometry.scaleFor Safety.minSwarmSeparationMeters outdoorScaleMargin outdoorSpacing) 0

            // The indoor (Crazyflie) show is laid out for the room: airborne
            // formations flat at the show height, each scaled (unless --scale)
            // to keep its slots and transits apart, every transition re-gated in
            // this layout. It is the geometry of --export and of the evidence pack
            // when there is no MAVLink export; a MAVLink-only run does not build it.
            let roomX = Cli.getFloatOr "room-x" 4.0 args
            let roomY = Cli.getFloatOr "room-y" 4.0 args

            // Indoor re-solve: QAOA on the downwash-weighted distances, taking only
            // an assignment that passes the safety gate in the weighted space.
            let resolveIndoor (starts: Position3D[]) (targets: Position3D[]) =
                let n = starts.Length

                let distances =
                    Array2D.init n n (fun i j -> Geometry.distance starts.[i] targets.[j])

                let bound = TransitionSafety.bound starts targets

                let target: Formation =
                    {
                        Name = "weighted target"
                        Positions = targets
                        Colors = None
                        Flow = false
                    }

                let safe (candidate: Assignment[]) =
                    TransitionSafety.minSeparation starts (TransitionSafety.ends target candidate n)
                    >= bound * (1.0 - 1e-6)

                printfn "Indoor downwash gate: re-solving a transition by QAOA on weighted distances..."

                Solver.solve backend shots blockDrones conflict safe starts targets distances
                |> Result.map (fun (a, how) ->
                    printfn "  QAOA %s" how
                    (a, how))

            let indoorPlan =
                lazy
                    (match IndoorLayout.plan resolveIndoor formations (transitions.ToArray()) explicitScale with
                     | Ok plan -> plan
                     | Error msg ->
                         eprintfn ""
                         eprintfn "INDOOR LAYOUT FAILED: %s" msg
                         eprintfn "  No indoor show is exported: every transition must have a QAOA assignment"
                         eprintfn "  that passes the indoor downwash-weighted safety gate."
                         exit 1)

            let show =
                lazy
                    (CrazyflieExport.fromTransitionResults
                        (indoorPlan.Value.Transitions
                         |> Array.map (fun t ->
                             {|
                                 FromFormation = t.FromFormation
                                 ToFormation = t.ToFormation
                                 Assignments =
                                     t.Assignments
                                     |> Array.map (fun a ->
                                         {|
                                             DroneId = a.DroneId
                                             TargetPositionIndex = a.TargetPositionIndex
                                         |})
                                 TotalDistance = t.TotalDistance
                                 Method = t.Method
                             |}))
                        (indoorPlan.Value.Formations
                         |> Array.map (fun f ->
                             {|
                                 Name = f.Name
                                 Positions = f.Positions |> Array.map (fun p -> {| X = p.X; Y = p.Y; Z = p.Z |})
                                 Colors = f.Colors
                             |}))
                        1.0 // already in metres
                        transitionDuration)
            // Export to Crazyflie Python if requested
            if Cli.hasFlag "export" args then
                printfn ""
                printfn "╔══════════════════════════════════════════════════╗"
                printfn "║  CRAZYFLIE EXPORT                                ║"
                printfn "╚══════════════════════════════════════════════════╝"

                // Write JSON waypoints
                let jsonPath = Path.Combine(outDir, "crazyflie_show.json")
                CrazyflieExport.writeJson jsonPath show.Value

                // Write Python script
                let pythonPath = Path.Combine(outDir, "crazyflie_show.py")
                CrazyflieExport.writePythonScript pythonPath show.Value

                printfn ""
                printfn "Export parameters:"

                printfn
                    "  Layout: %.1f x %.1f m room; airborne formations flat at %.1f m, ground formations at %.1f m; scales %s (%s)"
                    roomX
                    roomY
                    IndoorLayout.showHeightM
                    IndoorLayout.hoverM
                    (indoorPlan.Value.Scales |> Array.map (sprintf "%.3f") |> String.concat " / ")
                    (if indoorPlan.Value.Automatic then
                         "automatic"
                     else
                         "--scale")

                printfn "  Transition duration: %.1f seconds" transitionDuration
                printfn ""
                printfn "Generated files:"
                printfn "  %s" jsonPath
                printfn "  %s" pythonPath
                printfn ""
                printfn "To run with Crazyflie drones:"
                printfn "  1. Install cflib: pip install cflib"
                printfn "  2. Set up Lighthouse/Loco positioning"
                printfn "  3. Update DRONE_URIS in the Python script"
                printfn "  4. Run: python %s" pythonPath

            // Export to MAVLink if requested (can be combined with Crazyflie export)
            let mavlinkSwarm =
                if not (Cli.hasFlag "mavlink" args) then
                    None
                else
                    printfn ""
                    printfn "╔══════════════════════════════════════════════════╗"
                    printfn "║  MAVLINK EXPORT                                  ║"
                    printfn "╚══════════════════════════════════════════════════╝"

                    // Home position is REQUIRED for MAVLink export (GPS coordinates)
                    match Cli.tryGet "home-lat" args, Cli.tryGet "home-lon" args with
                    | None, _
                    | _, None ->
                        printfn ""
                        printfn "ERROR: --home-lat and --home-lon are required for MAVLink export."
                        printfn ""
                        printfn "These specify the GPS coordinates where drones will take off from."
                        printfn "Using incorrect coordinates is dangerous!"
                        printfn ""
                        printfn "Example:"
                        printfn "  dotnet run -- --mavlink --home-lat 49.8397 --home-lon 24.0297"
                        printfn ""
                        printfn "To find coordinates:"
                        printfn "  - Google Maps: Right-click location → coordinates shown"
                        printfn "  - GPS device: Read from drone's GPS when placed at takeoff point"
                        None
                    | Some latStr, Some lonStr ->

                        let homeLat = latStr |> float
                        let homeLon = lonStr |> float
                        let homeAlt = Cli.getOr "home-alt" "0.0" args |> float

                        let homePosition: MavlinkMission.GeoCoordinate =
                            {
                                Latitude = homeLat
                                Longitude = homeLon
                                Altitude = homeAlt
                            }

                        let mavlinkDir = Path.Combine(outDir, "mavlink")
                        let transitionSpeed = mavlinkCruiseMs
                        let numDrones = formationsForExport.[0].Positions.Length

                        printfn ""
                        printfn "Export parameters:"

                        printfn
                            "  Show origin: %.6f, %.6f (site MSL alt: %.1f m); each drone arms on its own ground slot around it"
                            homeLat
                            homeLon
                            homeAlt

                        printfn
                            "  Scale: %.2f (%s)"
                            outdoorScale
                            (match explicitScale with
                             | Some _ -> "--scale; positions = original × scale"
                             | None ->
                                 sprintf
                                     "automatic: closest slots %.1f m, closest in transit %.1f m (straight lines), %.1f m as flown (S-curve legs); at least %.1f × the %.0f m minimum separation"
                                     (outdoorScale * Geometry.minSlotSpacing formations)
                                     (outdoorScale * transitSpacing)
                                     flownSpacing
                                     outdoorScaleMargin
                                     Safety.minSwarmSeparationMeters)

                        printfn "  Cruise speed: %.1f m/s" transitionSpeed
                        printfn "  Drones: %d" numDrones
                        printfn ""

                        let swarm =
                            MAVLinkExport.exportShow
                                mavlinkDir
                                "Quantum Drone Choreography"
                                homePosition
                                outdoorScale
                                transitionSpeed
                                formationsFlown
                                numDrones
                                // RTL altitudes staggered by the same margin as the formations
                                (Safety.minSwarmSeparationMeters * outdoorScaleMargin)
                                "Quantum (QAOA)"

                        printfn ""
                        printfn "To use with ArduPilot/PX4 drones:"
                        printfn "  Option 1 - QGroundControl:"
                        printfn "    1. Open QGroundControl"
                        printfn "    2. Load DroneN_mission.plan files"
                        printfn "    3. Upload to each drone"
                        printfn ""
                        printfn "  Option 2 - the launcher (see examples/Drones/SITL.md):"
                        printfn "    1. Check without a network: dotnet fsi mavlink_show.fsx --dry-run"
                        printfn "    2. Fly: dotnet fsi mavlink_show.fsx"
                        printfn ""
                        printfn "  Lights: not in the missions (an ArduPilot mission has no light command);"
                        printfn "  the formation colours are in the Crazyflie export (--export)."
                        Some swarm

            // 1:N permission evidence, on exactly what was exported: the MAVLink
            // missions when there are any, otherwise the Crazyflie show.
            let flown =
                match mavlinkSwarm with
                | Some swarm -> Evidence.Outdoor(swarm, outdoorScale, Path.Combine(outDir, "mavlink"))
                | None -> Evidence.Indoor(show.Value, indoorPlan.Value, roomX, roomY)

            let evidence =
                Evidence.build
                    backend
                    shots
                    (max 1 (Cli.getIntOr "pilots" 1 args))
                    (Cli.getFloatOr "endurance-min" (Evidence.defaultEnduranceMin flown) args)
                    formations
                    flown

            FSharp.Azure.Quantum.Examples.Drones.PermissionEvidence.print evidence
            FSharp.Azure.Quantum.Examples.Drones.PermissionEvidence.write outDir evidence

            printfn ""
            printfn "Results written to: %s" outDir
            0
