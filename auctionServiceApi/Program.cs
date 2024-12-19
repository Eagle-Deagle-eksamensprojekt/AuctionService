using Services;
using NLog;
using NLog.Web;
using VaultSharp;
using VaultSharp.V1.AuthMethods.Token;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// NLog setup
var logger = NLog.LogManager.Setup().LoadConfigurationFromAppSettings()
    .GetCurrentClassLogger();
logger.Debug("init main"); // NLog setup

// Register Auction MongoDB repository (similar to UserMongoDBService)
builder.Services.AddSingleton<IAuctionDbRepository, AuctionMongoDBService>(); // Register MongoDB repository for AuctionService

builder.Services.AddSingleton<RabbitMQListener>(); // Register RabbitMQ listener as a singleton
builder.Services.AddSingleton<AuctionService>(); // Register AuctionService as a singleton


builder.Services.AddMemoryCache(); 


// Register other services
builder.Services.AddControllers();

// Swagger/OpenAPI configuration
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register NLog for logging
builder.Logging.ClearProviders();
builder.Host.UseNLog();

// Register HttpClientFactory
builder.Services.AddHttpClient(); //tjek

// Vault-integration
// Vault-integration
var vaultToken = Environment.GetEnvironmentVariable("VAULT_TOKEN") 
                 ?? throw new Exception("Vault token not found");
var vaultUrl = Environment.GetEnvironmentVariable("VAULT_URL") 
               ?? "http://vault:8200"; // Standard Vault URL

var authMethod = new TokenAuthMethodInfo(vaultToken);
var vaultClientSettings = new VaultClientSettings(vaultUrl, authMethod);
var vaultClient = new VaultClient(vaultClientSettings);

var kv2Secret = await vaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(path: "Secrets", mountPoint: "secret");
var jwtSecret = kv2Secret.Data.Data["jwtSecret"]?.ToString() ?? throw new Exception("jwtSecret not found in Vault.");
var jwtIssuer = kv2Secret.Data.Data["jwtIssuer"]?.ToString() ?? throw new Exception("jwtIssuer not found in Vault.");
var mongoConnectionString = kv2Secret.Data.Data["MongoConnectionString"]?.ToString() ?? throw new Exception("MongoConnectionString not found in Vault.");

// Dependency injection for AuctionMongoDBService
builder.Services.AddSingleton<IAuctionDbRepository>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<AuctionMongoDBService>>();
    var configuration = sp.GetRequiredService<IConfiguration>();
    return new AuctionMongoDBService(logger, mongoConnectionString, configuration);
});


// Register JWT authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = "http://localhost", // Tilpas efter behov
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Start RabbitMQ listener hvis der er aktive auktioner
var rabbitListener = app.Services.GetRequiredService<RabbitMQListener>();
var auctionRepo = app.Services.GetRequiredService<IAuctionDbRepository>();

var activeAuctions = await auctionRepo.GetAllAuctions();
foreach (var auction in activeAuctions)
{
    if (DateTimeOffset.UtcNow < auction.EndAuctionDateTime)
    {
        rabbitListener.StartListening(auction.ItemId!, auction.EndAuctionDateTime);
    }
}


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication(); // Auhorization
app.UseAuthorization();
app.MapControllers();

app.Run();
