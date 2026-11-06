using Proofsheet.Application.Abstractions;
using Proofsheet.Application.Models;
using Proofsheet.Application.Services;
using Proofsheet.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Proofsheet.Tests.Unit.Submission;

public sealed class ResearchJobSubmissionServiceTests
{
    [Fact]
    public async Task SubmitAsync_Returns_QueueCapacityExceeded_And_Removes_Provisional_Record()
    {
        var store = new TestJobStore();
        var service = new ResearchJobSubmissionService(
            new IdentifierValidator(),
            new FakeTickerDirectory(),
            new FakeExecutionModeResolver(),
            store,
            new FakeQueue(false),
            TimeProvider.System,
            Options.Create(new ResearchExecutionOptions()),
            NullLogger<ResearchJobSubmissionService>.Instance);

        var result = await service.SubmitAsync("AAPL", null, CancellationToken.None);

        Assert.False(result.IsAccepted);
        Assert.Equal("QueueCapacityExceeded", result.Problem!.ErrorCode);
        Assert.Empty(store.Records);
    }

    [Fact]
    public async Task SubmitAsync_Accepts_Valid_Request()
    {
        var service = new ResearchJobSubmissionService(
            new IdentifierValidator(),
            new FakeTickerDirectory(),
            new FakeExecutionModeResolver(),
            new TestJobStore(),
            new FakeQueue(true),
            TimeProvider.System,
            Options.Create(new ResearchExecutionOptions()),
            NullLogger<ResearchJobSubmissionService>.Instance);

        var result = await service.SubmitAsync("msft", ResearchMode.Autonomous, CancellationToken.None);

        Assert.True(result.IsAccepted);
        Assert.Equal(JobStatus.Queued, result.AcceptedResponse!.Status);
        Assert.Contains("/api/v1/research-jobs/", result.AcceptedResponse.StatusUrl);
    }

    private sealed class FakeQueue : IResearchJobQueue
    {
        private readonly bool _canEnqueue;

        public FakeQueue(bool canEnqueue)
        {
            _canEnqueue = canEnqueue;
        }

        public bool TryEnqueue(ResearchJobQueueItem item) => _canEnqueue;

        public ValueTask<ResearchJobQueueItem> DequeueAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTickerDirectory : ITickerDirectoryService
    {
        public Task<ResolvedTickerInfo?> ResolveAsync(string identifier, CancellationToken cancellationToken = default) =>
            Task.FromResult<ResolvedTickerInfo?>(new(identifier.ToUpperInvariant(), $"{identifier.ToUpperInvariant()} Corp", "NASDAQ", "0000000001"));

        public Task<IReadOnlyList<TickerSearchItemResponse>> SearchAsync(string query, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TickerSearchItemResponse>>(Array.Empty<TickerSearchItemResponse>());
    }

    private sealed class FakeExecutionModeResolver : IResearchExecutionModeResolver
    {
        public ResearchExecutionMode Resolve() => ResearchExecutionMode.Mock;
    }

    private sealed class TestJobStore : IResearchJobStore
    {
        public Dictionary<Guid, ResearchJobRecord> Records { get; } = new();

        public bool TryAdd(ResearchJobRecord record) => Records.TryAdd(record.JobId, record);

        public bool TryRemove(Guid jobId) => Records.Remove(jobId);

        public ResearchJobRecord? Get(Guid jobId) => Records.GetValueOrDefault(jobId);

        public bool TryUpdate(Guid jobId, Func<ResearchJobRecord, ResearchJobRecord> update)
        {
            if (!Records.TryGetValue(jobId, out var record))
            {
                return false;
            }

            Records[jobId] = update(record);
            return true;
        }
    }
}
