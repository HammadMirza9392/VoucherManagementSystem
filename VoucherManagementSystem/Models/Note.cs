using System.ComponentModel.DataAnnotations;
using VoucherManagementSystem.Helpers;

namespace VoucherManagementSystem.Models
{
    // Note Book entry — a free-form note (title + description) written by any logged-in user.
    // Notes are shared: everyone sees every note, but only the author or an Admin can edit or delete one.
    public class Note
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Title is required")]
        [MaxLength(200)]
        [Display(Name = "Title")]
        public string Title { get; set; } = "";

        [Required(ErrorMessage = "Description is required")]
        [MaxLength(5000)]
        [Display(Name = "Description")]
        public string Description { get; set; } = "";

        [Display(Name = "Pinned")]
        public bool IsPinned { get; set; } = false;

        [MaxLength(100)]
        [Display(Name = "Created By")]
        public string? CreatedBy { get; set; }

        [Display(Name = "Created Date")]
        public DateTime CreatedDate { get; set; } = DateTimeHelper.PkNow;

        [MaxLength(100)]
        [Display(Name = "Updated By")]
        public string? UpdatedBy { get; set; }

        [Display(Name = "Last Updated")]
        public DateTime? LastUpdated { get; set; }
    }
}
