# R3/R4 成果記録と残存事項

Avalonia による Windows / macOS / Linux 統合プロトタイプの R3/R4 ラウンドの確定記録。

Owner による macOS 実機受入が完了した時点(最終 HEAD `e2b729a`)の状態を記録する。**本書に「PASS」と書かれているのは、実際に実行して確認した項目だけである。** 未検証の項目は §6 に分けて記載し、PASS として扱わない。

---

## 1. 受入状態(Owner 確定)

| 項目 | 状態 |
| --- | --- |
| `MAC_OWNER_ACCEPTANCE` | **PASS** |
| `MAC_DEVELOPMENT` | **COMPLETED** |
| `WINDOWS_PHYSICAL_ACCEPTANCE` | **DEFERRED** |
| `FINAL_INTEGRATION` | **PENDING_WINDOWS_ACCEPTANCE_AND_REVIEW** |
| `RELEASE` | **NOT_AUTHORIZED** |

Owner は HEAD `e2b729a` の目視・操作確認を完了し、macOS 側の受入を承認した。**追加開発および追加修正は行わない。**

`RELEASE = NOT_AUTHORIZED` のため、本ラウンドの成果に対する**新バージョンの公開は行っていない**。`CHANGELOG.md` にリリース エントリーは追加していない。

## 2. 最終 HEAD と PR 構成

