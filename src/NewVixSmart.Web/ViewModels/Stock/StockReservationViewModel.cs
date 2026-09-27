using Microsoft.AspNetCore.Mvc.Rendering;
using NewVixSmart.Web.Models.Stock;
using NewVixSmart.Web.Services;

namespace NewVixSmart.Web.ViewModels.Stock;

public class StockReservationViewModel
{
    public StockReservation Reservation { get; set; } = new();

    public List<StockReservationLine> StandaloneItems { get; set; } = new();

    public SelectList Customers { get; set; } = new(new List<object>());

    public List<ItemAvailability> Availability { get; set; } = new();
}
