using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;
using Common.Utils;

public class EventProcessingService : BackgroundService //To Be Implemented TBD
{
    private readonly EventHubConsumer _consumer;
    private readonly Logger _logger;

    public EventProcessingService(EventHubConsumer consumer, Logger logger)
    {
        _consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var log = _logger.StartMethod(nameof(EventProcessingService) + ".ExecuteAsync");
        try
        {
            await _consumer.StartConsuming(stoppingToken);
            log.SetAttribute("status", "completed");
        }
        catch (Exception ex)
        {
            log.SetAttribute("status", "error");
            log.SetAttribute("errorMessage", ex.Message);
            throw;
        }
    }
}