| | |
| --- | --- |
| 最終 HEAD | `e2b729a`(+ 本書を含む文書コミット。最終値は PR #13 の head を参照) |
| ブランチ | `feat/unified-cross-platform-r3r4` |
| PR | **#13**(base = `feat/unified-cross-platform-r2` = PR #10 の head) |

**PR #13 は PR #10 の上に積まれている。** 統合順序と必要な Review は [windows-handover.md §2](windows-handover.md) に記載する。

| PR | base | 規模(受入時点 `e2b729a`) | Review |
| --- | --- | --- | --- |
| #10 (R2) | `main` | 48 ファイル / +3,729 / −0 / 2 コミット | `REVIEW_REQUIRED`(`sxrsing` 指名) |
| #13 (R3/R4) | `feat/unified-cross-platform-r2` | 88 ファイル / +13,392 / −221 / 35 コミット | 未実施(`sxrsing` 指名) |

> #13 の規模は macOS 受入時点の値である。本書を含む文書コミットの分だけ増える。

## 3. CI(最終 HEAD `e2b729a`)

**全 9 チェック合格。**

| チェック | 結果 | 時間 |
| --- | --- | --- |
| Build, test & checks | pass | 2m24s |
| Windows Avalonia prototype | pass | 1m37s |
| macOS Avalonia prototype | pass | 1m48s |
| Linux Avalonia prototype | pass | 2m38s |
| macOS Core build & test | pass | 27s |
| Analyze (csharp) | pass | 3m44s |
| CodeQL | pass | 4s |
| Dependency review | pass | 8s |
| TruffleHog OSS | pass | 12s |

## 4. テスト結果(CI ログの実値)

CI ログが出力した値をそのまま記載する。

| ジョブ / プロジェクト | 合格 | スキップ | 合計 |
| --- | --- | --- | --- |
| プロトタイプ(macOS CI、arm64) | **333** | 0 | 333 |
| プロトタイプ(Windows CI、`net10.0-windows` — Windows アダプターのテストを含む) | **333** | 31 | 364 |
| プロトタイプ(Windows CI、`net10.0`) | 300 | 31 | 331 |
| プロトタイプ(Linux CI) | 300 | 31 | 331 |
| `BrowserFlip.Core.Tests`(`net10.0`) | 95 | 0 | 95 |
| `BrowserFlip.Core.Tests`(`net10.0-windows`) | 99 | 0 | 99 |
| `BrowserFlip.Core.Tests`(macOS、Windows 専用はスキップ) | 95 | 4 | 99 |
| `BrowserFlip.App.Tests`(出荷中 WPF 製品) | **405** | 0 | 405 |

Windows ホストではテスト プロジェクトに `net10.0-windows` が追加されるため、Windows CI は 2 回テストを実行する。`net10.0-windows` 側の 364 件が Windows アダプターのテストを含む。

スキップはプラットフォーム依存のテストが対象 OS でないために除外されたもの(`[SupportedOSPlatform("macos")]` のテストは Linux / Windows では実行されない等)。macOS では 333 件すべてが実行され、スキップは 0 である。ローカルの macOS 実機でも同じ 333 件が合格した。

**出荷中 WPF 製品のテスト 405 件は本ラウンドで変更していない。** `git diff --name-only <R2 base>..HEAD -- src/` は空である。

### 自己診断(アプリケーションを実際に起動して実行)

| プラットフォーム | 結果 |
| --- | --- |
| macOS(CI、arm64) | `RESULT: PASS pass=13 pending=0 skipped=1 unsupported=0` |
| macOS(Owner 実機、`/Applications` インストール版) | `RESULT: PASS pass=13 pending=0 skipped=1 unsupported=0` |
| Windows(CI、windows-latest、実ウィンドウ描画確認付き) | `RESULT: PASS pass=11 pending=0 skipped=1 unsupported=1` |
| Linux(CI、実 X11 サーバー) | `RESULT: PASS pass=11 pending=0 skipped=1 unsupported=1` |

`skipped=1` は常にチェック 6(既定ブラウザーの実変更)で、これは `--switch-default` を明示的に渡さない限り実行されない。CI は渡さない。`unsupported=1` はチェック 14(macOS アプリケーション メニュー)で、Windows / Linux にはアプリケーション レベルのメニュー バーが存在しないため。

## 5. 成果物(macOS)

| | |
| --- | --- |
| `.app` | `BrowserFlip Prototype.app` — 113 MB、arm64、最小 macOS 12.0 |
| バンドル識別子 | `co.jp.tlzs.browserflip.prototype` |
| `CFBundleName` / `CFBundleDisplayName` | `BrowserFlip Prototype` |
| 自己完結 | `runtimeconfig.json` に `includedFrameworks`。**`.NET` 環境変数なしで起動することを `env -i` で確認済み** |
| 署名 | **アドホックのみ**(`authority: none`)。Gatekeeper は `rejected` — 未署名ローカル ビルドとして想定どおり |
| DMG | `BrowserFlip-Prototype-arm64.dmg` — 52,031,059 バイト |
| DMG sha256 | `d033066e2eb0bf3403e0b8264a5381cbbbf75f4e9d920d327b77fcb4dee9c50d` |
| 埋め込みバージョン | `0.3.0+e2b729a36359eec78935be8e318f2405b6e21578`(= HEAD `e2b729a`) |
| インストール先 | `/Applications/BrowserFlip Prototype.app`(DMG から導入) |
| 恒久保存 | `~/workspace/30_test/browserflip-macos-d7/artifacts/` |

Linux の成果物(AppDir / squashfs)および Windows の成果物については §6 を参照。

## 6. 検証の度合い(プラットフォーム別)

Owner の証跡分類に従う。**実機で確認していない項目を `END_TO_END_VERIFIED` と書かない。**

分類の定義(リポジトリ内に定義文書が無いため本書に明記する):

| 分類 | 意味 |
| --- | --- |
| `MOCK_TESTED` | モック/フェイクに対してのみ確認 |
| `COMPILE_VERIFIED` | コンパイルが通ることのみ確認 |
| `CI_RUNTIME_VERIFIED` | CI ランナー上で実際に動かして確認 |
| `NATIVE_RUNTIME_VERIFIED` | 実 OS 上でネイティブ API を実際に呼んで確認 |
| `DESKTOP_SESSION_VERIFIED` | 実デスクトップ セッション(実ウィンドウ サーバー/コンポジター)で確認 |
| `END_TO_END_VERIFIED` | 人が実機で目視・操作して確認 |
| `UNSUPPORTED` | 当該プラットフォームで実現不可(理由を併記) |
| `DEFERRED` | 意図的に後回し(未検証) |

| 対象 | 分類 | 根拠 |
| --- | --- | --- |
| macOS arm64 — 全機能 | `END_TO_END_VERIFIED` | Owner の実機目視・操作、`/Applications` インストール版、HEAD `e2b729a` |
| macOS メニューバー アイコン / メニュー | `END_TO_END_VERIFIED` | Owner 受入 + 実 `NSStatusItem` 読み出し + 実 `NSEvent` クリック |
| macOS アプリケーション メニュー | `END_TO_END_VERIFIED` | Owner 受入 + `[NSApp mainMenu]` 読み出し + `-[NSMenu performActionForItemAtIndex:]` による起動 |
| macOS 既定ブラウザー変更 | `NATIVE_RUNTIME_VERIFIED` | 公開 `NSWorkspace` API の呼び出しまで。**OS 同意ダイアログへの応答が必要なため最終回帰は未実施**(§7) |
| Windows x64 | `CI_RUNTIME_VERIFIED` | windows-latest 上で実ウィンドウ描画と自己診断。**物理実機は未検証**(§7) |
| Linux arm64(X11) | `CI_RUNTIME_VERIFIED` + `DESKTOP_SESSION_VERIFIED` | CI の実 X11 サーバーは最終 HEAD。**実デスクトップ セッション(KDE)の測定は本ラウンド中の先行コミット時点のもので、最終 HEAD で再実行していない** |
| Linux x64 | **未実施** | 成果物を作成していない |
| macOS x86-64 | **未実施** | Rosetta 未導入。ABI 分岐は論証のみ(§7) |
| 署名・公証 | **未実施** | Apple Developer Program 加入保留 |

## 7. 残存事項(PASS として扱わない)

Owner 指示により、以下は**未検証・未完了**として明示的に記録する。

| # | 項目 | 状態 | 備考 |
| --- | --- | --- | --- |
| 1 | **Windows 物理実機検証** | `DEFERRED` | CI(windows-latest)では合格。物理実機での目視受入は Windows 側 CC へ引継ぎ。手順は [windows-handover.md](windows-handover.md) |
| 2 | **x86-64 実機検証** | 未実施 | 本機に Rosetta が無い。`MacStatusAreaProbe` の `NSRect` 返却 ABI 分岐(arm64 は `objc_msgSend`、x86-64 は `objc_msgSend_stret`)は**実測ではなく論証**。`osx-x64` は宣言済み RID なので実配置構成である |
| 3 | **MAC-D6 の原因** | **未確定** | Owner が最初に報告した「アイコンをクリックしてもメニューが開かない」は、7 条件すべてで再現しなかった。修正版では Owner 操作でも再現しない。**原因未確定のまま記録する。特定の上流ライブラリの欠陥とも断定しない。** 調査過程で私が失敗した個体(pid 81519)を状態採取前に終了させたため、その個体の状態は永久に確認できない。詳細は [macos-menu-bar.md](macos-menu-bar.md) |
| 4 | **既定ブラウザー変更・復元の最終回帰確認** | 未実施 | macOS は OS 自身が同意ダイアログを出すため、人の応答が必要。本ラウンドで当該コードは変更していないが、**最終 HEAD での変更・復元の通し確認は行っていない** |
| 5 | **正式署名・公証** | 未実施 | アドホック署名のみ。[macos-signing.md](macos-signing.md)。**未署名またはアドホック署名の成果物を正式署名済みと報告しない** |
| 6 | **Linux 正式配布** | 未完了 | `.AppImage` の単一ファイル化は未完了(`appimagetool` の取得が実行ポリシーで拒否された)。サイズ・依存関係・起動可否は測定済み。x64 成果物なし。CJK フォント同梱は既知欠陥として別記([known-defect-font-bundling.md](known-defect-font-bundling.md))。GNOME ネイティブ X11 は `UNSUPPORTED_UPSTREAM` |
| 7 | macOS 標準メニュー項目の**動作** | 未確認 | `サービス` / `隠す` / `ほかを隠す` / `すべてを表示` は Avalonia の実装をそのまま使用。ラベルとショートカットは読み出し確認済みだが、動作は未確認([macos-menu-bar.md §9.5](macos-menu-bar.md)) |
| 8 | フッター行の余白 | 未確認 | ボタン行は折り返さないため、実フォント幅が headless 測定を超えると `設定…` が切れる可能性。推測でレイアウトを変えず、Windows 実画面での確認事項として残す |

## 8. R3/R4 で修正した欠陥

本ラウンドは**プロトタイプの実装ラウンドであり、同時に実機受入で見つかった欠陥の修正ラウンド**だった。主なものを記録する。

### macOS(Owner 実機受入で発見)

| ID | 内容 | 状態 |
| --- | --- | --- |
| MAC-D6 | メニューバー アイコンをクリックしてもメニューが開かない | **原因未確定・再現せず**(§7-3)。同じ画面で実欠陥 3 件を発見・修正 |
| MAC-D6-a | 終了項目が英語リテラル(日本語メニューに `Quit`) | 修正済み |
| MAC-D6-b | アプリケーション メニューが反映されない | **修正済み(MAC-D9 で再修正)**。当初「上流制限で対応不可」と誤って断定した |
| MAC-D6-c | 受入チェック 3 が「`SetIcons` を呼んだ」ことを根拠に PASS していた | 修正済み(実 `NSStatusItem` を読み戻す) |
| MAC-D7 | 異常終了ダイアログ | **原因判明:私が書いた一時調査コード**(生メモリ走査、SIGSEGV)。削除済み。証跡 3 件を起動時刻・スタック・終了コードで同定 |
| MAC-D8 | `bin/Release` 直接起動で `.NET location: Not found` | **仕様どおりの挙動**。開発ビルドの apphost は framework-dependent で、本機の .NET は `~/.dotnet` にある。正式配布は Self-contained `.app` |
| MAC-D9 | `About Avalonia` が出る / 設定項目が無い / 日本語でも英語表示 | **修正済み**。前ラウンドの「対応不可」判断が誤りだったことを含め [macos-menu-bar.md §9](macos-menu-bar.md) に記録 |

### 設定導線(Owner が設定画面を 2 回見つけられなかったことへの対応)

| 内容 | 状態 |
| --- | --- |
| 設定ウィンドウが最前面のオーナーの真下に開き、ほぼ完全に重なって見えなかった | 修正済み |
| 初回オープン時に `Activate()` されていなかった | 修正済み |
| ステータス エリア メニューのラベルが起動時の言語のまま固定されていた | 修正済み |
| 言語選択欄が離れた言語のラベルを保持していた(日本語 UI に `跟随系统`) | 修正済み |
| 設定ウィンドウがメイン画面側の「常に手前に表示」「言語変更」に追随していなかった | 修正済み |
| メイン ウィンドウに設定ボタンが無かった | 追加済み(Owner 承認済みの最小変更) |
| メイン ウィンドウを閉じた後「設定…」が永久に無反応になる(既存の潜在不具合) | 修正済み |

### Linux / Windows / 診断

| 内容 | 状態 |
| --- | --- |
| Wayland でそもそも起動できない / 能力モデルが未配線 | 修正済み |
| 能力モデルを `XDG_SESSION_TYPE` ではなく実バックエンドから決定 | 修正済み |
| XDG パスがホストのパス区切りに依存していた | 修正済み |
| Owner 起動の自己診断スイッチが無限に待機 | 修正済み |
| 実機 Linux ARM64 受入で見つかった 4 件 | 修正済み |
| 自己診断の検証結果がレポート本体・終了コードに入っていなかった | 修正済み |
| 日本語環境でのみ成功するローカライズ テスト | 修正済み |

### 独立レビューで見つかった重大事項

2 回の独立レビューで計 20 件を指摘され、すべて対応した。うち重大 4 件:

- `MacStatusAreaProbe.SendRect` の x86-64 ABI 違反
- チェック 13 の結果が JSON レポートにも `RESULT:` 行にも入っていなかった(壊れていても「全件合格」と報告しうる)
- `SettingsViewModel` がメイン ビューモデルを購読しておらず、修正の効果が別経路で無効化されていた
- 新規ボタンの `Click` 配線を何もテストしていなかった(ハンドラー削除でも全緑)

### 検証自体の欠陥として記録すべき私の失敗

- **Owner の設定ファイルを検証中に上書きした。** 以後、検証は `--settings-path` で隔離ファイルに対して行う。永続化を伴う診断は隔離時のみ実行する(`SelfTest.SettingsAreIsolated`)
- **MAC-D6 を示していた唯一のプロセスを、状態採取前に終了させた**
- **MAC-D7 の異常終了は私が書いた一時調査コードが原因だった**
- **「原因(確定)」と断定した MAC-D6-b の機構説明が誤っていた。** 上流ソースを読まず呼び出し順を推測したため。**以後、上流の制限を結論として書く前に、その上流の固定バージョンのソースを取得して読む。** 経緯は [macos-menu-bar.md §9.1](macos-menu-bar.md) に記録

## 9. 出荷中 WPF 製品への影響

**なし。**

| 確認 | 結果 |
| --- | --- |
| `git diff --name-only <R2 base>..HEAD -- src/` | 空 |
| `BrowserFlip.App.Tests` | 405 件合格(変更なし) |
| `BrowserFlip.Core.Tests` | 95 / 99 件合格(変更なし) |
| 製品の `Strings.resx` | 変更なし。プロトタイプ用の新規文字列はすべて `PrototypeStrings.*.resx` に追加 |
| Windows v0.3.0 | 変更なし |

プロトタイプの各プロジェクトは `BrowserFlip.slnx` に含まれず、リリースにも含まれない。

## 10. 関連文書

| 文書 | 内容 |
| --- | --- |
| [windows-handover.md](windows-handover.md) | **Windows 側 CC への引継ぎ**。PR 統合順序、必要 Review、実機検証チェックリスト |
| [unified-architecture.md](unified-architecture.md) | 三プラットフォーム統合アーキテクチャ、共有コード比率の実測 |
| [macos-menu-bar.md](macos-menu-bar.md) | MAC-D6 / D7 / D8 / D9 の全調査記録 |
| [macos-signing.md](macos-signing.md) | 署名・公証の要件と現状 |
| [linux-feasibility.md](linux-feasibility.md) / [linux-packaging-fonts.md](linux-packaging-fonts.md) / [avalonia-wayland-evaluation.md](avalonia-wayland-evaluation.md) | Linux の実測記録 |
| [known-defect-font-bundling.md](known-defect-font-bundling.md) | CJK フォント同梱の既知欠陥 |
| [r5-candidates.md](r5-candidates.md) | R5 検討候補(本ラウンドで解決した項目は除外済み) |
| [testing.md](testing.md) | 証跡分類 |
| [../prototypes/avalonia/README.md](../prototypes/avalonia/README.md) | プロトタイプのビルド・実行・検証手順 |
