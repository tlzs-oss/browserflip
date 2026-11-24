# 三プラットフォーム統合アーキテクチャ(R2 設計)

BrowserFlip を Windows / macOS / Linux の統合デスクトップ アプリケーションにするための設計です。**R2 は技術検証の段階**で、本ドキュメントは検証結果に基づく設計案です。Windows v0.3.0 (WPF) は完全に維持されます。

関連: [architecture.md](architecture.md)(現行 Windows 製品)/ [cross-platform-dev.md](cross-platform-dev.md)(R1 開発基盤)/ [linux-feasibility.md](linux-feasibility.md) / [../prototypes/avalonia/README.md](../prototypes/avalonia/README.md)

---

## 1. 技術的結論

検証済み(実機 macOS 27 / ARM64、Avalonia 12.1.4、.NET 10):**この技術路線は成立する。**

| 判断 | 根拠 |
| --- | --- |
| Avalonia + .NET 10 で三プラットフォーム統合は可能 | macOS ARM64 で実ビルド・実起動・10 項目中 9 項目 PASS(1 項目は意図的 SKIPPED) |
| 既存 Core は書き換えずに再利用できる | `BrowserFlip.Core` を **1 行も変更せず** macOS で動作。`DefaultBrowserService` / `DefaultBrowserState` / `ChangeTracker` がそのまま正しく動く |
| 共有率は 7 割強 | 実測 **71.5%**([7 章](#7-共有コード比率)) |
| Linux はコンパイル可能、ただし機能差あり | [linux-feasibility.md](linux-feasibility.md) |

---

## 2. モジュール構成

Owner 提示の 5 モジュールは**論理的な層**として採用し、機械的に 5 プロジェクトには分割していません(R2 時点)。

```
┌──────────────────────────────────────────────────────────────┐
│ BrowserFlip.UI            (Avalonia, net10.0)   ← 全 OS 共有  │
│   Views/        MainWindow, FloatingToolbar, Converters      │
│   ViewModels/   MainViewModel, BrowserItemViewModel          │  UI フレームワーク非依存
│   Localization/ Strings (製品の .resx をリンク再利用)        │
│   Platform/     IBrowserCatalog, IDefaultBrowserSwitcher,    │  ← 境界インターフェイス
│                 IAppIconProvider, ISettingsStore,            │
│                 JsonSettingsStore (全 OS 共通実装)           │
└──────────────────────────────────────────────────────────────┘
                              │ 依存
┌──────────────────────────────────────────────────────────────┐
│ BrowserFlip.Core          (net10.0 / net10.0-windows)        │
│   Detection/ Defaults/ Settings/   ← OS 非依存ロジック        │
│   Platform/                        ← 既存の境界インターフェイス│
│   Platform/Windows/                ← Windows 実装 (R1 で分離) │
└──────────────────────────────────────────────────────────────┘
                              ▲ 実装
          ┌───────────────────┼───────────────────┐
┌─────────┴────────┐ ┌────────┴─────────┐ ┌───────┴──────────┐
│ Platform.Windows │ │ Platform.MacOS   │ │ Platform.Linux   │
│ (= Core/Platform/│ │ (R2 で新規実装)  │ │ (未実装)         │
│    Windows, 既存)│ │ AppKit P/Invoke  │ │ xdg-settings 等  │
└──────────────────┘ └──────────────────┘ └──────────────────┘
                              ▲
┌──────────────────────────────────────────────────────────────┐
│ Prototype.Desktop — 合成ルート(唯一 OS を知る場所)          │
└──────────────────────────────────────────────────────────────┘
```

### なぜ 5 プロジェクトに分けないか

- `Platform.Windows` は **R1 で既に `Core/Platform/Windows/` として分離済み**で、`net10.0` ターゲットから除外されています。別プロジェクトに切り出す利点は現時点でありません。
- `Platform.Linux` は実装が存在しないため、空プロジェクトを作っても意味がありません。
- 分割が必要になる判断基準は「**Linux 実装が Windows/macOS と異なる NuGet 依存を必要としたとき**」です。それまでは現状維持が最小変更です。

---

## 3. 既存 Core が無改造で再利用できた理由

これが R2 で最も重要な発見です。

`BrowserFlip.Core` の既定ブラウザー判定は、**ProgID という「ハンドラーを識別する不透明な文字列」**の上に組まれていました。macOS の **Bundle Identifier** はまったく同じ役割を持ちます。

| Core の概念 | Windows | macOS | Linux(想定) |
| --- | --- | --- | --- |
| `BrowserInfo.Id` / `HttpProgId` / `HttpsProgId` | `ChromeHTML` | `com.google.Chrome` | `google-chrome.desktop` |
| `BrowserInfo.ExecutablePath` | `C:\...\chrome.exe` | `/Applications/Google Chrome.app` | `/usr/bin/google-chrome` |
| `IAssociationQuery` | `AssocQueryStringW` | `NSWorkspace URLForApplicationToOpenURL:` | `xdg-settings get` |
| `RegistrationScope` | HKCU / HKLM | `~/Applications` / `/Applications` | `~/.local/share` / `/usr/share` |

結果として、以下は**三プラットフォームで完全共有**できます。

- `DefaultBrowserState.Status`(None / Consistent / Mismatch / Unrecognized の判定)
- `DefaultBrowserState.RoleOf`(Both / HttpOnly / HttpsOnly / NotDefault)
- `DefaultBrowserService.Match`(ProgID 一致 → 所有 ProgID → 実行ファイル パス一致のフォールバック)
- `ChangeTracker`(Pending / AppliedToTarget / PartiallyApplied / ChangedToOther / NoChange)

これは `tests/BrowserFlip.Prototype.Tests/CoreReuseTests.cs` の 8 テストで検証しています。

---

## 4. 既定ブラウザー切り替えの三方式

BrowserFlip の原則「**BrowserFlip は関連付けを自分で書き換えない。確定は必ずユーザーが行う**」は三プラットフォームで維持できます。

| | Windows | macOS | Linux |
| --- | --- | --- | --- |
| `SwitchMechanism` | `OpensSystemSettings` | `SystemConsentPrompt` | `CommandLineTool` |
| API | `ms-settings:defaultapps` を起動 | `NSWorkspace.setDefaultApplicationAtURL:toOpenURLsWithScheme:completionHandler:`(macOS 12+、公開 API) | `xdg-settings set default-web-browser` |
| 確定者 | ユーザーが「既定に設定」を押す | **OS が確認ダイアログを出し**、ユーザーが承認 | **コマンドが即座に適用**(確認なし) |
| 結果の確認 | Windows から読み直す | OS から読み直す(本実装は最大 20 秒ポーリング) | 読み直す |

> **Linux だけは確認ダイアログがありません。** `xdg-settings` は即座に適用します。UI 文言と確認フローは Linux だけ別扱いが必要です([linux-feasibility.md](linux-feasibility.md) 参照)。

`IDefaultBrowserSwitcher.Mechanism` を UI が読み、文言を切り替える設計にしてあるため、この差は正直に表示できます。

---

## 5. Windows 互換(R2 で何も壊していない)

| 保護対象 | 状態 |
| --- | --- |
| `src/BrowserFlip.App`(WPF) | **変更なし**(1 行も触れていない) |
| `src/BrowserFlip.Core` | **変更なし** |
| `tests/BrowserFlip.App.Tests` / `BrowserFlip.Core.Tests` | **変更なし** |
| `BrowserFlip.slnx` | **変更なし**(プロトタイプは別ソリューション) |
| `Directory.Build.props` / `Directory.Packages.props`(ルート) | **変更なし**(プロトタイプは入れ子の props で上書き) |
| `.github/workflows/release.yml` | **変更なし** |
| v0.3.0 Release 資産 / タグ | **変更なし** |
| 製品の第三者依存 | **ゼロのまま**(Avalonia はプロトタイプ専用ソリューションのみ) |

R2 の変更は `prototypes/` 配下と新規ドキュメント、CI の追加ジョブだけです。

### 将来の Windows UI 移行

本設計は「WPF を今すぐ捨てる」ものではありません。移行可能性だけを確保しています。

1. `MainViewModel` は **Avalonia 型も WPF 型も参照していません**(`INotifyPropertyChanged` のみ)。そのまま WPF からもバインドできます。
2. `Strings.resx` は WPF 製品側が正本で、Avalonia 側はリンク参照です。翻訳の二重管理は発生しません。
3. Windows 実装は Core の既存インターフェイスを満たしているため、`IBrowserCatalog` / `IDefaultBrowserSwitcher` の Windows アダプターは既存コードの薄いラッパーで作れます(R2 では未実装)。

移行は「Avalonia UI を Windows でも動かす → 並行提供 → 旧 WPF を廃止」の順で、各段階で止められます。

---

## 6. 使用している macOS API(すべて公開 API)

Apple の非公開 API は使用していません。

| 用途 | API | 公開状況 |
| --- | --- | --- |
| 既定ブラウザー読み取り | `-[NSWorkspace URLForApplicationToOpenURL:]` | 公開 |
| ブラウザー一覧 | `-[NSWorkspace URLsForApplicationsToOpenURL:]` | 公開(macOS 12+) |
| 既定ブラウザー変更要求 | `-[NSWorkspace setDefaultApplicationAtURL:toOpenURLsWithScheme:completionHandler:]` | 公開(macOS 12+) |
| バンドル情報 | `+[NSBundle bundleWithURL:]`、`bundleIdentifier`、`objectForInfoDictionaryKey:` | 公開 |
| アイコン | `-[NSWorkspace iconForFile:]` → `NSBitmapImageRep` → PNG | 公開 |
| API 有無の判定 | `respondsToSelector:` | 公開 |

呼び出しは `objc_msgSend` 経由の P/Invoke(`DllImport`、`AllowUnsafeBlocks=false` のまま)で、Windows 側の `NativeMethods.cs` と同じ様式です。

---

## 7. 共有コード比率(R3/R4 実測)

空行を除く `.cs` + `.axaml` 行数。

| 区分 | LOC |
| --- | --- |
| Core の OS 非依存ロジック | 688 |
| 共有 Avalonia UI / ViewModel / 境界契約 | 1,610 |
| **共有合計** | **2,298** |
| Core の Windows 専用部分(レジストリ・P/Invoke) | 118 |
| macOS アダプター | 597 |
| Windows アダプター | 630 |
| Linux アダプター | 1,521 |
| 合成ルート | 126 |
| **プラットフォーム固有合計** | **2,992** |
| **共有率** | **43.4%** |

### R2 の見込み(約 60%)から下振れした理由

**Linux アダプターが macOS / Windows の約 2.5 倍あるため**です。実装の質の問題ではなく、構造的な差です。

| | ブラウザー一覧 | 既定ブラウザー | アイコン |
| --- | --- | --- | --- |
| Windows | OS がレジストリで提供 | `AssocQueryStringW` | `SHDefExtractIconW` |
| macOS | `NSWorkspace` が提供 | `NSWorkspace` | `NSWorkspace` |
| **Linux** | **仕様を自前実装**(`.desktop` 走査) | **`xdg-settings` をラップ + 出力解析** | **XDG Icon Theme 仕様を自前実装** |

Linux には LaunchServices / レジストリに相当する API がなく、freedesktop の仕様(Desktop Entry、XDG Base Directory、Icon Theme)を BrowserFlip 側で実装する必要があります。`DesktopEntry.cs` だけで 343 行(コメント除く)あり、これは他 OS では OS が担っている部分です。

**この数字が下がったこと自体は問題ではありません。** 重要なのは次の点です。

- **UI・ViewModel・業務ロジックは全面共有**で、プラットフォーム固有コードは**すべて境界インターフェイスの裏**にあります。
- 画面を 1 つ足すと共有側だけが増え、アダプターは増えません。
- したがって比率は**今後 UI を作り込むほど再び上昇します**。43.4% は「アダプターを 3 つ書き終えた直後」という、比率が最も低くなる時点の値です。
