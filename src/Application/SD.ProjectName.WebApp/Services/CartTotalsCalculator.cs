using System;
using System.Collections.Generic;
using System.Linq;

namespace SD.ProjectName.WebApp.Services
{
    public class CartTotalsCalculator
    {
        private readonly CartOptions _options;
        private readonly CommissionCalculator _commissionCalculator;

        public CartTotalsCalculator(CartOptions options)
        {
            _options = options;
            _commissionCalculator = new CommissionCalculator(options);
        }

        public CartSummary Calculate(IReadOnlyCollection<CartSellerGroup> sellerGroups)
        {
            if (sellerGroups == null)
            {
                throw new ArgumentNullException(nameof(sellerGroups));
            }

            if (sellerGroups.Count == 0)
            {
                return CartSummary.Empty;
            }

            var calculatedGroups = new List<CartSellerGroup>();
            var settlements = new List<CartSellerSettlement>();
            decimal itemsSubtotal = 0;
            decimal shippingTotal = 0;

            foreach (var group in sellerGroups)
            {
                var quantity = group.Items.Sum(i => i.Quantity);
                var shipping = CalculateShipping(group.SellerId, group.Subtotal, quantity);
                var total = group.Subtotal + shipping;

                calculatedGroups.Add(group with { Shipping = shipping, Total = total });

                itemsSubtotal += group.Subtotal;
                shippingTotal += shipping;

                var commission = _commissionCalculator.CalculateForCartGroup(group);
                var payout = _commissionCalculator.Round(Math.Max(0, group.Subtotal + shipping - commission));
                settlements.Add(new CartSellerSettlement(group.SellerId, group.Subtotal, shipping, commission, payout));
            }

            var grandTotal = itemsSubtotal + shippingTotal;
            var totalQuantity = sellerGroups.Sum(g => g.Items.Sum(i => i.Quantity));
            var settlementSummary = new CartSettlementSummary(
                settlements,
                _commissionCalculator.Round(settlements.Sum(s => s.Commission)),
                _commissionCalculator.Round(settlements.Sum(s => s.Payout)));

            return new CartSummary(calculatedGroups, itemsSubtotal, shippingTotal, grandTotal, totalQuantity, settlementSummary);
        }

        private decimal CalculateShipping(string sellerId, decimal subtotal, int quantity)
        {
            var rule = ResolveShippingRule(sellerId);
            if (rule.FreeShippingThreshold.HasValue && subtotal >= rule.FreeShippingThreshold.Value)
            {
                return 0;
            }

            var baseRate = Math.Max(0, rule.BaseRate);
            var perItemRate = Math.Max(0, rule.PerItemRate);
            var normalizedQuantity = Math.Max(0, quantity);
            return baseRate + perItemRate * normalizedQuantity;
        }

        private CartShippingRule ResolveShippingRule(string sellerId)
        {
            if (_options.ShippingRules != null && _options.ShippingRules.Count > 0)
            {
                var rule = _options.ShippingRules.FirstOrDefault(r => string.Equals(r.SellerId, sellerId, StringComparison.OrdinalIgnoreCase));
                if (rule != null)
                {
                    return rule;
                }
            }

            return new CartShippingRule
            {
                SellerId = sellerId,
                BaseRate = _options.DefaultShippingBase,
                PerItemRate = _options.DefaultShippingPerItem,
                FreeShippingThreshold = _options.DefaultFreeShippingThreshold
            };
        }
    }
}
