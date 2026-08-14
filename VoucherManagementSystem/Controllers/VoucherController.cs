using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Data;
using VoucherManagementSystem.Helpers;
using VoucherManagementSystem.Interfaces;
using VoucherManagementSystem.Models;

namespace VoucherManagementSystem.Controllers
{
    public class VouchersController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IVoucherRepository _voucherRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IItemRepository _itemRepository;
        private readonly IBankRepository _bankRepository;
        private readonly IExpenseHeadRepository _expenseHeadRepository;
        private readonly IProjectRepository _projectRepository;

        public VouchersController(
            ApplicationDbContext context,
            IVoucherRepository voucherRepository,
            ICustomerRepository customerRepository,
            IItemRepository itemRepository,
            IBankRepository bankRepository,
            IExpenseHeadRepository expenseHeadRepository,
            IProjectRepository projectRepository)
        {
            _context = context;
            _voucherRepository = voucherRepository;
            _customerRepository = customerRepository;
            _itemRepository = itemRepository;
            _bankRepository = bankRepository;
            _expenseHeadRepository = expenseHeadRepository;
            _projectRepository = projectRepository;
        }

        // Rows per page for the voucher lists. Only one page is ever fetched from the
        // database, so egress stays flat no matter how large the Vouchers table gets.
        private const int VoucherListPageSize = 25;   // Vouchers list
        private const int GeneralCreateListRows = 10; // GeneralCreate recent-entries list

        // GET: Vouchers
        public async Task<IActionResult> Index(VoucherType? voucherType, int? customerId, int? projectId, int? itemId, DateTime? fromDate, DateTime? toDate, bool? stockInclude, string? searchTransactionNo = null, int? page = null, int pageSize = VoucherListPageSize)
        {
            // Build the query — nothing is fetched until ToListAsync() below, so every
            // filter and the row limit are applied by the database. Revoked vouchers are
            // included so they appear in the list with a Restore button.
            var query = _voucherRepository.QueryVouchersIncludingRevoked();

            // Apply filters progressively
            if (voucherType.HasValue)
            {
                query = query.Where(v => v.VoucherType == voucherType.Value);
            }

            if (customerId.HasValue)
            {
                query = query.Where(v =>
                    v.PurchasingCustomerId == customerId.Value ||
                    v.ReceivingCustomerId == customerId.Value);
            }

            if (projectId.HasValue)
            {
                query = query.Where(v => v.ProjectId == projectId.Value);
            }

            if (itemId.HasValue)
            {
                query = query.Where(v => v.ItemId == itemId.Value);
            }

            if (fromDate.HasValue && toDate.HasValue)
            {
                query = query.Where(v => v.VoucherDate >= fromDate.Value && v.VoucherDate <= toDate.Value);
            }
            else if (fromDate.HasValue)
            {
                query = query.Where(v => v.VoucherDate >= fromDate.Value);
            }
            else if (toDate.HasValue)
            {
                query = query.Where(v => v.VoucherDate <= toDate.Value);
            }

            if (stockInclude.HasValue)
            {
                query = query.Where(v => v.StockInclude == stockInclude.Value);
            }

            // Transaction No. search (partial match) — runs in the database.
            var trimmedSearch = searchTransactionNo?.Trim();
            var hasSearch = !string.IsNullOrEmpty(trimmedSearch);
            if (hasSearch)
            {
                query = query.Where(v => v.TransactionNumber.Contains(trimmedSearch!));
            }

            // Paged in the database: a COUNT(*) for the pager plus Skip/Take for the rows.
            // The full table is never downloaded, whether filtered or not.
            if (pageSize < 1) pageSize = VoucherListPageSize;

            var totalRecords = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);
            if (totalPages < 1) totalPages = 1;

            var currentPage = page ?? 1;
            if (currentPage < 1) currentPage = 1;
            if (currentPage > totalPages) currentPage = totalPages;

            var vouchers = await query
                .OrderByDescending(v => v.VoucherDate)
                .Skip((currentPage - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentPage = currentPage;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalRecords = totalRecords;
            ViewBag.PageSize = pageSize;
            ViewBag.SearchTransactionNo = trimmedSearch;

            ViewBag.Customers = new SelectList(await _customerRepository.GetActiveCustomersAsync(), "Id", "Name", customerId);
            ViewBag.Projects = new SelectList(await _projectRepository.GetActiveProjectsAsync(), "Id", "Name", projectId);
            ViewBag.Items = new SelectList(await _itemRepository.GetActiveItemsAsync(), "Id", "Name", itemId);
            ViewBag.VoucherType = voucherType;
            ViewBag.CustomerId = customerId;
            ViewBag.ProjectId = projectId;
            ViewBag.ItemId = itemId;
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            ViewBag.StockInclude = stockInclude;

            return View(vouchers.ToList());
        }

        // GET: Vouchers/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var voucher = await _context.Vouchers
                .Include(v => v.PurchasingCustomer)
                .Include(v => v.ReceivingCustomer)
                .Include(v => v.BankCustomerPaid)
                .Include(v => v.BankCustomerReceiver)
                .Include(v => v.Item)
                .Include(v => v.ExpenseHead)
                .Include(v => v.Project)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (voucher == null)
            {
                return NotFound();
            }

            return View(voucher);
        }

