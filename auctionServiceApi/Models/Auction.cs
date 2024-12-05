namespace AuctionService.Models
{
    using System;
    using System.Collections.Generic;

    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Globalization;

    public partial class Auction
    {
        /// <summary>
        /// MongoDB's auto-generated ID for the auction document
        /// </summary>
        [JsonPropertyName("_id")]
        public string Id { get; set; }

        /// <summary>
        /// A unique identifier for the auction
        /// </summary>
        [JsonPropertyName("AuctionId")]
        public string AuctionId { get; set; }

        /// <summary>
        /// List of bids placed on the auction
        /// </summary>
        [JsonPropertyName("Bids")]
        public List<BidElement> Bids { get; set; }

        /// <summary>
        /// Reference to the item being auctioned
        /// </summary>
        [JsonPropertyName("ItemID")]
        public string ItemId { get; set; }
    }

    public partial class BidElement
    {
        /// <summary>
        /// Amount of the bid
        /// </summary>
        [JsonPropertyName("BidAmount")]
        public double BidAmount { get; set; }

        /// <summary>
        /// The unique identifier for the user who placed the bid
        /// </summary>
        [JsonPropertyName("UserId")]
        public string UserId { get; set; }
    }
}
