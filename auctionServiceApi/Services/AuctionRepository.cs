using MongoDB.Driver;
using AuctionServiceAPI.Models;

namespace Services
{
    public class AuctionMongoDBService : IAuctionDbRepository
    {
        private readonly IMongoCollection<Auction> _auctionCollection;
        private readonly ILogger<AuctionMongoDBService> _logger;

        public AuctionMongoDBService(ILogger<AuctionMongoDBService> logger, string mongoConnectionString, IConfiguration configuration)
        {
            _logger = logger;

            var connectionString = mongoConnectionString ?? throw new Exception("MongoConnectionString is missing");
            var databaseName = configuration["DatabaseName"] ?? "<blank>";
            var collectionName = configuration["CollectionName"] ?? "<blank>";

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
            catch (Exception ex)
            {
                _logger.LogError("Failed to create auction: {0}", ex.Message);
                return false;
            }
        }

        public async Task<Auction> GetAuctionById(string id)
        {
            return await _auctionCollection.Find(a => a.Id == id).FirstOrDefaultAsync(); // Find auction by ID
        }

        public async Task<IEnumerable<Auction>> GetAllAuctions()
        {
            return await _auctionCollection.Find(_ => true).ToListAsync(); // Find all auctions
        }

        public async Task<bool> UpdateAuction(string id, Auction updatedAuction)
        {
            try
            {
                var result = await _auctionCollection.ReplaceOneAsync(a => a.Id == id, updatedAuction); // Replace auction with updated auction
                if (result.IsAcknowledged && result.ModifiedCount > 0) // Check if the update was successful
                {
                    _logger.LogInformation($"Successfully updated auction {id}.");
                    return true;
                }

                _logger.LogWarning($"Update for auction {id} was acknowledged but did not modify any documents.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to update auction {id}.");
                return false;
            }
        }
                                        

        public async Task<bool> DeleteAuction(string id)
        {
            try
            {
                var result = await _auctionCollection.DeleteOneAsync(a => a.Id == id); // Delete auction by ID
                return result.IsAcknowledged && result.DeletedCount > 0; // Check if the delete was successful
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to delete auction: {0}", ex.Message);
                return false;
            }
        }

        public async Task<Auction?> GetAuctionByItemId(string itemId)
        {
            try
            {
                var auction = await _auctionCollection.Find(a => a.ItemId == itemId).FirstOrDefaultAsync(); // Find auction by item ID
                if (auction == null) // Check if the auction was found
                {
                    _logger.LogWarning($"No auction found for item {itemId}.");
                }
                _logger.LogInformation($"Retrieved auction for item {itemId}.");
                return auction;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error while retrieving auction for item {itemId}.");
                return null;
            }
        }


        public async Task<bool> ItemExists(string itemId)
        {
            return await _auctionCollection.Find(a => a.ItemId == itemId).AnyAsync(); // Check if an auction exists for the item
        }

        public async Task AddAuctionItem(Item item) // Create an auction for an item
        {
            var today = DateTimeOffset.UtcNow.Date;
            var startAuctionTime = today.AddHours(8); // Start kl. 08:00
            var endAuctionTime = startAuctionTime.AddHours(8); // Slut kl. 16:00

            var auction = new Auction
            {
                ItemId = item.Id!,
                StartAuctionDateTime = startAuctionTime,
                EndAuctionDateTime = endAuctionTime,
                Bids = new List<BidElement>()
            };

            await CreateAuction(auction);
            _logger.LogInformation("Auction created for item {ItemId}", item.Id);
        }
    }
}
