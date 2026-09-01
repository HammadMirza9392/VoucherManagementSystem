using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Data;
using VoucherManagementSystem.Interfaces;
using VoucherManagementSystem.Models;
using VoucherManagementSystem.Services.Caching;

namespace VoucherManagementSystem.Repositories
{
    public class CustomerRepository : GenericRepository<Customer>, ICustomerRepository
    {
        public CustomerRepository(ApplicationDbContext context, IMasterDataCache cache)
            : base(context, cache)
        {
        }

        public async Task<IEnumerable<Customer>> GetActiveCustomersAsync()
        {
            return await _cache.GetOrCreateAsync(CacheKeys.ActiveCustomers, async () =>
                await _context.Customers
                    .AsNoTracking()
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.Name)
                    .ToListAsync());
        }

        public async Task<CustomerItemRate> GetCustomerItemRateAsync(int customerId, int itemId)
        {
            return (await _cache.GetOrCreateAsync(CacheKeys.CustomerItemRate(customerId, itemId), async () =>
                await _context.CustomerItemRates
                    .AsNoTracking()
                    .Include(cir => cir.Item)
                    .FirstOrDefaultAsync(cir => cir.CustomerId == customerId && cir.ItemId == itemId)))!;
        }

        public async Task<CustomerItemRate> AddCustomerItemRateAsync(int customerId, int itemId, decimal rate)
        {
            // The context runs with QueryTrackingBehavior.NoTracking (see Program.cs), so a row
            // loaded here is NOT tracked — changing a property and calling SaveChangesAsync would
            // save nothing and still report success. AsTracking() is required for the update to
            // actually reach the database.
            var existingRate = await _context.CustomerItemRates
                .AsTracking()
                .FirstOrDefaultAsync(cir => cir.CustomerId == customerId && cir.ItemId == itemId);

            if (existingRate != null)
            {
                existingRate.Rate = rate;
                await _context.SaveChangesAsync();
                _cache.InvalidateCustomerItemRate(customerId, itemId);
                return existingRate;
            }

            var customerRate = new CustomerItemRate
            {
                CustomerId = customerId,
                ItemId = itemId,
                Rate = rate
            };

            _context.CustomerItemRates.Add(customerRate);
            await _context.SaveChangesAsync();
            _cache.InvalidateCustomerItemRate(customerId, itemId);
            return customerRate;
        }

        public async Task UpdateCustomerItemRateAsync(int customerId, int itemId, decimal rate)
        {
            // Same tracking requirement as AddCustomerItemRateAsync above.
            var customerRate = await _context.CustomerItemRates
                .AsTracking()
                .FirstOrDefaultAsync(cir => cir.CustomerId == customerId && cir.ItemId == itemId);

            if (customerRate != null)
            {
                customerRate.Rate = rate;
                await _context.SaveChangesAsync();
                _cache.InvalidateCustomerItemRate(customerId, itemId);
            }
        }

        public async Task<IEnumerable<CustomerItemRate>> GetCustomerRatesAsync(int customerId)
        {
            return await _cache.GetOrCreateAsync(CacheKeys.CustomerRates(customerId), async () =>
                await _context.CustomerItemRates
                    .AsNoTracking()
                    .Include(cir => cir.Item)
                    .Where(cir => cir.CustomerId == customerId)
                    .ToListAsync());
        }
    }
}
