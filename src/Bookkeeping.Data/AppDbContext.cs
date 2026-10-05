using Bookkeeping.Core.Enums;
using Bookkeeping.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Bookkeeping.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Fund> Funds => Set<Fund>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalEntryLine> JournalEntryLines => Set<JournalEntryLine>();
    public DbSet<Sponsor> Sponsors => Set<Sponsor>();
    public DbSet<SponsorTarget> SponsorTargets => Set<SponsorTarget>();
    public DbSet<OrgProgram> Programs => Set<OrgProgram>();
    public DbSet<Donation> Donations => Set<Donation>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<CashBankTransfer> CashBankTransfers => Set<CashBankTransfer>();
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<Budget> Budgets => Set<Budget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ---- Fund ----
        modelBuilder.Entity<Fund>(e =>
        {
            e.HasIndex(f => f.Name).IsUnique();
            e.Property(f => f.Name).HasMaxLength(100).IsRequired();
            e.Property(f => f.Description).HasMaxLength(500);
        });

        // ---- Account (Chart of Accounts) ----
        modelBuilder.Entity<Account>(e =>
        {
            e.HasIndex(a => a.Code).IsUnique();
            e.Property(a => a.Code).HasMaxLength(20).IsRequired();
            e.Property(a => a.Name).HasMaxLength(200).IsRequired();
            e.HasOne(a => a.Fund).WithMany(f => f.Accounts).HasForeignKey(a => a.FundId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.Parent).WithMany(a => a.Children).HasForeignKey(a => a.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        // ---- JournalEntry ----
        modelBuilder.Entity<JournalEntry>(e =>
        {
            e.HasIndex(j => j.Date);
            e.Property(j => j.Description).HasMaxLength(500).IsRequired();
            e.Property(j => j.Reference).HasMaxLength(100);
            e.HasOne(j => j.CreatedByUser).WithMany().HasForeignKey(j => j.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        // ---- JournalEntryLine ----
        modelBuilder.Entity<JournalEntryLine>(e =>
        {
            e.HasOne(l => l.JournalEntry).WithMany(j => j.Lines).HasForeignKey(l => l.JournalEntryId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(l => l.Account).WithMany(a => a.JournalEntryLines).HasForeignKey(l => l.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.Fund).WithMany().HasForeignKey(l => l.FundId).OnDelete(DeleteBehavior.Restrict);
            e.Property(l => l.DebitAmount).HasColumnType("decimal(18,2)");
            e.Property(l => l.CreditAmount).HasColumnType("decimal(18,2)");
            e.Property(l => l.Description).HasMaxLength(500);
        });

        // ---- Sponsor ----
        modelBuilder.Entity<Sponsor>(e =>
        {
            e.HasIndex(s => s.Name);
            e.Property(s => s.Name).HasMaxLength(200).IsRequired();
            e.Property(s => s.Address).HasMaxLength(500);
            e.Property(s => s.Phone).HasMaxLength(50);
            e.Property(s => s.Email).HasMaxLength(200);
            e.Property(s => s.Notes).HasMaxLength(2000);
        });

        // ---- OrgProgram ----
        modelBuilder.Entity<OrgProgram>(e =>
        {
            e.HasIndex(p => p.Name).IsUnique();
            e.Property(p => p.Name).HasMaxLength(200).IsRequired();
            e.Property(p => p.Description).HasMaxLength(500);
            e.HasOne(p => p.Fund).WithMany().HasForeignKey(p => p.FundId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.IncomeAccount).WithMany().HasForeignKey("IncomeAccountId").OnDelete(DeleteBehavior.SetNull);
            e.HasOne(p => p.ExpenseAccount).WithMany().HasForeignKey("ExpenseAccountId").OnDelete(DeleteBehavior.SetNull);
        });

        // ---- SponsorTarget ----
        modelBuilder.Entity<SponsorTarget>(e =>
        {
            e.HasIndex(t => new { t.SponsorId, t.ProgramId, t.Year }).IsUnique();
            e.Property(t => t.TargetAmount).HasColumnType("decimal(18,2)");
            e.HasOne(t => t.Sponsor).WithMany(s => s.Targets).HasForeignKey(t => t.SponsorId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(t => t.Program).WithMany(p => p.SponsorTargets).HasForeignKey(t => t.ProgramId).OnDelete(DeleteBehavior.Restrict);
        });

        // ---- Donation ----
        modelBuilder.Entity<Donation>(e =>
        {
            e.HasIndex(d => d.Date);
            e.HasIndex(d => d.ReceiptNumber);
            e.Property(d => d.Amount).HasColumnType("decimal(18,2)");
            e.Property(d => d.ReceiptNumber).HasMaxLength(50);
            e.Property(d => d.Notes).HasMaxLength(2000);
            e.HasOne(d => d.Sponsor).WithMany(s => s.Donations).HasForeignKey(d => d.SponsorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(d => d.Program).WithMany(p => p.Donations).HasForeignKey(d => d.ProgramId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(d => d.JournalEntry).WithOne(j => j.Donation).HasForeignKey<Donation>(d => d.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(d => d.CreatedByUser).WithMany().HasForeignKey(d => d.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.Property(d => d.PaymentMethod).HasConversion(new EnumToStringConverter<PaymentMethod>());
        });

        // ---- Expense ----
        modelBuilder.Entity<Expense>(e =>
        {
            e.HasIndex(ex => ex.Date);
            e.Property(ex => ex.Amount).HasColumnType("decimal(18,2)");
            e.Property(ex => ex.VendorName).HasMaxLength(200).IsRequired();
            e.Property(ex => ex.Notes).HasMaxLength(2000);
            e.Property(ex => ex.ReceiptAttachmentPath).HasMaxLength(500);
            e.HasOne(ex => ex.Program).WithMany(p => p.Expenses).HasForeignKey(ex => ex.ProgramId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(ex => ex.JournalEntry).WithOne(j => j.Expense).HasForeignKey<Expense>(ex => ex.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(ex => ex.CreatedByUser).WithMany().HasForeignKey(ex => ex.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.Property(ex => ex.PaymentMethod).HasConversion(new EnumToStringConverter<PaymentMethod>());
        });

        // ---- CashBankTransfer ----
        modelBuilder.Entity<CashBankTransfer>(e =>
        {
            e.HasIndex(t => t.Date);
            e.Property(t => t.Amount).HasColumnType("decimal(18,2)");
            e.Property(t => t.Direction).HasConversion(new EnumToStringConverter<TransferDirection>()).HasMaxLength(20).IsRequired();
            e.Property(t => t.Notes).HasMaxLength(500);
            e.HasOne(t => t.JournalEntry).WithOne(j => j.CashBankTransfer).HasForeignKey<CashBankTransfer>(t => t.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.CreatedByUser).WithMany().HasForeignKey(t => t.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        // ---- User ----
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.Username).HasMaxLength(100).IsRequired();
            e.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
            e.Property(u => u.FullName).HasMaxLength(200).IsRequired();
            e.Property(u => u.Role).HasConversion(new EnumToStringConverter<UserRole>());
        });

        // ---- AuditLog ----
        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasIndex(a => a.Timestamp);
            e.HasIndex(a => new { a.EntityType, a.EntityId });
            e.Property(a => a.Action).HasMaxLength(50).IsRequired();
            e.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
            e.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        // ---- AppSetting ----
        modelBuilder.Entity<AppSetting>(e =>
        {
            e.HasIndex(s => s.Key).IsUnique();
            e.Property(s => s.Key).HasMaxLength(100).IsRequired();
            e.Property(s => s.Value).HasMaxLength(500);
        });

        // ---- Budget ----
        modelBuilder.Entity<Budget>(e =>
        {
            e.HasIndex(b => new { b.Year, b.Month, b.ProgramId }).IsUnique();
            e.HasIndex(b => new { b.Year, b.ProgramId }).IsUnique()
                .HasDatabaseName("IX_Budgets_AnnualProgram")
                .HasFilter("Month IS NULL AND ProgramId IS NOT NULL");
            e.Property(b => b.Amount).HasColumnType("decimal(18,2)");
            e.Property(b => b.Notes).HasMaxLength(500);
            e.HasOne(b => b.Program).WithMany().HasForeignKey(b => b.ProgramId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---- Seed Data ----
        SeedData(modelBuilder);
    }

    private static void SeedData(ModelBuilder modelBuilder)
    {
        // Seed the General Fund (unrestricted)
        modelBuilder.Entity<Fund>().HasData(new Fund
        {
            Id = 1,
            Name = "General Fund",
            Description = "Unrestricted fund for general operations",
            IsRestricted = false,
            IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        // Seed core Chart of Accounts
        modelBuilder.Entity<Account>().HasData(
            new Account { Id = 1, Code = "1000", Name = "Cash", AccountType = AccountType.Asset, IsSystem = true, CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Account { Id = 2, Code = "1010", Name = "Bank", AccountType = AccountType.Asset, IsSystem = true, CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Account { Id = 3, Code = "3000", Name = "Unrestricted Net Assets", AccountType = AccountType.Equity, FundId = 1, IsSystem = true, CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        );

        // Seed default admin user (password: "admin" — must be changed on first login)
        modelBuilder.Entity<User>().HasData(new User
        {
            Id = 1,
            Username = "admin",
            PasswordHash = "$2a$11$K7Q5pY8vL3xMqZcBnRfXuOqJ1kP6sT7wV9yA0bC2dE4fG5hI6jK7", // BCrypt hash of "admin"
            FullName = "Administrator",
            Role = UserRole.Administrator,
            IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        // Seed default app settings
        modelBuilder.Entity<AppSetting>().HasData(
            new AppSetting { Id = 1, Key = "AutoLogoutMinutes", Value = "30" },
            new AppSetting { Id = 2, Key = "AutoBackupEnabled", Value = "false" },
            new AppSetting { Id = 3, Key = "AutoBackupIntervalHours", Value = "24" },
            new AppSetting { Id = 4, Key = "OrganizationName", Value = "My Organization" }
        );
    }
}
