# Domain References for InvestmentPortfolio Example

Brief summary of advanced portfolio theory concepts relevant to extending this example.

## Core Concepts Already Implemented

- **Modern Portfolio Theory (MPT)**: Markowitz mean-variance optimization
- **Sharpe Ratio**: Risk-adjusted return measure (μ - rf) / σ
- **Covariance Matrix (Σ)**: Asset return correlations

## Extension Concepts

### Risk Measures Beyond Volatility

| Measure | Formula | Use Case |
|---------|---------|----------|
| Value-at-Risk (VaR) | Quantile of loss distribution | Regulatory reporting |
| Expected Shortfall (ES) | E[Loss \| Loss > VaR] | Tail risk management |
| Marginal Risk Contribution | MR = 2Σw | Risk budgeting |

### Correlation Modeling (Copulas)

Standard covariance assumes Gaussian dependence. For tail risk:
- **t-copula**: Heavier tails, symmetric dependence
- **Clayton copula**: Lower tail dependence (crash correlation)
- **Gumbel copula**: Upper tail dependence

### Robust Optimization

When parameter estimates (μ, Σ) are uncertain:
- **Shrinkage estimators**: Reduce estimation error in Σ
- **Worst-case optimization**: Max-min approach for ambiguity aversion
- **Parameter uncertainty sets**: Optimize over confidence regions

## References

1. Glasserman & Xu - "Robust Risk Measurement and Model Risk"
2. Puzanova & Düllmann - "Copula-Specific Credit Portfolio Modeling"
3. Zagst et al. - "Risk Control in Asset Management"

*From: Innovations in Quantitative Risk Management (Springer, 2015)*
