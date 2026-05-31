using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Interfaces;
using VoucherManagementSystem.Models;

namespace VoucherManagementSystem.Controllers
{
    public class ExpenseHeadsController : Controller
    {
        private readonly IExpenseHeadRepository _expenseHeadRepository;

        public ExpenseHeadsController(IExpenseHeadRepository expenseHeadRepository)
        {
            _expenseHeadRepository = expenseHeadRepository;
        }

        public async Task<IActionResult> Index(DateTime? fromDate, DateTime? toDate)
        {
            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

            var expenseHeads = await _expenseHeadRepository.GetActiveExpenseHeadsWithDateFilterAsync(fromDate, toDate);
            return View(expenseHeads);
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var expenseHead = await _expenseHeadRepository.GetByIdAsync(id.Value);
            if (expenseHead == null)
            {
                return NotFound();
            }

            ViewBag.Expenses = await _expenseHeadRepository.GetExpensesByHeadAsync(id.Value);
            return View(expenseHead);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ExpenseHead expenseHead)
        {
            if (ModelState.IsValid)
            {
                expenseHead.CreatedBy = HttpContext.Session.GetString("Username") ?? "admin";
                expenseHead.CreatedDate = DateTime.Now;
                await _expenseHeadRepository.AddAsync(expenseHead);
                TempData["Success"] = "Expense Head created successfully!";
                return RedirectToAction(nameof(Index));
            }
            return View(expenseHead);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var expenseHead = await _expenseHeadRepository.GetByIdAsync(id.Value);
            if (expenseHead == null)
            {
                return NotFound();
            }
            return View(expenseHead);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ExpenseHead expenseHead)
        {
            if (id != expenseHead.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    expenseHead.UpdatedBy = HttpContext.Session.GetString("Username") ?? "admin";
                    expenseHead.UpdatedDate = DateTime.Now;
                    await _expenseHeadRepository.UpdateAsync(expenseHead);
                    TempData["Success"] = "Expense Head updated successfully!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await _expenseHeadRepository.ExistsAsync(expenseHead.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            return View(expenseHead);
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var expenseHead = await _expenseHeadRepository.GetByIdAsync(id.Value);
            if (expenseHead == null)
            {
                return NotFound();
            }

            return View(expenseHead);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var expenseHead = await _expenseHeadRepository.GetByIdAsync(id);
            if (expenseHead != null)
            {
                expenseHead.IsActive = false;
                await _expenseHeadRepository.UpdateAsync(expenseHead);
                TempData["Success"] = "Expense Head deactivated successfully!";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}