        // GET: Vouchers/GeneralCreate
        public async Task<IActionResult> GeneralCreate(int? page = null, int pageSize = GeneralCreateListRows, string? searchTransactionNo = null)
        {
            var voucher = new Voucher
            {
                VoucherType = VoucherType.Purchase,
                VoucherDate = DateTimeHelper.PkNow
            };

            // Full pagination is preserved, but the whole table is no longer downloaded to
            // produce it: the row count is a COUNT(*) and only the current page's rows are
            // fetched via Skip/Take in the database.
            // Revoked vouchers are included so they remain visible with a Restore button.
            var query = _voucherRepository.QueryVouchersIncludingRevoked();

            // Transaction No. search (partial match) — runs in the database against the
            // full table, so older vouchers are reachable without loading everything.
            var trimmedSearch = searchTransactionNo?.Trim();
            var hasSearch = !string.IsNullOrEmpty(trimmedSearch);
            if (hasSearch)
            {
                query = query.Where(v => v.TransactionNumber.Contains(trimmedSearch!));
            }

            if (pageSize < 1) pageSize = GeneralCreateListRows;

            var totalRecords = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);

            // Ensure at least 1 page
            if (totalPages < 1) totalPages = 1;

            // If no page specified, show the last page (most recent entries)
            var currentPage = page ?? totalPages;
            if (currentPage < 1) currentPage = 1;
            if (currentPage > totalPages) currentPage = totalPages;

