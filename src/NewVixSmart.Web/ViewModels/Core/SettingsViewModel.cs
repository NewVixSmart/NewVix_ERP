using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;

namespace NewVixSmart.Web.ViewModels.Core;

public class SettingsViewModel
{
    public List<Unit> Units { get; set; } = new();
    public List<ItemCategory> Categories { get; set; } = new();
    public List<ItemType> ItemTypes { get; set; } = new();
    public List<Branch> Branches { get; set; } = new();
    public int? CurrentBranchId { get; set; }
}
