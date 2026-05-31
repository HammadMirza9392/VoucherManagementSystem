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
            var bank = await _context.Banks.FindAsync(bankId);
            if (bank != null)
            {
                if (isAddition)
                    bank.Balance += amount;
                else
                    bank.Balance -= amount;

                await _context.SaveChangesAsync();
            }
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
                .Where(v => (v.BankCustomerPaidId == bankId || v.BankCustomerReceiverId == bankId) &&
                           v.VoucherDate >= fromDate && v.VoucherDate <= toDate)
                .OrderByDescending(v => v.VoucherDate)
                .ToListAsync();
        }
    }
}