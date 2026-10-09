using System.Globalization;
using System.Text.Json;
using Portal.Application.Commercial.Reports;
using Portal.Domain.Commercial.ProductionOrders;
using Portal.Domain.Commercial.Reports;

namespace Portal.Infrastructure.Commercial.Reports;

public static class OpRegisterCapture
{
    public static ProductionOrderReportRecord Create(ProductionOrder order, Guid actor, string seller, DateTimeOffset now, OpRecordData? prior = null)
    {
        string Number(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";
        var code = $"OP-{order.CreatedAt.Year}-{order.Consecutive:00000}";
        var data = new OpRecordData
        {
            PortalCode = code, RegistryId = order.Id, RegistryVersion = (prior?.RegistryVersion ?? 0) + 1,
            Origin = prior?.Origin ?? "Portal: datos enviados a Producción",
            CommercialOverrides = (prior?.CommercialOverrides ?? []).Where(i => i is >= 0 and < 21).Distinct().ToArray(),
            Cells =
        [order.CustomerOrderNumber ?? "", order.CreatedAt.ToString("yyyy-MM-dd"), order.QuotationNumber ?? "",
            order.ClientPurchaseOrder ?? "", "", order.ClientName ?? "", order.ReferenceNumber ?? "", "", "", order.ProductName ?? "",
            string.Join(" / ", order.Materials.OrderBy(m => m.Position).Select(m => m.Material)),
            string.Join(" / ", order.PrintLines.OrderBy(p => p.Position).Select(p => p.Inks)),
            order.ClosedSize ?? order.OpenSize ?? "", string.Join(" / ", order.Finishes.OrderBy(f => f.Position).Select(f => f.Specification)),
            "", order.DeliveryDate?.ToString("yyyy-MM-dd") ?? "", "", seller, Number(order.Quantity),
            Number(order.UnitValue), Number(order.Quantity * order.UnitValue)]
        };
        // Commercial fields belong to the historical register; a later Portal dispatch must not overwrite them.
        // Empty corrections are intentional too, so copy by changed index rather than by non-empty value.
        foreach (var index in data.CommercialOverrides)
            data.Cells[index] = prior?.Cells.ElementAtOrDefault(index) ?? "";
        return new ProductionOrderReportRecord
        {
            Id = Guid.NewGuid(), ProductionOrderId = order.Id, OrderVersion = order.Version,
            Number = data.Cells[0][..Math.Min(data.Cells[0].Length, 100)], Code = code,
            Client = data.Cells[5][..Math.Min(data.Cells[5].Length, 500)], Product = data.Cells[9][..Math.Min(data.Cells[9].Length, 500)],
            DataJson = JsonSerializer.Serialize(data), CapturedByUserId = actor, CapturedAt = now
        };
    }
}
