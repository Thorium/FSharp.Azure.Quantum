/// A deliberately small fire model: enough dynamics to make the plan go stale.
///
/// Each sector has an intensity in [0, 1]. Burning sectors grow logistically,
/// faster in wind, and water knocks them down. Unburnt sectors collect heat
/// exposure from burning neighbours (mostly downwind) and ignite past a
/// threshold; water dropped on them first keeps them wet, which blocks the
/// exposure. That gives the planner two jobs that compete for the same drones:
/// attack the fire, or pre-wet what is about to burn.
///
/// Suppression needs concentration: water landing on a burning sector below
/// `MinSalvoLpm` is mostly wasted (evaporation, a fire that outruns it), so
/// the planner must mass drones on a few hotspots rather than spread them.
///
/// The physics constants are per minute, as fire behaviour is usually quoted;
/// every function takes the simulation tick and scales them, so the model
/// also runs at a one-second tick.
///
/// Scale: sectors are about a hectare, i.e. spot fires and mop-up hotspots,
/// the work drones are plausible for (night, smoke, helicopters grounded).
/// A drone swarm does not replace a bucket helicopter on a running front.
/// The numbers are illustrative, not calibrated fire science.
module FSharp.Azure.Quantum.Examples.Drones.FireAirBridge.FireSim

open System

open FSharp.Azure.Quantum.Examples.Drones.FireAirBridge.FireDomain
open FSharp.Azure.Quantum.Examples.Drones.FireAirBridge.AirBridge

type FirePhysics =
    {
        GrowthPerMin: float
        /// Water that takes one hectare from full intensity to out.
        KnockdownLPerHa: float
        /// Demand is sized to knock a sector down within this many minutes.
        KnockdownTargetMin: float
        /// Water that fully soaks one unburnt hectare.
        PrewetLPerHa: float
        WetDecayPerMin: float
        /// Heat exposure spread per intensity-minute with no wind.
        BaseSpread: float
        IgnitionExposure: float
        IgnitionIntensity: float
        ExtinguishedBelow: float
        /// Least L/min on a burning sector for drops to act together; below it
        /// small drops mostly evaporate on the way down.
        MinSalvoLpm: float
        /// Share of sub-floor water that still does any good.
        ThinEfficiency: float
    }

let defaultPhysics =
    {
        GrowthPerMin = 0.03
        KnockdownLPerHa = 1500.0
        KnockdownTargetMin = 8.0
        PrewetLPerHa = 600.0
        WetDecayPerMin = 0.03
        BaseSpread = 0.10
        IgnitionExposure = 3.0
        IgnitionIntensity = 0.15
        ExtinguishedBelow = 0.02
        MinSalvoLpm = 100.0
        ThinEfficiency = 0.2
    }

type SectorState =
    {
        Intensity: float
        Wetness: float
        Exposure: float
        EverIgnited: bool
    }

let initial (sectors: FireSector[]) : SectorState[] =
    sectors
    |> Array.map (fun s ->
        {
            Intensity = s.InitialIntensity
            Wetness = 0.0
            Exposure = 0.0
            EverIgnited = s.InitialIntensity > 0.0
        })

/// Minutes in one tick: the factor from the per-minute constants to per-tick.
let private perTick (tick: Tick) = Tick.seconds tick / 60.0

/// The salvo floor in litres per tick.
let floorPerTick (phys: FirePhysics) (tick: Tick) = phys.MinSalvoLpm * perTick tick

let private windGrowth (cond: Conditions) = 1.0 + cond.WindSpeedMs / 10.0

/// Heat flowing into sector j per minute from its burning neighbours.
let private exposureRatePerMin
    (phys: FirePhysics)
    (cond: Conditions)
    (sectors: FireSector[])
    (index: Map<string, int>)
    (state: SectorState[])
    (j: int)
    =
    let wind = Geometry.bearing cond.WindToDeg

    sectors.[j].Neighbors
    |> List.choose index.TryFind
    |> List.sumBy (fun n ->
        let downwind =
            max 0.0 (Geometry.dot wind (Geometry.unit sectors.[n].Pos sectors.[j].Pos))

        state.[n].Intensity * (phys.BaseSpread + downwind * cond.WindSpeedMs / 10.0))

