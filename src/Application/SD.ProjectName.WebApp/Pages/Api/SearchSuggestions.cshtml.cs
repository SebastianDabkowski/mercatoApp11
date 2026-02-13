using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.Modules.Products.Application;

namespace SD.ProjectName.WebApp.Pages.Api
{
    public class SearchSuggestionsModel : PageModel
    {
        private readonly GetProducts _getProducts;
        private readonly ManageCategories _manageCategories;
        private const int MinQueryLength = 2;
        private const int MaxSuggestions = 5;

        public SearchSuggestionsModel(GetProducts getProducts, ManageCategories manageCategories)
        {
            _getProducts = getProducts;
            _manageCategories = manageCategories;
        }

        public async Task<JsonResult> OnGetAsync([FromQuery(Name = "q")] string? query, CancellationToken cancellationToken)
        {
            var term = NormalizeQuery(query);
            if (term == null)
            {
                return new JsonResult(SearchSuggestionResponse.Empty(MinQueryLength));
            }

            var categories = await _manageCategories.GetActiveCategories();
            var categoryMatches = categories
                .Where(c => !string.IsNullOrWhiteSpace(c.FullPath) && c.FullPath.Contains(term, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.FullPath)
                .Take(MaxSuggestions)
                .Select(c => new SearchCategorySuggestion(c.Id, c.FullPath))
                .ToList();

            var products = await _getProducts.SearchActiveLimited(term, MaxSuggestions, cancellationToken);
            var productMatches = products
                .Select(p => new SearchProductSuggestion(p.Id, p.Title, p.Category, p.Price, p.MainImageUrl))
                .ToList();

            var queries = BuildQuerySuggestions(term, categoryMatches, productMatches);
            var response = new SearchSuggestionResponse(MinQueryLength, queries, categoryMatches, productMatches);

            return new JsonResult(response);
        }

        private static string? NormalizeQuery(string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return null;
            }

            var term = query.Trim();
            if (term.Length < MinQueryLength)
            {
                return null;
            }

            if (term.Length > 200)
            {
                term = term[..200];
            }

            return term;
        }

        private static List<string> BuildQuerySuggestions(string term, List<SearchCategorySuggestion> categories, List<SearchProductSuggestion> products)
        {
            return categories
                .Select(c => c.Name)
                .Concat(products.Select(p => p.Title))
                .Append(term)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxSuggestions)
                .ToList();
        }
    }

    public record SearchCategorySuggestion(int Id, string Name);

    public record SearchProductSuggestion(int Id, string Title, string Category, decimal Price, string? MainImageUrl);

    public record SearchSuggestionResponse(int MinLength, List<string> Queries, List<SearchCategorySuggestion> Categories, List<SearchProductSuggestion> Products)
    {
        public static SearchSuggestionResponse Empty(int minLength) =>
            new(minLength, new List<string>(), new List<SearchCategorySuggestion>(), new List<SearchProductSuggestion>());
    }
}
