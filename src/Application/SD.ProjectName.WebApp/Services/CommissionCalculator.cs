using System;
using System.Collections.Generic;

namespace SD.ProjectName.WebApp.Services
{
    public class CommissionCalculator
    {
        private readonly CartOptions _options;
        private readonly int _precision;
        private readonly ICommissionRuleResolver? _resolver;

        public CommissionCalculator(CartOptions options, ICommissionRuleResolver? resolver = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _resolver = resolver;
            _precision = Math.Clamp(options.CommissionPrecision <= 0 ? 4 : options.CommissionPrecision, 2, 6);
        }

        public decimal ResolveRate(string sellerId, string? category)
        {
            return ResolveCommission(sellerId, category, null, DateTimeOffset.UtcNow).Rate;
        }

        public CommissionRuleResolution ResolveCommission(string sellerId, string? category, string? sellerType, DateTimeOffset asOf)
        {
            if (_resolver != null)
            {
                return _resolver.Resolve(sellerId, category, sellerType, asOf);
            }

            if (_options.SellerCommissionOverrides != null
                && _options.SellerCommissionOverrides.TryGetValue(sellerId, out var sellerRate))
            {
                return new CommissionRuleResolution(null, ClampRate(sellerRate), Math.Max(0, _options.PlatformFixedFee), $"seller-{sellerId}");
            }

            if (!string.IsNullOrWhiteSpace(category)
                && _options.CategoryCommissionRates != null
                && _options.CategoryCommissionRates.TryGetValue(category!, out var categoryRate))
            {
                return new CommissionRuleResolution(null, ClampRate(categoryRate), Math.Max(0, _options.PlatformFixedFee), $"category-{category}".ToLowerInvariant());
            }

            return new CommissionRuleResolution(null, ClampRate(_options.PlatformCommissionRate), Math.Max(0, _options.PlatformFixedFee), "default");
        }

        public decimal CalculateForCartGroup(CartSellerGroup group)
        {
            if (group == null || group.Items == null || group.Items.Count == 0)
            {
                return 0;
            }

            decimal commission = 0;
            var appliedFees = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var now = DateTimeOffset.UtcNow;
            foreach (var item in group.Items)
            {
                var resolution = ResolveCommission(group.SellerId, item.Product.Category, group.SellerType, now);
                commission += item.LineTotal * resolution.Rate;

                if (resolution.FixedFee > 0)
                {
                    var key = string.IsNullOrWhiteSpace(resolution.Key) ? "default" : resolution.Key;
                    if (appliedFees.Add(key))
                    {
                        commission += resolution.FixedFee;
                    }
                }
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
            var appliedFees = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var now = DateTimeOffset.UtcNow;
            foreach (var item in items)
            {
                var resolution = ResolveCommission(item.SellerId, item.Category, null, now);
                var rate = item.CommissionRate.HasValue ? ClampRate(item.CommissionRate.Value) : resolution.Rate;
                var status = OrderStatuses.Normalize(item.Status);
                if (string.Equals(status, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, OrderStatuses.Refunded, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, OrderStatuses.Failed, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                commission += item.LineTotal * rate;
                var shouldUseResolverFee = item.CommissionFixedFee <= 0 && (!item.CommissionRate.HasValue || item.CommissionRuleId.HasValue);
                var fixedFee = item.CommissionFixedFee > 0
                    ? item.CommissionFixedFee
                    : shouldUseResolverFee ? resolution.FixedFee : 0;
                var feeKey = item.CommissionRuleId.HasValue
                    ? $"rule-{item.CommissionRuleId}"
                    : item.CommissionFixedFee > 0
                        ? $"item-{item.ProductId}"
                        : string.IsNullOrWhiteSpace(resolution.Key) ? "default" : resolution.Key;
                if (fixedFee > 0 && appliedFees.Add(feeKey))
                {
                    commission += fixedFee;
                }
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
