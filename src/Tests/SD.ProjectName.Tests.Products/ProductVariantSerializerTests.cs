using System.Collections.Generic;
using SD.ProjectName.Modules.Products.Domain;

namespace SD.ProjectName.Tests.Products
{
    public class ProductVariantSerializerTests
    {
        [Fact]
        public void SerializeAndDeserialize_ShouldRoundTripVariants()
        {
            var variants = new List<ProductVariant>
            {
                new ProductVariant
                {
                    Sku = "VAR-1",
                    Price = 9.99m,
                    Stock = 3,
                    Attributes = new Dictionary<string, string> { { "Size", "S" }, { "Color", "Blue" } },
                    ImageUrl = "https://cdn.example.com/blue-s.jpg"
                },
                new ProductVariant
                {
                    Sku = "VAR-2",
                    Price = 10.99m,
                    Stock = 0,
                    Attributes = new Dictionary<string, string> { { "Size", "M" }, { "Color", "Red" } }
                }
            };

            var serialized = ProductVariantSerializer.Serialize(variants);
            var deserialized = ProductVariantSerializer.Deserialize(serialized);

            Assert.Equal(variants.Count, deserialized.Count);
            Assert.Equal("VAR-1", deserialized[0].Sku);
            Assert.Equal("Blue", deserialized[0].Attributes["Color"]);
            Assert.Equal(0, deserialized[1].Stock);
        }

        [Fact]
        public void Deserialize_ShouldReturnEmptyList_OnInvalidJson()
        {
            var result = ProductVariantSerializer.Deserialize("not-json");

            Assert.Empty(result);
        }
    }
}
