using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using SD.ProjectName.WebApp.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace SD.ProjectName.Tests.Products;

public class ProductImageServiceTests
{
    [Fact]
    public async Task SaveOptimizedAsync_CreatesOptimizedAndThumbnail()
    {
        var root = Path.Combine(Path.GetTempPath(), "product-image-service-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var environment = new TestEnvironment(root);
        var service = new ProductImageService(environment, NullLogger<ProductImageService>.Instance);

        await using var stream = new MemoryStream();
        using (var image = new Image<Rgba32>(640, 480))
        {
            await image.SaveAsPngAsync(stream);
        }

        stream.Position = 0;
        var file = new FormFile(stream, 0, stream.Length, "files", "sample.png");

        try
        {
            var result = await service.SaveOptimizedAsync(file, "seller123");

            Assert.StartsWith("/uploads/products/seller123/", result.OptimizedUrl);
            Assert.StartsWith("/uploads/products/seller123/", result.ThumbnailUrl);
            Assert.True(File.Exists(Path.Combine(root, result.OptimizedUrl.TrimStart('/'))));
            Assert.True(File.Exists(Path.Combine(root, result.ThumbnailUrl.TrimStart('/'))));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Validate_RejectedExtension_ReturnsError()
    {
        var root = Path.Combine(Path.GetTempPath(), "product-image-service-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var environment = new TestEnvironment(root);
        var service = new ProductImageService(environment, NullLogger<ProductImageService>.Instance);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var file = new FormFile(stream, 0, stream.Length, "files", "not-image.txt");

        var error = service.Validate(file);

        try
        {
            Assert.Equal("Images must be JPG, PNG, or WebP files.", error);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private class TestEnvironment : IWebHostEnvironment
    {
        public TestEnvironment(string webRoot)
        {
            WebRootPath = webRoot;
            ContentRootPath = webRoot;
            WebRootFileProvider = new PhysicalFileProvider(webRoot);
            ContentRootFileProvider = new PhysicalFileProvider(webRoot);
        }

        public string WebRootPath { get; set; }
        public IFileProvider WebRootFileProvider { get; set; }
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider ContentRootFileProvider { get; set; }
        public string ContentRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Development";
    }
}
