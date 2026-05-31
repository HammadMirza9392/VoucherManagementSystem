using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Data;
using VoucherManagementSystem.Interfaces;
using VoucherManagementSystem.Models;
using ClosedXML.Excel;

namespace VoucherManagementSystem.Controllers
{
    public class ReportsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IVoucherRepository _voucherRepository;
        private readonly IProjectRepository _projectRepository;
        private readonly IBankRepository _bankRepository;
        private readonly IItemRepository _itemRepository;
        private readonly IExpenseHeadRepository _expenseHeadRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly ILogger<ReportsController> _logger;
        private readonly IWebHostEnvironment _environment;

        public ReportsController(
            ApplicationDbContext context,
            IVoucherRepository voucherRepository,
            IProjectRepository projectRepository,
            IBankRepository bankRepository,
            IItemRepository itemRepository,
            IExpenseHeadRepository expenseHeadRepository,
            ICustomerRepository customerRepository,
            ILogger<ReportsController> logger,
            IWebHostEnvironment environment)
        {
            _context = context;
            _voucherRepository = voucherRepository;
            _projectRepository = projectRepository;
            _bankRepository = bankRepository;
            _itemRepository = itemRepository;
            _expenseHeadRepository = expenseHeadRepository;
            _customerRepository = customerRepository;
            _logger = logger;
            _environment = environment;
        }

        // GET: Reports
        public async Task<IActionResult> Index()
        {
            try
            {
                ViewBag.Projects = new SelectList(await _projectRepository.GetActiveProjectsAsync(), "Id", "Name");
                ViewBag.Banks = new SelectList(await _bankRepository.GetActiveBanksAsync(), "Id", "Name");
                ViewBag.Items = new SelectList(await _itemRepository.GetActiveItemsAsync(), "Id", "Name");
                ViewBag.ExpenseHeads = new SelectList(await _expenseHeadRepository.GetActiveExpenseHeadsAsync(), "Id", "Name");

                // Statistics for dashboard
                ViewBag.TotalVouchers = (await _voucherRepository.GetAllAsync()).Count();
                ViewBag.ActiveProjects = (await _projectRepository.GetActiveProjectsAsync()).Count();
                ViewBag.TotalCustomers = (await _customerRepository.GetActiveCustomersAsync()).Count();
                ViewBag.TotalItems = (await _itemRepository.GetActiveItemsAsync()).Count();

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading reports index");
                TempData["Error"] = "Error loading reports. Please try again.";
                return View();
            }
        }

