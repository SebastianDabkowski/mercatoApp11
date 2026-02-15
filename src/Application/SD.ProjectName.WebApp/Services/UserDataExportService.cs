using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Services;

public record UserDataExportResult(byte[] Content, string ContentType, string FileName);

public record UserDataExportDocument(
    ExportMeta Meta,
    ProfileExport Profile,
    List<ConsentExport> Consents,
    List<ShippingAddressExport> Addresses,
    OrderExportSection Orders);

public record ExportMeta(DateTimeOffset GeneratedOn, string Format, string Version);

public record ProfileExport(
    string Id,
    string Email,
    string? PhoneNumber,
    string FullName,
    string AccountType,
    string AccountStatus,
    string Country,
    string Address,
    string? BusinessName,
    string? TaxId,
    string? CompanyRegistrationNumber,
    string? PersonalIdNumber,
    string? ContactEmail,
    string? ContactPhone,
    string? ContactWebsite,
    string StoreDescription,
    string PayoutMethod,
    string PayoutSchedule,
    DateTimeOffset? EmailVerifiedOn,
    DateTimeOffset? LastLoginOn,
    string? LastLoginIp,
    DateTimeOffset? KycSubmittedOn,
    DateTimeOffset? KycApprovedOn,
    string? StoreOwnerId,
    string SellerType,
    string KycStatus,
    string OnboardingStatus,
    int OnboardingStep);

public record ConsentExport(string Type, bool Granted, DateTimeOffset DecidedOn, string VersionTag, DateTimeOffset EffectiveFrom);

public record ShippingAddressExport(
    string Recipient,
    string Line1,
    string? Line2,
    string City,
    string State,
    string PostalCode,
    string Country,
    string Phone,
    bool IsDefault,
    DateTimeOffset CreatedOn,
    DateTimeOffset UpdatedOn);

public record OrderMessageExport(string SubOrderNumber, string SenderRole, string Message, DateTimeOffset SentOn);

public record BuyerOrderExport(
    string OrderNumber,
    DateTimeOffset CreatedOn,
    string Status,
    string PaymentStatus,
    string? PaymentStatusMessage,
    string PaymentMethod,
    decimal GrandTotal,
    int TotalQuantity,
    DeliveryAddress? DeliveryAddress,
    List<OrderSubOrder> SubOrders,
    List<OrderMessageExport> Messages);

public record SellerOrderExport(
    string OrderNumber,
    string SubOrderNumber,
    DateTimeOffset CreatedOn,
    string Status,
    decimal GrandTotal,
    int TotalQuantity,
    OrderShippingDetail Shipping,
    List<OrderItemDetail> Items,
    ReturnRequest? ReturnRequest,
    List<OrderMessageExport> Messages,
    string PaymentStatus,
    string? PaymentStatusMessage,
    decimal RefundedAmount);

public record OrderExportSection(List<BuyerOrderExport> Buyer, List<SellerOrderExport> Seller);

