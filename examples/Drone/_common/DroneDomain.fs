/// Drone Domain Constants and Reference Formulas
/// 
/// Domain-specific constants, thresholds, and formulas extracted from
/// "Drone Systems and Operations" by Richard Johnson.
/// 
/// This module provides standardized values for realistic drone simulation
/// and mission planning, covering:
/// - Regulatory constraints (FAA/EASA)
/// - Battery and energy models
/// - Communication parameters
/// - Safety thresholds and margins
/// - Environmental factors
/// - Swarm coordination parameters
/// 
/// REFERENCE: Johnson, R. "Drone Systems and Operations", Chapters 2-8
module FSharp.Azure.Quantum.Examples.Drone.Domain

open System

// =============================================================================
// REGULATORY CONSTANTS (FAA Part 107 / EASA)
// =============================================================================

/// FAA Part 107 and EASA regulatory limits
module Regulations =
    /// Maximum altitude AGL (Above Ground Level) for recreational/commercial drones (meters)
    /// FAA Part 107: 400 feet = 121.92 meters
    let maxAltitudeAglMeters = 121.92
    
    /// Maximum ground speed under FAA Part 107 (m/s)
    /// 100 mph = 44.7 m/s
    let maxGroundSpeedMs = 44.7
    
    /// Minimum visibility for VLOS operations (statute miles)
    let minVisibilityMiles = 3.0
    
    /// Minimum distance from clouds - horizontal (feet)
    let minCloudDistanceHorizontalFt = 2000.0
    
    /// Minimum distance from clouds - below (feet)
    let minCloudDistanceBelowFt = 500.0
    
    /// BVLOS (Beyond Visual Line of Sight) requires special waiver
    /// Typical approved BVLOS range limit (km)
    let typicalBvlosRangeLimitKm = 10.0
    
    /// Night operations require anti-collision lighting visible at (statute miles)
    let nightLightingVisibilityMiles = 3.0

// =============================================================================
// BATTERY AND ENERGY CONSTANTS
// =============================================================================

/// Battery characteristics and energy consumption models
/// Reference: Chapter 4 - Power Systems and Propulsion Technologies
module Battery =
    
    // --- LiPo Battery Characteristics ---
    
    /// Typical LiPo energy density range (Wh/kg)
    /// "Typical energy densities range from 150 to 200 Wh/kg"
    let lipoEnergyDensityMinWhPerKg = 150.0
    let lipoEnergyDensityMaxWhPerKg = 200.0
    let lipoEnergyDensityTypicalWhPerKg = 175.0
    
    /// Solid-state battery energy density (emerging tech) (Wh/kg)
    /// "Commercial prototypes claim energy densities exceeding 300 Wh/kg"
    let solidStateEnergyDensityWhPerKg = 300.0
    
    /// Optimal operating temperature range for lithium-based cells (°C)
    /// "typically 20–40°C for lithium-based cells"
    let optimalTempMinCelsius = 20.0
    let optimalTempMaxCelsius = 40.0
    
    /// Lithium metal anode theoretical capacity (mAh/g)
    /// "lithium metal anodes with higher theoretical capacity (3860 mAh/g)"
    let lithiumMetalTheoreticalCapacity = 3860.0
    
    // --- Safety Thresholds ---
    
    /// Critical low battery threshold - trigger return-to-home (%)
    let criticalBatteryPercent = 20.0
    
    /// Warning battery threshold (%)
    let warningBatteryPercent = 30.0
    
    /// Emergency landing threshold (%)
    let emergencyLandingPercent = 10.0
    
    /// Reserve battery for emergency maneuvers (%)
    let reserveBatteryPercent = 15.0
    
    // --- Discharge Characteristics ---
    
    /// Voltage sag factor at high current draw
    let highCurrentVoltageSagFactor = 0.85
    
    /// Capacity reduction in cold weather (per 10°C below optimal)
    let coldWeatherCapacityLossPercentPer10C = 10.0
    
    /// Typical C-rating for multirotor discharge
    let typicalDischargeRateC = 25.0
    
    // --- Energy Consumption Models ---
    
    /// Hover power consumption factor (W per kg of total weight)
    /// Approximate for typical quadcopter efficiency
    let hoverPowerWPerKg = 150.0
    
    /// Forward flight is more efficient than hover (factor)
    let forwardFlightEfficiencyFactor = 0.7
    
    /// Climb power increase factor (compared to hover)
    let climbPowerIncreaseFactor = 1.3
    
    /// Descent power reduction factor (compared to hover)
    let descentPowerReductionFactor = 0.5

