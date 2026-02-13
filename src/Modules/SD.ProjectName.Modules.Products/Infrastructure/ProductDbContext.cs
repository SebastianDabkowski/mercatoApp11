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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ProductModel>(entity =>
            {
                entity.ToTable("ProductModel");
                entity.Property(p => p.Price).HasColumnType("decimal(18,2)");
                entity.Property(p => p.Title).HasMaxLength(200).IsRequired();
                entity.Property(p => p.MainImageUrl).HasMaxLength(500);
                entity.Property(p => p.GalleryImageUrls).HasMaxLength(2000);
                entity.Property(p => p.Category).HasMaxLength(100).IsRequired();
                entity.Property(p => p.Description).HasMaxLength(1000);
                entity.Property(p => p.WeightKg).HasColumnType("decimal(18,3)");
                entity.Property(p => p.LengthCm).HasColumnType("decimal(18,3)");
                entity.Property(p => p.WidthCm).HasColumnType("decimal(18,3)");
                entity.Property(p => p.HeightCm).HasColumnType("decimal(18,3)");
                entity.Property(p => p.ShippingMethods).HasMaxLength(200);
                entity.Property(p => p.WorkflowState)
                      .HasMaxLength(32)
                      .HasDefaultValue(ProductWorkflowStates.Draft)
                      .IsRequired();
                entity.Property(p => p.SellerId).IsRequired();
            });
        }

    }
}
