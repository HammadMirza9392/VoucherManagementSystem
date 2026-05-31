using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Data;
using VoucherManagementSystem.Interfaces;
using VoucherManagementSystem.Models;

namespace VoucherManagementSystem.Repositories
{
    public class ItemRepository : GenericRepository<Item>, IItemRepository
    {
        public ItemRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<Item>> GetActiveItemsAsync()
        {
            return await _context.Items
                .Where(i => i.IsActive)
                .OrderBy(i => i.Name)
                .ToListAsync();
        }

        public async Task<IEnumerable<Item>> GetItemsWithStockAsync()
        {
            return await _context.Items
                .Where(i => i.StockTrackingEnabled && i.IsActive)
                .OrderBy(i => i.Name)
                .ToListAsync();
        }

        public async Task UpdateStockAsync(int itemId, decimal quantity, bool isAddition)
        {
            var item = await _context.Items.FindAsync(itemId);
            if (item != null)
            {
                if (isAddition)
                    item.CurrentStock += quantity;
                else
                    item.CurrentStock -= quantity;

                item.UpdatedDate = DateTime.Now;
                _context.Items.Update(item);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<decimal> GetCurrentStockAsync(int itemId)
        {
            var item = await _context.Items.FindAsync(itemId);
            return item?.CurrentStock ?? 0;
        }

        public async Task<decimal> GetItemRateForCustomerAsync(int itemId, int customerId)
        {
            var customerRate = await _context.CustomerItemRates
                .FirstOrDefaultAsync(cir => cir.CustomerId == customerId && cir.ItemId == itemId);

            if (customerRate != null)
                return customerRate.Rate;

            var item = await _context.Items.FindAsync(itemId);
            return item?.DefaultRate ?? 0;
        }
    }
}