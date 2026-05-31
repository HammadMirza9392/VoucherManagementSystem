using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Data;
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

        // GET: Vouchers
        public async Task<IActionResult> Index(VoucherType? voucherType, int? customerId, int? projectId, int? itemId, DateTime? fromDate, DateTime? toDate, bool? stockInclude)
        {
            // Start with all vouchers
            var vouchers = await _voucherRepository.GetVouchersWithDetailsAsync();

            // Apply filters progressively
            if (voucherType.HasValue)
            {
                vouchers = vouchers.Where(v => v.VoucherType == voucherType.Value);
            }

            if (customerId.HasValue)
            {
                vouchers = vouchers.Where(v =>
                    v.PurchasingCustomerId == customerId.Value ||
                    v.ReceivingCustomerId == customerId.Value);
            }

            if (projectId.HasValue)
            {
                vouchers = vouchers.Where(v => v.ProjectId == projectId.Value);
            }

            if (itemId.HasValue)
            {
                vouchers = vouchers.Where(v => v.ItemId == itemId.Value);
            }

            if (fromDate.HasValue && toDate.HasValue)
            {
                vouchers = vouchers.Where(v => v.VoucherDate >= fromDate.Value && v.VoucherDate <= toDate.Value);
            }
            else if (fromDate.HasValue)
            {
                vouchers = vouchers.Where(v => v.VoucherDate >= fromDate.Value);
            }
            else if (toDate.HasValue)
            {
                vouchers = vouchers.Where(v => v.VoucherDate <= toDate.Value);
            }

            if (stockInclude.HasValue)
            {
                vouchers = vouchers.Where(v => v.StockInclude == stockInclude.Value);
            }

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
        public async Task<IActionResult> GeneralCreate(int? page = null, int pageSize = 10)
        {
            var voucher = new Voucher
            {
                VoucherType = VoucherType.Purchase,
                VoucherDate = DateTime.Now
            };

            // Get all recent vouchers (all types)
            var allVouchers = await _voucherRepository.GetVouchersWithDetailsAsync();
            var filteredVouchers = allVouchers
                .OrderBy(v => v.CreatedDate)
                .ToList();

            // Calculate pagination
            var totalRecords = filteredVouchers.Count;
            var totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);

            // Ensure at least 1 page
            if (totalPages < 1) totalPages = 1;

            // If no page specified, show the last page (most recent entries)
            var currentPage = page ?? totalPages;
            if (currentPage < 1) currentPage = 1;
            if (currentPage > totalPages) currentPage = totalPages;

            var voucherList = filteredVouchers
                .Skip((currentPage - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ViewBag.VoucherList = voucherList;
            ViewBag.CurrentPage = currentPage;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalRecords = totalRecords;
            ViewBag.PageSize = pageSize;

            await PrepareViewBags();
            return View(voucher);
        }

        // GET: Vouchers/Create
        public async Task<IActionResult> Create(VoucherType? type, int page = 1, int pageSize = 10)
        {
            var voucher = new Voucher
            {
                VoucherType = type ?? VoucherType.Purchase,
                VoucherDate = DateTime.Now
            };

            // Get recent vouchers filtered by type
            var voucherType = type ?? VoucherType.Purchase;
            var allVouchers = await _voucherRepository.GetVouchersWithDetailsAsync();
            var filteredVouchers = allVouchers
                .Where(v => v.VoucherType == voucherType)
                .OrderBy(v => v.CreatedDate)
                .ToList();

            // Calculate pagination
            var totalRecords = filteredVouchers.Count;
            var totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);
            var voucherList = filteredVouchers
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

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

                // Get original voucher for stock/balance reversal
                var originalVoucher = await _context.Vouchers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(v => v.Id == id);

                if (originalVoucher != null)
                {
                    // Reverse original stock changes - ONLY if StockInclude was true
                    if (originalVoucher.ItemId.HasValue && originalVoucher.Quantity.HasValue && originalVoucher.StockInclude)
                    {
                        if (originalVoucher.VoucherType == VoucherType.Purchase)
                        {
                            await _itemRepository.UpdateStockAsync(originalVoucher.ItemId.Value, originalVoucher.Quantity.Value, false);
                        }
                        else if (originalVoucher.VoucherType == VoucherType.Sale)
                        {
                            await _itemRepository.UpdateStockAsync(originalVoucher.ItemId.Value, originalVoucher.Quantity.Value, true);
                        }
                    }

                    // Reverse original bank changes
                    if (originalVoucher.BankCustomerPaidId.HasValue)
                    {
                        await _bankRepository.UpdateBalanceAsync(originalVoucher.BankCustomerPaidId.Value, originalVoucher.Amount, true);
                    }
                    if (originalVoucher.BankCustomerReceiverId.HasValue)
                    {
                        await _bankRepository.UpdateBalanceAsync(originalVoucher.BankCustomerReceiverId.Value, originalVoucher.Amount, false);
                    }
                }

                // Apply new changes
                if (voucher.Quantity.HasValue && voucher.Rate.HasValue && voucher.Amount == 0)
                {
                    voucher.Amount = voucher.Quantity.Value * voucher.Rate.Value;
                }

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

                // Set updated by and updated date
                voucher.UpdatedBy = HttpContext.Session.GetString("Username") ?? "admin";
                voucher.UpdatedDate = DateTime.Now;

                await _voucherRepository.UpdateAsync(voucher);
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

                await _voucherRepository.DeleteAsync(voucher);
                TempData["Success"] = "Voucher deleted successfully!";
            }

            return RedirectToAction(nameof(Index));
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

                        await _voucherRepository.DeleteAsync(voucher);
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

        private async Task PrepareViewBags()
        {
            ViewBag.Customers = new SelectList(await _customerRepository.GetActiveCustomersAsync(), "Id", "Name");
            ViewBag.Items = new SelectList(await _itemRepository.GetActiveItemsAsync(), "Id", "Name");
            ViewBag.Banks = new SelectList(await _bankRepository.GetActiveBanksAsync(), "Id", "Name");
            ViewBag.ExpenseHeads = new SelectList(await _expenseHeadRepository.GetActiveExpenseHeadsAsync(), "Id", "Name");
            ViewBag.Projects = new SelectList(await _projectRepository.GetActiveProjectsAsync(), "Id", "Name");
        }
    }
}