# BrowserFlip — Avalonia 技術プロトタイプ (R2)

Windows / macOS / Linux 統合に向けた**技術検証用プロトタイプ**です。製品ではありません。

> **R3/R4 は収束済みです。** 受入状態・CI・テスト・成果物・残存事項の確定記録は [../../docs/r3-r4-outcome.md](../../docs/r3-r4-outcome.md)、Windows 側への引継ぎは [../../docs/windows-handover.md](../../docs/windows-handover.md) にあります。

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

## 開発用ビルドと配布用 `.app` は別物です

| | 開発用 | 配布用 |
| --- | --- | --- |
| 場所 | `src/BrowserFlip.Prototype.Desktop/bin/Release/net10.0/BrowserFlipPrototype` | `artifacts/BrowserFlip Prototype.app` |
| 構成 | **framework-dependent** | **self-contained**(`osx-arm64`) |
| .NET ランタイム | **外部に必要** | **同梱**(`libcoreclr.dylib` ほか) |
| 起動方法 | `./scripts/dev.sh` 経由、または `DOTNET_ROOT` を自分で設定 | ダブルクリック / `open` / 直接実行 |

**開発用の実行ファイルを直接ダブルクリックしても動きません。** .NET SDK が `/usr/local/share/dotnet` ではなくユーザー ローカル(`~/.dotnet` など)に入っている機体では、apphost がランタイムを見つけられず次のように出ます。

```
.NET location: Not found
  Environment variable:
    DOTNET_ROOT_ARM64 = <not set>
    DOTNET_ROOT = <not set>
```

これは製品の欠陥ではありません。**受入・配布は必ず `.app` で行ってください。** 配布用 `.app` は `DOTNET_ROOT` に依存せず、`env -i` で環境変数を全て取り除いても起動することを確認しています。詳細は [../../docs/macos-menu-bar.md](../../docs/macos-menu-bar.md) の MAC-D8。

## 受入用の起動オプション

```sh
"BrowserFlip Prototype.app/Contents/MacOS/BrowserFlipPrototype" \
    --settings-path /tmp/acceptance-settings.json --show-settings ja
```

`--settings-path` は、その実行だけで使う設定ファイルを指定します。**値が無い、または次がフラグの場合は実行を拒否します**(終了コード 2)。実ファイルに静かに戻ってしまえば、守るために作った仕組みが守る対象を書き換えることになるためです。`--show-settings` の言語も `en` / `ja` / `zh-CN` 以外なら拒否します。**検証がユーザーの実設定ファイルを読み書きしないため**に用意しています(設定の保存先はアカウントのホームから解決され、`$HOME` の差し替えでは隔離できません — `env -i HOME=<temp>` で実行しても実ファイルが書かれることを実測しました)。

**ステータス エリア メニューの「設定…」を実際に選んで**設定ウィンドウを開き、画面に出たものを報告して、そのまま起動し続けます(自己診断と違い終了しません)。言語を省略すると切り替えません。自動テストの PASS 行ではなく実画面を確認したいときに使います。

```
choosing the status-area menu item "設定…" ...
  main window     visible=True topmost=True active=False pos=476,157 size=560x620
  settings window visible=True topmost=True active=True  pos=496,131 size=520x700 title="BrowserFlip の設定"
  language picker now reads: システムに合わせる | en | ja | zh-CN
  selected: "ja"  (stored language: ja)
  choosing it again -> 1 settings window(s) open (not duplicated)
  after closing    -> 0 settings window(s) open
  reopening        -> open again, visible=True topmost=True active=True
  main window's settings button reads "設定…"
  main-window "settings" button -> 1 settings window(s) open (same single window)
```

## 設定画面へのアクセス

Owner の受入で「設定ウィンドウ:未確認」となったため、設計と実測を明記します。

