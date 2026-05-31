using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Data;
using VoucherManagementSystem.Interfaces;
using VoucherManagementSystem.Models;

namespace VoucherManagementSystem.Controllers
{
    public class CustomersController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ICustomerRepository _customerRepository;
        private readonly IItemRepository _itemRepository;
        private readonly ILogger<CustomersController> _logger;

        public CustomersController(
            ApplicationDbContext context,
            ICustomerRepository customerRepository,
            IItemRepository itemRepository,
            ILogger<CustomersController> logger)
        {
            _context = context;
            _customerRepository = customerRepository;
            _itemRepository = itemRepository;
            _logger = logger;
        }

        // GET: Customers
        public async Task<IActionResult> Index()
        {
            try
            {
                var customers = await _customerRepository.GetActiveCustomersAsync();
                return View(customers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading customers");
                TempData["Error"] = "Error loading customers. Please try again.";
                return View(new List<Customer>());
            }
        }

        // GET: Customers/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            try
            {
                var customer = await _customerRepository.GetByIdAsync(id.Value);
                if (customer == null)
                {
                    return NotFound();
                }

                ViewBag.CustomerRates = await _customerRepository.GetCustomerRatesAsync(id.Value);
                return View(customer);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading customer details");
                TempData["Error"] = "Error loading customer details.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Customers/Create
        public IActionResult Create()
        {
            var customer = new Customer
            {
                IsActive = true
            };
            return View(customer);
        }

        // POST: Customers/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Name,Phone,Address,IsActive")] Customer customer)
        {
            try
            {
                // Remove validation for navigation properties
                ModelState.Remove("CustomerItemRates");
                ModelState.Remove("PurchasingVouchers");
                ModelState.Remove("ReceivingVouchers");

                if (ModelState.IsValid)
                {
                    customer.CreatedDate = DateTime.Now;
                    customer.CreatedBy = HttpContext.Session.GetString("Username") ?? "admin";
                    await _customerRepository.AddAsync(customer);
                    TempData["Success"] = "Customer created successfully!";
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
                _logger.LogError(ex, "Error creating customer");
                ModelState.AddModelError("", "Unable to save customer. Please try again.");
            }

            return View(customer);
        }

        // GET: Customers/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            try
            {
                var customer = await _customerRepository.GetByIdAsync(id.Value);
                if (customer == null)
                {
                    return NotFound();
                }
                return View(customer);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading customer for edit");
                TempData["Error"] = "Error loading customer.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Customers/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,Phone,Address,IsActive,CreatedDate,CreatedBy")] Customer customer)
        {
            if (id != customer.Id)
            {
                return NotFound();
            }

            try
            {
                // Remove validation for navigation properties
                ModelState.Remove("CustomerItemRates");
                ModelState.Remove("PurchasingVouchers");
                ModelState.Remove("ReceivingVouchers");

                if (ModelState.IsValid)
                {
                    customer.UpdatedBy = HttpContext.Session.GetString("Username") ?? "admin";
                    customer.UpdatedDate = DateTime.Now;
                    _context.Update(customer);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Customer updated successfully!";
                    return RedirectToAction(nameof(Index));
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await CustomerExists(customer.Id))
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
                _logger.LogError(ex, "Error updating customer");
                ModelState.AddModelError("", "Unable to update customer. Please try again.");
            }

            return View(customer);
        }

        // GET: Customers/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            try
            {
                var customer = await _customerRepository.GetByIdAsync(id.Value);
                if (customer == null)
                {
                    return NotFound();
                }

                return View(customer);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading customer for delete");
                TempData["Error"] = "Error loading customer.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Customers/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                var customer = await _customerRepository.GetByIdAsync(id);
                if (customer != null)
                {
                    // Soft delete - just deactivate
                    customer.IsActive = false;
                    await _customerRepository.UpdateAsync(customer);
                    TempData["Success"] = "Customer deactivated successfully!";
                }
                else
                {
                    TempData["Error"] = "Customer not found.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting customer");
                TempData["Error"] = "Error deleting customer. Please try again.";
            }

            return RedirectToAction(nameof(Index));
        }

        // GET: Customers/ManageRates/5
        public async Task<IActionResult> ManageRates(int id)
        {
            try
            {
                var customer = await _customerRepository.GetByIdAsync(id);
                if (customer == null)
                {
                    return NotFound();
                }

                ViewBag.Customer = customer;
                ViewBag.Items = await _itemRepository.GetActiveItemsAsync();
                ViewBag.CustomerRates = await _customerRepository.GetCustomerRatesAsync(id);

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading customer rates");
                TempData["Error"] = "Error loading customer rates.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Customers/UpdateRate
        [HttpPost]
        public async Task<IActionResult> UpdateRate(int customerId, int itemId, decimal rate)
        {
            try
            {
                await _customerRepository.AddCustomerItemRateAsync(customerId, itemId, rate);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating customer rate");
                return Json(new { success = false, message = "Error updating rate." });
            }
        }

        private async Task<bool> CustomerExists(int id)
        {
            return await _customerRepository.ExistsAsync(id);
        }
    }
}
