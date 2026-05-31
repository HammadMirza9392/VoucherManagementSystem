using System.ComponentModel.DataAnnotations;

namespace VoucherManagementSystem.Models
{
    public class Voucher
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        [Display(Name = "Transaction Number")]
        public string TransactionNumber { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Voucher Type")]
        public VoucherType VoucherType { get; set; }

        [Display(Name = "Cash Type")]
        public CashType? CashType { get; set; }

        [Display(Name = "Voucher Date")]
        [DataType(DataType.Date)]
        public DateTime VoucherDate { get; set; } = DateTime.Now;

        // Foreign Keys
        [Display(Name = "Purchasing Customer")]
        public int? PurchasingCustomerId { get; set; }

        [Display(Name = "Receiving Customer")]
        public int? ReceivingCustomerId { get; set; }

        [Display(Name = "Bank Paid")]
        public int? BankCustomerPaidId { get; set; }

        [Display(Name = "Bank Receiver")]
        public int? BankCustomerReceiverId { get; set; }

        [Display(Name = "Item")]
        public int? ItemId { get; set; }

        [Display(Name = "Expense Head")]
        public int? ExpenseHeadId { get; set; }

        [Display(Name = "Project")]
        public int? ProjectId { get; set; }

        // Numeric Fields
        [Display(Name = "Weight")]
        [Range(0, double.MaxValue, ErrorMessage = "Weight cannot be negative")]
        public decimal? Weight { get; set; }

        [Display(Name = "Kat")]
        public decimal? Kat { get; set; }

        [Display(Name = "Quantity")]
        [Range(0, double.MaxValue, ErrorMessage = "Quantity cannot be negative")]
        public decimal? Quantity { get; set; }

        [Display(Name = "Rate")]
        [Range(0, double.MaxValue, ErrorMessage = "Rate cannot be negative")]
        public decimal? Rate { get; set; }

        [Required]
        [Display(Name = "Amount")]
        [Range(0, double.MaxValue, ErrorMessage = "Amount cannot be negative")]
        public decimal Amount { get; set; } = 0;

        [Display(Name = "Expense Head Rate")]
        [Range(0, double.MaxValue, ErrorMessage = "Rate cannot be negative")]
        public decimal? ExpenseHeadRate { get; set; }

        // Text Fields
        [StringLength(100)]
        [Display(Name = "Mon")]
        public string? Mon { get; set; }

        [StringLength(100)]
        [Display(Name = "Gari No.")]
        public string? GariNo { get; set; }

        [StringLength(500)]
        [Display(Name = "Expense Details")]
        public string? ExpenseHeadDetails { get; set; }

        [Display(Name = "Include in Stock")]
        public bool StockInclude { get; set; } = false;

        [StringLength(500)]
        [Display(Name = "Purchasing Customer Details")]
        public string? PurchasingCustomerDetails { get; set; }

        [StringLength(500)]
        [Display(Name = "Receiving Customer Details")]
        public string? ReceivingCustomerDetails { get; set; }

        [StringLength(500)]
        [Display(Name = "Bank Paid Details")]
        public string? BankCustomerPaidDetails { get; set; }

        [StringLength(500)]
        [Display(Name = "Bank Receiver Details")]
        public string? BankCustomerReceiverDetails { get; set; }

        [Display(Name = "Status")]
        public TransactionStatus Status { get; set; } = TransactionStatus.Completed;

        [Display(Name = "Created Date")]
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        [Display(Name = "Created By")]
        public string? CreatedBy { get; set; }

        [Display(Name = "Updated Date")]
        public DateTime? UpdatedDate { get; set; }

        [StringLength(100)]
        [Display(Name = "Updated By")]
        public string? UpdatedBy { get; set; }

        // Navigation properties
        public virtual Customer? PurchasingCustomer { get; set; }
        public virtual Customer? ReceivingCustomer { get; set; }
        public virtual Bank? BankCustomerPaid { get; set; }
        public virtual Bank? BankCustomerReceiver { get; set; }
        public virtual Item? Item { get; set; }
        public virtual ExpenseHead? ExpenseHead { get; set; }
        public virtual Project? Project { get; set; }
    }
}