            // Ordered oldest-first, exactly as the list has always been displayed.
            var voucherList = await query
                .OrderBy(v => v.CreatedDate)
                .Skip((currentPage - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.VoucherList = voucherList;
            ViewBag.CurrentPage = currentPage;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalRecords = totalRecords;
            ViewBag.PageSize = pageSize;
            ViewBag.SearchTransactionNo = trimmedSearch;

            await PrepareViewBags();
            return View(voucher);
        }

        // GET: Vouchers/Create
        public async Task<IActionResult> Create(VoucherType? type, int page = 1, int pageSize = 10)
        {
            var voucher = new Voucher
            {
                VoucherType = type ?? VoucherType.Purchase,
                VoucherDate = DateTimeHelper.PkNow
            };

            // Recent vouchers of this type. Filtering, counting and paging all run in the
            // database — only the current page's rows are transferred (this used to pull
            // every voucher of every type and page it in memory).
            var voucherType = type ?? VoucherType.Purchase;
            var typeQuery = _context.Vouchers
                .AsNoTracking()
                .AsSplitQuery()
                .Include(v => v.PurchasingCustomer)
                .Include(v => v.ReceivingCustomer)
                .Include(v => v.BankCustomerPaid)
                .Include(v => v.BankCustomerReceiver)
                .Include(v => v.Item)
                .Include(v => v.ExpenseHead)
                .Include(v => v.Project)
                .Where(v => v.VoucherType == voucherType);

            var totalRecords = await typeQuery.CountAsync();
            var totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);

            if (page < 1) page = 1;

            var voucherList = await typeQuery
                .OrderBy(v => v.CreatedDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.VoucherList = voucherList;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalRecords = totalRecords;
            ViewBag.PageSize = pageSize;

            await PrepareViewBags();
            return View(voucher);
        }

        // POST: Vouchers/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Voucher voucher, bool returnToGeneral = false)
        {
            try
            {
                // Validate Project is required for Purchase, Sale, Expense, and Hazri
                if ((voucher.VoucherType == VoucherType.Purchase ||
                     voucher.VoucherType == VoucherType.Sale ||
                     voucher.VoucherType == VoucherType.Expense ||
                     voucher.VoucherType == VoucherType.Hazri) &&
                    !voucher.ProjectId.HasValue)
                {
                    TempData["Error"] = "Project is required for " + voucher.VoucherType + " vouchers. Please select a project.";
                    await PrepareViewBags();
                    return View(voucher);
                }

                // For AdvancedCashPaid/Received, route to dedicated save actions
                if (voucher.VoucherType == VoucherType.AdvancedCashPaid)
                    return await SaveAdvancedCashPaid(voucher);
                if (voucher.VoucherType == VoucherType.AdvancedCashReceived)
                    return await SaveAdvancedCashReceived(voucher);

                // ATM vouchers: money is withdrawn from a bank into cash / daily cash.
                // Force the CashType so the correct cash report picks it up.
                if (voucher.VoucherType == VoucherType.ATMCash)
                    voucher.CashType = CashType.Cash;
                else if (voucher.VoucherType == VoucherType.ATMDailyCash)
                    voucher.CashType = CashType.DailyCashBook;

                // Generate transaction number
                voucher.TransactionNumber = await _voucherRepository.GenerateTransactionNumberAsync(voucher.VoucherType);
                voucher.CreatedBy = HttpContext.Session.GetString("Username") ?? "admin";

                // Calculate amount if not provided
                if (voucher.Quantity.HasValue && voucher.Rate.HasValue && voucher.Amount == 0)
                {
                    voucher.Amount = voucher.Quantity.Value * voucher.Rate.Value;
                }

                // Handle stock updates for Purchase and Sale - ONLY if StockInclude is true
                if (voucher.ItemId.HasValue && voucher.Quantity.HasValue && voucher.StockInclude)
                {
                    if (voucher.VoucherType == VoucherType.Purchase)
                    {
                        await _itemRepository.UpdateStockAsync(voucher.ItemId.Value, voucher.Quantity.Value, true);
                    }
                    else if (voucher.VoucherType == VoucherType.Sale)
                    {
                        await _itemRepository.UpdateStockAsync(voucher.ItemId.Value, voucher.Quantity.Value, false);
                    }
                }

                // Handle bank balance updates
                if (voucher.BankCustomerPaidId.HasValue)
                {
                    await _bankRepository.UpdateBalanceAsync(voucher.BankCustomerPaidId.Value, voucher.Amount, false);
                }
                if (voucher.BankCustomerReceiverId.HasValue)
                {
                    await _bankRepository.UpdateBalanceAsync(voucher.BankCustomerReceiverId.Value, voucher.Amount, true);
                }

                await _voucherRepository.AddAsync(voucher);
                TempData["Success"] = "Voucher created successfully!";

                // Check if should return to GeneralCreate page
                if (returnToGeneral)
                {
                    return RedirectToAction(nameof(GeneralCreate));
                }

                return RedirectToAction(nameof(Create), new { type = voucher.VoucherType });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Error creating voucher: {ex.Message}");
            }

            await PrepareViewBags();

            // Check if should return to GeneralCreate page for error handling
            if (returnToGeneral)
            {
                return View("GeneralCreate", voucher);
            }

            return View(voucher);
        }

        // GET: Vouchers/Edit/5
        public async Task<IActionResult> Edit(int? id, bool returnToGeneral = false)
        {
            if (id == null)
            {
                return NotFound();
            }

            var voucher = await _voucherRepository.GetByIdAsync(id.Value);
            if (voucher == null)
            {
                return NotFound();
            }

            ViewBag.ReturnToGeneral = returnToGeneral;
            await PrepareViewBags();
            return View(voucher);
        }

        // POST: Vouchers/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Voucher voucher)
        {
            if (id != voucher.Id)
            {
                return NotFound();
            }

            try
            {
                // Validate Project is required for Purchase, Sale, Expense, and Hazri
                if ((voucher.VoucherType == VoucherType.Purchase ||
                     voucher.VoucherType == VoucherType.Sale ||
                     voucher.VoucherType == VoucherType.Expense ||
                     voucher.VoucherType == VoucherType.Hazri) &&
                    !voucher.ProjectId.HasValue)
                {
                    TempData["Error"] = "Project is required for " + voucher.VoucherType + " vouchers. Please select a project.";
                    await PrepareViewBags();
                    return View(voucher);
                }

                // ATM vouchers: keep CashType consistent so reports pick them up correctly
                if (voucher.VoucherType == VoucherType.ATMCash)
                    voucher.CashType = CashType.Cash;
                else if (voucher.VoucherType == VoucherType.ATMDailyCash)
                    voucher.CashType = CashType.DailyCashBook;

                // Load the stored row WITH tracking, so the edit can be applied field by field.
                // The previous code called _dbSet.Update() on the form-bound object, which writes
                // EVERY column — so anything the Edit form does not post (the advanced-customer
                // links, the delete/revoke audit trail) was silently blanked out on every edit.
                // That is what removed Advanced Cash Received vouchers from the advanced reports.
                var originalVoucher = await _context.Vouchers
                    .IgnoreQueryFilters()
                    .AsTracking()
                    .FirstOrDefaultAsync(v => v.Id == id);

                if (originalVoucher == null)
                {
                    return NotFound();
                }

                // A revoked or deleted voucher currently has NO effect on stock or bank balances
                // (they were reversed when it was revoked/deleted), so an edit must not reverse
                // them again nor re-apply them. Restoring it re-applies the edited values.
                var affectsBalances = !originalVoucher.IsRevoked && !originalVoucher.IsDeleted;

                // Snapshot the stored values needed to reverse the original stock/bank effects,
                // before any of them are overwritten below.
                var origVoucherType = originalVoucher.VoucherType;
                var origItemId = originalVoucher.ItemId;
                var origQuantity = originalVoucher.Quantity;
                var origStockInclude = originalVoucher.StockInclude;
                var origBankPaidId = originalVoucher.BankCustomerPaidId;
                var origBankReceiverId = originalVoucher.BankCustomerReceiverId;
                var origAmount = originalVoucher.Amount;

                if (affectsBalances)
                {
                    // Reverse original stock changes - ONLY if StockInclude was true
                    if (origItemId.HasValue && origQuantity.HasValue && origStockInclude)
                    {
                        if (origVoucherType == VoucherType.Purchase)
                        {
                            await _itemRepository.UpdateStockAsync(origItemId.Value, origQuantity.Value, false);
                        }
                        else if (origVoucherType == VoucherType.Sale)
                        {
                            await _itemRepository.UpdateStockAsync(origItemId.Value, origQuantity.Value, true);
                        }
                    }

                    // Reverse original bank changes
                    if (origBankPaidId.HasValue)
                    {
                        await _bankRepository.UpdateBalanceAsync(origBankPaidId.Value, origAmount, true);
                    }
                    if (origBankReceiverId.HasValue)
                    {
                        await _bankRepository.UpdateBalanceAsync(origBankReceiverId.Value, origAmount, false);
                    }
                }

                // Apply new changes
                if (voucher.Quantity.HasValue && voucher.Rate.HasValue && voucher.Amount == 0)
                {
                    voucher.Amount = voucher.Quantity.Value * voucher.Rate.Value;
                }

                if (affectsBalances)
                {
                    // Apply new stock changes - ONLY if StockInclude is true
                    if (voucher.ItemId.HasValue && voucher.Quantity.HasValue && voucher.StockInclude)
                    {
                        if (voucher.VoucherType == VoucherType.Purchase)
                        {
                            await _itemRepository.UpdateStockAsync(voucher.ItemId.Value, voucher.Quantity.Value, true);
                        }
                        else if (voucher.VoucherType == VoucherType.Sale)
                        {
                            await _itemRepository.UpdateStockAsync(voucher.ItemId.Value, voucher.Quantity.Value, false);
                        }
                    }

                    // Apply new bank changes
                    if (voucher.BankCustomerPaidId.HasValue)
                    {
                        await _bankRepository.UpdateBalanceAsync(voucher.BankCustomerPaidId.Value, voucher.Amount, false);
                    }
                    if (voucher.BankCustomerReceiverId.HasValue)
                    {
                        await _bankRepository.UpdateBalanceAsync(voucher.BankCustomerReceiverId.Value, voucher.Amount, true);
                    }
                }

                // Copy ONLY the fields the Edit form posts onto the stored row. Everything else
                // (IsDeleted / IsRevoked and their audit columns, plus any field this particular
                // voucher type does not display) keeps its stored value.
                ApplyEditableFields(originalVoucher, voucher);

                // Set updated by and updated date
                originalVoucher.UpdatedBy = HttpContext.Session.GetString("Username") ?? "admin";
                originalVoucher.UpdatedDate = DateTimeHelper.PkNow;

                await _context.SaveChangesAsync();
                TempData["Success"] = "Voucher updated successfully!";

                // Check if came from GeneralCreate page
                if (Request.Form["returnToGeneral"] == "true")
                {
                    return RedirectToAction(nameof(GeneralCreate));
                }

                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _voucherRepository.ExistsAsync(voucher.Id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            await PrepareViewBags();
            return View(voucher);
        }

        // GET: Vouchers/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var voucher = await _context.Vouchers
                .Include(v => v.PurchasingCustomer)
                .Include(v => v.ReceivingCustomer)
                .Include(v => v.Item)
                .Include(v => v.Project)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (voucher == null)
            {
                return NotFound();
            }

            return View(voucher);
        }

        // POST: Vouchers/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var voucher = await _voucherRepository.GetByIdAsync(id);
            if (voucher != null)
            {
                // Reverse stock changes - ONLY if StockInclude was true
                if (voucher.ItemId.HasValue && voucher.Quantity.HasValue && voucher.StockInclude)
                {
                    if (voucher.VoucherType == VoucherType.Purchase)
                    {
                        await _itemRepository.UpdateStockAsync(voucher.ItemId.Value, voucher.Quantity.Value, false);
                    }
                    else if (voucher.VoucherType == VoucherType.Sale)
                    {
                        await _itemRepository.UpdateStockAsync(voucher.ItemId.Value, voucher.Quantity.Value, true);
                    }
                }

                // Reverse bank changes
                if (voucher.BankCustomerPaidId.HasValue)
                {
                    await _bankRepository.UpdateBalanceAsync(voucher.BankCustomerPaidId.Value, voucher.Amount, true);
                }
                if (voucher.BankCustomerReceiverId.HasValue)
                {
                    await _bankRepository.UpdateBalanceAsync(voucher.BankCustomerReceiverId.Value, voucher.Amount, false);
                }

                // Soft delete: flag the voucher instead of removing it
                voucher.IsDeleted = true;
                voucher.DeletedDate = DateTimeHelper.PkNow;
                voucher.DeletedBy = HttpContext.Session.GetString("Username") ?? "admin";
                await _voucherRepository.UpdateAsync(voucher);
                TempData["Success"] = "Voucher deleted successfully!";
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: Vouchers/Revoke/5
        // Temporarily removes a voucher's effect from the ENTIRE system (stock, cash, bank,
        // ledgers, reports, dashboard) without deleting it. The row stays in the DB and is
        // hidden everywhere by the global query filter (!IsRevoked). Can be restored later.
        [HttpPost]
        public async Task<IActionResult> Revoke(int id)
        {
            // Use IgnoreQueryFilters in case of any edge state; FindAsync also bypasses filters.
            var voucher = await _context.Vouchers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(v => v.Id == id);

            if (voucher == null)
                return Json(new { success = false, message = "Voucher not found." });

            if (voucher.IsRevoked)
                return Json(new { success = false, message = "Voucher is already revoked." });

            // Reverse stock changes (same as delete) - ONLY if StockInclude was true
            if (voucher.ItemId.HasValue && voucher.Quantity.HasValue && voucher.StockInclude)
            {
                if (voucher.VoucherType == VoucherType.Purchase)
                    await _itemRepository.UpdateStockAsync(voucher.ItemId.Value, voucher.Quantity.Value, false);
                else if (voucher.VoucherType == VoucherType.Sale)
                    await _itemRepository.UpdateStockAsync(voucher.ItemId.Value, voucher.Quantity.Value, true);
            }

            // Reverse bank balance changes (same as delete)
            if (voucher.BankCustomerPaidId.HasValue)
                await _bankRepository.UpdateBalanceAsync(voucher.BankCustomerPaidId.Value, voucher.Amount, true);
            if (voucher.BankCustomerReceiverId.HasValue)
                await _bankRepository.UpdateBalanceAsync(voucher.BankCustomerReceiverId.Value, voucher.Amount, false);

            // Flag as revoked + audit trail
            voucher.IsRevoked = true;
            voucher.RevokedDate = DateTimeHelper.PkNow;
            voucher.RevokedBy = HttpContext.Session.GetString("Username") ?? "admin";

            _context.Vouchers.Update(voucher);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Voucher revoked successfully. It no longer affects any report or balance." });
        }

        // POST: Vouchers/Restore/5
        // Re-activates a revoked voucher and re-applies ALL of its original stock, cash,
        // bank, ledger and reporting effects, exactly as before it was revoked.
        [HttpPost]
        public async Task<IActionResult> Restore(int id)
        {
            var voucher = await _context.Vouchers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(v => v.Id == id);

            if (voucher == null)
                return Json(new { success = false, message = "Voucher not found." });

            if (!voucher.IsRevoked)
                return Json(new { success = false, message = "Voucher is not revoked." });

            // Re-apply stock changes (same as create) - ONLY if StockInclude is true
            if (voucher.ItemId.HasValue && voucher.Quantity.HasValue && voucher.StockInclude)
            {
                if (voucher.VoucherType == VoucherType.Purchase)
                    await _itemRepository.UpdateStockAsync(voucher.ItemId.Value, voucher.Quantity.Value, true);
                else if (voucher.VoucherType == VoucherType.Sale)
                    await _itemRepository.UpdateStockAsync(voucher.ItemId.Value, voucher.Quantity.Value, false);
            }

            // Re-apply bank balance changes (same as create)
            if (voucher.BankCustomerPaidId.HasValue)
                await _bankRepository.UpdateBalanceAsync(voucher.BankCustomerPaidId.Value, voucher.Amount, false);
            if (voucher.BankCustomerReceiverId.HasValue)
                await _bankRepository.UpdateBalanceAsync(voucher.BankCustomerReceiverId.Value, voucher.Amount, true);

            // Clear revoked flag + audit trail
            voucher.IsRevoked = false;
            voucher.RestoredDate = DateTimeHelper.PkNow;
            voucher.RestoredBy = HttpContext.Session.GetString("Username") ?? "admin";

            _context.Vouchers.Update(voucher);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Voucher restored successfully. All effects re-applied." });
        }

        // GET: Vouchers/AdvancedPayment
        public async Task<IActionResult> AdvancedPayment(int? customerId)
        {
            var voucher = new Voucher
            {
                VoucherType = VoucherType.AdvancedPayment,
                VoucherDate = DateTimeHelper.PkToday,
                CashType = CashType.Cash
            };

            ViewBag.PreSelectedCustomerId = customerId;
            ViewBag.Customers = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                await _customerRepository.GetActiveCustomersAsync(), "Id", "Name", customerId);
            ViewBag.Banks = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                await _bankRepository.GetActiveBanksAsync(), "Id", "Name");
            return View(voucher);
        }

        // POST: Vouchers/SaveAdvancedPayment
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveAdvancedPayment(Voucher voucher)
        {
            try
            {
                if (!voucher.ReceivingCustomerId.HasValue)
                {
                    TempData["Error"] = "Please select a customer.";
                    ViewBag.Customers = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                        await _customerRepository.GetActiveCustomersAsync(), "Id", "Name");
                    ViewBag.Banks = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                        await _bankRepository.GetActiveBanksAsync(), "Id", "Name");
                    return View("AdvancedPayment", voucher);
                }

                voucher.VoucherType = VoucherType.AdvancedPayment;
                voucher.TransactionNumber = await _voucherRepository.GenerateTransactionNumberAsync(VoucherType.AdvancedPayment);
                voucher.CreatedBy = HttpContext.Session.GetString("Username") ?? "admin";
                voucher.CreatedDate = DateTimeHelper.PkNow;

                // Update bank balance if paid into a bank account
                if (voucher.BankCustomerReceiverId.HasValue && voucher.CashType == CashType.Bank)
                {
                    await _bankRepository.UpdateBalanceAsync(voucher.BankCustomerReceiverId.Value, voucher.Amount, true);
                }

                await _voucherRepository.AddAsync(voucher);
                TempData["Success"] = $"Advanced payment of Rs. {voucher.Amount:N0} recorded successfully!";
                return RedirectToAction("CustomerLedger", "Reports", new { customerId = voucher.ReceivingCustomerId });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Error saving advanced payment: {ex.Message}");
            }

