using Azure.Messaging.EventHubs.Consumer;
using Microsoft.Extensions.Logging;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Utils;
using Azure.Security.KeyVault.Secrets;

public class EventHubConsumer // To Be Implemented TBD
{
    private readonly string _eventHubConnectionString;
    private readonly string _eventHubName;
    private readonly Logger _logger;

    public EventHubConsumer(IConfiguration configuration, Logger logger, SecretClient secretClient)
    {
        _eventHubConnectionString = configuration["EventHub:ConnectionString"] ?? secretClient?.GetSecret("EventHubConnectionString")?.Value?.Value ?? throw new ArgumentNullException(nameof(configuration), "The 'EventHub:ConnectionString' property is missing or null");
        _eventHubName = configuration["EventHub:EventHubName"] ?? secretClient?.GetSecret("EventHubName")?.Value?.Value ?? throw new ArgumentNullException(nameof(configuration), "The 'EventHub:EventHubName' property is missing or null");
        _logger = logger;
    }

    public async Task StartConsuming(CancellationToken cancellationToken)
    {
        using (var log = _logger.StartMethod(nameof(EventHubConsumer) + ".StartConsuming"))
        {
            var consumerClient = new EventHubConsumerClient(EventHubConsumerClient.DefaultConsumerGroupName, _eventHubConnectionString, _eventHubName);

            try
            {
                await foreach (PartitionEvent partitionEvent in consumerClient.ReadEventsAsync(cancellationToken))
                {
                    string eventData = Encoding.UTF8.GetString(partitionEvent.Data.Body.ToArray());
                    log.SetAttribute("eventData", eventData);
                    log.SetAttribute("status", "received");
                }
            }
            catch (Exception ex)
            {
                log.SetAttribute("status", "error");
                log.SetAttribute("errorMessage", ex.Message);
            }
        }
    }
}
