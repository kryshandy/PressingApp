using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pressing.Core.Entities;

namespace Pressing.Core.Data;

public class PressingDbContext : DbContext
{
    public PressingDbContext(DbContextOptions<PressingDbContext> options) : base(options)
    {
    }

    public DbSet<Client> Clients => Set<Client>();
    public DbSet<StaffUser> StaffUsers => Set<StaffUser>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<CategoryPrice> CategoryPrices => Set<CategoryPrice>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Pickup> Pickups => Set<Pickup>();
    public DbSet<PickupItem> PickupItems => Set<PickupItem>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<LoyaltyTransaction> LoyaltyTransactions => Set<LoyaltyTransaction>();
    public DbSet<EmailMessage> EmailMessages => Set<EmailMessage>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<StaffNotification> StaffNotifications => Set<StaffNotification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HaveConversion<DecimalToCentsConverter>();
        configurationBuilder.Properties<string>().HaveMaxLength(4000);
    }

    private sealed class DecimalToCentsConverter : ValueConverter<decimal, long>
    {
        public DecimalToCentsConverter()
            : base(
                v => (long)Math.Round(v * 100m, MidpointRounding.AwayFromZero),
                v => v / 100m)
        {
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Client>(entity =>
        {
            entity.HasIndex(c => c.PhoneNormalized).IsUnique();
            entity.HasIndex(c => c.Phone);
            entity.HasIndex(c => c.Email);
            entity.HasIndex(c => c.LastName);
            entity.Property(c => c.FirstName).HasMaxLength(80).IsRequired();
            entity.Property(c => c.LastName).HasMaxLength(80).IsRequired();
            entity.Property(c => c.Phone).HasMaxLength(30).IsRequired();
            entity.Property(c => c.WhatsApp).HasMaxLength(30).IsRequired();
        });

        modelBuilder.Entity<StaffUser>(entity =>
        {
            entity.HasIndex(s => s.Username).IsUnique();
            entity.HasIndex(s => s.Email);
            entity.Property(s => s.Role).HasConversion<string>();
            entity.Property(s => s.Username).HasMaxLength(60).IsRequired();
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasIndex(c => c.Name).IsUnique();
            entity.HasMany(c => c.Prices)
                .WithOne(p => p.Category)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(c => c.Name).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<CategoryPrice>(entity =>
        {
            entity.HasIndex(p => new { p.CategoryId, p.ServiceType }).IsUnique();
            entity.Property(p => p.ServiceType).HasConversion<string>();
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasIndex(o => o.TicketNumber).IsUnique();
            entity.HasIndex(o => o.Status);
            entity.HasIndex(o => o.CreatedAt);
            entity.HasIndex(o => o.DueDate);
            entity.HasIndex(o => o.ClientId);
            entity.Property(o => o.Status).HasConversion<string>();
            entity.Property(o => o.DeliveryType).HasConversion<string>();
            entity.HasOne(o => o.Client)
                .WithMany(c => c.Orders)
                .HasForeignKey(o => o.ClientId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(o => o.CreatedByStaff)
                .WithMany(s => s.Orders)
                .HasForeignKey(o => o.CreatedByStaffId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasMany(o => o.Items)
                .WithOne(i => i.Order)
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(o => o.Payments)
                .WithOne(p => p.Order)
                .HasForeignKey(p => p.OrderId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(o => o.Pickups)
                .WithOne(p => p.Order)
                .HasForeignKey(p => p.OrderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.Property(i => i.Status).HasConversion<string>();
            entity.Property(i => i.ServiceType).HasConversion<string>();
            entity.HasOne(i => i.Category)
                .WithMany()
                .HasForeignKey(i => i.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.Property(p => p.Method).HasConversion<string>();
            entity.Property(p => p.Type).HasConversion<string>();
            entity.HasOne(p => p.Client)
                .WithMany(c => c.Payments)
                .HasForeignKey(p => p.ClientId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(p => p.StaffUser)
                .WithMany()
                .HasForeignKey(p => p.StaffUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(p => p.PaidAt);
        });

        modelBuilder.Entity<Pickup>(entity =>
        {
            entity.HasOne(p => p.Client)
                .WithMany()
                .HasForeignKey(p => p.ClientId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(p => p.StaffUser)
                .WithMany()
                .HasForeignKey(p => p.StaffUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasMany(p => p.Items)
                .WithOne(i => i.Pickup)
                .HasForeignKey(i => i.PickupId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PickupItem>(entity =>
        {
            entity.HasOne(i => i.OrderItem)
                .WithMany()
                .HasForeignKey(i => i.OrderItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Promotion>(entity =>
        {
            entity.HasIndex(p => p.Code).IsUnique();
            entity.Property(p => p.Type).HasConversion<string>();
            entity.Property(p => p.Code).HasMaxLength(40).IsRequired();
        });

        modelBuilder.Entity<LoyaltyTransaction>(entity =>
        {
            entity.Property(t => t.Type).HasConversion<string>();
            entity.HasOne(t => t.Client)
                .WithMany(c => c.LoyaltyTransactions)
                .HasForeignKey(t => t.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(t => t.Order)
                .WithMany()
                .HasForeignKey(t => t.OrderId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<EmailMessage>(entity =>
        {
            entity.Property(e => e.Status).HasConversion<string>();
            entity.Property(e => e.HtmlBody).HasMaxLength(200_000);
            entity.Property(e => e.TextBody).HasMaxLength(200_000);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasIndex(t => t.TokenHash);
            entity.HasOne(t => t.Client)
                .WithMany()
                .HasForeignKey(t => t.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StaffNotification>(entity =>
        {
            entity.Property(n => n.Kind).HasConversion<string>();
            entity.HasIndex(n => new { n.IsRead, n.CreatedAt });
        });

        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.HasKey(s => s.Key);
            entity.Property(s => s.Key).HasMaxLength(100);
        });
    }
}
