using VoucherManagementSystem.Models;

namespace VoucherManagementSystem.Interfaces
{
    public interface IVoucherRepository : IGenericRepository<Voucher>
    {
        Task<string> GenerateTransactionNumberAsync(VoucherType type);
        Task<IEnumerable<Voucher>> GetVouchersByTypeAsync(VoucherType type);
        Task<IEnumerable<Voucher>> GetVouchersByDateRangeAsync(DateTime fromDate, DateTime toDate);
        Task<IEnumerable<Voucher>> GetVouchersByCustomerAsync(int customerId);
        Task<IEnumerable<Voucher>> GetVouchersByProjectAsync(int projectId);
        Task<IEnumerable<Voucher>> GetVouchersWithDetailsAsync();
        // Same as above but INCLUDES revoked vouchers (still excludes deleted).
        // Used by the GeneralCreate list so revoked rows stay visible for Restore.
        Task<IEnumerable<Voucher>> GetVouchersWithDetailsIncludingRevokedAsync();
        // Only revoked (and not deleted) vouchers — for the Revoked Vouchers report.
        Task<IEnumerable<Voucher>> GetRevokedVouchersAsync();
        Task<decimal> GetProjectProfitLossAsync(int projectId, DateTime fromDate, DateTime toDate);
        Task UpdateStockAsync(int itemId, decimal quantity, bool isAddition);
    }
}