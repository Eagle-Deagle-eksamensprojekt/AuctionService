using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using AuctionServiceAPI.Models;

public class RabbitMqListener : BackgroundService
{
    private readonly ILogger<RabbitMqListener> _logger;
    private readonly string _auctionId; // AuctionId for the queue
    private readonly string _rabbitHost;

    public RabbitMqListener(ILogger<RabbitMqListener> logger, string auctionId)
    {
        _logger = logger;
        _auctionId = auctionId;
        _rabbitHost = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";
    }

    // This method is executed when the background service starts
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation($"RabbitMQ Host: {_rabbitHost}");
        _logger.LogInformation("Connecting to RabbitMQ...");

        // Opret forbindelse til RabbitMQ
        var factory = new ConnectionFactory() { HostName = _rabbitHost };

        using var connection = factory.CreateConnection();
        _logger.LogInformation("Connected to RabbitMQ.");

        using var channel = connection.CreateModel();

        // Declare queue using auctionId (this will create a unique queue for each auction)
        var queueName = $"{_auctionId}Queue";
        channel.QueueDeclare(queue: queueName, durable: false, exclusive: false, autoDelete: false, arguments: null);

        var consumer = new EventingBasicConsumer(channel);
        consumer.Received += async (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);

            // Deserialisere beskeden og håndter buddet
            var bid = JsonSerializer.Deserialize<Bid>(message);
            _logger.LogInformation($"Received bid {bid?.Id} for auction {_auctionId}");

            // Process the bid (e.g., validate, update auction status, etc.)
            await ProcessBid(bid);
        };

        // Start listening for incoming bids on the queue
        channel.BasicConsume(queue: queueName, autoAck: true, consumer: consumer);

        _logger.LogInformation($"Listening for bids on RabbitMQ queue {queueName}.");

        // Keep the background service running
        await Task.CompletedTask;
    }

    // Example method to process a received bid
    private async Task ProcessBid(Bid bid)
    {
        if (bid == null)
        {
            _logger.LogWarning("Received invalid bid.");
            return;
        }

        // Here you would implement your logic for processing the bid
        _logger.LogInformation($"Processing bid {bid.Id} for auction {_auctionId}");

        // Example: Update auction with the new bid
        // E.g., validate if the bid is higher than the current bid, update auction status, etc.
    }
}
