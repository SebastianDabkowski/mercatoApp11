using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Data;

namespace SD.ProjectName.WebApp.Services
{
    public record ShippingMethodOption(string Id, string Label, decimal Cost, string? Description, bool Recommended, string? DeliveryEstimate = null);

    public record SellerShippingOptions(string SellerId, string SellerName, List<ShippingMethodOption> Options);

    public record ShippingQuote(CartSummary Summary, List<SellerShippingOptions> SellerOptions, Dictionary<string, string> SelectedMethods);

    public class ShippingOptionsService
    {
        private readonly CheckoutOptions _checkoutOptions;

        public ShippingOptionsService(CheckoutOptions checkoutOptions)
        {
            _checkoutOptions = checkoutOptions;
        }

        public ShippingQuote BuildQuote(
            CartSummary summary,
            DeliveryAddress address,
            IReadOnlyDictionary<string, string>? sellerCountries = null,
            IDictionary<string, string>? selectedMethods = null,
            IReadOnlyDictionary<string, List<SellerShippingMethod>>? sellerShippingMethods = null)
        {
            if (summary == null)
            {
                throw new ArgumentNullException(nameof(summary));
            }

            if (address == null)
            {
                throw new ArgumentNullException(nameof(address));
            }

            var selected = NormalizeSelections(selectedMethods);
            var sellerOptions = new List<SellerShippingOptions>();
            var adjustedGroups = new List<CartSellerGroup>();
            var settlements = new List<CartSellerSettlement>();

            foreach (var group in summary.SellerGroups)
            {
                var configuredMethods = sellerShippingMethods != null && sellerShippingMethods.TryGetValue(group.SellerId, out var storeMethods)
                    ? storeMethods
                    : null;
                var options = BuildOptionsForSeller(group, address, sellerCountries, configuredMethods);
                var selectionId = ResolveSelection(options, selected.TryGetValue(group.SellerId, out var stored) ? stored : null);
                selected[group.SellerId] = selectionId;

                var selectedOption = options.First(o => string.Equals(o.Id, selectionId, StringComparison.OrdinalIgnoreCase));
                adjustedGroups.Add(group with { Shipping = selectedOption.Cost, Total = group.Subtotal + selectedOption.Cost });
                sellerOptions.Add(new SellerShippingOptions(group.SellerId, group.SellerName, options));

                var sellerSettlement = summary.Settlement.Sellers.FirstOrDefault(s => s.SellerId == group.SellerId);
                var commission = sellerSettlement?.Commission ?? 0;
                settlements.Add(new CartSellerSettlement(group.SellerId, group.Subtotal, selectedOption.Cost, commission, Math.Max(0, group.Subtotal + selectedOption.Cost - commission)));
            }

            var shippingTotal = adjustedGroups.Sum(g => g.Shipping);
            var settlementSummary = new CartSettlementSummary(settlements, settlements.Sum(s => s.Commission), settlements.Sum(s => s.Payout));
            var updatedSummary = new CartSummary(
                adjustedGroups,
                summary.ItemsSubtotal,
                shippingTotal,
                Math.Max(0, summary.ItemsSubtotal + shippingTotal - summary.DiscountTotal),
                summary.TotalQuantity,
                settlementSummary,
                summary.DiscountTotal,
                summary.AppliedPromoCode);

            return new ShippingQuote(updatedSummary, sellerOptions, selected);
        }

        private List<ShippingMethodOption> BuildOptionsForSeller(
            CartSellerGroup group,
            DeliveryAddress address,
            IReadOnlyDictionary<string, string>? sellerCountries,
            List<SellerShippingMethod>? configuredMethods)
        {
            var options = BuildConfiguredOptions(configuredMethods);

            if (options.Count == 0)
            {
                options = BuildDefaultOptions(group, address, sellerCountries);
            }

            if (options.Count == 0)
            {
                options.Add(new ShippingMethodOption("standard", "Standard", Math.Max(0, group.Shipping), "Standard delivery", true, ResolveDeliveryEstimate("Standard")));
            }

            var cheapest = options.OrderBy(o => o.Cost).First();
            for (var i = 0; i < options.Count; i++)
            {
                var option = options[i];
                options[i] = option with { Recommended = string.Equals(option.Id, cheapest.Id, StringComparison.OrdinalIgnoreCase) };
            }

            return options;
        }