/// Litres per minute a burning sector asks for.
let private burningDemandLpm (phys: FirePhysics) (cond: Conditions) (s: FireSector) (st: SectorState) =
    let growth =
        phys.GrowthPerMin * windGrowth cond * st.Intensity * (1.0 - st.Intensity)

    s.AreaHa * phys.KnockdownLPerHa * (growth + st.Intensity / phys.KnockdownTargetMin)

/// What each sector asks of the air bridge this tick.
let needs
    (phys: FirePhysics)
    (tick: Tick)
    (cond: Conditions)
    (sectors: FireSector[])
    (index: Map<string, int>)
    (state: SectorState[])
    : SectorNeed[] =
    let k = perTick tick

    sectors
    |> Array.mapi (fun j s ->
        let st = state.[j]

        if st.Intensity > 0.0 then
            {
                DemandPerTick = burningDemandLpm phys cond s st * k
                Weight = s.AssetPriority
                Concentrated = true
            }
        else
            let threat = exposureRatePerMin phys cond sectors index state j

            if threat < 0.01 then
                {
                    DemandPerTick = 0.0
                    Weight = 0.0
                    Concentrated = false
                }
            else
                // Pre-wetting is worth as much as the threat is real.
                {
                    DemandPerTick =
                        s.AreaHa
                        * phys.PrewetLPerHa
                        * ((1.0 - st.Wetness) / phys.KnockdownTargetMin + phys.WetDecayPerMin)
                        * k
                    Weight = s.AssetPriority * min 1.0 threat
                    Concentrated = false
                })

/// Advance the fire one tick given the water that landed in each sector.
let step
    (phys: FirePhysics)
    (tick: Tick)
    (cond: Conditions)
    (sectors: FireSector[])
    (index: Map<string, int>)
    (state: SectorState[])
    (deliveredL: float[])
    : SectorState[] =
    let k = perTick tick

    state
    |> Array.mapi (fun j st ->
        let s = sectors.[j]

        if st.Intensity > 0.0 then
            let growth =
                phys.GrowthPerMin * windGrowth cond * st.Intensity * (1.0 - st.Intensity) * k

            let demand = burningDemandLpm phys cond s st * k

            // Same floor the evaluator uses: min(salvo, demand).
            let effective =
                if deliveredL.[j] < min (floorPerTick phys tick) demand - 1e-9 then
                    deliveredL.[j] * phys.ThinEfficiency
                else
                    deliveredL.[j]

            let knock = effective / (s.AreaHa * phys.KnockdownLPerHa)
            let next = Math.Clamp(st.Intensity + growth - knock, 0.0, 1.0)

            if next < phys.ExtinguishedBelow then
                // Out, and soaked: it has to dry before it can catch again.
                { st with
                    Intensity = 0.0
                    Wetness = 1.0
                    Exposure = 0.0
                }
            else
                { st with Intensity = next }
        else
            let wet =
                min
                    1.0
                    (st.Wetness * (1.0 - phys.WetDecayPerMin * k)
                     + deliveredL.[j] / (s.AreaHa * phys.PrewetLPerHa))

            let exposure =
                st.Exposure
                + exposureRatePerMin phys cond sectors index state j * k * (1.0 - wet)

            if exposure >= phys.IgnitionExposure then
                {
                    Intensity = phys.IgnitionIntensity
                    Wetness = wet
                    Exposure = 0.0
                    EverIgnited = true
                }
            else
                { st with
                    Wetness = wet
                    Exposure = exposure
                })

/// An ember spot fire: the sector jumps to at least the given intensity.
let spotFire (state: SectorState[]) (j: int) (intensity: float) =
    state
    |> Array.mapi (fun i st ->
        if i = j then
            { st with
                Intensity = max st.Intensity intensity
                EverIgnited = true
            }
        else
            st)

/// Burning area weighted by intensity (ha).
let activeFireHa (sectors: FireSector[]) (state: SectorState[]) =
    Array.map2 (fun (s: FireSector) st -> s.AreaHa * st.Intensity) sectors state
    |> Array.sum
