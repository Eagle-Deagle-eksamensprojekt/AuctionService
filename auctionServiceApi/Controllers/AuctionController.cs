using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;
using Services;  // For the IAuctionDbRepository interface
using AuctionService.Models;

namespace AuctionServiceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuctionController : ControllerBase
    {
        private readonly ILogger<AuctionController> _logger;
        private readonly IAuctionDbRepository _auctionDbRepository;

        // Constructor to inject dependencies
        public AuctionController(ILogger<AuctionController> logger, IAuctionDbRepository auctionDbRepository)
        {
            _logger = logger;
            _auctionDbRepository = auctionDbRepository;
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

        // Get an auction by ItemId
        [HttpGet("byItemId")]
        public async Task<IActionResult> GetAuctionByItemId([FromQuery] string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return BadRequest("ItemId is required.");
            }

            var auction = await _auctionDbRepository.GetAuctionByItemId(itemId);
            if (auction == null)
            {
                return Ok(null); // Returnerer null, hvis auktionen ikke findes
            }

            return Ok(auction);
        }
    }
}
