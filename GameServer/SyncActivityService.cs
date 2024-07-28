using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Utils;
public class SyncActivityService : BackgroundService //To Be Implemented TBD
{
    private readonly IDatabase _db;
    private readonly HttpClient _client;
    private readonly Logger _logger;
    private readonly TimeSpan _syncInterval = TimeSpan.FromMinutes(7); // 동기화 간격
    private readonly string _umsUrl;

    public SyncActivityService(IDatabase db,  IHttpClientFactory clientFactory, Logger logger, IConfiguration configuration)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _client = clientFactory?.CreateClient() ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _umsUrl = configuration?["UMSURL:Value"] ?? throw new ArgumentNullException("UMSURL configuration is missing");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using (var log = _logger.StartMethod(nameof(SyncActivityService) + ".ExecuteAsync"))
            {
                await SyncActivityData(stoppingToken);
                log.SetAttribute("status", "completed");
                await Task.Delay(_syncInterval, stoppingToken);
            }
        }
    }

    private async Task SyncActivityData(CancellationToken stoppingToken)
    {
        using (var log = _logger.StartMethod(nameof(SyncActivityService) + ".SyncActivityData"))
        {
            var server = _db.Multiplexer.GetServer(_db.Multiplexer.GetEndPoints()[0]);
            var keys = server.Keys(pattern: "user:*:activity");

            foreach (var key in keys)
            {
                if (stoppingToken.IsCancellationRequested) break;

                var userActivityData = await _db.StringGetAsync(key);
                if (!userActivityData.IsNullOrEmpty)
                {
                    var activityData = JsonSerializer.Deserialize<ActivityData>(userActivityData.ToString());
                    if (activityData != null)
                    {
                        log.SetAttribute("userId", activityData.UserId);
                    }
                    else
                    {
                        log.SetAttribute("status", "failed");
                        continue;
                    }
                    var response = await _client.PostAsJsonAsync($"{_umsUrl}/api/sync-activity", activityData, stoppingToken);
                    log.SetAttribute("status", response.IsSuccessStatusCode ? "success" : "failed");
                }
            }
        }

    }
}
