using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Domain.Interfaces;
using SD.ProjectName.Modules.Products.Infrastructure;

namespace SD.ProjectName.Modules.Products.Application
{
    public class ProductExportOptions
    {
        public string Format { get; set; } = "csv";
        public bool UseFilters { get; set; }
        public string? Search { get; set; }
        public string? WorkflowState { get; set; }
    }

    public class ProductCatalogExportService
    {
        private static readonly string[] ExportHeaders = [
            "sku", "title", "description", "price", "stock", "category",
            "shippingmethods", "mainimageurl", "galleryimageurls",
            "weightkg", "lengthcm", "widthcm", "heightcm"
        ];

        private readonly ProductDbContext _context;
        private readonly IProductRepository _repository;
        private readonly ProductExportQueue _queue;
        private readonly ILogger<ProductCatalogExportService> _logger;

        public ProductCatalogExportService(ProductDbContext context, IProductRepository repository, ProductExportQueue queue, ILogger<ProductCatalogExportService> logger)
        {
            _context = context;
            _repository = repository;
            _queue = queue;
            _logger = logger;
        }

        public async Task<ProductExportJob> QueueAsync(string sellerId, ProductExportOptions options, CancellationToken cancellationToken = default)
        {
            var format = NormalizeFormat(options.Format);
            var workflowState = NormalizeWorkflowState(options.WorkflowState);

            var job = new ProductExportJob
            {
                Id = Guid.NewGuid(),
                SellerId = sellerId,
                Format = format,
                Status = ProductExportJobStatus.Queued,
                UseFilters = options.UseFilters,
                Search = options.UseFilters ? options.Search : null,
                WorkflowState = options.UseFilters ? workflowState : null,
                FileName = BuildFileName(format),
                ContentType = GetContentType(format),
                CreatedOn = DateTimeOffset.UtcNow,
                Summary = BuildQueuedSummary(options.UseFilters, options.Search, workflowState, format)
            };

            _context.ProductExportJobs.Add(job);
            await _context.SaveChangesAsync(cancellationToken);
            await _queue.EnqueueAsync(job.Id, cancellationToken);
            return job;
        }

        public async Task<ProductExportJob?> GetJobAsync(Guid id, string sellerId, CancellationToken cancellationToken = default)
        {
            return await _context.ProductExportJobs
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == id && j.SellerId == sellerId, cancellationToken);
        }

        public async Task<List<ProductExportJob>> GetHistoryAsync(string sellerId, int take = 50, CancellationToken cancellationToken = default)
        {
            return await _context.ProductExportJobs
                .AsNoTracking()
                .Where(j => j.SellerId == sellerId)
                .OrderByDescending(j => j.CreatedOn)
                .Take(take)
                .ToListAsync(cancellationToken);
        }

        public async Task ProcessJobAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            var job = await _context.ProductExportJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
            if (job == null)
            {
                return;
            }

            if (ProductExportJobStatus.IsTerminal(job.Status))
            {
                return;
            }

            job.Status = ProductExportJobStatus.Processing;
            await _context.SaveChangesAsync(cancellationToken);

            try
            {
                var products = await LoadProducts(job, cancellationToken);
                job.TotalProducts = products.Count;

                job.FileContent = BuildExportFile(job.Format, products);
                job.CompletedOn = DateTimeOffset.UtcNow;
                job.Status = ProductExportJobStatus.Completed;
                job.Summary = BuildCompletedSummary(products.Count, job);
            }
            catch (Exception ex)
            {
                job.Status = ProductExportJobStatus.Failed;
                job.CompletedOn = DateTimeOffset.UtcNow;
                job.Error = ex.Message;
                job.Summary = "Export failed.";
                _logger.LogError(ex, "Product export job {JobId} failed", jobId);
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        private async Task<List<ProductModel>> LoadProducts(ProductExportJob job, CancellationToken cancellationToken)
        {
            var search = job.UseFilters ? job.Search : null;
            var workflowState = job.UseFilters ? job.WorkflowState : null;
            return await _repository.GetListFiltered(job.SellerId, includeDrafts: true, search, workflowState, cancellationToken);
        }

        private static byte[] BuildExportFile(string format, List<ProductModel> products)
        {
            var builder = new StringBuilder();
            builder.AppendLine(string.Join(",", ExportHeaders));

            foreach (var product in products)
            {
                var values = new[]
                {
                    Escape(product.MerchantSku),
                    Escape(product.Title),
                    Escape(product.Description),
                    Escape(product.Price.ToString(CultureInfo.InvariantCulture)),
                    Escape(product.Stock.ToString(CultureInfo.InvariantCulture)),
                    Escape(product.Category),
                    Escape(product.ShippingMethods),
                    Escape(product.MainImageUrl),
                    Escape(product.GalleryImageUrls),
                    Escape(product.WeightKg?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                    Escape(product.LengthCm?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                    Escape(product.WidthCm?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                    Escape(product.HeightCm?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)
                };

                builder.AppendLine(string.Join(",", values));
            }

            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            return encoding.GetBytes(builder.ToString());
        }

        private static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var escaped = value.Replace("\"", "\"\"");
            return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
                ? $"\"{escaped}\""
                : escaped;
        }

        private static string NormalizeFormat(string? format)
        {
            if (string.Equals(format, "xls", StringComparison.OrdinalIgnoreCase))
            {
                return "xls";
            }

            return "csv";
        }

        private static string? NormalizeWorkflowState(string? workflowState)
        {
            if (string.IsNullOrWhiteSpace(workflowState))
            {
                return null;
            }

            var normalized = workflowState.Trim().ToLowerInvariant();
            return ProductWorkflowStates.IsValid(normalized) ? normalized : null;
        }

        private static string BuildFileName(string format)
        {
            return $"products-export-{DateTime.UtcNow:yyyyMMddHHmmss}.{format}";
        }

        private static string GetContentType(string format)
        {
            return format == "xls" ? "application/vnd.ms-excel" : "text/csv";
        }

        private static string BuildQueuedSummary(bool useFilters, string? search, string? workflowState, string format)
        {
            if (!useFilters)
            {
                return $"Queued export to {format.ToUpperInvariant()} for full catalog.";
            }

            var filters = new List<string>();
            if (!string.IsNullOrWhiteSpace(search))
            {
                filters.Add($"search: {search}");
            }
            if (!string.IsNullOrWhiteSpace(workflowState))
            {
                filters.Add($"state: {workflowState}");
            }

            var filterText = filters.Any() ? string.Join(", ", filters) : "current filters";
            return $"Queued export to {format.ToUpperInvariant()} with {filterText}.";
        }

        private static string BuildCompletedSummary(int count, ProductExportJob job)
        {
            var scope = job.UseFilters ? "filtered products" : "full catalog";
            return $"Exported {count} {scope} to {job.Format.ToUpperInvariant()}.";
        }
    }
}
