using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
        public const string PendingSellerReview = "Pending seller review";
        public const string PendingBuyerInfo = "Pending buyer info";
        public const string SellerProposed = "Seller proposed";
        public const string UnderAdminReview = "Under admin review";
        public const string Requested = "Requested";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Completed = "Completed";

        private static readonly IReadOnlyList<string> OrderedStatuses = new[]
        {
            PendingSellerReview, PendingBuyerInfo, SellerProposed, UnderAdminReview, Requested, Approved, Rejected, Completed
        };

        public static string Normalize(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return PendingSellerReview;
            }

            if (status.Trim().Equals(Requested, StringComparison.OrdinalIgnoreCase))
            {
                return PendingSellerReview;
            }

            var match = OrderedStatuses.FirstOrDefault(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));
            return match ?? status.Trim();
        }

        public static bool IsOpen(string? status)
        {
            var normalized = Normalize(status);
            return !string.Equals(normalized, Completed, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(normalized, Rejected, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static class ReturnRequestTypes
    {
        public const string Return = "Return";
        public const string Complaint = "Complaint";

        private static readonly IReadOnlyList<string> AllowedTypes = new[] { Return, Complaint };

        public static string Normalize(string? type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return Return;
            }

            var match = AllowedTypes.FirstOrDefault(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase));
            return match ?? Return;
        }

        public static bool IsSupported(string? type)
        {
            return AllowedTypes.Any(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static class ReturnPolicies
    {
        public const int ReturnWindowDays = 14;
        public static readonly TimeSpan ReturnWindow = TimeSpan.FromDays(ReturnWindowDays);
    }

    public static class ReviewStatuses
    {
        public const string Pending = "Pending";
        public const string Published = "Published";
        public const string Rejected = "Rejected";
        public const string Hidden = "Hidden";

        private static readonly IReadOnlyList<string> Ordered = new[] { Pending, Published, Rejected, Hidden };

        public static string Normalize(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return Pending;
            }

            var match = Ordered.FirstOrDefault(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));
            return match ?? Pending;
        }

        public static bool IsVisible(string? status)
        {
            return string.Equals(Normalize(status), Published, StringComparison.OrdinalIgnoreCase);
        }
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

        public string? SavedAddressKey { get; set; }

        public string DeliveryAddressJson { get; set; } = string.Empty;

        public string DetailsJson { get; set; } = string.Empty;
    }

    public class ProductReview
    {
        public int Id { get; set; }

        public int OrderId { get; set; }

        public int ProductId { get; set; }

        public string BuyerId { get; set; } = string.Empty;

        public string BuyerName { get; set; } = string.Empty;

        public int Rating { get; set; }

        public string Comment { get; set; } = string.Empty;

        public DateTimeOffset CreatedOn { get; set; }

        public string Status { get; set; } = ReviewStatuses.Pending;

        public bool IsFlagged { get; set; }

        public string? FlagReason { get; set; }

        public string? LastModeratedBy { get; set; }

        public DateTimeOffset? LastModeratedOn { get; set; }
    }

    public class ProductReviewAudit
    {
        public int Id { get; set; }

        public int ReviewId { get; set; }

        public string Action { get; set; } = string.Empty;

        public string? Actor { get; set; }

        public string? Reason { get; set; }

        public string? FromStatus { get; set; }

        public string? ToStatus { get; set; }

        public DateTimeOffset CreatedOn { get; set; }
    }

    public class SellerRating
    {
        public int Id { get; set; }

        public int OrderId { get; set; }

        public string SellerId { get; set; } = string.Empty;

        public string BuyerId { get; set; } = string.Empty;

        public int Rating { get; set; }

        public DateTimeOffset CreatedOn { get; set; }
    }

    public record SellerRatingSummary(double? AverageRating, int RatedOrderCount);

    public record OrderItemDetail(int ProductId, string Name, string Variant, int Quantity, decimal UnitPrice, decimal LineTotal, string SellerId, string SellerName, string Status = OrderStatuses.Paid, string Category = "", decimal? CommissionRate = null);

    public record OrderShippingDetail(string SellerId, string SellerName, string MethodId, string MethodLabel, decimal Cost, string? Description, string? DeliveryEstimate = null, string? ProviderId = null, string? ProviderServiceCode = null);

    public record ReturnRequestItem(int ProductId, int Quantity);

    public record ReturnRequestHistoryEntry(string Status, string Actor, DateTimeOffset ChangedOn, string? Note = null);

    public record ReturnRequestMessage(string Actor, string Message, DateTimeOffset SentOn);

    public record ReturnRequest(
        string SubOrderNumber,
        string Status,
        string Reason,
        DateTimeOffset RequestedOn,
        List<ReturnRequestItem> Items,
        string Type = ReturnRequestTypes.Return,
        string? Description = null,
        string? CaseId = null,
        List<ReturnRequestHistoryEntry>? History = null,
        List<ReturnRequestMessage>? Messages = null,
        string? ResolutionOutcome = null,
        string? ResolutionNote = null,
        decimal? ResolutionRefundAmount = null,
        string? ResolutionRefundReference = null,
        string? ResolutionRefundStatus = null,
        DateTimeOffset? ResolvedOn = null,
        string? ResolutionActor = null,
        DateTimeOffset? FirstResponseDueOn = null,
        DateTimeOffset? ResolutionDueOn = null,
        DateTimeOffset? FirstRespondedOn = null,
        bool SlaBreached = false,
        DateTimeOffset? SlaBreachedOn = null);

    public record OrderStatusChange(string Status, DateTimeOffset ChangedOn, string? TrackingNumber = null, string? TrackingCarrier = null);

    public record ShippingLabelInfo(string FileName, string ContentType, string Base64Content, DateTimeOffset CreatedOn, DateTimeOffset? ExpiresOn = null);

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
        ReturnRequest? Return = null,
        List<OrderStatusChange>? StatusHistory = null,
        string? ShippingProviderId = null,
        string? ShippingProviderService = null,
        string? ShippingProviderReference = null,
        string? TrackingUrl = null,
        ShippingLabelInfo? ShippingLabel = null);

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

    public record SellerRatingResult(bool Success, string? Error = null, SellerRating? Rating = null, double? AverageRating = null);

    public record ProductReviewView(int ProductId, int Rating, string Comment, string BuyerName, DateTimeOffset CreatedOn);
    public record ProductReviewsPage(
        IReadOnlyList<ProductReviewView> Reviews,
        int TotalCount,
        int PageNumber,
        int PageSize,
        string Sort,
        double? AverageRating);

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
        string? PaymentStatusMessage,
        ReturnRequest? ReturnRequest,
        EscrowAllocation? Escrow,
        List<OrderStatusChange> StatusHistory,
        bool HasShippingLabel,
        string? ShippingLabelFileName,
        DateTimeOffset? ShippingLabelExpiresOn);

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

    public record ProductReviewResult(bool Success, string? Error, ProductReview? Review = null);

    public record ReviewModerationResult(bool Success, string? Error);

    public record ReviewModerationItem(
        int Id,
        int ProductId,
        int OrderId,
        string BuyerName,
        int Rating,
        string Comment,
        string Status,
        bool IsFlagged,
        string? FlagReason,
        DateTimeOffset CreatedOn,
        DateTimeOffset? LastModeratedOn,
        string? LastModeratedBy);

    public record ReviewAuditView(
        int Id,
        string Action,
        string? Actor,
        string? Reason,
        string? FromStatus,
        string? ToStatus,
        DateTimeOffset CreatedOn);

    public class ReviewModerationFilters
    {
        public List<string> Statuses { get; init; } = new();

        public bool? FlaggedOnly { get; init; }

        public string? Query { get; init; }
    }

    public record CaseMessageView(string Actor, string Message, DateTimeOffset SentOn);

    public record BuyerCaseSummaryView(
        string CaseId,
        int OrderId,
        string OrderNumber,
        string SubOrderNumber,
        string SellerName,
        string Type,
        string Status,
        DateTimeOffset RequestedOn,
        DateTimeOffset LastUpdatedOn,
        decimal RefundedAmount,
        string PaymentStatus,
        string? PaymentReference);

    public record BuyerCaseItemView(string Label, int ProductId, int Quantity);

    public record CaseResolutionView(string Outcome, string Summary, decimal RefundedAmount, decimal SubOrderTotal, string PaymentStatus, string? PaymentReference, string? DecisionNote = null, DateTimeOffset? ResolvedOn = null);

    public record BuyerCaseDetailView(
        BuyerCaseSummaryView Summary,
        string Reason,
        string? Description,
        List<BuyerCaseItemView> Items,
        CaseResolutionView Resolution,
        List<OrderStatusChange> StatusHistory,
        List<CaseMessageView> Messages);

    public record SellerCaseSummaryView(
        string CaseId,
        int OrderId,
        string OrderNumber,
        string SubOrderNumber,
        string BuyerName,
        string BuyerEmail,
        string Type,
        string Status,
        DateTimeOffset RequestedOn,
        DateTimeOffset LastUpdatedOn);

    public record SellerCaseDetailView(
        SellerCaseSummaryView Summary,
        string Reason,
        string? Description,
        List<BuyerCaseItemView> Items,
        DeliveryAddress Address,
        OrderShippingDetail Shipping,
        string PaymentStatus,
        string? PaymentReference,
        decimal RefundedAmount,
        List<OrderStatusChange> StatusHistory,
        List<ReturnRequestHistoryEntry> History,
        List<CaseMessageView> Messages,
        CaseResolutionView Resolution);

    public record AdminCaseSummaryView(
        string CaseId,
        int OrderId,
        string OrderNumber,
        string SubOrderNumber,
        string SellerId,
        string SellerName,
        string BuyerName,
        string BuyerEmail,
        string Type,
        string Status,
        DateTimeOffset RequestedOn,
        DateTimeOffset LastUpdatedOn,
        DateTimeOffset? FirstResponseDueOn,
        DateTimeOffset? ResolutionDueOn,
        DateTimeOffset? FirstRespondedOn,
        bool IsSlaBreached);

    public record AdminCaseDetailView(
        AdminCaseSummaryView Summary,
        string Reason,
        string? Description,
        List<BuyerCaseItemView> Items,
        DeliveryAddress Address,
        OrderShippingDetail Shipping,
        string PaymentStatus,
        string? PaymentReference,
        decimal RefundedAmount,
        List<OrderStatusChange> StatusHistory,
        List<ReturnRequestHistoryEntry> History,
        List<CaseMessageView> Messages,
        CaseResolutionView Resolution);

    public record SellerSlaMetricsView(
        string SellerId,
        string SellerName,
        int TotalCases,
        int ResolvedCases,
        int ResolvedWithinSla,
        double ResolutionSlaRate,
        TimeSpan? AverageFirstResponseTime);

    public record ReturnCaseFilterOptions
    {
        public List<string> Statuses { get; init; } = new();

        public DateTimeOffset? FromDate { get; init; }

        public DateTimeOffset? ToDate { get; init; }
    }

    public record AdminCaseFilterOptions
    {
        public List<string> Statuses { get; init; } = new();

        public DateTimeOffset? FromDate { get; init; }

        public DateTimeOffset? ToDate { get; init; }

        public string? Query { get; init; }

        public string? Type { get; init; }
    }

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

        public bool MissingTrackingOnly { get; init; }
    }

    public record SellerPayoutFilterOptions
    {
        public List<string> Statuses { get; init; } = new();

        public DateTimeOffset? FromDate { get; init; }

        public DateTimeOffset? ToDate { get; init; }
    }

    public record SellerMonthlySettlementLine(
        int OrderId,
        string OrderNumber,
        string SubOrderNumber,
        DateTimeOffset PayoutOn,
        DateTimeOffset CreatedOn,
        decimal GrossTotal,
        decimal CommissionTotal,
        decimal PayoutTotal,
        string PayoutStatus,
        bool IsAdjustment);

    public record SellerMonthlySettlementSummary(
        string SellerId,
        string SellerName,
        int Year,
        int Month,
        DateTimeOffset PeriodStart,
        DateTimeOffset PeriodEnd,
        int OrderCount,
        decimal GrossTotal,
        decimal CommissionTotal,
        decimal PayoutTotal,
        int AdjustmentCount,
        decimal AdjustmentTotal);

    public record SellerMonthlySettlementDetail(SellerMonthlySettlementSummary Summary, List<SellerMonthlySettlementLine> Orders);

    public record SellerOrderExportResult(byte[] Content, int RowCount, int TotalMatching, bool Truncated);

    public static class InvoiceStatuses
    {
        public const string Issued = "Issued";
        public const string Pending = "Pending";
        public const string Paid = "Paid";
        public const string Blocked = "Blocked";
        public const string Draft = "Draft";

        private static readonly IReadOnlyList<string> OrderedStatuses = new[]
        {
            Draft, Issued, Pending, Paid, Blocked
        };

        public static string Normalize(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return Issued;
            }

            var match = OrderedStatuses.FirstOrDefault(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));
            return match ?? status.Trim();
        }
    }

    public record CommissionInvoiceLine(string OrderNumber, string SubOrderNumber, DateTimeOffset PayoutOn, decimal CommissionAmount, string PayoutStatus, bool IsCorrection);

    public record CommissionInvoiceSummaryView(
        string InvoiceNumber,
        int Year,
        int Month,
        DateTimeOffset PeriodStart,
        DateTimeOffset PeriodEnd,
        DateTimeOffset IssuedOn,
        decimal NetAmount,
        decimal TaxAmount,
        decimal TotalAmount,
        string Status,
        bool HasCorrections,
        bool IsCreditNote,
        string Currency,
        decimal TaxRate);

    public record CommissionInvoiceDocument(
        CommissionInvoiceSummaryView Summary,
        string SellerId,
        string SellerName,
        string? SellerTaxId,
        string? SellerAddress,
        string IssuerName,
        string IssuerTaxId,
        string IssuerAddress,
        string TaxLabel,
        List<CommissionInvoiceLine> Lines);

    public record CommissionInvoicePdf(byte[] Content, string FileName);

    public record ShippingLabelFile(byte[] Content, string ContentType, string FileName);

    public record SellerFilterOption(string Id, string Name);

    public class OrderService
    {
        private const int SellerExportRowLimit = 5000;

        private readonly ApplicationDbContext _dbContext;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<OrderService> _logger;
        private readonly EscrowOptions _escrowOptions;
        private readonly CartOptions _cartOptions;
        private readonly SettlementOptions _settlementOptions;
        private readonly TimeZoneInfo _settlementTimeZone;
        private readonly InvoiceOptions _invoiceOptions;
        private readonly CaseSlaOptions _caseSlaOptions;
        private readonly CommissionCalculator _commissionCalculator;
        private readonly ShippingProviderService _shippingProviderService;
        private readonly JsonSerializerOptions _serializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        private static readonly string[] ReviewFlagKeywords = new[] { "fraud", "scam", "abuse", "spam", "fake" };

        private record SellerOrderMatch(OrderRecord Order, OrderSubOrder SubOrder, DeliveryAddress Address, string Status);

        public OrderService(
            ApplicationDbContext dbContext,
            IEmailSender emailSender,
            ILogger<OrderService> logger,
            EscrowOptions? escrowOptions = null,
            CartOptions? cartOptions = null,
            SettlementOptions? settlementOptions = null,
            InvoiceOptions? invoiceOptions = null,
            ShippingProviderService? shippingProviderService = null,
            CaseSlaOptions? caseSlaOptions = null)
        {
            _dbContext = dbContext;
            _emailSender = emailSender;
            _logger = logger;
            _escrowOptions = escrowOptions ?? new EscrowOptions();
            _cartOptions = cartOptions ?? new CartOptions();
            _settlementOptions = settlementOptions ?? new SettlementOptions();
            _settlementTimeZone = ResolveSettlementTimeZone(_settlementOptions.TimeZone);
            _invoiceOptions = invoiceOptions ?? new InvoiceOptions();
            _caseSlaOptions = caseSlaOptions ?? new CaseSlaOptions();
            _commissionCalculator = new CommissionCalculator(_cartOptions);
            _shippingProviderService = shippingProviderService ?? new ShippingProviderService(new ShippingProviderOptions(), TimeProvider.System, NullLogger<ShippingProviderService>.Instance);
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
                SavedAddressKey = string.IsNullOrWhiteSpace(state.SavedAddressKey) ? null : state.SavedAddressKey.Trim(),
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
            var targetRefund = Math.Max(details.PaymentRefundedAmount, paymentRefundedAmount);
            var hasChanges = false;
            var updatedDetails = ApplyPaymentRefund(details, targetRefund, paymentReference ?? order.PaymentReference, out var refundChanged);
            hasChanges |= refundChanged;

            var paymentRefunded = Math.Max(updatedDetails.PaymentRefundedAmount, targetRefund);
            if (paymentRefunded > 0 && !string.Equals(normalizedPaymentStatus, PaymentStatuses.Refunded, StringComparison.OrdinalIgnoreCase))
            {
                normalizedPaymentStatus = PaymentStatuses.Refunded;
            }

            if (!string.Equals(updatedDetails.PaymentStatus, normalizedPaymentStatus, StringComparison.OrdinalIgnoreCase)
                || paymentRefunded > updatedDetails.PaymentRefundedAmount
                || !string.Equals(updatedDetails.PaymentStatusMessage ?? string.Empty, normalizedMessage, StringComparison.Ordinal))
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
                hasChanges = true;
            }

            var recalculatedStatus = CalculateOrderStatus(updatedDetails.SubOrders, normalizedInitialStatus);
            if (!string.Equals(order.Status, recalculatedStatus, StringComparison.OrdinalIgnoreCase))
            {
                order.Status = recalculatedStatus;
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

        public async Task<SellerRatingResult> SubmitSellerRatingAsync(
            int orderId,
            string sellerId,
            string? buyerId,
            int rating,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(buyerId))
            {
                return new SellerRatingResult(false, "Buyer is required.");
            }

            if (string.IsNullOrWhiteSpace(sellerId))
            {
                return new SellerRatingResult(false, "Seller is required.");
            }

            if (rating < 1 || rating > 5)
            {
                return new SellerRatingResult(false, "Rating must be between 1 and 5.");
            }

            var normalizedSellerId = sellerId.Trim();
            var order = await _dbContext.Orders.FirstOrDefaultAsync(
                o => o.Id == orderId && o.BuyerId == buyerId,
                cancellationToken);
            if (order == null)
            {
                return new SellerRatingResult(false, "Order not found.");
            }

            var details = DeserializeDetails(order.DetailsJson);
            var orderStatus = OrderStatuses.Normalize(CalculateOrderStatus(details.SubOrders, order.Status));
            if (!string.Equals(orderStatus, OrderStatuses.Delivered, StringComparison.OrdinalIgnoreCase))
            {
                return new SellerRatingResult(false, "Ratings are available after delivery.");
            }

            var subOrder = details.SubOrders.FirstOrDefault(s => string.Equals(s.SellerId, normalizedSellerId, StringComparison.OrdinalIgnoreCase));
            if (subOrder == null)
            {
                return new SellerRatingResult(false, "Seller not found in this order.");
            }

            if (!string.Equals(OrderStatuses.Normalize(subOrder.Status), OrderStatuses.Delivered, StringComparison.OrdinalIgnoreCase))
            {
                return new SellerRatingResult(false, "Ratings are available after delivery.");
            }

            var existing = await _dbContext.SellerRatings.FirstOrDefaultAsync(
                r => r.OrderId == orderId && r.SellerId == normalizedSellerId && r.BuyerId == buyerId,
                cancellationToken);
            if (existing != null)
            {
                return new SellerRatingResult(false, "You already rated this seller for this order.");
            }

            var ratingRecord = new SellerRating
            {
                OrderId = order.Id,
                SellerId = normalizedSellerId,
                BuyerId = buyerId,
                Rating = rating,
                CreatedOn = DateTimeOffset.UtcNow
            };

            _dbContext.SellerRatings.Add(ratingRecord);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var average = await _dbContext.SellerRatings.AsNoTracking()
                .Where(r => r.SellerId == normalizedSellerId)
                .Select(r => (double?)r.Rating)
                .AverageAsync(cancellationToken);

            return new SellerRatingResult(true, null, ratingRecord, average);
        }

        public async Task<ProductReviewResult> SubmitProductReviewAsync(
            int orderId,
            int productId,
            string? buyerId,
            string? buyerName,
            int rating,
            string? comment,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(buyerId))
            {
                return new ProductReviewResult(false, "Buyer is required.");
            }

            if (productId <= 0)
            {
                return new ProductReviewResult(false, "Select a product to review.");
            }

            if (rating < 1 || rating > 5)
            {
                return new ProductReviewResult(false, "Rating must be between 1 and 5.");
            }

            var normalizedComment = string.IsNullOrWhiteSpace(comment) ? string.Empty : comment.Trim();
            if (normalizedComment.Length == 0)
            {
                return new ProductReviewResult(false, "Provide feedback for your review.");
            }

            if (normalizedComment.Length > 2000)
            {
                normalizedComment = normalizedComment[..2000];
            }

            var order = await _dbContext.Orders.FirstOrDefaultAsync(
                o => o.Id == orderId && o.BuyerId == buyerId,
                cancellationToken);
            if (order == null)
            {
                return new ProductReviewResult(false, "Order not found.");
            }

            var details = DeserializeDetails(order.DetailsJson);
            var orderStatus = OrderStatuses.Normalize(CalculateOrderStatus(details.SubOrders, order.Status));
            if (!string.Equals(orderStatus, OrderStatuses.Delivered, StringComparison.OrdinalIgnoreCase))
            {
                return new ProductReviewResult(false, "Reviews are available after delivery.");
            }

            var purchasedItem = details.Items.FirstOrDefault(i => i.ProductId == productId);
            if (purchasedItem == null)
            {
                return new ProductReviewResult(false, "Product not found in this order.");
            }

            var subOrder = details.SubOrders.FirstOrDefault(s => s.Items.Any(i => i.ProductId == productId));
            if (subOrder != null && !string.Equals(OrderStatuses.Normalize(subOrder.Status), OrderStatuses.Delivered, StringComparison.OrdinalIgnoreCase))
            {
                return new ProductReviewResult(false, "Reviews are available after delivery.");
            }

            var existing = await _dbContext.ProductReviews.FirstOrDefaultAsync(
                r => r.OrderId == orderId && r.ProductId == productId && r.BuyerId == buyerId,
                cancellationToken);
            if (existing != null)
            {
                return new ProductReviewResult(false, "You already reviewed this product for this order.");
            }

            var now = DateTimeOffset.UtcNow;
            var recent = await _dbContext.ProductReviews
                .Where(r => r.BuyerId == buyerId)
                .OrderByDescending(r => r.CreatedOn)
                .FirstOrDefaultAsync(cancellationToken);
            if (recent != null && now - recent.CreatedOn < TimeSpan.FromMinutes(1))
            {
                return new ProductReviewResult(false, "Please wait before submitting another review.");
            }

            var reviewerName = string.IsNullOrWhiteSpace(buyerName)
                ? order.BuyerName
                : buyerName.Trim();
            var moderation = EvaluateReviewForModeration(normalizedComment, rating);
            var review = new ProductReview
            {
                OrderId = order.Id,
                ProductId = productId,
                BuyerId = buyerId,
                BuyerName = string.IsNullOrWhiteSpace(reviewerName) ? "Buyer" : reviewerName,
                Rating = rating,
                Comment = normalizedComment,
                CreatedOn = now,
                Status = moderation.Flagged ? ReviewStatuses.Pending : ReviewStatuses.Published,
                IsFlagged = moderation.Flagged,
                FlagReason = moderation.Reason,
                LastModeratedBy = moderation.Flagged ? "System" : null,
                LastModeratedOn = moderation.Flagged ? now : null
            };

            _dbContext.ProductReviews.Add(review);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (moderation.Flagged)
            {
                AddReviewAudit(review, "Flagged", "System", moderation.Reason, ReviewStatuses.Published, review.Status, now);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return new ProductReviewResult(true, null, review);
        }

        public async Task<List<ProductReviewView>> GetPublishedReviewsAsync(
            int productId,
            int take = 20,
            CancellationToken cancellationToken = default)
        {
            var page = await GetPublishedReviewsPageAsync(productId, 1, take, "newest", cancellationToken);
            return page.Reviews.ToList();
        }

        public async Task<ProductReviewsPage> GetPublishedReviewsPageAsync(
            int productId,
            int page = 1,
            int pageSize = 10,
            string? sort = null,
            CancellationToken cancellationToken = default)
        {
            if (productId <= 0)
            {
                var normalizedPageSize = pageSize <= 0 ? 10 : Math.Min(pageSize, 50);
                var normalizedSort = NormalizeReviewSort(sort);
                return new ProductReviewsPage(Array.Empty<ProductReviewView>(), 0, 1, normalizedPageSize, normalizedSort, null);
            }

            var normalizedSortOption = NormalizeReviewSort(sort);
            var limit = pageSize <= 0 ? 10 : Math.Min(pageSize, 50);
            var source = _dbContext.ProductReviews.AsNoTracking()
                .Where(r => r.ProductId == productId && ReviewStatuses.IsVisible(r.Status));

            var totalCount = await source.CountAsync(cancellationToken);
            var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)limit);
            var pageNumber = Math.Clamp(page, 1, totalPages);

            double? averageRating = null;
            if (totalCount > 0)
            {
                var average = await source.AverageAsync(r => (double)r.Rating, cancellationToken);
                averageRating = Math.Round(average, 1);
            }

            source = normalizedSortOption switch
            {
                "highest" => source.OrderByDescending(r => r.Rating).ThenByDescending(r => r.CreatedOn),
                "lowest" => source.OrderBy(r => r.Rating).ThenByDescending(r => r.CreatedOn),
                _ => source.OrderByDescending(r => r.CreatedOn)
            };

            var reviews = await source
                .Skip((pageNumber - 1) * limit)
                .Take(limit)
                .ToListAsync(cancellationToken);

            var views = reviews
                .Select(r => new ProductReviewView(r.ProductId, r.Rating, r.Comment, r.BuyerName, r.CreatedOn))
                .ToList();

            return new ProductReviewsPage(views, totalCount, pageNumber, limit, normalizedSortOption, averageRating);
        }

        private static string NormalizeReviewSort(string? sort)
        {
            return sort?.Trim().ToLowerInvariant() switch
            {
                "highest" or "rating_desc" => "highest",
                "lowest" or "rating_asc" => "lowest",
                _ => "newest"
            };
        }

        public async Task<ReviewModerationResult> FlagReviewAsync(
            int reviewId,
            string actor,
            string? reason = null,
            CancellationToken cancellationToken = default)
        {
            var review = await _dbContext.ProductReviews.FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken);
            if (review == null)
            {
                return new ReviewModerationResult(false, "Review not found.");
            }

            var normalizedActor = NormalizeActor(actor);
            var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "Flagged for review" : reason.Trim();
            var previousStatus = ReviewStatuses.Normalize(review.Status);
            var now = DateTimeOffset.UtcNow;

            review.IsFlagged = true;
            review.FlagReason = normalizedReason;
            review.LastModeratedBy = normalizedActor;
            review.LastModeratedOn = now;
            if (ReviewStatuses.IsVisible(review.Status))
            {
                review.Status = ReviewStatuses.Pending;
            }

            AddReviewAudit(review, "Flagged", normalizedActor, normalizedReason, previousStatus, review.Status, now);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new ReviewModerationResult(true, null);
        }

        public async Task<ReviewModerationResult> ApproveReviewAsync(
            int reviewId,
            string actor,
            string? note = null,
            CancellationToken cancellationToken = default)
        {
            var review = await _dbContext.ProductReviews.FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken);
            if (review == null)
            {
                return new ReviewModerationResult(false, "Review not found.");
            }

            var normalizedActor = NormalizeActor(actor);
            var normalizedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            var previousStatus = ReviewStatuses.Normalize(review.Status);
            var now = DateTimeOffset.UtcNow;

            review.Status = ReviewStatuses.Published;
            review.IsFlagged = false;
            review.FlagReason = normalizedNote ?? review.FlagReason;
            review.LastModeratedBy = normalizedActor;
            review.LastModeratedOn = now;

            AddReviewAudit(review, "Approved", normalizedActor, normalizedNote, previousStatus, review.Status, now);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new ReviewModerationResult(true, null);
        }

        public async Task<ReviewModerationResult> RejectReviewAsync(
            int reviewId,
            string actor,
            string? note = null,
            CancellationToken cancellationToken = default)
        {
            var review = await _dbContext.ProductReviews.FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken);
            if (review == null)
            {
                return new ReviewModerationResult(false, "Review not found.");
            }

            var normalizedActor = NormalizeActor(actor);
            var normalizedNote = string.IsNullOrWhiteSpace(note) ? "Rejected by moderator" : note.Trim();
            var previousStatus = ReviewStatuses.Normalize(review.Status);
            var now = DateTimeOffset.UtcNow;

            review.Status = ReviewStatuses.Rejected;
            review.IsFlagged = true;
            review.FlagReason = normalizedNote;
            review.LastModeratedBy = normalizedActor;
            review.LastModeratedOn = now;

            AddReviewAudit(review, "Rejected", normalizedActor, normalizedNote, previousStatus, review.Status, now);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new ReviewModerationResult(true, null);
        }

        public async Task<ReviewModerationResult> UpdateReviewVisibilityAsync(
            int reviewId,
            string actor,
            bool isVisible,
            string? note = null,
            CancellationToken cancellationToken = default)
        {
            var review = await _dbContext.ProductReviews.FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken);
            if (review == null)
            {
                return new ReviewModerationResult(false, "Review not found.");
            }

            var normalizedActor = NormalizeActor(actor);
            var normalizedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            var previousStatus = ReviewStatuses.Normalize(review.Status);
            var now = DateTimeOffset.UtcNow;
            var targetStatus = isVisible ? ReviewStatuses.Published : ReviewStatuses.Hidden;

            review.Status = targetStatus;
            review.IsFlagged = !isVisible;
            review.FlagReason = normalizedNote ?? review.FlagReason;
            review.LastModeratedBy = normalizedActor;
            review.LastModeratedOn = now;

            AddReviewAudit(review, "VisibilityUpdated", normalizedActor, normalizedNote, previousStatus, review.Status, now);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new ReviewModerationResult(true, null);
        }

        public async Task<PagedResult<ReviewModerationItem>> GetReviewsForModerationAsync(
            ReviewModerationFilters? filters = null,
            int page = 1,
            int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            var normalizedFilters = NormalizeReviewFilters(filters);
            var limit = pageSize <= 0 ? 20 : Math.Min(50, pageSize);
            var source = _dbContext.ProductReviews.AsNoTracking().AsQueryable();

            if (normalizedFilters.FlaggedOnly == true)
            {
                source = source.Where(r => r.IsFlagged);
            }

            if (normalizedFilters.Statuses.Count > 0)
            {
                source = source.Where(r => normalizedFilters.Statuses.Contains(r.Status));
            }

            if (!string.IsNullOrWhiteSpace(normalizedFilters.Query))
            {
                var query = normalizedFilters.Query;
                source = source.Where(r =>
                    r.Comment.Contains(query) ||
                    r.BuyerName.Contains(query) ||
                    (r.FlagReason != null && r.FlagReason.Contains(query)));
            }

            var totalCount = await source.CountAsync(cancellationToken);
            var totalPages = limit <= 0 ? 0 : (int)Math.Ceiling(totalCount / (double)limit);
            var pageNumber = totalPages == 0 ? 1 : Math.Clamp(page, 1, totalPages);

            var items = await source
                .OrderByDescending(r => r.IsFlagged)
                .ThenByDescending(r => r.CreatedOn)
                .Skip((pageNumber - 1) * limit)
                .Take(limit)
                .Select(r => new ReviewModerationItem(
                    r.Id,
                    r.ProductId,
                    r.OrderId,
                    r.BuyerName,
                    r.Rating,
                    r.Comment,
                    r.Status,
                    r.IsFlagged,
                    r.FlagReason,
                    r.CreatedOn,
                    r.LastModeratedOn,
                    r.LastModeratedBy))
                .ToListAsync(cancellationToken);

            return new PagedResult<ReviewModerationItem>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = limit
            };
        }

        public async Task<List<ReviewAuditView>> GetReviewAuditAsync(int reviewId, CancellationToken cancellationToken = default)
        {
            var audits = await _dbContext.ProductReviewAudits.AsNoTracking()
                .Where(a => a.ReviewId == reviewId)
                .OrderByDescending(a => a.CreatedOn)
                .Take(50)
                .ToListAsync(cancellationToken);

            return audits
                .Select(a => new ReviewAuditView(a.Id, a.Action, a.Actor, a.Reason, a.FromStatus, a.ToStatus, a.CreatedOn))
                .ToList();
        }

        private static (bool Flagged, string? Reason) EvaluateReviewForModeration(string comment, int rating)
        {
            var lowered = comment.ToLowerInvariant();
            foreach (var keyword in ReviewFlagKeywords)
            {
                if (lowered.Contains(keyword))
                {
                    return (true, $"Contains flagged keyword '{keyword}'");
                }
            }

            if (lowered.Contains("http://") || lowered.Contains("https://"))
            {
                return (true, "Contains external link");
            }

            if (rating <= 2 && lowered.Contains("refund"))
            {
                return (true, "Low rating mentioning refund");
            }

            return (false, null);
        }

        private static ReviewModerationFilters NormalizeReviewFilters(ReviewModerationFilters? filters)
        {
            var normalizedStatuses = filters?.Statuses?
                .Select(ReviewStatuses.Normalize)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            return new ReviewModerationFilters
            {
                Statuses = normalizedStatuses,
                FlaggedOnly = filters?.FlaggedOnly,
                Query = string.IsNullOrWhiteSpace(filters?.Query) ? null : filters!.Query!.Trim()
            };
        }

        private static string NormalizeActor(string? actor)
        {
            return string.IsNullOrWhiteSpace(actor) ? "System" : actor.Trim();
        }

        private void AddReviewAudit(
            ProductReview review,
            string action,
            string? actor,
            string? reason,
            string? fromStatus,
            string? toStatus,
            DateTimeOffset? createdOn = null)
        {
            var entry = new ProductReviewAudit
            {
                ReviewId = review.Id,
                Action = string.IsNullOrWhiteSpace(action) ? "Updated" : action.Trim(),
                Actor = NormalizeActor(actor),
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                FromStatus = string.IsNullOrWhiteSpace(fromStatus) ? null : ReviewStatuses.Normalize(fromStatus),
                ToStatus = string.IsNullOrWhiteSpace(toStatus) ? null : ReviewStatuses.Normalize(toStatus),
                CreatedOn = createdOn ?? DateTimeOffset.UtcNow
            };

            _dbContext.ProductReviewAudits.Add(entry);
        }

        public async Task<Dictionary<string, int>> GetSellerRatingsForOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(buyerId))
            {
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }

            var ratings = await _dbContext.SellerRatings.AsNoTracking()
                .Where(r => r.OrderId == orderId && r.BuyerId == buyerId)
                .ToListAsync(cancellationToken);

            return ratings.ToDictionary(r => r.SellerId, r => r.Rating, StringComparer.OrdinalIgnoreCase);
        }

        public async Task<SellerRatingSummary> GetSellerRatingSummaryAsync(string sellerId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sellerId))
            {
                return new SellerRatingSummary(null, 0);
            }

            var normalizedSellerId = sellerId.Trim();
            var ratings = _dbContext.SellerRatings.AsNoTracking()
                .Where(r => r.SellerId == normalizedSellerId);

            var count = await ratings.CountAsync(cancellationToken);
            if (count == 0)
            {
                return new SellerRatingSummary(null, 0);
            }

            var average = await ratings.Select(r => (double?)r.Rating).AverageAsync(cancellationToken);
            return new SellerRatingSummary(average.HasValue ? Math.Round(average.Value, 1) : null, count);
        }

        public async Task<double?> GetSellerRatingScoreAsync(string sellerId, CancellationToken cancellationToken = default)
        {
            var summary = await GetSellerRatingSummaryAsync(sellerId, cancellationToken);
            return summary.AverageRating;
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

            var targetPaymentStatus = PaymentStatusMapper.MapProviderStatus(providerStatus);
            if (!string.IsNullOrWhiteSpace(providerMessage))
            {
                var level = string.Equals(targetPaymentStatus, PaymentStatuses.Failed, StringComparison.OrdinalIgnoreCase)
                    ? LogLevel.Warning
                    : LogLevel.Information;
                _logger.Log(level, "Payment webhook status {Status} for {Reference}: {Message}", providerStatus, reference, providerMessage);
            }

            var buyerMessage = string.IsNullOrWhiteSpace(providerMessage)
                ? PaymentStatusMapper.BuildBuyerMessage(targetPaymentStatus)
                : providerMessage!.Trim();
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
            string? type = null,
            string? description = null,
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
                return new ReturnRequestResult(false, "Provide a reason for the request.");
            }

            if (string.IsNullOrWhiteSpace(type) || !ReturnRequestTypes.IsSupported(type))
            {
                return new ReturnRequestResult(false, "Select return or complaint.");
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                return new ReturnRequestResult(false, "Provide a description of the issue.");
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
                return new ReturnRequestResult(false, "Requests are only available after delivery.");
            }

            if (subOrder.Return != null && ReturnRequestStatuses.IsOpen(subOrder.Return.Status))
            {
                return new ReturnRequestResult(false, "An open case already exists for this sub-order.");
            }

            var normalizedType = ReturnRequestTypes.Normalize(type);
            if (string.Equals(normalizedType, ReturnRequestTypes.Return, StringComparison.OrdinalIgnoreCase)
                && !IsReturnWindowOpen(subOrder, order.CreatedOn))
            {
                return new ReturnRequestResult(false, $"Returns are only available within {ReturnPolicies.ReturnWindowDays} days of delivery.");
            }

            var items = BuildReturnItems(subOrder, productIds);
            if (items.Count == 0)
            {
                return new ReturnRequestResult(false, "Select at least one item.");
            }

            var requestedOn = DateTimeOffset.UtcNow;
            var normalizedDescription = description.Trim();
            var history = new List<ReturnRequestHistoryEntry>
            {
                new(ReturnRequestStatuses.PendingSellerReview, "Buyer", requestedOn, "Case opened")
            };
            var request = new ReturnRequest(
                subOrder.SubOrderNumber,
                ReturnRequestStatuses.PendingSellerReview,
                reason.Trim(),
                requestedOn,
                items,
                normalizedType,
                normalizedDescription,
                BuildCaseId(subOrder.SubOrderNumber, requestedOn),
                history);
            request = ApplySlaTracking(request, subOrder.Items, requestedOn);
            details.SubOrders[subOrderIndex] = subOrder with { Return = request };
            order.DetailsJson = JsonSerializer.Serialize(details, _serializerOptions);

            await _dbContext.SaveChangesAsync(cancellationToken);
            return new ReturnRequestResult(true, null, request);
        }

        public async Task<PagedResult<BuyerCaseSummaryView>> GetReturnCasesForBuyerAsync(
            string buyerId,
            ReturnCaseFilterOptions? filters = null,
            int pageNumber = 1,
            int pageSize = 10,
            CancellationToken cancellationToken = default)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Clamp(pageSize, 1, 50);

            var normalizedBuyer = buyerId?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedBuyer))
            {
                return new PagedResult<BuyerCaseSummaryView>
                {
                    Items = new List<BuyerCaseSummaryView>(),
                    TotalCount = 0,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
            }

            var orders = await _dbContext.Orders.AsNoTracking()
                .Where(o => o.BuyerId == normalizedBuyer)
                .OrderByDescending(o => o.CreatedOn)
                .ToListAsync(cancellationToken);

            var normalizedFilters = NormalizeReturnCaseFilters(filters);
            var cases = new List<BuyerCaseSummaryView>();
            foreach (var order in orders)
            {
                var details = DeserializeDetails(order.DetailsJson);
                foreach (var subOrder in details.SubOrders)
                {
                    if (subOrder.Return == null)
                    {
                        continue;
                    }

                    var normalizedRequest = NormalizeReturnRequest(subOrder.Return, subOrder.Items);
                    if (normalizedRequest == null || !MatchesReturnFilters(normalizedRequest, normalizedFilters))
                    {
                        continue;
                    }

                    cases.Add(BuildBuyerCaseSummary(order, subOrder, normalizedRequest, details.PaymentStatus));
                }
            }

            var totalCount = cases.Count;
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
            var items = cases
                .OrderByDescending(c => c.LastUpdatedOn)
                .Skip(skip)
                .Take(pageSize)
                .ToList();

            return new PagedResult<BuyerCaseSummaryView>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<BuyerCaseDetailView?> GetReturnCaseForBuyerAsync(
            string buyerId,
            string caseId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(buyerId) || string.IsNullOrWhiteSpace(caseId))
            {
                return null;
            }

            var normalizedBuyer = buyerId.Trim();
            var normalizedCase = caseId.Trim();
            var order = await _dbContext.Orders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.BuyerId == normalizedBuyer && o.DetailsJson.Contains(normalizedCase), cancellationToken);
            if (order == null)
            {
                return null;
            }

            var details = DeserializeDetails(order.DetailsJson);
            var subOrder = details.SubOrders.FirstOrDefault(s =>
                s.Return != null
                && string.Equals(s.Return.CaseId, normalizedCase, StringComparison.OrdinalIgnoreCase))
                ?? details.SubOrders.FirstOrDefault(s =>
                    s.Return != null
                    && string.Equals(s.SubOrderNumber, normalizedCase, StringComparison.OrdinalIgnoreCase));

            if (subOrder?.Return == null)
            {
                return null;
            }

            var normalizedRequest = NormalizeReturnRequest(subOrder.Return, subOrder.Items);
            if (normalizedRequest == null)
            {
                return null;
            }

            var summary = BuildBuyerCaseSummary(order, subOrder, normalizedRequest, details.PaymentStatus);
            var items = BuildBuyerCaseItems(subOrder, normalizedRequest);
            var resolution = BuildCaseResolution(subOrder, normalizedRequest, details.PaymentStatus, order.PaymentReference);
            var history = subOrder.StatusHistory?.OrderByDescending(h => h.ChangedOn).ToList() ?? new List<OrderStatusChange>();
            var messages = normalizedRequest.Messages?
                .OrderBy(m => m.SentOn)
                .Select(m => new CaseMessageView(m.Actor, m.Message, m.SentOn))
                .ToList() ?? new List<CaseMessageView>();

            return new BuyerCaseDetailView(
                summary,
                normalizedRequest.Reason,
                normalizedRequest.Description,
                items,
                resolution,
                history,
                messages);
        }

        public async Task<PagedResult<SellerCaseSummaryView>> GetReturnCasesForSellerAsync(
            string sellerId,
            ReturnCaseFilterOptions? filters = null,
            int pageNumber = 1,
            int pageSize = 10,
            CancellationToken cancellationToken = default)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Clamp(pageSize, 1, 50);

            var normalizedSeller = sellerId?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedSeller))
            {
                return new PagedResult<SellerCaseSummaryView>
                {
                    Items = new List<SellerCaseSummaryView>(),
                    TotalCount = 0,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
            }

            var sellerToken = $"\"sellerId\":\"{normalizedSeller}\"";
            var orders = await _dbContext.Orders.AsNoTracking()
                .Where(o => o.DetailsJson.Contains(sellerToken))
                .OrderByDescending(o => o.CreatedOn)
                .ToListAsync(cancellationToken);

            var normalizedFilters = NormalizeReturnCaseFilters(filters);
            var cases = new List<SellerCaseSummaryView>();
            foreach (var order in orders)
            {
                var details = DeserializeDetails(order.DetailsJson);
                var buyerName = string.IsNullOrWhiteSpace(order.BuyerName) ? "Buyer" : order.BuyerName;
                foreach (var subOrder in details.SubOrders.Where(s => string.Equals(s.SellerId, normalizedSeller, StringComparison.OrdinalIgnoreCase) && s.Return != null))
                {
                    if (subOrder.Return == null)
                    {
                        continue;
                    }

                    var normalizedRequest = NormalizeReturnRequest(subOrder.Return, subOrder.Items);
                    if (normalizedRequest == null || !MatchesReturnFilters(normalizedRequest, normalizedFilters))
                    {
                        continue;
                    }

                    var lastUpdated = CalculateCaseLastUpdated(normalizedRequest);
                    cases.Add(new SellerCaseSummaryView(
                        string.IsNullOrWhiteSpace(normalizedRequest.CaseId) ? BuildCaseId(subOrder.SubOrderNumber, normalizedRequest.RequestedOn) : normalizedRequest.CaseId!,
                        order.Id,
                        order.OrderNumber,
                        subOrder.SubOrderNumber,
                        buyerName,
                        order.BuyerEmail ?? string.Empty,
                        normalizedRequest.Type,
                        ReturnRequestStatuses.Normalize(normalizedRequest.Status),
                        normalizedRequest.RequestedOn,
                        lastUpdated));
                }
            }

            var totalCount = cases.Count;
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
            var items = cases
                .OrderByDescending(c => c.LastUpdatedOn)
                .Skip(skip)
                .Take(pageSize)
                .ToList();

            return new PagedResult<SellerCaseSummaryView>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<SellerCaseDetailView?> GetReturnCaseForSellerAsync(
            string sellerId,
            string caseId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sellerId) || string.IsNullOrWhiteSpace(caseId))
            {
                return null;
            }

            var normalizedSeller = sellerId.Trim();
            var normalizedCase = caseId.Trim();
            var sellerToken = $"\"sellerId\":\"{normalizedSeller}\"";
            var order = await _dbContext.Orders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.DetailsJson.Contains(normalizedCase) && o.DetailsJson.Contains(sellerToken), cancellationToken);
            if (order == null)
            {
                return null;
            }

            var details = DeserializeDetails(order.DetailsJson);
            var subOrder = details.SubOrders.FirstOrDefault(s =>
                string.Equals(s.SellerId, normalizedSeller, StringComparison.OrdinalIgnoreCase)
                && s.Return != null
                && string.Equals(s.Return.CaseId, normalizedCase, StringComparison.OrdinalIgnoreCase))
                ?? details.SubOrders.FirstOrDefault(s =>
                    string.Equals(s.SellerId, normalizedSeller, StringComparison.OrdinalIgnoreCase)
                    && s.Return != null
                    && string.Equals(s.SubOrderNumber, normalizedCase, StringComparison.OrdinalIgnoreCase));

            if (subOrder?.Return == null)
            {
                return null;
            }

            var normalizedRequest = NormalizeReturnRequest(subOrder.Return, subOrder.Items);
            if (normalizedRequest == null)
            {
                return null;
            }

            var buyerName = string.IsNullOrWhiteSpace(order.BuyerName) ? "Buyer" : order.BuyerName;
            var lastUpdated = CalculateCaseLastUpdated(normalizedRequest);
            var summary = new SellerCaseSummaryView(
                string.IsNullOrWhiteSpace(normalizedRequest.CaseId) ? BuildCaseId(subOrder.SubOrderNumber, normalizedRequest.RequestedOn) : normalizedRequest.CaseId!,
                order.Id,
                order.OrderNumber,
                subOrder.SubOrderNumber,
                buyerName,
                order.BuyerEmail ?? string.Empty,
                normalizedRequest.Type,
                ReturnRequestStatuses.Normalize(normalizedRequest.Status),
                normalizedRequest.RequestedOn,
                lastUpdated);

            var items = BuildBuyerCaseItems(subOrder, normalizedRequest);
            var address = DeserializeAddress(order.DeliveryAddressJson);
            var statusHistory = subOrder.StatusHistory?.OrderByDescending(h => h.ChangedOn).ToList() ?? new List<OrderStatusChange>();
            var paymentStatus = PaymentStatuses.Normalize(details.PaymentStatus);
            var paymentReference = string.IsNullOrWhiteSpace(order.PaymentReference) ? null : order.PaymentReference.Trim();
            var resolution = BuildCaseResolution(subOrder, normalizedRequest, paymentStatus, paymentReference);
            var history = normalizedRequest.History?.OrderByDescending(h => h.ChangedOn).ToList() ?? new List<ReturnRequestHistoryEntry>();
            var messages = normalizedRequest.Messages?
                .OrderBy(m => m.SentOn)
                .Select(m => new CaseMessageView(m.Actor, m.Message, m.SentOn))
                .ToList() ?? new List<CaseMessageView>();

            return new SellerCaseDetailView(
                summary,
                normalizedRequest.Reason,
                normalizedRequest.Description,
                items,
                address,
                subOrder.ShippingDetail,
                paymentStatus,
                paymentReference,
                Math.Max(0, subOrder.RefundedAmount),
                statusHistory,
                history,
                messages,
                resolution);
        }

        public async Task<PagedResult<AdminCaseSummaryView>> GetReturnCasesForAdminAsync(
            AdminCaseFilterOptions? filters = null,
            int pageNumber = 1,
            int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Clamp(pageSize, 1, 50);

            var orders = await _dbContext.Orders.AsNoTracking()
                .OrderByDescending(o => o.CreatedOn)
                .ToListAsync(cancellationToken);

            var normalizedFilters = NormalizeAdminCaseFilters(filters);
            var cases = new List<AdminCaseSummaryView>();
            foreach (var order in orders)
            {
                var details = DeserializeDetails(order.DetailsJson);
                var buyerName = string.IsNullOrWhiteSpace(order.BuyerName) ? "Buyer" : order.BuyerName;
                foreach (var subOrder in details.SubOrders.Where(s => s.Return != null))
                {
                    var normalizedRequest = NormalizeReturnRequest(subOrder.Return, subOrder.Items);
                    if (normalizedRequest == null || !MatchesAdminCaseFilters(order, subOrder, normalizedRequest, normalizedFilters))
                    {
                        continue;
                    }

                    var sellerName = string.IsNullOrWhiteSpace(subOrder.SellerName) ? subOrder.SellerId : subOrder.SellerName;
                    cases.Add(new AdminCaseSummaryView(
                        string.IsNullOrWhiteSpace(normalizedRequest.CaseId) ? BuildCaseId(subOrder.SubOrderNumber, normalizedRequest.RequestedOn) : normalizedRequest.CaseId!,
                        order.Id,
                        order.OrderNumber,
                        subOrder.SubOrderNumber,
                        subOrder.SellerId,
                        sellerName,
                        buyerName,
                        order.BuyerEmail ?? string.Empty,
                        normalizedRequest.Type,
                        ReturnRequestStatuses.Normalize(normalizedRequest.Status),
                        normalizedRequest.RequestedOn,
                        CalculateCaseLastUpdated(normalizedRequest),
                        normalizedRequest.FirstResponseDueOn,
                        normalizedRequest.ResolutionDueOn,
                        normalizedRequest.FirstRespondedOn,
                        normalizedRequest.SlaBreached || normalizedRequest.SlaBreachedOn.HasValue));
                }
            }

            var totalCount = cases.Count;
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
            var items = cases
                .OrderByDescending(c => c.LastUpdatedOn)
                .Skip(skip)
                .Take(pageSize)
                .ToList();

            return new PagedResult<AdminCaseSummaryView>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<AdminCaseDetailView?> GetReturnCaseForAdminAsync(
            string caseId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(caseId))
            {
                return null;
            }

            var normalizedCase = caseId.Trim();
            var order = await _dbContext.Orders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.DetailsJson.Contains(normalizedCase), cancellationToken);
            if (order == null)
            {
                return null;
            }

            var details = DeserializeDetails(order.DetailsJson);
            var subOrder = details.SubOrders.FirstOrDefault(s =>
                s.Return != null
                && (string.Equals(s.Return.CaseId, normalizedCase, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.SubOrderNumber, normalizedCase, StringComparison.OrdinalIgnoreCase)));

            if (subOrder?.Return == null)
            {
                return null;
            }

            var normalizedRequest = NormalizeReturnRequest(subOrder.Return, subOrder.Items);
            if (normalizedRequest == null)
            {
                return null;
            }

            var buyerName = string.IsNullOrWhiteSpace(order.BuyerName) ? "Buyer" : order.BuyerName;
            var sellerName = string.IsNullOrWhiteSpace(subOrder.SellerName) ? subOrder.SellerId : subOrder.SellerName;
            var summary = new AdminCaseSummaryView(
                string.IsNullOrWhiteSpace(normalizedRequest.CaseId) ? BuildCaseId(subOrder.SubOrderNumber, normalizedRequest.RequestedOn) : normalizedRequest.CaseId!,
                order.Id,
                order.OrderNumber,
                subOrder.SubOrderNumber,
                subOrder.SellerId,
                sellerName,
                buyerName,
                order.BuyerEmail ?? string.Empty,
                normalizedRequest.Type,
                ReturnRequestStatuses.Normalize(normalizedRequest.Status),
                normalizedRequest.RequestedOn,
                CalculateCaseLastUpdated(normalizedRequest),
                normalizedRequest.FirstResponseDueOn,
                normalizedRequest.ResolutionDueOn,
                normalizedRequest.FirstRespondedOn,
                normalizedRequest.SlaBreached || normalizedRequest.SlaBreachedOn.HasValue);

            var items = BuildBuyerCaseItems(subOrder, normalizedRequest);
            var address = DeserializeAddress(order.DeliveryAddressJson);
            var statusHistory = subOrder.StatusHistory?.OrderByDescending(h => h.ChangedOn).ToList() ?? new List<OrderStatusChange>();
            var paymentStatus = PaymentStatuses.Normalize(details.PaymentStatus);
            var paymentReference = string.IsNullOrWhiteSpace(order.PaymentReference) ? null : order.PaymentReference.Trim();
            var resolution = BuildCaseResolution(subOrder, normalizedRequest, paymentStatus, paymentReference);
            var history = normalizedRequest.History?.OrderByDescending(h => h.ChangedOn).ToList() ?? new List<ReturnRequestHistoryEntry>();
            var messages = normalizedRequest.Messages?
                .OrderBy(m => m.SentOn)
                .Select(m => new CaseMessageView(m.Actor, m.Message, m.SentOn))
                .ToList() ?? new List<CaseMessageView>();

            return new AdminCaseDetailView(
                summary,
                normalizedRequest.Reason,
                normalizedRequest.Description,
                items,
                address,
                subOrder.ShippingDetail,
                paymentStatus,
                paymentReference,
                Math.Max(0, subOrder.RefundedAmount),
                statusHistory,
                history,
                messages,
                resolution);
        }

        public async Task<List<SellerSlaMetricsView>> GetSellerSlaMetricsAsync(
            DateTimeOffset? fromDate = null,
            DateTimeOffset? toDate = null,
            CancellationToken cancellationToken = default)
        {
            var normalizedFrom = fromDate;
            var normalizedTo = toDate;
            if (normalizedFrom.HasValue && normalizedTo.HasValue && normalizedFrom > normalizedTo)
            {
                (normalizedFrom, normalizedTo) = (normalizedTo, normalizedFrom);
            }

            var orders = await _dbContext.Orders.AsNoTracking()
                .OrderByDescending(o => o.CreatedOn)
                .ToListAsync(cancellationToken);

            var metrics = new Dictionary<string, (string SellerName, int Total, int Resolved, int ResolvedWithinSla, List<TimeSpan> Responses)>(StringComparer.OrdinalIgnoreCase);

            foreach (var order in orders)
            {
                var details = DeserializeDetails(order.DetailsJson);
                foreach (var subOrder in details.SubOrders.Where(s => s.Return != null))
                {
                    var normalizedRequest = NormalizeReturnRequest(subOrder.Return, subOrder.Items);
                    if (normalizedRequest == null)
                    {
                        continue;
                    }

                    if (normalizedFrom.HasValue && normalizedRequest.RequestedOn < normalizedFrom.Value)
                    {
                        continue;
                    }

                    if (normalizedTo.HasValue && normalizedRequest.RequestedOn > normalizedTo.Value)
                    {
                        continue;
                    }

                    var sellerName = string.IsNullOrWhiteSpace(subOrder.SellerName) ? subOrder.SellerId : subOrder.SellerName;
                    if (!metrics.TryGetValue(subOrder.SellerId, out var entry))
                    {
                        entry = (sellerName, 0, 0, 0, new List<TimeSpan>());
                    }

                    entry.Total += 1;
                    if (normalizedRequest.FirstRespondedOn.HasValue && normalizedRequest.FirstRespondedOn.Value >= normalizedRequest.RequestedOn)
                    {
                        entry.Responses.Add(normalizedRequest.FirstRespondedOn.Value - normalizedRequest.RequestedOn);
                    }

                    if (!ReturnRequestStatuses.IsOpen(normalizedRequest.Status))
                    {
                        entry.Resolved += 1;
                        var resolutionDue = normalizedRequest.ResolutionDueOn
                            ?? (_caseSlaOptions.Enabled
                                ? normalizedRequest.RequestedOn.AddHours(_caseSlaOptions.DefaultResolutionHours)
                                : (DateTimeOffset?)null);
                        var resolvedOn = normalizedRequest.ResolvedOn ?? normalizedRequest.History?.LastOrDefault()?.ChangedOn;
                        if (resolutionDue.HasValue && resolvedOn.HasValue && resolvedOn.Value <= resolutionDue.Value)
                        {
                            entry.ResolvedWithinSla += 1;
                        }
                    }

                    metrics[subOrder.SellerId] = entry;
                }
            }

            var results = new List<SellerSlaMetricsView>();
            foreach (var kvp in metrics)
            {
                var responseAverage = kvp.Value.Responses.Count == 0
                    ? (TimeSpan?)null
                    : TimeSpan.FromTicks((long)kvp.Value.Responses.Average(r => r.Ticks));
                var resolutionRate = kvp.Value.Resolved == 0
                    ? 0
                    : (double)kvp.Value.ResolvedWithinSla / kvp.Value.Resolved * 100;

                results.Add(new SellerSlaMetricsView(
                    kvp.Key,
                    kvp.Value.SellerName,
                    kvp.Value.Total,
                    kvp.Value.Resolved,
                    kvp.Value.ResolvedWithinSla,
                    Math.Round(resolutionRate, 1),
                    responseAverage));
            }

            return results
                .OrderByDescending(r => r.TotalCases)
                .ThenBy(r => r.SellerName)
                .ToList();
        }

        public async Task<ReturnRequestResult> EscalateReturnCaseForAdminAsync(
            int orderId,
            string caseId,
            string? escalationSource,
            string? note = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(caseId))
            {
                return new ReturnRequestResult(false, "Case ID is required.");
            }

            var normalizedCase = caseId.Trim();
            var order = await _dbContext.Orders.FirstOrDefaultAsync(
                o => o.Id == orderId && o.DetailsJson.Contains(normalizedCase),
                cancellationToken);
            if (order == null)
            {
                return new ReturnRequestResult(false, "Case not found.");
            }

            var details = DeserializeDetails(order.DetailsJson);
            var subOrderIndex = details.SubOrders.FindIndex(s =>
                s.Return != null
                && (string.Equals(s.Return.CaseId, normalizedCase, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.SubOrderNumber, normalizedCase, StringComparison.OrdinalIgnoreCase)));
            if (subOrderIndex < 0)
            {
                return new ReturnRequestResult(false, "Case not found.");
            }

            var subOrder = details.SubOrders[subOrderIndex];
            var normalizedRequest = NormalizeReturnRequest(subOrder.Return, subOrder.Items);
            if (normalizedRequest == null)
            {
                return new ReturnRequestResult(false, "Case not found.");
            }

            if (string.Equals(ReturnRequestStatuses.Normalize(normalizedRequest.Status), ReturnRequestStatuses.UnderAdminReview, StringComparison.OrdinalIgnoreCase))
            {
                return new ReturnRequestResult(false, "Case is already under admin review.");
            }

            var source = string.IsNullOrWhiteSpace(escalationSource) ? "manual" : escalationSource.Trim().ToLowerInvariant();
            var reason = source switch
            {
                "buyer" or "buyerrequest" or "buyer-requested" => "Buyer requested escalation.",
                "sla" or "breach" or "system" => "Escalated due to SLA breach.",
                _ => "Manual admin flag for review."
            };

            var combinedNote = string.IsNullOrWhiteSpace(note) ? reason : $"{reason} {note.Trim()}";
            var now = DateTimeOffset.UtcNow;
            var updatedRequest = normalizedRequest with { Status = ReturnRequestStatuses.UnderAdminReview };
            updatedRequest = AppendReturnHistory(updatedRequest, ReturnRequestStatuses.UnderAdminReview, "Admin", combinedNote, now);
            var updatedSubOrder = subOrder with { Return = updatedRequest };
            details.SubOrders[subOrderIndex] = updatedSubOrder;

            order.DetailsJson = JsonSerializer.Serialize(details, _serializerOptions);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var sellerEmail = await GetSellerEmailAsync(updatedSubOrder.SellerId, cancellationToken);
            await SendReturnCaseUpdateEmailAsync(order, updatedSubOrder, updatedRequest, combinedNote, sellerEmail);

            return new ReturnRequestResult(true, null, updatedRequest);
        }

        public async Task<ReturnRequestResult> ResolveReturnCaseForAdminAsync(
            int orderId,
            string caseId,
            string resolution,
            decimal? refundAmount = null,
            string? refundReference = null,
            string? note = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(caseId))
            {
                return new ReturnRequestResult(false, "Case ID is required.");
            }

            if (string.IsNullOrWhiteSpace(resolution))
            {
                return new ReturnRequestResult(false, "Select a resolution.");
            }

            var normalizedCase = caseId.Trim();
            var normalizedResolution = resolution.Trim().ToLowerInvariant();
            var normalizedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            var normalizedReference = string.IsNullOrWhiteSpace(refundReference) ? null : refundReference.Trim();

            var order = await _dbContext.Orders.FirstOrDefaultAsync(o => o.Id == orderId && o.DetailsJson.Contains(normalizedCase), cancellationToken);
            if (order == null)
            {
                return new ReturnRequestResult(false, "Order not found.");
            }

            var details = DeserializeDetails(order.DetailsJson);
            var subOrderIndex = details.SubOrders.FindIndex(s =>
                s.Return != null
                && (string.Equals(s.Return.CaseId, normalizedCase, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.SubOrderNumber, normalizedCase, StringComparison.OrdinalIgnoreCase)));
            if (subOrderIndex < 0)
            {
                return new ReturnRequestResult(false, "Case not found.");
            }

            var subOrder = details.SubOrders[subOrderIndex];
            var normalizedRequest = NormalizeReturnRequest(subOrder.Return, subOrder.Items);
            if (normalizedRequest == null)
            {
                return new ReturnRequestResult(false, "Case not found.");
            }

            var targetStatus = ReturnRequestStatuses.Completed;
            var requiresRefund = false;
            var outcome = "Pending review";
            decimal? requestedRefund = null;

            switch (normalizedResolution)
            {
                case "fullrefund":
                case "refund":
                case "full_refund":
                    outcome = "Full refund";
                    requiresRefund = true;
                    requestedRefund = subOrder.GrandTotal;
                    break;
                case "partialrefund":
                case "partial_refund":
                case "partial":
                    outcome = "Partial refund";
                    requiresRefund = true;
                    requestedRefund = refundAmount;
                    break;
                case "replacement":
                    outcome = "Replacement";
                    break;
                case "repair":
                    outcome = "Repair";
                    break;
                case "norefund":
                case "no_refund":
                case "reject":
                    outcome = "No refund";
                    targetStatus = ReturnRequestStatuses.Rejected;
                    break;
                case "close":
                case "closewithoutaction":
                case "close_without_action":
                    outcome = "Closed without action";
                    targetStatus = ReturnRequestStatuses.Completed;
                    break;
                default:
                    return new ReturnRequestResult(false, "Select a valid resolution.");
            }

            var maxRefund = Math.Max(0, subOrder.GrandTotal);
            var existingRefund = Math.Max(0, subOrder.RefundedAmount);
            var resolvedOn = DateTimeOffset.UtcNow;
            decimal resolvedRefund = existingRefund;

            if (requiresRefund)
            {
                var desired = requestedRefund ?? refundAmount ?? 0;
                if (desired <= 0 && string.Equals(outcome, "Partial refund", StringComparison.OrdinalIgnoreCase))
                {
                    return new ReturnRequestResult(false, "Enter a refund amount.");
                }

                var cappedDesired = Math.Min(maxRefund, Math.Max(0, desired));
                resolvedRefund = Math.Max(existingRefund, cappedDesired);

                var refundUpdate = await UpdateSubOrderStatusAsync(
                    orderId,
                    subOrder.SellerId,
                    OrderStatuses.Refunded,
                    null,
                    resolvedRefund,
                    null,
                    null,
                    null,
                    cancellationToken);

                if (!refundUpdate.Success)
                {
                    return new ReturnRequestResult(false, refundUpdate.Error ?? "Unable to apply refund.");
                }

                order = await _dbContext.Orders.FirstOrDefaultAsync(o => o.Id == orderId && o.DetailsJson.Contains(normalizedCase), cancellationToken);
                if (order == null)
                {
                    return new ReturnRequestResult(false, "Order not found after refund update.");
                }

                details = DeserializeDetails(order.DetailsJson);
                subOrderIndex = details.SubOrders.FindIndex(s =>
                    s.Return != null
                    && (string.Equals(s.Return.CaseId, normalizedCase, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(s.SubOrderNumber, normalizedCase, StringComparison.OrdinalIgnoreCase)));
                if (subOrderIndex < 0)
                {
                    return new ReturnRequestResult(false, "Case not found.");
                }

                subOrder = details.SubOrders[subOrderIndex];
                normalizedRequest = NormalizeReturnRequest(subOrder.Return, subOrder.Items);
                if (normalizedRequest == null)
                {
                    return new ReturnRequestResult(false, "Case not found.");
                }
            }

            var paymentStatus = PaymentStatuses.Normalize(details.PaymentStatus);
            var resolutionReference = normalizedReference
                ?? normalizedRequest.ResolutionRefundReference
                ?? (string.IsNullOrWhiteSpace(order.PaymentReference) ? null : order.PaymentReference.Trim());
            var historyNote = outcome switch
            {
                "Full refund" => $"Admin enforced full refund of {resolvedRefund.ToString("C", CultureInfo.InvariantCulture)}",
                "Partial refund" => $"Admin set partial refund of {resolvedRefund.ToString("C", CultureInfo.InvariantCulture)}",
                "Replacement" => "Admin approved replacement",
                "Repair" => "Admin approved repair",
                "No refund" => "Admin declined refund",
                "Closed without action" => "Admin closed without further action",
                _ => "Admin decision recorded"
            };

            if (!string.IsNullOrWhiteSpace(normalizedNote))
            {
                historyNote = $"{historyNote}. {normalizedNote}";
            }

            var updatedRequest = normalizedRequest with
            {
                Status = ReturnRequestStatuses.Normalize(targetStatus),
                ResolutionOutcome = outcome,
                ResolutionNote = normalizedNote,
                ResolutionRefundAmount = resolvedRefund,
                ResolutionRefundReference = resolutionReference,
                ResolutionRefundStatus = requiresRefund ? paymentStatus : (normalizedRequest.ResolutionRefundStatus ?? "Not required"),
                ResolvedOn = resolvedOn,
                ResolutionActor = "Admin"
            };

            updatedRequest = AppendReturnHistory(updatedRequest, updatedRequest.Status, "Admin", historyNote, resolvedOn);
            updatedRequest = ApplySlaTracking(updatedRequest, subOrder.Items, resolvedOn, sellerResponded: true);
            var updatedSubOrder = subOrder with { Return = updatedRequest };
            details.SubOrders[subOrderIndex] = updatedSubOrder;

            order.DetailsJson = JsonSerializer.Serialize(details, _serializerOptions);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var sellerEmail = await GetSellerEmailAsync(updatedSubOrder.SellerId, cancellationToken);
            await SendReturnCaseUpdateEmailAsync(order, updatedSubOrder, updatedRequest, historyNote, sellerEmail);

            return new ReturnRequestResult(true, null, updatedRequest);
        }

        public async Task<ReturnRequestResult> AddReturnCaseMessageForBuyerAsync(
            string buyerId,
            string caseId,
            string message,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(buyerId))
            {
                return new ReturnRequestResult(false, "Buyer is required.");
            }

            if (string.IsNullOrWhiteSpace(caseId))
            {
                return new ReturnRequestResult(false, "Case ID is required.");
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return new ReturnRequestResult(false, "Enter a message.");
            }

            var normalizedBuyer = buyerId.Trim();
            var normalizedCase = caseId.Trim();
            var normalizedMessage = message.Trim();

            var order = await _dbContext.Orders.FirstOrDefaultAsync(
                o => o.BuyerId == normalizedBuyer && o.DetailsJson.Contains(normalizedCase),
                cancellationToken);
            if (order == null)
            {
                return new ReturnRequestResult(false, "Case not found.");
            }

            var details = DeserializeDetails(order.DetailsJson);
            var subOrderIndex = details.SubOrders.FindIndex(s =>
                s.Return != null
                && (string.Equals(s.Return.CaseId, normalizedCase, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.SubOrderNumber, normalizedCase, StringComparison.OrdinalIgnoreCase)));
            if (subOrderIndex < 0)
            {
                return new ReturnRequestResult(false, "Case not found.");
            }

            var subOrder = details.SubOrders[subOrderIndex];
            var normalizedRequest = NormalizeReturnRequest(subOrder.Return, subOrder.Items);
            if (normalizedRequest == null)
            {
                return new ReturnRequestResult(false, "Case not found.");
            }

            var updatedRequest = AppendReturnMessage(normalizedRequest, "Buyer", normalizedMessage, DateTimeOffset.UtcNow);
            details.SubOrders[subOrderIndex] = subOrder with { Return = updatedRequest };
            order.DetailsJson = JsonSerializer.Serialize(details, _serializerOptions);

            await _dbContext.SaveChangesAsync(cancellationToken);
            return new ReturnRequestResult(true, null, updatedRequest);
        }

        public async Task<ReturnRequestResult> AddReturnCaseMessageForSellerAsync(
            string sellerId,
            string caseId,
            string message,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sellerId))
            {
                return new ReturnRequestResult(false, "Seller is required.");
            }

            if (string.IsNullOrWhiteSpace(caseId))
            {
                return new ReturnRequestResult(false, "Case ID is required.");
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return new ReturnRequestResult(false, "Enter a message.");
            }

            var normalizedSeller = sellerId.Trim();
            var normalizedCase = caseId.Trim();
            var normalizedMessage = message.Trim();
            var sellerToken = $"\"sellerId\":\"{normalizedSeller}\"";

            var order = await _dbContext.Orders.FirstOrDefaultAsync(
                o => o.DetailsJson.Contains(normalizedCase) && o.DetailsJson.Contains(sellerToken),
                cancellationToken);
            if (order == null)
            {
                return new ReturnRequestResult(false, "Case not found.");
            }

            var details = DeserializeDetails(order.DetailsJson);
            var subOrderIndex = details.SubOrders.FindIndex(s =>
                string.Equals(s.SellerId, normalizedSeller, StringComparison.OrdinalIgnoreCase)
                && s.Return != null
                && (string.Equals(s.Return.CaseId, normalizedCase, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.SubOrderNumber, normalizedCase, StringComparison.OrdinalIgnoreCase)));
            if (subOrderIndex < 0)
            {
                return new ReturnRequestResult(false, "Case not found.");
            }

            var subOrder = details.SubOrders[subOrderIndex];
            var normalizedRequest = NormalizeReturnRequest(subOrder.Return, subOrder.Items);
            if (normalizedRequest == null)
            {
                return new ReturnRequestResult(false, "Case not found.");
            }

            var updatedRequest = AppendReturnMessage(normalizedRequest, "Seller", normalizedMessage, DateTimeOffset.UtcNow);
            updatedRequest = ApplySlaTracking(updatedRequest, subOrder.Items, DateTimeOffset.UtcNow, sellerResponded: true);
            details.SubOrders[subOrderIndex] = subOrder with { Return = updatedRequest };
            order.DetailsJson = JsonSerializer.Serialize(details, _serializerOptions);

            await _dbContext.SaveChangesAsync(cancellationToken);
            return new ReturnRequestResult(true, null, updatedRequest);
        }

        public async Task<ReturnRequestResult> UpdateReturnCaseForSellerAsync(
            int orderId,
            string sellerId,
            string caseId,
            string action,
            string? note = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sellerId))
            {
                return new ReturnRequestResult(false, "Seller is required.");
            }

            if (string.IsNullOrWhiteSpace(caseId))
            {
                return new ReturnRequestResult(false, "Case ID is required.");
            }

            if (string.IsNullOrWhiteSpace(action))
            {
                return new ReturnRequestResult(false, "Select an action.");
            }

            var normalizedSeller = sellerId.Trim();
            var normalizedCase = caseId.Trim();
            var normalizedAction = action.Trim().ToLowerInvariant();

            string targetStatus;
            string defaultNote;
            switch (normalizedAction)
            {
                case "accept":
                case "approve":
                case "acceptreturn":
                    targetStatus = ReturnRequestStatuses.Approved;
                    defaultNote = "Seller accepted the request.";
                    break;
                case "partial":
                case "propose":
                case "partialsolution":
                    targetStatus = ReturnRequestStatuses.SellerProposed;
                    defaultNote = "Seller proposed a partial solution.";
                    break;
                case "requestinfo":
                case "info":
                case "moreinfo":
                    targetStatus = ReturnRequestStatuses.PendingBuyerInfo;
                    defaultNote = "Seller requested more information.";
                    break;
                case "reject":
                    targetStatus = ReturnRequestStatuses.Rejected;
                    defaultNote = "Seller rejected the request.";
                    break;
                default:
                    return new ReturnRequestResult(false, "Select a valid action.");
            }

            var order = await _dbContext.Orders.FirstOrDefaultAsync(o => o.Id == orderId && o.DetailsJson.Contains(normalizedCase), cancellationToken);
            if (order == null)
            {
                return new ReturnRequestResult(false, "Order not found.");
            }

            var details = DeserializeDetails(order.DetailsJson);
            var subOrderIndex = details.SubOrders.FindIndex(s =>
                string.Equals(s.SellerId, normalizedSeller, StringComparison.OrdinalIgnoreCase)
                && s.Return != null
                && (string.Equals(s.Return.CaseId, normalizedCase, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.SubOrderNumber, normalizedCase, StringComparison.OrdinalIgnoreCase)));
            if (subOrderIndex < 0)
            {
                return new ReturnRequestResult(false, "Case not found for this seller.");
            }

            var subOrder = details.SubOrders[subOrderIndex];
            if (subOrder.Return == null)
            {
                return new ReturnRequestResult(false, "Case not found for this seller.");
            }

            var normalizedRequest = NormalizeReturnRequest(subOrder.Return, subOrder.Items);
            if (normalizedRequest == null)
            {
                return new ReturnRequestResult(false, "Case not found for this seller.");
            }

            if (!ReturnRequestStatuses.IsOpen(normalizedRequest.Status))
            {
                return new ReturnRequestResult(false, "Case is already closed.");
            }

            var combinedNote = string.IsNullOrWhiteSpace(note)
                ? defaultNote
                : $"{defaultNote} {note.Trim()}";
            var now = DateTimeOffset.UtcNow;
            var updatedRequest = normalizedRequest with { Status = targetStatus };
            updatedRequest = AppendReturnHistory(updatedRequest, targetStatus, "Seller", combinedNote, now);
            updatedRequest = ApplySlaTracking(updatedRequest, subOrder.Items, now, sellerResponded: true);
            var updatedSubOrder = subOrder with { Return = updatedRequest };
            details.SubOrders[subOrderIndex] = updatedSubOrder;

            order.DetailsJson = JsonSerializer.Serialize(details, _serializerOptions);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await SendReturnCaseUpdateEmailAsync(order, updatedSubOrder, updatedRequest, combinedNote);

            return new ReturnRequestResult(true, null, updatedRequest);
        }

        public async Task<ReturnRequestResult> ResolveReturnCaseForSellerAsync(
            int orderId,
            string sellerId,
            string caseId,
            string resolution,
            decimal? refundAmount = null,
            string? refundReference = null,
            string? note = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sellerId))
            {
                return new ReturnRequestResult(false, "Seller is required.");
            }

            if (string.IsNullOrWhiteSpace(caseId))
            {
                return new ReturnRequestResult(false, "Case ID is required.");
            }

            if (string.IsNullOrWhiteSpace(resolution))
            {
                return new ReturnRequestResult(false, "Select a resolution.");
            }

            var normalizedSeller = sellerId.Trim();
            var normalizedCase = caseId.Trim();
            var normalizedResolution = resolution.Trim().ToLowerInvariant();
            var normalizedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            var normalizedReference = string.IsNullOrWhiteSpace(refundReference) ? null : refundReference.Trim();

            var order = await _dbContext.Orders.FirstOrDefaultAsync(o => o.Id == orderId && o.DetailsJson.Contains(normalizedCase), cancellationToken);
            if (order == null)
            {
                return new ReturnRequestResult(false, "Order not found.");
            }

            var details = DeserializeDetails(order.DetailsJson);
            var subOrderIndex = details.SubOrders.FindIndex(s =>
                string.Equals(s.SellerId, normalizedSeller, StringComparison.OrdinalIgnoreCase)
                && s.Return != null
                && (string.Equals(s.Return.CaseId, normalizedCase, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.SubOrderNumber, normalizedCase, StringComparison.OrdinalIgnoreCase)));
            if (subOrderIndex < 0)
            {
                return new ReturnRequestResult(false, "Case not found for this seller.");
            }

            var subOrder = details.SubOrders[subOrderIndex];
            var normalizedRequest = NormalizeReturnRequest(subOrder.Return, subOrder.Items);
            if (normalizedRequest == null)
            {
                return new ReturnRequestResult(false, "Case not found for this seller.");
            }

            if (!ReturnRequestStatuses.IsOpen(normalizedRequest.Status))
            {
                return new ReturnRequestResult(false, "Case is already resolved.");
            }

            var targetStatus = ReturnRequestStatuses.Completed;
            var requiresRefund = false;
            var outcome = "Pending review";
            decimal? requestedRefund = null;

            switch (normalizedResolution)
            {
                case "fullrefund":
                case "refund":
                case "full_refund":
                    outcome = "Full refund";
                    requiresRefund = true;
                    requestedRefund = subOrder.GrandTotal;
                    break;
                case "partialrefund":
                case "partial_refund":
                case "partial":
                    outcome = "Partial refund";
                    requiresRefund = true;
                    requestedRefund = refundAmount;
                    break;
                case "replacement":
                    outcome = "Replacement";
                    targetStatus = ReturnRequestStatuses.Completed;
                    break;
                case "repair":
                    outcome = "Repair";
                    targetStatus = ReturnRequestStatuses.Completed;
                    break;
                case "norefund":
                case "no_refund":
                    outcome = "No refund";
                    targetStatus = ReturnRequestStatuses.Rejected;
                    break;
                case "reject":
                case "rejected":
                    outcome = "Rejected";
                    targetStatus = ReturnRequestStatuses.Rejected;
                    break;
                default:
                    return new ReturnRequestResult(false, "Select a valid resolution.");
            }

            var maxRefund = Math.Max(0, subOrder.GrandTotal);
            var existingRefund = Math.Max(0, subOrder.RefundedAmount);
            var resolvedOn = DateTimeOffset.UtcNow;
            decimal resolvedRefund = existingRefund;

            if (requiresRefund)
            {
                var desired = requestedRefund ?? refundAmount ?? 0;
                if (desired <= 0 && string.Equals(outcome, "Partial refund", StringComparison.OrdinalIgnoreCase))
                {
                    return new ReturnRequestResult(false, "Enter a refund amount.");
                }

                var cappedDesired = Math.Min(maxRefund, Math.Max(0, desired));
                resolvedRefund = Math.Max(existingRefund, cappedDesired);

                var refundUpdate = await UpdateSubOrderStatusAsync(
                    orderId,
                    normalizedSeller,
                    OrderStatuses.Refunded,
                    null,
                    resolvedRefund,
                    null,
                    null,
                    null,
                    cancellationToken);

                if (!refundUpdate.Success)
                {
                    return new ReturnRequestResult(false, refundUpdate.Error ?? "Unable to apply refund.");
                }

                order = await _dbContext.Orders.FirstOrDefaultAsync(o => o.Id == orderId && o.DetailsJson.Contains(normalizedCase), cancellationToken);
                if (order == null)
                {
                    return new ReturnRequestResult(false, "Order not found after refund update.");
                }

                details = DeserializeDetails(order.DetailsJson);
                subOrderIndex = details.SubOrders.FindIndex(s =>
                    string.Equals(s.SellerId, normalizedSeller, StringComparison.OrdinalIgnoreCase)
                    && s.Return != null
                    && (string.Equals(s.Return.CaseId, normalizedCase, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(s.SubOrderNumber, normalizedCase, StringComparison.OrdinalIgnoreCase)));
                if (subOrderIndex < 0)
                {
                    return new ReturnRequestResult(false, "Case not found for this seller.");
                }

                subOrder = details.SubOrders[subOrderIndex];
                normalizedRequest = NormalizeReturnRequest(subOrder.Return, subOrder.Items);
                if (normalizedRequest == null)
                {
                    return new ReturnRequestResult(false, "Case not found for this seller.");
                }
            }

            var paymentStatus = PaymentStatuses.Normalize(details.PaymentStatus);
            var resolutionReference = normalizedReference
                ?? normalizedRequest.ResolutionRefundReference
                ?? (string.IsNullOrWhiteSpace(order.PaymentReference) ? null : order.PaymentReference.Trim());
            var historyNote = outcome switch
            {
                "Full refund" => $"Resolution: full refund of {resolvedRefund.ToString("C", CultureInfo.InvariantCulture)}",
                "Partial refund" => $"Resolution: partial refund of {resolvedRefund.ToString("C", CultureInfo.InvariantCulture)}",
                "Replacement" => "Resolution: replacement will be provided",
                "Repair" => "Resolution: repair will be arranged",
                "No refund" => "Resolution: no refund provided",
                "Rejected" => "Resolution: request rejected",
                _ => "Resolution recorded"
            };

            if (!string.IsNullOrWhiteSpace(normalizedNote))
            {
                historyNote = $"{historyNote}. {normalizedNote}";
            }

            var updatedRequest = normalizedRequest with
            {
                Status = ReturnRequestStatuses.Normalize(targetStatus),
                ResolutionOutcome = outcome,
                ResolutionNote = normalizedNote,
                ResolutionRefundAmount = resolvedRefund,
                ResolutionRefundReference = resolutionReference,
                ResolutionRefundStatus = requiresRefund ? paymentStatus : (normalizedRequest.ResolutionRefundStatus ?? "Not required"),
                ResolvedOn = resolvedOn
            };

            updatedRequest = AppendReturnHistory(updatedRequest, updatedRequest.Status, "Seller", historyNote, resolvedOn);
            updatedRequest = ApplySlaTracking(updatedRequest, subOrder.Items, resolvedOn, sellerResponded: true);
            var updatedSubOrder = subOrder with { Return = updatedRequest };
            details.SubOrders[subOrderIndex] = updatedSubOrder;

            order.DetailsJson = JsonSerializer.Serialize(details, _serializerOptions);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await SendReturnCaseUpdateEmailAsync(order, updatedSubOrder, updatedRequest, historyNote);

            return new ReturnRequestResult(true, null, updatedRequest);
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

        private async Task<List<SellerOrderMatch>> GetSellerOrderMatchesAsync(
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

            var matches = new List<SellerOrderMatch>();
            foreach (var order in candidates)
            {
                var details = DeserializeDetails(order.DetailsJson);
                var subOrder = details.SubOrders.FirstOrDefault(s => string.Equals(s.SellerId, sellerId, StringComparison.OrdinalIgnoreCase));
                if (subOrder == null)
                {
                    continue;
                }

                var normalizedStatus = OrderStatuses.Normalize(subOrder.Status);
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

                if (filters?.MissingTrackingOnly == true && !string.IsNullOrWhiteSpace(subOrder.TrackingNumber))
                {
                    continue;
                }

                var address = DeserializeAddress(order.DeliveryAddressJson);
                matches.Add(new SellerOrderMatch(order, subOrder, address, normalizedStatus));
            }

            return matches;
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

        public async Task<SellerOrderExportResult?> ExportSellerOrdersAsync(
            string sellerId,
            SellerOrderFilterOptions? filters = null,
            CancellationToken cancellationToken = default)
        {
            var matches = await GetSellerOrderMatchesAsync(sellerId, filters, cancellationToken);
            var totalMatches = matches.Count;
            if (totalMatches == 0)
            {
                return null;
            }

            var ordered = matches
                .OrderByDescending(s => s.Order.CreatedOn)
                .Take(SellerExportRowLimit)
                .ToList();
            var truncated = ordered.Count < totalMatches;
            var builder = new StringBuilder();
            builder.AppendLine("SubOrder,Order,CreatedOn,Status,BuyerName,BuyerEmail,BuyerPhone,Recipient,AddressLine1,AddressLine2,City,State,PostalCode,Country,ShippingMethod,ShippingCost,TrackingNumber,TrackingCarrier,Items,TotalQuantity,GrandTotal,PaymentReference");

            foreach (var match in ordered)
            {
                var subOrder = match.SubOrder;
                var address = match.Address;
                var buyerName = string.IsNullOrWhiteSpace(match.Order.BuyerName) ? match.Order.BuyerEmail ?? string.Empty : match.Order.BuyerName;
                var shippingMethod = string.IsNullOrWhiteSpace(subOrder.ShippingDetail.MethodLabel)
                    ? subOrder.ShippingDetail.MethodId
                    : subOrder.ShippingDetail.MethodLabel;
                var items = string.Join(" | ", subOrder.Items.Select(FormatItemForExport));

                builder.AppendLine(string.Join(",", new[]
                {
                    CsvEscape(subOrder.SubOrderNumber),
                    CsvEscape(match.Order.OrderNumber),
                    CsvEscape(match.Order.CreatedOn.ToString("u", CultureInfo.InvariantCulture)),
                    CsvEscape(match.Status),
                    CsvEscape(buyerName),
                    CsvEscape(match.Order.BuyerEmail ?? string.Empty),
                    CsvEscape(address.Phone ?? string.Empty),
                    CsvEscape(address.Recipient),
                    CsvEscape(address.Line1),
                    CsvEscape(address.Line2 ?? string.Empty),
                    CsvEscape(address.City),
                    CsvEscape(address.State),
                    CsvEscape(address.PostalCode),
                    CsvEscape(address.Country),
                    CsvEscape(shippingMethod),
                    CsvEscape(subOrder.Shipping.ToString("F2", CultureInfo.InvariantCulture)),
                    CsvEscape(subOrder.TrackingNumber ?? string.Empty),
                    CsvEscape(subOrder.TrackingCarrier ?? string.Empty),
                    CsvEscape(items),
                    CsvEscape(subOrder.TotalQuantity.ToString(CultureInfo.InvariantCulture)),
                    CsvEscape(subOrder.GrandTotal.ToString("F2", CultureInfo.InvariantCulture)),
                    CsvEscape(match.Order.PaymentReference ?? string.Empty)
                }));
            }

            return new SellerOrderExportResult(Encoding.UTF8.GetBytes(builder.ToString()), ordered.Count, totalMatches, truncated);
        }

        private async Task<List<SellerOrderSummaryView>> GetSellerOrderSummariesAsync(
            string sellerId,
            SellerOrderFilterOptions? filters,
            CancellationToken cancellationToken)
        {
            var matches = await GetSellerOrderMatchesAsync(sellerId, filters, cancellationToken);
            var summaries = new List<SellerOrderSummaryView>();
            foreach (var match in matches)
            {
                var shippingMethod = string.IsNullOrWhiteSpace(match.SubOrder.ShippingDetail.MethodLabel)
                    ? match.SubOrder.ShippingDetail.MethodId
                    : match.SubOrder.ShippingDetail.MethodLabel;
                var buyerName = string.IsNullOrWhiteSpace(match.Order.BuyerName) ? match.Order.BuyerEmail ?? string.Empty : match.Order.BuyerName;

                summaries.Add(new SellerOrderSummaryView(
                    match.Order.Id,
                    match.Order.OrderNumber,
                    match.SubOrder.SubOrderNumber,
                    match.Order.CreatedOn,
                    match.Status,
                    match.SubOrder.GrandTotal,
                    match.SubOrder.TotalQuantity,
                    match.SubOrder.SellerName,
                    buyerName,
                    match.Order.BuyerEmail ?? string.Empty,
                    shippingMethod));
            }

            return summaries;
        }

        private static string FormatItemForExport(OrderItemDetail item)
        {
            var variant = string.IsNullOrWhiteSpace(item.Variant) ? string.Empty : $" ({item.Variant})";
            return $"{item.Name}{variant} x{item.Quantity}";
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

        public async Task<List<SellerMonthlySettlementSummary>> GetMonthlySettlementsAsync(
            int year,
            int month,
            string? sellerId = null,
            CancellationToken cancellationToken = default)
        {
            var (start, end) = ResolveSettlementWindow(year, month);
            var normalizedSeller = string.IsNullOrWhiteSpace(sellerId) ? null : sellerId.Trim();
            var sellerToken = normalizedSeller == null ? null : $"\"sellerId\":\"{normalizedSeller}\"";

            var ordersQuery = _dbContext.Orders.AsNoTracking().Where(o => o.CreatedOn < end);
            if (!string.IsNullOrWhiteSpace(sellerToken))
            {
                ordersQuery = ordersQuery.Where(o => o.DetailsJson.Contains(sellerToken));
            }

            var orders = await ordersQuery
                .OrderBy(o => o.CreatedOn)
                .ToListAsync(cancellationToken);

            var summaries = new Dictionary<string, (string SellerName, int Count, decimal Gross, decimal Commission, decimal Payout, int Adjustments, decimal AdjustmentTotal)>(StringComparer.OrdinalIgnoreCase);
            foreach (var order in orders)
            {
                var details = DeserializeDetails(order.DetailsJson);
                var subOrders = details.SubOrders ?? new List<OrderSubOrder>();
                var allocations = details.Escrow ?? new List<EscrowAllocation>();

                foreach (var subOrder in subOrders)
                {
                    if (normalizedSeller != null && !string.Equals(subOrder.SellerId, normalizedSeller, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var allocation = allocations.FirstOrDefault(e =>
                        string.Equals(e.SubOrderNumber, subOrder.SubOrderNumber, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(e.SellerId, subOrder.SellerId, StringComparison.OrdinalIgnoreCase));

                    var payoutOn = allocation == null ? order.CreatedOn : ResolvePayoutDate(allocation, order.CreatedOn);
                    if (payoutOn < start || payoutOn >= end)
                    {
                        continue;
                    }

                    var key = subOrder.SellerId ?? string.Empty;
                    var sellerName = string.IsNullOrWhiteSpace(subOrder.SellerName) ? key : subOrder.SellerName;
                    if (!summaries.TryGetValue(key, out var acc))
                    {
                        acc = (sellerName, 0, 0, 0, 0, 0, 0);
                    }

                    var gross = allocation?.HeldAmount ?? subOrder.GrandTotal;
                    var commission = allocation?.CommissionAmount ?? 0;
                    var payout = allocation?.ReleasedToSeller > 0
                        ? allocation.ReleasedToSeller
                        : allocation?.SellerPayoutAmount ?? Math.Max(0, gross - commission);
                    var isAdjustment = order.CreatedOn < start;

                    acc.SellerName = string.IsNullOrWhiteSpace(acc.SellerName) ? sellerName : acc.SellerName;
                    acc.Count += 1;
                    acc.Gross += gross;
                    acc.Commission += commission;
                    acc.Payout += payout;
                    if (isAdjustment)
                    {
                        acc.Adjustments += 1;
                        acc.AdjustmentTotal += payout;
                    }

                    summaries[key] = acc;
                }
            }

            return summaries
                .Select(s => new SellerMonthlySettlementSummary(
                    s.Key,
                    string.IsNullOrWhiteSpace(s.Value.SellerName) ? s.Key : s.Value.SellerName,
                    year,
                    month,
                    start,
                    end,
                    s.Value.Count,
                    s.Value.Gross,
                    s.Value.Commission,
                    s.Value.Payout,
                    s.Value.Adjustments,
                    s.Value.AdjustmentTotal))
                .OrderBy(s => s.SellerName)
                .ToList();
        }

        public async Task<SellerMonthlySettlementDetail?> GetMonthlySettlementDetailAsync(
            int year,
            int month,
            string sellerId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sellerId))
            {
                return null;
            }

            var normalizedSeller = sellerId.Trim();
            var (start, end) = ResolveSettlementWindow(year, month);
            var sellerToken = $"\"sellerId\":\"{normalizedSeller}\"";

            var orders = await _dbContext.Orders.AsNoTracking()
                .Where(o => o.CreatedOn < end && o.DetailsJson.Contains(sellerToken))
                .OrderBy(o => o.CreatedOn)
                .ToListAsync(cancellationToken);

            var lines = new List<SellerMonthlySettlementLine>();
            string sellerName = normalizedSeller;

            foreach (var order in orders)
            {
                var details = DeserializeDetails(order.DetailsJson);
                var subOrder = (details.SubOrders ?? new List<OrderSubOrder>())
                    .FirstOrDefault(s => string.Equals(s.SellerId, normalizedSeller, StringComparison.OrdinalIgnoreCase));
                if (subOrder == null)
                {
                    continue;
                }

                var allocations = details.Escrow ?? new List<EscrowAllocation>();
                var allocation = allocations.FirstOrDefault(e =>
                    string.Equals(e.SubOrderNumber, subOrder.SubOrderNumber, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(e.SellerId, normalizedSeller, StringComparison.OrdinalIgnoreCase));

                var payoutOn = allocation == null ? order.CreatedOn : ResolvePayoutDate(allocation, order.CreatedOn);
                if (payoutOn < start || payoutOn >= end)
                {
                    continue;
                }

                sellerName = string.IsNullOrWhiteSpace(subOrder.SellerName) ? sellerName : subOrder.SellerName;
                var gross = allocation?.HeldAmount ?? subOrder.GrandTotal;
                var commission = allocation?.CommissionAmount ?? 0;
                var payout = allocation?.ReleasedToSeller > 0
                    ? allocation.ReleasedToSeller
                    : allocation?.SellerPayoutAmount ?? Math.Max(0, gross - commission);
                var status = PayoutStatuses.Normalize(allocation?.PayoutStatus);
                var isAdjustment = order.CreatedOn < start;

                lines.Add(new SellerMonthlySettlementLine(
                    order.Id,
                    order.OrderNumber,
                    subOrder.SubOrderNumber,
                    payoutOn,
                    order.CreatedOn,
                    gross,
                    commission,
                    payout,
                    status,
                    isAdjustment));
            }

            var summary = (await GetMonthlySettlementsAsync(year, month, normalizedSeller, cancellationToken)).FirstOrDefault();
            if (summary == null)
            {
                summary = new SellerMonthlySettlementSummary(
                    normalizedSeller,
                    sellerName,
                    year,
                    month,
                    start,
                    end,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0);
            }
            else if (string.IsNullOrWhiteSpace(summary.SellerName))
            {
                summary = summary with { SellerName = sellerName };
            }

            return new SellerMonthlySettlementDetail(
                summary,
                lines.OrderByDescending(l => l.PayoutOn).ToList());
        }

        public async Task<byte[]> ExportMonthlySettlementsAsync(
            int year,
            int month,
            string? sellerId = null,
            CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(sellerId))
            {
                var detail = await GetMonthlySettlementDetailAsync(year, month, sellerId, cancellationToken)
                             ?? new SellerMonthlySettlementDetail(
                                 new SellerMonthlySettlementSummary(sellerId.Trim(), sellerId.Trim(), year, month, DateTimeOffset.MinValue, DateTimeOffset.MinValue, 0, 0, 0, 0, 0, 0),
                                 new List<SellerMonthlySettlementLine>());

                var builder = new StringBuilder();
                builder.AppendLine("Order,SubOrder,PayoutOn,CreatedOn,Gross,Commission,Payout,Status,Adjustment");
                foreach (var line in detail.Orders.OrderByDescending(l => l.PayoutOn))
                {
                    builder.AppendLine(string.Join(",", new[]
                    {
                        CsvEscape(line.OrderNumber),
                        CsvEscape(line.SubOrderNumber),
                        CsvEscape(line.PayoutOn.ToString("u", CultureInfo.InvariantCulture)),
                        CsvEscape(line.CreatedOn.ToString("u", CultureInfo.InvariantCulture)),
                        line.GrossTotal.ToString("F2", CultureInfo.InvariantCulture),
                        line.CommissionTotal.ToString("F2", CultureInfo.InvariantCulture),
                        line.PayoutTotal.ToString("F2", CultureInfo.InvariantCulture),
                        CsvEscape(line.PayoutStatus),
                        line.IsAdjustment ? "Adjustment" : string.Empty
                    }));
                }

                return Encoding.UTF8.GetBytes(builder.ToString());
            }

            var summaries = await GetMonthlySettlementsAsync(year, month, null, cancellationToken);
            var summaryBuilder = new StringBuilder();
            summaryBuilder.AppendLine("Seller,Orders,Gross,Commission,Payout,Adjustments,AdjustmentTotal,PeriodStart,PeriodEnd");
            foreach (var summary in summaries.OrderBy(s => s.SellerName))
            {
                summaryBuilder.AppendLine(string.Join(",", new[]
                {
                    CsvEscape(summary.SellerName),
                    summary.OrderCount.ToString(CultureInfo.InvariantCulture),
                    summary.GrossTotal.ToString("F2", CultureInfo.InvariantCulture),
                    summary.CommissionTotal.ToString("F2", CultureInfo.InvariantCulture),
                    summary.PayoutTotal.ToString("F2", CultureInfo.InvariantCulture),
                    summary.AdjustmentCount.ToString(CultureInfo.InvariantCulture),
                    summary.AdjustmentTotal.ToString("F2", CultureInfo.InvariantCulture),
                    CsvEscape(summary.PeriodStart.ToString("u", CultureInfo.InvariantCulture)),
                    CsvEscape(summary.PeriodEnd.ToString("u", CultureInfo.InvariantCulture))
                }));
            }

            return Encoding.UTF8.GetBytes(summaryBuilder.ToString());
        }

        public async Task<List<CommissionInvoiceSummaryView>> GetCommissionInvoicesForSellerAsync(
            string sellerId,
            int historyMonths = 12,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sellerId))
            {
                return new List<CommissionInvoiceSummaryView>();
            }

            var months = Math.Clamp(historyMonths, 1, Math.Max(1, _invoiceOptions.HistoryMonths));
            var anchor = DateTimeOffset.UtcNow;
            var invoices = new List<CommissionInvoiceSummaryView>();

            for (var i = 0; i < months; i++)
            {
                var cursor = anchor.AddMonths(-i);
                var summary = (await GetMonthlySettlementsAsync(cursor.Year, cursor.Month, sellerId, cancellationToken)).FirstOrDefault();
                if (summary == null || (summary.OrderCount == 0 && summary.AdjustmentCount == 0))
                {
                    continue;
                }

                var detail = await GetMonthlySettlementDetailAsync(cursor.Year, cursor.Month, sellerId, cancellationToken);
                if (detail == null)
                {
                    continue;
                }

                var invoiceNumber = await ResolveInvoiceNumberAsync(cursor.Year, cursor.Month, sellerId, cancellationToken);
                var status = ResolveInvoiceStatus(detail);
                var net = RoundCurrency(summary.CommissionTotal);
                var tax = RoundCurrency(net * _invoiceOptions.TaxRate);
                var total = RoundCurrency(net + tax);

                invoices.Add(new CommissionInvoiceSummaryView(
                    invoiceNumber,
                    cursor.Year,
                    cursor.Month,
                    detail.Summary.PeriodStart,
                    detail.Summary.PeriodEnd,
                    detail.Summary.PeriodEnd,
                    net,
                    tax,
                    total,
                    status,
                    summary.AdjustmentCount > 0,
                    net < 0,
                    _invoiceOptions.Currency,
                    _invoiceOptions.TaxRate));
            }

            return invoices
                .OrderByDescending(i => i.Year)
                .ThenByDescending(i => i.Month)
                .ToList();
        }

        public async Task<CommissionInvoiceDocument?> GetCommissionInvoiceAsync(
            string invoiceNumber,
            string sellerId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(invoiceNumber) || string.IsNullOrWhiteSpace(sellerId))
            {
                return null;
            }

            if (!TryParseInvoiceNumber(invoiceNumber, out var year, out var month))
            {
                return null;
            }

            var detail = await GetMonthlySettlementDetailAsync(year, month, sellerId, cancellationToken);
            if (detail == null || (detail.Summary.OrderCount == 0 && detail.Summary.AdjustmentCount == 0))
            {
                return null;
            }

            var normalizedNumber = await ResolveInvoiceNumberAsync(year, month, sellerId, cancellationToken);
            var seller = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == sellerId, cancellationToken);
            var sellerName = !string.IsNullOrWhiteSpace(detail.Summary.SellerName)
                ? detail.Summary.SellerName
                : seller?.BusinessName ?? seller?.FullName ?? sellerId;

            var net = RoundCurrency(detail.Summary.CommissionTotal);
            var tax = RoundCurrency(net * _invoiceOptions.TaxRate);
            var total = RoundCurrency(net + tax);
            var status = ResolveInvoiceStatus(detail);

            var summaryView = new CommissionInvoiceSummaryView(
                normalizedNumber,
                year,
                month,
                detail.Summary.PeriodStart,
                detail.Summary.PeriodEnd,
                detail.Summary.PeriodEnd,
                net,
                tax,
                total,
                status,
                detail.Summary.AdjustmentCount > 0,
                net < 0,
                _invoiceOptions.Currency,
                _invoiceOptions.TaxRate);

            var lines = detail.Orders
                .OrderByDescending(o => o.PayoutOn)
                .Select(o => new CommissionInvoiceLine(
                    o.OrderNumber,
                    o.SubOrderNumber,
                    o.PayoutOn,
                    o.CommissionTotal,
                    o.PayoutStatus,
                    o.IsAdjustment))
                .ToList();

            return new CommissionInvoiceDocument(
                summaryView,
                sellerId,
                sellerName,
                seller?.TaxId,
                seller?.Address,
                _invoiceOptions.IssuerName,
                _invoiceOptions.IssuerTaxId,
                _invoiceOptions.IssuerAddress,
                _invoiceOptions.TaxLabel,
                lines);
        }

        public async Task<CommissionInvoicePdf?> GetCommissionInvoicePdfAsync(
            string invoiceNumber,
            string sellerId,
            CancellationToken cancellationToken = default)
        {
            var document = await GetCommissionInvoiceAsync(invoiceNumber, sellerId, cancellationToken);
            if (document == null)
            {
                return null;
            }

            var bytes = RenderInvoicePdf(document);
            return new CommissionInvoicePdf(bytes, $"{document.Summary.InvoiceNumber}.pdf");
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
            var hasLabel = subOrder.ShippingLabel != null && !string.IsNullOrWhiteSpace(subOrder.ShippingLabel.Base64Content);
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
                details.PaymentStatusMessage,
                subOrder.Return,
                escrow,
                subOrder.StatusHistory ?? new List<OrderStatusChange>(),
                hasLabel,
                subOrder.ShippingLabel?.FileName,
                subOrder.ShippingLabel?.ExpiresOn);
        }

        public async Task<ShippingLabelFile?> GetShippingLabelAsync(int orderId, string sellerId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sellerId))
            {
                return null;
            }

            var sellerToken = $"\"sellerId\":\"{sellerId}\"";
            var order = await _dbContext.Orders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == orderId && o.DetailsJson.Contains(sellerToken), cancellationToken);
            if (order == null)
            {
                return null;
            }

            var details = DeserializeDetails(order.DetailsJson);
            var subOrder = details.SubOrders.FirstOrDefault(s => string.Equals(s.SellerId, sellerId, StringComparison.OrdinalIgnoreCase));
            var label = subOrder?.ShippingLabel;
            if (subOrder == null || label == null || string.IsNullOrWhiteSpace(label.Base64Content))
            {
                return null;
            }

            try
            {
                var bytes = Convert.FromBase64String(label.Base64Content);
                var fileName = string.IsNullOrWhiteSpace(label.FileName) ? $"{subOrder.SubOrderNumber}-label.pdf" : label.FileName;
                var contentType = string.IsNullOrWhiteSpace(label.ContentType) ? "application/pdf" : label.ContentType;
                return new ShippingLabelFile(bytes, contentType, fileName);
            }
            catch
            {
                return null;
            }
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
            string? shippingProviderReference = null,
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
            var originalStatus = OrderStatuses.Normalize(subOrder.Status);
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

            var deliveryAddress = DeserializeAddress(order.DeliveryAddressJson);
            var providerId = string.IsNullOrWhiteSpace(subOrder.ShippingProviderId) ? subOrder.ShippingDetail.ProviderId : subOrder.ShippingProviderId;
            var providerService = string.IsNullOrWhiteSpace(subOrder.ShippingProviderService) ? subOrder.ShippingDetail.ProviderServiceCode : subOrder.ShippingProviderService;
            providerId = string.IsNullOrWhiteSpace(providerId) ? null : providerId.Trim();
            providerService = string.IsNullOrWhiteSpace(providerService) ? null : providerService.Trim();
            var providerReference = string.IsNullOrWhiteSpace(shippingProviderReference) ? subOrder.ShippingProviderReference : shippingProviderReference.Trim();
            providerReference = string.IsNullOrWhiteSpace(providerReference) ? null : providerReference;
            var trackingUrl = string.IsNullOrWhiteSpace(subOrder.TrackingUrl) ? null : subOrder.TrackingUrl.Trim();
            var shippingLabel = subOrder.ShippingLabel;

            var tracking = string.IsNullOrWhiteSpace(trackingNumber) ? subOrder.TrackingNumber : trackingNumber.Trim();
            var carrier = string.IsNullOrWhiteSpace(trackingCarrier) ? subOrder.TrackingCarrier : trackingCarrier.Trim();

            if (string.Equals(updatedStatus, OrderStatuses.Shipped, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(tracking)
                && !string.IsNullOrWhiteSpace(providerId)
                && !string.IsNullOrWhiteSpace(providerService))
            {
                var shipment = _shippingProviderService.CreateShipment(new ShippingProviderShipmentRequest(
                    providerId!,
                    providerService!,
                    order.OrderNumber,
                    subOrder.SubOrderNumber,
                    sellerId,
                    deliveryAddress,
                    order.BuyerEmail,
                    order.BuyerName,
                    deliveryAddress.Phone,
                    subOrder.GrandTotal,
                    subOrder.TotalQuantity,
                    providerReference ?? order.PaymentReference));

                if (!shipment.Success)
                {
                    return new SubOrderStatusUpdateResult(false, shipment.Error ?? "Unable to create shipment via provider.");
                }

                tracking = shipment.TrackingNumber ?? tracking;
                carrier = string.IsNullOrWhiteSpace(carrier) ? shipment.Carrier : carrier;
                providerReference = shipment.ProviderReference ?? providerReference;
                if (!string.IsNullOrWhiteSpace(shipment.TrackingUrl))
                {
                    trackingUrl = shipment.TrackingUrl;
                }

                if (shipment.Label != null)
                {
                    if (shipment.Label.Content == null || shipment.Label.Content.Length == 0)
                    {
                        return new SubOrderStatusUpdateResult(false, "Shipping label could not be generated.");
                    }

                    var base64 = Convert.ToBase64String(shipment.Label.Content);
                    var fileName = string.IsNullOrWhiteSpace(shipment.Label.FileName)
                        ? $"{subOrder.SubOrderNumber}-label.pdf"
                        : shipment.Label.FileName.Trim();
                    var contentType = string.IsNullOrWhiteSpace(shipment.Label.ContentType)
                        ? "application/pdf"
                        : shipment.Label.ContentType.Trim();
                    var createdOn = shipment.Label.CreatedOn == default ? now : shipment.Label.CreatedOn;
                    shippingLabel = new ShippingLabelInfo(fileName, contentType, base64, createdOn, shipment.Label.ExpiresOn);
                }
            }

            var history = NormalizeStatusHistory(subOrder.StatusHistory, originalStatus, subOrder.TrackingNumber, subOrder.TrackingCarrier, now);
            var statusChanged = !string.Equals(originalStatus, updatedStatus, StringComparison.OrdinalIgnoreCase);
            if (statusChanged || !string.IsNullOrWhiteSpace(tracking))
            {
                history = AppendStatusHistory(history, updatedStatus, tracking, carrier, now);
            }

            var updatedSubOrder = subOrder with
            {
                Items = updatedItems,
                Status = updatedStatus,
                TrackingNumber = tracking,
                TrackingCarrier = carrier,
                RefundedAmount = computedRefund,
                DeliveredOn = string.Equals(updatedStatus, OrderStatuses.Delivered, StringComparison.OrdinalIgnoreCase)
                    ? subOrder.DeliveredOn ?? now
                    : subOrder.DeliveredOn,
                StatusHistory = history,
                ShippingProviderId = providerId,
                ShippingProviderService = providerService,
                ShippingProviderReference = providerReference,
                TrackingUrl = trackingUrl,
                ShippingLabel = shippingLabel
            };

            var updatedReturn = UpdateReturnRequest(subOrder.Return, updatedStatus, computedRefund, updatedItems);
            if (updatedReturn != null)
            {
                var previousStatus = ReturnRequestStatuses.Normalize(subOrder.Return?.Status);
                var updatedReturnStatus = ReturnRequestStatuses.Normalize(updatedReturn.Status);
                if (!string.Equals(previousStatus, updatedReturnStatus, StringComparison.OrdinalIgnoreCase))
                {
                    var note = string.Equals(updatedReturnStatus, ReturnRequestStatuses.Completed, StringComparison.OrdinalIgnoreCase)
                        ? "Case completed after status/refund update."
                        : null;
                    updatedReturn = AppendReturnHistory(updatedReturn, updatedReturnStatus, "System", note, now);
                }
            }
            updatedSubOrder = updatedSubOrder with { Return = updatedReturn };

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

            if (statusChanged && string.Equals(updatedStatus, OrderStatuses.Shipped, StringComparison.OrdinalIgnoreCase))
            {
                await SendShippingUpdateEmailAsync(order, updatedSubOrder);
            }

            return new SubOrderStatusUpdateResult(true, null, updatedSubOrder, order.Status);
        }

        public async Task<SubOrderStatusUpdateResult> UpdateShippingStatusFromProviderAsync(
            string providerId,
            string providerReference,
            string providerStatus,
            string? trackingNumber = null,
            string? trackingCarrier = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(providerReference))
            {
                return new SubOrderStatusUpdateResult(false, "Provider id and reference are required.");
            }

            var normalizedProvider = providerId.Trim();
            var normalizedReference = providerReference.Trim();
            var referenceToken = $"\"shippingProviderReference\":\"{normalizedReference}\"";
            var providerToken = $"\"shippingProviderId\":\"{normalizedProvider}\"";
            var order = await _dbContext.Orders.FirstOrDefaultAsync(
                o => o.DetailsJson.Contains(referenceToken) && o.DetailsJson.Contains(providerToken),
                cancellationToken);
            if (order == null)
            {
                return new SubOrderStatusUpdateResult(false, "Order not found for this shipment reference.");
            }

            var details = DeserializeDetails(order.DetailsJson);
            var subOrder = details.SubOrders.FirstOrDefault(s =>
                string.Equals(s.ShippingProviderReference, normalizedReference, StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.ShippingProviderId, normalizedProvider, StringComparison.OrdinalIgnoreCase));
            if (subOrder == null)
            {
                return new SubOrderStatusUpdateResult(false, "Sub-order not found for this shipment reference.");
            }

            var targetStatus = _shippingProviderService.MapProviderStatus(providerStatus);
            var provider = _shippingProviderService.GetProvider(normalizedProvider);
            var resolvedCarrier = string.IsNullOrWhiteSpace(trackingCarrier)
                ? provider?.Name ?? subOrder.TrackingCarrier
                : trackingCarrier.Trim();
            var resolvedTracking = string.IsNullOrWhiteSpace(trackingNumber)
                ? (string.IsNullOrWhiteSpace(subOrder.TrackingNumber) ? normalizedReference : subOrder.TrackingNumber)
                : trackingNumber.Trim();

            return await UpdateSubOrderStatusAsync(
                order.Id,
                subOrder.SellerId,
                targetStatus,
                resolvedTracking,
                null,
                resolvedCarrier,
                null,
                normalizedReference,
                cancellationToken);
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
                var initialHistory = new List<OrderStatusChange>
                {
                    new(OrderStatuses.Normalize(initialStatus), DateTimeOffset.UtcNow, null, null)
                };

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
                    initialStatus,
                    null,
                    null,
                    0,
                    null,
                    null,
                    initialHistory,
                    ship.ProviderId,
                    ship.ProviderServiceCode,
                    null,
                    null));
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
                return new OrderShippingDetail(sellerId, sellerName, "standard", "Standard", Math.Max(0, fallbackCost), null, null);
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
                match.Description,
                match.DeliveryEstimate,
                match.ProviderId,
                match.ProviderServiceCode);
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

        private OrderDetailsPayload ApplyPaymentRefund(
            OrderDetailsPayload details,
            decimal paymentRefundedAmount,
            string? paymentReference,
            out bool changed)
        {
            changed = false;
            var normalized = NormalizeDetails(details);
            var subOrders = normalized.SubOrders.ToList();
            if (subOrders.Count == 0)
            {
                return normalized;
            }

            var totalHold = subOrders.Sum(s => Math.Max(0, s.GrandTotal));
            if (totalHold <= 0)
            {
                return normalized;
            }

            var targetRefund = Math.Min(Math.Max(0, paymentRefundedAmount), totalHold);
            var alreadyRefunded = subOrders.Sum(s => Math.Max(0, s.RefundedAmount));
            if (targetRefund <= alreadyRefunded && targetRefund <= normalized.PaymentRefundedAmount)
            {
                return normalized;
            }

            var outstandingTotals = subOrders
                .Select(s => Math.Max(0, s.GrandTotal - Math.Max(0, s.RefundedAmount)))
                .ToList();
            var totalOutstanding = outstandingTotals.Sum();
            if (totalOutstanding <= 0)
            {
                return normalized with { PaymentRefundedAmount = Math.Max(normalized.PaymentRefundedAmount, targetRefund) };
            }

            var remaining = Math.Max(0, targetRefund - alreadyRefunded);
            decimal allocated = 0;
            var updatedSubOrders = new List<OrderSubOrder>();

            for (var i = 0; i < subOrders.Count; i++)
            {
                var subOrder = subOrders[i];
                var outstanding = outstandingTotals[i];
                if (outstanding <= 0)
                {
                    updatedSubOrders.Add(subOrder);
                    continue;
                }

                var proportional = remaining * (outstanding / totalOutstanding);
                var share = Math.Min(outstanding, Math.Round(proportional, 2, MidpointRounding.AwayFromZero));
                if (i == subOrders.Count - 1)
                {
                    share = Math.Max(0, Math.Min(outstanding, remaining - allocated));
                }

                var newRefund = Math.Min(subOrder.GrandTotal, Math.Max(0, subOrder.RefundedAmount) + share);
                var fullyRefunded = newRefund >= subOrder.GrandTotal;
                var updatedItems = subOrder.Items
                    .Select(i => fullyRefunded ? i with { Status = OrderStatuses.Refunded } : i)
                    .ToList();
                var updatedStatus = fullyRefunded ? OrderStatuses.Refunded : subOrder.Status;

                allocated += Math.Max(0, newRefund - Math.Max(0, subOrder.RefundedAmount));
                updatedSubOrders.Add(subOrder with
                {
                    RefundedAmount = newRefund,
                    Items = updatedItems,
                    Status = updatedStatus
                });
            }

            var updatedItemsList = updatedSubOrders.SelectMany(s => s.Items).ToList();
            var updatedEscrow = normalized.Escrow;
            foreach (var updatedSubOrder in updatedSubOrders)
            {
                updatedEscrow = UpdateEscrowAllocations(updatedEscrow, updatedSubOrders, updatedSubOrder, paymentReference);
            }

            changed = allocated > 0;
            var updatedRefunded = Math.Max(normalized.PaymentRefundedAmount, Math.Min(targetRefund, alreadyRefunded + allocated));
            var updatedPaymentStatus = updatedRefunded > 0 ? PaymentStatuses.Refunded : normalized.PaymentStatus;
            var updatedMessage = string.IsNullOrWhiteSpace(normalized.PaymentStatusMessage)
                ? PaymentStatusMapper.BuildBuyerMessage(updatedPaymentStatus)
                : normalized.PaymentStatusMessage;

            return normalized with
            {
                SubOrders = updatedSubOrders,
                Items = updatedItemsList,
                Escrow = updatedEscrow,
                PaymentRefundedAmount = updatedRefunded,
                PaymentStatus = updatedPaymentStatus,
                PaymentStatusMessage = updatedMessage
            };
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
            var commission = CalculateCommissionAfterRefund(updatedSubOrder);
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

        private decimal CalculateCommissionAfterRefund(OrderSubOrder subOrder)
        {
            var commission = _commissionCalculator.CalculateForOrderItems(subOrder.Items);
            if (commission <= 0)
            {
                return commission;
            }

            var refundAmount = Math.Max(0, subOrder.RefundedAmount);
            if (refundAmount <= 0)
            {
                return commission;
            }

            var hasItemLevelRefunds = subOrder.Items.Any(i =>
            {
                var status = OrderStatuses.Normalize(i.Status);
                return string.Equals(status, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, OrderStatuses.Refunded, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, OrderStatuses.Failed, StringComparison.OrdinalIgnoreCase);
            });
            if (hasItemLevelRefunds)
            {
                return commission;
            }

            var shipping = Math.Max(0, subOrder.Shipping);
            var refundableForItems = Math.Max(0, refundAmount - Math.Min(refundAmount, shipping));
            var itemTotal = Math.Max(0, subOrder.Items.Sum(i => i.LineTotal));
            if (itemTotal <= 0 || refundableForItems <= 0)
            {
                return commission;
            }

            var ratio = Math.Min(1, refundableForItems / itemTotal);
            var reduced = commission * (1 - ratio);
            return _commissionCalculator.Round(Math.Max(0, reduced));
        }

        private static string BuildCaseId(string subOrderNumber, DateTimeOffset requestedOn)
        {
            var safeNumber = string.IsNullOrWhiteSpace(subOrderNumber) ? "ORDER" : subOrderNumber.Replace(" ", string.Empty);
            var timestamp = requestedOn == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : requestedOn;
            return $"CASE-{safeNumber}-{timestamp.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)}";
        }

        private static ReturnCaseFilterOptions NormalizeReturnCaseFilters(ReturnCaseFilterOptions? filters)
        {
            if (filters == null)
            {
                return new ReturnCaseFilterOptions();
            }

            var statuses = filters.Statuses
                .Select(ReturnRequestStatuses.Normalize)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new ReturnCaseFilterOptions
            {
                Statuses = statuses,
                FromDate = filters.FromDate,
                ToDate = filters.ToDate
            };
        }

        private static bool MatchesReturnFilters(ReturnRequest request, ReturnCaseFilterOptions filters)
        {
            var normalizedStatus = ReturnRequestStatuses.Normalize(request.Status);
            if (filters.Statuses.Count > 0 && !filters.Statuses.Contains(normalizedStatus, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            if (filters.FromDate.HasValue && request.RequestedOn < filters.FromDate.Value)
            {
                return false;
            }

            if (filters.ToDate.HasValue && request.RequestedOn > filters.ToDate.Value)
            {
                return false;
            }

            return true;
        }

        private static AdminCaseFilterOptions NormalizeAdminCaseFilters(AdminCaseFilterOptions? filters)
        {
            if (filters == null)
            {
                return new AdminCaseFilterOptions();
            }

            var statuses = filters.Statuses
                .Select(ReturnRequestStatuses.Normalize)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var normalizedType = string.IsNullOrWhiteSpace(filters.Type) ? null : ReturnRequestTypes.Normalize(filters.Type);
            var query = string.IsNullOrWhiteSpace(filters.Query) ? null : filters.Query.Trim();

            return new AdminCaseFilterOptions
            {
                Statuses = statuses,
                FromDate = filters.FromDate,
                ToDate = filters.ToDate,
                Query = query,
                Type = normalizedType
            };
        }

        private static bool MatchesAdminCaseFilters(OrderRecord order, OrderSubOrder subOrder, ReturnRequest request, AdminCaseFilterOptions filters)
        {
            var normalizedStatus = ReturnRequestStatuses.Normalize(request.Status);
            if (filters.Statuses.Count > 0 && !filters.Statuses.Contains(normalizedStatus, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            if (filters.FromDate.HasValue && request.RequestedOn < filters.FromDate.Value)
            {
                return false;
            }

            if (filters.ToDate.HasValue && request.RequestedOn > filters.ToDate.Value)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(filters.Type))
            {
                var normalizedType = ReturnRequestTypes.Normalize(request.Type);
                if (!string.Equals(normalizedType, filters.Type, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (!string.IsNullOrWhiteSpace(filters.Query))
            {
                var query = filters.Query.Trim();
                var haystack = new[]
                {
                    request.CaseId,
                    subOrder.SubOrderNumber,
                    order.OrderNumber,
                    subOrder.SellerName,
                    subOrder.SellerId,
                    order.BuyerName,
                    order.BuyerEmail
                }.Where(v => !string.IsNullOrWhiteSpace(v));

                if (!haystack.Any(v => v!.Contains(query, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }
            }

            return true;
        }

        private static DateTimeOffset CalculateCaseLastUpdated(ReturnRequest request)
        {
            var timestamps = new List<DateTimeOffset> { request.RequestedOn };
            if (request.History != null && request.History.Count > 0)
            {
                timestamps.AddRange(request.History.Select(h => h.ChangedOn));
            }

            if (request.Messages != null && request.Messages.Count > 0)
            {
                timestamps.AddRange(request.Messages.Select(m => m.SentOn));
            }

            if (request.ResolvedOn.HasValue && request.ResolvedOn != DateTimeOffset.MinValue)
            {
                timestamps.Add(request.ResolvedOn.Value);
            }

            var normalized = timestamps
                .Where(t => t != DateTimeOffset.MinValue)
                .ToList();

            return normalized.Count == 0 ? DateTimeOffset.UtcNow : normalized.Max();
        }

        private static BuyerCaseSummaryView BuildBuyerCaseSummary(OrderRecord order, OrderSubOrder subOrder, ReturnRequest request, string paymentStatus)
        {
            var sellerName = string.IsNullOrWhiteSpace(subOrder.SellerName) ? subOrder.SellerId : subOrder.SellerName;
            var normalizedPaymentStatus = PaymentStatuses.Normalize(paymentStatus);
            var paymentReference = string.IsNullOrWhiteSpace(order.PaymentReference) ? null : order.PaymentReference.Trim();
            var lastUpdated = CalculateCaseLastUpdated(request);

            return new BuyerCaseSummaryView(
                string.IsNullOrWhiteSpace(request.CaseId) ? BuildCaseId(subOrder.SubOrderNumber, request.RequestedOn) : request.CaseId!,
                order.Id,
                order.OrderNumber,
                subOrder.SubOrderNumber,
                sellerName,
                request.Type,
                ReturnRequestStatuses.Normalize(request.Status),
                request.RequestedOn,
                lastUpdated,
                Math.Max(0, subOrder.RefundedAmount),
                normalizedPaymentStatus,
                paymentReference);
        }

        private static List<BuyerCaseItemView> BuildBuyerCaseItems(OrderSubOrder subOrder, ReturnRequest request)
        {
            var items = new List<BuyerCaseItemView>();
            foreach (var item in request.Items)
            {
                var match = subOrder.Items.FirstOrDefault(i => i.ProductId == item.ProductId);
                var label = match == null ? $"Item {item.ProductId}" : match.Name;
                if (match != null && !string.IsNullOrWhiteSpace(match.Variant))
                {
                    label = $"{label} ({match.Variant})";
                }

                items.Add(new BuyerCaseItemView(label, item.ProductId, Math.Max(1, item.Quantity)));
            }

            return items;
        }

        private static CaseResolutionView BuildCaseResolution(OrderSubOrder subOrder, ReturnRequest request, string paymentStatus, string? paymentReference)
        {
            var normalizedStatus = ReturnRequestStatuses.Normalize(request.Status);
            var refunded = Math.Max(0, request.ResolutionRefundAmount ?? subOrder.RefundedAmount);
            var total = Math.Max(0, subOrder.GrandTotal);
            var normalizedPaymentStatus = PaymentStatuses.Normalize(paymentStatus);
            var normalizedReference = string.IsNullOrWhiteSpace(request.ResolutionRefundReference)
                ? (string.IsNullOrWhiteSpace(paymentReference) ? null : paymentReference.Trim())
                : request.ResolutionRefundReference.Trim();
            var resolutionOutcome = string.IsNullOrWhiteSpace(request.ResolutionOutcome) ? null : request.ResolutionOutcome.Trim();
            var resolutionNote = string.IsNullOrWhiteSpace(request.ResolutionNote) ? null : request.ResolutionNote.Trim();
            var resolvedOn = request.ResolvedOn == DateTimeOffset.MinValue ? null : request.ResolvedOn;
            var refundStatus = string.IsNullOrWhiteSpace(request.ResolutionRefundStatus)
                ? normalizedPaymentStatus
                : request.ResolutionRefundStatus.Trim();
            var resolutionActor = string.IsNullOrWhiteSpace(request.ResolutionActor) ? "Seller" : request.ResolutionActor.Trim();

            if (refunded == 0 && subOrder.RefundedAmount > 0 && !request.ResolutionRefundAmount.HasValue)
            {
                refunded = subOrder.RefundedAmount;
            }

            var outcome = resolutionOutcome ?? ResolveOutcomeFromStatus(normalizedStatus, refunded, total);
            var summary = BuildResolutionSummary(resolutionActor, outcome, refunded, resolutionNote, refundStatus);

            return new CaseResolutionView(outcome, summary, refunded, total, refundStatus, normalizedReference, resolutionNote, resolvedOn);
        }

        private static string ResolveOutcomeFromStatus(string normalizedStatus, decimal refunded, decimal total)
        {
            if (string.Equals(normalizedStatus, ReturnRequestStatuses.Rejected, StringComparison.OrdinalIgnoreCase))
            {
                return "Rejected";
            }

            if (string.Equals(normalizedStatus, ReturnRequestStatuses.PendingBuyerInfo, StringComparison.OrdinalIgnoreCase))
            {
                return "Information requested";
            }

            if (string.Equals(normalizedStatus, ReturnRequestStatuses.SellerProposed, StringComparison.OrdinalIgnoreCase))
            {
                return "Seller proposed solution";
            }

            if (refunded > 0 && total > 0 && refunded < total)
            {
                return "Partially approved";
            }

            if (refunded > 0 || string.Equals(normalizedStatus, ReturnRequestStatuses.Approved, StringComparison.OrdinalIgnoreCase))
            {
                return "Approved";
            }

            if (string.Equals(normalizedStatus, ReturnRequestStatuses.Completed, StringComparison.OrdinalIgnoreCase))
            {
                return refunded > 0 ? "Completed" : "Resolved";
            }

            return "Pending review";
        }

        private static string BuildResolutionSummary(string actor, string outcome, decimal refunded, string? note, string refundStatus)
        {
            var actorLabel = string.IsNullOrWhiteSpace(actor) ? "Seller" : actor.Trim();
            var normalizedOutcome = string.IsNullOrWhiteSpace(outcome) ? "Pending review" : outcome.Trim();
            var formattedAmount = refunded > 0 ? refunded.ToString("C", CultureInfo.InvariantCulture) : null;
            var statusSuffix = string.IsNullOrWhiteSpace(refundStatus) ? string.Empty : $" ({refundStatus})";

            if (string.Equals(normalizedOutcome, "Full refund", StringComparison.OrdinalIgnoreCase))
            {
                return formattedAmount == null
                    ? $"{actorLabel} approved a full refund{statusSuffix}."
                    : $"{actorLabel} processed a full refund of {formattedAmount}{statusSuffix}.";
            }

            if (string.Equals(normalizedOutcome, "Partial refund", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedOutcome, "Partially approved", StringComparison.OrdinalIgnoreCase))
            {
                return formattedAmount == null
                    ? $"{actorLabel} approved a partial refund{statusSuffix}."
                    : $"{actorLabel} approved a partial refund of {formattedAmount}{statusSuffix}.";
            }

            if (string.Equals(normalizedOutcome, "Rejected", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(note)
                    ? $"{actorLabel} rejected the request."
                    : $"{actorLabel} rejected the request. Reason: {note}";
            }

            if (string.Equals(normalizedOutcome, "No refund", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(note)
                    ? $"{actorLabel} resolved the case without a refund."
                    : $"{actorLabel} resolved without a refund. Reason: {note}";
            }

            if (string.Equals(normalizedOutcome, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                return refunded > 0
                    ? $"{actorLabel} approved a refund of {formattedAmount}{statusSuffix}."
                    : $"{actorLabel} approved the request.";
            }

            if (string.Equals(normalizedOutcome, "Replacement", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(note)
                    ? $"{actorLabel} will provide a replacement."
                    : $"{actorLabel} will provide a replacement. Note: {note}";
            }

            if (string.Equals(normalizedOutcome, "Repair", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(note)
                    ? $"{actorLabel} will arrange a repair."
                    : $"{actorLabel} will arrange a repair. Note: {note}";
            }

            if (string.Equals(normalizedOutcome, "Information requested", StringComparison.OrdinalIgnoreCase))
            {
                return $"{actorLabel} requested more information to continue.";
            }

            if (string.Equals(normalizedOutcome, "Seller proposed solution", StringComparison.OrdinalIgnoreCase))
            {
                return $"{actorLabel} proposed an alternative or partial resolution.";
            }

            if (string.Equals(normalizedOutcome, "Completed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedOutcome, "Resolved", StringComparison.OrdinalIgnoreCase))
            {
                return refunded > 0
                    ? $"Case closed after refund of {formattedAmount}{statusSuffix}."
                    : "Case marked as completed.";
            }

            return "Awaiting seller decision.";
        }

        private ReturnRequest? UpdateReturnRequest(ReturnRequest? request, string targetStatus, decimal refundedAmount, List<OrderItemDetail> items)
        {
            var normalized = NormalizeReturnRequest(request, items);
            if (normalized == null)
            {
                return null;
            }

            var normalizedReturnStatus = ReturnRequestStatuses.Normalize(normalized.Status);
            var normalizedTargetStatus = OrderStatuses.Normalize(targetStatus);
            var shouldComplete = string.Equals(normalizedTargetStatus, OrderStatuses.Refunded, StringComparison.OrdinalIgnoreCase)
                || refundedAmount > 0;

            if (shouldComplete && !string.Equals(normalizedReturnStatus, ReturnRequestStatuses.Rejected, StringComparison.OrdinalIgnoreCase))
            {
                normalizedReturnStatus = ReturnRequestStatuses.Completed;
            }

            if (!string.Equals(normalizedReturnStatus, normalized.Status, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized with { Status = normalizedReturnStatus };
            }

            return ApplySlaTracking(normalized, items, DateTimeOffset.UtcNow);
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

        private async Task SendShippingUpdateEmailAsync(OrderRecord order, OrderSubOrder subOrder)
        {
            if (string.IsNullOrWhiteSpace(order.BuyerEmail))
            {
                return;
            }

            if (!string.Equals(OrderStatuses.Normalize(subOrder.Status), OrderStatuses.Shipped, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                var builder = new StringBuilder();
                builder.Append($"<h2>Your order {order.OrderNumber} has shipped</h2>");
                builder.Append($"<p>{subOrder.SellerName} marked sub-order {subOrder.SubOrderNumber} as shipped.</p>");

                if (!string.IsNullOrWhiteSpace(subOrder.ShippingDetail.MethodLabel))
                {
                    builder.Append($"<p>Shipping method: {subOrder.ShippingDetail.MethodLabel}</p>");
                }

                if (!string.IsNullOrWhiteSpace(subOrder.TrackingNumber) || !string.IsNullOrWhiteSpace(subOrder.TrackingCarrier))
                {
                    builder.Append("<p>");
                    if (!string.IsNullOrWhiteSpace(subOrder.TrackingNumber))
                    {
                        builder.Append($"Tracking: {subOrder.TrackingNumber}");
                        if (!string.IsNullOrWhiteSpace(subOrder.TrackingCarrier))
                        {
                            builder.Append(" • ");
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(subOrder.TrackingCarrier))
                    {
                        builder.Append($"Carrier: {subOrder.TrackingCarrier}");
                    }

                    builder.Append("</p>");
                }

                if (!string.IsNullOrWhiteSpace(subOrder.TrackingUrl))
                {
                    builder.Append($"<p><a href=\"{subOrder.TrackingUrl}\" target=\"_blank\" rel=\"noopener\">Track your shipment</a></p>");
                }

                await _emailSender.SendEmailAsync(order.BuyerEmail, $"Order {order.OrderNumber} shipped", builder.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send shipping update email for {OrderNumber} sub-order {SubOrder}", order.OrderNumber, subOrder.SubOrderNumber);
            }
        }

        private Task<string?> GetSellerEmailAsync(string sellerId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sellerId))
            {
                return Task.FromResult<string?>(null);
            }

            return _dbContext.Users.AsNoTracking()
                .Where(u => u.Id == sellerId)
                .Select(u => string.IsNullOrWhiteSpace(u.ContactEmail) ? u.Email : u.ContactEmail)
                .FirstOrDefaultAsync(cancellationToken);
        }

        private async Task SendReturnCaseUpdateEmailAsync(OrderRecord order, OrderSubOrder subOrder, ReturnRequest request, string? message, string? sellerEmail = null)
        {
            if (string.IsNullOrWhiteSpace(order.BuyerEmail))
            {
                return;
            }

            try
            {
                var builder = new StringBuilder();
                var caseId = string.IsNullOrWhiteSpace(request.CaseId) ? subOrder.SubOrderNumber : request.CaseId;
                builder.Append($"<p>Your case {caseId} for order {order.OrderNumber} was updated to {ReturnRequestStatuses.Normalize(request.Status)}.</p>");
                if (!string.IsNullOrWhiteSpace(message))
                {
                    builder.Append($"<p>{message}</p>");
                }

                builder.Append($"<p>Sub-order: {subOrder.SubOrderNumber}</p>");
                await _emailSender.SendEmailAsync(order.BuyerEmail, $"Case update {caseId}", builder.ToString());

                if (!string.IsNullOrWhiteSpace(sellerEmail))
                {
                    var sellerBuilder = new StringBuilder();
                    sellerBuilder.Append($"<p>Case {caseId} for order {order.OrderNumber} was updated to {ReturnRequestStatuses.Normalize(request.Status)}.</p>");
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        sellerBuilder.Append($"<p>{message}</p>");
                    }

                    sellerBuilder.Append($"<p>Sub-order: {subOrder.SubOrderNumber}</p>");
                    await _emailSender.SendEmailAsync(sellerEmail, $"Case update {caseId}", sellerBuilder.ToString());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send case update email for {CaseId}", request.CaseId ?? subOrder.SubOrderNumber);
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
                .Select(sub => NormalizeSubOrder(sub))
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

        private async Task<string> ResolveInvoiceNumberAsync(int year, int month, string sellerId, CancellationToken cancellationToken)
        {
            var summaries = await GetMonthlySettlementsAsync(year, month, null, cancellationToken);
            var ordered = summaries
                .OrderBy(s => s.SellerId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.SellerName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var index = ordered.FindIndex(s => string.Equals(s.SellerId, sellerId, StringComparison.OrdinalIgnoreCase));
            var sequence = index >= 0 ? index + 1 : ordered.Count + 1;
            return $"{_invoiceOptions.Series}-{year}{month:00}-{sequence:0000}";
        }

        private static bool TryParseInvoiceNumber(string invoiceNumber, out int year, out int month)
        {
            year = 0;
            month = 0;
            if (string.IsNullOrWhiteSpace(invoiceNumber))
            {
                return false;
            }

            var parts = invoiceNumber.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return false;
            }

            var payload = parts[1];
            if (payload.Length < 6)
            {
                return false;
            }

            if (!int.TryParse(payload[..4], out year))
            {
                return false;
            }

            if (!int.TryParse(payload.Substring(4, 2), out month))
            {
                return false;
            }

            return month >= 1 && month <= 12;
        }

        private static string ResolveInvoiceStatus(SellerMonthlySettlementDetail detail)
        {
            if (detail.Orders.Count == 0)
            {
                return InvoiceStatuses.Draft;
            }

            var statuses = detail.Orders.Select(o => PayoutStatuses.Normalize(o.PayoutStatus)).ToList();
            if (statuses.All(s => string.Equals(s, PayoutStatuses.Paid, StringComparison.OrdinalIgnoreCase)))
            {
                return InvoiceStatuses.Paid;
            }

            if (statuses.Any(s => string.Equals(s, PayoutStatuses.Failed, StringComparison.OrdinalIgnoreCase)))
            {
                return InvoiceStatuses.Blocked;
            }

            if (statuses.Any(s => string.Equals(s, PayoutStatuses.Processing, StringComparison.OrdinalIgnoreCase)))
            {
                return InvoiceStatuses.Pending;
            }

            return InvoiceStatuses.Issued;
        }

        private static decimal RoundCurrency(decimal amount) =>
            Math.Round(amount, 2, MidpointRounding.AwayFromZero);

        private byte[] RenderInvoicePdf(CommissionInvoiceDocument document)
        {
            var objects = new List<string>
            {
                "1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj",
                "2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj",
                "3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>"
            };

            var content = BuildInvoiceContent(document);
            var contentBytes = Encoding.ASCII.GetBytes(content);
            objects.Add($"4 0 obj << /Length {contentBytes.Length} >> stream\n{content}\nendstream endobj");
            objects.Add("5 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> endobj");

            using var stream = new MemoryStream();
            var offsets = new List<long> { 0 };

            void WriteLine(string line)
            {
                var bytes = Encoding.ASCII.GetBytes(line + "\n");
                stream.Write(bytes, 0, bytes.Length);
            }

            WriteLine("%PDF-1.4");

            foreach (var obj in objects)
            {
                offsets.Add(stream.Position);
                WriteLine(obj);
            }

            var xrefPosition = stream.Position;
            WriteLine("xref");
            WriteLine($"0 {objects.Count + 1}");
            WriteLine("0000000000 65535 f ");
            foreach (var offset in offsets.Skip(1))
            {
                WriteLine($"{offset:0000000000} 00000 n ");
            }

            WriteLine($"trailer << /Size {objects.Count + 1} /Root 1 0 R >>");
            WriteLine("startxref");
            WriteLine(xrefPosition.ToString(CultureInfo.InvariantCulture));
            WriteLine("%%EOF");

            return stream.ToArray();
        }

        private string BuildInvoiceContent(CommissionInvoiceDocument document)
        {
            var lines = BuildInvoiceTextLines(document);
            var builder = new StringBuilder();
            builder.AppendLine("BT");
            builder.AppendLine("/F1 12 Tf");
            var y = 760;
            foreach (var line in lines)
            {
                builder.AppendLine($"1 0 0 1 72 {y} Tm");
                builder.AppendLine($"({EscapePdfText(line)}) Tj");
                y -= 16;
            }

            builder.AppendLine("ET");
            return builder.ToString();
        }

        private List<string> BuildInvoiceTextLines(CommissionInvoiceDocument document)
        {
            var lines = new List<string>
            {
                $"{document.IssuerName} commission invoice",
                $"Invoice: {document.Summary.InvoiceNumber}",
                $"Issued: {document.Summary.IssuedOn:yyyy-MM-dd}",
                $"Period: {document.Summary.PeriodStart:yyyy-MM-dd} to {document.Summary.PeriodEnd:yyyy-MM-dd}",
                $"Status: {document.Summary.Status}",
                $"Seller: {document.SellerName}",
                $"Seller tax id: {(string.IsNullOrWhiteSpace(document.SellerTaxId) ? "N/A" : document.SellerTaxId)}",
                $"Seller address: {(string.IsNullOrWhiteSpace(document.SellerAddress) ? "N/A" : document.SellerAddress)}",
                $"Issuer tax id: {document.IssuerTaxId}",
                $"Issuer address: {document.IssuerAddress}",
                $"Net commission: {document.Summary.NetAmount.ToString("F2", CultureInfo.InvariantCulture)} {document.Summary.Currency}",
                $"{document.TaxLabel} {(document.Summary.TaxRate * 100).ToString("F0", CultureInfo.InvariantCulture)}%: {document.Summary.TaxAmount.ToString("F2", CultureInfo.InvariantCulture)} {document.Summary.Currency}",
                $"Total due: {document.Summary.TotalAmount.ToString("F2", CultureInfo.InvariantCulture)} {document.Summary.Currency}"
            };

            if (document.Summary.HasCorrections)
            {
                lines.Add("Contains corrections or credit notes for prior periods.");
            }

            lines.Add("Lines:");
            foreach (var line in document.Lines)
            {
                var correction = line.IsCorrection ? " (Correction)" : string.Empty;
                lines.Add($"{line.OrderNumber}/{line.SubOrderNumber} - {line.PayoutOn:yyyy-MM-dd} - {line.CommissionAmount.ToString("F2", CultureInfo.InvariantCulture)} {document.Summary.Currency} - {line.PayoutStatus}{correction}");
            }

            return lines;
        }

        private static string EscapePdfText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text
                .Replace("\\", "\\\\")
                .Replace("(", "\\(")
                .Replace(")", "\\)");
        }

        private TimeZoneInfo ResolveSettlementTimeZone(string? timeZone)
        {
            if (string.IsNullOrWhiteSpace(timeZone))
            {
                return TimeZoneInfo.Utc;
            }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZone);
            }
            catch
            {
                _logger.LogWarning("Unknown settlement timezone {TimeZoneId}, falling back to UTC.", timeZone);
                return TimeZoneInfo.Utc;
            }
        }

        private (DateTimeOffset Start, DateTimeOffset End) ResolveSettlementWindow(int year, int month)
        {
            var closeDay = Math.Clamp(_settlementOptions.CloseDay, 1, 28);
            var normalizedMonth = Math.Clamp(month, 1, 12);
            var normalizedYear = Math.Max(1, year);
            var anchorLocal = new DateTime(normalizedYear, normalizedMonth, closeDay, 0, 0, 0, DateTimeKind.Unspecified);
            var anchor = new DateTimeOffset(anchorLocal, _settlementTimeZone.GetUtcOffset(anchorLocal));
            var start = anchor.AddMonths(-1);
            return (start, anchor);
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

        private static List<OrderStatusChange> NormalizeStatusHistory(
            List<OrderStatusChange>? history,
            string currentStatus,
            string? tracking,
            string? carrier,
            DateTimeOffset now)
        {
            var normalizedStatus = OrderStatuses.Normalize(currentStatus);
            var normalizedHistory = (history ?? new List<OrderStatusChange>())
                .Where(h => !string.IsNullOrWhiteSpace(h.Status))
                .Select(h => new OrderStatusChange(
                    OrderStatuses.Normalize(h.Status),
                    h.ChangedOn == default ? now : h.ChangedOn,
                    string.IsNullOrWhiteSpace(h.TrackingNumber) ? null : h.TrackingNumber.Trim(),
                    string.IsNullOrWhiteSpace(h.TrackingCarrier) ? null : h.TrackingCarrier.Trim()))
                .OrderBy(h => h.ChangedOn)
                .ToList();

            var normalizedTracking = string.IsNullOrWhiteSpace(tracking) ? null : tracking.Trim();
            var normalizedCarrier = string.IsNullOrWhiteSpace(carrier) ? null : carrier.Trim();

            if (normalizedHistory.Count == 0 || !string.Equals(normalizedHistory.Last().Status, normalizedStatus, StringComparison.OrdinalIgnoreCase))
            {
                normalizedHistory.Add(new OrderStatusChange(normalizedStatus, now, normalizedTracking, normalizedCarrier));
            }

            return normalizedHistory;
        }

        private static List<OrderStatusChange> AppendStatusHistory(
            List<OrderStatusChange> history,
            string newStatus,
            string? tracking,
            string? carrier,
            DateTimeOffset changedOn)
        {
            var normalizedStatus = OrderStatuses.Normalize(newStatus);
            var normalizedTracking = string.IsNullOrWhiteSpace(tracking) ? null : tracking.Trim();
            var normalizedCarrier = string.IsNullOrWhiteSpace(carrier) ? null : carrier.Trim();

            if (history.Count > 0 && string.Equals(history.Last().Status, normalizedStatus, StringComparison.OrdinalIgnoreCase))
            {
                var last = history[^1];
                var trackingChanged = normalizedTracking != null && !string.Equals(last.TrackingNumber, normalizedTracking, StringComparison.OrdinalIgnoreCase);
                var carrierChanged = normalizedCarrier != null && !string.Equals(last.TrackingCarrier, normalizedCarrier, StringComparison.OrdinalIgnoreCase);
                if (trackingChanged || carrierChanged)
                {
                    history[^1] = last with { TrackingNumber = normalizedTracking, TrackingCarrier = normalizedCarrier };
                }

                return history;
            }

            history.Add(new OrderStatusChange(normalizedStatus, changedOn, normalizedTracking, normalizedCarrier));
            return history;
        }

        private OrderSubOrder NormalizeSubOrder(OrderSubOrder subOrder)
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
            var history = NormalizeStatusHistory(subOrder.StatusHistory, derivedStatus, tracking, carrier, DateTimeOffset.UtcNow);
            var providerId = string.IsNullOrWhiteSpace(subOrder.ShippingProviderId) ? null : subOrder.ShippingProviderId.Trim();
            var providerService = string.IsNullOrWhiteSpace(subOrder.ShippingProviderService) ? null : subOrder.ShippingProviderService.Trim();
            var providerReference = string.IsNullOrWhiteSpace(subOrder.ShippingProviderReference) ? null : subOrder.ShippingProviderReference.Trim();
            var trackingUrl = string.IsNullOrWhiteSpace(subOrder.TrackingUrl) ? null : subOrder.TrackingUrl.Trim();
            var shippingLabel = NormalizeShippingLabel(subOrder.ShippingLabel);

            return subOrder with
            {
                Items = normalizedItems,
                Status = string.IsNullOrWhiteSpace(derivedStatus) ? status : derivedStatus,
                TrackingNumber = tracking,
                TrackingCarrier = carrier,
                RefundedAmount = refunded,
                DeliveredOn = deliveredOn,
                Return = normalizedReturn,
                StatusHistory = history,
                ShippingProviderId = providerId,
                ShippingProviderService = providerService,
                ShippingProviderReference = providerReference,
                TrackingUrl = trackingUrl,
                ShippingLabel = shippingLabel
            };
        }

        private static ShippingLabelInfo? NormalizeShippingLabel(ShippingLabelInfo? label)
        {
            if (label == null || string.IsNullOrWhiteSpace(label.Base64Content))
            {
                return null;
            }

            var fileName = string.IsNullOrWhiteSpace(label.FileName) ? "shipping-label.pdf" : label.FileName.Trim();
            var contentType = string.IsNullOrWhiteSpace(label.ContentType) ? "application/pdf" : label.ContentType.Trim();
            var createdOn = label.CreatedOn == default ? DateTimeOffset.UtcNow : label.CreatedOn;
            var expiresOn = label.ExpiresOn == DateTimeOffset.MinValue ? null : label.ExpiresOn;
            return new ShippingLabelInfo(fileName, contentType, label.Base64Content.Trim(), createdOn, expiresOn);
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

        private ReturnRequest? NormalizeReturnRequest(ReturnRequest? request, List<OrderItemDetail> items)
        {
            return NormalizeReturnRequest(request, items, DateTimeOffset.UtcNow);
        }

        private ReturnRequest? NormalizeReturnRequest(ReturnRequest? request, List<OrderItemDetail> items, DateTimeOffset asOf)
        {
            if (request == null)
            {
                return null;
            }

            var normalizedStatus = ReturnRequestStatuses.Normalize(request.Status);
            var normalizedType = ReturnRequestTypes.Normalize(request.Type);
            var normalizedReason = request.Reason?.Trim() ?? string.Empty;
            var normalizedDescription = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            var requestedOn = request.RequestedOn == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : request.RequestedOn;
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

            var normalizedHistory = NormalizeReturnHistory(request.History, normalizedStatus, requestedOn);
            var normalizedMessages = NormalizeReturnMessages(request.Messages, requestedOn);
            var resolutionOutcome = string.IsNullOrWhiteSpace(request.ResolutionOutcome) ? null : request.ResolutionOutcome.Trim();
            var resolutionNote = string.IsNullOrWhiteSpace(request.ResolutionNote) ? null : request.ResolutionNote.Trim();
            var resolutionRefundAmount = request.ResolutionRefundAmount.HasValue ? Math.Max(0, request.ResolutionRefundAmount.Value) : (decimal?)null;
            var resolutionRefundReference = string.IsNullOrWhiteSpace(request.ResolutionRefundReference) ? null : request.ResolutionRefundReference.Trim();
            var resolutionRefundStatus = string.IsNullOrWhiteSpace(request.ResolutionRefundStatus) ? null : request.ResolutionRefundStatus.Trim();
            var resolvedOn = request.ResolvedOn == DateTimeOffset.MinValue ? null : request.ResolvedOn;
            var resolutionActor = string.IsNullOrWhiteSpace(request.ResolutionActor) ? "Seller" : request.ResolutionActor.Trim();
            var firstResponseDue = request.FirstResponseDueOn == DateTimeOffset.MinValue ? null : request.FirstResponseDueOn;
            var resolutionDue = request.ResolutionDueOn == DateTimeOffset.MinValue ? null : request.ResolutionDueOn;
            var firstResponded = request.FirstRespondedOn == DateTimeOffset.MinValue ? null : request.FirstRespondedOn;
            var slaBreachedOn = request.SlaBreachedOn == DateTimeOffset.MinValue ? null : request.SlaBreachedOn;

            var normalized = request with
            {
                Status = normalizedStatus,
                Type = normalizedType,
                Reason = normalizedReason,
                Description = normalizedDescription,
                Items = normalizedItems,
                CaseId = string.IsNullOrWhiteSpace(request.CaseId) ? BuildCaseId(request.SubOrderNumber, requestedOn) : request.CaseId.Trim(),
                RequestedOn = requestedOn,
                History = normalizedHistory,
                Messages = normalizedMessages,
                ResolutionOutcome = resolutionOutcome,
                ResolutionNote = resolutionNote,
                ResolutionRefundAmount = resolutionRefundAmount,
                ResolutionRefundReference = resolutionRefundReference,
                ResolutionRefundStatus = resolutionRefundStatus,
                ResolvedOn = resolvedOn,
                ResolutionActor = resolutionActor,
                FirstResponseDueOn = firstResponseDue,
                ResolutionDueOn = resolutionDue,
                FirstRespondedOn = firstResponded ?? ResolveFirstResponse(normalizedHistory, normalizedMessages, requestedOn),
                SlaBreached = request.SlaBreached,
                SlaBreachedOn = slaBreachedOn
            };

            return ApplySlaTracking(normalized, items, asOf);
        }

        private static List<ReturnRequestHistoryEntry> NormalizeReturnHistory(List<ReturnRequestHistoryEntry>? history, string currentStatus, DateTimeOffset requestedOn)
        {
            var entries = history?.ToList() ?? new List<ReturnRequestHistoryEntry>();
            if (entries.Count == 0)
            {
                entries.Add(new ReturnRequestHistoryEntry(currentStatus, "Buyer", requestedOn, "Case opened"));
            }

            var normalized = entries
                .Select(h => new ReturnRequestHistoryEntry(
                    ReturnRequestStatuses.Normalize(h.Status),
                    string.IsNullOrWhiteSpace(h.Actor) ? "System" : h.Actor.Trim(),
                    h.ChangedOn == DateTimeOffset.MinValue ? requestedOn : h.ChangedOn,
                    string.IsNullOrWhiteSpace(h.Note) ? null : h.Note.Trim()))
                .OrderBy(h => h.ChangedOn)
                .ToList();

            if (!normalized.Any(h => string.Equals(h.Status, currentStatus, StringComparison.OrdinalIgnoreCase)))
            {
                normalized.Add(new ReturnRequestHistoryEntry(currentStatus, "System", requestedOn, null));
            }

            return normalized;
        }

        private static List<ReturnRequestMessage> NormalizeReturnMessages(List<ReturnRequestMessage>? messages, DateTimeOffset requestedOn)
        {
            var normalized = messages?
                .Where(m => !string.IsNullOrWhiteSpace(m.Message))
                .Select(m => new ReturnRequestMessage(
                    string.IsNullOrWhiteSpace(m.Actor) ? "System" : m.Actor.Trim(),
                    m.Message.Trim(),
                    m.SentOn == DateTimeOffset.MinValue ? requestedOn : m.SentOn))
                .OrderBy(m => m.SentOn)
                .ToList() ?? new List<ReturnRequestMessage>();

            return normalized;
        }

        private static DateTimeOffset? ResolveFirstResponse(List<ReturnRequestHistoryEntry>? history, List<ReturnRequestMessage>? messages, DateTimeOffset requestedOn)
        {
            var candidates = new List<DateTimeOffset>();
            if (history != null)
            {
                candidates.AddRange(history
                    .Where(h => string.Equals(h.Actor, "Seller", StringComparison.OrdinalIgnoreCase))
                    .Select(h => h.ChangedOn));
            }

            if (messages != null)
            {
                candidates.AddRange(messages
                    .Where(m => string.Equals(m.Actor, "Seller", StringComparison.OrdinalIgnoreCase))
                    .Select(m => m.SentOn));
            }

            candidates = candidates
                .Where(d => d != DateTimeOffset.MinValue)
                .OrderBy(d => d)
                .ToList();

            var earliest = candidates.FirstOrDefault();
            return earliest == DateTimeOffset.MinValue ? null : earliest;
        }

        private CaseSlaRule ResolveCaseSlaRule(ReturnRequest request, List<OrderItemDetail> items)
        {
            var defaultRule = new CaseSlaRule
            {
                FirstResponseHours = Math.Max(1, _caseSlaOptions.DefaultFirstResponseHours),
                ResolutionHours = Math.Max(1, _caseSlaOptions.DefaultResolutionHours)
            };

            var category = items.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.Category))?.Category;
            if (!string.IsNullOrWhiteSpace(category)
                && _caseSlaOptions.CategoryRules.TryGetValue(category, out var categoryRule))
            {
                return new CaseSlaRule
                {
                    FirstResponseHours = categoryRule.FirstResponseHours ?? defaultRule.FirstResponseHours,
                    ResolutionHours = categoryRule.ResolutionHours ?? defaultRule.ResolutionHours
                };
            }

            return defaultRule;
        }

        private static bool IsSellerActionPending(string status)
        {
            var normalized = ReturnRequestStatuses.Normalize(status);
            return string.Equals(normalized, ReturnRequestStatuses.PendingSellerReview, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, ReturnRequestStatuses.Approved, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, ReturnRequestStatuses.SellerProposed, StringComparison.OrdinalIgnoreCase);
        }

        private ReturnRequest ApplySlaTracking(ReturnRequest request, List<OrderItemDetail> items, DateTimeOffset asOf, bool sellerResponded = false)
        {
            if (request == null)
            {
                return request!;
            }

            if (!_caseSlaOptions.Enabled)
            {
                return request with
                {
                    FirstResponseDueOn = null,
                    ResolutionDueOn = null,
                    SlaBreached = false,
                    SlaBreachedOn = null
                };
            }

            var rule = ResolveCaseSlaRule(request, items);
            var firstResponseDue = request.FirstResponseDueOn ?? request.RequestedOn.AddHours(rule.FirstResponseHours ?? _caseSlaOptions.DefaultFirstResponseHours);
            var resolutionDue = request.ResolutionDueOn ?? request.RequestedOn.AddHours(rule.ResolutionHours ?? _caseSlaOptions.DefaultResolutionHours);
            var firstRespondedOn = request.FirstRespondedOn;
            if (!firstRespondedOn.HasValue && sellerResponded)
            {
                firstRespondedOn = asOf;
            }

            var slaBreached = request.SlaBreached;
            var slaBreachedOn = request.SlaBreachedOn;
            var sellerPending = IsSellerActionPending(request.Status);

            if (!firstRespondedOn.HasValue && asOf > firstResponseDue)
            {
                slaBreached = true;
                slaBreachedOn ??= asOf;
            }
            else if (firstRespondedOn.HasValue && firstRespondedOn.Value > firstResponseDue)
            {
                slaBreached = true;
                slaBreachedOn ??= firstRespondedOn;
            }

            if (resolutionDue != default)
            {
                if (request.ResolvedOn.HasValue)
                {
                    if (request.ResolvedOn.Value > resolutionDue)
                    {
                        slaBreached = true;
                        slaBreachedOn ??= request.ResolvedOn;
                    }
                }
                else if (sellerPending && asOf > resolutionDue)
                {
                    slaBreached = true;
                    slaBreachedOn ??= asOf;
                }
            }

            return request with
            {
                FirstResponseDueOn = firstResponseDue,
                ResolutionDueOn = resolutionDue,
                FirstRespondedOn = firstRespondedOn,
                SlaBreached = slaBreached,
                SlaBreachedOn = slaBreachedOn
            };
        }

        private static ReturnRequest AppendReturnHistory(ReturnRequest request, string status, string actor, string? note, DateTimeOffset changedOn)
        {
            var history = request.History?.ToList() ?? new List<ReturnRequestHistoryEntry>();
            var entry = new ReturnRequestHistoryEntry(
                ReturnRequestStatuses.Normalize(status),
                string.IsNullOrWhiteSpace(actor) ? "System" : actor.Trim(),
                changedOn == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : changedOn,
                string.IsNullOrWhiteSpace(note) ? null : note.Trim());

            history.Add(entry);
            history = history.OrderBy(h => h.ChangedOn).ToList();
            return request with { History = history, Status = ReturnRequestStatuses.Normalize(status) };
        }

        private static ReturnRequest AppendReturnMessage(ReturnRequest request, string actor, string message, DateTimeOffset sentOn)
        {
            var messages = request.Messages?.ToList() ?? new List<ReturnRequestMessage>();
            var normalizedMessage = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();
            if (normalizedMessage.Length == 0)
            {
                return request;
            }

            messages.Add(new ReturnRequestMessage(
                string.IsNullOrWhiteSpace(actor) ? "System" : actor.Trim(),
                normalizedMessage,
                sentOn == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : sentOn));

            messages = messages
                .Where(m => !string.IsNullOrWhiteSpace(m.Message))
                .OrderBy(m => m.SentOn)
                .ToList();

            return request with { Messages = messages };
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
