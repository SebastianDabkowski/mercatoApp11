using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Services
{
    public static class OrderStatuses
    {
        public const string New = "New";
        public const string Paid = "Paid";
        public const string Preparing = "Preparing";
        public const string Shipped = "Shipped";
        public const string Delivered = "Delivered";
        public const string Cancelled = "Cancelled";
        public const string Refunded = "Refunded";

        private static readonly Dictionary<string, HashSet<string>> AllowedTransitions = new(StringComparer.OrdinalIgnoreCase)
        {
            [New] = new HashSet<string>(new[] { Paid, Cancelled }, StringComparer.OrdinalIgnoreCase),
            [Paid] = new HashSet<string>(new[] { Preparing, Shipped, Delivered, Cancelled, Refunded }, StringComparer.OrdinalIgnoreCase),
            [Preparing] = new HashSet<string>(new[] { Shipped, Cancelled, Refunded }, StringComparer.OrdinalIgnoreCase),
            [Shipped] = new HashSet<string>(new[] { Delivered, Refunded }, StringComparer.OrdinalIgnoreCase),
            [Delivered] = new HashSet<string>(new[] { Refunded }, StringComparer.OrdinalIgnoreCase),
            [Cancelled] = new HashSet<string>(new[] { Refunded }, StringComparer.OrdinalIgnoreCase),
            [Refunded] = new HashSet<string>(Array.Empty<string>(), StringComparer.OrdinalIgnoreCase)
        };

        private static readonly IReadOnlyList<string> OrderedStatuses = new[]
        {
            New, Paid, Preparing, Shipped, Delivered, Cancelled, Refunded
        };

        public static IReadOnlyList<string> All => OrderedStatuses;

        public static string Normalize(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return Paid;
            }

            if (status.Trim().Equals("Confirmed", StringComparison.OrdinalIgnoreCase))
            {
                return Paid;
            }

            var match = OrderedStatuses.FirstOrDefault(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));
            return match ?? status.Trim();
        }

        public static bool CanTransition(string current, string target)
        {
            var normalizedCurrent = Normalize(current);
            var normalizedTarget = Normalize(target);

            if (string.Equals(normalizedCurrent, normalizedTarget, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return AllowedTransitions.TryGetValue(normalizedCurrent, out var allowed)
                && allowed.Contains(normalizedTarget);
        }

        public static List<string> NextStatuses(string current)
        {
            var normalized = Normalize(current);
            return AllowedTransitions.TryGetValue(normalized, out var allowed)
                ? allowed.OrderBy(IndexOf).ToList()
                : new List<string>();
        }

        private static int IndexOf(string status)
        {
            for (var i = 0; i < OrderedStatuses.Count; i++)
            {
                if (string.Equals(OrderedStatuses[i], status, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return int.MaxValue;
        }
    }

    public static class ReturnRequestStatuses
    {
        public const string Requested = "Requested";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Completed = "Completed";

        private static readonly IReadOnlyList<string> OrderedStatuses = new[]
        {
            Requested, Approved, Rejected, Completed
        };

        public static string Normalize(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return Requested;
            }

            var match = OrderedStatuses.FirstOrDefault(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));
            return match ?? status.Trim();
        }
    }

    public static class ReturnPolicies
    {
        public const int ReturnWindowDays = 14;
        public static readonly TimeSpan ReturnWindow = TimeSpan.FromDays(ReturnWindowDays);
    }

    public class OrderRecord
    {
        public int Id { get; set; }

        public string OrderNumber { get; set; } = string.Empty;

        public string Status { get; set; } = OrderStatuses.New;

        public string? BuyerId { get; set; }

        public string BuyerEmail { get; set; } = string.Empty;

        public string BuyerName { get; set; } = string.Empty;

        public string PaymentMethodId { get; set; } = string.Empty;

        public string PaymentMethodLabel { get; set; } = string.Empty;

        public string? PaymentReference { get; set; }

        public string? CartSignature { get; set; }

        public decimal ItemsSubtotal { get; set; }

        public decimal ShippingTotal { get; set; }

        public decimal GrandTotal { get; set; }

        public int TotalQuantity { get; set; }

        public DateTimeOffset CreatedOn { get; set; }

        public string DeliveryAddressJson { get; set; } = string.Empty;

        public string DetailsJson { get; set; } = string.Empty;
    }

    public record OrderItemDetail(int ProductId, string Name, string Variant, int Quantity, decimal UnitPrice, decimal LineTotal, string SellerId, string SellerName, string Status = OrderStatuses.Paid);

    public record OrderShippingDetail(string SellerId, string SellerName, string MethodId, string MethodLabel, decimal Cost, string? Description);

    public record ReturnRequestItem(int ProductId, int Quantity);

    public record ReturnRequest(string SubOrderNumber, string Status, string Reason, DateTimeOffset RequestedOn, List<ReturnRequestItem> Items);

    public record OrderSubOrder(
        string SubOrderNumber,
        string SellerId,
        string SellerName,
        decimal ItemsSubtotal,
        decimal Shipping,
        decimal DiscountTotal,
        decimal GrandTotal,
        int TotalQuantity,
        List<OrderItemDetail> Items,
        OrderShippingDetail ShippingDetail,
        string Status,
        string? TrackingNumber = null,
        string? TrackingCarrier = null,
        decimal RefundedAmount = 0,
        DateTimeOffset? DeliveredOn = null,
        ReturnRequest? Return = null);

    public record OrderDetailsPayload(List<OrderItemDetail> Items, List<OrderShippingDetail> Shipping, int TotalQuantity, decimal DiscountTotal = 0, string? PromoCode = null, List<OrderSubOrder> SubOrders = null!);

    public record OrderView(
        int Id,
        string OrderNumber,
        string Status,
        DateTimeOffset CreatedOn,
        string PaymentMethodLabel,
        string? PaymentReference,
        decimal ItemsSubtotal,
        decimal ShippingTotal,
        decimal GrandTotal,
        decimal DiscountTotal,
        string? PromoCode,
        int TotalQuantity,
        DeliveryAddress Address,
        List<OrderItemDetail> Items,
        List<OrderShippingDetail> Shipping,
        List<OrderSubOrder> SubOrders);

    public record OrderSummaryView(int Id, string OrderNumber, DateTimeOffset CreatedOn, string Status, decimal GrandTotal, int TotalQuantity);

    public record SellerOrderSummaryView(
        int Id,
        string OrderNumber,
        string SubOrderNumber,
        DateTimeOffset CreatedOn,
        string Status,
        decimal GrandTotal,
        int TotalQuantity,
        string SellerName,
        string BuyerName,
        string BuyerEmail,
        string ShippingMethod);

    public record SellerOrderView(
        int Id,
        string OrderNumber,
        string SubOrderNumber,
        string Status,
        string? TrackingNumber,
        string? TrackingCarrier,
        decimal RefundedAmount,
        DateTimeOffset CreatedOn,
        string PaymentMethodLabel,
        string? PaymentReference,
        decimal ItemsSubtotal,
        decimal ShippingTotal,
        decimal DiscountTotal,
        decimal GrandTotal,
        int TotalQuantity,
        DeliveryAddress Address,
        List<OrderItemDetail> Items,
        OrderShippingDetail Shipping,
        string BuyerName,
        string BuyerEmail,
        string? BuyerPhone,
        string PaymentStatus,
        ReturnRequest? ReturnRequest);

    public record OrderCreationResult(OrderRecord Order, bool Created);

    public record SubOrderStatusUpdateResult(bool Success, string? Error, OrderSubOrder? UpdatedSubOrder = null, string? OrderStatus = null);

    public record ReturnRequestResult(bool Success, string? Error, ReturnRequest? Request = null);

    public record BuyerOrderFilterOptions
    {
        public List<string> Statuses { get; init; } = new();

        public DateTimeOffset? FromDate { get; init; }

        public DateTimeOffset? ToDate { get; init; }

        public string? SellerId { get; init; }
    }

    public record SellerOrderFilterOptions
    {
        public List<string> Statuses { get; init; } = new();

        public DateTimeOffset? FromDate { get; init; }

        public DateTimeOffset? ToDate { get; init; }

        public string? BuyerQuery { get; init; }
    }

    public record SellerFilterOption(string Id, string Name);

    public class OrderService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<OrderService> _logger;
        private readonly JsonSerializerOptions _serializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public OrderService(ApplicationDbContext dbContext, IEmailSender emailSender, ILogger<OrderService> logger)
        {
            _dbContext = dbContext;
            _emailSender = emailSender;
            _logger = logger;
        }

        public async Task<OrderCreationResult> EnsureOrderAsync(
            CheckoutState state,
            ShippingQuote quote,
            DeliveryAddress address,
            string? buyerId,
            string? buyerEmail,
            string? buyerName,
            string? paymentMethodLabel,
            string? paymentMethodId,
            CancellationToken cancellationToken = default)
        {
            var normalizedReference = string.IsNullOrWhiteSpace(state.PaymentReference) ? null : state.PaymentReference.Trim();
            if (!string.IsNullOrEmpty(normalizedReference))
            {
                var existingByReference = await _dbContext.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.PaymentReference == normalizedReference, cancellationToken);
                if (existingByReference != null)
                {
                    return new OrderCreationResult(existingByReference, false);
                }
            }

            var normalizedSignature = string.IsNullOrWhiteSpace(state.CartSignature) ? null : state.CartSignature.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedSignature) && !string.IsNullOrWhiteSpace(buyerId))
            {
                var existingBySignature = await _dbContext.Orders.AsNoTracking().FirstOrDefaultAsync(
                    o => o.BuyerId == buyerId && o.CartSignature == normalizedSignature,
                    cancellationToken);
                if (existingBySignature != null)
                {
                    return new OrderCreationResult(existingBySignature, false);
                }
            }

            var orderNumber = GenerateOrderNumber();
            var details = BuildDetailsPayload(orderNumber, quote);
            var order = new OrderRecord
            {
                OrderNumber = orderNumber,
                Status = CalculateOrderStatus(details.SubOrders, OrderStatuses.Paid),
                BuyerId = buyerId,
                BuyerEmail = buyerEmail ?? string.Empty,
                BuyerName = buyerName ?? string.Empty,
                PaymentMethodId = paymentMethodId ?? string.Empty,
                PaymentMethodLabel = paymentMethodLabel ?? paymentMethodId ?? string.Empty,
                PaymentReference = normalizedReference,
                CartSignature = normalizedSignature,
                ItemsSubtotal = quote.Summary.ItemsSubtotal,
                ShippingTotal = quote.Summary.ShippingTotal,
                GrandTotal = quote.Summary.GrandTotal,
                TotalQuantity = quote.Summary.TotalQuantity,
                CreatedOn = DateTimeOffset.UtcNow,
                DeliveryAddressJson = JsonSerializer.Serialize(address, _serializerOptions),
                DetailsJson = JsonSerializer.Serialize(details, _serializerOptions)
            };

            _dbContext.Orders.Add(order);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await SendConfirmationEmailAsync(order, address, details, cancellationToken);

            return new OrderCreationResult(order, true);
        }

        public async Task<OrderView?> GetOrderAsync(int id, string? currentUserId, CancellationToken cancellationToken = default)
        {
            var order = await _dbContext.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
            if (order == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(order.BuyerId) && !string.Equals(order.BuyerId, currentUserId, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var address = DeserializeAddress(order.DeliveryAddressJson);
            var details = DeserializeDetails(order.DetailsJson);
            var orderStatus = CalculateOrderStatus(details.SubOrders, order.Status);
            var discountTotal = details.DiscountTotal > 0
                ? details.DiscountTotal
                : Math.Max(0, order.ItemsSubtotal + order.ShippingTotal - order.GrandTotal);
            var promoCode = string.IsNullOrWhiteSpace(details.PromoCode) ? null : details.PromoCode;

            return new OrderView(
                order.Id,
                order.OrderNumber,
                orderStatus,
                order.CreatedOn,
                string.IsNullOrWhiteSpace(order.PaymentMethodLabel) ? order.PaymentMethodId : order.PaymentMethodLabel,
                order.PaymentReference,
                order.ItemsSubtotal,
                order.ShippingTotal,
                order.GrandTotal,
                discountTotal,
                promoCode,
                order.TotalQuantity,
                address,
                details.Items,
                details.Shipping,
                details.SubOrders);
        }

        public async Task<ReturnRequestResult> CreateReturnRequestAsync(
            int orderId,
            string buyerId,
            string? subOrderNumber,
            List<int>? productIds,
            string? reason,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(buyerId))
            {
                return new ReturnRequestResult(false, "Buyer is required.");
            }

            if (string.IsNullOrWhiteSpace(subOrderNumber))
            {
                return new ReturnRequestResult(false, "Select a sub-order to return.");
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                return new ReturnRequestResult(false, "Provide a reason for the return.");
            }

            var order = await _dbContext.Orders.FirstOrDefaultAsync(
                o => o.Id == orderId && o.BuyerId == buyerId,
                cancellationToken);
            if (order == null)
            {
                return new ReturnRequestResult(false, "Order not found.");
            }

            var details = DeserializeDetails(order.DetailsJson);
            var subOrderIndex = details.SubOrders.FindIndex(s => string.Equals(s.SubOrderNumber, subOrderNumber, StringComparison.OrdinalIgnoreCase));
            if (subOrderIndex < 0)
            {
                return new ReturnRequestResult(false, "Sub-order not found.");
            }

            var subOrder = details.SubOrders[subOrderIndex];
            var status = OrderStatuses.Normalize(subOrder.Status);
            if (!string.Equals(status, OrderStatuses.Delivered, StringComparison.OrdinalIgnoreCase))
            {
                return new ReturnRequestResult(false, "Returns are only available after delivery.");
            }

            if (subOrder.Return != null)
            {
                return new ReturnRequestResult(false, "A return has already been requested for this sub-order.");
            }

            if (!IsReturnWindowOpen(subOrder, order.CreatedOn))
            {
                return new ReturnRequestResult(false, $"Returns are only available within {ReturnPolicies.ReturnWindowDays} days of delivery.");
            }

            var items = BuildReturnItems(subOrder, productIds);
            if (items.Count == 0)
            {
                return new ReturnRequestResult(false, "Select at least one item to return.");
            }

            var request = new ReturnRequest(subOrder.SubOrderNumber, ReturnRequestStatuses.Requested, reason.Trim(), DateTimeOffset.UtcNow, items);
            details.SubOrders[subOrderIndex] = subOrder with { Return = request };
            order.DetailsJson = JsonSerializer.Serialize(details, _serializerOptions);

            await _dbContext.SaveChangesAsync(cancellationToken);
            return new ReturnRequestResult(true, null, request);
        }

        public async Task<PagedResult<OrderSummaryView>> GetSummariesForBuyerAsync(
            string buyerId,
            BuyerOrderFilterOptions? filters = null,
            int pageNumber = 1,
            int pageSize = 10,
            CancellationToken cancellationToken = default)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Clamp(pageSize, 1, 50);

            var query = _dbContext.Orders.AsNoTracking()
                .Where(o => o.BuyerId == buyerId);

            if (filters != null)
            {
                var statuses = filters.Statuses
                    .Select(OrderStatuses.Normalize)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (statuses.Count > 0)
                {
                    query = query.Where(o => statuses.Contains(o.Status));
                }

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
                    var sellerToken = $"\"sellerId\":\"{filters.SellerId.Trim()}\"";
                    query = query.Where(o => o.DetailsJson.Contains(sellerToken));
                }
            }

            var totalCount = await query.CountAsync(cancellationToken);
            var totalPages = pageSize <= 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
            if (totalPages == 0)
            {
                pageNumber = 1;
            }
            else if (pageNumber > totalPages)
            {
                pageNumber = totalPages;
            }

            var skip = (pageNumber - 1) * pageSize;
            var items = await query
                .OrderByDescending(o => o.CreatedOn)
                .Skip(skip)
                .Take(pageSize)
                .Select(o => new OrderSummaryView(o.Id, o.OrderNumber, o.CreatedOn, o.Status, o.GrandTotal, o.TotalQuantity))
                .ToListAsync(cancellationToken);

            return new PagedResult<OrderSummaryView>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<PagedResult<SellerOrderSummaryView>> GetSummariesForSellerAsync(
            string sellerId,
            SellerOrderFilterOptions? filters = null,
            int pageNumber = 1,
            int pageSize = 10,
            CancellationToken cancellationToken = default)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Clamp(pageSize, 1, 50);

            var summaries = await GetSellerOrderSummariesAsync(sellerId, filters, cancellationToken);
            var totalCount = summaries.Count;
            var totalPages = pageSize <= 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
            if (totalPages == 0)
            {
                pageNumber = 1;
            }
            else if (pageNumber > totalPages)
            {
                pageNumber = totalPages;
            }

            var skip = (pageNumber - 1) * pageSize;
            var items = summaries
                .OrderByDescending(o => o.CreatedOn)
                .Skip(skip)
                .Take(pageSize)
                .ToList();

            return new PagedResult<SellerOrderSummaryView>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<byte[]> ExportSellerOrdersAsync(
            string sellerId,
            SellerOrderFilterOptions? filters = null,
            CancellationToken cancellationToken = default)
        {
            var summaries = await GetSellerOrderSummariesAsync(sellerId, filters, cancellationToken);
            var builder = new StringBuilder();
            builder.AppendLine("SubOrder,Order,CreatedOn,Status,Buyer,BuyerEmail,Total,ShippingMethod");

            foreach (var summary in summaries.OrderByDescending(s => s.CreatedOn))
            {
                builder.AppendLine(string.Join(",", new[]
                {
                    CsvEscape(summary.SubOrderNumber),
                    CsvEscape(summary.OrderNumber),
                    CsvEscape(summary.CreatedOn.ToString("u", CultureInfo.InvariantCulture)),
                    CsvEscape(summary.Status),
                    CsvEscape(summary.BuyerName),
                    CsvEscape(summary.BuyerEmail),
                    CsvEscape(summary.GrandTotal.ToString("F2", CultureInfo.InvariantCulture)),
                    CsvEscape(summary.ShippingMethod)
                }));
            }

            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        private async Task<List<SellerOrderSummaryView>> GetSellerOrderSummariesAsync(
            string sellerId,
            SellerOrderFilterOptions? filters,
            CancellationToken cancellationToken)
        {
            var normalizedStatuses = filters?.Statuses
                .Select(OrderStatuses.Normalize)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            var buyerQuery = filters?.BuyerQuery?.Trim();
            var sellerToken = $"\"sellerId\":\"{sellerId}\"";

            var query = _dbContext.Orders.AsNoTracking()
                .Where(o => o.DetailsJson.Contains(sellerToken));

            if (filters?.FromDate.HasValue == true)
            {
                query = query.Where(o => o.CreatedOn >= filters.FromDate.Value);
            }

            if (filters?.ToDate.HasValue == true)
            {
                query = query.Where(o => o.CreatedOn <= filters.ToDate.Value);
            }

            if (!string.IsNullOrWhiteSpace(buyerQuery))
            {
                var like = $"%{buyerQuery}%";
                query = query.Where(o => EF.Functions.Like(o.BuyerName, like) || EF.Functions.Like(o.BuyerEmail, like));
            }

            var candidates = await query
                .OrderByDescending(o => o.CreatedOn)
                .ToListAsync(cancellationToken);

            var summaries = new List<SellerOrderSummaryView>();
            foreach (var order in candidates)
            {
                var details = DeserializeDetails(order.DetailsJson);
                var match = details.SubOrders.FirstOrDefault(s => string.Equals(s.SellerId, sellerId, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    continue;
                }

                var normalizedStatus = OrderStatuses.Normalize(match.Status);
                if (normalizedStatuses.Count > 0 && !normalizedStatuses.Contains(normalizedStatus))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(buyerQuery))
                {
                    var matchesBuyer = (!string.IsNullOrWhiteSpace(order.BuyerName) && order.BuyerName.Contains(buyerQuery, StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrWhiteSpace(order.BuyerEmail) && order.BuyerEmail.Contains(buyerQuery, StringComparison.OrdinalIgnoreCase));
                    if (!matchesBuyer)
                    {
                        continue;
                    }
                }

                var shippingMethod = string.IsNullOrWhiteSpace(match.ShippingDetail.MethodLabel)
                    ? match.ShippingDetail.MethodId
                    : match.ShippingDetail.MethodLabel;
                var buyerName = string.IsNullOrWhiteSpace(order.BuyerName) ? order.BuyerEmail ?? string.Empty : order.BuyerName;

                summaries.Add(new SellerOrderSummaryView(
                    order.Id,
                    order.OrderNumber,
                    match.SubOrderNumber,
                    order.CreatedOn,
                    normalizedStatus,
                    match.GrandTotal,
                    match.TotalQuantity,
                    match.SellerName,
                    buyerName,
                    order.BuyerEmail ?? string.Empty,
                    shippingMethod));
            }

            return summaries;
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

        public async Task<List<SellerFilterOption>> GetSellerFiltersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
        {
            var orders = await _dbContext.Orders.AsNoTracking()
                .Where(o => o.BuyerId == buyerId)
                .Select(o => o.DetailsJson)
                .ToListAsync(cancellationToken);

            var sellers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var payload in orders)
            {
                var details = DeserializeDetails(payload);
                foreach (var sub in details.SubOrders)
                {
                    if (string.IsNullOrWhiteSpace(sub.SellerId))
                    {
                        continue;
                    }

                    var sellerId = sub.SellerId.Trim();
                    var sellerName = string.IsNullOrWhiteSpace(sub.SellerName) ? sellerId : sub.SellerName;
                    if (!sellers.ContainsKey(sellerId))
                    {
                        sellers[sellerId] = sellerName;
                    }
                }
            }

            return sellers
                .Select(s => new SellerFilterOption(s.Key, s.Value))
                .OrderBy(s => s.Name)
                .ToList();
        }

        public Task<bool> OrderExistsAsync(int id, CancellationToken cancellationToken = default)
        {
            return _dbContext.Orders.AsNoTracking().AnyAsync(o => o.Id == id, cancellationToken);
        }

        public async Task<SellerOrderView?> GetSellerOrderAsync(int id, string sellerId, CancellationToken cancellationToken = default)
        {
            var sellerToken = $"\"sellerId\":\"{sellerId}\"";
            var order = await _dbContext.Orders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == id && o.DetailsJson.Contains(sellerToken), cancellationToken);
            if (order == null)
            {
                return null;
            }

            var details = DeserializeDetails(order.DetailsJson);
            var subOrder = details.SubOrders.FirstOrDefault(s => string.Equals(s.SellerId, sellerId, StringComparison.OrdinalIgnoreCase));
            if (subOrder == null)
            {
                return null;
            }

            var address = DeserializeAddress(order.DeliveryAddressJson);
            return new SellerOrderView(
                order.Id,
                order.OrderNumber,
                subOrder.SubOrderNumber,
                subOrder.Status,
                subOrder.TrackingNumber,
                subOrder.TrackingCarrier,
                subOrder.RefundedAmount,
                order.CreatedOn,
                string.IsNullOrWhiteSpace(order.PaymentMethodLabel) ? order.PaymentMethodId : order.PaymentMethodLabel,
                order.PaymentReference,
                subOrder.ItemsSubtotal,
                subOrder.Shipping,
                Math.Max(0, subOrder.DiscountTotal),
                subOrder.GrandTotal,
                subOrder.TotalQuantity,
                address,
                subOrder.Items,
                subOrder.ShippingDetail,
                order.BuyerName,
                order.BuyerEmail,
                address.Phone,
                OrderStatuses.Normalize(order.Status),
                subOrder.Return);
        }

        public async Task<SubOrderStatusUpdateResult> UpdateSubOrderStatusAsync(
            int orderId,
            string sellerId,
            string? newStatus,
            string? trackingNumber = null,
            decimal? refundedAmount = null,
            string? trackingCarrier = null,
            IEnumerable<int>? itemProductIds = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sellerId))
            {
                return new SubOrderStatusUpdateResult(false, "Seller is required.");
            }

            var sellerToken = $"\"sellerId\":\"{sellerId}\"";
            var order = await _dbContext.Orders.FirstOrDefaultAsync(
                o => o.Id == orderId && o.DetailsJson.Contains(sellerToken),
                cancellationToken);
            if (order == null)
            {
                return new SubOrderStatusUpdateResult(false, "Order not found.");
            }

            var details = DeserializeDetails(order.DetailsJson);
            var subOrder = details.SubOrders.FirstOrDefault(s => string.Equals(s.SellerId, sellerId, StringComparison.OrdinalIgnoreCase));
            if (subOrder == null)
            {
                return new SubOrderStatusUpdateResult(false, "Sub-order not found.");
            }

            var normalizedStatus = string.IsNullOrWhiteSpace(newStatus) ? subOrder.Status : OrderStatuses.Normalize(newStatus);
            if (string.IsNullOrWhiteSpace(normalizedStatus))
            {
                return new SubOrderStatusUpdateResult(false, "Status is required.");
            }

            var targetItems = itemProductIds?
                .Where(id => id > 0)
                .Distinct()
                .ToHashSet() ?? new HashSet<int>();

            if (targetItems.Count == 0 && !OrderStatuses.CanTransition(subOrder.Status, normalizedStatus))
            {
                return new SubOrderStatusUpdateResult(false, $"Cannot change status from {subOrder.Status} to {normalizedStatus}.");
            }

            var updatedItems = new List<OrderItemDetail>();
            var matchedCount = 0;
            foreach (var item in subOrder.Items)
            {
                var applyUpdate = targetItems.Count == 0 || targetItems.Contains(item.ProductId);
                if (!applyUpdate)
                {
                    updatedItems.Add(item);
                    continue;
                }

                matchedCount++;
                var currentItemStatus = OrderStatuses.Normalize(item.Status);
                if (!OrderStatuses.CanTransition(currentItemStatus, normalizedStatus))
                {
                    return new SubOrderStatusUpdateResult(false, $"Cannot change status for {item.Name} from {currentItemStatus} to {normalizedStatus}.");
                }

                updatedItems.Add(item with { Status = normalizedStatus });
            }

            if (targetItems.Count > 0 && matchedCount == 0)
            {
                return new SubOrderStatusUpdateResult(false, "Selected items were not found in this sub-order.");
            }

            var now = DateTimeOffset.UtcNow;
            var updatedStatus = CalculateSubOrderStatusFromItems(updatedItems, normalizedStatus);
            var computedRefund = subOrder.RefundedAmount;

            if (string.Equals(normalizedStatus, OrderStatuses.Refunded, StringComparison.OrdinalIgnoreCase))
            {
                var automaticRefund = CalculateRefundAmountForCancelledItems(updatedItems, subOrder.DiscountTotal, subOrder.Shipping, targetItems);
                computedRefund = Math.Max(0, refundedAmount ?? automaticRefund);
            }
            else if (refundedAmount.HasValue)
            {
                computedRefund = Math.Max(0, refundedAmount.Value);
            }

            var updatedSubOrder = subOrder with
            {
                Items = updatedItems,
                Status = updatedStatus,
                TrackingNumber = string.IsNullOrWhiteSpace(trackingNumber) ? subOrder.TrackingNumber : trackingNumber.Trim(),
                TrackingCarrier = string.IsNullOrWhiteSpace(trackingCarrier) ? subOrder.TrackingCarrier : trackingCarrier.Trim(),
                RefundedAmount = computedRefund,
                DeliveredOn = string.Equals(updatedStatus, OrderStatuses.Delivered, StringComparison.OrdinalIgnoreCase)
                    ? subOrder.DeliveredOn ?? now
                    : subOrder.DeliveredOn
            };

            var subOrderIndex = details.SubOrders.FindIndex(s => string.Equals(s.SellerId, sellerId, StringComparison.OrdinalIgnoreCase));
            if (subOrderIndex < 0)
            {
                return new SubOrderStatusUpdateResult(false, "Sub-order not found.");
            }

            details.SubOrders[subOrderIndex] = updatedSubOrder;

            order.Status = CalculateOrderStatus(details.SubOrders, order.Status);
            order.DetailsJson = JsonSerializer.Serialize(details, _serializerOptions);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new SubOrderStatusUpdateResult(true, null, updatedSubOrder, order.Status);
        }

        private OrderDetailsPayload BuildDetailsPayload(string orderNumber, ShippingQuote quote)
        {
            var items = new List<OrderItemDetail>();
            var shipping = new List<OrderShippingDetail>();
            var subOrders = new List<OrderSubOrder>();

            var sellerTotals = quote.Summary.SellerGroups
                .Select(g => Math.Max(0, g.Subtotal + g.Shipping))
                .ToList();
            var remainingDiscount = Math.Max(0, quote.Summary.DiscountTotal);
            var totalBeforeDiscount = sellerTotals.Sum();
            var sellerIndex = 0;

            foreach (var group in quote.Summary.SellerGroups)
            {
                sellerIndex++;
                var groupItems = new List<OrderItemDetail>();
                foreach (var item in group.Items)
                {
                    var detail = new OrderItemDetail(
                        item.Product.Id,
                        item.Product.Title,
                        item.VariantLabel,
                        item.Quantity,
                        item.UnitPrice,
                        item.LineTotal,
                        group.SellerId,
                        group.SellerName);
                    items.Add(detail);
                    groupItems.Add(detail);
                }

                var ship = ResolveShippingDetail(quote, group.SellerId, group.SellerName, group.Shipping);
                shipping.Add(ship);

                var baseTotal = Math.Max(0, group.Subtotal + ship.Cost);
                var discountShare = CalculateDiscountShare(remainingDiscount, baseTotal, totalBeforeDiscount, sellerIndex == quote.Summary.SellerGroups.Count);
                remainingDiscount -= discountShare;
                totalBeforeDiscount -= baseTotal;

                subOrders.Add(new OrderSubOrder(
                    $"{orderNumber}-{sellerIndex:00}",
                    group.SellerId,
                    group.SellerName,
                    group.Subtotal,
                    ship.Cost,
                    discountShare,
                    Math.Max(0, baseTotal - discountShare),
                    groupItems.Sum(i => i.Quantity),
                    groupItems,
                    ship,
                    OrderStatuses.Paid));
            }

            return new OrderDetailsPayload(items, shipping, quote.Summary.TotalQuantity, quote.Summary.DiscountTotal, quote.Summary.AppliedPromoCode, subOrders);
        }

        private static OrderShippingDetail ResolveShippingDetail(ShippingQuote quote, string sellerId, string sellerName, decimal fallbackCost)
        {
            var options = quote.SellerOptions.FirstOrDefault(o => string.Equals(o.SellerId, sellerId, StringComparison.OrdinalIgnoreCase));
            if (options == null || options.Options.Count == 0)
            {
                return new OrderShippingDetail(sellerId, sellerName, "standard", "Standard", Math.Max(0, fallbackCost), null);
            }

            var selected = quote.SelectedMethods.TryGetValue(sellerId, out var selection) ? selection : options.Options.FirstOrDefault()?.Id;
            var match = options.Options.FirstOrDefault(o => string.Equals(o.Id, selected, StringComparison.OrdinalIgnoreCase))
                ?? options.Options.First();

            return new OrderShippingDetail(
                sellerId,
                sellerName,
                match.Id,
                match.Label,
                match.Cost,
                match.Description);
        }

        private static decimal CalculateDiscountShare(decimal remainingDiscount, decimal baseTotal, decimal totalBeforeDiscount, bool isLastSeller)
        {
            if (remainingDiscount <= 0 || baseTotal <= 0 || totalBeforeDiscount <= 0)
            {
                return 0;
            }

            if (isLastSeller)
            {
                return Math.Min(remainingDiscount, baseTotal);
            }

            var proportional = remainingDiscount * (baseTotal / totalBeforeDiscount);
            var rounded = Math.Round(proportional, 2, MidpointRounding.AwayFromZero);
            return Math.Min(rounded, baseTotal);
        }

        private static List<ReturnRequestItem> BuildReturnItems(OrderSubOrder subOrder, List<int>? productIds)
        {
            var normalizedIds = productIds?
                .Where(id => id > 0)
                .Distinct()
                .ToHashSet() ?? new HashSet<int>();

            var items = new List<ReturnRequestItem>();
            foreach (var item in subOrder.Items)
            {
                if (normalizedIds.Count == 0 || normalizedIds.Contains(item.ProductId))
                {
                    items.Add(new ReturnRequestItem(item.ProductId, Math.Max(1, item.Quantity)));
                }
            }

            return items;
        }

        public static bool IsReturnWindowOpen(OrderSubOrder subOrder, DateTimeOffset orderCreatedOn)
        {
            var deliveredOn = subOrder.DeliveredOn ?? orderCreatedOn;
            if (deliveredOn == DateTimeOffset.MinValue)
            {
                return false;
            }

            return deliveredOn.Add(ReturnPolicies.ReturnWindow) >= DateTimeOffset.UtcNow;
        }

        private static decimal CalculateRefundAmountForCancelledItems(
            List<OrderItemDetail> items,
            decimal discountTotal,
            decimal shipping,
            HashSet<int> targetProducts)
        {
            var eligible = items
                .Where(i => (targetProducts.Count == 0 || targetProducts.Contains(i.ProductId))
                    && (string.Equals(i.Status, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(i.Status, OrderStatuses.Refunded, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (eligible.Count == 0)
            {
                return 0;
            }

            var cancelledTotal = eligible.Sum(i => i.LineTotal);
            var totalLines = Math.Max(0, items.Sum(i => i.LineTotal));
            var normalizedDiscount = Math.Max(0, discountTotal);
            var discountShare = totalLines <= 0
                ? 0
                : Math.Min(cancelledTotal, normalizedDiscount * (cancelledTotal / totalLines));
            var shippingShare = eligible.Count == items.Count ? shipping : 0;
            var refund = Math.Max(0, cancelledTotal + shippingShare - discountShare);
            return Math.Round(refund, 2, MidpointRounding.AwayFromZero);
        }

        private async Task SendConfirmationEmailAsync(OrderRecord order, DeliveryAddress address, OrderDetailsPayload details, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(order.BuyerEmail))
            {
                return;
            }

            try
            {
                var body = BuildEmailBody(order, address, details);
                await _emailSender.SendEmailAsync(order.BuyerEmail, $"Order confirmation {order.OrderNumber}", body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send order confirmation email for {OrderNumber}", order.OrderNumber);
            }
        }

        private static string BuildEmailBody(OrderRecord order, DeliveryAddress address, OrderDetailsPayload details)
        {
            var builder = new StringBuilder();
            builder.Append($"<h2>Thank you for your order {order.OrderNumber}</h2>");
            builder.Append($"<p>We confirmed your payment. Total: {order.GrandTotal:C}. Payment reference: {order.PaymentReference ?? "n/a"}.</p>");
            builder.Append("<h3>Items</h3><ul>");
            foreach (var item in details.Items)
            {
                var variant = string.IsNullOrWhiteSpace(item.Variant) ? string.Empty : $" ({item.Variant})";
                builder.Append($"<li>{item.Name}{variant} &times; {item.Quantity} — {item.LineTotal:C}</li>");
            }

            builder.Append("</ul>");
            builder.Append("<h3>Shipping</h3><ul>");
            foreach (var ship in details.Shipping)
            {
                builder.Append($"<li>{ship.SellerName}: {ship.MethodLabel} — {ship.Cost:C}");
                if (!string.IsNullOrWhiteSpace(ship.Description))
                {
                    builder.Append($" <span style='color: #666;'>{ship.Description}</span>");
                }

                builder.Append("</li>");
            }

            builder.Append("</ul>");
            builder.Append("<h3>Delivery address</h3>");
            builder.Append($"<p>{address.Recipient}<br />{address.Line1}<br />");
            if (!string.IsNullOrWhiteSpace(address.Line2))
            {
                builder.Append($"{address.Line2}<br />");
            }

            builder.Append($"{address.City}, {address.State} {address.PostalCode}<br />{address.Country}</p>");
            return builder.ToString();
        }

        private DeliveryAddress DeserializeAddress(string payload)
        {
            try
            {
                var address = JsonSerializer.Deserialize<DeliveryAddress>(payload, _serializerOptions);
                if (address != null)
                {
                    return address;
                }
            }
            catch
            {
            }

            return new DeliveryAddress(string.Empty, string.Empty, null, string.Empty, string.Empty, string.Empty, string.Empty, null);
        }

        private OrderDetailsPayload DeserializeDetails(string payload)
        {
            try
            {
                var details = JsonSerializer.Deserialize<OrderDetailsPayload>(payload, _serializerOptions);
                if (details != null)
                {
                    return NormalizeDetails(details);
                }
            }
            catch
            {
            }

            return new OrderDetailsPayload(new List<OrderItemDetail>(), new List<OrderShippingDetail>(), 0, 0, null, new List<OrderSubOrder>());
        }

        private static OrderDetailsPayload NormalizeDetails(OrderDetailsPayload details)
        {
            var normalizedItems = (details.Items ?? new List<OrderItemDetail>())
                .Select(i => NormalizeItem(i, OrderStatuses.Paid))
                .ToList();
            var normalizedShipping = details.Shipping ?? new List<OrderShippingDetail>();
            var normalizedSubOrders = (details.SubOrders ?? new List<OrderSubOrder>())
                .Select(NormalizeSubOrder)
                .ToList();

            return details with
            {
                Items = normalizedItems,
                Shipping = normalizedShipping,
                SubOrders = normalizedSubOrders
            };
        }

        private static OrderSubOrder NormalizeSubOrder(OrderSubOrder subOrder)
        {
            var status = OrderStatuses.Normalize(subOrder.Status);
            var normalizedItems = (subOrder.Items ?? new List<OrderItemDetail>())
                .Select(i => NormalizeItem(i, status))
                .ToList();
            var derivedStatus = CalculateSubOrderStatusFromItems(normalizedItems, status);
            var tracking = string.IsNullOrWhiteSpace(subOrder.TrackingNumber) ? null : subOrder.TrackingNumber.Trim();
            var carrier = string.IsNullOrWhiteSpace(subOrder.TrackingCarrier) ? null : subOrder.TrackingCarrier.Trim();
            var refunded = Math.Max(0, subOrder.RefundedAmount);
            var deliveredOn = subOrder.DeliveredOn == DateTimeOffset.MinValue ? null : subOrder.DeliveredOn;
            var normalizedReturn = NormalizeReturnRequest(subOrder.Return, normalizedItems);

            return subOrder with
            {
                Items = normalizedItems,
                Status = string.IsNullOrWhiteSpace(derivedStatus) ? OrderStatuses.Paid : derivedStatus,
                TrackingNumber = tracking,
                TrackingCarrier = carrier,
                RefundedAmount = refunded,
                DeliveredOn = deliveredOn,
                Return = normalizedReturn
            };
        }

        private static OrderItemDetail NormalizeItem(OrderItemDetail item, string fallbackStatus)
        {
            var normalizedStatus = OrderStatuses.Normalize(item.Status);
            if (string.IsNullOrWhiteSpace(normalizedStatus))
            {
                normalizedStatus = OrderStatuses.Normalize(fallbackStatus);
            }

            return item with { Status = string.IsNullOrWhiteSpace(normalizedStatus) ? OrderStatuses.Paid : normalizedStatus };
        }

        private static string CalculateSubOrderStatusFromItems(List<OrderItemDetail> items, string fallbackStatus)
        {
            var normalizedFallback = string.IsNullOrWhiteSpace(fallbackStatus) ? OrderStatuses.Paid : OrderStatuses.Normalize(fallbackStatus);
            var statuses = items
                .Select(i => OrderStatuses.Normalize(i.Status))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            if (statuses.Count == 0)
            {
                return normalizedFallback;
            }

            if (statuses.All(s => string.Equals(s, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)))
            {
                return OrderStatuses.Cancelled;
            }

            if (statuses.All(s => string.Equals(s, OrderStatuses.Refunded, StringComparison.OrdinalIgnoreCase)))
            {
                return OrderStatuses.Refunded;
            }

            if (statuses.All(s => string.Equals(s, OrderStatuses.Delivered, StringComparison.OrdinalIgnoreCase)))
            {
                return OrderStatuses.Delivered;
            }

            if (statuses.All(s => string.Equals(s, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)
                || string.Equals(s, OrderStatuses.Refunded, StringComparison.OrdinalIgnoreCase)))
            {
                return statuses.Any(s => string.Equals(s, OrderStatuses.Refunded, StringComparison.OrdinalIgnoreCase))
                    ? OrderStatuses.Refunded
                    : OrderStatuses.Cancelled;
            }

            if (statuses.All(s => string.Equals(s, OrderStatuses.Shipped, StringComparison.OrdinalIgnoreCase)
                || string.Equals(s, OrderStatuses.Delivered, StringComparison.OrdinalIgnoreCase)))
            {
                return OrderStatuses.Shipped;
            }

            if (statuses.Any(s => string.Equals(s, OrderStatuses.Shipped, StringComparison.OrdinalIgnoreCase)
                || string.Equals(s, OrderStatuses.Delivered, StringComparison.OrdinalIgnoreCase)))
            {
                return OrderStatuses.Shipped;
            }

            if (statuses.Any(s => string.Equals(s, OrderStatuses.Preparing, StringComparison.OrdinalIgnoreCase)))
            {
                return OrderStatuses.Preparing;
            }

            if (statuses.Any(s => string.Equals(s, OrderStatuses.New, StringComparison.OrdinalIgnoreCase)))
            {
                return OrderStatuses.New;
            }

            if (statuses.Any(s => string.Equals(s, OrderStatuses.Paid, StringComparison.OrdinalIgnoreCase)))
            {
                return OrderStatuses.Paid;
            }

            return normalizedFallback;
        }

        private static ReturnRequest? NormalizeReturnRequest(ReturnRequest? request, List<OrderItemDetail> items)
        {
            if (request == null)
            {
                return null;
            }

            var normalizedStatus = ReturnRequestStatuses.Normalize(request.Status);
            var normalizedReason = request.Reason?.Trim() ?? string.Empty;
            var normalizedItems = request.Items ?? new List<ReturnRequestItem>();
            if (normalizedItems.Count == 0)
            {
                normalizedItems = items.Select(i => new ReturnRequestItem(i.ProductId, i.Quantity)).ToList();
            }
            else
            {
                var allowedProducts = items.Select(i => i.ProductId).ToHashSet();
                normalizedItems = normalizedItems
                    .Where(i => allowedProducts.Contains(i.ProductId))
                    .Select(i => new ReturnRequestItem(i.ProductId, Math.Max(1, i.Quantity)))
                    .ToList();

                if (normalizedItems.Count == 0)
                {
                    normalizedItems = items.Select(i => new ReturnRequestItem(i.ProductId, i.Quantity)).ToList();
                }
            }

            return request with
            {
                Status = normalizedStatus,
                Reason = normalizedReason,
                Items = normalizedItems
            };
        }

        private static string CalculateOrderStatus(IEnumerable<OrderSubOrder> subOrders, string? fallbackStatus = null)
        {
            var statuses = subOrders?
                .Select(s => OrderStatuses.Normalize(s.Status))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList() ?? new List<string>();

            if (statuses.Count == 0)
            {
                return string.IsNullOrWhiteSpace(fallbackStatus) ? OrderStatuses.New : OrderStatuses.Normalize(fallbackStatus);
            }

            if (statuses.All(s => string.Equals(s, OrderStatuses.Refunded, StringComparison.OrdinalIgnoreCase)))
            {
                return OrderStatuses.Refunded;
            }

            if (statuses.All(s => string.Equals(s, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)))
            {
                return OrderStatuses.Cancelled;
            }

            if (statuses.Any(s => string.Equals(s, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)))
            {
                return OrderStatuses.Cancelled;
            }

            if (statuses.Any(s => string.Equals(s, OrderStatuses.Refunded, StringComparison.OrdinalIgnoreCase)))
            {
                return OrderStatuses.Refunded;
            }

            if (statuses.All(s => string.Equals(s, OrderStatuses.Delivered, StringComparison.OrdinalIgnoreCase)))
            {
                return OrderStatuses.Delivered;
            }

            if (statuses.Any(s => string.Equals(s, OrderStatuses.Delivered, StringComparison.OrdinalIgnoreCase)))
            {
                return OrderStatuses.Delivered;
            }

            if (statuses.Any(s => string.Equals(s, OrderStatuses.Shipped, StringComparison.OrdinalIgnoreCase)))
            {
                return OrderStatuses.Shipped;
            }

            if (statuses.Any(s => string.Equals(s, OrderStatuses.Preparing, StringComparison.OrdinalIgnoreCase)))
            {
                return OrderStatuses.Preparing;
            }

            if (statuses.Any(s => string.Equals(s, OrderStatuses.Paid, StringComparison.OrdinalIgnoreCase)))
            {
                return OrderStatuses.Paid;
            }

            return string.IsNullOrWhiteSpace(fallbackStatus) ? OrderStatuses.New : OrderStatuses.Normalize(fallbackStatus);
        }

        private static string GenerateOrderNumber()
        {
            var random = Random.Shared.Next(1000, 9999);
            return $"ORD-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{random}";
        }
    }
}
