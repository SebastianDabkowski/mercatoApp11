using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Services;

public record UserDeletionResult(bool Success, string? Error = null, List<string>? BlockingReasons = null);

public class UserAccountDeletionService
{
    private const string DeletedUserName = "Deleted user";
    private const string DeletedSellerName = "Deleted seller";
    private const string RedactedValue = "Removed";

    private static readonly HashSet<string> ActiveStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        OrderStatuses.New,
        OrderStatuses.Paid,
        OrderStatuses.Preparing,
        OrderStatuses.Shipped
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly CriticalActionAuditService _criticalAudit;
    private readonly ILogger<UserAccountDeletionService> _logger;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public UserAccountDeletionService(
        ApplicationDbContext dbContext,
        TimeProvider timeProvider,
        CriticalActionAuditService criticalAudit,
        ILogger<UserAccountDeletionService> logger)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _criticalAudit = criticalAudit;
        _logger = logger;
    }

    public async Task<UserDeletionResult> DeleteAsync(string userId, string? actorUserId, string? actorName, CancellationToken cancellationToken = default)
    {
        var normalizedActor = string.IsNullOrWhiteSpace(actorName) ? "User request" : actorName.Trim();

        if (string.IsNullOrWhiteSpace(userId))
        {
            await _criticalAudit.RecordAsync(
                new CriticalActionAuditEntry("AccountDeletion", "User", null, normalizedActor, actorUserId, false, "User id is required."),
                cancellationToken);
            return new UserDeletionResult(false, "User id is required.");
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null)
        {
            await _criticalAudit.RecordAsync(
                new CriticalActionAuditEntry("AccountDeletion", "User", userId, normalizedActor, actorUserId, false, "User not found."),
                cancellationToken);
            return new UserDeletionResult(false, "User not found.");
        }

        var blocking = await FindBlockingReasonsAsync(userId, cancellationToken);
        if (blocking.Count > 0)
        {
            await _criticalAudit.RecordAsync(
                new CriticalActionAuditEntry("AccountDeletion", "User", userId, normalizedActor, actorUserId, false, "Blocked by active items."),
                cancellationToken);
            return new UserDeletionResult(false, "Account deletion is blocked by active items.", blocking);
        }

        IDbContextTransaction? transaction = null;
        if (_dbContext.Database.IsRelational())
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        var anonymizedEmail = BuildAnonymizedEmail(user.Id);

        await AnonymizeOrdersAsync(user.Id, anonymizedEmail, cancellationToken);
        await AnonymizeSavedAddressesAsync(user.Id, cancellationToken);
        await AnonymizeReviewsAsync(user.Id, cancellationToken);
        await AnonymizeQuestionsAsync(user.Id, cancellationToken);
        await AnonymizeRatingsAsync(user.Id, cancellationToken);
        await AnonymizeLoginAuditsAsync(user.Id, anonymizedEmail, cancellationToken);
        await RemoveExternalCredentialsAsync(user.Id, cancellationToken);

        ApplyUserAnonymization(user, anonymizedEmail);

        _dbContext.UserAdminAudits.Add(new UserAdminAudit
        {
            UserId = user.Id,
            ActorUserId = actorUserId,
            ActorName = normalizedActor,
            Action = "Deleted",
            Reason = "Account deletion with anonymization",
            CreatedOn = _timeProvider.GetUtcNow()
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
            await transaction.DisposeAsync();
        }

        await _criticalAudit.RecordAsync(
            new CriticalActionAuditEntry("AccountDeletion", "User", user.Id, normalizedActor, actorUserId, true, "Account anonymized and disabled."),
            cancellationToken);

        _logger.LogInformation("Account {UserId} anonymized and disabled by {Actor}.", user.Id, actorName ?? "User");
        return new UserDeletionResult(true);
    }

    private async Task<List<string>> FindBlockingReasonsAsync(string userId, CancellationToken cancellationToken)
    {
        var reasons = new List<string>();

        var buyerOrders = await _dbContext.Orders.AsNoTracking()
            .Where(o => o.BuyerId == userId)
            .ToListAsync(cancellationToken);

        if (buyerOrders.Any(o => ActiveStatuses.Contains(OrderStatuses.Normalize(o.Status))))
        {
            reasons.Add("You have active orders that must be completed first.");
        }

        if (HasOpenCases(buyerOrders, userId, asSeller: false))
        {
            reasons.Add("Resolve open disputes or return cases before deleting your account.");
        }

        var sellerOrders = await _dbContext.Orders.AsNoTracking()
            .Where(o => o.DetailsJson.Contains($"\"sellerId\":\"{userId}\""))
            .ToListAsync(cancellationToken);

        if (HasOpenCases(sellerOrders, userId, asSeller: true))
        {
            reasons.Add("Resolve open disputes or return cases before deleting your account.");
        }

        return reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private bool HasOpenCases(IEnumerable<OrderRecord> orders, string userId, bool asSeller)
    {
        foreach (var order in orders)
        {
            var details = NormalizeDetails(DeserializeDetails(order.DetailsJson));
            foreach (var subOrder in details.SubOrders)
            {
                if (asSeller && !string.Equals(subOrder.SellerId, userId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (subOrder.Return != null && ReturnRequestStatuses.IsOpen(subOrder.Return.Status))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private async Task AnonymizeOrdersAsync(string userId, string anonymizedEmail, CancellationToken cancellationToken)
    {
        var orders = await _dbContext.Orders
            .Where(o => o.BuyerId == userId || o.DetailsJson.Contains($"\"sellerId\":\"{userId}\""))
            .ToListAsync(cancellationToken);

        foreach (var order in orders)
        {
            if (string.Equals(order.BuyerId, userId, StringComparison.OrdinalIgnoreCase))
            {
                order.BuyerEmail = anonymizedEmail;
                order.BuyerName = DeletedUserName;
                order.DeliveryAddressJson = JsonSerializer.Serialize(BuildAnonymousAddress(), _serializerOptions);
            }

            var details = ScrubSellerDetails(NormalizeDetails(DeserializeDetails(order.DetailsJson)), userId);
            order.DetailsJson = JsonSerializer.Serialize(details, _serializerOptions);
        }
    }

    private async Task AnonymizeSavedAddressesAsync(string userId, CancellationToken cancellationToken)
    {
        var addresses = await _dbContext.ShippingAddresses
            .Where(a => a.UserId == userId)
            .ToListAsync(cancellationToken);

        foreach (var address in addresses)
        {
            address.Recipient = DeletedUserName;
            address.Line1 = RedactedValue;
            address.Line2 = null;
            address.City = RedactedValue;
            address.State = RedactedValue;
            address.PostalCode = RedactedValue;
            address.Country = RedactedValue;
            address.Phone = string.Empty;
            address.UpdatedOn = _timeProvider.GetUtcNow();
        }
    }

    private async Task AnonymizeReviewsAsync(string userId, CancellationToken cancellationToken)
    {
        var reviews = await _dbContext.ProductReviews
            .Where(r => r.BuyerId == userId)
            .ToListAsync(cancellationToken);

        foreach (var review in reviews)
        {
            review.BuyerName = DeletedUserName;
        }
    }

    private async Task AnonymizeQuestionsAsync(string userId, CancellationToken cancellationToken)
    {
        var questions = await _dbContext.ProductQuestions
            .Where(q => q.BuyerId == userId)
            .ToListAsync(cancellationToken);

        foreach (var question in questions)
        {
            question.BuyerName = DeletedUserName;
        }
    }

    private async Task AnonymizeRatingsAsync(string userId, CancellationToken cancellationToken)
    {
        var ratings = await _dbContext.SellerRatings
            .Where(r => r.BuyerId == userId || r.SellerId == userId)
            .ToListAsync(cancellationToken);

        foreach (var rating in ratings)
        {
            if (string.Equals(rating.BuyerId, userId, StringComparison.OrdinalIgnoreCase))
            {
                rating.BuyerName = DeletedUserName;
            }

            if (string.Equals(rating.SellerId, userId, StringComparison.OrdinalIgnoreCase))
            {
                rating.SellerName = DeletedSellerName;
            }
        }
    }

    private async Task AnonymizeLoginAuditsAsync(string userId, string anonymizedEmail, CancellationToken cancellationToken)
    {
        var audits = await _dbContext.LoginAudits
            .Where(a => a.UserId == userId)
            .ToListAsync(cancellationToken);

        foreach (var audit in audits)
        {
            audit.Email = anonymizedEmail;
            audit.IpAddress = null;
            audit.UserAgent = null;
        }
    }

    private async Task RemoveExternalCredentialsAsync(string userId, CancellationToken cancellationToken)
    {
        var logins = await _dbContext.Set<IdentityUserLogin<string>>()
            .Where(l => l.UserId == userId)
            .ToListAsync(cancellationToken);
        var tokens = await _dbContext.Set<IdentityUserToken<string>>()
            .Where(t => t.UserId == userId)
            .ToListAsync(cancellationToken);

        _dbContext.Set<IdentityUserLogin<string>>().RemoveRange(logins);
        _dbContext.Set<IdentityUserToken<string>>().RemoveRange(tokens);
    }

    private void ApplyUserAnonymization(ApplicationUser user, string anonymizedEmail)
    {
        var normalizedEmail = anonymizedEmail.ToUpperInvariant();

        user.Email = anonymizedEmail;
        user.NormalizedEmail = normalizedEmail;
        user.UserName = anonymizedEmail;
        user.NormalizedUserName = normalizedEmail;
        user.FullName = DeletedUserName;
        user.PhoneNumber = null;
        user.PhoneNumberConfirmed = false;
        user.TwoFactorEnabled = false;
        user.EmailConfirmed = false;
        user.Address = RedactedValue;
        user.Country = RedactedValue;
        user.BusinessName = null;
        user.TaxId = null;
        user.CompanyRegistrationNumber = null;
        user.PersonalIdNumber = null;
        user.VerificationContactName = null;
        user.ContactEmail = string.Empty;
        user.ContactPhone = string.Empty;
        user.ContactWebsite = string.Empty;
        user.StoreDescription = string.Empty;
        user.PayoutMethod = string.Empty;
        user.PayoutSchedule = string.Empty;
        user.PayoutAccount = string.Empty;
        user.PayoutBankAccount = null;
        user.PayoutBankRouting = null;
        user.PayoutUpdatedOn = null;
        user.CartData = null;
        user.LastLoginIp = null;
        user.LastLoginOn = null;
        user.BlockedOn = null;
        user.BlockedByName = null;
        user.BlockedByUserId = null;
        user.BlockReason = null;
        user.AccessFailedCount = 0;
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;
        user.PasswordHash = null;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        user.TwoFactorEnabledOn = null;
    }

    private OrderDetailsPayload ScrubSellerDetails(OrderDetailsPayload details, string userId)
    {
        var updatedItems = details.Items
            .Select(i => string.Equals(i.SellerId, userId, StringComparison.OrdinalIgnoreCase)
                ? i with { SellerName = DeletedSellerName }
                : i)
            .ToList();

        var updatedShipping = details.Shipping
            .Select(s => string.Equals(s.SellerId, userId, StringComparison.OrdinalIgnoreCase)
                ? s with { SellerName = DeletedSellerName }
                : s)
            .ToList();

        var updatedSubOrders = details.SubOrders
            .Select(sub =>
            {
                var subItems = sub.Items
                    .Select(i => string.Equals(i.SellerId, userId, StringComparison.OrdinalIgnoreCase)
                        ? i with { SellerName = DeletedSellerName }
                        : i)
                    .ToList();

                var shipping = string.Equals(sub.ShippingDetail.SellerId, userId, StringComparison.OrdinalIgnoreCase)
                    ? sub.ShippingDetail with { SellerName = DeletedSellerName }
                    : sub.ShippingDetail;

                var updated = sub with
                {
                    Items = subItems,
                    ShippingDetail = shipping
                };

                return string.Equals(sub.SellerId, userId, StringComparison.OrdinalIgnoreCase)
                    ? updated with { SellerName = DeletedSellerName }
                    : updated;
            })
            .ToList();

        return details with
        {
            Items = updatedItems,
            Shipping = updatedShipping,
            SubOrders = updatedSubOrders
        };
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

    private OrderDetailsPayload NormalizeDetails(OrderDetailsPayload details)
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

    private DeliveryAddress BuildAnonymousAddress() =>
        new(DeletedUserName, RedactedValue, null, RedactedValue, RedactedValue, RedactedValue, string.Empty, null);

    private static string BuildAnonymizedEmail(string userId) => $"deleted+{userId}@example.invalid";
}
