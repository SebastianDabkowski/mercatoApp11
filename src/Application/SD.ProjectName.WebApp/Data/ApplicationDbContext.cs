using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public DbSet<LoginAudit> LoginAudits => Set<LoginAudit>();
        public DbSet<SellerTeamMember> SellerTeamMembers => Set<SellerTeamMember>();

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

                entity.Property(u => u.StoreOwnerId)
                    .HasMaxLength(450);

                entity.Property(u => u.SellerType)
                    .HasMaxLength(32)
                    .HasDefaultValue(SellerTypes.Individual);

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

                entity.HasIndex(u => u.BusinessName)
                    .IsUnique()
                    .HasFilter("[BusinessName] IS NOT NULL");

                entity.Property(u => u.TaxId)
                    .HasMaxLength(128);

                entity.Property(u => u.CompanyRegistrationNumber)
                    .HasMaxLength(128);

                entity.Property(u => u.PersonalIdNumber)
                    .HasMaxLength(128);

                entity.Property(u => u.VerificationContactName)
                    .HasMaxLength(256);

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

                entity.Property(u => u.StoreDescription)
                    .HasMaxLength(2048);

                entity.Property(u => u.ContactEmail)
                    .HasMaxLength(256)
                    .HasDefaultValue(string.Empty);

                entity.Property(u => u.ContactPhone)
                    .HasMaxLength(64);

                entity.Property(u => u.ContactWebsite)
                    .HasMaxLength(256);

                entity.Property(u => u.StoreLogoPath)
                    .HasMaxLength(512);

                entity.Property(u => u.PayoutMethod)
                    .HasMaxLength(64)
                    .HasDefaultValue("BankTransfer");

                entity.Property(u => u.PayoutAccount)
                    .HasMaxLength(256);

                entity.Property(u => u.PayoutBankAccount)
                    .HasMaxLength(256);

                entity.Property(u => u.PayoutBankRouting)
                    .HasMaxLength(128);

                entity.Property(u => u.PayoutUpdatedOn);

                entity.Property(u => u.OnboardingStatus)
                    .HasMaxLength(64)
                    .HasDefaultValue(OnboardingStatuses.NotStarted);

                entity.Property(u => u.OnboardingStep)
                    .HasDefaultValue(0);

                entity.Property(u => u.OnboardingStartedOn);

                entity.Property(u => u.OnboardingCompletedOn);

                entity.HasIndex(u => u.StoreOwnerId);

                entity.Property(u => u.CartData)
                    .HasColumnType("nvarchar(max)");
            });

            builder.Entity<SellerTeamMember>(entity =>
            {
                entity.HasKey(m => m.Id);
                entity.Property(m => m.StoreOwnerId)
                    .IsRequired()
                    .HasMaxLength(450);
                entity.Property(m => m.Email)
                    .IsRequired()
                    .HasMaxLength(256);
                entity.Property(m => m.Role)
                    .IsRequired()
                    .HasMaxLength(64);
                entity.Property(m => m.Status)
                    .IsRequired()
                    .HasMaxLength(32);
                entity.Property(m => m.InvitationCode)
                    .IsRequired()
                    .HasMaxLength(64);
                entity.Property(m => m.InvitedOn)
                    .IsRequired();
                entity.HasIndex(m => m.InvitationCode)
                    .IsUnique();
                entity.HasIndex(m => new { m.StoreOwnerId, m.Email });
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