| 問い | 答え |
| --- | --- |
| 独立した設定ウィンドウは存在するか | **存在します。** `Views/SettingsWindow.axaml`。520×700、リサイズ可、オーナー中央配置、**モーダルではありません**。同時に 1 つだけ開き、既に開いていれば前面化します |
| メニューバーの「設定…」は何を開くか | その `SettingsWindow` です。`App.ShowSettings()` がメイン ウィンドウをオーナーにして `Show()` します |
| 正確なアクセス方法 | **3 つあります。(1) メイン ウィンドウの「設定…」ボタン**(「状態を更新」の隣)**、(2) メニューバー アイコン → 「設定…」、(3) macOS のアプリケーション メニュー(画面左上)→ 「設定…」(`⌘,`)。** いずれも同じ 1 つのウィンドウに到達します |
| 設定画面に何があるか | 言語、常に手前に表示、フローティング ツールバー、能力テーブル(4 行)、設定ファイルの場所 |

> **設定ウィンドウは「常に手前に表示」に追随します**(どちらの画面で切り替えても。設定ビューモデルがメイン ビューモデルを購読します)。 メイン ウィンドウがオーナーなので、オーナーだけが最前面だと設定ウィンドウはその**真下**に開き、ほぼ完全に重なるため何も起きていないように見えます(実測:メイン 560×620 @476,153 topmost=True、設定 520×700 @496,127 topmost=False)。Owner が設定画面を見つけられなかったのはこれです。初回オープン時に `Activate()` も呼びます(以前は 2 回目以降だけでした)。

> **メニューバー メニューのラベルは言語変更に追随します。** ラベルは生成時の資源参照なので、以前は起動時の言語のまま残りました(実測:日本語 UI で項目が中国語の `设置…`)。

## macOS のアプリケーション メニュー

画面左上の `BrowserFlip` メニューです。日本語 UI では次のように表示されます(実機の `[NSApp mainMenu]` から読み出した値):

```
BrowserFlip について
設定…                 ⌘,
──────────
サービス              ▸
──────────
BrowserFlip を隠す    ⌘H
ほかを隠す            ⌥⌘H
すべてを表示
──────────
BrowserFlip を終了    ⌘Q
```

- **先頭 2 項目が BrowserFlip のもの**です。`BrowserFlip について` は製品自身の About ウィンドウを開きます(以前は Avalonia の `About Avalonia` が出ていました)。`設定…` は上記と同じ `SettingsWindow` を開きます。
- **残りは Avalonia が追記する macOS 標準項目**で、生成・配線・ショートカットは Avalonia のものです。BrowserFlip は**ラベルだけ**を資源から書き換えます。`サービス` は実際に `NSApplication.servicesMenu` に登録されたサブメニューです。
- **en / ja / zh-CN を実行中に切り替えても追随します。**
- このメニューは `App.Initialize()` で `Application` に取り付けます。**`OnFrameworkInitializationCompleted` では遅すぎます** — Avalonia はその手前の `AfterSetup` でメニューを一度だけ書き出すためです。`ApplicationMenuAttachTests` がこれを守ります。
- Windows / Linux にはアプリケーション レベルのネイティブ メニューがないため取り付けません。同じ項目はウィンドウ メニューとステータス エリア メニューから到達できます。

詳細は [../../docs/macos-menu-bar.md](../../docs/macos-menu-bar.md) の §9。

**実機で自動検証しています。** 自己診断のチェック 12 は設定ウィンドウを直接構築するのではなく、**ステータス エリア メニューの項目を実際に選んで**開き、開いたウィンドウの種類・タイトル・サイズ・能力テーブルの行数を記録します。開かなければ `FAIL` です。ステータス エリアが無いセッション(StatusNotifierWatcher の無い Linux など)では、**メイン ウィンドウの「設定…」ボタンに切り替えて**同じ検証を行います。

```
 PASS  12. Settings window opens from the status-area menu
       choosing "設定…" opened SettingsWindow "BrowserFlip 設定", 520x700,
       visible=True; 4 capabilities listed; .../settings-window.png
```

