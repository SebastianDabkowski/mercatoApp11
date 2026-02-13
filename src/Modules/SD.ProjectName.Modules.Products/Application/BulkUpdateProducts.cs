using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Domain.Interfaces;

namespace SD.ProjectName.Modules.Products.Application
{
    public class BulkUpdateProducts
    {
        private readonly IProductRepository _repository;

        public BulkUpdateProducts(IProductRepository repository)
        {
            _repository = repository;
        }

        public Task<BulkUpdateResult> PreviewAsync(string sellerId, IEnumerable<int> productIds, BulkUpdateCommand command)
        {
            return ExecuteAsync(sellerId, productIds, command, applyChanges: false);
        }

        public Task<BulkUpdateResult> ApplyAsync(string sellerId, IEnumerable<int> productIds, BulkUpdateCommand command)
        {
            return ExecuteAsync(sellerId, productIds, command, applyChanges: true);
        }

        private async Task<BulkUpdateResult> ExecuteAsync(string sellerId, IEnumerable<int> productIds, BulkUpdateCommand command, bool applyChanges)
        {
            var result = new BulkUpdateResult();
            var ids = productIds?.Distinct().ToList() ?? new List<int>();

            if (!ids.Any())
            {
                result.ValidationErrors.Add("Select at least one product to update.");
                return result;
            }

            var commandErrors = ValidateCommand(command);
            if (commandErrors.Any())
            {
                result.ValidationErrors.AddRange(commandErrors);
                return result;
            }

            var products = await _repository.GetByIds(ids, includeDrafts: true);
            var productLookup = products.ToDictionary(p => p.Id, p => p);

            foreach (var id in ids)
            {
                if (!productLookup.TryGetValue(id, out var product))
                {
                    result.Items.Add(BulkUpdateItemResult.NotFound(id));
                    continue;
                }

                if (product.SellerId != sellerId)
                {
                    result.Items.Add(BulkUpdateItemResult.NotOwned(product));
                    continue;
                }

                var currentPrice = product.Price;
                var currentStock = product.Stock;

                var targetPrice = CalculatePrice(currentPrice, command);
                var targetStock = CalculateStock(currentStock, command);

                var itemError = ValidateResult(targetPrice, targetStock);
                var item = new BulkUpdateItemResult
                {
                    ProductId = product.Id,
                    Title = product.Title,
                    CurrentPrice = currentPrice,
                    CurrentStock = currentStock,
                    NewPrice = targetPrice ?? currentPrice,
                    NewStock = targetStock ?? currentStock,
                    Error = itemError
                };

                if (string.IsNullOrWhiteSpace(itemError) && applyChanges)
                {
                    product.Price = targetPrice ?? currentPrice;
                    product.Stock = targetStock ?? currentStock;
                    await _repository.Update(product);
                    item.Applied = true;
                }

                result.Items.Add(item);
            }

            return result;
        }

        private static List<string> ValidateCommand(BulkUpdateCommand command)
        {
            var errors = new List<string>();
            var priceChangeSelected = command.PriceOperation != BulkPriceOperation.None;
            var stockChangeSelected = command.StockOperation != BulkStockOperation.None;

            if (!priceChangeSelected && !stockChangeSelected)
            {
                errors.Add("Select a price or stock change to apply.");
                return errors;
            }

            if (priceChangeSelected && !command.PriceValue.HasValue)
            {
                errors.Add("Provide a price value for the selected price change.");
            }

            if (stockChangeSelected && !command.StockValue.HasValue)
            {
                errors.Add("Provide a stock value for the selected stock change.");
            }

            if (command.StockValue.HasValue && command.StockValue.Value < 0)
            {
                errors.Add("Stock value cannot be negative.");
            }

            if (command.PriceValue.HasValue && command.PriceValue.Value < 0)
            {
                errors.Add("Price value cannot be negative.");
            }

            if (command.PriceOperation == BulkPriceOperation.SetTo && command.PriceValue.HasValue && command.PriceValue.Value <= 0)
            {
                errors.Add("Price must be greater than zero.");
            }

            return errors;
        }

        private static decimal? CalculatePrice(decimal currentPrice, BulkUpdateCommand command)
        {
            return command.PriceOperation switch
            {
                BulkPriceOperation.SetTo => command.PriceValue.HasValue ? Math.Round(command.PriceValue.Value, 2, MidpointRounding.AwayFromZero) : null,
                BulkPriceOperation.IncreasePercent => command.PriceValue.HasValue ? Math.Round(currentPrice * (1 + command.PriceValue.Value / 100), 2, MidpointRounding.AwayFromZero) : null,
                BulkPriceOperation.DecreasePercent => command.PriceValue.HasValue ? Math.Round(currentPrice * (1 - command.PriceValue.Value / 100), 2, MidpointRounding.AwayFromZero) : null,
                _ => null
            };
        }

        private static int? CalculateStock(int currentStock, BulkUpdateCommand command)
        {
            return command.StockOperation switch
            {
                BulkStockOperation.SetTo => command.StockValue,
                BulkStockOperation.Increase => command.StockValue.HasValue ? currentStock + command.StockValue.Value : null,
                BulkStockOperation.Decrease => command.StockValue.HasValue ? currentStock - command.StockValue.Value : null,
                _ => null
            };
        }

        private static string? ValidateResult(decimal? price, int? stock)
        {
            if (price.HasValue && price.Value < 0.01m)
            {
                return "Price cannot be zero or negative.";
            }

            if (stock.HasValue && stock.Value < 0)
            {
                return "Stock cannot be negative.";
            }

            return null;
        }
    }

    public class BulkUpdateCommand
    {
        public BulkPriceOperation PriceOperation { get; set; } = BulkPriceOperation.None;

        public decimal? PriceValue { get; set; }

        public BulkStockOperation StockOperation { get; set; } = BulkStockOperation.None;

        public int? StockValue { get; set; }
    }

    public enum BulkPriceOperation
    {
        None = 0,
        SetTo = 1,
        IncreasePercent = 2,
        DecreasePercent = 3
    }

    public enum BulkStockOperation
    {
        None = 0,
        SetTo = 1,
        Increase = 2,
        Decrease = 3
    }

    public class BulkUpdateResult
    {
        public List<string> ValidationErrors { get; } = new();

        public List<BulkUpdateItemResult> Items { get; } = new();

        public int AppliedCount => Items.Count(i => i.Applied);
    }

    public class BulkUpdateItemResult
    {
        public int ProductId { get; set; }

        public string Title { get; set; } = string.Empty;

        public decimal CurrentPrice { get; set; }

        public decimal NewPrice { get; set; }

        public int CurrentStock { get; set; }

        public int NewStock { get; set; }

        public string? Error { get; set; }

        public bool Applied { get; set; }

        public static BulkUpdateItemResult NotFound(int id)
        {
            return new BulkUpdateItemResult
            {
                ProductId = id,
                Title = "Unavailable product",
                Error = "Product not found."
            };
        }

        public static BulkUpdateItemResult NotOwned(ProductModel product)
        {
            return new BulkUpdateItemResult
            {
                ProductId = product.Id,
                Title = product.Title,
                CurrentPrice = product.Price,
                CurrentStock = product.Stock,
                NewPrice = product.Price,
                NewStock = product.Stock,
                Error = "You cannot update this product."
            };
        }
    }
}
