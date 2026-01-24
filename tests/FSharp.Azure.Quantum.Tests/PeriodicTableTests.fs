module PeriodicTableTests

open Xunit
open FSharp.Azure.Quantum.Data

// =============================================================================
// ELEMENT LOOKUP TESTS
// =============================================================================

[<Fact>]
let ``PeriodicTable.bySymbol returns correct data for hydrogen`` () =
    let h = PeriodicTable.bySymbol "H"
    Assert.Equal("H", h.Symbol)
    Assert.Equal("Hydrogen", h.Name)
    Assert.Equal(1, h.AtomicNumber)
    Assert.True(abs(h.AtomicMass - 1.008) < 0.001)

[<Fact>]
let ``PeriodicTable.bySymbol returns correct data for carbon`` () =
    let c = PeriodicTable.bySymbol "C"
    Assert.Equal("C", c.Symbol)
    Assert.Equal("Carbon", c.Name)
    Assert.Equal(6, c.AtomicNumber)
    Assert.True(abs(c.AtomicMass - 12.011) < 0.001)

[<Fact>]
let ``PeriodicTable.bySymbol returns correct data for iron`` () =
    let fe = PeriodicTable.bySymbol "Fe"
    Assert.Equal("Fe", fe.Symbol)
    Assert.Equal("Iron", fe.Name)
    Assert.Equal(26, fe.AtomicNumber)

[<Fact>]
let ``PeriodicTable.bySymbol throws for invalid symbol`` () =
    Assert.Throws<System.Exception>(fun () -> PeriodicTable.bySymbol "Xx" |> ignore)

[<Fact>]
let ``PeriodicTable.tryBySymbol returns Some for valid element`` () =
    let result = PeriodicTable.tryBySymbol "O"
    Assert.True(result.IsSome)
    Assert.Equal("Oxygen", result.Value.Name)

[<Fact>]
let ``PeriodicTable.tryBySymbol returns None for invalid element`` () =
    let result = PeriodicTable.tryBySymbol "Invalid"
    Assert.True(result.IsNone)

[<Fact>]
let ``PeriodicTable.tryBySymbol is case-insensitive`` () =
    let lower = PeriodicTable.tryBySymbol "fe"
    let upper = PeriodicTable.tryBySymbol "FE"
    let mixed = PeriodicTable.tryBySymbol "Fe"
    Assert.True(lower.IsSome)
    Assert.True(upper.IsSome)
    Assert.True(mixed.IsSome)
    Assert.Equal(lower.Value.Name, upper.Value.Name)
    Assert.Equal(lower.Value.Name, mixed.Value.Name)

// =============================================================================
// COLLECTION TESTS
// =============================================================================

[<Fact>]
let ``PeriodicTable.all returns at least 100 elements`` () =
    let elements = PeriodicTable.all ()
    Assert.True(elements.Length >= 100, $"Expected at least 100 elements, got {elements.Length}")

[<Fact>]
let ``PeriodicTable.all length is consistent`` () =
    let all = PeriodicTable.all ()
    // Verify we have a reasonable number of elements
    Assert.True(all.Length >= 100 && all.Length <= 120, 
        $"Expected 100-120 elements, got {all.Length}")

[<Fact>]
let ``PeriodicTable elements have unique atomic numbers`` () =
    let elements = PeriodicTable.all ()
    let atomicNumbers = elements |> Array.map (fun e -> e.AtomicNumber)
    let uniqueCount = atomicNumbers |> Array.distinct |> Array.length
    Assert.Equal(elements.Length, uniqueCount)

[<Fact>]
let ``PeriodicTable elements have unique symbols`` () =
    let elements = PeriodicTable.all ()
    let symbols = elements |> Array.map (fun e -> e.Symbol.ToUpperInvariant())
    let uniqueCount = symbols |> Array.distinct |> Array.length
    Assert.Equal(elements.Length, uniqueCount)

// =============================================================================
// LOOKUP BY ATOMIC NUMBER TESTS
// =============================================================================

