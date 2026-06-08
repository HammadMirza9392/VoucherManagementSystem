using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Data;
using VoucherManagementSystem.Interfaces;
using VoucherManagementSystem.Models;

namespace VoucherManagementSystem.Repositories
{
    public class BankRepository : GenericRepository<Bank>, IBankRepository
    {
        public BankRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<Bank>> GetActiveBanksAsync()
        {
            return await _context.Banks
                .Where(b => b.IsActive)
                .OrderBy(b => b.Name)
                .ToListAsync();
        }

        public async Task UpdateBalanceAsync(int bankId, decimal amount, bool isAddition)
        {
            // Use direct SQL update to bypass NoTracking global query behavior
            if (isAddition)
                await _context.Banks
                    .Where(b => b.Id == bankId)
                    .ExecuteUpdateAsync(s => s.SetProperty(b => b.Balance, b => b.Balance + amount));
            else
                await _context.Banks
                    .Where(b => b.Id == bankId)
                    .ExecuteUpdateAsync(s => s.SetProperty(b => b.Balance, b => b.Balance - amount));
        }

        public async Task<decimal> GetBankBalanceAsync(int bankId)
        {
            var bank = await _context.Banks.FindAsync(bankId);
            return bank?.Balance ?? 0;
        }

        public async Task<IEnumerable<Voucher>> GetBankTransactionsAsync(int bankId, DateTime fromDate, DateTime toDate)
        {
            return await _context.Vouchers
                .Include(v => v.BankCustomerPaid)
                .Include(v => v.BankCustomerReceiver)
                .Include(v => v.PurchasingCustomer)
                .Include(v => v.ReceivingCustomer)
                .Where(v => (v.BankCustomerPaidId == bankId || v.BankCustomerReceiverId == bankId) &&
                           v.VoucherDate >= fromDate && v.VoucherDate <= toDate &&
                           // Bank-affecting cash vouchers (CashType = Bank)
                           ((v.CashType == CashType.Bank &&
                             (v.VoucherType == VoucherType.CashPaid ||
                              v.VoucherType == VoucherType.CashReceived ||
                              v.VoucherType == VoucherType.Expense))
                            // BCR (bank-to-bank transfer) — identified by bank fields, no CashType
                            || v.VoucherType == VoucherType.BCR))
                .OrderByDescending(v => v.VoucherDate)
                .ToListAsync();
        }
    }
}