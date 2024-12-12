using System.Text;
using System.Text.Json;
using AuctionServiceAPI.Models;
using RabbitMQ.Client;


namespace Services
{
    public class AuctionService 
    {
        private readonly IAuctionDbRepository _auctionDbRepository;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AuctionService> _logger;
        private readonly IConfiguration _config;

        public AuctionService(
            IAuctionDbRepository auctionDbRepository,
            IHttpClientFactory httpClientFactory,
            ILogger<AuctionService> logger,
            IConfiguration config)
        {
            _auctionDbRepository = auctionDbRepository;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _config = config;
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
            var startTime = DateTimeOffset.UtcNow.Date.AddHours(8); // Næste dag kl. 08:00
            var endTime = startTime.AddHours(24); // Slutter kl. 8:00 næste dag

            var auction = new Auction
            {
                ItemId = item.Id!,
                StartAuctionDateTime = startTime,
                EndAuctionDateTime = endTime,
                CurrentBid = 0,
                CurrentWinnerId = "",
                Bids = new List<BidElement>()
            };

            try
            {
                // Opret auktion i databasen
                await _auctionDbRepository.CreateAuction(auction);
                _logger.LogInformation("Created auction for item {ItemId}.", item.Id);

                // Publiser auktionen til RabbitMQ
                var publishSuccess = await PublishAuctionToRabbitMQ(item);
                if (!publishSuccess)
                {
                    _logger.LogWarning("Failed to publish auction for item {ItemId} to RabbitMQ.", item.Id);
                }
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create auction for item {ItemId}.", item.Id);
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
