using System.ComponentModel.DataAnnotations;

namespace VoucherManagementSystem.Models
{
    // Model for tracking admin cash adjustments (deposits/withdrawals)
    public class CashAdjustment
    {
        public int Id { get; set; }

        [Required]
        [Display(Name = "Adjustment Date")]
        public DateTime AdjustmentDate { get; set; } = DateTime.Now;

        [Required]
        [Display(Name = "Adjustment Type")]
        public CashAdjustmentType AdjustmentType { get; set; }

        [Required]
        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
        [Display(Name = "Amount")]
        public decimal Amount { get; set; }

        [StringLength(500)]
        [Display(Name = "Description")]
        public string? Description { get; set; }

        [Display(Name = "Reference Number")]
        public string? ReferenceNumber { get; set; }

        [Display(Name = "Created By")]
        public string? CreatedBy { get; set; }

        [Display(Name = "Created Date")]
        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }

    public enum CashAdjustmentType
    {
        CashIn,     // Admin adds cash
        CashOut     // Admin removes cash
    }
}
