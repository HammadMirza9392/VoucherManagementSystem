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

            // Basic counts
            ViewBag.TotalVouchers = (await _voucherRepository.GetAllAsync()).Count();
            ViewBag.ActiveProjects = (await _projectRepository.GetActiveProjectsAsync()).Count();
            ViewBag.TotalCustomers = (await _customerRepository.GetActiveCustomersAsync()).Count();
            ViewBag.TotalItems = (await _itemRepository.GetActiveItemsAsync()).Count();

            // Today's transactions
            var todayVouchers = await _voucherRepository.GetVouchersByDateRangeAsync(today, date);
            ViewBag.TodayTransactions = todayVouchers.Count();
            ViewBag.TodayAmount = todayVouchers.Sum(v => v.Amount);

            // Recent vouchers
            var recentVouchers = await _voucherRepository.GetVouchersWithDetailsAsync();
            ViewBag.RecentVouchers = recentVouchers.Take(10);

            // === CAPITAL REPORT DATA ===

            // 1. Stock Value
            var items = await _itemRepository.GetActiveItemsAsync();
            decimal totalStockValue = 0;
            var stockData = new List<DashboardStockItem>();

            foreach (var item in items)
            {
                decimal currentQty = item.CurrentStock;

                if (currentQty > 0)
                {
                    // Get all purchases for this item to calculate average rate
                    var stockPurchases = await _context.Vouchers
                        .Where(v => v.ItemId == item.Id && v.VoucherType == VoucherType.Purchase && v.StockInclude == true)
                        .ToListAsync();

                    decimal totalPurchaseAmount = stockPurchases.Sum(p => p.Amount);
                    decimal totalPurchaseQty = stockPurchases.Sum(p => p.Quantity ?? 0);
                    decimal avgRate = totalPurchaseQty > 0 ? totalPurchaseAmount / totalPurchaseQty : item.DefaultRate;
                    decimal stockValue = currentQty * avgRate;
                    totalStockValue += stockValue;
                    stockData.Add(new DashboardStockItem { Name = item.Name, Quantity = currentQty, Value = stockValue });
                }
            }
            ViewBag.TotalStockValue = totalStockValue;
            ViewBag.StockData = stockData.ToList();

            // 2. Customer Receivables & Payables
            var customers = await _customerRepository.GetActiveCustomersAsync();
            decimal totalReceivables = 0;
            decimal totalPayables = 0;
            var receivablesData = new List<DashboardNameAmount>();
            var payablesData = new List<DashboardNameAmount>();

            foreach (var customer in customers)
            {
                var vouchers = await _context.Vouchers
                    .Where(v => (v.PurchasingCustomerId == customer.Id || v.ReceivingCustomerId == customer.Id) && v.VoucherDate < date)
                    .ToListAsync();

                decimal toReceive = 0;
                decimal toPay = 0;

                foreach (var v in vouchers)
                {
                    if (v.ReceivingCustomerId == customer.Id)
                    {
                        if (v.VoucherType == VoucherType.Sale) toReceive += v.Amount;
                        else if (v.VoucherType == VoucherType.CashReceived) toReceive -= v.Amount;
                    }
                    if (v.PurchasingCustomerId == customer.Id)
                    {
                        if (v.VoucherType == VoucherType.Purchase) toPay += v.Amount;
                        else if (v.VoucherType == VoucherType.CashPaid) toPay -= v.Amount;
                    }
                }

                // Calculate net ending balance
                decimal netBalance = toReceive - toPay;

                // Only show in one section based on net balance
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
            var cashVouchers = await _context.Vouchers.Where(v => v.CashType == CashType.Cash && v.VoucherDate < date).ToListAsync();
            foreach (var v in cashVouchers)
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

            // 4. Bank Balances
            var banks = await _bankRepository.GetActiveBanksAsync();
            decimal totalBankBalance = 0;
            var bankData = new List<DashboardBankBalance>();

            foreach (var bank in banks)
            {
                decimal balance = bank.Balance;
                var bankVouchers = await _context.Vouchers
                    .Where(v => (v.BankCustomerPaidId == bank.Id || v.BankCustomerReceiverId == bank.Id) && v.VoucherDate < date)
                    .ToListAsync();

                foreach (var v in bankVouchers)
                {
                    if (v.BankCustomerPaidId == bank.Id) balance -= v.Amount;
                    if (v.BankCustomerReceiverId == bank.Id) balance += v.Amount;
                }

                totalBankBalance += balance;
                bankData.Add(new DashboardBankBalance { Name = bank.Name, Balance = balance });
            }
            ViewBag.TotalBankBalance = totalBankBalance;
            ViewBag.BankData = bankData;

            // 5. Expense Summary (Last 30 days)
            var last30Days = today.AddDays(-30);
            var expenseVouchers = await _context.Vouchers
                .Include(v => v.ExpenseHead)
                .Where(v => (v.VoucherType == VoucherType.Expense || v.VoucherType == VoucherType.Hazri) && v.VoucherDate >= last30Days)
                .ToListAsync();

            var expenseData = expenseVouchers
                .GroupBy(v => v.ExpenseHead?.Name ?? "Other")
                .Select(g => new DashboardNameAmount { Name = g.Key, Amount = g.Sum(v => v.Amount) })
                .OrderByDescending(x => x.Amount)
                .Take(10)
                .ToList();
            ViewBag.ExpenseData = expenseData;
            ViewBag.TotalExpenses30Days = expenseVouchers.Sum(v => v.Amount);

            // 6. Monthly Trends (Last 6 months)
            var monthlyData = new List<DashboardMonthlyData>();
            for (int i = 5; i >= 0; i--)
            {
                var monthStart = new DateTime(today.Year, today.Month, 1).AddMonths(-i);
                var monthEnd = monthStart.AddMonths(1);
                var monthVouchers = await _context.Vouchers.Where(v => v.VoucherDate >= monthStart && v.VoucherDate < monthEnd).ToListAsync();

                var sales = monthVouchers.Where(v => v.VoucherType == VoucherType.Sale).Sum(v => v.Amount);
                var purchases = monthVouchers.Where(v => v.VoucherType == VoucherType.Purchase).Sum(v => v.Amount);
                var expenses = monthVouchers.Where(v => v.VoucherType == VoucherType.Expense || v.VoucherType == VoucherType.Hazri).Sum(v => v.Amount);

                monthlyData.Add(new DashboardMonthlyData { Month = monthStart.ToString("MMM yyyy"), Sales = sales, Purchases = purchases, Expenses = expenses });
            }
            ViewBag.MonthlyData = monthlyData;

            // 7. Total Expenses (all time)
            var allExpenseVouchers = await _context.Vouchers
                .Where(v => v.VoucherType == VoucherType.Expense || v.VoucherType == VoucherType.Hazri)
                .ToListAsync();
            decimal totalExpenses = allExpenseVouchers.Sum(v => v.Amount);
            ViewBag.TotalExpenses = totalExpenses;

            // 8. Total Capital
            ViewBag.TotalCapital = totalStockValue + totalReceivables + cashInHand + totalBankBalance - totalPayables - totalExpenses;

            // 9. Voucher Type Distribution (Last 30 days)
            var voucherTypeData = todayVouchers
                .Concat(await _context.Vouchers.Where(v => v.VoucherDate >= last30Days).ToListAsync())
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
}
