using System.Text;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class ImportExportCenterTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public ImportExportCenterTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private static ImportCenterService CreateImportService(AppDbContext db)
        => new(db, null!, null!, null!, new MemoryCache(new MemoryCacheOptions()));

    private static byte[] CsvBytes(string text) => Encoding.UTF8.GetBytes(text);

    // ---------- Import round-trips ----------

    [Fact]
    public async Task Import_ItemCategories_ArabicHeadersWithIgnoredColumn_Persists()
    {
        using var db = CreateContext();
        var svc = CreateImportService(db);

        var csv = CsvBytes("اسم التصنيف,ملاحظات,الحالة,عدد الأصناف\n" +
                           "أجهزة إلكترونية,ملاحظة أولى,نشط,10\n" +
                           "ملابس,,معطل,0\n");
        var vm = await svc.ParseAsync("itemCategories", "cats.csv", csv);
        Assert.Null(vm.FatalError);
        Assert.Equal(2, vm.ValidCount);
        Assert.Equal(0, vm.ErrorCount);

        var result = await svc.ImportAsync("itemCategories", vm.Payload, vm.ApplyToken!);
        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.Created);

        var categories = await db.ItemCategories.OrderBy(c => c.Name).ToListAsync();
        Assert.Equal(2, categories.Count);
        Assert.Contains(categories, c => c.Name == "أجهزة إلكترونية" && c.IsActive && c.Notes == "ملاحظة أولى");
        Assert.Contains(categories, c => c.Name == "ملابس" && !c.IsActive);
    }

    [Fact]
    public async Task Import_ItemTypes_RoundTrip_Persists()
    {
        using var db = CreateContext();
        var svc = CreateImportService(db);

        var csv = CsvBytes("اسم النوع,الحالة\n" +
                           "جاهز,نشط\n" +
                           "خام,معطل\n");
        var vm = await svc.ParseAsync("itemTypes", "types.csv", csv);
        Assert.Null(vm.FatalError);
        Assert.Equal(2, vm.ValidCount);

        var result = await svc.ImportAsync("itemTypes", vm.Payload, vm.ApplyToken!);
        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.Created);

        Assert.Equal(2, await db.ItemTypes.CountAsync());
    }

    [Fact]
    public async Task Import_Suppliers_NotesColumn_IsPersisted()
    {
        using var db = CreateContext();
        var svc = CreateImportService(db);

        var csv = CsvBytes("الكود,الاسم,ملاحظات\n" +
                           "SUP-9,مورد تجريبي,ملاحظة محفوظة\n");
        var vm = await svc.ParseAsync("suppliers", "s.csv", csv);
        Assert.Null(vm.FatalError);
        Assert.Equal(1, vm.ValidCount);

        var result = await svc.ImportAsync("suppliers", vm.Payload, vm.ApplyToken!);
        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.Created);

        var supplier = Assert.Single(await db.Suppliers.ToListAsync());
        Assert.Equal("SUP-9", supplier.Code);
        Assert.Equal("ملاحظة محفوظة", supplier.Notes);
    }

    [Fact]
    public async Task Import_Currencies_IsRejectedAsUnknownDataset()
    {
        // The currency dataset is gone with the feature, so the importer must refuse it
        // instead of silently accepting rows it can no longer store.
        using var db = CreateContext();
        var svc = CreateImportService(db);

        var csv = CsvBytes("الرمز,الاسم,رمز العملة,سعر الصرف,الحالة\n" +
                           "USD,دولار أمريكي,$,3.75,نشط\n");
        var vm = await svc.ParseAsync("currencies", "cur.csv", csv);

        Assert.NotNull(vm.FatalError);
    }

    [Fact]
    public async Task Import_ApplyToken_IsSingleUse()
    {
        using var db = CreateContext();
        var svc = CreateImportService(db);

        var csv = CsvBytes("الكود,الاسم\n" +
                           "B1,فرع واحد\n");
        var vm = await svc.ParseAsync("branches", "b.csv", csv);
        Assert.Null(vm.FatalError);

        var first = await svc.ImportAsync("branches", vm.Payload, vm.ApplyToken!);
        Assert.True(first.Success, first.Message);

        var second = await svc.ImportAsync("branches", vm.Payload, vm.ApplyToken!);
        Assert.False(second.Success);
        Assert.Equal(1, await db.Branches.CountAsync());
    }

    // ---------- Import stock-counter / opening-balance guards ----------

    [Fact]
    public async Task Import_Items_IgnoresStockBalanceColumns()
    {
        using var db = CreateContext();
        db.ItemTypes.Add(new NewVixSmart.Web.Models.Core.ItemType { Name = "منتج" });
        db.ItemCategories.Add(new NewVixSmart.Web.Models.Core.ItemCategory { Name = "تصنيف" });
        await db.SaveChangesAsync();
        var svc = CreateImportService(db);

        var csv = CsvBytes("الكود,الاسم,النوع,التصنيف,الرصيد (عدد),الرصيد (كمية)\n" +
                           "IT-1,منتج أول,منتج,تصنيف,50,20\n");
        var vm = await svc.ParseAsync("items", "items.csv", csv);
        Assert.Null(vm.FatalError);
        Assert.Equal(1, vm.ValidCount);

        var result = await svc.ImportAsync("items", vm.Payload, vm.ApplyToken!);
        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.Created);

        var item = Assert.Single(db.Items);
        Assert.Equal(0m, item.CurrentCount);
        Assert.Equal(0m, item.CurrentQuantity);
    }

    [Fact]
    public async Task Import_Customers_PreservesExistingOpeningBalance()
    {
        using var db = CreateContext();
        db.Customers.Add(new NewVixSmart.Web.Models.Sales.Customer { Code = "C-1", Name = "عميل قديم", OpeningBalance = 100m });
        await db.SaveChangesAsync();
        var svc = CreateImportService(db);

        var csv = CsvBytes("الكود,الاسم,الرصيد الافتتاحي\n" +
                           "C-1,عميل قديم,250\n" +
                           "C-2,عميل جديد,300\n");
        var vm = await svc.ParseAsync("customers", "c.csv", csv);
        Assert.Null(vm.FatalError);
        Assert.Equal(2, vm.ValidCount);

        var result = await svc.ImportAsync("customers", vm.Payload, vm.ApplyToken!);
        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Updated);

        var existing = await db.Customers.AsNoTracking().SingleAsync(c => c.Code == "C-1");
        var created = await db.Customers.AsNoTracking().SingleAsync(c => c.Code == "C-2");
        Assert.Equal(100m, existing.OpeningBalance);
        Assert.Equal(300m, created.OpeningBalance);
    }

    // ---------- Export formula-injection guards ----------

    [Fact]
    public async Task Export_SuppliersCsv_NeutralizesFormulaCells()
    {
        using var db = CreateContext();
        db.Suppliers.Add(new Models.Purchases.Supplier
        {
            Name = "=1+1",
            Code = "X-1",
            Notes = "@cmd",
            TaxNumber = "=HYPERLINK(\"http://x\")"
        });
        await db.SaveChangesAsync();

        var csv = Encoding.UTF8.GetString(await new ExportCenterService(db).SuppliersCsvAsync());
        Assert.Contains("'=1+1", csv);
        Assert.Contains("'@cmd", csv);
        Assert.Contains("'=HYPERLINK(\"\"http://x\"\")", csv);
        Assert.DoesNotContain("\n=1+1", csv);
    }

    [Fact]
    public async Task Export_SuppliersXlsx_NeutralizesFormulaCells()
    {
        using var db = CreateContext();
        db.Suppliers.Add(new Models.Purchases.Supplier
        {
            Name = "+cmd",
            Code = "X-2",
            Notes = "=1+1"
        });
        await db.SaveChangesAsync();

        var bytes = await new ExportCenterService(db).SuppliersXlsxAsync();
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheets.First();
        var nameCell = ws.Cell(2, 2);
        var notesCell = ws.Cell(2, 8);
        Assert.Equal(XLDataType.Text, nameCell.DataType);
        Assert.Equal(XLDataType.Text, notesCell.DataType);
        Assert.Contains("+cmd", nameCell.GetString());
        Assert.Contains("=1+1", notesCell.GetString());
    }

    [Fact]
    public async Task ReportExport_SalesToCsv_NeutralizesNewlineInjection()
    {
        using var db = CreateContext();
        var customer = new NewVixSmart.Web.Models.Sales.Customer { Name = "\n=HYPERLINK(\"http://evil\")" };
        db.Customers.Add(customer);
        db.SaleInvoices.Add(new NewVixSmart.Web.Models.Sales.SaleInvoice
        {
            InvoiceNumber = "SI-INJ-1",
            CustomerId = customer.Id,
            Customer = customer,
            InvoiceDate = DateTime.Today,
            TotalAmount = 100m,
            Discount = 0m,
            Tax = 0m,
            NetAmount = 100m
        });
        await db.SaveChangesAsync();

        var svc = new ReportExportService(db);
        var salesCsv = Encoding.UTF8.GetString((await svc.SalesToCsv(DateTime.Today.AddDays(-1), DateTime.Today)).Bytes);
        Assert.DoesNotContain("\r\n=HYPERLINK(", salesCsv);
        Assert.Contains("'\n=HYPERLINK", salesCsv);

        db.Payments.Add(new NewVixSmart.Web.Models.Accounting.Payment
        {
            Type = NewVixSmart.Web.Models.Accounting.PaymentType.Receipt,
            CustomerId = customer.Id,
            ReceiptNumber = "RP-INJ-1",
            Amount = 50m,
            PaymentDate = DateTime.Today,
            Method = NewVixSmart.Web.Models.Accounting.PaymentMethod.Cash
        });
        await db.SaveChangesAsync();

        var paymentsCsv = Encoding.UTF8.GetString((await svc.PaymentsToCsv(DateTime.Today.AddDays(-1), DateTime.Today)).Bytes);
        Assert.DoesNotContain("\r\n=HYPERLINK(", paymentsCsv);
        Assert.DoesNotContain("\r\n=cmd", paymentsCsv);
    }

    // ---------- Import robustness (Round-7) ----------

    [Fact]
    public async Task Import_Supplier_DecimalComma_IsNotMultipliedByTen()
    {
        using var db = CreateContext();
        var svc = CreateImportService(db);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("موردون");
        ws.Cell(1, 1).Value = "الكود";
        ws.Cell(1, 2).Value = "الاسم";
        ws.Cell(1, 3).Value = "الرصيد الافتتاحي";
        ws.Cell(2, 1).Value = "S-D";
        ws.Cell(2, 2).Value = "مورد المفصولة";
        ws.Cell(2, 3).Value = "1,5";
        using var ms = new MemoryStream();
        wb.SaveAs(ms);

        var vm = await svc.ParseAsync("suppliers", "s.xlsx", ms.ToArray());
        Assert.Null(vm.FatalError);
        Assert.Equal(1, vm.ValidCount);
        Assert.Equal(0, vm.ErrorCount);

        var result = await svc.ImportAsync("suppliers", vm.Payload, vm.ApplyToken!);
        Assert.True(result.Success, result.Message);

        var supplier = await db.Suppliers.SingleAsync(s => s.Code == "S-D");
        Assert.Equal(1.5m, supplier.OpeningBalance);
    }

    [Fact]
    public async Task Import_Supplier_SemicolonSeparatedCsv_WithDecimalComma_ParsesCorrectly()
    {
        using var db = CreateContext();
        var svc = CreateImportService(db);

        var csv = CsvBytes("الكود;الاسم;الرصيد الافتتاحي\nS-1;مورد الفاصلة المنقوطة;1,5\n");
        var vm = await svc.ParseAsync("suppliers", "s.csv", csv);
        Assert.Null(vm.FatalError);
        Assert.Equal(1, vm.ValidCount);
        Assert.Equal(0, vm.ErrorCount);

        var result = await svc.ImportAsync("suppliers", vm.Payload, vm.ApplyToken!);
        Assert.True(result.Success, result.Message);

        var supplier = await db.Suppliers.SingleAsync(s => s.Code == "S-1");
        Assert.Equal("مورد الفاصلة المنقوطة", supplier.Name);
        Assert.Equal(1.5m, supplier.OpeningBalance);
    }

    [Fact]
    public async Task Import_Account_ParentCode_ForwardReference_IsLinked()
    {
        using var db = CreateContext();
        var svc = CreateImportService(db);

        var csv = CsvBytes("كود الحساب,اسم الحساب,نوع الحساب,طبيعة الحساب,الحساب الأب,الحالة\n" +
                           "520001,خدمات فرعية,مصروف,مدين,520000,نشط\n" +
                           "520000,الخدمات,مصروف,مدين,,نشط\n");
        var vm = await svc.ParseAsync("glAccounts", "acc.csv", csv);
        Assert.Null(vm.FatalError);
        Assert.Equal(2, vm.ValidCount);
        Assert.Equal(0, vm.ErrorCount);

        var result = await svc.ImportAsync("glAccounts", vm.Payload, vm.ApplyToken!);
        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.Created);

        var child = await db.GLAccounts.Include(a => a.ParentAccount).SingleAsync(a => a.Code == "520001");
        Assert.NotNull(child.ParentAccount);
        Assert.Equal("520000", child.ParentAccount.Code);
    }

    [Fact]
    public async Task Import_SupplierUpdate_BlankCell_KeepsExistingValue()
    {
        using var db = CreateContext();
        var svc = CreateImportService(db);

        var first = CsvBytes("الكود,الاسم,التليفون,ملاحظات\nS-U,مورد التحديث,01111,ملاحظة أولى\n");
        var vm1 = await svc.ParseAsync("suppliers", "s1.csv", first);
        var r1 = await svc.ImportAsync("suppliers", vm1.Payload, vm1.ApplyToken!);
        Assert.True(r1.Success, r1.Message);

        var second = CsvBytes("الكود,الاسم,التليفون,ملاحظات\nS-U,مورد التحديث,,ملاحظة ثانية\n");
        var vm2 = await svc.ParseAsync("suppliers", "s2.csv", second);
        Assert.Equal(1, vm2.ValidCount);
        var r2 = await svc.ImportAsync("suppliers", vm2.Payload, vm2.ApplyToken!);
        Assert.True(r2.Success, r2.Message);
        Assert.Equal(0, r2.Created);
        Assert.Equal(1, r2.Updated);

        var supplier = await db.Suppliers.SingleAsync(s => s.Code == "S-U");
        Assert.Equal("01111", supplier.Phone);
        Assert.Equal("ملاحظة ثانية", supplier.Notes);
    }

    [Fact]
    public async Task Import_Supplier_DuplicateName_SameBatch_BecomesUpdateNotInsert()
    {
        using var db = CreateContext();
        var svc = CreateImportService(db);

        var csv = CsvBytes("الكود,الاسم,التليفون\nS-A,مورد مكرر,111\nS-B,مورد مكرر,222\n");
        var vm = await svc.ParseAsync("suppliers", "s.csv", csv);
        Assert.Null(vm.FatalError);
        Assert.Equal(2, vm.ValidCount);

        var result = await svc.ImportAsync("suppliers", vm.Payload, vm.ApplyToken!);
        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Updated);

        Assert.Equal(1, await db.Suppliers.CountAsync(s => s.Name == "مورد مكرر"));
    }
}
