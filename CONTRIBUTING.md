# コントリビューション ガイド / Contributing Guide

BrowserFlip へのコントリビューションを歓迎します。日本語の説明の後に [English version](#english) があります。

参加するすべての方は [行動規範 (Code of Conduct)](CODE_OF_CONDUCT.md) に従ってください。セキュリティ上の問題は Issue ではなく [SECURITY.md](SECURITY.md) の手順で報告してください。

## 開発環境

- Windows 11(開発・テストは Windows 11 で行っています。Windows 10 は未検証)
- [.NET 10 SDK](https://dotnet.microsoft.com/download)(バージョンは `global.json` で固定)
- Visual Studio 2026、Visual Studio Code(C# Dev Kit)、または JetBrains Rider など

```powershell
git clone https://github.com/tlzs-oss/browserflip.git
cd browserflip
dotnet build BrowserFlip.slnx -c Release
dotnet test BrowserFlip.slnx
dotnet run --project src/BrowserFlip.App
```

構成は [docs/architecture.md](docs/architecture.md)、要件は [docs/requirements.md](docs/requirements.md)、テスト方針は [docs/testing.md](docs/testing.md) を参照してください。

## ブランチと PR の流れ

1. 大きな変更は先に Issue で相談してください。
2. リポジトリを fork し、`main` からトピック ブランチを作成します(例: `feat/tray-icon`, `fix/detect-scoop-browsers`)。
3. 変更・テストを行い、下記のチェックをローカルで通します。
4. `main` への Pull Request を作成し、テンプレートに沿って記入します。
5. CI がすべて成功し、メンテナーのレビューで承認されるとマージされます。

## コミット メッセージ

[Conventional Commits](https://www.conventionalcommits.org/ja/v1.0.0/) に従ってください。

```
feat(detection): support per-user Scoop installs
fix(settings): fall back to top page when deep link fails
docs: update Japanese README
test(defaults): cover PartiallyApplied outcome
```

主な type: `feat`, `fix`, `docs`, `test`, `refactor`, `perf`, `build`, `ci`, `chore`。

## PR 前のチェック

```powershell
dotnet build BrowserFlip.slnx -c Release     # 警告はエラー扱い
dotnet test BrowserFlip.slnx
dotnet format BrowserFlip.slnx --verify-no-changes
```

- **テストは必須です。** ロジックの変更には単体テストを追加・更新してください。OS 依存は `BrowserFlip.Core.Platform` のインターフェイスをフェイクに差し替えてテストします。
- **Windows の設定を実際に変更するテストを追加しないでください。** そのような確認は隔離された VM での手動受け入れテストで行います([docs/testing.md](docs/testing.md))。
- UI の変更にはスクリーンショット(ライト / ダーク)を添えてください。

## 多言語(i18n)ルール

- UI 文字列はハードコードせず、リソースに追加します。
- **すべてのリソース キーは `Strings.resx`(英語)、`Strings.ja.resx`、`Strings.zh-CN.resx` の 3 ファイルすべてに存在しなければなりません。** これは単体テストで強制されます。
- 翻訳に自信がない場合は英語で仮置きし、PR でその旨を明記してください。ネイティブ話者のレビューを歓迎します。

## 禁止事項

BrowserFlip は Windows が保護する既定ブラウザーの選択を尊重します。以下を行う変更は受け付けません。

- `UserChoice` / `UserChoiceLatest` レジストリ キーへの書き込み
- UserChoice ハッシュの計算・偽造、またはそれを行うツールやライブラリの利用
- DISM の既定関連付け XML(`/Import-DefaultAppAssociations`)の利用
- レジストリへの書き込み全般、管理者権限の要求
- `ms-settings:` 以外の URI やプロセスの起動
- テレメトリ、ネットワーク通信
- 本体へのサードパーティ実行時依存の追加(テスト用パッケージはライセンス許可リストの範囲内で可)

## ライセンスと DCO

- DCO(Developer Certificate of Origin)の署名は不要です。
- コントリビューションは本プロジェクトと同じ [MIT License](LICENSE) で提供されたものとみなします。

---

<a id="english"></a>

## English

Thank you for your interest in BrowserFlip! Everyone participating must follow the [Code of Conduct](CODE_OF_CONDUCT.md). Please report security issues privately as described in [SECURITY.md](SECURITY.md), not in public issues.

Project docs under `docs/` are written in Japanese; issues and pull requests in English are welcome.

### Development setup

- Windows 11 (development and testing use Windows 11; Windows 10 is not tested)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (pinned in `global.json`)

```powershell
dotnet build BrowserFlip.slnx -c Release
dotnet test BrowserFlip.slnx
dotnet run --project src/BrowserFlip.App
```

### Branches and pull requests

1. For larger changes, open an issue first to discuss.
2. Fork the repository and create a topic branch from `main`.
3. Make your changes and run the checks below.
4. Open a pull request against `main` and fill in the template.
5. A maintainer merges it once CI is green and the review is approved.

### Commit messages

Use [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/), e.g. `feat(detection): ...`, `fix(settings): ...`, `docs: ...`, `test: ...`.

### Before opening a PR

```powershell
dotnet build BrowserFlip.slnx -c Release     # warnings are errors
dotnet test BrowserFlip.slnx
dotnet format BrowserFlip.slnx --verify-no-changes
```

- **Tests are required** for logic changes. Use fakes for the interfaces in `BrowserFlip.Core.Platform`.
- **Never add tests that change real Windows settings.** Those checks are manual and run only on an isolated test VM (see `docs/testing.md`).
- Include screenshots (light and dark) for UI changes.

### Localization rule

Every resource key must exist in **all three** files: `Strings.resx` (English), `Strings.ja.resx` and `Strings.zh-CN.resx`. A unit test enforces this. If you cannot translate, add the English text as a placeholder and say so in the PR.

### Forbidden approaches

BrowserFlip respects Windows' protection of the default browser choice. We will not accept changes that:

- write `UserChoice` / `UserChoiceLatest` registry keys,
- compute or forge the UserChoice hash, or bundle tools that do,
- use DISM default-association XML,
- write to the registry at all, or require administrator rights,
- launch anything other than `ms-settings:` URIs,
- add telemetry or network access,
- add third-party runtime dependencies (test-only packages must pass the license allowlist).

### License and DCO

A DCO sign-off is **not** required. By contributing, you agree that your contributions are licensed under the project's [MIT License](LICENSE).
