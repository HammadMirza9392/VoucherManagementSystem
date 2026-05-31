using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Data;
using VoucherManagementSystem.Models;

namespace VoucherManagementSystem.Controllers
{
    public class UsersController : Controller
    {
        private readonly ApplicationDbContext _context;

        public UsersController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Users
        public async Task<IActionResult> Index()
        {
            // Only admin can access
            if (HttpContext.Session.GetString("UserRole") != "Admin")
            {
                return RedirectToAction("Index", "Home");
            }

            var users = await _context.Users.OrderByDescending(u => u.CreatedDate).ToListAsync();
            return View(users);
        }

        // GET: Users/Create
        public IActionResult Create()
        {
            // Only admin can access
            if (HttpContext.Session.GetString("UserRole") != "Admin")
            {
                return RedirectToAction("Index", "Home");
            }

            return View();
        }

        // POST: Users/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(User user)
        {
            // Only admin can access
            if (HttpContext.Session.GetString("UserRole") != "Admin")
            {
                return RedirectToAction("Index", "Home");
            }

            if (ModelState.IsValid)
            {
                // Check if username already exists
                var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == user.Username);
                if (existingUser != null)
                {
                    ModelState.AddModelError("Username", "Username already exists");
                    return View(user);
                }

                user.CreatedDate = DateTime.Now;
                user.CreatedBy = HttpContext.Session.GetString("Username");
                user.IsActive = true;

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                TempData["Success"] = "User created successfully";
                return RedirectToAction(nameof(Index));
            }

            return View(user);
        }

        // GET: Users/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            // Only admin can access
            if (HttpContext.Session.GetString("UserRole") != "Admin")
            {
                return RedirectToAction("Index", "Home");
            }

            if (id == null)
            {
                return NotFound();
            }

            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            return View(user);
        }

        // POST: Users/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, User user)
        {
            // Only admin can access
            if (HttpContext.Session.GetString("UserRole") != "Admin")
            {
                return RedirectToAction("Index", "Home");
            }

            if (id != user.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    // Check if username already exists (excluding current user)
                    var existingUser = await _context.Users
                        .FirstOrDefaultAsync(u => u.Username == user.Username && u.Id != id);
                    if (existingUser != null)
                    {
                        ModelState.AddModelError("Username", "Username already exists");
                        return View(user);
                    }

                    _context.Update(user);
                    await _context.SaveChangesAsync();

                    TempData["Success"] = "User updated successfully";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!UserExists(user.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            return View(user);
        }

        // POST: Users/ChangePassword
        [HttpPost]
        public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword)
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, message = "User not logged in" });
            }

            var user = await _context.Users.FindAsync(int.Parse(userId));
            if (user == null)
            {
                return Json(new { success = false, message = "User not found" });
            }

            if (user.Password != currentPassword)
            {
                return Json(new { success = false, message = "Current password is incorrect" });
            }

            user.Password = newPassword;
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Password changed successfully" });
        }

        // POST: Users/ResetPassword
        [HttpPost]
        public async Task<IActionResult> ResetPassword(int userId, string newPassword)
        {
            // Only admin can reset passwords
            if (HttpContext.Session.GetString("UserRole") != "Admin")
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return Json(new { success = false, message = "User not found" });
            }

            user.Password = newPassword;
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Password reset successfully" });
        }

        // POST: Users/ToggleStatus
        [HttpPost]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            // Only admin can toggle status
            if (HttpContext.Session.GetString("UserRole") != "Admin")
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return Json(new { success = false, message = "User not found" });
            }

            // Don't allow deactivating yourself
            var currentUserId = HttpContext.Session.GetString("UserId");
            if (user.Id.ToString() == currentUserId)
            {
                return Json(new { success = false, message = "Cannot deactivate your own account" });
            }

            user.IsActive = !user.IsActive;
            await _context.SaveChangesAsync();

            return Json(new { success = true, isActive = user.IsActive });
        }

        private bool UserExists(int id)
        {
            return _context.Users.Any(e => e.Id == id);
        }
    }
}
