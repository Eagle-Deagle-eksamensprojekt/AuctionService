using MongoDB.Driver;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using AuctionService.Models; // For at referere til Auction modellen

namespace Services
{
    public class AuctionMongoDBService : IAuctionDbRepository
    {
        private readonly IMongoCollection<Auction> _auctionCollection;
        private readonly ILogger<AuctionMongoDBService> _logger;

        public AuctionMongoDBService(ILogger<AuctionMongoDBService> logger, IConfiguration configuration)
        {
            _logger = logger;

            var connectionString = configuration["MongoConnectionString"] ?? "<blank>";
            var databaseName = configuration["DatabaseName"] ?? "<blank>";
            var collectionName = configuration["collectionName"] ?? "<blank>";

            _logger.LogInformation($"Connecting to MongoDB using: {connectionString}");
            _logger.LogInformation($"Using database: {databaseName}");
            _logger.LogInformation($"Using collection: {collectionName}");

            try
            {
                var client = new MongoClient(connectionString);
                var database = client.GetDatabase(databaseName);
                _auctionCollection = database.GetCollection<Auction>(collectionName);
                _logger.LogInformation("Connected to MongoDB.");
            }
            catch (System.Exception ex)
            {
                _logger.LogError("Failed to connect to MongoDB: {0}", ex.Message);
                throw;
            }
        }

        public async Task<bool> CreateAuction(Auction auction)
        {
            try
            {
                await _auctionCollection.InsertOneAsync(auction);
                return true;
            }
            catch (System.Exception ex)
            {
                _logger.LogError("Failed to create auction: {0}", ex.Message);
                return false;
            }
        }

        public async Task<Auction> GetAuctionById(string id)
        {
            return await _auctionCollection.Find(a => a.Id == id).FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<Auction>> GetAllAuctions()
        {
            return await _auctionCollection.Find(a => true).ToListAsync();
        }

        public async Task<bool> UpdateAuction(string id, Auction updatedAuction)
        {
            try
            {
                var result = await _auctionCollection.ReplaceOneAsync(a => a.Id == id, updatedAuction);
                return result.IsAcknowledged && result.ModifiedCount > 0;
            }
            catch (System.Exception ex)
            {
                _logger.LogError("Failed to update auction: {0}", ex.Message);
                return false;
            }
        }

        public async Task<bool> DeleteAuction(string id)
        {
            try
            {
                var result = await _auctionCollection.DeleteOneAsync(a => a.Id == id);
                return result.IsAcknowledged && result.DeletedCount > 0;
            }
            catch (System.Exception ex)
            {
                _logger.LogError("Failed to delete auction: {0}", ex.Message);
                return false;
            }
        }

/*
        public async Task<Auction> GetAuctionByItemId(string itemId)
        {
            return await _auctionCollection.Find(a => a.ItemId == itemId).FirstOrDefaultAsync();
        }
        */

        public async Task<bool> ItemExists(string itemId)
        {
            return await _auctionCollection.Find(a => a.ItemId == itemId).AnyAsync();
        }

        public async Task AddAuctionItem(Item item)
        {
            var auction = new Auction
            {
                ItemId = item.Id,
                StartAuctionDateTime = item.StartAuctionDateTime,
                EndAuctionDateTime = item.EndAuctionDateTime,
                OwnerId = item.OwnerId
            };

            await CreateAuction(auction);
        }
    }
}
