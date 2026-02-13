using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SD.ProjectName.Modules.Products.Application;

namespace SD.ProjectName.WebApp.Services
{
    public class ProductExportBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ProductExportQueue _queue;
        private readonly ILogger<ProductExportBackgroundService> _logger;

        public ProductExportBackgroundService(IServiceScopeFactory scopeFactory, ProductExportQueue queue, ILogger<ProductExportBackgroundService> logger)
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
                    var service = scope.ServiceProvider.GetRequiredService<ProductCatalogExportService>();
                    await service.ProcessJobAsync(jobId, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background export job {JobId} failed", jobId);
                }
            }
        }
    }
}
