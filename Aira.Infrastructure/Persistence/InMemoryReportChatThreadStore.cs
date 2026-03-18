using System.Collections.Concurrent;
using Aira.Application.Abstractions;
using Aira.Application.Models;

namespace Aira.Infrastructure.Persistence;

public sealed class InMemoryReportChatThreadStore : IReportChatThreadStore
{
    private static readonly TimeSpan Expiration = TimeSpan.FromMinutes(60);

    private readonly ConcurrentDictionary<string, ReportChatThreadState> _threads = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public InMemoryReportChatThreadStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public ReportChatThreadState Create(Guid jobId, CompletedResearchResult reportSnapshot)
    {
        CleanupExpired();

        var now = _timeProvider.GetUtcNow();
        var thread = new ReportChatThreadState
        {
            ThreadId = Guid.NewGuid().ToString("N"),
            JobId = jobId,
            ReportSnapshot = reportSnapshot,
            CreatedAtUtc = now,
            LastUpdatedUtc = now
        };

        _threads[thread.ThreadId] = thread;
        return thread;
    }

    public ReportChatThreadState? Get(string threadId)
    {
        if (!_threads.TryGetValue(threadId, out var thread))
        {
            return null;
        }

        if (IsExpired(thread))
        {
            Remove(threadId);
            return null;
        }

        thread.LastUpdatedUtc = _timeProvider.GetUtcNow();
        return thread;
    }

    public bool Remove(string threadId)
    {
        if (!_threads.TryRemove(threadId, out var thread))
        {
            return false;
        }

        lock (thread.StreamSync)
        {
            if (thread.ActiveStreamCancellation is not null)
            {
                try
                {
                    thread.ActiveStreamCancellation.Cancel();
                }
                catch
                {
                }

                thread.ActiveStreamCancellation.Dispose();
                thread.ActiveStreamCancellation = null;
            }
        }

        return true;
    }

    private void CleanupExpired()
    {
        foreach (var pair in _threads)
        {
            if (IsExpired(pair.Value))
            {
                Remove(pair.Key);
            }
        }
    }

    private bool IsExpired(ReportChatThreadState thread) =>
        _timeProvider.GetUtcNow() - thread.LastUpdatedUtc > Expiration;
}
