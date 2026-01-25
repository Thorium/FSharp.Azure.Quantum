# Domain References for InvestmentPortfolio Example

Brief summary of advanced portfolio theory concepts relevant to extending this example.

## Core Concepts Already Implemented

- **Modern Portfolio Theory (MPT)**: Markowitz mean-variance optimization
- **Sharpe Ratio**: Risk-adjusted return measure (μ - rf) / σ
- **Covariance Matrix (Σ)**: Asset return correlations

## Extension Concepts

### Stochastic Processes (Finance Context)

Brownian motion/Wiener processes are core to continuous-time finance models (e.g., diffusion models for prices and rates). Even if this example stays discrete/optimization-focused, these concepts are useful when extending the scenario generation and risk modeling.

Why this matters:
- **Risk is distributional**: meaningful risk statements are about ranges/sets of outcomes (probability of loss exceeding a threshold), not single “exact” realizations.
- **Information updates**: conditional expectations formalize “what we know now” vs “what we’ll know later”, which is central to multi-stage investment decisions and regime-based models.
- **Model rigor**: continuous-time models require careful handling of expectations/integration (measure-theoretic foundations) to avoid common derivation pitfalls.

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

1. Löffler & Kruschwitz - *The Brownian Motion: A Rigorous but Gentle Introduction for Economists* (Springer, 2019, Open Access)
   - Useful background on Brownian/Wiener processes, “almost sure” reasoning, and conditional expectation in finance contexts.
2. Glasserman & Xu - "Robust Risk Measurement and Model Risk"
3. Puzanova & Düllmann - "Copula-Specific Credit Portfolio Modeling"
4. Zagst et al. - "Risk Control in Asset Management"

*From: Innovations in Quantitative Risk Management (Springer, 2015) for items 2–4*
