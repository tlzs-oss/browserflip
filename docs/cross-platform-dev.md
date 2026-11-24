# クロスプラットフォーム開発ガイド

BrowserFlip は **Windows + macOS の 2 台体制**で開発します。同じ開発者が必要に応じて 2 台を行き来し、同期は **GitHub 経由のみ**で行います(共有ディスクは使いません)。

> **BrowserFlip の製品は Windows アプリケーションのままです。** このガイドが整備するのは開発基盤だけで、macOS 版を作るものではありません。macOS では移植可能な Core のビルドとテストだけを行い、WPF アプリケーションはビルドもテストもできません。

関連: [architecture.md](architecture.md) / [testing.md](testing.md) / [../CONTRIBUTING.md](../CONTRIBUTING.md)

---

## 1. 2 台の開発環境

| | Windows | macOS |
| --- | --- | --- |
| 主リポジトリ | `C:\workspace\00_projects\browserflip` | `~/workspace/00_projects/browserflip` |
| Worktree 置き場 | `C:\workspace\10_worktrees\browserflip\` | `~/workspace/10_worktrees/browserflip/` |
| 必要なもの | .NET 10 SDK、PowerShell 7 | .NET 10 SDK |
| できること | 製品の完全ビルド・全テスト・リリース | Core(`net10.0`)のビルドとテストのみ |

- 2 台はそれぞれ独立にビルド・テスト・コミットします。
- **絶対パスを共有設定(`.csproj`、`global.json`、CI、スクリプト)に書かない。** 上の表はあくまで運用上の置き場所で、リポジトリ内のどのファイルも参照していません。
- macOS に .NET SDK が PATH 上に無い場合、`scripts/dev.sh` は `~/.dotnet`、`/usr/local/share/dotnet`、`/usr/share/dotnet` を自動で探します。恒久的に通すなら `~/.zshrc` に次を追加してください。

  ```sh
  export PATH="$HOME/.dotnet:$PATH"
  ```

---

## 2. プロジェクト構成とターゲット フレームワーク

| プロジェクト | TFM | Windows | macOS |
| --- | --- | --- | --- |
| `src/BrowserFlip.Core` | `net10.0` / `net10.0-windows` | 両方 | `net10.0` のみ |
| `src/BrowserFlip.App`(WPF) | `net10.0-windows` | ビルド・実行 | **UNSUPPORTED** |
| `tests/BrowserFlip.Core.Tests` | `net10.0` / `net10.0-windows` | 両方 | `net10.0` のみ |
| `tests/BrowserFlip.App.Tests`(WPF) | `net10.0-windows` | 実行 | **UNSUPPORTED** |

### Core の分離ルール

- `src/BrowserFlip.Core/Platform/Windows/` 配下だけが Windows 専用実装です(P/Invoke と レジストリ)。
  - `NativeMethods.cs` — `AssocQueryStringW` / `SHLoadIndirectString`、`ShellAssociationQuery`、`ShellIndirectStringResolver`、`ShellUriLauncher`
  - `WindowsRegistryReader.cs` — `Microsoft.Win32.Registry` による読み取り専用実装
- このフォルダーは `net10.0` ターゲットのコンパイルから除外されます(`BrowserFlip.Core.csproj`)。
- それ以外(検出ロジック、既定ブラウザーの状態判定、ポリシー解釈、設定ページの URI 生成)は OS 非依存で、`BrowserFlip.Core.Platform` のインターフェイス越しにのみ OS に触れます。
- **新しく Windows API を使うときは `Platform/Windows/` に置き、インターフェイスを `Platform/` に足してください。** そうすれば macOS 側のビルドは壊れません。

### Windows ネイティブ API のテスト

実際の Windows API を叩くスモーク テストは `tests/BrowserFlip.Core.Tests/Platform/WindowsNativeApiTests.cs` にまとめ、二重に保護しています。

1. `net10.0` ターゲットではコンパイルされません(`Compile Remove`)。
2. `[WindowsFact]` により、`net10.0-windows` ターゲットを非 Windows で実行した場合も **SKIPPED** になります(失敗しません)。

つまり macOS でどのターゲットを実行しても、ネイティブ API が誤って呼ばれることはありません。

---

## 3. 統一された開発入口

同じコマンド体系を 2 つの実装で提供します。どちらも OS を自動判別します。

| | macOS / Linux / Git Bash | Windows(PowerShell 7) |
| --- | --- | --- |
| | `./scripts/dev.sh <command>` | `./scripts/dev.ps1 <command>` |

| コマンド | 内容 |
| --- | --- |
| `info` | 検出した OS、SDK、このホストで実行される対象を表示 |
| `restore` | 対象プロジェクトの復元 |
| `build` | このホストでビルド可能なものをビルド |
| `test` | このホストで適用できるテストを実行 |
| `check` | `restore` → `build` → `test`(+ Windows では書式チェック) |
| `format` | 書式チェック(Windows のみ。macOS では UNSUPPORTED) |
| `sync-start` | 作業開始時の安全な Git 同期([5 章](#5-git-クロス-pc-同期規範)) |
| `sync-finish` | テスト → 変更確認 → コミット → プッシュ → 同期確認 |

実行後に必ずレポートが出ます。状態は 4 種類に区別されます。

| 状態 | 意味 |
| --- | --- |
| `PASS` | 実行して成功した |
| `FAIL` | 実行して失敗した |
| `SKIPPED` | 実行対象だが意図的に飛ばした(例: ネイティブ テスト) |
| `UNSUPPORTED` | このホストでは実行そのものが不可能(例: macOS の WPF) |

最後の `RESULT` 行と終了コードの対応は次のとおりです。

| `RESULT` | 終了コード | 意味 |
| --- | --- | --- |
| `PASS` | 0 | 実行した全ステップが成功した |
| `FAIL` | 1 | いずれかのステップが失敗した |
| `INCOMPLETE` | 1 | 失敗はないが、意図的に途中で止めた(例: `sync-finish` でコミット メッセージが無く、変更を残して終了) |

ビルドとテストを含むレポートのうち、macOS では次の注意書きが必ず付きます。

```
NOTE: this host cannot build or run the WPF application. A green run here does NOT
      verify Windows behaviour — that requires a Windows machine or the Windows CI job.
