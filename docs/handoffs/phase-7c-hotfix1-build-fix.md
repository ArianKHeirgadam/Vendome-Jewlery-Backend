# Phase 7C Invoice Documents — Hotfix 1

This hotfix resolves the two warnings reported by the Windows verification script when warnings are treated as errors:

- `CS8604` in `VendomeJewleryDesktopApp/MainWindow.xaml.cs`: the initialized `CoreWebView2` instance is now checked and stored in a non-null local reference before configuration and navigation.
- `CS9124` in `GoldInvoice.IntegrationTests/PhaseFiveWorkflowTests.cs`: the scenario uses its `InvoiceService` property instead of capturing the primary-constructor parameter a second time.

The changes are compile-safety fixes only and do not alter invoice, payment, PDF, or printing behavior.

## Hotfix 2

Hotfix 2 resolves `NETSDK1152` during the desktop publish smoke test. The
React `dist` files are copied to `publish/ClientApp/dist` after the SDK publish
conflict-analysis phase instead of being registered a second time through
`ResolvedFileToPublish`. The verification script also checks that the published
`ClientApp/dist/index.html` actually exists.

Run the complete verification on Windows from the solution root:

```powershell
.\scripts\verify-phase7b.ps1
```
