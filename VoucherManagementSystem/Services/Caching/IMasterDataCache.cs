namespace VoucherManagementSystem.Services.Caching
{
    public interface IMasterDataCache
    {
        Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? absoluteExpiration = null);

        void Remove(string key);

        void InvalidateCustomers();
        void InvalidateItems();
        void InvalidateBanks();
        void InvalidateProjects();
        void InvalidateExpenseHeads();
        void InvalidateMonMultipliers();

        /// <summary>Clears all rate-related keys for one customer (list + per-item + resolved).</summary>
        void InvalidateCustomerRates(int customerId);

        /// <summary>Clears one customer-item rate and its resolved AJAX value.</summary>
        void InvalidateCustomerItemRate(int customerId, int itemId);

        /// <summary>Clears every cached rate key (used when item default rate changes).</summary>
        void InvalidateAllRates();

        /// <summary>
        /// Called after GenericRepository Add/Update/Delete so master lists stay fresh.
        /// Voucher writes are ignored (transactional data is never cached).
        /// </summary>
        void InvalidateForEntityType(Type entityType);
    }
}
