using System.ComponentModel.DataAnnotations;

namespace VoucherManagementSystem.Models
{
    public class MasterPassword
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string PasswordType { get; set; } = "MasterLock"; // Type identifier

        [Required]
        [MaxLength(500)]
        public string Password { get; set; } = "";

        public DateTime? LastModifiedDate { get; set; }

        [MaxLength(100)]
        public string? LastModifiedBy { get; set; }
    }
}
