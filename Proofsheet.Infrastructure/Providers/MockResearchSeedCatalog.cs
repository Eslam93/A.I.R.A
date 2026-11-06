using Proofsheet.Application.Models;
using Proofsheet.Domain.Enums;
using Proofsheet.Domain.Models;

namespace Proofsheet.Infrastructure.Providers;

public sealed class MockResearchSeedCatalog
{
    private readonly IReadOnlyDictionary<string, MockResearchSeed> _seeds;

    public MockResearchSeedCatalog()
    {
        _seeds = BuildSeeds();
    }

    public bool TryGet(string identifier, out MockResearchSeed seed) =>
        _seeds.TryGetValue(identifier, out seed!);

    public MockResearchSeed GetOrGenerate(string identifier, ResolvedTickerInfo? resolvedTicker = null) =>
        _seeds.TryGetValue(identifier, out var fixedSeed)
            ? fixedSeed
            : BuildGeneratedSeed(identifier, resolvedTicker);

    private static IReadOnlyDictionary<string, MockResearchSeed> BuildSeeds() =>
        new Dictionary<string, MockResearchSeed>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAPL"] = new MockResearchSeed(
                new CompanyProfile("AAPL", "profile:AAPL", "Apple Inc.", "NASDAQ", "0000320193", "Apple company profile", Utc(2026, 3, 1), TimestampType.Captured, 0.90m, 0.50m),
                new MarketSnapshot("AAPL", "market:AAPL", 221.18m, 18.6m, "The share price has held its premium multiple on resilient platform demand.", "Valuation remains sensitive to iPhone cycle expectations.", "Apple market snapshot", Utc(2026, 3, 15), TimestampType.AsOf, 0.82m, 0.50m),
                new FinancialStatement("AAPL", "financials:AAPL", 6.4m, 30.8m, 102.4m, 67.3m, 11.2m, "high margins and recurring services revenue support resilient cash generation", "net cash and buyback capacity support downside resilience", "Apple financial statements", Utc(2026, 1, 31), TimestampType.AsOf, 0.92m, 1.00m),
                new NewsCoverage("AAPL", "Apple news coverage", Utc(2026, 3, 14), TimestampType.Published, 0.68m, 0.50m,
                [
                    new NewsItem("news:AAPL:001", "Services growth continues to outpace hardware", "Analysts highlighted steady subscription mix and installed-base monetization.", "https://example.com/apple-services", Utc(2026, 3, 14), TimestampType.Published, 0.70m, 0.50m),
                    new NewsItem("news:AAPL:002", "Apple supply chain remains stable into the next launch cycle", "Channel checks pointed to manageable supply constraints and healthy premium mix.", "https://example.com/apple-supply-chain", Utc(2026, 3, 10), TimestampType.Published, 0.66m, 0.50m)
                ]),
                new FilingCoverage("AAPL", "Apple filing coverage", Utc(2026, 2, 2), TimestampType.Published, 0.85m, 0.50m,
                [
                    new FilingExcerpt("filing:AAPL:001", "Apple 10-Q capital allocation excerpt", "Management reiterated disciplined capital returns and ongoing investment in the installed base.", "https://example.com/apple-filing", Utc(2026, 2, 2), TimestampType.Published, 0.85m, 0.50m)
                ])),

            ["MSFT"] = new MockResearchSeed(
                new CompanyProfile("MSFT", "profile:MSFT", "Microsoft Corporation", "NASDAQ", "0000789019", "Microsoft company profile", Utc(2026, 3, 1), TimestampType.Captured, 0.91m, 0.50m),
                new MarketSnapshot("MSFT", "market:MSFT", 446.72m, 21.3m, "Market leadership remains tied to Azure execution and AI monetization.", "Multiple expansion leaves little room for cloud deceleration.", "Microsoft market snapshot", Utc(2026, 3, 15), TimestampType.AsOf, 0.80m, 0.50m),
                new FinancialStatement("MSFT", "financials:MSFT", 11.2m, 42.1m, 79.4m, 81.7m, 49.1m, "software mix and cloud scale continue to drive elite operating leverage", "the balance sheet gives flexibility for AI investment without stressing returns", "Microsoft financial statements", Utc(2026, 1, 31), TimestampType.AsOf, 0.90m, 1.00m),
                new NewsCoverage("MSFT", "Microsoft news coverage", Utc(2026, 3, 13), TimestampType.Published, 0.70m, 0.50m,
                [
                    new NewsItem("news:MSFT:001", "Azure demand remains durable across enterprise workloads", "Enterprise checks suggested healthy cloud consumption and improving AI attach.", "https://example.com/microsoft-azure", Utc(2026, 3, 13), TimestampType.Published, 0.71m, 0.50m),
                    new NewsItem("news:MSFT:002", "Microsoft expands copilots into broader business workflows", "Product launches continued to deepen adoption across the installed enterprise base.", "https://example.com/microsoft-copilot", Utc(2026, 3, 11), TimestampType.Published, 0.69m, 0.50m)
                ]),
                new FilingCoverage("MSFT", "Microsoft filing coverage", Utc(2026, 2, 1), TimestampType.Published, 0.86m, 0.50m,
                [
                    new FilingExcerpt("filing:MSFT:001", "Microsoft 10-Q AI investment excerpt", "The filing emphasized continued infrastructure investment while preserving operating discipline.", "https://example.com/microsoft-filing", Utc(2026, 2, 1), TimestampType.Published, 0.86m, 0.50m)
                ])),

            ["NVDA"] = new MockResearchSeed(
                new CompanyProfile("NVDA", "profile:NVDA", "NVIDIA Corporation", "NASDAQ", "0001045810", "NVIDIA company profile", Utc(2026, 3, 1), TimestampType.Captured, 0.88m, 0.50m),
                new MarketSnapshot("NVDA", "market:NVDA", 1173.54m, 46.8m, "Momentum reflects sustained data-center demand and premium expectations.", "Valuation sensitivity rises if hyperscaler demand normalizes.", "NVIDIA market snapshot", Utc(2026, 3, 15), TimestampType.AsOf, 0.84m, 1.00m),
                new FinancialStatement("NVDA", "financials:NVDA", 38.4m, 55.7m, 46.3m, 28.5m, 9.8m, "data-center growth and high gross margins continue to compound cash generation", "net cash and capex flexibility support a rapid product cadence", "NVIDIA financial statements", Utc(2026, 1, 31), TimestampType.AsOf, 0.89m, 1.00m),
                new NewsCoverage("NVDA", "NVIDIA news coverage", Utc(2026, 3, 12), TimestampType.Published, 0.74m, 0.50m,
                [
                    new NewsItem("news:NVDA:001", "Hyperscaler AI spending remains elevated", "Suppliers reported sustained accelerator orders and little evidence of abrupt demand pause.", "https://example.com/nvidia-ai-spend", Utc(2026, 3, 12), TimestampType.Published, 0.76m, 0.50m),
                    new NewsItem("news:NVDA:002", "New platform roadmap broadens enterprise AI adoption", "Channel commentary pointed to expanding use cases beyond the largest cloud providers.", "https://example.com/nvidia-roadmap", Utc(2026, 3, 9), TimestampType.Published, 0.72m, 0.50m)
                ]),
                new FilingCoverage("NVDA", "NVIDIA filing coverage", Utc(2026, 2, 3), TimestampType.Published, 0.81m, 0.50m,
                [
                    new FilingExcerpt("filing:NVDA:001", "NVIDIA 10-Q demand concentration excerpt", "Management described customer concentration risk while affirming investment in next-generation platforms.", "https://example.com/nvidia-filing", Utc(2026, 2, 3), TimestampType.Published, 0.81m, 0.50m)
                ])),

            ["TSLA"] = new MockResearchSeed(
                new CompanyProfile("TSLA", "profile:TSLA", "Tesla, Inc.", "NASDAQ", "0001318605", "Tesla company profile", Utc(2026, 3, 1), TimestampType.Captured, 0.80m, 0.50m),
                new MarketSnapshot("TSLA", "market:TSLA", 181.44m, -12.1m, "The market is balancing long-term platform optionality against softer near-term automotive sentiment.", "Volatility remains elevated because execution and margin expectations shift quickly.", "Tesla market snapshot", Utc(2026, 3, 15), TimestampType.AsOf, 0.72m, -0.50m),
                new FinancialStatement("TSLA", "financials:TSLA", 1.8m, 11.4m, 8.2m, 25.6m, 6.1m, "cash generation remains positive but less convincing than the strongest large-cap peers", "balance sheet liquidity supports continued investment, but margin compression limits enthusiasm", "Tesla financial statements", Utc(2026, 1, 31), TimestampType.AsOf, 0.62m, 0.00m),
                new NewsCoverage("TSLA", "Tesla news coverage", Utc(2026, 3, 12), TimestampType.Published, 0.28m, -0.50m,
                [
                    new NewsItem("news:TSLA:001", "Auto pricing pressure clouds near-term delivery narrative", "Coverage emphasized margin pressure and limited high-quality incremental evidence.", "https://example.com/tesla-pricing", Utc(2026, 3, 12), TimestampType.Published, 0.28m, -0.50m)
                ]),
                new FilingCoverage("TSLA", "Tesla filing coverage", Utc(2026, 2, 5), TimestampType.Published, 0.74m, 0.00m,
                [
                    new FilingExcerpt("filing:TSLA:001", "Tesla 10-Q risk factors excerpt", "The filing highlighted macro demand sensitivity and execution risk while reiterating long-term platform ambitions.", "https://example.com/tesla-filing", Utc(2026, 2, 5), TimestampType.Published, 0.74m, 0.00m)
                ])),

            ["JPM"] = new MockResearchSeed(
                new CompanyProfile("JPM", "profile:JPM", "JPMorgan Chase & Co.", "NYSE", "0000019617", "JPMorgan company profile", Utc(2026, 3, 1), TimestampType.Captured, 0.87m, 0.50m),
                new MarketSnapshot("JPM", "market:JPM", 198.33m, 16.4m, "The market continues to reward deposit resilience and operating diversification.", "Net interest income normalization remains a visible debate for the next few quarters.", "JPMorgan market snapshot", Utc(2026, 3, 15), TimestampType.AsOf, 0.78m, 0.50m),
                new FinancialStatement("JPM", "financials:JPM", 5.3m, 34.7m, 32.5m, 475.0m, 169.0m, "capital generation and expense discipline continue to anchor returns", "fortress balance sheet positioning supports downside resilience across the cycle", "JPMorgan financial statements", Utc(2026, 1, 31), TimestampType.AsOf, 0.88m, 0.50m),
                new NewsCoverage("JPM", "JPMorgan news coverage", Utc(2026, 3, 13), TimestampType.Published, 0.66m, 0.00m,
                [
                    new NewsItem("news:JPM:001", "JPMorgan prepares for a more normalized rate backdrop", "Coverage emphasized a steady outlook but fewer upside surprises from rate tailwinds.", "https://example.com/jpm-rates", Utc(2026, 3, 13), TimestampType.Published, 0.64m, 0.00m),
                    new NewsItem("news:JPM:002", "Fee businesses continue to offset lending cyclicality", "Analysts pointed to resilient payments and advisory franchises.", "https://example.com/jpm-fees", Utc(2026, 3, 9), TimestampType.Published, 0.68m, 0.00m)
                ]),
                new FilingCoverage("JPM", "JPMorgan filing coverage", Utc(2026, 2, 4), TimestampType.Published, 0.83m, 0.50m,
                [
                    new FilingExcerpt("filing:JPM:001", "JPMorgan 10-Q capital planning excerpt", "The filing stressed capital discipline and the durability of the diversified earnings mix.", "https://example.com/jpm-filing", Utc(2026, 2, 4), TimestampType.Published, 0.83m, 0.50m)
                ])),

            ["AMZN"] = new MockResearchSeed(
                new CompanyProfile("AMZN", "profile:AMZN", "Amazon.com, Inc.", "NASDAQ", "0001018724", "Amazon company profile", Utc(2026, 3, 1), TimestampType.Captured, 0.89m, 0.50m),
                new MarketSnapshot("AMZN", "market:AMZN", 191.22m, 14.8m, "The market remains focused on AWS durability and disciplined retail margins.", "Valuation still expects steady cloud and advertising execution.", "Amazon market snapshot", Utc(2026, 3, 15), TimestampType.AsOf, 0.81m, 0.50m),
                new FinancialStatement("AMZN", "financials:AMZN", 6.9m, 14.2m, 62.8m, 575.4m, 76.9m, "AWS and advertising continue to lift group profitability and cash generation", "balance sheet flexibility supports ongoing AI and logistics investment", "Amazon financial statements", Utc(2026, 1, 31), TimestampType.AsOf, 0.90m, 0.50m),
                new NewsCoverage("AMZN", "Amazon news coverage", Utc(2026, 3, 13), TimestampType.Published, 0.69m, 0.50m,
                [
                    new NewsItem("news:AMZN:001", "AWS demand remains resilient across enterprise workloads", "Checks suggested durable cloud demand and improving workload mix.", "https://example.com/amazon-aws", Utc(2026, 3, 13), TimestampType.Published, 0.70m, 0.50m),
                    new NewsItem("news:AMZN:002", "Advertising momentum continues to support margin expansion", "Coverage highlighted ad monetization as a higher-margin growth driver.", "https://example.com/amazon-ads", Utc(2026, 3, 10), TimestampType.Published, 0.68m, 0.50m)
                ]),
                new FilingCoverage("AMZN", "Amazon filing coverage", Utc(2026, 2, 3), TimestampType.Published, 0.84m, 0.50m,
                [
                    new FilingExcerpt("filing:AMZN:001", "Amazon 10-Q efficiency excerpt", "Management emphasized fulfillment efficiency and continued disciplined capital allocation.", "https://example.com/amazon-filing", Utc(2026, 2, 3), TimestampType.Published, 0.84m, 0.50m)
                ])),

            ["GOOGL"] = new MockResearchSeed(
                new CompanyProfile("GOOGL", "profile:GOOGL", "Alphabet Inc.", "NASDAQ", "0001652044", "Alphabet company profile", Utc(2026, 3, 1), TimestampType.Captured, 0.90m, 0.50m),
                new MarketSnapshot("GOOGL", "market:GOOGL", 176.45m, 12.9m, "The market is rewarding stable search economics and optionality across cloud and AI.", "Regulatory overhang still limits multiple expansion.", "Alphabet market snapshot", Utc(2026, 3, 15), TimestampType.AsOf, 0.80m, 0.50m),
                new FinancialStatement("GOOGL", "financials:GOOGL", 9.4m, 28.1m, 85.5m, 331.8m, 111.4m, "search and cloud profitability continue to support high free cash generation", "net cash and platform scale support aggressive AI investment", "Alphabet financial statements", Utc(2026, 1, 31), TimestampType.AsOf, 0.91m, 0.50m),
                new NewsCoverage("GOOGL", "Alphabet news coverage", Utc(2026, 3, 14), TimestampType.Published, 0.68m, 0.50m,
                [
                    new NewsItem("news:GOOGL:001", "Cloud margins continue to improve alongside AI demand", "Coverage highlighted better operating leverage in Google Cloud.", "https://example.com/alphabet-cloud", Utc(2026, 3, 14), TimestampType.Published, 0.69m, 0.50m),
                    new NewsItem("news:GOOGL:002", "Search monetization remains durable despite AI product shifts", "Analysts continued to view core search economics as resilient.", "https://example.com/alphabet-search", Utc(2026, 3, 11), TimestampType.Published, 0.67m, 0.50m)
                ]),
                new FilingCoverage("GOOGL", "Alphabet filing coverage", Utc(2026, 2, 2), TimestampType.Published, 0.85m, 0.50m,
                [
                    new FilingExcerpt("filing:GOOGL:001", "Alphabet 10-Q investment excerpt", "The filing stressed long-term AI investment while maintaining strong capital return capacity.", "https://example.com/alphabet-filing", Utc(2026, 2, 2), TimestampType.Published, 0.85m, 0.50m)
                ])),

            ["AMD"] = new MockResearchSeed(
                new CompanyProfile("AMD", "profile:AMD", "Advanced Micro Devices, Inc.", "NASDAQ", "0000002488", "AMD company profile", Utc(2026, 3, 1), TimestampType.Captured, 0.84m, 0.50m),
                new MarketSnapshot("AMD", "market:AMD", 184.12m, 18.1m, "The market is leaning into data-center and AI acceleration upside.", "Execution risk remains tied to share gains versus larger competitors.", "AMD market snapshot", Utc(2026, 3, 15), TimestampType.AsOf, 0.79m, 0.50m),
                new FinancialStatement("AMD", "financials:AMD", 4.1m, 22.4m, 9.7m, 28.6m, 5.4m, "data-center mix is supporting better margins and improving cash conversion", "balance sheet flexibility supports a faster product roadmap", "AMD financial statements", Utc(2026, 1, 31), TimestampType.AsOf, 0.86m, 0.50m),
                new NewsCoverage("AMD", "AMD news coverage", Utc(2026, 3, 13), TimestampType.Published, 0.66m, 0.50m,
                [
                    new NewsItem("news:AMD:001", "Server share gains continue in enterprise and cloud", "Channel checks indicated steady traction in higher-value compute workloads.", "https://example.com/amd-server", Utc(2026, 3, 13), TimestampType.Published, 0.67m, 0.50m),
                    new NewsItem("news:AMD:002", "Accelerator roadmap broadens investor confidence", "Coverage focused on credible AI product expansion.", "https://example.com/amd-ai", Utc(2026, 3, 9), TimestampType.Published, 0.65m, 0.50m)
                ]),
                new FilingCoverage("AMD", "AMD filing coverage", Utc(2026, 2, 5), TimestampType.Published, 0.79m, 0.50m,
                [
                    new FilingExcerpt("filing:AMD:001", "AMD 10-Q roadmap excerpt", "Management described continued investment in data-center and AI products with a focus on profitable mix.", "https://example.com/amd-filing", Utc(2026, 2, 5), TimestampType.Published, 0.79m, 0.50m)
                ])),

            ["XOM"] = new MockResearchSeed(
                new CompanyProfile("XOM", "profile:XOM", "Exxon Mobil Corporation", "NYSE", "0000034088", "Exxon Mobil company profile", Utc(2026, 3, 1), TimestampType.Captured, 0.85m, 0.50m),
                new MarketSnapshot("XOM", "market:XOM", 119.64m, 9.7m, "The market is rewarding capital discipline and steady shareholder returns.", "Commodity volatility remains the dominant variable for near-term sentiment.", "Exxon Mobil market snapshot", Utc(2026, 3, 15), TimestampType.AsOf, 0.77m, 0.50m),
                new FinancialStatement("XOM", "financials:XOM", 7.8m, 12.9m, 55.8m, 344.2m, 28.9m, "cash generation remains robust thanks to cost discipline and asset quality", "balance sheet strength supports dividends and buybacks through cycles", "Exxon Mobil financial statements", Utc(2026, 1, 31), TimestampType.AsOf, 0.87m, 0.50m),
                new NewsCoverage("XOM", "Exxon Mobil news coverage", Utc(2026, 3, 12), TimestampType.Published, 0.64m, 0.50m,
                [
                    new NewsItem("news:XOM:001", "Capital returns remain central to the investment case", "Coverage emphasized disciplined project screening and shareholder payouts.", "https://example.com/xom-returns", Utc(2026, 3, 12), TimestampType.Published, 0.64m, 0.50m),
                    new NewsItem("news:XOM:002", "Production growth remains tied to high-return assets", "Analysts focused on advantaged basin exposure and execution consistency.", "https://example.com/xom-production", Utc(2026, 3, 8), TimestampType.Published, 0.64m, 0.50m)
                ]),
                new FilingCoverage("XOM", "Exxon Mobil filing coverage", Utc(2026, 2, 4), TimestampType.Published, 0.80m, 0.50m,
                [
                    new FilingExcerpt("filing:XOM:001", "Exxon Mobil 10-Q capital allocation excerpt", "The filing reinforced disciplined spending and durable shareholder return priorities.", "https://example.com/xom-filing", Utc(2026, 2, 4), TimestampType.Published, 0.80m, 0.50m)
                ])),

            ["UNH"] = new MockResearchSeed(
                new CompanyProfile("UNH", "profile:UNH", "UnitedHealth Group Incorporated", "NYSE", "0000731766", "UnitedHealth company profile", Utc(2026, 3, 1), TimestampType.Captured, 0.86m, 0.50m),
                new MarketSnapshot("UNH", "market:UNH", 557.91m, 11.2m, "The market remains focused on diversified earnings durability across insurance and services.", "Medical cost trend surprises can shift sentiment quickly.", "UnitedHealth market snapshot", Utc(2026, 3, 15), TimestampType.AsOf, 0.79m, 0.50m),
                new FinancialStatement("UNH", "financials:UNH", 5.2m, 8.7m, 25.2m, 401.3m, 34.6m, "services diversification and disciplined execution continue to support resilient earnings", "cash generation and scale support steady capital return capacity", "UnitedHealth financial statements", Utc(2026, 1, 31), TimestampType.AsOf, 0.88m, 0.50m),
                new NewsCoverage("UNH", "UnitedHealth news coverage", Utc(2026, 3, 13), TimestampType.Published, 0.65m, 0.50m,
                [
                    new NewsItem("news:UNH:001", "Optum breadth continues to reinforce group earnings quality", "Coverage highlighted diversified service lines and execution consistency.", "https://example.com/unh-optum", Utc(2026, 3, 13), TimestampType.Published, 0.66m, 0.50m),
                    new NewsItem("news:UNH:002", "Care utilization trends remain the key debate", "Analysts pointed to cost trend monitoring as the main swing factor.", "https://example.com/unh-utilization", Utc(2026, 3, 10), TimestampType.Published, 0.64m, 0.50m)
                ]),
                new FilingCoverage("UNH", "UnitedHealth filing coverage", Utc(2026, 2, 6), TimestampType.Published, 0.81m, 0.50m,
                [
                    new FilingExcerpt("filing:UNH:001", "UnitedHealth 10-Q cost trend excerpt", "The filing described disciplined pricing and cost management while noting ongoing trend monitoring.", "https://example.com/unh-filing", Utc(2026, 2, 6), TimestampType.Published, 0.81m, 0.50m)
                ]))
        };

    private static MockResearchSeed BuildGeneratedSeed(string identifier, ResolvedTickerInfo? resolvedTicker)
    {
        var symbol = identifier.Trim().ToUpperInvariant();
        var companyName = string.IsNullOrWhiteSpace(resolvedTicker?.CompanyName) ? symbol : resolvedTicker!.CompanyName;
        var exchange = string.IsNullOrWhiteSpace(resolvedTicker?.Exchange) ? "US" : resolvedTicker!.Exchange!;
        var cik = string.IsNullOrWhiteSpace(resolvedTicker?.CikPadded) ? symbol.PadLeft(10, '0')[..10] : resolvedTicker!.CikPadded;

        var positiveTilt = StableFraction(symbol, "positive");
        var secondaryTilt = StableFraction(symbol, "secondary");
        var price = DecimalRange(symbol, "price", 24m, 420m);
        var returnPercent = DecimalRange(symbol, "return", -12m, 28m);
        var revenueGrowth = DecimalRange(symbol, "revenue", -3m, 22m);
        var operatingMargin = DecimalRange(symbol, "margin", 6m, 33m);
        var freeCashFlow = DecimalRange(symbol, "fcf", 1.2m, 48m);
        var cash = DecimalRange(symbol, "cash", 4m, 160m);
        var debt = DecimalRange(symbol, "debt", 1m, 70m);
        var stanceScore = Math.Round(DecimalRange(symbol, "stance", -0.15m, 0.70m), 2, MidpointRounding.AwayFromZero);
        var qualityScore = Math.Round(DecimalRange(symbol, "quality", 0.66m, 0.88m), 2, MidpointRounding.AwayFromZero);

        return new MockResearchSeed(
            new CompanyProfile(symbol, $"profile:{symbol}", companyName, exchange, cik, $"{companyName} company profile", Utc(2026, 3, 1), TimestampType.Captured, qualityScore, Math.Max(0.10m, stanceScore)),
            new MarketSnapshot(
                symbol,
                $"market:{symbol}",
                Math.Round(price, 2, MidpointRounding.AwayFromZero),
                Math.Round(returnPercent, 2, MidpointRounding.AwayFromZero),
                positiveTilt >= 0.50m ? "recent trading has stayed constructive around the current setup" : "recent trading has remained mixed around the current setup",
                secondaryTilt >= 0.50m ? "valuation and execution sensitivity remain the main watch items" : "short-term positioning remains sensitive to near-term execution and sentiment shifts",
                $"{companyName} market snapshot",
                Utc(2026, 3, 15),
                TimestampType.AsOf,
                Math.Round(Math.Clamp(qualityScore - 0.03m, 0.60m, 0.85m), 2, MidpointRounding.AwayFromZero),
                Math.Round(Math.Clamp(stanceScore, -1m, 1m), 2, MidpointRounding.AwayFromZero)),
            new FinancialStatement(
                symbol,
                $"financials:{symbol}",
                Math.Round(revenueGrowth, 2, MidpointRounding.AwayFromZero),
                Math.Round(operatingMargin, 2, MidpointRounding.AwayFromZero),
                Math.Round(freeCashFlow, 2, MidpointRounding.AwayFromZero),
                Math.Round(cash, 2, MidpointRounding.AwayFromZero),
                Math.Round(debt, 2, MidpointRounding.AwayFromZero),
                operatingMargin >= 20m ? "strong operating margins continue to support resilient cash generation" : "operating profitability remains positive and supports a credible cash-generation profile",
                cash >= debt ? "cash balances currently offset debt obligations and support balance-sheet flexibility" : "debt obligations remain a constraint relative to current liquidity",
                $"{companyName} financial statements",
                Utc(2026, 1, 31),
                TimestampType.AsOf,
                Math.Round(Math.Clamp(qualityScore + 0.02m, 0.65m, 0.90m), 2, MidpointRounding.AwayFromZero),
                Math.Round(Math.Clamp(stanceScore + 0.10m, -1m, 1m), 2, MidpointRounding.AwayFromZero)),
            new NewsCoverage(
                symbol,
                $"{companyName} news coverage",
                Utc(2026, 3, 12),
                TimestampType.Published,
                Math.Round(Math.Clamp(qualityScore - 0.08m, 0.55m, 0.82m), 2, MidpointRounding.AwayFromZero),
                Math.Round(Math.Clamp(stanceScore, -1m, 1m), 2, MidpointRounding.AwayFromZero),
                [
                    new NewsItem($"news:{symbol}:001", $"{companyName} maintains constructive operating momentum", "Coverage emphasizes execution consistency and current business momentum.", null, Utc(2026, 3, 12), TimestampType.Published, Math.Round(Math.Clamp(qualityScore - 0.07m, 0.55m, 0.82m), 2, MidpointRounding.AwayFromZero), Math.Round(Math.Clamp(stanceScore, -1m, 1m), 2, MidpointRounding.AwayFromZero)),
                    new NewsItem($"news:{symbol}:002", $"{companyName} faces ongoing valuation and execution debates", "Coverage highlights the main factors that could temper conviction in the near term.", null, Utc(2026, 3, 9), TimestampType.Published, Math.Round(Math.Clamp(qualityScore - 0.10m, 0.50m, 0.80m), 2, MidpointRounding.AwayFromZero), Math.Round(Math.Clamp(stanceScore - 0.10m, -1m, 1m), 2, MidpointRounding.AwayFromZero))
                ]),
            new FilingCoverage(
                symbol,
                $"{companyName} filing coverage",
                Utc(2026, 2, 4),
                TimestampType.Published,
                Math.Round(Math.Clamp(qualityScore - 0.02m, 0.60m, 0.88m), 2, MidpointRounding.AwayFromZero),
                0m,
                [
                    new FilingExcerpt($"filing:{symbol}:001", $"{companyName} recent filing excerpt", "The latest filing continues to frame capital allocation, operating priorities, and the main disclosed risks.", null, Utc(2026, 2, 4), TimestampType.Published, Math.Round(Math.Clamp(qualityScore - 0.02m, 0.60m, 0.88m), 2, MidpointRounding.AwayFromZero), 0m)
                ]));
    }

    private static decimal DecimalRange(string identifier, string salt, decimal minimum, decimal maximum) =>
        minimum + ((maximum - minimum) * StableFraction(identifier, salt));

    private static decimal StableFraction(string identifier, string salt)
    {
        unchecked
        {
            var value = 17;
            foreach (var character in $"{identifier}:{salt}".ToUpperInvariant())
            {
                value = (value * 31) + character;
            }

            var normalized = Math.Abs(value % 10_000);
            return normalized / 10_000m;
        }
    }

    private static DateTimeOffset Utc(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, TimeSpan.Zero);
}

public sealed record MockResearchSeed(
    CompanyProfile CompanyProfile,
    MarketSnapshot MarketSnapshot,
    FinancialStatement FinancialStatement,
    NewsCoverage NewsCoverage,
    FilingCoverage FilingCoverage);
