# Final Targeted Verification

Date: 2026-11-14 · Branch: `codex/phase-1-security`

## Environment (important)

No Windows computer was connected to this session, so **LocalDB could not be used**. Verification ran in the Linux cloud container instead:

- **Database:** a disposable **SQL Server 2022** Docker container (`mcr.microsoft.com/mssql/server:2022-latest`). It was passed to the harness through `STMS_PHASE2_TEST_SQL` as a server-level connection string (no catalog). The tests create and drop their own `STMS_Phase2_*`, `STMS_Phase3_*` and `STMS_Phase4_*` catalogs.
- **Project database:** the real project database was never touched.
- **Browser:** Playwright Chromium was passed through `STMS_BROWSER_EXE`, using a wrapper that adds `--no-sandbox` because the container runs as root.
- **SDK:** .NET 10.0.115.

## Git state

- Start: HEAD `1aa254b`, equal to `origin/codex/phase-1-security`, with a clean tree. The previous changes were present.
- Changes from this verification are committed on top of `1aa254b`.
- The regenerated browser screenshots were discarded and not committed.

## Test results

| Run | Result |
|---|---|
| 5 previously blocked SQL tests (`SqlServer_upgrade_and_HTTP_bidding_lifecycle`, `Evaluation_award_and_cancellation_http` award/reject/notification-failure/race) | **5/5 passed** |
| Browser test `Real_browser_identity_and_workspace` (18 scenarios) | **17/18 PASS**. **P6-A-award FAIL** (see blocker) |
| Full suite, run once after fixes | **52 tests: 51 passed, 1 failed** (the same browser test, because of P6-A only) |

Browser scenarios that passed: BR01–BR06, the seven role logins, P6-B-rounds (×3) and **P6-C-cancellation**.

## Cancellation handoff (Step 4)

Verified over HTTP on an isolated database by `Evaluation_award_and_cancellation_http("reject")`, which passed:

1. The Top Committee rejected the tender with the reason "Review concerns". The tender status became `Cancelled`, and a second vote returned 409.
2. The Sourcing Manager received the notification "Tender cancelled: handoff required", and its message contains the reason.
3. The manager handoff (`ReturnToOfficer`) succeeded. The Sourcing Officer received "Tender cancelled: create a new tender". The link is exactly `/Tenders/Create?purchaseRequisitionId={id}`, and the message contains the reason.
4. Before the officer acted, only one tender existed for the requisition, so nothing was auto-created.
5. The evaluation details page shows "Reason: Review concerns" and the "Create a new tender" section. *(assertion added in this pass)*
6. Opening the notification link returned the Create page with the **correct requisition preselected** (`<option selected="selected" value="{id}">`). *(assertion added in this pass)*
7. After the new tender was created:
   - It is `Draft` and has no bids.
   - The cancelled tender keeps its 3 bids, invitations, round 3, 4 evaluation submissions and the rejection approval with its reason.
   - The cancelled tender's status is still `Cancelled`. *(assertion added in this pass)*

The browser scenario P6-C-cancellation also passed.

## Currency verification (Step 3)

| Area | Finding |
|---|---|
| Evaluation scoring (`EvaluationRules.Weighted`) | Uses only the committee's normalized scores. Prices are not used. ✅ |
| Financial scoring (`CommitteeReviews` financial input) | `TotalCost = TotalPrice + taxes + extras` in the bid's own currency. It is stored for reference, never ranked, and the score is entered manually. Unchanged. ✅ |
| Evaluation report generation and supplier comparison | Ordered by weighted score total. The USD reference per bid is saved into `SupplierComparison` and `ResultsSnapshot` at upload. ✅ |
| Frozen history | After upload, Details renders from the stored snapshot (`ResultsSnapshot != null`), so rates never change afterwards. Legacy rows without rates show "unavailable". ✅ |
| Same reference date per tender | `ExchangeRates.ReferenceDate(tender.CloseDate, now)` is computed once per comparison and applied to every bid. ✅ |
| Compliance Review | Mixed currencies are ordered by USD only when every bid has a rate. Otherwise bids are grouped by currency. ✅ |
| AI Bid Advisor facts and ranking | Ranks by USD when currencies are mixed. If any rate is missing, `PriceRank = 0` and prices are "Not comparable". ✅ |
| **AI report per-item price table** | ❌ **Defect, fixed:** the "lowest unit price" highlight compared raw unit prices across currencies. |
| Tender and bid details, exported PDFs | Display the original amount and currency only. No cross-bid calculation. ✅ |
| Weekends | Live API: requests for Saturday or Sunday return a rate labelled with the requested date. If a provider returns an earlier business day, that effective date is accepted and stored. A later date is rejected. New unit test added. ✅ |
| Unavailable, unsupported or failed | 404 (future date), 422 (invalid code), malformed JSON, wrong currency pair, zero rate, network error or timeout (8 s) all return `null`, and the UI shows a warning. The bid form only accepts JOD, USD and EUR. ✅ |

## Defects corrected

1. `Views/CommitteeReviews/AiReport.cshtml`: the per-item lowest-price highlight now applies only when all bids share one currency.

Tests added:
- `CurrencyComparisonTests.Weekend_request_keeps_the_provider_effective_date`.
- Extended assertions in `EvaluationAwardTests` (reject path): the officer details page, the retender link with the requisition preselected, and the original tender still `Cancelled`.

## Remaining blockers

1. **P6-A-award browser scenario** fails at its first step: switching the session to `DepartmentSmoke` lands on `/Account/AccessDenied` while still signed in as SourcingOfficer.
   - **It also fails identically on the pre-change commit `56756c3`**, so it is not a regression from the cancellation or currency changes.
   - It is a Linux headless-Chromium session-reset race in the test harness (`phase6-lifecycle.mjs` `as()` / `resetSession`).
   - It should be re-run in Chrome on Windows, where it was previously reported as passing.
2. **LocalDB specifically** was not exercised, because no Windows computer was connected. Re-run on Windows with `STMS_PHASE2_TEST_SQL="Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true"` and `STMS_BROWSER_EXE` pointing to Chrome.
3. **Open business decision:** the Manager Reject/Return policy before Top Committee voting (unchanged from the previous report).
