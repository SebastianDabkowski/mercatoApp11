using System;
using System.Collections.Generic;
using System.Linq;

namespace SD.ProjectName.WebApp.Services
{
    public static class PaymentStatuses
    {
        public const string Pending = "Pending";
        public const string Paid = "Paid";
        public const string Failed = "Failed";
        public const string Refunded = "Refunded";

        private static readonly IReadOnlyList<string> OrderedStatuses = new[]
        {
            Pending, Paid, Failed, Refunded
        };

        public static IReadOnlyList<string> All => OrderedStatuses;

        public static string Normalize(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return Paid;
            }

            var match = OrderedStatuses.FirstOrDefault(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));
            return match ?? status.Trim();
        }
    }

    public static class PaymentStatusMapper
    {
        private static readonly Dictionary<string, string> ProviderStatusMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["pending"] = PaymentStatuses.Pending,
            ["waiting"] = PaymentStatuses.Pending,
            ["processing"] = PaymentStatuses.Pending,
            ["paid"] = PaymentStatuses.Paid,
            ["authorized"] = PaymentStatuses.Paid,
            ["succeeded"] = PaymentStatuses.Paid,
            ["failed"] = PaymentStatuses.Failed,
            ["error"] = PaymentStatuses.Failed,
            ["declined"] = PaymentStatuses.Failed,
            ["cancelled"] = PaymentStatuses.Failed,
            ["refunded"] = PaymentStatuses.Refunded,
            ["partial_refund"] = PaymentStatuses.Refunded,
            ["charged_back"] = PaymentStatuses.Refunded
        };

        public static string MapProviderStatus(string? providerStatus)
        {
            if (string.IsNullOrWhiteSpace(providerStatus))
            {
                return PaymentStatuses.Pending;
            }

            return ProviderStatusMap.TryGetValue(providerStatus.Trim(), out var mapped)
                ? mapped
                : PaymentStatuses.Pending;
        }

        public static string BuildBuyerMessage(string paymentStatus)
        {
            return PaymentStatuses.Normalize(paymentStatus) switch
            {
                PaymentStatuses.Pending => "Payment is pending confirmation. You'll be notified when the provider responds.",
                PaymentStatuses.Failed => "Payment failed. No charge was captured. Please try again or choose another method.",
                PaymentStatuses.Refunded => "Payment was refunded. Funds should return to your account per your bank's timeline.",
                _ => string.Empty
            };
        }
    }
}
