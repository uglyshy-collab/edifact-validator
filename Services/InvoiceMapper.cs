using EdifactValidator.Models;

namespace EdifactValidator.Services;

public static class InvoiceMapper
{
    public static InvoiceData Map(EdifactMessage msg, EdifactDelimiters d)
    {
        var inv = new InvoiceData
        {
            EdifactVersion = msg.VersionString,
        };

        // BGM — invoice number + type
        var bgm = msg.Segments.FirstOrDefault(s => s.Tag == "BGM");
        if (bgm is not null)
        {
            inv.InvoiceType = bgm.El(1);
            inv.InvoiceId   = bgm.El(2);
        }

        // DTM+137 — invoice date
        var dtm137 = msg.Segments.FirstOrDefault(s => s.Tag == "DTM" && s.Comp(1, 1) == "137");
        if (dtm137 is not null)
            inv.IssueDate = ParseDate(dtm137.Comp(1, 2), dtm137.Comp(1, 3));

        // DTM+35 — delivery date
        var dtm35 = msg.Segments.FirstOrDefault(s => s.Tag == "DTM" && s.Comp(1, 1) == "35");
        if (dtm35 is not null)
            inv.DeliveryDate = ParseDate(dtm35.Comp(1, 2), dtm35.Comp(1, 3));

        // CUX — currency
        var cux = msg.Segments.FirstOrDefault(s => s.Tag == "CUX");
        if (cux is not null) inv.CurrencyCode = cux.Comp(1, 2).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(inv.CurrencyCode)) inv.CurrencyCode = "EUR";

        // References
        var rffOn  = msg.Segments.FirstOrDefault(s => s.Tag == "RFF" && s.Comp(1, 1) == "ON");
        var rffDq  = msg.Segments.FirstOrDefault(s => s.Tag == "RFF" && s.Comp(1, 1) == "DQ");
        var rffApi = msg.Segments.FirstOrDefault(s => s.Tag == "RFF" && s.Comp(1, 1) == "API");
        inv.OrderNumber    = rffOn?.Comp(1, 2) ?? string.Empty;
        inv.DeliveryNote   = rffDq?.Comp(1, 2) ?? string.Empty;
        inv.CustomerNumber = rffApi?.Comp(1, 2) ?? string.Empty;

        // NAD parties
        var nads = msg.Segments.Where(s => s.Tag == "NAD").ToList();
        foreach (var nad in nads)
        {
            var party = MapParty(nad, msg);
            switch (nad.El(1))
            {
                case "SU": inv.Seller       = party; break;
                case "BY": inv.Buyer        = party; break;
                case "DP": inv.DeliveryParty = party; break;
                case "IV": inv.Invoicee     = party; break;
            }
        }

        // Line items
        inv.Lines = MapLineItems(msg);

        // Tax
        inv.TaxLines = MapTax(msg);

        // Totals
        inv.Totals = MapTotals(msg);

