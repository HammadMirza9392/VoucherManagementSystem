using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Data;
using VoucherManagementSystem.Interfaces;
using VoucherManagementSystem.Models;
using VoucherManagementSystem.Services.Caching;

namespace VoucherManagementSystem.Repositories
{
    public class ItemRepository : GenericRepository<Item>, IItemRepository
    {
        public ItemRepository(ApplicationDbContext context, IMasterDataCache cache)
            : base(context, cache)
        {
        }

        public async Task<IEnumerable<Item>> GetActiveItemsAsync()
        {
            return await _cache.GetOrCreateAsync(CacheKeys.ActiveItems, async () =>
                await _context.Items
                    .AsNoTracking()
                    .Where(i => i.IsActive)
                    .OrderBy(i => i.Name)
                    .ToListAsync());
        }

        public async Task<IEnumerable<Item>> GetItemsWithStockAsync()
        {
            return await _cache.GetOrCreateAsync(CacheKeys.ItemsWithStock, async () =>
                await _context.Items
                    .AsNoTracking()
                    .Where(i => i.StockTrackingEnabled && i.IsActive)
                    .OrderBy(i => i.Name)
                    .ToListAsync());
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
                // Stock changed — refresh item lists used on Index / stock screens
                _cache.InvalidateItems();
            }
        }

        public async Task<decimal> GetCurrentStockAsync(int itemId)
        {
            // Always live — stock must not be served from cache
            var item = await _context.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId);
            return item?.CurrentStock ?? 0;
        }

        public async Task<decimal> GetItemRateForCustomerAsync(int itemId, int customerId)
        {
            return await _cache.GetOrCreateAsync(CacheKeys.ResolvedItemRate(itemId, customerId), async () =>
            {
                var customerRate = await _context.CustomerItemRates
                    .AsNoTracking()
                    .FirstOrDefaultAsync(cir => cir.CustomerId == customerId && cir.ItemId == itemId);

                if (customerRate != null)
                    return customerRate.Rate;

                var item = await _context.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId);
                return item?.DefaultRate ?? 0m;
            });
        }
    }
}
