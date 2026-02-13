using System;
using System.Collections.Generic;

namespace SD.ProjectName.WebApp.Services
{
    public class CommissionCalculator
    {
        private readonly CartOptions _options;
        private readonly int _precision;

        public CommissionCalculator(CartOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _precision = Math.Clamp(options.CommissionPrecision <= 0 ? 4 : options.CommissionPrecision, 2, 6);
        }

        public decimal ResolveRate(string sellerId, string? category)
        {
            if (_options.SellerCommissionOverrides != null
                && _options.SellerCommissionOverrides.TryGetValue(sellerId, out var sellerRate))
            {
                return ClampRate(sellerRate);
            }

            if (!string.IsNullOrWhiteSpace(category)
                && _options.CategoryCommissionRates != null
                && _options.CategoryCommissionRates.TryGetValue(category!, out var categoryRate))
            {
                return ClampRate(categoryRate);
            }

            return ClampRate(_options.PlatformCommissionRate);
        }

        public decimal CalculateForCartGroup(CartSellerGroup group)
        {
            if (group == null || group.Items == null || group.Items.Count == 0)
            {
                return 0;
            }

            decimal commission = 0;
            foreach (var item in group.Items)
            {
                var rate = ResolveRate(group.SellerId, item.Product.Category);
                commission += item.LineTotal * rate;
            }

            return Round(commission);
        }

        public decimal CalculateForOrderItems(IEnumerable<OrderItemDetail> items)
        {
            if (items == null)
            {
                return 0;
            }

            decimal commission = 0;
            foreach (var item in items)
            {
                var rate = item.CommissionRate.HasValue ? ClampRate(item.CommissionRate.Value) : ResolveRate(item.SellerId, item.Category);
                var status = OrderStatuses.Normalize(item.Status);
                if (string.Equals(status, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, OrderStatuses.Refunded, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, OrderStatuses.Failed, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                commission += item.LineTotal * rate;
            }

            return Round(commission);
        }

        public decimal Round(decimal amount)
        {
            return Math.Round(amount, _precision, MidpointRounding.AwayFromZero);
        }

        private static decimal ClampRate(decimal rate)
        {
            if (rate < 0)
            {
                return 0;
            }

            if (rate > 1)
            {
                return 1;
            }

            return rate;
        }
    }
}
