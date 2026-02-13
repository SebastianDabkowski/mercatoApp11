using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Services
{
    public static class OrderStatuses
    {
        public const string Confirmed = "Confirmed";
    }

    public class OrderRecord
    {
        public int Id { get; set; }

        public string OrderNumber { get; set; } = string.Empty;

        public string Status { get; set; } = OrderStatuses.Confirmed;

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

    public record OrderItemDetail(int ProductId, string Name, string Variant, int Quantity, decimal UnitPrice, decimal LineTotal, string SellerId, string SellerName);

    public record OrderShippingDetail(string SellerId, string SellerName, string MethodId, string MethodLabel, decimal Cost, string? Description);

    public record OrderDetailsPayload(List<OrderItemDetail> Items, List<OrderShippingDetail> Shipping, int TotalQuantity, decimal DiscountTotal = 0, string? PromoCode = null);

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
        List<OrderShippingDetail> Shipping);

    public record OrderSummaryView(int Id, string OrderNumber, DateTimeOffset CreatedOn, string Status, decimal GrandTotal, int TotalQuantity);

    public record OrderCreationResult(OrderRecord Order, bool Created);

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

            var details = BuildDetailsPayload(quote);
            var order = new OrderRecord
            {
                OrderNumber = GenerateOrderNumber(),
                Status = OrderStatuses.Confirmed,
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
            var discountTotal = details.DiscountTotal > 0
                ? details.DiscountTotal
                : Math.Max(0, order.ItemsSubtotal + order.ShippingTotal - order.GrandTotal);
            var promoCode = string.IsNullOrWhiteSpace(details.PromoCode) ? null : details.PromoCode;

            return new OrderView(
                order.Id,
                order.OrderNumber,
                order.Status,
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
                details.Shipping);
        }

        public async Task<List<OrderSummaryView>> GetSummariesForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
        {
            return await _dbContext.Orders.AsNoTracking()
                .Where(o => o.BuyerId == buyerId)
                .OrderByDescending(o => o.CreatedOn)
                .Select(o => new OrderSummaryView(o.Id, o.OrderNumber, o.CreatedOn, o.Status, o.GrandTotal, o.TotalQuantity))
                .ToListAsync(cancellationToken);
        }

        private OrderDetailsPayload BuildDetailsPayload(ShippingQuote quote)
        {
            var items = new List<OrderItemDetail>();
            foreach (var group in quote.Summary.SellerGroups)
            {
                foreach (var item in group.Items)
                {
                    items.Add(new OrderItemDetail(
                        item.Product.Id,
                        item.Product.Title,
                        item.VariantLabel,
                        item.Quantity,
                        item.UnitPrice,
                        item.LineTotal,
                        group.SellerId,
                        group.SellerName));
                }
            }

            var shipping = new List<OrderShippingDetail>();
            foreach (var options in quote.SellerOptions)
            {
                var selected = quote.SelectedMethods.TryGetValue(options.SellerId, out var selection) ? selection : options.Options.FirstOrDefault()?.Id;
                var match = options.Options.FirstOrDefault(o => string.Equals(o.Id, selected, StringComparison.OrdinalIgnoreCase))
                    ?? options.Options.First();
                shipping.Add(new OrderShippingDetail(
                    options.SellerId,
                    options.SellerName,
                    match.Id,
                    match.Label,
                    match.Cost,
                    match.Description));
            }

            return new OrderDetailsPayload(items, shipping, quote.Summary.TotalQuantity, quote.Summary.DiscountTotal, quote.Summary.AppliedPromoCode);
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
                    return details;
                }
            }
            catch
            {
            }

            return new OrderDetailsPayload(new List<OrderItemDetail>(), new List<OrderShippingDetail>(), 0, 0, null);
        }

        private static string GenerateOrderNumber()
        {
            var random = Random.Shared.Next(1000, 9999);
            return $"ORD-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{random}";
        }
    }
}
