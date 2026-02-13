using System;
using System.Collections.Generic;
using System.Linq;

namespace SD.ProjectName.Modules.Products.Domain
{
    public static class ProductConditions
    {
        public const string New = "New";
        public const string Used = "Used";
        public const string Refurbished = "Refurbished";

        public static readonly string[] Allowed = [New, Used, Refurbished];

        public static bool IsValid(string? condition) =>
            !string.IsNullOrWhiteSpace(condition) &&
            Allowed.Contains(condition, StringComparer.OrdinalIgnoreCase);

        public static string Normalize(string? condition)
        {
            if (string.IsNullOrWhiteSpace(condition))
            {
                return New;
            }

            var match = Allowed.FirstOrDefault(c => c.Equals(condition, StringComparison.OrdinalIgnoreCase));
            return match ?? New;
        }
    }

    public class ProductFilterContext
    {
        public string? Search { get; set; }

        public IEnumerable<int>? CategoryIds { get; set; }
    }

    public class ProductFilterOptions : ProductFilterContext
    {
        public decimal? MinPrice { get; set; }

        public decimal? MaxPrice { get; set; }

        public string? Condition { get; set; }

        public string? SellerId { get; set; }

        public bool HasAnyFilters() =>
            (CategoryIds != null && CategoryIds.Any()) ||
            MinPrice.HasValue ||
            MaxPrice.HasValue ||
            !string.IsNullOrWhiteSpace(Condition) ||
            !string.IsNullOrWhiteSpace(SellerId);

        public ProductFilterContext ToContext() => new()
        {
            Search = Search,
            CategoryIds = CategoryIds
        };
    }

    public class ProductFilterMetadata
    {
        public decimal? MinPrice { get; set; }

        public decimal? MaxPrice { get; set; }

        public List<string> Conditions { get; set; } = new();

        public List<string> SellerIds { get; set; } = new();
    }
}
