using Aira.Application.Models;
using Aira.Domain.Enums;
using Aira.Infrastructure.Persistence;

namespace Aira.Tests.Unit.Chat;

public sealed class InMemoryReportChatThreadStoreTests
{
    [Fact]
    public void Get_Returns_Null_After_Expiration_Window()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 3, 18, 12, 0, 0, TimeSpan.Zero));
        var store = new InMemoryReportChatThreadStore(timeProvider);
        var report = CreateReport();

        var thread = store.Create(Guid.NewGuid(), report);
        Assert.NotNull(store.Get(thread.ThreadId));

        timeProvider.Advance(TimeSpan.FromMinutes(61));

        Assert.Null(store.Get(thread.ThreadId));
    }

    [Fact]
    public void Get_Refreshes_LastUpdatedUtc()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 3, 18, 12, 0, 0, TimeSpan.Zero));
        var store = new InMemoryReportChatThreadStore(timeProvider);
        var report = CreateReport();

        var thread = store.Create(Guid.NewGuid(), report);
        timeProvider.Advance(TimeSpan.FromMinutes(10));
        var loaded = store.Get(thread.ThreadId);

        Assert.NotNull(loaded);
        Assert.Equal(timeProvider.GetUtcNow(), loaded!.LastUpdatedUtc);
    }

    private static CompletedResearchResult CreateReport()
    {
        return new CompletedResearchResult(
            "Apple Inc.",
            "AAPL",
            "Apple has a resilient ecosystem.",
            InvestmentSignal.Bullish,
            92,
            new DateTimeOffset(2026, 3, 18, 12, 0, 0, TimeSpan.Zero),
            ["Strong services momentum.", "Balance sheet remains supportive."],
            ["profile:AAPL", "financials:AAPL"],
            [],
            [
                new ResearchTraceEvent(WorkflowStep.IdentifierResolution, TraceStatus.Completed, "Identifier resolved.", [], new DateTimeOffset(2026, 3, 18, 12, 0, 1, TimeSpan.Zero)),
                new ResearchTraceEvent(WorkflowStep.Completion, TraceStatus.Completed, "Completed.", [], new DateTimeOffset(2026, 3, 18, 12, 0, 2, TimeSpan.Zero))
            ],
            [
                new SourceDetailResponse("profile:AAPL", SourceType.CompanyProfile, "Apple profile", new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), TimestampType.Captured, 0.90m, null, ProviderOrigin.Mock, "SeededMock", false),
                new SourceDetailResponse("financials:AAPL", SourceType.FinancialStatement, "Apple financials", new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero), TimestampType.AsOf, 0.92m, null, ProviderOrigin.Mock, "SeededMock", false)
            ],
            [
                new ProviderUsageResponse(SourceType.CompanyProfile, ProviderResultStatus.Success, ProviderOrigin.Mock, "SeededMock", false, null),
                new ProviderUsageResponse(SourceType.FinancialStatement, ProviderResultStatus.Success, ProviderOrigin.Mock, "SeededMock", false, null)
            ],
            [
                new InsightDetailResponse("Strong services momentum.", ["profile:AAPL"]),
                new InsightDetailResponse("Balance sheet remains supportive.", ["financials:AAPL"])
            ],
            new ThesisDetailResponse("Thesis supported by profile and financials.", ["profile:AAPL", "financials:AAPL"]));
    }
}
