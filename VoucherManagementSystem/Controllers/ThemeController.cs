using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Data;
using VoucherManagementSystem.Models;

namespace VoucherManagementSystem.Controllers
{
    public class ThemeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ThemeController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Theme/Settings
        public async Task<IActionResult> Settings()
        {
            // Check if user is admin
            if (HttpContext.Session.GetString("UserRole") != "Admin")
            {
                TempData["Error"] = "Access denied. Only administrators can access theme settings.";
                return RedirectToAction("Index", "Home");
            }

            // Get active theme settings or create default
            var themeSettings = await _context.ThemeSettings
                .Where(t => t.IsActive)
                .FirstOrDefaultAsync();

            if (themeSettings == null)
            {
                themeSettings = new ThemeSettings
                {
                    ThemeMode = "Light",
                    PrimaryColor = "#0d6efd",
                    SecondaryColor = "#6c757d",
                    SuccessColor = "#198754",
                    DangerColor = "#dc3545",
                    WarningColor = "#ffc107",
                    InfoColor = "#0dcaf0",
                    BackgroundColor = "#ffffff",
                    TextColor = "#212529",
                    CardBackgroundColor = "#ffffff",
                    NavbarBackgroundColor = "#ffffff",
                    SidebarBackgroundColor = "#ffffff",
                    FooterBackgroundColor = "#f8f9fa",
                    IsActive = true
                };
            }

            return View(themeSettings);
        }

        // POST: Theme/SaveSettings
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveSettings(ThemeSettings model)
        {
            // Check if user is admin
            if (HttpContext.Session.GetString("UserRole") != "Admin")
            {
                return Json(new { success = false, message = "Access denied." });
            }

            try
            {
                // Get existing active theme
                var existingTheme = await _context.ThemeSettings
                    .Where(t => t.IsActive)
                    .FirstOrDefaultAsync();

                if (existingTheme != null)
                {
                    // Update existing theme
                    existingTheme.ThemeMode = model.ThemeMode;
                    existingTheme.PrimaryColor = model.PrimaryColor;
                    existingTheme.SecondaryColor = model.SecondaryColor;
                    existingTheme.SuccessColor = model.SuccessColor;
                    existingTheme.DangerColor = model.DangerColor;
                    existingTheme.WarningColor = model.WarningColor;
                    existingTheme.InfoColor = model.InfoColor;
                    existingTheme.BackgroundColor = model.BackgroundColor;
                    existingTheme.TextColor = model.TextColor;
                    existingTheme.CardBackgroundColor = model.CardBackgroundColor;
                    existingTheme.NavbarBackgroundColor = model.NavbarBackgroundColor;
                    existingTheme.SidebarBackgroundColor = model.SidebarBackgroundColor;
                    existingTheme.FooterBackgroundColor = model.FooterBackgroundColor;
                    existingTheme.LastUpdated = DateTime.Now;
                    existingTheme.UpdatedBy = HttpContext.Session.GetString("Username");

                    _context.Update(existingTheme);
                }
                else
                {
                    // Create new theme
                    model.IsActive = true;
                    model.LastUpdated = DateTime.Now;
                    model.UpdatedBy = HttpContext.Session.GetString("Username");
                    _context.Add(model);
                }

                await _context.SaveChangesAsync();
                TempData["Success"] = "Theme settings saved successfully!";
                return Json(new { success = true, message = "Theme settings saved successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error saving theme: " + ex.Message });
            }
        }

        // POST: Theme/ApplyPreset
        [HttpPost]
        public async Task<IActionResult> ApplyPreset(string themeMode)
        {
            // Check if user is admin
            if (HttpContext.Session.GetString("UserRole") != "Admin")
            {
                return Json(new { success = false, message = "Access denied." });
            }

            try
            {
                var existingTheme = await _context.ThemeSettings
                    .Where(t => t.IsActive)
                    .FirstOrDefaultAsync();

                ThemeSettings theme = existingTheme ?? new ThemeSettings { IsActive = true };

                theme.ThemeMode = themeMode;

                switch (themeMode)
                {
                    case "Light":
                        theme.PrimaryColor = "#0d6efd";
                        theme.SecondaryColor = "#6c757d";
                        theme.SuccessColor = "#198754";
                        theme.DangerColor = "#dc3545";
                        theme.WarningColor = "#ffc107";
                        theme.InfoColor = "#0dcaf0";
                        theme.BackgroundColor = "#ffffff";
                        theme.TextColor = "#212529";
                        theme.CardBackgroundColor = "#ffffff";
                        theme.NavbarBackgroundColor = "#ffffff";
                        theme.SidebarBackgroundColor = "#ffffff";
                        theme.FooterBackgroundColor = "#f8f9fa";
                        break;

                    case "Dark":
                        theme.PrimaryColor = "#0d6efd";
                        theme.SecondaryColor = "#6c757d";
                        theme.SuccessColor = "#198754";
                        theme.DangerColor = "#dc3545";
                        theme.WarningColor = "#ffc107";
                        theme.InfoColor = "#0dcaf0";
                        theme.BackgroundColor = "#1a1d20";
                        theme.TextColor = "#e9ecef";
                        theme.CardBackgroundColor = "#212529";
                        theme.NavbarBackgroundColor = "#212529";
                        theme.SidebarBackgroundColor = "#212529";
                        theme.FooterBackgroundColor = "#1a1d20";
                        break;

                    case "SemiDark":
                        theme.PrimaryColor = "#0d6efd";
                        theme.SecondaryColor = "#6c757d";
                        theme.SuccessColor = "#198754";
                        theme.DangerColor = "#dc3545";
                        theme.WarningColor = "#ffc107";
                        theme.InfoColor = "#0dcaf0";
                        theme.BackgroundColor = "#f4f6f9";
                        theme.TextColor = "#212529";
                        theme.CardBackgroundColor = "#ffffff";
                        theme.NavbarBackgroundColor = "#2c3e50";
                        theme.SidebarBackgroundColor = "#2c3e50";
                        theme.FooterBackgroundColor = "#2c3e50";
                        break;
                }

                theme.LastUpdated = DateTime.Now;
                theme.UpdatedBy = HttpContext.Session.GetString("Username");

                if (existingTheme == null)
                {
                    _context.Add(theme);
                }
                else
                {
                    _context.Update(theme);
                }

                await _context.SaveChangesAsync();

                return Json(new { success = true, theme = theme });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error applying preset: " + ex.Message });
            }
        }

        // GET: Theme/GetCurrentTheme
        [HttpGet]
        public async Task<IActionResult> GetCurrentTheme()
        {
            var theme = await _context.ThemeSettings
                .Where(t => t.IsActive)
                .FirstOrDefaultAsync();

            if (theme == null)
            {
                // Return default light theme
                theme = new ThemeSettings
                {
                    ThemeMode = "Light",
                    PrimaryColor = "#0d6efd",
                    SecondaryColor = "#6c757d",
                    SuccessColor = "#198754",
                    DangerColor = "#dc3545",
                    WarningColor = "#ffc107",
                    InfoColor = "#0dcaf0",
                    BackgroundColor = "#ffffff",
                    TextColor = "#212529",
                    CardBackgroundColor = "#ffffff",
                    NavbarBackgroundColor = "#ffffff",
                    SidebarBackgroundColor = "#ffffff",
                    FooterBackgroundColor = "#f8f9fa"
                };
            }

            return Json(theme);
        }
    }
}
