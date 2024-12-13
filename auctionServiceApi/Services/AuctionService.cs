using System.Text;
using System.Text.Json;
using AuctionServiceAPI.Models;
using Microsoft.AspNetCore.Mvc;
using RabbitMQ.Client;
using Services;



namespace Services
{
    public class AuctionService : BackgroundService
    {
        private readonly IAuctionDbRepository _auctionDbRepository;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AuctionService> _logger;
        private readonly IConfiguration _config;
        private readonly RabbitMQListener _rabbitListener;

        public AuctionService(
            IAuctionDbRepository auctionDbRepository,
            IHttpClientFactory httpClientFactory,
            ILogger<AuctionService> logger,
            RabbitMQListener rabbitListener,
            IConfiguration config)
        {
            _auctionDbRepository = auctionDbRepository;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _rabbitListener = rabbitListener;
            _config = config;
        }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("AuctionScheduler started.");
            while (!stoppingToken.IsCancellationRequested)
            {
                await ScheduleAuctions();
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred in AuctionScheduler.");
            throw;
        }
    }

        
        /// <summary>
        /// Opstartsmetode for at hente auktioner fra databasen og starte RabbitMQ listener.
        /// </summary>
        public async Task ScheduleAuctions()
        {
            while (true)
            {
                var now = DateTime.UtcNow;

                if (now.Hour == 7 && now.Minute == 0) // Planlægning kl. 07:00
                {
                    _logger.LogInformation("Scheduling auctions for the day...");

                    var items = await GetAndSaveAuctionableItems();

                    foreach (var item in items)
                    {
                        _logger.LogInformation("Starting auction for item {ItemId}", item.Id);
                        await StartAuctionService(item.Id!); // Start auktionen
                    }
                    // Start listeners for auktioner
                    // Start auktioner
                    
                }

                if (now.Hour == 18 && now.Minute == 0) // Luk ned kl. 18:00
                {
                    _logger.LogInformation("Shutting down auctions for the day...");
                    // Stop alle listeners
                    // Stop og marker auktioner som afsluttet
                    // Send notifikationer til vindere
                    // Gem salgspris til ItemService
                }

                await Task.Delay(TimeSpan.FromMinutes(1)); // Tjek hvert minut
            }
        }
        
        
        /// <summary>
        /// Start af listener for auktionsservice baseret på itemId.
        /// </summary>
        /// <param name="itemId"></param>
        /// <returns></returns>
        public async Task<IActionResult> StartAuctionService(string itemId)
        {
            var auction = await _auctionDbRepository.GetAuctionByItemId(itemId);
            if (auction == null)
            {
                return new NotFoundObjectResult("Auction not found for the specified item.,");
            }

            if (DateTimeOffset.UtcNow >= auction.EndAuctionDateTime)
            {
                return new BadRequestObjectResult("Cannot start listening for an auction that has already ended.");
            }
            
            var cancellationTokenSource = new CancellationTokenSource(1000); // 1000 ms timeout

            await _rabbitListener.ListenOnQueue(itemId, cancellationTokenSource.Token, auction.EndAuctionDateTime); // Start listener for auktionen
            return new OkObjectResult($"Started listening for auction on item {itemId}.");
        } 


        /// <summary>
        /// Intern metode til at hente og gemme auktioner.
        /// </summary>
        public async Task<List<Item>> GetAndSaveAuctionableItems()
        {
            // Hent items fra ItemService
            var items = await GetItemsFromItemService();
            if (items == null || !items.Any())
                return new List<Item>();

            // Sorter items efter oprettelsesdato (ældste først)
            var sortedItems = items.OrderBy(i => i.CreatedDate).ToList();

            // Find de 3 ældste, der ikke allerede er i databasen
            var auctionableItems = new List<Item>();
            foreach (var item in sortedItems)
            {
                var exists = await _auctionDbRepository.ItemExists(item.Id!);
                if (!exists)
                {
                    
                    auctionableItems.Add(item);
                    if (auctionableItems.Count == 3)
                        break;
                }
            }

            // Opret auktioner for de fundne items
            foreach (var item in auctionableItems)
            {
                await AddAuctionItem(item);
            }

            return auctionableItems;
        }

        /// <summary>
        /// Intern metode til at hente items fra ItemService.
        /// </summary>
        private async Task<List<Item>> GetItemsFromItemService()
        {
            var client = _httpClientFactory.CreateClient();
            var url = $"{_config["ItemServiceEndpoint"]}/all";
            _logger.LogInformation("Fetching items from ItemService: {Url}", url);

            try
            {
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<Item>>(responseContent) ?? new List<Item>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch items from ItemService.");
                return new List<Item>();
            }
        }

        /// <summary>
        /// Intern metode til at oprette en ny auktion for et item.
        /// </summary>
        private async Task AddAuctionItem(Item item)
        {
            var startTime = DateTimeOffset.UtcNow.Date.AddHours(7); // Næste dag kl. 07:00
            var endTime = startTime.AddHours(18); // Slutter kl. 8:00 næste dag

            var auction = new Auction
            {
                ItemId = item.Id!,
                StartAuctionDateTime = startTime,
                EndAuctionDateTime = endTime,
                Bids = new List<BidElement> { new BidElement { BidAmount = 0, UserId = "Starting Bid" } }
            };

            try
            {
                await _auctionDbRepository.CreateAuction(auction);
                _logger.LogInformation("Created auction for item {ItemId}.", item.Id);

                var publishSuccess = await PublishAuctionToRabbitMQ(item);
                if (!publishSuccess)
                {
                    _logger.LogWarning("Failed to publish auction for item {ItemId} to RabbitMQ.", item.Id);
                }

                var listenerResult = await StartAuctionService(item.Id!);
                if (listenerResult is not OkObjectResult)
                {
                    _logger.LogWarning("Failed to start listener for auction on item {ItemId}.", item.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create auction for item {ItemId}. Retrying in 5 seconds.", item.Id);
                await Task.Delay(5000);
                await AddAuctionItem(item); // Retry
            }
        }


        public async Task<bool> PublishAuctionToRabbitMQ(Item item)
        {
            var rabbitHost = _config["RABBITMQ_HOST"] ?? "localhost"; 

            try
            {
                var factory = new ConnectionFactory
                {
                    HostName = rabbitHost,
                    DispatchConsumersAsync = true
                };

                using var connection = factory.CreateConnection(); 
                using var channel = connection.CreateModel();

                // Queue name based on ItemId
                var itemId = item.Id;
                if (itemId == null)
                {
                    _logger.LogError("Failed to get ItemId from item.");
                    return false;
                }
                var queueName = _config["TodaysAuctionsRabbitQueue"];

                channel.QueueDeclare(
                    queue: queueName,
                    durable: false,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null
                );

                var message = JsonSerializer.Serialize(item);
                var body = Encoding.UTF8.GetBytes(message);

                channel.BasicPublish(
                    exchange: "",
                    routingKey: queueName,
                    basicProperties: null,
                    body: body
                );

                _logger.LogInformation("Published item {ItemId} to RabbitMQ queue {QueueName}.", item.Id, queueName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish item {ItemId} to RabbitMQ.", item.Id);
                return false;
            }
        }


    }
}
