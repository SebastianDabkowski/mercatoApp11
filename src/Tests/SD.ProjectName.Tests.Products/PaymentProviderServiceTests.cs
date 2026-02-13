using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.Tests.Products
{
    public class PaymentProviderServiceTests
    {
        private static PaymentProviderService CreateService()
        {
            var options = new PaymentProviderOptions
            {
                SigningKey = "abcdefghijklmnopqrstuvwxyz0123456789abcdefghijklmnopqrstuvwxyz",
                TokenLifetimeMinutes = 30,
                ProviderName = "TestPay"
            };

            return new PaymentProviderService(options, TimeProvider.System);
        }

        [Fact]
        public void CreateRedirectPayment_ShouldIssueSignedTokens()
        {
            var service = CreateService();
            var redirect = service.CreateRedirectPayment(new PaymentRedirectRequest("card", 123.45m, "PLN", "https://merchant.test/return", "https://merchant.test/cancel"));

            Assert.False(string.IsNullOrWhiteSpace(redirect.PaymentReference));
            Assert.Contains("providerToken=", redirect.RedirectUrl);

            var token = redirect.RedirectUrl.Split("providerToken=")[1];
            var validation = service.ValidateReturn(token, 123.45m, "PLN", "card");

            Assert.Equal(CheckoutPaymentStatus.Confirmed, validation.Status);
            Assert.Equal(redirect.PaymentReference, validation.PaymentReference);
            Assert.Null(validation.Error);
        }

        [Fact]
        public void ValidateReturn_ShouldFailForMismatchedAmount()
        {
            var service = CreateService();
            var redirect = service.CreateRedirectPayment(new PaymentRedirectRequest("bank_transfer", 99.99m, "PLN", "https://merchant.test/return", "https://merchant.test/cancel"));
            var token = redirect.RedirectUrl.Split("providerToken=")[1];

            var validation = service.ValidateReturn(token, 10m, "PLN", "bank_transfer");

            Assert.Equal(CheckoutPaymentStatus.Failed, validation.Status);
            Assert.NotNull(validation.Error);
            Assert.Equal(redirect.PaymentReference, validation.PaymentReference);
        }

        [Fact]
        public void AuthorizeBlik_ShouldValidateCode()
        {
            var service = CreateService();

            var success = service.AuthorizeBlik("blik", 10m, "PLN", "123456");
            var failure = service.AuthorizeBlik("blik", 10m, "PLN", "123450");

            Assert.Equal(CheckoutPaymentStatus.Confirmed, success.Status);
            Assert.Equal(CheckoutPaymentStatus.Failed, failure.Status);
            Assert.False(string.IsNullOrWhiteSpace(failure.Error));
        }
    }
}
