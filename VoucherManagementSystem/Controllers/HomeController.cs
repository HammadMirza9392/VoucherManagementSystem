using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Data;
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

        public async Task<IActionResult> Index()
        {
            var today = DateTime.Today;
            var date = DateTime.Today.AddDays(1);
            var last30Days = today.AddDays(-30);
            var sixMonthsStart = new DateTime(today.Year, today.Month, 1).AddMonths(-5);

            // === Load the data sets we need ONCE, then compute everything in memory. ===
            // This avoids the previous N+1 query pattern (one DB round-trip per customer/item/bank)
            // and the many separate full-table voucher scans that made the dashboard slow.

            // All vouchers, lightweight (no navigation includes) - reused for every aggregation below.
            var allVouchers = await _context.Vouchers
                .AsNoTracking()
                .Select(v => new DashboardVoucherRow
                {
                    VoucherType = v.VoucherType,
                    CashType = v.CashType,
                    Amount = v.Amount,
                    Quantity = v.Quantity,
                    StockInclude = v.StockInclude,
                    VoucherDate = v.VoucherDate,
                    ItemId = v.ItemId,
                    ExpenseHeadId = v.ExpenseHeadId,
                    PurchasingCustomerId = v.PurchasingCustomerId,
                    ReceivingCustomerId = v.ReceivingCustomerId,
                    AdvancedPurchasingCustomerId = v.AdvancedPurchasingCustomerId,
                    AdvancedReceivingCustomerId = v.AdvancedReceivingCustomerId,
                    BankCustomerPaidId = v.BankCustomerPaidId,
                    BankCustomerReceiverId = v.BankCustomerReceiverId
                })
                .ToListAsync();

            var items = await _itemRepository.GetActiveItemsAsync();
            var customers = await _customerRepository.GetActiveCustomersAsync();
            var banks = await _bankRepository.GetActiveBanksAsync();
            var expenseHeads = await _expenseHeadRepository.GetAllAsync();
            var expenseHeadNames = expenseHeads.ToDictionary(e => e.Id, e => e.Name);

            // Basic counts (cheap aggregate queries / in-memory counts)
            ViewBag.TotalVouchers = allVouchers.Count;
            ViewBag.ActiveProjects = (await _projectRepository.GetActiveProjectsAsync()).Count();
            ViewBag.TotalCustomers = customers.Count();
            ViewBag.TotalItems = items.Count();

            // Today's transactions
            var todayVouchers = allVouchers.Where(v => v.VoucherDate >= today && v.VoucherDate < date).ToList();
            ViewBag.TodayTransactions = todayVouchers.Count;
            ViewBag.TodayAmount = todayVouchers.Sum(v => v.Amount);

            // Recent vouchers (needs navigation properties, so query with details and take 10)
            var recentVouchers = await _voucherRepository.GetVouchersWithDetailsAsync();
            ViewBag.RecentVouchers = recentVouchers.Take(10);

            // === CAPITAL REPORT DATA ===

            // 1. Stock Value - average rate per item computed from purchase vouchers (grouped once)
            decimal totalStockValue = 0;
            var stockData = new List<DashboardStockItem>();

            var stockPurchaseByItem = allVouchers
                .Where(v => v.ItemId.HasValue && v.VoucherType == VoucherType.Purchase && v.StockInclude)
                .GroupBy(v => v.ItemId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => new { Amount = g.Sum(p => p.Amount), Qty = g.Sum(p => p.Quantity ?? 0) });

            foreach (var item in items)
            {
                decimal currentQty = item.CurrentStock;
                if (currentQty > 0)
                {
                    decimal avgRate = item.DefaultRate;
                    if (stockPurchaseByItem.TryGetValue(item.Id, out var p) && p.Qty > 0)
                    {
                        avgRate = p.Amount / p.Qty;
                    }
                    decimal stockValue = currentQty * avgRate;
                    totalStockValue += stockValue;
                    stockData.Add(new DashboardStockItem { Name = item.Name, Quantity = currentQty, Value = stockValue });
                }
            }
            ViewBag.TotalStockValue = totalStockValue;
            ViewBag.StockData = stockData.ToList();

            // 2. Customer Receivables & Payables - computed in memory from the single voucher load
            decimal totalReceivables = 0;
            decimal totalPayables = 0;
            var receivablesData = new List<DashboardNameAmount>();
            var payablesData = new List<DashboardNameAmount>();

            // Net per customer: + for receivable side, - for payable side
            var receiveByCustomer = new Dictionary<int, decimal>();
            var payByCustomer = new Dictionary<int, decimal>();

            foreach (var v in allVouchers.Where(v => v.VoucherDate < date))
            {
                if (v.ReceivingCustomerId.HasValue)
                {
                    if (v.VoucherType == VoucherType.Sale)
                        receiveByCustomer[v.ReceivingCustomerId.Value] = receiveByCustomer.GetValueOrDefault(v.ReceivingCustomerId.Value) + v.Amount;
                    else if (v.VoucherType == VoucherType.CashReceived)
                        receiveByCustomer[v.ReceivingCustomerId.Value] = receiveByCustomer.GetValueOrDefault(v.ReceivingCustomerId.Value) - v.Amount;
                }
                if (v.PurchasingCustomerId.HasValue)
                {
                    if (v.VoucherType == VoucherType.Purchase)
                        payByCustomer[v.PurchasingCustomerId.Value] = payByCustomer.GetValueOrDefault(v.PurchasingCustomerId.Value) + v.Amount;
                    else if (v.VoucherType == VoucherType.CashPaid)
                        payByCustomer[v.PurchasingCustomerId.Value] = payByCustomer.GetValueOrDefault(v.PurchasingCustomerId.Value) - v.Amount;
                }
            }

            foreach (var customer in customers)
            {
                decimal netBalance = receiveByCustomer.GetValueOrDefault(customer.Id) - payByCustomer.GetValueOrDefault(customer.Id);

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

            // 3. Cash in Hand
            decimal cashInHand = 0;
            foreach (var v in allVouchers.Where(v => v.CashType == CashType.Cash && v.VoucherDate < date))
            {
                switch (v.VoucherType)
                {
                    case VoucherType.Sale:
                    case VoucherType.CashReceived:
                        cashInHand += v.Amount;
                        break;
                    case VoucherType.Purchase:
                    case VoucherType.Expense:
                    case VoucherType.CashPaid:
                    case VoucherType.Hazri:
                        cashInHand -= v.Amount;
                        break;
                }
            }

            // Include CashAdjustments
            try
            {
                var cashAdjustments = await _context.CashAdjustments.Where(c => c.AdjustmentDate < date).ToListAsync();
                foreach (var adj in cashAdjustments)
                {
                    if (adj.AdjustmentType == CashAdjustmentType.CashIn) cashInHand += adj.Amount;
                    else if (adj.AdjustmentType == CashAdjustmentType.CashOut) cashInHand -= adj.Amount;
                }
            }
            catch { /* CashAdjustments table may not exist */ }

            ViewBag.CashInHand = cashInHand;

            // 4. Bank Balances - net movement per bank computed once
            decimal totalBankBalance = 0;
            var bankData = new List<DashboardBankBalance>();

            var bankPaidById = allVouchers
                .Where(v => v.BankCustomerPaidId.HasValue && v.VoucherDate < date)
                .GroupBy(v => v.BankCustomerPaidId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(v => v.Amount));
            var bankReceivedById = allVouchers
                .Where(v => v.BankCustomerReceiverId.HasValue && v.VoucherDate < date)
                .GroupBy(v => v.BankCustomerReceiverId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(v => v.Amount));

            foreach (var bank in banks)
            {
                decimal balance = bank.Balance
                    - bankPaidById.GetValueOrDefault(bank.Id)
                    + bankReceivedById.GetValueOrDefault(bank.Id);

                totalBankBalance += balance;
                bankData.Add(new DashboardBankBalance { Name = bank.Name, Balance = balance });
            }
            ViewBag.TotalBankBalance = totalBankBalance;
            ViewBag.BankData = bankData;

            // 5. Advanced Customers Balances
            var advancedCustomerData = new List<DashboardAdvancedCustomer>();
            decimal totalAdvancedBalance = 0;

            var allAdvVouchers = allVouchers
                .Where(v => v.VoucherType == VoucherType.AdvancedCashPaid ||
                            v.VoucherType == VoucherType.AdvancedCashReceived ||
                            v.VoucherType == VoucherType.AdvancedPayment)
                .ToList();

            // Get all unique customer IDs involved in advanced vouchers
            var advCustomerIds = allAdvVouchers
                .SelectMany(v => new[] {
                    v.AdvancedPurchasingCustomerId,
                    v.AdvancedReceivingCustomerId,
                    v.ReceivingCustomerId
                })
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToHashSet();

            // Reuse the already-loaded active customers (filtered to those involved in advanced vouchers)
            var advCustomers = customers.Where(c => advCustomerIds.Contains(c.Id));

            foreach (var cust in advCustomers)
            {
                decimal bal = 0;
                foreach (var v in allAdvVouchers)
                {
                    if ((v.VoucherType == VoucherType.AdvancedCashReceived && v.AdvancedReceivingCustomerId == cust.Id) ||
                        (v.VoucherType == VoucherType.AdvancedPayment && v.ReceivingCustomerId == cust.Id))
                        bal -= v.Amount;
                    else if (v.VoucherType == VoucherType.AdvancedCashPaid && v.AdvancedPurchasingCustomerId == cust.Id)
                        bal += v.Amount;
                }
                if (bal != 0)
                {
                    advancedCustomerData.Add(new DashboardAdvancedCustomer { Name = cust.Name, Balance = bal });
                    totalAdvancedBalance += bal;
                }
            }
            ViewBag.AdvancedCustomerData = advancedCustomerData.OrderByDescending(x => Math.Abs(x.Balance)).ToList();
            ViewBag.TotalAdvancedBalance = totalAdvancedBalance;

            // 6. Expense Summary (Last 30 days) - grouped in memory by expense head name
            var expenseVouchers = allVouchers
                .Where(v => (v.VoucherType == VoucherType.Expense || v.VoucherType == VoucherType.Hazri) && v.VoucherDate >= last30Days)
                .ToList();

            var expenseData = expenseVouchers
                .GroupBy(v => v.ExpenseHeadId.HasValue && expenseHeadNames.ContainsKey(v.ExpenseHeadId.Value)
                    ? expenseHeadNames[v.ExpenseHeadId.Value]
                    : "Other")
                .Select(g => new DashboardNameAmount { Name = g.Key, Amount = g.Sum(v => v.Amount) })
                .OrderByDescending(x => x.Amount)
                .Take(10)
                .ToList();
            ViewBag.ExpenseData = expenseData;
            ViewBag.TotalExpenses30Days = expenseVouchers.Sum(v => v.Amount);

            // 6. Monthly Trends (Last 6 months) - bucketed in memory
            var monthlyData = new List<DashboardMonthlyData>();
            for (int i = 5; i >= 0; i--)
            {
                var monthStart = new DateTime(today.Year, today.Month, 1).AddMonths(-i);
                var monthEnd = monthStart.AddMonths(1);
                var monthVouchers = allVouchers.Where(v => v.VoucherDate >= monthStart && v.VoucherDate < monthEnd).ToList();

                var sales = monthVouchers.Where(v => v.VoucherType == VoucherType.Sale).Sum(v => v.Amount);
                var purchases = monthVouchers.Where(v => v.VoucherType == VoucherType.Purchase).Sum(v => v.Amount);
                var expenses = monthVouchers.Where(v => v.VoucherType == VoucherType.Expense || v.VoucherType == VoucherType.Hazri).Sum(v => v.Amount);

                monthlyData.Add(new DashboardMonthlyData { Month = monthStart.ToString("MMM yyyy"), Sales = sales, Purchases = purchases, Expenses = expenses });
            }
            ViewBag.MonthlyData = monthlyData;

            // 7. Total Expenses (all time)
            decimal totalExpenses = allVouchers
                .Where(v => v.VoucherType == VoucherType.Expense || v.VoucherType == VoucherType.Hazri)
                .Sum(v => v.Amount);
            ViewBag.TotalExpenses = totalExpenses;

            // 8. Total Capital
            ViewBag.TotalCapital = totalStockValue + totalReceivables + cashInHand + totalBankBalance + totalAdvancedBalance - totalPayables - totalExpenses;

            // 9. Voucher Type Distribution (Last 30 days)
            var voucherTypeData = allVouchers
                .Where(v => v.VoucherDate >= last30Days)
                .GroupBy(v => v.VoucherType)
                .Select(g => new DashboardVoucherType { Type = g.Key.ToString(), Count = g.Count(), Amount = g.Sum(v => v.Amount) })
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
                user.LastLoginDate = DateTime.Now;
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

    // Lightweight voucher projection used by the dashboard so all aggregations can run
    // off a single in-memory load instead of many per-entity database queries.
    public class DashboardVoucherRow
    {
        public VoucherType VoucherType { get; set; }
        public CashType? CashType { get; set; }
        public decimal Amount { get; set; }
        public decimal? Quantity { get; set; }
        public bool StockInclude { get; set; }
        public DateTime VoucherDate { get; set; }
        public int? ItemId { get; set; }
        public int? ExpenseHeadId { get; set; }
        public int? PurchasingCustomerId { get; set; }
        public int? ReceivingCustomerId { get; set; }
        public int? AdvancedPurchasingCustomerId { get; set; }
        public int? AdvancedReceivingCustomerId { get; set; }
        public int? BankCustomerPaidId { get; set; }
        public int? BankCustomerReceiverId { get; set; }
    }
}
