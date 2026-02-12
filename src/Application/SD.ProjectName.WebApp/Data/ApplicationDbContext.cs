using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<ApplicationUser>(entity =>
            {
                entity.Property(u => u.AccountStatus)
                    .HasMaxLength(32)
                    .HasDefaultValue(AccountStatuses.Unverified);

                entity.Property(u => u.AccountType)
                    .HasMaxLength(32)
                    .IsRequired();

                entity.Property(u => u.FullName)
                    .HasMaxLength(256)
                    .IsRequired();

                entity.Property(u => u.Address)
                    .HasMaxLength(512)
                    .IsRequired();

                entity.Property(u => u.Country)
                    .HasMaxLength(120)
                    .IsRequired();

                entity.Property(u => u.BusinessName)
                    .HasMaxLength(256);

                entity.Property(u => u.TaxId)
                    .HasMaxLength(128);
            });
        }
    }
}
