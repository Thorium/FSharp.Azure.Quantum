namespace FSharp.Azure.Quantum

open System
open FSharp.Azure.Quantum.Core

/// <summary>
/// Shared types for Portfolio optimization across both Classical and Quantum solvers
/// </summary>
module PortfolioTypes =

    /// <summary>
    /// Represents a financial asset with characteristics for portfolio optimization
    /// </summary>
    type Asset =
        {
            /// Stock ticker symbol (e.g., "AAPL", "GOOGL")
            Symbol: string

            /// Expected annual return rate as decimal (e.g., 0.12 = 12%)
            ExpectedReturn: float

            /// Risk measure (standard deviation) as decimal
            Risk: float

            /// Current price per share
            Price: float
        }

    // ================================================================================
    // COVARIANCE AND PORTFOLIO RISK
    // ================================================================================

    /// <summary>
    /// Relative tolerance of covariance validation. With scale = the largest diagonal entry and
    /// τ = max(tolerance × scale, 1e-300), a covariance is accepted when |Σ[i,j] - Σ[j,i]| ≤ τ
    /// and its smallest eigenvalue is greater than -τ.
    /// </summary>
    [<Literal>]
    let CovarianceTolerance = 1e-6

    /// <summary>
    /// Covariance matrix from per-asset volatilities and a correlation matrix:
    /// Σ[i,j] = ρ[i,j] × σ[i] × σ[j].
    /// </summary>
    /// <param name="volatilities">Standard deviations σ, one per asset</param>
    /// <param name="correlation">Correlation matrix ρ (n × n, unit diagonal)</param>
    /// <returns>The covariance matrix, or a ValidationError when the shapes do not match</returns>
    let covarianceFromCorrelation (volatilities: float[]) (correlation: float[,]) : QuantumResult<float[,]> =
        let n = volatilities.Length

        if Array2D.length1 correlation <> n || Array2D.length2 correlation <> n then
            Error(
                QuantumError.ValidationError(
                    "correlation",
                    $"Correlation matrix is {Array2D.length1 correlation}x{Array2D.length2 correlation} but there are {n} volatilities"
                )
            )
        else
            Ok(Array2D.init n n (fun i j -> correlation.[i, j] * volatilities.[i] * volatilities.[j]))

    /// True when Σ + shift × I has a Cholesky factorisation, i.e. its smallest eigenvalue exceeds -shift.
    let private isPositiveDefiniteAfterShift (matrix: float[,]) (shift: float) : bool =
        let n = Array2D.length1 matrix
        let l = Array2D.zeroCreate<float> n n

        let rec column j =
            if j >= n then
                true
            else
                let mutable pivot = matrix.[j, j] + shift

                for k in 0 .. j - 1 do
                    pivot <- pivot - l.[j, k] * l.[j, k]

                if not (pivot > 0.0) then
                    false
                else
                    let d = sqrt pivot
                    l.[j, j] <- d

                    for i in j + 1 .. n - 1 do
                        let mutable s = 0.5 * (matrix.[i, j] + matrix.[j, i])

                        for k in 0 .. j - 1 do
                            s <- s - l.[i, k] * l.[j, k]

                        l.[i, j] <- s / d

                    column (j + 1)

        column 0

    /// <summary>
    /// Validate a covariance matrix for a portfolio of <paramref name="assetCount"/> assets:
    /// square, one row per asset, finite, symmetric and positive semidefinite within
    /// <see cref="CovarianceTolerance"/>.
    /// </summary>
    /// <returns>Ok (), or a ValidationError for field "covariance"</returns>
    let validateCovariance (assetCount: int) (covariance: float[,]) : QuantumResult<unit> =
        let fail reason =
            Error(QuantumError.ValidationError("covariance", reason))

        let rows = Array2D.length1 covariance
        let cols = Array2D.length2 covariance

        if rows <> cols then
            fail $"Covariance matrix must be square, got {rows}x{cols}"
        elif rows <> assetCount then
            fail $"Covariance matrix is {rows}x{cols} but the portfolio has {assetCount} assets"
        else
            let entries =
                [|
                    for i in 0 .. rows - 1 do
                        for j in 0 .. cols - 1 do
                            yield (i, j, covariance.[i, j])
                |]

            match
                entries
                |> Array.tryFind (fun (_, _, v) -> Double.IsNaN v || Double.IsInfinity v)
            with
            | Some(i, j, v) -> fail $"Covariance entry [{i},{j}] is not finite: {v}"
            | None ->
                match Seq.init rows id |> Seq.tryFind (fun i -> covariance.[i, i] < 0.0) with
                | Some i -> fail $"Covariance diagonal entry [{i},{i}] is negative: {covariance.[i, i]}"
                | None ->
                    let scale =
                        if rows = 0 then
                            0.0
                        else
                            Seq.init rows (fun i -> covariance.[i, i]) |> Seq.max

                    // The absolute floor keeps an all-zero covariance (every asset riskless) valid.
                    let tolerance = max (CovarianceTolerance * scale) 1e-300

                    let asymmetric =
                        entries
                        |> Array.tryFind (fun (i, j, v) -> i < j && abs (v - covariance.[j, i]) > tolerance)

                    match asymmetric with
                    | Some(i, j, v) ->
                        fail $"Covariance matrix is not symmetric: [{i},{j}] = {v} but [{j},{i}] = {covariance.[j, i]}"
                    | None when not (isPositiveDefiniteAfterShift covariance tolerance) ->
                        fail "Covariance matrix is not positive semidefinite (it has a negative eigenvalue)"
                    | None -> Ok()

    /// <summary>
    /// Portfolio variance wᵀΣw.
    /// </summary>
    /// <param name="weights">Portfolio weights, one per covariance row</param>
    /// <param name="covariance">Covariance matrix Σ</param>
    let portfolioVariance (weights: float[]) (covariance: float[,]) : float =
        let n = weights.Length
        let mutable total = 0.0

        for i in 0 .. n - 1 do
            if weights.[i] <> 0.0 then
                for j in 0 .. n - 1 do
                    total <- total + weights.[i] * weights.[j] * covariance.[i, j]

        total

    /// <summary>
    /// Portfolio risk (standard deviation) of <paramref name="weights"/> over <paramref name="assets"/>.
    /// With a covariance matrix: sqrt(wᵀΣw). Without one the assets are taken as independent:
    /// sqrt(Σ (wᵢσᵢ)²), with σᵢ = Asset.Risk.
    /// </summary>
    /// <param name="assets">Assets in covariance row order</param>
    /// <param name="weights">Weights aligned with <paramref name="assets"/></param>
    /// <param name="covariance">Optional covariance matrix aligned with <paramref name="assets"/></param>
    let portfolioRisk (assets: Asset list) (weights: float[]) (covariance: float[,] option) : float =
        match covariance with
        | Some sigma -> portfolioVariance weights sigma |> max 0.0 |> sqrt
        | None ->
            assets
            |> List.mapi (fun i asset ->
                let weightedRisk = weights.[i] * asset.Risk
                weightedRisk * weightedRisk)
            |> List.sum
            |> sqrt
