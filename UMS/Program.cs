using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Azure.Cosmos;
using System.Configuration;
using System.Drawing;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
public class Program
{
    public static void Main(string[] args)
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

        // Add services to the container.
        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        // Register CosmosDbService
        builder.Services.AddSingleton<CosmosDbService>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            return new CosmosDbService(configuration, secretClient ?? throw new ArgumentNullException(nameof(secretClient)), isDevelopment);
        });

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        app.MapGet("/api/healthcheck", () =>     Results.Ok(new { Status = "Healthy", Timestamp = DateTime.Now }));


        if(IsProduction)
        {
            app.MapGet("/api/secretcheck", () =>
            {
                var CosmosEndpoint = secretClient?.GetSecret("CosmosEndpoint")?.Value?.Value;
                var CosmosDatabaseName = secretClient?.GetSecret("CosmosDatabaseName")?.Value?.Value;
                var CosmosContainerName = secretClient?.GetSecret("CosmosContainerName")?.Value?.Value;

                if(string.IsNullOrEmpty(CosmosEndpoint) || string.IsNullOrEmpty(CosmosDatabaseName) || string.IsNullOrEmpty(CosmosContainerName))
                {
                    return Results.BadRequest("Secrets not found");
                }

                return Results.Ok(new { CosmosEndpoint, CosmosDatabaseName, CosmosContainerName });
            });
        }
        
        app.UseHttpsRedirection();
        app.UseAuthorization();
        app.MapControllers();

        app.Run();
    }
}