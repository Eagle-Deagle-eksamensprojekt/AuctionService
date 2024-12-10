using Microsoft.AspNetCore.Builder;
using Services;
using NLog;
using NLog.Web;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// NLog setup
var logger = NLog.LogManager.Setup().LoadConfigurationFromAppSettings()
    .GetCurrentClassLogger();
logger.Debug("init main"); // NLog setup

// Register Auction MongoDB repository (similar to UserMongoDBService)
builder.Services.AddSingleton<IAuctionDbRepository, AuctionMongoDBService>(); // Register MongoDB repository for AuctionService

builder.Services.AddHostedService<RabbitMqListener>();  // Register RabbitMQ listener service


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


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
