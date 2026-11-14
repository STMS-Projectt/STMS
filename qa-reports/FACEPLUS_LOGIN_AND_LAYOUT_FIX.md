# Face++ Demo Login & Biometric Layout Fix

Date: 2026-11-14 · Branch: `codex/phase-1-security`

## Status at a glance

| Question | Answer |
|---|---|
| Webcam face sign-in implemented as a real Identity login? | **Yes**, behind `FacePlusPlus:EnableDemoFaceLogin` (off by default, and refused in Production unless separately allowed). |
| Tested end-to-end with HTTP and a real browser? | **Yes, against a local Face++ mock.** Real Identity cookies, SQL Server, Chromium with a fake webcam. |
| Tested with the **real Face++ API**? | **BLOCKED / NOT EXECUTED.** No Face++ credentials are available, and `api-us.faceplusplus.com` is not reachable from this environment. |
| Tested with a **real camera and a real face**? | **NOT EXECUTED.** There is no camera hardware here; Chromium's synthetic camera was used. |
| Overall | **Implemented and verified against a mocked provider and a synthetic camera; not yet proven with real Face++ and a real webcam.** Run the steps in §11 on the demo machine. |

## 1. Root cause of the disabled face login

- `AccountController.Camera` and `DirectCamera` always returned **503** ("requires a live challenge and is unavailable"). No face endpoint ever issued a sign-in.
- `FaceLab` supported enrollment and a comparison "lab" for already signed-in users only, and it explicitly "cannot sign you in".
- `Login.cshtml` hard-coded "Face-only sign-in is unavailable".

This was a deliberate earlier security decision (no certified liveness detection), not a bug. The owner has now accepted the limitation for the academic demo.

## 2. How webcam sign-in works now

New anonymous endpoints use the `biometric-login` rate limit (12 requests per 2 minutes per client address) and the site's global anti-forgery validation:

1. `POST /Account/FaceLoginStart {email}`
   - The server looks up the account and checks eligibility, using the same checks as passkey login: active employee profile, `CanSignInAsync`, not locked out, no 2FA.
   - Eligible and enrolled: a single-use challenge (2-minute lifetime) is issued.
   - **Unknown, unenrolled or ineligible: an indistinguishable decoy challenge is issued.** It runs the same steps and always fails, so the response does not reveal whether an account exists.
2. `POST /Account/FaceLoginCapture {token, frame}` ×3 runs the existing FaceLab challenge: look straight → turn 20–45° → look straight.
   - Each frame goes to Face++ **Detect**, which must find exactly one face.
   - Frames are matched to each other with **Compare**.
   - The last frame is compared with the stored reference.
   - Tokens are single-use, and reused frames are rejected.
3. On the last frame, the server re-reads the user and requires all of the following:
   - the comparison passed;
   - the security stamp is unchanged;
   - the account is still eligible.

   Only then does it call `SignInManager.SignInAsync`, the same call passkey login uses, and return the role dashboard URL.
4. Any failure leaves the user signed out:
   - Mismatch, malformed provider response, missing reference, decoy, disabled or locked account: **401** with one generic message, and **no cookie**.
   - Provider error: 503. Quota exhausted: 429.
   - For real accounts, a failure calls `AccessFailedAsync`, which counts toward the normal Identity lockout.
   - Successes and failures are written to `AuditLogs` (`FaceLoginSucceeded`, `FaceLoginFailed`, `FaceEnrolled`, `FaceRemoved`). No images or secrets are recorded.

Other points:

- The browser only sends frames. There is no client-side "matched" flag.
- Password and passkey sign-in are unchanged.
- The legacy `Camera` and `DirectCamera` endpoints still return 503.
- The match threshold is configurable with `FacePlusPlus:CompareThreshold`. It selects which Face++ false-accept threshold must be met: `1e-3`, `1e-4` or **`1e-5` (the default and the strictest)**.
- `FacePlusPlus:StrictTurnDirection` defaults to **false**, so a turn in either direction is accepted. The left/right sign of Face++ yaw has not been verified on a real mirrored webcam.

## 3. Encrypted enrollment

- Enrollment reuses the existing FaceLab pipeline. The user must be signed in with a full session, enter their **current password**, and pass the 3-frame challenge (exactly one face, the same face in every frame).
- New: the reference is **not saved until the user clicks "Save face"** (`POST /Account/FaceLabConfirm`). The confirm token is bound to that user's id and security stamp, so another account cannot save it (tested).
- The reference frame is encrypted with ASP.NET Core Data Protection (purpose `STMS.FaceLab.Reference.v1`). It is stored under `App_Data/Biometrics/FaceLab` (git-ignored, outside `wwwroot`), in a file named after a SHA-256 hash of the user id.
- **Replace** (the password is required again) atomically overwrites the reference. **Remove** deletes it after a site confirmation dialog.
- The free-quota counter (`MonthlyCallsPerEndpoint`, at most 450) still applies. When it is exhausted, the user gets a clear 429 message and is offered password or passkey sign-in. Paid usage is never enabled.

