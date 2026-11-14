# Continuation Implementation Report

Date: 2026-11-14 · Branch: `codex/phase-1-security`

## 1. What existed before

- **Cancellation (A):** A Top Committee rejection (reason required) already set the Tender to `Cancelled` and the report to `Cancelled - Awaiting Manager Handoff`. It notified the assigned Sourcing Manager. The manager's `ReturnToOfficer` changed the report to `Cancelled - Ready to Retender` and notified the creating Sourcing Officer. `Tenders/Create` then allowed one new Tender for the same Purchase Requisition. The cancelled Tender, its bids, rounds and approvals were preserved, and nothing was created automatically.
  - Gap: the notifications contained only the tender title. They did not state the rejection reason or that a NEW tender for the SAME requisition is required. The officer link opened the evaluation page instead of the create action.
- **Legacy reports (B):** Reports without `ResultsSnapshot` already showed a "Legacy report" warning. `Approve` already refused any progression without a snapshot, without four complete submissions, or without four evaluation seats. No historical data is fabricated.
  - Gap: the warning did not name the missing submissions.
- **Currency (C):** Bids stored the original `TotalPrice` and `Currency` (JOD/USD/EUR). There was no conversion.
  - Compliance Review sorted mixed currencies by raw amount, with a warning.
  - The AI advisor computed `PriceRank` and `PriceGapPercent` from raw amounts across currencies, which breaks the rule against unnormalized ranking.

## 2. What was changed

### A. Cancellation notification
- The manager notification is now "Tender cancelled: handoff required" and includes the Top Committee rejection reason.
- After handoff, the officer notification is "Tender cancelled: create a new tender". It includes the reason and the requisition number, states that the cancelled tender and its history are preserved, and links directly to `/Tenders/Create?purchaseRequisitionId={id}`. That action is officer-only and already validated server-side by `TenderRules.CanCreate`.
- On the evaluation details page:
  - A cancelled tender shows who rejected it and why.
  - The officer's retender button now sits in an explanatory "Create a new tender" section.
- Unchanged: the workflow, the status values, the manager Reject/Return policy and the "returned for correction" behaviour. No tender is created automatically.

### B. Legacy reports
- The legacy warning now lists any missing evaluator submissions and incomplete seats for tenders that are not finalized. It states that the report stays readable but cannot advance.
- No migration was needed: legacy rows remain valid and are guarded by the existing `Approve` checks.

### C. Multi-currency comparison
- New service `ExchangeRates` uses the free Frankfurter v2 API (`GET /v2/rate/{CUR}/USD?date=YYYY-MM-DD`). Live behaviour was verified:
  - JOD and EUR are supported.
  - A future date returns 404.
  - An invalid currency returns 422.
- How rates are applied:
  - USD needs no lookup.
  - The arithmetic is decimal, rounded to 2 decimal places (away from zero).
  - There is one reference date per tender: the tender's final `CloseDate`, capped at today.
  - Rates are cached in memory for 12 h, and the HTTP timeout is 8 s.
  - If the response is unavailable, invalid, for the wrong pair or for a later date, the service returns `null` and never estimates a value.
- `SupplierEvaluationResult` gained optional `UsdRate`, `UsdEquivalent`, `RateDate` and `RateSource`. When the final report is uploaded, these are saved inside the existing JSON columns (`SupplierComparison` and `ResultsSnapshot`), so historical results never change. Older snapshots deserialize with nulls.
- Display (new partial `_UsdReference`): original amount and currency, "≈ X USD (approx.)", the rate, the effective date and the source. When no rate is available, it shows a clear warning.
  - Used in the evaluation details comparison, together with a mixed-currency note.
  - Used in the Compliance Review table.
- Ranking safety:
  - Weighted evaluation ranking is unchanged and still based on committee scores, not prices.
  - Compliance Review with mixed currencies orders by USD only when every bid has a rate. Otherwise it groups by currency and does not rank across currencies.
  - The AI advisor ranks mixed currencies only by USD equivalents. If any rate is missing, `PriceRank = 0` and the bids are shown as "Not comparable". The prompt tells the model not to compare raw amounts. `PromptVersion` was bumped to 2, which invalidates old cached analyses.
  - No winner is ever selected from conversion.

## 3. Exact files changed

