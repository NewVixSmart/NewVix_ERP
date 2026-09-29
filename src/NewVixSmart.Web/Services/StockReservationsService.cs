using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;

namespace NewVixSmart.Web.Services;

public sealed class StockReservationsService : IStockReservationsService
{
    private const int MaxAttempts = 3;
    private readonly AppDbContext _db;

    public StockReservationsService(AppDbContext db) => _db = db;

    private static StockReservationStatus[] HoldingStatuses =>
    [
        StockReservationStatus.Active,
        StockReservationStatus.PartiallyConsumed
    ];

    public async Task<IReadOnlyList<ItemAvailability>> GetAvailabilityAsync(IEnumerable<int> itemIds)
    {
        var ids = itemIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await _db.Items.AsNoTracking()
            .Where(i => ids.Contains(i.Id))
            .Select(i => new ItemAvailability(i.Id, i.Name, i.CurrentQuantity, i.CurrentCount, i.ReservedQuantity, i.ReservedCount))
            .ToListAsync();
    }

    public async Task<ItemAvailability?> GetAvailabilityAsync(int itemId)
        => (await GetAvailabilityAsync([itemId])).FirstOrDefault();

    public async Task<StockReservation?> GetForOrderAsync(int salesOrderId)
    {
        var holding = HoldingStatuses;
        return await _db.StockReservations
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.SalesOrderId == salesOrderId && holding.Contains(r.Status));
    }

    public async Task<StockReservation?> GetByIdAsync(int reservationId)
        => await _db.StockReservations
            .Include(r => r.Items).ThenInclude(i => i.Item)
            .FirstOrDefaultAsync(r => r.Id == reservationId);

    public async Task<IReadOnlyList<StockReservation>> GetAllAsync(StockReservationStatus? status = null)
    {
        var query = _db.StockReservations.AsNoTracking()
            .Include(r => r.Items).ThenInclude(i => i.Item)
            .Include(r => r.SalesOrder)
            .Include(r => r.Customer)
            .AsQueryable();
        if (status.HasValue)
        {
            query = query.Where(r => r.Status == status.Value);
        }

        return await query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Take(500).ToListAsync();
    }

    public async Task<(bool Success, string? Error)> ReserveOrderAsync(int salesOrderId, string? user, bool beginOwnTransaction = true)
    {
        if (!beginOwnTransaction)
        {
            return await ReserveOrderCoreAsync(salesOrderId, user);
        }

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var result = await ReserveOrderCoreAsync(salesOrderId, user);
                if (!result.Success) { await tx.RollbackAsync(); DetachAll(); return result; }
                await tx.CommitAsync();
                return (true, null);
            }
            catch (DbUpdateConcurrencyException) { await tx.RollbackAsync(); DetachAll(); }
            catch (DbUpdateException) { await tx.RollbackAsync(); DetachAll(); }
        }
        return (false, "تعذر حفظ الحجز بسبب تعارض في البيانات، حاول مرة أخرى");
    }

    private async Task<(bool Success, string? Error)> ReserveOrderCoreAsync(int salesOrderId, string? user)
    {
        var order = await _db.SalesOrders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == salesOrderId);
        if (order == null)
        {
            return (false, "أمر البيع غير موجود");
        }

        if (order.Status == SalesOrderStatus.Draft)
        {
            return (false, "يمكن حجز الكميات لأمر معتمد فقط");
        }

        if (order.Status == SalesOrderStatus.Cancelled)
        {
            return (false, "لا يمكن الحجز لأمر ملغي");
        }

        var holding = HoldingStatuses;
        if (await _db.StockReservations.AnyAsync(r => r.SalesOrderId == order.Id && holding.Contains(r.Status)))
        {
            return (false, "يوجد حجز ساري لهذا الأمر بالفعل");
        }

        var wanted = order.Items
            .Where(i => !DeliveryOpenLines.IsLineSettled(i.Quantity, i.Count, i.DeliveredQty, i.DeliveredCount))
            .Select(i => new
            {
                Line = i,
                Quantity = Math.Max(0m, i.Quantity - i.DeliveredQty),
                Count = Math.Max(0m, i.Count - i.DeliveredCount)
            })
            .ToList();
        if (wanted.Count == 0)
        {
            return (false, "لا توجد كميات متبقية للحجز في هذا الأمر");
        }

        var items = await _db.Items
            .Where(i => wanted.Select(w => w.Line.ItemId).Contains(i.Id))
            .ToDictionaryAsync(i => i.Id);

        var shortages = new List<string>();
        foreach (var w in wanted)
        {
            if (!items.TryGetValue(w.Line.ItemId, out var item))
            {
                shortages.Add($"الصنف رقم {w.Line.ItemId} غير موجود");
                continue;
            }
            if (w.Quantity > item.AvailableQuantity)
            {
                shortages.Add($"«{item.Name}»: المطلوب {w.Quantity:N2} كمية والمتاح {item.AvailableQuantity:N2}");
            }

            if (w.Count > item.AvailableCount)
            {
                shortages.Add($"«{item.Name}»: المطلوب {w.Count:N2} عدد والمتاح {item.AvailableCount:N2}");
            }
        }
        if (shortages.Count > 0)
        {
            return (false, "الرصيد المتاح غير كافٍ للحجز — " + string.Join(" — ", shortages));
        }

        var reservation = new StockReservation
        {
            ReservationNumber = await NextReservationNumberAsync(),
            SalesOrderId = order.Id,
            CustomerId = order.CustomerId,
            Status = StockReservationStatus.Active,
            Reason = $"حجز كميات أمر البيع {order.OrderNumber}",
            CreatedBy = user,
            CreatedAt = DateTime.UtcNow
        };
        foreach (var w in wanted)
        {
            reservation.Items.Add(new StockReservationLine
            {
                ItemId = w.Line.ItemId,
                SalesOrderItemId = w.Line.Id,
                Quantity = w.Quantity,
                Count = w.Count
            });
        }

        _db.StockReservations.Add(reservation);
        await _db.SaveChangesAsync();

        await RecalculateForOrderAsync(order.Id, items.Keys);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error, StockReservation? Reservation)> CreateStandaloneAsync(
        StockReservation reservation, List<StockReservationLine> lines, string? user, bool beginOwnTransaction = true)
    {
        var valid = lines.Where(l => l.ItemId > 0 && (l.Quantity > 0 || l.Count > 0)).ToList();
        if (valid.Count == 0)
        {
            return (false, "يرجى إضافة صنف واحد على الأقل بالكمية أو العدد", null);
        }

        if (valid.Any(l => l.Quantity < 0 || l.Count < 0))
        {
            return (false, "الكمية أو العدد يجب ألا يكون سالباً", null);
        }

        if (valid.GroupBy(l => l.ItemId).Any(g => g.Count() > 1))
        {
            return (false, "لا يمكن إضافة الصنف نفسه في أكثر من سطر", null);
        }

        if (valid.Any(l => l.SalesOrderItemId.HasValue))
        {
            return (false, "الحجز المستقل لا يرتبط بسطر أمر بيع", null);
        }

        if (reservation.CustomerId.HasValue &&
            !await _db.Customers.AnyAsync(c => c.Id == reservation.CustomerId.Value))
        {
            return (false, "العميل غير موجود", null);
        }

        if (!beginOwnTransaction)
        {
            return await CreateStandaloneCoreAsync(reservation, valid, user);
        }

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var result = await CreateStandaloneCoreAsync(reservation, valid, user);
                if (!result.Success) { await tx.RollbackAsync(); DetachAll(); return result; }
                await tx.CommitAsync();
                return result;
            }
            catch (DbUpdateConcurrencyException) { await tx.RollbackAsync(); DetachAll(); }
            catch (DbUpdateException) { await tx.RollbackAsync(); DetachAll(); }
        }
        return (false, "تعذر حفظ الحجز بسبب تعارض في البيانات، حاول مرة أخرى", null);
    }

    private async Task<(bool Success, string? Error, StockReservation? Reservation)> CreateStandaloneCoreAsync(
        StockReservation reservation, List<StockReservationLine> valid, string? user)
    {
        var items = await _db.Items
            .Where(i => valid.Select(l => l.ItemId).Contains(i.Id))
            .ToDictionaryAsync(i => i.Id);

        var shortages = new List<string>();
        foreach (var line in valid)
        {
            if (!items.TryGetValue(line.ItemId, out var item))
            {
                shortages.Add($"الصنف رقم {line.ItemId} غير موجود");
                continue;
            }
            if (line.Quantity > item.AvailableQuantity)
            {
                shortages.Add($"«{item.Name}»: المطلوب {line.Quantity:N2} كمية والمتاح {item.AvailableQuantity:N2}");
            }

            if (line.Count > item.AvailableCount)
            {
                shortages.Add($"«{item.Name}»: المطلوب {line.Count:N2} عدد والمتاح {item.AvailableCount:N2}");
            }
        }
        if (shortages.Count > 0)
        {
            return (false, "الرصيد المتاح غير كافٍ للحجز — " + string.Join(" — ", shortages), null);
        }

        reservation.ReservationNumber = await NextReservationNumberAsync();
        reservation.Status = StockReservationStatus.Active;
        reservation.CreatedBy = user;
        reservation.CreatedAt = DateTime.UtcNow;
        reservation.Items = valid;

        _db.StockReservations.Add(reservation);
        await _db.SaveChangesAsync();

        await RecalculateForOrderAsync(null, items.Keys);
        await _db.SaveChangesAsync();
        return (true, null, reservation);
    }

    public async Task<(bool Success, string? Error)> ReleaseAsync(int reservationId, string? user, bool beginOwnTransaction = true)
    {
        if (!beginOwnTransaction)
        {
            return await ReleaseCoreAsync(reservationId, user);
        }

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var result = await ReleaseCoreAsync(reservationId, user);
                if (!result.Success) { await tx.RollbackAsync(); DetachAll(); return result; }
                await tx.CommitAsync();
                return (true, null);
            }
            catch (DbUpdateConcurrencyException) { await tx.RollbackAsync(); DetachAll(); }
            catch (DbUpdateException) { await tx.RollbackAsync(); DetachAll(); }
        }
        return (false, "تعذر تحرير الحجز بسبب تعارض في البيانات، حاول مرة أخرى");
    }

    private async Task<(bool Success, string? Error)> ReleaseCoreAsync(int reservationId, string? user)
    {
        var reservation = await _db.StockReservations.Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == reservationId);
        if (reservation == null)
        {
            return (false, "الحجز غير موجود");
        }

        if (reservation.Status is StockReservationStatus.Released or StockReservationStatus.Cancelled)
        {
            return (false, "الحجز محرَّر أو ملغي بالفعل");
        }

        if (reservation.Items.Any(i => i.ConsumedQuantity > 0 || i.ConsumedCount > 0))
        {
            return (false, "لا يمكن تحرير حجز تم استهلاك جزء منه");
        }

        var orderId = reservation.SalesOrderId;
        var itemIds = reservation.Items.Select(i => i.ItemId).Distinct().ToList();

        reservation.Status = StockReservationStatus.Released;
        reservation.ReleasedBy = user;
        reservation.ReleasedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await RecalculateForOrderAsync(orderId, itemIds);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task ReleaseForOrderAsync(int salesOrderId, string? user)
    {
        var holding = HoldingStatuses;
        var reservations = await _db.StockReservations.Include(r => r.Items)
            .Where(r => r.SalesOrderId == salesOrderId && holding.Contains(r.Status))
            .ToListAsync();
        if (reservations.Count == 0)
        {
            return;
        }

        var itemIds = reservations.SelectMany(r => r.Items).Select(i => i.ItemId).Distinct().ToList();
        foreach (var reservation in reservations)
        {
            if (reservation.Items.Any(i => i.ConsumedQuantity > 0 || i.ConsumedCount > 0))
            {
                continue;
            }

            reservation.Status = StockReservationStatus.Cancelled;
            reservation.ReleasedBy = user;
            reservation.ReleasedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        await RecalculateForOrderAsync(salesOrderId, itemIds);
        await _db.SaveChangesAsync();
    }

    public async Task<(bool Success, string? Error)> ConsumeForIssuesAsync(
        IReadOnlyCollection<DeliveryIssueItemLine> lines, int? customerId, DateTime date, bool beginOwnTransaction = true)
    {
        if (lines.Count == 0)
        {
            return (true, null);
        }

        if (!beginOwnTransaction)
        {
            return await ConsumeForIssuesCoreAsync(lines, customerId);
        }

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var result = await ConsumeForIssuesCoreAsync(lines, customerId);
                if (!result.Success) { await tx.RollbackAsync(); DetachAll(); return result; }
                await tx.CommitAsync();
                return (true, null);
            }
            catch (DbUpdateConcurrencyException) { await tx.RollbackAsync(); DetachAll(); }
            catch (DbUpdateException) { await tx.RollbackAsync(); DetachAll(); }
        }
        return (false, "تعذر استهلاك الحجز بسبب تعارض في البيانات، حاول مرة أخرى");
    }

    private async Task<(bool Success, string? Error)> ConsumeForIssuesCoreAsync(
        IReadOnlyCollection<DeliveryIssueItemLine> lines, int? customerId)
    {
        var holding = HoldingStatuses;
        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();

        var reservations = await _db.StockReservations.Include(r => r.Items)
            .Where(r => holding.Contains(r.Status) && r.Items.Any(i => itemIds.Contains(i.ItemId)))
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .ToListAsync();
        if (reservations.Count == 0)
        {
            return (true, null);
        }

        foreach (var line in lines)
        {
            // The need is the issued line's own figure, taken as it stands. Both it and the
            // reservation lines it is matched against are decimal(18,4), so rounding it here used to
            // discard a hundredth of the delivery before IsLineSettled was asked whether a
            // reservation was already spent, and before the leftover was written to
            // ConsumedQuantity - which handed the settled rule an answer computed from a number that
            // no longer existed.
            var needQty = line.Quantity;
            var needCnt = line.Count;
            if (needQty <= 0 && needCnt <= 0)
            {
                continue;
            }

            var candidates = reservations
                .Where(r => customerId == null || r.CustomerId == null || r.CustomerId == customerId)
                .SelectMany(r => r.Items.Where(i => i.ItemId == line.ItemId).Select(i => new { Reservation = r, Line = i }))
                .Where(x => !DeliveryOpenLines.IsLineSettled(
                    x.Line.Quantity, x.Line.Count, x.Line.ConsumedQuantity, x.Line.ConsumedCount))
                .OrderByDescending(x => line.SalesOrderItemId.HasValue && x.Line.SalesOrderItemId == line.SalesOrderItemId)
                .ThenBy(x => x.Reservation.CreatedAt).ThenBy(x => x.Reservation.Id).ThenBy(x => x.Line.Id)
                .ToList();

            foreach (var candidate in candidates)
            {
                if (needQty <= 0 && needCnt <= 0)
                {
                    break;
                }

                var takeQty = Math.Min(needQty, candidate.Line.Quantity - candidate.Line.ConsumedQuantity);
                var takeCnt = Math.Min(needCnt, candidate.Line.Count - candidate.Line.ConsumedCount);
                if (takeQty <= 0 && takeCnt <= 0)
                {
                    continue;
                }

                candidate.Line.ConsumedQuantity += takeQty;
                candidate.Line.ConsumedCount += takeCnt;
                needQty -= takeQty;
                needCnt -= takeCnt;
            }
        }

        foreach (var reservation in reservations)
        {
            var fullyConsumed = reservation.Items.All(i =>
                DeliveryOpenLines.IsLineSettled(i.Quantity, i.Count, i.ConsumedQuantity, i.ConsumedCount));
            var anyConsumed = reservation.Items.Any(i => i.ConsumedQuantity > 0 || i.ConsumedCount > 0);
            reservation.Status = fullyConsumed
                ? StockReservationStatus.Consumed
                : anyConsumed ? StockReservationStatus.PartiallyConsumed : StockReservationStatus.Active;
        }

        var touchedOrders = reservations.Where(r => r.SalesOrderId.HasValue)
            .Select(r => r.SalesOrderId!.Value).Distinct().ToList();
        await _db.SaveChangesAsync();
        foreach (var orderId in touchedOrders)
        {
            await RecalculateForOrderAsync(orderId, itemIds);
        }

        foreach (var itemId in itemIds)
        {
            await RecalculateItemReservedAsync(itemId);
        }

        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task RecalculateItemReservationsAsync(int itemId)
    {
        await _db.SaveChangesAsync();
        await RecalculateItemReservedAsync(itemId);
        await _db.SaveChangesAsync();
    }

    private async Task RecalculateItemReservedAsync(int itemId)
    {
        var holding = HoldingStatuses;
        var reserved = await _db.StockReservationLines
            .Where(l => l.ItemId == itemId && holding.Contains(l.StockReservation.Status))
            .SumAsync(l => (decimal?)(l.Quantity - l.ConsumedQuantity)) ?? 0m;
        var reservedCount = await _db.StockReservationLines
            .Where(l => l.ItemId == itemId && holding.Contains(l.StockReservation.Status))
            .SumAsync(l => (decimal?)(l.Count - l.ConsumedCount)) ?? 0m;

        var item = await _db.Items.FirstOrDefaultAsync(i => i.Id == itemId);
        if (item == null)
        {
            return;
        }

        if (item.CurrentQuantity < reserved || item.CurrentCount < reservedCount)
        {
            throw new InvalidOperationException(
                $"المحجوز يتجاوز رصيد الصنف «{item.Name}» — رصيد {item.CurrentQuantity:N2} كمية / {item.CurrentCount:N2} عدد ومحجوز {reserved:N2} / {reservedCount:N2}");
        }

        item.ReservedQuantity = reserved;
        item.ReservedCount = reservedCount;
    }

    private async Task RecalculateForOrderAsync(int? salesOrderId, IEnumerable<int> itemIds)
    {
        var holding = HoldingStatuses;
        var ids = itemIds.Distinct().ToList();

        if (salesOrderId.HasValue)
        {
            var orderLines = await _db.SalesOrderItems.Where(i => i.SalesOrderId == salesOrderId.Value).ToListAsync();
            if (orderLines.Count > 0)
            {
                var sums = await _db.StockReservationLines
                    .Where(l => l.StockReservation.SalesOrderId == salesOrderId.Value
                        && holding.Contains(l.StockReservation.Status)
                        && l.SalesOrderItemId != null)
                    .GroupBy(l => l.SalesOrderItemId!.Value)
                    .Select(g => new
                    {
                        OrderItemId = g.Key,
                        Quantity = g.Sum(x => x.Quantity - x.ConsumedQuantity),
                        Count = g.Sum(x => x.Count - x.ConsumedCount)
                    })
                    .ToListAsync();

                foreach (var line in orderLines)
                {
                    line.ReservedQty = 0m;
                    line.ReservedCount = 0m;
                }
                foreach (var sum in sums)
                {
                    var line = orderLines.FirstOrDefault(i => i.Id == sum.OrderItemId);
                    if (line == null)
                    {
                        continue;
                    }

                    line.ReservedQty = sum.Quantity;
                    line.ReservedCount = sum.Count;
                }
            }
        }

        foreach (var itemId in ids)
        {
            await RecalculateItemReservedAsync(itemId);
        }
    }

    private async Task<string> NextReservationNumberAsync()
    {
        var seriesPrefix = $"RSV-{DateTime.Now:yyyyMMdd}-";
        var values = await _db.StockReservations.AsNoTracking()
            .Where(r => r.ReservationNumber.StartsWith(seriesPrefix))
            .Select(r => r.ReservationNumber)
            .ToListAsync();
        int next = 1;
        foreach (var value in values)
        {
            if (value.Length > seriesPrefix.Length &&
                int.TryParse(value.AsSpan(seriesPrefix.Length), out var parsed) && parsed >= next)
            {
                next = parsed + 1;
            }
        }
        string num = $"{seriesPrefix}{next:D3}";
        while (await _db.StockReservations.AnyAsync(r => r.ReservationNumber == num))
        {
            next++;
            num = $"{seriesPrefix}{next:D3}";
        }
        return num;
    }

    private void DetachAll()
    {
        foreach (var entry in _db.ChangeTracker.Entries().ToList())
        {
            entry.State = EntityState.Detached;
        }
    }
}
