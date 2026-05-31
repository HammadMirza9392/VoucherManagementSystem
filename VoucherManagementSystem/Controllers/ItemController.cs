using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Data;
using VoucherManagementSystem.Interfaces;
using VoucherManagementSystem.Models;

namespace VoucherManagementSystem.Controllers
{
    public class ItemsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IItemRepository _itemRepository;
        private readonly ILogger<ItemsController> _logger;

        public ItemsController(
            ApplicationDbContext context,
            IItemRepository itemRepository,
            ILogger<ItemsController> logger)
        {
            _context = context;
            _itemRepository = itemRepository;
            _logger = logger;
        }

        // GET: Items
        public async Task<IActionResult> Index()
        {
            try
            {
                var items = await _itemRepository.GetActiveItemsAsync();
                return View(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading items");
                TempData["Error"] = "Error loading items. Please try again.";
                return View(new List<Item>());
            }
        }

        // GET: Items/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            try
            {
                var item = await _itemRepository.GetByIdAsync(id.Value);
                if (item == null)
                {
                    return NotFound();
                }

                return View(item);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading item details");
                TempData["Error"] = "Error loading item details.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Items/Create
        public IActionResult Create()
        {
            var item = new Item
            {
                IsActive = true,
                StockTrackingEnabled = true,
                CurrentStock = 0,
                DefaultRate = 0
            };
            return View(item);
        }

        // POST: Items/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Name,Unit,StockTrackingEnabled,CurrentStock,DefaultRate,IsActive")] Item item)
        {
            try
            {
                // Remove validation for navigation properties
                ModelState.Remove("CustomerItemRates");
                ModelState.Remove("Vouchers");

                if (ModelState.IsValid)
                {
                    item.CreatedDate = DateTime.Now;
                    item.CreatedBy = HttpContext.Session.GetString("Username") ?? "admin";
                    await _itemRepository.AddAsync(item);
                    TempData["Success"] = "Item created successfully!";
                    return RedirectToAction(nameof(Index));
                }

                // Log validation errors
                foreach (var error in ModelState.Values.SelectMany(v => v.Errors))
                {
                    _logger.LogWarning($"Validation error: {error.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating item");
                ModelState.AddModelError("", "Unable to save item. Please try again.");
            }

            return View(item);
        }

        // GET: Items/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            try
            {
                var item = await _itemRepository.GetByIdAsync(id.Value);
                if (item == null)
                {
                    return NotFound();
                }
                return View(item);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading item for edit");
                TempData["Error"] = "Error loading item.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Items/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,Unit,StockTrackingEnabled,CurrentStock,DefaultRate,IsActive,CreatedDate,CreatedBy")] Item item)
        {
            if (id != item.Id)
            {
                return NotFound();
            }

            try
            {
                // Remove validation for navigation properties
                ModelState.Remove("CustomerItemRates");
                ModelState.Remove("Vouchers");

                if (ModelState.IsValid)
                {
                    item.UpdatedBy = HttpContext.Session.GetString("Username") ?? "admin";
                    item.UpdatedDate = DateTime.Now;
                    _context.Update(item);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Item updated successfully!";
                    return RedirectToAction(nameof(Index));
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await ItemExists(item.Id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating item");
                ModelState.AddModelError("", "Unable to update item. Please try again.");
            }

            return View(item);
        }

        // GET: Items/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            try
            {
                var item = await _itemRepository.GetByIdAsync(id.Value);
                if (item == null)
                {
                    return NotFound();
                }

                return View(item);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading item for delete");
                TempData["Error"] = "Error loading item.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Items/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                var item = await _itemRepository.GetByIdAsync(id);
                if (item != null)
                {
                    // Soft delete - just deactivate
                    item.IsActive = false;
                    await _itemRepository.UpdateAsync(item);
                    TempData["Success"] = "Item deactivated successfully!";
                }
                else
                {
                    TempData["Error"] = "Item not found.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting item");
                TempData["Error"] = "Error deleting item. Please try again.";
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task<bool> ItemExists(int id)
        {
            return await _itemRepository.ExistsAsync(id);
        }
    }
}