```

> **macOS のコンパイル成功を Windows の動作検証の代わりにしてはいけません。** Windows の受け入れは実機または Windows CI でのみ成立します([testing.md](testing.md) の実機受け入れテスト)。

### 素の dotnet CLI を使う場合

```sh
# macOS / Linux — 移植可能な Core のみ
dotnet build BrowserFlip.CrossPlatform.slnx -c Release -f net10.0
dotnet test  BrowserFlip.CrossPlatform.slnx -c Release -f net10.0
```

```powershell
# Windows — 製品の全体
dotnet build BrowserFlip.slnx -c Release
dotnet test  BrowserFlip.slnx
```

`BrowserFlip.CrossPlatform.slnx` は `BrowserFlip.slnx` から WPF の 2 プロジェクトを除いたものです。macOS で `BrowserFlip.slnx` をビルドすると `NETSDK1100` で失敗します(これは想定どおりの挙動です)。

---

## 4. CI(GitHub Actions)

`.github/workflows/ci.yml` は 2 つのジョブに分かれています。

| ジョブ | ランナー | 内容 |
| --- | --- | --- |
| `Build, test & checks` | `windows-latest` | 完全ビルド、全テスト(両 TFM、ネイティブ スモーク テストを含む)、書式・脆弱性・ライセンス チェック |
| `macOS Core build & test` | `macos-latest` | `scripts/dev.sh check`(移植可能 Core のビルドとテスト)+ 「Windows 専用テストが実行されず SKIPPED になること」の検証 |

- macOS 側は Windows 側のチェック(書式・ライセンス・脆弱性)を**繰り返しません**。
- `Build, test & checks` は `main` の必須ステータス チェックです。**ジョブ名を変更しないでください。**
- `macOS Core build & test` を必須チェックに追加するかどうかは、リポジトリ設定でメンテナーが判断します(本変更では追加していません)。
- リリース ワークフロー(`release.yml`)は Windows 専用のままで、変更していません。

---

## 5. Git クロス PC 同期規範

2 台が未同期のまま互いの成果を上書きしないための手順です。`sync-start` / `sync-finish` はこの手順をそのまま実装しています。

### 作業開始時 — `./scripts/dev.sh sync-start`

1. 作業ツリーの状態を表示する(未コミットの変更があれば報告するだけ)。
2. `git fetch --prune origin`。
3. 現在のブランチと upstream の関係(ahead / behind)を表示する。
4. 安全に同期する。
   - `behind = 0` → 何もしない。
   - 作業ツリーが汚れている → **更新を拒否**し、コミットか `git stash push` を促す。
   - `ahead = 0` かつ `behind > 0` → `git merge --ff-only` のみ。
   - 分岐している(`ahead > 0` かつ `behind > 0`)→ **自動では触らず**、`git rebase` / `git merge` を人間に委ねる。
5. 未プッシュのコミットと stash を一覧表示する(消さない)。

### 作業終了時 — `./scripts/dev.sh sync-finish`

1. 適用できるテストを実行する。**失敗したらここで中止**(コミットもプッシュもしない)。
2. 変更内容を表示する。
3. コミットする。コミット メッセージが与えられていない場合は**コミットせずに終了**します。

   ```sh
   SYNC_MESSAGE="feat(core): ..." ./scripts/dev.sh sync-finish
   ```

   ```powershell
   ./scripts/dev.ps1 sync-finish -Message "feat(core): ..."
   ```

4. プッシュする。拒否された場合は `sync-start` からやり直す。
5. `HEAD` と `origin` が一致していることを確認する。

### スクリプトが絶対にしないこと

- ローカルの変更を自動で上書きしない。
- `git push --force` しない。
- `git reset --hard` しない。
- stash を自動で削除しない。
- 他のブランチや worktree を削除しない。
- 分岐したブランチを自動で rebase / merge しない。

これらが必要な状況は、レポートに `FAIL` または `SKIPPED` として出して人間に返します。

### 2 台を行き来するときの原則

- **片方の PC を離れる前に必ずプッシュする。** 未プッシュのコミットは `sync-start` が警告しますが、もう 1 台からは見えません。
- stash は PC をまたげません。持ち越したい作業は WIP コミットにしてプッシュしてください。
- Worktree はマシンごとにローカルなものです。リポジトリに worktree 構成を持ち込まないでください。
- 改行コードは `.gitattributes` が管理します(`.cs` / `.csproj` / `.ps1` などは CRLF、`.sh` / `.yml` / `.json` / `.md` は LF、インデックスは常に LF)。2 台で差分が出ることはありません。

---

## 6. 既知の制限

- macOS では WPF(`BrowserFlip.App`、`BrowserFlip.App.Tests`)をビルドできません。`EnableWindowsTargeting` であえてビルドを通すことはしていません。コンパイルが通っても Windows の動作検証にはならないためです。
- `WindowsRegistryReader` と P/Invoke の実動作は Windows でしか検証できません。macOS では SKIPPED になります。
- 書式チェック(`dotnet format`)は WPF プロジェクトを含む `BrowserFlip.slnx` を対象にするため Windows でのみ実行します。
- `scripts/dev.ps1` は PowerShell 7 以降が必要です(Windows PowerShell 5.1 では動きません)。macOS に `pwsh` を入れる必要はありません — `scripts/dev.sh` を使ってください。
