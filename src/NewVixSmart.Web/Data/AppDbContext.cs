using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewVixSmart.Web.Models.Access;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;
namespace NewVixSmart.Web.Data;

public class AppDbContext : IdentityDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ItemCategory> ItemCategories => Set<ItemCategory>();
    public DbSet<ItemType> ItemTypes => Set<ItemType>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();
    public DbSet<PurchaseInvoiceItem> PurchaseInvoiceItems => Set<PurchaseInvoiceItem>();
    public DbSet<PurchaseReturn> PurchaseReturns => Set<PurchaseReturn>();
    public DbSet<PurchaseReturnItem> PurchaseReturnItems => Set<PurchaseReturnItem>();
    public DbSet<SaleInvoice> SaleInvoices => Set<SaleInvoice>();
    public DbSet<SaleInvoiceItem> SaleInvoiceItems => Set<SaleInvoiceItem>();
    public DbSet<SaleReturn> SaleReturns => Set<SaleReturn>();
    public DbSet<SaleReturnItem> SaleReturnItems => Set<SaleReturnItem>();
    public DbSet<SaleQuote> SaleQuotes => Set<SaleQuote>();
    public DbSet<SaleQuoteItem> SaleQuoteItems => Set<SaleQuoteItem>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderItem> SalesOrderItems => Set<SalesOrderItem>();
    public DbSet<DeliveryOrder> DeliveryOrders => Set<DeliveryOrder>();
    public DbSet<DeliveryOrderItem> DeliveryOrderItems => Set<DeliveryOrderItem>();
    public DbSet<DeliveryIssue> DeliveryIssues => Set<DeliveryIssue>();
    public DbSet<DeliveryIssueItem> DeliveryIssueItems => Set<DeliveryIssueItem>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();
    public DbSet<StockReservationLine> StockReservationLines => Set<StockReservationLine>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<InventoryAdjustment> InventoryAdjustments => Set<InventoryAdjustment>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<SalePaymentAllocation> SalePaymentAllocations => Set<SalePaymentAllocation>();
    public DbSet<PurchasePaymentAllocation> PurchasePaymentAllocations => Set<PurchasePaymentAllocation>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<SupplierQuote> SupplierQuotes => Set<SupplierQuote>();
    public DbSet<GLAccount> GLAccounts => Set<GLAccount>();
    public DbSet<FiscalPeriod> FiscalPeriods => Set<FiscalPeriod>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalEntryLine> JournalEntryLines => Set<JournalEntryLine>();
    public DbSet<StockLayer> StockLayers => Set<StockLayer>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferItem> StockTransferItems => Set<StockTransferItem>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<BudgetYear> BudgetYears => Set<BudgetYear>();
    public DbSet<BudgetLine> BudgetLines => Set<BudgetLine>();
    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ItemCategory>(e =>
        {
            e.HasIndex(c => c.Name).IsUnique();
        });

        builder.Entity<ItemType>(e =>
        {
            e.HasIndex(t => t.Name).IsUnique();
        });

        builder.Entity<Unit>(e =>
        {
            e.HasIndex(u => u.Name).IsUnique();
            e.HasOne(u => u.ParentUnit).WithMany().HasForeignKey(u => u.ParentUnitId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Item>(e =>
        {
            e.HasIndex(i => i.Code).IsUnique().HasFilter("[Code] IS NOT NULL");
            e.HasIndex(i => i.Name).IsUnique();
            e.HasIndex(i => i.Barcode).IsUnique().HasFilter("[Barcode] IS NOT NULL");
            e.HasIndex(i => i.PublicId).IsUnique();
            e.Property(i => i.PublicId).ValueGeneratedNever();
            e.HasOne(i => i.Category).WithMany(c => c.Items).HasForeignKey(i => i.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.ItemType).WithMany(t => t.Items).HasForeignKey(i => i.ItemTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.CountUnit).WithMany().HasForeignKey(i => i.CountUnitId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.QuantityUnit).WithMany().HasForeignKey(i => i.QuantityUnitId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Supplier>(e =>
        {
            e.HasIndex(s => s.Code).IsUnique().HasFilter("[Code] IS NOT NULL");
            e.HasIndex(s => s.Name).IsUnique();
        });

        builder.Entity<Customer>(e =>
        {
            e.HasIndex(c => c.Code).IsUnique().HasFilter("[Code] IS NOT NULL");
            e.HasIndex(c => c.Name).IsUnique();
        });

        builder.Entity<Branch>(e =>
        {
            e.HasIndex(b => b.Code).IsUnique();
        });


        builder.Entity<PurchaseInvoice>(e =>
        {
            e.HasIndex(p => p.InvoiceNumber).IsUnique();
            e.HasIndex(p => p.PublicId).IsUnique();
            e.Property(p => p.PublicId).ValueGeneratedNever();
            e.HasIndex(p => p.PurchaseOrderId).IsUnique().HasFilter("[PurchaseOrderId] IS NOT NULL");
            e.HasOne(p => p.Supplier).WithMany(s => s.PurchaseInvoices).HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.Branch).WithMany().HasForeignKey(p => p.BranchId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PurchaseInvoiceItem>(e =>
        {
            e.HasOne(pi => pi.PurchaseInvoice).WithMany(p => p.Items).HasForeignKey(pi => pi.PurchaseInvoiceId);
            e.HasOne(pi => pi.Item).WithMany(i => i.PurchaseInvoiceItems).HasForeignKey(pi => pi.ItemId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PurchaseReturn>(e =>
        {
            e.HasIndex(p => p.ReturnNumber).IsUnique();
            e.HasIndex(p => p.PublicId).IsUnique();
            e.Property(p => p.PublicId).ValueGeneratedNever();
            e.Property(p => p.RowVersion).IsRowVersion();
            e.HasOne(p => p.Supplier).WithMany(s => s.PurchaseReturns).HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.PurchaseInvoice).WithMany().HasForeignKey(p => p.PurchaseInvoiceId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Branch>().WithMany().HasForeignKey(p => p.BranchId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PurchaseReturnItem>(e =>
        {
            e.HasOne(pi => pi.PurchaseReturn).WithMany(p => p.Items).HasForeignKey(pi => pi.PurchaseReturnId);
            e.HasOne(pi => pi.Item).WithMany(i => i.PurchaseReturnItems).HasForeignKey(pi => pi.ItemId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SaleInvoice>(e =>
        {
            e.HasIndex(s => s.InvoiceNumber).IsUnique();
            e.HasIndex(s => s.PublicId).IsUnique();
            e.Property(s => s.PublicId).ValueGeneratedNever();
            e.HasIndex(s => s.SalesOrderId).HasFilter("[SalesOrderId] IS NOT NULL");
            e.HasOne(s => s.Customer).WithMany(c => c.SaleInvoices).HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.Branch).WithMany().HasForeignKey(s => s.BranchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.SalesOrder).WithMany(o => o.Invoices).HasForeignKey(s => s.SalesOrderId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<SaleInvoiceItem>(e =>
        {
            e.HasOne(si => si.SaleInvoice).WithMany(s => s.Items).HasForeignKey(si => si.SaleInvoiceId);
            e.HasOne(si => si.Item).WithMany(i => i.SaleInvoiceItems).HasForeignKey(si => si.ItemId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SaleReturn>(e =>
        {
            e.HasIndex(s => s.ReturnNumber).IsUnique();
            e.HasIndex(s => s.PublicId).IsUnique();
            e.Property(s => s.PublicId).ValueGeneratedNever();
            e.Property(s => s.RowVersion).IsRowVersion();
            e.HasOne(s => s.Customer).WithMany(c => c.SaleReturns).HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.SaleInvoice).WithMany().HasForeignKey(s => s.SaleInvoiceId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Branch>().WithMany().HasForeignKey(s => s.BranchId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SaleReturnItem>(e =>
        {
            e.HasOne(si => si.SaleReturn).WithMany(s => s.Items).HasForeignKey(si => si.SaleReturnId);
            e.HasOne(si => si.Item).WithMany(i => i.SaleReturnItems).HasForeignKey(si => si.ItemId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SaleQuote>(e =>
        {
            e.HasIndex(s => s.QuoteNumber).IsUnique();
            e.HasIndex(s => s.PublicId).IsUnique();
            e.Property(s => s.PublicId).ValueGeneratedNever();
            e.HasOne(s => s.Customer).WithMany().HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.SaleInvoice).WithMany().HasForeignKey(s => s.SaleInvoiceId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(s => s.SalesOrder).WithMany().HasForeignKey(s => s.SalesOrderId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(s => s.SupplierQuote).WithMany().HasForeignKey(s => s.SupplierQuoteId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<SaleQuoteItem>(e =>
        {
            e.HasOne(si => si.SaleQuote).WithMany(s => s.Items).HasForeignKey(si => si.SaleQuoteId);
            e.HasOne(si => si.Item).WithMany().HasForeignKey(si => si.ItemId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<StockMovement>(e =>
        {
            e.HasOne(sm => sm.Item).WithMany(i => i.StockMovements).HasForeignKey(sm => sm.ItemId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<InventoryAdjustment>(e =>
        {
            e.HasIndex(ia => ia.ReferenceNumber).IsUnique();
            e.HasOne(ia => ia.Item).WithMany().HasForeignKey(ia => ia.ItemId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Payment>(e =>
        {
            e.HasIndex(p => p.ReceiptNumber).IsUnique();
            e.HasIndex(p => p.PublicId).IsUnique();
            e.Property(p => p.PublicId).ValueGeneratedNever();
            e.HasIndex(p => p.DedupeKey).IsUnique().HasFilter("[DedupeKey] IS NOT NULL");
            e.HasOne(p => p.Customer).WithMany(c => c.Payments).HasForeignKey(p => p.CustomerId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(p => p.Supplier).WithMany(s => s.Payments).HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Branch>().WithMany().HasForeignKey(p => p.BranchId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SalePaymentAllocation>(e =>
        {
            e.HasIndex(a => new { a.PaymentId, a.SaleInvoiceId }).IsUnique();
            e.HasOne(a => a.Payment).WithMany(p => p.SalePaymentAllocations).HasForeignKey(a => a.PaymentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.SaleInvoice).WithMany().HasForeignKey(a => a.SaleInvoiceId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PurchasePaymentAllocation>(e =>
        {
            e.HasIndex(a => new { a.PaymentId, a.PurchaseInvoiceId }).IsUnique();
            e.HasOne(a => a.Payment).WithMany(p => p.PurchasePaymentAllocations).HasForeignKey(a => a.PaymentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.PurchaseInvoice).WithMany().HasForeignKey(a => a.PurchaseInvoiceId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SupplierQuote>(e =>
        {
            e.HasIndex(q => new { q.SupplierId, q.ItemId }).IsUnique();
            e.HasOne(q => q.Supplier).WithMany().HasForeignKey(q => q.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(q => q.Item).WithMany().HasForeignKey(q => q.ItemId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<UserPermission>(e =>
        {
            e.HasIndex(p => new { p.UserId, p.PermissionKey }).IsUnique();
        });

        builder.Entity<GLAccount>(e =>
        {
            e.HasIndex(a => a.Code).IsUnique();
            e.HasOne(a => a.ParentAccount).WithMany().HasForeignKey(a => a.ParentAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Branch>().WithMany().HasForeignKey(a => a.BranchId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<FiscalPeriod>(e =>
        {
            e.HasIndex(p => p.Year).IsUnique();
        });

        builder.Entity<JournalEntry>(e =>
        {
            e.HasIndex(j => j.EntryNumber).IsUnique();
            e.HasIndex(j => new { j.Source, j.SourceId });
        });

        builder.Entity<JournalEntryLine>(e =>
        {
            e.HasOne(l => l.Account).WithMany().HasForeignKey(l => l.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.JournalEntry).WithMany(j => j.Lines).HasForeignKey(l => l.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<StockLayer>(e =>
        {
            e.HasOne(sl => sl.Item).WithMany().HasForeignKey(sl => sl.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(sl => new { sl.WarehouseId, sl.ItemId });
        });

        builder.Entity<Warehouse>(e =>
        {
            e.HasIndex(w => w.Code).IsUnique();
        });

        builder.Entity<StockTransfer>(e =>
        {
            e.HasIndex(t => t.TransferNumber).IsUnique();
            e.HasOne(t => t.SourceWarehouse).WithMany().HasForeignKey(t => t.SourceWarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.TargetWarehouse).WithMany().HasForeignKey(t => t.TargetWarehouseId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<StockTransferItem>(e =>
        {
            e.HasOne(ti => ti.StockTransfer).WithMany(t => t.Items).HasForeignKey(ti => ti.StockTransferId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(ti => ti.Item).WithMany().HasForeignKey(ti => ti.ItemId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PurchaseOrder>(e =>
        {
            e.HasIndex(o => o.OrderNumber).IsUnique();
            e.HasIndex(o => o.PublicId).IsUnique();
            e.Property(o => o.PublicId).ValueGeneratedNever();
            e.HasOne(o => o.Supplier).WithMany(s => s.PurchaseOrders).HasForeignKey(o => o.SupplierId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PurchaseOrderItem>(e =>
        {
            e.Property(oi => oi.RowVersion).IsRowVersion();
            e.HasOne(oi => oi.PurchaseOrder).WithMany(o => o.Items).HasForeignKey(oi => oi.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(oi => oi.Item).WithMany().HasForeignKey(oi => oi.ItemId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PurchaseInvoice>(e =>
        {
            e.HasOne(p => p.PurchaseOrder).WithMany().HasForeignKey(p => p.PurchaseOrderId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<SalesOrder>(e =>
        {
            e.HasIndex(o => o.OrderNumber).IsUnique();
            e.HasIndex(o => o.PublicId).IsUnique();
            e.Property(o => o.PublicId).ValueGeneratedNever();
            e.HasOne(o => o.Customer).WithMany().HasForeignKey(o => o.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(o => o.SaleQuote).WithMany().HasForeignKey(o => o.SaleQuoteId).OnDelete(DeleteBehavior.SetNull);
        });
        builder.Entity<SalesOrderItem>(e =>
        {
            e.Property(oi => oi.RowVersion).IsRowVersion();
            e.HasOne(oi => oi.SalesOrder).WithMany(o => o.Items).HasForeignKey(oi => oi.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(oi => oi.Item).WithMany().HasForeignKey(oi => oi.ItemId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<DeliveryOrder>(e =>
        {
            e.HasIndex(d => d.DeliveryNumber).IsUnique();
            e.HasIndex(d => d.PublicId).IsUnique();
            e.Property(d => d.PublicId).ValueGeneratedNever();
            e.Property(d => d.RowVersion).IsRowVersion();
            e.HasOne(d => d.SaleInvoice).WithMany().HasForeignKey(d => d.SaleInvoiceId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(d => d.SalesOrder).WithMany(o => o.DeliveryOrders).HasForeignKey(d => d.SalesOrderId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(d => d.StockReservation).WithMany().HasForeignKey(d => d.StockReservationId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(d => d.Customer).WithMany().HasForeignKey(d => d.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable("DeliveryOrders", t => t.HasCheckConstraint("CK_DeliveryOrders_SingleSource",
                "([SalesOrderId] IS NULL OR [SaleInvoiceId] IS NULL)"));
        });

        builder.Entity<DeliveryOrderItem>(e =>
        {
            e.HasOne(di => di.DeliveryOrder).WithMany(d => d.Items).HasForeignKey(di => di.DeliveryOrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(di => di.Item).WithMany().HasForeignKey(di => di.ItemId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<DeliveryIssue>(e =>
        {
            e.HasIndex(d => d.IssueNumber).IsUnique();
            e.HasIndex(d => d.PublicId).IsUnique();
            e.Property(d => d.PublicId).ValueGeneratedNever();
            e.Property(d => d.RowVersion).IsRowVersion();
            e.HasOne(d => d.DeliveryOrder).WithMany(p => p.Issues).HasForeignKey(d => d.DeliveryOrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(d => d.Customer).WithMany().HasForeignKey(d => d.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(d => d.SalesOrder).WithMany().HasForeignKey(d => d.SalesOrderId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(d => d.SaleInvoice).WithMany(i => i.DeliveryIssues).HasForeignKey(d => d.SaleInvoiceId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<DeliveryIssueItem>(e =>
        {
            e.HasOne(di => di.DeliveryIssue).WithMany(d => d.Items).HasForeignKey(di => di.DeliveryIssueId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(di => di.DeliveryOrderItem).WithMany().HasForeignKey(di => di.DeliveryOrderItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(di => di.Item).WithMany().HasForeignKey(di => di.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(di => di.SalesOrderItem).WithMany().HasForeignKey(di => di.SalesOrderItemId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<StockReservation>(e =>
        {
            e.HasIndex(r => r.ReservationNumber).IsUnique();
            e.HasIndex(r => r.PublicId).IsUnique();
            e.Property(r => r.PublicId).ValueGeneratedNever();
            e.Property(r => r.RowVersion).IsRowVersion();
            e.HasOne(r => r.SalesOrder).WithMany(o => o.Reservations).HasForeignKey(r => r.SalesOrderId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(r => r.Customer).WithMany().HasForeignKey(r => r.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<StockReservationLine>(e =>
        {
            e.HasOne(l => l.StockReservation).WithMany(r => r.Items).HasForeignKey(l => l.StockReservationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.Item).WithMany().HasForeignKey(l => l.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.SalesOrderItem).WithMany().HasForeignKey(l => l.SalesOrderItemId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<BudgetYear>(e =>
        {
            e.HasIndex(b => b.Year).IsUnique();
        });

        builder.Entity<BudgetLine>(e =>
        {
            e.HasIndex(b => new { b.BudgetYearId, b.AccountId }).IsUnique();
            e.HasOne(b => b.BudgetYear).WithMany(y => y.Lines).HasForeignKey(b => b.BudgetYearId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(b => b.Account).WithMany().HasForeignKey(b => b.AccountId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<CompanyProfile>(e =>
        {
            e.Property(p => p.LogoData);
            e.Property(p => p.FaviconData);
        });

        builder.Entity<SystemSetting>(e =>
        {
            e.Property(s => s.Key).HasMaxLength(100);
        });

        ConfigureDecimalPrecision(builder);
    }

    /// <summary>
    /// Widen the quantity and unit-price columns that the model files still declare as
    /// <c>decimal(18,2)</c>, and assert - rather than widen - the money columns.
    ///
    /// The model classes carry the historical <c>decimal(18,2)</c> in a <see cref="ColumnAttribute"/>,
    /// which silently rounded a third decimal off every quantity and every unit price on write: a
    /// quantity of 0.125 became 0.13, a price of 12.345 became 12.35, and the stock valuation and
    /// the journal entry then disagreed with the document the operator typed. The store type is
    /// therefore restated here, in the one place a migration can see it, and the two widths are kept
    /// apart on purpose: <see cref="DecimalPrecision.QuantityScale"/> for measured amounts,
    /// <see cref="DecimalPrecision.PriceScale"/> for prices, and
    /// <see cref="DecimalPrecision.MoneyScale"/> - unchanged - for money, because widening a money
    /// column would contradict <see cref="Services.Money.Format"/> and the 0.005 money materiality
    /// constant the ledger is balanced against.
    /// </summary>
    private static void ConfigureDecimalPrecision(ModelBuilder builder)
    {
        builder.Entity<Item>(e =>
        {
            e.Property(i => i.PurchasePrice).HasPricePrecision();
            e.Property(i => i.SalePrice).HasPricePrecision();

            e.Property(i => i.MinCount).HasQuantityPrecision();
            e.Property(i => i.MinQuantity).HasQuantityPrecision();
            e.Property(i => i.CurrentCount).HasQuantityPrecision();
            e.Property(i => i.CurrentQuantity).HasQuantityPrecision();
            e.Property(i => i.ReservedCount).HasQuantityPrecision();
            e.Property(i => i.ReservedQuantity).HasQuantityPrecision();
        });

        builder.Entity<PurchaseInvoiceItem>(e =>
        {
            e.Property(i => i.Quantity).HasQuantityPrecision();
            e.Property(i => i.Count).HasQuantityPrecision();
            e.Property(i => i.UnitPrice).HasPricePrecision();
            e.Property(i => i.Discount).HasMoneyPrecision();
        });

        builder.Entity<PurchaseOrderItem>(e =>
        {
            e.Property(i => i.Quantity).HasQuantityPrecision();
            e.Property(i => i.Count).HasQuantityPrecision();
            e.Property(i => i.UnitPrice).HasPricePrecision();
            e.Property(i => i.ReceivedQty).HasQuantityPrecision();
            e.Property(i => i.ReceivedCount).HasQuantityPrecision();
        });

        builder.Entity<PurchaseReturnItem>(e =>
        {
            e.Property(i => i.Quantity).HasQuantityPrecision();
            e.Property(i => i.Count).HasQuantityPrecision();
            e.Property(i => i.UnitPrice).HasPricePrecision();
        });

        builder.Entity<SaleInvoiceItem>(e =>
        {
            e.Property(i => i.Quantity).HasQuantityPrecision();
            e.Property(i => i.Count).HasQuantityPrecision();
            e.Property(i => i.UnitPrice).HasPricePrecision();
            e.Property(i => i.Discount).HasMoneyPrecision();
        });

        builder.Entity<SaleQuoteItem>(e =>
        {
            e.Property(i => i.Quantity).HasQuantityPrecision();
            e.Property(i => i.Count).HasQuantityPrecision();
            e.Property(i => i.UnitPrice).HasPricePrecision();
        });

        builder.Entity<SaleReturnItem>(e =>
        {
            e.Property(i => i.Quantity).HasQuantityPrecision();
            e.Property(i => i.Count).HasQuantityPrecision();
            e.Property(i => i.UnitPrice).HasPricePrecision();
        });

        builder.Entity<SalesOrderItem>(e =>
        {
            e.Property(i => i.Quantity).HasQuantityPrecision();
            e.Property(i => i.Count).HasQuantityPrecision();
            e.Property(i => i.UnitPrice).HasPricePrecision();
            e.Property(i => i.InvoicedQty).HasQuantityPrecision();
            e.Property(i => i.InvoicedCount).HasQuantityPrecision();
            e.Property(i => i.ReservedQty).HasQuantityPrecision();
            e.Property(i => i.ReservedCount).HasQuantityPrecision();
            e.Property(i => i.DeliveredQty).HasQuantityPrecision();
            e.Property(i => i.DeliveredCount).HasQuantityPrecision();
        });

        builder.Entity<DeliveryOrderItem>(e =>
        {
            e.Property(i => i.Quantity).HasQuantityPrecision();
            e.Property(i => i.Count).HasQuantityPrecision();
        });

        builder.Entity<DeliveryIssueItem>(e =>
        {
            e.Property(i => i.Quantity).HasQuantityPrecision();
            e.Property(i => i.Count).HasQuantityPrecision();
        });

        builder.Entity<StockReservationLine>(e =>
        {
            e.Property(l => l.Quantity).HasQuantityPrecision();
            e.Property(l => l.Count).HasQuantityPrecision();
            e.Property(l => l.ConsumedQuantity).HasQuantityPrecision();
            e.Property(l => l.ConsumedCount).HasQuantityPrecision();
        });

        builder.Entity<StockMovement>(e =>
        {
            e.Property(m => m.Quantity).HasQuantityPrecision();
            e.Property(m => m.Count).HasQuantityPrecision();
            e.Property(m => m.BalanceBefore).HasQuantityPrecision();
            e.Property(m => m.BalanceAfter).HasQuantityPrecision();
            e.Property(m => m.CountBefore).HasQuantityPrecision();
            e.Property(m => m.CountAfter).HasQuantityPrecision();
        });

        builder.Entity<StockLayer>(e =>
        {
            e.Property(l => l.Qty).HasQuantityPrecision();
            e.Property(l => l.Count).HasQuantityPrecision();
            e.Property(l => l.RemainingQty).HasQuantityPrecision();
            e.Property(l => l.RemainingCount).HasQuantityPrecision();
            e.Property(l => l.UnitCost).HasCostPrecision();
            e.Property(l => l.CountCost).HasCostPrecision();
        });

        builder.Entity<StockTransferItem>(e =>
        {
            e.Property(i => i.Quantity).HasQuantityPrecision();
            e.Property(i => i.Count).HasQuantityPrecision();
            e.Property(i => i.UnitCost).HasCostPrecision();
        });

        builder.Entity<InventoryAdjustment>(e =>
        {
            e.Property(a => a.NewCount).HasQuantityPrecision();
            e.Property(a => a.NewQuantity).HasQuantityPrecision();
        });

        builder.Entity<SupplierQuote>(e => e.Property(q => q.UnitPrice).HasPricePrecision());

        ConfigureMoneyPrecision(builder);
    }

    /// <summary>
    /// Restates <see cref="MoneyScale"/> on every money column. It is the width they already have, so
    /// it emits no DDL, but stating it here makes the money contract machine-checkable: the
    /// <c>Precision*Tests</c> suite asserts the scale of every money column, so a later blanket widen
    /// that would break the piastre contract fails the build instead of quietly changing the ledger.
    /// </summary>
    private static void ConfigureMoneyPrecision(ModelBuilder builder)
    {
        builder.Entity<PurchaseInvoice>(e =>
        {
            e.Property(i => i.TotalAmount).HasMoneyPrecision();
            e.Property(i => i.Discount).HasMoneyPrecision();
            e.Property(i => i.Discount2).HasMoneyPrecision();
            e.Property(i => i.Discount3).HasMoneyPrecision();
            e.Property(i => i.Tax).HasMoneyPrecision();
            e.Property(i => i.NetAmount).HasMoneyPrecision();
            e.Property(i => i.PaidAmount).HasMoneyPrecision();
        });

        builder.Entity<SaleInvoice>(e =>
        {
            e.Property(i => i.TotalAmount).HasMoneyPrecision();
            e.Property(i => i.Discount).HasMoneyPrecision();
            e.Property(i => i.Discount2).HasMoneyPrecision();
            e.Property(i => i.Discount3).HasMoneyPrecision();
            e.Property(i => i.Tax).HasMoneyPrecision();
            e.Property(i => i.NetAmount).HasMoneyPrecision();
            e.Property(i => i.PaidAmount).HasMoneyPrecision();
        });

        builder.Entity<SaleQuote>(e =>
        {
            e.Property(q => q.TotalAmount).HasMoneyPrecision();
            e.Property(q => q.Discount).HasMoneyPrecision();
            e.Property(q => q.Tax).HasMoneyPrecision();
            e.Property(q => q.NetAmount).HasMoneyPrecision();
        });

        builder.Entity<PurchaseReturn>(e => e.Property(r => r.TotalAmount).HasMoneyPrecision());
        builder.Entity<SaleReturn>(e => e.Property(r => r.TotalAmount).HasMoneyPrecision());
        builder.Entity<Supplier>(e => e.Property(s => s.OpeningBalance).HasMoneyPrecision());
        builder.Entity<Customer>(e => e.Property(c => c.OpeningBalance).HasMoneyPrecision());
        builder.Entity<Payment>(e => e.Property(p => p.Amount).HasMoneyPrecision());
        builder.Entity<SalePaymentAllocation>(e => e.Property(a => a.AllocatedAmount).HasMoneyPrecision());
        builder.Entity<PurchasePaymentAllocation>(e => e.Property(a => a.AllocatedAmount).HasMoneyPrecision());
        builder.Entity<JournalEntryLine>(e =>
        {
            e.Property(l => l.Debit).HasMoneyPrecision();
            e.Property(l => l.Credit).HasMoneyPrecision();
        });
        builder.Entity<BudgetLine>(e => e.Property(l => l.AnnualAmount).HasMoneyPrecision());
    }
}

/// <summary>
/// The one place that decides how many decimals each kind of decimal column stores.
/// <para>
/// Three widths, kept apart on purpose. Money stays at <see cref="MoneyScale"/> because the Egyptian
/// piastre is the smallest practical amount and <see cref="Services.Money"/> formats to two decimals:
/// a wider money column would let the ledger hold a figure no screen, report or printed document can
/// show, and it would invalidate the 0.005 money materiality constant the payment allocator is
/// balanced against. A quantity is a measured physical amount, not an amount of money, so it is not
/// held to the piastre grid and is widened to <see cref="QuantityScale"/>. A unit price sits between
/// the two and is widened to <see cref="PriceScale"/>.
/// </para>
/// <para>
/// Both the widened columns matter for data fidelity. At the old <c>decimal(18,2)</c> a quantity of
/// 0.125 was silently stored as 0.13 and a price of 12.345 as 12.35, so the stock valuation and the
/// journal entry disagreed with the document the operator typed, and the difference was invisible.
/// Four decimals on a quantity is the smallest width that both holds a weight measured to a tenth of
/// a gram and keeps the money error a quantity truncation introduces under the 0.005 materiality
/// constant: the rounding error is at most 0.00005 per unit, which stays below 0.005 for any unit
/// price under 100 L.E, whereas a 3-decimal quantity would already exceed it above 10 L.E. Three
/// decimals on a price is the smallest width that round-trips the price the system already computes.
/// </para>
/// </summary>
internal static class DecimalPrecision
{
    /// <summary>Store precision shared by every money, price, quantity and cost column.</summary>
    internal const int StorePrecision = 18;

    /// <summary>Scale of a money amount. Unchanged, and asserted by the test suite.</summary>
    internal const int MoneyScale = 2;

    /// <summary>Scale of an operator-entered unit price or unit cost.</summary>
    internal const int PriceScale = 3;

    /// <summary>Scale of a quantity or a count.</summary>
    internal const int QuantityScale = 4;

    /// <summary>
    /// Scale of a per-unit cost held in the stock valuation layer. This one is not new: the FIFO layer
    /// has always stored a unit cost at six decimals, and it is why the quantity could not stay at two.
    /// A three-decimal cost multiplied by a three-decimal quantity is a nine-decimal product, so
    /// rounding either factor before the multiply is what made the valuation drift away from the
    /// document. Restating the width here emits no DDL - the column is already
    /// <c>decimal(18,6)</c> - but it puts the cost width on the same declared footing as the others so
    /// the test suite can check all of them.
    /// </summary>
    internal const int CostScale = 6;

    /// <summary>
    /// Restates both the <c>Precision</c>/<c>Scale</c> annotations and the explicit
    /// <c>ColumnType</c>, because the model classes carry a
    /// <c>[Column(TypeName = "decimal(18,2)")]</c> annotation that only the explicit column type can
    /// override - <c>HasPrecision</c> alone would lose to it.
    /// </summary>
    internal static PropertyBuilder HasStoreType(this PropertyBuilder property, int scale)
    {
        var columnType = $"decimal({StorePrecision},{scale})";
        property.HasPrecision(StorePrecision, scale);
        return property.HasColumnType(columnType);
    }

    /// <summary>Holds the column at the quantity width.</summary>
    internal static PropertyBuilder HasQuantityPrecision(this PropertyBuilder property)
        => HasStoreType(property, QuantityScale);

    /// <summary>Holds the column at the unit price width.</summary>
    internal static PropertyBuilder HasPricePrecision(this PropertyBuilder property)
        => HasStoreType(property, PriceScale);

    /// <summary>Holds the column at the money width.</summary>
    internal static PropertyBuilder HasMoneyPrecision(this PropertyBuilder property)
        => HasStoreType(property, MoneyScale);

    /// <summary>Holds the column at the per-unit cost width.</summary>
    internal static PropertyBuilder HasCostPrecision(this PropertyBuilder property)
        => HasStoreType(property, CostScale);
}
