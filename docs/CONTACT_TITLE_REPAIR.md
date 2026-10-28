# Contact title production investigation

The frontend and DTO already carry `contactTitle`. Both generation and preview pass
the request to `IWordQuoteGeneratorService`, registered as
`ZinStoneWordQuoteGeneratorService` in `Program.cs`. The legacy generic generator is
not registered. The original template contains `عناية المهندس :` in one text node.

On 2026-10-28 production health reported `1.0.0+a13d5719ad9f2ecf1a286716037340a99acb1903`,
while GitHub main/frontend were `de6d42563a3d4290c7e41db18be496774e16d925`.
The backend therefore predates merged PR #4 (`722d75e`). An authenticated browser
preview sent `contactTitle: المهندسة` and `contactPerson: سهيلة`, but both returned
documents displayed `عناية المهندس : سهيلة`. This is a backend deployment mismatch,
not DTO loss, PDF caching or the wrong DI service.

PR #4 also assumed the complete label was inside one OpenXML text node. The repair
locates the title in the combined paragraph text and replaces its characters across
nodes, preserving original run/paragraph properties and punctuation. Unknown titles
return HTTP 400 at DTO validation; missing/blank values retain the existing default.
The DOCX template and display filename implementation are unchanged.

## Reproducible checks

```powershell
dotnet test security-tests/FactoryQuoteApi.SecurityTests.csproj -c Release
node security-tests/xss-regression-tests.cjs
node security-tests/isolation-source-tests.cjs
node document-tests/frontend-contract.cjs
dotnet run --project document-tests/ZinStone.DocumentTests.csproj -c Release -- . document-test-artifacts
python -m pip install PyMuPDF==1.26.7
python document-tests/verify_pdf.py document-test-artifacts
```

The Windows document-pipeline workflow uses the application's real PDF conversion
service on 12 official-template cases. It verifies all eight titles, missing/blank
title, empty person, long Arabic name, invalid-title rejection, template package
integrity and Arabic filenames; extracted PDF text and rendered pages are retained.
HTTP tests separately exercise DTO binding, invalid-title 400 responses and UTF-8
download headers. Mobile File names execute the existing frontend code; WhatsApp
document/template names are tested using the existing mocked transport, without
real sends.

## Backend release

Vercel deploys the frontend automatically; the existing CI publishes an IIS artifact
but **does not deploy the backend**. Use the authorized site102307 FTPS procedure
with the complete matching publish directory, preserve the current runtime secrets,
`App_Data` and documents, use `app_offline.htm`, and verify uploaded file hashes.
Publish with `dotnet publish backend/FactoryQuoteApi.csproj -c Release -o publish -p:SourceRevisionId=<merged-sha>`.
Remove maintenance only after file verification. Health must report
`1.0.0+<merged-sha>` before running authenticated preview checks.

The production diagnostics workflow now fails on an unexpected backend SHA, failed
health/frontend, FTP-host frontend configuration or missing approved CORS origin.
It remains read-only and never prints credentials. Production preview checks must
use authorized test credentials; no production quotation creation is required.
