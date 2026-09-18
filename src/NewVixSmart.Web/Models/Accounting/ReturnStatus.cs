using System.ComponentModel.DataAnnotations;

namespace NewVixSmart.Web.Models.Accounting;

public enum ReturnStatus
{
    [Display(Name = "مسودة")]
    Draft = 0,
    [Display(Name = "مرحّلة")]
    Posted = 1
}
