# テスト方針

BrowserFlip のテスト戦略、実行方法、実機受け入れテストの手順をまとめます。要件は [requirements.md](requirements.md)、設計は [architecture.md](architecture.md) を参照してください。

## 1. 基本ルール

> **Windows の設定を実際に変更するテストは、隔離された Windows テスト機または VM でのみ実行すること。CI や開発者の普段使いの PC では絶対に実行しない。**

- 単体テストは OS の状態を読み書きしない。レジストリ・ファイル システム・関連付け・URI 起動はすべてフェイクに差し替える。
- CI(GitHub Actions)で実行するのは単体テストのみ。CI ランナー上で既定ブラウザーの変更や `ms-settings:` の起動は行わない。
- 実機での手動受け入れテストは、スナップショットに戻せる VM か、専用のテスト用アカウント / 端末で実施する。

## 2. 単体テスト

### 構成

| 項目 | 内容 |
| --- | --- |
| フレームワーク | xUnit(`xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`) |
| カバレッジ | `coverlet.collector` |
| プロジェクト | `tests/BrowserFlip.Core.Tests`(`InternalsVisibleTo` により internal メンバーも検証可能) |
| パッケージ管理 | `Directory.Packages.props` による集中管理(テスト専用パッケージのみ) |

### フェイク

Core は OS 依存をすべて `BrowserFlip.Core.Platform` のインターフェイス越しに扱うため、以下をフェイクで置き換えます。

| インターフェイス | フェイクの役割 |
| --- | --- |
| `IRegistryReader` | ルート(HKCU/HKLM)・ビュー(既定/64/32)・パス・値名をキーにしたインメモリ レジストリ |
| `IFileSystem` | 存在するファイル パスの集合 |
| `IIndirectStringResolver` | `@...` 文字列の解決結果の辞書(解決不能 = `null`) |
| `IAssociationQuery` | `http` / `https` に対する `AssociationInfo` を返す。テスト中に値を差し替えて変更を再現 |
| `IUriLauncher` | 起動された URI を記録し、指定に応じて例外を投げる |
| `IOsInfo` | 任意のビルド番号と UBR(例: 19045 = Windows 10、22621.1555 = Windows 11 22H2 2023-04 更新)を返す |

### カバー対象

| 対象 | 主な観点 |
| --- | --- |
| `RegistryValueParser` | 引用符あり/なしのコマンド、空白を含むパス、`.exe` なし、`DefaultIcon` のインデックス(負数含む)、パス正規化 |
| `BrowserDetector` | HKCU 優先、HKLM 64/32 の重複排除、URL 関連付けのないキーの除外、表示名の優先順位と間接文字列、`RegisteredApplications` の対応付け(パス/スコープ/名前)、`Health` の判定、並び順 |
| `DefaultBrowserService` | ProgID の完全一致 / 所有 ProgID 一致 / 実行ファイル パス一致による対応付け、未設定時 |
| `DefaultBrowserState` | `Status`(`None` / `Consistent` / `Mismatch` / `Unrecognized`)の全分岐、`RoleOf`、`SameAssociationsAs` |
| `ChangeTracker` | `Pending` / `AppliedToTarget` / `PartiallyApplied` / `ChangedToOther` / `NoChange` を `userReturned` の真偽ごとに |
| `PolicyInspector` | `NoControlPanel`(HKCU/HKLM)、`SettingsPageVisibility` の `hide:` / `showonly:` と大文字小文字・空白、`DefaultAssociationsConfiguration` |
| `SettingsLauncher` | `SupportsAppDeepLink(build, ubr)` の境界値(21999 / 22000.1816 / 22000.1817 / 22621.1554 / 22621.1555 / 22631.0)、`registeredAppUser` / `registeredAppMachine`、名前のエスケープ、ディープリンク失敗時のフォールバック、`Failed`、`BlockedByPolicy`、`BrowserUnavailable` |
| `ShellUriLauncher` | `ms-settings:` 以外のスキームを拒否する(実際の起動は行わない) |
| 多言語リソース | `Strings.resx` のすべてのキーが `Strings.ja.resx` と `Strings.zh-CN.resx` に存在し、空でないこと |

### 実行方法

```powershell
# すべてのテスト
dotnet test BrowserFlip.slnx

# Release 構成
dotnet test BrowserFlip.slnx -c Release

# カバレッジ収集(Cobertura 形式が TestResults 配下に出力される)
dotnet test BrowserFlip.slnx --collect:"XPlat Code Coverage"

# フォーマット確認(CI と同じ)
dotnet format BrowserFlip.slnx --verify-no-changes
```

カバレッジの目安: Core のロジック(`Detection` / `Defaults` / `Settings`)は行カバレッジ 80% 以上。P/Invoke や `Registry` を直接呼ぶ Windows 実装クラス(`WindowsRegistryReader`, `ShellAssociationQuery`, `ShellIndirectStringResolver`)は単体テストの対象外とし、実機受け入れテストで確認します。

## 3. CI で実行するもの

