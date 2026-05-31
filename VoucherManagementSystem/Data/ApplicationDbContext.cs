// Data/ApplicationDbContext.cs
using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Models;

namespace VoucherManagementSystem.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Voucher> Vouchers { get; set; }
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Item> Items { get; set; }
        public DbSet<Bank> Banks { get; set; }
        public DbSet<ExpenseHead> ExpenseHeads { get; set; }
        public DbSet<Project> Projects { get; set; }
        public DbSet<CustomerItemRate> CustomerItemRates { get; set; }
        public DbSet<CashAdjustment> CashAdjustments { get; set; }
        public DbSet<PageLock> PageLocks { get; set; }
        public DbSet<MasterPassword> MasterPasswords { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<ThemeSettings> ThemeSettings { get; set; }
        public DbSet<MonMultiplier> MonMultipliers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Voucher configurations
            modelBuilder.Entity<Voucher>()
                .HasIndex(v => v.TransactionNumber)
                .IsUnique();

            modelBuilder.Entity<Voucher>()
                .Property(v => v.Amount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Voucher>()
                .Property(v => v.Weight)
                .HasPrecision(18, 3);

            modelBuilder.Entity<Voucher>()
                .Property(v => v.Kat)
                .HasPrecision(18, 3);

            modelBuilder.Entity<Voucher>()
                .Property(v => v.Quantity)
                .HasPrecision(18, 3);

            modelBuilder.Entity<Voucher>()
                .Property(v => v.Rate)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Voucher>()
                .Property(v => v.ExpenseHeadRate)
                .HasPrecision(18, 2);

            // Voucher relationships
            modelBuilder.Entity<Voucher>()
                .HasOne(v => v.PurchasingCustomer)
                .WithMany(c => c.PurchasingVouchers)
                .HasForeignKey(v => v.PurchasingCustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Voucher>()
                .HasOne(v => v.ReceivingCustomer)
                .WithMany(c => c.ReceivingVouchers)
                .HasForeignKey(v => v.ReceivingCustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Voucher>()
                .HasOne(v => v.BankCustomerPaid)
                .WithMany(b => b.PaidVouchers)
                .HasForeignKey(v => v.BankCustomerPaidId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Voucher>()
                .HasOne(v => v.BankCustomerReceiver)
                .WithMany(b => b.ReceivedVouchers)
                .HasForeignKey(v => v.BankCustomerReceiverId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Voucher>()
                .HasOne(v => v.Item)
                .WithMany(i => i.Vouchers)
                .HasForeignKey(v => v.ItemId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Voucher>()
                .HasOne(v => v.ExpenseHead)
                .WithMany(e => e.Vouchers)
                .HasForeignKey(v => v.ExpenseHeadId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Voucher>()
                .HasOne(v => v.Project)
                .WithMany(p => p.Vouchers)
                .HasForeignKey(v => v.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            // Item configurations
            modelBuilder.Entity<Item>()
                .Property(i => i.CurrentStock)
                .HasPrecision(18, 3);

            modelBuilder.Entity<Item>()
                .Property(i => i.DefaultRate)
                .HasPrecision(18, 2);

            // Bank configurations
            modelBuilder.Entity<Bank>()
                .Property(b => b.Balance)
                .HasPrecision(18, 2);

            // CashAdjustment configurations
            modelBuilder.Entity<CashAdjustment>()
                .Property(c => c.Amount)
                .HasPrecision(18, 2);

            // ExpenseHead configurations
            modelBuilder.Entity<ExpenseHead>()
                .Property(e => e.DefaultRate)
                .HasPrecision(18, 2);

            // CustomerItemRate configurations
            modelBuilder.Entity<CustomerItemRate>()
                .HasIndex(cir => new { cir.CustomerId, cir.ItemId })
                .IsUnique();

            modelBuilder.Entity<CustomerItemRate>()
                .Property(cir => cir.Rate)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CustomerItemRate>()
                .HasOne(cir => cir.Customer)
                .WithMany(c => c.CustomerItemRates)
                .HasForeignKey(cir => cir.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CustomerItemRate>()
                .HasOne(cir => cir.Item)
                .WithMany(i => i.CustomerItemRates)
                .HasForeignKey(cir => cir.ItemId)
                .OnDelete(DeleteBehavior.Cascade);

            // User configurations
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique();

            // MonMultiplier configurations
            modelBuilder.Entity<MonMultiplier>()
                .Property(m => m.Multiplier)
                .HasPrecision(18, 4);
        }
    }
}

// Data/SeedData.cs
