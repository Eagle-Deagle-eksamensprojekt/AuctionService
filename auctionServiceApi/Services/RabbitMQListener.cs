using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using AuctionServiceAPI.Models;
using Services;

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
    private readonly IAuctionDbRepository _auctionDbRepository;
    private readonly IConfiguration _config;


    public RabbitMQListener(ILogger<RabbitMQListener> logger, IConfiguration config, IAuctionDbRepository auctionDbRepository)
    {
        _logger = logger;
        _auctionDbRepository = auctionDbRepository;
        _config = config;

        var rabbitHost = _config["RABBITMQ_HOST"] ?? "localhost";
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

        _ = ListenOnQueue(itemId, cancellationTokenSource.Token, endAuctionTime); //ListenOnQueue kaldes direkte som en fire-and-forget opgave med _
        _logger.LogInformation("Started listener for item {ItemId} asynchronously.", itemId);
    }


    // Stop listening for a specific queue
    public void StopListening(string itemId)
    {   
        if (_activeListeners.TryGetValue(itemId, out var cancellationTokenSource))
        {
            _activeListeners.Remove(itemId); // Fjern før annullering for at undgå race conditions
            cancellationTokenSource.Cancel();
            _logger.LogInformation("Manually stopped listening on queue for item {ItemId}.", itemId);
        }
        else
        {
            _logger.LogWarning("No active listener found for item {ItemId}.", itemId);
        }
    }





    public async Task ListenOnQueue(string itemId, CancellationToken token, DateTimeOffset endAuctionTime)
    {
        var queueName = $"{itemId}Queue";
        _channel.QueueDeclare(queue: queueName, durable: false, exclusive: false, autoDelete: false, arguments: null);

        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            var bid = JsonSerializer.Deserialize<Bid>(message);

            _logger.LogInformation("Received bid for item {ItemId}: {Amount}.", itemId, bid?.Amount);
            ProcessBid(bid!);
        };

        _channel.BasicConsume(queue: queueName, autoAck: true, consumer: consumer);
        _logger.LogInformation("Started listening for bids on queue {QueueName}.", queueName);

        try
        {
            while (!token.IsCancellationRequested && DateTimeOffset.UtcNow < endAuctionTime)
            {
                await Task.Delay(1000, token); // Vent 1 sekund mellem iterationer
            }
        }
        catch (TaskCanceledException)
        {
            _logger.LogInformation("Listening for item {ItemId} was canceled.", itemId);
        }
        finally
        {
            StopListening(itemId);
        }

        _logger.LogInformation("Auction for {ItemId} ended. Stopping listener.", itemId);
    }


        private async void ProcessBid(Bid bid)
        {
            _logger.LogInformation($"Processing bid {bid.Id} for item {bid.ItemId}.");

            // Hent auktionen
            var auction = await _auctionDbRepository.GetAuctionByItemId(bid.ItemId!);

            if (auction == null)
            {
                _logger.LogWarning($"Auction for item {bid.ItemId} not found.");
                return; // Auktion ikke fundet, så vi afslutter metoden
            }

            // Sørg for, at Bids-listen er initialiseret
            auction.Bids ??= new List<BidElement>();

            // Tjek om det nye bud er højere end det nuværende
            if (bid.Amount <= auction.CurrentBid)
            {
                _logger.LogWarning($"Bid {bid.Amount:C} is not higher than the current bid {auction.CurrentBid:C}.");
                return; // Hvis buddet ikke er højere end det nuværende, afslut
            }

            // Tilføj det nye bud til Bids-listen
            auction.Bids.Add(new BidElement
            {
                BidAmount = bid.Amount,
                UserId = bid.UserId!
            });

            // Opdater auktionen i databasen
            var updated = await _auctionDbRepository.UpdateAuction(auction.Id!, auction);
            if (updated)
            {
                _logger.LogInformation($"Auction {auction.Id} updated with new bid: {bid.Amount:C}, Current Winner: {bid.UserId}.");
            }
            else
            {
                _logger.LogError($"Failed to update auction {auction.Id} with new bid.");
            }
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