## メニューバー項目の検証

macOS では自己診断が**実際の `NSStatusItem` を OS から読み戻します**。「`TrayIcon.SetIcons` を呼んだ」ことは BrowserFlip についての主張であって macOS についての主張ではなく、それを根拠に PASS を出していたために、クリックしてもメニューが開かない欠陥(MAC-D6)を見逃しました。

- **チェック 3** — メニューバー上の実 `NSStatusBarButton` の表示状態・画像・メニュー項目のラベルを記録します。見つからなければ `FAIL` です。
- **チェック 13** — `+[NSEvent mouseEventWithType:…]` で作った実イベントを `-[NSApplication postEvent:atStart:]` でこのプロセスのキューに投入し、`NativeMenu.Opening` が発火するかを確認します。**合成 CGEvent を使わないため、Accessibility も Screen Recording も不要です。** 結果はレポート本体(`capability-report.json` と `.txt` の `RESULT:` 行)に入るので、メニューが壊れていればレポート全体が `FAIL` になります。

  ステータス項目がメニューバーの混雑などで見つからなかった場合は `FAIL` ではなく `SKIPPED` です(測れなかったことと、押したのに開かなかったことは別)。見つからないこと自体はチェック 3 が `FAIL` にします。

- **チェック 14** — `[NSApp mainMenu]` のアプリケーション メニューを読み戻し、全項目のタイトル・ショートカット・サブメニュー有無を記録します。期待ラベルの欠落、英語のまま残った項目、`Avalonia` を名乗る項目、`設定…` の `⌘,` 欠落のいずれかで `FAIL` です。さらに `-[NSMenu performActionForItemAtIndex:]` で `BrowserFlip について` を選択し、**製品の About ウィンドウが本当に開くか**を確認します(ラベルが正しいことと動くことは別の主張です)。macOS 以外では `UNSUPPORTED`。

プローブを持たない Linux / Windows では、チェック 3 は「このプラットフォームでは読み戻せないので実表示とクリック動作はこのチェックでは未検証」と明記します。

詳細・計測値・再現できなかった条件の一覧は [../../docs/macos-menu-bar.md](../../docs/macos-menu-bar.md)。

## 既知の制限

- **Wayland は未検証。** 能力モデルの Wayland 申告は調査ベースで、実コンポジター上では未測定です。
- **Linux のアイコンは PNG のみ。** SVG のみを持つアプリ(GNOME 系の多く)はアイコンなしで表示されます。
- **CJK フォントを同梱していません。** フォントの無い最小環境では日本語・中国語が豆腐(□)になります。AppImage は依存を宣言できないため、配布時に同梱が必要です。
- **macOS の同意ダイアログは抑止できませんし、抑止しません。** 答えがないまま待ちを打ち切った場合、結果は「不明(PENDING)」であって「失敗」ではありません。
- **`.app` / DMG は未署名**のため、他の Mac では Gatekeeper が拒否します(`verify-bundle.sh` で `rejected` と出るのが正常)。配布条件は [../../docs/macos-signing.md](../../docs/macos-signing.md)。
- **プロトタイプのテストだけ xunit v3** を使います(`Avalonia.Headless.XUnit` 12.x の要件)。製品側のテストは xunit 2.9.3 のままで、両者は別ソリューションです。
- **Wayland では**フローティング ツールバーと「常に手前に表示」が使えません。能力モデルが `UNSUPPORTED` として申告し、UI が理由付きで無効化します。
- **署名・公証は未実施**(証明書未購入)。[../../docs/macos-signing.md](../../docs/macos-signing.md) に必要条件をまとめています。
- **macOS の標準メニュー項目(`サービス` / `隠す` / `ほかを隠す` / `すべてを表示`)の動作は Avalonia の実装です。** BrowserFlip はラベルのみ差し替えています。ラベルとショートカットは実機で読み出して確認済みですが、動作そのものは未確認です。[../../docs/macos-menu-bar.md](../../docs/macos-menu-bar.md) の §9.5。

