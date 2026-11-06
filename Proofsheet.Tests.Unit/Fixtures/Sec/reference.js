// Independent reference for the SEC fixtures in this folder, written separately from the C# provider so the two can
// check each other. It implements the same rules as Proofsheet.Infrastructure/Clients/SecFactSelector.cs and
// Proofsheet.Infrastructure/Providers/HybridFinancialStatementProvider.cs.
//
// Refresh or add a fixture:
//   1. Download https://data.sec.gov/api/xbrl/companyfacts/CIK##########.json for each company into a folder, as
//      companyfacts-<TICKER>.json. SEC requires a User-Agent with contact details.
//   2. node reference.js trim <download-folder> . <TICKER> [<TICKER> ...]
//   3. node reference.js compute . <capture date, yyyy-MM-dd>
//      and copy the printed values into SecFinancialStatementFixtureTests.
const fs = require("fs");
const path = require("path");

const LISTS = {
  revenue: ["Revenues", "RevenueFromContractWithCustomerExcludingAssessedTax", "SalesRevenueNet"],
  operatingIncome: ["OperatingIncomeLoss"],
  cfo: ["NetCashProvidedByUsedInOperatingActivities", "NetCashProvidedByUsedInOperatingActivitiesContinuingOperations"],
  capex: ["PaymentsToAcquirePropertyPlantAndEquipment", "PaymentsToAcquireProductiveAssets"],
  cash: ["CashAndCashEquivalentsAtCarryingValue", "CashCashEquivalentsRestrictedCashAndRestrictedCashEquivalents"],
  totalDebt: ["DebtLongtermAndShorttermCombinedAmount", "LongTermDebtAndCapitalLeaseObligationsIncludingCurrentMaturities", "DebtAndCapitalLeaseObligations", "LongTermDebt"],
  currentDebt: ["DebtCurrent", "LongTermDebtCurrent", "ShortTermBorrowings", "LongTermDebtAndCapitalLeaseObligationsCurrent"],
  nonCurrentDebt: ["LongTermDebtNoncurrent", "LongTermDebtAndCapitalLeaseObligations"],
};
// Kept in fixtures so tests prove they are ignored.
const IGNORED = ["PropertyPlantAndEquipmentAdditions", "CapitalExpendituresIncurredButNotYetPaid"];
const ANNUAL_FORMS = ["10-K", "10-K/A", "10-KT", "10-KT/A", "20-F", "20-F/A", "40-F", "40-F/A"];
const DAY = 86400000;
const TOL = 7 * DAY;
const t = s => Date.parse(s + "T00:00:00Z");
const iso = ms => new Date(ms).toISOString().slice(0, 10);

const usd = (root, c) => (root.facts && root.facts["us-gaap"] && root.facts["us-gaap"][c] && root.facts["us-gaap"][c].units && root.facts["us-gaap"][c].units.USD) || [];
const isAnnual = p => ANNUAL_FORMS.includes(p.form);
function latestFiledPerKey(points, key) {
  const m = new Map();
  for (const p of points) { const k = key(p); const cur = m.get(k); if (!cur || p.filed > cur.filed) m.set(k, p); }
  return [...m.values()];
}
function fiscalYears(root, c) {
  const fy = usd(root, c).filter(p => p.start && isAnnual(p) && (t(p.end) - t(p.start)) / DAY >= 350 && (t(p.end) - t(p.start)) / DAY <= 380);
  return latestFiledPerKey(fy, p => p.start + "|" + p.end).sort((a, b) => b.end.localeCompare(a.end));
}
// Within a week of the target end: latest filing first, then the closest date.
const latestFiledNear = (years, endMs) => years.filter(p => Math.abs(t(p.end) - endMs) <= TOL)
  .sort((a, b) => b.filed.localeCompare(a.filed) || Math.abs(t(a.end) - endMs) - Math.abs(t(b.end) - endMs))[0];
function instant(root, concepts, dateMs, annualOnly) {
  for (const c of concepts) {
    const pool = latestFiledPerKey(usd(root, c).filter(p => !p.start && (!annualOnly || isAnnual(p))), p => p.end);
    const hit = pool.filter(p => Math.abs(t(p.end) - dateMs) <= TOL)
      .sort((a, b) => Math.abs(t(a.end) - dateMs) - Math.abs(t(b.end) - dateMs) || b.filed.localeCompare(a.filed))[0];
    if (hit) return { concept: c, val: hit.val };
  }
  return null;
}
function fiscalYearFact(root, concepts, endMs) {
  for (const c of concepts) { const hit = latestFiledNear(fiscalYears(root, c), endMs); if (hit) return { concept: c, val: hit.val }; }
  return null;
}
function debtAt(root, dateMs, annualOnly) {
  const total = instant(root, LISTS.totalDebt, dateMs, annualOnly);
  const nc = instant(root, LISTS.nonCurrentDebt, dateMs, annualOnly);
  let parts = null;
  if (nc) { const cur = instant(root, LISTS.currentDebt, dateMs, annualOnly); parts = cur ? { concept: nc.concept + "+" + cur.concept, val: nc.val + cur.val } : nc; }
  return !total ? parts : (parts && parts.val > total.val ? parts : total);
}
const r2 = x => Math.round(x * 100 + Math.sign(x) * 1e-7) / 100;