        private List<ShippingMethodOption> BuildConfiguredOptions(List<SellerShippingMethod>? configuredMethods)
        {
            var options = new List<ShippingMethodOption>();
            if (configuredMethods == null || configuredMethods.Count == 0)
            {
                return options;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var method in configuredMethods)
            {
                if (string.IsNullOrWhiteSpace(method.Name))
                {
                    continue;
                }

                var label = method.Name.Trim();
                if (!seen.Add(label))
                {
                    continue;
                }
                var description = string.IsNullOrWhiteSpace(method.Description) ? ResolveDescription(label) : method.Description.Trim();
                var estimate = string.IsNullOrWhiteSpace(method.DeliveryEstimate) ? ResolveDeliveryEstimate(label) : method.DeliveryEstimate.Trim();
                var cost = NormalizeCost(method.BaseCost);
                options.Add(new ShippingMethodOption(NormalizeId(label), label, cost, description, false, estimate));
            }

            return options;
        }

        private List<ShippingMethodOption> BuildDefaultOptions(
            CartSellerGroup group,
            DeliveryAddress address,
            IReadOnlyDictionary<string, string>? sellerCountries)
        {
            var methods = ResolveMethods(group);
            var sellerCountry = sellerCountries != null && sellerCountries.TryGetValue(group.SellerId, out var found) ? found : null;
            var options = new List<ShippingMethodOption>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var method in methods)
            {
                if (string.IsNullOrWhiteSpace(method) || !seen.Add(method))
                {
                    continue;
                }

                var (cost, description, estimate) = CalculateCost(group, address, sellerCountry, method);
                options.Add(new ShippingMethodOption(NormalizeId(method), method, cost, description, false, estimate));
            }

            return options;
        }

        private (decimal Cost, string Description, string? Estimate) CalculateCost(CartSellerGroup group, DeliveryAddress address, string? sellerCountry, string method)
        {
            var baseShipping = Math.Max(group.Shipping, 0);
            var quantity = Math.Max(group.Items.Sum(i => i.Quantity), 1);
            var totalWeight = group.Items.Sum(i => (i.Product.WeightKg ?? 0) * i.Quantity);

            var isInternational = !string.IsNullOrWhiteSpace(sellerCountry)
                && !string.IsNullOrWhiteSpace(address.Country)
                && !string.Equals(sellerCountry, address.Country, StringComparison.OrdinalIgnoreCase);

            if (isInternational)
            {
                baseShipping += 7 + Math.Min(quantity * 2, 12);
            }

            var normalized = method.Trim().ToLowerInvariant();
            decimal cost = baseShipping;
            var description = ResolveDescription(normalized);
            var estimate = ResolveDeliveryEstimate(normalized);

            if (normalized.Contains("express") || normalized.Contains("priority"))
            {
                cost = baseShipping + Math.Max(5, quantity);
                estimate = string.IsNullOrWhiteSpace(estimate) ? "1-2 business days" : estimate;
            }
            else if (normalized.Contains("pickup"))
            {
                cost = 0;
                estimate = "Same-day pickup";
            }
            else if (normalized.Contains("locker"))
            {
                cost = Math.Max(0, baseShipping + Math.Max(1, quantity * 0.5m));
                estimate = string.IsNullOrWhiteSpace(estimate) ? "1-3 business days" : estimate;
            }
            else if (normalized.Contains("economy") || normalized.Contains("postal"))
            {
                cost = Math.Max(0, baseShipping * 0.7m);
                estimate = string.IsNullOrWhiteSpace(estimate) ? "4-7 business days" : estimate;
            }
            else if (normalized.Contains("freight") || normalized.Contains("cargo"))
            {
                cost = baseShipping + Math.Max(8, (decimal)totalWeight * 1.2m);
                estimate = string.IsNullOrWhiteSpace(estimate) ? "5-10 business days" : estimate;
            }

            return (Math.Round(cost, 2, MidpointRounding.AwayFromZero), description, estimate);
        }

