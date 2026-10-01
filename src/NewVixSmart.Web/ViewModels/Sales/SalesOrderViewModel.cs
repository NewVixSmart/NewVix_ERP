using Microsoft.AspNetCore.Mvc.Rendering;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Forms;
using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.ViewModels.Sales;

/// <summary>
/// نموذج العرض لشاشتي إنشاء/تعديل أمر البيع.
/// <para>
/// الحمولة المربوطة هي <see cref="SalesOrderFormModel"/> و<code>SalesOrderLineFormModel</code>
/// من <c>Models.Forms</c> — لا كيان <see cref="SalesOrder"/>.
/// </para>
/// <para>
/// أسماء الخصائص هي ما يحدّد بادئات الحقول المرسلة، فبقيت <c>Order</c> و<code>Items</code>
/// على حالهما: يبقى عقد النماذج والعروض كما هو بلا تعديل، ويصير الخادم وحده هو من يملك
/// رقم الأمر وحالته.
/// </para>
/// </summary>
public class SalesOrderViewModel
{
    public SalesOrderFormModel Order { get; set; } = new();
    public List<SalesOrderLineFormModel> Items { get; set; } = new();
    public IEnumerable<SelectListItem>? Customers { get; set; }
    public List<Item> ItemsData { get; set; } = new();
}
