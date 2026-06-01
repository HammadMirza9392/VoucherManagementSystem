namespace VoucherManagementSystem.Models
{
    public enum VoucherType
    {
        Purchase,
        Sale,
        Expense,
        Hazri,
        CashPaid,
        CashReceived,
        CCR,
        BCR,
        AdvancedPayment,
        AdvancedCashPaid,
        AdvancedCashReceived
    }

    public enum CashType
    {
        Credit,
        Cash,
        Bank
    }

    public enum TransactionStatus
    {
        Pending,
        Completed,
        Cancelled
    }
}