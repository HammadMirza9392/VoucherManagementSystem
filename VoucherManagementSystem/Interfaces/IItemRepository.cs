using VoucherManagementSystem.Models;

namespace VoucherManagementSystem.Interfaces
{
    public interface IItemRepository : IGenericRepository<Item>
    {
        Task<IEnumerable<Item>> GetActiveItemsAsync();
        Task<IEnumerable<Item>> GetItemsWithStockAsync();
        Task UpdateStockAsync(int itemId, decimal quantity, bool isAddition);
        Task<decimal> GetCurrentStockAsync(int itemId);
        Task<decimal> GetItemRateForCustomerAsync(int itemId, int customerId);
    }
}