using System.Globalization;
using System.Text;
using ExcelDataReader;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Domain.Interfaces;
using SD.ProjectName.Modules.Products.Infrastructure;

namespace SD.ProjectName.Modules.Products.Application
{
    public class ProductCatalogImportService
    {
        private static readonly string[] RequiredHeaders = ["sku", "title", "price", "stock", "category"];
        private static readonly string[] KnownHeaders = [
            "sku", "title", "description", "price", "stock", "category", "shippingmethods",
            "mainimageurl", "galleryimageurls", "weightkg", "lengthcm", "widthcm", "heightcm"
        ];

        private readonly ProductDbContext _context;
        private readonly ManageCategories _categories;
        private readonly IProductRepository _repository;
        private readonly ProductImportQueue _queue;
        private readonly ILogger<ProductCatalogImportService> _logger;

        static ProductCatalogImportService()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public ProductCatalogImportService(
            ProductDbContext context,
            ManageCategories categories,
            IProductRepository repository,
            ProductImportQueue queue,
            ILogger<ProductCatalogImportService> logger)
        {
            _context = context;
            _categories = categories;
            _repository = repository;
            _queue = queue;
            _logger = logger;
        }

        public async Task<(ProductImportPreview Preview, ProductImportJob? Job)> CreatePendingJobAsync(string sellerId, byte[] fileContent, string fileName, CancellationToken cancellationToken = default)
        {
            var preview = await PreviewAsync(sellerId, fileContent, fileName, cancellationToken);
            if (preview.Errors.Any())
            {
                return (preview, null);
            }

            var job = new ProductImportJob
            {
                SellerId = sellerId,
                FileName = fileName,
                ContentType = GuessContentType(fileName),
                FileContent = fileContent,
                TotalRows = preview.TotalRows,
                CreatedCount = preview.CreateCount,
                UpdatedCount = preview.UpdateCount,
                FailedCount = 0,
                Summary = BuildSummary(preview),
                Status = ProductImportJobStatus.PendingConfirmation
            };

            _context.ProductImportJobs.Add(job);
            await _context.SaveChangesAsync(cancellationToken);
            return (preview, job);
        }

        public async Task<bool> QueueAsync(Guid jobId, string sellerId, CancellationToken cancellationToken = default)
        {
            var job = await _context.ProductImportJobs.FirstOrDefaultAsync(j => j.Id == jobId && j.SellerId == sellerId, cancellationToken);
            if (job == null || job.Status != ProductImportJobStatus.PendingConfirmation)
            {
                return false;
            }

            job.Status = ProductImportJobStatus.Queued;
            await _context.SaveChangesAsync(cancellationToken);
            await _queue.EnqueueAsync(job.Id, cancellationToken);
            return true;
        }

        public async Task<ProductImportPreview> PreviewAsync(string sellerId, byte[] fileContent, string fileName, CancellationToken cancellationToken = default)
        {
            var parse = await ParseAsync(fileContent, fileName, cancellationToken);
            var errors = new List<ProductImportRowError>();

            foreach (var missing in RequiredHeaders.Where(h => !parse.Headers.Contains(h)))
            {
                errors.Add(new ProductImportRowError(0, $"Missing required column: {missing}"));
            }

            if (errors.Any())
            {
                return new ProductImportPreview(parse.TotalRows, 0, 0, errors, new List<ProductImportRow>());
            }

            if (parse.TotalRows == 0)
            {
                errors.Add(new ProductImportRowError(0, "The file does not contain any data rows."));
                return new ProductImportPreview(0, 0, 0, errors, new List<ProductImportRow>());
            }

            var categories = await _categories.GetActiveCategories();
            var categoryLookup = categories.ToDictionary(c => c.FullPath, StringComparer.OrdinalIgnoreCase);

            var skuSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var validRows = new List<ProductImportRow>();

            foreach (var raw in parse.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsRowEmpty(raw.Values))
                {
                    continue;
                }

                var parsed = ParseRow(raw, categoryLookup, errors);
                if (parsed == null)
                {
                    continue;
                }

                if (!skuSet.Add(parsed.MerchantSku))
                {
                    errors.Add(new ProductImportRowError(raw.RowNumber, $"Duplicate SKU '{parsed.MerchantSku}' in file."));
                    continue;
                }

                validRows.Add(parsed);
            }

            var existing = await _context.Products
                .Where(p => p.SellerId == sellerId && skuSet.Contains(p.MerchantSku))
                .Select(p => new { p.MerchantSku, p.WorkflowState })
                .ToListAsync(cancellationToken);

