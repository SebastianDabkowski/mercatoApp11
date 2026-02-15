using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.Tests.Products;

public class UserDataExportServiceTests
{
    [Fact]
    public async Task GenerateAsync_ProducesZipWithUserDataAndAudits()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"export-{Guid.NewGuid()}")
            .Options;

        await using var context = new ApplicationDbContext(options);
        var timeProvider = TimeProvider.System;

        var user = new ApplicationUser
        {
            Id = "user-1",
            UserName = "user@test.com",
            Email = "user@test.com",
            PhoneNumber = "123456789",
            FullName = "Test User",
            AccountType = AccountTypes.Buyer,
            AccountStatus = AccountStatuses.Verified,
            Country = "US",
            Address = "123 Main St",
            StoreDescription = "My store",
            PayoutMethod = "BankTransfer",
            PayoutSchedule = "Weekly",
            LastLoginIp = "127.0.0.1",
            LastLoginOn = DateTimeOffset.UtcNow
        };
        context.Users.Add(user);

        var consentDefinition = new ConsentDefinition
        {
            ConsentType = ConsentTypes.Newsletter,
            Title = "Newsletter",
            Description = "desc",
            AllowPreselect = true,
            IsRequired = false,
            CreatedOn = DateTimeOffset.UtcNow
        };
        var consentVersion = new ConsentVersion
        {
            ConsentDefinition = consentDefinition,
            VersionTag = "v1",
            Content = "content",
            EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedOn = DateTimeOffset.UtcNow.AddDays(-1)
        };
        var consentDecision = new UserConsentDecision
        {
            ConsentVersion = consentVersion,
            UserId = user.Id,
            Granted = true,
            DecidedOn = DateTimeOffset.UtcNow
        };
        context.ConsentDefinitions.Add(consentDefinition);
        context.ConsentVersions.Add(consentVersion);
        context.UserConsentDecisions.Add(consentDecision);

        var address = new ShippingAddress
        {
            UserId = user.Id,
            Recipient = "Test User",
            Line1 = "123 Main",
            City = "City",
            State = "ST",
            PostalCode = "12345",
            Country = "US",
            Phone = "123",
            IsDefault = true,
            CreatedOn = DateTimeOffset.UtcNow,
            UpdatedOn = DateTimeOffset.UtcNow
        };
        context.ShippingAddresses.Add(address);

        var serializerOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        var subOrder = new OrderSubOrder(
            "SO-1",
            user.Id,
            "Seller Name",
            10,
            5,
            0,
            15,
            1,
            new List<OrderItemDetail>
            {
                new(1, "Product", "Default", 1, 10, 10, user.Id, "Seller Name")
            },
            new OrderShippingDetail(user.Id, "Seller Name", "standard", "Standard", 5, "desc"),
            OrderStatuses.Paid,
            "TRACK1",
            "Carrier",
            0,
            DateTimeOffset.UtcNow,
            new ReturnRequest(
                "SO-1",
                ReturnRequestStatuses.PendingSellerReview,
                "Damaged",
                DateTimeOffset.UtcNow,
                new List<ReturnRequestItem> { new(1, 1) },
                Messages: new List<ReturnRequestMessage> { new("Buyer", "Need help", DateTimeOffset.UtcNow) }
            ),
            new List<OrderStatusChange> { new(OrderStatuses.Paid, DateTimeOffset.UtcNow) });

        var details = new OrderDetailsPayload(
            new List<OrderItemDetail>
            {
                new(1, "Product", "Default", 1, 10, 10, user.Id, "Seller Name")
            },
            new List<OrderShippingDetail>
            {
                new(user.Id, "Seller Name", "standard", "Standard", 5, "desc")
            },
            1,
            0,
            null,
            new List<OrderSubOrder> { subOrder },
            new List<EscrowAllocation>(),
            PaymentStatuses.Paid,
            "Paid",
            0,
            new List<OrderMessage>
            {
                new(Guid.NewGuid(), "SO-1", user.Id, "Seller", user.Id, "Hello buyer", DateTimeOffset.UtcNow),
                new(Guid.NewGuid(), "SO-2", "someone-else", "Seller", "someone-else", "Ignore me", DateTimeOffset.UtcNow, true)
            });

        var order = new OrderRecord
        {
            OrderNumber = "ORD-1",
            Status = OrderStatuses.Paid,
            BuyerId = user.Id,
            BuyerEmail = user.Email!,
            BuyerName = user.FullName,
            PaymentMethodId = "card",
            PaymentMethodLabel = "Card",
            PaymentReference = "REF-1",
            CartSignature = "cart",
            ItemsSubtotal = 10,
            ShippingTotal = 5,
            GrandTotal = 15,
            TotalQuantity = 1,
            CreatedOn = DateTimeOffset.UtcNow.AddMinutes(-5),
            SavedAddressKey = "home",
            DeliveryAddressJson = JsonSerializer.Serialize(
                new DeliveryAddress("Test User", "123 Main", null, "City", "ST", "12345", "US", "123"),
                serializerOptions),
            DetailsJson = JsonSerializer.Serialize(details, serializerOptions)
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var consentService = new ConsentService(context, timeProvider);
        var logger = NullLogger<UserDataExportService>.Instance;
        var sensitiveEncryption = new SensitiveDataEncryptionService(DataProtectionProvider.Create("tests"));
        var service = new UserDataExportService(context, consentService, timeProvider, logger, sensitiveEncryption);

        var result = await service.GenerateAsync(user.Id, CancellationToken.None);

        Assert.Equal("application/zip", result.ContentType);

        using var archive = new ZipArchive(new MemoryStream(result.Content), ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.GetEntry("user-data.json");
        Assert.NotNull(entry);

        using var entryStream = entry!.Open();
        using var reader = new StreamReader(entryStream);
        var json = await reader.ReadToEndAsync();
        var export = JsonSerializer.Deserialize<UserDataExportDocument>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(export);
        Assert.Equal(user.Email, export!.Profile.Email);
        Assert.Single(export.Consents);
        Assert.Single(export.Addresses);
        Assert.Single(export.Orders.Buyer);
        Assert.Single(export.Orders.Seller);
        Assert.Single(export.Orders.Seller[0].Messages);
        Assert.Equal(2, context.UserAdminAudits.Count());
        Assert.Contains(context.UserAdminAudits, a => a.Action == "Data export requested");
        Assert.Contains(context.UserAdminAudits, a => a.Action == "Data export completed");
    }
}
