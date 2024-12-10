using System.Collections.Generic;
using System.Threading.Tasks;
using AuctionServiceAPI.Models; // Antager, at Auction-modellen findes her

namespace Services
{
    public interface IAuctionDbRepository
    {
        Task<Auction> GetAuctionById(string id);
        Task<IEnumerable<Auction>> GetAllAuctions();
        Task<bool> CreateAuction(Auction newAuction);
        Task<bool> UpdateAuction(string id, Auction updatedAuction);
        Task<bool> DeleteAuction(string id);
        Task<Auction> GetAuctionByItemId(string itemId);
        Task<bool> ItemExists(string itemId);
        Task AddAuctionItem(Item item);
        Task<bool> CheckItemIsAuctionable(string itemId, DateTime currentDateTime); // Check om et item er klar til auktion


        
        //Task<bool> UpdateAuctionBid(string auctionId, double newBid, string bidderId);
        //Task<bool> UpdateAuctionWinner(string auctionId, string winnerId);

        /*
        // Hent en auktion ved ID
        Task<Auction> GetAuctionById(string id);

        // Hent alle auktioner
        //Task<IEnumerable<Auction>> GetAllAuctions();

        // Opret en ny auktion
        Task<bool> CreateAuction(Auction newAuction);

        // Opdater en eksisterende auktion
        Task<bool> UpdateAuction(string id, Auction updatedAuction);

        // Slet en auktion
        Task<bool> DeleteAuction(string id);

        // Hent en auktion baseret på ItemId
        //Task<Auction> GetAuctionByItemId(string itemId);

        Task<bool> ItemExists(string itemId); // Check om et item allerede eksistere i databasen
        Task AddAuctionItem(Item item); // Gemmer item i database
        */
    }
}
