using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Infrastructure;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Services
{
    public static class DashboardMetricKeys
    {
        public const string Gmv = "gmv";
        public const string Orders = "orders";
        public const string Sellers = "sellers";
        public const string Products = "products";
        public const string Users = "users";

        public static bool IsValid(string? metric) =>
            metric is Gmv or Orders or Sellers or Products or Users;
    }

    public record DashboardKpiSummary(
        decimal TotalGmv,
        int Orders,
        int ActiveSellers,
        int ActiveProducts,
        int NewUsers,
        DateTimeOffset RefreshedOn);

    public record DashboardDetailItem(
        string Title,
        string? Subtitle = null,
        string? Status = null,
        decimal? Amount = null,
        DateTimeOffset? OccurredOn = null);

    public record DashboardMetricsResult(DashboardKpiSummary Summary, IReadOnlyList<DashboardDetailItem> Details);

    public class AdminReportingService
    {
        private readonly ApplicationDbContext _applicationDbContext;
        private readonly ProductDbContext _productDbContext;
        private readonly ILogger<AdminReportingService> _logger;
        private readonly JsonSerializerOptions _serializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private record ActiveProductEntry(int Id, string Title, string MerchantSku, string SellerId, decimal Price);

        private record NewUserEntry(string Id, string FullName, string Email, string AccountType, DateTimeOffset? TermsAcceptedOn);

        private record SellerProfileEntry(string Id, string FullName, string? BusinessName);

        public AdminReportingService(
            ApplicationDbContext applicationDbContext,
            ProductDbContext productDbContext,
            ILogger<AdminReportingService> logger)
        {
            _applicationDbContext = applicationDbContext;
            _productDbContext = productDbContext;
            _logger = logger;
        }

        public async Task<DashboardMetricsResult> GetDashboardMetricsAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            string? detailMetric,
            CancellationToken cancellationToken = default)
        {
            var normalizedFrom = from <= to ? from : to;
            var normalizedTo = to >= from ? to : from;

            var orders = await _applicationDbContext.Orders.AsNoTracking()
                .Where(o => o.CreatedOn >= normalizedFrom && o.CreatedOn <= normalizedTo)
                .Where(o => !string.Equals(o.Status, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(o.Status, OrderStatuses.Failed, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(o => o.CreatedOn)
                .ToListAsync(cancellationToken);

            var totalGmv = orders.Sum(o => o.GrandTotal);
            var sellerIdsFromOrders = ExtractSellerIds(orders);

            var activeProductQuery = _productDbContext.Products.AsNoTracking()
                .Where(p => p.WorkflowState == ProductWorkflowStates.Active);
            var activeProductsCount = await activeProductQuery.CountAsync(cancellationToken);
            var sellerIdsFromProducts = await activeProductQuery
                .Select(p => p.SellerId)
                .Distinct()
                .ToListAsync(cancellationToken);
            var activeProducts = detailMetric == DashboardMetricKeys.Products
                ? await activeProductQuery
                    .OrderBy(p => p.Title)
                    .Take(100)
                    .Select(p => new ActiveProductEntry(p.Id, p.Title, p.MerchantSku, p.SellerId, p.Price))
                    .ToListAsync(cancellationToken)
                : new List<ActiveProductEntry>();

            var activeSellers = new HashSet<string>(sellerIdsFromProducts, StringComparer.OrdinalIgnoreCase);
            foreach (var sellerId in sellerIdsFromOrders)
            {
                activeSellers.Add(sellerId);
            }

            var newUsersQuery = _applicationDbContext.Users.AsNoTracking()
                .Where(u => u.TermsAcceptedOn.HasValue &&
                            u.TermsAcceptedOn.Value >= normalizedFrom &&
                            u.TermsAcceptedOn.Value <= normalizedTo);
            var newUsersCount = await newUsersQuery.CountAsync(cancellationToken);
            var newUsers = detailMetric == DashboardMetricKeys.Users
                ? await newUsersQuery
                    .OrderByDescending(u => u.TermsAcceptedOn)
                    .Take(100)
                    .Select(u => new NewUserEntry(u.Id, u.FullName, u.Email ?? string.Empty, u.AccountType, u.TermsAcceptedOn))
                    .ToListAsync(cancellationToken)
                : new List<NewUserEntry>();

            var summary = new DashboardKpiSummary(
                totalGmv,
                orders.Count,
                activeSellers.Count,
                activeProductsCount,
                newUsersCount,
                DateTimeOffset.UtcNow);

            var detailItems = await BuildDetailsAsync(
                detailMetric,
                orders,
                activeProducts,
                activeSellers,
                sellerIdsFromOrders,
                newUsers,
                cancellationToken);

            return new DashboardMetricsResult(summary, detailItems);
        }

        private async Task<IReadOnlyList<DashboardDetailItem>> BuildDetailsAsync(
            string? detailMetric,
            List<OrderRecord> orders,
            List<ActiveProductEntry> activeProducts,
            HashSet<string> activeSellers,
            HashSet<string> sellerIdsFromOrders,
            List<NewUserEntry> newUsers,
            CancellationToken cancellationToken)
        {
            if (!DashboardMetricKeys.IsValid(detailMetric))
            {
                return Array.Empty<DashboardDetailItem>();
            }

            if (detailMetric == DashboardMetricKeys.Gmv || detailMetric == DashboardMetricKeys.Orders)
            {
                return orders
                    .Select(o => new DashboardDetailItem(
                        o.OrderNumber,
                        o.Status,
                        o.PaymentMethodLabel,
                        o.GrandTotal,
                        o.CreatedOn))
                    .ToList();
            }

            if (detailMetric == DashboardMetricKeys.Products)
            {
                return activeProducts
                    .Select(p => new DashboardDetailItem(
                        p.Title,
                        $"SKU {p.MerchantSku}",
                        "Active",
                        p.Price,
                        null))
                    .ToList();
            }

            if (detailMetric == DashboardMetricKeys.Users)
            {
                return newUsers
                    .Select(u => new DashboardDetailItem(
                        u.FullName,
                        u.Email,
                        u.AccountType,
                        null,
                        u.TermsAcceptedOn))
                    .ToList();
            }

            if (detailMetric == DashboardMetricKeys.Sellers)
            {
                var sellerIds = activeSellers.ToList();
                var sellerProfiles = await _applicationDbContext.Users.AsNoTracking()
                    .Where(u => sellerIds.Contains(u.Id))
                    .Select(u => new SellerProfileEntry(u.Id, u.FullName, u.BusinessName))
                    .ToListAsync(cancellationToken);
                var sellerNames = sellerProfiles.ToDictionary(
                    s => s.Id,
                    s => string.IsNullOrWhiteSpace(s.BusinessName) ? s.FullName : s.BusinessName,
                    StringComparer.OrdinalIgnoreCase);

                var items = new List<DashboardDetailItem>();
                foreach (var sellerId in sellerIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
                {
                    var reason = sellerIdsFromOrders.Contains(sellerId)
                        ? "Order activity"
                        : "Active listings";
                    var displayName = sellerNames.TryGetValue(sellerId, out var name) ? name : sellerId;
                    items.Add(new DashboardDetailItem(displayName, sellerId, reason));
                }

                return items;
            }

            return Array.Empty<DashboardDetailItem>();
        }

        private HashSet<string> ExtractSellerIds(List<OrderRecord> orders)
        {
            var sellerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var order in orders)
            {
                var details = DeserializeDetails(order.DetailsJson);
                foreach (var sub in details.SubOrders ?? Enumerable.Empty<OrderSubOrder>())
                {
                    if (!string.IsNullOrWhiteSpace(sub.SellerId))
                    {
                        sellerIds.Add(sub.SellerId);
                    }
                }
            }

            return sellerIds;
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
                _logger.LogWarning(ex, "Failed to deserialize order details payload for reporting.");
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
