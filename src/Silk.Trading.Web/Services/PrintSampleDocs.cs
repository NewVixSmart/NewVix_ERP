using Silk.Trading.Web.ViewModels.Core;
using Silk.Trading.Web.ViewModels.PrintStudio;

namespace Silk.Trading.Web.Services;

public static class PrintSampleDocs
{
    public static PrintDocModel Build(PrintGroup group, PrintLayoutOptions layout)
    {
        var today = DateTime.Today;
        var model = new PrintDocModel
        {
            Group = group,
            Layout = layout,
            DocDateLabel = "التاريخ",
            DocDateValue = today.ToString("dd/MM/yyyy"),
            PartyTitle = "جهة التعامل",
            PartyName = "شركة الأفق التجارية — العميل التجريبي",
            PartyCode = "CUST-0001",
            CompanyName = "سلك للتجارة",
            Tagline = "نظام التجارة الفاخر",
            CompanyContact = "القاهرة، مصر • 01000000000 • info@silk-trading.example",
            TaxNumber = "301-234-567",
            CreatedBy = "أحمد محمد",
            Note = "مستند تجريبي للعرض المباشر — البيانات غير حقيقية.",
            Totals = new PrintDocTotals
            {
                Subtotal = 5357m,
                Discount = 250m,
                Tax = 615m,
                GrandTotal = 5722m,
                IsPaid = false,
                CurrencyCode = "ج.م"
            }
        };

        switch (group)
        {
            case PrintGroup.SalesInvoice:
                model.DocTitle = "فاتورة بيع";
                model.DocNumber = "INV-2026-00123";
                model.PartyTitle = "العميل";
                AddInvoiceLines(model);
                break;
            case PrintGroup.PurchaseInvoice:
                model.DocTitle = "فاتورة شراء";
                model.DocNumber = "PINV-2026-00075";
                model.PartyTitle = "المورد";
                AddInvoiceLines(model);
                break;
            case PrintGroup.SalesQuote:
                model.DocTitle = "عرض سعر";
                model.DocNumber = "QTN-2026-00051";
                model.PartyTitle = "العميل";
                model.SecondDateLabel = "صالح حتى";
                model.SecondDateValue = today.AddDays(14).ToString("dd/MM/yyyy");
                AddInvoiceLines(model);
                break;
            case PrintGroup.ItemLabel:
                model.DocTitle = "ملصق صنف";
                model.DocNumber = "ITM-001";
                model.DocDateLabel = "تاريخ الطباعة";
                model.HidesPartyBlock = true;
                model.Note = null;
                model.CreatedBy = null;
                model.Totals = new PrintDocTotals
                {
                    Subtotal = 850m,
                    Discount = 0m,
                    Tax = 0m,
                    GrandTotal = 850m,
                    IsPaid = true,
                    CurrencyCode = ""
                };
                model.Lines.Add(new PrintDocLine
                {
                    ItemCode = "ITM-001",
                    Barcode = "6221000012345",
                    Name = "موتور مروحة 12 فولت",
                    Unit = "قطعة",
                    Count = 1m,
                    Quantity = 1m,
                    UnitPrice = 850m,
                    Discount = 0m,
                    LineTotal = 850m
                });
                break;
            case PrintGroup.FinancialReports:
                model.DocTitle = "ميزان المراجعة — بيان تجريبي";
                model.DocNumber = "TB-2026-Q2";
                model.DocDateLabel = "الفترة";
                model.DocDateValue = "الربع الثاني 2026";
                model.HidesPartyBlock = true;
                model.Note = null;
                model.Totals = new PrintDocTotals
                {
                    Subtotal = 379000m,
                    Discount = 0m,
                    Tax = 0m,
                    GrandTotal = 379000m,
                    IsPaid = false,
                    CurrencyCode = "ج.م"
                };
                AddAccountLines(model);
                break;
            case PrintGroup.CustomerStatement:
                model.DocTitle = "كشف حساب";
                model.DocNumber = "STMT-2026-00012";
                model.PartyTitle = "العميل";
                model.SecondDateLabel = "الفترة";
                model.SecondDateValue = "01/06/2026 — 30/06/2026";
                model.Totals = new PrintDocTotals
                {
                    Subtotal = 4300m,
                    Discount = 1000m,
                    Tax = 0m,
                    GrandTotal = 3300m,
                    IsPaid = false,
                    CurrencyCode = "ج.م"
                };
                AddStatementLines(model);
                break;
            case PrintGroup.SupplierStatement:
                model.DocTitle = "كشف حساب";
                model.DocNumber = "STMT-2026-00018";
                model.PartyTitle = "المورد";
                model.PartyName = "شركة الدلتا للمستلزمات — المورد التجريبي";
                model.PartyCode = "SUPP-0002";
                model.SecondDateLabel = "الفترة";
                model.SecondDateValue = "01/06/2026 — 30/06/2026";
                model.Totals = new PrintDocTotals
                {
                    Subtotal = 4300m,
                    Discount = 1000m,
                    Tax = 0m,
                    GrandTotal = 3300m,
                    IsPaid = false,
                    CurrencyCode = "ج.م"
                };
                AddStatementLines(model);
                break;
            case PrintGroup.StockTransfer:
                model.DocTitle = "تحويل مخزون";
                model.DocNumber = "ST-2026-00088";
                model.PartyTitle = "الفرع";
                model.Totals = new PrintDocTotals
                {
                    Subtotal = 30m,
                    Discount = 0m,
                    Tax = 0m,
                    GrandTotal = 30m,
                    IsPaid = false,
                    CurrencyCode = ""
                };
                AddTransferLines(model);
                break;
            case PrintGroup.PurchaseOrder:
                model.DocTitle = "أمر شراء";
                model.DocNumber = "PO-2026-00095";
                model.PartyTitle = "المورد";
                AddInvoiceLines(model);
                break;
            case PrintGroup.SaleReturn:
                model.DocTitle = "مرتجع بيع";
                model.DocNumber = "SR-2026-00042";
                model.PartyTitle = "العميل";
                AddInvoiceLines(model);
                break;
            case PrintGroup.PurchaseReturn:
                model.DocTitle = "مرتجع شراء";
                model.DocNumber = "PR-2026-00019";
                model.PartyTitle = "المورد";
                AddInvoiceLines(model);
                break;
        }

        return model;
    }

