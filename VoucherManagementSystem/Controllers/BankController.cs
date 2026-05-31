using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Interfaces;
using VoucherManagementSystem.Models;

namespace VoucherManagementSystem.Controllers
{
    public class BanksController : Controller
    {
        private readonly IBankRepository _bankRepository;

        public BanksController(IBankRepository bankRepository)
        {
            _bankRepository = bankRepository;
        }

        public async Task<IActionResult> Index()
        {
            return View(await _bankRepository.GetActiveBanksAsync());
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var bank = await _bankRepository.GetByIdAsync(id.Value);
            if (bank == null)
            {
                return NotFound();
            }

            return View(bank);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Bank bank)
        {
            if (ModelState.IsValid)
            {
                bank.CreatedBy = HttpContext.Session.GetString("Username") ?? "admin";
                bank.CreatedDate = DateTime.Now;
                await _bankRepository.AddAsync(bank);
                TempData["Success"] = "Bank created successfully!";
                return RedirectToAction(nameof(Index));
            }
            return View(bank);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var bank = await _bankRepository.GetByIdAsync(id.Value);
            if (bank == null)
            {
                return NotFound();
            }
            return View(bank);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Bank bank)
        {
            if (id != bank.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    bank.UpdatedBy = HttpContext.Session.GetString("Username") ?? "admin";
                    bank.UpdatedDate = DateTime.Now;
                    await _bankRepository.UpdateAsync(bank);
                    TempData["Success"] = "Bank updated successfully!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await _bankRepository.ExistsAsync(bank.Id))
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
            return View(bank);
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var bank = await _bankRepository.GetByIdAsync(id.Value);
            if (bank == null)
            {
                return NotFound();
            }

            return View(bank);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var bank = await _bankRepository.GetByIdAsync(id);
            if (bank != null)
            {
                bank.IsActive = false;
                await _bankRepository.UpdateAsync(bank);
                TempData["Success"] = "Bank deactivated successfully!";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}