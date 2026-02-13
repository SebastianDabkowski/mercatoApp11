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
        public const string Failed = "Failed";

        private static readonly Dictionary<string, HashSet<string>> AllowedTransitions = new(StringComparer.OrdinalIgnoreCase)
        {
            [New] = new HashSet<string>(new[] { Paid, Cancelled, Failed }, StringComparer.OrdinalIgnoreCase),
            [Paid] = new HashSet<string>(new[] { Preparing, Shipped, Delivered, Cancelled, Refunded }, StringComparer.OrdinalIgnoreCase),
            [Preparing] = new HashSet<string>(new[] { Shipped, Cancelled, Refunded }, StringComparer.OrdinalIgnoreCase),
            [Shipped] = new HashSet<string>(new[] { Delivered, Refunded }, StringComparer.OrdinalIgnoreCase),
            [Delivered] = new HashSet<string>(new[] { Refunded }, StringComparer.OrdinalIgnoreCase),
            [Cancelled] = new HashSet<string>(new[] { Refunded }, StringComparer.OrdinalIgnoreCase),
            [Refunded] = new HashSet<string>(Array.Empty<string>(), StringComparer.OrdinalIgnoreCase),
            [Failed] = new HashSet<string>(Array.Empty<string>(), StringComparer.OrdinalIgnoreCase)
        };

        private static readonly IReadOnlyList<string> OrderedStatuses = new[]
        {
            New, Paid, Preparing, Shipped, Delivered, Cancelled, Refunded, Failed
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

            if (status.Trim().Equals(Failed, StringComparison.OrdinalIgnoreCase))
            {
                return Failed;
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

    public record OrderItemDetail(int ProductId, string Name, string Variant, int Quantity, decimal UnitPrice, decimal LineTotal, string SellerId, string SellerName, string Status = OrderStatuses.Paid, string Category = "", decimal? CommissionRate = null);

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

    public static class EscrowEntryTypes
    {
        public const string Hold = "Hold";
        public const string ReleaseToBuyer = "ReleaseToBuyer";
        public const string PayoutEligible = "PayoutEligible";
    }

    public record EscrowLedgerEntry(string SubOrderNumber, string SellerId, string Type, decimal Amount, string? Note, DateTimeOffset RecordedOn, string? Reference = null);

    public static class PayoutStatuses
    {
        public const string Scheduled = "Scheduled";
        public const string Processing = "Processing";
        public const string Paid = "Paid";
        public const string Failed = "Failed";

        private static readonly string[] Ordered = { Scheduled, Processing, Paid, Failed };

        public static IReadOnlyList<string> All => Ordered;

        public static string Normalize(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return Scheduled;
            }

            var match = Ordered.FirstOrDefault(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));
            return match ?? status.Trim();
        }
    }

    public record EscrowAllocation(
        string SubOrderNumber,
        string SellerId,
        decimal HeldAmount,
        decimal CommissionAmount,
        decimal SellerPayoutAmount,
        decimal ReleasedToBuyer,
        decimal ReleasedToSeller,
        bool PayoutEligible,
        List<EscrowLedgerEntry> Ledger,
        string PayoutSchedule = PayoutSchedules.Weekly,
        string PayoutStatus = PayoutStatuses.Scheduled,
        string? PayoutErrorReference = null);

    public record OrderDetailsPayload(
        List<OrderItemDetail> Items,
        List<OrderShippingDetail> Shipping,
        int TotalQuantity,
        decimal DiscountTotal = 0,
        string? PromoCode = null,
        List<OrderSubOrder> SubOrders = null!,
        List<EscrowAllocation>? Escrow = null,
        string PaymentStatus = PaymentStatuses.Paid,
        string? PaymentStatusMessage = null,
        decimal PaymentRefundedAmount = 0);

    public record OrderView(
        int Id,
        string OrderNumber,
        string Status,
        DateTimeOffset CreatedOn,
        string PaymentMethodLabel,
        string? PaymentReference,
        string PaymentStatus,
        string? PaymentStatusMessage,
        decimal ItemsSubtotal,
        decimal ShippingTotal,
        decimal GrandTotal,
        decimal PaymentRefundedAmount,
        decimal DiscountTotal,
        string? PromoCode,
        int TotalQuantity,
        DeliveryAddress Address,
        List<OrderItemDetail> Items,
        List<OrderShippingDetail> Shipping,
        List<OrderSubOrder> SubOrders,
        List<EscrowAllocation> Escrow);

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
        ReturnRequest? ReturnRequest,
        EscrowAllocation? Escrow);

    public record SellerPayoutScheduleView(string Schedule, string Status, decimal EligibleAmount, decimal ProcessingAmount, decimal PaidAmount, decimal Threshold, string? ErrorReference);

    public record SellerPayoutSummaryView(
        int OrderId,
        string OrderNumber,
        string SubOrderNumber,
        DateTimeOffset PayoutOn,
        decimal Amount,
        string Status,
        string? ErrorReference);

    public record SellerPayoutDetailView(
        int OrderId,
        string OrderNumber,
        string SubOrderNumber,
        DateTimeOffset PayoutOn,
        string Status,
        decimal PayoutAmount,
        decimal ReleasedToSeller,
        decimal CommissionAmount,
        decimal HeldAmount,
        decimal ReleasedToBuyer,
        string PaymentMethod,
        string PaymentStatus,
        string BuyerName,
        string BuyerEmail,
        string? BuyerPhone,
        DeliveryAddress Address,
        OrderSubOrder SubOrder,
        List<EscrowLedgerEntry> Ledger,
        string? ErrorReference);

    public record PayoutRunResult(bool Success, string Status, decimal ProcessedAmount, string? ErrorReference = null);

    public record OrderCreationResult(OrderRecord Order, bool Created);

    public record PaymentStatusUpdateResult(bool Success, string? Error, string? PaymentStatus = null, decimal PaymentRefundedAmount = 0);

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

    public record SellerPayoutFilterOptions
    {
        public List<string> Statuses { get; init; } = new();

        public DateTimeOffset? FromDate { get; init; }

        public DateTimeOffset? ToDate { get; init; }
    }

    public record SellerFilterOption(string Id, string Name);

    public class OrderService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<OrderService> _logger;
        private readonly EscrowOptions _escrowOptions;
        private readonly CartOptions _cartOptions;
        private readonly CommissionCalculator _commissionCalculator;
        private readonly JsonSerializerOptions _serializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public OrderService(ApplicationDbContext dbContext, IEmailSender emailSender, ILogger<OrderService> logger, EscrowOptions? escrowOptions = null, CartOptions? cartOptions = null)
        {
            _dbContext = dbContext;
            _emailSender = emailSender;
            _logger = logger;
            _escrowOptions = escrowOptions ?? new EscrowOptions();
            _cartOptions = cartOptions ?? new CartOptions();
            _commissionCalculator = new CommissionCalculator(_cartOptions);
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
            string initialStatus = OrderStatuses.Paid,
            string paymentStatus = PaymentStatuses.Paid,
            string? paymentMessage = null,
            decimal paymentRefundedAmount = 0,
            CancellationToken cancellationToken = default)
        {
            var normalizedReference = string.IsNullOrWhiteSpace(state.PaymentReference) ? null : state.PaymentReference.Trim();
            var normalizedSignature = string.IsNullOrWhiteSpace(state.CartSignature) ? null : state.CartSignature.Trim();
            OrderRecord? existingOrder = null;
            if (!string.IsNullOrEmpty(normalizedReference))
            {
                existingOrder = await _dbContext.Orders.FirstOrDefaultAsync(o => o.PaymentReference == normalizedReference, cancellationToken);
            }

            if (existingOrder == null && !string.IsNullOrWhiteSpace(normalizedSignature) && !string.IsNullOrWhiteSpace(buyerId))
            {
                existingOrder = await _dbContext.Orders.FirstOrDefaultAsync(
                    o => o.BuyerId == buyerId && o.CartSignature == normalizedSignature,
                    cancellationToken);
                if (existingOrder != null && string.IsNullOrWhiteSpace(existingOrder.PaymentReference) && !string.IsNullOrWhiteSpace(normalizedReference))
                {
                    existingOrder.PaymentReference = normalizedReference;
                }
            }

            var normalizedInitialStatus = OrderStatuses.Normalize(initialStatus);
            var normalizedPaymentStatus = PaymentStatuses.Normalize(paymentStatus);
            if (string.Equals(normalizedPaymentStatus, PaymentStatuses.Paid, StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(normalizedInitialStatus, OrderStatuses.Failed, StringComparison.OrdinalIgnoreCase))
                {
                    normalizedPaymentStatus = PaymentStatuses.Failed;
                }
                else if (string.Equals(normalizedInitialStatus, OrderStatuses.Refunded, StringComparison.OrdinalIgnoreCase))
                {
                    normalizedPaymentStatus = PaymentStatuses.Refunded;
                }
                else if (string.Equals(normalizedInitialStatus, OrderStatuses.New, StringComparison.OrdinalIgnoreCase))
                {
                    normalizedPaymentStatus = PaymentStatuses.Pending;
                }
            }

            var normalizedPaymentMessage = string.IsNullOrWhiteSpace(paymentMessage)
                ? PaymentStatusMapper.BuildBuyerMessage(normalizedPaymentStatus)
                : paymentMessage.Trim();
            var normalizedRefunded = Math.Max(0, paymentRefundedAmount);
            if (existingOrder != null)
            {
                var updated = await UpdateOrderPaymentAsync(existingOrder, normalizedInitialStatus, normalizedPaymentStatus, normalizedPaymentMessage, normalizedRefunded, normalizedReference, cancellationToken);
                return new OrderCreationResult(updated, false);
            }

            var orderNumber = GenerateOrderNumber();
            var details = BuildDetailsPayload(orderNumber, quote, normalizedInitialStatus, normalizedReference, normalizedPaymentStatus, normalizedPaymentMessage, normalizedRefunded);
            var order = new OrderRecord
            {
                OrderNumber = orderNumber,
                Status = CalculateOrderStatus(details.SubOrders, normalizedInitialStatus),
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

            if (string.Equals(details.PaymentStatus, PaymentStatuses.Paid, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(order.Status, OrderStatuses.Failed, StringComparison.OrdinalIgnoreCase))
            {
                await SendConfirmationEmailAsync(order, address, details, cancellationToken);
            }

            return new OrderCreationResult(order, true);
        }

        private async Task<OrderRecord> UpdateOrderPaymentAsync(
            OrderRecord order,
            string normalizedInitialStatus,
            string normalizedPaymentStatus,
            string paymentMessage,
            decimal paymentRefundedAmount,
            string? paymentReference,
            CancellationToken cancellationToken)
        {
            var details = DeserializeDetails(order.DetailsJson);
            var normalizedMessage = string.IsNullOrWhiteSpace(paymentMessage)
                ? PaymentStatusMapper.BuildBuyerMessage(normalizedPaymentStatus)
                : paymentMessage.Trim();
            var paymentRefunded = Math.Max(details.PaymentRefundedAmount, paymentRefundedAmount);
            var hasChanges = false;
            var updatedDetails = details;

            if (!string.Equals(details.PaymentStatus, normalizedPaymentStatus, StringComparison.OrdinalIgnoreCase)
                || paymentRefunded > details.PaymentRefundedAmount
                || !string.Equals(details.PaymentStatusMessage ?? string.Empty, normalizedMessage, StringComparison.Ordinal))
            {
                updatedDetails = updatedDetails with
                {
                    PaymentStatus = normalizedPaymentStatus,
                    PaymentStatusMessage = normalizedMessage,
                    PaymentRefundedAmount = paymentRefunded
                };
                hasChanges = true;
            }

            if (ShouldApplyFulfillmentStatus(order.Status, normalizedInitialStatus, updatedDetails.SubOrders))
            {
                updatedDetails = ApplyStatusToDetails(updatedDetails, normalizedInitialStatus);
                order.Status = CalculateOrderStatus(updatedDetails.SubOrders, normalizedInitialStatus);
                hasChanges = true;
            }

            if (string.Equals(normalizedInitialStatus, OrderStatuses.Paid, StringComparison.OrdinalIgnoreCase)
                && (updatedDetails.Escrow == null || updatedDetails.Escrow.Count == 0)
                && updatedDetails.SubOrders.Count > 0)
            {
                var reference = !string.IsNullOrWhiteSpace(paymentReference) ? paymentReference : order.PaymentReference;
                updatedDetails = updatedDetails with
                {
                    Escrow = BuildEscrowAllocations(updatedDetails.SubOrders, normalizedInitialStatus, reference)
                };
                hasChanges = true;
            }

            if (!string.IsNullOrWhiteSpace(paymentReference) && !string.Equals(order.PaymentReference, paymentReference, StringComparison.OrdinalIgnoreCase))
            {
                order.PaymentReference = paymentReference;
                hasChanges = true;
            }

            if (hasChanges)
            {
                order.DetailsJson = JsonSerializer.Serialize(updatedDetails, _serializerOptions);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return order;
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
            var paymentStatus = PaymentStatuses.Normalize(details.PaymentStatus);
            var paymentRefunded = Math.Max(details.PaymentRefundedAmount, details.SubOrders.Sum(s => Math.Max(0, s.RefundedAmount)));
            if (paymentRefunded > 0 && !string.Equals(paymentStatus, PaymentStatuses.Refunded, StringComparison.OrdinalIgnoreCase))
            {
                paymentStatus = PaymentStatuses.Refunded;
            }

            var paymentMessage = string.IsNullOrWhiteSpace(details.PaymentStatusMessage)
                ? PaymentStatusMapper.BuildBuyerMessage(paymentStatus)
                : details.PaymentStatusMessage;

                return new OrderView(
                order.Id,
                order.OrderNumber,
                orderStatus,
                order.CreatedOn,
                string.IsNullOrWhiteSpace(order.PaymentMethodLabel) ? order.PaymentMethodId : order.PaymentMethodLabel,
                order.PaymentReference,
                paymentStatus,
                paymentMessage,
                order.ItemsSubtotal,
                order.ShippingTotal,
                order.GrandTotal,
                paymentRefunded,
                discountTotal,
                promoCode,
                order.TotalQuantity,
                address,
                details.Items,
                details.Shipping,
                details.SubOrders,
                details.Escrow ?? new List<EscrowAllocation>());
        }

        public async Task<PaymentStatusUpdateResult> UpdatePaymentStatusAsync(
            string paymentReference,
            string providerStatus,
            decimal? refundedAmount = null,
            string? providerMessage = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(paymentReference))
            {
                return new PaymentStatusUpdateResult(false, "Payment reference is required.");
            }

            var reference = paymentReference.Trim();
            var order = await _dbContext.Orders.FirstOrDefaultAsync(o => o.PaymentReference == reference, cancellationToken);
            if (order == null)
            {
                return new PaymentStatusUpdateResult(false, "Order not found for this payment reference.");
            }

            if (!string.IsNullOrWhiteSpace(providerMessage))
            {
                _logger.LogInformation("Payment webhook status {Status} for {Reference}: {Message}", providerStatus, reference, providerMessage);
            }

            var targetPaymentStatus = PaymentStatusMapper.MapProviderStatus(providerStatus);
            var buyerMessage = PaymentStatusMapper.BuildBuyerMessage(targetPaymentStatus);
            var normalizedOrderStatus = OrderStatuses.Normalize(order.Status);
            var targetFulfillmentStatus = normalizedOrderStatus;
            if (targetPaymentStatus == PaymentStatuses.Paid && string.Equals(normalizedOrderStatus, OrderStatuses.New, StringComparison.OrdinalIgnoreCase))
            {
                targetFulfillmentStatus = OrderStatuses.Paid;
            }
            else if (targetPaymentStatus == PaymentStatuses.Failed && string.Equals(normalizedOrderStatus, OrderStatuses.New, StringComparison.OrdinalIgnoreCase))
            {
                targetFulfillmentStatus = OrderStatuses.Failed;
            }
            else if (targetPaymentStatus == PaymentStatuses.Refunded && string.Equals(normalizedOrderStatus, OrderStatuses.Paid, StringComparison.OrdinalIgnoreCase))
            {
                targetFulfillmentStatus = OrderStatuses.Refunded;
            }

            var refunded = Math.Max(0, refundedAmount ?? 0);
            var updated = await UpdateOrderPaymentAsync(order, targetFulfillmentStatus, targetPaymentStatus, buyerMessage, refunded, reference, cancellationToken);
            var details = DeserializeDetails(updated.DetailsJson);

            return new PaymentStatusUpdateResult(true, null, details.PaymentStatus, details.PaymentRefundedAmount);
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

            var escrow = details.Escrow?.FirstOrDefault(e =>
                string.Equals(e.SubOrderNumber, subOrder.SubOrderNumber, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.SellerId, sellerId, StringComparison.OrdinalIgnoreCase));
            var address = DeserializeAddress(order.DeliveryAddressJson);
            var paymentStatus = ResolvePaymentStatusForSubOrder(details, subOrder);
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
                paymentStatus,
                subOrder.Return,
                escrow);
        }

        public async Task<PagedResult<SellerPayoutSummaryView>> GetPayoutsForSellerAsync(
            string sellerId,
            SellerPayoutFilterOptions? filters = null,
            int pageNumber = 1,
            int pageSize = 10,
            CancellationToken cancellationToken = default)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Clamp(pageSize, 1, 50);

            var normalizedStatuses = filters?.Statuses
                .Select(PayoutStatuses.Normalize)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            var sellerToken = $"\"sellerId\":\"{sellerId}\"";
            var orders = await _dbContext.Orders.AsNoTracking()
                .Where(o => o.DetailsJson.Contains(sellerToken))
                .OrderByDescending(o => o.CreatedOn)
                .ToListAsync(cancellationToken);

            var payouts = new List<SellerPayoutSummaryView>();
            foreach (var order in orders)
            {
                var details = DeserializeDetails(order.DetailsJson);
                var subOrder = details.SubOrders.FirstOrDefault(s => string.Equals(s.SellerId, sellerId, StringComparison.OrdinalIgnoreCase));
                var allocation = details.Escrow?.FirstOrDefault(e =>
                    string.Equals(e.SubOrderNumber, subOrder?.SubOrderNumber, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(e.SellerId, sellerId, StringComparison.OrdinalIgnoreCase));
                if (subOrder == null || allocation == null)
                {
                    continue;
                }

                var status = PayoutStatuses.Normalize(allocation.PayoutStatus);
                if (normalizedStatuses.Count > 0 && !normalizedStatuses.Contains(status))
                {
                    continue;
                }

                var payoutOn = ResolvePayoutDate(allocation, order.CreatedOn);
                if (filters?.FromDate.HasValue == true && payoutOn < filters.FromDate.Value)
                {
                    continue;
                }

                if (filters?.ToDate.HasValue == true && payoutOn > filters.ToDate.Value)
                {
                    continue;
                }

                var amount = Math.Max(allocation.SellerPayoutAmount, allocation.ReleasedToSeller);
                payouts.Add(new SellerPayoutSummaryView(
                    order.Id,
                    order.OrderNumber,
                    subOrder.SubOrderNumber,
                    payoutOn,
                    amount,
                    status,
                    allocation.PayoutErrorReference));
            }

            var totalCount = payouts.Count;
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
            var items = payouts
                .OrderByDescending(p => p.PayoutOn)
                .Skip(skip)
                .Take(pageSize)
                .ToList();

            return new PagedResult<SellerPayoutSummaryView>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<SellerPayoutDetailView?> GetSellerPayoutAsync(int orderId, string sellerId, CancellationToken cancellationToken = default)
        {
            var sellerToken = $"\"sellerId\":\"{sellerId}\"";
            var order = await _dbContext.Orders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == orderId && o.DetailsJson.Contains(sellerToken), cancellationToken);
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

            var allocation = details.Escrow?.FirstOrDefault(e =>
                string.Equals(e.SubOrderNumber, subOrder.SubOrderNumber, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.SellerId, sellerId, StringComparison.OrdinalIgnoreCase));
            if (allocation == null)
            {
                return null;
            }

            var address = DeserializeAddress(order.DeliveryAddressJson);
            var paymentStatus = ResolvePaymentStatusForSubOrder(details, subOrder);
            var payoutOn = ResolvePayoutDate(allocation, order.CreatedOn);

            return new SellerPayoutDetailView(
                order.Id,
                order.OrderNumber,
                subOrder.SubOrderNumber,
                payoutOn,
                PayoutStatuses.Normalize(allocation.PayoutStatus),
                allocation.SellerPayoutAmount,
                allocation.ReleasedToSeller,
                allocation.CommissionAmount,
                allocation.HeldAmount,
                allocation.ReleasedToBuyer,
                string.IsNullOrWhiteSpace(order.PaymentMethodLabel) ? order.PaymentMethodId : order.PaymentMethodLabel,
                paymentStatus,
                order.BuyerName,
                order.BuyerEmail,
                address.Phone,
                address,
                subOrder,
                allocation.Ledger ?? new List<EscrowLedgerEntry>(),
                allocation.PayoutErrorReference);
        }

        public async Task<SellerPayoutScheduleView> GetSellerPayoutScheduleAsync(string sellerId, string payoutSchedule, CancellationToken cancellationToken = default)
        {
            var normalizedSchedule = PayoutSchedules.IsValid(payoutSchedule) ? payoutSchedule : _escrowOptions.DefaultPayoutSchedule;
            var threshold = Math.Max(0, _escrowOptions.MinimumPayoutAmount);
            var orders = await _dbContext.Orders.AsNoTracking().OrderByDescending(o => o.CreatedOn).ToListAsync(cancellationToken);
            decimal eligible = 0;
            decimal processing = 0;
            decimal paid = 0;
            var status = PayoutStatuses.Scheduled;
            string? errorRef = null;

            foreach (var order in orders)
            {
                var details = DeserializeDetails(order.DetailsJson);
                var allocations = details.Escrow ?? new List<EscrowAllocation>();
                foreach (var allocation in allocations.Where(e => string.Equals(e.SellerId, sellerId, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!allocation.PayoutEligible)
                    {
                        continue;
                    }

                    var payoutStatus = PayoutStatuses.Normalize(allocation.PayoutStatus);
                    var remaining = Math.Max(0, allocation.SellerPayoutAmount - allocation.ReleasedToSeller);
                    switch (payoutStatus)
                    {
                        case PayoutStatuses.Failed:
                            status = PayoutStatuses.Failed;
                            errorRef ??= allocation.PayoutErrorReference;
                            eligible += remaining;
                            break;
                        case PayoutStatuses.Processing:
                            if (status != PayoutStatuses.Failed)
                            {
                                status = PayoutStatuses.Processing;
                            }

                            processing += remaining;
                            break;
                        case PayoutStatuses.Paid:
                            paid += allocation.ReleasedToSeller > 0 ? allocation.ReleasedToSeller : allocation.SellerPayoutAmount;
                            if (status == PayoutStatuses.Scheduled)
                            {
                                status = PayoutStatuses.Paid;
                            }

                            break;
                        default:
                            eligible += remaining;
                            if (status == PayoutStatuses.Paid)
                            {
                                status = PayoutStatuses.Processing;
                            }

                            break;
                    }
                }
            }

            return new SellerPayoutScheduleView(normalizedSchedule, status, eligible, processing, paid, threshold, errorRef);
        }

        public async Task<PayoutRunResult> RunSellerPayoutsAsync(string sellerId, CancellationToken cancellationToken = default)
        {
            var normalizedSchedule = _escrowOptions.DefaultPayoutSchedule;
            var batchSize = Math.Max(1, _escrowOptions.PayoutBatchSize);
            var threshold = Math.Max(0, _escrowOptions.MinimumPayoutAmount);
            var orders = await _dbContext.Orders.OrderBy(o => o.CreatedOn).ToListAsync(cancellationToken);
            var seller = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == sellerId, cancellationToken);
            var payoutReady = seller == null || seller.PayoutUpdatedOn != null;
            var processed = 0;
            decimal processedAmount = 0;
            string? errorRef = null;
            var runStatus = PayoutStatuses.Scheduled;

            foreach (var order in orders)
            {
                var details = DeserializeDetails(order.DetailsJson);
                var updatedEscrow = new List<EscrowAllocation>();
                var changed = false;

                var allocations = details.Escrow ?? new List<EscrowAllocation>();
                foreach (var allocation in allocations)
                {
                    if (!string.Equals(allocation.SellerId, sellerId, StringComparison.OrdinalIgnoreCase))
                    {
                        updatedEscrow.Add(allocation);
                        continue;
                    }

                    var payoutStatus = PayoutStatuses.Normalize(allocation.PayoutStatus);
                    var remaining = Math.Max(0, allocation.SellerPayoutAmount - allocation.ReleasedToSeller);
                    if (!allocation.PayoutEligible || remaining <= 0)
                    {
                        updatedEscrow.Add(allocation with
                        {
                            PayoutStatus = remaining <= 0 ? PayoutStatuses.Paid : payoutStatus,
                            PayoutErrorReference = null
                        });
                        continue;
                    }

                    if (!payoutReady)
                    {
                        if (seller != null)
                        {
                            errorRef ??= "payout_details_missing";
                            runStatus = PayoutStatuses.Failed;
                        }

                        updatedEscrow.Add(allocation with
                        {
                            PayoutStatus = seller == null ? PayoutStatuses.Scheduled : PayoutStatuses.Failed,
                            PayoutErrorReference = errorRef
                        });
                        changed = true;
                        continue;
                    }

                    if (remaining < threshold)
                    {
                        updatedEscrow.Add(allocation with
                        {
                            PayoutStatus = PayoutStatuses.Scheduled,
                            PayoutErrorReference = null,
                            PayoutSchedule = normalizedSchedule
                        });
                        continue;
                    }

                    if (processed >= batchSize)
                    {
                        updatedEscrow.Add(allocation);
                        continue;
                    }

                    runStatus = PayoutStatuses.Processing;
                    var ledger = allocation.Ledger ?? new List<EscrowLedgerEntry>();
                    ledger.Add(new EscrowLedgerEntry(
                        allocation.SubOrderNumber,
                        allocation.SellerId,
                        EscrowEntryTypes.PayoutEligible,
                        remaining,
                        "Payout processing",
                        DateTimeOffset.UtcNow,
                        seller?.PayoutMethod));

                    processed++;
                    processedAmount += remaining;
                    updatedEscrow.Add(allocation with
                    {
                        ReleasedToSeller = allocation.ReleasedToSeller + remaining,
                        PayoutStatus = PayoutStatuses.Paid,
                        PayoutErrorReference = null,
                        PayoutSchedule = normalizedSchedule,
                        Ledger = ledger
                    });
                    changed = true;
                }

                if (changed)
                {
                    var normalized = NormalizeDetails(details with { Escrow = updatedEscrow });
                    order.DetailsJson = JsonSerializer.Serialize(normalized, _serializerOptions);
                }
            }

            if (processedAmount > 0 && errorRef == null)
            {
                runStatus = PayoutStatuses.Paid;
            }

            if (runStatus == PayoutStatuses.Scheduled && errorRef != null)
            {
                runStatus = PayoutStatuses.Failed;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return new PayoutRunResult(errorRef == null, runStatus, processedAmount, errorRef);
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

            var totalRefunded = details.SubOrders.Sum(s => Math.Max(0, s.RefundedAmount));
            if (totalRefunded > 0 && !string.Equals(details.PaymentStatus, PaymentStatuses.Refunded, StringComparison.OrdinalIgnoreCase))
            {
                details = details with { PaymentStatus = PaymentStatuses.Refunded, PaymentRefundedAmount = totalRefunded };
            }
            else
            {
                details = details with { PaymentRefundedAmount = Math.Max(details.PaymentRefundedAmount, totalRefunded) };
            }

            var updatedEscrow = UpdateEscrowAllocations(details.Escrow, details.SubOrders, updatedSubOrder, order.PaymentReference);
            details = details with { Escrow = updatedEscrow };
            order.Status = CalculateOrderStatus(details.SubOrders, order.Status);
            order.DetailsJson = JsonSerializer.Serialize(details, _serializerOptions);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new SubOrderStatusUpdateResult(true, null, updatedSubOrder, order.Status);
        }

        private OrderDetailsPayload BuildDetailsPayload(string orderNumber, ShippingQuote quote, string initialStatus, string? paymentReference, string paymentStatus, string paymentMessage, decimal paymentRefundedAmount)
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
                    var rate = _commissionCalculator.ResolveRate(group.SellerId, item.Product.Category);
                    var detail = new OrderItemDetail(
                        item.Product.Id,
                        item.Product.Title,
                        item.VariantLabel,
                        item.Quantity,
                        item.UnitPrice,
                        item.LineTotal,
                        group.SellerId,
                        group.SellerName,
                        initialStatus,
                        item.Product.Category,
                        rate);
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
                    initialStatus));
            }

            var escrow = BuildEscrowAllocations(subOrders, initialStatus, paymentReference);
            return new OrderDetailsPayload(
                items,
                shipping,
                quote.Summary.TotalQuantity,
                quote.Summary.DiscountTotal,
                quote.Summary.AppliedPromoCode,
                subOrders,
                escrow,
                paymentStatus,
                paymentMessage,
                Math.Max(0, paymentRefundedAmount));
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

        private List<EscrowAllocation> BuildEscrowAllocations(List<OrderSubOrder> subOrders, string initialStatus, string? paymentReference)
        {
            var allocations = new List<EscrowAllocation>();
            if (!string.Equals(initialStatus, OrderStatuses.Paid, StringComparison.OrdinalIgnoreCase))
            {
                return allocations;
            }

            var now = DateTimeOffset.UtcNow;

            foreach (var subOrder in subOrders)
            {
                var holdAmount = Math.Max(0, subOrder.GrandTotal);
                var commission = _commissionCalculator.CalculateForOrderItems(subOrder.Items);
                var payout = _commissionCalculator.Round(Math.Max(0, holdAmount - commission));
                var payoutEligible = IsPayoutEligible(subOrder.Status);
                var ledger = new List<EscrowLedgerEntry>
                {
                    new EscrowLedgerEntry(subOrder.SubOrderNumber, subOrder.SellerId, EscrowEntryTypes.Hold, holdAmount, "Payment confirmed and held in escrow", now, paymentReference)
                };

                if (payoutEligible)
                {
                    ledger.Add(new EscrowLedgerEntry(subOrder.SubOrderNumber, subOrder.SellerId, EscrowEntryTypes.PayoutEligible, payout, $"Status {OrderStatuses.Normalize(subOrder.Status)} allows payout", now, paymentReference));
                }

                allocations.Add(new EscrowAllocation(
                    subOrder.SubOrderNumber,
                    subOrder.SellerId,
                    holdAmount,
                    commission,
                    payout,
                    0,
                    0,
                    payoutEligible,
                    ledger,
                    _escrowOptions.DefaultPayoutSchedule,
                    payoutEligible ? PayoutStatuses.Scheduled : PayoutStatuses.Scheduled));
            }

            return allocations;
        }

        private List<EscrowAllocation> UpdateEscrowAllocations(List<EscrowAllocation>? existing, List<OrderSubOrder> subOrders, OrderSubOrder updatedSubOrder, string? paymentReference)
        {
            var normalized = NormalizeEscrow(existing, subOrders);
            var index = normalized.FindIndex(e =>
                string.Equals(e.SubOrderNumber, updatedSubOrder.SubOrderNumber, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.SellerId, updatedSubOrder.SellerId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                normalized.Add(new EscrowAllocation(
                    updatedSubOrder.SubOrderNumber,
                    updatedSubOrder.SellerId,
                    Math.Max(0, updatedSubOrder.GrandTotal),
                    0,
                    Math.Max(0, updatedSubOrder.GrandTotal),
                    0,
                    0,
                    IsPayoutEligible(updatedSubOrder.Status),
                    new List<EscrowLedgerEntry>(),
                    _escrowOptions.DefaultPayoutSchedule,
                    PayoutStatuses.Scheduled));
                index = normalized.Count - 1;
            }

            var allocation = normalized[index];
            var ledger = allocation.Ledger ?? new List<EscrowLedgerEntry>();
            var now = DateTimeOffset.UtcNow;
            var payoutEligible = allocation.PayoutEligible;
            var holdAmount = Math.Max(0, updatedSubOrder.GrandTotal);
            var refundAmount = Math.Max(0, updatedSubOrder.RefundedAmount);
            if (refundAmount <= 0 && ShouldReleaseToBuyer(updatedSubOrder.Status))
            {
                refundAmount = holdAmount;
            }

            refundAmount = Math.Min(refundAmount, holdAmount);
            var commission = _commissionCalculator.CalculateForOrderItems(updatedSubOrder.Items);
            var releaseDelta = Math.Max(0, refundAmount - allocation.ReleasedToBuyer);

            if (IsPayoutEligible(updatedSubOrder.Status) && !allocation.PayoutEligible)
            {
                payoutEligible = true;
                ledger.Add(new EscrowLedgerEntry(
                    updatedSubOrder.SubOrderNumber,
                    updatedSubOrder.SellerId,
                    EscrowEntryTypes.PayoutEligible,
                    Math.Max(0, holdAmount - refundAmount - commission),
                    $"Status {OrderStatuses.Normalize(updatedSubOrder.Status)} allows payout",
                    now,
                    paymentReference));
            }

            if (releaseDelta > 0)
            {
                ledger.Add(new EscrowLedgerEntry(
                    updatedSubOrder.SubOrderNumber,
                    updatedSubOrder.SellerId,
                    EscrowEntryTypes.ReleaseToBuyer,
                    releaseDelta,
                    $"Released after {OrderStatuses.Normalize(updatedSubOrder.Status)}",
                    now,
                    paymentReference));
                allocation = allocation with
                {
                    ReleasedToBuyer = allocation.ReleasedToBuyer + releaseDelta
                };
            }

            var sellerPayout = _commissionCalculator.Round(Math.Max(0, holdAmount - allocation.ReleasedToBuyer - commission));
            if (sellerPayout <= 0)
            {
                payoutEligible = false;
            }

            normalized[index] = allocation with
            {
                HeldAmount = holdAmount,
                CommissionAmount = commission,
                SellerPayoutAmount = sellerPayout,
                Ledger = ledger,
                PayoutEligible = payoutEligible
            };

            return normalized;
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

        private List<EscrowAllocation> NormalizeEscrow(List<EscrowAllocation>? escrow, List<OrderSubOrder> subOrders)
        {
            var normalized = new List<EscrowAllocation>();
            var now = DateTimeOffset.UtcNow;

            foreach (var subOrder in subOrders)
            {
                var allocation = escrow?.FirstOrDefault(e =>
                    string.Equals(e.SubOrderNumber, subOrder.SubOrderNumber, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(e.SellerId, subOrder.SellerId, StringComparison.OrdinalIgnoreCase));

                if (allocation == null)
                {
                    normalized.Add(new EscrowAllocation(
                        subOrder.SubOrderNumber,
                        subOrder.SellerId,
                    Math.Max(0, subOrder.GrandTotal),
                    0,
                    Math.Max(0, subOrder.GrandTotal),
                    0,
                    0,
                    IsPayoutEligible(subOrder.Status),
                    new List<EscrowLedgerEntry>(),
                    _escrowOptions.DefaultPayoutSchedule,
                    PayoutStatuses.Scheduled));
                    continue;
                }

                var ledger = (allocation.Ledger ?? new List<EscrowLedgerEntry>())
                    .Select(entry => NormalizeLedgerEntry(entry, subOrder, now))
                    .ToList();

                normalized.Add(allocation with
                {
                    SubOrderNumber = string.IsNullOrWhiteSpace(allocation.SubOrderNumber) ? subOrder.SubOrderNumber : allocation.SubOrderNumber,
                    SellerId = string.IsNullOrWhiteSpace(allocation.SellerId) ? subOrder.SellerId : allocation.SellerId,
                    HeldAmount = Math.Max(0, allocation.HeldAmount),
                    CommissionAmount = _commissionCalculator.Round(Math.Max(0, allocation.CommissionAmount)),
                    SellerPayoutAmount = _commissionCalculator.Round(Math.Max(0, allocation.SellerPayoutAmount)),
                    ReleasedToBuyer = Math.Max(0, allocation.ReleasedToBuyer),
                    ReleasedToSeller = Math.Max(0, allocation.ReleasedToSeller),
                    PayoutEligible = allocation.PayoutEligible,
                    Ledger = ledger,
                    PayoutSchedule = PayoutSchedules.IsValid(allocation.PayoutSchedule) ? allocation.PayoutSchedule : PayoutSchedules.Weekly,
                    PayoutStatus = PayoutStatuses.Normalize(allocation.PayoutStatus),
                    PayoutErrorReference = string.IsNullOrWhiteSpace(allocation.PayoutErrorReference) ? null : allocation.PayoutErrorReference.Trim()
                });
            }

            return normalized;
        }

        private static EscrowLedgerEntry NormalizeLedgerEntry(EscrowLedgerEntry entry, OrderSubOrder subOrder, DateTimeOffset fallbackTimestamp)
        {
            var subOrderNumber = string.IsNullOrWhiteSpace(entry.SubOrderNumber) ? subOrder.SubOrderNumber : entry.SubOrderNumber;
            var sellerId = string.IsNullOrWhiteSpace(entry.SellerId) ? subOrder.SellerId : entry.SellerId;
            var type = string.IsNullOrWhiteSpace(entry.Type) ? EscrowEntryTypes.Hold : entry.Type.Trim();
            var amount = Math.Max(0, entry.Amount);
            var note = string.IsNullOrWhiteSpace(entry.Note) ? null : entry.Note.Trim();
            var recordedOn = entry.RecordedOn == DateTimeOffset.MinValue ? fallbackTimestamp : entry.RecordedOn;
            var reference = string.IsNullOrWhiteSpace(entry.Reference) ? null : entry.Reference.Trim();

            return new EscrowLedgerEntry(subOrderNumber, sellerId, type, amount, note, recordedOn, reference);
        }

        private bool IsPayoutEligible(string? status)
        {
            if (_escrowOptions?.PayoutEligibleStatuses == null || _escrowOptions.PayoutEligibleStatuses.Count == 0)
            {
                return false;
            }

            var normalized = OrderStatuses.Normalize(status);
            return _escrowOptions.PayoutEligibleStatuses
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(OrderStatuses.Normalize)
                .Any(s => string.Equals(s, normalized, StringComparison.OrdinalIgnoreCase));
        }

        private static bool ShouldReleaseToBuyer(string? status)
        {
            var normalized = OrderStatuses.Normalize(status);
            return string.Equals(normalized, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, OrderStatuses.Refunded, StringComparison.OrdinalIgnoreCase);
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

            return new OrderDetailsPayload(new List<OrderItemDetail>(), new List<OrderShippingDetail>(), 0, 0, null, new List<OrderSubOrder>(), new List<EscrowAllocation>());
        }

        private OrderDetailsPayload NormalizeDetails(OrderDetailsPayload details)
        {
            var normalizedItems = (details.Items ?? new List<OrderItemDetail>())
                .Select(i => NormalizeItem(i, OrderStatuses.Paid))
                .ToList();
            var normalizedShipping = details.Shipping ?? new List<OrderShippingDetail>();
            var normalizedSubOrders = (details.SubOrders ?? new List<OrderSubOrder>())
                .Select(NormalizeSubOrder)
                .ToList();
            var normalizedEscrow = NormalizeEscrow(details.Escrow, normalizedSubOrders);
            var normalizedPaymentStatus = PaymentStatuses.Normalize(details.PaymentStatus);
            var paymentRefunded = Math.Max(0, details.PaymentRefundedAmount);
            if (paymentRefunded <= 0 && normalizedSubOrders.Count > 0)
            {
                paymentRefunded = normalizedSubOrders.Sum(s => Math.Max(0, s.RefundedAmount));
            }

            if (paymentRefunded > 0 && !string.Equals(normalizedPaymentStatus, PaymentStatuses.Refunded, StringComparison.OrdinalIgnoreCase))
            {
                normalizedPaymentStatus = PaymentStatuses.Refunded;
            }

            var paymentMessage = string.IsNullOrWhiteSpace(details.PaymentStatusMessage)
                ? PaymentStatusMapper.BuildBuyerMessage(normalizedPaymentStatus)
                : details.PaymentStatusMessage!.Trim();

            return details with
            {
                Items = normalizedItems,
                Shipping = normalizedShipping,
                SubOrders = normalizedSubOrders,
                Escrow = normalizedEscrow,
                PaymentStatus = normalizedPaymentStatus,
                PaymentStatusMessage = paymentMessage,
                PaymentRefundedAmount = paymentRefunded
            };
        }

        private static OrderDetailsPayload ApplyStatusToDetails(OrderDetailsPayload details, string status)
        {
            var normalizedStatus = OrderStatuses.Normalize(status);
            var updatedSubOrders = (details.SubOrders ?? new List<OrderSubOrder>())
                .Select(sub =>
                {
                    var updatedItems = (sub.Items ?? new List<OrderItemDetail>())
                        .Select(i => i with { Status = normalizedStatus })
                        .ToList();
                    return sub with { Status = normalizedStatus, Items = updatedItems };
                })
                .ToList();

            var updatedItems = updatedSubOrders.SelectMany(s => s.Items).ToList();
            return details with
            {
                SubOrders = updatedSubOrders,
                Items = updatedItems
            };
        }

        private static bool ShouldApplyFulfillmentStatus(string currentStatus, string targetStatus, List<OrderSubOrder> subOrders)
        {
            var normalizedCurrent = OrderStatuses.Normalize(currentStatus);
            var normalizedTarget = OrderStatuses.Normalize(targetStatus);
            if (string.Equals(normalizedCurrent, normalizedTarget, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!OrderStatuses.CanTransition(normalizedCurrent, normalizedTarget))
            {
                return false;
            }

            return subOrders.All(s =>
            {
                var status = OrderStatuses.Normalize(s.Status);
                return string.Equals(status, OrderStatuses.New, StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, OrderStatuses.Failed, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static string ResolvePaymentStatusForSubOrder(OrderDetailsPayload details, OrderSubOrder subOrder)
        {
            if (subOrder == null)
            {
                return PaymentStatuses.Paid;
            }

            if (subOrder.RefundedAmount > 0 || string.Equals(subOrder.Status, OrderStatuses.Refunded, StringComparison.OrdinalIgnoreCase))
            {
                return PaymentStatuses.Refunded;
            }

            var baseStatus = PaymentStatuses.Normalize(details.PaymentStatus);
            if (string.Equals(baseStatus, PaymentStatuses.Refunded, StringComparison.OrdinalIgnoreCase))
            {
                return PaymentStatuses.Refunded;
            }

            if (string.Equals(baseStatus, PaymentStatuses.Failed, StringComparison.OrdinalIgnoreCase))
            {
                return PaymentStatuses.Failed;
            }

            if (string.Equals(baseStatus, PaymentStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            {
                return PaymentStatuses.Pending;
            }

            return PaymentStatuses.Paid;
        }

        private static DateTimeOffset ResolvePayoutDate(EscrowAllocation allocation, DateTimeOffset fallback)
        {
            var ledgerDate = allocation.Ledger?
                .Where(l => string.Equals(l.Type, EscrowEntryTypes.PayoutEligible, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(l => l.RecordedOn)
                .Select(l => l.RecordedOn)
                .FirstOrDefault(d => d != DateTimeOffset.MinValue);

            if (ledgerDate.HasValue && ledgerDate.Value != default)
            {
                return ledgerDate.Value;
            }

            return fallback;
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
                Status = string.IsNullOrWhiteSpace(derivedStatus) ? status : derivedStatus,
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

            var normalizedCategory = string.IsNullOrWhiteSpace(item.Category) ? string.Empty : item.Category.Trim();
            decimal? rate = item.CommissionRate;
            if (rate.HasValue)
            {
                var clamped = rate.Value < 0 ? 0 : rate.Value;
                rate = clamped > 1 ? 1 : clamped;
            }

            return item with
            {
                Status = string.IsNullOrWhiteSpace(normalizedStatus) ? OrderStatuses.Paid : normalizedStatus,
                Category = normalizedCategory,
                CommissionRate = rate
            };
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

            if (statuses.All(s => string.Equals(s, OrderStatuses.Failed, StringComparison.OrdinalIgnoreCase)))
            {
                return OrderStatuses.Failed;
            }

            if (statuses.Any(s => string.Equals(s, OrderStatuses.Failed, StringComparison.OrdinalIgnoreCase)))
            {
                return OrderStatuses.Failed;
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

            if (statuses.All(s => string.Equals(s, OrderStatuses.Failed, StringComparison.OrdinalIgnoreCase)))
            {
                return OrderStatuses.Failed;
            }

            if (statuses.Any(s => string.Equals(s, OrderStatuses.Failed, StringComparison.OrdinalIgnoreCase)))
            {
                return OrderStatuses.Failed;
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
