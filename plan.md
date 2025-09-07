# DriftOS — 6‑Week Execution Plan (Refined)

**Stack:** .NET 8 (C#), **WPF** desktop app, XInput (controller), Win32 `SendInput` (pointer), JSON settings, **Squirrel.Windows** packaging.

**Why this version:** You already set up the repo + docs and selected the WPF + Squirrel route. This plan assumes that baseline is done and focuses on shipping a usable alpha fast, then iterating.

**Timeframe:** 6 weeks from **Week 1: 08–14 Sep 2025** through **Week 6: 13–19 Oct 2025**.

---

## Milestones (what we ship)
- **M0 (Done):** Repo + README + LICENSE + Project Development Guide.
- **M1 (14 Sep):** Core pointer control — smooth cursor move + left/right click + drag hold; tray toggle; JSON settings.
- **M2 (21 Sep):** Scroll mapping, sensitivity/deadzone UI; crash‑safe logging; portable zip artifact from CI.
- **M3 (28 Sep):** Acceleration curves (2 presets), per‑display DPI sanity; first user‑visible onboarding; “Safe Mode”.
- **M4 (05 Oct):** Squirrel installer + update channel, versioning, basic release notes; **Private Beta**.
- **M5 (12 Oct):** Polish & bugfixes, telemetry switch **off** by default; **Public Alpha v0.1.0**.

> **Ship criteria (Alpha):** Cursor smooth at 60–144 Hz; clicks/drag reliable for 30s hold; scroll consistent across apps; no crashes in a one‑hour session; installer works on a clean Windows 11 VM.

---

## Definitions
- **Definition of Ready (DoR):** User story has acceptance criteria, test notes, and UI sketch (if applicable).
- **Definition of Done (DoD):** Built locally, unit tests added/updated & passing, smoke checklist passed, docs updated, CI green, reviewer approved.

---

## Week‑by‑Week Plan

### Week 1 — Core Input & Pointer (08–14 Sep)
**Goals**  
1) XInput polling at 120 Hz with cancellation token.  
2) Pointer synthesis via `SendInput` with speed clamp + basic deadzone.  
3) Left click (A/X), Right click (B/Y), Hold‑to‑drag (press+hold A).  
4) JSON settings read/write (sensitivity, deadzone, invert scroll).  
5) Tray menu (enable/disable, Settings…, Exit).

**Acceptance**  
- Moving left stick moves cursor at adjustable speed; no oscillation near center.  
- Press‑and‑hold drag works with Windows Explorer window for 30s without drop.  
- Toggling from tray pauses all controller input instantly.

**Issues to file**  
- `feat: XInputPoller with deadzone`  
- `feat: PointerSynthesizer (SendInput)`  
- `feat: Click/drag mapping`  
- `feat: AppTray with toggle`  
- `feat: JsonSettings service`  
- `docs: smoke checklist v1`

---

### Week 2 — Scroll + UX Basics (15–21 Sep)
**Goals**  
1) Right stick or triggers map to vertical scroll (configurable).  
2) Settings window with sliders (sensitivity, deadzone) + scroll mode dropdown.  
3) Serilog file logging to `%AppData%/DriftOS/logs`.  
4) CI: build & test on `windows-latest`; upload **portable zip** artifact.

**Acceptance**  
- Scroll works reliably in Edge, Notepad, Explorer; speed adjustable.  
- Settings persist and apply without restart.  
- CI workflow produces downloadable artifact.

**Issues**  
- `feat: ScrollMapper (right stick / triggers)`  
- `feat: SettingsWindow (WPF)`  
- `infra: Serilog file sink + rotation`  
- `ci: windows build/test + artifact upload`

---

### Week 3 — Accel, DPI & Safety (22–28 Sep)
**Goals**  
1) Add 2 acceleration curve presets (linear, gentle‑expo) selectable in UI.  
2) DPI sanity (100–200%): clamp max velocity to avoid overshoot.  
3) “Safe Mode” toggle (suspend all input quickly).  
4) First‑run onboarding tip (balloon or dialog).

**Acceptance**  
- Switching curves changes small‑motion precision (linear vs expo).  
- On 200% scaling monitor, cursor remains controllable (no runaway).  
- Safe Mode instantly stops injection and shows tray state.

**Issues**  
- `feat: AccelerationPresets + UI`  
- `feat: DpiAwareVelocityClamp`  
- `feat: SafeMode switch + tray indicator`  
- `ux: Onboarding tip`  

---

