using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Infrastructure;
using SD.ProjectName.WebApp.Data;

namespace SD.ProjectName.WebApp.Services
{
    public static class SellerSalesGranularities
    {
        public const string Day = "day";
        public const string Week = "week";
        public const string Month = "month";

        public static bool IsValid(string? granularity) =>
            granularity is Day or Week or Month;

        public static string Normalize(string? granularity) =>
            IsValid(granularity?.Trim().ToLowerInvariant())
                ? granularity!.Trim().ToLowerInvariant()
                : Day;
    }

    public record SellerSalesPoint(DateTimeOffset PeriodStart, string Label, decimal Gmv, int Orders);

    public record SellerProductOption(int Id, string Title);

    public record SellerCategoryOption(int Id, string Name, string FullPath);

    public record SellerSalesDashboardResult(
        decimal TotalGmv,
        int TotalOrders,
        IReadOnlyList<SellerSalesPoint> Series,
        IReadOnlyList<SellerProductOption> ProductOptions,
        IReadOnlyList<SellerCategoryOption> CategoryOptions,
        int? ActiveProductId,
        int? ActiveCategoryId);

    public class SellerReportingService
    {
        private readonly ApplicationDbContext _applicationDbContext;
        private readonly ProductDbContext _productDbContext;
        private readonly ILogger<SellerReportingService> _logger;
        private readonly JsonSerializerOptions _serializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public SellerReportingService(
            ApplicationDbContext applicationDbContext,
            ProductDbContext productDbContext,
            ILogger<SellerReportingService> logger)
        {
            _applicationDbContext = applicationDbContext;
            _productDbContext = productDbContext;
            _logger = logger;
        }

        public async Task<SellerSalesDashboardResult> GetSalesAsync(
            string sellerId,
            DateTimeOffset from,
            DateTimeOffset to,
            string granularity,
            int? productId,
            int? categoryId,
            CancellationToken cancellationToken = default)
        {
            var normalizedSeller = sellerId?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedSeller))
            {
                return EmptyResult();
            }

            var normalizedGranularity = SellerSalesGranularities.Normalize(granularity);
            var (rangeStart, rangeEnd) = NormalizeRange(from, to);

            var activeProduct = await ResolveProductAsync(normalizedSeller, productId, cancellationToken);
            var activeCategory = await ResolveCategoryAsync(normalizedSeller, categoryId, cancellationToken);

            var productOptions = await LoadProductOptionsAsync(normalizedSeller, cancellationToken);
            var categoryOptions = await LoadCategoryOptionsAsync(normalizedSeller, cancellationToken);

            var sellerToken = $"\"sellerId\":\"{normalizedSeller}\"";
            var orders = await _applicationDbContext.Orders.AsNoTracking()
                .Where(o => o.CreatedOn >= rangeStart && o.CreatedOn <= rangeEnd)
                .Where(o => !string.Equals(o.Status, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(o.Status, OrderStatuses.Failed, StringComparison.OrdinalIgnoreCase))
                .Where(o => o.DetailsJson.Contains(sellerToken))
                .OrderBy(o => o.CreatedOn)
                .ToListAsync(cancellationToken);

            var buckets = BuildEmptyBuckets(rangeStart, rangeEnd, normalizedGranularity);

            foreach (var order in orders)
            {
                var details = DeserializeDetails(order.DetailsJson);
                var sellerSubOrders = details.SubOrders
                    .Where(s => string.Equals(s.SellerId, normalizedSeller, StringComparison.OrdinalIgnoreCase));

                foreach (var subOrder in sellerSubOrders)
                {
                    var (gmv, ordersCount) = CalculateContribution(subOrder, activeProduct?.Id, activeCategory?.FullPath);
                    if (gmv <= 0 && ordersCount == 0)
                    {
                        continue;
                    }

                    var bucketKey = TruncateToBucket(order.CreatedOn, normalizedGranularity);
                    if (!buckets.TryGetValue(bucketKey, out var current))
                    {
                        buckets[bucketKey] = (gmv, ordersCount);
                    }
                    else
                    {
                        buckets[bucketKey] = (current.Gmv + gmv, current.Orders + ordersCount);
                    }
                }
            }

            var series = buckets
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => new SellerSalesPoint(
                    kvp.Key,
                    FormatLabel(kvp.Key, normalizedGranularity),
                    Math.Round(kvp.Value.Gmv, 2),
                    kvp.Value.Orders))
                .ToList();

