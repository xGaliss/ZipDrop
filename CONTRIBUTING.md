# Contributing to ZipDrop

Thanks for helping! A few ground rules keep ZipDrop small and trustworthy.

## Scope

ZipDrop does one thing: **collect files → create a ZIP**, fast. Before opening a big PR, open an issue to
discuss it. Features that don't speed up that flow (file management, archive browsing, cloud sync…)
will likely be declined. Ideas already planned are in [docs/ROADMAP.md](docs/ROADMAP.md).

## Hard rules

- No network access, telemetry, analytics or accounts. Ever.
- Never return `DragDropEffects.Move` from a drop, never overwrite entries inside a ZIP.
- Logic that can be tested without Windows goes in `src/ZipDrop.Core` with tests.
- All P/Invoke lives in `src/ZipDrop/Interop/NativeMethods.cs` (or next to its only user, documented).
- The low-level mouse hook callback must stay trivial (Windows removes slow hooks).

## Workflow

```powershell
dotnet build      # must be warning-free
dotnet test       # must be green
```

- Shake detector changes: add synthetic cursor paths to `tests/ZipDrop.Core.Tests/ShakeDetectorTests.cs`.
- UI changes: include a screenshot in the PR. `tools/e2e` has helpers for real drag & drop tests
  (they move your mouse).
- Record important technical choices in `docs/DECISIONS.md` and limitations in `docs/KNOWN_ISSUES.md`
  (Spanish or English are both fine).
- Code comments and UI text in English.
