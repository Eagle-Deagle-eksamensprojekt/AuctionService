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
            await Task.Delay(1000, stoppingToken); // Wait 1 second between iterations
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
        _logger = logger; // Dependency injection for ILogger
        _auctionDbRepository = auctionDbRepository; // Dependency injection for AuctionMongoDBService
        _config = config; // Dependency injection for IConfiguration

        var rabbitHost = _config["RABBITMQ_HOST"] ?? "localhost"; // Get RabbitMQ host from configuration
        var factory = new ConnectionFactory() { HostName = rabbitHost }; // Create a new connection factory

        _connection = factory.CreateConnection(); // Create a new connection
        _channel = _connection.CreateModel(); // Create a new channel
        _activeListeners = new Dictionary<string, CancellationTokenSource>(); // Initialize dictionary for active listeners
    }

    // Start listening on a specific queue
    public void StartListening(string itemId, DateTimeOffset endAuctionTime)
    {
        if (_activeListeners.ContainsKey(itemId))
        {
            _logger.LogWarning("Already listening on queue for item {ItemId}.", itemId);
            return;
        }

        var cancellationTokenSource = new CancellationTokenSource(); // Create a new cancellation token source
        _activeListeners[itemId] = cancellationTokenSource; // Add the cancellation token source to the dictionary

        _ = ListenOnQueue(itemId, cancellationTokenSource.Token, endAuctionTime); //ListenOnQueue kaldes direkte som en fire-and-forget opgave med _
        _logger.LogInformation("Started listener for item {ItemId} asynchronously.", itemId); // Log that the listener has started
    }


    // Stop listening for a specific queue
    public void StopListening(string itemId)
    {   
        if (_activeListeners.TryGetValue(itemId, out var cancellationTokenSource)) 
        {
            _activeListeners.Remove(itemId); // Fjern før annullering for at undgå race conditions
            cancellationTokenSource.Cancel(); // Annuller lytteren
            _logger.LogInformation("Manually stopped listening on queue for item {ItemId}.", itemId);
        }
        else
        {
            _logger.LogWarning("No active listener found for item {ItemId}.", itemId);
        }
    }


    public async Task ListenOnQueue(string itemId, CancellationToken token, DateTimeOffset endAuctionTime)
    {
        var queueName = $"{itemId}Queue"; // Opret kønavn baseret på itemId
        _channel.QueueDeclare(queue: queueName, durable: false, exclusive: false, autoDelete: false, arguments: null); // Opret køen

        var consumer = new EventingBasicConsumer(_channel); // Opret forbruger
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

        // Process a bid received from the queue
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
            var updated = await _auctionDbRepository.UpdateAuction(auction.Id!, auction); // Opdater auktionen i databasen
            if (updated)
            {
                _logger.LogInformation($"Auction {auction.Id} updated with new bid: {bid.Amount:C}, Current Winner: {bid.UserId}.");
            }
            else
            {
                _logger.LogError($"Failed to update auction {auction.Id} with new bid.");
            }
        }

    public override void Dispose() // Dispose metode til at rydde op i rabbitMQ forbindelsen
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

