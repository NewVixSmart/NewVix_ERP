using Microsoft.AspNetCore.Mvc.Rendering;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Forms;

namespace NewVixSmart.Web.ViewModels.Stock;

public class StockTransferFormViewModel
{
    public StockTransferFormModel Transfer { get; set; } = new();

    /// <summary>
    /// السطور كما وصلت من النموذج، أو صفٌّ واحد فارغ على GET الأول: بدونها كان أي خطأ
    /// يعيد الصفحة بصف واحد بأصفار ويهدر ما كتبه المشغّل في بقية السطور.
    /// </summary>
    public List<StockTransferLineFormModel> Items { get; set; } = [new()];

    public IEnumerable<SelectListItem>? Warehouses { get; set; }

    public List<Item> ItemsData { get; set; } = new();
}
