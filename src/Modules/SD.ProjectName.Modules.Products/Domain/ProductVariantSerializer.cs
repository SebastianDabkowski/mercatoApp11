using System.Collections.Generic;
using System.Text.Json;

namespace SD.ProjectName.Modules.Products.Domain
{
    public static class ProductVariantSerializer
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        public static string? Serialize(List<ProductVariant>? variants)
        {
            if (variants == null || variants.Count == 0)
            {
                return null;
            }

            return JsonSerializer.Serialize(variants, Options);
        }

        public static List<ProductVariant> Deserialize(string? data)
        {
            if (string.IsNullOrWhiteSpace(data))
            {
                return new List<ProductVariant>();
            }

            try
            {
                var variants = JsonSerializer.Deserialize<List<ProductVariant>>(data, Options);
                return variants ?? new List<ProductVariant>();
            }
            catch
            {
                return new List<ProductVariant>();
            }
        }
    }
}

