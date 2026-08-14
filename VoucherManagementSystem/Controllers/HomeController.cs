using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Data;
using VoucherManagementSystem.Helpers;
using VoucherManagementSystem.Interfaces;
using VoucherManagementSystem.Models;
using System.Diagnostics;

namespace VoucherManagementSystem.Controllers
{
    public class HomeController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IVoucherRepository _voucherRepository;
        private readonly IProjectRepository _projectRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IItemRepository _itemRepository;
        private readonly IBankRepository _bankRepository;
        private readonly IExpenseHeadRepository _expenseHeadRepository;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<HomeController> _logger;

        public HomeController(
            IConfiguration configuration,
            IVoucherRepository voucherRepository,
            IProjectRepository projectRepository,
            ICustomerRepository customerRepository,
            IItemRepository itemRepository,
            IBankRepository bankRepository,
            IExpenseHeadRepository expenseHeadRepository,
            ApplicationDbContext context,
            ILogger<HomeController> logger)
        {
            _configuration = configuration;
            _voucherRepository = voucherRepository;
            _projectRepository = projectRepository;
            _customerRepository = customerRepository;
            _itemRepository = itemRepository;
            _bankRepository = bankRepository;
            _expenseHeadRepository = expenseHeadRepository;
            _context = context;
            _logger = logger;
        }

        // Shortest window offered by the dashboard's "Inactive Customers" dropdown (7/10/15/30).
        private const int InactiveCustomerMinDays = 7;

