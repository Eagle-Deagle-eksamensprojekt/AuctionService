using System.Collections.Generic;
using System.Threading.Tasks;
using AuctionServiceAPI.Models;
using Microsoft.AspNetCore.Mvc; // Antager, at Auction-modellen findes her

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
    }
}
