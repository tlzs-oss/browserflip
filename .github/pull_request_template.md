## Summary / 概要

<!-- What does this PR change and why? Link related issues (e.g. "Closes #123"). -->

## Type of change / 変更の種類

- [ ] feat — new feature
- [ ] fix — bug fix
- [ ] docs — documentation only
- [ ] test — tests only
- [ ] refactor / perf / build / ci / chore

## Checklist / チェックリスト

- [ ] The PR title follows [Conventional Commits](https://www.conventionalcommits.org/) (e.g. `fix(settings): ...`).
- [ ] `dotnet build BrowserFlip.slnx -c Release` succeeds with no warnings.
- [ ] `dotnet test BrowserFlip.slnx` passes, and I added or updated tests for logic changes.
- [ ] `dotnet format BrowserFlip.slnx --verify-no-changes` passes.
- [ ] New or changed UI strings exist in **all** of `Strings.resx`, `Strings.ja.resx` and `Strings.zh-CN.resx`.
- [ ] UI changes include screenshots (light and dark theme) and keep keyboard navigation and `AutomationProperties.Name` working.
- [ ] Documentation (README / `docs/`) and `CHANGELOG.md` (`[Unreleased]`) are updated if needed.
- [ ] No new third-party runtime dependency (test-only packages must pass the license allowlist).

## Security principles / セキュリティ原則

- [ ] No registry writes; nothing touches `UserChoice` / `UserChoiceLatest`; no UserChoice hash computation; no DISM association XML.
- [ ] No administrator rights required; only `ms-settings:` URIs are launched.
- [ ] No telemetry or network access added; no secrets or personal data in code, logs or screenshots.

## Testing / テスト

<!-- How did you verify this? Unit tests, and for behavior changes, manual checks on an isolated
     Windows VM (OS build, x64/Arm64). Never run tests that change real Windows settings in CI. -->
