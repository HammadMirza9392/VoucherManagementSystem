using System.ComponentModel.DataAnnotations;

namespace VoucherManagementSystem.Models
{
    public class Item
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Item name is required")]
        [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters")]
        [Display(Name = "Item Name")]
        public string Name { get; set; } = string.Empty;

        [StringLength(50, ErrorMessage = "Unit cannot exceed 50 characters")]
        [Display(Name = "Unit of Measurement")]
        public string? Unit { get; set; }

        [Display(Name = "Enable Stock Tracking")]
        public bool StockTrackingEnabled { get; set; } = true;

        [Display(Name = "Current Stock")]
        [Range(0, double.MaxValue, ErrorMessage = "Stock cannot be negative")]
        public decimal CurrentStock { get; set; } = 0;

        [Display(Name = "Default Rate")]
        [Range(0, double.MaxValue, ErrorMessage = "Rate cannot be negative")]
        public decimal DefaultRate { get; set; } = 0;

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
        public virtual ICollection<CustomerItemRate>? CustomerItemRates { get; set; }
        public virtual ICollection<Voucher>? Vouchers { get; set; }
    }
}