function compute(root, asOf) {
  let freshest = null;
  for (const c of LISTS.revenue) { const ys = fiscalYears(root, c); if (ys.length && (!freshest || ys[0].end > freshest.years[0].end)) freshest = { concept: c, years: ys }; }
  if (!freshest) return { unavailable: "no two consecutive fiscal years of us-gaap revenue in USD" };
  const latest = latestFiledNear(freshest.years, t(freshest.years[0].end));
  const previous = latestFiledNear(freshest.years, t(latest.start) - DAY);
  if (!previous) return { unavailable: "no two consecutive fiscal years of us-gaap revenue in USD" };
  const fyEnd = t(latest.end);
  for (const c of [...LISTS.operatingIncome, ...LISTS.cfo, ...LISTS.capex]) {
    const ys = fiscalYears(root, c);
    if (ys.length && t(ys[0].end) - fyEnd > TOL) return { unavailable: `no revenue for the fiscal year ending ${ys[0].end}`, fy: latest.end };
  }
  const cutoff = new Date(t(asOf)); cutoff.setUTCMonth(cutoff.getUTCMonth() - 18);
  if (fyEnd <= cutoff.getTime()) return { unavailable: `stale (${latest.end})` };
  const op = fiscalYearFact(root, LISTS.operatingIncome, fyEnd), cfo = fiscalYearFact(root, LISTS.cfo, fyEnd), capex = fiscalYearFact(root, LISTS.capex, fyEnd);
  const cash = instant(root, LISTS.cash, fyEnd, true) || instant(root, LISTS.cash, fyEnd, false);
  const debt = debtAt(root, fyEnd, true) || debtAt(root, fyEnd, false);
  const missing = [["operating income", op], ["operating cash flow", cfo], ["capital expenditure", capex], ["cash", cash], ["debt", debt]].filter(([, v]) => !v).map(([n]) => n);
  if (missing.length) return { unavailable: "no " + missing.join(", "), fy: latest.end };
  return {
    fiscalYearEnd: latest.end, revenueConcept: freshest.concept, revenueB: r2(latest.val / 1e9), priorRevenueB: r2(previous.val / 1e9),
    growth: previous.val === 0 ? 0 : r2(((latest.val - previous.val) / Math.abs(previous.val)) * 100),
    margin: r2((op.val / latest.val) * 100), fcf: r2((cfo.val - capex.val) / 1e9), cash: r2(cash.val / 1e9), debt: r2(debt.val / 1e9),
    capexConcept: capex.concept, cashConcept: cash.concept, debtConcept: debt.concept,
  };
}

const [mode, dir, ...rest] = process.argv.slice(2);
if (mode === "compute") {
  const asOf = rest[0];
  const rows = fs.readdirSync(dir).filter(f => /^companyfacts-.*\.json$/.test(f)).sort().map(f => {
    const ticker = f.slice("companyfacts-".length, -".json".length);
    return { ticker, ...compute(JSON.parse(fs.readFileSync(path.join(dir, f), "utf8")), asOf) };
  });
  console.log(JSON.stringify(rows, null, 1));
} else if (mode === "trim") {
  const [outDir, ...tickers] = rest;
  const keep = new Set([...Object.values(LISTS).flat(), ...IGNORED]);
  fs.mkdirSync(outDir, { recursive: true });
  for (const ticker of tickers) {
    const root = JSON.parse(fs.readFileSync(path.join(dir, `companyfacts-${ticker}.json`), "utf8"));
    const trimmed = { cik: root.cik, entityName: root.entityName, facts: {} };
    if (root.facts["us-gaap"]) trimmed.facts["us-gaap"] = Object.fromEntries(Object.entries(root.facts["us-gaap"]).filter(([n]) => keep.has(n)));
    if (!root.facts["us-gaap"] && root.facts["ifrs-full"]) trimmed.facts["ifrs-full"] = Object.fromEntries(Object.entries(root.facts["ifrs-full"]).filter(([n]) => ["Revenue", "CashAndCashEquivalents"].includes(n)));
    fs.writeFileSync(path.join(outDir, `companyfacts-${ticker}.json`), JSON.stringify(trimmed));
    console.log(`trimmed ${ticker}: ${Math.round(fs.statSync(path.join(outDir, `companyfacts-${ticker}.json`)).size / 1024)} KB`);
  }
}
