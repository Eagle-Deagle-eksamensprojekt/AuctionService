using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;
using Services;  // For the IAuctionDbRepository interface
using AuctionService.Models;
using System.Text;
using System.Text.Json;

namespace AuctionServiceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuctionController : ControllerBase
    {
        private readonly ILogger<AuctionController> _logger;
        private readonly IAuctionDbRepository _auctionDbRepository;
        private readonly IConfiguration _config; //bruges til at hente konfigurationsdata
        private readonly IHttpClientFactory _httpClientFactory; //bruges til at lave HTTP requests til andre services

        // Constructor to inject dependencies
        public AuctionController(ILogger<AuctionController> logger,IConfiguration config, IAuctionDbRepository auctionDbRepository, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _config = config;
            _auctionDbRepository = auctionDbRepository;
            _httpClientFactory = httpClientFactory;
        }
        

        // Get an auction by ID
        [HttpGet("{id}")]
        public async Task<IActionResult> GetAuctionById(string id)
        {
            var auction = await _auctionDbRepository.GetAuctionById(id);

            if (auction == null)
            {
                return Ok(null); // Returnerer null, hvis auktionen ikke findes
            }

            return Ok(auction); // Returnerer 200 OK med auktionsdata
        }

        // Get all auctions
        [HttpGet("all")]
        public async Task<IActionResult> GetAllAuctions()
        {
            var auctions = await _auctionDbRepository.GetAllAuctions();
            return Ok(auctions); // Returnerer listen over auktioner med statuskode 200
        }

        // Create a new auction
        [HttpPost]
        public async Task<IActionResult> CreateAuction([FromBody] Auction newAuction)
        {
            if (newAuction == null || string.IsNullOrWhiteSpace(newAuction.Id) || string.IsNullOrWhiteSpace(newAuction.ItemId))
            {
                return BadRequest("Auction data is invalid");
            }

            // Gem auktionen i databasen
            var wasCreated = await _auctionDbRepository.CreateAuction(newAuction);
            if (wasCreated)
            {
                return CreatedAtAction(nameof(GetAuctionById), new { id = newAuction.Id }, newAuction);
            }

            return Conflict("Auction already exists");
        }

        // Update an auction
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateAuction(string id, [FromBody] Auction updatedAuction)
        {
            var wasUpdated = await _auctionDbRepository.UpdateAuction(id, updatedAuction);

            if (!wasUpdated)
            {
                return Ok(null); // Returnerer null, hvis auktionen ikke findes
            }

            return Ok(); // Returnerer 200, hvis opdateringen lykkes
        }

        // Delete an auction
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAuction(string id)
        {
            var wasDeleted = await _auctionDbRepository.DeleteAuction(id);

            if (!wasDeleted)
            {
                return Ok(null); // Returnerer null, hvis auktionen ikke findes
            }

            return NoContent(); // Returnerer 204, hvis sletningen lykkes
        }



        // Get auctionable items from the item service, should be a list
        // of items that are available for auctioning
         private async Task<List<Item>> GetAuctionableItems()
        {
            var existsUrl = $"{_config["ItemServiceEndpoint"]}/auctionable";
            var client = _httpClientFactory.CreateClient();
            HttpResponseMessage response;

            try
            {
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                response = await client.GetAsync(existsUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while connecting to ItemService.");
                return new List<Item>(); // Returner tom liste ved fejl
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ItemService returned a non-success status: {StatusCode}", response.StatusCode);
                return new List<Item>(); // Returner tom liste ved fejlstatus
            }

            try
            {
                string responseContent = await response.Content.ReadAsStringAsync();
                var items = JsonSerializer.Deserialize<List<Item>>(responseContent);
                if (items == null || !items.Any())
                {
                    _logger.LogInformation("No auctionable items found.");
                    return new List<Item>();
                }

                _logger.LogInformation("{Count} auctionable items found.", items.Count);
                return items;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize ItemService response.");
                return new List<Item>();
            }
        }


        // Save auctionable items to the database
        private async Task SaveAuctionableItemsToDatabase(List<Item> items)
        {
            foreach (var item in items)
            {
                // Tjek om item allerede findes i databasen
                var exists = await _auctionDbRepository.ItemExists(item.Id);
                if (!exists)
                {
                    await _auctionDbRepository.AddAuctionItem(item);
                    _logger.LogInformation("Added item {ItemId} to AuctionDatabase.", item.Id);
                }
            }
        }

        // Fetch and save auctionable items
        [HttpGet("auctionable")]
        public async Task<IActionResult> FetchAndSaveAuctionableItems()
        {
            var items = await GetAuctionableItems();
            if (items == null || !items.Any())
            {
                return Ok(new List<Item>()); // Returner tom liste
            }

            await SaveAuctionableItemsToDatabase(items);
            return Ok(items);
        }



    }
}
