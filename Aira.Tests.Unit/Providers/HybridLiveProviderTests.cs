using System.Net;
using System.Text;
using Aira.Application.Services;
using Aira.Domain.Enums;
using Aira.Infrastructure.Clients;
using Aira.Infrastructure.Options;
using Aira.Infrastructure.Providers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aira.Tests.Unit.Providers;

public sealed class HybridLiveProviderTests
{
    private readonly MockResearchSeedCatalog _catalog = new();

    [Fact]
    public async Task HybridCompanyProfileProvider_Uses_Sec_Identity_When_Configured()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var secClient = CreateSecClient(
            new RoutedHandler(request =>
            {
                if (request.RequestUri!.AbsoluteUri.Contains("company_tickers_exchange.json", StringComparison.Ordinal))
                {
                    return JsonResponse("""
                        {
                          "fields": ["cik", "name", "ticker", "exchange"],
                          "data": [[320193, "Apple SEC Name", "AAPL", "Nasdaq"]]
                        }
                        """);
                }

                if (request.RequestUri.AbsoluteUri.Contains("/submissions/CIK0000320193.json", StringComparison.Ordinal))
                {
                    return JsonResponse("""
                        {
                          "name": "Apple SEC Name",
                          "tickers": ["AAPL"],
                          "exchanges": ["Nasdaq"],
                          "filings": {
                            "recent": {
                              "form": ["10-Q"],
                              "filingDate": ["2026-01-31"],
                              "accessionNumber": ["0000320193-26-000001"],
                              "primaryDocument": ["aapl10q.htm"],
                              "primaryDocDescription": ["Quarterly report"]
                            }
                          }
                        }
                        """);
                }

                throw new InvalidOperationException($"Unexpected URI {request.RequestUri}");
            }),
            cache);

        var provider = new HybridCompanyProfileProvider(
            new MockCompanyProfileProvider(_catalog),
            secClient,
            TimeProvider.System,
            NullLogger<HybridCompanyProfileProvider>.Instance);

        var result = await provider.GetAsync("AAPL", CancellationToken.None);

