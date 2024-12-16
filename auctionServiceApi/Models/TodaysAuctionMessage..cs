
namespace AuctionServiceAPI.Models
{
    public class TodaysAuctionMessage
    {
        public string? ItemId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTimeOffset EndAuctionDateTime { get; set; }

    }
}