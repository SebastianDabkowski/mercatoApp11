using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.Tests.Products;

public class UserAccountDeletionServiceTests
{
    private readonly JsonSerializerOptions _serializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public async Task DeleteAsync_BlocksWhenOpenDisputeExists()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"delete-block-{Guid.NewGuid()}")
            .Options;

        await using var context = new ApplicationDbContext(options);
        var service = new UserAccountDeletionService(context, TimeProvider.System, NullLogger<UserAccountDeletionService>.Instance);

        var user = new ApplicationUser
        {
            Id = "buyer-1",
            Email = "buyer@test.com",
            UserName = "buyer@test.com",
            FullName = "Buyer Name",
            Address = "123 Main",
            Country = "US"
        };
        context.Users.Add(user);

        var subOrder = new OrderSubOrder(
            "SO-1",
            "seller-1",
            "Seller One",
            10,
            5,
            0,
            15,
            1,
            new List<OrderItemDetail> { new(1, "Product", "Default", 1, 10, 10, "seller-1", "Seller One") },
            new OrderShippingDetail("seller-1", "Seller One", "std", "Standard", 5, "desc"),
            OrderStatuses.Paid,
            null,
            null,
            0,
            DateTimeOffset.UtcNow,
            new ReturnRequest(
                "SO-1",
                ReturnRequestStatuses.PendingSellerReview,
                "Damaged",
                DateTimeOffset.UtcNow,
                new List<ReturnRequestItem> { new(1, 1) }));

        var details = new OrderDetailsPayload(
            new List<OrderItemDetail> { new(1, "Product", "Default", 1, 10, 10, "seller-1", "Seller One") },
            new List<OrderShippingDetail> { new("seller-1", "Seller One", "std", "Standard", 5, "desc") },
            1,
            0,
            null,
            new List<OrderSubOrder> { subOrder },
            new List<EscrowAllocation>(),
            PaymentStatuses.Paid,
            "Paid",
            0,
            new List<OrderMessage>());

        context.Orders.Add(new OrderRecord
        {
            OrderNumber = "ORD-1",
            Status = OrderStatuses.Paid,
            BuyerId = user.Id,
            BuyerEmail = user.Email!,
            BuyerName = user.FullName,
            PaymentMethodId = "card",
            PaymentMethodLabel = "Card",
            ItemsSubtotal = 10,
            ShippingTotal = 5,
            GrandTotal = 15,
            TotalQuantity = 1,
            CreatedOn = DateTimeOffset.UtcNow,
            DeliveryAddressJson = JsonSerializer.Serialize(
                new DeliveryAddress(user.FullName, "123 Main", null, "City", "ST", "12345", "US", "555-1234"),
                _serializerOptions),
            DetailsJson = JsonSerializer.Serialize(details, _serializerOptions)
        });

        await context.SaveChangesAsync();

        var result = await service.DeleteAsync(user.Id, user.Id, "User", CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.BlockingReasons);
        Assert.Contains(result.BlockingReasons!, r => r.Contains("disputes", StringComparison.OrdinalIgnoreCase));

        var persisted = await context.Users.FirstAsync();
        Assert.Equal("Buyer Name", persisted.FullName);
    }

    [Fact]
    public async Task DeleteAsync_AnonymizesUserAndRelatedRecords()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"delete-success-{Guid.NewGuid()}")
            .Options;

        await using var context = new ApplicationDbContext(options);
        var service = new UserAccountDeletionService(context, TimeProvider.System, NullLogger<UserAccountDeletionService>.Instance);

        var user = new ApplicationUser
        {
            Id = "user-1",
            Email = "user@test.com",
            UserName = "user@test.com",
            PasswordHash = "hash",
            FullName = "User Name",
            Address = "123 Main",
            Country = "US",
            BusinessName = "Biz",
            ContactEmail = "contact@test.com",
            ContactPhone = "123",
            ContactWebsite = "site",
            StoreDescription = "desc",
            PayoutAccount = "acct",
            PayoutMethod = "Bank",
            PayoutSchedule = "Weekly",
            LastLoginIp = "127.0.0.1",
            LastLoginOn = DateTimeOffset.UtcNow
        };
        context.Users.Add(user);

        context.ShippingAddresses.Add(new ShippingAddress
        {
            UserId = user.Id,
            Recipient = "User Name",
            Line1 = "123 Main",
            City = "City",
            State = "ST",
            PostalCode = "12345",
            Country = "US",
            Phone = "555-1234",
            IsDefault = true,
            CreatedOn = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedOn = DateTimeOffset.UtcNow.AddDays(-1)
        });

        var subOrder = new OrderSubOrder(
            "SO-1",
            user.Id,
            "Seller Name",
            10,
            5,
            0,
            15,
            1,
            new List<OrderItemDetail> { new(1, "Product", "Default", 1, 10, 10, user.Id, "Seller Name") },
            new OrderShippingDetail(user.Id, "Seller Name", "std", "Standard", 5, "desc"),
            OrderStatuses.Delivered,
            null,
            null,
            0,
            DateTimeOffset.UtcNow,
            null,
            new List<OrderStatusChange>());

        var details = new OrderDetailsPayload(
            new List<OrderItemDetail> { new(1, "Product", "Default", 1, 10, 10, user.Id, "Seller Name") },
            new List<OrderShippingDetail> { new(user.Id, "Seller Name", "std", "Standard", 5, "desc") },
            1,
            0,
            null,
            new List<OrderSubOrder> { subOrder },
            new List<EscrowAllocation>(),
            PaymentStatuses.Paid,
            "Paid",
            0,
            new List<OrderMessage> { new(Guid.NewGuid(), "SO-1", user.Id, "Seller", user.Id, "hello", DateTimeOffset.UtcNow) });

        context.Orders.Add(new OrderRecord
        {
            OrderNumber = "ORD-2",
            Status = OrderStatuses.Delivered,
            BuyerId = user.Id,
            BuyerEmail = user.Email!,
            BuyerName = user.FullName,
            PaymentMethodId = "card",
            PaymentMethodLabel = "Card",
            ItemsSubtotal = 10,
            ShippingTotal = 5,
            GrandTotal = 15,
            TotalQuantity = 1,
            CreatedOn = DateTimeOffset.UtcNow.AddMinutes(-30),
            DeliveryAddressJson = JsonSerializer.Serialize(
                new DeliveryAddress(user.FullName, "123 Main", null, "City", "ST", "12345", "US", "555-1234"),
                _serializerOptions),
            DetailsJson = JsonSerializer.Serialize(details, _serializerOptions)
        });

        context.ProductReviews.Add(new ProductReview
        {
            BuyerId = user.Id,
            BuyerName = user.FullName,
            ProductId = 1,
            Rating = 5,
            Comment = "Great",
            CreatedOn = DateTimeOffset.UtcNow
        });

        context.ProductQuestions.Add(new ProductQuestion
        {
            BuyerId = user.Id,
            BuyerName = user.FullName,
            ProductId = 1,
            SellerId = "seller-1",
            Question = "q",
            Status = ProductQuestionStatuses.Open,
            CreatedOn = DateTimeOffset.UtcNow
        });

        context.SellerRatings.Add(new SellerRating
        {
            BuyerId = user.Id,
            BuyerName = user.FullName,
            SellerId = user.Id,
            SellerName = "Seller Name",
            OrderId = 1,
            Rating = 5,
            CreatedOn = DateTimeOffset.UtcNow
        });

        context.LoginAudits.Add(new LoginAudit
        {
            UserId = user.Id,
            Email = user.Email!,
            EventType = "Login",
            IpAddress = "127.0.0.1",
            UserAgent = "agent",
            OccurredOn = DateTimeOffset.UtcNow,
            ExpiresOn = DateTimeOffset.UtcNow.AddDays(7)
        });

        context.Set<IdentityUserLogin<string>>().Add(new IdentityUserLogin<string>
        {
            LoginProvider = "Google",
            ProviderKey = "key",
            ProviderDisplayName = "Google",
            UserId = user.Id
        });

        context.Set<IdentityUserToken<string>>().Add(new IdentityUserToken<string>
        {
            UserId = user.Id,
            LoginProvider = "Default",
            Name = "AuthToken",
            Value = "value"
        });

        await context.SaveChangesAsync();

        var result = await service.DeleteAsync(user.Id, user.Id, "User self-service", CancellationToken.None);

        Assert.True(result.Success);
        var persistedUser = await context.Users.FirstAsync();
        Assert.Equal("Deleted user", persistedUser.FullName);
        Assert.Contains("deleted+", persistedUser.Email);
        Assert.Null(persistedUser.PasswordHash);
        Assert.True(persistedUser.LockoutEnabled);
        Assert.NotNull(persistedUser.LockoutEnd);
        Assert.Equal(DateTimeOffset.MaxValue, persistedUser.LockoutEnd);

        var address = await context.ShippingAddresses.FirstAsync();
        Assert.Equal("Deleted user", address.Recipient);
        Assert.Equal("Removed", address.Line1);

        var order = await context.Orders.FirstAsync();
        Assert.Equal("Deleted user", order.BuyerName);
        Assert.Equal(persistedUser.Email, order.BuyerEmail);
        var delivery = JsonSerializer.Deserialize<DeliveryAddress>(order.DeliveryAddressJson, _serializerOptions);
        Assert.Equal("Deleted user", delivery!.Recipient);

        var orderDetails = JsonSerializer.Deserialize<OrderDetailsPayload>(order.DetailsJson, _serializerOptions);
        Assert.All(orderDetails!.SubOrders, s => Assert.Equal("Deleted seller", s.SellerName));
        Assert.All(orderDetails.Items, i => Assert.Equal("Deleted seller", i.SellerName));
        Assert.All(orderDetails.Shipping, s => Assert.Equal("Deleted seller", s.SellerName));

        var review = await context.ProductReviews.FirstAsync();
        Assert.Equal("Deleted user", review.BuyerName);

        var question = await context.ProductQuestions.FirstAsync();
        Assert.Equal("Deleted user", question.BuyerName);

        var rating = await context.SellerRatings.FirstAsync();
        Assert.Equal("Deleted user", rating.BuyerName);
        Assert.Equal("Deleted seller", rating.SellerName);

        var audit = await context.LoginAudits.FirstAsync();
        Assert.Equal(persistedUser.Email, audit.Email);
        Assert.Null(audit.IpAddress);
        Assert.Null(audit.UserAgent);

        Assert.Empty(context.Set<IdentityUserLogin<string>>());
        Assert.Empty(context.Set<IdentityUserToken<string>>());
        Assert.Single(context.UserAdminAudits);
        Assert.Equal("Deleted", context.UserAdminAudits.First().Action);
    }
}
