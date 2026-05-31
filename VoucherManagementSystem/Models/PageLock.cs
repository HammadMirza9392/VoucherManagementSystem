using System.ComponentModel.DataAnnotations;

namespace VoucherManagementSystem.Models
{
    public class PageLock
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string PageName { get; set; } = "";

        [Required]
        [MaxLength(200)]
        public string PageUrl { get; set; } = "";

        public bool IsLocked { get; set; } = false;

        [MaxLength(500)]
        public string? Password { get; set; }

        // Lock mode: "JustView" = lock after navigating away, "Login" = stay unlocked during session
        [MaxLength(20)]
        public string LockMode { get; set; } = "JustView";

        public DateTime? LastModifiedDate { get; set; }

        [MaxLength(100)]
        public string? LastModifiedBy { get; set; }
    }
}
