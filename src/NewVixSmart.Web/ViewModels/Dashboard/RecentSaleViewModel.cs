namespace NewVixSmart.Web.ViewModels.Dashboard;

public class RecentSaleViewModel
{
    public int Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string PartyName { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public decimal NetAmount { get; set; }
}