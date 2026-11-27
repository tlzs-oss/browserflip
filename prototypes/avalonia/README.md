# BrowserFlip — Avalonia 技術プロトタイプ (R2)

Windows / macOS / Linux 統合に向けた**技術検証用プロトタイプ**です。製品ではありません。

- **出荷中の Windows 製品には一切影響しません。** `BrowserFlip.slnx` には含まれず、ルートの `Directory.Build.props` / `Directory.Packages.props` も変更していません。製品の「第三者ランタイム依存ゼロ」は維持されています。
- **macOS・Windows・Linux の 3 アダプターすべて実装済み**です。macOS と Linux ARM64 は実機/実コンテナで動作確認済み、Windows は CI(windows-latest)で動作確認済みです。

設計: [../../docs/unified-architecture.md](../../docs/unified-architecture.md) / Linux 評価: [../../docs/linux-feasibility.md](../../docs/linux-feasibility.md)

---

## 構成

| プロジェクト | 役割 |
| --- | --- |
| `src/BrowserFlip.UI` | 全 OS 共有の Avalonia UI・ViewModel・境界インターフェイス・能力モデル・多言語・自己診断 |
| `src/BrowserFlip.Platform.MacOS` | macOS アダプター(AppKit への P/Invoke、公開 API のみ) |
| `src/BrowserFlip.Platform.Linux` | Linux アダプター(`.desktop` 走査、`xdg-settings`、XDG アイコン) |
| `src/BrowserFlip.Platform.Windows` | Windows アダプター(既存 Core の Windows 実装を再利用) |
| `src/BrowserFlip.Prototype.Desktop` | 合成ルート(唯一 OS を判定する場所) |
| `tests/BrowserFlip.Prototype.Tests` | 単体 + Mock + Avalonia ヘッドレス UI テスト |

### 能力モデル(`IPlatformCapabilities`)

三プラットフォームは**本当に違います**。Wayland はクライアントに自ウィンドウの配置も最前面要求も許さず、GNOME は既定でトレイを持たず、`xdg-settings` は確認なしで即適用します。

不安定な回避策で取り繕うのではなく、各アダプターが「**何ができて、できないものはなぜか**」を申告し、UI はできないものを**理由付きで無効化**します。設定ウィンドウに能力一覧表が出ます。

`BrowserFlip.Core` は**リポジトリの出荷版をそのまま参照**しています(コピーではありません)。

---

## ビルドと実行

```sh
cd prototypes/avalonia

# ビルドとテスト
dotnet build BrowserFlip.Prototype.slnx -c Release
dotnet test  tests/BrowserFlip.Prototype.Tests/BrowserFlip.Prototype.Tests.csproj -c Release

# macOS の .app を作る(self-contained。Finder から起動するため)
./macos/bundle.sh Release

# 配布用 DMG を作る(未署名。下記「署名」を参照)
./macos/make-dmg.sh Release

# 製品アイコン(.icns)だけを作る。bundle.sh が内部で呼ぶので通常は不要
./macos/make-icns.sh

# バンドルの実態を確認する(アーキテクチャ・アイコン・署名・Gatekeeper 判定)
./macos/verify-bundle.sh

# 普通に起動する
open "artifacts/BrowserFlip Prototype.app"
```

Linux では実 X11 サーバー(Xvfb)上で同じ自己診断を実行できます。

```sh
./linux/run-selftest.sh ./out Release
```

### 自己診断(R2 の検証項目を実機で確認する)

```sh
# .app として起動する(LaunchServices 経由。メニューバー項目と Dock アイコンは
# これでないと本番と同じ経路になりません)
open -n "artifacts/BrowserFlip Prototype.app" --args --selftest ./out

# 直接バイナリを起動してもよい(標準出力に表が出る)
"artifacts/BrowserFlip Prototype.app/Contents/MacOS/BrowserFlipPrototype" --selftest ./out
```

実際にウィンドウを開き、実際のメニュー バー項目を作り、実際の macOS API を呼んだうえで、`out/capability-report.txt`、`out/capability-report.json`、`out/prototype-window.png`(**アプリが自分の実ウィンドウをレンダリングした画像**)を出力して終了します。

終了コード: **0** = 問題なし / **2** = FAIL あり / **3** = PENDING あり(開始したが OS がまだ答えていない項目がある)。

`PENDING` は `FAIL` ではありません。macOS の同意ダイアログは `CoreServicesUIAgent` のもので、ユーザーが答えるまで出たままになり、**要求元プロセスが終了しても消えません**(実測: 画面上に 7.5 分)。答えた時点で変更は適用されます(実測: Firefox で 44 秒後、Chrome で 474 秒後)。待ちを打ち切ったことを「失敗」と報告するのは事実に反するため、別の状態にしてあります。

