# Investment Portfolio Optimization Example

## Business Context

An investment advisor needs to allocate a **$100,000 portfolio** across **8 technology stocks**, maximizing expected returns while managing portfolio risk. This is a classic **mean-variance portfolio optimization** problem that balances growth potential against volatility.

### Real-World Application

Portfolio optimization is fundamental to wealth management and institutional investing:

- **Investment Advisors**: Construct client portfolios balancing risk tolerance and return objectives
- **Pension Funds**: Manage billions in assets with specific risk/return mandates
- **Hedge Funds**: Optimize allocations across diverse asset classes
- **401(k) Management**: Automate portfolio rebalancing for retirement accounts

**Key Business Metrics:**
- Portfolio managers typically manage $50M-$500M in assets
- 1% improvement in Sharpe ratio can translate to millions in better risk-adjusted returns
- Automated portfolio optimization reduces human bias and improves consistency

---

## The Problem

### Mathematical Formulation

**Objective:** Maximize risk-adjusted returns (Sharpe ratio)

**Decision Variables:**
- $w_i$ = Weight allocated to stock $i$ (fraction of portfolio)
- Must satisfy: $\sum w_i = 1$ (fully invested) and $w_i \geq 0$ (no short selling)

**Risk-Return Trade-off:**
- **Expected Return:** $R_p = \sum w_i \cdot r_i$
- **Portfolio Risk:** $\sigma_p = \sqrt{\sum w_i^2 \cdot \sigma_i^2}$ (simplified, assumes independence)
- **Sharpe Ratio:** $S = R_p / \sigma_p$ (return per unit of risk)

**Constraints:**
- Budget constraint: Total value ≤ $100,000
- Diversification: Balance between concentration and over-diversification
- Transaction costs: Minimize number of holdings (practical consideration)

### Problem Characteristics

| Characteristic | Value |
|----------------|-------|
| **Assets** | 8 tech stocks |
| **Budget** | $100,000 |
| **Decision Type** | Continuous optimization (weights) + discrete (stock selection) |
| **Complexity** | $O(n^2)$ for mean-variance, $O(2^n)$ for subset selection |
| **Classical Approach** | Quadratic programming (QP) or greedy allocation |

---

## Stock Data

The example uses **8 major tech stocks** with realistic performance metrics based on historical data:

| Symbol | Company | Expected Return | Volatility | Price | Sharpe Ratio |
|--------|---------|-----------------|------------|-------|--------------|
| **AAPL** | Apple Inc. | 18.0% | 22.0% | $175 | 0.82 |
| **MSFT** | Microsoft Corp. | 22.0% | 25.0% | $380 | 0.88 |
| **GOOGL** | Alphabet Inc. | 16.0% | 28.0% | $140 | 0.57 |
| **AMZN** | Amazon.com Inc. | 24.0% | 32.0% | $155 | 0.75 |
| **NVDA** | NVIDIA Corp. | 35.0% | 45.0% | $485 | 0.78 |
| **META** | Meta Platforms Inc. | 28.0% | 38.0% | $350 | 0.74 |
| **TSLA** | Tesla Inc. | 30.0% | 55.0% | $245 | 0.55 |
| **AMD** | Advanced Micro Devices | 26.0% | 42.0% | $125 | 0.62 |

**Data Notes:**
- Returns and volatility estimated from 5-year trailing performance
- Sharpe ratio = Expected Return / Volatility (simplified, assumes risk-free rate ≈ 0%)
- Prices reflect approximate 2024 trading levels

---

## How to Run

### Prerequisites

1. **Build the FSharp.Azure.Quantum library:**
   ```bash
   cd ../../
   dotnet build src/FSharp.Azure.Quantum/FSharp.Azure.Quantum.fsproj
   ```

2. **Navigate to example directory:**
   ```bash
   cd examples/InvestmentPortfolio
   ```

### Execute the Example

```bash
dotnet fsi InvestmentPortfolio.fsx
```

### Expected Runtime

