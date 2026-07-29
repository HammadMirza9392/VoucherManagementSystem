using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Data;
using VoucherManagementSystem.Interfaces;
using VoucherManagementSystem.Models;

namespace VoucherManagementSystem.Repositories
{
    public class VoucherRepository : GenericRepository<Voucher>, IVoucherRepository
    {
        public VoucherRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<string> GenerateTransactionNumberAsync(VoucherType type)
        {
            var prefix = type switch
            {
                VoucherType.Purchase => "PUR",
                VoucherType.Sale => "SAL",
                VoucherType.Expense => "EXP",
                VoucherType.Hazri => "HAZ",
                VoucherType.CashPaid => "CPD",
                VoucherType.CashReceived => "CRC",
                VoucherType.CCR => "CCR",
                VoucherType.BCR => "BCR",
                VoucherType.AdvancedPayment => "ADV",
                VoucherType.AdvancedCashPaid => "ACP",
                VoucherType.AdvancedCashReceived => "ACR",
                VoucherType.ATMCash => "ATM",
                VoucherType.ATMDailyCash => "ATMD",
                _ => "VCH"
            };

            // Include deleted vouchers so unique index is never violated on soft-deleted numbers
            var lastVoucher = await _context.Vouchers
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(v => v.TransactionNumber.StartsWith($"{prefix}-"))
                .OrderByDescending(v => v.Id)
                .Select(v => v.TransactionNumber)
                .FirstOrDefaultAsync();

            int nextNumber = 1;
            if (!string.IsNullOrEmpty(lastVoucher))
            {
                var lastNumber = lastVoucher.Split('-').Last();
                if (int.TryParse(lastNumber, out int num))
                {
                    nextNumber = num + 1;
                }
            }

            return $"{prefix}-{nextNumber}";
        }

        public async Task<IEnumerable<Voucher>> GetVouchersByTypeAsync(VoucherType type)
        {
            return await _context.Vouchers
                .AsNoTracking()
                .AsSplitQuery()
                .Include(v => v.PurchasingCustomer)
                .Include(v => v.ReceivingCustomer)
                .Include(v => v.Item)
                .Include(v => v.Project)
                .Where(v => v.VoucherType == type)
                .OrderByDescending(v => v.VoucherDate)
                .ToListAsync();
        }

        public async Task<IEnumerable<Voucher>> GetVouchersByDateRangeAsync(DateTime fromDate, DateTime toDate)
        {
            return await _context.Vouchers
                .AsNoTracking()
                .AsSplitQuery()
                .Include(v => v.PurchasingCustomer)
                .Include(v => v.ReceivingCustomer)
                .Include(v => v.Item)
                .Include(v => v.Project)
                .Include(v => v.ExpenseHead)
                .Include(v => v.BankCustomerPaid)
                .Where(v => v.VoucherDate >= fromDate && v.VoucherDate <= toDate)
                .OrderByDescending(v => v.VoucherDate)
                .ToListAsync();
        }

        public async Task<IEnumerable<Voucher>> GetVouchersByCustomerAsync(int customerId)
        {
            return await _context.Vouchers
                .AsNoTracking()
                .AsSplitQuery()
                .Include(v => v.PurchasingCustomer)
                .Include(v => v.ReceivingCustomer)
                .Include(v => v.Item)
                .Include(v => v.Project)
                .Where(v => v.PurchasingCustomerId == customerId || v.ReceivingCustomerId == customerId)
                .OrderByDescending(v => v.VoucherDate)
                .ToListAsync();
        }

        public async Task<IEnumerable<Voucher>> GetVouchersByProjectAsync(int projectId)
        {
            return await _context.Vouchers
                .AsNoTracking()
                .AsSplitQuery()
                .Include(v => v.PurchasingCustomer)
                .Include(v => v.ReceivingCustomer)
                .Include(v => v.Item)
                .Include(v => v.ExpenseHead)
                .Where(v => v.ProjectId == projectId)
                .OrderByDescending(v => v.VoucherDate)
                .ToListAsync();
        }

