using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using AuctionServiceAPI.Models;
using Services; // 
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Microsoft.Extensions.Caching.Memory;
using System.Diagnostics;
using System.Text;

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
        private readonly IMemoryCache _memoryCache;
        private readonly RabbitMQListener _rabbitListener;
        private readonly AuctionService _auctionService;


        public AuctionController(
            ILogger<AuctionController> logger,
            IAuctionDbRepository auctionDbRepository,
            IHttpClientFactory httpClientFactory,
            IMemoryCache memoryCache,
            IConfiguration config,
            AuctionService auctionService,
            RabbitMQListener rabbitListener)
            
        {
            _logger = logger;
            _auctionDbRepository = auctionDbRepository;
            _httpClientFactory = httpClientFactory;
            _memoryCache = memoryCache;
            _config = config;
            _auctionService = auctionService;
            _rabbitListener = rabbitListener;
        }

        /// <summary>
        /// Hent version af Service
        /// </summary>
        [HttpGet("version")]
        public async Task<Dictionary<string,string>> GetVersion()
        {
            var properties = new Dictionary<string, string>();
            var assembly = typeof(Program).Assembly;

            properties.Add("service", "OrderService");
            var ver = FileVersionInfo.GetVersionInfo(
                typeof(Program).Assembly.Location).ProductVersion ?? "N/A";
            properties.Add("version", ver);
            
            var hostName = System.Net.Dns.GetHostName();
            var ips = await System.Net.Dns.GetHostAddressesAsync(hostName);
            var ipa = ips.First().MapToIPv4().ToString() ?? "N/A";
            properties.Add("ip-address", ipa);
            
            return properties;
        }

        /// <summary>
        /// Hent alle auktioner.
        /// </summary>
        // Hent alle auktioner og cache resultatet
        [HttpGet("all")]
        public async Task<IActionResult> GetAllAuctions()
        {
            if (!_memoryCache.TryGetValue("all_auctions", out List<Auction>? auctions)) // Hvis data ikke er i cachen
            {
                // Hent auktioner fra databasen og konverter til List<Auction>
                auctions = (await _auctionDbRepository.GetAllAuctions()).ToList();

                // Cache dataen i 30 minutter
                _memoryCache.Set("all_auctions", auctions, TimeSpan.FromMinutes(30));
                _logger.LogInformation("Auktioner hentet fra databasen og gemt i cache.");
            }
            else
            {
                _logger.LogInformation("Auktioner hentet fra cache.");
            }


            return Ok(auctions);
        }


        /// <summary>
        /// Hent en specifik auktion ved ID.
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetAuctionById(string id)
        {
            if (!_memoryCache.TryGetValue(id, out Auction? auction)) // Hvis auktionen ikke er i cachen
            {
                // Hent auktionen fra databasen
                auction = await _auctionDbRepository.GetAuctionById(id);

                if (auction == null)
                    return NotFound($"Auction with ID {id} not found.");

                // Cache auktionen i 30 minutter
                _memoryCache.Set(id, auction, TimeSpan.FromMinutes(30));
                _logger.LogInformation($"Auktion med ID {id} hentet fra databasen og gemt i cache.");
            }
            else
            {
                _logger.LogInformation($"Auktion med ID {id} hentet fra cache.");
            }

            return Ok(auction);
        }

        /// <summary>
        /// Fetches and saves auctionable items, then publishes them to RabbitMQ.
        /// </summary>
        [HttpGet("fetch-and-save-auctionable")]
        public async Task<IActionResult> FetchAndSaveAuctionableItems()
        {
            try
            {
                // Hent og gem auctionable items
                var auctionableItems = await _auctionService.GetAndSaveAuctionableItems();
                if (auctionableItems == null || !auctionableItems.Any())
                {
                    _logger.LogInformation("No auctionable items were found or saved.");
                    return Ok("No auctionable items were found or saved.");
                }

                return Ok(new
                {
                    Message = "Successfully fetched, saved, and published auctionable items.",
                    Items = auctionableItems
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching and saving auctionable items.");
                return StatusCode(500, "An error occurred while processing the request.");
            }
        }



        /// <summary>
        /// Bid service skal validere om et bud er gyldigt inden den poste til RabbitMQ
        /// </summary>
        // Tjekker om item er true, hvis den er auctionable
        [HttpGet("auctionable/{itemId}")]
        public async Task<IActionResult> CheckItemIsAuctionable(string itemId)
        {
            // Validering af itemId
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return BadRequest("Item ID is required.");
            }

            try
            {
                _logger.LogInformation("Checking if item {ItemId} is auctionable.", itemId);

                // Hent auktion baseret på itemId
                var auction = await _auctionDbRepository.GetAuctionByItemId(itemId);

                if (auction == null)
                {
                    _logger.LogWarning("No auction found for item {ItemId}.", itemId);
                    return NotFound(new { Message = $"No auction found for item {itemId}" });
                }

                // Returner start- og sluttidspunkter som JSON
                var result = new
                {
                    StartAuctionDateTime = auction.StartAuctionDateTime.ToUniversalTime(),
                    EndAuctionDateTime = auction.EndAuctionDateTime.ToUniversalTime()
                };

                _logger.LogInformation("Item {ItemId} auction details: Start: {Start}, End: {End}.",
                    itemId, result.StartAuctionDateTime, result.EndAuctionDateTime);

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if item {ItemId} is auctionable.", itemId);
                return StatusCode(500, new { Message = "An error occurred while checking the item.", Error = ex.Message });
            }
        }

        [HttpPost("clear-cache")]
        public IActionResult ClearCache()
        {
            _memoryCache.Remove("all_auctions");
            _logger.LogInformation("Cache for 'all_auctions' er blevet ryddet.");
            return Ok("Cache cleared.");
        }

        /// <summary>
        /// Til manuel stop af auktionsservice listener
        /// </summary>
        /// <param name="itemId"></param>
        /// <returns></returns>
        [HttpPost("stop-auction")]
        public IActionResult StopAuction(string itemId)
        {
            _rabbitListener.StopListening(itemId);
            return Ok($"Stopped listening for auction on item {itemId}.");
        }


        [HttpPost("{itemId}")]
        public async Task<IActionResult> PlaceBid(string itemId, [FromBody] Bid bid)
        {
            _logger.LogInformation($"Placing bid for item {itemId}. Amount: {bid.Amount}");

            // Hent auktionen baseret på itemId
            var auction = await _auctionDbRepository.GetAuctionByItemId(itemId);
            if (auction == null)
            {
                _logger.LogWarning($"Auction for item {itemId} not found.");
                return NotFound($"Auction for item {itemId} not found.");
            }

            // Tjek om buddet er højere end det nuværende højeste bud
            if (bid.Amount <= auction.CurrentBid)
            {
                _logger.LogWarning($"Bid {bid.Amount} is not higher than current bid {auction.CurrentBid}.");
                return BadRequest($"Bid is not higher than current bid {auction.CurrentBid}.");
            }

            // Opdater auktionen med det nye bud
            auction.Bids?.Add(new BidElement { BidAmount = bid.Amount, UserId = bid.UserId!});

            var updated = await _auctionDbRepository.UpdateAuction(auction.Id!, auction);
            if (updated)
            {
                _logger.LogInformation($"Auction {auction.Id} updated with new bid of {bid.Amount}.");
                return Ok(auction);
            }
            else
            {
                _logger.LogError($"Failed to update auction {auction.Id}.");
                return StatusCode(500, "Failed to update auction.");
            }
        }


        [HttpPost("start-listener")]
        public async Task<IActionResult> StartListener(string id)
        {
            // Trigger ScheduleAuctions method manually
            await _auctionService.StartAuctionService(id);
            return Ok($"Started listening for auction on item {id}.");
        }
    }
    
}