### 既定ブラウザーを実際に変更する(Owner のみ)

自動テストは**絶対に実システムを変更しません**。実変更は明示的に要求したときだけ行われ、そのうえで **macOS 自身が確認ダイアログを出します**。

```sh
open -n "artifacts/BrowserFlip Prototype.app" --args \
    --selftest ./out --switch-default org.mozilla.firefox
```

**1 回の操作に対して OS のダイアログは 1 つだけ**です。`https` を 1 回要求すると macOS が
http / https を同時に更新するため(実測)、要求も 1 回にしてあります。OS が片方しか更新しなかった
場合に限り、残りのスキームをもう一度要求します(その場合はダイアログがもう 1 つ出ます)。

結果はすべて **OS に問い直して**から判定します(`NSWorkspace.urlForApplication(toOpen:)`)。
ダイアログに答えがないまま待ちを打ち切ったときは `PENDING` であって `FAIL` ではなく、
アプリはそのまま最大 10 分バックグラウンドで監視し、適用された時点で表示を更新します。

---

## 検証結果(実機 macOS ARM64)

```
----------------------------------------------------------------------------------------------------
 BrowserFlip Avalonia prototype — self-test on macOS Unix 27.0.0 (Arm64), Avalonia 12.1.4.0
----------------------------------------------------------------------------------------------------
 PASS          1. Avalonia builds and runs on macOS ARM64      process is live; Avalonia 12.1.4.0; Arm64
 PASS          2. Native desktop window displayed              IsVisible=True bounds=560x620 handle=7C98EECF00
 PASS          3. System menu-bar icon and menu                TrayIcon registered with a 5-item NativeMenu (NSStatusItem)
 PASS          4. Installed browser list                       7 found: Brave Browser, Firefox, Google Chrome, Google Chrome for Testing, Microsoft Edge, Safari, Vivaldi
 PASS          5. Current default browser                      status=Consistent; default=Firefox; text="現在の既定のブラウザー: Firefox"
 SKIPPED       6. Default-browser switch API                   available via SystemConsentPrompt (public NSWorkspace API, OS-owned consent prompt). Not invoked: a real system change requires --switch-default <bundle-id>, triggered by the Owner.
 PASS          7. Browser icons                                7/7 decoded as PNG; sizes: 1512161B, 2202312B, 1733760B, 1682880B
 PASS          8. English / Japanese / Simplified Chinese      en="Refresh status"  ja="状態を更新"  zh-CN="刷新状态"
 PASS          9. Topmost window and floating toolbar          main.Topmost=True; toolbar visible=True, topmost=True, decorations=None, size=300x52
 PASS         10. Settings persistence                         /Users/sxrsing/Library/Application Support/BrowserFlip/prototype-settings.json: wrote LaunchCount=1843, read back 1843, Language=ja
----------------------------------------------------------------------------------------------------
 RESULT: PASS   pass=9 skipped=1 unsupported=0
----------------------------------------------------------------------------------------------------
```

---

## 既知の制限

- **Wayland は未検証。** 能力モデルの Wayland 申告は調査ベースで、実コンポジター上では未測定です。
- **Linux のアイコンは PNG のみ。** SVG のみを持つアプリ(GNOME 系の多く)はアイコンなしで表示されます。
- **CJK フォントを同梱していません。** フォントの無い最小環境では日本語・中国語が豆腐(□)になります。AppImage は依存を宣言できないため、配布時に同梱が必要です。
- **macOS の同意ダイアログは抑止できませんし、抑止しません。** 答えがないまま待ちを打ち切った場合、結果は「不明(PENDING)」であって「失敗」ではありません。
- **`.app` / DMG は未署名**のため、他の Mac では Gatekeeper が拒否します(`verify-bundle.sh` で `rejected` と出るのが正常)。配布条件は [../../docs/macos-signing.md](../../docs/macos-signing.md)。
- **プロトタイプのテストだけ xunit v3** を使います(`Avalonia.Headless.XUnit` 12.x の要件)。製品側のテストは xunit 2.9.3 のままで、両者は別ソリューションです。
- **Wayland では**フローティング ツールバーと「常に手前に表示」が使えません。能力モデルが `UNSUPPORTED` として申告し、UI が理由付きで無効化します。
- **署名・公証は未実施**(証明書未購入)。[../../docs/macos-signing.md](../../docs/macos-signing.md) に必要条件をまとめています。
