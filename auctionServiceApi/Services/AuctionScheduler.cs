using Services;
using AuctionServiceAPI.Controllers;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

// Background service that listens for incoming bids on a RabbitMQ queue
public class AuctionScheduler : BackgroundService
{
    //private Timer? _timer;
    private readonly ILogger<AuctionScheduler> _logger;
    private readonly IAuctionDbRepository _auctionDbRepository;
    private readonly AuctionService _auctionService;
    private readonly RabbitMQListener _rabbitListener;
    

    public AuctionScheduler(ILogger<AuctionScheduler> logger, AuctionService auctionService, IAuctionDbRepository auctionDbRepository, RabbitMQListener rabbitListener)
    {
        _logger = logger;
        _auctionService = auctionService;
        _auctionDbRepository = auctionDbRepository;
        _rabbitListener = rabbitListener;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("AuctionScheduler started.");
            while (!stoppingToken.IsCancellationRequested)
            {
                await ScheduleAuctions();
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred in AuctionScheduler.");
            throw;
        }
    }

        
        /// <summary>
        /// Opstartsmetode for at hente auktioner fra databasen og starte RabbitMQ listener.
        /// </summary>
        public async Task ScheduleAuctions()
        {
            while (true)
            {
                var now = DateTime.UtcNow;

                if (now.Hour == 7 && now.Minute == 0) // Planlægning kl. 07:00
                {
                    _logger.LogInformation("Scheduling auctions for the day...");

                    var items = await _auctionService.GetAndSaveAuctionableItems();

                    foreach (var item in items)
                    {
                        _logger.LogInformation("Starting auction for item {ItemId}", item.Id);
                        StartBidServiceForItem(item.Id!); // Start en BidService for hver item
                        await StartAuctionService(item.Id!); // Start auktionen
                    }
                }

                if (now.Hour == 18 && now.Minute == 0) // Luk ned kl. 18:00
                {
                    _logger.LogInformation("Shutting down auctions for the day...");
                    StopBidServicesForTheDay(); // Stop alle BidService-instanser
                }

                await Task.Delay(TimeSpan.FromMinutes(1)); // Tjek hvert minut
            }
        }
        
        /// <summary>
        /// Start af listener for auktionsservice baseret på itemId.
        /// </summary>
        /// <param name="itemId"></param>
        /// <returns></returns>
        public async Task<IActionResult> StartAuctionService(string itemId)
        {
            var auction = await _auctionDbRepository.GetAuctionByItemId(itemId);
            if (auction == null)
            {
                return new NotFoundObjectResult("Auction not found for the specified item.");
            }

            if (DateTimeOffset.UtcNow >= auction.EndAuctionDateTime)
            {
                return new BadRequestObjectResult("Cannot start listening for an auction that has already ended.");
            }
            await _rabbitListener.ListenOnQueue(itemId, CancellationToken.None, auction.EndAuctionDateTime); // Start listener for auktionen
            return new OkObjectResult($"Started listening for auction on item {itemId}.");
        }

        private void StartBidServiceForItem(string itemId)
        {
            var process = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"run --rm -d --name bidservice_{itemId} -e ITEM_ID={itemId} bidservice:latest",
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var processResult = Process.Start(process);
            _logger.LogInformation($"Started BidService for item {itemId}. Output: {processResult?.StandardOutput.ReadToEnd()}");
        }

        private void StopBidServicesForTheDay()
        {
            var process = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "ps -q --filter \"name=bid-service_\" | xargs docker stop",
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var processResult = Process.Start(process);
            _logger.LogInformation($"Stopped all BidServices. Output: {processResult?.StandardOutput.ReadToEnd()}");

            //_rabbitListener.StopAllListeners();
        }
}