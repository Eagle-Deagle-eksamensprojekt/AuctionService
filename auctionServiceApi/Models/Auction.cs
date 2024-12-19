namespace AuctionServiceAPI.Models
{
    using System;
    using System.Collections.Generic;

    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Globalization;
    using MongoDB.Bson.Serialization.Attributes;
    using MongoDB.Bson;

    public partial class Auction
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        /// <summary>
        /// MongoDB's auto-generated ID for the auction document
        /// </summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        /// <summary>
        /// List of bids placed on the auction
        /// </summary>
        [JsonPropertyName("bids")]
        public List<BidElement>? Bids { get; set; }

        /// <summary>
        /// The current bid amount for the auction
        /// </summary>
        [JsonIgnore] // Beregnede felter behøver ikke serialiseres
        public double CurrentBid => Bids?.LastOrDefault()?.BidAmount ?? 0;

        /// <summary>
        /// The ID of the user who is currently winning the auction
        /// </summary>
        [JsonIgnore]
        public string CurrentWinnerId => Bids?.LastOrDefault()?.UserId ?? string.Empty;                      

        /// <summary>
        /// The start date and time for the auction of this item.
        /// </summary>
        [JsonPropertyName("startAuctionDateTime")]
        public DateTimeOffset StartAuctionDateTime { get; set; }

        /// <summary>
        /// The date and time when the auction ends
        /// </summary>
        [JsonPropertyName("endAuctionDateTime")]
        public DateTimeOffset EndAuctionDateTime { get; set; }

        /// <summary>
        /// The ID of the item being auctioned
        /// </summary>
        [JsonPropertyName("itemId")]
        public string? ItemId { get; set; }
    }

    public class BidElement
    {
        /// <summary>
        /// Amount of the bid
        /// </summary>
        [JsonPropertyName("BidAmount")]
        public double BidAmount { get; set; } = 0;

        /// <summary>
        /// The unique identifier for the user who placed the bid
        /// </summary>
        [JsonPropertyName("UserId")]
        public string UserId { get; set; }  = string.Empty;
    }
}
