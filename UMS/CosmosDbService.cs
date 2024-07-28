using Microsoft.Azure.Cosmos;
using Azure.Identity;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Common.Utils;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.DataProtection;
using System.Text.Json; // Add this using directive for JsonSerializer
public class CosmosDbService
{
    private readonly Container _container;
    private readonly Logger _logger;

    private readonly CosmosClient _client;

    private readonly SecretClient _secretClient;
    public CosmosDbService(IConfiguration configuration, SecretClient secretClient, bool isDevelopment = false)
    {
        if (null == configuration)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        string serviceName = configuration?["Logging:ServiceName:Value"] ?? string.Empty;
        _logger = new Logger(serviceName);
        _secretClient = secretClient;

        string cosmosEndpoint = configuration?["AzureFileServer:ConnectionStrings:CosmosEndpoint"] ?? secretClient.GetSecret("CosmosEndpoint").Value?.Value
            ?? throw new ArgumentNullException("CosmosEndpoint configuration is missing");
        string databaseName = configuration?["AzureFileServer:ConnectionStrings:CosmosDatabaseName"]  ?? secretClient.GetSecret("CosmosDatabaseName").Value?.Value
            ?? throw new ArgumentNullException("CosmosDatabaseName configuration is missing");
        string containerName = configuration?["AzureFileServer:ConnectionStrings:CosmosContainerName"]  ?? secretClient.GetSecret("CosmosContainerName").Value?.Value
            ?? throw new ArgumentNullException("CosmosContainerName configuration is missing");
        
        if(isDevelopment)
        {
            string connectionString = configuration?["AzureFileServer:ConnectionStrings:CosmosConnectionString"]
            ?? throw new ArgumentNullException("CosmosConnectionString configuration is missing");
            _client = new CosmosClient(connectionString, new CosmosClientOptions());
            _container = _client.GetContainer(databaseName, containerName);
        }
        else
        {
            _client = new CosmosClient(cosmosEndpoint, new DefaultAzureCredential());
            _container = _client.GetContainer(databaseName, containerName);
        }

        
    }

    public async Task AddUserAsync(User user)
    {
        if (user == null)
        {
            throw new ArgumentNullException(nameof(user), "User cannot be null");
        }

        using var log = _logger.StartMethod(nameof(AddUserAsync));
        log.SetAttribute("userId", user.userid);

        if (string.IsNullOrEmpty(user.id))
        {
            user.id = user.userid;
        }

        try
        {
            await _container.CreateItemAsync(user, new PartitionKey(user.id));
            log.SetAttribute("status", "success");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            log.SetAttribute("status", "conflict");
            throw new InvalidOperationException($"A user with ID {user.userid} already exists.", ex);
        }
    }

