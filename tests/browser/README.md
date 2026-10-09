# Commercial review assignment browser validation

Run the frontend on an isolated URL, install Playwright in a temporary directory,
and run `commercial-review-assignment.mjs` with Node. Set `PORTAL_TEST_FRONTEND_URL`
(default `http://127.0.0.1:5173`) and, if Playwright is installed outside this
directory, `PORTAL_PLAYWRIGHT_MODULE` to its absolute `index.mjs` path.

The script opens the real Vue order detail and intercepts every API call with
synthetic accounts and orders. It never logs in to an operational backend or
sends email. Seven scenarios check empty/error/one/multiple candidates, cancel,
busy controls, reload, resend, changed eligibility and version conflict. Backend
authorization, persistence and outbox delivery are covered by integration tests.

The relational concurrency test uses an isolated PostgreSQL database:

```sh
PORTAL_TEST_POSTGRES_CONNECTION='Host=localhost;Database=isolated_review_test;Username=test;Password=test' \
  dotnet test tests/Portal.IntegrationTests/Portal.IntegrationTests.csproj --filter 'Category=Postgres'
```

This database must be disposable; the test creates its schema and fictitious data.
Without that variable, the relational test is explicitly skipped. The ordinary
suite runs with `--filter 'Category!=Postgres'`.

# Commercial reports browser validation

Run `commercial-reports.mjs` with the same Playwright settings and an isolated
frontend URL (default `http://127.0.0.1:5178`). It uses synthetic data and checks
preparation, all three details, manual FACTURA, amount reconciliation, saving,
download with CSRF and mobile layout. `PORTAL_SCREENSHOT_DIR` optionally saves
desktop/mobile screenshots.

Run `commercial-report-pagination.mjs` with the same settings to check both
review sections with 23 synthetic records: five rows per page, compact page buttons, a page selector,
search/status filters, drafts retained across pages and sections, and a save
payload containing all records rather than only the visible page. It also
checks desktop/mobile layout, first/middle/last pages with 103 records,
seven-page controls with 35 records at 320/381/390px, and
optionally saves screenshots.

Run `commercial-report-download.mjs` with the same settings to check the download
step using 23 synthetic pending entries in each source: five-row pagination,
search, links to the correct editor and review page, drafts retained after a save
error, saved validation before export, permissions, an empty blocked report, an
export error and successful download. It checks desktop and 320/390px layouts
and optionally saves screenshots. All API responses are intercepted locally.

Run `commercial-report-record-editor.mjs` to check the record review tasks and
the ten-column Excel preview with synthetic records: five-row/detail pagination,
local edits including zero/blank values, explicit OP selection and confirmation,
full-report saves, continuity after regrouping/splitting/merging/exclusion,
save errors, permissions and desktop/320/390px layouts. It uses the same isolated
frontend/Playwright settings and never contacts an operational API.

Report migration and concurrency checks use `Category=PostgresReports` and
require a disposable database named exactly `portal_plan003_tests`. Never point
these tests at an operational database. Ordinary backend tests exclude both
Postgres categories. `ReportSourceSamplesTests` is a local, optional read-only
check using `PORTAL_REPORT_FIXTURE_DIR`; source workbooks are not committed.