/// Calculate estimated power consumption
let estimatePowerConsumption (totalMassKg: float) (speedMs: float) (climbRateMs: float) : float =
    let basePower = Battery.hoverPowerWPerKg * totalMassKg
    let forwardFactor = 
        if speedMs > 1.0 then Battery.forwardFlightEfficiencyFactor
        else 1.0
    let climbFactor =
        if climbRateMs > 0.5 then Battery.climbPowerIncreaseFactor
        elif climbRateMs < -0.5 then Battery.descentPowerReductionFactor
        else 1.0
    basePower * forwardFactor * climbFactor

/// Calculate remaining flight time (minutes)
let estimateRemainingFlightTime (batteryWh: float) (socPercent: float) (powerDrawW: float) : float =
    let usableEnergy = batteryWh * (socPercent - Battery.reserveBatteryPercent) / 100.0
    if powerDrawW > 0.0 then (usableEnergy / powerDrawW) * 60.0
    else 0.0

// =============================================================================
// PROPULSION AND AERODYNAMICS
// =============================================================================

/// Propulsion system constants
/// Reference: Chapter 4.3 - Propeller Aerodynamics and Mechanisms
module Propulsion =
    
    /// Maximum tip Mach number before compressibility effects
    /// "Maintaining tip Mach number below approximately 0.85"
    let maxTipMachNumber = 0.85
    
    /// Speed of sound at sea level (m/s)
    let speedOfSoundSeaLevelMs = 343.0
    
    /// Maximum safe tip speed (m/s)
    let maxTipSpeedMs = maxTipMachNumber * speedOfSoundSeaLevelMs  // ~291 m/s
    
    /// Typical propeller efficiency range
    let propellerEfficiencyMin = 0.60
    let propellerEfficiencyMax = 0.85
    let propellerEfficiencyTypical = 0.75
    
    /// BLDC motor efficiency typical range
    let motorEfficiencyMin = 0.80
    let motorEfficiencyMax = 0.92
    let motorEfficiencyTypical = 0.88

// =============================================================================
// COMMUNICATION PARAMETERS
// =============================================================================

/// RF Communication and telemetry parameters
/// Reference: Chapter 3 - Communication and Networking for UAVs
module Communication =
    
    // --- Frequency Bands ---
    
    /// 900 MHz band - longer range, better penetration
    let band900MhzFrequency = 900.0e6
    
    /// 2.4 GHz ISM band - common for consumer drones
    let band2400MhzFrequency = 2400.0e6
    
    /// 5.8 GHz ISM band - higher throughput, shorter range
    let band5800MhzFrequency = 5800.0e6
    
    // --- Link Budget Parameters ---
    
    /// Typical transmit power for consumer drone (dBm)
    let typicalTxPowerDbm = 20.0  // 100 mW
    
    /// Typical antenna gain (dBi)
    let typicalAntennaGainDbi = 3.0
    
    /// Typical receiver sensitivity (dBm)
    let typicalRxSensitivityDbm = -90.0
    
    /// Free-space path loss constant
    let freeSpacePathLossConstant = 32.44
    
    // --- MAVLink Protocol ---
    
    /// MAVLink default baud rate (bps)
    let mavlinkDefaultBaudRate = 57600
    
    /// MAVLink high-speed baud rate (bps)
    let mavlinkHighSpeedBaudRate = 115200
    
    /// MAVLink heartbeat interval (seconds)
    let mavlinkHeartbeatIntervalSec = 1.0
    
    /// MAVLink timeout for connection loss (seconds)
    let mavlinkTimeoutSec = 3.0
    
    // --- Latency Requirements ---
    
    /// Maximum acceptable control latency (ms)
    /// "ensure that control signals and telemetry feedback occur within milliseconds"
    let maxControlLatencyMs = 100.0
    
    /// Critical control latency requiring action (ms)
    let criticalControlLatencyMs = 250.0
    
    /// 5G target latency for BVLOS (ms)
    /// "ultra-low latency (as low as 1 ms)"
    let target5gLatencyMs = 1.0

