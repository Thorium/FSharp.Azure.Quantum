namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Algorithms

/// CryptographicRandom honours the System.Random contract for every overload, and the key
/// distribution protocols take it whenever no seed is given.
module CryptographicRandomTests =

    [<Fact>]
    let ``integer draws stay inside the range System.Random documents`` () =
        let rng = CryptographicRandom() :> Random

        for _ in 1..2000 do
            Assert.InRange(rng.Next(), 0, Int32.MaxValue - 1)
            Assert.InRange(rng.Next 7, 0, 6)
            Assert.InRange(rng.Next(-3, 4), -3, 3)
            Assert.InRange(rng.NextInt64(), 0L, Int64.MaxValue - 1L)
            Assert.InRange(rng.NextInt64 10_000_000_000L, 0L, 9_999_999_999L)
            Assert.InRange(rng.NextInt64(-5L, 5L), -5L, 4L)

        // A span wider than Int64.MaxValue.
        for _ in 1..200 do
            Assert.InRange(rng.NextInt64(Int64.MinValue, Int64.MaxValue), Int64.MinValue, Int64.MaxValue - 1L)

        Assert.Equal(0, rng.Next 0)
        Assert.Equal(1, rng.Next 1 + 1)
        Assert.Equal(5, rng.Next(5, 5))
        Assert.Equal(0L, rng.NextInt64 0L)
        Assert.Equal(-9L, rng.NextInt64(-9L, -9L))

    [<Fact>]
    let ``every virtual member of System.Random is overridden`` () =
        // A member left to the base class would draw from its own generator, not the OS one.
        // Random's helpers (Shuffle, GetItems) draw through these virtual members.
        let flags =
            Reflection.BindingFlags.Instance
            ||| Reflection.BindingFlags.Public
            ||| Reflection.BindingFlags.NonPublic

        let inherited =
            typeof<Random>.GetMethods flags
            |> Array.filter (fun m -> m.IsVirtual && m.DeclaringType = typeof<Random>)
            |> Array.filter (fun m ->
                let parameters = m.GetParameters() |> Array.map (fun p -> p.ParameterType)

                typeof<CryptographicRandom>.GetMethod(m.Name, flags, null, parameters, null).DeclaringType
                <> typeof<CryptographicRandom>)
            |> Array.map string

        Assert.Empty inherited

    [<Fact>]
    let ``the widest integer range yields both signs`` () =
        let rng = CryptographicRandom() :> Random
        let ints = Array.init 400 (fun _ -> rng.Next(Int32.MinValue, Int32.MaxValue))
        let longs = Array.init 400 (fun _ -> rng.NextInt64(Int64.MinValue, Int64.MaxValue))

        // 400 fair signs: one sign missing has probability 2^-399.
        Assert.Contains(ints, fun x -> x < 0)
        Assert.Contains(ints, fun x -> x >= 0)
        Assert.Contains(longs, fun x -> x < 0L)
        Assert.Contains(longs, fun x -> x >= 0L)

    [<Fact>]
    let ``an invalid range is refused as System.Random refuses it`` () =
        let rng = CryptographicRandom() :> Random

        Assert.Throws<ArgumentOutOfRangeException>(fun () -> rng.Next -1 |> ignore) |> ignore
        Assert.Throws<ArgumentOutOfRangeException>(fun () -> rng.Next(4, 3) |> ignore) |> ignore
        Assert.Throws<ArgumentOutOfRangeException>(fun () -> rng.NextInt64 -1L |> ignore) |> ignore
        Assert.Throws<ArgumentOutOfRangeException>(fun () -> rng.NextInt64(4L, 3L) |> ignore) |> ignore
        Assert.Throws<ArgumentNullException>(fun () -> rng.NextBytes(null: byte[])) |> ignore

    [<Fact>]
    let ``fractions are in the half-open unit interval`` () =
        let rng = CryptographicRandom() :> Random

        for _ in 1..5000 do
            let d = rng.NextDouble()
            Assert.True(d >= 0.0 && d < 1.0, $"NextDouble gave {d}")
            let s = rng.NextSingle()
            Assert.True(s >= 0.0f && s < 1.0f, $"NextSingle gave {s}")

    [<Fact>]
    let ``every value of a small range turns up about equally often`` () =
        let rng = CryptographicRandom() :> Random
        let draws = 40_000
        let counts = Array.zeroCreate<int> 4

        for _ in 1..draws do
            let k = rng.Next 4
            counts.[k] <- counts.[k] + 1

        // Expected 10000 each, standard deviation 87: the bounds are eight deviations out.
        for count in counts do
            Assert.InRange(count, 9300, 10700)

        // The same through NextDouble, which the samplers use.
        let below = Seq.init draws (fun _ -> rng.NextDouble()) |> Seq.filter (fun d -> d < 0.25) |> Seq.length
        Assert.InRange(below, 9300, 10700)

    [<Fact>]
    let ``bytes are filled and two generators do not repeat each other`` () =
        let first = Array.zeroCreate<byte> 32
        let second = Array.zeroCreate<byte> 32
        (CryptographicRandom() :> Random).NextBytes first
        (CryptographicRandom() :> Random).NextBytes(Span second)

        Assert.Contains(first, fun b -> b <> 0uy)
        Assert.Contains(second, fun b -> b <> 0uy)
        Assert.NotEqual<byte[]>(first, second)

    [<Fact>]
    let ``Shuffle draws from the generator and returns a permutation`` () =
        let rng = CryptographicRandom() :> Random
        let items = Array.init 200 id
        rng.Shuffle items

        Assert.Equal<int[]>(Array.init 200 id, Array.sort items)
        Assert.NotEqual<int[]>(Array.init 200 id, items)

    [<Fact>]
    let ``UnlessSeeded is reproducible with a seed and cryptographic without one`` () =
        let draw (rng: Random) = Array.init 16 (fun _ -> rng.Next 1000)

        Assert.Equal<int[]>(draw (Random 11), draw (CryptographicRandom.UnlessSeeded(ValueSome 11)))
        Assert.IsType<CryptographicRandom>(CryptographicRandom.UnlessSeeded ValueNone) |> ignore
        Assert.IsNotType<CryptographicRandom>(CryptographicRandom.UnlessSeeded(ValueSome 11)) |> ignore

    [<Fact>]
    let ``BB84 building blocks accept the cryptographic generator`` () =
        let rng = CryptographicRandom() :> Random
        let alice = QuantumKeyDistribution.createAliceState 400 rng
        let bobBases = QuantumKeyDistribution.createBobBases 400 rng

        Assert.Equal(400, alice.Bits.Length)
        Assert.Equal(400, bobBases.Length)

        // 400 fair coins: 200 expected, standard deviation 10.
        let ones = alice.Bits |> Array.filter (fun b -> b = QuantumKeyDistribution.One) |> Array.length
        Assert.InRange(ones, 120, 280)

        let diagonal =
            bobBases
            |> Array.filter (fun b -> b = QuantumKeyDistribution.Diagonal)
            |> Array.length

        Assert.InRange(diagonal, 120, 280)
