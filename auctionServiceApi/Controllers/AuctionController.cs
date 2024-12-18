using Microsoft.AspNetCore.Mvc;
using AuctionServiceAPI.Models;
using Services; 
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;


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
        private readonly RabbitMQListener _rabbitListener;
        private readonly AuctionService _auctionService;

        public AuctionController(
            ILogger<AuctionController> logger,
            IAuctionDbRepository auctionDbRepository,
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            AuctionService auctionService,
            RabbitMQListener rabbitListener)
        {
            _logger = logger;
            _auctionDbRepository = auctionDbRepository;
            _httpClientFactory = httpClientFactory;
            _config = config;
            _auctionService = auctionService;
            _rabbitListener = rabbitListener;
        }

        /// <summary>
        /// Hent version af Service
        /// </summary>
        [Authorize]
        [HttpGet("version")]
        public async Task<Dictionary<string, string>> GetVersion()
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
        [Authorize]
        [HttpGet("all")]
        public async Task<IActionResult> GetAllAuctions()
        {
            // Hent auktioner fra databasen og konverter til List<Auction>
            var auctions = (await _auctionDbRepository.GetAllAuctions()).ToList(); // Hent alle auktioner fra databasen
            _logger.LogInformation("Auktioner hentet fra databasen.");
            return Ok( new {auctions}); // Returner auktioner som JSON
        }

        /// <summary>
        /// Hent en specifik auktion ved ID.
        /// </summary>
        [Authorize]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetAuctionById(string id)
        {
            var auction = await _auctionDbRepository.GetAuctionById(id); // Hent auktion fra databasen

            if (auction == null)
                return NotFound($"Auction with ID {id} not found.");

            _logger.LogInformation($"Auktion med ID {id} hentet fra databasen.");
            return Ok(auction);
        }

        /// <summary>
        /// Fetches and saves auctionable items, then publishes them to RabbitMQ.
        /// </summary>
        [Authorize]
        [HttpGet("fetch-and-save-auctionable")]
        public async Task<IActionResult> FetchAndSaveAuctionableItems()
        {
            try
            {
                var auctionableItems = await _auctionService.GetAndSaveAuctionableItems(); // Hent og gem auktionsvarer
                if (auctionableItems == null || !auctionableItems.Any())
                {
                    _logger.LogInformation("No auctionable items were found or saved.");
                    return Ok("No auctionable items were found or saved."); // Ingen auktionsvarer fundet
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
        [AllowAnonymous]
        [HttpGet("auctionable/{itemId}")]
        public async Task<IActionResult> CheckItemIsAuctionable(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return BadRequest("Item ID is required.");
            }

            if (!MongoDB.Bson.ObjectId.TryParse(itemId, out _)) // Tjek om item ID er en gyldig 24 karakter hex string
            {
                _logger.LogWarning("Invalid Item ID format: {ItemId}. Must be a 24-character hex string.", itemId);
                return BadRequest("Invalid Item ID format. Must be a 24-character hex string.");
            }

            try
            {
                _logger.LogInformation("Checking if item {ItemId} is auctionable.", itemId);

                var auction = await _auctionDbRepository.GetAuctionByItemId(itemId);

                if (auction == null)
                {
                    _logger.LogWarning("No auction found for item {ItemId}.", itemId);
                    return NotFound(new { Message = $"No auction found for item {itemId}" });
                }

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

        [Authorize]
        [HttpPost("stop-auction")]
        public IActionResult StopAuction(string itemId)
        {
            _rabbitListener.StopListening(itemId); // Stop listener for auktion
            return Ok($"Stopped listening for auction on item {itemId}.");
        }

        [Authorize]
        [HttpPost("{itemId}")]
        public async Task<IActionResult> PlaceBid(string itemId, [FromBody] Bid bid)
        {
            _logger.LogInformation($"Placing bid for item {itemId}. Amount: {bid.Amount}");

            var auction = await _auctionDbRepository.GetAuctionByItemId(itemId); // Hent auktion fra databasen
            if (auction == null)
            {
                _logger.LogWarning($"Auction for item {itemId} not found.");
                return NotFound($"Auction for item {itemId} not found.");
            }

            if (bid.Amount <= auction.CurrentBid) // Tjek om det nye bud er højere end det nuværende
            {
                _logger.LogWarning($"Bid {bid.Amount} is not higher than current bid {auction.CurrentBid}.");
                return BadRequest($"Bid is not higher than current bid {auction.CurrentBid}.");
            }

            auction.Bids?.Add(new BidElement { BidAmount = bid.Amount, UserId = bid.UserId! }); // Tilføj det nye bud til auktionen

            var updated = await _auctionDbRepository.UpdateAuction(auction.Id!, auction); // Opdater auktionen i databasen
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

        [Authorize]
        [HttpPost("start-listener")]
        public async Task<IActionResult> StartListener(string id)
        {
            await _auctionService.StartAuctionService(id); // Start listener for auktion
            return Ok($"Started listening for auction on item {id}.");
        }

        // Hurtig klasse til at tjekke prisen på et item
        public class PriceRequest
        {
            public string ItemId { get; set; }
        }

        [Authorize]
        [HttpPost("price")]
        public async Task<IActionResult> GetPrice([FromBody] PriceRequest request)
        {
            if (string.IsNullOrEmpty(request.ItemId))
            {
                return BadRequest("ItemId is required.");
            }

            var auction = await _auctionDbRepository.GetAuctionByItemId(request.ItemId); // Hent auktion fra databasen
            if (auction == null)
            {
                return NotFound($"Auction for item {request.ItemId} not found.");
            }

            return Ok(auction.CurrentBid); // Returner nuværende højeste bud
        }

        [Authorize]
        [HttpPost("data")]
        public async Task<IActionResult> GetData([FromBody] PriceRequest request) // genbrug af PriceRequest for nemhed
        {
            if (string.IsNullOrEmpty(request.ItemId))
            {
                return BadRequest("ItemId is required.");
            }

            var auction = await _auctionDbRepository.GetAuctionByItemId(request.ItemId); // Hent auktion fra databasen
            if (auction == null)
            {
                return NotFound($"Auction for item {request.ItemId} not found.");
            }

            return Ok(auction); // Returner auktion
        }
    }
}
