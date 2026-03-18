using System.Threading.Channels;
using Aira.Application.Abstractions;
using Aira.Application.Models;
using Microsoft.Extensions.Options;

namespace Aira.Infrastructure.Queueing;

public sealed class ChannelResearchJobQueue : IResearchJobQueue
{
    private readonly Channel<ResearchJobQueueItem> _channel;

    public ChannelResearchJobQueue(IOptions<ResearchExecutionOptions> options)
    {
        _channel = Channel.CreateBounded<ResearchJobQueueItem>(
            new BoundedChannelOptions(options.Value.QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            });
    }

    public bool TryEnqueue(ResearchJobQueueItem item) => _channel.Writer.TryWrite(item);

    public ValueTask<ResearchJobQueueItem> DequeueAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);
}