    private static void AddInvoiceLines(PrintDocModel model)
    {
        model.Lines.Add(new PrintDocLine
        {
            ItemCode = "ITM-001",
            Barcode = "6221000012345",
            Name = "موتور مروحة 12 فولت",
            Unit = "قطعة",
            Count = 25m,
            Quantity = 25m,
            UnitPrice = 85m,
            Discount = 5m,
            LineTotal = 2120m
        });
        model.Lines.Add(new PrintDocLine
        {
            ItemCode = "ITM-002",
            Barcode = "6221000023456",
            Name = "كابل تمديد 3 أمتار",
            Unit = "قطعة",
            Count = 60m,
            Quantity = 60m,
            UnitPrice = 22m,
            Discount = 0m,
            LineTotal = 1320m
        });
        model.Lines.Add(new PrintDocLine
        {
            ItemCode = "ITM-003",
            Barcode = "6221000034567",
            Name = "شاحن سيارة 45 واط",
            Unit = "قطعة",
            Count = 40m,
            Quantity = 40m,
            UnitPrice = 48m,
            Discount = 3m,
            LineTotal = 1917m
        });
    }

    private static void AddTransferLines(PrintDocModel model)
    {
        model.Lines.Add(new PrintDocLine
        {
            ItemCode = "ITM-001",
            Barcode = "6221000012345",
            Name = "موتور مروحة 12 فولت",
            Unit = "قطعة",
            Count = 10m,
            Quantity = 10m,
            UnitPrice = 0m,
            Discount = 0m,
            LineTotal = 10m
        });
        model.Lines.Add(new PrintDocLine
        {
            ItemCode = "ITM-002",
            Barcode = "6221000023456",
            Name = "كابل تمديد 3 أمتار",
            Unit = "قطعة",
            Count = 20m,
            Quantity = 20m,
            UnitPrice = 0m,
            Discount = 0m,
            LineTotal = 20m
        });
    }

    private static void AddStatementLines(PrintDocModel model)
    {
        model.Lines.Add(new PrintDocLine
        {
            ItemCode = "INV-2026-00123",
            Barcode = null,
            Name = "فاتورة بيع — 05/06/2026",
            Unit = "",
            Count = 1m,
            Quantity = 0m,
            UnitPrice = 0m,
            Discount = 0m,
            LineTotal = 2500m
        });
        model.Lines.Add(new PrintDocLine
        {
            ItemCode = "RCV-2026-00077",
            Barcode = null,
            Name = "سند قبض — 18/06/2026",
            Unit = "",
            Count = 1m,
            Quantity = 0m,
            UnitPrice = 0m,
            Discount = 0m,
            LineTotal = -1000m
        });
        model.Lines.Add(new PrintDocLine
        {
            ItemCode = "INV-2026-00142",
            Barcode = null,
            Name = "فاتورة بيع — 30/06/2026",
            Unit = "",
            Count = 1m,
            Quantity = 0m,
            UnitPrice = 0m,
            Discount = 0m,
            LineTotal = 1800m
        });
    }

    private static void AddAccountLines(PrintDocModel model)
    {
        model.Lines.Add(new PrintDocLine
        {
            ItemCode = "1101",
            Barcode = null,
            Name = "النقدية بالصناديق",
            Unit = "",
            Count = 0m,
            Quantity = 0m,
            UnitPrice = 0m,
            Discount = 0m,
            LineTotal = 125000m
        });
        model.Lines.Add(new PrintDocLine
        {
            ItemCode = "1202",
            Barcode = null,
            Name = "العملاء",
            Unit = "",
            Count = 0m,
            Quantity = 0m,
            UnitPrice = 0m,
            Discount = 0m,
            LineTotal = 48000m
        });
        model.Lines.Add(new PrintDocLine
        {
            ItemCode = "2101",
            Barcode = null,
            Name = "الموردون",
            Unit = "",
            Count = 0m,
            Quantity = 0m,
            UnitPrice = 0m,
            Discount = 0m,
            LineTotal = 23500m
        });
        model.Lines.Add(new PrintDocLine
        {
            ItemCode = "4101",
            Barcode = null,
            Name = "المبيعات",
            Unit = "",
            Count = 0m,
            Quantity = 0m,
            UnitPrice = 0m,
            Discount = 0m,
            LineTotal = 150000m
        });
        model.Lines.Add(new PrintDocLine
        {
            ItemCode = "5101",
            Barcode = null,
            Name = "المصروفات العمومية",
            Unit = "",
            Count = 0m,
            Quantity = 0m,
            UnitPrice = 0m,
            Discount = 0m,
            LineTotal = 32500m
        });
    }
}