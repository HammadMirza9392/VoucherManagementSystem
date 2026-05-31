using System.ComponentModel.DataAnnotations;

namespace VoucherManagementSystem.Models
{
    public class Bank
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Bank name is required")]
        [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters")]
        [Display(Name = "Bank Name")]
        public string Name { get; set; } = string.Empty;

        [StringLength(50, ErrorMessage = "Account number cannot exceed 50 characters")]
        [Display(Name = "Account Number")]
        public string? AccountNumber { get; set; }

        [Display(Name = "Current Balance")]
        [Range(0, double.MaxValue, ErrorMessage = "Balance cannot be negative")]
        public decimal Balance { get; set; } = 0;

        [StringLength(500, ErrorMessage = "Details cannot exceed 500 characters")]
        [Display(Name = "Bank Details")]
        public string? Details { get; set; }

        [Display(Name = "Active Status")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "Created Date")]
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        [StringLength(100)]
        [Display(Name = "Created By")]
        public string? CreatedBy { get; set; }

        [Display(Name = "Updated Date")]
        public DateTime? UpdatedDate { get; set; }

        [StringLength(100)]
        [Display(Name = "Updated By")]
        public string? UpdatedBy { get; set; }

        // Navigation properties
        public virtual ICollection<Voucher>? PaidVouchers { get; set; }
        public virtual ICollection<Voucher>? ReceivedVouchers { get; set; }
    }
}