## 4. Was the real Face++ integration tested?

**No.** The request format, response parsing and error handling are the existing FaceLab code. They were exercised only against mocks: in-process fakes in the unit and HTTP tests, and a loopback Node server (`qa-reports/mock-facepp.mjs`) in the browser run. `FacePlusPlus:BaseUrl` is now honoured; only HTTPS is allowed, plus loopback HTTP for mocks.

## 5. Files modified

| File | Change |
|---|---|
| `Services/Biometrics/FaceLab.cs` | Demo login (`StartLogin`, `CaptureLogin`, decoys), `ConfirmEnrollment`, replace, `DemoLoginEnabled`, configurable threshold, BaseUrl and storage path, lenient turn option |
| `Controllers/AccountController.cs` | `FaceLoginStart`, `FaceLoginCapture`, `FaceLabConfirm`, audit, lockout counting, replace allowed |
| `Views/Shared/_Layout.cshtml` | Portal shell only for Login, BiometricVerify and first-time onboarding |
| `Views/Account/Login.cshtml` | Password / Face / Passkey tabs and the face panel |
| `Views/Account/BiometricSetup.cshtml` | Rebuilt as "Sign-in security" with the Face Recognition Setup card |
| `Views/Shared/_FaceCamera.cshtml` | **new**: shared camera stage partial |
| `Views/Shared/_Icon.cshtml` | `camera`, `face` and `key` icons |
| `wwwroot/js/face-lab.js` | Rewritten state machine: enroll and login, every state, camera release |
| `wwwroot/js/biometrics.js` | Re-initializes after AJAX navigation; accessible tabs |
| `wwwroot/css/site.css` | Method tabs and camera/face styles, built only on existing tokens |
| `appsettings.json` | New non-secret keys (all demo features off) |
| `SecurityTests/FaceLabTests.cs` | Confirm step; 11 new tests |
| `SecurityTests/FaceLoginHttpTests.cs` | **new**: end-to-end Identity test on SQL Server |
| `qa-reports/phase4-browser.mjs`, `phase6-lifecycle.mjs` | P6-A session-switch fix; tab-aware `login()` |
| `qa-reports/face-browser.mjs`, `mock-facepp.mjs`, `faceplus-evidence/*.png` | **new**: browser verification and evidence |

There are no database migrations.

## 6. Login and BiometricSetup layout changes

- **Root cause:** `_Layout.cshtml` forced every `BiometricSetup` request onto the public "Supplier portal" shell, even for signed-in staff. The page therefore lost the role sidebar, the header and the profile.
  - Now only Login, BiometricVerify (the pending second step) and first-time onboarding (`ViewData["BiometricOnboarding"]`) use the portal shell.
  - Sign-in security renders inside the user's own role workspace, and its AJAX navigation re-initializes the scripts.
- **Sign-in security:**
  - Standard `page-heading`.
  - "Face Recognition Setup" card with an Enrolled / Not enrolled / Verification required / Unavailable status pill.
  - A 4:3 camera stage with an oval guide, a loader, a 3-step progress strip, and a coloured status panel (info, success, warning, error).
  - Guidance list, password field, and only the actions valid in the current state: Set up / Replace, Capture, Save face, Try again, Cancel, Remove.
  - Passkey card.
- **Login:** a segmented tab control (WAI-ARIA tabs with arrow and Home/End keys) for Password, Face and Passkey. Before sign-in there is no dashboard navigation. When the demo flag is off, the Face tab shows an explanatory notice instead.
- **Messages:** no `alert()`. Status messages use `role="status"` with `aria-live`. Removal uses the site's confirmation dialog.

## 7. Browser testing (Chromium, fake webcam, loopback Face++ mock, SQL Server 2022)

| # | Check | Result |
|---|---|---|
| F01 | Login renders Password, Face and Passkey; no dashboard navigation | PASS |
| F02 | Face tab opens; keyboard arrows move focus | PASS |
| F03 | Login: no overflow at 1440/1024/768/390/320; camera visible | PASS |
| F04 | Password login goes to the role dashboard | PASS |
| F05 | Setup page uses the real role workspace (sidebar, header, profile; no portal bar) | PASS |
| F06 | Setup: no overflow at 1440/1024/768/390/320; camera visible | PASS |
| F07 | Enrollment refuses to open the camera without a password | PASS |
| F08 | Camera permission → 3 captures → Save → encrypted file outside wwwroot → "Enrolled" plus notice | PASS |
| F09 | AJAX navigation initializes the component; navigating away stops the camera | PASS |
| F10 | **Face login → real Identity cookie → /AdminDashboard as that user** | PASS |
| F11 | **Mismatch → generic error, camera off, no cookie, dashboard still redirects to login** | PASS |
| F12 | Cancel and switching tab both stop all camera tracks | PASS |
| F13 | Permission-denied message | PASS (see note) |

