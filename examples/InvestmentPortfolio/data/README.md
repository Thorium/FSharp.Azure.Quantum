# Bundled market statistics

`market-stats-2019-2023.json` holds the figures the InvestmentPortfolio examples use by default, so
they run offline and give the same result every time. It contains derived statistics only; no daily
prices are stored.

| | |
|---|---|
| Source | Yahoo Finance daily **adjusted** closes (split- and dividend-adjusted), fetched with the library's `FinancialData.fetchYahooHistoryAsync` for explicit dates (`StartDate`/`EndDate`: 10 days before the window to the end of the hold-out year) |
| Estimation window | 2019-01-01 to 2023-12-31 (1,258 daily returns per symbol) |
| Hold-out | 2024-01-01 to 2024-12-31 |
| Generated | 2026-09-30 by `generate-market-data.fsx` |
| Symbols | AAPL, MSFT, GOOGL, AMZN, NVDA, META, TSLA, AMD, TQQQ, JNJ, XLP, GLD, TLT, SH, SPY (market proxy) |

## Method

With `A_t` the adjusted close and `r_t = A_t / A_(t-1) - 1` the daily simple return, counting the
returns dated inside the window:

- **annualReturn**: arithmetic mean of `r_t` × 252
- **annualVolatility**: sample standard deviation of `r_t` × √252
- **correlation**: Pearson correlation of `r_t` on the dates all symbols share
- **buyPrice**: adjusted close on the last trading day of the window (2023-12-29)
- **holdoutReturn**: adjusted close on the last trading day of 2024 / buyPrice − 1, i.e. the
  buy-and-hold total return of 2024 with dividends reinvested
- **recent30DailyMean / recent30DailyStd**: mean and sample standard deviation of the last 30
  daily returns of the window (used by `RegimeAwarePortfolio.fsx`)
- **regimeModel**: a two-state Gaussian hidden Markov model of SPY's daily returns, fitted by
  Viterbi training (label the days with the current parameters, re-estimate from the labels,
  repeat until the labels stop changing); **regime** per asset holds the mean and standard
  deviation of its daily returns on the days labelled Bull and Bear

Adjusted closes are expressed on today's share basis: Yahoo rescales past prices after every later
split and dividend. The buy prices therefore differ from the prices quoted at the time (NVDA's
2023-12-29 buy price is 49.38 because of its 2024 10-for-1 split; the quoted close was 495.22),
and a later regeneration can shift them slightly. Returns and volatilities are unaffected by the
rescaling.

These are past figures from one fixed window. They are not forecasts and not investment advice.

## Regenerating or using other symbols and dates

From the repository root:

```bash
# Rebuild this file (fetches from Yahoo Finance; responses are cached for 6 hours under %TEMP%/FSharp.Azure.Quantum/yahoo-cache)
dotnet fsi --define:LOCAL_BUILD examples/InvestmentPortfolio/data/generate-market-data.fsx

# Another window or symbol set, written to its own file
dotnet fsi --define:LOCAL_BUILD examples/InvestmentPortfolio/data/generate-market-data.fsx -- --symbols AAPL,KO,SPY --from 2014-01-01 --to 2018-12-31
```

Every example also accepts `--live` (with optional `--symbols`, `--from`, `--to`) to compute the
same figures from Yahoo Finance at run time instead of reading this file.

Yahoo Finance's terms do not allow redistributing its price data, which is why only derived
figures are committed here.
