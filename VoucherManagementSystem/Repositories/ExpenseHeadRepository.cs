using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Data;
using VoucherManagementSystem.Interfaces;
using VoucherManagementSystem.Models;
using VoucherManagementSystem.Services.Caching;

namespace VoucherManagementSystem.Repositories
{
    public class ExpenseHeadRepository : GenericRepository<ExpenseHead>, IExpenseHeadRepository
    {
        public ExpenseHeadRepository(ApplicationDbContext context, IMasterDataCache cache)
            : base(context, cache)
        {
        }

        public async Task<IEnumerable<ExpenseHead>> GetActiveExpenseHeadsAsync()
        {
            return await _cache.GetOrCreateAsync(CacheKeys.ActiveExpenseHeads, async () =>
                await _context.ExpenseHeads
                    .AsNoTracking()
                    .Where(e => e.IsActive)
                    .OrderBy(e => e.Name)
                    .ToListAsync());
        }

        public async Task<IEnumerable<ExpenseHead>> GetActiveExpenseHeadsWithDateFilterAsync(DateTime? fromDate, DateTime? toDate)
        {
            // Unfiltered active list is cached; date-filtered Index queries stay live
            if (!fromDate.HasValue && !toDate.HasValue)
                return await GetActiveExpenseHeadsAsync();

            var query = _context.ExpenseHeads
                .AsNoTracking()
                .Where(e => e.IsActive);

            if (fromDate.HasValue)
                query = query.Where(e => e.CreatedDate >= fromDate.Value);

            if (toDate.HasValue)
                query = query.Where(e => e.CreatedDate <= toDate.Value);

            return await query.OrderBy(e => e.Name).ToListAsync();
        }

        public async Task<decimal> GetTotalExpensesByHeadAsync(int expenseHeadId, DateTime fromDate, DateTime toDate)
        {
            return await _context.Vouchers
                .Where(v => v.ExpenseHeadId == expenseHeadId &&
                           (v.VoucherType == VoucherType.Expense || v.VoucherType == VoucherType.Hazri) &&
                           v.VoucherDate >= fromDate && v.VoucherDate <= toDate)
                .SumAsync(v => v.Amount);
        }

        public async Task<IEnumerable<Voucher>> GetExpensesByHeadAsync(int expenseHeadId)
        {
            return await _context.Vouchers
                .Include(v => v.ExpenseHead)
                .Include(v => v.Project)
                .Where(v => v.ExpenseHeadId == expenseHeadId)
                .OrderByDescending(v => v.VoucherDate)
                .ToListAsync();
        }
    }
}