        Assert.Equal(ProviderResultStatus.Success, result.Status);
        Assert.Equal(ProviderOrigin.Live, result.Origin);
        Assert.Equal("SEC EDGAR", result.ProviderName);
        Assert.Equal("Apple SEC Name", result.Value!.CompanyName);
        Assert.Contains("Nasdaq", result.Value.Sector);
        Assert.Equal(TimestampType.Captured, result.Value.TimestampType);
    }

    [Fact]
    public async Task HybridMarketSnapshotProvider_Uses_TwelveData_When_Configured()
    {
        var provider = new HybridMarketSnapshotProvider(
            new MockMarketSnapshotProvider(_catalog, new JobExecutionContextAccessor()),
            new HttpClient(new RoutedHandler(_ => JsonResponse("""
                {
                  "symbol": "AAPL",
                  "close": "205.55",
                  "percent_change": "1.80",
                  "datetime": "2026-03-18T14:30:00Z"
                }
                """))),
            Options.Create(new TwelveDataOptions
            {
                Enabled = true,
                ApiKey = "test-key",
                BaseUrl = "https://api.twelvedata.com/"
            }),
            TimeProvider.System,
            NullLogger<HybridMarketSnapshotProvider>.Instance);

        var result = await provider.GetAsync("AAPL", CancellationToken.None);

        Assert.Equal(ProviderResultStatus.Success, result.Status);
        Assert.Equal(ProviderOrigin.Live, result.Origin);
        Assert.Equal("Twelve Data", result.ProviderName);
        Assert.Equal(205.55m, result.Value!.PriceUsd);
        Assert.Equal("market:AAPL", result.Value.SourceId);
        Assert.NotEqual(221.18m, result.Value.PriceUsd);
    }

    [Fact]
    public async Task HybridMarketSnapshotProvider_Falls_Back_To_Mock_When_Live_Request_Fails()
    {
        var provider = new HybridMarketSnapshotProvider(
            new MockMarketSnapshotProvider(_catalog, new JobExecutionContextAccessor()),
            new HttpClient(new RoutedHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError))),
            Options.Create(new TwelveDataOptions
            {
                Enabled = true,
                ApiKey = "test-key",
                BaseUrl = "https://api.twelvedata.com/"
            }),
            TimeProvider.System,
            NullLogger<HybridMarketSnapshotProvider>.Instance);

        var result = await provider.GetAsync("AAPL", CancellationToken.None);

        Assert.Equal(ProviderResultStatus.Success, result.Status);
        Assert.Equal(ProviderOrigin.Mock, result.Origin);
        Assert.True(result.FallbackOccurred);
        Assert.Equal(221.18m, result.Value!.PriceUsd);
    }

    [Fact]
    public async Task HybridNewsProvider_Uses_AlphaVantage_When_Configured()
    {
        var provider = new HybridNewsProvider(
            new MockNewsProvider(_catalog),
            new HttpClient(new RoutedHandler(_ => JsonResponse("""
                {
                  "feed": [
                    {
                      "title": "Apple launch cycle remains on track",
                      "summary": "Suppliers highlighted steady flagship demand.",
                      "url": "https://example.com/aapl-news-1",
                      "time_published": "20260318T120000",
                      "overall_sentiment_score": 0.20,
                      "ticker_sentiment": [
                        {
                          "ticker": "AAPL",
                          "relevance_score": "0.80",
                          "ticker_sentiment_score": "0.40"
                        }
                      ]
                    },
                    {
                      "title": "Services attach remains durable",
                      "summary": "Subscriptions remained a margin support lever.",
                      "url": "https://example.com/aapl-news-2",
                      "time_published": "20260317T120000",
                      "overall_sentiment_score": 0.10,
                      "ticker_sentiment": [
                        {
                          "ticker": "AAPL",
                          "relevance_score": "0.70",
                          "ticker_sentiment_score": "0.30"
                        }
                      ]
                    }
                  ]
                }
                """))),
            Options.Create(new AlphaVantageOptions
            {
                Enabled = true,
                ApiKey = "test-key",
                BaseUrl = "https://www.alphavantage.co/"
            }),
            TimeProvider.System,
            NullLogger<HybridNewsProvider>.Instance);

        var result = await provider.GetAsync("AAPL", CancellationToken.None);

        Assert.Equal(ProviderResultStatus.Success, result.Status);
        Assert.Equal(ProviderOrigin.Live, result.Origin);
        Assert.Equal("Alpha Vantage", result.ProviderName);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Equal("news:AAPL:001", result.Value.Items[0].SourceId);
        Assert.True(result.Value.QualityScore >= 0.30m);
    }

    [Fact]
    public async Task HybridFinancialStatementProvider_Uses_Sec_CompanyFacts_When_Configured()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new HybridFinancialStatementProvider(
            new MockFinancialStatementProvider(_catalog),
            CreateSecClient(
                new RoutedHandler(request =>
                {
                    if (request.RequestUri!.AbsoluteUri.Contains("company_tickers_exchange.json", StringComparison.Ordinal))
                    {
                        return JsonResponse("""
                            {
                              "fields": ["cik", "name", "ticker", "exchange"],
                              "data": [[320193, "Apple Inc.", "AAPL", "Nasdaq"]]
                            }
                            """);
                    }

                    if (request.RequestUri.AbsoluteUri.Contains("/companyfacts/CIK0000320193.json", StringComparison.Ordinal))
                    {
                        return JsonResponse("""
                            {
                              "entityName": "Apple Inc.",
                              "facts": {
                                "us-gaap": {
                                  "Revenues": {
                                    "units": {
                                      "USD": [
                                        { "val": 100000000000, "fy": 2025, "fp": "FY", "form": "10-K", "filed": "2026-01-31", "start": "2024-09-29", "end": "2025-09-27" },
                                        { "val": 90000000000, "fy": 2024, "fp": "FY", "form": "10-K", "filed": "2025-01-31", "start": "2023-10-01", "end": "2024-09-28" }
                                      ]
                                    }
                                  },
                                  "OperatingIncomeLoss": {
                                    "units": {
                                      "USD": [
                                        { "val": 30000000000, "fy": 2025, "fp": "FY", "form": "10-K", "filed": "2026-01-31", "start": "2024-09-29", "end": "2025-09-27" }
                                      ]
                                    }
                                  },
                                  "NetCashProvidedByUsedInOperatingActivities": {
                                    "units": {
                                      "USD": [
                                        { "val": 35000000000, "fy": 2025, "fp": "FY", "form": "10-K", "filed": "2026-01-31", "start": "2024-09-29", "end": "2025-09-27" }
                                      ]
                                    }
                                  },
                                  "PaymentsToAcquirePropertyPlantAndEquipment": {
                                    "units": {
                                      "USD": [
                                        { "val": 5000000000, "fy": 2025, "fp": "FY", "form": "10-K", "filed": "2026-01-31", "start": "2024-09-29", "end": "2025-09-27" }
                                      ]
                                    }
                                  },
                                  "CashAndCashEquivalentsAtCarryingValue": {
                                    "units": {
                                      "USD": [
                                        { "val": 50000000000, "fy": 2025, "fp": "FY", "form": "10-K", "filed": "2026-01-31", "end": "2025-09-27" }
                                      ]
                                    }
                                  },
                                  "LongTermDebtAndCapitalLeaseObligations": {
                                    "units": {
                                      "USD": [
                                        { "val": 40000000000, "fy": 2025, "fp": "FY", "form": "10-K", "filed": "2026-01-31", "end": "2025-09-27" }
                                      ]
                                    }
                                  }
                                }
                              }
                            }
                            """);
                    }

                    throw new InvalidOperationException($"Unexpected URI {request.RequestUri}");
                }),
                cache),
            NullLogger<HybridFinancialStatementProvider>.Instance);

        var result = await provider.GetAsync("AAPL", CancellationToken.None);

        Assert.Equal(ProviderResultStatus.Success, result.Status);
        Assert.Equal(ProviderOrigin.Live, result.Origin);
        Assert.Equal("SEC EDGAR", result.ProviderName);
        Assert.Equal("financials:AAPL", result.Value!.SourceId);
        Assert.Equal(11.11m, result.Value.RevenueGrowthPercent);
        Assert.Equal(30.00m, result.Value.OperatingMarginPercent);
        Assert.Equal(30.00m, result.Value.FreeCashFlowBillions);
    }

    [Fact]
    public async Task HybridFilingExcerptProvider_Uses_Sec_Submissions_When_Configured()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new HybridFilingExcerptProvider(
            new MockFilingExcerptProvider(_catalog),
            CreateSecClient(
                new RoutedHandler(request =>
                {
                    if (request.RequestUri!.AbsoluteUri.Contains("company_tickers_exchange.json", StringComparison.Ordinal))
                    {
                        return JsonResponse("""
                            {
                              "fields": ["cik", "name", "ticker", "exchange"],
                              "data": [[320193, "Apple Inc.", "AAPL", "Nasdaq"]]
                            }
                            """);
                    }

                    if (request.RequestUri.AbsoluteUri.Contains("/submissions/CIK0000320193.json", StringComparison.Ordinal))
                    {
                        return JsonResponse("""
                            {
                              "name": "Apple Inc.",
                              "tickers": ["AAPL"],
                              "exchanges": ["Nasdaq"],
                              "filings": {
                                "recent": {
                                  "form": ["10-Q", "8-K"],
                                  "filingDate": ["2026-01-31", "2026-01-20"],
                                  "accessionNumber": ["0000320193-26-000001", "0000320193-26-000002"],
                                  "primaryDocument": ["aapl10q.htm", "aapl8k.htm"],
                                  "primaryDocDescription": ["Quarterly report", "Current report"]
                                }
                              }
                            }
                            """);
                    }

                    throw new InvalidOperationException($"Unexpected URI {request.RequestUri}");
                }),
                cache),
            TimeProvider.System,
            NullLogger<HybridFilingExcerptProvider>.Instance);

        var result = await provider.GetAsync("AAPL", CancellationToken.None);

        Assert.Equal(ProviderResultStatus.Success, result.Status);
        Assert.Equal(ProviderOrigin.Live, result.Origin);
        Assert.Equal("SEC EDGAR", result.ProviderName);
        Assert.Single(result.Value!.Items);
        Assert.Equal("filing:AAPL:001", result.Value.Items[0].SourceId);
        Assert.Contains("sec.gov/Archives/edgar/data", result.Value.Items[0].Url);
    }

    private static SecEdgarClient CreateSecClient(HttpMessageHandler handler, MemoryCache cache) =>
        new(
            new HttpClient(handler),
            Options.Create(new SecEdgarOptions
            {
                Enabled = true,
                DataBaseUrl = "https://data.sec.gov/",
                MappingUrl = "https://www.sec.gov/files/company_tickers_exchange.json",
                UserAgent = "AIRA-Test/1.0 test@example.com"
            }),
            cache,
            NullLogger<SecEdgarClient>.Instance);

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class RoutedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _route;

        public RoutedHandler(Func<HttpRequestMessage, HttpResponseMessage> route)
        {
            _route = route;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_route(request));
    }
}
