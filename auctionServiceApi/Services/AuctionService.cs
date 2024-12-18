using System.Text.Json;
using AuctionServiceAPI.Models;
using Microsoft.AspNetCore.Mvc;
using RabbitMQ.Client;



namespace Services
{
    public class AuctionService : BackgroundService
    {
        private readonly IAuctionDbRepository _auctionDbRepository;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AuctionService> _logger;
        private readonly IConfiguration _config;
        private readonly RabbitMQListener _rabbitListener;
        private DateTimeOffset _auctionEndTime;

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
            var auction = await _auctionDbRepository.GetAuctionByItemId(itemId); // Hent auktionen fra databasen
            if (auction == null)
            {
                return new NotFoundObjectResult($"Auction not found for item {itemId}.");
            }

            if (DateTimeOffset.UtcNow >= auction.EndAuctionDateTime) // Tjek om auktionen er afsluttet
            {
                return new BadRequestObjectResult($"Cannot start listening for an auction that has already ended: {itemId}.");
            }

            try
            {
                // Brug asynkron lytning her
            _rabbitListener.StartListening(itemId, auction.EndAuctionDateTime); // Start listener for auktionen

            _logger.LogInformation("Started listener for auction {ItemId}.", itemId);
            return new OkObjectResult($"Started listening for auction on item {itemId}."); // Returner OK
        }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start listener for auction {ItemId}.", itemId);
                return new StatusCodeResult(500);
            }
        }

        /// <summary>
        /// Intern metode til at hente og gemme auktioner.
        /// </summary>
        public async Task<List<Item>> GetAndSaveAuctionableItems() // Hent og gem auktioner
        {
            var items = await GetItemsFromItemService(); // Hent items fra ItemService
            if (items == null || !items.Any())
            {
                _logger.LogWarning("No items fetched from ItemService.");
                return new List<Item>();
            }

            var auctionableItems = new List<Item>(); // Opret en liste til auktioner

            foreach (var item in items.OrderBy(i => i.CreatedDate)) // Sorter items efter oprettelsesdato
            {
                if (!await _auctionDbRepository.ItemExists(item.Id!)) // Tjek om auktionen allerede eksisterer
                {
                    auctionableItems.Add(item); // Tilføj item til listen
                    if (auctionableItems.Count == 3) // Hvis der er 3 auktioner
                    {
                        break;
                    }
                }
            }

            var tasks = auctionableItems.Select(async item => // Opret en opgave for hver auktion
            {
                _logger.LogInformation("Processing auction for item {ItemId}.", item.Id); 
                await AddAuctionItem(item); // Tilføj auktionen
                _logger.LogInformation("Finished processing auction for item {ItemId}.", item.Id);
            });

            await Task.WhenAll(tasks); // Vent på at alle opgaver er færdige
            return auctionableItems!; // Returner listen med auktioner
        }

        /// <summary>
        /// Intern metode til at hente items fra ItemService.
        /// </summary>
        private async Task<List<Item>> GetItemsFromItemService() // Hent items fra ItemService
        {
            var client = _httpClientFactory.CreateClient(); // Opret en HTTP-klient
            var url = $"{_config["ItemServiceEndpoint"]}/all"; // URL til ItemService
            _logger.LogInformation("Fetching items from ItemService: {Url}", url);

            try // Forsøg at hente items fra ItemService
            {
                var response = await client.GetAsync(url); // Send en GET-anmodning
                response.EnsureSuccessStatusCode(); // Sikre at anmodningen er succesfuld

                var responseContent = await response.Content.ReadAsStringAsync(); // Læs svaret som en streng
                return JsonSerializer.Deserialize<List<Item>>(responseContent) ?? new List<Item>(); // Returner items
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch items from ItemService.");
                return new List<Item>(); // Returner en tom liste
            }
        }

        /// <summary>
        /// Intern metode til at oprette en ny auktion for et item.
        /// </summary>
        private async Task AddAuctionItem(Item item)
        {
            var startTime = DateTimeOffset.UtcNow.Date.AddHours(8); // Start kl. 08:00
            var endTime = startTime.AddHours(8); 

            var auction = new Auction // Opret en ny auktion
            {
                ItemId = item.Id!,
                StartAuctionDateTime = startTime,
                EndAuctionDateTime = endTime,
                Bids = new List<BidElement> { new BidElement { BidAmount = 0, UserId = "Starting Bid" } }
            };

            for (int attempt = 0; attempt < 3; attempt++) // Maksimalt 3 forsøg
            {
                try
                {
                    _logger.LogInformation("Attempt {Attempt} to add auction for item {ItemId}.", attempt + 1, item.Id);

                    await _auctionDbRepository.CreateAuction(auction); // Opret auktionen i databasen
                    _logger.LogInformation("Auction for item {ItemId} created in database.", item.Id);

                    var publishSuccess = await PublishAuctionToRabbitMQ(item); // Publicer auktionen til RabbitMQ TodaysAuctionQueue
                    if (!publishSuccess)
                    {
                        _logger.LogWarning("Failed to publish auction for item {ItemId} to RabbitMQ.", item.Id);
                        continue;
                    }

                    _logger.LogInformation("Auction for item {ItemId} published to RabbitMQ.", item.Id);

                    var listenerResult = await StartAuctionService(item.Id!); // Start listener for auktionen
                    if (listenerResult is OkObjectResult)
                    {
                        _logger.LogInformation("Listener started successfully for item {ItemId}.", item.Id);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error adding auction for item {ItemId}. Retrying in 5 seconds.", item.Id);
                    await Task.Delay(5000);
                }
            }
        }


        /// <summary>
        /// metode til at publicere auktion til RabbitMQ.
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public async Task<bool> PublishAuctionToRabbitMQ(Item item)
        {
            var rabbitHost = _config["RABBITMQ_HOST"] ?? "localhost"; // Default RabbitMQ host er "localhost"

            try
            {
                var factory = new ConnectionFactory // Opret forbindelse til RabbitMQ
                {
                    HostName = rabbitHost,
                    DispatchConsumersAsync = true
                };

                using var connection = factory.CreateConnection(); 
                using var channel = connection.CreateModel();

                // Queue for today's auctions
                var todaysAuctionsQueue = _config["TodaysAuctionsRabbitQueue"] ?? "TodaysAuctions"; // Default queue name is "TodaysAuctions"
                channel.QueueDeclare(
                    queue: todaysAuctionsQueue,
                    durable: false,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null
                );

                // Brug AuctionMessage-model til beskeden
                var message = new TodaysAuctionMessage
                {
                    ItemId = item.Id!,
                    StartDate = DateTime.UtcNow,
                    EndAuctionDateTime = _auctionEndTime
                };

                var body = JsonSerializer.SerializeToUtf8Bytes(message);

                // Publish the message to the queue
                channel.BasicPublish(
                    exchange: "",
                    routingKey: todaysAuctionsQueue,
                    basicProperties: null,
                    body: body
                );

                // Start listening on the specific item queue
                await StartListeningForItemQueue(item.Id!);
                _logger.LogInformation("Published auction for item {ItemId} to queue {QueueName}.", item.Id, todaysAuctionsQueue);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish auction for item {ItemId} to RabbitMQ.", item.Id);
                return false;
            }
        }

        /// <summary>
        /// Metode til at starte lytning på en specifik item-kø.
        /// </summary>
        private async Task StartListeningForItemQueue(string itemId)
        {
            try
            {
                // Retrieve the listener instance
                var rabbitLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<RabbitMQListener>();
                var listener = new RabbitMQListener(rabbitLogger, _config, _auctionDbRepository);

                // Determine auction end time (for simplicity, set to 18:00 UTC)
                _auctionEndTime = DateTime.UtcNow.Date.AddHours(18);

                listener.StartListening(itemId, _auctionEndTime); // fjernet await fordi det er en void metode

                _logger.LogInformation("Started listener for item queue {ItemId}Queue.", itemId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start listener for item {ItemId}Queue.", itemId);
            }
        }
    }
}
