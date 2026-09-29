using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace NewVixSmart.Web.Infrastructure;

[AttributeUsage(AttributeTargets.Property)]
public sealed class DiscountWithinTotalAttribute : ValidationAttribute
{
    public const string DefaultErrorMessage = "خصم الصنف لا يمكن أن يتجاوز إجمالي السطر";

    public DiscountWithinTotalAttribute() : base(DefaultErrorMessage)
    {
    }

    public override string FormatErrorMessage(string name) => ErrorMessage ?? DefaultErrorMessage;

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not decimal discount)
        {
            return ValidationResult.Success;
        }

        var quantity = ReadDecimal(validationContext.ObjectInstance, "Quantity");
        var count = ReadDecimal(validationContext.ObjectInstance, "Count");
        var unitPrice = ReadDecimal(validationContext.ObjectInstance, "UnitPrice");
        if (quantity is null || count is null || unitPrice is null)
        {
            return ValidationResult.Success;
        }

        decimal gross = (quantity.Value > 0m ? quantity.Value : count.Value) * unitPrice.Value;
        return discount <= gross
            ? ValidationResult.Success
            : new ValidationResult(FormatErrorMessage(validationContext.DisplayName));
    }

    private static decimal? ReadDecimal(object instance, string name)
    {
        var property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        if (property is null || !property.CanRead)
        {
            return null;
        }

        return property.GetValue(instance) as decimal?;
    }
}
