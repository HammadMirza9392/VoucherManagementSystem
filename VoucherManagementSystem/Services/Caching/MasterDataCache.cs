using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using VoucherManagementSystem.Models;

namespace VoucherManagementSystem.Services.Caching
{
    /// <summary>
    /// In-memory master-data cache with write-through invalidation.
    /// Safe for single-instance / single-user apps; reduces Supabase round-trips.
    /// </summary>
    public class MasterDataCache : IMasterDataCache
    {
        private static readonly TimeSpan DefaultExpiration = TimeSpan.FromMinutes(30);

        private readonly IMemoryCache _cache;
        private readonly ConcurrentDictionary<string, byte> _knownKeys = new();

        public MasterDataCache(IMemoryCache cache)
        {
            _cache = cache;
        }

        public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? absoluteExpiration = null)
        {
            if (_cache.TryGetValue(key, out var existing))
                return (T)existing!;

            var value = await factory();

            // IMemoryCache does not store null; skip caching so callers can still return null.
            if (value is null)
                return value!;

            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = absoluteExpiration ?? DefaultExpiration
            };
            options.RegisterPostEvictionCallback((evictedKey, _, _, _) =>
            {
                if (evictedKey is string s)
                    _knownKeys.TryRemove(s, out _);
            });

            _cache.Set(key, value, options);
            _knownKeys[key] = 0;
            return value;
        }

        public void Remove(string key)
        {
            _cache.Remove(key);
            _knownKeys.TryRemove(key, out _);
        }

        public void InvalidateCustomers() => Remove(CacheKeys.ActiveCustomers);

        public void InvalidateItems()
        {
            Remove(CacheKeys.ActiveItems);
            Remove(CacheKeys.ItemsWithStock);
        }

        public void InvalidateBanks() => Remove(CacheKeys.ActiveBanks);

        public void InvalidateProjects() => Remove(CacheKeys.ActiveProjects);

        public void InvalidateExpenseHeads() => Remove(CacheKeys.ActiveExpenseHeads);

        public void InvalidateMonMultipliers()
        {
            Remove(CacheKeys.MonMultipliersAll);
            RemoveKeysWithPrefix("master:mon-multiplier:");
        }

        public void InvalidateCustomerRates(int customerId)
        {
            Remove(CacheKeys.CustomerRates(customerId));
            RemoveKeysWithPrefix($"master:rates:customer:{customerId}:item:");
            RemoveKeysWithPrefix($"master:rates:resolved:{customerId}:");
        }

        public void InvalidateCustomerItemRate(int customerId, int itemId)
        {
            Remove(CacheKeys.CustomerItemRate(customerId, itemId));
            Remove(CacheKeys.ResolvedItemRate(itemId, customerId));
            Remove(CacheKeys.CustomerRates(customerId));
        }

        public void InvalidateAllRates()
        {
            RemoveKeysWithPrefix("master:rates:");
        }

        public void InvalidateForEntityType(Type entityType)
        {
            if (entityType == typeof(Customer))
            {
                InvalidateCustomers();
                return;
            }

            if (entityType == typeof(Item))
            {
                InvalidateItems();
                // DefaultRate may change — clear resolved rates that fall back to it
                InvalidateAllRates();
                return;
            }

            if (entityType == typeof(Bank))
            {
                InvalidateBanks();
                return;
            }

            if (entityType == typeof(Project))
            {
                InvalidateProjects();
                return;
            }

            if (entityType == typeof(ExpenseHead))
            {
                InvalidateExpenseHeads();
                return;
            }

            if (entityType == typeof(MonMultiplier))
            {
                InvalidateMonMultipliers();
                return;
            }

            // Voucher and other transactional entities: do not cache / no-op
        }

        private void RemoveKeysWithPrefix(string prefix)
        {
            foreach (var key in _knownKeys.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                    Remove(key);
            }
        }
    }
}