| チェック | 内容 |
| --- | --- |
| Build | Windows ランナーで Release ビルド(警告はエラー) |
| Unit tests | `dotnet test`(フェイクのみ、OS の設定は変更しない) |
| Format | `dotnet format --verify-no-changes` |
| CodeQL | C# の静的解析 |
| Dependency review / license | 依存関係レビューと `scripts/license-check.ps1` によるライセンス許可リスト確認 |
| Secret scan | TruffleHog |
| Release artifacts | `win-x64` / `win-arm64` の自己完結型シングルファイル zip と `SHA256SUMS` の生成(GitHub Release への公開はメンテナーが手動で行う) |

## 4. 実機受け入れテスト(手動)

### 準備

- [ ] 隔離されたテスト機または VM(スナップショット取得済み)を用意する。
- [ ] 標準ユーザー(管理者でない)アカウントでサインインする。
- [ ] 2 種類以上のブラウザー(例: Microsoft Edge と Firefox / Chrome)をインストールする。
- [ ] 結果記録用に OS のエディションとビルド番号(`winver`)を控える。

### Windows 11(23H2 以降、または 2023 年 4 月の累積更新プログラム適用済みの 21H2 / 22H2)

| # | 手順 | 期待結果 | 結果 |
| --- | --- | --- | --- |
| 1 | BrowserFlip を起動 | UAC プロンプトなしで起動し、インストール済みブラウザーがカード表示される | [ ] |
| 2 | 一覧を確認 | 現在の既定ブラウザーにバッジが付いている。アイコンと名前が正しい | [ ] |
| 3 | 既定でないブラウザーのカードをクリック | そのブラウザー専用の「既定のアプリ」ページが開く | [ ] |
| 4 | 設定で「既定値に設定」→ BrowserFlip に戻る | バッジが新しいブラウザーに移動し、成功が表示される | [ ] |
| 5 | もう一度カードをクリックし、設定で何もせずに戻る | 「変更されませんでした」旨の情報が表示される | [ ] |
| 6 | 設定で `HTTP` のみ別のブラウザーに変更して戻る | `http` / `https` 不一致の警告が表示される | [ ] |
| 7 | 設定で別のブラウザーを既定にして戻る | 別アプリに変更された旨が表示され、バッジが正しく移動する | [ ] |
| 8 | ブラウザーを 1 つアンインストール(登録が残るケース)して再起動 | 該当カードが無効表示になり、クリックしても設定が開かない | [ ] |
| 9 | Windows のテーマをライト ⇔ ダークに切り替え | アプリの配色が追従する | [ ] |
| 10 | 拡大率 100% / 150% / 200%、異なる DPI のモニター間を移動 | ぼやけ・はみ出しがない | [ ] |
| 11 | UI 言語を English / 日本語 / 简体中文 に切り替え | 実行中に全文言が切り替わり、未翻訳の文言がない | [ ] |
| 12 | OS の表示言語を日本語 / 簡体字中国語にして初回起動 | 対応する言語で起動する | [ ] |
| 13 | マウスを使わず Tab / Enter / Space で操作 | すべてのカードと操作要素に到達・実行できる | [ ] |
| 14 | ナレーターで一覧を読み上げ | 各カードの名前と既定状態が読み上げられる | [ ] |
| 15 | (VM のみ)ポリシー `SettingsPageVisibility = hide:defaultapps` を設定 | 設定を開かず、ポリシーによりブロックされた旨を表示する | [ ] |
| 16 | (VM のみ)ポリシー `NoControlPanel = 1` を設定 | 同上 | [ ] |
| 17 | (VM のみ)`DefaultAssociationsConfiguration` を構成 | サインイン時に元に戻る可能性がある旨の警告を表示する | [ ] |
| 18 | ネットワークを切断して起動・操作 | すべて正常に動作する(ネットワーク不要) | [ ] |
| 19 | (可能なら)2023 年 4 月の更新プログラム未適用の 21H2 / 22H2 VM でカードをクリック | ブラウザー別ページではなく「既定のアプリ」トップページが開く | [ ] |

> 手順 15〜17 はポリシーを変更するため、必ずスナップショットに戻せる VM で実施し、終了後に復元してください。

### Windows 10(ビルド 17763 以上)

| # | 手順 | 期待結果 | 結果 |
| --- | --- | --- | --- |
| 1 | BrowserFlip を起動 | UAC なしで起動し、ブラウザーが表示される | [ ] |
| 2 | カードをクリック | 「既定のアプリ」トップページが開く(ブラウザー別ページは対応ビルドの Windows 11 のみ) | [ ] |
| 3 | 「Web ブラウザー」で選択して戻る | 表示が更新される | [ ] |
| 4 | 何もせずに戻る | 「変更されませんでした」旨の情報が表示される | [ ] |
| 5 | テーマ / DPI / 言語 / キーボード操作 | Windows 11 の手順 9〜14 と同等 | [ ] |

### 記録

テスト結果は PR またはリリース準備用の Issue に、OS ビルド番号、アーキテクチャ(x64 / Arm64)、ブラウザーとバージョン、合否、スクリーンショット(個人情報が写り込まないもの)を添えて記録します。
