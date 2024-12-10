using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using AuctionServiceAPI.Models;
using Services; // For the IAuctionDbRepository interface
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace AuctionServiceAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class AuctionController : ControllerBase
    {
        private readonly ILogger<AuctionController> _logger;
        private readonly IAuctionDbRepository _auctionDbRepository;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;

        public AuctionController(
            ILogger<AuctionController> logger,
            IAuctionDbRepository auctionDbRepository,
            IHttpClientFactory httpClientFactory,
            IConfiguration config)
        {
            _logger = logger;
            _auctionDbRepository = auctionDbRepository;
            _httpClientFactory = httpClientFactory;
            _config = config;
        }

        /// <summary>
        /// Hent alle auktioner.
        /// </summary>
        [HttpGet("all")]
        public async Task<IActionResult> GetAllAuctions()
        {
            var auctions = await _auctionDbRepository.GetAllAuctions();
            return Ok(auctions);
        }

        /// <summary>
        /// Hent en specifik auktion ved ID.
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetAuctionById(string id)
        {
            var auction = await _auctionDbRepository.GetAuctionById(id);

            if (auction == null)
                return NotFound($"Auction with ID {id} not found.");

            return Ok(auction);
        }

        /// <summary>
        /// Hent items fra ItemService og gem de 3 ældste i databasen som auktioner.
        /// </summary>
        [HttpGet("fetch-and-save-auctionable")]
        public async Task<IActionResult> FetchAndSaveAuctionableItems()
        {
            try
            {
                var auctionableItems = await GetAndSaveAuctionableItems();
                if (auctionableItems == null || !auctionableItems.Any())
                    return Ok("No auctionable items were found or saved.");

                return Ok(auctionableItems);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching and saving auctionable items.");
                return StatusCode(500, "An error occurred while processing the request.");
            }
        }

        /// <summary>
        /// Intern metode til at hente og gemme auktioner.
        /// </summary>
        private async Task<List<Item>> GetAndSaveAuctionableItems()
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

            await _auctionDbRepository.CreateAuction(auction);
            _logger.LogInformation("Created auction for item {ItemId}.", item.Id);
        }


        /// <summary>
        /// Bid service skal validere om et bud er gyldigt inden den poste til RabbitMQ
        /// </summary>
        // Tjekker om item er true, hvis den er auctionable
        [HttpGet("auctionable/{itemId}")]
        public async Task<IActionResult> CheckItemIsAuctionable(string itemId, [FromQuery] string dateTime)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return BadRequest("Item ID is required.");
            }

            if (!DateTimeOffset.TryParse(dateTime, out var parsedDateTime)) // Tjekker om DateTime er gyldig
            {
                return BadRequest("Invalid DateTime format."); // Returnerer bad request hvis DateTime ikke er gyldig
            }

            try
            {
                var isAuctionable = await _auctionDbRepository.CheckItemIsAuctionable(itemId, parsedDateTime.UtcDateTime);
                return Ok(isAuctionable);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if item is auctionable.");
                return StatusCode(500, "An error occurred while checking the item.");
            }
        }
/*
        private Task ConsumeRabbitMQ(string auctionId)
        {
            var rabbitHost = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";

            var factory = new ConnectionFactory { HostName = rabbitHost };
            using var connection = factory.CreateConnection();
            using var channel = connection.CreateModel();

            var queueName = $"{auctionId}Queue";
            channel.QueueDeclare(
                queue: queueName,
                durable: false,
                exclusive: false,
                autoDelete: false,
                arguments: null
            );

            var consumer = new EventingBasicConsumer(channel);
            consumer.Received += (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                var bid = JsonSerializer.Deserialize<Bid>(message);

                _logger.LogInformation("Received bid {BidId} for auction {AuctionId}.", bid?.Id, auctionId);

                // Process bid logic here
            };

            channel.BasicConsume(
                queue: queueName,
                autoAck: true,
                consumer: consumer
            );

            _logger.LogInformation("Listening for bids on RabbitMQ queue {QueueName}.", queueName);

            return Task.CompletedTask;
        }
*/
    }
}
