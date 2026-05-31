using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Data;
using VoucherManagementSystem.Interfaces;
using VoucherManagementSystem.Models;

namespace VoucherManagementSystem.Repositories
{
    public class ProjectRepository : GenericRepository<Project>, IProjectRepository
    {
        public ProjectRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<Project>> GetActiveProjectsAsync()
        {
            return await _context.Projects
                .Where(p => p.IsActive)
                .OrderBy(p => p.Name)
                .ToListAsync();
        }

        public async Task<decimal> GetProjectRevenueAsync(int projectId, DateTime fromDate, DateTime toDate)
        {
            return await _context.Vouchers
                .Where(v => v.ProjectId == projectId &&
                           (v.VoucherType == VoucherType.Sale || v.VoucherType == VoucherType.CashReceived) &&
                           v.VoucherDate >= fromDate && v.VoucherDate <= toDate)
                .SumAsync(v => v.Amount);
        }

        public async Task<decimal> GetProjectExpenseAsync(int projectId, DateTime fromDate, DateTime toDate)
        {
            return await _context.Vouchers
                .Where(v => v.ProjectId == projectId &&
                           (v.VoucherType == VoucherType.Purchase ||
                            v.VoucherType == VoucherType.Expense ||
                            v.VoucherType == VoucherType.Hazri ||
                            v.VoucherType == VoucherType.CashPaid) &&
                           v.VoucherDate >= fromDate && v.VoucherDate <= toDate)
                .SumAsync(v => v.Amount);
        }

        public async Task<IEnumerable<Voucher>> GetProjectVouchersAsync(int projectId)
        {
            return await _context.Vouchers
                .Include(v => v.PurchasingCustomer)
                .Include(v => v.ReceivingCustomer)
                .Include(v => v.Item)
                .Include(v => v.ExpenseHead)
                .Where(v => v.ProjectId == projectId)
                .OrderByDescending(v => v.VoucherDate)
                .ToListAsync();
        }
    }
}