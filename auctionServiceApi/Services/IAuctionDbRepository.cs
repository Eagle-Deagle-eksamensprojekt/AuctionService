using System.Collections.Generic;
using System.Threading.Tasks;
using AuctionServiceAPI.Models; // Antager, at Auction-modellen findes her

namespace Services
{
    public interface IAuctionDbRepository
    {
        // Hent en auktion ved ID
        Task<Auction> GetAuctionById(string id);

        // Hent alle auktioner
        Task<IEnumerable<Auction>> GetAllAuctions();

        // Opret en ny auktion
        Task<bool> CreateAuction(Auction newAuction);

        // Opdater en eksisterende auktion
        Task<bool> UpdateAuction(string id, Auction updatedAuction);

        // Slet en auktion
        Task<bool> DeleteAuction(string id);

        // Hent en auktion baseret på ItemId
        //Task<Auction> GetAuctionByItemId(string itemId);

        Task<bool> ItemExists(string itemId); // Check om et item allerede eksistere i databasen
        Task AddAuctionItem(Item item); // Gemmer item i databasen
        Task<bool> CheckItemIsAuctionable(string id, DateTime currentDateTime);
        Task<list<Item>> GetItems(); // Modtager en liste over items
    }
}
