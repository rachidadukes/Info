namespace LegalHoldAdmin.Models;

public sealed class EJHeaderRecord
{
    public object? RecordID { get; set; }
    public object? BranchNumber { get; set; }
    public object? BranchID { get; set; }
    public object? UserID { get; set; }
    public object? TranNo { get; set; }
    public object? TranName { get; set; }
    public object? TranID { get; set; }
    public object? EJTime { get; set; }
    public object? CalendarDate { get; set; }
    public object? EnterpriseStatus { get; set; }
    public object? HostStatus { get; set; }
    public object? HostApproved { get; set; }
    public object? HostTimeOut { get; set; }
    public object? ForwardToHost { get; set; }
    public object? ForwardToHostApproved { get; set; }
    public object? ForwardToHostTimeOut { get; set; }
    public object? CustomerID { get; set; }
    public object? MultiTranNumber { get; set; }
    public object? AccountNumber { get; set; }
    public object? TranAmount { get; set; }
    public object? Passbook { get; set; }
    public object? BeginTime { get; set; }
    public object? CashBoxID { get; set; }
    public object? CashAccumInd { get; set; }
    public object? DeferredStatus { get; set; }
    public object? TranCommitted { get; set; }
    public object? SigCaptured { get; set; }
    public object? AccessNo { get; set; }
}
