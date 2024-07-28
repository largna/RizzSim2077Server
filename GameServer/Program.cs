using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Extensions.AspNetCore.Configuration.Secrets;

using Common.Utils;
using Microsoft.IdentityModel.Clients.ActiveDirectory;


internal class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Configuration
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();

        bool isDevelopment = builder.Environment.IsDevelopment();
        bool IsProduction = builder.Environment.IsProduction();

        string keyVaultUrl = builder.Configuration["AzureKeyVault:KeyVaultUrl"] ?? string.Empty;

        SecretClient? secretClient = null;
        if (!string.IsNullOrEmpty(keyVaultUrl))
        {
            secretClient = new SecretClient(new Uri(keyVaultUrl), new DefaultAzureCredential());
        }

        if (IsProduction && !string.IsNullOrEmpty(keyVaultUrl))
        {
            builder.Configuration.AddAzureKeyVault(
                new Uri(keyVaultUrl),
                new DefaultAzureCredential());
            
            if (secretClient != null)
            {
                builder.Services.AddSingleton(secretClient);
            }
        }

        IConfiguration configuration = builder.Configuration;

        string serviceName = configuration["Logging:ServiceName:Value"] ?? "GameServer";
        Logger logger = new Logger(serviceName);

        builder.Services.AddControllers();

        builder.Services.AddSingleton<IDatabase>(sp =>
        {
            string? connectionString = configuration["Redis:ConnectionString"];
            if (string.IsNullOrEmpty(connectionString) && secretClient != null)
            {
                try
                {
                    connectionString = secretClient.GetSecret("RedisConnectionString").Value.Value;
                }
                catch (Exception ex)
                {
                    throw new Exception("Failed to retrieve Redis connection string from Key Vault", ex);
                }
            }
            
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException(nameof(configuration), "The 'Redis:ConnectionString' is missing or null");
            }
            
            var redis = ConnectionMultiplexer.Connect(connectionString);
            return redis.GetDatabase();
        });

        builder.Services.AddHttpClient();

        builder.Services.AddSingleton<Logger>(sp => new Logger(serviceName));




        var app = builder.Build();

 
        // 로그인 요청 처리
        app.MapPost("/api/login", async (JsonElement request) =>
        {
            using (var log = logger.StartMethod(nameof(Program) + ".Login"))
            {
                try 
                {
                    var userId = request.GetProperty("userid").GetString() ?? throw new ArgumentNullException(nameof(request), "The 'userid' property is missing or null");
                    var password = request.GetProperty("password").GetString() ?? throw new ArgumentNullException(nameof(request), "The 'password' property is missing or null");

                    log.SetAttribute("userId", userId);

                    var db = app.Services.GetRequiredService<IDatabase>();
                    var client = app.Services.GetRequiredService<HttpClient>();
                    
                    // UMS에서 사용자 정보 가져오기
                    string UMSURL = configuration["UMSURL:Value"] ?? secretClient?.GetSecret("UMSURLValue")?.Value?.Value ?? throw new ArgumentNullException(nameof(configuration), "The 'UMSURL:Value' property is missing or null");

                    var loggedInUser = await db.StringGetAsync($"activity:{userId}");

                    if(loggedInUser.HasValue)
                    {
                        log.SetAttribute("status", "alreadyLoggedIn");
                        return Results.Ok("User already logged in");
                    }

                    var response = await client.GetFromJsonAsync<User>($"{UMSURL}/api/user/{userId}");
                    if (response == null)
                    {
                        Console.WriteLine("User not found");
                        return Results.NotFound("User not found");
                    }

                    // UMS에서 가져온 사용자 정보로 비밀번호 검증
                    var user = response;
                    if (user == null ||(user.userid != userId && user.password != password))  // 주의: 실제로는 해시 비교를 해야 합니다
                    {
                        log.SetAttribute("status", "failed");
                        return Results.Unauthorized();
                    }

                    // Redis에 ActivityData 저장 또는 업데이트
                    var activityData = new ActivityData
                    {
                        UserId = userId,
                        LastActivity = DateTime.Now,
                        usedTokenPerMin = 0,  // 초기값 또는 기존 값 유지
                        usedTokenPerDay = user.usedTokenPerDay,  // User 객체에서 가져옴
                        totalTokenUsage = user.totalTokenUsage  // User 객체에서 가져옴
                    };

                    await db.StringSetAsync($"activity:{userId}", JsonSerializer.Serialize(activityData));
                    Console.WriteLine($"User {userId} logged in");
                    log.SetAttribute("status", $"User {userId} logged in");
                    log.SetAttribute("activityData", activityData.ToString());
                    return Results.Ok("Login successful");
                }
                catch (ArgumentNullException ex)
                {
                    log.SetAttribute("status", "invalidInput");
                    return Results.BadRequest(ex.Message);
                }
                catch (Exception ex)
                {
                    log.SetAttribute("status", "error");
                    log.SetAttribute("errorMessage", ex.Message);
                    Console.WriteLine(ex.Message);
                    return Results.StatusCode(500);
                }
            }
        });

        // 유저 생성 요청 처리
        app.MapPost("/api/signup", async (JsonElement request) =>
        {
            using (var log = logger.StartMethod(nameof(Program) + ".Signup"))
            {
                try
                {
                    string userId = request.GetProperty("userid").GetString() ?? throw new ArgumentNullException(nameof(request), "The 'userid' property is missing or null");

                    var client = app.Services.GetRequiredService<HttpClient>();
                    string UMSURL = configuration["UMSURL:Value"] ?? secretClient?.GetSecret("UMSURLValue")?.Value?.Value ?? throw new ArgumentNullException(nameof(configuration), "The 'UMSURL:Value' property is missing or null");
                    var validUserId = await client.GetAsync($"{UMSURL}/api/user/{userId}");
                    
                    if (validUserId.IsSuccessStatusCode)
                    {
                        log.SetAttribute("status", "failed");
                        return Results.Ok("User already exists");
                    }

                    var user = new User
                    {
                        userid = userId,
                        id = userId,
                        username = request.GetProperty("userName").GetString() ?? throw new ArgumentNullException(nameof(request), "The 'userName' property is missing or null"),
                        password = request.GetProperty("password").GetString() ?? throw new ArgumentNullException(nameof(request), "The 'password' property is missing or null"),
                        usedTokenPerDay = 0,
                        totalTokenUsage = 0,
                        LastActivity = DateTime.Now
                    };

                    log.SetAttribute("userId", user.userid);
                    log.SetAttribute("userName", user.username);

                    var response = await client.PostAsJsonAsync($"{UMSURL}/api/signup", user);
                    if (!response.IsSuccessStatusCode)
                    {
                        log.SetAttribute("status", "failed");
                        return Results.BadRequest("Sign-up failed");
                    }
                    return Results.Ok("Sign-up successful");
                }
                catch (ArgumentNullException ex)
                {
                    log.SetAttribute("status", "invalidInput");
                    return Results.BadRequest(ex.Message);
                }
                catch (Exception)
                {
                    log.SetAttribute("status", "error");
                    return Results.StatusCode(500);
                }
            }
        });

        // 게임 요청 처리
        app.MapPost("/api/openAIcall", async (JsonElement request) =>
        {
            using (var log = logger.StartMethod(nameof(Program) + ".OpenAICall"))
            {
                var requestDoc = JsonDocument.Parse(request.GetRawText());
                var root = requestDoc.RootElement;

                string userId = root.GetProperty("userid").GetString() ?? throw new ArgumentNullException(nameof(request), "The 'userid' property is missing or null");
                log.SetAttribute("userId", userId);

                var db = app.Services.GetRequiredService<IDatabase>();
                var loggedInUser = await db.StringGetAsync($"activity:{userId}");

                if(!loggedInUser.HasValue)
                {
                    log.SetAttribute("status", "Not Logged In"); 
                    return Results.BadRequest("User didn't logged In");
                }

                var newRequest = new JsonObject();
                foreach (var property in root.EnumerateObject())
                {
                    if (property.Name != "userid")
                    {
                        newRequest.Add(property.Name, JsonNode.Parse(property.Value.GetRawText()));
                    }
                }
                
                var user = loggedInUser.ToString();
                var activityData = JsonSerializer.Deserialize<ActivityData>(user);
                var userTokenUsage = activityData?.usedTokenPerMin;
                var userTokenUsagePerDay = activityData?.usedTokenPerDay;
                var totalTokenUsage = activityData?.totalTokenUsage;
                var lastActivity = activityData?.LastActivity;
                
                if(DateTime.Now - lastActivity > TimeSpan.FromMinutes(1))
                {
                    await db.StringSetAsync($"activity:{userId}:usedTokenPerMin", 0);
                }

                if (int.Parse(userTokenUsage.ToString()!) > Constants.MAX_TOKEN_PER_MIN || int.Parse(userTokenUsagePerDay.ToString()!) > Constants.MAX_USER_TOKEN_USAGE)
                {
                    return Results.BadRequest("Token usage limit exceeded");
                }

                string? apiKey = configuration?["GorqAPIKey:Value"] ?? secretClient?.GetSecret("GroqAPIKey")?.Value?.Value ?? string.Empty;

                var openAIResponse = await CallOpenAI(newRequest, logger, apiKey);
                var usageElement = openAIResponse["usage"]?.AsObject();
                int responseTotalTokenUsage = usageElement?["total_tokens"]?.GetValue<int>() ?? 0;

                await db.StringIncrementAsync($"activity:{userId}:usedTokenPerMin", responseTotalTokenUsage);
                await db.StringIncrementAsync($"activity:{userId}:usedTokenPerDay", responseTotalTokenUsage);
                await db.StringIncrementAsync($"activity:{userId}:totalTokenUsage", responseTotalTokenUsage);
                await db.StringSetAsync($"user:{userId}:lastActivity", DateTime.Now.ToString());

                log.SetAttribute("userId", userId);
                log.SetAttribute("tokenUsage", responseTotalTokenUsage);
                log.SetAttribute("totalTokenUsage", responseTotalTokenUsage);

                // 게임 요청 이벤트 게시
                log.SetAttribute("status", "success");
                log.SetAttribute("tokenUsage", responseTotalTokenUsage);
                return Results.Ok(openAIResponse);
            }
        });

        app.MapPost("/api/logout",async (JsonElement request) =>
        {
            using (var log = logger.StartMethod(nameof(Program) + ".Logout"))
            {
                try
                {
                    string userId = request.GetProperty("userid").GetString() ?? throw new ArgumentNullException(nameof(request), "The 'userid' property is missing or null");
                    log.SetAttribute("userId", userId);

                    var db = app.Services.GetRequiredService<IDatabase>();
                    var loggedInUser = await db.StringGetAsync($"activity:{userId}");
                    if(!loggedInUser.HasValue)
                    {
                        log.SetAttribute("status", "notLoggedIn");
                        return Results.BadRequest("User didn't logged In");
                    }
                    var activityData = JsonSerializer.Deserialize<ActivityData>(loggedInUser.ToString());
                    if (activityData != null)
                    {
                        activityData.LastActivity = DateTime.Now;
                    }
                    var httpClient = app.Services.GetRequiredService<HttpClient>();
                    string UMSURL = configuration["UMSURL:Value"] ?? secretClient?.GetSecret("UMSURLValue")?.Value?.Value ?? throw new ArgumentNullException(nameof(configuration), "The 'UMSURL:Value' property is missing or null");
                    var response = await httpClient.PutAsJsonAsync($"{UMSURL}/api/update", activityData);
                    if (!response.IsSuccessStatusCode)
                    {
                        log.SetAttribute("status", "failed");
                        return Results.BadRequest("Sign-up failed");
                    }

                    await db.KeyDeleteAsync($"activity:{userId}");

                    log.SetAttribute("status", "success");
                    return Results.Ok("Logout successful");
                }
                catch (ArgumentNullException ex)
                {
                    log.SetAttribute("status", "invalidInput");
                    return Results.BadRequest(ex.Message);
                }
                catch (Exception)
                {
                    log.SetAttribute("status", "error");
                    return Results.StatusCode(500);
                }
            }
        });

        app.MapGet("/api/health", () => Results.Ok("Healthy"));

        if(isDevelopment)
        {
            app.MapGet("/api/secretcheck", () =>
            {
                var GroqAPIKey = secretClient?.GetSecret("GroqAPIKey")?.Value?.Value;
                var RedisConnectionString = secretClient?.GetSecret("RedisConnectionString")?.Value?.Value;
                var UMSURLValue = secretClient?.GetSecret("UMSURLValue")?.Value?.Value;

                if(string.IsNullOrEmpty(GroqAPIKey) || string.IsNullOrEmpty(RedisConnectionString) || string.IsNullOrEmpty(UMSURLValue))
                {
                    return Results.BadRequest("Secrets not found");
                }

                return Results.Ok(new { GroqAPIKey, RedisConnectionString, UMSURLValue });
            });
        }

        app.Run();
    }

    private static async Task<JsonObject> CallOpenAI(JsonObject request, Logger logger, string apiKey = "")
    {
        using (var log = logger.StartMethod(nameof(Program) + ".CallOpenAI"))
        {
            // OpenAI API 호출 로직 구현
            HttpClient client = new HttpClient();

            if(string.IsNullOrEmpty(apiKey))
            {
                throw new ArgumentNullException(nameof(apiKey), "The 'apiKey' is missing or null");
            }

            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");


            StringContent httpContent = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json");
            HttpResponseMessage response = await client.PostAsync("https://api.groq.com/openai/v1/chat/completions", httpContent);
            response.EnsureSuccessStatusCode();

            string responseString = await response.Content.ReadAsStringAsync();
            JsonObject? responseJson = JsonSerializer.Deserialize<JsonObject>(responseString);
            log.SetAttribute("status", response.IsSuccessStatusCode ? "success" : "failed");
            return responseJson!;
        }
    }

}
