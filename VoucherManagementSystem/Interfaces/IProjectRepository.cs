using VoucherManagementSystem.Models;

namespace VoucherManagementSystem.Interfaces
{
    public interface IProjectRepository : IGenericRepository<Project>
    {
        Task<IEnumerable<Project>> GetActiveProjectsAsync();
        Task<decimal> GetProjectRevenueAsync(int projectId, DateTime fromDate, DateTime toDate);
        Task<decimal> GetProjectExpenseAsync(int projectId, DateTime fromDate, DateTime toDate);
        Task<IEnumerable<Voucher>> GetProjectVouchersAsync(int projectId);
    }
}