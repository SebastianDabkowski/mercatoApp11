using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SD.ProjectName.Modules.Products.Application;

namespace SD.ProjectName.WebApp.Services
{
    public class ProductImportBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ProductImportQueue _queue;
        private readonly ILogger<ProductImportBackgroundService> _logger;

        public ProductImportBackgroundService(IServiceScopeFactory scopeFactory, ProductImportQueue queue, ILogger<ProductImportBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _queue = queue;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var jobId in _queue.DequeueAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<ProductCatalogImportService>();
                    await service.ProcessJobAsync(jobId, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background import job {JobId} failed", jobId);
                }
            }
        }
    }
}