/// Calculate theoretical maximum range using Friis equation (km)
/// Pr = Pt + Gt + Gr - Lf, where Lf = 20*log10(R) + 20*log10(f) + 32.44
let calculateTheoreticalRange 
    (txPowerDbm: float) 
    (txGainDbi: float) 
    (rxGainDbi: float) 
    (rxSensitivityDbm: float) 
    (frequencyMhz: float) : float =
    
    let linkBudget = txPowerDbm + txGainDbi + rxGainDbi - rxSensitivityDbm
    let freqTerm = 20.0 * Math.Log10(frequencyMhz)
    let rangeExponent = (linkBudget - Communication.freeSpacePathLossConstant - freqTerm) / 20.0
    Math.Pow(10.0, rangeExponent)  // Returns km

// =============================================================================
// SAFETY AND SEPARATION
// =============================================================================

/// Safety distances and collision avoidance parameters
/// Reference: Chapter 6.5 - Sense-and-Avoid in Dense Environments
module Safety =
    
    // --- Separation Distances ---
    
    /// Minimum horizontal separation between drones in swarm (meters)
    let minSwarmSeparationMeters = 5.0
    
    /// Safe following distance for formation flight (meters)
    let formationFollowingDistanceMeters = 10.0
    
    /// Collision avoidance trigger distance (meters)
    let collisionAvoidanceTriggerMeters = 30.0
    
    /// Emergency avoidance distance (meters)
    let emergencyAvoidanceMeters = 15.0
    
    // --- Geofencing ---
    
    /// Default geofence buffer from no-fly zones (meters)
    let geofenceBufferMeters = 50.0
    
    /// Airport proximity restriction radius (km)
    /// FAA requires notification within 5 miles
    let airportRestrictionRadiusKm = 8.0  // ~5 miles
    
    // --- Return-to-Home Triggers ---
    
    /// Signal loss duration before RTH (seconds)
    let signalLossRthTriggerSec = 5.0
    
    /// GPS loss duration before emergency landing (seconds)
    let gpsLossEmergencyTriggerSec = 10.0
    
    /// IMU failure response - immediate controlled descent
    let imuFailureDescentRateMs = 2.0
    
    // --- Wind Limits ---
    
    /// Maximum safe operating wind speed (m/s)
    /// Approximately 25 mph for typical consumer drones
    let maxOperatingWindSpeedMs = 11.0
    
    /// Wind gust tolerance above steady wind (m/s)
    let windGustToleranceMs = 5.0
    
    /// Wind speed requiring mission abort (m/s)
    let missionAbortWindSpeedMs = 15.0

// =============================================================================
// ENVIRONMENTAL FACTORS
// =============================================================================

/// Environmental and atmospheric parameters
/// Reference: Chapter 4.6 - Power Consumption Estimation
module Environment =
    
    /// Air density at sea level (kg/m³)
    let airDensitySeaLevel = 1.225
    
    /// Air density reduction per 1000m altitude (approximate)
    let airDensityReductionPer1000m = 0.12
    
    /// Standard temperature lapse rate (°C per 1000m)
    let temperatureLapseRatePer1000m = 6.5
    
    /// Sea level standard temperature (°C)
    let seaLevelStandardTempC = 15.0
    
    /// Earth radius for distance calculations (km)
    let earthRadiusKm = 6371.0
    
    /// Gravity acceleration (m/s²)
    let gravityMs2 = 9.81

/// Calculate air density at altitude
let airDensityAtAltitude (altitudeMeters: float) : float =
    let altitudeKm = altitudeMeters / 1000.0
    Environment.airDensitySeaLevel * (1.0 - Environment.airDensityReductionPer1000m * altitudeKm)

/// Calculate temperature at altitude (standard atmosphere)
let temperatureAtAltitude (altitudeMeters: float) (groundTempC: float) : float =
    let altitudeKm = altitudeMeters / 1000.0
    groundTempC - (Environment.temperatureLapseRatePer1000m * altitudeKm)

// =============================================================================
// SWARM COORDINATION
// =============================================================================

