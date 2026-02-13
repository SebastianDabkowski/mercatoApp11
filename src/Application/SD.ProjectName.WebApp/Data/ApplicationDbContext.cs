using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public DbSet<LoginAudit> LoginAudits => Set<LoginAudit>();
        public DbSet<UserAdminAudit> UserAdminAudits => Set<UserAdminAudit>();
        public DbSet<SellerTeamMember> SellerTeamMembers => Set<SellerTeamMember>();
        public DbSet<OrderRecord> Orders => Set<OrderRecord>();
        public DbSet<SellerShippingMethod> SellerShippingMethods => Set<SellerShippingMethod>();
        public DbSet<ShippingAddress> ShippingAddresses => Set<ShippingAddress>();
        public DbSet<ProductReview> ProductReviews => Set<ProductReview>();
        public DbSet<SellerRating> SellerRatings => Set<SellerRating>();
        public DbSet<SellerReputation> SellerReputations => Set<SellerReputation>();
        public DbSet<ProductReviewAudit> ProductReviewAudits => Set<ProductReviewAudit>();
        public DbSet<ProductReviewReport> ProductReviewReports => Set<ProductReviewReport>();
        public DbSet<ProductQuestion> ProductQuestions => Set<ProductQuestion>();
        public DbSet<AnalyticsEvent> AnalyticsEvents => Set<AnalyticsEvent>();

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

                entity.Property(u => u.PayoutSchedule)
                    .HasMaxLength(32)
                    .HasDefaultValue("Weekly");

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

                entity.Property(u => u.BlockedOn);

                entity.Property(u => u.BlockedByUserId)
                    .HasMaxLength(450);

                entity.Property(u => u.BlockedByName)
                    .HasMaxLength(256);

                entity.Property(u => u.BlockReason)
                    .HasMaxLength(512);
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

            builder.Entity<SellerShippingMethod>(entity =>
            {
                entity.HasKey(m => m.Id);
                entity.Property(m => m.StoreOwnerId)
                    .IsRequired()
                    .HasMaxLength(450);
                entity.Property(m => m.Name)
                    .IsRequired()
                    .HasMaxLength(128);
                entity.Property(m => m.Description)
                    .HasMaxLength(1024);
                entity.Property(m => m.BaseCost)
                    .HasColumnType("decimal(18,2)")
                    .HasDefaultValue(0m);
                entity.Property(m => m.DeliveryEstimate)
                    .HasMaxLength(128);
                entity.Property(m => m.Availability)
                    .HasMaxLength(256);
                entity.Property(m => m.ProviderId)
                    .HasMaxLength(64);
                entity.Property(m => m.ProviderServiceCode)
                    .HasMaxLength(64);
                entity.Property(m => m.IsActive)
                    .IsRequired();
                entity.Property(m => m.IsDeleted)
                    .IsRequired();
                entity.Property(m => m.CreatedOn)
                    .IsRequired();
                entity.Property(m => m.UpdatedOn)
                    .IsRequired();
                entity.HasIndex(m => new { m.StoreOwnerId, m.IsDeleted });
            });

            builder.Entity<ShippingAddress>(entity =>
            {
                entity.Property(a => a.UserId)
                    .IsRequired()
                    .HasMaxLength(450);

                entity.Property(a => a.Recipient)
                    .IsRequired()
                    .HasMaxLength(256);

                entity.Property(a => a.Line1)
                    .IsRequired()
                    .HasMaxLength(256);

                entity.Property(a => a.Line2)
                    .HasMaxLength(256);

                entity.Property(a => a.City)
                    .HasMaxLength(128);

                entity.Property(a => a.State)
                    .HasMaxLength(128);

                entity.Property(a => a.PostalCode)
                    .IsRequired()
                    .HasMaxLength(32);

                entity.Property(a => a.Country)
                    .IsRequired()
                    .HasMaxLength(120);

                entity.Property(a => a.Phone)
                    .IsRequired()
                    .HasMaxLength(32);

                entity.Property(a => a.CreatedOn)
                    .IsRequired();

                entity.Property(a => a.UpdatedOn)
                    .IsRequired();

                entity.HasOne(a => a.User)
                    .WithMany()
                    .HasForeignKey(a => a.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(a => new { a.UserId, a.IsDefault })
                    .HasFilter("[IsDefault] = 1")
                    .IsUnique();

                entity.HasIndex(a => new { a.UserId, a.CreatedOn });
            });

            builder.Entity<OrderRecord>(entity =>
            {
                entity.Property(o => o.OrderNumber)
                    .HasMaxLength(40)
                    .IsRequired();

                entity.Property(o => o.Status)
                    .HasMaxLength(32)
                    .IsRequired();

                entity.Property(o => o.BuyerId)
                    .HasMaxLength(450);

                entity.Property(o => o.BuyerEmail)
                    .HasMaxLength(256);

                entity.Property(o => o.BuyerName)
                    .HasMaxLength(256);

                entity.Property(o => o.PaymentMethodId)
                    .HasMaxLength(64);

                entity.Property(o => o.PaymentMethodLabel)
                    .HasMaxLength(128);

                entity.Property(o => o.PaymentReference)
                    .HasMaxLength(128);

                entity.Property(o => o.CartSignature)
                    .HasMaxLength(256);

                entity.Property(o => o.SavedAddressKey)
                    .HasMaxLength(64);

                entity.Property(o => o.DeliveryAddressJson)
                    .IsRequired();

                entity.Property(o => o.DetailsJson)
                    .IsRequired();

                entity.HasIndex(o => o.OrderNumber)
                    .IsUnique();

                entity.HasIndex(o => o.PaymentReference)
                    .IsUnique()
                    .HasFilter("[PaymentReference] IS NOT NULL");

                entity.HasIndex(o => new { o.BuyerId, o.Status, o.CreatedOn });
                entity.HasIndex(o => o.Status);
                entity.HasIndex(o => o.CreatedOn);
            });

            builder.Entity<ProductReview>(entity =>
            {
                entity.Property(r => r.BuyerId)
                    .HasMaxLength(450)
                    .IsRequired();

                entity.Property(r => r.BuyerName)
                    .HasMaxLength(256)
                    .IsRequired();

                entity.Property(r => r.Comment)
                    .HasMaxLength(2000);

                entity.Property(r => r.Status)
                    .HasMaxLength(32)
                    .IsRequired();

                entity.Property(r => r.FlagReason)
                    .HasMaxLength(512);

                entity.Property(r => r.LastModeratedBy)
                    .HasMaxLength(256);

                entity.Property(r => r.CreatedOn)
                    .IsRequired();

                entity.Property(r => r.Rating)
                    .IsRequired();

                entity.HasIndex(r => new { r.OrderId, r.ProductId, r.BuyerId })
                    .IsUnique();

                entity.HasIndex(r => new { r.ProductId, r.Status, r.CreatedOn });
            });

            builder.Entity<ProductReviewAudit>(entity =>
            {
                entity.Property(a => a.Action)
                    .HasMaxLength(64)
                    .IsRequired();

                entity.Property(a => a.Actor)
                    .HasMaxLength(256);

                entity.Property(a => a.Reason)
                    .HasMaxLength(512);

                entity.Property(a => a.FromStatus)
                    .HasMaxLength(32);

                entity.Property(a => a.ToStatus)
                    .HasMaxLength(32);

                entity.Property(a => a.CreatedOn)
                    .IsRequired();

                entity.HasIndex(a => a.ReviewId);
            });

            builder.Entity<ProductReviewReport>(entity =>
            {
                entity.Property(r => r.ReporterId)
                    .HasMaxLength(450)
                    .IsRequired();

                entity.Property(r => r.Reason)
                    .HasMaxLength(128)
                    .IsRequired();

                entity.Property(r => r.Details)
                    .HasMaxLength(2000);

                entity.Property(r => r.CreatedOn)
                    .IsRequired();

                entity.HasIndex(r => new { r.ReviewId, r.ReporterId })
                    .IsUnique();
            });

            builder.Entity<ProductQuestion>(entity =>
            {
                entity.Property(q => q.ProductId)
                    .IsRequired();

                entity.Property(q => q.SellerId)
                    .IsRequired()
                    .HasMaxLength(450);

                entity.Property(q => q.BuyerId)
                    .IsRequired()
                    .HasMaxLength(450);

                entity.Property(q => q.BuyerName)
                    .IsRequired()
                    .HasMaxLength(256);

                entity.Property(q => q.Question)
                    .IsRequired()
                    .HasMaxLength(2000);

                entity.Property(q => q.Answer)
                    .HasMaxLength(2000);

                entity.Property(q => q.Status)
                    .IsRequired()
                    .HasMaxLength(32)
                    .HasDefaultValue(ProductQuestionStatuses.Open);

                entity.Property(q => q.CreatedOn)
                    .IsRequired();

                entity.HasIndex(q => new { q.ProductId, q.Status, q.CreatedOn });
                entity.HasIndex(q => q.SellerId);
            });

            builder.Entity<SellerRating>(entity =>
            {
                entity.Property(r => r.SellerId)
                    .HasMaxLength(450)
                    .IsRequired();

                entity.Property(r => r.BuyerId)
                    .HasMaxLength(450)
                    .IsRequired();

                entity.Property(r => r.Rating)
                    .IsRequired();

                entity.Property(r => r.CreatedOn)
                    .IsRequired();

                entity.HasIndex(r => new { r.OrderId, r.SellerId, r.BuyerId })
                    .IsUnique();

                entity.HasIndex(r => new { r.SellerId, r.CreatedOn });
            });

            builder.Entity<SellerReputation>(entity =>
            {
                entity.Property(r => r.SellerId)
                    .HasMaxLength(450)
                    .IsRequired();

                entity.Property(r => r.Label)
                    .HasMaxLength(64)
                    .IsRequired();

                entity.Property(r => r.CalculatedOn)
                    .IsRequired();

                entity.HasIndex(r => r.SellerId)
                    .IsUnique();
            });

            builder.Entity<UserAdminAudit>(entity =>
            {
                entity.Property(a => a.UserId)
                    .HasMaxLength(450)
                    .IsRequired();

                entity.Property(a => a.ActorUserId)
                    .HasMaxLength(450);

                entity.Property(a => a.ActorName)
                    .HasMaxLength(256);

                entity.Property(a => a.Action)
                    .HasMaxLength(64)
                    .IsRequired();

                entity.Property(a => a.Reason)
                    .HasMaxLength(512);

                entity.Property(a => a.CreatedOn)
                    .IsRequired();

                entity.HasIndex(a => a.UserId);
                entity.HasIndex(a => new { a.UserId, a.CreatedOn });
            });

            builder.Entity<AnalyticsEvent>(entity =>
            {
                entity.Property(e => e.EventType)
                    .HasMaxLength(64)
                    .IsRequired();

                entity.Property(e => e.SessionId)
                    .HasMaxLength(64);

                entity.Property(e => e.UserId)
                    .HasMaxLength(450);

                entity.Property(e => e.SellerId)
                    .HasMaxLength(450);

                entity.Property(e => e.Keyword)
                    .HasMaxLength(256);

                entity.Property(e => e.MetadataJson)
                    .HasColumnType("nvarchar(max)");

                entity.Property(e => e.OccurredOn)
                    .IsRequired();

                entity.HasIndex(e => e.EventType);
                entity.HasIndex(e => e.OccurredOn);
                entity.HasIndex(e => new { e.EventType, e.OccurredOn });
            });
        }
    }
}
