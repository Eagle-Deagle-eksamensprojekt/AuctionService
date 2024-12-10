using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using AuctionServiceAPI.Models;

// Background service that listens for incoming bids on a RabbitMQ queue
public class RabbitMQListener : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Implement the logic to run in the background service
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken); // Example delay
        }
    }

    private readonly ILogger<RabbitMQListener> _logger;
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly Dictionary<string, CancellationTokenSource> _activeListeners;

    public RabbitMQListener(ILogger<RabbitMQListener> logger, IConfiguration config)
    {
        _logger = logger;

        var rabbitHost = config["RABBITMQ_HOST"] ?? "localhost";
        var factory = new ConnectionFactory() { HostName = rabbitHost };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
        _activeListeners = new Dictionary<string, CancellationTokenSource>();
    }

    // Start listening on a specific queue
    public void StartListening(string itemId, DateTimeOffset endAuctionTime)
    {
        if (_activeListeners.ContainsKey(itemId))
        {
            _logger.LogWarning("Already listening on queue for item {ItemId}.", itemId);
            return;
        }

        var cancellationTokenSource = new CancellationTokenSource();
        _activeListeners[itemId] = cancellationTokenSource;

        Task.Run(() => ListenOnQueue(itemId, cancellationTokenSource.Token, endAuctionTime));
    }

    // Stop listening for a specific queue
    public void StopListening(string itemId)
    {
        if (_activeListeners.TryGetValue(itemId, out var cancellationTokenSource))
        {
            cancellationTokenSource.Cancel();
            _activeListeners.Remove(itemId);
            _logger.LogInformation("Stopped listening on queue for item {ItemId}.", itemId);
        }
    }

    private async Task ListenOnQueue(string itemId, CancellationToken token, DateTimeOffset endAuctionTime)
    {
        var queueName = $"{itemId}Queue";
        _channel.QueueDeclare(queue: queueName, durable: false, exclusive: false, autoDelete: false, arguments: null);

        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            var bid = JsonSerializer.Deserialize<Bid>(message);

            _logger.LogInformation($"Received bid for item {itemId}: {bid?.Amount:C}");
            ProcessBid(bid!);
        };

        _channel.BasicConsume(queue: queueName, autoAck: true, consumer: consumer);
        _logger.LogInformation($"Started listening for bids on queue {queueName}.");

        // Hold lytningen aktiv, indtil auktionen slutter eller token annulleres
        while (!token.IsCancellationRequested && DateTimeOffset.UtcNow < endAuctionTime)
        {
            await Task.Delay(1000, token); // Tjek hver sekund
        }

        StopListening(itemId); // Automatisk stop, når auktionen slutter
    }

    private void ProcessBid(Bid bid)
    {
        _logger.LogInformation($"Processing bid {bid.Id} for item {bid.ItemId}.");
        // Implement logic for processing the bid
    }

    public override void Dispose()
    {
        foreach (var cts in _activeListeners.Values)
        {
            cts.Cancel();
        }

        _channel?.Close();
        _connection?.Close();
        base.Dispose();
    }
}

