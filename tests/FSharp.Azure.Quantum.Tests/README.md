# FSharp.Azure.Quantum tests

## Fast vs. slow tests

A handful of tests run genuine, full-depth quantum algorithms (e.g. end-to-end Shor
factoring via real QPE) and take **minutes** each. These are tagged with the xUnit trait
`Category = Slow` so they can be excluded from the everyday inner-loop run.

Anything that takes **more than ~20 seconds** should carry the trait:

```fsharp
[<Fact>]
[<Trait("Category", "Slow")>]
let ``my genuine-quantum integration test`` () = ...
```

### Running

**Fast suite (default for dev / PR CI)** — excludes the slow genuine-quantum tests:

```bash
dotnet test --filter "Category!=Slow"
```

**Full suite (nightly / pre-release)** — everything, including the slow tests:

```bash
dotnet test
```

**Only the slow tests** (e.g. to validate genuine Shor before a release):

```bash
dotnet test --filter "Category=Slow"
```
