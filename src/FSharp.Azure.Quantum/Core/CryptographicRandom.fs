namespace FSharp.Azure.Quantum.Core

open System
open System.Numerics
open System.Security.Cryptography

/// A System.Random whose every draw comes from the operating system's cryptographically
/// secure generator.
///
/// It cannot be seeded, and later output cannot be predicted from earlier output, so it fits
/// secret material: key bits, basis choices, the positions a protocol reveals. A draw costs
/// roughly twenty times a System.Random draw; simulation, sampling and optimisation loops
/// keep System.Random, where a seed makes a run reproducible.
[<Sealed>]
type CryptographicRandom() =
    inherit Random()

    static let bits64 () : uint64 =
        BitConverter.ToUInt64(RandomNumberGenerator.GetBytes 8, 0)

    /// Uniform in [0, bound) for bound > 0: rejection over the smallest bit mask that covers
    /// the range, so no value is favoured.
    static let below (bound: uint64) : uint64 =
        let mask =
            UInt64.MaxValue >>> BitOperations.LeadingZeroCount((bound - 1UL) ||| 1UL)

        let mutable value = bits64 () &&& mask

        while value >= bound do
            value <- bits64 () &&& mask

        value

    /// A generator for a run: reproducible `Random(seed)` when a seed is given, a
    /// `CryptographicRandom` otherwise.
    static member UnlessSeeded(seed: int voption) : Random =
        match seed with
        | ValueSome s -> Random s
        | ValueNone -> CryptographicRandom()

    override _.Next() : int =
        RandomNumberGenerator.GetInt32 Int32.MaxValue

    override _.Next(maxValue: int) : int =
        if maxValue < 0 then
            raise (ArgumentOutOfRangeException(nameof maxValue, maxValue, "maxValue must not be negative"))
        elif maxValue = 0 then
            0
        else
            RandomNumberGenerator.GetInt32 maxValue

    override _.Next(minValue: int, maxValue: int) : int =
        if minValue > maxValue then
            raise (
                ArgumentOutOfRangeException(
                    nameof minValue,
                    minValue,
                    $"minValue must not exceed maxValue ({maxValue})"
                )
            )
        elif minValue = maxValue then
            minValue
        else
            RandomNumberGenerator.GetInt32(minValue, maxValue)

    override _.NextInt64() : int64 = int64 (below (uint64 Int64.MaxValue))

    override _.NextInt64(maxValue: int64) : int64 =
        if maxValue < 0L then
            raise (ArgumentOutOfRangeException(nameof maxValue, maxValue, "maxValue must not be negative"))
        elif maxValue = 0L then
            0L
        else
            int64 (below (uint64 maxValue))

    override _.NextInt64(minValue: int64, maxValue: int64) : int64 =
        if minValue > maxValue then
            raise (
                ArgumentOutOfRangeException(
                    nameof minValue,
                    minValue,
                    $"minValue must not exceed maxValue ({maxValue})"
                )
            )
        elif minValue = maxValue then
            minValue
        else
            // Unsigned arithmetic wraps, so the span and the sum are right even when
            // maxValue - minValue exceeds Int64.MaxValue.
            int64 (uint64 minValue + below (uint64 maxValue - uint64 minValue))

    /// Uniform in [0, 1) with 53 random bits.
    override _.NextDouble() : float =
        float (bits64 () >>> 11) * (1.0 / 9007199254740992.0)

    /// Uniform in [0, 1) with 24 random bits.
    override _.NextSingle() : float32 =
        float32 (bits64 () >>> 40) * (1.0f / 16777216.0f)

    override this.Sample() : float = this.NextDouble()

    override _.NextBytes(buffer: byte[]) : unit =
        ArgumentNullException.ThrowIfNull buffer
        RandomNumberGenerator.Fill(Span buffer)

    override _.NextBytes(buffer: Span<byte>) : unit = RandomNumberGenerator.Fill buffer
