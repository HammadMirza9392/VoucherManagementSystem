namespace VoucherManagementSystem.Services.Caching
{
    /// <summary>
    /// Central cache key names for master data. Keep keys stable and invalidate on writes.
    /// </summary>
    public static class CacheKeys
    {
        public const string ActiveCustomers = "master:customers:active";
        public const string ActiveItems = "master:items:active";
        public const string ItemsWithStock = "master:items:with-stock";
        public const string ActiveBanks = "master:banks:active";
        public const string ActiveProjects = "master:projects:active";
        public const string ActiveExpenseHeads = "master:expense-heads:active";
        public const string MonMultipliersAll = "master:mon-multipliers:all";

        public static string CustomerRates(int customerId) => $"master:rates:customer:{customerId}";
        public static string CustomerItemRate(int customerId, int itemId) => $"master:rates:customer:{customerId}:item:{itemId}";
        public static string ResolvedItemRate(int itemId, int customerId) => $"master:rates:resolved:{customerId}:{itemId}";
        public static string MonMultiplier(string voucherType) => $"master:mon-multiplier:{voucherType}";
    }
}
