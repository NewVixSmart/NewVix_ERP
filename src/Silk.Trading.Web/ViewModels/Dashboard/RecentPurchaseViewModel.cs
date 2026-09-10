namespace Silk.Trading.Web.ViewModels.Dashboard;

public class RecentPurchaseViewModel
{
    public int Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string PartyName { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public decimal NetAmount { get; set; }
}