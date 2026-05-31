using VoucherManagementSystem.Models;

namespace VoucherManagementSystem.Interfaces
{
    public interface IExpenseHeadRepository : IGenericRepository<ExpenseHead>
    {
        Task<IEnumerable<ExpenseHead>> GetActiveExpenseHeadsAsync();
        Task<IEnumerable<ExpenseHead>> GetActiveExpenseHeadsWithDateFilterAsync(DateTime? fromDate, DateTime? toDate);
        Task<decimal> GetTotalExpensesByHeadAsync(int expenseHeadId, DateTime fromDate, DateTime toDate);
        Task<IEnumerable<Voucher>> GetExpensesByHeadAsync(int expenseHeadId);
    }
}