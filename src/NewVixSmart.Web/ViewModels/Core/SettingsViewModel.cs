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

    /// <summary>
    /// The units that may be picked as somebody's parent unit: a root unit, or one that already
    /// has sub-units. A leaf with no children is not offered, because choosing it would leave a
    /// sub-unit list that can never be filled. This filter used to run as a LINQ <c>Where</c> in the
    /// settings markup, where it looked like a rendering detail rather than a units rule.
    /// </summary>
    public List<Unit> SelectableUnits => Units
        .Where(u => (u.SubUnits ?? 0) > 0 || u.ParentUnitId is null)
        .ToList();
}
