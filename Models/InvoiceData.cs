namespace EdifactValidator.Models;

public class InvoiceData
{
    public string        InvoiceId      { get; set; } = string.Empty;
    public string        InvoiceType    { get; set; } = string.Empty;
    public DateOnly      IssueDate      { get; set; }
    public DateOnly      DeliveryDate   { get; set; }
    public string        CurrencyCode   { get; set; } = "EUR";
    public string        EdifactVersion { get; set; } = string.Empty;
    public string        OrderNumber    { get; set; } = string.Empty;
    public string        DeliveryNote   { get; set; } = string.Empty;
    public string        CustomerNumber { get; set; } = string.Empty;
    public InvoiceParty  Seller         { get; set; } = new();
    public InvoiceParty  Buyer          { get; set; } = new();
    public InvoiceParty? DeliveryParty  { get; set; }
    public InvoiceParty? Invoicee       { get; set; }
    public List<InvoiceLine>    Lines    { get; set; } = new();
    public List<TaxBreakdown>   TaxLines { get; set; } = new();
    public InvoiceTotals        Totals   { get; set; } = new();
}

public class InvoiceParty
{
    public string  Qualifier   { get; set; } = string.Empty;
    public string  Name        { get; set; } = string.Empty;
    public string? Gln         { get; set; }
    public string? Street      { get; set; }
    public string? City        { get; set; }
    public string? PostalCode  { get; set; }
    public string? Country     { get; set; }
    public string? VatId       { get; set; }
    public string? TaxNumber   { get; set; }
}

public class InvoiceLine
{
    public string  LineId       { get; set; } = string.Empty;
    public string  Gtin         { get; set; } = string.Empty;
    public string  Description  { get; set; } = string.Empty;
    public decimal Quantity     { get; set; }
    public string  UnitCode     { get; set; } = string.Empty;
    public decimal UnitPrice    { get; set; }
    public decimal LineTotal    { get; set; }
    public decimal TaxPercent   { get; set; }
}

public class TaxBreakdown
{
    public decimal TaxPercent    { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal TaxAmount     { get; set; }
}

public class InvoiceTotals
{
    public decimal TotalAmount    { get; set; }  // MOA+77
    public decimal LineItemsTotal { get; set; }  // MOA+79
    public decimal TaxableAmount  { get; set; }  // MOA+125
    public decimal TaxAmount      { get; set; }  // MOA+124
    public decimal Allowances     { get; set; }  // MOA+131 (document level)
}
