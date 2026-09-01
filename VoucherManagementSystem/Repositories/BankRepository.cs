using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Data;
using VoucherManagementSystem.Interfaces;
using VoucherManagementSystem.Models;
using VoucherManagementSystem.Services.Caching;

namespace VoucherManagementSystem.Repositories
{
    public class BankRepository : GenericRepository<Bank>, IBankRepository
    {
        public BankRepository(ApplicationDbContext context, IMasterDataCache cache)
            : base(context, cache)
        {
        }

        public async Task<IEnumerable<Bank>> GetActiveBanksAsync()
        {
            return await _cache.GetOrCreateAsync(CacheKeys.ActiveBanks, async () =>
                await _context.Banks
                    .AsNoTracking()
                    .Where(b => b.IsActive)
                    .OrderBy(b => b.Name)
                    .ToListAsync());
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

            // Balance changed — Bank Index / lists must refresh
            _cache.InvalidateBanks();
        }

        public async Task<decimal> GetBankBalanceAsync(int bankId)
        {
            // Always live — balances must not be served from cache
            var bank = await _context.Banks.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bankId);
            return bank?.Balance ?? 0;
        }

        public async Task<IEnumerable<Voucher>> GetBankTransactionsAsync(int bankId, DateTime fromDate, DateTime toDate)
        {
            // Projected instead of Include()d: the Bank Statement view renders ~15 values, while a
            // Voucher row carries 52 columns and each Include pulled a whole related row as well.
            return (await _context.Vouchers
                .Where(v => (v.BankCustomerPaidId == bankId || v.BankCustomerReceiverId == bankId) &&
                           v.VoucherDate >= fromDate && v.VoucherDate <= toDate &&
                           // Bank-affecting cash vouchers (CashType = Bank)
                           ((v.CashType == CashType.Bank &&
                             (v.VoucherType == VoucherType.CashPaid ||
                              v.VoucherType == VoucherType.CashReceived ||
                              v.VoucherType == VoucherType.Expense))
                            // BCR (bank-to-bank transfer) — identified by bank fields, no CashType
                            || v.VoucherType == VoucherType.BCR
                            // ATM withdrawals — money out of bank into cash / daily cash
                            || v.VoucherType == VoucherType.ATMCash
                            || v.VoucherType == VoucherType.ATMDailyCash))
                .OrderByDescending(v => v.VoucherDate)
                .Select(v => new
                {
                    v.Id,
                    v.TransactionNumber,
                    v.VoucherDate,
                    v.VoucherType,
                    v.Amount,
                    v.BankCustomerPaidId,
                    v.BankCustomerReceiverId,
                    v.BankCustomerPaidDetails,
                    v.BankCustomerReceiverDetails,
                    v.PurchasingCustomerId,
                    v.ReceivingCustomerId,
                    v.PurchasingCustomerDetails,
                    v.ReceivingCustomerDetails,
                    BankCustomerPaidName = v.BankCustomerPaid!.Name,
                    BankCustomerReceiverName = v.BankCustomerReceiver!.Name,
                    PurchasingCustomerName = v.PurchasingCustomer!.Name,
                    ReceivingCustomerName = v.ReceivingCustomer!.Name
                })
                .ToListAsync())
                .Select(r => new Voucher
                {
                    Id = r.Id,
                    TransactionNumber = r.TransactionNumber,
                    VoucherDate = r.VoucherDate,
                    VoucherType = r.VoucherType,
                    Amount = r.Amount,
                    BankCustomerPaidId = r.BankCustomerPaidId,
                    BankCustomerReceiverId = r.BankCustomerReceiverId,
                    BankCustomerPaidDetails = r.BankCustomerPaidDetails,
                    BankCustomerReceiverDetails = r.BankCustomerReceiverDetails,
                    PurchasingCustomerId = r.PurchasingCustomerId,
                    ReceivingCustomerId = r.ReceivingCustomerId,
                    PurchasingCustomerDetails = r.PurchasingCustomerDetails,
                    ReceivingCustomerDetails = r.ReceivingCustomerDetails,
                    BankCustomerPaid = r.BankCustomerPaidName == null ? null : new Bank { Name = r.BankCustomerPaidName },
                    BankCustomerReceiver = r.BankCustomerReceiverName == null ? null : new Bank { Name = r.BankCustomerReceiverName },
                    PurchasingCustomer = r.PurchasingCustomerName == null ? null : new Customer { Name = r.PurchasingCustomerName },
                    ReceivingCustomer = r.ReceivingCustomerName == null ? null : new Customer { Name = r.ReceivingCustomerName }
                })
                .ToList();
        }
    }
}
