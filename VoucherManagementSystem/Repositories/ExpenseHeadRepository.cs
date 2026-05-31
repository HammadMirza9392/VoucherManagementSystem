using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Data;
using VoucherManagementSystem.Interfaces;
using VoucherManagementSystem.Models;

namespace VoucherManagementSystem.Repositories
{
    public class ExpenseHeadRepository : GenericRepository<ExpenseHead>, IExpenseHeadRepository
    {
        public ExpenseHeadRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<ExpenseHead>> GetActiveExpenseHeadsAsync()
        {
            return await _context.ExpenseHeads
                .Where(e => e.IsActive)
                .OrderBy(e => e.Name)
                .ToListAsync();
        }

        public async Task<IEnumerable<ExpenseHead>> GetActiveExpenseHeadsWithDateFilterAsync(DateTime? fromDate, DateTime? toDate)
        {
            var query = _context.ExpenseHeads
                .Where(e => e.IsActive);

            if (fromDate.HasValue)
            {
                query = query.Where(e => e.CreatedDate >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(e => e.CreatedDate <= toDate.Value);
            }

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