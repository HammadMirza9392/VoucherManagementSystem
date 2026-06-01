using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoucherManagementSystem.Models;

namespace VoucherManagementSystem.Data
{
    public static class SeedData
    {
        public static void Initialize(IServiceProvider serviceProvider)
        {
            using var context = new ApplicationDbContext(
                serviceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());

            // Seed default admin user
            if (!context.Users.Any())
            {
                context.Users.Add(new User
                {
                    Username = "admin",
                    Password = "admin123",
                    FullName = "System Administrator",
                    Email = "admin@system.com",
                    Role = "Admin",
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow,
                    CreatedBy = "system"
                });
                context.SaveChanges();
            }

            // Seed master password
            if (!context.MasterPasswords.Any())
            {
                context.MasterPasswords.Add(new MasterPassword
                {
                    PasswordType = "MasterLock",
                    Password = "admin123"
                });
                context.SaveChanges();
            }

            // Seed default theme settings
            if (!context.ThemeSettings.Any())
            {
                context.ThemeSettings.Add(new ThemeSettings
                {
                    ThemeMode = "Light",
                    PrimaryColor = "#0d6efd",
                    SecondaryColor = "#6c757d",
                    SuccessColor = "#198754",
                    DangerColor = "#dc3545",
                    WarningColor = "#ffc107",
                    InfoColor = "#0dcaf0",
                    BackgroundColor = "#ffffff",
                    TextColor = "#212529",
                    CardBackgroundColor = "#ffffff",
                    NavbarBackgroundColor = "#ffffff",
                    SidebarBackgroundColor = "#ffffff",
                    FooterBackgroundColor = "#f8f9fa",
                    LastUpdated = DateTime.UtcNow,
                    IsActive = true
                });
                context.SaveChanges();
            }

            // Check if data already exists
            if (context.Customers.Any())
            {
                return; // Database has been seeded
            }

            // Seed Customers
            var customers = new Customer[]
            {
                new Customer { Name = "Wajid Mandi", Phone = "0300-0000000", Address = "Mandi shah jiwna", IsActive = true, CreatedDate = DateTime.UtcNow },
                new Customer { Name = "Muddasr Jutt", Phone = "0321-1234567", Address = "Jhang city", IsActive = true, CreatedDate = DateTime.UtcNow },
                new Customer { Name = "Salman Bkr", Phone = "0333-7654321", Address = "Bhakkar road", IsActive = true, CreatedDate = DateTime.UtcNow },
                new Customer { Name = "Shahid Loom", Phone = "0345-5555555", Address = "Adhi wal", IsActive = true, CreatedDate = DateTime.UtcNow },
                new Customer { Name = "Bhai Bilal", Phone = "0312-9999999", Address = "Chiniot road", IsActive = true, CreatedDate = DateTime.UtcNow }
            };
            context.Customers.AddRange(customers);
            context.SaveChanges();

            // Seed Items
            var items = new Item[]
            {
                new Item { Name = "Karak bottle", Unit = "", StockTrackingEnabled = true, CurrentStock = 0, DefaultRate = 0, IsActive = true, CreatedDate = DateTime.UtcNow },
                new Item { Name = "Scrap", Unit = "", StockTrackingEnabled = true, CurrentStock = 0, DefaultRate = 0, IsActive = true, CreatedDate = DateTime.UtcNow },
                new Item { Name = "Gatta", Unit = "", StockTrackingEnabled = true, CurrentStock = 0, DefaultRate = 0, IsActive = true, CreatedDate = DateTime.UtcNow },
                new Item { Name = "Kapi", Unit = "", StockTrackingEnabled = true, CurrentStock = 0, DefaultRate = 0, IsActive = true, CreatedDate = DateTime.UtcNow },
                new Item { Name = "kitab", Unit = "", StockTrackingEnabled = true, CurrentStock = 0, DefaultRate = 0, IsActive = true, CreatedDate = DateTime.UtcNow },
            };
            context.Items.AddRange(items);
            context.SaveChanges();

            // Seed Banks
            var banks = new Bank[]
            {
                new Bank { Name = "HBL", AccountNumber = "1234567890", Balance = 100000, Details = "Main Branch Account", IsActive = true, CreatedDate = DateTime.UtcNow },
                new Bank { Name = "MCB", AccountNumber = "0987654321", Balance = 100000, Details = "Corporate Account", IsActive = true, CreatedDate = DateTime.UtcNow },
                new Bank { Name = "UBL", AccountNumber = "1122334455", Balance = 100000, Details = "Business Account", IsActive = true, CreatedDate = DateTime.UtcNow },
                new Bank { Name = "Allied Bank", AccountNumber = "5544332211", Balance = 100000, Details = "Current Account", IsActive = true, CreatedDate = DateTime.UtcNow },
                new Bank { Name = "Meezan Bank", AccountNumber = "9988776655", Balance = 100000, Details = "Islamic Banking Account", IsActive = true, CreatedDate = DateTime.UtcNow }
            };
            context.Banks.AddRange(banks);
            context.SaveChanges();

            // Seed ExpenseHeads
            var expenseHeads = new ExpenseHead[]
            {
                new ExpenseHead { Name = "Labor Charges", DefaultRate = 1200, Notes = "Daily labor wages", IsActive = true, CreatedDate = DateTime.UtcNow },
                new ExpenseHead { Name = "Transportation", DefaultRate = 5000, Notes = "Vehicle and fuel expenses", IsActive = true, CreatedDate = DateTime.UtcNow },
                new ExpenseHead { Name = "Utilities", DefaultRate = 0, Notes = "Electricity, Gas, Water bills", IsActive = true, CreatedDate = DateTime.UtcNow },
                new ExpenseHead { Name = "Office Expenses", DefaultRate = 0, Notes = "Stationery and supplies", IsActive = true, CreatedDate = DateTime.UtcNow },
                new ExpenseHead { Name = "Maintenance", DefaultRate = 0, Notes = "Equipment and machinery maintenance", IsActive = true, CreatedDate = DateTime.UtcNow },
                new ExpenseHead { Name = "Marketing", DefaultRate = 0, Notes = "Advertisement and promotion", IsActive = true, CreatedDate = DateTime.UtcNow },
                new ExpenseHead { Name = "Miscellaneous", DefaultRate = 0, Notes = "Other expenses", IsActive = true, CreatedDate = DateTime.UtcNow }
            };
            context.ExpenseHeads.AddRange(expenseHeads);
            context.SaveChanges();

            // Seed Projects
            var projects = new Project[]
            {
                new Project
                {
                    Name = "Project Karak bottle",
                    Description = "5-story commercial plaza project",
                    StartDate = DateTime.UtcNow.AddMonths(-3),
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow
                },
                new Project
                {
                    Name = "Project Scrap",
                    Description = "50 houses residential project",
                    StartDate = DateTime.UtcNow.AddMonths(-6),
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow
                },
                new Project
                {
                    Name = "Project Gata",
                    Description = "Government infrastructure project",
                    StartDate = DateTime.UtcNow.AddMonths(-1),
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow
                },
                new Project
                {
                    Name = "Project Kapi",
                    Description = "Modern shopping center development",
                    StartDate = DateTime.UtcNow.AddMonths(-2),
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow
                }
            };
            context.Projects.AddRange(projects);
            context.SaveChanges();

            // Seed Customer Item Rates
            var customerRates = new CustomerItemRate[]
            {
                new CustomerItemRate { CustomerId = customers[1].Id, ItemId = items[0].Id, Rate = 1200 },
                new CustomerItemRate { CustomerId = customers[1].Id, ItemId = items[1].Id, Rate = 182000 },
                new CustomerItemRate { CustomerId = customers[2].Id, ItemId = items[0].Id, Rate = 1280 },
                new CustomerItemRate { CustomerId = customers[2].Id, ItemId = items[2].Id, Rate = 11500 }
            };
            context.CustomerItemRates.AddRange(customerRates);
            context.SaveChanges();
        }
    }
}