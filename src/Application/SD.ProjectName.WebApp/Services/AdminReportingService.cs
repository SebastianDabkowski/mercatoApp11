using System.Globalization;
using System.Text;
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

    public record AdminOrderReportFilterOptions
    {
        public DateTimeOffset? FromDate { get; init; }

        public DateTimeOffset? ToDate { get; init; }

        public string? SellerId { get; init; }

        public List<string> Statuses { get; init; } = new();

        public List<string> PaymentStatuses { get; init; } = new();
    }

    public record AdminOrderReportRow(
        string OrderNumber,
        string SubOrderNumber,
        DateTimeOffset CreatedOn,
        string Buyer,
        string BuyerEmail,
        string SellerId,
        string SellerName,
        string Status,
        string PaymentStatus,
        decimal OrderValue,
        decimal Commission,
        decimal Payout);

    public record AdminOrderReportResult(
        IReadOnlyList<AdminOrderReportRow> Rows,
        decimal TotalOrderValue,
        decimal TotalCommission,
        decimal TotalPayout,
        int TotalCount,
        int PageNumber,
        int TotalPages);

    public record AdminOrderExportResult(byte[] Content, int RowCount, int TotalMatching, bool Truncated);

    public class AdminReportingService
    {
        private readonly ApplicationDbContext _applicationDbContext;
        private readonly ProductDbContext _productDbContext;
        private readonly AdminReportOptions _reportOptions;
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
            AdminReportOptions reportOptions,
            ILogger<AdminReportingService> logger)
        {
            _applicationDbContext = applicationDbContext;
            _productDbContext = productDbContext;
            _reportOptions = reportOptions;
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

        public async Task<AdminOrderReportResult> GetOrderReportAsync(
            AdminOrderReportFilterOptions? filters,
            int pageNumber = 1,
            int? pageSize = null,
            CancellationToken cancellationToken = default)
        {
            var normalized = NormalizeFilters(filters);
            var size = ResolvePageSize(pageSize);
            pageNumber = Math.Max(1, pageNumber);

            var orders = await LoadOrdersForReportAsync(normalized, cancellationToken);
            var rows = BuildOrderRows(orders, normalized);

            var totalCount = rows.Count;
            var totalPages = size <= 0 ? 0 : (int)Math.Ceiling(totalCount / (double)size);
            if (totalPages == 0)
            {
                pageNumber = 1;
            }
            else if (pageNumber > totalPages)
            {
                pageNumber = totalPages;
            }

            var pagedRows = rows
                .OrderByDescending(r => r.CreatedOn)
                .Skip((pageNumber - 1) * size)
                .Take(size)
                .ToList();

            return new AdminOrderReportResult(
                pagedRows,
                RoundAmount(rows.Sum(r => r.OrderValue)),
                RoundAmount(rows.Sum(r => r.Commission)),
                RoundAmount(rows.Sum(r => r.Payout)),
                totalCount,
                pageNumber,
                totalPages);
        }

        public async Task<AdminOrderExportResult?> ExportOrdersAsync(
            AdminOrderReportFilterOptions? filters,
            CancellationToken cancellationToken = default)
        {
            var normalized = NormalizeFilters(filters);
            var orders = await LoadOrdersForReportAsync(normalized, cancellationToken);
            var rows = BuildOrderRows(orders, normalized);
            if (rows.Count == 0)
            {
                return null;
            }

            var limit = ResolveExportLimit();
            var ordered = rows
                .OrderByDescending(r => r.CreatedOn)
                .Take(limit)
                .ToList();
            var truncated = ordered.Count < rows.Count;

            var builder = new StringBuilder();
            builder.AppendLine("OrderNumber,SubOrderNumber,CreatedOn,Buyer,BuyerEmail,SellerId,SellerName,Status,PaymentStatus,OrderValue,Commission,PayoutAmount");
            foreach (var row in ordered)
            {
                builder.AppendLine(string.Join(",", new[]
                {
                    CsvEscape(row.OrderNumber),
                    CsvEscape(row.SubOrderNumber),
                    CsvEscape(row.CreatedOn.ToString("u", CultureInfo.InvariantCulture)),
                    CsvEscape(row.Buyer),
                    CsvEscape(row.BuyerEmail),
                    CsvEscape(row.SellerId),
                    CsvEscape(row.SellerName),
                    CsvEscape(row.Status),
                    CsvEscape(row.PaymentStatus),
                    CsvEscape(row.OrderValue.ToString("F2", CultureInfo.InvariantCulture)),
                    CsvEscape(row.Commission.ToString("F2", CultureInfo.InvariantCulture)),
                    CsvEscape(row.Payout.ToString("F2", CultureInfo.InvariantCulture))
                }));
            }

            return new AdminOrderExportResult(Encoding.UTF8.GetBytes(builder.ToString()), ordered.Count, rows.Count, truncated);
        }

        private async Task<List<OrderRecord>> LoadOrdersForReportAsync(
            AdminOrderReportFilterOptions filters,
            CancellationToken cancellationToken)
        {
            var query = _applicationDbContext.Orders.AsNoTracking();

            if (filters.FromDate.HasValue)
            {
                query = query.Where(o => o.CreatedOn >= filters.FromDate.Value);
            }

            if (filters.ToDate.HasValue)
            {
                query = query.Where(o => o.CreatedOn <= filters.ToDate.Value);
            }

            if (!string.IsNullOrWhiteSpace(filters.SellerId))
            {
                var sellerToken = $"\"sellerId\":\"{filters.SellerId}\"";
                query = query.Where(o => o.DetailsJson.Contains(sellerToken));
            }

            return await query
                .OrderByDescending(o => o.CreatedOn)
                .ToListAsync(cancellationToken);
        }

        private List<AdminOrderReportRow> BuildOrderRows(
            List<OrderRecord> orders,
            AdminOrderReportFilterOptions filters)
        {
            var rows = new List<AdminOrderReportRow>();

            foreach (var order in orders)
            {
                var details = DeserializeDetails(order.DetailsJson);
                var paymentStatus = PaymentStatuses.Normalize(details.PaymentStatus);
                if (filters.PaymentStatuses.Count > 0
                    && !filters.PaymentStatuses.Contains(paymentStatus, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var allocations = details.Escrow ?? new List<EscrowAllocation>();
                var subOrders = details.SubOrders ?? new List<OrderSubOrder>();

                foreach (var subOrder in subOrders)
                {
                    if (!string.IsNullOrWhiteSpace(filters.SellerId)
                        && !string.Equals(subOrder.SellerId, filters.SellerId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var normalizedStatus = OrderStatuses.Normalize(string.IsNullOrWhiteSpace(subOrder.Status) ? order.Status : subOrder.Status);
                    if (filters.Statuses.Count > 0
                        && !filters.Statuses.Contains(normalizedStatus, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var allocation = ResolveAllocationForSubOrder(allocations, subOrder);
                    var orderValue = allocation?.HeldAmount ?? subOrder.GrandTotal;
                    var commission = allocation?.CommissionAmount ?? 0;
                    var payout = allocation?.ReleasedToSeller > 0
                        ? allocation.ReleasedToSeller
                        : allocation?.SellerPayoutAmount ?? Math.Max(0, orderValue - commission);

                    var buyerName = string.IsNullOrWhiteSpace(order.BuyerName)
                        ? order.BuyerEmail ?? string.Empty
                        : order.BuyerName;
                    var sellerName = string.IsNullOrWhiteSpace(subOrder.SellerName) ? subOrder.SellerId : subOrder.SellerName;

                    rows.Add(new AdminOrderReportRow(
                        order.OrderNumber,
                        subOrder.SubOrderNumber,
                        order.CreatedOn,
                        buyerName,
                        order.BuyerEmail ?? string.Empty,
                        subOrder.SellerId,
                        sellerName,
                        normalizedStatus,
                        paymentStatus,
                        RoundAmount(orderValue),
                        RoundAmount(commission),
                        RoundAmount(payout)));
                }
            }

            return rows;
        }

        private AdminOrderReportFilterOptions NormalizeFilters(AdminOrderReportFilterOptions? filters)
        {
            var normalized = filters ?? new AdminOrderReportFilterOptions();
            var from = normalized.FromDate;
            var to = normalized.ToDate;

            if (!from.HasValue && !to.HasValue)
            {
                var now = DateTime.UtcNow;
                from = new DateTimeOffset(now.Date.AddDays(-30), TimeSpan.Zero);
                to = new DateTimeOffset(now.Date.AddDays(1).AddTicks(-1), TimeSpan.Zero);
            }
            else
            {
                if (from.HasValue)
                {
                    from = EnsureUtcStartOfDay(from.Value);
                }

                if (to.HasValue)
                {
                    to = EnsureUtcEndOfDay(to.Value);
                }
            }

            if (from.HasValue && to.HasValue && from > to)
            {
                (from, to) = (to, from);
            }

            return new AdminOrderReportFilterOptions
            {
                FromDate = from,
                ToDate = to,
                SellerId = string.IsNullOrWhiteSpace(normalized.SellerId) ? null : normalized.SellerId.Trim(),
                Statuses = normalized.Statuses
                    .Select(OrderStatuses.Normalize)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                PaymentStatuses = normalized.PaymentStatuses
                    .Select(PaymentStatuses.Normalize)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }

        private int ResolvePageSize(int? requested)
        {
            var fallback = _reportOptions.PreviewPageSize <= 0 ? 50 : _reportOptions.PreviewPageSize;
            var size = requested ?? fallback;
            return Math.Clamp(size, 10, 200);
        }

        private int ResolveExportLimit()
        {
            var limit = _reportOptions.ExportRowLimit <= 0 ? 20000 : _reportOptions.ExportRowLimit;
            return Math.Max(1, limit);
        }

        private static decimal RoundAmount(decimal amount) =>
            Math.Round(amount, 2, MidpointRounding.AwayFromZero);

        private static EscrowAllocation? ResolveAllocationForSubOrder(List<EscrowAllocation> allocations, OrderSubOrder subOrder)
        {
            return allocations.FirstOrDefault(e =>
                string.Equals(e.SubOrderNumber, subOrder.SubOrderNumber, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.SellerId, subOrder.SellerId, StringComparison.OrdinalIgnoreCase));
        }

        private static DateTimeOffset EnsureUtcStartOfDay(DateTimeOffset value)
        {
            var start = DateTime.SpecifyKind(value.UtcDateTime.Date, DateTimeKind.Utc);
            return new DateTimeOffset(start);
        }

        private static DateTimeOffset EnsureUtcEndOfDay(DateTimeOffset value)
        {
            var end = DateTime.SpecifyKind(value.UtcDateTime.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            return new DateTimeOffset(end);
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

        private static string CsvEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var needsQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
            if (value.Contains('"'))
            {
                value = value.Replace("\"", "\"\"");
            }

            return needsQuotes ? $"\"{value}\"" : value;
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