/// Swarm robotics and multi-agent parameters
/// Reference: Chapter 6.3 - Swarm Robotics and Multi-Agent Coordination
module Swarm =
    
    /// Consensus algorithm convergence coefficient (ε)
    /// "where ε is a small positive coefficient controlling the convergence rate"
    let consensusConvergenceEpsilon = 0.1
    
    /// Formation control gain parameter (α)
    let formationControlGain = 0.5
    
    /// Maximum swarm size for decentralized control (before hierarchical needed)
    let maxDecentralizedSwarmSize = 50
    
    /// Communication update rate for swarm coordination (Hz)
    let swarmCoordinationUpdateRateHz = 10.0
    
    /// Neighbor discovery timeout (seconds)
    let neighborDiscoveryTimeoutSec = 2.0
    
    /// Maximum communication hops in mesh network
    let maxMeshHops = 5

// =============================================================================
// TASK SCHEDULING PARAMETERS
// =============================================================================

/// Task allocation and scheduling parameters
/// Reference: Chapter 8 - Operations Management
module Scheduling =
    
    /// Takeoff/landing time overhead (minutes)
    let takeoffLandingOverheadMin = 2.0
    
    /// Pre-flight check duration (minutes)
    let preflightCheckDurationMin = 5.0
    
    /// Battery swap time (minutes)
    let batterySwapTimeMin = 3.0
    
    /// Fast charging time to 80% (minutes, typical)
    let fastChargeTo80PercentMin = 45.0
    
    /// Full charge time (minutes, typical)
    let fullChargeTimeMin = 90.0
    
    /// Minimum ground time between flights (minutes)
    let minGroundTimeMin = 10.0
    
    /// Default task priority levels
    let priorityEmergency = 1
    let priorityHigh = 2
    let priorityMedium = 3
    let priorityLow = 4
    
    /// Emergency response preemption allowed
    let emergencyPreemptionEnabled = true

// =============================================================================
// SENSOR PARAMETERS
// =============================================================================

/// Sensor specifications and fusion parameters
/// Reference: Chapter 2.2 - Sensor Fusion and State Estimation
module Sensors =
    
    /// GPS update rate (Hz)
    let gpsUpdateRateHz = 10.0
    
    /// IMU update rate (Hz)
    let imuUpdateRateHz = 200.0
    
    /// Barometer update rate (Hz)
    let barometerUpdateRateHz = 50.0
    
    /// GPS horizontal accuracy (meters, typical)
    let gpsHorizontalAccuracyMeters = 2.5
    
    /// GPS vertical accuracy (meters, typical)
    let gpsVerticalAccuracyMeters = 5.0
    
    /// Barometer altitude accuracy (meters)
    let barometerAccuracyMeters = 0.5
    
    /// IMU gyroscope drift rate (degrees/hour, typical MEMS)
    let gyroscopeDriftRateDegreesPerHour = 10.0
    
    /// Magnetometer calibration validity duration (hours)
    let magnetometerCalibrationValidityHours = 24.0

// =============================================================================
// PID CONTROLLER TUNING GUIDELINES
// =============================================================================

/// Flight controller tuning parameters
/// Reference: Chapter 2.3 - Flight Control Algorithms
module FlightControl =
    
    /// Typical attitude control loop rate (Hz)
    let attitudeControlRateHz = 500.0
    
    /// Typical position control loop rate (Hz)
    let positionControlRateHz = 50.0
    
    /// Roll/Pitch PID typical values (multirotor)
    let rollPitchKpTypical = 4.5
    let rollPitchKiTypical = 0.05
    let rollPitchKdTypical = 0.15
    
    /// Yaw PID typical values
    let yawKpTypical = 3.0
    let yawKiTypical = 0.03
    let yawKdTypical = 0.0
    
    /// Altitude PID typical values
    let altitudeKpTypical = 2.0
    let altitudeKiTypical = 0.5
    let altitudeKdTypical = 0.2
    
    /// Position hold PID typical values
    let positionKpTypical = 1.0
    let positionKiTypical = 0.1
    let positionKdTypical = 0.3

// =============================================================================
// UTILITY TYPES
// =============================================================================

/// Drone operational state
type DroneState =
    | Idle
    | Preflight
    | TakingOff
    | Hovering
    | Flying
    | Landing
    | Charging
    | Emergency
    | Maintenance

