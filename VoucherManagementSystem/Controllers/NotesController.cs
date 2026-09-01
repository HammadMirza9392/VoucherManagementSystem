using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoucherManagementSystem.Data;
using VoucherManagementSystem.Models;

namespace VoucherManagementSystem.Controllers
{
    // Note Book — shared notes (title + description) that any logged-in user can add.
    // Everyone can read every note; only the author or an Admin can edit or delete one.
    public class NotesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<NotesController> _logger;

        public NotesController(ApplicationDbContext context, ILogger<NotesController> logger)
        {
            _context = context;
            _logger = logger;
        }

        private string CurrentUser => HttpContext.Session.GetString("Username") ?? "System";
        private bool IsAdmin => HttpContext.Session.GetString("UserRole") == "Admin";

        // An Admin may touch any note; everyone else only their own.
        private bool CanModify(Note note) =>
            IsAdmin || string.Equals(note.CreatedBy, CurrentUser, StringComparison.OrdinalIgnoreCase);

        // GET: Notes?search=...
        public async Task<IActionResult> Index(string? search)
        {
            var query = _context.Notes.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(n => EF.Functions.ILike(n.Title, $"%{term}%") ||
                                         EF.Functions.ILike(n.Description, $"%{term}%"));
            }

            // Pinned notes first, then newest.
            var notes = await query
                .OrderByDescending(n => n.IsPinned)
                .ThenByDescending(n => n.CreatedDate)
                .ThenByDescending(n => n.Id)
                .ToListAsync();

            ViewBag.Search = search;
            ViewBag.CurrentUser = CurrentUser;
            ViewBag.IsAdmin = IsAdmin;
            return View(notes);
        }

        // GET: Notes/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var note = await _context.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id);
            if (note == null) return NotFound();

            ViewBag.CanModify = CanModify(note);
            return View(note);
        }

        // GET: Notes/Create
        public IActionResult Create()
        {
            return View(new Note());
        }

        // POST: Notes/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Note model)
        {
            if (!ModelState.IsValid) return View(model);

            try
            {
                model.CreatedBy = CurrentUser;
                model.CreatedDate = DateTimeHelper.PkNow;
                model.UpdatedBy = null;
                model.LastUpdated = null;

                _context.Notes.Add(model);
                await _context.SaveChangesAsync();

                TempData["Success"] = "Note added successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving note");
                ModelState.AddModelError("", $"Error saving note: {ex.Message}");
                return View(model);
            }
        }

        // GET: Notes/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            var note = await _context.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id);
            if (note == null) return NotFound();

            if (!CanModify(note))
            {
                TempData["Error"] = "You can only edit your own notes.";
                return RedirectToAction(nameof(Index));
            }

            return View(note);
        }

        // POST: Notes/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Note model)
        {
            if (id != model.Id) return NotFound();

            var note = await _context.Notes.AsTracking().FirstOrDefaultAsync(n => n.Id == id);
            if (note == null) return NotFound();

            if (!CanModify(note))
            {
                TempData["Error"] = "You can only edit your own notes.";
                return RedirectToAction(nameof(Index));
            }

            if (!ModelState.IsValid) return View(model);

            try
            {
                // Author and creation stamp stay with the original writer.
                note.Title = model.Title;
                note.Description = model.Description;
                note.IsPinned = model.IsPinned;
                note.UpdatedBy = CurrentUser;
                note.LastUpdated = DateTimeHelper.PkNow;

                await _context.SaveChangesAsync();

                TempData["Success"] = "Note updated successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating note {NoteId}", id);
                ModelState.AddModelError("", $"Error updating note: {ex.Message}");
                return View(model);
            }
        }

        // POST: Notes/TogglePin/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TogglePin(int id)
        {
            var note = await _context.Notes.AsTracking().FirstOrDefaultAsync(n => n.Id == id);
            if (note == null) return NotFound();

            if (!CanModify(note))
            {
                TempData["Error"] = "You can only pin your own notes.";
                return RedirectToAction(nameof(Index));
            }

            note.IsPinned = !note.IsPinned;
            note.UpdatedBy = CurrentUser;
            note.LastUpdated = DateTimeHelper.PkNow;
            await _context.SaveChangesAsync();

            TempData["Success"] = note.IsPinned ? "Note pinned to the top." : "Note unpinned.";
            return RedirectToAction(nameof(Index));
        }

        // POST: Notes/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var note = await _context.Notes.AsTracking().FirstOrDefaultAsync(n => n.Id == id);
            if (note == null) return NotFound();

            if (!CanModify(note))
            {
                TempData["Error"] = "You can only delete your own notes.";
                return RedirectToAction(nameof(Index));
            }

            _context.Notes.Remove(note);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Note deleted.";
            return RedirectToAction(nameof(Index));
        }
    }
}
