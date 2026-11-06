# SEC company facts fixtures

Real XBRL company facts from SEC EDGAR (`https://data.sec.gov/api/xbrl/companyfacts/CIK##########.json`), captured on 2026-11-06. SEC EDGAR data is public domain.

Each file is trimmed to the US GAAP concepts the financial statement provider reads, plus two capital-expenditure concepts the provider deliberately ignores. Every fact inside a kept concept is unchanged. The TSM file keeps two IFRS concepts instead, because that company reports no US GAAP facts.

| Ticker | Why it is here |
|---|---|
| AAPL | `Revenues` stops at FY2018, and the FY2018 10-K also reports a fourth-quarter figure. The old selection mixed them and reported +322% revenue growth. |
| MSFT | `Revenues` stops at FY2010. The old selection reported +289.6% growth and a 248% operating margin. |
| AMZN | 10-Qs report trailing-twelve-month cash flows, which are a year long but are not fiscal years. Capital expenditure is tagged `PaymentsToAcquireProductiveAssets`. |
| COST | 52/53-week fiscal year. Revenue moved to `RevenueFromContractWithCustomerExcludingAssessedTax` in FY2026, and `LongTermDebt` stops in 2022. |
| NVDA | `PaymentsToAcquirePropertyPlantAndEquipment` stops at FY2012, so capital expenditure has to come from `PaymentsToAcquireProductiveAssets`. |
| AMD | The total-debt concepts stop in 2016 and 2021, and `Revenues` has no fiscal-year facts. |
| GOOGL, TSLA | `LongTermDebtAndCapitalLeaseObligations` stopped being reported (in 2025 and 2018), so debt has to come from `LongTermDebt` at the fiscal year end. |
| UNH | Total debt is tagged `DebtLongtermAndShorttermCombinedAmount`, which is lower than the balance sheet's long-term plus current debt. |
| BA | `LongTermDebtAndCapitalLeaseObligations` holds only the non-current amount. The expected debt is the filing's own total, `DebtAndCapitalLeaseObligations`. |
| PG | `LongTermDebtCurrent` leaves out commercial paper, while `DebtCurrent` includes it. The expected debt is long-term debt plus debt due within one year from the balance sheet. |
| JPM, XOM | No `OperatingIncomeLoss` (a bank and an oil major), so no statement can be built. |
| TSM | Foreign filer reporting under IFRS, which is unsupported. |

The expected values in `SecFinancialStatementFixtureTests` were computed from these files by `reference.js`, a separate implementation of the same rules, not by the provider. The latest and prior-year revenue were also checked against each company's published results. See the top of `reference.js` to refresh a fixture or add one.
