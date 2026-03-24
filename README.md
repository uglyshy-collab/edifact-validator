# EDI Validator

A browser-based EDIFACT validation tool for Porta IT-Service, built with Blazor WebAssembly (.NET 8).
Runs entirely in the browser — no server, no data upload.

## Features

### Validation
- **INVOIC D96A** — Invoice
- **ORDRSP D96A** — Order response
- **DESADV D96A** — Despatch advice
- **INVRPT D96A** — Inventory report

Rules follow the Porta IT-Service EANCOM guideline. Each rule can be individually enabled/disabled and its severity (Error/Warning) overridden in the admin panel.

### Upload & Results
- Click or **drag & drop** `.edi` / `.txt` files
- **Batch validation** — select multiple files at once; results shown as an expandable summary table
- **Multi-interchange** — a single file containing multiple UNB…UNZ blocks is automatically split and shown as a batch
- **Validation report** — errors and structural warnings in one table, optional field recommendations ("Angabe empfohlen") in a separate card
- **Segment view** — all EDIFACT segments with colour-coded tags; click any segment to expand element/component breakdown
- **GLN comparison** — match buyer/delivery/invoicee GLNs from the file against stored branch data, with EAN-13 check-digit validation
- **CSV export** — download validation results or batch summary as CSV

### Diff View (`/diff`)
Side-by-side segment comparison of two EDI files. Highlights added, removed, and modified segments. Filter to show only differences.

### Admin Panel (`/admin`, password-protected)
- **GLN database** — 200+ Porta branches (Porta Mitte/Ost/West, SB-Möbel Boss, ASKO CZ/SK) with NAD+BY / NAD+DP / NAD+IV GLNs; fully editable
- **Rule configuration** — enable/disable rules, override severity per rule code
- **Statistics** — KPI cards, most common errors, most common recommendations (shown separately), validations per day, top suppliers
- **History** — log of all past validation runs with filters and CSV export

## Technology

| | |
|---|---|
| Framework | Blazor WebAssembly (.NET 8) |
| Storage | Browser LocalStorage + IndexedDB (all client-side) |
| CSS | Bootstrap 5 + custom theme |
| Languages | DE / EN / CS |

## Getting Started

```bash
git clone https://github.com/uglyshy-collab/edifact-validator.git
cd edifact-validator
dotnet run
```

Open `http://localhost:5235` in your browser.

## Project Structure

```
Pages/
  Home.razor            Main validation UI
  Home.BatchResult.cs   Batch result model
  Diff.razor            File diff view
  Admin.razor           Admin panel

Services/
  EdifactParser.cs      EDIFACT parser (UNA/UNB/UNH…, multi-interchange)
  InvoicValidator.cs    INVOIC rules
  OrdrspValidator.cs    ORDRSP rules
  DesadvValidator.cs    DESADV rules
  InvrptValidator.cs    INVRPT rules
  GlnStore.cs           GLN data, rule config, statistics (LocalStorage)
  GlnDatabase.cs        Hardcoded branch GLN master data
  GlnHelper.cs          EAN-13 check digit validation
  L10n.cs               DE / EN / CS translations

Models/
  EdifactInterchange.cs
  EdifactSegment.cs
  EdifactMessage.cs
  ValidationIssue.cs
  GlnEntry.cs
```

## License

Internal tool — Porta IT-Service.