            var totalGmv = series.Sum(p => p.Gmv);
            var totalOrders = series.Sum(p => p.Orders);

            return new SellerSalesDashboardResult(
                totalGmv,
                totalOrders,
                series,
                productOptions,
                categoryOptions,
                activeProduct?.Id,
                activeCategory?.Id);
        }

        private static SellerSalesDashboardResult EmptyResult() =>
            new(0, 0, Array.Empty<SellerSalesPoint>(), Array.Empty<SellerProductOption>(), Array.Empty<SellerCategoryOption>(), null, null);

        private (decimal Gmv, int Orders) CalculateContribution(OrderSubOrder subOrder, int? productId, string? categoryPath)
        {
            var normalizedStatus = OrderStatuses.Normalize(subOrder.Status);
            if (string.Equals(normalizedStatus, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedStatus, OrderStatuses.Failed, StringComparison.OrdinalIgnoreCase))
            {
                return (0, 0);
            }

            var items = subOrder.Items
                .Where(i => string.Equals(i.SellerId, subOrder.SellerId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (productId.HasValue)
            {
                items = items.Where(i => i.ProductId == productId.Value).ToList();
            }

            if (!string.IsNullOrWhiteSpace(categoryPath))
            {
                items = items
                    .Where(i => string.Equals(i.Category, categoryPath, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (items.Count == 0)
            {
                return (0, 0);
            }

            var subtotal = items.Sum(i => i.LineTotal);
            var ratio = subOrder.ItemsSubtotal <= 0
                ? 1m
                : Math.Clamp(subtotal / subOrder.ItemsSubtotal, 0, 1);
            var gmv = Math.Max(0, subOrder.GrandTotal * ratio);

            return (gmv, 1);
        }

        private async Task<SellerProductOption?> ResolveProductAsync(string sellerId, int? productId, CancellationToken cancellationToken)
        {
            if (!productId.HasValue)
            {
                return null;
            }

            var product = await _productDbContext.Products.AsNoTracking()
                .Where(p => p.Id == productId.Value && p.SellerId == sellerId)
                .Select(p => new SellerProductOption(p.Id, p.Title))
                .FirstOrDefaultAsync(cancellationToken);

            return product;
        }

        private async Task<SellerCategoryOption?> ResolveCategoryAsync(string sellerId, int? categoryId, CancellationToken cancellationToken)
        {
            if (!categoryId.HasValue)
            {
                return null;
            }

            var category = await _productDbContext.Categories.AsNoTracking()
                .Where(c => c.Id == categoryId.Value)
                .Select(c => new SellerCategoryOption(c.Id, c.Name, c.FullPath))
                .FirstOrDefaultAsync(cancellationToken);
            if (category == null)
            {
                return null;
            }

            var sellerHasCategory = await _productDbContext.Products.AsNoTracking()
                .AnyAsync(p => p.SellerId == sellerId && p.CategoryId == category.Id, cancellationToken);

            return sellerHasCategory ? category : null;
        }

        private async Task<IReadOnlyList<SellerProductOption>> LoadProductOptionsAsync(string sellerId, CancellationToken cancellationToken)
        {
            var options = await _productDbContext.Products.AsNoTracking()
                .Where(p => p.SellerId == sellerId && p.WorkflowState == ProductWorkflowStates.Active)
                .OrderBy(p => p.Title)
                .Take(100)
                .Select(p => new SellerProductOption(p.Id, p.Title))
                .ToListAsync(cancellationToken);

            return options;
        }

        private async Task<IReadOnlyList<SellerCategoryOption>> LoadCategoryOptionsAsync(string sellerId, CancellationToken cancellationToken)
        {
            var categories = await _productDbContext.Products.AsNoTracking()
                .Where(p => p.SellerId == sellerId && p.CategoryId != null)
                .Join(
                    _productDbContext.Categories.AsNoTracking(),
                    p => p.CategoryId,
                    c => c.Id,
                    (p, c) => new SellerCategoryOption(c.Id, c.Name, c.FullPath))
                .ToListAsync(cancellationToken);

            return categories
                .GroupBy(c => c.Id)
                .Select(g => g.First())
                .OrderBy(c => c.FullPath, StringComparer.OrdinalIgnoreCase)
                .Take(100)
                .ToList();
        }

        private static (DateTimeOffset From, DateTimeOffset To) NormalizeRange(DateTimeOffset from, DateTimeOffset to)
        {
            if (from > to)
            {
                return (to, from);
            }

            return (from, to);
        }

        private static SortedDictionary<DateTimeOffset, (decimal Gmv, int Orders)> BuildEmptyBuckets(
            DateTimeOffset rangeStart,
            DateTimeOffset rangeEnd,
            string granularity)
        {
            var buckets = new SortedDictionary<DateTimeOffset, (decimal, int)>();
            var cursor = TruncateToBucket(rangeStart, granularity);
            var target = TruncateToBucket(rangeEnd, granularity);

            while (cursor <= target)
            {
                buckets[cursor] = (0, 0);
                cursor = NextBucket(cursor, granularity);
            }

            return buckets;
        }

        private static DateTimeOffset TruncateToBucket(DateTimeOffset timestamp, string granularity)
        {
            var utc = timestamp.ToUniversalTime();
            return granularity switch
            {
                SellerSalesGranularities.Week => StartOfWeek(utc),
                SellerSalesGranularities.Month => new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero),
                _ => new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero)
            };
        }

        private static DateTimeOffset NextBucket(DateTimeOffset timestamp, string granularity)
        {
            return granularity switch
            {
                SellerSalesGranularities.Week => timestamp.AddDays(7),
                SellerSalesGranularities.Month => timestamp.AddMonths(1),
                _ => timestamp.AddDays(1)
            };
        }

        private static DateTimeOffset StartOfWeek(DateTimeOffset timestamp)
        {
            var diff = (7 + (timestamp.DayOfWeek - DayOfWeek.Monday)) % 7;
            var start = timestamp.Date.AddDays(-diff);
            return new DateTimeOffset(start, TimeSpan.Zero);
        }

        private static string FormatLabel(DateTimeOffset periodStart, string granularity)
        {
            return granularity switch
            {
                SellerSalesGranularities.Week => $"{periodStart:MMM dd} - {periodStart.AddDays(6):MMM dd}",
                SellerSalesGranularities.Month => periodStart.ToString("MMM yyyy"),
                _ => periodStart.ToString("MMM dd")
            };
        }

        private OrderDetailsPayload DeserializeDetails(string? payload)
        {
            try
            {
                var details = string.IsNullOrWhiteSpace(payload)
                    ? null
                    : JsonSerializer.Deserialize<OrderDetailsPayload>(payload, _serializerOptions);
                return NormalizeDetails(details);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize order details payload for seller reporting.");
                return NormalizeDetails(null);
            }
        }

        private static OrderDetailsPayload NormalizeDetails(OrderDetailsPayload? details)
        {
            var fallback = new OrderDetailsPayload(
                new List<OrderItemDetail>(),
                new List<OrderShippingDetail>(),
                0,
                0,
                null,
                new List<OrderSubOrder>(),
                new List<EscrowAllocation>(),
                Messages: new List<OrderMessage>());

            if (details == null)
            {
                return fallback;
            }

            return new OrderDetailsPayload(
                details.Items ?? fallback.Items,
                details.Shipping ?? fallback.Shipping,
                details.TotalQuantity,
                details.DiscountTotal,
                details.PromoCode,
                details.SubOrders ?? fallback.SubOrders,
                details.Escrow ?? fallback.Escrow,
                details.PaymentStatus,
                details.PaymentStatusMessage,
                details.PaymentRefundedAmount,
                details.Messages ?? fallback.Messages);
        }
    }
}