The app log contained no API secret or image data.

> **Note on F13:** headless Chromium reports every denied camera request as `NotSupportedError`. The UI now maps that to a clear message (this was a defect found here: it previously showed the raw text "Not supported"). The `NotAllowedError` path was verified by making `getUserMedia` reject with that exact DOMException.

## 8. Automated test results

The full suite ran once at the end against SQL Server 2022 (Docker) and Chromium: **64 tests, 64 passed.**

New tests:

- `FaceLabTests`, 11 new tests:
  - successful comparison;
  - incorrect face and malformed compare response;
  - decoys for unknown and unenrolled accounts;
  - replay of tokens and frames, and enroll/login token separation;
  - provider failure and quota;
  - confirmation, replace and remove;
  - demo-flag and Production restrictions (5 cases).
- `FaceLoginHttpTests`, end-to-end with real Identity:
  - unauthorized (anonymous) enrollment refused;
  - password required;
  - another user cannot confirm someone else's enrollment;
  - successful login creates a session and the dashboard returns 200;
  - mismatch, unknown, **disabled** and **locked** accounts get 401 and no session;
  - lockout counter incremented;
  - rate limit returns 429;
  - demo off returns 503 and the Face tab has no camera;
  - removal deletes the reference.

## 9. P6-A browser test status

**Fixed: P6-A now passes (18/18 browser scenarios).**

- **Cause:** `as()` began switching users immediately after a form POST (`AssignCommittee`), before that navigation finished. Because `SecurityStampValidator` runs with a zero interval, Identity re-issues the auth cookie on every authenticated response. The in-flight response could therefore restore the previous SourcingOfficer cookie after it had been cleared.
- **Fix (test harness only):**
  - wait for in-flight requests to settle;
  - clear cookies and confirm that no `.STMS.*` cookie remains;
  - retry the whole switch (reset, sign-in, navigation, identity check) if the server shows the wrong user.
- No server authorization or business rule was changed.

## 10. Remaining security limitations (by design for the demo)

- **No certified liveness.** A printed photo, a screen or a video replay of the enrolled person may pass. The head-turn challenge is a usability check, not anti-spoofing.
- Face++ Detect/Compare is a third-party cloud service. Frames leave the machine, so participants must consent.
- The face-login decoy hides enrollment from responses. Timing could still differ slightly: a real account makes one extra Compare call.
- Starting a challenge for an email cancels any earlier pending challenge for that account. Someone could abuse this to annoy a user, but not to sign in.
- Suspended suppliers are treated exactly as with password and passkey sign-in: they can sign in to see their status. No new rule was added.
- Captured frames are held in server memory only for the 2-minute challenge. The reference is encrypted with Data Protection keys; protect the key ring like any credential.

## 11. Enabling the academic demo

1. Create a free Face++ account and get an API key and secret. Do not enable paid usage.
2. Store the credentials outside the repository, either with user secrets in `SourcingTenderSystem/`:

   ```bash
   dotnet user-secrets set "FacePlusPlus:ApiKey" "<key>"
   ```

   ```bash
   dotnet user-secrets set "FacePlusPlus:ApiSecret" "<secret>"
   ```

   or with the environment variables `FacePlusPlus__ApiKey` and `FacePlusPlus__ApiSecret`.
3. Turn both flags on for the demo environment only: `FacePlusPlus__EnableFreeLab=true` and `FacePlusPlus__EnableDemoFaceLogin=true`.
   - Run in `Development` (or another non-Production environment).
   - Production additionally requires `FacePlusPlus__AllowDemoFaceLoginInProduction=true`. Do not set this outside the demo.
4. Optional settings:
   - `FacePlusPlus__CompareThreshold=1e-4` if real webcams are rejected too often (the default `1e-5` is the strictest).
   - `FacePlusPlus__StrictTurnDirection=true` once you have confirmed the left/right instruction on your webcam.
5. Serve over HTTPS or `http://localhost`, because browsers only open cameras in secure contexts.
6. Run the demo:
   1. Sign in with your password, which requires a passkey as before.
   2. Open **Sign-in security** (My profile → Manage sign-in methods).
   3. Enter your password, choose **Set up face recognition**, follow the 3 steps, and choose **Save face**.
   4. Sign out. On the login page choose **Face**, enter your email, choose **Start verification**, and capture the 3 steps.
7. **Before the demo, perform one real sign-in with the actual webcam and Face++ credentials.** It has not been executed in this environment.
