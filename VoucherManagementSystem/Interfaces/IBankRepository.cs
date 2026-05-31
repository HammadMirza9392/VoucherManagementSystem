using VoucherManagementSystem.Models;

namespace VoucherManagementSystem.Interfaces
{
    public interface IBankRepository : IGenericRepository<Bank>
    {
        Task<IEnumerable<Bank>> GetActiveBanksAsync();
        Task UpdateBalanceAsync(int bankId, decimal amount, bool isAddition);
        Task<decimal> GetBankBalanceAsync(int bankId);
        Task<IEnumerable<Voucher>> GetBankTransactionsAsync(int bankId, DateTime fromDate, DateTime toDate);
    }
}