        // GET: Reports/ProjectReport - Project details report
        public async Task<IActionResult> ProjectReport(int projectId, DateTime? fromDate, DateTime? toDate, string? voucherType, int? itemId, int? customerId, string? gariNo)
        {
            try
            {
                var project = await _projectRepository.GetByIdAsync(projectId);
                if (project == null)
                {
                    TempData["Error"] = "Project not found.";
                    return RedirectToAction(nameof(Index));
                }

                var startDate = fromDate ?? new DateTime(DateTime.Today.Year, 1, 1);
                var endDate = toDate ?? DateTime.Today;

                // Get base vouchers with all related data
                var query = _context.Vouchers
                    .Include(v => v.PurchasingCustomer)
                    .Include(v => v.ReceivingCustomer)
                    .Include(v => v.Item)
                    .Include(v => v.ExpenseHead)
                    .Include(v => v.Project)
                    .Where(v => v.ProjectId == projectId &&
                               v.VoucherDate >= startDate &&
                               v.VoucherDate <= endDate)
                    .AsQueryable();

                // Apply voucher type filter if selected
                if (!string.IsNullOrEmpty(voucherType) && Enum.TryParse<VoucherType>(voucherType, out var vType))
                {
                    query = query.Where(v => v.VoucherType == vType);
                }

                // Apply item filter if selected
                if (itemId.HasValue && itemId.Value > 0)
                {
                    query = query.Where(v => v.ItemId == itemId.Value);
                }

                // Apply customer filter if selected (check both purchasing and receiving customer)
                if (customerId.HasValue && customerId.Value > 0)
                {
                    query = query.Where(v => v.PurchasingCustomerId == customerId.Value ||
                                            v.ReceivingCustomerId == customerId.Value);
                }

                // Apply gari no filter if provided
                if (!string.IsNullOrWhiteSpace(gariNo))
                {
                    query = query.Where(v => v.GariNo != null && v.GariNo.Contains(gariNo));
                }

                var vouchers = await query.OrderBy(v => v.VoucherDate).ToListAsync();

                // Get item-wise purchase and sale summary with filters
                var itemSummary = await GetProjectItemSummaryAsync(projectId, startDate, endDate, voucherType, itemId, customerId);

                // Calculate separate values
                var totalSale = vouchers.Where(v => v.VoucherType == VoucherType.Sale || v.VoucherType == VoucherType.CashReceived).Sum(v => v.Amount);
                var totalStock = itemSummary.Sum(i => i.StockValue); // Total stock value from items
                var totalRevenue = totalSale + totalStock; // Revenue = Sale + Stock

                // Separate Purchase and Expense
                var totalPurchase = vouchers.Where(v => v.VoucherType == VoucherType.Purchase).Sum(v => v.Amount);
                var totalExpense = vouchers.Where(v => v.VoucherType == VoucherType.Expense ||
                                                       v.VoucherType == VoucherType.CashPaid ||
                                                       v.VoucherType == VoucherType.Hazri).Sum(v => v.Amount);
                var totalExpenses = totalPurchase + totalExpense; // Total Expenses = Purchase + Expense

                var profitLoss = totalRevenue - totalExpenses; // Net Profit/Loss = Total Revenue - Total Expenses

                // Populate dropdowns for filters
                ViewBag.Items = new SelectList(await _itemRepository.GetActiveItemsAsync(), "Id", "Name", itemId);
                ViewBag.Customers = new SelectList(await _customerRepository.GetActiveCustomersAsync(), "Id", "Name", customerId);

                ViewBag.Project = project;
                ViewBag.FromDate = startDate;
                ViewBag.ToDate = endDate;
                ViewBag.TotalSale = totalSale;
                ViewBag.TotalStock = totalStock;
                ViewBag.Revenue = totalRevenue; // Total Revenue (Sale + Stock)
                ViewBag.TotalPurchase = totalPurchase;
                ViewBag.TotalExpense = totalExpense;
                ViewBag.Expenses = totalExpenses; // Total Expenses (Purchase + Expense)
                ViewBag.ProfitLoss = profitLoss;
                ViewBag.Vouchers = vouchers;
                ViewBag.ItemSummary = itemSummary;
                ViewBag.SelectedVoucherType = voucherType;
                ViewBag.SelectedItemId = itemId;
                ViewBag.SelectedCustomerId = customerId;
                ViewBag.SelectedGariNo = gariNo;

                return View("ProfitLoss");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating project report");
                TempData["Error"] = "Error generating report. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Reports/ProfitLoss
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProfitLoss(int projectId, DateTime fromDate, DateTime toDate, string? voucherType, int? itemId, int? customerId, string? gariNo)
        {
            try
            {
                var project = await _projectRepository.GetByIdAsync(projectId);
                if (project == null)
                {
                    TempData["Error"] = "Project not found.";
                    return RedirectToAction(nameof(Index));
                }

                // Redirect to GET with query parameters to enable filtering
                return RedirectToAction(nameof(ProjectReport), new
                {
                    projectId = projectId,
                    fromDate = fromDate,
                    toDate = toDate,
                    voucherType = voucherType,
                    itemId = itemId,
                    customerId = customerId,
                    gariNo = gariNo
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating profit/loss report");
                TempData["Error"] = "Error generating report. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Reports/StockReport
        //public async Task<IActionResult> StockReport()
        //{
        //    try
        //    {
        //        var items = await _context.Items
        //            .Where(i => i.StockTrackingEnabled && i.IsActive)
        //            .OrderBy(i => i.Name)
        //            .ToListAsync();

        //        return View(items);
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error generating stock report");
        //        TempData["Error"] = "Error generating stock report.";
        //        return RedirectToAction(nameof(Index));
        //    }
        //}

        // GET: Reports/BankStatement
        public async Task<IActionResult> BankStatement(int bankId, DateTime fromDate, DateTime toDate)
        {
            try
            {
                var bank = await _bankRepository.GetByIdAsync(bankId);
                if (bank == null)
                {
                    TempData["Error"] = "Bank not found.";
                    return RedirectToAction(nameof(Index));
                }

                // Get opening balance
                var openingBalance = await GetBankOpeningBalanceAsync(bankId, fromDate);

                var transactions = await _bankRepository.GetBankTransactionsAsync(bankId, fromDate, toDate);

                ViewBag.Bank = bank;
                ViewBag.FromDate = fromDate;
                ViewBag.ToDate = toDate;
                ViewBag.OpeningBalance = openingBalance;
                ViewBag.Transactions = transactions;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating bank statement");
                TempData["Error"] = "Error generating bank statement.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Reports/CashStatement - Tracks cash from vouchers (CashType = Cash)
        public async Task<IActionResult> CashStatement(DateTime? fromDate, DateTime? toDate, int? customerId, string? voucherType)
        {
            try
            {
                var endDate = toDate ?? DateTime.Today;
                var startDate = fromDate ?? DateTime.Today.AddMonths(-1);

                // Get customers for filter dropdown
                ViewBag.Customers = new SelectList(await _customerRepository.GetActiveCustomersAsync(), "Id", "Name", customerId);

                // Voucher types for filter
                var voucherTypes = new List<SelectListItem>
                {
                    new SelectListItem { Value = "", Text = "-- All Types --" },
                    new SelectListItem { Value = "Sale", Text = "Sale" },
                    new SelectListItem { Value = "Purchase", Text = "Purchase" },
                    new SelectListItem { Value = "CashReceived", Text = "Cash Received" },
                    new SelectListItem { Value = "CashPaid", Text = "Cash Paid" },
                    new SelectListItem { Value = "Expense", Text = "Expense" },
                    new SelectListItem { Value = "Hazri", Text = "Hazri" }
                };
                ViewBag.VoucherTypes = new SelectList(voucherTypes, "Value", "Text", voucherType);
                ViewBag.SelectedVoucherType = voucherType;

                // Build query for cash vouchers
                var query = _context.Vouchers
                    .Include(v => v.PurchasingCustomer)
                    .Include(v => v.ReceivingCustomer)
                    .Include(v => v.Item)
                    .Include(v => v.ExpenseHead)
                    .Where(v => v.CashType == CashType.Cash &&
                               v.VoucherDate >= startDate && v.VoucherDate <= endDate.AddDays(1));

                // Apply customer filter if selected
                if (customerId.HasValue)
                {
                    query = query.Where(v => v.PurchasingCustomerId == customerId || v.ReceivingCustomerId == customerId);
                    ViewBag.SelectedCustomerId = customerId;
                    ViewBag.SelectedCustomer = await _customerRepository.GetByIdAsync(customerId.Value);
                }

                // Apply voucher type filter if selected
                if (!string.IsNullOrEmpty(voucherType) && Enum.TryParse<VoucherType>(voucherType, out var vType))
                {
                    query = query.Where(v => v.VoucherType == vType);
                }

                var vouchers = await query.OrderBy(v => v.VoucherDate).ThenBy(v => v.Id).ToListAsync();

                // Get cash adjustments for the period (handle if table doesn't exist yet)
                var cashAdjustments = new List<CashAdjustment>();
                try
                {
                    cashAdjustments = await _context.CashAdjustments
                        .Where(a => a.AdjustmentDate >= startDate && a.AdjustmentDate <= endDate.AddDays(1))
                        .OrderBy(a => a.AdjustmentDate)
                        .ToListAsync();
                }
                catch
                {
                    // Table doesn't exist yet - will be created after migration
                }

                // Calculate opening balance (all cash transactions before start date)
                var openingBalance = await GetCashOpeningBalanceAsync(startDate, customerId);

                // Calculate totals from vouchers
                decimal totalReceipts = 0;
                decimal totalPayments = 0;

                foreach (var v in vouchers)
                {
                    switch (v.VoucherType)
                    {
                        case VoucherType.Sale:
                        case VoucherType.CashReceived:
                            totalReceipts += v.Amount;
                            break;
                        case VoucherType.Purchase:
                        case VoucherType.Expense:
                        case VoucherType.CashPaid:
                        case VoucherType.Hazri:
                            totalPayments += v.Amount;
                            break;
                    }
                }

                // Add cash adjustments to totals
                foreach (var adj in cashAdjustments)
                {
                    if (adj.AdjustmentType == CashAdjustmentType.CashIn)
                        totalReceipts += adj.Amount;
                    else
                        totalPayments += adj.Amount;
                }

                ViewBag.FromDate = startDate;
                ViewBag.ToDate = endDate;
                ViewBag.OpeningBalance = openingBalance;
                ViewBag.TotalReceipts = totalReceipts;
                ViewBag.TotalPayments = totalPayments;
                ViewBag.ClosingBalance = openingBalance + totalReceipts - totalPayments;
                ViewBag.Vouchers = vouchers;
                ViewBag.CashAdjustments = cashAdjustments;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating cash statement");
                TempData["Error"] = "Error generating cash statement.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Reports/AddCashAdjustment
        public IActionResult AddCashAdjustment(string type = "CashIn")
        {
            ViewBag.AdjustmentType = type == "CashOut" ? CashAdjustmentType.CashOut : CashAdjustmentType.CashIn;
            return View(new CashAdjustment { AdjustmentType = type == "CashOut" ? CashAdjustmentType.CashOut : CashAdjustmentType.CashIn });
        }

        // POST: Reports/AddCashAdjustment
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddCashAdjustment(CashAdjustment adjustment)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    adjustment.CreatedBy = HttpContext.Session.GetString("Username") ?? "Admin";
                    adjustment.CreatedDate = DateTime.Now;

                    // Generate reference number
                    int count = 1;
                    try { count = await _context.CashAdjustments.CountAsync() + 1; } catch { }
                    adjustment.ReferenceNumber = $"CASH-{(adjustment.AdjustmentType == CashAdjustmentType.CashIn ? "IN" : "OUT")}-{DateTime.Now:yyyyMMdd}-{count:D4}";

                    _context.CashAdjustments.Add(adjustment);
                    await _context.SaveChangesAsync();

                    TempData["Success"] = adjustment.AdjustmentType == CashAdjustmentType.CashIn
                        ? $"Cash In of Rs. {adjustment.Amount:N0} added successfully!"
                        : $"Cash Out of Rs. {adjustment.Amount:N0} recorded successfully!";

                    return RedirectToAction(nameof(CashStatement));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error adding cash adjustment");
                    TempData["Error"] = "Error: Please run migration first - dotnet ef migrations add AddCashAdjustment && dotnet ef database update";
                    return RedirectToAction(nameof(CashStatement));
                }
            }

            ViewBag.AdjustmentType = adjustment.AdjustmentType;
            return View(adjustment);
        }

        // GET: Reports/ActivityLog - Show system activity (entries added/edited/deleted) by date
        public async Task<IActionResult> ActivityLog(DateTime? activityDate)
        {
            try
            {
                var logDate = activityDate ?? DateTime.Today;
                var nextDay = logDate.AddDays(1);

                // Find all vouchers created on this date
                var createdVouchers = await _context.Vouchers
                    .Include(v => v.PurchasingCustomer)
                    .Include(v => v.ReceivingCustomer)
                    .Include(v => v.Item)
                    .Include(v => v.ExpenseHead)
                    .Include(v => v.Project)
                    .Where(v => v.CreatedDate >= logDate && v.CreatedDate < nextDay)
                    .OrderBy(v => v.CreatedDate)
                    .ToListAsync();

                // Find all vouchers updated on this date (that were NOT created today)
                var updatedVouchers = await _context.Vouchers
                    .Include(v => v.PurchasingCustomer)
                    .Include(v => v.ReceivingCustomer)
                    .Include(v => v.Item)
                    .Include(v => v.ExpenseHead)
                    .Include(v => v.Project)
                    .Where(v => v.UpdatedDate.HasValue && v.UpdatedDate >= logDate && v.UpdatedDate < nextDay)
                    .OrderBy(v => v.UpdatedDate)
                    .ToListAsync();

                ViewBag.ActivityDate = logDate;
                ViewBag.CreatedVouchers = createdVouchers;
                ViewBag.UpdatedVouchers = updatedVouchers;
                ViewBag.TotalCreated = createdVouchers.Count;
                ViewBag.TotalUpdated = updatedVouchers.Count;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating activity log");
                TempData["Error"] = "Error generating activity log.";
                return RedirectToAction(nameof(Index));
            }
        }

        // Helper method to get opening cash balance
        private async Task<decimal> GetCashOpeningBalanceAsync(DateTime date, int? customerId = null)
        {
            decimal balance = 0;

            // Get voucher transactions before date
            var voucherQuery = _context.Vouchers
                .Where(v => v.CashType == CashType.Cash && v.VoucherDate < date);

            if (customerId.HasValue)
            {
                voucherQuery = voucherQuery.Where(v => v.PurchasingCustomerId == customerId || v.ReceivingCustomerId == customerId);
            }

            var previousVouchers = await voucherQuery.ToListAsync();

            foreach (var v in previousVouchers)
            {
                switch (v.VoucherType)
                {
                    case VoucherType.Sale:
                    case VoucherType.CashReceived:
                        balance += v.Amount;
                        break;
                    case VoucherType.Purchase:
                    case VoucherType.Expense:
                    case VoucherType.CashPaid:
                    case VoucherType.Hazri:
                        balance -= v.Amount;
                        break;
                }
            }

            // Add cash adjustments before date (only if no customer filter)
            if (!customerId.HasValue)
            {
                try
                {
                    var adjustments = await _context.CashAdjustments
                        .Where(a => a.AdjustmentDate < date)
                        .ToListAsync();

                    foreach (var adj in adjustments)
                    {
                        if (adj.AdjustmentType == CashAdjustmentType.CashIn)
                            balance += adj.Amount;
                        else
                            balance -= adj.Amount;
                    }
                }
                catch
                {
                    // Table doesn't exist yet - will be created after migration
                }
            }

            return balance;
        }

        // GET: Reports/ExportToExcel
        public async Task<IActionResult> ExportToExcel(string reportType, int? id, DateTime? fromDate, DateTime? toDate)
        {
            try
            {
                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("Report");

                if (reportType == "vouchers")
                {
                    var vouchers = await _voucherRepository.GetVouchersWithDetailsAsync();
                    if (fromDate.HasValue && toDate.HasValue)
                    {
                        vouchers = vouchers.Where(v => v.VoucherDate >= fromDate.Value && v.VoucherDate <= toDate.Value);
                    }

                    // Headers
                    worksheet.Cell(1, 1).Value = "Transaction No";
                    worksheet.Cell(1, 2).Value = "Type";
                    worksheet.Cell(1, 3).Value = "Date";
                    worksheet.Cell(1, 4).Value = "Amount";
                    worksheet.Cell(1, 5).Value = "Customer";
                    worksheet.Cell(1, 6).Value = "Project";

                    // Data
                    int row = 2;
                    foreach (var voucher in vouchers)
                    {
                        worksheet.Cell(row, 1).Value = voucher.TransactionNumber;
                        worksheet.Cell(row, 2).Value = voucher.VoucherType.ToString();
                        worksheet.Cell(row, 3).Value = voucher.VoucherDate.ToString("yyyy-MM-dd");
                        worksheet.Cell(row, 4).Value = voucher.Amount;
                        worksheet.Cell(row, 5).Value = voucher.PurchasingCustomer?.Name ?? voucher.ReceivingCustomer?.Name ?? "";
                        worksheet.Cell(row, 6).Value = voucher.Project?.Name ?? "";
                        row++;
                    }

                    // Format as table
                    var range = worksheet.Range(1, 1, row - 1, 6);
                    range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    worksheet.Row(1).Style.Font.Bold = true;
                    worksheet.Row(1).Style.Fill.BackgroundColor = XLColor.LightGray;
                }
                else if (reportType == "stock")
                {
                    var items = await _itemRepository.GetItemsWithStockAsync();

                    // Headers
                    worksheet.Cell(1, 1).Value = "Item Name";
                    worksheet.Cell(1, 2).Value = "Unit";
                    worksheet.Cell(1, 3).Value = "Current Stock";
                    worksheet.Cell(1, 4).Value = "Default Rate";
                    worksheet.Cell(1, 5).Value = "Stock Value";

                    // Data
                    int row = 2;
                    foreach (var item in items)
                    {
                        worksheet.Cell(row, 1).Value = item.Name;
                        worksheet.Cell(row, 2).Value = item.Unit;
                        worksheet.Cell(row, 3).Value = item.CurrentStock;
                        worksheet.Cell(row, 4).Value = item.DefaultRate;
                        worksheet.Cell(row, 5).Value = item.CurrentStock * item.DefaultRate;
                        row++;
                    }

                    // Format as table
                    var range = worksheet.Range(1, 1, row - 1, 5);
                    range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    worksheet.Row(1).Style.Font.Bold = true;
                    worksheet.Row(1).Style.Fill.BackgroundColor = XLColor.LightGray;
                }
                else if (reportType == "customers")
                {
                    var customers = await _customerRepository.GetActiveCustomersAsync();

                    // Headers
                    worksheet.Cell(1, 1).Value = "Name";
                    worksheet.Cell(1, 2).Value = "Phone";
                    worksheet.Cell(1, 3).Value = "Address";
                    worksheet.Cell(1, 4).Value = "Status";

                    // Data
                    int row = 2;
                    foreach (var customer in customers)
                    {
                        worksheet.Cell(row, 1).Value = customer.Name;
                        worksheet.Cell(row, 2).Value = customer.Phone;
                        worksheet.Cell(row, 3).Value = customer.Address;
                        worksheet.Cell(row, 4).Value = customer.IsActive ? "Active" : "Inactive";
                        row++;
                    }

                    // Format as table
                    var range = worksheet.Range(1, 1, row - 1, 4);
                    range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    worksheet.Row(1).Style.Font.Bold = true;
                    worksheet.Row(1).Style.Fill.BackgroundColor = XLColor.LightGray;
                }
                else if (reportType == "customerLedger" && id.HasValue && fromDate.HasValue && toDate.HasValue)
                {
                    var customer = await _customerRepository.GetByIdAsync(id.Value);
                    if (customer != null)
                    {
                        var openingBalance = await GetCustomerOpeningBalanceAsync(id.Value, fromDate.Value);

                        var vouchers = await _context.Vouchers
                            .Include(v => v.PurchasingCustomer)
                            .Include(v => v.ReceivingCustomer)
                            .Include(v => v.Item)
                            .Include(v => v.ExpenseHead)
                            .Include(v => v.Project)
                            .Where(v => (v.PurchasingCustomerId == id.Value || v.ReceivingCustomerId == id.Value) &&
                                       v.VoucherDate >= fromDate.Value &&
                                       v.VoucherDate <= toDate.Value.AddDays(1))
                            .OrderBy(v => v.VoucherDate)
                            .ThenBy(v => v.Id)
                            .ToListAsync();

                        // Headers
                        worksheet.Cell(1, 1).Value = "Customer Ledger Report";
                        worksheet.Cell(2, 1).Value = $"Customer: {customer.Name}";
                        worksheet.Cell(3, 1).Value = $"Period: {fromDate.Value:dd-MMM-yyyy} to {toDate.Value:dd-MMM-yyyy}";

                        // Table headers
                        worksheet.Cell(5, 1).Value = "Date";
                        worksheet.Cell(5, 2).Value = "Transaction No";
                        worksheet.Cell(5, 3).Value = "Type";
                        worksheet.Cell(5, 4).Value = "Particulars";
                        worksheet.Cell(5, 5).Value = "Debit (Dr)";
                        worksheet.Cell(5, 6).Value = "Credit (Cr)";
                        worksheet.Cell(5, 7).Value = "Balance";

                        // Opening balance
                        int row = 6;
                        worksheet.Cell(row, 1).Value = fromDate.Value.ToString("dd-MMM-yyyy");
                        worksheet.Cell(row, 4).Value = "Opening Balance";
                        worksheet.Cell(row, 5).Value = openingBalance > 0 ? openingBalance : 0;
                        worksheet.Cell(row, 6).Value = openingBalance < 0 ? Math.Abs(openingBalance) : 0;
                        worksheet.Cell(row, 7).Value = $"{Math.Abs(openingBalance):N0} {(openingBalance >= 0 ? "Dr" : "Cr")}";
                        row++;

                        decimal runningBalance = openingBalance;
                        decimal totalDebit = 0;
                        decimal totalCredit = 0;

                        // NEW DR/CR Logic: Purchase=CR, Sale=DR
                        foreach (var voucher in vouchers)
                        {
                            decimal debit = 0;
                            decimal credit = 0;
                            string particulars = "";

                            if (voucher.PurchasingCustomerId == id.Value)
                            {
                                switch (voucher.VoucherType)
                                {
                                    case VoucherType.Purchase:
                                        credit = voucher.Amount;  // Purchase = CR
                                        particulars = $"Purchase - {voucher.Item?.Name ?? "N/A"}";
                                        break;
                                    case VoucherType.CashPaid:
                                        debit = voucher.Amount;   // CashPaid = DR
                                        particulars = "Cash Paid";
                                        break;
                                    case VoucherType.CCR:
                                        debit = voucher.Amount;   // CCR = DR
                                        particulars = $"CCR - From {voucher.ReceivingCustomer?.Name ?? "N/A"}";
                                        break;
                                }
                            }

                            if (voucher.ReceivingCustomerId == id.Value)
                            {
                                switch (voucher.VoucherType)
                                {
                                    case VoucherType.Sale:
                                        debit = voucher.Amount;   // Sale = DR
                                        particulars = $"Sale - {voucher.Item?.Name ?? "N/A"}";
                                        break;
                                    case VoucherType.CashReceived:
                                        credit = voucher.Amount;  // CashReceived = CR
                                        particulars = "Cash Received";
                                        break;
                                    case VoucherType.CCR:
                                        credit = voucher.Amount;  // CCR = CR
                                        particulars = $"CCR - To {voucher.PurchasingCustomer?.Name ?? "N/A"}";
                                        break;
                                }
                            }

                            runningBalance += debit - credit;
                            totalDebit += debit;
                            totalCredit += credit;

                            worksheet.Cell(row, 1).Value = voucher.VoucherDate.ToString("dd-MMM-yyyy");
                            worksheet.Cell(row, 2).Value = voucher.TransactionNumber;
                            worksheet.Cell(row, 3).Value = voucher.VoucherType.ToString();
                            worksheet.Cell(row, 4).Value = particulars;
                            worksheet.Cell(row, 5).Value = debit > 0 ? debit : 0;
                            worksheet.Cell(row, 6).Value = credit > 0 ? credit : 0;
                            worksheet.Cell(row, 7).Value = $"{Math.Abs(runningBalance):N0} {(runningBalance >= 0 ? "Dr" : "Cr")}";
                            row++;
                        }

                        // Totals
                        worksheet.Cell(row, 4).Value = "Total:";
                        worksheet.Cell(row, 5).Value = totalDebit;
                        worksheet.Cell(row, 6).Value = totalCredit;
                        worksheet.Cell(row, 7).Value = $"{Math.Abs(runningBalance):N0} {(runningBalance >= 0 ? "Dr" : "Cr")}";

                        // Format
                        worksheet.Row(5).Style.Font.Bold = true;
                        worksheet.Row(5).Style.Fill.BackgroundColor = XLColor.LightGray;
                        worksheet.Row(row).Style.Font.Bold = true;
                        worksheet.Row(row).Style.Fill.BackgroundColor = XLColor.LightGray;

                        var range = worksheet.Range(5, 1, row, 7);
                        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    }
                }

                // Auto-fit columns
                worksheet.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                var content = stream.ToArray();

                return File(content,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"{reportType}_Report_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting to Excel");
                TempData["Error"] = "Error exporting report. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Reports/CashFlow
        public async Task<IActionResult> CashFlow(DateTime? fromDate, DateTime? toDate)
        {
            try
            {
                // Default to showing last 30 days if no dates specified
                var endDate = toDate ?? DateTime.Today;
                var startDate = fromDate ?? DateTime.Today.AddDays(-30);

                // Get all cash transactions
                var vouchers = await _voucherRepository.GetVouchersByDateRangeAsync(startDate, endDate.AddDays(1));

                // Filter only cash transactions
                var cashVouchers = vouchers.Where(v =>
                    v.CashType == CashType.Cash ||
                    v.VoucherType == VoucherType.CashPaid ||
                    v.VoucherType == VoucherType.CashReceived).ToList();

                // Calculate cash in and out
                decimal cashIn = 0;
                decimal cashOut = 0;
                decimal openingBalance = await GetOpeningCashBalanceAsync(startDate);

                foreach (var voucher in cashVouchers)
                {
                    switch (voucher.VoucherType)
                    {
                        case VoucherType.Sale:
                        case VoucherType.CashReceived:
                            if (voucher.CashType == CashType.Cash)
                                cashIn += voucher.Amount;
                            break;
                        case VoucherType.Purchase:
                        case VoucherType.Expense:
                        case VoucherType.CashPaid:
                        case VoucherType.Hazri:
                            if (voucher.CashType == CashType.Cash)
                                cashOut += voucher.Amount;
                            break;
                    }
                }

                ViewBag.FromDate = startDate;
                ViewBag.ToDate = endDate;
                ViewBag.CashIn = cashIn;
                ViewBag.CashOut = cashOut;
                ViewBag.OpeningBalance = openingBalance;
                ViewBag.ClosingBalance = openingBalance + cashIn - cashOut;
                ViewBag.CashVouchers = cashVouchers.OrderBy(v => v.VoucherDate);

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating cash flow report");
                TempData["Error"] = "Error generating cash flow report.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Reports/StockReport - UPDATED with date filtering
        public async Task<IActionResult> StockReport(DateTime? fromDate, DateTime? toDate, bool showAll = false)
        {
            try
            {
                var items = await _context.Items
                    .Where(i => i.StockTrackingEnabled && i.IsActive)
                    .OrderBy(i => i.Name)
                    .ToListAsync();

                // If dates are specified, calculate stock movement
                if (fromDate.HasValue && toDate.HasValue && !showAll)
                {
                    var vouchers = await _voucherRepository.GetVouchersByDateRangeAsync(
                        fromDate.Value,
                        toDate.Value.AddDays(1));

                    // Create stock movement summary
                    var stockMovements = new Dictionary<int, StockMovement>();

                    foreach (var item in items)
                    {
                        stockMovements[item.Id] = new StockMovement
                        {
                            Item = item,
                            OpeningStock = await GetOpeningStockAsync(item.Id, fromDate.Value),
                            PurchaseQty = 0,
                            SaleQty = 0,
                            CurrentStock = item.CurrentStock
                        };
                    }

                    // Calculate movements from vouchers
                    foreach (var voucher in vouchers.Where(v => v.ItemId.HasValue))
                    {
                        if (stockMovements.ContainsKey(voucher.ItemId.Value))
                        {
                            if (voucher.VoucherType == VoucherType.Purchase && voucher.StockInclude)
                            {
                                stockMovements[voucher.ItemId.Value].PurchaseQty += voucher.Quantity ?? 0;
                            }
                            else if (voucher.VoucherType == VoucherType.Sale)
                            {
                                stockMovements[voucher.ItemId.Value].SaleQty += voucher.Quantity ?? 0;
                            }
                        }
                    }

                    ViewBag.StockMovements = stockMovements.Values;
                    ViewBag.ShowMovement = true;
                }
                else
                {
                    ViewBag.ShowMovement = false;
                }

                ViewBag.FromDate = fromDate;
                ViewBag.ToDate = toDate;
                ViewBag.ShowAll = showAll;

                return View(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating stock report");
                TempData["Error"] = "Error generating stock report.";
                return RedirectToAction(nameof(Index));
            }
        }

        // Helper method to get opening cash balance
        private async Task<decimal> GetOpeningCashBalanceAsync(DateTime date)
        {
            var previousVouchers = await _context.Vouchers
                .Where(v => v.VoucherDate < date &&
                           (v.CashType == CashType.Cash ||
                            v.VoucherType == VoucherType.CashPaid ||
                            v.VoucherType == VoucherType.CashReceived))
                .ToListAsync();

            decimal balance = 0;
            foreach (var voucher in previousVouchers)
            {
                switch (voucher.VoucherType)
                {
                    case VoucherType.Sale:
                    case VoucherType.CashReceived:
                        if (voucher.CashType == CashType.Cash)
                            balance += voucher.Amount;
                        break;
                    case VoucherType.Purchase:
                    case VoucherType.Expense:
                    case VoucherType.CashPaid:
                    case VoucherType.Hazri:
                        if (voucher.CashType == CashType.Cash)
                            balance -= voucher.Amount;
                        break;
                }
            }
            return balance;
        }

        // Helper method to get opening stock
        private async Task<decimal> GetOpeningStockAsync(int itemId, DateTime date)
        {
            var previousVouchers = await _context.Vouchers
                .Where(v => v.ItemId == itemId && v.VoucherDate < date)
                .ToListAsync();

            decimal stock = 0;
            foreach (var voucher in previousVouchers)
            {
                if (voucher.VoucherType == VoucherType.Purchase && voucher.StockInclude)
                {
                    stock += voucher.Quantity ?? 0;
                }
                else if (voucher.VoucherType == VoucherType.Sale)
                {
                    stock -= voucher.Quantity ?? 0;
                }
            }
            return stock;
        }

        // GET: Reports/DailyCashBook
        public async Task<IActionResult> DailyCashBook(DateTime? fromDate, DateTime? toDate)
        {
            try
            {
                var startDate = fromDate ?? DateTime.Today;
                var endDate = toDate ?? DateTime.Today;
                var nextDay = endDate.AddDays(1);

                // Get opening balance (based on start date)
                var openingBalance = await GetOpeningCashBalanceAsync(startDate);

                // Get transactions in range
                var vouchers = await _context.Vouchers
                    .Include(v => v.PurchasingCustomer)
                    .Include(v => v.ReceivingCustomer)
                    .Include(v => v.Item)
                    .Include(v => v.ExpenseHead)
                    .Include(v => v.Project)
                    .Where(v => v.VoucherDate >= startDate &&
                               v.VoucherDate < nextDay &&
                               (v.CashType == CashType.Cash ||
                                v.VoucherType == VoucherType.CashPaid ||
                                v.VoucherType == VoucherType.CashReceived))
                    .OrderBy(v => v.VoucherDate)
                    .ToListAsync();

                ViewBag.FromDate = startDate;
                ViewBag.ToDate = endDate;
                ViewBag.ReportDate = startDate; // kept for backward compat
                ViewBag.OpeningBalance = openingBalance;
                ViewBag.Vouchers = vouchers;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating daily cash book");
                TempData["Error"] = "Error generating daily cash book.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Reports/CustomerLedger
        public async Task<IActionResult> CustomerLedger(int? customerId, DateTime? fromDate, DateTime? toDate, int? itemId, string? voucherType)
        {
            try
            {
                if (!customerId.HasValue)
                {
                    // Show selection page
                    ViewBag.Customers = new SelectList(await _customerRepository.GetActiveCustomersAsync(), "Id", "Name");
                    return View();
                }

                var customer = await _customerRepository.GetByIdAsync(customerId.Value);
                if (customer == null)
                {
                    TempData["Error"] = "Customer not found.";
                    return RedirectToAction(nameof(Index));
                }

                // Default to showing last 90 days if no dates specified
                var endDate = toDate ?? DateTime.Today;
                var startDate = fromDate ?? DateTime.Today.AddDays(-90);

                // Get opening balance (transactions before start date)
                var openingBalance = await GetCustomerOpeningBalanceAsync(customerId.Value, startDate);

                // Get all transactions for the customer in the date range
                var query = _context.Vouchers
                    .Include(v => v.PurchasingCustomer)
                    .Include(v => v.ReceivingCustomer)
                    .Include(v => v.Item)
                    .Include(v => v.ExpenseHead)
                    .Include(v => v.Project)
                    .Where(v => (v.PurchasingCustomerId == customerId.Value ||
                                v.ReceivingCustomerId == customerId.Value) &&
                               v.VoucherDate >= startDate &&
                               v.VoucherDate <= endDate.AddDays(1))
                    .AsQueryable();

                // Apply item filter if selected
                if (itemId.HasValue && itemId.Value > 0)
                {
                    query = query.Where(v => v.ItemId == itemId.Value);
                }

                // Apply voucher type filter if selected
                if (!string.IsNullOrEmpty(voucherType) && Enum.TryParse<VoucherType>(voucherType, out var vType))
                {
                    query = query.Where(v => v.VoucherType == vType);
                }

                var vouchers = await query
                    .OrderBy(v => v.VoucherDate)
                    .ThenBy(v => v.Id)
                    .ToListAsync();

                // Calculate totals
                // NEW DR/CR Logic: Purchase=CR, Sale=DR
                decimal totalDebit = 0;
                decimal totalCredit = 0;

                foreach (var voucher in vouchers)
                {
                    // Purchase = CR (we owe the supplier)
                    // CashPaid = DR (we paid, reduces what we owe)
                    if (voucher.PurchasingCustomerId == customerId.Value)
                    {
                        switch (voucher.VoucherType)
                        {
                            case VoucherType.Purchase:
                                totalCredit += voucher.Amount;  // Purchase = CR
                                break;
                            case VoucherType.CashPaid:
                            case VoucherType.CCR:
                                totalDebit += voucher.Amount;   // CashPaid = DR
                                break;
                        }
                    }

                    // Sale = DR (customer owes us)
                    // CashReceived = CR (customer paid, reduces what they owe)
                    if (voucher.ReceivingCustomerId == customerId.Value)
                    {
                        switch (voucher.VoucherType)
                        {
                            case VoucherType.Sale:
                                totalDebit += voucher.Amount;   // Sale = DR
                                break;
                            case VoucherType.CashReceived:
                            case VoucherType.CCR:
                                totalCredit += voucher.Amount;  // CashReceived = CR
                                break;
                        }
                    }
                }

                // Populate filter dropdowns
                ViewBag.Items = new SelectList(await _itemRepository.GetActiveItemsAsync(), "Id", "Name", itemId);
                ViewBag.Customer = customer;
                ViewBag.FromDate = startDate;
                ViewBag.ToDate = endDate;
                ViewBag.OpeningBalance = openingBalance;
                ViewBag.TotalDebit = totalDebit;
                ViewBag.TotalCredit = totalCredit;
                ViewBag.ClosingBalance = openingBalance + totalDebit - totalCredit;
                ViewBag.Vouchers = vouchers;
                ViewBag.Customers = new SelectList(await _customerRepository.GetActiveCustomersAsync(), "Id", "Name", customerId);
                ViewBag.SelectedItemId = itemId;
                ViewBag.SelectedVoucherType = voucherType;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating customer ledger");
                TempData["Error"] = "Error generating customer ledger.";
                return RedirectToAction(nameof(Index));
            }
        }

        // Helper method to get opening balance for customer
        // NEW DR/CR Logic: Purchase=CR, Sale=DR
        private async Task<decimal> GetCustomerOpeningBalanceAsync(int customerId, DateTime date)
        {
            var previousVouchers = await _context.Vouchers
                .Where(v => (v.PurchasingCustomerId == customerId || v.ReceivingCustomerId == customerId) &&
                           v.VoucherDate < date)
                .ToListAsync();

            decimal balance = 0;
            foreach (var voucher in previousVouchers)
            {
                // Purchase = CR (we owe them) - decreases balance
                // CashPaid = DR (we paid) - increases balance
                if (voucher.PurchasingCustomerId == customerId)
                {
                    switch (voucher.VoucherType)
                    {
                        case VoucherType.Purchase:
                            balance -= voucher.Amount;  // CR decreases balance
                            break;
                        case VoucherType.CashPaid:
                        case VoucherType.CCR:
                            balance += voucher.Amount;  // DR increases balance
                            break;
                    }
                }

                // Sale = DR (they owe us) - increases balance
                // CashReceived = CR (they paid) - decreases balance
                if (voucher.ReceivingCustomerId == customerId)
                {
                    switch (voucher.VoucherType)
                    {
                        case VoucherType.Sale:
                            balance += voucher.Amount;  // DR increases balance
                            break;
                        case VoucherType.CashReceived:
                        case VoucherType.CCR:
                            balance -= voucher.Amount;  // CR decreases balance
                            break;
                    }
                }
            }
            return balance;
        }

        // Helper method to get item-wise purchase and sale summary for a project
        private async Task<List<ProjectItemSummary>> GetProjectItemSummaryAsync(int projectId, DateTime fromDate, DateTime toDate, string? voucherType = null, int? itemId = null, int? customerId = null)
        {
            var query = _context.Vouchers
                .Include(v => v.Item)
                .Where(v => v.ProjectId == projectId &&
                           v.ItemId != null &&
                           v.VoucherDate >= fromDate &&
                           v.VoucherDate <= toDate &&
                           (v.VoucherType == VoucherType.Purchase || v.VoucherType == VoucherType.Sale));

            // Apply voucher type filter if specified
            if (!string.IsNullOrEmpty(voucherType) && Enum.TryParse<VoucherType>(voucherType, out var vType))
            {
                query = query.Where(v => v.VoucherType == vType);
            }

            // Apply item filter if specified
            if (itemId.HasValue && itemId.Value > 0)
            {
                query = query.Where(v => v.ItemId == itemId.Value);
            }

            // Apply customer filter if specified (check both purchasing and receiving customer)
            if (customerId.HasValue && customerId.Value > 0)
            {
                query = query.Where(v => v.PurchasingCustomerId == customerId.Value ||
                                        v.ReceivingCustomerId == customerId.Value);
            }

            var vouchers = await query.ToListAsync();

            var itemGroups = vouchers.GroupBy(v => v.ItemId.Value);
            var summary = new List<ProjectItemSummary>();

            foreach (var group in itemGroups)
            {
                var item = group.First().Item;
                var purchases = group.Where(v => v.VoucherType == VoucherType.Purchase).ToList();
                var sales = group.Where(v => v.VoucherType == VoucherType.Sale).ToList();

                var purchaseQty = purchases.Sum(p => p.Quantity ?? 0);
                var saleQty = sales.Sum(s => s.Quantity ?? 0);
                var stockQty = purchaseQty - saleQty;

                var purchaseAmount = purchases.Sum(p => p.Amount);
                var saleAmount = sales.Sum(s => s.Amount);

                // Calculate opening stock (purchases - sales before fromDate for this project+item)
                var openingStockQty = await GetOpeningStockAsync(item.Id, fromDate);

                // Calculate average purchase rate
                var avgPurchaseRate = purchaseQty > 0 ? purchaseAmount / purchaseQty : 0;
                var stockValue = stockQty * avgPurchaseRate;

                summary.Add(new ProjectItemSummary
                {
                    ItemName = item?.Name ?? "Unknown",
                    OpeningStockQty = openingStockQty,
                    PurchaseQty = purchaseQty,
                    SaleQty = saleQty,
                    StockQty = stockQty,
                    PurchaseAmount = purchaseAmount,
                    SaleAmount = saleAmount,
                    AvgPurchaseRate = avgPurchaseRate,
                    StockValue = stockValue,
                    Unit = item?.Unit ?? ""
                });
            }

            return summary.OrderBy(s => s.ItemName).ToList();
        }

        // Helper method to get opening bank balance
        private async Task<decimal> GetBankOpeningBalanceAsync(int bankId, DateTime date)
        {
            // Get initial bank balance from Bank model
            var bank = await _bankRepository.GetByIdAsync(bankId);
            decimal balance = bank?.Balance ?? 0;

            var previousVouchers = await _context.Vouchers
                .Where(v => (v.BankCustomerPaidId == bankId || v.BankCustomerReceiverId == bankId) &&
                           v.VoucherDate < date)
                .ToListAsync();

            foreach (var voucher in previousVouchers)
            {
                // Money paid from bank (debit - reduces bank balance)
                if (voucher.BankCustomerPaidId == bankId)
                {
                    balance -= voucher.Amount;
                }

                // Money received into bank (credit - increases bank balance)
                if (voucher.BankCustomerReceiverId == bankId)
                {
                    balance += voucher.Amount;
                }
            }
            return balance;
        }

        // GET: Reports/StockTrackReport - Track which customer purchased what and when
        public async Task<IActionResult> StockTrackReport(DateTime? fromDate, DateTime? toDate, int? itemId, int? customerId)
        {
            try
            {
                var startDate = fromDate ?? DateTime.Today.AddMonths(-1);
                var endDate = toDate ?? DateTime.Today;

                ViewBag.Items = new SelectList(await _itemRepository.GetActiveItemsAsync(), "Id", "Name", itemId);
                ViewBag.Customers = new SelectList(await _customerRepository.GetActiveCustomersAsync(), "Id", "Name", customerId);

                var query = _context.Vouchers
                    .Include(v => v.Item)
                    .Include(v => v.PurchasingCustomer)
                    .Include(v => v.ReceivingCustomer)
                    .Include(v => v.Project)
                    .Where(v => (v.VoucherType == VoucherType.Purchase || v.VoucherType == VoucherType.Sale) &&
                               v.ItemId != null &&
                               v.VoucherDate >= startDate && v.VoucherDate <= endDate.AddDays(1));

                if (itemId.HasValue)
                {
                    query = query.Where(v => v.ItemId == itemId);
                }

                if (customerId.HasValue)
                {
                    query = query.Where(v => v.PurchasingCustomerId == customerId || v.ReceivingCustomerId == customerId);
                }

                var transactions = await query.OrderByDescending(v => v.VoucherDate).ThenByDescending(v => v.Id).ToListAsync();

                ViewBag.FromDate = startDate;
                ViewBag.ToDate = endDate;
                ViewBag.SelectedItemId = itemId;
                ViewBag.SelectedCustomerId = customerId;
                ViewBag.Transactions = transactions;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating stock track report");
                TempData["Error"] = "Error generating stock track report.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Reports/AllExpensesReport - All expenses in one page
        public async Task<IActionResult> AllExpensesReport(DateTime? fromDate, DateTime? toDate, int? expenseHeadId)
        {
            try
            {
                var startDate = fromDate ?? DateTime.Today.AddMonths(-1);
                var endDate = toDate ?? DateTime.Today;

                ViewBag.ExpenseHeads = new SelectList(await _expenseHeadRepository.GetActiveExpenseHeadsAsync(), "Id", "Name", expenseHeadId);

                var query = _context.Vouchers
                    .Include(v => v.ExpenseHead)
                    .Include(v => v.Project)
                    .Include(v => v.PurchasingCustomer)
                    .Where(v => (v.VoucherType == VoucherType.Expense || v.VoucherType == VoucherType.Hazri) &&
                               v.VoucherDate >= startDate && v.VoucherDate <= endDate.AddDays(1));

                if (expenseHeadId.HasValue)
                {
                    query = query.Where(v => v.ExpenseHeadId == expenseHeadId);
                }

                var expenses = await query.OrderByDescending(v => v.VoucherDate).ThenByDescending(v => v.Id).ToListAsync();

                // Separate hazri entries from expense entries
                var hazriEntries = expenses.Where(e => e.VoucherType == VoucherType.Hazri).ToList();
                var expenseEntries = expenses.Where(e => e.VoucherType == VoucherType.Expense).ToList();

                // Calculate opening balance: sum of expenses before startDate (excluding hazri)
                var openingBalanceQuery = _context.Vouchers
                    .Where(v => v.VoucherType == VoucherType.Expense &&
                               v.VoucherDate < startDate);
                if (expenseHeadId.HasValue)
                {
                    openingBalanceQuery = openingBalanceQuery.Where(v => v.ExpenseHeadId == expenseHeadId);
                }
                var openingBalance = await openingBalanceQuery.SumAsync(v => v.Amount);

                // Group by expense head for summary (expense only, excluding hazri)
                var expenseSummary = expenseEntries
                    .GroupBy(e => e.ExpenseHead?.Name ?? "Unknown")
                    .Select(g => new ExpenseSummaryItem { ExpenseHead = g.Key, Total = g.Sum(e => e.Amount) })
                    .OrderByDescending(x => x.Total)
                    .ToList();

                ViewBag.FromDate = startDate;
                ViewBag.ToDate = endDate;
                ViewBag.SelectedExpenseHeadId = expenseHeadId;
                ViewBag.Expenses = expenses;
                ViewBag.ExpenseEntries = expenseEntries;
                ViewBag.HazriEntries = hazriEntries;
                ViewBag.ExpenseSummary = expenseSummary;
                ViewBag.TotalExpenses = expenseEntries.Sum(e => e.Amount);
                ViewBag.TotalHazri = hazriEntries.Sum(e => e.Amount);
                ViewBag.OpeningBalance = openingBalance;
                ViewBag.NetTotal = expenseEntries.Sum(e => e.Amount) - hazriEntries.Sum(e => e.Amount);

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating expenses report");
                TempData["Error"] = "Error generating expenses report.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Reports/ExpenseReport - Expense only (excluding Hazri)
        public async Task<IActionResult> ExpenseReport(DateTime? fromDate, DateTime? toDate, int? expenseHeadId, int? projectId)
        {
            try
            {
                var startDate = fromDate ?? DateTime.Today.AddMonths(-1);
                var endDate = toDate ?? DateTime.Today;

                ViewBag.ExpenseHeads = new SelectList(await _expenseHeadRepository.GetActiveExpenseHeadsAsync(), "Id", "Name", expenseHeadId);
                ViewBag.Projects = new SelectList(await _projectRepository.GetActiveProjectsAsync(), "Id", "Name", projectId);

                var query = _context.Vouchers
                    .Include(v => v.ExpenseHead)
                    .Include(v => v.Project)
                    .Include(v => v.PurchasingCustomer)
                    .Where(v => v.VoucherType == VoucherType.Expense &&
                               v.VoucherDate >= startDate && v.VoucherDate <= endDate.AddDays(1));

                if (expenseHeadId.HasValue)
                {
                    query = query.Where(v => v.ExpenseHeadId == expenseHeadId);
                }

                if (projectId.HasValue)
                {
                    query = query.Where(v => v.ProjectId == projectId);
                }

                var expenses = await query.OrderByDescending(v => v.VoucherDate).ThenByDescending(v => v.Id).ToListAsync();

                // Group by expense head for summary
                var expenseSummary = expenses
                    .GroupBy(e => e.ExpenseHead?.Name ?? "Unknown")
                    .Select(g => new ExpenseSummaryItem { ExpenseHead = g.Key, Total = g.Sum(e => e.Amount) })
                    .OrderByDescending(x => x.Total)
                    .ToList();

                ViewBag.FromDate = startDate;
                ViewBag.ToDate = endDate;
                ViewBag.SelectedExpenseHeadId = expenseHeadId;
                ViewBag.SelectedProjectId = projectId;
                ViewBag.Expenses = expenses;
                ViewBag.ExpenseSummary = expenseSummary;
                ViewBag.TotalExpenses = expenses.Sum(e => e.Amount);

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating expense report");
                TempData["Error"] = "Error generating expense report.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Reports/HazriReport - Hazri (Attendance) only
        public async Task<IActionResult> HazriReport(DateTime? fromDate, DateTime? toDate, int? expenseHeadId, int? projectId)
        {
            try
            {
                var startDate = fromDate ?? DateTime.Today.AddMonths(-1);
                var endDate = toDate ?? DateTime.Today;

                ViewBag.ExpenseHeads = new SelectList(await _expenseHeadRepository.GetActiveExpenseHeadsAsync(), "Id", "Name", expenseHeadId);
                ViewBag.Projects = new SelectList(await _projectRepository.GetActiveProjectsAsync(), "Id", "Name", projectId);

                var query = _context.Vouchers
                    .Include(v => v.ExpenseHead)
                    .Include(v => v.Project)
                    .Include(v => v.PurchasingCustomer)
                    .Where(v => v.VoucherType == VoucherType.Hazri &&
                               v.VoucherDate >= startDate && v.VoucherDate <= endDate.AddDays(1));

                if (expenseHeadId.HasValue)
                {
                    query = query.Where(v => v.ExpenseHeadId == expenseHeadId);
                }

                if (projectId.HasValue)
                {
                    query = query.Where(v => v.ProjectId == projectId);
                }

                var hazriRecords = await query.OrderByDescending(v => v.VoucherDate).ThenByDescending(v => v.Id).ToListAsync();

                // Group by expense head for summary
                var hazriSummary = hazriRecords
                    .GroupBy(h => h.ExpenseHead?.Name ?? "Unknown")
                    .Select(g => new { ExpenseHead = g.Key, Total = g.Sum(h => h.Amount) })
                    .OrderByDescending(x => x.Total)
                    .ToList();

                ViewBag.FromDate = startDate;
                ViewBag.ToDate = endDate;
                ViewBag.SelectedExpenseHeadId = expenseHeadId;
                ViewBag.SelectedProjectId = projectId;
                ViewBag.HazriRecords = hazriRecords;
                ViewBag.HazriSummary = hazriSummary;
                ViewBag.TotalHazri = hazriRecords.Sum(h => h.Amount);

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating hazri report");
                TempData["Error"] = "Error generating hazri report.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Reports/AllProjectsReport - All projects summary
        public async Task<IActionResult> AllProjectsReport(DateTime? fromDate, DateTime? toDate)
        {
            try
            {
                var startDate = fromDate ?? new DateTime(DateTime.Today.Year, 1, 1);
                var endDate = toDate ?? DateTime.Today;

                var projects = await _projectRepository.GetActiveProjectsAsync();
                var projectReports = new List<ProjectReportItem>();

                foreach (var project in projects)
                {
                    var vouchers = await _context.Vouchers
                        .Where(v => v.ProjectId == project.Id &&
                                   v.VoucherDate >= startDate && v.VoucherDate <= endDate.AddDays(1))
                        .ToListAsync();

                    var revenue = vouchers.Where(v => v.VoucherType == VoucherType.Sale).Sum(v => v.Amount);
                    var purchases = vouchers.Where(v => v.VoucherType == VoucherType.Purchase).Sum(v => v.Amount);
                    var expenses = vouchers.Where(v => v.VoucherType == VoucherType.Expense || v.VoucherType == VoucherType.Hazri).Sum(v => v.Amount);

                    projectReports.Add(new ProjectReportItem
                    {
                        Project = project,
                        Revenue = revenue,
                        Purchases = purchases,
                        Expenses = expenses,
                        ProfitLoss = revenue - purchases - expenses,
                        VoucherCount = vouchers.Count
                    });
                }

                ViewBag.FromDate = startDate;
                ViewBag.ToDate = endDate;
                ViewBag.ProjectReports = projectReports.OrderByDescending(p => p.Revenue).ToList();
                ViewBag.TotalRevenue = projectReports.Sum(p => p.Revenue);
                ViewBag.TotalPurchases = projectReports.Sum(p => p.Purchases);
                ViewBag.TotalExpenses = projectReports.Sum(p => p.Expenses);
                ViewBag.TotalProfitLoss = projectReports.Sum(p => p.ProfitLoss);

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating projects report");
                TempData["Error"] = "Error generating projects report.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Reports/OpenWhatsAppFolder
        [HttpGet]
        public IActionResult OpenWhatsAppFolder()
        {
            try
            {
                string whatsAppFolder = Path.Combine(_environment.ContentRootPath, "WhatsAppData");

                if (Directory.Exists(whatsAppFolder))
                {
                    // Open file explorer at the WhatsAppData folder
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = whatsAppFolder,
                        UseShellExecute = true,
                        Verb = "open"
                    });

                    return Json(new { success = true, message = "Folder opened successfully" });
                }
                else
                {
                    return Json(new { success = false, message = "WhatsAppData folder not found" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error opening WhatsApp folder");
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // POST: Reports/SendCustomerLedgerToWhatsApp
        [HttpPost]
        public async Task<IActionResult> SendCustomerLedgerToWhatsApp(IFormFile pdfFile, int customerId, DateTime fromDate, DateTime toDate, decimal closingBalance, string balanceType)
        {
            try
            {
                if (pdfFile == null || pdfFile.Length == 0)
                {
                    return Json(new { success = false, message = "PDF file is required" });
                }

                var customer = await _customerRepository.GetByIdAsync(customerId);
                if (customer == null)
                {
                    return Json(new { success = false, message = "Customer not found" });
                }

                // 1. Create WhatsAppData folder if it doesn't exist
                string whatsAppFolder = Path.Combine(_environment.ContentRootPath, "WhatsAppData");
                Directory.CreateDirectory(whatsAppFolder);

                // 2. Create timestamped filename
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string safeFileName = $"{timestamp}_CustomerLedger_{customer.Name.Replace(" ", "_")}.pdf";
                string filePath = Path.Combine(whatsAppFolder, safeFileName);

                // 3. Save PDF file
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await pdfFile.CopyToAsync(stream);
                }

                _logger.LogInformation($"PDF saved to: {filePath}");

                // 4. Prepare WhatsApp message
                string message = $"*Customer Ledger Report*\n" +
                                $"━━━━━━━━━━━━━━━━━━━━\n" +
                                $"📋 Customer: {customer.Name}\n" +
                                $"📅 Period: {fromDate:dd-MMM-yyyy} to {toDate:dd-MMM-yyyy}\n" +
                                $"💰 Closing Balance: Rs. {closingBalance:N0} {balanceType}\n\n" +
                                $"Please find the attached ledger report PDF.";

                // 5. Format phone number
                string phoneNumber = FormatPhoneNumber(customer.Phone);

                // 6. Build WhatsApp Web URL
                string whatsappUrl;
                if (!string.IsNullOrEmpty(phoneNumber))
                {
                    whatsappUrl = $"https://web.whatsapp.com/send?phone={phoneNumber}&text={Uri.EscapeDataString(message)}";
                }
                else
                {
                    whatsappUrl = $"https://web.whatsapp.com/send?text={Uri.EscapeDataString(message)}";
                }

                return Json(new
                {
                    success = true,
                    whatsappUrl = whatsappUrl,
                    filePath = filePath,
                    fileName = safeFileName,
                    message = "PDF saved successfully! WhatsApp Web will open shortly."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SendCustomerLedgerToWhatsApp");
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Formats phone number for WhatsApp (adds country code if needed)
        /// </summary>
        private string FormatPhoneNumber(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
                return string.Empty;

            // Remove all non-digit characters
            string phoneNumber = new string(phone.Where(char.IsDigit).ToArray());

            // If phone doesn't start with country code, add Pakistan code (+92)
            if (!phoneNumber.StartsWith("92"))
            {
                if (phoneNumber.StartsWith("0"))
                {
                    phoneNumber = "92" + phoneNumber.Substring(1);
                }
                else
                {
                    phoneNumber = "92" + phoneNumber;
                }
            }

            return phoneNumber;
        }

        // GET: Reports/AllCustomersReport - Customers receivables and payables
        public async Task<IActionResult> AllCustomersReport(DateTime? asOfDate)
        {
            try
            {
                var date = asOfDate ?? DateTime.Today.AddDays(1);

                var customers = await _customerRepository.GetActiveCustomersAsync();
                var customerReports = new List<CustomerReportItem>();

                foreach (var customer in customers)
                {
                    // Calculate balance based on DR/CR logic
                    // Purchase = CR (we owe supplier), Sale = DR (customer owes us)
                    // CashPaid = DR (reduces what we owe), CashReceived = CR (reduces what they owe)
                    var vouchers = await _context.Vouchers
                        .Where(v => (v.PurchasingCustomerId == customer.Id || v.ReceivingCustomerId == customer.Id) &&
                                   v.VoucherDate < date)
                        .ToListAsync();

                    decimal toReceive = 0; // Amount customer owes us (DR)
                    decimal toPay = 0;     // Amount we owe to supplier (CR)

                    foreach (var v in vouchers)
                    {
                        if (v.ReceivingCustomerId == customer.Id)
                        {
                            // Sale or CashReceived to this customer
                            if (v.VoucherType == VoucherType.Sale)
                                toReceive += v.Amount; // Customer owes us
                            else if (v.VoucherType == VoucherType.CashReceived)
                                toReceive -= v.Amount; // Customer paid us
                        }

                        if (v.PurchasingCustomerId == customer.Id)
                        {
                            // Purchase or CashPaid to this supplier
                            if (v.VoucherType == VoucherType.Purchase)
                                toPay += v.Amount; // We owe supplier
                            else if (v.VoucherType == VoucherType.CashPaid)
                                toPay -= v.Amount; // We paid supplier
                        }
                    }

                    if (toReceive != 0 || toPay != 0)
                    {
                        customerReports.Add(new CustomerReportItem
                        {
                            Customer = customer,
                            ToReceive = toReceive > 0 ? toReceive : 0,
                            ToPay = toPay > 0 ? toPay : 0,
                            NetBalance = toReceive - toPay
                        });
                    }
                }

                ViewBag.AsOfDate = date.AddDays(-1);
                ViewBag.CustomerReports = customerReports.OrderByDescending(c => Math.Abs(c.NetBalance)).ToList();
                ViewBag.TotalToReceive = customerReports.Sum(c => c.ToReceive);
                ViewBag.TotalToPay = customerReports.Sum(c => c.ToPay);
                ViewBag.NetBalance = customerReports.Sum(c => c.NetBalance);

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating customers report");
                TempData["Error"] = "Error generating customers report.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Reports/CapitalReport - Complete financial overview
        public async Task<IActionResult> CapitalReport(DateTime? asOfDate)
        {
            try
            {
                var date = asOfDate ?? DateTime.Today.AddDays(1);

                // 1. Stock Value - Calculate current stock with average purchase price
                var items = await _itemRepository.GetActiveItemsAsync();
                var stockItems = new List<StockValueItem>();
                decimal totalStockValue = 0;

                foreach (var item in items)
                {
                    // Get purchases (with or without StockInclude flag)
                    var purchases = await _context.Vouchers
                        .Where(v => v.ItemId == item.Id && v.VoucherType == VoucherType.Purchase && v.VoucherDate < date)
                        .ToListAsync();

                    // Get sales (with or without StockInclude flag)
                    var sales = await _context.Vouchers
                        .Where(v => v.ItemId == item.Id && v.VoucherType == VoucherType.Sale && v.VoucherDate < date)
                        .ToListAsync();

                    // Filter for stock calculations (only where StockInclude is true or null)
                    var stockPurchases = purchases.Where(p => p.StockInclude == true).ToList();
                    var stockSales = sales.Where(s => s.StockInclude == true).ToList();

                    decimal purchaseQty = stockPurchases.Sum(p => p.Quantity ?? 0);
                    decimal saleQty = stockSales.Sum(s => s.Quantity ?? 0);

                    // Include item's initial CurrentStock
                    decimal initialStock = item.CurrentStock;
                    decimal currentQty = initialStock + purchaseQty - saleQty;

                    if (currentQty > 0)
                    {
                        // Calculate average rate from purchases, or use item's DefaultRate if no purchases
                        decimal totalPurchaseAmount = stockPurchases.Sum(p => p.Amount);
                        decimal avgRate = purchaseQty > 0 ? totalPurchaseAmount / purchaseQty : item.DefaultRate;
                        decimal stockValue = currentQty * avgRate;
                        totalStockValue += stockValue;

                        stockItems.Add(new StockValueItem
                        {
                            Item = item,
                            Quantity = currentQty,
                            AvgRate = avgRate,
                            Value = stockValue
                        });
                    }
                }

                // 2. Customer Receivables & Payables
                var customers = await _customerRepository.GetActiveCustomersAsync();
                decimal totalReceivables = 0;
                decimal totalPayables = 0;
                var receivablesList = new List<CustomerReportItem>();
                var payablesList = new List<CustomerReportItem>();

                foreach (var customer in customers)
                {
                    var vouchers = await _context.Vouchers
                        .Where(v => (v.PurchasingCustomerId == customer.Id || v.ReceivingCustomerId == customer.Id) &&
                                   v.VoucherDate < date)
                        .ToListAsync();

                    decimal toReceive = 0;
                    decimal toPay = 0;

                    foreach (var v in vouchers)
                    {
                        if (v.ReceivingCustomerId == customer.Id)
                        {
                            if (v.VoucherType == VoucherType.Sale)
                                toReceive += v.Amount;
                            else if (v.VoucherType == VoucherType.CashReceived)
                                toReceive -= v.Amount;
                        }

                        if (v.PurchasingCustomerId == customer.Id)
                        {
                            if (v.VoucherType == VoucherType.Purchase)
                                toPay += v.Amount;
                            else if (v.VoucherType == VoucherType.CashPaid)
                                toPay -= v.Amount;
                        }
                    }

                    if (toReceive > 0)
                    {
                        totalReceivables += toReceive;
                        receivablesList.Add(new CustomerReportItem { Customer = customer, ToReceive = toReceive });
                    }
                    if (toPay > 0)
                    {
                        totalPayables += toPay;
                        payablesList.Add(new CustomerReportItem { Customer = customer, ToPay = toPay });
                    }
                }

                // 3. Cash in Hand
                decimal cashInHand = await GetCashOpeningBalanceAsync(date, null);

                // 4. Bank Balances
                var banks = await _bankRepository.GetActiveBanksAsync();
                var bankBalances = new List<BankBalanceItem>();
                decimal totalBankBalance = 0;

                foreach (var bank in banks)
                {
                    var balance = await GetBankOpeningBalanceAsync(bank.Id, date);
                    totalBankBalance += balance;
                    bankBalances.Add(new BankBalanceItem { Bank = bank, Balance = balance });
                }

                // 5. Expense Head Summary (amounts spent per expense head)
                var expenseHeads = await _expenseHeadRepository.GetActiveExpenseHeadsAsync();
                var expenseHeadSummary = new List<ExpenseHeadSummaryItem>();
                decimal totalExpenseHeadAmount = 0;

                foreach (var eh in expenseHeads)
                {
                    var expenseAmount = await _context.Vouchers
                        .Where(v => v.ExpenseHeadId == eh.Id &&
                                   (v.VoucherType == VoucherType.Expense || v.VoucherType == VoucherType.Hazri) &&
                                   v.VoucherDate < date)
                        .SumAsync(v => (decimal?)v.Amount) ?? 0;

                    if (expenseAmount > 0)
                    {
                        totalExpenseHeadAmount += expenseAmount;
                        expenseHeadSummary.Add(new ExpenseHeadSummaryItem { ExpenseHead = eh, TotalAmount = expenseAmount });
                    }
                }

                // Calculate Total Capital
                decimal totalCapital = totalStockValue + totalReceivables + cashInHand + totalBankBalance - totalPayables;

                ViewBag.AsOfDate = date.AddDays(-1);
                ViewBag.StockItems = stockItems.OrderByDescending(s => s.Value).ToList();
                ViewBag.TotalStockValue = totalStockValue;
                ViewBag.ReceivablesList = receivablesList.OrderByDescending(r => r.ToReceive).ToList();
                ViewBag.TotalReceivables = totalReceivables;
                ViewBag.PayablesList = payablesList.OrderByDescending(p => p.ToPay).ToList();
                ViewBag.TotalPayables = totalPayables;
                ViewBag.CashInHand = cashInHand;
                ViewBag.BankBalances = bankBalances.OrderByDescending(b => b.Balance).ToList();
                ViewBag.TotalBankBalance = totalBankBalance;
                ViewBag.ExpenseHeadSummary = expenseHeadSummary.OrderByDescending(e => e.TotalAmount).ToList();
                ViewBag.TotalExpenseHeadAmount = totalExpenseHeadAmount;
                ViewBag.TotalCapital = totalCapital;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating capital report");
                TempData["Error"] = "Error generating capital report.";
                return RedirectToAction(nameof(Index));
            }
        }
    }

    // Helper class for stock movement
    public class StockMovement
    {
        public Item Item { get; set; }
        public decimal OpeningStock { get; set; }
        public decimal PurchaseQty { get; set; }
        public decimal SaleQty { get; set; }
        public decimal CurrentStock { get; set; }
        public decimal ClosingStock => OpeningStock + PurchaseQty - SaleQty;
    }

    // Helper class for project report
    public class ProjectReportItem
    {
        public Project Project { get; set; }
        public decimal Revenue { get; set; }
        public decimal Purchases { get; set; }
        public decimal Expenses { get; set; }
        public decimal ProfitLoss { get; set; }
        public int VoucherCount { get; set; }
    }

    // Helper class for customer report
    public class CustomerReportItem
    {
        public Customer Customer { get; set; }
        public decimal ToReceive { get; set; }
        public decimal ToPay { get; set; }
        public decimal NetBalance { get; set; }
    }

    // Helper class for stock value in capital report
    public class StockValueItem
    {
        public Item Item { get; set; }
        public decimal Quantity { get; set; }
        public decimal AvgRate { get; set; }
        public decimal Value { get; set; }
    }

    // Helper class for bank balance in capital report
    public class BankBalanceItem
    {
        public Bank Bank { get; set; }
        public decimal Balance { get; set; }
    }

    // Helper class for expense head summary
    public class ExpenseHeadSummaryItem
    {
        public ExpenseHead ExpenseHead { get; set; }
        public decimal TotalAmount { get; set; }
    }

    // Helper class for project item summary
    public class ProjectItemSummary
    {
        public string ItemName { get; set; }
        public decimal OpeningStockQty { get; set; }
        public decimal PurchaseQty { get; set; }
        public decimal SaleQty { get; set; }
        public decimal StockQty { get; set; }
        public decimal PurchaseAmount { get; set; }
        public decimal SaleAmount { get; set; }
        public decimal AvgPurchaseRate { get; set; }
        public decimal StockValue { get; set; }
        public string Unit { get; set; }
    }

    // Helper class for expense summary
    public class ExpenseSummaryItem
    {
        public string ExpenseHead { get; set; }
        public decimal Total { get; set; }
    }
}