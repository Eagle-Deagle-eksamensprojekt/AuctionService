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

                if (now.Hour == 1 && now.Minute == 13) // Planlægning kl. 07:00
                {
                    _logger.LogInformation("Scheduling auctions for the day...");

                    var items = await _auctionService.GetAndSaveAuctionableItems();

                    foreach (var item in items)
                    {
                        _logger.LogInformation("Starting auction for item {ItemId}", item.Id);
                        StartBidServiceForItem(item.Id!); // Start en BidService for hver item
                        await StartAuctionService(item.Id!); // Start auktionen lytter
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
                // Docker netværk
                var networkName = "gron-network";
                //var AuctionServiceEndpoint = _config["AuctionServiceEndpoint"];
                var AuctionServiceEndpoint = "http://auctionService:8080/auction";

                // Tildel en unik port baseret på itemId's hash (simpelt eksempel)
                //Tilføj i controller, at der skal der skal løbes porte igennem for at finde ud af hvilken port bidService bruger
                var port = 5010 + Math.Abs(itemId.GetHashCode() % 1000); // Generer port mellem 5000 og 5999

                var process = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"run --rm -d --name bidservice_{itemId} " +
                                $"-p {port}:8080 " + // Map værtsmaskinens {port} til containerens 8080
                                $"-e ITEM_ID={itemId} " +
                                $"-e RABBITMQ_HOST={Environment.GetEnvironmentVariable("RABBITMQ_HOST")} " +
                                $"-e AuctionServiceEndpoint={AuctionServiceEndpoint} " +
                                $"-e LOKI_URL={Environment.GetEnvironmentVariable("LOKI_URL")} " +
                                $"--network {networkName} " +
                                "mikkelhv/4sembidservice:latest",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                // Start process
                var result = Process.Start(process);
                var output = result?.StandardOutput.ReadToEnd();
                var error = result?.StandardError.ReadToEnd();
               
                if (!string.IsNullOrEmpty(error))
                {
                    _logger.LogError($"Error starting BidService for item {itemId}: {error}");
                }

                _logger.LogInformation($"Started BidService for item {itemId}. Output: {output}");

                // Optional: Trigger NGINX reload (if dynamic routing is used)
                _auctionService.ReloadNginx();
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