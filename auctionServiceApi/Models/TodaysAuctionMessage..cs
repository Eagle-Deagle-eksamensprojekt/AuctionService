
namespace AuctionServiceAPI.Models
{
    public class TodaysAuctionMessage
    {
        /// <summary>
        /// The ID of the item being auctioned
        /// </summary>
        public string? ItemId { get; set; }
        /// <summary>
        /// The title or name of the item.
        /// </summary>
        public DateTime StartDate { get; set; }
        /// <summary>
        /// The date and time when the auction ends
        /// </summary>
        public DateTimeOffset EndAuctionDateTime { get; set; }

    }
}