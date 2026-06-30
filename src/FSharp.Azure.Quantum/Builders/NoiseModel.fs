namespace FSharp.Azure.Quantum

open FSharp.Azure.Quantum.CircuitBuilder

/// Device noise model and noise-aware compilation.
///
/// A `DeviceNoiseProfile` carries per-qubit and per-edge error rates. Two things
/// use it:
///   * `routeNoiseAware` — qubit routing that prefers low-error two-qubit links
///     (built on `QubitRouting.routeWith`), rather than merely fewest hops.
///   * `estimateSuccessProbability` — a first-order circuit fidelity estimate.
module NoiseModel =

    /// Per-qubit / per-edge error rates. Lookups fall back to the matching Default*
    /// when a specific qubit or coupling edge is not listed.
    type DeviceNoiseProfile =
        { SingleQubitError: Map<int, float>
          TwoQubitError: Map<int * int, float>
          ReadoutError: Map<int, float>
          DefaultSingleQubitError: float
          DefaultTwoQubitError: float
          DefaultReadoutError: float }

    let private norm (a, b) = if a <= b then (a, b) else (b, a)

    /// A uniform noise profile (the same error everywhere).
    let uniform (singleQubitError: float) (twoQubitError: float) (readoutError: float) : DeviceNoiseProfile =
        { SingleQubitError = Map.empty
          TwoQubitError = Map.empty
          ReadoutError = Map.empty
          DefaultSingleQubitError = singleQubitError
          DefaultTwoQubitError = twoQubitError
          DefaultReadoutError = readoutError }

    /// Build a profile from explicit per-qubit / per-edge maps plus fallback defaults.
    let create
        (singleQubit: Map<int, float>)
        (twoQubit: Map<int * int, float>)
        (readout: Map<int, float>)
        (defaults: float * float * float)
        : DeviceNoiseProfile =
        let ds, dt, dr = defaults
        { SingleQubitError = singleQubit
          TwoQubitError = twoQubit |> Map.toList |> List.map (fun (k, v) -> norm k, v) |> Map.ofList
          ReadoutError = readout
          DefaultSingleQubitError = ds
          DefaultTwoQubitError = dt
          DefaultReadoutError = dr }

    let singleQubitError (profile: DeviceNoiseProfile) (q: int) : float =
        profile.SingleQubitError |> Map.tryFind q |> Option.defaultValue profile.DefaultSingleQubitError

    let twoQubitError (profile: DeviceNoiseProfile) (a: int) (b: int) : float =
        profile.TwoQubitError |> Map.tryFind (norm (a, b)) |> Option.defaultValue profile.DefaultTwoQubitError

    let readoutError (profile: DeviceNoiseProfile) (q: int) : float =
        profile.ReadoutError |> Map.tryFind q |> Option.defaultValue profile.DefaultReadoutError

    /// Noise-aware routing: insert SWAPs through the links with the lowest two-qubit
    /// error, rather than simply the fewest hops. Returns the routed circuit and the
    /// final logical->physical mapping.
    let routeNoiseAware (cm: QubitRouting.CouplingMap) (noise: DeviceNoiseProfile) (circuit: Circuit) : Circuit * int[] =
        QubitRouting.routeWith (fun (a, b) -> twoQubitError noise a b) cm circuit

    /// First-order success-probability (fidelity) estimate: the product of
    /// per-operation fidelities (1 - error) over every single-qubit gate, two-qubit
    /// gate, and measurement. Ignores crosstalk and idling decoherence, so it is a
    /// comparative estimate (useful for ranking alternative compilations), not an
    /// absolute prediction.
    let estimateSuccessProbability (noise: DeviceNoiseProfile) (circuit: Circuit) : float =
        getGates circuit
        |> List.fold (fun acc gate ->
            match getAffectedQubits gate with
            | [ q ] ->
                match gate with
                | Measure _ -> acc * (1.0 - readoutError noise q)
                | _ -> acc * (1.0 - singleQubitError noise q)
            | [ a; b ] -> acc * (1.0 - twoQubitError noise a b)
            | qs when not (List.isEmpty qs) ->
                // Multi-qubit gate: approximate as a chain of two-qubit interactions.
                let pairs = List.length qs - 1
                acc * ((1.0 - noise.DefaultTwoQubitError) ** float pairs)
            | _ -> acc) 1.0