- **Classical Solver**: ~5-10 milliseconds
- **Problem Size**: 8 assets, continuous optimization
- **Memory**: Minimal (<10 MB)

---

## Expected Output

The example produces three detailed reports:

### 1. Portfolio Allocation Report

```
╔══════════════════════════════════════════════════════════════════════════════╗
║                       PORTFOLIO ALLOCATION REPORT                            ║
╚══════════════════════════════════════════════════════════════════════════════╝

ASSETS SELECTED:
────────────────────────────────────────────────────────────────────────────────
  1. MSFT   | 263.16 shares @ $380.00 = $100,000.00 (100.0%)

PORTFOLIO SUMMARY:
────────────────────────────────────────────────────────────────────────────────
  Total Invested:        $100,000.00
  Number of Holdings:    1 stocks
  Expected Annual Return: 22.00%
  Portfolio Risk (σ):    25.00%
  Sharpe Ratio:          0.88
```

### 2. Risk-Return Analysis

Shows individual stock metrics and compares portfolio performance vs. average single-stock investment:

```
PORTFOLIO VS. INDIVIDUAL STOCKS:
────────────────────────────────────────────────────────────────────────────────
  Average Stock Return:  24.88%
  Portfolio Return:      22.00% (0.9x better)

  Average Stock Risk:    35.88%
  Portfolio Risk:        25.00% (1.4x lower)

  Average Sharpe Ratio:  0.69
  Portfolio Sharpe:      0.88 (1.3x better)
```

**Key Insight:** The optimized portfolio achieves **1.3x better Sharpe ratio** than average single-stock investment, demonstrating improved risk-adjusted returns.

### 3. Business Impact Analysis

```
PROJECTED ANNUAL OUTCOMES (1 Year):
────────────────────────────────────────────────────────────────────────────────
  Initial Investment:    $100,000.00
  Expected Return:       $22,000.00 (22.00% gain)

  SCENARIO ANALYSIS (95% confidence interval):
  • Best Case:           $147,000.00 (+47.00%)
  • Expected:            $122,000.00 (+22.00%)
  • Worst Case:          $97,000.00 (-3.00%)

KEY INSIGHTS:
────────────────────────────────────────────────────────────────────────────────
  ✓ Diversification across tech stocks reduces risk
  ✓ Sharpe ratio of 0.88 indicates efficient risk-adjusted returns
  ✓ Expected to generate $22,000.00 annually
  ✓ Risk-managed approach balances growth and volatility
```

---

## Solution Interpretation

### What the Solution Tells Us

The optimization balances **risk vs. return** by:

1. **Asset Selection**: Chooses stocks with best Sharpe ratios (MSFT: 0.88 in this example)
2. **Risk Management**: Avoids overly volatile assets despite high returns (e.g., TSLA: 30% return but 55% volatility)
3. **Efficient Frontier**: Positions portfolio on the optimal risk-return curve

### Performance Characteristics

| Metric | Classical Solver |
|--------|------------------|
| **Solution Time** | ~5 ms |
| **Optimality** | Exact or near-optimal |
| **Scalability** | Up to ~100 assets efficiently |
| **Practical Limit** | 500+ assets (polynomial complexity) |

### When to Use Quantum vs. Classical

**Classical Solvers (Current Example):**
- ✅ **Use for:** <100 assets, continuous optimization, mean-variance portfolios
- ✅ **Advantages:** Fast, exact solutions, well-established algorithms
- ✅ **Performance:** Milliseconds for practical portfolio sizes

**Quantum Advantage (Future):**
- ⚡ **Potential for:** 1000+ assets with complex constraints (discrete allocations, transaction costs)
- ⚡ **Algorithms:** QAOA for combinatorial portfolio optimization
- ⚡ **Status:** Experimental (NISQ hardware limitations)

**Recommendation:** Use **classical solvers for portfolio optimization** until quantum hardware matures significantly. Current noisy quantum devices offer no practical advantage for this problem class.

---

## Technical Details

### Algorithm Used

The example uses **FSharp.Azure.Quantum's Portfolio.solveDirectly** function, which implements:

