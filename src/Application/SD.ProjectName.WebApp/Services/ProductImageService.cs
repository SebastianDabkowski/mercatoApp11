using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace SD.ProjectName.WebApp.Services
{
    public class ProductImageService
    {
        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp"
        };

        public const long MaxFileBytes = 5 * 1024 * 1024;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<ProductImageService> _logger;

        public ProductImageService(IWebHostEnvironment environment, ILogger<ProductImageService> logger)
        {
            _environment = environment;
            _logger = logger;
        }

        public string? Validate(IFormFile file)
        {
            var extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
            {
                return "Images must be JPG, PNG, or WebP files.";
            }

            if (file.Length <= 0)
            {
                return "Uploaded image is empty.";
            }

            if (file.Length > MaxFileBytes)
            {
                return "Images must be 5 MB or smaller.";
            }

            return null;
        }

        public async Task<ProductImageResult> SaveOptimizedAsync(IFormFile file, string sellerId, CancellationToken cancellationToken = default)
        {
            var validationError = Validate(file);
            if (!string.IsNullOrEmpty(validationError))
            {
                throw new InvalidOperationException(validationError);
            }

            var root = GetWebRoot();
            var uploadsFolder = Path.Combine(root, "uploads", "products", sellerId);
            Directory.CreateDirectory(uploadsFolder);

            var fileBase = $"{Guid.NewGuid():N}";
            var optimizedPath = Path.Combine(uploadsFolder, $"{fileBase}.webp");
            var thumbnailPath = Path.Combine(uploadsFolder, $"{fileBase}_thumb.webp");

            await using var sourceStream = file.OpenReadStream();
            using var image = await Image.LoadAsync(sourceStream, cancellationToken);

            image.Mutate(c => c.AutoOrient().Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(1600, 1600)
            }));

            var encoder = new WebpEncoder { Quality = 80 };
            await image.SaveAsync(optimizedPath, encoder, cancellationToken);

            using var thumbnail = image.Clone(c => c.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(480, 480)
            }));
            await thumbnail.SaveAsync(thumbnailPath, encoder, cancellationToken);

            var optimizedUrl = ToRelativeUrl(root, optimizedPath);
            var thumbnailUrl = ToRelativeUrl(root, thumbnailPath);
            _logger.LogInformation("Stored product image for seller {SellerId} at {ImagePath}", sellerId, optimizedUrl);
            return new ProductImageResult(optimizedUrl, thumbnailUrl);
        }

        private string GetWebRoot()
        {
            if (!string.IsNullOrEmpty(_environment.WebRootPath))
            {
                return _environment.WebRootPath;
            }

            var fallback = Path.Combine(_environment.ContentRootPath, "wwwroot");
            Directory.CreateDirectory(fallback);
            return fallback;
        }

        private static string ToRelativeUrl(string webRoot, string absolutePath)
        {
            var relative = Path.GetRelativePath(webRoot, absolutePath)
                .Replace(Path.DirectorySeparatorChar, '/');
            return "/" + relative.TrimStart('/');
        }
    }

    public record ProductImageResult(string OptimizedUrl, string ThumbnailUrl);
}
