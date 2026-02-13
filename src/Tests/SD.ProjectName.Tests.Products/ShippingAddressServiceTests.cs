using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Pages.Checkout;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.Tests.Products
{
    public class ShippingAddressServiceTests
    {
        [Fact]
        public async Task UpsertAsync_ShouldCreateDefaultAddress()
        {
            await using var context = CreateContext();
            var service = new ShippingAddressService(context, new ShippingAddressOptions { AllowedCountries = ["US"] });
            var form = new AddressForm
            {
                Recipient = "Jane Doe ",
                Line1 = "123 Main St",
                PostalCode = "12345",
                Country = "US",
                City = "Metropolis",
                Phone = "111-1111"
            };

            var created = await service.UpsertAsync("user-1", form, makeDefault: false);
            var addresses = await service.GetAddressesAsync("user-1");

            Assert.True(created.IsDefault);
            Assert.Single(addresses);
            Assert.Equal("Jane Doe", addresses[0].Recipient);
        }

        [Fact]
        public async Task SetDefaultAsync_ShouldSwitchDefault()
        {
            await using var context = CreateContext();
            var service = new ShippingAddressService(context, new ShippingAddressOptions());

            var first = await service.UpsertAsync("user-2", new AddressForm
            {
                Recipient = "One",
                Line1 = "1 First St",
                PostalCode = "10001",
                Country = "US",
                City = "City One",
                Phone = "222-2222"
            }, makeDefault: true);

            var second = await service.UpsertAsync("user-2", new AddressForm
            {
                Recipient = "Two",
                Line1 = "2 Second St",
                PostalCode = "20002",
                Country = "US",
                City = "City Two",
                Phone = "333-3333"
            }, makeDefault: false);

            var updated = await service.SetDefaultAsync("user-2", second.Id);
            var addresses = await service.GetAddressesAsync("user-2");

            Assert.True(updated);
            Assert.Single(addresses, a => a.IsDefault && a.Id == second.Id);
            Assert.Contains(addresses, a => a.Id == first.Id && !a.IsDefault);
        }

        [Fact]
        public async Task DeleteAsync_ShouldBlock_WhenAddressUsedInActiveOrder()
        {
            await using var context = CreateContext();
            var service = new ShippingAddressService(context, new ShippingAddressOptions());
            var form = new AddressForm
            {
                Recipient = "Blocked",
                Line1 = "3 Third St",
                PostalCode = "30003",
                Country = "US",
                City = "City Three",
                Phone = "444-4444"
            };

            var saved = await service.UpsertAsync("buyer-1", form, makeDefault: true);
            var delivery = service.ToDeliveryAddress(saved);
            var serializerOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

            context.Orders.Add(new OrderRecord
            {
                OrderNumber = "ORD-1",
                Status = OrderStatuses.Paid,
                BuyerId = "buyer-1",
                BuyerEmail = "buyer@example.com",
                BuyerName = "Blocked Buyer",
                PaymentMethodId = "card",
                PaymentMethodLabel = "Card",
                ItemsSubtotal = 10,
                ShippingTotal = 5,
                GrandTotal = 15,
                TotalQuantity = 1,
                CreatedOn = DateTimeOffset.UtcNow,
                DeliveryAddressJson = JsonSerializer.Serialize(delivery, serializerOptions),
                DetailsJson = "{}"
            });
            await context.SaveChangesAsync();

            var result = await service.DeleteAsync("buyer-1", saved.Id);
            var addresses = await service.GetAddressesAsync("buyer-1");

            Assert.Equal(ShippingAddressDeleteResult.BlockedByActiveOrder, result);
            Assert.Single(addresses);
        }

        private static ApplicationDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new ApplicationDbContext(options);
        }
    }
}
