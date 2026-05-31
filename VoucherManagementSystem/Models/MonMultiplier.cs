using System.ComponentModel.DataAnnotations;

namespace VoucherManagementSystem.Models
{
    public class MonMultiplier
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(20)]
        [Display(Name = "Voucher Type")]
        public string VoucherType { get; set; } = "";

        [Range(0.0001, 99999, ErrorMessage = "Multiplier must be greater than 0")]
        [Display(Name = "Multiplier")]
        public decimal Multiplier { get; set; } = 40;

        [MaxLength(200)]
        [Display(Name = "Description")]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime LastUpdated { get; set; } = DateTime.Now;

        [MaxLength(100)]
        public string? UpdatedBy { get; set; }
    }
}
