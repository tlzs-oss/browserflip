# BrowserFlip — Avalonia 技術プロトタイプ (R2)

Windows / macOS / Linux 統合に向けた**技術検証用プロトタイプ**です。製品ではありません。

- **出荷中の Windows 製品には一切影響しません。** `BrowserFlip.slnx` には含まれず、ルートの `Directory.Build.props` / `Directory.Packages.props` も変更していません。製品の「第三者ランタイム依存ゼロ」は維持されています。
- 検証対象は macOS ARM64 です。Windows / Linux のアダプターは未実装で、起動はしますが「未対応」と表示します。

設計: [../../docs/unified-architecture.md](../../docs/unified-architecture.md) / Linux 評価: [../../docs/linux-feasibility.md](../../docs/linux-feasibility.md)

---

## 構成

| プロジェクト | 役割 |
| --- | --- |
| `src/BrowserFlip.UI` | 全 OS 共有の Avalonia UI・ViewModel・境界インターフェイス・多言語・自己診断 |
| `src/BrowserFlip.Platform.MacOS` | macOS アダプター(AppKit への P/Invoke、公開 API のみ) |
| `src/BrowserFlip.Prototype.Desktop` | 合成ルート(唯一 OS を判定する場所) |
| `tests/BrowserFlip.Prototype.Tests` | 単体 + Mock + Avalonia ヘッドレス UI テスト |

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

# 普通に起動する
open "artifacts/BrowserFlip Prototype.app"
```

### 自己診断(R2 の検証項目を実機で確認する)

```sh
"artifacts/BrowserFlip Prototype.app/Contents/MacOS/BrowserFlipPrototype" --selftest ./out
```

実際にウィンドウを開き、実際のメニュー バー項目を作り、実際の macOS API を呼んだうえで、`out/capability-report.txt`、`out/capability-report.json`、`out/prototype-window.png`(**アプリが自分の実ウィンドウをレンダリングした画像**)を出力して終了します。終了コードは 0 = 失敗なし、2 = FAIL あり。

### 既定ブラウザーを実際に変更する(Owner のみ)

自動テストは**絶対に実システムを変更しません**。実変更は明示的に要求したときだけ行われ、そのうえで **macOS 自身が確認ダイアログを出します**。

```sh
"artifacts/BrowserFlip Prototype.app/Contents/MacOS/BrowserFlipPrototype" \
    --selftest ./out --switch-default org.mozilla.firefox
```

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

- **Windows / Linux のアダプターは未実装。** 起動はしますが一覧は空で、切り替えは `UNSUPPORTED` を返します。
- **アイコンのペイロードが大きい。** `-[NSWorkspace iconForFile:]` が返す `NSImage` の表現は `NSISIconImageRep` で、PNG へ直接エンコードできません。`TIFFRepresentation` 経由で最大解像度(1〜2 MB)になります。表示側はデコード幅を 64 px に制限しているため UI メモリは小さいままですが、`byte[]` の削減には `CGImageForProposedRect:` のブリッジが必要です。
- **プロトタイプのテストだけ xunit v3** を使います(`Avalonia.Headless.XUnit` 12.x の要件)。製品側のテストは xunit 2.9.3 のままで、両者は別ソリューションです。
- **`.app` に署名していません**(ローカル実行用のアドホック署名のみ)。配布には Developer ID 署名と公証が必要です。
- **Linux は未実行。** コンパイルが通ることだけを確認しています。