public class UserDataExportService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IConsentService _consents;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UserDataExportService> _logger;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public UserDataExportService(
        ApplicationDbContext dbContext,
        IConsentService consents,
        TimeProvider timeProvider,
        ILogger<UserDataExportService> logger)
    {
        _dbContext = dbContext;
        _consents = consents;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<UserDataExportResult> GenerateAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null)
        {
            throw new InvalidOperationException("User not found.");
        }

        var requestLoggedAt = _timeProvider.GetUtcNow();
        await LogAuditAsync(user, "Data export requested", "User initiated personal data export", requestLoggedAt, cancellationToken);

        var consents = await _consents.GetUserConsentsAsync(userId, cancellationToken);
        var addresses = await _dbContext.ShippingAddresses.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.CreatedOn)
            .ToListAsync(cancellationToken);

        var buyerOrders = await LoadBuyerOrdersAsync(user, cancellationToken);
        var sellerOrders = await LoadSellerOrdersAsync(user, cancellationToken);

        var payload = new UserDataExportDocument(
            new ExportMeta(_timeProvider.GetUtcNow(), "json.zip", "1.0"),
            MapProfile(user),
            consents.Select(MapConsent).ToList(),
            addresses.Select(MapAddress).ToList(),
            new OrderExportSection(buyerOrders, sellerOrders));

        var fileName = $"user-data-{user.Id}-{requestLoggedAt:yyyyMMddHHmmss}.zip";
        var content = BuildArchive(payload);

        var completedAt = _timeProvider.GetUtcNow();
        await LogAuditAsync(user, "Data export completed", "Personal data export generated", completedAt, cancellationToken);

        _logger.LogInformation("Generated personal data export for user {UserId}", user.Id);
        return new UserDataExportResult(content, "application/zip", fileName);
    }

    private async Task<List<BuyerOrderExport>> LoadBuyerOrdersAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var orders = await _dbContext.Orders.AsNoTracking()
            .Where(o => o.BuyerId == user.Id)
            .OrderByDescending(o => o.CreatedOn)
            .ToListAsync(cancellationToken);

        var result = new List<BuyerOrderExport>();
        foreach (var order in orders)
        {
            var details = DeserializeDetails(order.DetailsJson);
            var address = DeserializeAddress(order.DeliveryAddressJson);

            result.Add(new BuyerOrderExport(
                order.OrderNumber,
                order.CreatedOn,
                order.Status,
                details.PaymentStatus,
                details.PaymentStatusMessage,
                order.PaymentMethodLabel,
                order.GrandTotal,
                order.TotalQuantity,
                address,
                details.SubOrders ?? new List<OrderSubOrder>(),
                MapMessages(details.Messages)));
        }

        return result;
    }

    private async Task<List<SellerOrderExport>> LoadSellerOrdersAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var sellerToken = $"\"sellerId\":\"{user.Id}\"";
        var orders = await _dbContext.Orders.AsNoTracking()
            .Where(o => o.DetailsJson.Contains(sellerToken))
            .OrderByDescending(o => o.CreatedOn)
            .ToListAsync(cancellationToken);

        var exports = new List<SellerOrderExport>();
        foreach (var order in orders)
        {
            var details = DeserializeDetails(order.DetailsJson);
            foreach (var subOrder in (details.SubOrders ?? new List<OrderSubOrder>())
                .Where(s => string.Equals(s.SellerId, user.Id, StringComparison.OrdinalIgnoreCase)))
            {
                var messages = MapMessages(details.Messages)
                    .Where(m => string.Equals(m.SubOrderNumber, subOrder.SubOrderNumber, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                exports.Add(new SellerOrderExport(
                    order.OrderNumber,
                    subOrder.SubOrderNumber,
                    order.CreatedOn,
                    subOrder.Status,
                    subOrder.GrandTotal,
                    subOrder.TotalQuantity,
                    subOrder.ShippingDetail,
                    subOrder.Items,
                    subOrder.Return,
                    messages,
                    details.PaymentStatus,
                    details.PaymentStatusMessage,
                    subOrder.RefundedAmount));
            }
        }

        return exports;
    }

    private ProfileExport MapProfile(ApplicationUser user) =>
        new(
            user.Id,
            user.Email ?? string.Empty,
            user.PhoneNumber,
            user.FullName,
            user.AccountType,
            user.AccountStatus,
            user.Country,
            user.Address,
            user.BusinessName,
            user.TaxId,
            user.CompanyRegistrationNumber,
            user.PersonalIdNumber,
            user.ContactEmail,
            user.ContactPhone,
            user.ContactWebsite,
            user.StoreDescription,
            user.PayoutMethod,
            user.PayoutSchedule,
            user.EmailVerifiedOn,
            user.LastLoginOn,
            user.LastLoginIp,
            user.KycSubmittedOn,
            user.KycApprovedOn,
            user.StoreOwnerId,
            user.SellerType,
            user.KycStatus,
            user.OnboardingStatus,
            user.OnboardingStep);

    private static ConsentExport MapConsent(UserConsentSnapshot snapshot) =>
        new(
            snapshot.ConsentType,
            snapshot.Granted,
            snapshot.DecidedOn,
            snapshot.Version.VersionTag,
            snapshot.Version.EffectiveFrom);

    private static ShippingAddressExport MapAddress(ShippingAddress address) =>
        new(
            address.Recipient,
            address.Line1,
            address.Line2,
            address.City,
            address.State,
            address.PostalCode,
            address.Country,
            address.Phone,
            address.IsDefault,
            address.CreatedOn,
            address.UpdatedOn);

    private List<OrderMessageExport> MapMessages(List<OrderMessage>? messages)
    {
        if (messages == null || messages.Count == 0)
        {
            return new List<OrderMessageExport>();
        }

        return messages
            .Where(m => !m.IsHidden)
            .Select(m => new OrderMessageExport(m.SubOrderNumber, m.SenderRole, m.Message, m.SentOn))
            .OrderBy(m => m.SentOn)
            .ToList();
    }

    private OrderDetailsPayload DeserializeDetails(string payload)
    {
        try
        {
            var details = JsonSerializer.Deserialize<OrderDetailsPayload>(payload, _serializerOptions);
            if (details != null)
            {
                return Normalize(details);
            }
        }
        catch
        {
            // ignored
        }

        return new OrderDetailsPayload(
            new List<OrderItemDetail>(),
            new List<OrderShippingDetail>(),
            0,
            0,
            null,
            new List<OrderSubOrder>(),
            new List<EscrowAllocation>(),
            PaymentStatuses.Pending,
            PaymentStatusMapper.BuildBuyerMessage(PaymentStatuses.Pending),
            0,
            new List<OrderMessage>());
    }

    private OrderDetailsPayload Normalize(OrderDetailsPayload details)
    {
        return details with
        {
            SubOrders = details.SubOrders ?? new List<OrderSubOrder>(),
            Shipping = details.Shipping ?? new List<OrderShippingDetail>(),
            Items = details.Items ?? new List<OrderItemDetail>(),
            Messages = details.Messages ?? new List<OrderMessage>(),
            Escrow = details.Escrow ?? new List<EscrowAllocation>()
        };
    }

    private DeliveryAddress? DeserializeAddress(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<DeliveryAddress>(payload, _serializerOptions);
        }
        catch
        {
            return null;
        }
    }

    private byte[] BuildArchive(UserDataExportDocument payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("user-data.json", CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(json);
        }

        return buffer.ToArray();
    }

    private async Task LogAuditAsync(ApplicationUser user, string action, string reason, DateTimeOffset timestamp, CancellationToken cancellationToken)
    {
        _dbContext.UserAdminAudits.Add(new UserAdminAudit
        {
            UserId = user.Id,
            ActorUserId = user.Id,
            ActorName = string.IsNullOrWhiteSpace(user.FullName) ? user.Email ?? "User" : user.FullName,
            Action = action,
            Reason = reason,
            CreatedOn = timestamp
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
