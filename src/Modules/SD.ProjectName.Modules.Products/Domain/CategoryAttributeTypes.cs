using System;

namespace SD.ProjectName.Modules.Products.Domain
{
    public static class CategoryAttributeTypes
    {
        public const string Text = "text";
        public const string Number = "number";
        public const string List = "list";

        public static string Normalize(string? type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return Text;
            }

            var normalized = type.Trim().ToLowerInvariant();
            return normalized switch
            {
                Text => Text,
                Number => Number,
                List => List,
                _ => Text
            };
        }

        public static bool IsValid(string? type)
        {
            var normalized = Normalize(type);
            return normalized == Text || normalized == Number || normalized == List;
        }
    }
}
