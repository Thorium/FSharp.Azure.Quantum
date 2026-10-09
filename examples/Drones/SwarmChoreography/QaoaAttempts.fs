// Copyright (c) 2025 FSharp.Azure.Quantum Contributors
// SPDX-License-Identifier: MIT

/// Attempts of a fixed-angle QAOA run whose samples hold no acceptable solution: the same
/// angles with four times the shots, then two other angle sets at four times the shots. When
/// every attempt fails the caller gets an Error; no classical solver stands in.
module FSharp.Azure.Quantum.Examples.Drones.SwarmChoreography.QaoaAttempts

/// (shots, angles) per attempt, in order.
let schedule (shots: int) (angles: (float * float)[]) : (int * (float * float)[]) list =
    [
        shots, angles
        4 * shots, angles
        4 * shots, angles |> Array.map (fun (gamma, beta) -> (1.6 * gamma, 1.3 * beta))
        4 * shots, angles |> Array.map (fun (gamma, beta) -> (0.6 * gamma, 0.7 * beta))
    ]

/// The first attempt's accepted solution and a description of that attempt. `attempt`
/// runs the circuit and returns the accepted solution its samples hold, if any; an
/// execution Error stops at once.
let firstSolved
    (shots: int)
    (angles: (float * float)[])
    (attempt: int -> (float * float)[] -> Result<'T option, string>)
    : Result<'T * string, string> =
    let attempts = schedule shots angles

    let describe (s: int) (a: (float * float)[]) =
        a
        |> Array.map (fun (gamma, beta) -> sprintf "γ=%.3f β=%.3f" gamma beta)
        |> String.concat ", "
        |> sprintf "%d shots, %s" s

    let rec go (index: int) (remaining: (int * (float * float)[]) list) =
        match remaining with
        | [] ->
            Error(
                sprintf
                    "no QAOA sample gave an acceptable solution in %d attempts (%s)"
                    attempts.Length
                    (attempts |> List.map (fun (s, a) -> describe s a) |> String.concat "; ")
            )
        | (s, a) :: rest ->
            match attempt s a with
            | Error err -> Error err
            | Ok(Some solution) -> Ok(solution, sprintf "attempt %d of %d: %s" index attempts.Length (describe s a))
            | Ok None -> go (index + 1) rest

    go 1 attempts
