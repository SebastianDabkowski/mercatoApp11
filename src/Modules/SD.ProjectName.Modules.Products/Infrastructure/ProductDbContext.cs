using Microsoft.EntityFrameworkCore;
using SD.ProjectName.Modules.Products.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SD.ProjectName.Modules.Products.Infrastructure
{
    public class ProductDbContext : DbContext
    {
        public ProductDbContext(DbContextOptions<ProductDbContext> options)
            : base(options)
        {
        }

        public DbSet<ProductModel> Products { get; set; }
        public DbSet<CategoryModel> Categories { get; set; }
        public DbSet<ProductImportJob> ProductImportJobs { get; set; }
        public DbSet<ProductExportJob> ProductExportJobs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ProductModel>(entity =>
            {
                entity.ToTable("ProductModel");
                entity.Property(p => p.Price).HasColumnType("decimal(18,2)");
                entity.Property(p => p.Title).HasMaxLength(200).IsRequired();
                entity.Property(p => p.MerchantSku).HasMaxLength(100).IsRequired();
                entity.Property(p => p.MainImageUrl).HasMaxLength(500);
                entity.Property(p => p.GalleryImageUrls).HasMaxLength(2000);
                entity.Property(p => p.Category).HasMaxLength(256).IsRequired();
                entity.Property(p => p.Description).HasMaxLength(1000);
                entity.Property(p => p.WeightKg).HasColumnType("decimal(18,3)");
                entity.Property(p => p.LengthCm).HasColumnType("decimal(18,3)");
                entity.Property(p => p.WidthCm).HasColumnType("decimal(18,3)");
                entity.Property(p => p.HeightCm).HasColumnType("decimal(18,3)");
                entity.Property(p => p.ShippingMethods).HasMaxLength(200);
                entity.Property(p => p.HasVariants).HasDefaultValue(false);
                entity.Property(p => p.VariantData).HasMaxLength(8000);
                entity.Property(p => p.WorkflowState)
                      .HasMaxLength(32)
                      .HasDefaultValue(ProductWorkflowStates.Draft)
                      .IsRequired();
                entity.Property(p => p.SellerId).IsRequired();
                entity.HasOne<CategoryModel>()
                      .WithMany()
                      .HasForeignKey(p => p.CategoryId)
                      .OnDelete(DeleteBehavior.SetNull);
                entity.HasIndex(p => new { p.SellerId, p.MerchantSku }).IsUnique();
            });

            modelBuilder.Entity<CategoryModel>(entity =>
            {
                entity.ToTable("CategoryModel");
                entity.Property(c => c.Name).HasMaxLength(120).IsRequired();
                entity.Property(c => c.FullPath).HasMaxLength(256).IsRequired();
                entity.Property(c => c.SortOrder).HasDefaultValue(0);
                entity.Property(c => c.IsActive).HasDefaultValue(true);
                entity.HasOne(c => c.Parent)
                      .WithMany(c => c.Children)
                      .HasForeignKey(c => c.ParentId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(c => new { c.ParentId, c.SortOrder });
            });

            modelBuilder.Entity<ProductImportJob>(entity =>
            {
                entity.ToTable("ProductImportJob");
                entity.Property(j => j.Status).HasMaxLength(64).IsRequired();
                entity.Property(j => j.FileName).HasMaxLength(200).IsRequired();
                entity.Property(j => j.SellerId).HasMaxLength(450).IsRequired();
                entity.Property(j => j.Summary).HasMaxLength(4000);
                entity.Property(j => j.ContentType).HasMaxLength(128);
                entity.Property(j => j.TemplateVersion).HasMaxLength(32).HasDefaultValue("v1");
                entity.HasIndex(j => new { j.SellerId, j.CreatedOn });
            });

            modelBuilder.Entity<ProductExportJob>(entity =>
            {
                entity.ToTable("ProductExportJob");
                entity.Property(j => j.Status).HasMaxLength(64).IsRequired();
                entity.Property(j => j.Format).HasMaxLength(16).IsRequired();
                entity.Property(j => j.FileName).HasMaxLength(200).IsRequired();
                entity.Property(j => j.SellerId).HasMaxLength(450).IsRequired();
                entity.Property(j => j.Search).HasMaxLength(200);
                entity.Property(j => j.WorkflowState).HasMaxLength(32);
                entity.Property(j => j.Summary).HasMaxLength(4000);
                entity.Property(j => j.ContentType).HasMaxLength(128);
                entity.HasIndex(j => new { j.SellerId, j.CreatedOn });
            });
        }

    }
}