| File | Change |
|---|---|
| `SourcingTenderSystem/Services/ExchangeRates.cs` | **new**: Frankfurter v2 client and USD conversion |
| `SourcingTenderSystem/Program.cs` | registers MemoryCache, the `Frankfurter` HttpClient (`ExchangeRates:BaseUrl` override) and `ExchangeRates` |
| `SourcingTenderSystem/Controllers/EvaluationWorkflowController.cs` | notification text, reason and direct link; USD reference in `Compare` |
| `SourcingTenderSystem/Controllers/ComplianceController.cs` | mixed-currency USD reference and safe ordering |
| `SourcingTenderSystem/Controllers/CommitteeReviewsController.cs` | USD equivalents passed to the AI snapshot |
| `SourcingTenderSystem/Services/BidAiAdvisor.cs` | normalized price ranking, prompt rule, PromptVersion 2 |
| `SourcingTenderSystem/ViewModels/EvaluationWorkflow.cs` | optional rate fields, `UsdReferenceView` |
| `SourcingTenderSystem/ViewModels/BidViewModels.cs` | `UsdReferences`, `UsdOrdered` |
| `SourcingTenderSystem/Views/Shared/_UsdReference.cshtml` | **new** partial |
| `SourcingTenderSystem/Views/EvaluationWorkflow/Details.cshtml` | USD column, rejection reason, retender section, legacy missing list |
| `SourcingTenderSystem/Views/Compliance/Review.cshtml` | USD reference and mixed-currency wording |
| `SourcingTenderSystem/Views/CommitteeReviews/AiReport.cshtml` | USD reference and "Not comparable" |
| `SourcingTenderSystem.SecurityTests/CurrencyComparisonTests.cs` | **new**: 12 focused tests |
| `SourcingTenderSystem.SecurityTests/EvaluationAwardTests.cs` | reject path asserts the manager and officer notifications, the direct link and that no tender is auto-created |
| `SourcingTenderSystem.SecurityTests/BiddingRoundTests.cs` | the test host points `ExchangeRates:BaseUrl` at a closed local port, so integration tests stay offline |

## 4. Database changes

None. There is no schema change and no migration. Applied rates are stored in the existing `nvarchar(max)` JSON snapshot columns, and the project database was not touched.

## 5. Tests executed and results

The tests ran on Linux with the .NET SDK 10.0.115 installed for this session. The build had 0 errors and only pre-existing warnings.

- Focused: `CurrencyComparisonTests`, **12/12 passed**.
- Full suite: **51 tests, 45 passed, 6 failed (environmental)**.
  - Passed: all 33 non-database security, FaceLab, round-threshold and passkey tests, plus the 12 new tests.
  - Failed: the 6 tests that need SQL Server LocalDB or the Chrome harness (`Evaluation_award_and_cancellation_http` ×4, `SqlServer_upgrade_and_HTTP_bidding_lifecycle`, `Real_browser_identity_and_workspace`). They stop at the precondition "Set STMS_PHASE2_TEST_SQL to a disposable SQL Server instance". No SQL Server is available in this Linux container, so they never reached product code.

## 6. Unresolved issues / decisions needed

1. **Manager Reject/Return policy before Top Committee voting**: still undefined, and the existing behaviour is preserved. The manager can only approve (forward). A business decision is needed on whether the manager may return a report to the officer or the evaluators for correction, which must not cancel the tender, and whether a manager-level rejection should cancel.
2. **Rate source per currency**: Frankfurter v2 blends several central-bank providers. The stored source is "Frankfurter v2 (api.frankfurter.dev)". If an auditor needs the specific underlying provider, Frankfurter's `providers` parameter would need to be pinned.
3. **Pre-change reports** have no stored rates. They show "USD equivalent unavailable", by design, rather than a rate fetched afterwards.

## 7. Remaining tasks

- On Windows, set `STMS_PHASE2_TEST_SQL` to a disposable LocalDB and re-run the 6 SQL/browser tests, including the extended `reject` assertions.
- Optional Chrome check: run Scenario C and confirm the officer notification opens `Tenders/Create` with the requisition preselected. Run a mixed JOD/EUR/USD tender and confirm the USD references show on Compliance Review and Evaluation Details.
- The deployment host needs outbound HTTPS to `api.frankfurter.dev`. Without it, the UI shows the "unavailable" warning and keeps working.