    public async Task<User?> GetUserAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
        }

        using var log = _logger.StartMethod(nameof(GetUserAsync));
        log.SetAttribute("userId", userId);

        try
        {
            ItemResponse<User> response = await _container.ReadItemAsync<User>(userId, new PartitionKey(userId));
            log.SetAttribute("user", JsonSerializer.Serialize(response.Resource));
            Console.WriteLine(JsonSerializer.Serialize(response.Resource));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            log.SetAttribute("status", "notFound");
            log.SetAttribute("errorMessage", ex.Message);
            return null;
        }
    }

    public async Task<IEnumerable<User>> GetActiveUsersAsync(TimeSpan activityThreshold) //To Be Implemented TBD
    {
        using var log = _logger.StartMethod(nameof(GetActiveUsersAsync));
        
        var threshold = DateTime.UtcNow - activityThreshold;
        var queryDefinition = new QueryDefinition("SELECT * FROM c WHERE c.LastActivity > @threshold")
            .WithParameter("@threshold", threshold);

        var query = _container.GetItemQueryIterator<User>(queryDefinition);
        var results = new List<User>();

        while (query.HasMoreResults)
        {
            var response = await query.ReadNextAsync();
            results.AddRange(response.ToList());
        }

        log.SetAttribute("activeUserCount", results.Count);
        log.SetAttribute("status", "success");
        return results;
    }

    public async Task<User> UpdateUserActivityAsync(ActivityData activityData)
    {
        if (activityData == null)
        {
            throw new ArgumentNullException(nameof(activityData), "Activity data cannot be null");
        }

        using var log = _logger.StartMethod(nameof(UpdateUserActivityAsync));
        log.SetAttribute("userId", activityData.UserId);
        log.SetAttribute("lastActivity", activityData.LastActivity);
        log.SetAttribute("tokenUsage", activityData.totalTokenUsage);

        try
        {
            var user = await GetUserAsync(activityData.UserId);
            if (user == null)
            {
                log.SetAttribute("status", "userNotFound");
                throw new InvalidOperationException($"User with ID {activityData.UserId} not found.");
            }

            user.LastActivity = activityData.LastActivity;
            user.totalTokenUsage += activityData.totalTokenUsage;
            user.usedTokenPerDay += activityData.usedTokenPerDay;

            ItemResponse<User> response = await _container.UpsertItemAsync(user, new PartitionKey(user.userid));
            log.SetAttribute("status", "success");
            return response.Resource;
        }
        catch (CosmosException ex)
        {
            log.SetAttribute("status", "error");
            log.SetAttribute("errorMessage", ex.Message);
            throw new InvalidOperationException($"Failed to update activity for user {activityData.UserId}", ex);
        }
    }

    public async Task<User> UpdateUserAsync(User user) //To Be Implemented TBD
    {
        if (user == null)
        {
            throw new ArgumentNullException(nameof(user), "User cannot be null");
        }

        using var log = _logger.StartMethod(nameof(UpdateUserAsync));
        log.SetAttribute("userId", user.userid);

        try
        {
            ItemResponse<User> response = await _container.UpsertItemAsync(user, new PartitionKey(user.userid));
            log.SetAttribute("status", "success");
            return response.Resource;
        }
        catch (CosmosException ex)
        {
            log.SetAttribute("status", "error");
            log.SetAttribute("errorMessage", ex.Message);
            throw new InvalidOperationException($"Failed to update user {user.userid}", ex);
        }
    }

    public async Task<IEnumerable<User>> GetUsersByTokenUsageAsync(int minTokenUsage) //To Be Implemented TBD
    {
        using var log = _logger.StartMethod(nameof(GetUsersByTokenUsageAsync));
        log.SetAttribute("minTokenUsage", minTokenUsage);

        var queryDefinition = new QueryDefinition("SELECT * FROM c WHERE c.totalTokenUsage >= @minUsage")
            .WithParameter("@minUsage", minTokenUsage);

        var query = _container.GetItemQueryIterator<User>(queryDefinition);
        var results = new List<User>();

        while (query.HasMoreResults)
        {
            var response = await query.ReadNextAsync();
            results.AddRange(response.ToList());
        }

        log.SetAttribute("userCount", results.Count);
        log.SetAttribute("status", "success");
        return results;
    }

    public async Task DeleteUserAsync(string userId) //To Be Implemented TBD
    {
        if (string.IsNullOrEmpty(userId))
        {
            throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
        }

        using var log = _logger.StartMethod(nameof(DeleteUserAsync));
        log.SetAttribute("userId", userId);

        try
        {
            await _container.DeleteItemAsync<User>(userId, new PartitionKey(userId));
            log.SetAttribute("status", "success");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            log.SetAttribute("status", "notFound");
            throw new InvalidOperationException($"User with ID {userId} not found.", ex);
        }
        catch (CosmosException ex)
        {
            log.SetAttribute("status", "error");
            log.SetAttribute("errorMessage", ex.Message);
            throw new InvalidOperationException($"Failed to delete user {userId}", ex);
        }
    }
}
