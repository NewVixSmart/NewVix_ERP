using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Models.Access;
using Silk.Trading.Web.Models.Accounting;
using Silk.Trading.Web.Models.Core;
using Silk.Trading.Web.Models.Purchases;
using Silk.Trading.Web.Models.Sales;
using Silk.Trading.Web.Models.Stock;
namespace Silk.Trading.Web.Data;

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
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<InventoryAdjustment> InventoryAdjustments => Set<InventoryAdjustment>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
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
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<BudgetYear> BudgetYears => Set<BudgetYear>();
    public DbSet<BudgetLine> BudgetLines => Set<BudgetLine>();

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
            e.HasOne(i => i.Category).WithMany(c => c.Items).HasForeignKey(i => i.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.ItemType).WithMany(t => t.Items).HasForeignKey(i => i.ItemTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.CountUnit).WithMany().HasForeignKey(i => i.CountUnitId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.QuantityUnit).WithMany().HasForeignKey(i => i.QuantityUnitId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Supplier>(e =>
        {
            e.HasIndex(s => s.Code).IsUnique().HasFilter("[Code] IS NOT NULL");
            e.HasOne(s => s.Currency).WithMany().HasForeignKey(s => s.CurrencyId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Customer>(e =>
        {
            e.HasIndex(c => c.Code).IsUnique().HasFilter("[Code] IS NOT NULL");
            e.HasOne(c => c.Currency).WithMany().HasForeignKey(c => c.CurrencyId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Branch>(e =>
        {
            e.HasIndex(b => b.Code).IsUnique();
        });

        builder.Entity<Currency>(e =>
        {
            e.HasIndex(c => c.Code).IsUnique();
        });

        builder.Entity<PurchaseInvoice>(e =>
        {
            e.HasIndex(p => p.InvoiceNumber).IsUnique();
            e.HasOne(p => p.Supplier).WithMany(s => s.PurchaseInvoices).HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.Currency).WithMany().HasForeignKey(p => p.CurrencyId).OnDelete(DeleteBehavior.Restrict);
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
            e.HasOne(p => p.Supplier).WithMany(s => s.PurchaseReturns).HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.PurchaseInvoice).WithMany().HasForeignKey(p => p.PurchaseInvoiceId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(p => p.Currency).WithMany().HasForeignKey(p => p.CurrencyId).OnDelete(DeleteBehavior.Restrict);
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
            e.HasOne(s => s.Customer).WithMany(c => c.SaleInvoices).HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.Currency).WithMany().HasForeignKey(s => s.CurrencyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.Branch).WithMany().HasForeignKey(s => s.BranchId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SaleInvoiceItem>(e =>
        {
            e.HasOne(si => si.SaleInvoice).WithMany(s => s.Items).HasForeignKey(si => si.SaleInvoiceId);
            e.HasOne(si => si.Item).WithMany(i => i.SaleInvoiceItems).HasForeignKey(si => si.ItemId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SaleReturn>(e =>
        {
            e.HasIndex(s => s.ReturnNumber).IsUnique();
            e.HasOne(s => s.Customer).WithMany(c => c.SaleReturns).HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.SaleInvoice).WithMany().HasForeignKey(s => s.SaleInvoiceId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(s => s.Currency).WithMany().HasForeignKey(s => s.CurrencyId).OnDelete(DeleteBehavior.Restrict);
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
            e.HasOne(s => s.Customer).WithMany().HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.Currency).WithMany().HasForeignKey(s => s.CurrencyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.SaleInvoice).WithMany().HasForeignKey(s => s.SaleInvoiceId).OnDelete(DeleteBehavior.SetNull);
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
            e.HasOne(p => p.Customer).WithMany(c => c.Payments).HasForeignKey(p => p.CustomerId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(p => p.Supplier).WithMany(s => s.Payments).HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(p => p.Currency).WithMany().HasForeignKey(p => p.CurrencyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Branch>().WithMany().HasForeignKey(p => p.BranchId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PaymentAllocation>(e =>
        {
            e.HasIndex(a => new { a.PaymentId, a.InvoiceType, a.InvoiceId });
            e.Property(a => a.InvoiceType).HasColumnType("smallint");
            e.HasOne(a => a.Payment).WithMany(p => p.PaymentAllocations).HasForeignKey(a => a.PaymentId).OnDelete(DeleteBehavior.Restrict);
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
        });

        builder.Entity<JournalEntryLine>(e =>
        {
            e.HasOne(l => l.Account).WithMany().HasForeignKey(l => l.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.JournalEntry).WithMany(j => j.Lines).HasForeignKey(l => l.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Shipment>(e =>
        {
            e.HasIndex(s => s.ShipmentNumber).IsUnique();
            e.HasOne(s => s.SaleInvoice).WithMany().HasForeignKey(s => s.SaleInvoiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.PurchaseInvoice).WithMany().HasForeignKey(s => s.PurchaseInvoiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.Customer).WithMany().HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.Supplier).WithMany().HasForeignKey(s => s.SupplierId).OnDelete(DeleteBehavior.Restrict);
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
            e.HasOne(o => o.Supplier).WithMany(s => s.PurchaseOrders).HasForeignKey(o => o.SupplierId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PurchaseOrderItem>(e =>
        {
            e.HasOne(oi => oi.PurchaseOrder).WithMany(o => o.Items).HasForeignKey(oi => oi.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(oi => oi.Item).WithMany().HasForeignKey(oi => oi.ItemId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PurchaseInvoice>(e =>
        {
            e.HasOne(p => p.PurchaseOrder).WithMany().HasForeignKey(p => p.PurchaseOrderId).OnDelete(DeleteBehavior.SetNull);
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
    }
}
