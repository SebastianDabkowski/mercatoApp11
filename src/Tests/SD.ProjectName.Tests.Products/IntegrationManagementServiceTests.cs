using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.Tests.Products
{
    public class IntegrationManagementServiceTests
    {
        private IntegrationManagementService CreateService(out ApplicationDbContext dbContext, string environment = "Development")
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            dbContext = new ApplicationDbContext(options);
            var paymentOptions = new PaymentProviderOptions
            {
                ProviderName = "SecurePay"
            };
            var shippingOptions = new ShippingProviderOptions
            {
                Providers = new List<ShippingProviderDefinition>
                {
                    new ShippingProviderDefinition
                    {
                        Id = "shipfast",
                        Name = "ShipFast",
                        Enabled = true,
                        Services = new List<ShippingProviderServiceDefinition>
                        {
                            new ShippingProviderServiceDefinition { Code = "standard", Name = "Standard" }
                        }
                    }
                }
            };
            var hostEnvironment = new FakeHostEnvironment(environment);
            return new IntegrationManagementService(dbContext, paymentOptions, shippingOptions, TimeProvider.System, hostEnvironment);
        }

        [Fact]
        public async Task GetAllAsync_SeedsDefaultIntegrations()
        {
            var service = CreateService(out var dbContext);

            var integrations = await service.GetAllAsync();

            Assert.Contains(integrations, i => i.Key == IntegrationManagementService.PaymentIntegrationKey && i.Type == IntegrationTypes.Payment);
            Assert.Contains(integrations, i => i.Type == IntegrationTypes.Shipping);

            await dbContext.DisposeAsync();
        }

        [Fact]
        public async Task SaveAndHealthCheck_ShouldMaskSecretAndUpdateStatus()
        {
            var service = CreateService(out var dbContext);
            var integrations = await service.GetAllAsync();
            var payment = integrations.First(i => i.Key == IntegrationManagementService.PaymentIntegrationKey);

            var saveResult = await service.SaveAsync(
                new IntegrationUpdateInput(
                    payment.Id,
                    payment.Key,
                    payment.Name,
                    payment.Type,
                    payment.Environment,
                    true,
                    "https://provider.test/api",
                    "merchant-123",
                    "https://provider.test/callback",
                    "super-secret-key"),
                CancellationToken.None);

            Assert.True(saveResult.Success);

            var refreshed = await service.GetAsync(payment.Id);
            Assert.NotNull(refreshed);
            Assert.False(string.IsNullOrWhiteSpace(refreshed!.ApiKeyPreview));
            Assert.DoesNotContain("super-secret-key", refreshed.ApiKeyPreview ?? string.Empty);

            var health = await service.RunHealthCheckAsync(payment.Id, CancellationToken.None);

            Assert.True(health.Success);

            var after = await service.GetAsync(payment.Id);
            Assert.Equal(IntegrationStatuses.Healthy, after!.Status);

            await dbContext.DisposeAsync();
        }

        private class FakeHostEnvironment : IWebHostEnvironment
        {
            public FakeHostEnvironment(string name)
            {
                EnvironmentName = name;
                ApplicationName = "Tests";
                WebRootPath = string.Empty;
                ContentRootPath = string.Empty;
            }

            public string ApplicationName { get; set; }

            public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

            public string WebRootPath { get; set; }

            public string EnvironmentName { get; set; }

            public string ContentRootPath { get; set; }

            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }
    }
}