            ViewBag.Customers = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                await _customerRepository.GetActiveCustomersAsync(), "Id", "Name");
            ViewBag.Banks = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                await _bankRepository.GetActiveBanksAsync(), "Id", "Name");
            return View("AdvancedPayment", voucher);
        }

        // GET: Vouchers/AdvancedCashPaid
        public async Task<IActionResult> AdvancedCashPaid(int page = 1, int pageSize = 10)
        {
            var voucher = new Voucher
            {
                VoucherType = VoucherType.AdvancedCashPaid,
                VoucherDate = DateTimeHelper.PkToday,
                CashType = CashType.Cash
            };

            // Count and page in the database — only the current page's rows are fetched.
            var advPaidQuery = _context.Vouchers
                .AsNoTracking()
                .Include(v => v.AdvancedPurchasingCustomer)
                .Include(v => v.BankCustomerPaid)
                .Where(v => v.VoucherType == VoucherType.AdvancedCashPaid);

            var totalRecords = await advPaidQuery.CountAsync();
            var totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);
            if (totalPages < 1) totalPages = 1;
            var currentPage = Math.Max(1, Math.Min(page, totalPages));

            ViewBag.VoucherList = await advPaidQuery
                .OrderByDescending(v => v.VoucherDate).ThenByDescending(v => v.Id)
                .Skip((currentPage - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            ViewBag.CurrentPage = currentPage;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalRecords = totalRecords;
            ViewBag.PageSize = pageSize;

            ViewBag.Customers = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                await _customerRepository.GetActiveCustomersAsync(), "Id", "Name");
            ViewBag.Banks = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                await _bankRepository.GetActiveBanksAsync(), "Id", "Name");
            return View(voucher);
        }

        // POST: Vouchers/SaveAdvancedCashPaid
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveAdvancedCashPaid(Voucher voucher)
        {
            try
            {
                if (!voucher.AdvancedPurchasingCustomerId.HasValue)
                {
                    TempData["Error"] = "Please select a customer.";
                    ViewBag.Customers = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                        await _customerRepository.GetActiveCustomersAsync(), "Id", "Name");
                    ViewBag.Banks = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                        await _bankRepository.GetActiveBanksAsync(), "Id", "Name");
                    return View("AdvancedCashPaid", voucher);
                }

                voucher.VoucherType = VoucherType.AdvancedCashPaid;
                voucher.TransactionNumber = await _voucherRepository.GenerateTransactionNumberAsync(VoucherType.AdvancedCashPaid);
                voucher.CreatedBy = HttpContext.Session.GetString("Username") ?? "admin";
                voucher.CreatedDate = DateTimeHelper.PkNow;

                // Deduct from bank if paid via bank
                if (voucher.BankCustomerPaidId.HasValue && voucher.CashType == CashType.Bank)
                {
                    await _bankRepository.UpdateBalanceAsync(voucher.BankCustomerPaidId.Value, voucher.Amount, false);
                }

                await _voucherRepository.AddAsync(voucher);
                TempData["Success"] = $"Advanced Cash Paid of Rs. {voucher.Amount:N0} recorded successfully!";
                return RedirectToAction(nameof(GeneralCreate));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Error saving advanced cash paid: {ex.Message}");
            }

            ViewBag.Customers = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                await _customerRepository.GetActiveCustomersAsync(), "Id", "Name");
            ViewBag.Banks = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                await _bankRepository.GetActiveBanksAsync(), "Id", "Name");
            return View("AdvancedCashPaid", voucher);
        }

        // GET: Vouchers/AdvancedCashReceived
        public async Task<IActionResult> AdvancedCashReceived(int page = 1, int pageSize = 10)
        {
            var voucher = new Voucher
            {
                VoucherType = VoucherType.AdvancedCashReceived,
                VoucherDate = DateTimeHelper.PkToday,
                CashType = CashType.Cash
            };

            // Count and page in the database — only the current page's rows are fetched.
            var advReceivedQuery = _context.Vouchers
                .AsNoTracking()
                .Include(v => v.AdvancedReceivingCustomer)
                .Include(v => v.BankCustomerReceiver)
                .Where(v => v.VoucherType == VoucherType.AdvancedCashReceived);

            var totalRecords = await advReceivedQuery.CountAsync();
            var totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);
            if (totalPages < 1) totalPages = 1;
            var currentPage = Math.Max(1, Math.Min(page, totalPages));

            ViewBag.VoucherList = await advReceivedQuery
                .OrderByDescending(v => v.VoucherDate).ThenByDescending(v => v.Id)
                .Skip((currentPage - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            ViewBag.CurrentPage = currentPage;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalRecords = totalRecords;
            ViewBag.PageSize = pageSize;

            ViewBag.Customers = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                await _customerRepository.GetActiveCustomersAsync(), "Id", "Name");
            ViewBag.Banks = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                await _bankRepository.GetActiveBanksAsync(), "Id", "Name");
            return View(voucher);
        }

        // POST: Vouchers/SaveAdvancedCashReceived
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveAdvancedCashReceived(Voucher voucher)
        {
            try
            {
                if (!voucher.AdvancedReceivingCustomerId.HasValue)
                {
                    TempData["Error"] = "Please select a customer.";
                    ViewBag.Customers = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                        await _customerRepository.GetActiveCustomersAsync(), "Id", "Name");
                    ViewBag.Banks = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                        await _bankRepository.GetActiveBanksAsync(), "Id", "Name");
                    return View("AdvancedCashReceived", voucher);
                }

                voucher.VoucherType = VoucherType.AdvancedCashReceived;
                voucher.TransactionNumber = await _voucherRepository.GenerateTransactionNumberAsync(VoucherType.AdvancedCashReceived);
                voucher.CreatedBy = HttpContext.Session.GetString("Username") ?? "admin";
                voucher.CreatedDate = DateTimeHelper.PkNow;

                // Credit bank if received via bank
                if (voucher.BankCustomerReceiverId.HasValue && voucher.CashType == CashType.Bank)
                {
                    await _bankRepository.UpdateBalanceAsync(voucher.BankCustomerReceiverId.Value, voucher.Amount, true);
                }

                await _voucherRepository.AddAsync(voucher);
                TempData["Success"] = $"Advanced Cash Received of Rs. {voucher.Amount:N0} recorded successfully!";
                return RedirectToAction(nameof(GeneralCreate));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Error saving advanced cash received: {ex.Message}");
            }

            ViewBag.Customers = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                await _customerRepository.GetActiveCustomersAsync(), "Id", "Name");
            ViewBag.Banks = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                await _bankRepository.GetActiveBanksAsync(), "Id", "Name");
            return View("AdvancedCashReceived", voucher);
        }

        // AJAX Methods
        [HttpGet]
        public async Task<IActionResult> GetItemRate(int itemId, int? customerId)
        {
            decimal rate = 0;
            if (customerId.HasValue)
            {
                rate = await _itemRepository.GetItemRateForCustomerAsync(itemId, customerId.Value);
            }
            else
            {
                var item = await _itemRepository.GetByIdAsync(itemId);
                rate = item?.DefaultRate ?? 0;
            }
            return Json(new { rate });
        }

        [HttpGet]
        public async Task<IActionResult> GetTransactionNumber(VoucherType type)
        {
            var transactionNumber = await _voucherRepository.GenerateTransactionNumberAsync(type);
            return Json(new { transactionNumber });
        }

        // POST: Vouchers/DeleteMultiple
        [HttpPost]
        public async Task<IActionResult> DeleteMultiple([FromBody] List<int> voucherIds)
        {
            try
            {
                if (voucherIds == null || !voucherIds.Any())
                {
                    return Json(new { success = false, message = "No vouchers selected for deletion." });
                }

                int deletedCount = 0;
                foreach (var id in voucherIds)
                {
                    var voucher = await _voucherRepository.GetByIdAsync(id);
                    if (voucher != null)
                    {
                        // Reverse stock changes - ONLY if StockInclude was true
                        if (voucher.ItemId.HasValue && voucher.Quantity.HasValue && voucher.StockInclude)
                        {
                            if (voucher.VoucherType == VoucherType.Purchase)
                            {
                                await _itemRepository.UpdateStockAsync(voucher.ItemId.Value, voucher.Quantity.Value, false);
                            }
                            else if (voucher.VoucherType == VoucherType.Sale)
                            {
                                await _itemRepository.UpdateStockAsync(voucher.ItemId.Value, voucher.Quantity.Value, true);
                            }
                        }

                        // Reverse bank changes
                        if (voucher.BankCustomerPaidId.HasValue)
                        {
                            await _bankRepository.UpdateBalanceAsync(voucher.BankCustomerPaidId.Value, voucher.Amount, true);
                        }
                        if (voucher.BankCustomerReceiverId.HasValue)
                        {
                            await _bankRepository.UpdateBalanceAsync(voucher.BankCustomerReceiverId.Value, voucher.Amount, false);
                        }

                        // Soft delete: flag the voucher instead of removing it
                        voucher.IsDeleted = true;
                        voucher.DeletedDate = DateTimeHelper.PkNow;
                        voucher.DeletedBy = HttpContext.Session.GetString("Username") ?? "admin";
                        await _voucherRepository.UpdateAsync(voucher);
                        deletedCount++;
                    }
                }

                return Json(new { success = true, message = $"Successfully deleted {deletedCount} voucher(s)." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error deleting vouchers: {ex.Message}" });
            }
        }

        // Copies the fields the Edit form is allowed to change from the posted voucher onto the
        // stored one. Anything not listed here — TransactionNumber, CreatedDate/CreatedBy, and
        // the IsDeleted / IsRevoked audit columns — deliberately keeps its stored value.
        private static void ApplyEditableFields(Voucher target, Voucher posted)
        {
            target.VoucherType = posted.VoucherType;
            target.CashType = posted.CashType;
            target.VoucherDate = posted.VoucherDate;
            target.Status = posted.Status;

            target.ProjectId = posted.ProjectId;
            target.ItemId = posted.ItemId;
            target.ExpenseHeadId = posted.ExpenseHeadId;

            target.PurchasingCustomerId = posted.PurchasingCustomerId;
            target.ReceivingCustomerId = posted.ReceivingCustomerId;
            target.AdvancedPurchasingCustomerId = posted.AdvancedPurchasingCustomerId;
            target.AdvancedReceivingCustomerId = posted.AdvancedReceivingCustomerId;
            target.BankCustomerPaidId = posted.BankCustomerPaidId;
            target.BankCustomerReceiverId = posted.BankCustomerReceiverId;

            target.PurchasingCustomerDetails = posted.PurchasingCustomerDetails;
            target.ReceivingCustomerDetails = posted.ReceivingCustomerDetails;
            target.AdvancedPurchasingCustomerDetails = posted.AdvancedPurchasingCustomerDetails;
            target.AdvancedReceivingCustomerDetails = posted.AdvancedReceivingCustomerDetails;
            target.BankCustomerPaidDetails = posted.BankCustomerPaidDetails;
            target.BankCustomerReceiverDetails = posted.BankCustomerReceiverDetails;
            target.ExpenseHeadDetails = posted.ExpenseHeadDetails;

            target.Mon = posted.Mon;
            target.Weight = posted.Weight;
            target.Kat = posted.Kat;
            target.Quantity = posted.Quantity;
            target.Rate = posted.Rate;
            target.Amount = posted.Amount;
            target.ExpenseHeadRate = posted.ExpenseHeadRate;

            target.GariNo = posted.GariNo;
            target.StockInclude = posted.StockInclude;
        }

        private async Task PrepareViewBags()
        {
            ViewBag.Customers = new SelectList(await _customerRepository.GetActiveCustomersAsync(), "Id", "Name");
            ViewBag.Items = new SelectList(await GetItemsMostUsedFirstAsync(), "Id", "Name");
            ViewBag.Banks = new SelectList(await _bankRepository.GetActiveBanksAsync(), "Id", "Name");
            ViewBag.ExpenseHeads = new SelectList(await _expenseHeadRepository.GetActiveExpenseHeadsAsync(), "Id", "Name");
            ViewBag.Projects = new SelectList(await _projectRepository.GetActiveProjectsAsync(), "Id", "Name");
        }

        // Returns active items with the most-used item (by Purchase/Sale voucher count) placed
        // first, so it appears at the top of the Item dropdown for quick selection.
        private async Task<List<Item>> GetItemsMostUsedFirstAsync()
        {
            var items = (await _itemRepository.GetActiveItemsAsync()).ToList();

            // Count how often each item is used in Purchase/Sale vouchers
            var usageCounts = await _context.Vouchers
                .Where(v => v.ItemId.HasValue &&
                            (v.VoucherType == VoucherType.Purchase || v.VoucherType == VoucherType.Sale))
                .GroupBy(v => v.ItemId!.Value)
                .Select(g => new { ItemId = g.Key, Count = g.Count() })
                .ToListAsync();

            var countById = usageCounts.ToDictionary(x => x.ItemId, x => x.Count);

            // Order by usage (highest first), then alphabetically for the rest
            return items
                .OrderByDescending(i => countById.TryGetValue(i.Id, out var c) ? c : 0)
                .ThenBy(i => i.Name)
                .ToList();
        }
    }
}