[<Fact>]
let ``PeriodicTable.tryByNumber returns correct element`` () =
    let result = PeriodicTable.tryByNumber 79  // Gold
    Assert.True(result.IsSome)
    Assert.Equal("Au", result.Value.Symbol)
    Assert.Equal("Gold", result.Value.Name)

[<Fact>]
let ``PeriodicTable.tryByNumber returns None for invalid number`` () =
    let result = PeriodicTable.tryByNumber 999
    Assert.True(result.IsNone)

[<Fact>]
let ``PeriodicTable.byNumber returns correct element`` () =
    let gold = PeriodicTable.byNumber 79
    Assert.Equal("Au", gold.Symbol)
    Assert.Equal("Gold", gold.Name)

[<Fact>]
let ``PeriodicTable.byNumber throws for invalid number`` () =
    Assert.Throws<System.Exception>(fun () -> PeriodicTable.byNumber 999 |> ignore)

// =============================================================================
// COVALENT RADIUS TESTS
// =============================================================================

[<Fact>]
let ``PeriodicTable.covalentRadius returns value for common elements`` () =
    let hRadius = PeriodicTable.covalentRadius "H"
    let cRadius = PeriodicTable.covalentRadius "C"
    let oRadius = PeriodicTable.covalentRadius "O"
    
    Assert.True(hRadius.IsSome)
    Assert.True(cRadius.IsSome)
    Assert.True(oRadius.IsSome)
    
    // H should be smallest, C larger than H
    Assert.True(hRadius.Value < cRadius.Value)

[<Fact>]
let ``PeriodicTable.estimateBondLength calculates reasonable H-H bond`` () =
    let bondLength = PeriodicTable.estimateBondLength "H" "H"
    Assert.True(bondLength.IsSome)
    // H-H bond is ~0.74 Å, sum of covalent radii gives approximation
    Assert.True(bondLength.Value > 0.5 && bondLength.Value < 1.5)

[<Fact>]
let ``PeriodicTable.estimateBondLength calculates reasonable C-C bond`` () =
    let bondLength = PeriodicTable.estimateBondLength "C" "C"
    Assert.True(bondLength.IsSome)
    // C-C single bond is ~1.54 Å
    Assert.True(bondLength.Value > 1.0 && bondLength.Value < 2.0)

[<Fact>]
let ``PeriodicTable.estimateBondLength is symmetric`` () =
    let ch = PeriodicTable.estimateBondLength "C" "H"
    let hc = PeriodicTable.estimateBondLength "H" "C"
    Assert.True(ch.IsSome)
    Assert.True(hc.IsSome)
    Assert.Equal(ch.Value, hc.Value)

// =============================================================================
// CATEGORY TESTS
// =============================================================================

[<Fact>]
let ``PeriodicTable includes transition metals`` () =
    // Check some transition metals are present
    Assert.True(PeriodicTable.isValidSymbol "Fe")  // Iron
    Assert.True(PeriodicTable.isValidSymbol "Cu")  // Copper
    Assert.True(PeriodicTable.isValidSymbol "Zn")  // Zinc
    Assert.True(PeriodicTable.isValidSymbol "Pt")  // Platinum
    Assert.True(PeriodicTable.isValidSymbol "Au")  // Gold

[<Fact>]
let ``PeriodicTable includes quantum dot elements`` () =
    // Elements commonly used in quantum dots
    Assert.True(PeriodicTable.isValidSymbol "Cd")  // Cadmium
    Assert.True(PeriodicTable.isValidSymbol "Se")  // Selenium
    Assert.True(PeriodicTable.isValidSymbol "Te")  // Tellurium
    Assert.True(PeriodicTable.isValidSymbol "Pb")  // Lead

[<Fact>]
let ``PeriodicTable includes common organic elements`` () =
    Assert.True(PeriodicTable.isValidSymbol "C")
    Assert.True(PeriodicTable.isValidSymbol "H")
    Assert.True(PeriodicTable.isValidSymbol "N")
    Assert.True(PeriodicTable.isValidSymbol "O")
    Assert.True(PeriodicTable.isValidSymbol "S")
    Assert.True(PeriodicTable.isValidSymbol "P")
