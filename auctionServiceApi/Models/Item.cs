namespace AuctionServiceAPI.Models
{
    using System;
    using System.Collections.Generic;

    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Globalization;
    using MongoDB.Bson;
    using MongoDB.Bson.Serialization.Attributes;

    
    public partial class Item
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        /// <summary>
        /// A unique identifier for the item (MongoDB ObjectId).
        /// </summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        /// <summary>
        /// The date and time when the item was created.
        /// </summary>
        [JsonPropertyName("createdDate")]
        public DateTimeOffset CreatedDate { get; set; }

        /// <summary>
        /// A detailed description of the item.
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; }

        /// <summary>
        /// The end date and time for the auction of this item.
        /// </summary>
        [JsonPropertyName("endAuctionDateTime")]
        public DateTimeOffset EndAuctionDateTime { get; set; }

        /// <summary>
        /// The ID of the user who owns this item (must reference a user with isSeller = true).
        /// </summary>
        [JsonPropertyName("ownerId")]
        public string OwnerId { get; set; }

        /// <summary>
        /// The start date and time for the auction of this item.
        /// </summary>
        [JsonPropertyName("startAuctionDateTime")]
        public DateTimeOffset StartAuctionDateTime { get; set; }

        /// <summary>
        /// The starting price for the auction of this item.
        /// </summary>
        [JsonPropertyName("startPrice")]
        public double StartPrice { get; set; }

        /// <summary>
        /// The title or name of the item.
        /// </summary>
        [JsonPropertyName("title")]
        public string Title { get; set; }

        /// <summary>
        /// The estimated value of the item.
        /// </summary>
        [JsonPropertyName("vurderetPrice")]
        public double VurderetPrice { get; set; }
    }
}