### Week 4 — Packaging & Beta (29 Sep–05 Oct)
**Goals**  
1) Squirrel.Windows packaging (create‑release, delta updates).  
2) Appcast/update check UI (Settings → “Check for updates”).  
3) Versioning: SemVer, tag `v0.1.0‑beta.1`; Release Notes stub.  
4) Smoke test on clean Win 11 VM; document SmartScreen expectations.

**Acceptance**  
- Installer installs user‑scope; app starts on login if user selects it.  
- Update from beta.1 → beta.2 works without manual uninstall.  
- Release assets attached to GitHub Release.

**Issues**  
- `release: Squirrel packaging + releasify script`  
- `feat: Update check + channel config`  
- `docs: release notes + SmartScreen note`  
- `qa: VM installation smoke`  

---

### Week 5 — Polish, Stability, Accessibility (06–12 Oct)
**Goals**  
1) Edge cases: controller disconnect/reconnect; rapid alt‑tab; window drag cancel.  
2) Accessibility: enlarge UI fonts toggle; tooltip help for each setting.  
3) Crash‑safe global exception handler with friendly dialog + log link.  
4) Finalise **Public Alpha v0.1.0** notes and screenshots.

**Acceptance**  
- Disconnect/reconnect does not crash; safe recovery.  
- Users can find logs via Settings → “Open logs folder”.  
- All settings have simple tooltips.

**Issues**  
- `fix: XInput hot‑plug resilience`  
- `feat: Accessibility font scale`  
- `infra: Global exception handler`  
- `docs: Alpha notes + screenshots`

---

### Week 6 — Public Alpha & Feedback (13–19 Oct)
**Goals**  
1) Publish `v0.1.0` (GitHub Release).  
2) Open feedback issue template with labelled categories (control, scroll, bugs).  
3) Instrument optional (off‑by‑default) **anonymous** metrics hook for just version + install/update success (no PII) — or defer to later if you prefer zero telemetry.

**Acceptance**  
- Installable, updateable build available publicly.  
- Users can file feedback easily; triage labels documented.  
- Post‑alpha backlog created from feedback.

**Issues**  
- `meta: v0.1.0 release`  
- `docs: Feedback issue template`  
- `feat: Optional metrics toggled off by default (can defer)`

---

## Backlog (post‑alpha, not scheduled yet)
- Rebindable controls (UI).  
- Multiple profiles + per‑app overrides.  
- Horizontal scroll + zoom gesture.  
- Calibration wizard for stick drift.  
- MSIX packaging + signing pathway.

---

## Risk Log (current)
- **SmartScreen prompts (unsigned).** Mitigation: document clearly; explore cert or MSIX later.
- **Anti‑cheat conflicts** with input injection. Mitigation: warn users; “Safe Mode”; never auto‑inject on game launch.
- **Controller variance (drift).** Mitigation: expose deadzone early; wizard later.
- **High‑DPI overshoot.** Mitigation: clamp velocity; provide gentle‑expo curve.

---

## Engineering Notes (snippets to keep handy)
- **Project setup**  
  `dotnet new wpf -n DriftOS.App` → add class libs `DriftOS.Core`, `DriftOS.Input`.  
- **Threading model**  
  Input polling on a background Task; UI dispatch via `Dispatcher`.  
- **Abstractions**  
  `IInputSource` (XInput), `IMouseOutput` (SendInput), `ISettingsStore` (JSON), `IAccelerationStrategy` (linear/expo).  
- **File locations**  
  `%AppData%/DriftOS/config.json`, `%AppData%/DriftOS/logs/…`.

---

## Testing
- **Unit:** deadzone math, accel strategy, mapping logic (press/hold thresholds).  
- **Integration:** fake input → expected `SendInput` calls (wrap P/Invoke behind interface for fakes).  
- **Manual smoke:** install, move, click/drag, scroll in 3 apps, safe‑mode toggle, disconnect/reconnect.

---

## House Rules (quick)
- Trunk‑based: small PRs (< ~300 LOC), Conventional Commits.  
- Every feature has a Test Plan in its PR.  
- Update docs when UX changes.

---

## What’s already done (baseline)
- GitHub repo with README, LICENSE, .gitignore.  
- `PROJECT_DEVELOPMENT.md` in root.  
- Decision: **WPF + Squirrel** path (this plan aligns with it).

> Drop this file at `docs/6-week-execution-plan.md` and link it from README and PROJECT_DEVELOPMENT.md. If you want me to convert this into GitHub Issues automatically, say “make the issues,” and I’ll prepare a CSV you can import or a ready-to-run `gh issue create` script.

