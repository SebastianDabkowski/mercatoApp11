using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SD.ProjectName.Modules.Products.Domain
{
    public static class ProductAttributeSerializer
    {
        private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

        public static Dictionary<string, string> Deserialize(string? data)
        {
            if (string.IsNullOrWhiteSpace(data))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(data, Options);
                return parsed != null
                    ? new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public static string? Serialize(Dictionary<string, string>? attributes)
        {
            if (attributes == null || !attributes.Any())
            {
                return null;
            }

            return JsonSerializer.Serialize(attributes);
        }
    }
}
