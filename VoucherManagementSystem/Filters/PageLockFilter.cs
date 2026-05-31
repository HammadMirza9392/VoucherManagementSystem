using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Data;
using VoucherManagementSystem.Models;

namespace VoucherManagementSystem.Filters
{
    public class PageLockFilter : IAsyncActionFilter
    {
        private readonly ApplicationDbContext _context;

        public PageLockFilter(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var httpContext = context.HttpContext;
            var controller = context.RouteData.Values["controller"]?.ToString() ?? "";
            var action = context.RouteData.Values["action"]?.ToString() ?? "";
            var path = $"/{controller}/{action}";

            // Skip check for PageLock controller, Login, Logout
            if (controller.Equals("PageLock", StringComparison.OrdinalIgnoreCase) ||
                (controller.Equals("Home", StringComparison.OrdinalIgnoreCase) &&
                 (action.Equals("Login", StringComparison.OrdinalIgnoreCase) ||
                  action.Equals("DoLogin", StringComparison.OrdinalIgnoreCase) ||
                  action.Equals("Logout", StringComparison.OrdinalIgnoreCase))))
            {
                await next();
                return;
            }

            // Get all locked pages
            var lockedPages = await _context.PageLocks
                .Where(p => p.IsLocked)
                .ToListAsync();

            // Check if current path matches any locked page
            PageLock? matchedLock = null;
            foreach (var pageLock in lockedPages)
            {
                // Normalize both paths for comparison
                var normalizedPageUrl = pageLock.PageUrl.TrimEnd('/');
                var normalizedPath = path.TrimEnd('/');

                // Check exact match or if path starts with PageUrl
                if (normalizedPath.Equals(normalizedPageUrl, StringComparison.OrdinalIgnoreCase) ||
                    normalizedPath.StartsWith(normalizedPageUrl + "/", StringComparison.OrdinalIgnoreCase))
                {
                    matchedLock = pageLock;
                    break;
                }
            }

            if (matchedLock != null)
            {
                // Check if page is already unlocked in session
                var sessionKey = $"PageUnlocked_{matchedLock.PageUrl}";
                var isUnlocked = httpContext.Session.GetString(sessionKey);

                if (isUnlocked != "true")
                {
                    // BLOCK ACCESS - Return a view with the lock modal
                    // Store page lock info for the view
                    context.HttpContext.Items["PageLocked"] = true;
                    context.HttpContext.Items["LockedPageName"] = matchedLock.PageName;
                    context.HttpContext.Items["LockedPageUrl"] = matchedLock.PageUrl;

                    // Return JSON for AJAX requests
                    if (httpContext.Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    {
                        context.Result = new JsonResult(new
                        {
                            locked = true,
                            pageName = matchedLock.PageName,
                            pageUrl = matchedLock.PageUrl,
                            message = "This page is locked. Please enter the password."
                        })
                        {
                            StatusCode = 403
                        };
                        return;
                    }

                    // For normal page requests, render a locked page view instead of continuing
                    context.Result = new ViewResult
                    {
                        ViewName = "~/Views/Shared/LockedPage.cshtml",
                        ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(
                            new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(),
                            new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary())
                        {
                            ["PageName"] = matchedLock.PageName,
                            ["PageUrl"] = matchedLock.PageUrl
                        }
                    };
                    return; // IMPORTANT: Don't call next() - block the request here
                }
                else
                {
                    // Page is unlocked - check lock mode to determine if we should clear the session
                    // If mode is "JustView", clear the session key so next visit requires password again
                    // If mode is "Login", keep the session key so user stays unlocked until session expires
                    if (matchedLock.LockMode == "JustView")
                    {
                        // Clear the unlock status for next visit
                        httpContext.Session.Remove(sessionKey);
                    }
                    // If "Login" mode, session persists until explicitly cleared or session expires
                }
            }

            await next();
        }
    }
}
