using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public DbSet<LoginAudit> LoginAudits => Set<LoginAudit>();

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

                entity.Property(u => u.KycStatus)
                    .HasMaxLength(32)
                    .HasDefaultValue(KycStatuses.NotRequired);

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

                entity.Property(u => u.EmailVerifiedOn);

                entity.Property(u => u.KycSubmittedOn);

                entity.Property(u => u.KycApprovedOn);

                entity.Property(u => u.TwoFactorMethod)
                    .HasMaxLength(64)
                    .HasDefaultValue(TokenOptions.DefaultEmailProvider);

                entity.Property(u => u.TwoFactorEnabledOn);

                entity.Property(u => u.LastLoginIp)
                    .HasMaxLength(128);

                entity.Property(u => u.LastLoginOn);
            });

            builder.Entity<LoginAudit>(entity =>
            {
                entity.Property(e => e.EventType)
                    .HasMaxLength(64)
                    .IsRequired();

                entity.Property(e => e.Email)
                    .HasMaxLength(256);

                entity.Property(e => e.IpAddress)
                    .HasMaxLength(128);

                entity.Property(e => e.UserAgent)
                    .HasMaxLength(512);

                entity.Property(e => e.OccurredOn)
                    .IsRequired();

                entity.Property(e => e.ExpiresOn)
                    .IsRequired();

                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.OccurredOn);
            });
        }
    }
}
