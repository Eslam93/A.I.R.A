using Aira.Application.Abstractions;
using Aira.Application.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aira.Infrastructure.Execution;

public sealed class ResearchJobWorkerHostedService : BackgroundService
{
    private readonly IResearchJobQueue _jobQueue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ResearchExecutionOptions _options;
    private readonly ILogger<ResearchJobWorkerHostedService> _logger;

    public ResearchJobWorkerHostedService(
        IResearchJobQueue jobQueue,
        IServiceScopeFactory scopeFactory,
        IOptions<ResearchExecutionOptions> options,
        ILogger<ResearchJobWorkerHostedService> logger)
    {
        _jobQueue = jobQueue;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workers = Enumerable.Range(0, _options.MaxConcurrentJobs)
            .Select(workerId => RunWorkerAsync(workerId, stoppingToken))
            .ToArray();

        return Task.WhenAll(workers);
    }

    private async Task RunWorkerAsync(int workerId, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            ResearchJobQueueItem item;

            try
            {
                item = await _jobQueue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IResearchJobProcessor>();

            _logger.LogInformation("Worker {WorkerId} picked up job {JobId}", workerId, item.JobId);
            await processor.ProcessAsync(item, stoppingToken);
        }
    }
}