        public async Task<IActionResult> Index()
        {
            var today = DateTimeHelper.PkToday;
            var date = DateTimeHelper.PkToday.AddDays(1);
            var last30Days = today.AddDays(-30);
            var sixMonthsStart = new DateTime(today.Year, today.Month, 1).AddMonths(-5);

            // === Aggregate in the DATABASE, not in memory. ===
            // Previously this pulled every voucher row into memory on every dashboard load,
            // which made egress grow with table size (the free-tier limit was blown by it).
            // Each block below now sends back only the grouped totals it needs, so the amount
            // of data transferred stays roughly constant no matter how large Vouchers gets.
            // The arithmetic and the DR/CR rules are unchanged — only WHERE the sums run moved.

            var vouchers = _context.Vouchers.AsNoTracking();

            var items = await _itemRepository.GetActiveItemsAsync();
            var customers = await _customerRepository.GetActiveCustomersAsync();
            var banks = await _bankRepository.GetActiveBanksAsync();
            var expenseHeads = await _expenseHeadRepository.GetAllAsync();
            var expenseHeadNames = expenseHeads.ToDictionary(e => e.Id, e => e.Name);

            // Basic counts — COUNT(*) in the database, one number back instead of every row.
            ViewBag.TotalVouchers = await vouchers.CountAsync();
            ViewBag.ActiveProjects = (await _projectRepository.GetActiveProjectsAsync()).Count();
            ViewBag.TotalCustomers = customers.Count();
            ViewBag.TotalItems = items.Count();

            // Today's transactions — count and sum computed in the database.
            var todayQuery = vouchers.Where(v => v.VoucherDate >= today && v.VoucherDate < date);
            ViewBag.TodayTransactions = await todayQuery.CountAsync();
            ViewBag.TodayAmount = await todayQuery.SumAsync(v => (decimal?)v.Amount) ?? 0m;

            // The Recent Transactions table was removed from the dashboard, so no voucher
            // rows (or their related Customer/Item/Project/Bank records) are fetched here
            // at all — only the aggregates below.

            // === CAPITAL REPORT DATA ===

            // 1. Stock Value - average rate per item computed from purchase vouchers (grouped once)
            decimal totalStockValue = 0;
            var stockData = new List<DashboardStockItem>();

            var stockPurchaseByItem = (await vouchers
                .Where(v => v.ItemId.HasValue && v.VoucherType == VoucherType.Purchase && v.StockInclude)
                .GroupBy(v => v.ItemId!.Value)
                .Select(g => new
                {
                    ItemId = g.Key,
                    Amount = g.Sum(p => p.Amount),
                    Qty = g.Sum(p => p.Quantity ?? 0)
                })
                .ToListAsync())
                .ToDictionary(x => x.ItemId, x => new { x.Amount, x.Qty });

            // Sale-average rate per item — used as a fallback when an item has no
            // purchase rows (typical for an over-sold/negative-stock item). Without
            // this, the rate would fall back to a 0 DefaultRate and the negative stock
            // value would render as 0, hiding it from the card and from Total Capital.
            var stockSaleByItem = (await vouchers
                .Where(v => v.ItemId.HasValue && v.VoucherType == VoucherType.Sale)
                .GroupBy(v => v.ItemId!.Value)
                .Select(g => new
                {
                    ItemId = g.Key,
                    Amount = g.Sum(s => s.Amount),
                    Qty = g.Sum(s => s.Quantity ?? 0)
                })
                .ToListAsync())
                .ToDictionary(x => x.ItemId, x => new { x.Amount, x.Qty });

            foreach (var item in items)
            {
                decimal currentQty = item.CurrentStock;
                // Include items with negative stock too: an over-sold item carries a
                // negative stock value that must reduce both the Stock Items total and
                // Total Capital. Only items at exactly zero stock are skipped.
                if (currentQty != 0)
                {
                    // Resolve the best available rate: purchase average → sale average
                    // → item default. Guarantees a non-zero rate for negative stock so
                    // its value is actually shown and netted into the totals.
                    decimal avgRate = item.DefaultRate;
                    if (stockPurchaseByItem.TryGetValue(item.Id, out var p) && p.Qty > 0)
                    {
                        avgRate = p.Amount / p.Qty;
                    }
                    else if (stockSaleByItem.TryGetValue(item.Id, out var s) && s.Qty > 0)
                    {
                        avgRate = s.Amount / s.Qty;
                    }
                    decimal stockValue = currentQty * avgRate; // negative when currentQty < 0
                    totalStockValue += stockValue;
                    stockData.Add(new DashboardStockItem { Name = item.Name, Quantity = currentQty, Value = stockValue });
                }
            }
            ViewBag.TotalStockValue = totalStockValue;
            ViewBag.StockData = stockData.ToList();

            // 2. Customer Receivables & Payables - computed in memory from the single voucher load.
            // This MUST match the Customer Ledger's net balance logic exactly so the figures agree:
            //   Purchasing side: Purchase = CR (-),  CashPaid/CCR = DR (+)
            //   Receiving  side: Sale = DR (+),  CashReceived/CCR/AdvancedPayment = CR (-)
            // Positive net = receivable (customer owes us), Negative net = payable (we owe them).
            decimal totalReceivables = 0;
            decimal totalPayables = 0;
            var receivablesData = new List<DashboardNameAmount>();
            var payablesData = new List<DashboardNameAmount>();

            var netByCustomer = new Dictionary<int, decimal>();

            // Purchasing side, grouped per customer in the database.
            // Purchase = CR (-);  CashPaid/CCR = DR (+)  — identical to the previous row-by-row loop.
            var purchasingSideNets = await vouchers
                .Where(v => v.VoucherDate < date && v.PurchasingCustomerId.HasValue &&
                            (v.VoucherType == VoucherType.Purchase ||
                             v.VoucherType == VoucherType.CashPaid ||
                             v.VoucherType == VoucherType.CCR))
                .GroupBy(v => v.PurchasingCustomerId!.Value)
                .Select(g => new
                {
                    CustomerId = g.Key,
                    Net = g.Sum(v => v.VoucherType == VoucherType.Purchase ? -v.Amount : v.Amount)
                })
                .ToListAsync();

            foreach (var row in purchasingSideNets)
            {
                netByCustomer[row.CustomerId] = netByCustomer.GetValueOrDefault(row.CustomerId) + row.Net;
            }

            // Receiving side, grouped per customer in the database.
            // Sale = DR (+);  CashReceived/CCR/AdvancedPayment = CR (-)  — same rules as before.
            var receivingSideNets = await vouchers
                .Where(v => v.VoucherDate < date && v.ReceivingCustomerId.HasValue &&
                            (v.VoucherType == VoucherType.Sale ||
                             v.VoucherType == VoucherType.CashReceived ||
                             v.VoucherType == VoucherType.CCR ||
                             v.VoucherType == VoucherType.AdvancedPayment))
                .GroupBy(v => v.ReceivingCustomerId!.Value)
                .Select(g => new
                {
                    CustomerId = g.Key,
                    Net = g.Sum(v => v.VoucherType == VoucherType.Sale ? v.Amount : -v.Amount)
                })
                .ToListAsync();

            foreach (var row in receivingSideNets)
            {
                netByCustomer[row.CustomerId] = netByCustomer.GetValueOrDefault(row.CustomerId) + row.Net;
            }

            foreach (var customer in customers)
            {
                decimal netBalance = netByCustomer.GetValueOrDefault(customer.Id);

                if (netBalance > 0)
                {
                    totalReceivables += netBalance;
                    receivablesData.Add(new DashboardNameAmount { Name = customer.Name, Amount = netBalance });
                }
                else if (netBalance < 0)
                {
                    totalPayables += Math.Abs(netBalance);
                    payablesData.Add(new DashboardNameAmount { Name = customer.Name, Amount = Math.Abs(netBalance) });
                }
            }
            ViewBag.TotalReceivables = totalReceivables;
            ViewBag.TotalPayables = totalPayables;
            ViewBag.ReceivablesData = receivablesData.OrderByDescending(x => x.Amount).ToList();
            ViewBag.PayablesData = payablesData.OrderByDescending(x => x.Amount).ToList();

            // 3. Cash in Hand — netted in the database.
            // In: Sale, CashReceived, ATMCash (withdrawal → cash in)
            // Out: Purchase, Expense, CashPaid, Hazri
            decimal cashInHand = await vouchers
                .Where(v => v.CashType == CashType.Cash && v.VoucherDate < date &&
                            (v.VoucherType == VoucherType.Sale ||
                             v.VoucherType == VoucherType.CashReceived ||
                             v.VoucherType == VoucherType.ATMCash ||
                             v.VoucherType == VoucherType.Purchase ||
                             v.VoucherType == VoucherType.Expense ||
                             v.VoucherType == VoucherType.CashPaid ||
                             v.VoucherType == VoucherType.Hazri))
                .SumAsync(v => (decimal?)(
                    v.VoucherType == VoucherType.Sale ||
                    v.VoucherType == VoucherType.CashReceived ||
                    v.VoucherType == VoucherType.ATMCash
                        ? v.Amount
                        : -v.Amount)) ?? 0m;

            // Include CashAdjustments
            try
            {
                cashInHand += await _context.CashAdjustments
                    .AsNoTracking()
                    .Where(c => c.AdjustmentDate < date &&
                                (c.AdjustmentType == CashAdjustmentType.CashIn ||
                                 c.AdjustmentType == CashAdjustmentType.CashOut))
                    .SumAsync(c => (decimal?)(
                        c.AdjustmentType == CashAdjustmentType.CashIn ? c.Amount : -c.Amount)) ?? 0m;
            }
            catch { /* CashAdjustments table may not exist */ }

            ViewBag.CashInHand = cashInHand;

            // 3b. Daily Cash Book balance (CashType = DailyCashBook) — netted in the database.
            decimal dailyCashBalance = await vouchers
                .Where(v => v.CashType == CashType.DailyCashBook && v.VoucherDate < date &&
                            (v.VoucherType == VoucherType.Sale ||
                             v.VoucherType == VoucherType.CashReceived ||
                             v.VoucherType == VoucherType.ATMDailyCash ||
                             v.VoucherType == VoucherType.Purchase ||
                             v.VoucherType == VoucherType.Expense ||
                             v.VoucherType == VoucherType.CashPaid ||
                             v.VoucherType == VoucherType.Hazri))
                .SumAsync(v => (decimal?)(
                    v.VoucherType == VoucherType.Sale ||
                    v.VoucherType == VoucherType.CashReceived ||
                    v.VoucherType == VoucherType.ATMDailyCash
                        ? v.Amount
                        : -v.Amount)) ?? 0m;
            ViewBag.DailyCashBalance = dailyCashBalance;

            // 4. Bank Balances
            // bank.Balance is the live running "Current Balance" — it is already adjusted
            // (via BankRepository.UpdateBalanceAsync) every time a bank voucher is created/edited/deleted.
            // So we display it directly. (Previously this re-applied the voucher movements on top of
            // bank.Balance, which double-counted every bank transaction and showed wrong balances.)
            decimal totalBankBalance = 0;
            var bankData = new List<DashboardBankBalance>();

            foreach (var bank in banks)
            {
                decimal balance = bank.Balance;
                totalBankBalance += balance;
                bankData.Add(new DashboardBankBalance { Name = bank.Name, Balance = balance });
            }
            ViewBag.TotalBankBalance = totalBankBalance;
            ViewBag.BankData = bankData;

            // 5. Advanced Customers Balances
            var advancedCustomerData = new List<DashboardAdvancedCustomer>();
            decimal totalAdvancedBalance = 0;

            // Three independent rules, each grouped in the database and then merged:
            //   AdvancedCashPaid     on AdvancedPurchasingCustomerId  → +Amount
            //   AdvancedCashReceived on AdvancedReceivingCustomerId   → -Amount
            //   AdvancedPayment      on ReceivingCustomerId           → -Amount
            // A customer appears here only if at least one advanced voucher references
            // them, which the original in-memory version enforced via advCustomerIds.
            var advBalances = new Dictionary<int, decimal>();

            var advPaid = await vouchers
                .Where(v => v.VoucherType == VoucherType.AdvancedCashPaid &&
                            v.AdvancedPurchasingCustomerId.HasValue)
                .GroupBy(v => v.AdvancedPurchasingCustomerId!.Value)
                .Select(g => new { CustomerId = g.Key, Amount = g.Sum(v => v.Amount) })
                .ToListAsync();

            foreach (var row in advPaid)
                advBalances[row.CustomerId] = advBalances.GetValueOrDefault(row.CustomerId) + row.Amount;

            var advReceived = await vouchers
                .Where(v => v.VoucherType == VoucherType.AdvancedCashReceived &&
                            v.AdvancedReceivingCustomerId.HasValue)
                .GroupBy(v => v.AdvancedReceivingCustomerId!.Value)
                .Select(g => new { CustomerId = g.Key, Amount = g.Sum(v => v.Amount) })
                .ToListAsync();

            foreach (var row in advReceived)
                advBalances[row.CustomerId] = advBalances.GetValueOrDefault(row.CustomerId) - row.Amount;

            var advPayment = await vouchers
                .Where(v => v.VoucherType == VoucherType.AdvancedPayment &&
                            v.ReceivingCustomerId.HasValue)
                .GroupBy(v => v.ReceivingCustomerId!.Value)
                .Select(g => new { CustomerId = g.Key, Amount = g.Sum(v => v.Amount) })
                .ToListAsync();

            foreach (var row in advPayment)
                advBalances[row.CustomerId] = advBalances.GetValueOrDefault(row.CustomerId) - row.Amount;

            // Only active customers are shown, matching the original (which iterated the
            // already-loaded active customer list).
            foreach (var cust in customers)
            {
                if (!advBalances.TryGetValue(cust.Id, out var bal)) continue;
                if (bal != 0)
                {
                    advancedCustomerData.Add(new DashboardAdvancedCustomer { Name = cust.Name, Balance = bal });
                    totalAdvancedBalance += bal;
                }
            }
            ViewBag.AdvancedCustomerData = advancedCustomerData.OrderByDescending(x => Math.Abs(x.Balance)).ToList();
            ViewBag.TotalAdvancedBalance = totalAdvancedBalance;

            // 5b. Inactive Customers — customers with no voucher of ANY kind for a while.
            // The last voucher date per customer is a MAX(...) GROUP BY in the database on each
            // of the four customer links; only those four small result sets cross the wire.
            // Revoked and deleted vouchers are excluded by the global query filter, so a revoked
            // voucher correctly does not count as activity.
            var lastActivity = new Dictionary<int, DateTime>();

            void MergeLastDates(IEnumerable<(int CustomerId, DateTime Last)> rows)
            {
                foreach (var row in rows)
                {
                    if (!lastActivity.TryGetValue(row.CustomerId, out var current) || row.Last > current)
                        lastActivity[row.CustomerId] = row.Last;
                }
            }

            MergeLastDates((await vouchers
                .Where(v => v.PurchasingCustomerId.HasValue)
                .GroupBy(v => v.PurchasingCustomerId!.Value)
                .Select(g => new { CustomerId = g.Key, Last = g.Max(v => v.VoucherDate) })
                .ToListAsync()).Select(x => (x.CustomerId, x.Last)));

            MergeLastDates((await vouchers
                .Where(v => v.ReceivingCustomerId.HasValue)
                .GroupBy(v => v.ReceivingCustomerId!.Value)
                .Select(g => new { CustomerId = g.Key, Last = g.Max(v => v.VoucherDate) })
                .ToListAsync()).Select(x => (x.CustomerId, x.Last)));

            MergeLastDates((await vouchers
                .Where(v => v.AdvancedPurchasingCustomerId.HasValue)
                .GroupBy(v => v.AdvancedPurchasingCustomerId!.Value)
                .Select(g => new { CustomerId = g.Key, Last = g.Max(v => v.VoucherDate) })
                .ToListAsync()).Select(x => (x.CustomerId, x.Last)));

            MergeLastDates((await vouchers
                .Where(v => v.AdvancedReceivingCustomerId.HasValue)
                .GroupBy(v => v.AdvancedReceivingCustomerId!.Value)
                .Select(g => new { CustomerId = g.Key, Last = g.Max(v => v.VoucherDate) })
                .ToListAsync()).Select(x => (x.CustomerId, x.Last)));

            // Balance shown is the customer's single net position: the receivable/payable net
            // plus their advanced balance. Positive = they owe us, negative = we owe them.
            var inactiveCustomers = new List<DashboardInactiveCustomer>();

            foreach (var cust in customers)
            {
                DateTime? lastDate = lastActivity.TryGetValue(cust.Id, out var d) ? d : null;

                // Never-transacted customers qualify for every window, so they use a days value
                // larger than any option in the dropdown.
                int daysInactive = lastDate.HasValue
                    ? Math.Max(0, (int)(today - lastDate.Value.Date).TotalDays)
                    : int.MaxValue;

                // 7 days is the shortest window the dashboard offers; anything more recent than
                // that can never be shown, so it is not sent to the page at all.
                if (daysInactive < InactiveCustomerMinDays) continue;

                inactiveCustomers.Add(new DashboardInactiveCustomer
                {
                    Name = cust.Name,
                    Balance = netByCustomer.GetValueOrDefault(cust.Id) + advBalances.GetValueOrDefault(cust.Id),
                    LastTransactionDate = lastDate,
                    DaysInactive = daysInactive
                });
            }

            ViewBag.InactiveCustomers = inactiveCustomers
                .OrderByDescending(x => Math.Abs(x.Balance))
                .ThenByDescending(x => x.DaysInactive)
                .ToList();

            // 6. Expense Summary (Last 30 days) - grouped in memory by expense head name
            // Grouped by expense-head id in the database; the id → name mapping (and the
            // "Other" fallback for unknown/missing heads) is applied afterwards in memory,
            // exactly as before.
            var expenseByHead = await vouchers
                .Where(v => (v.VoucherType == VoucherType.Expense || v.VoucherType == VoucherType.Hazri) &&
                            v.VoucherDate >= last30Days)
                .GroupBy(v => v.ExpenseHeadId)
                .Select(g => new { ExpenseHeadId = g.Key, Amount = g.Sum(v => v.Amount) })
                .ToListAsync();

            var expenseData = expenseByHead
                .GroupBy(x => x.ExpenseHeadId.HasValue && expenseHeadNames.ContainsKey(x.ExpenseHeadId.Value)
                    ? expenseHeadNames[x.ExpenseHeadId.Value]
                    : "Other")
                .Select(g => new DashboardNameAmount { Name = g.Key, Amount = g.Sum(x => x.Amount) })
                .OrderByDescending(x => x.Amount)
                .Take(10)
                .ToList();
            ViewBag.ExpenseData = expenseData;
            // Expense card total (all-time): only Expense vouchers whose CashType is anything except Credit.
            // Mirrors exactly: SELECT SUM("Amount") FROM "Vouchers" WHERE "VoucherType" = 2 AND "CashType" != 0;
            // Uses IgnoreQueryFilters() so the figure matches the raw DB query (counts every such row).
            var expenseCardTotal = await _context.Vouchers
                .IgnoreQueryFilters()
                .Where(v => v.VoucherType == VoucherType.Expense &&
                            v.CashType.HasValue && v.CashType.Value != CashType.Credit)
                .SumAsync(v => v.Amount);
            ViewBag.TotalExpenses30Days = expenseCardTotal;

            // 6. Monthly Trends (Last 6 months) — one grouped query covering the whole
            // window, then bucketed by (year, month) in memory. Only the six months'
            // worth of totals cross the wire, not the rows behind them.
            var sixMonthsEnd = new DateTime(today.Year, today.Month, 1).AddMonths(1);
            var monthlyTotals = await vouchers
                .Where(v => v.VoucherDate >= sixMonthsStart && v.VoucherDate < sixMonthsEnd &&
                            (v.VoucherType == VoucherType.Sale ||
                             v.VoucherType == VoucherType.Purchase ||
                             v.VoucherType == VoucherType.Expense ||
                             v.VoucherType == VoucherType.Hazri))
                .GroupBy(v => new { v.VoucherDate.Year, v.VoucherDate.Month, v.VoucherType })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    g.Key.VoucherType,
                    Amount = g.Sum(v => v.Amount)
                })
                .ToListAsync();

            var monthlyData = new List<DashboardMonthlyData>();
            for (int i = 5; i >= 0; i--)
            {
                var monthStart = new DateTime(today.Year, today.Month, 1).AddMonths(-i);
                var monthRows = monthlyTotals
                    .Where(x => x.Year == monthStart.Year && x.Month == monthStart.Month)
                    .ToList();

                var sales = monthRows.Where(x => x.VoucherType == VoucherType.Sale).Sum(x => x.Amount);
                var purchases = monthRows.Where(x => x.VoucherType == VoucherType.Purchase).Sum(x => x.Amount);
                var expenses = monthRows.Where(x => x.VoucherType == VoucherType.Expense || x.VoucherType == VoucherType.Hazri).Sum(x => x.Amount);

                monthlyData.Add(new DashboardMonthlyData { Month = monthStart.ToString("MMM yyyy"), Sales = sales, Purchases = purchases, Expenses = expenses });
            }
            ViewBag.MonthlyData = monthlyData;

            // 7. Total Expenses (all time)
            decimal totalExpenses = await vouchers
                .Where(v => v.VoucherType == VoucherType.Expense || v.VoucherType == VoucherType.Hazri)
                .SumAsync(v => (decimal?)v.Amount) ?? 0m;
            ViewBag.TotalExpenses = totalExpenses;

            // 8. Total Capital
            // Stock + Receivables + Cash + Daily Cash + Bank + Advanced - Payables
            // (Expenses are intentionally excluded from the capital calculation.)
            ViewBag.TotalCapital = totalStockValue + totalReceivables + cashInHand + dailyCashBalance + totalBankBalance + totalAdvancedBalance - totalPayables;

            // 9. Voucher Type Distribution (Last 30 days) — grouped in the database.
            var voucherTypeRows = await vouchers
                .Where(v => v.VoucherDate >= last30Days)
                .GroupBy(v => v.VoucherType)
                .Select(g => new { Type = g.Key, Count = g.Count(), Amount = g.Sum(v => v.Amount) })
                .ToListAsync();

            var voucherTypeData = voucherTypeRows
                .Select(x => new DashboardVoucherType { Type = x.Type.ToString(), Count = x.Count, Amount = x.Amount })
                .ToList();
            ViewBag.VoucherTypeData = voucherTypeData;

            return View();
        }

        [HttpGet]
        public IActionResult Login()
        {
            // If already logged in, redirect to home
            if (HttpContext.Session.GetString("IsLoggedIn") == "true")
            {
                return RedirectToAction("Index");
            }
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> DoLogin(string username, string password)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == username && u.Password == password && u.IsActive);

            if (user != null)
            {
                // Update last login date
                user.LastLoginDate = DateTimeHelper.PkNow;
                await _context.SaveChangesAsync();

                // Set session variables
                HttpContext.Session.SetString("IsLoggedIn", "true");
                HttpContext.Session.SetString("Username", user.Username);
                HttpContext.Session.SetString("UserId", user.Id.ToString());
                HttpContext.Session.SetString("UserRole", user.Role);
                HttpContext.Session.SetString("FullName", user.FullName);

                return Json(new { success = true });
            }

            return Json(new { success = false, message = "Invalid username or password" });
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }

    public class ErrorViewModel
    {
        public string? RequestId { get; set; }
        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }

    // Dashboard helper classes
    public class DashboardStockItem
    {
        public string Name { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal Value { get; set; }
    }

    public class DashboardNameAmount
    {
        public string Name { get; set; } = "";
        public decimal Amount { get; set; }
    }

    public class DashboardBankBalance
    {
        public string Name { get; set; } = "";
        public decimal Balance { get; set; }
    }

    public class DashboardMonthlyData
    {
        public string Month { get; set; } = "";
        public decimal Sales { get; set; }
        public decimal Purchases { get; set; }
        public decimal Expenses { get; set; }
    }

    public class DashboardVoucherType
    {
        public string Type { get; set; } = "";
        public int Count { get; set; }
        public decimal Amount { get; set; }
    }

    public class DashboardAdvancedCustomer
    {
        public string Name { get; set; } = "";
        public decimal Balance { get; set; }
    }

    // A customer with no voucher activity for at least the dashboard's shortest window.
    // DaysInactive is int.MaxValue when the customer has never had a voucher.
    public class DashboardInactiveCustomer
    {
        public string Name { get; set; } = "";
        public decimal Balance { get; set; }
        public DateTime? LastTransactionDate { get; set; }
        public int DaysInactive { get; set; }
        public bool HasEverTransacted => LastTransactionDate.HasValue;
    }

}