            foreach (var record in existing.Where(e => e.WorkflowState == ProductWorkflowStates.Archived))
            {
                errors.Add(new ProductImportRowError(0, $"Archived product already uses SKU '{record.MerchantSku}'. Restore or change SKU before importing."));
                validRows.RemoveAll(r => string.Equals(r.MerchantSku, record.MerchantSku, StringComparison.OrdinalIgnoreCase));
            }

            var existingLookup = existing
                .Where(e => e.WorkflowState != ProductWorkflowStates.Archived)
                .ToDictionary(e => e.MerchantSku, StringComparer.OrdinalIgnoreCase);

            var updateCount = validRows.Count(r => existingLookup.ContainsKey(r.MerchantSku));
            var createCount = validRows.Count - updateCount;

            return new ProductImportPreview(parse.TotalRows, createCount, updateCount, errors, validRows);
        }

        public async Task<ProductImportJob?> GetJobAsync(Guid id, string sellerId, CancellationToken cancellationToken = default)
        {
            return await _context.ProductImportJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id && j.SellerId == sellerId, cancellationToken);
        }

        public async Task<List<ProductImportJob>> GetHistoryAsync(string sellerId, int take = 50, CancellationToken cancellationToken = default)
        {
            return await _context.ProductImportJobs
                .AsNoTracking()
                .Where(j => j.SellerId == sellerId)
                .OrderByDescending(j => j.CreatedOn)
                .Take(take)
                .ToListAsync(cancellationToken);
        }

        public async Task ProcessJobAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            var job = await _context.ProductImportJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
            if (job == null || job.FileContent == null)
            {
                return;
            }

            if (job.Status == ProductImportJobStatus.Completed)
            {
                return;
            }

            job.Status = ProductImportJobStatus.Processing;
            await _context.SaveChangesAsync(cancellationToken);

            try
            {
                var preview = await PreviewAsync(job.SellerId, job.FileContent, job.FileName, cancellationToken);
                var errors = new List<ProductImportRowError>(preview.Errors);
                var rows = preview.Rows;
                job.TotalRows = preview.TotalRows;

                if (!rows.Any())
                {
                    job.Status = ProductImportJobStatus.Failed;
                    job.FailedCount = errors.Count;
                    job.CreatedCount = 0;
                    job.UpdatedCount = 0;
                    job.ErrorReport = BuildErrorReport(errors);
                    job.Summary = "No rows imported.";
                    job.CompletedOn = DateTimeOffset.UtcNow;
                    await _context.SaveChangesAsync(cancellationToken);
                    return;
                }

                var skuSet = rows.Select(r => r.MerchantSku).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var existingProducts = await _context.Products
                    .Where(p => p.SellerId == job.SellerId && skuSet.Contains(p.MerchantSku))
                    .ToListAsync(cancellationToken);
                var existingLookup = existingProducts.ToDictionary(p => p.MerchantSku, StringComparer.OrdinalIgnoreCase);

                job.CreatedCount = 0;
                job.UpdatedCount = 0;
                job.FailedCount = 0;

                foreach (var row in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        if (existingLookup.TryGetValue(row.MerchantSku, out var existing))
                        {
                            UpdateProduct(existing, row);
                            job.UpdatedCount++;
                        }
                        else
                        {
                            var product = CreateProduct(row, job.SellerId);
                            _context.Products.Add(product);
                            job.CreatedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to import row {Row}", row.RowNumber);
                        errors.Add(new ProductImportRowError(row.RowNumber, "Unexpected error while importing this row."));
                    }
                }

                job.FailedCount = errors.Count;
                job.ErrorReport = BuildErrorReport(errors);
                job.Status = job.CreatedCount + job.UpdatedCount > 0 || !errors.Any()
                    ? ProductImportJobStatus.Completed
                    : ProductImportJobStatus.Failed;
                job.Summary = $"Total: {job.TotalRows}, Created: {job.CreatedCount}, Updated: {job.UpdatedCount}, Failed: {job.FailedCount}";
                job.CompletedOn = DateTimeOffset.UtcNow;

                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Import job {JobId} failed", jobId);
                job.Status = ProductImportJobStatus.Failed;
                job.CompletedOn = DateTimeOffset.UtcNow;
                job.ErrorReport = $"Unexpected error: {ex.Message}";
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        private static ProductModel CreateProduct(ProductImportRow row, string sellerId)
        {
            return new ProductModel
            {
                Title = row.Title,
                MerchantSku = row.MerchantSku,
                Price = row.Price,
                Stock = row.Stock,
                Category = row.CategoryFullPath,
                CategoryId = row.CategoryId,
                Description = row.Description,
                MainImageUrl = row.MainImageUrl,
                GalleryImageUrls = row.GalleryImageUrls,
                WeightKg = row.WeightKg,
                LengthCm = row.LengthCm,
                WidthCm = row.WidthCm,
                HeightCm = row.HeightCm,
                ShippingMethods = row.ShippingMethods,
                WorkflowState = ProductWorkflowStates.Draft,
                SellerId = sellerId
            };
        }

        private static void UpdateProduct(ProductModel product, ProductImportRow row)
        {
            product.Title = row.Title;
            product.MerchantSku = row.MerchantSku;
            product.Price = row.Price;
            product.Stock = row.Stock;
            product.Category = row.CategoryFullPath;
            product.CategoryId = row.CategoryId;
            product.Description = row.Description;
            product.MainImageUrl = row.MainImageUrl;
            product.GalleryImageUrls = row.GalleryImageUrls;
            product.WeightKg = row.WeightKg;
            product.LengthCm = row.LengthCm;
            product.WidthCm = row.WidthCm;
            product.HeightCm = row.HeightCm;
            product.ShippingMethods = row.ShippingMethods;
        }

        private static bool IsRowEmpty(IReadOnlyDictionary<string, string?> values)
        {
            return values.Values.All(v => string.IsNullOrWhiteSpace(v));
        }

        private ProductImportRow? ParseRow(ProductImportRawRow raw, Dictionary<string, CategoryModel> categoryLookup, List<ProductImportRowError> errors)
        {
            var sku = raw.Get("sku");
            if (string.IsNullOrWhiteSpace(sku))
            {
                errors.Add(new ProductImportRowError(raw.RowNumber, "SKU is required."));
                return null;
            }

            var title = raw.Get("title");
            if (string.IsNullOrWhiteSpace(title))
            {
                errors.Add(new ProductImportRowError(raw.RowNumber, "Title is required."));
                return null;
            }

            var priceText = raw.Get("price");
            if (!decimal.TryParse(priceText, NumberStyles.Any, CultureInfo.InvariantCulture, out var price) || price <= 0)
            {
                errors.Add(new ProductImportRowError(raw.RowNumber, "Price must be a number greater than zero."));
                return null;
            }

            var stockText = raw.Get("stock");
            if (!int.TryParse(stockText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stock) || stock < 0)
            {
                errors.Add(new ProductImportRowError(raw.RowNumber, "Stock must be zero or a positive whole number."));
                return null;
            }

            var categoryText = raw.Get("category");
            if (string.IsNullOrWhiteSpace(categoryText))
            {
                errors.Add(new ProductImportRowError(raw.RowNumber, "Category is required."));
                return null;
            }

            if (!categoryLookup.TryGetValue(categoryText.Trim(), out var category))
            {
                errors.Add(new ProductImportRowError(raw.RowNumber, $"Category '{categoryText}' is not valid. Use the full path from the category tree."));
                return null;
            }

            return new ProductImportRow(
                raw.RowNumber,
                sku.Trim(),
                title.Trim(),
                raw.Get("description")?.Trim(),
                price,
                stock,
                category.Id,
                category.FullPath,
                raw.Get("shippingmethods")?.Trim(),
                raw.Get("mainimageurl")?.Trim(),
                raw.Get("galleryimageurls")?.Trim(),
                TryParseNullableDecimal(raw.Get("weightkg")),
                TryParseNullableDecimal(raw.Get("lengthcm")),
                TryParseNullableDecimal(raw.Get("widthcm")),
                TryParseNullableDecimal(raw.Get("heightcm"))
            );
        }

        private async Task<ProductImportParseResult> ParseAsync(byte[] fileContent, string fileName, CancellationToken cancellationToken)
        {
            var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
            if (extension == ".csv")
            {
                return await ParseCsvAsync(fileContent, cancellationToken);
            }

            if (extension == ".xls" || extension == ".xlsx")
            {
                return await ParseExcelAsync(fileContent, cancellationToken);
            }

            return new ProductImportParseResult([], [], 0);
        }

        private static async Task<ProductImportParseResult> ParseCsvAsync(byte[] content, CancellationToken cancellationToken)
        {
            await using var stream = new MemoryStream(content);
            using var parser = new TextFieldParser(stream)
            {
                TextFieldType = FieldType.Delimited,
                HasFieldsEnclosedInQuotes = true,
                TrimWhiteSpace = false
            };
            parser.SetDelimiters(",");

            if (parser.EndOfData)
            {
                return new ProductImportParseResult([], [], 0);
            }

            var headers = parser.ReadFields()?.Select(NormalizeHeader).Where(h => h != null).Cast<string>().ToList() ?? [];
            var rows = new List<ProductImportRawRow>();
            var rowNumber = 2;

            while (!parser.EndOfData)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fields = parser.ReadFields();
                if (fields == null)
                {
                    continue;
                }

                var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < headers.Count && i < fields.Length; i++)
                {
                    map[headers[i]] = fields[i];
                }

                rows.Add(new ProductImportRawRow(rowNumber, map));
                rowNumber++;
            }

            return new ProductImportParseResult(headers, rows, rows.Count);
        }

        private static async Task<ProductImportParseResult> ParseExcelAsync(byte[] content, CancellationToken cancellationToken)
        {
            await using var stream = new MemoryStream(content);
            using var reader = ExcelReaderFactory.CreateReader(stream);

            var headers = new List<string>();
            var rows = new List<ProductImportRawRow>();
            var rowNumber = 0;

            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                rowNumber++;
                if (rowNumber == 1)
                {
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        headers.Add(NormalizeHeader(reader.GetValue(i)?.ToString()) ?? string.Empty);
                    }

                    continue;
                }

                var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    if (string.IsNullOrWhiteSpace(header))
                    {
                        continue;
                    }

                    map[header] = reader.GetValue(i)?.ToString();
                }

                rows.Add(new ProductImportRawRow(rowNumber, map));
            }

            var cleanedHeaders = headers.Where(h => !string.IsNullOrWhiteSpace(h)).ToList();
            return new ProductImportParseResult(cleanedHeaders, rows, rows.Count);
        }

        private static decimal? TryParseNullableDecimal(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        }

        private static string? NormalizeHeader(string? header)
        {
            var cleaned = header?.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return null;
            }

            var normalized = cleaned.Trim().ToLowerInvariant();
            return KnownHeaders.Contains(normalized) ? normalized : normalized;
        }

        private static string? BuildErrorReport(List<ProductImportRowError> errors)
        {
            if (!errors.Any())
            {
                return null;
            }

            var lines = errors
                .OrderBy(e => e.RowNumber)
                .Select(e => e.RowNumber > 0 ? $"Row {e.RowNumber}: {e.Message}" : e.Message);
            return string.Join(Environment.NewLine, lines);
        }

        private static string BuildSummary(ProductImportPreview preview)
        {
            return $"Total: {preview.TotalRows}, Created: {preview.CreateCount}, Updated: {preview.UpdateCount}, Failed: {preview.Errors.Count}";
        }

        private static string GuessContentType(string fileName)
        {
            var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
            return extension switch
            {
                ".csv" => "text/csv",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".xls" => "application/vnd.ms-excel",
                _ => "application/octet-stream"
            };
        }
    }

    public class ProductImportPreview
    {
        public ProductImportPreview(int totalRows, int createCount, int updateCount, List<ProductImportRowError> errors, List<ProductImportRow> rows)
        {
            TotalRows = totalRows;
            CreateCount = createCount;
            UpdateCount = updateCount;
            Errors = errors;
            Rows = rows;
        }

        public int TotalRows { get; }

        public int CreateCount { get; }

        public int UpdateCount { get; }

        public List<ProductImportRowError> Errors { get; }

        public List<ProductImportRow> Rows { get; }
    }

    public record ProductImportRowError(int RowNumber, string Message);

    public record ProductImportRow(
        int RowNumber,
        string MerchantSku,
        string Title,
        string? Description,
        decimal Price,
        int Stock,
        int CategoryId,
        string CategoryFullPath,
        string? ShippingMethods,
        string? MainImageUrl,
        string? GalleryImageUrls,
        decimal? WeightKg,
        decimal? LengthCm,
        decimal? WidthCm,
        decimal? HeightCm);

    internal record ProductImportRawRow(int RowNumber, IReadOnlyDictionary<string, string?> Values)
    {
        public string? Get(string key)
        {
            return Values.TryGetValue(key, out var value) ? value : null;
        }
    }

    internal record ProductImportParseResult(List<string> Headers, List<ProductImportRawRow> Rows, int TotalRows);
}