/// Battery health status
type BatteryStatus =
    | Healthy
    | Warning
    | Critical
    | Emergency

/// Determine battery status from state of charge
let getBatteryStatus (socPercent: float) : BatteryStatus =
    if socPercent <= Battery.emergencyLandingPercent then Emergency
    elif socPercent <= Battery.criticalBatteryPercent then Critical
    elif socPercent <= Battery.warningBatteryPercent then Warning
    else Healthy

/// Communication link quality
type LinkQuality =
    | Excellent  // > -60 dBm
    | Good       // -60 to -70 dBm
    | Fair       // -70 to -80 dBm
    | Poor       // -80 to -90 dBm
    | Lost       // < -90 dBm

/// Determine link quality from RSSI
let getLinkQuality (rssiDbm: float) : LinkQuality =
    if rssiDbm > -60.0 then Excellent
    elif rssiDbm > -70.0 then Good
    elif rssiDbm > -80.0 then Fair
    elif rssiDbm > -90.0 then Poor
    else Lost

/// Wind condition assessment
type WindCondition =
    | Calm           // < 3 m/s
    | Light          // 3-7 m/s
    | Moderate       // 7-11 m/s
    | Strong         // 11-15 m/s (at operating limit)
    | Dangerous      // > 15 m/s (mission abort)

/// Assess wind condition
let getWindCondition (windSpeedMs: float) : WindCondition =
    if windSpeedMs < 3.0 then Calm
    elif windSpeedMs < 7.0 then Light
    elif windSpeedMs <= Safety.maxOperatingWindSpeedMs then Moderate
    elif windSpeedMs <= Safety.missionAbortWindSpeedMs then Strong
    else Dangerous

// =============================================================================
// VALIDATION HELPERS
// =============================================================================

/// Validate mission parameters against safety constraints
let validateMissionParameters 
    (altitudeMeters: float) 
    (speedMs: float) 
    (windSpeedMs: float) 
    (batteryPercent: float) : Result<unit, string list> =
    
    let errors = [
        if altitudeMeters > Regulations.maxAltitudeAglMeters then
            sprintf "Altitude %.1fm exceeds max %.1fm AGL" altitudeMeters Regulations.maxAltitudeAglMeters
        if speedMs > Regulations.maxGroundSpeedMs then
            sprintf "Speed %.1fm/s exceeds max %.1fm/s" speedMs Regulations.maxGroundSpeedMs
        if windSpeedMs > Safety.maxOperatingWindSpeedMs then
            sprintf "Wind %.1fm/s exceeds safe limit %.1fm/s" windSpeedMs Safety.maxOperatingWindSpeedMs
        if batteryPercent < Battery.criticalBatteryPercent then
            sprintf "Battery %.0f%% below critical threshold %.0f%%" batteryPercent Battery.criticalBatteryPercent
    ]
    
    if errors.IsEmpty then Ok ()
    else Error errors

/// Calculate safe operating envelope based on current conditions
let calculateOperatingEnvelope 
    (batteryPercent: float) 
    (windSpeedMs: float) 
    (temperatureC: float) : {| MaxRange: float; MaxAltitude: float; MaxFlightTime: float |} =
    
    // Reduce range based on wind
    let windFactor = max 0.5 (1.0 - (windSpeedMs / Safety.missionAbortWindSpeedMs) * 0.5)
    
    // Reduce capacity in cold weather
    let tempFactor = 
        if temperatureC < Battery.optimalTempMinCelsius then
            let tempDelta = Battery.optimalTempMinCelsius - temperatureC
            max 0.7 (1.0 - (tempDelta / 10.0) * (Battery.coldWeatherCapacityLossPercentPer10C / 100.0))
        else 1.0
    
    // Effective battery
    let effectiveBattery = (batteryPercent - Battery.reserveBatteryPercent) / 100.0 * windFactor * tempFactor
    
    {|
        MaxRange = 20.0 * effectiveBattery  // Assuming 20km nominal max range
        MaxAltitude = min Regulations.maxAltitudeAglMeters (effectiveBattery * 150.0)
        MaxFlightTime = 30.0 * effectiveBattery  // Assuming 30min nominal max time
    |}
