using Microsoft.AspNetCore.Mvc;
using OpenTelemetry;
using System.Text.Json; // Add this using directive
using Common.Utils;
using Microsoft.Azure.Cosmos; // Add this using directive for CosmosException
using System.Net;
using Azure.Security.KeyVault.Secrets; // Add this using directive for HttpStatusCode


[ApiController]
[Route("api")]
public class UserController : ControllerBase
{
    private readonly CosmosDbService _cosmosDbService;
    private readonly Logger _logger;

    public UserController(CosmosDbService cosmosDbService, IConfiguration configuration)
    {
        _cosmosDbService = cosmosDbService;
        string serviceName = configuration["Logging:ServiceName:Value"] ?? "UserController";
        _logger = new Logger(serviceName);
    }

    [HttpPost("signup")]
    public async Task<IActionResult> SignUp([FromBody] User user)
    {
        using (var log = _logger.StartMethod(nameof(UserController) + ".SignUp"))
        {
            log.SetAttribute("userId", user.userid);

            if (!ModelState.IsValid)
            {
                var errorMessages = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                foreach (var errorMessage in errorMessages)
                {
                    log.SetAttribute("validationError", errorMessage);
                }

                return BadRequest(ModelState);
            }

            if(string.IsNullOrEmpty(user.id))
            {
                user.id = user.userid;
            }

            try
            {
                await _cosmosDbService.AddUserAsync(user);
                log.SetAttribute("status", "success");
                return CreatedAtAction(nameof(GetUser), new { userId = user.userid }, user);
            }
            catch (InvalidOperationException ex)
            {
                log.SetAttribute("status", "conflict");
                return Conflict(ex.Message);
            }
        }
    }

    [HttpPost("login")] // Needs more implementation => it is now implemented in GameServer/Program.cs + Not just comparing the ID and PW
    public async Task<IActionResult> Login([FromBody] JsonElement request)
    {
        using (var log = _logger.StartMethod(nameof(UserController) + ".Login"))
        {
            if (!request.TryGetProperty("userId", out JsonElement userIdElement) || 
                !request.TryGetProperty("password", out JsonElement passwordElement))
            {
                log.SetAttribute("status", "invalidRequest");
                return BadRequest("Invalid request format");
            }

            string userId = userIdElement.GetString() ?? string.Empty;
            string password = passwordElement.GetString() ?? string.Empty;

            log.SetAttribute("userId", userId);

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(password))
            {
                log.SetAttribute("status", "missingCredentials");
                return BadRequest("UserId and Password are required");
            }

            var user = await _cosmosDbService.GetUserAsync(userId);
            if (user == null || !VerifyPassword(password, user.password))
            {
                log.SetAttribute("status", "invalidCredentials");
                return Unauthorized("Invalid credentials");
            }

            log.SetAttribute("status", "success");
            // TODO: Generate and return a JWT token here
            return Ok("Login successful");
        }
    }

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUser(string userId)
    {
        using (var log = _logger.StartMethod(nameof(UserController) + ".GetUser"))
        {
            log.SetAttribute("userId", userId);

            var user = await _cosmosDbService.GetUserAsync(userId);

            log.SetAttribute("user", JsonSerializer.Serialize(user));

            if (user == null)
            {
                log.SetAttribute("status", "userNotFound");
                return NotFound("User not found");
            }

            log.SetAttribute("status", "success");
            return Ok(user);
        }
    }

    [HttpPost("sync-activity")] //To Be Implemented TBD
    public async Task<IActionResult> SyncActivity([FromBody] ActivityData activityData)
    {
        using (var log = _logger.StartMethod(nameof(UserController) + ".SyncActivity"))
        {
            log.SetAttribute("userId", activityData.UserId);
            log.SetAttribute("lastActivity", activityData.LastActivity.ToString());
            log.SetAttribute("usedTokenPerMin", activityData.usedTokenPerMin);
            log.SetAttribute("totalTokenUsage", activityData.totalTokenUsage);

            try
            {
                await _cosmosDbService.UpdateUserActivityAsync(activityData);
                log.SetAttribute("status", "success");
                return Ok("Activity synced successfully");
            }
            catch (InvalidOperationException ex)
            {
                log.SetAttribute("status", "userNotFound");
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                log.SetAttribute("status", "error");
                log.SetAttribute("errorMessage", ex.Message);
                return StatusCode(500, "An error occurred while syncing activity");
            }
        }
    }

    [HttpPut("update")]
    public async Task<IActionResult> UpdateUser([FromBody] ActivityData activityData)
    {
        using (var log = _logger.StartMethod(nameof(UserController) + ".UpdateUser"))
        {
            log.SetAttribute("userId", activityData.UserId);

            try
            {
                var updatedUser = await _cosmosDbService.UpdateUserActivityAsync(activityData);
                log.SetAttribute("status", "success");
                return Ok(updatedUser);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                log.SetAttribute("status", "notFound");
                return NotFound($"User with ID {activityData.UserId} not found");
            }
            catch (Exception ex)
            {
                log.SetAttribute("status", "error");
                log.SetAttribute("error", ex.Message);
                return StatusCode(500, "An error occurred while updating the user");
            }
        }
    }

    [HttpGet("high-usage")] // To Be Implemented
    public async Task<IActionResult> GetHighUsageUsers([FromQuery] int minTokenUsage = 1000)
    {
        using (var log = _logger.StartMethod(nameof(UserController) + ".GetHighUsageUsers"))
        {
            log.SetAttribute("minTokenUsage", minTokenUsage);

            try
            {
                var users = await _cosmosDbService.GetUsersByTokenUsageAsync(minTokenUsage);
                log.SetAttribute("status", "success");
                log.SetAttribute("userCount", users.Count());
                return Ok(users);
            }
            catch (Exception ex)
            {
                log.SetAttribute("status", "error");
                log.SetAttribute("error", ex.Message);
                return StatusCode(500, "An error occurred while fetching high usage users");
            }
        }
    }

    [HttpGet("health")]
    public IActionResult HealthCheck()
    {
        using (var log = _logger.StartMethod(nameof(UserController) + ".HealthCheck"))
        {
            log.SetAttribute("status", "healthy");
            return Ok("Service is healthy");
        }
    }

    private bool VerifyPassword(string inputPassword, string storedPassword) //To Be Implemented TBD
    {
        // TODO: Implement proper password hashing and verification
        return inputPassword == storedPassword;
    }
}
