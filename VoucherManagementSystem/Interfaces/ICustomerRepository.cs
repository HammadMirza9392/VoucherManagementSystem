using VoucherManagementSystem.Models;

namespace VoucherManagementSystem.Interfaces
{
    public interface ICustomerRepository : IGenericRepository<Customer>
    {
        Task<IEnumerable<Customer>> GetActiveCustomersAsync();
        Task<CustomerItemRate> GetCustomerItemRateAsync(int customerId, int itemId);
        Task<CustomerItemRate> AddCustomerItemRateAsync(int customerId, int itemId, decimal rate);
        Task UpdateCustomerItemRateAsync(int customerId, int itemId, decimal rate);
        Task<IEnumerable<CustomerItemRate>> GetCustomerRatesAsync(int customerId);
    }
}