## Linux 配布(AppImage)

> 配布対象はこのプロトタイプです。**出荷中の Windows 製品(WPF)には影響しません。**

### 作り方

```sh
# 1. self-contained で publish する
dotnet publish src/BrowserFlip.Prototype.Desktop/BrowserFlip.Prototype.Desktop.csproj \
  -c Release -f net10.0 -r linux-arm64 --self-contained true -o out/linux-arm64

# 2. 単一ファイルの AppImage にする
linux/make-appimage.sh out/linux-arm64 BrowserFlip-arm64.AppImage
```

`make-appimage.sh` は AppDir を組み立て、`mksquashfs`(zstd)で固め、AppImage の
**type-2 runtime** を先頭に連結します。

### runtime の入手について

`appimagetool` は Ubuntu のアーカイブに**ありません**。本プロジェクトでは
**公式ソースからビルド**しています(リリース バイナリの取得はしていません)。

| 項目 | 内容 |
| --- | --- |
| runtime ソース | `github.com/AppImage/type2-runtime`(commit `fedfad5`) |
| 依存 | `github.com/vasi/squashfuse`(commit `b008af9`)をソースからビルド |
| ライセンス | AppImage runtime: MIT / squashfuse: BSD-2-Clause |
| ビルド | `clang` + システムの `libfuse3` / `zstd` / `zlib` / `lzma` |

> **上流の公式ビルドは Docker + Alpine(musl)で静的リンクします。** 本番配布では
> その公式手順で作った runtime を使うべきです。ここでビルドした runtime は
> glibc にリンクされるため、**ビルドしたディストリビューションと同等以上の glibc を
> 持つ環境でのみ動作します**。再現性のため上記 commit を固定しています。

### 必要なフォント(重要)

**この AppImage に CJK フォントは同梱されていません。** 日本語・簡体中文の表示には
**システム側に CJK フォントが必要**です。

| ディストリビューション | 導入方法 |
| --- | --- |
| Ubuntu / Debian | `sudo apt install fonts-noto-cjk` |
| Fedora | `sudo dnf install google-noto-sans-cjk-fonts` |
| Arch | `sudo pacman -S noto-fonts-cjk` |
| openSUSE | `sudo zypper install noto-sans-cjk-fonts` |

**CJK フォントが入っていない環境では、日本語と簡体中文が豆腐(□)になります。**
実測で確認済みです — フォント無しでは `fc-match sans:lang=ja` が `DejaVuSans.ttf` に
解決し、CJK 字形を持たないため表示できません。UI の英語表示と機能そのものは影響を受けません。

日本語・中国語のデスクトップ環境には通常これらのフォントが標準で入っています。

> フォントを AppImage に同梱する案は実装しましたが、**同梱するとアプリが起動しません**。
> 詳細と再現条件は [docs/known-defect-font-bundling.md](../../docs/known-defect-font-bundling.md) を参照してください。
> 内蔵対応を進めるかどうかは Owner 判断です。

### 動作要件

| 項目 | 内容 |
| --- | --- |
| アーキテクチャ | ARM64(aarch64)。x64 は下記参照 |
| FUSE | `libfuse2` 系(AppImage の自己マウントに必要)。無い場合は `--appimage-extract-and-run` |
| ホスト側の共有ライブラリ | `libc` `libstdc++` `libm` `libgcc_s` `libdl` `libpthread` のみ。**未解決の依存は 0 件**(実測) |
| .NET | **不要**(self-contained) |

### x64 について

本ラウンドで実機検証したのは **ARM64 のみ**です。x64 版の成果物は作っていません。
理由は、QA 環境(TLZS-Linux-ARM64-QA)が ARM64 であり、x64 の AppImage runtime は
x86_64 上でビルドする必要があるためです。**x64 は未検証**です。