        private static string ResolveDescription(string method)
        {
            var normalized = string.IsNullOrWhiteSpace(method) ? string.Empty : method.Trim().ToLowerInvariant();
            if (normalized.Contains("express") || normalized.Contains("priority"))
            {
                return "Faster delivery with priority handling";
            }

            if (normalized.Contains("pickup"))
            {
                return "Collect from a pickup point";
            }

            if (normalized.Contains("locker"))
            {
                return "Deliver to parcel locker";
            }

            if (normalized.Contains("economy") || normalized.Contains("postal"))
            {
                return "Economy delivery";
            }

            if (normalized.Contains("freight") || normalized.Contains("cargo"))
            {
                return "Freight delivery for heavy items";
            }

            return "Standard delivery";
        }

        private static string ResolveDeliveryEstimate(string? method)
        {
            var normalized = string.IsNullOrWhiteSpace(method) ? string.Empty : method.Trim().ToLowerInvariant();
            if (normalized.Contains("express") || normalized.Contains("priority"))
            {
                return "1-2 business days";
            }

            if (normalized.Contains("pickup"))
            {
                return "Same-day pickup";
            }

            if (normalized.Contains("locker"))
            {
                return "1-3 business days";
            }

            if (normalized.Contains("economy") || normalized.Contains("postal"))
            {
                return "4-7 business days";
            }

            if (normalized.Contains("freight") || normalized.Contains("cargo"))
            {
                return "5-10 business days";
            }

            return "2-5 business days";
        }

        private List<string> ResolveMethods(CartSellerGroup group)
        {
            var methods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in group.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Product.ShippingMethods))
                {
                    continue;
                }

                var tokens = item.Product.ShippingMethods.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var token in tokens)
                {
                    var method = token.Trim();
                    if (!string.IsNullOrWhiteSpace(method))
                    {
                        methods.Add(method);
                    }
                }
            }

            if (methods.Count == 0 && _checkoutOptions.DefaultShippingMethods != null)
            {
                foreach (var method in _checkoutOptions.DefaultShippingMethods)
                {
                    if (!string.IsNullOrWhiteSpace(method))
                    {
                        methods.Add(method.Trim());
                    }
                }
            }

            if (methods.Count == 0)
            {
                methods.Add("Standard");
            }

            return methods.ToList();
        }

        private static decimal NormalizeCost(decimal cost)
        {
            var normalized = Math.Max(0, cost);
            return Math.Round(normalized, 2, MidpointRounding.AwayFromZero);
        }

        private static string ResolveSelection(IEnumerable<ShippingMethodOption> options, string? preferred)
        {
            if (!string.IsNullOrWhiteSpace(preferred) && options.Any(o => string.Equals(o.Id, preferred, StringComparison.OrdinalIgnoreCase)))
            {
                return preferred;
            }

            var recommended = options.FirstOrDefault(o => o.Recommended);
            return recommended?.Id ?? options.First().Id;
        }

        private static Dictionary<string, string> NormalizeSelections(IDictionary<string, string>? selections)
        {
            var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (selections == null)
            {
                return normalized;
            }

            foreach (var pair in selections)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                normalized[pair.Key.Trim()] = pair.Value.Trim();
            }

            return normalized;
        }

        private static string NormalizeId(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return "standard";
            }

            var normalized = new string(label.Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
            return string.IsNullOrWhiteSpace(normalized) ? "standard" : normalized.Trim('-');
        }
    }
}