        return inv;
    }

    private static InvoiceParty MapParty(EdifactSegment nad, EdifactMessage msg)
    {
        var party = new InvoiceParty
        {
            Qualifier  = nad.El(1),
            Gln        = nad.Comp(2, 1),
            Name       = nad.El(4),
            Street     = nad.El(5),
            City       = nad.El(6),
            PostalCode = nad.El(8),
            Country    = nad.El(9),
        };
        if (string.IsNullOrWhiteSpace(party.Name)) party.Name = nad.El(3);

        // RFF after NAD until next NAD
        var nadIdx  = msg.Segments.IndexOf(nad);
        var nextNad = msg.Segments.Skip(nadIdx + 1).FirstOrDefault(s => s.Tag == "NAD");
        var endIdx  = nextNad is not null ? msg.Segments.IndexOf(nextNad) : msg.Segments.Count;
        var nadGroup = msg.Segments.Skip(nadIdx + 1).Take(endIdx - nadIdx - 1).ToList();

        party.VatId     = nadGroup.FirstOrDefault(s => s.Tag == "RFF" && s.Comp(1, 1) == "VA")?.Comp(1, 2);
        party.TaxNumber = nadGroup.FirstOrDefault(s => s.Tag == "RFF" && s.Comp(1, 1) == "FC")?.Comp(1, 2);

        return party;
    }

    private static List<InvoiceLine> MapLineItems(EdifactMessage msg)
    {
        var lines   = new List<InvoiceLine>();
        var linSegs = msg.Segments.Where(s => s.Tag == "LIN").ToList();

        foreach (var lin in linSegs)
        {
            var linIdx  = msg.Segments.IndexOf(lin);
            var nextLin = msg.Segments.Skip(linIdx + 1).FirstOrDefault(s => s.Tag == "LIN");
            var endIdx  = nextLin is not null ? msg.Segments.IndexOf(nextLin) : msg.Segments.Count;
            var group   = msg.Segments.Skip(linIdx).Take(endIdx - linIdx).ToList();

            var imd  = group.FirstOrDefault(s => s.Tag == "IMD");
            var qty  = group.FirstOrDefault(s => s.Tag == "QTY" && s.Comp(1, 1) == "47");
            var pri  = group.FirstOrDefault(s => s.Tag == "PRI");
            var moa  = group.FirstOrDefault(s => s.Tag == "MOA" && s.Comp(1, 1) == "203");
            var tax  = group.FirstOrDefault(s => s.Tag == "TAX" && s.El(2) == "VAT");

            var desc = imd?.Comp(3, 4) ?? imd?.Comp(3, 3) ?? imd?.El(3) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(desc)) desc = imd?.El(4) ?? string.Empty;

            lines.Add(new InvoiceLine
            {
                LineId      = lin.El(1),
                Gtin        = lin.Comp(3, 1),
                Description = desc,
                Quantity    = D(qty?.Comp(1, 2)),
                UnitCode    = qty?.Comp(1, 3) ?? string.Empty,
                UnitPrice   = D(pri?.Comp(1, 2)),
                LineTotal   = D(moa?.Comp(1, 2)),
                TaxPercent  = D(tax?.Comp(5, 4)),
            });
        }
        return lines;
    }

    private static List<TaxBreakdown> MapTax(EdifactMessage msg)
    {
        var result = new List<TaxBreakdown>();
        var taxes  = msg.Segments.Where(s => s.Tag == "TAX" && s.El(2) == "VAT").ToList();
        foreach (var t in taxes)
        {
            var tIdx   = msg.Segments.IndexOf(t);
            var moaAmt = msg.Segments.Skip(tIdx).Take(3).FirstOrDefault(s => s.Tag == "MOA" && s.Comp(1, 1) == "124");
            var moaBas = msg.Segments.Skip(tIdx).Take(3).FirstOrDefault(s => s.Tag == "MOA" && s.Comp(1, 1) == "125");
            result.Add(new TaxBreakdown
            {
                TaxPercent    = D(t.Comp(5, 4)),
                TaxAmount     = D(moaAmt?.Comp(1, 2)),
                TaxableAmount = D(moaBas?.Comp(1, 2)),
            });
        }
        return result;
    }

    private static InvoiceTotals MapTotals(EdifactMessage msg) => new()
    {
        TotalAmount    = D(msg.Segments.FirstOrDefault(s => s.Tag == "MOA" && s.Comp(1, 1) == "77")?.Comp(1, 2)),
        LineItemsTotal = D(msg.Segments.FirstOrDefault(s => s.Tag == "MOA" && s.Comp(1, 1) == "79")?.Comp(1, 2)),
        TaxableAmount  = D(msg.Segments.FirstOrDefault(s => s.Tag == "MOA" && s.Comp(1, 1) == "125")?.Comp(1, 2)),
        TaxAmount      = D(msg.Segments.FirstOrDefault(s => s.Tag == "MOA" && s.Comp(1, 1) == "124")?.Comp(1, 2)),
        Allowances     = D(msg.Segments.FirstOrDefault(s => s.Tag == "MOA" && s.Comp(1, 1) == "131")?.Comp(1, 2)),
    };

    private static DateOnly ParseDate(string? value, string? format)
    {
        if (string.IsNullOrWhiteSpace(value)) return DateOnly.MinValue;
        if ((format == "102" || format == "") && value.Length == 8 &&
            int.TryParse(value[..4], out var y) &&
            int.TryParse(value[4..6], out var m) &&
            int.TryParse(value[6..], out var dd))
            return new DateOnly(y, m, dd);
        return DateOnly.MinValue;
    }

    private static decimal D(string? v) =>
        decimal.TryParse(v?.Replace(',', '.'),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : 0m;
}
