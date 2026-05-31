using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Data;
using VoucherManagementSystem.Interfaces;
using VoucherManagementSystem.Models;

namespace VoucherManagementSystem.Repositories
{
    public class CustomerRepository : GenericRepository<Customer>, ICustomerRepository
    {
        public CustomerRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<Customer>> GetActiveCustomersAsync()
        {
            return await _context.Customers
                .Where(c => c.IsActive)
                .OrderBy(c => c.Name)
                .ToListAsync();
        }

        public async Task<CustomerItemRate> GetCustomerItemRateAsync(int customerId, int itemId)
        {
            return await _context.CustomerItemRates
                .Include(cir => cir.Item)
                .FirstOrDefaultAsync(cir => cir.CustomerId == customerId && cir.ItemId == itemId);
        }

        public async Task<CustomerItemRate> AddCustomerItemRateAsync(int customerId, int itemId, decimal rate)
        {
            var existingRate = await GetCustomerItemRateAsync(customerId, itemId);
            if (existingRate != null)
            {
                existingRate.Rate = rate;
                await _context.SaveChangesAsync();
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
            return customerRate;
        }

        public async Task UpdateCustomerItemRateAsync(int customerId, int itemId, decimal rate)
        {
            var customerRate = await GetCustomerItemRateAsync(customerId, itemId);
            if (customerRate != null)
            {
                customerRate.Rate = rate;
                await _context.SaveChangesAsync();
            }
        }

        public async Task<IEnumerable<CustomerItemRate>> GetCustomerRatesAsync(int customerId)
        {
            return await _context.CustomerItemRates
                .Include(cir => cir.Item)
                .Where(cir => cir.CustomerId == customerId)
                .ToListAsync();
        }
    }
}