        public async Task<IEnumerable<Voucher>> GetVouchersWithDetailsAsync()
        {
            return await _context.Vouchers
                .AsNoTracking()
                .AsSplitQuery()
                .Include(v => v.PurchasingCustomer)
                .Include(v => v.ReceivingCustomer)
                .Include(v => v.BankCustomerPaid)
                .Include(v => v.BankCustomerReceiver)
                .Include(v => v.Item)
                .Include(v => v.ExpenseHead)
                .Include(v => v.Project)
                .OrderByDescending(v => v.VoucherDate)
                .ToListAsync();
        }

        // Includes revoked vouchers (still excludes deleted) — for the GeneralCreate list.
        public async Task<IEnumerable<Voucher>> GetVouchersWithDetailsIncludingRevokedAsync()
        {
            return await _context.Vouchers
                .IgnoreQueryFilters()        // bypass the global !IsDeleted && !IsRevoked filter
                .Where(v => !v.IsDeleted)    // ...but still hide deleted
                .AsNoTracking()
                .AsSplitQuery()
                .Include(v => v.PurchasingCustomer)
                .Include(v => v.ReceivingCustomer)
                .Include(v => v.BankCustomerPaid)
                .Include(v => v.BankCustomerReceiver)
                .Include(v => v.Item)
                .Include(v => v.ExpenseHead)
                .Include(v => v.Project)
                .OrderByDescending(v => v.VoucherDate)
                .ToListAsync();
        }

        // Base query for the voucher lists: excludes deleted but keeps revoked rows
        // (they render with a Restore button). Returned as IQueryable so callers can
        // filter/page in the database instead of pulling everything into memory.
        public IQueryable<Voucher> QueryVouchersIncludingRevoked()
        {
            return _context.Vouchers
                .IgnoreQueryFilters()        // bypass the global !IsDeleted && !IsRevoked filter
                .Where(v => !v.IsDeleted)    // ...but still hide deleted
                .AsNoTracking()
                .AsSplitQuery()
                .Include(v => v.PurchasingCustomer)
                .Include(v => v.ReceivingCustomer)
                .Include(v => v.BankCustomerPaid)
                .Include(v => v.BankCustomerReceiver)
                .Include(v => v.Item)
                .Include(v => v.ExpenseHead)
                .Include(v => v.Project);
        }

        // Only revoked (and not deleted) vouchers — for the Revoked Vouchers report.
        public async Task<IEnumerable<Voucher>> GetRevokedVouchersAsync()
        {
            return await _context.Vouchers
                .IgnoreQueryFilters()
                .Where(v => v.IsRevoked && !v.IsDeleted)
                .AsNoTracking()
                .AsSplitQuery()
                .Include(v => v.PurchasingCustomer)
                .Include(v => v.ReceivingCustomer)
                .Include(v => v.BankCustomerPaid)
                .Include(v => v.BankCustomerReceiver)
                .Include(v => v.Item)
                .Include(v => v.ExpenseHead)
                .Include(v => v.Project)
                .OrderByDescending(v => v.RevokedDate)
                .ToListAsync();
        }

        public async Task<decimal> GetProjectProfitLossAsync(int projectId, DateTime fromDate, DateTime toDate)
        {
            // Optimized: Calculate directly in database instead of loading all vouchers
            var revenue = await _context.Vouchers
                .AsNoTracking()
                .Where(v => v.ProjectId == projectId &&
                            v.VoucherDate >= fromDate &&
                            v.VoucherDate <= toDate &&
                            (v.VoucherType == VoucherType.Sale || v.VoucherType == VoucherType.CashReceived))
                .SumAsync(v => v.Amount);

            var expenses = await _context.Vouchers
                .AsNoTracking()
                .Where(v => v.ProjectId == projectId &&
                            v.VoucherDate >= fromDate &&
                            v.VoucherDate <= toDate &&
                            (v.VoucherType == VoucherType.Purchase ||
                             v.VoucherType == VoucherType.Expense ||
                             v.VoucherType == VoucherType.CashPaid ||
                             v.VoucherType == VoucherType.Hazri))
                .SumAsync(v => v.Amount);

            return revenue - expenses;
        }

        public async Task UpdateStockAsync(int itemId, decimal quantity, bool isAddition)
        {
            var item = await _context.Items.FindAsync(itemId);
            if (item != null && item.StockTrackingEnabled)
            {
                if (isAddition)
                    item.CurrentStock += quantity;
                else
                    item.CurrentStock -= quantity;

                await _context.SaveChangesAsync();
            }
        }
    }
}