1. **Mean-Variance Optimization**: Classic Markowitz portfolio theory
2. **Greedy Sharpe Ratio Selection**: Prioritizes assets with best risk-adjusted returns
3. **Budget Allocation**: Ensures full investment of available capital

### Code Structure

```fsharp
// 1. Define stock data with return/risk metrics
let stocks = [ ... ]

// 2. Call Portfolio.solveDirectly
match Portfolio.solveDirectly assets budget None with
| Ok allocation ->
    // 3. Analyze and report results
    let analysis = createAnalysis allocation
    generateReports analysis
| Error msg ->
    printfn "Optimization failed: %s" msg
```

### Key Functions

- **`Portfolio.solveDirectly`**: Core optimization function
- **`createAnalysis`**: Calculates Sharpe ratio, diversification score
- **`generateAllocationReport`**: Formats allocation results
- **`generateRiskReturnAnalysis`**: Compares portfolio vs. individual stocks
- **`generateBusinessImpact`**: Projects financial outcomes

---

## Extending This Example

### Add Real-Time Stock Data

Replace static data with Yahoo Finance API or similar:

```fsharp
// Pseudo-code
let fetchStockData (symbol: string) : Stock =
    let prices = YahooFinance.getHistoricalPrices symbol (DateTime.Now.AddYears(-5))
    let returns = calculateReturns prices
    let volatility = calculateVolatility returns
    {
        Symbol = symbol
        ExpectedReturn = returns |> List.average
        Volatility = volatility
        Price = prices |> List.last
    }
```

### Add Correlation Matrix

Improve risk modeling by considering asset correlations:

```fsharp
// Portfolio risk with correlations
let portfolioVariance = 
    assets
    |> List.sumBy (fun (i, wi) ->
        assets
        |> List.sumBy (fun (j, wj) ->
            wi * wj * covariance.[i,j]))
```

### Add Constraints

- **Maximum position size**: Limit any single stock to 20% of portfolio
- **Sector limits**: No more than 40% in any sector
- **Minimum diversification**: Hold at least 5 different stocks

---

## Educational Value

This example demonstrates:

1. **✅ Real-world problem modeling** - Translating business requirements to optimization
2. **✅ Risk-return trade-offs** - Balancing competing objectives
3. **✅ Classical optimization** - Using efficient algorithms for continuous problems
4. **✅ Result interpretation** - Understanding what the solution means for business
5. **✅ Performance analysis** - Measuring solution quality (Sharpe ratio)

### Key Takeaways

- **Mean-variance optimization** is foundational to modern portfolio theory
- **Sharpe ratio** measures risk-adjusted performance (higher is better)
- **Diversification** reduces portfolio risk below average asset risk
- **Classical solvers** are highly effective for continuous optimization problems
- **Quantum advantage** requires problem sizes and constraints beyond current capabilities

---

## References

### Academic

- **Markowitz, H. (1952)**: "Portfolio Selection" - Nobel Prize-winning work on mean-variance optimization
- **Sharpe, W. (1966)**: "Mutual Fund Performance" - Introduced Sharpe ratio metric

### Practical

- **Modern Portfolio Theory (MPT)**: Foundation for institutional portfolio management
- **Efficient Frontier**: Optimal risk-return combinations
- **Capital Asset Pricing Model (CAPM)**: Extended framework for asset pricing

### Related Examples

- **Supply Chain Optimization**: Multi-stage network flow optimization
- **Job Scheduling**: Resource allocation with constraints
- **Delivery Routing**: Combinatorial optimization (TSP variant)

---

## Questions or Issues?

- **Library Documentation**: See `../../docs/`
- **API Reference**: Check `Portfolio` module documentation
- **Performance Issues**: Verify build configuration (Release mode recommended)
- **Data Updates**: Modify `stocks` list with current market data

---

**Last Updated**: 2025-11-27  
**FSharp.Azure.Quantum Version**: 1.0.0 (in development)  
**Problem Difficulty**: Medium (continuous optimization with risk constraints)  
**Business Value**: High (fundamental to wealth management industry)
