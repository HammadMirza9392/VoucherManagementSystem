using System.ComponentModel.DataAnnotations;

namespace VoucherManagementSystem.Models
{
    public class CustomerItemRate
    {
        public int Id { get; set; }

        [Required]
        public int CustomerId { get; set; }

        [Required]
        public int ItemId { get; set; }

        [Required]
        [Range(0, double.MaxValue, ErrorMessage = "Rate cannot be negative")]
        public decimal Rate { get; set; }

        // Navigation properties
        public virtual Customer? Customer { get; set; }
        public virtual Item? Item { get; set; }
    }
}