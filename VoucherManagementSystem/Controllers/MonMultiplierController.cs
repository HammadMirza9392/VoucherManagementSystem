using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Data;
using VoucherManagementSystem.Models;
using VoucherManagementSystem.Services.Caching;

namespace VoucherManagementSystem.Controllers
{
    public class MonMultiplierController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IMasterDataCache _cache;
        private readonly ILogger<MonMultiplierController> _logger;

        public MonMultiplierController(
            ApplicationDbContext context,
            IMasterDataCache cache,
            ILogger<MonMultiplierController> logger)
        {
            _context = context;
            _cache = cache;
            _logger = logger;
        }

        // GET: MonMultiplier
        public async Task<IActionResult> Index()
        {
            var multipliers = await _cache.GetOrCreateAsync(CacheKeys.MonMultipliersAll, async () =>
                await _context.MonMultipliers.AsNoTracking().OrderBy(m => m.VoucherType).ToListAsync());
            return View(multipliers);
        }

        // GET: MonMultiplier/Create
        public IActionResult Create()
        {
            return View(new MonMultiplier());
        }

        // POST: MonMultiplier/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(MonMultiplier model)
        {
            if (ModelState.IsValid)
            {
                model.LastUpdated = DateTimeHelper.PkNow;
                model.UpdatedBy = HttpContext.Session.GetString("Username") ?? "System";
                _context.MonMultipliers.Add(model);
                await _context.SaveChangesAsync();
                _cache.InvalidateMonMultipliers();
                TempData["Success"] = "Mon multiplier saved successfully.";
                return RedirectToAction(nameof(Index));
            }
            return View(model);
        }

        // GET: MonMultiplier/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            var multiplier = await _context.MonMultipliers.FindAsync(id);
            if (multiplier == null) return NotFound();
            return View(multiplier);
        }

        // POST: MonMultiplier/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, MonMultiplier model)
        {
            if (id != model.Id) return NotFound();

            if (ModelState.IsValid)
            {
                model.LastUpdated = DateTimeHelper.PkNow;
                model.UpdatedBy = HttpContext.Session.GetString("Username") ?? "System";
                _context.Update(model);
                await _context.SaveChangesAsync();
                _cache.InvalidateMonMultipliers();
                TempData["Success"] = "Mon multiplier updated successfully.";
                return RedirectToAction(nameof(Index));
            }
            return View(model);
        }

        // POST: MonMultiplier/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var multiplier = await _context.MonMultipliers.FindAsync(id);
            if (multiplier != null)
            {
                _context.MonMultipliers.Remove(multiplier);
                await _context.SaveChangesAsync();
                _cache.InvalidateMonMultipliers();
                TempData["Success"] = "Mon multiplier deleted.";
            }
            return RedirectToAction(nameof(Index));
        }

        // GET: MonMultiplier/GetMultiplier?voucherType=Purchase
        // API endpoint called from GeneralCreate JS
        [HttpGet]
        public async Task<IActionResult> GetMultiplier(string voucherType)
        {
            var key = CacheKeys.MonMultiplier(voucherType ?? string.Empty);
            var value = await _cache.GetOrCreateAsync(key, async () =>
            {
                var multiplier = await _context.MonMultipliers
                    .AsNoTracking()
                    .Where(m => m.VoucherType == voucherType && m.IsActive)
                    .FirstOrDefaultAsync();

                return multiplier?.Multiplier ?? 40m;
            });

            return Json(new { multiplier = value });
        }
    }
}
