using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Data;
using VoucherManagementSystem.Helpers;
using VoucherManagementSystem.Interfaces;
using VoucherManagementSystem.Models;
using ClosedXML.Excel;
using System.Text;
using System.Reflection;

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

                var startDate = fromDate ?? new DateTime(DateTimeHelper.PkToday.Year, 1, 1);
                var endDate = toDate ?? DateTimeHelper.PkToday;

                // Related names come from the projection below instead of Include()
                var query = _context.Vouchers
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

                // Only the columns the Transaction Details table renders and the totals use
                var vouchers = (await query
                    .OrderBy(v => v.VoucherDate)
                    .Select(v => new
                    {
                        v.Id,
                        v.TransactionNumber,
                        v.VoucherDate,
                        v.VoucherType,
                        v.Amount,
                        v.Quantity,
                        v.Rate,
                        v.ExpenseHeadRate,
                        v.GariNo,
                        v.ExpenseHeadDetails,
                        v.PurchasingCustomerDetails,
                        v.ReceivingCustomerDetails,
                        ItemName = v.Item!.Name,
                        ExpenseHeadName = v.ExpenseHead!.Name,
                        PurchasingCustomerName = v.PurchasingCustomer!.Name,
                        ReceivingCustomerName = v.ReceivingCustomer!.Name
                    })
                    .ToListAsync())
                    .Select(r => new Voucher
                    {
                        Id = r.Id,
                        TransactionNumber = r.TransactionNumber,
                        VoucherDate = r.VoucherDate,
                        VoucherType = r.VoucherType,
                        Amount = r.Amount,
                        Quantity = r.Quantity,
                        Rate = r.Rate,
                        ExpenseHeadRate = r.ExpenseHeadRate,
                        GariNo = r.GariNo,
                        ExpenseHeadDetails = r.ExpenseHeadDetails,
                        PurchasingCustomerDetails = r.PurchasingCustomerDetails,
                        ReceivingCustomerDetails = r.ReceivingCustomerDetails,
                        Item = r.ItemName == null ? null : new Item { Name = r.ItemName },
                        ExpenseHead = r.ExpenseHeadName == null ? null : new ExpenseHead { Name = r.ExpenseHeadName },
                        PurchasingCustomer = r.PurchasingCustomerName == null ? null : new Customer { Name = r.PurchasingCustomerName },
                        ReceivingCustomer = r.ReceivingCustomerName == null ? null : new Customer { Name = r.ReceivingCustomerName }
                    })
                    .ToList();

                // Get item-wise purchase and sale summary with filters
                var itemSummary = await GetProjectItemSummaryAsync(projectId, startDate, endDate, voucherType, itemId, customerId);

                // Calculate separate values
                var totalSale = vouchers.Where(v => v.VoucherType == VoucherType.Sale || v.VoucherType == VoucherType.CashReceived).Sum(v => v.Amount);
                var totalStock = itemSummary.Sum(i => i.StockValue); // Total stock value from items
                var totalRevenue = totalSale + totalStock; // Revenue = Sale + Stock

                // Separate Purchase and Expense
                var totalPurchase = vouchers.Where(v => v.VoucherType == VoucherType.Purchase).Sum(v => v.Amount);
                var totalExpense = vouchers.Where(v => v.VoucherType == VoucherType.Expense ||
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
                var endDate = toDate ?? DateTimeHelper.PkToday;
                var startDate = fromDate ?? DateTimeHelper.PkToday.AddMonths(-1);

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

                // Build query for cash vouchers.
                // No Include(): the related tables are read through a projection below, which
                // fetches only the handful of columns this report actually shows.
                var query = _context.Vouchers
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

                // Only the columns the view renders leave the database — a Voucher row has 52
                // columns (several 500-char text fields) and each Include pulled whole related
                // rows; this report displays a dozen values.
                var vouchers = (await query
                    .OrderBy(v => v.VoucherDate).ThenBy(v => v.Id)
                    .Select(v => new
                    {
                        v.Id,
                        v.TransactionNumber,
                        v.VoucherDate,
                        v.VoucherType,
                        v.Amount,
                        v.ExpenseHeadDetails,
                        v.PurchasingCustomerDetails,
                        v.ReceivingCustomerDetails,
                        PurchasingCustomerName = v.PurchasingCustomer!.Name,
                        ReceivingCustomerName = v.ReceivingCustomer!.Name,
                        ItemName = v.Item!.Name,
                        ExpenseHeadName = v.ExpenseHead!.Name,
                        ProjectName = v.Project!.Name,
                        BankCustomerPaidName = v.BankCustomerPaid!.Name
                    })
                    .ToListAsync())
                    .Select(r => new Voucher
                    {
                        Id = r.Id,
                        TransactionNumber = r.TransactionNumber,
                        VoucherDate = r.VoucherDate,
                        VoucherType = r.VoucherType,
                        Amount = r.Amount,
                        ExpenseHeadDetails = r.ExpenseHeadDetails,
                        PurchasingCustomerDetails = r.PurchasingCustomerDetails,
                        ReceivingCustomerDetails = r.ReceivingCustomerDetails,
                        PurchasingCustomer = r.PurchasingCustomerName == null ? null : new Customer { Name = r.PurchasingCustomerName },
                        ReceivingCustomer = r.ReceivingCustomerName == null ? null : new Customer { Name = r.ReceivingCustomerName },
                        Item = r.ItemName == null ? null : new Item { Name = r.ItemName },
                        ExpenseHead = r.ExpenseHeadName == null ? null : new ExpenseHead { Name = r.ExpenseHeadName },
                        Project = r.ProjectName == null ? null : new Project { Name = r.ProjectName },
                        BankCustomerPaid = r.BankCustomerPaidName == null ? null : new Bank { Name = r.BankCustomerPaidName }
                    })
                    .ToList();

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
                        case VoucherType.ATMCash:   // ATM withdrawal → cash in
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
                    adjustment.CreatedDate = DateTimeHelper.PkNow;

                    // Generate reference number
                    int count = 1;
                    try { count = await _context.CashAdjustments.CountAsync() + 1; } catch { }
                    adjustment.ReferenceNumber = $"CASH-{(adjustment.AdjustmentType == CashAdjustmentType.CashIn ? "IN" : "OUT")}-{DateTimeHelper.PkNow:yyyyMMdd}-{count:D4}";

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

        // GET: Reports/ActivityLog - Show system activity (added/edited/deleted) by date
        public async Task<IActionResult> ActivityLog(DateTime? activityDate)
        {
            try
            {
                var logDate = activityDate ?? DateTimeHelper.PkToday;
                var nextDay = logDate.AddDays(1);

                // Find all vouchers created on this date (exclude deleted, but include revoked
                // so the audit log still shows them; bypass global filter then re-filter).
                var createdVouchers = (await _context.Vouchers
                    .IgnoreQueryFilters()
                    .Where(v => !v.IsDeleted)
                    .Where(v => v.CreatedDate >= logDate && v.CreatedDate < nextDay)
                    .OrderBy(v => v.CreatedDate)
                    .Select(v => new
                    {
                        v.Id,
                        v.TransactionNumber,
                        v.VoucherDate,
                        v.VoucherType,
                        v.Amount,
                        v.Quantity,
                        v.Rate,
                        v.PurchasingCustomerDetails,
                        v.ReceivingCustomerDetails,
                        v.CreatedBy,
                        v.CreatedDate,
                        v.UpdatedBy,
                        v.UpdatedDate,
                        v.DeletedBy,
                        v.DeletedDate,
                        v.RevokedBy,
                        v.RevokedDate,
                        v.RestoredBy,
                        v.RestoredDate,
                        ItemName = v.Item!.Name,
                        ProjectName = v.Project!.Name,
                        PurchasingCustomerName = v.PurchasingCustomer!.Name,
                        ReceivingCustomerName = v.ReceivingCustomer!.Name
                    })
                    .ToListAsync())
                    .Select(r => new Voucher
                    {
                        Id = r.Id,
                        TransactionNumber = r.TransactionNumber,
                        VoucherDate = r.VoucherDate,
                        VoucherType = r.VoucherType,
                        Amount = r.Amount,
                        Quantity = r.Quantity,
                        Rate = r.Rate,
                        PurchasingCustomerDetails = r.PurchasingCustomerDetails,
                        ReceivingCustomerDetails = r.ReceivingCustomerDetails,
                        CreatedBy = r.CreatedBy,
                        CreatedDate = r.CreatedDate,
                        UpdatedBy = r.UpdatedBy,
                        UpdatedDate = r.UpdatedDate,
                        DeletedBy = r.DeletedBy,
                        DeletedDate = r.DeletedDate,
                        RevokedBy = r.RevokedBy,
                        RevokedDate = r.RevokedDate,
                        RestoredBy = r.RestoredBy,
                        RestoredDate = r.RestoredDate,
                        Item = r.ItemName == null ? null : new Item { Name = r.ItemName },
                        Project = r.ProjectName == null ? null : new Project { Name = r.ProjectName },
                        PurchasingCustomer = r.PurchasingCustomerName == null ? null : new Customer { Name = r.PurchasingCustomerName },
                        ReceivingCustomer = r.ReceivingCustomerName == null ? null : new Customer { Name = r.ReceivingCustomerName }
                    })
                    .ToList();

                // Find all vouchers updated on this date
                var updatedVouchers = (await _context.Vouchers
                    .IgnoreQueryFilters()
                    .Where(v => !v.IsDeleted)
                    .Where(v => v.UpdatedDate.HasValue && v.UpdatedDate >= logDate && v.UpdatedDate < nextDay)
                    .OrderBy(v => v.UpdatedDate)
                    .Select(v => new
                    {
                        v.Id,
                        v.TransactionNumber,
                        v.VoucherDate,
                        v.VoucherType,
                        v.Amount,
                        v.Quantity,
                        v.Rate,
                        v.PurchasingCustomerDetails,
                        v.ReceivingCustomerDetails,
                        v.CreatedBy,
                        v.CreatedDate,
                        v.UpdatedBy,
                        v.UpdatedDate,
                        v.DeletedBy,
                        v.DeletedDate,
                        v.RevokedBy,
                        v.RevokedDate,
                        v.RestoredBy,
                        v.RestoredDate,
                        ItemName = v.Item!.Name,
                        ProjectName = v.Project!.Name,
                        PurchasingCustomerName = v.PurchasingCustomer!.Name,
                        ReceivingCustomerName = v.ReceivingCustomer!.Name
                    })
                    .ToListAsync())
                    .Select(r => new Voucher
                    {
                        Id = r.Id,
                        TransactionNumber = r.TransactionNumber,
                        VoucherDate = r.VoucherDate,
                        VoucherType = r.VoucherType,
                        Amount = r.Amount,
                        Quantity = r.Quantity,
                        Rate = r.Rate,
                        PurchasingCustomerDetails = r.PurchasingCustomerDetails,
                        ReceivingCustomerDetails = r.ReceivingCustomerDetails,
                        CreatedBy = r.CreatedBy,
                        CreatedDate = r.CreatedDate,
                        UpdatedBy = r.UpdatedBy,
                        UpdatedDate = r.UpdatedDate,
                        DeletedBy = r.DeletedBy,
                        DeletedDate = r.DeletedDate,
                        RevokedBy = r.RevokedBy,
                        RevokedDate = r.RevokedDate,
                        RestoredBy = r.RestoredBy,
                        RestoredDate = r.RestoredDate,
                        Item = r.ItemName == null ? null : new Item { Name = r.ItemName },
                        Project = r.ProjectName == null ? null : new Project { Name = r.ProjectName },
                        PurchasingCustomer = r.PurchasingCustomerName == null ? null : new Customer { Name = r.PurchasingCustomerName },
                        ReceivingCustomer = r.ReceivingCustomerName == null ? null : new Customer { Name = r.ReceivingCustomerName }
                    })
                    .ToList();

                // Find all vouchers deleted on this date — must bypass the global query filter
                var deletedVouchers = (await _context.Vouchers
                    .IgnoreQueryFilters()
                    .Where(v => v.IsDeleted && v.DeletedDate >= logDate && v.DeletedDate < nextDay)
                    .OrderBy(v => v.DeletedDate)
                    .Select(v => new
                    {
                        v.Id,
                        v.TransactionNumber,
                        v.VoucherDate,
                        v.VoucherType,
                        v.Amount,
                        v.Quantity,
                        v.Rate,
                        v.PurchasingCustomerDetails,
                        v.ReceivingCustomerDetails,
                        v.CreatedBy,
                        v.CreatedDate,
                        v.UpdatedBy,
                        v.UpdatedDate,
                        v.DeletedBy,
                        v.DeletedDate,
                        v.RevokedBy,
                        v.RevokedDate,
                        v.RestoredBy,
                        v.RestoredDate,
                        ItemName = v.Item!.Name,
                        ProjectName = v.Project!.Name,
                        PurchasingCustomerName = v.PurchasingCustomer!.Name,
                        ReceivingCustomerName = v.ReceivingCustomer!.Name
                    })
                    .ToListAsync())
                    .Select(r => new Voucher
                    {
                        Id = r.Id,
                        TransactionNumber = r.TransactionNumber,
                        VoucherDate = r.VoucherDate,
                        VoucherType = r.VoucherType,
                        Amount = r.Amount,
                        Quantity = r.Quantity,
                        Rate = r.Rate,
                        PurchasingCustomerDetails = r.PurchasingCustomerDetails,
                        ReceivingCustomerDetails = r.ReceivingCustomerDetails,
                        CreatedBy = r.CreatedBy,
                        CreatedDate = r.CreatedDate,
                        UpdatedBy = r.UpdatedBy,
                        UpdatedDate = r.UpdatedDate,
                        DeletedBy = r.DeletedBy,
                        DeletedDate = r.DeletedDate,
                        RevokedBy = r.RevokedBy,
                        RevokedDate = r.RevokedDate,
                        RestoredBy = r.RestoredBy,
                        RestoredDate = r.RestoredDate,
                        Item = r.ItemName == null ? null : new Item { Name = r.ItemName },
                        Project = r.ProjectName == null ? null : new Project { Name = r.ProjectName },
                        PurchasingCustomer = r.PurchasingCustomerName == null ? null : new Customer { Name = r.PurchasingCustomerName },
                        ReceivingCustomer = r.ReceivingCustomerName == null ? null : new Customer { Name = r.ReceivingCustomerName }
                    })
                    .ToList();

                // Find all vouchers revoked on this date — bypass the global filter
                var revokedVouchers = (await _context.Vouchers
                    .IgnoreQueryFilters()
                    .Where(v => v.RevokedDate.HasValue && v.RevokedDate >= logDate && v.RevokedDate < nextDay)
                    .OrderBy(v => v.RevokedDate)
                    .Select(v => new
                    {
                        v.Id,
                        v.TransactionNumber,
                        v.VoucherDate,
                        v.VoucherType,
                        v.Amount,
                        v.Quantity,
                        v.Rate,
                        v.PurchasingCustomerDetails,
                        v.ReceivingCustomerDetails,
                        v.CreatedBy,
                        v.CreatedDate,
                        v.UpdatedBy,
                        v.UpdatedDate,
                        v.DeletedBy,
                        v.DeletedDate,
                        v.RevokedBy,
                        v.RevokedDate,
                        v.RestoredBy,
                        v.RestoredDate,
                        ItemName = v.Item!.Name,
                        ProjectName = v.Project!.Name,
                        PurchasingCustomerName = v.PurchasingCustomer!.Name,
                        ReceivingCustomerName = v.ReceivingCustomer!.Name
                    })
                    .ToListAsync())
                    .Select(r => new Voucher
                    {
                        Id = r.Id,
                        TransactionNumber = r.TransactionNumber,
                        VoucherDate = r.VoucherDate,
                        VoucherType = r.VoucherType,
                        Amount = r.Amount,
                        Quantity = r.Quantity,
                        Rate = r.Rate,
                        PurchasingCustomerDetails = r.PurchasingCustomerDetails,
                        ReceivingCustomerDetails = r.ReceivingCustomerDetails,
                        CreatedBy = r.CreatedBy,
                        CreatedDate = r.CreatedDate,
                        UpdatedBy = r.UpdatedBy,
                        UpdatedDate = r.UpdatedDate,
                        DeletedBy = r.DeletedBy,
                        DeletedDate = r.DeletedDate,
                        RevokedBy = r.RevokedBy,
                        RevokedDate = r.RevokedDate,
                        RestoredBy = r.RestoredBy,
                        RestoredDate = r.RestoredDate,
                        Item = r.ItemName == null ? null : new Item { Name = r.ItemName },
                        Project = r.ProjectName == null ? null : new Project { Name = r.ProjectName },
                        PurchasingCustomer = r.PurchasingCustomerName == null ? null : new Customer { Name = r.PurchasingCustomerName },
                        ReceivingCustomer = r.ReceivingCustomerName == null ? null : new Customer { Name = r.ReceivingCustomerName }
                    })
                    .ToList();

                // Find all vouchers restored on this date — bypass the global filter
                var restoredVouchers = (await _context.Vouchers
                    .IgnoreQueryFilters()
                    .Where(v => v.RestoredDate.HasValue && v.RestoredDate >= logDate && v.RestoredDate < nextDay)
                    .OrderBy(v => v.RestoredDate)
                    .Select(v => new
                    {
                        v.Id,
                        v.TransactionNumber,
                        v.VoucherDate,
                        v.VoucherType,
                        v.Amount,
                        v.Quantity,
                        v.Rate,
                        v.PurchasingCustomerDetails,
                        v.ReceivingCustomerDetails,
                        v.CreatedBy,
                        v.CreatedDate,
                        v.UpdatedBy,
                        v.UpdatedDate,
                        v.DeletedBy,
                        v.DeletedDate,
                        v.RevokedBy,
                        v.RevokedDate,
                        v.RestoredBy,
                        v.RestoredDate,
                        ItemName = v.Item!.Name,
                        ProjectName = v.Project!.Name,
                        PurchasingCustomerName = v.PurchasingCustomer!.Name,
                        ReceivingCustomerName = v.ReceivingCustomer!.Name
                    })
                    .ToListAsync())
                    .Select(r => new Voucher
                    {
                        Id = r.Id,
                        TransactionNumber = r.TransactionNumber,
                        VoucherDate = r.VoucherDate,
                        VoucherType = r.VoucherType,
                        Amount = r.Amount,
                        Quantity = r.Quantity,
                        Rate = r.Rate,
                        PurchasingCustomerDetails = r.PurchasingCustomerDetails,
                        ReceivingCustomerDetails = r.ReceivingCustomerDetails,
                        CreatedBy = r.CreatedBy,
                        CreatedDate = r.CreatedDate,
                        UpdatedBy = r.UpdatedBy,
                        UpdatedDate = r.UpdatedDate,
                        DeletedBy = r.DeletedBy,
                        DeletedDate = r.DeletedDate,
                        RevokedBy = r.RevokedBy,
                        RevokedDate = r.RevokedDate,
                        RestoredBy = r.RestoredBy,
                        RestoredDate = r.RestoredDate,
                        Item = r.ItemName == null ? null : new Item { Name = r.ItemName },
                        Project = r.ProjectName == null ? null : new Project { Name = r.ProjectName },
                        PurchasingCustomer = r.PurchasingCustomerName == null ? null : new Customer { Name = r.PurchasingCustomerName },
                        ReceivingCustomer = r.ReceivingCustomerName == null ? null : new Customer { Name = r.ReceivingCustomerName }
                    })
                    .ToList();

                ViewBag.ActivityDate = logDate;
                ViewBag.CreatedVouchers = createdVouchers;
                ViewBag.UpdatedVouchers = updatedVouchers;
                ViewBag.DeletedVouchers = deletedVouchers;
                ViewBag.RevokedVouchers = revokedVouchers;
                ViewBag.RestoredVouchers = restoredVouchers;
                ViewBag.TotalCreated = createdVouchers.Count;
                ViewBag.TotalUpdated = updatedVouchers.Count;
                ViewBag.TotalDeleted = deletedVouchers.Count;
                ViewBag.TotalRevoked = revokedVouchers.Count;
                ViewBag.TotalRestored = restoredVouchers.Count;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating activity log");
                TempData["Error"] = "Error generating activity log.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Reports/RevokedVouchers - lists only revoked vouchers with restore option
        public async Task<IActionResult> RevokedVouchers(DateTime? fromDate, DateTime? toDate, DateTime? voucherFromDate, DateTime? voucherToDate, string? voucherType, int? customerId, string? revokedBy)
        {
            try
            {
                // Load all revoked vouchers (bypasses the global filter inside the repo method)
                var revoked = (await _voucherRepository.GetRevokedVouchersAsync()).AsEnumerable();

                // Date range filter (by RevokedDate)
                if (fromDate.HasValue)
                    revoked = revoked.Where(v => v.RevokedDate.HasValue && v.RevokedDate.Value.Date >= fromDate.Value.Date);
                if (toDate.HasValue)
                    revoked = revoked.Where(v => v.RevokedDate.HasValue && v.RevokedDate.Value.Date <= toDate.Value.Date);

                // Date range filter (by VoucherDate)
                if (voucherFromDate.HasValue)
                    revoked = revoked.Where(v => v.VoucherDate.Date >= voucherFromDate.Value.Date);
                if (voucherToDate.HasValue)
                    revoked = revoked.Where(v => v.VoucherDate.Date <= voucherToDate.Value.Date);

                // Voucher type filter
                if (!string.IsNullOrEmpty(voucherType) && Enum.TryParse<VoucherType>(voucherType, out var vType))
                    revoked = revoked.Where(v => v.VoucherType == vType);

                // Party filter
                if (customerId.HasValue)
                    revoked = revoked.Where(v => v.PurchasingCustomerId == customerId || v.ReceivingCustomerId == customerId);

                // Revoked-by filter
                if (!string.IsNullOrEmpty(revokedBy))
                    revoked = revoked.Where(v => v.RevokedBy == revokedBy);

                var list = revoked.ToList();

                // Filter dropdown data
                ViewBag.Customers = new SelectList(await _customerRepository.GetActiveCustomersAsync(), "Id", "Name", customerId);
                var revokedByUsers = (await _voucherRepository.GetRevokedVouchersAsync())
                    .Where(v => !string.IsNullOrEmpty(v.RevokedBy))
                    .Select(v => v.RevokedBy!)
                    .Distinct()
                    .OrderBy(u => u)
                    .ToList();
                ViewBag.RevokedByUsers = new SelectList(revokedByUsers, revokedBy);

                ViewBag.FromDate = fromDate;
                ViewBag.ToDate = toDate;
                ViewBag.VoucherFromDate = voucherFromDate;
                ViewBag.VoucherToDate = voucherToDate;
                ViewBag.SelectedVoucherType = voucherType;
                ViewBag.SelectedCustomerId = customerId;
                ViewBag.SelectedRevokedBy = revokedBy;
                ViewBag.RevokedVouchers = list;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating revoked vouchers report");
                TempData["Error"] = "Error generating revoked vouchers report.";
                return RedirectToAction(nameof(Index));
            }
        }

        // Helper method to get opening cash balance
        private async Task<decimal> GetCashOpeningBalanceAsync(DateTime date, int? customerId = null)
        {
            decimal balance = 0;

            // Get voucher transactions before date. Netted in the database — one number
            // back instead of every prior cash voucher.
            //   In:  Sale, CashReceived, ATMCash (withdrawal → cash in)
            //   Out: Purchase, Expense, CashPaid, Hazri
            var voucherQuery = _context.Vouchers
                .AsNoTracking()
                .Where(v => v.CashType == CashType.Cash && v.VoucherDate < date &&
                            (v.VoucherType == VoucherType.Sale ||
                             v.VoucherType == VoucherType.CashReceived ||
                             v.VoucherType == VoucherType.ATMCash ||
                             v.VoucherType == VoucherType.Purchase ||
                             v.VoucherType == VoucherType.Expense ||
                             v.VoucherType == VoucherType.CashPaid ||
                             v.VoucherType == VoucherType.Hazri));

            if (customerId.HasValue)
            {
                voucherQuery = voucherQuery.Where(v => v.PurchasingCustomerId == customerId || v.ReceivingCustomerId == customerId);
            }

            balance += await voucherQuery
                .SumAsync(v => (decimal?)(
                    v.VoucherType == VoucherType.Sale ||
                    v.VoucherType == VoucherType.CashReceived ||
                    v.VoucherType == VoucherType.ATMCash
                        ? v.Amount
                        : -v.Amount)) ?? 0m;

            // Add cash adjustments before date (only if no customer filter)
            if (!customerId.HasValue)
            {
                try
                {
                    // Netted in the database. Matches the original: CashIn adds, anything
                    // else subtracts.
                    balance += await _context.CashAdjustments
                        .AsNoTracking()
                        .Where(a => a.AdjustmentDate < date)
                        .SumAsync(a => (decimal?)(
                            a.AdjustmentType == CashAdjustmentType.CashIn ? a.Amount : -a.Amount)) ?? 0m;
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
                    $"{reportType}_Report_{DateTimeHelper.PkNow:yyyyMMddHHmmss}.xlsx");
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
                var endDate = toDate ?? DateTimeHelper.PkToday;
                var startDate = fromDate ?? DateTimeHelper.PkToday.AddDays(-30);

                // Only cash transactions (CashType = Cash) — bank and other cash types are excluded
                // in the database so just this report's rows come back. Related names are read
                // through the projection rather than Include(), so only displayed columns travel.
                var cashVouchers = (await _context.Vouchers
                    .AsNoTracking()
                    .Where(v => v.CashType == CashType.Cash &&
                                v.VoucherDate >= startDate && v.VoucherDate <= endDate.AddDays(1))
                    .Select(v => new
                    {
                        v.Id,
                        v.TransactionNumber,
                        v.VoucherDate,
                        v.VoucherType,
                        v.CashType,
                        v.Amount,
                        v.ProjectId,
                        v.ExpenseHeadDetails,
                        v.BankCustomerPaidDetails,
                        v.PurchasingCustomerDetails,
                        v.ReceivingCustomerDetails,
                        v.AdvancedPurchasingCustomerDetails,
                        v.AdvancedReceivingCustomerDetails,
                        PurchasingCustomerName = v.PurchasingCustomer!.Name,
                        ReceivingCustomerName = v.ReceivingCustomer!.Name,
                        AdvancedPurchasingCustomerName = v.AdvancedPurchasingCustomer!.Name,
                        AdvancedReceivingCustomerName = v.AdvancedReceivingCustomer!.Name,
                        ItemName = v.Item!.Name,
                        ProjectName = v.Project!.Name,
                        ExpenseHeadName = v.ExpenseHead!.Name,
                        BankCustomerPaidName = v.BankCustomerPaid!.Name
                    })
                    .ToListAsync())
                    .Select(r => new Voucher
                    {
                        Id = r.Id,
                        TransactionNumber = r.TransactionNumber,
                        VoucherDate = r.VoucherDate,
                        VoucherType = r.VoucherType,
                        CashType = r.CashType,
                        Amount = r.Amount,
                        ProjectId = r.ProjectId,
                        ExpenseHeadDetails = r.ExpenseHeadDetails,
                        BankCustomerPaidDetails = r.BankCustomerPaidDetails,
                        PurchasingCustomerDetails = r.PurchasingCustomerDetails,
                        ReceivingCustomerDetails = r.ReceivingCustomerDetails,
                        AdvancedPurchasingCustomerDetails = r.AdvancedPurchasingCustomerDetails,
                        AdvancedReceivingCustomerDetails = r.AdvancedReceivingCustomerDetails,
                        PurchasingCustomer = r.PurchasingCustomerName == null ? null : new Customer { Name = r.PurchasingCustomerName },
                        ReceivingCustomer = r.ReceivingCustomerName == null ? null : new Customer { Name = r.ReceivingCustomerName },
                        AdvancedPurchasingCustomer = r.AdvancedPurchasingCustomerName == null ? null : new Customer { Name = r.AdvancedPurchasingCustomerName },
                        AdvancedReceivingCustomer = r.AdvancedReceivingCustomerName == null ? null : new Customer { Name = r.AdvancedReceivingCustomerName },
                        Item = r.ItemName == null ? null : new Item { Name = r.ItemName },
                        Project = r.ProjectName == null ? null : new Project { Name = r.ProjectName },
                        ExpenseHead = r.ExpenseHeadName == null ? null : new ExpenseHead { Name = r.ExpenseHeadName },
                        BankCustomerPaid = r.BankCustomerPaidName == null ? null : new Bank { Name = r.BankCustomerPaidName }
                    })
                    .ToList();

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
                        case VoucherType.ATMCash:                 // ATM withdrawal → cash in
                        case VoucherType.AdvancedCashReceived:    // advance received from customer → cash in
                            cashIn += voucher.Amount;
                            break;
                        case VoucherType.Purchase:
                        case VoucherType.Expense:
                        case VoucherType.CashPaid:
                        case VoucherType.Hazri:
                        case VoucherType.AdvancedCashPaid:        // advance paid to customer → cash out
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

        // Helper method to get opening cash balance (CashType = Cash only)
        private async Task<decimal> GetOpeningCashBalanceAsync(DateTime date)
        {
            // Netted in the database.
            //   In:  Sale, CashReceived, ATMCash (withdrawal → cash in), AdvancedCashReceived
            //   Out: Purchase, Expense, CashPaid, Hazri, AdvancedCashPaid
            return await _context.Vouchers
                .AsNoTracking()
                .Where(v => v.VoucherDate < date && v.CashType == CashType.Cash &&
                            (v.VoucherType == VoucherType.Sale ||
                             v.VoucherType == VoucherType.CashReceived ||
                             v.VoucherType == VoucherType.ATMCash ||
                             v.VoucherType == VoucherType.AdvancedCashReceived ||
                             v.VoucherType == VoucherType.Purchase ||
                             v.VoucherType == VoucherType.Expense ||
                             v.VoucherType == VoucherType.CashPaid ||
                             v.VoucherType == VoucherType.Hazri ||
                             v.VoucherType == VoucherType.AdvancedCashPaid))
                .SumAsync(v => (decimal?)(
                    v.VoucherType == VoucherType.Sale ||
                    v.VoucherType == VoucherType.CashReceived ||
                    v.VoucherType == VoucherType.ATMCash ||
                    v.VoucherType == VoucherType.AdvancedCashReceived
                        ? v.Amount
                        : -v.Amount)) ?? 0m;
        }

        // Helper method to get opening stock
        private async Task<decimal> GetOpeningStockAsync(int itemId, DateTime date, int? projectId = null)
        {
            var query = _context.Vouchers
                .AsNoTracking()
                .Where(v => v.ItemId == itemId &&
                            v.VoucherDate < date &&
                            (v.VoucherType == VoucherType.Purchase || v.VoucherType == VoucherType.Sale));

            if (projectId.HasValue)
                query = query.Where(v => v.ProjectId == projectId.Value);

            // Purchases add quantity, sales subtract it — netted in the database.
            return await query
                .SumAsync(v => (decimal?)(
                    v.VoucherType == VoucherType.Purchase
                        ? (v.Quantity ?? 0)
                        : -(v.Quantity ?? 0))) ?? 0m;
        }

        // GET: Reports/DailyCashBook - Tracks cash from vouchers (CashType = DailyCashBook)
        // Mirrors the Cash Statement report (same fields and calculations) but for the DailyCashBook cash type.
        public async Task<IActionResult> DailyCashBook(DateTime? fromDate, DateTime? toDate, int? customerId, string? voucherType)
        {
            try
            {
                var endDate = toDate ?? DateTimeHelper.PkToday;
                var startDate = fromDate ?? DateTimeHelper.PkToday.AddMonths(-1);

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

                // Build query for Daily Cash Book vouchers.
                // Related tables are read through the projection below instead of Include(),
                // so only the columns this report shows travel over the wire.
                var query = _context.Vouchers
                    .Where(v => v.CashType == CashType.DailyCashBook &&
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

                var vouchers = (await query
                    .OrderBy(v => v.VoucherDate).ThenBy(v => v.Id)
                    .Select(v => new
                    {
                        v.Id,
                        v.TransactionNumber,
                        v.VoucherDate,
                        v.VoucherType,
                        v.Amount,
                        v.ExpenseHeadDetails,
                        v.BankCustomerPaidDetails,
                        v.PurchasingCustomerDetails,
                        v.ReceivingCustomerDetails,
                        PurchasingCustomerName = v.PurchasingCustomer!.Name,
                        ReceivingCustomerName = v.ReceivingCustomer!.Name,
                        ItemName = v.Item!.Name,
                        ExpenseHeadName = v.ExpenseHead!.Name,
                        ProjectName = v.Project!.Name,
                        BankCustomerPaidName = v.BankCustomerPaid!.Name
                    })
                    .ToListAsync())
                    .Select(r => new Voucher
                    {
                        Id = r.Id,
                        TransactionNumber = r.TransactionNumber,
                        VoucherDate = r.VoucherDate,
                        VoucherType = r.VoucherType,
                        Amount = r.Amount,
                        ExpenseHeadDetails = r.ExpenseHeadDetails,
                        BankCustomerPaidDetails = r.BankCustomerPaidDetails,
                        PurchasingCustomerDetails = r.PurchasingCustomerDetails,
                        ReceivingCustomerDetails = r.ReceivingCustomerDetails,
                        PurchasingCustomer = r.PurchasingCustomerName == null ? null : new Customer { Name = r.PurchasingCustomerName },
                        ReceivingCustomer = r.ReceivingCustomerName == null ? null : new Customer { Name = r.ReceivingCustomerName },
                        Item = r.ItemName == null ? null : new Item { Name = r.ItemName },
                        ExpenseHead = r.ExpenseHeadName == null ? null : new ExpenseHead { Name = r.ExpenseHeadName },
                        Project = r.ProjectName == null ? null : new Project { Name = r.ProjectName },
                        BankCustomerPaid = r.BankCustomerPaidName == null ? null : new Bank { Name = r.BankCustomerPaidName }
                    })
                    .ToList();

                // Calculate opening balance (all Daily Cash Book transactions before start date)
                var openingBalance = await GetDailyCashBookOpeningBalanceAsync(startDate, customerId);

                // Calculate totals from vouchers
                decimal totalReceipts = 0;
                decimal totalPayments = 0;

                foreach (var v in vouchers)
                {
                    switch (v.VoucherType)
                    {
                        case VoucherType.Sale:
                        case VoucherType.CashReceived:
                        case VoucherType.ATMDailyCash:   // ATM withdrawal → daily cash in
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

                ViewBag.FromDate = startDate;
                ViewBag.ToDate = endDate;
                ViewBag.ReportDate = startDate; // kept for backward compat
                ViewBag.OpeningBalance = openingBalance;
                ViewBag.TotalReceipts = totalReceipts;
                ViewBag.TotalPayments = totalPayments;
                ViewBag.ClosingBalance = openingBalance + totalReceipts - totalPayments;
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

        // Helper method to get opening Daily Cash Book balance (mirrors GetCashOpeningBalanceAsync but for CashType.DailyCashBook)
        private async Task<decimal> GetDailyCashBookOpeningBalanceAsync(DateTime date, int? customerId = null)
        {
            // Netted in the database.
            //   In:  Sale, CashReceived, ATMDailyCash (withdrawal → daily cash in)
            //   Out: Purchase, Expense, CashPaid, Hazri
            var voucherQuery = _context.Vouchers
                .AsNoTracking()
                .Where(v => v.CashType == CashType.DailyCashBook && v.VoucherDate < date &&
                            (v.VoucherType == VoucherType.Sale ||
                             v.VoucherType == VoucherType.CashReceived ||
                             v.VoucherType == VoucherType.ATMDailyCash ||
                             v.VoucherType == VoucherType.Purchase ||
                             v.VoucherType == VoucherType.Expense ||
                             v.VoucherType == VoucherType.CashPaid ||
                             v.VoucherType == VoucherType.Hazri));

            if (customerId.HasValue)
            {
                voucherQuery = voucherQuery.Where(v => v.PurchasingCustomerId == customerId || v.ReceivingCustomerId == customerId);
            }

            return await voucherQuery
                .SumAsync(v => (decimal?)(
                    v.VoucherType == VoucherType.Sale ||
                    v.VoucherType == VoucherType.CashReceived ||
                    v.VoucherType == VoucherType.ATMDailyCash
                        ? v.Amount
                        : -v.Amount)) ?? 0m;
        }

        // GET: Reports/AdvancedPaymentReport
        public async Task<IActionResult> AdvancedPaymentReport(int? customerId, DateTime? fromDate, DateTime? toDate)
        {
            try
            {
                ViewBag.Customers = new SelectList(await _customerRepository.GetActiveCustomersAsync(), "Id", "Name", customerId);

                if (!customerId.HasValue)
                    return View();

                var customer = await _customerRepository.GetByIdAsync(customerId.Value);
                if (customer == null)
                {
                    TempData["Error"] = "Customer not found.";
                    return RedirectToAction(nameof(Index));
                }

                var endDate = toDate ?? DateTimeHelper.PkToday;
                var startDate = fromDate ?? DateTimeHelper.PkToday.AddDays(-90);

                var cid = customerId.Value;

                // An advanced voucher belongs to this customer if ANY of its customer links
                // points at them. Matching only the advanced columns hid rows whose link sits on
                // the ordinary purchasing/receiving column, which is how Advanced Cash Received
                // entries went missing from this report.
                var advancedForCustomer = _context.Vouchers
                    .AsNoTracking()
                    .Where(v => (v.VoucherType == VoucherType.AdvancedPayment ||
                                 v.VoucherType == VoucherType.AdvancedCashPaid ||
                                 v.VoucherType == VoucherType.AdvancedCashReceived) &&
                               (v.AdvancedPurchasingCustomerId == cid ||
                                v.AdvancedReceivingCustomerId == cid ||
                                v.ReceivingCustomerId == cid ||
                                v.PurchasingCustomerId == cid));

                // All advanced-type vouchers for this customer in range
                // Only the columns the table renders and the totals below use
                var vouchers = (await advancedForCustomer
                    .Where(v => v.VoucherDate >= startDate &&
                                v.VoucherDate <= endDate.AddDays(1))
                    .OrderBy(v => v.VoucherDate)
                    .ThenBy(v => v.Id)
                    .Select(v => new
                    {
                        v.Id,
                        v.TransactionNumber,
                        v.VoucherDate,
                        v.VoucherType,
                        v.Amount,
                        v.ReceivingCustomerDetails,
                        v.AdvancedPurchasingCustomerDetails,
                        v.AdvancedReceivingCustomerDetails
                    })
                    .ToListAsync())
                    .Select(r => new Voucher
                    {
                        Id = r.Id,
                        TransactionNumber = r.TransactionNumber,
                        VoucherDate = r.VoucherDate,
                        VoucherType = r.VoucherType,
                        Amount = r.Amount,
                        ReceivingCustomerDetails = r.ReceivingCustomerDetails,
                        AdvancedPurchasingCustomerDetails = r.AdvancedPurchasingCustomerDetails,
                        AdvancedReceivingCustomerDetails = r.AdvancedReceivingCustomerDetails
                    })
                    .ToList();

                // Opening balance: all advanced transactions before startDate
                // Opening balance only reads type and amount
                var prevVouchers = await advancedForCustomer
                    .Where(v => v.VoucherDate < startDate)
                    .Select(v => new { v.VoucherType, v.Amount })
                    .ToListAsync();

                // Direction comes from the voucher type alone — the row is already known to
                // belong to this customer:
                //   AdvancedCashReceived / AdvancedPayment = we received → we OWE customer → (-)
                //   AdvancedCashPaid                       = we paid     → customer OWES us → (+)
                decimal openingBalance = 0;
                foreach (var v in prevVouchers)
                {
                    if (v.VoucherType == VoucherType.AdvancedCashPaid)
                        openingBalance += v.Amount;
                    else
                        openingBalance -= v.Amount;
                }

                decimal totalReceived = 0;
                decimal totalPaid = 0;

                foreach (var v in vouchers)
                {
                    if (v.VoucherType == VoucherType.AdvancedCashPaid)
                        totalPaid += v.Amount;
                    else
                        totalReceived += v.Amount;
                }

                // Received = negative (we owe), Paid = positive (customer owes us)
                decimal closingBalance = openingBalance - totalReceived + totalPaid;

                ViewBag.Customer = customer;
                ViewBag.FromDate = startDate;
                ViewBag.ToDate = endDate;
                ViewBag.OpeningBalance = openingBalance;
                ViewBag.TotalReceived = totalReceived;
                ViewBag.TotalPaid = totalPaid;
                ViewBag.ClosingBalance = closingBalance;
                ViewBag.Vouchers = vouchers;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating advanced payment report");
                TempData["Error"] = "Error generating report.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Reports/CustomerLedger
        public async Task<IActionResult> CustomerLedger(int? customerId, DateTime? fromDate, DateTime? toDate, int? itemId, string? voucherType)
        {
            try
            {
                // Default to showing last 90 days if no dates specified
                var endDate = toDate ?? DateTimeHelper.PkToday;
                var startDate = fromDate ?? DateTimeHelper.PkToday.AddDays(-90);

                if (!customerId.HasValue)
                {
                    // Show selection page
                    ViewBag.Customers = new SelectList(await _customerRepository.GetActiveCustomersAsync(), "Id", "Name");
                    ViewBag.FromDate = startDate;
                    ViewBag.ToDate = endDate;
                    return View();
                }

                var customer = await _customerRepository.GetByIdAsync(customerId.Value);
                if (customer == null)
                {
                    TempData["Error"] = "Customer not found.";
                    return RedirectToAction(nameof(Index));
                }

                // Get opening balance (transactions before start date)
                var openingBalance = await GetCustomerOpeningBalanceAsync(customerId.Value, startDate);

                // Get all transactions for the customer in the date range.
                // Related names come from the projection below instead of Include().
                var query = _context.Vouchers
                    .Where(v => (v.PurchasingCustomerId == customerId.Value ||
                                v.ReceivingCustomerId == customerId.Value) &&
                               v.VoucherDate >= startDate &&
                               v.VoucherDate <= endDate)
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

                // Only the columns the ledger table renders and the DR/CR maths below needs.
                var vouchers = (await query
                    .OrderBy(v => v.VoucherDate)
                    .ThenBy(v => v.Id)
                    .Select(v => new
                    {
                        v.Id,
                        v.TransactionNumber,
                        v.VoucherDate,
                        v.VoucherType,
                        v.Amount,
                        v.Quantity,
                        v.Rate,
                        v.Weight,
                        v.Kat,
                        v.GariNo,
                        v.PurchasingCustomerId,
                        v.ReceivingCustomerId,
                        v.PurchasingCustomerDetails,
                        v.ReceivingCustomerDetails,
                        PurchasingCustomerName = v.PurchasingCustomer!.Name,
                        ReceivingCustomerName = v.ReceivingCustomer!.Name,
                        ItemName = v.Item!.Name
                    })
                    .ToListAsync())
                    .Select(r => new Voucher
                    {
                        Id = r.Id,
                        TransactionNumber = r.TransactionNumber,
                        VoucherDate = r.VoucherDate,
                        VoucherType = r.VoucherType,
                        Amount = r.Amount,
                        Quantity = r.Quantity,
                        Rate = r.Rate,
                        Weight = r.Weight,
                        Kat = r.Kat,
                        GariNo = r.GariNo,
                        PurchasingCustomerId = r.PurchasingCustomerId,
                        ReceivingCustomerId = r.ReceivingCustomerId,
                        PurchasingCustomerDetails = r.PurchasingCustomerDetails,
                        ReceivingCustomerDetails = r.ReceivingCustomerDetails,
                        PurchasingCustomer = r.PurchasingCustomerName == null ? null : new Customer { Name = r.PurchasingCustomerName },
                        ReceivingCustomer = r.ReceivingCustomerName == null ? null : new Customer { Name = r.ReceivingCustomerName },
                        Item = r.ItemName == null ? null : new Item { Name = r.ItemName }
                    })
                    .ToList();

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
                    // CashReceived / AdvancedPayment = CR (customer paid, reduces what they owe)
                    if (voucher.ReceivingCustomerId == customerId.Value)
                    {
                        switch (voucher.VoucherType)
                        {
                            case VoucherType.Sale:
                                totalDebit += voucher.Amount;   // Sale = DR
                                break;
                            case VoucherType.CashReceived:
                            case VoucherType.CCR:
                            case VoucherType.AdvancedPayment:
                                totalCredit += voucher.Amount;  // CR
                                break;
                        }
                    }
                }

                // Calculate advanced payment net balance for this customer (all time, not date-filtered)
                // Only the four fields the loop below reads — this query is not date-limited,
                // so it grows forever; fetching whole rows was the most expensive part of the page.
                var allAdvancedVouchers = await _context.Vouchers
                    .Where(v => (v.VoucherType == VoucherType.AdvancedPayment ||
                                 v.VoucherType == VoucherType.AdvancedCashPaid ||
                                 v.VoucherType == VoucherType.AdvancedCashReceived) &&
                               (v.AdvancedPurchasingCustomerId == customerId.Value ||
                                v.AdvancedReceivingCustomerId == customerId.Value ||
                                v.ReceivingCustomerId == customerId.Value))
                    .Select(v => new
                    {
                        v.VoucherType,
                        v.Amount,
                        v.AdvancedPurchasingCustomerId,
                        v.AdvancedReceivingCustomerId,
                        v.ReceivingCustomerId
                    })
                    .ToListAsync();

                decimal advancedBalance = 0;
                foreach (var av in allAdvancedVouchers)
                {
                    // Received = we owe customer → negative
                    // Paid = customer owes us → positive
                    if ((av.VoucherType == VoucherType.AdvancedCashReceived && av.AdvancedReceivingCustomerId == customerId.Value) ||
                        (av.VoucherType == VoucherType.AdvancedPayment && av.ReceivingCustomerId == customerId.Value))
                        advancedBalance -= av.Amount;
                    else if (av.VoucherType == VoucherType.AdvancedCashPaid && av.AdvancedPurchasingCustomerId == customerId.Value)
                        advancedBalance += av.Amount;
                }

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
                ViewBag.AdvancedBalance = advancedBalance;

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
            // Both sides netted in the database — two scalar results instead of every
            // voucher row for this customer. Sign rules are unchanged.
            //   Purchase = CR (we owe them) → -,  CashPaid/CCR = DR (we paid) → +
            var purchasingSide = await _context.Vouchers
                .AsNoTracking()
                .Where(v => v.PurchasingCustomerId == customerId && v.VoucherDate < date &&
                            (v.VoucherType == VoucherType.Purchase ||
                             v.VoucherType == VoucherType.CashPaid ||
                             v.VoucherType == VoucherType.CCR))
                .SumAsync(v => (decimal?)(v.VoucherType == VoucherType.Purchase ? -v.Amount : v.Amount)) ?? 0m;

            //   Sale = DR (they owe us) → +,  CashReceived/CCR/AdvancedPayment = CR → -
            var receivingSide = await _context.Vouchers
                .AsNoTracking()
                .Where(v => v.ReceivingCustomerId == customerId && v.VoucherDate < date &&
                            (v.VoucherType == VoucherType.Sale ||
                             v.VoucherType == VoucherType.CashReceived ||
                             v.VoucherType == VoucherType.CCR ||
                             v.VoucherType == VoucherType.AdvancedPayment))
                .SumAsync(v => (decimal?)(v.VoucherType == VoucherType.Sale ? v.Amount : -v.Amount)) ?? 0m;

            return purchasingSide + receivingSide;
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

            var vouchers = (await query
                .Select(v => new
                {
                    v.ItemId,
                    v.VoucherType,
                    v.Quantity,
                    v.Amount,
                    ItemId2 = v.Item!.Id,
                    ItemName = v.Item!.Name,
                    ItemUnit = v.Item!.Unit
                })
                .ToListAsync())
                .Select(r => new Voucher
                {
                    ItemId = r.ItemId,
                    VoucherType = r.VoucherType,
                    Quantity = r.Quantity,
                    Amount = r.Amount,
                    Item = new Item { Id = r.ItemId2, Name = r.ItemName, Unit = r.ItemUnit }
                })
                .ToList();

            var itemGroups = vouchers.GroupBy(v => v.ItemId.Value);
            var summary = new List<ProjectItemSummary>();

            // Opening (pre-fromDate) purchase/sale totals for every item in this project,
            // fetched once as grouped sums. Previously this ran two full-row queries per
            // item inside the loop below — and AllProjectsReport calls this method once per
            // project, so the cost multiplied out to projects × items × 2 queries.
            var openingTotals = (await _context.Vouchers
                .AsNoTracking()
                .Where(v => v.ItemId.HasValue &&
                            v.ProjectId == projectId &&
                            v.VoucherDate < fromDate &&
                            (v.VoucherType == VoucherType.Purchase || v.VoucherType == VoucherType.Sale))
                .GroupBy(v => v.ItemId!.Value)
                .Select(g => new
                {
                    ItemId = g.Key,
                    PurchaseQty = g.Sum(v => v.VoucherType == VoucherType.Purchase ? (v.Quantity ?? 0) : 0m),
                    PurchaseAmt = g.Sum(v => v.VoucherType == VoucherType.Purchase ? v.Amount : 0m),
                    SaleQty = g.Sum(v => v.VoucherType == VoucherType.Sale ? (v.Quantity ?? 0) : 0m),
                    SaleAmt = g.Sum(v => v.VoucherType == VoucherType.Sale ? v.Amount : 0m)
                })
                .ToListAsync())
                .ToDictionary(x => x.ItemId);

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

                // Opening stock qty = purchases qty - sales qty before fromDate (scoped to this project)
                var openingStockQty = await GetOpeningStockAsync(item.Id, fromDate, projectId);

                // Opening stock amount = net cost of stock before fromDate
                openingTotals.TryGetValue(item.Id, out var opening);

                var openingTotalPurchaseQty = opening?.PurchaseQty ?? 0m;
                var openingTotalPurchaseAmt = opening?.PurchaseAmt ?? 0m;
                var openingTotalSaleQty = opening?.SaleQty ?? 0m;
                var openingTotalSaleAmt = opening?.SaleAmt ?? 0m;

                // Calculate opening stock amount based on weighted average cost method
                decimal openingStockAmount = 0;
                if (openingStockQty != 0)
                {
                    if (openingStockQty > 0)
                    {
                        // Positive stock: value at weighted average purchase cost
                        var avgPurchaseRate = openingTotalPurchaseQty > 0
                            ? openingTotalPurchaseAmt / openingTotalPurchaseQty
                            : 0;
                        openingStockAmount = openingStockQty * avgPurchaseRate;
                    }
                    else
                    {
                        // Negative stock (oversold): value at weighted average sale price (reversed)
                        // This represents the liability or commitment to supply stock
                        var avgSaleRate = openingTotalSaleQty > 0
                            ? openingTotalSaleAmt / openingTotalSaleQty
                            : 0;
                        openingStockAmount = openingStockQty * avgSaleRate;
                    }
                }

                summary.Add(new ProjectItemSummary
                {
                    ItemName = item?.Name ?? "Unknown",
                    Unit = item?.Unit ?? "",
                    OpeningStockQty = openingStockQty,
                    OpeningStockAmount = openingStockAmount,
                    PurchaseQty = purchaseQty,
                    PurchaseAmount = purchaseAmount,
                    SaleQty = saleQty,
                    SaleAmount = saleAmount,
                });
            }

            return summary.OrderBy(s => s.ItemName).ToList();
        }

        // Helper method to get opening bank balance (CashType = Bank with CashPaid/CashReceived/Expense only)
        // Opening balance = sum of all transactions strictly before the given date (starting from zero)
        private async Task<decimal> GetBankOpeningBalanceAsync(int bankId, DateTime date)
        {
            decimal balance = 0;

            var previousVouchers = await _context.Vouchers
                .Where(v => (v.BankCustomerPaidId == bankId || v.BankCustomerReceiverId == bankId) &&
                           v.VoucherDate < date &&
                           // Bank-affecting cash vouchers (CashType = Bank)
                           ((v.CashType == CashType.Bank &&
                             (v.VoucherType == VoucherType.CashPaid ||
                              v.VoucherType == VoucherType.CashReceived ||
                              v.VoucherType == VoucherType.Expense))
                            // BCR (bank-to-bank transfer) — identified by bank fields, no CashType
                            || v.VoucherType == VoucherType.BCR
                            // ATM withdrawals — money out of bank into cash / daily cash
                            || v.VoucherType == VoucherType.ATMCash
                            || v.VoucherType == VoucherType.ATMDailyCash))
                .Select(v => new { v.Amount, v.BankCustomerPaidId, v.BankCustomerReceiverId })
                .ToListAsync();

            foreach (var voucher in previousVouchers)
            {
                if (voucher.BankCustomerReceiverId == bankId)
                    balance += voucher.Amount;   // credit — money came in

                if (voucher.BankCustomerPaidId == bankId)
                    balance -= voucher.Amount;   // debit — money went out
            }
            return balance;
        }

        // GET: Reports/StockTrackReport - Track which customer purchased what and when
        public async Task<IActionResult> StockTrackReport(DateTime? fromDate, DateTime? toDate, int? itemId, int? customerId)
        {
            try
            {
                var startDate = fromDate ?? DateTimeHelper.PkToday.AddMonths(-1);
                var endDate = toDate ?? DateTimeHelper.PkToday;

                ViewBag.Items = new SelectList(await _itemRepository.GetActiveItemsAsync(), "Id", "Name", itemId);
                ViewBag.Customers = new SelectList(await _customerRepository.GetActiveCustomersAsync(), "Id", "Name", customerId);

                // Related names come from the projection below, not Include().
                var query = _context.Vouchers
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

                var transactions = (await query
                    .OrderByDescending(v => v.VoucherDate).ThenByDescending(v => v.Id)
                    .Select(v => new
                    {
                        v.Id,
                        v.TransactionNumber,
                        v.VoucherDate,
                        v.VoucherType,
                        v.Amount,
                        v.Quantity,
                        v.Rate,
                        v.PurchasingCustomerId,
                        v.ReceivingCustomerId,
                        v.PurchasingCustomerDetails,
                        v.ReceivingCustomerDetails,
                        ItemName = v.Item!.Name,
                        ProjectName = v.Project!.Name,
                        PurchasingCustomerName = v.PurchasingCustomer!.Name,
                        ReceivingCustomerName = v.ReceivingCustomer!.Name
                    })
                    .ToListAsync())
                    .Select(r => new Voucher
                    {
                        Id = r.Id,
                        TransactionNumber = r.TransactionNumber,
                        VoucherDate = r.VoucherDate,
                        VoucherType = r.VoucherType,
                        Amount = r.Amount,
                        Quantity = r.Quantity,
                        Rate = r.Rate,
                        PurchasingCustomerId = r.PurchasingCustomerId,
                        ReceivingCustomerId = r.ReceivingCustomerId,
                        PurchasingCustomerDetails = r.PurchasingCustomerDetails,
                        ReceivingCustomerDetails = r.ReceivingCustomerDetails,
                        Item = r.ItemName == null ? null : new Item { Name = r.ItemName },
                        Project = r.ProjectName == null ? null : new Project { Name = r.ProjectName },
                        PurchasingCustomer = r.PurchasingCustomerName == null ? null : new Customer { Name = r.PurchasingCustomerName },
                        ReceivingCustomer = r.ReceivingCustomerName == null ? null : new Customer { Name = r.ReceivingCustomerName }
                    })
                    .ToList();

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
        public async Task<IActionResult> AllExpensesReport(DateTime? fromDate, DateTime? toDate, int? expenseHeadId, int? projectId, string? voucherType)
        {
            try
            {
                var startDate = fromDate ?? DateTimeHelper.PkToday.AddMonths(-1);
                var endDate = toDate ?? DateTimeHelper.PkToday;

                ViewBag.ExpenseHeads = new SelectList(await _expenseHeadRepository.GetActiveExpenseHeadsAsync(), "Id", "Name", expenseHeadId);
                ViewBag.Projects = new SelectList(await _projectRepository.GetActiveProjectsAsync(), "Id", "Name", projectId);

                // Voucher type filter — only Expense, Hazri, Purchase are relevant here
                Enum.TryParse<VoucherType>(voucherType, out var selectedType);
                bool hasTypeFilter = !string.IsNullOrEmpty(voucherType) &&
                    (selectedType == VoucherType.Expense || selectedType == VoucherType.Hazri || selectedType == VoucherType.Purchase);

                // 1. Expense + Hazri vouchers (skipped entirely if the filter is "Purchase")
                var expHazVouchers = new List<Voucher>();
                if (!hasTypeFilter || selectedType == VoucherType.Expense || selectedType == VoucherType.Hazri)
                {
                    // Related names via projection instead of Include()
                    var expHazQuery = _context.Vouchers
                        .Where(v => (v.VoucherType == VoucherType.Expense || v.VoucherType == VoucherType.Hazri) &&
                                   v.VoucherDate >= startDate && v.VoucherDate <= endDate.AddDays(1));

                    if (hasTypeFilter)
                        expHazQuery = expHazQuery.Where(v => v.VoucherType == selectedType);
                    if (expenseHeadId.HasValue)
                        expHazQuery = expHazQuery.Where(v => v.ExpenseHeadId == expenseHeadId);
                    if (projectId.HasValue)
                        expHazQuery = expHazQuery.Where(v => v.ProjectId == projectId);

                    // Only the columns that build ExpenseReportRow below
                    expHazVouchers = (await expHazQuery
                        .Select(v => new
                        {
                            v.Id,
                            v.VoucherDate,
                            v.TransactionNumber,
                            v.VoucherType,
                            v.Amount,
                            v.Weight,
                            v.Quantity,
                            v.ExpenseHeadRate,
                            v.ExpenseHeadDetails,
                            v.ProjectId,
                            ExpenseHeadName = v.ExpenseHead!.Name,
                            ProjectName = v.Project!.Name
                        })
                        .ToListAsync())
                        .Select(r => new Voucher
                        {
                            Id = r.Id,
                            VoucherDate = r.VoucherDate,
                            TransactionNumber = r.TransactionNumber,
                            VoucherType = r.VoucherType,
                            Amount = r.Amount,
                            Weight = r.Weight,
                            Quantity = r.Quantity,
                            ExpenseHeadRate = r.ExpenseHeadRate,
                            ExpenseHeadDetails = r.ExpenseHeadDetails,
                            ProjectId = r.ProjectId,
                            ExpenseHead = r.ExpenseHeadName == null ? null : new ExpenseHead { Name = r.ExpenseHeadName },
                            Project = r.ProjectName == null ? null : new Project { Name = r.ProjectName }
                        })
                        .ToList();
                }

                // 2. Purchase vouchers with an expense head (skipped if the filter is Expense/Hazri)
                var purchaseVouchers = new List<Voucher>();
                if (!hasTypeFilter || selectedType == VoucherType.Purchase)
                {
                    var purchaseQuery = _context.Vouchers
                        .Where(v => v.VoucherType == VoucherType.Purchase &&
                                   v.ExpenseHeadId != null &&
                                   v.VoucherDate >= startDate && v.VoucherDate <= endDate.AddDays(1));

                    if (expenseHeadId.HasValue)
                        purchaseQuery = purchaseQuery.Where(v => v.ExpenseHeadId == expenseHeadId);
                    if (projectId.HasValue)
                        purchaseQuery = purchaseQuery.Where(v => v.ProjectId == projectId);

                    purchaseVouchers = (await purchaseQuery
                        .Select(v => new
                        {
                            v.Id,
                            v.VoucherDate,
                            v.TransactionNumber,
                            v.VoucherType,
                            v.Amount,
                            v.Weight,
                            v.Quantity,
                            v.ExpenseHeadRate,
                            v.ExpenseHeadDetails,
                            v.ProjectId,
                            ExpenseHeadName = v.ExpenseHead!.Name,
                            ProjectName = v.Project!.Name
                        })
                        .ToListAsync())
                        .Select(r => new Voucher
                        {
                            Id = r.Id,
                            VoucherDate = r.VoucherDate,
                            TransactionNumber = r.TransactionNumber,
                            VoucherType = r.VoucherType,
                            Amount = r.Amount,
                            Weight = r.Weight,
                            Quantity = r.Quantity,
                            ExpenseHeadRate = r.ExpenseHeadRate,
                            ExpenseHeadDetails = r.ExpenseHeadDetails,
                            ProjectId = r.ProjectId,
                            ExpenseHead = r.ExpenseHeadName == null ? null : new ExpenseHead { Name = r.ExpenseHeadName },
                            Project = r.ProjectName == null ? null : new Project { Name = r.ProjectName }
                        })
                        .ToList();
                }

                // 3. Build unified rows
                var rows = new List<ExpenseReportRow>();

                foreach (var v in expHazVouchers)
                {
                    rows.Add(new ExpenseReportRow
                    {
                        VoucherId         = v.Id,
                        VoucherDate       = v.VoucherDate,
                        TransactionNumber = v.TransactionNumber,
                        ExpenseHeadName   = v.ExpenseHead?.Name ?? "-",
                        Details           = v.ExpenseHeadDetails ?? "",
                        ProjectName       = v.Project?.Name,
                        ProjectId         = v.ProjectId,
                        Weight            = v.Weight,
                        Quantity          = null,
                        Rate              = null,
                        Amount            = v.Amount,
                        Source            = v.VoucherType == VoucherType.Hazri ? "Hazri" : "Expense"
                    });
                }

                foreach (var v in purchaseVouchers)
                {
                    var rate   = v.ExpenseHeadRate ?? 0;
                    var weight = v.Weight ?? 0;
                    var amount = rate * weight;

                    rows.Add(new ExpenseReportRow
                    {
                        VoucherId         = v.Id,
                        VoucherDate       = v.VoucherDate,
                        TransactionNumber = v.TransactionNumber,
                        ExpenseHeadName   = v.ExpenseHead?.Name ?? "-",
                        Details           = v.ExpenseHeadDetails ?? "",
                        ProjectName       = v.Project?.Name,
                        ProjectId         = v.ProjectId,
                        Weight            = v.Weight,
                        Quantity          = v.Quantity,
                        Rate              = v.ExpenseHeadRate,
                        Amount            = amount,
                        Source            = "Purchase"
                    });
                }

                rows = rows.OrderByDescending(r => r.VoucherDate).ThenByDescending(r => r.VoucherId).ToList();

                var expenseRows  = rows.Where(r => r.Source == "Expense").ToList();
                var hazriRows    = rows.Where(r => r.Source == "Hazri").ToList();
                var purchaseRows = rows.Where(r => r.Source == "Purchase").ToList();

                // Opening balance: net of Expense − Hazri − Purchase before startDate
                var openingBalanceQuery = _context.Vouchers
                    .Where(v => v.VoucherDate < startDate &&
                               ((v.VoucherType == VoucherType.Expense) ||
                                (v.VoucherType == VoucherType.Hazri) ||
                                (v.VoucherType == VoucherType.Purchase && v.ExpenseHeadId != null)));
                if (hasTypeFilter)
                    openingBalanceQuery = openingBalanceQuery.Where(v => v.VoucherType == selectedType);
                if (expenseHeadId.HasValue)
                    openingBalanceQuery = openingBalanceQuery.Where(v => v.ExpenseHeadId == expenseHeadId);
                if (projectId.HasValue)
                    openingBalanceQuery = openingBalanceQuery.Where(v => v.ProjectId == projectId);

                var openingVouchers = await openingBalanceQuery
                    .Select(v => new { v.VoucherType, v.Amount, v.ExpenseHeadRate, v.Weight })
                    .ToListAsync();
                var openingBalance = openingVouchers.Sum(v =>
                    v.VoucherType == VoucherType.Purchase
                        ? -((v.ExpenseHeadRate ?? 0) * (v.Weight ?? 0))
                        : v.VoucherType == VoucherType.Hazri
                            ? -v.Amount
                            : v.Amount);

                // Summary by expense head — net per head: Expense − Hazri − Purchase
                var expenseSummary = rows
                    .GroupBy(r => r.ExpenseHeadName)
                    .Select(g => new ExpenseSummaryItem
                    {
                        ExpenseHead     = g.Key,
                        ExpenseAmount   = g.Where(r => r.Source == "Expense").Sum(r => r.Amount),
                        HazriAmount     = g.Where(r => r.Source == "Hazri").Sum(r => r.Amount),
                        PurchaseAmount  = g.Where(r => r.Source == "Purchase").Sum(r => r.Amount),
                    })
                    .OrderByDescending(x => x.Total)
                    .ToList();

                var totalExpenses          = expenseRows.Sum(r => r.Amount);
                var totalHazri             = hazriRows.Sum(r => r.Amount);
                var totalPurchaseDeductions = purchaseRows.Sum(r => r.Amount);

                ViewBag.FromDate              = startDate;
                ViewBag.ToDate                = endDate;
                ViewBag.SelectedExpenseHeadId = expenseHeadId;
                ViewBag.SelectedProjectId     = projectId;
                ViewBag.SelectedVoucherType   = voucherType;
                ViewBag.Expenses              = rows;
                ViewBag.ExpenseSummary        = expenseSummary;
                ViewBag.TotalExpenses         = totalExpenses;
                ViewBag.TotalHazri            = totalHazri;
                ViewBag.TotalPurchaseDeductions = totalPurchaseDeductions;
                ViewBag.OpeningBalance        = openingBalance;
                ViewBag.NetTotal              = openingBalance + totalExpenses - totalHazri - totalPurchaseDeductions;

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
                var startDate = fromDate ?? DateTimeHelper.PkToday.AddMonths(-1);
                var endDate = toDate ?? DateTimeHelper.PkToday;

                ViewBag.ExpenseHeads = new SelectList(await _expenseHeadRepository.GetActiveExpenseHeadsAsync(), "Id", "Name", expenseHeadId);
                ViewBag.Projects = new SelectList(await _projectRepository.GetActiveProjectsAsync(), "Id", "Name", projectId);

                // 1. Dedicated Expense vouchers
                var expenseQuery = _context.Vouchers
                    .Where(v => v.VoucherType == VoucherType.Expense &&
                               v.VoucherDate >= startDate && v.VoucherDate <= endDate.AddDays(1));

                if (expenseHeadId.HasValue)
                    expenseQuery = expenseQuery.Where(v => v.ExpenseHeadId == expenseHeadId);
                if (projectId.HasValue)
                    expenseQuery = expenseQuery.Where(v => v.ProjectId == projectId);

                // Only the columns that feed ExpenseReportRow below
                var expenseVouchers = (await expenseQuery
                    .Select(v => new
                    {
                        v.Id,
                        v.VoucherDate,
                        v.TransactionNumber,
                        v.VoucherType,
                        v.Amount,
                        v.Weight,
                        v.Quantity,
                        v.ExpenseHeadRate,
                        v.ExpenseHeadDetails,
                        v.ProjectId,
                        ExpenseHeadName = v.ExpenseHead!.Name,
                        ProjectName = v.Project!.Name
                    })
                    .ToListAsync())
                    .Select(r => new Voucher
                    {
                        Id = r.Id,
                        VoucherDate = r.VoucherDate,
                        TransactionNumber = r.TransactionNumber,
                        VoucherType = r.VoucherType,
                        Amount = r.Amount,
                        Weight = r.Weight,
                        Quantity = r.Quantity,
                        ExpenseHeadRate = r.ExpenseHeadRate,
                        ExpenseHeadDetails = r.ExpenseHeadDetails,
                        ProjectId = r.ProjectId,
                        ExpenseHead = r.ExpenseHeadName == null ? null : new ExpenseHead { Name = r.ExpenseHeadName },
                        Project = r.ProjectName == null ? null : new Project { Name = r.ProjectName }
                    })
                    .ToList();

                // 2. Purchase vouchers that have an Expense Head filled
                var purchaseQuery = _context.Vouchers
                    .Where(v => v.VoucherType == VoucherType.Purchase &&
                               v.ExpenseHeadId != null &&
                               v.VoucherDate >= startDate && v.VoucherDate <= endDate.AddDays(1));

                if (expenseHeadId.HasValue)
                    purchaseQuery = purchaseQuery.Where(v => v.ExpenseHeadId == expenseHeadId);
                if (projectId.HasValue)
                    purchaseQuery = purchaseQuery.Where(v => v.ProjectId == projectId);

                var purchaseVouchers = (await purchaseQuery
                    .Select(v => new
                    {
                        v.Id,
                        v.VoucherDate,
                        v.TransactionNumber,
                        v.VoucherType,
                        v.Amount,
                        v.Weight,
                        v.Quantity,
                        v.ExpenseHeadRate,
                        v.ExpenseHeadDetails,
                        v.ProjectId,
                        ExpenseHeadName = v.ExpenseHead!.Name,
                        ProjectName = v.Project!.Name
                    })
                    .ToListAsync())
                    .Select(r => new Voucher
                    {
                        Id = r.Id,
                        VoucherDate = r.VoucherDate,
                        TransactionNumber = r.TransactionNumber,
                        VoucherType = r.VoucherType,
                        Amount = r.Amount,
                        Weight = r.Weight,
                        Quantity = r.Quantity,
                        ExpenseHeadRate = r.ExpenseHeadRate,
                        ExpenseHeadDetails = r.ExpenseHeadDetails,
                        ProjectId = r.ProjectId,
                        ExpenseHead = r.ExpenseHeadName == null ? null : new ExpenseHead { Name = r.ExpenseHeadName },
                        Project = r.ProjectName == null ? null : new Project { Name = r.ProjectName }
                    })
                    .ToList();

                // 3. Build unified rows
                var rows = new List<ExpenseReportRow>();

                foreach (var v in expenseVouchers)
                {
                    rows.Add(new ExpenseReportRow
                    {
                        VoucherId       = v.Id,
                        VoucherDate     = v.VoucherDate,
                        TransactionNumber = v.TransactionNumber,
                        ExpenseHeadName = v.ExpenseHead?.Name ?? "-",
                        Details         = v.ExpenseHeadDetails ?? "",
                        ProjectName     = v.Project?.Name,
                        ProjectId       = v.ProjectId,
                        Amount          = v.Amount,
                        Source          = "Expense"
                    });
                }

                foreach (var v in purchaseVouchers)
                {
                    var rate = v.ExpenseHeadRate ?? 0;
                    var qty  = v.Quantity ?? 0;
                    var amount = rate * qty;

                    rows.Add(new ExpenseReportRow
                    {
                        VoucherId         = v.Id,
                        VoucherDate       = v.VoucherDate,
                        TransactionNumber = v.TransactionNumber,
                        ExpenseHeadName   = v.ExpenseHead?.Name ?? "-",
                        Details           = v.ExpenseHeadDetails ?? "",
                        ProjectName       = v.Project?.Name,
                        ProjectId         = v.ProjectId,
                        Weight            = v.Weight,
                        Quantity          = v.Quantity,
                        Rate              = v.ExpenseHeadRate,
                        Amount            = amount,
                        Source            = "Purchase"
                    });
                }

                rows = rows.OrderByDescending(r => r.VoucherDate).ThenByDescending(r => r.VoucherId).ToList();

                // Summary by expense head
                var expenseSummary = rows
                    .GroupBy(r => r.ExpenseHeadName)
                    .Select(g => new ExpenseSummaryItem { ExpenseHead = g.Key, ExpenseAmount = g.Sum(r => r.Amount) })
                    .OrderByDescending(x => x.Total)
                    .ToList();

                ViewBag.FromDate = startDate;
                ViewBag.ToDate = endDate;
                ViewBag.SelectedExpenseHeadId = expenseHeadId;
                ViewBag.SelectedProjectId = projectId;
                ViewBag.Expenses = rows;
                ViewBag.ExpenseSummary = expenseSummary;
                ViewBag.TotalExpenses = rows.Sum(r => r.Amount);

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
                var startDate = fromDate ?? DateTimeHelper.PkToday.AddMonths(-1);
                var endDate = toDate ?? DateTimeHelper.PkToday;

                ViewBag.ExpenseHeads = new SelectList(await _expenseHeadRepository.GetActiveExpenseHeadsAsync(), "Id", "Name", expenseHeadId);
                ViewBag.Projects = new SelectList(await _projectRepository.GetActiveProjectsAsync(), "Id", "Name", projectId);

                var query = _context.Vouchers
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

                // Only the columns the Hazri table and its summary use
                var hazriRecords = (await query
                    .OrderByDescending(v => v.VoucherDate).ThenByDescending(v => v.Id)
                    .Select(v => new
                    {
                        v.Id,
                        v.TransactionNumber,
                        v.VoucherDate,
                        v.Amount,
                        v.ExpenseHeadDetails,
                        v.ProjectId,
                        ExpenseHeadName = v.ExpenseHead!.Name,
                        ProjectName = v.Project!.Name
                    })
                    .ToListAsync())
                    .Select(r => new Voucher
                    {
                        Id = r.Id,
                        TransactionNumber = r.TransactionNumber,
                        VoucherDate = r.VoucherDate,
                        Amount = r.Amount,
                        ExpenseHeadDetails = r.ExpenseHeadDetails,
                        ProjectId = r.ProjectId,
                        ExpenseHead = r.ExpenseHeadName == null ? null : new ExpenseHead { Name = r.ExpenseHeadName },
                        Project = r.ProjectName == null ? null : new Project { Name = r.ProjectName }
                    })
                    .ToList();

                // Group by expense head for summary
                var hazriSummary = hazriRecords
                    .GroupBy(h => h.ExpenseHead?.Name ?? "Unknown")
                    .Select(g => new ExpenseSummaryItem { ExpenseHead = g.Key, HazriAmount = g.Sum(h => h.Amount) })
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
                var startDate = fromDate ?? new DateTime(DateTimeHelper.PkToday.Year, 1, 1);
                var endDate = toDate ?? DateTimeHelper.PkToday;

                var projects = await _projectRepository.GetActiveProjectsAsync();
                var projectReports = new List<ProjectReportItem>();

                // Per-project totals in a single grouped query instead of one full voucher
                // download per project. Same date window and same voucher-type buckets as before.
                var windowEnd = endDate.AddDays(1);
                var projectTotals = (await _context.Vouchers
                    .AsNoTracking()
                    .Where(v => v.ProjectId.HasValue &&
                                v.VoucherDate >= startDate && v.VoucherDate <= windowEnd)
                    .GroupBy(v => v.ProjectId!.Value)
                    .Select(g => new
                    {
                        ProjectId = g.Key,
                        TotalSale = g.Sum(v => v.VoucherType == VoucherType.Sale ||
                                               v.VoucherType == VoucherType.CashReceived ? v.Amount : 0m),
                        Purchases = g.Sum(v => v.VoucherType == VoucherType.Purchase ? v.Amount : 0m),
                        Expenses = g.Sum(v => v.VoucherType == VoucherType.Expense ||
                                              v.VoucherType == VoucherType.Hazri ? v.Amount : 0m),
                        VoucherCount = g.Count()
                    })
                    .ToListAsync())
                    .ToDictionary(x => x.ProjectId);

                foreach (var project in projects)
                {
                    var itemSummary = await GetProjectItemSummaryAsync(project.Id, startDate, endDate);
                    var stockValue = itemSummary.Sum(i => i.StockValue);

                    projectTotals.TryGetValue(project.Id, out var totals);

                    var totalSale = totals?.TotalSale ?? 0m;
                    var revenue = totalSale + stockValue; // Revenue = Sale + CashReceived + Stock

                    var purchases = totals?.Purchases ?? 0m;
                    var expenses = totals?.Expenses ?? 0m;
                    var totalExpenses = purchases + expenses;

                    projectReports.Add(new ProjectReportItem
                    {
                        Project = project,
                        Revenue = revenue,
                        Purchases = purchases,
                        Expenses = expenses,
                        ProfitLoss = revenue - totalExpenses,
                        VoucherCount = totals?.VoucherCount ?? 0
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
                string timestamp = DateTimeHelper.PkNow.ToString("yyyyMMdd_HHmmss");
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
                var date = asOfDate ?? DateTimeHelper.PkToday.AddDays(1);

                var customers = await _customerRepository.GetActiveCustomersAsync();
                var customerReports = new List<CustomerReportItem>();

                // Use the same DR/CR logic as CustomerLedger and GetCustomerOpeningBalanceAsync:
                // Sale = DR (+), CashReceived/AdvancedPayment/CCR = CR (-)
                // Purchase = CR (-), CashPaid/CCR = DR (+)
                // Positive net = DR = customer owes us (ToReceive)
                // Negative net = CR = we owe them (ToPay)
                //
                // Both sides are grouped per customer in the database. This replaces a
                // per-customer query loop that re-downloaded the voucher history for every
                // customer on the list — the dominant cost of this report.
                var balances = new Dictionary<int, decimal>();

                var purchasingNets = await _context.Vouchers
                    .AsNoTracking()
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

                foreach (var row in purchasingNets)
                    balances[row.CustomerId] = balances.GetValueOrDefault(row.CustomerId) + row.Net;

                var receivingNets = await _context.Vouchers
                    .AsNoTracking()
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

                foreach (var row in receivingNets)
                    balances[row.CustomerId] = balances.GetValueOrDefault(row.CustomerId) + row.Net;

                foreach (var customer in customers)
                {
                    decimal balance = balances.GetValueOrDefault(customer.Id);

                    if (balance != 0)
                    {
                        customerReports.Add(new CustomerReportItem
                        {
                            Customer  = customer,
                            ToReceive = balance > 0 ? balance : 0,   // DR — they owe us
                            ToPay     = balance < 0 ? -balance : 0,  // CR — we owe them
                            NetBalance = balance
                        });
                    }
                }

                ViewBag.AsOfDate = date.AddDays(-1);
                ViewBag.CustomerReports = customerReports.OrderByDescending(c => Math.Abs(c.NetBalance)).ToList();
                ViewBag.TotalToReceive = customerReports.Where(c => c.NetBalance > 0).Sum(c => c.NetBalance);
                ViewBag.TotalToPay = customerReports.Where(c => c.NetBalance < 0).Sum(c => Math.Abs(c.NetBalance));
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

        // GET: Reports/KhataReport - Urdu Khata Detail (PDF-style)
        public async Task<IActionResult> KhataReport(int? customerId, DateTime? fromDate, DateTime? toDate)
        {
            try
            {
                var endDate = toDate ?? DateTimeHelper.PkToday;
                var startDate = fromDate ?? DateTimeHelper.PkToday.AddMonths(-1);

                ViewBag.Customers = new SelectList(await _customerRepository.GetActiveCustomersAsync(), "Id", "Name", customerId);
                ViewBag.FromDate = startDate;
                ViewBag.ToDate = endDate;

                if (!customerId.HasValue)
                    return View();

                var customer = await _customerRepository.GetByIdAsync(customerId.Value);
                if (customer == null)
                {
                    TempData["Error"] = "Customer not found.";
                    return RedirectToAction(nameof(Index));
                }

                // Opening balance before startDate
                var openingBalance = await GetCustomerOpeningBalanceAsync(customerId.Value, startDate);

                // Bill table (top) vouchers in range:
                //  - Purchase vouchers (customer is PurchasingCustomer)
                //  - CashReceived vouchers (customer is ReceivingCustomer) — shown as positive, added to the bill side
                //  - CCR vouchers where customer is the ReceivingCustomer (money received from another customer) —
                //    treated like CashReceived and added to the bill side
                var purchaseVouchers = (await _context.Vouchers
                    .Where(v =>
                        ((v.PurchasingCustomerId == customerId.Value && v.VoucherType == VoucherType.Purchase) ||
                         (v.ReceivingCustomerId == customerId.Value && v.VoucherType == VoucherType.CashReceived) ||
                         (v.ReceivingCustomerId == customerId.Value && v.VoucherType == VoucherType.CCR)) &&
                        v.VoucherDate >= startDate &&
                        v.VoucherDate <= endDate.AddDays(1))
                    .OrderBy(v => v.VoucherDate).ThenBy(v => v.Id)
                    .Select(v => new
                    {
                        v.Id,
                        v.TransactionNumber,
                        v.VoucherDate,
                        v.VoucherType,
                        v.Amount,
                        v.Quantity,
                        v.Rate,
                        v.Weight,
                        v.Kat,
                        v.Mon,
                        v.GariNo,
                        v.PurchasingCustomerDetails,
                        v.ReceivingCustomerDetails,
                        v.BankCustomerPaidDetails,
                        v.BankCustomerReceiverDetails,
                        ItemName = v.Item!.Name,
                        PurchasingCustomerName = v.PurchasingCustomer!.Name,
                        ReceivingCustomerName = v.ReceivingCustomer!.Name
                    })
                    .ToListAsync())
                    .Select(r => new Voucher
                    {
                        Id = r.Id,
                        TransactionNumber = r.TransactionNumber,
                        VoucherDate = r.VoucherDate,
                        VoucherType = r.VoucherType,
                        Amount = r.Amount,
                        Quantity = r.Quantity,
                        Rate = r.Rate,
                        Weight = r.Weight,
                        Kat = r.Kat,
                        Mon = r.Mon,
                        GariNo = r.GariNo,
                        PurchasingCustomerDetails = r.PurchasingCustomerDetails,
                        ReceivingCustomerDetails = r.ReceivingCustomerDetails,
                        BankCustomerPaidDetails = r.BankCustomerPaidDetails,
                        BankCustomerReceiverDetails = r.BankCustomerReceiverDetails,
                        Item = r.ItemName == null ? null : new Item { Name = r.ItemName },
                        PurchasingCustomer = r.PurchasingCustomerName == null ? null : new Customer { Name = r.PurchasingCustomerName },
                        ReceivingCustomer = r.ReceivingCustomerName == null ? null : new Customer { Name = r.ReceivingCustomerName }
                    })
                    .ToList();

                // Payment table (رقم ادائیگی): money we PAID to this customer:
                //  - CashPaid (CPD) where customer is the PurchasingCustomer
                //  - CCR where customer is the PurchasingCustomer (money paid to another customer)
                var paymentVouchers = (await _context.Vouchers
                    .Where(v =>
                        v.PurchasingCustomerId == customerId.Value &&
                        (v.VoucherType == VoucherType.CashPaid || v.VoucherType == VoucherType.CCR) &&
                        v.VoucherDate >= startDate &&
                        v.VoucherDate <= endDate.AddDays(1))
                    .OrderBy(v => v.VoucherDate).ThenBy(v => v.Id)
                    .Select(v => new
                    {
                        v.Id,
                        v.TransactionNumber,
                        v.VoucherDate,
                        v.VoucherType,
                        v.Amount,
                        v.Quantity,
                        v.Rate,
                        v.Weight,
                        v.Kat,
                        v.Mon,
                        v.GariNo,
                        v.PurchasingCustomerDetails,
                        v.ReceivingCustomerDetails,
                        v.BankCustomerPaidDetails,
                        v.BankCustomerReceiverDetails,
                        ItemName = v.Item!.Name,
                        PurchasingCustomerName = v.PurchasingCustomer!.Name,
                        ReceivingCustomerName = v.ReceivingCustomer!.Name
                    })
                    .ToListAsync())
                    .Select(r => new Voucher
                    {
                        Id = r.Id,
                        TransactionNumber = r.TransactionNumber,
                        VoucherDate = r.VoucherDate,
                        VoucherType = r.VoucherType,
                        Amount = r.Amount,
                        Quantity = r.Quantity,
                        Rate = r.Rate,
                        Weight = r.Weight,
                        Kat = r.Kat,
                        Mon = r.Mon,
                        GariNo = r.GariNo,
                        PurchasingCustomerDetails = r.PurchasingCustomerDetails,
                        ReceivingCustomerDetails = r.ReceivingCustomerDetails,
                        BankCustomerPaidDetails = r.BankCustomerPaidDetails,
                        BankCustomerReceiverDetails = r.BankCustomerReceiverDetails,
                        Item = r.ItemName == null ? null : new Item { Name = r.ItemName },
                        PurchasingCustomer = r.PurchasingCustomerName == null ? null : new Customer { Name = r.PurchasingCustomerName },
                        ReceivingCustomer = r.ReceivingCustomerName == null ? null : new Customer { Name = r.ReceivingCustomerName }
                    })
                    .ToList();

                // Totals
                decimal totalBillWeight = purchaseVouchers.Sum(v => v.Weight ?? 0);
                decimal totalBillKat = purchaseVouchers.Sum(v => v.Kat ?? 0);
                decimal totalBillQty = purchaseVouchers.Sum(v => v.Quantity ?? 0);
                decimal totalBillAmount = purchaseVouchers.Sum(v => v.Amount);

                decimal totalPayment = paymentVouchers.Sum(v => v.Amount);

                decimal closingBalance = openingBalance + totalBillAmount - totalPayment;

                // Advanced balance (display only, not included in calculation)
                var advancedVouchers = await _context.Vouchers
                    .Where(v => (v.VoucherType == VoucherType.AdvancedPayment ||
                                 v.VoucherType == VoucherType.AdvancedCashPaid ||
                                 v.VoucherType == VoucherType.AdvancedCashReceived) &&
                               (v.AdvancedPurchasingCustomerId == customerId.Value ||
                                v.AdvancedReceivingCustomerId == customerId.Value ||
                                v.ReceivingCustomerId == customerId.Value))
                    .Select(v => new
                    {
                        v.VoucherType,
                        v.Amount,
                        v.AdvancedPurchasingCustomerId,
                        v.AdvancedReceivingCustomerId,
                        v.ReceivingCustomerId
                    })
                    .ToListAsync();

                decimal advancedBalance = 0;
                foreach (var av in advancedVouchers)
                {
                    if ((av.VoucherType == VoucherType.AdvancedCashReceived && av.AdvancedReceivingCustomerId == customerId.Value) ||
                        (av.VoucherType == VoucherType.AdvancedPayment && av.ReceivingCustomerId == customerId.Value))
                        advancedBalance -= av.Amount;
                    else if (av.VoucherType == VoucherType.AdvancedCashPaid && av.AdvancedPurchasingCustomerId == customerId.Value)
                        advancedBalance += av.Amount;
                }

                ViewBag.Customer = customer;
                ViewBag.FromDate = startDate;
                ViewBag.ToDate = endDate;
                ViewBag.OpeningBalance = openingBalance;
                ViewBag.PurchaseVouchers = purchaseVouchers;
                ViewBag.PaymentVouchers = paymentVouchers;
                ViewBag.TotalBillWeight = totalBillWeight;
                ViewBag.TotalBillKat = totalBillKat;
                ViewBag.TotalBillQty = totalBillQty;
                ViewBag.TotalBillAmount = totalBillAmount;
                ViewBag.TotalPayment = totalPayment;
                ViewBag.ClosingBalance = closingBalance;
                ViewBag.AdvancedBalance = advancedBalance;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating khata report");
                TempData["Error"] = "Error generating khata report.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Reports/KhataReportSale - Urdu Khata Detail for SALE vouchers (PDF-style)
        // Mirrors KhataReport but from the sale side: the top table lists Sale items the
        // customer bought from us (DR / increases what they owe), and the bottom table
        // (رقم وصولی) lists money received from the customer (CR / decreases what they owe).
        public async Task<IActionResult> KhataReportSale(int? customerId, DateTime? fromDate, DateTime? toDate)
        {
            try
            {
                var endDate = toDate ?? DateTimeHelper.PkToday;
                var startDate = fromDate ?? DateTimeHelper.PkToday.AddMonths(-1);

                ViewBag.Customers = new SelectList(await _customerRepository.GetActiveCustomersAsync(), "Id", "Name", customerId);
                ViewBag.FromDate = startDate;
                ViewBag.ToDate = endDate;

                if (!customerId.HasValue)
                    return View();

                var customer = await _customerRepository.GetByIdAsync(customerId.Value);
                if (customer == null)
                {
                    TempData["Error"] = "Customer not found.";
                    return RedirectToAction(nameof(Index));
                }

                // Opening balance before startDate (same shared helper, sale side already
                // increases the balance there, so the sign is consistent for this report).
                var openingBalance = await GetCustomerOpeningBalanceAsync(customerId.Value, startDate);

                // Sale (bill) table (top) vouchers in range:
                //  - Sale vouchers (customer is ReceivingCustomer) — items the customer bought from us
                //  - CCR vouchers where customer is the PurchasingCustomer (opposite of the regular
                //    Khata report: here CCR-as-purchasing goes in the TOP table)
                var saleVouchers = (await _context.Vouchers
                    .Where(v =>
                        ((v.ReceivingCustomerId == customerId.Value && v.VoucherType == VoucherType.Sale) ||
                         (v.PurchasingCustomerId == customerId.Value && v.VoucherType == VoucherType.CCR)) &&
                        v.VoucherDate >= startDate &&
                        v.VoucherDate <= endDate.AddDays(1))
                    .OrderBy(v => v.VoucherDate).ThenBy(v => v.Id)
                    .Select(v => new
                    {
                        v.Id,
                        v.TransactionNumber,
                        v.VoucherDate,
                        v.VoucherType,
                        v.Amount,
                        v.Quantity,
                        v.Rate,
                        v.Weight,
                        v.Kat,
                        v.Mon,
                        v.GariNo,
                        v.PurchasingCustomerDetails,
                        v.ReceivingCustomerDetails,
                        v.BankCustomerPaidDetails,
                        v.BankCustomerReceiverDetails,
                        ItemName = v.Item!.Name,
                        PurchasingCustomerName = v.PurchasingCustomer!.Name,
                        ReceivingCustomerName = v.ReceivingCustomer!.Name
                    })
                    .ToListAsync())
                    .Select(r => new Voucher
                    {
                        Id = r.Id,
                        TransactionNumber = r.TransactionNumber,
                        VoucherDate = r.VoucherDate,
                        VoucherType = r.VoucherType,
                        Amount = r.Amount,
                        Quantity = r.Quantity,
                        Rate = r.Rate,
                        Weight = r.Weight,
                        Kat = r.Kat,
                        Mon = r.Mon,
                        GariNo = r.GariNo,
                        PurchasingCustomerDetails = r.PurchasingCustomerDetails,
                        ReceivingCustomerDetails = r.ReceivingCustomerDetails,
                        BankCustomerPaidDetails = r.BankCustomerPaidDetails,
                        BankCustomerReceiverDetails = r.BankCustomerReceiverDetails,
                        Item = r.ItemName == null ? null : new Item { Name = r.ItemName },
                        PurchasingCustomer = r.PurchasingCustomerName == null ? null : new Customer { Name = r.PurchasingCustomerName },
                        ReceivingCustomer = r.ReceivingCustomerName == null ? null : new Customer { Name = r.ReceivingCustomerName }
                    })
                    .ToList();

                // Receipt table (رقم وصولی): money RECEIVED from this customer:
                //  - CashReceived (CRC) where customer is the ReceivingCustomer
                //  - CCR where customer is the ReceivingCustomer (opposite of the regular Khata
                //    report: here CCR-as-receiving goes in the receipt/ادائیگی table)
                var receiptVouchers = (await _context.Vouchers
                    .Where(v =>
                        ((v.ReceivingCustomerId == customerId.Value && v.VoucherType == VoucherType.CashReceived) ||
                         (v.ReceivingCustomerId == customerId.Value && v.VoucherType == VoucherType.CCR)) &&
                        v.VoucherDate >= startDate &&
                        v.VoucherDate <= endDate.AddDays(1))
                    .OrderBy(v => v.VoucherDate).ThenBy(v => v.Id)
                    .Select(v => new
                    {
                        v.Id,
                        v.TransactionNumber,
                        v.VoucherDate,
                        v.VoucherType,
                        v.Amount,
                        v.Quantity,
                        v.Rate,
                        v.Weight,
                        v.Kat,
                        v.Mon,
                        v.GariNo,
                        v.PurchasingCustomerDetails,
                        v.ReceivingCustomerDetails,
                        v.BankCustomerPaidDetails,
                        v.BankCustomerReceiverDetails,
                        ItemName = v.Item!.Name,
                        PurchasingCustomerName = v.PurchasingCustomer!.Name,
                        ReceivingCustomerName = v.ReceivingCustomer!.Name
                    })
                    .ToListAsync())
                    .Select(r => new Voucher
                    {
                        Id = r.Id,
                        TransactionNumber = r.TransactionNumber,
                        VoucherDate = r.VoucherDate,
                        VoucherType = r.VoucherType,
                        Amount = r.Amount,
                        Quantity = r.Quantity,
                        Rate = r.Rate,
                        Weight = r.Weight,
                        Kat = r.Kat,
                        Mon = r.Mon,
                        GariNo = r.GariNo,
                        PurchasingCustomerDetails = r.PurchasingCustomerDetails,
                        ReceivingCustomerDetails = r.ReceivingCustomerDetails,
                        BankCustomerPaidDetails = r.BankCustomerPaidDetails,
                        BankCustomerReceiverDetails = r.BankCustomerReceiverDetails,
                        Item = r.ItemName == null ? null : new Item { Name = r.ItemName },
                        PurchasingCustomer = r.PurchasingCustomerName == null ? null : new Customer { Name = r.PurchasingCustomerName },
                        ReceivingCustomer = r.ReceivingCustomerName == null ? null : new Customer { Name = r.ReceivingCustomerName }
                    })
                    .ToList();

                // Totals
                decimal totalBillWeight = saleVouchers.Sum(v => v.Weight ?? 0);
                decimal totalBillKat = saleVouchers.Sum(v => v.Kat ?? 0);
                decimal totalBillQty = saleVouchers.Sum(v => v.Quantity ?? 0);
                decimal totalBillAmount = saleVouchers.Sum(v => v.Amount);

                decimal totalPayment = receiptVouchers.Sum(v => v.Amount);

                // Sale side: customer owes us for sales (DR, +), receipts reduce that (CR, -).
                decimal closingBalance = openingBalance + totalBillAmount - totalPayment;

                // Advanced balance (display only, not included in calculation)
                var advancedVouchers = await _context.Vouchers
                    .Where(v => (v.VoucherType == VoucherType.AdvancedPayment ||
                                 v.VoucherType == VoucherType.AdvancedCashPaid ||
                                 v.VoucherType == VoucherType.AdvancedCashReceived) &&
                               (v.AdvancedPurchasingCustomerId == customerId.Value ||
                                v.AdvancedReceivingCustomerId == customerId.Value ||
                                v.ReceivingCustomerId == customerId.Value))
                    .Select(v => new
                    {
                        v.VoucherType,
                        v.Amount,
                        v.AdvancedPurchasingCustomerId,
                        v.AdvancedReceivingCustomerId,
                        v.ReceivingCustomerId
                    })
                    .ToListAsync();

                decimal advancedBalance = 0;
                foreach (var av in advancedVouchers)
                {
                    if ((av.VoucherType == VoucherType.AdvancedCashReceived && av.AdvancedReceivingCustomerId == customerId.Value) ||
                        (av.VoucherType == VoucherType.AdvancedPayment && av.ReceivingCustomerId == customerId.Value))
                        advancedBalance -= av.Amount;
                    else if (av.VoucherType == VoucherType.AdvancedCashPaid && av.AdvancedPurchasingCustomerId == customerId.Value)
                        advancedBalance += av.Amount;
                }

                ViewBag.Customer = customer;
                ViewBag.FromDate = startDate;
                ViewBag.ToDate = endDate;
                ViewBag.OpeningBalance = openingBalance;
                ViewBag.PurchaseVouchers = saleVouchers;     // reuse same view bag keys as KhataReport
                ViewBag.PaymentVouchers = receiptVouchers;
                ViewBag.TotalBillWeight = totalBillWeight;
                ViewBag.TotalBillKat = totalBillKat;
                ViewBag.TotalBillQty = totalBillQty;
                ViewBag.TotalBillAmount = totalBillAmount;
                ViewBag.TotalPayment = totalPayment;
                ViewBag.ClosingBalance = closingBalance;
                ViewBag.AdvancedBalance = advancedBalance;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating khata sale report");
                TempData["Error"] = "Error generating khata sale report.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Reports/DatabaseBackup - Show backup page with all export options
    public IActionResult DatabaseBackup()
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return Forbid("Only Admin users can access backup");
        }
        return View();
    }

    // GET: Reports/ExportAllData - Export all database tables to Excel
    public async Task<IActionResult> ExportAllData(string format = "excel", string? downloadToken = null)
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return Forbid("Only Admin users can export data");
        }

        try
        {
            if (format == "sql")
            {
                var sql = await GenerateSqlBackup();
                var sqlFileName = $"DatabaseBackup_{DateTime.Now:yyyyMMdd_HHmmss}.sql";
                SetDownloadCompleteCookie(downloadToken);
                return File(Encoding.UTF8.GetBytes(sql), "application/sql", sqlFileName);
            }

            using var workbook = new XLWorkbook();

            // Export each table
            await ExportTableToWorkbook(workbook, "Banks", async () => await _context.Banks.AsNoTracking().ToListAsync());
            await ExportTableToWorkbook(workbook, "Customers", async () => await _context.Customers.AsNoTracking().ToListAsync());
            await ExportTableToWorkbook(workbook, "Items", async () => await _context.Items.AsNoTracking().ToListAsync());
            await ExportTableToWorkbook(workbook, "Projects", async () => await _context.Projects.AsNoTracking().ToListAsync());
            await ExportTableToWorkbook(workbook, "ExpenseHeads", async () => await _context.ExpenseHeads.AsNoTracking().ToListAsync());
            await ExportTableToWorkbook(workbook, "CustomerItemRates", async () => await _context.CustomerItemRates.AsNoTracking().ToListAsync());
            await ExportTableToWorkbook(workbook, "MonMultipliers", async () => await _context.MonMultipliers.AsNoTracking().ToListAsync());
            await ExportTableToWorkbook(workbook, "Users", async () => await _context.Users.AsNoTracking().ToListAsync());
            await ExportTableToWorkbook(workbook, "Vouchers", async () => await _context.Vouchers.IgnoreQueryFilters().AsNoTracking().ToListAsync());
            await ExportTableToWorkbook(workbook, "ThemeSettings", async () => await _context.ThemeSettings.AsNoTracking().ToListAsync());
            await ExportTableToWorkbook(workbook, "PageLocks", async () => await _context.PageLocks.AsNoTracking().ToListAsync());
            await ExportTableToWorkbook(workbook, "MasterPasswords", async () => await _context.MasterPasswords.AsNoTracking().ToListAsync());
            await ExportTableToWorkbook(workbook, "CashAdjustments", async () => await _context.CashAdjustments.AsNoTracking().ToListAsync());

            // Add summary sheet
            var summary = workbook.Worksheets.Add("_Summary");
            summary.Cell("A1").Value = "Database Backup Summary";
            summary.Cell("A1").Style.Font.Bold = true;
            summary.Cell("A2").Value = $"Backup Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            summary.Cell("A3").Value = "Each sheet contains a complete table with all columns and rows.";
            summary.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var fileName = $"DatabaseBackup_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            SetDownloadCompleteCookie(downloadToken);
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting all data");
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }

    // GET: Reports/ExportTable - Export individual table
    public async Task<IActionResult> ExportTable(string tableName, string? downloadToken = null)
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return Forbid("Only Admin users can export data");
        }

        try
        {
            using var workbook = new XLWorkbook();

            switch (tableName)
            {
                case "Banks":
                    await ExportTableToWorkbook(workbook, "Banks", async () => await _context.Banks.AsNoTracking().ToListAsync());
                    break;
                case "Customers":
                    await ExportTableToWorkbook(workbook, "Customers", async () => await _context.Customers.AsNoTracking().ToListAsync());
                    break;
                case "Items":
                    await ExportTableToWorkbook(workbook, "Items", async () => await _context.Items.AsNoTracking().ToListAsync());
                    break;
                case "Projects":
                    await ExportTableToWorkbook(workbook, "Projects", async () => await _context.Projects.AsNoTracking().ToListAsync());
                    break;
                case "ExpenseHeads":
                    await ExportTableToWorkbook(workbook, "ExpenseHeads", async () => await _context.ExpenseHeads.AsNoTracking().ToListAsync());
                    break;
                case "CustomerItemRates":
                    await ExportTableToWorkbook(workbook, "CustomerItemRates", async () => await _context.CustomerItemRates.AsNoTracking().ToListAsync());
                    break;
                case "MonMultipliers":
                    await ExportTableToWorkbook(workbook, "MonMultipliers", async () => await _context.MonMultipliers.AsNoTracking().ToListAsync());
                    break;
                case "Users":
                    await ExportTableToWorkbook(workbook, "Users", async () => await _context.Users.AsNoTracking().ToListAsync());
                    break;
                case "Vouchers":
                    await ExportTableToWorkbook(workbook, "Vouchers", async () => await _context.Vouchers.IgnoreQueryFilters().AsNoTracking().ToListAsync());
                    break;
                case "ThemeSettings":
                    await ExportTableToWorkbook(workbook, "ThemeSettings", async () => await _context.ThemeSettings.AsNoTracking().ToListAsync());
                    break;
                case "PageLocks":
                    await ExportTableToWorkbook(workbook, "PageLocks", async () => await _context.PageLocks.AsNoTracking().ToListAsync());
                    break;
                case "MasterPasswords":
                    await ExportTableToWorkbook(workbook, "MasterPasswords", async () => await _context.MasterPasswords.AsNoTracking().ToListAsync());
                    break;
                case "CashAdjustments":
                    await ExportTableToWorkbook(workbook, "CashAdjustments", async () => await _context.CashAdjustments.AsNoTracking().ToListAsync());
                    break;
                default:
                    return BadRequest("Unknown table");
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var fileName = $"{tableName}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            SetDownloadCompleteCookie(downloadToken);
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error exporting table: {tableName}");
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }

    // Sets a short-lived cookie so the client-side loading overlay knows the download is ready
    private void SetDownloadCompleteCookie(string? downloadToken)
    {
        if (string.IsNullOrEmpty(downloadToken)) return;
        Response.Cookies.Append("downloadToken", downloadToken, new CookieOptions
        {
            HttpOnly = false, // must be readable by JavaScript
            Expires = DateTimeOffset.Now.AddMinutes(2),
            Path = "/"
        });
    }

    // Returns true if the property is a simple/scalar type we want to export
    // (excludes navigation properties / collections, but keeps Nullable<T> like DateTime?, decimal?)
    private static bool IsExportableProperty(PropertyInfo p)
    {
        var t = p.PropertyType;
        // Unwrap Nullable<T> -> T
        var underlying = Nullable.GetUnderlyingType(t) ?? t;

        return underlying.IsPrimitive
            || underlying.IsEnum
            || underlying == typeof(string)
            || underlying == typeof(decimal)
            || underlying == typeof(DateTime)
            || underlying == typeof(DateTimeOffset)
            || underlying == typeof(TimeSpan)
            || underlying == typeof(Guid);
    }

    private async Task ExportTableToWorkbook<T>(XLWorkbook workbook, string sheetName, Func<Task<List<T>>> getData)
    {
        List<T> data;
        try
        {
            data = await getData();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Skipping table {sheetName} - could not load data");
            return;
        }

        var worksheet = workbook.Worksheets.Add(sheetName);

        // Get exportable scalar properties (INSTANCE + PUBLIC are both required)
        var props = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(IsExportableProperty)
            .ToList();

        // Headers
        for (int i = 0; i < props.Count; i++)
        {
            var cell = worksheet.Cell(1, i + 1);
            cell.Value = props[i].Name;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        // Data rows
        for (int row = 0; row < data.Count; row++)
        {
            for (int col = 0; col < props.Count; col++)
            {
                var value = props[col].GetValue(data[row]);
                SetCellValue(worksheet.Cell(row + 2, col + 1), value);
            }
        }

        // Freeze header row and add autofilter
        worksheet.SheetView.FreezeRows(1);
        if (data.Count > 0)
        {
            worksheet.Range(1, 1, data.Count + 1, props.Count).SetAutoFilter();
        }
        worksheet.Columns().AdjustToContents();
    }

    // Set a cell value with the correct native Excel type (so numbers/dates sort correctly)
    private static void SetCellValue(IXLCell cell, object? value)
    {
        if (value == null)
        {
            cell.Value = "";
            return;
        }

        switch (value)
        {
            case bool b:
                cell.Value = b;
                break;
            case DateTime dt:
                cell.Value = dt;
                cell.Style.DateFormat.Format = "yyyy-mm-dd HH:mm:ss";
                break;
            case int or long or short or byte:
                cell.Value = Convert.ToDouble(value);
                break;
            case decimal or double or float:
                cell.Value = Convert.ToDouble(value);
                break;
            default:
                cell.Value = value.ToString();
                break;
        }
    }

    // Generate real SQL INSERT statements for the whole database
    private async Task<string> GenerateSqlBackup()
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- ============================================");
        sb.AppendLine($"-- Database Backup (SQL INSERT statements)");
        sb.AppendLine($"-- Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("-- ============================================");
        sb.AppendLine();

        await AppendTableInserts(sb, "Banks", await _context.Banks.AsNoTracking().ToListAsync());
        await AppendTableInserts(sb, "Customers", await _context.Customers.AsNoTracking().ToListAsync());
        await AppendTableInserts(sb, "Items", await _context.Items.AsNoTracking().ToListAsync());
        await AppendTableInserts(sb, "Projects", await _context.Projects.AsNoTracking().ToListAsync());
        await AppendTableInserts(sb, "ExpenseHeads", await _context.ExpenseHeads.AsNoTracking().ToListAsync());
        await AppendTableInserts(sb, "CustomerItemRates", await _context.CustomerItemRates.AsNoTracking().ToListAsync());
        await AppendTableInserts(sb, "MonMultipliers", await _context.MonMultipliers.AsNoTracking().ToListAsync());
        await AppendTableInserts(sb, "Users", await _context.Users.AsNoTracking().ToListAsync());
        await AppendTableInserts(sb, "Vouchers", await _context.Vouchers.IgnoreQueryFilters().AsNoTracking().ToListAsync());
        await AppendTableInserts(sb, "ThemeSettings", await _context.ThemeSettings.AsNoTracking().ToListAsync());
        await AppendTableInserts(sb, "PageLocks", await _context.PageLocks.AsNoTracking().ToListAsync());
        await AppendTableInserts(sb, "MasterPasswords", await _context.MasterPasswords.AsNoTracking().ToListAsync());
        try
        {
            await AppendTableInserts(sb, "CashAdjustments", await _context.CashAdjustments.AsNoTracking().ToListAsync());
        }
        catch { /* table may not exist yet */ }

        return sb.ToString();
    }

    private Task AppendTableInserts<T>(StringBuilder sb, string tableName, List<T> rows)
    {
        sb.AppendLine($"-- Table: {tableName} ({rows.Count} rows)");

        if (rows.Count == 0)
        {
            sb.AppendLine($"-- (no data)");
            sb.AppendLine();
            return Task.CompletedTask;
        }

        var props = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(IsExportableProperty)
            .ToList();

        var columnList = string.Join(", ", props.Select(p => $"\"{p.Name}\""));

        foreach (var row in rows)
        {
            var values = props.Select(p => FormatSqlValue(p.GetValue(row)));
            sb.AppendLine($"INSERT INTO \"{tableName}\" ({columnList}) VALUES ({string.Join(", ", values)});");
        }
        sb.AppendLine();
        return Task.CompletedTask;
    }

    private static string FormatSqlValue(object? value)
    {
        if (value == null) return "NULL";

        switch (value)
        {
            case bool b:
                return b ? "TRUE" : "FALSE";
            case DateTime dt:
                return $"'{dt:yyyy-MM-dd HH:mm:ss}'";
            case int or long or short or byte or decimal or double or float:
                return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "NULL";
            default:
                // Strings and enums: escape single quotes
                return $"'{value.ToString()?.Replace("'", "''")}'";
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

// Helper class for project item summary
public class ProjectItemSummary
{
    public string ItemName { get; set; }
    public string Unit { get; set; }

    // Opening Stock
    public decimal OpeningStockQty { get; set; }
    public decimal OpeningStockAmount { get; set; }
    public decimal OpeningStockRate => OpeningStockQty > 0 ? OpeningStockAmount / OpeningStockQty : 0;

    // Purchase (period)
    public decimal PurchaseQty { get; set; }
    public decimal PurchaseAmount { get; set; }
    public decimal PurchaseRate => PurchaseQty > 0 ? PurchaseAmount / PurchaseQty : 0;

    // Total Qty (Opening + Purchase only — no sale involved)
    public decimal TotalQty => OpeningStockQty + PurchaseQty;
    public decimal TotalQtyAmount => OpeningStockAmount + PurchaseAmount;
    public decimal TotalQtyRate => TotalQty > 0 ? TotalQtyAmount / TotalQty : 0;

    // Sale
    public decimal SaleQty { get; set; }
    public decimal SaleAmount { get; set; }
    public decimal SaleRate => SaleQty > 0 ? SaleAmount / SaleQty : 0;

    // Stock Balance
    // Qty  = TotalQty - SaleQty
    // Rate = TotalQtyRate (avg purchase cost — stock is valued at cost, not sale price)
    // Amount = StockQty × TotalQtyRate
    public decimal StockQty => TotalQty - SaleQty;
    public decimal StockRate => TotalQtyRate;
    public decimal StockValue => StockQty * TotalQtyRate;

    // Legacy
    public decimal AvgPurchaseRate => PurchaseRate;
}

// Helper class for expense summary
public class ExpenseSummaryItem
{
    public string ExpenseHead { get; set; }
    public decimal ExpenseAmount { get; set; }   // + (Expense vouchers)
    public decimal HazriAmount { get; set; }     // − (Hazri deduction)
    public decimal PurchaseAmount { get; set; }  // − (Purchase deduction)
    public decimal Total => ExpenseAmount - HazriAmount - PurchaseAmount; // net
}

// Unified row for Expense Report (covers both Expense vouchers and Purchase vouchers with expense head)
public class ExpenseReportRow
{
    public int VoucherId { get; set; }
    public DateTime VoucherDate { get; set; }
    public string TransactionNumber { get; set; }
    public string ExpenseHeadName { get; set; }
    public string Details { get; set; }
    public string ProjectName { get; set; }
    public int? ProjectId { get; set; }
    public decimal? Weight { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? Rate { get; set; }
    public decimal Amount { get; set; }
    public string Source { get; set; } // "Expense" or "Purchase"
}
}