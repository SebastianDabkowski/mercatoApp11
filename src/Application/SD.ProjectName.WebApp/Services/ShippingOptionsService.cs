using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Data;

namespace SD.ProjectName.WebApp.Services
{
    public record ShippingMethodOption(string Id, string Label, decimal Cost, string? Description, bool Recommended);

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
            var methods = ResolveMethods(group);
            if (configuredMethods != null && configuredMethods.Count > 0)
            {
                methods = configuredMethods
                    .Select(m => m.Name)
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .ToList();
            }

            var sellerCountry = sellerCountries != null && sellerCountries.TryGetValue(group.SellerId, out var found) ? found : null;
            var options = new List<ShippingMethodOption>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var method in methods)
            {
                if (string.IsNullOrWhiteSpace(method) || !seen.Add(method))
                {
                    continue;
                }

                var (cost, description) = CalculateCost(group, address, sellerCountry, method);
                var customDescription = configuredMethods?.FirstOrDefault(m => string.Equals(m.Name, method, StringComparison.OrdinalIgnoreCase))?.Description;
                options.Add(new ShippingMethodOption(NormalizeId(method), method, cost, string.IsNullOrWhiteSpace(customDescription) ? description : customDescription, false));
            }

            if (options.Count == 0)
            {
                options.Add(new ShippingMethodOption("standard", "Standard", Math.Max(0, group.Shipping), "Standard delivery", true));
            }

            var cheapest = options.OrderBy(o => o.Cost).First();
            for (var i = 0; i < options.Count; i++)
            {
                var option = options[i];
                options[i] = option with { Recommended = string.Equals(option.Id, cheapest.Id, StringComparison.OrdinalIgnoreCase) };
            }

            return options;
        }

        private (decimal Cost, string Description) CalculateCost(CartSellerGroup group, DeliveryAddress address, string? sellerCountry, string method)
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
            var description = "Standard delivery";

            if (normalized.Contains("express") || normalized.Contains("priority"))
            {
                cost = baseShipping + Math.Max(5, quantity);
                description = "Faster delivery with priority handling";
            }
            else if (normalized.Contains("pickup"))
            {
                cost = 0;
                description = "Collect from a pickup point";
            }
            else if (normalized.Contains("locker"))
            {
                cost = Math.Max(0, baseShipping + Math.Max(1, quantity * 0.5m));
                description = "Deliver to parcel locker";
            }
            else if (normalized.Contains("economy") || normalized.Contains("postal"))
            {
                cost = Math.Max(0, baseShipping * 0.7m);
                description = "Economy delivery";
            }
            else if (normalized.Contains("freight") || normalized.Contains("cargo"))
            {
                cost = baseShipping + Math.Max(8, (decimal)totalWeight * 1.2m);
                description = "Freight delivery for heavy items";
            }

            return (Math.Round(cost, 2, MidpointRounding.AwayFromZero), description);
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
