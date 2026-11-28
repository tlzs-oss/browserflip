# macOS メニューバー項目(MAC-D6)調査記録

Owner による実機目視受入の結果:

- メニューバー アイコンの表示 — **PASS**
- アイコンのクリックによるメニュー表示 — **FAIL**(アイコンは見えるが、クリックしてもメニューが開かない)

本書はその調査で**実測した内容**、再現を試みて**再現しなかった**こと、調査の過程で**別に見つかった実欠陥**、および**まだ検証されていないこと**を区別して記録する。

環境:macOS 27.0.0(arm64)、Avalonia 12.1.4、`BrowserFlip Prototype.app`(アドホック署名のみ)。

---

## 1. 結論(先に)

| | 状態 |
| --- | --- |
| MAC-D6(クリックでメニューが開かない) | **再現せず。** 下記 7 条件すべてで、実イベントによるクリックはメニューを開いた。修正版では **Owner の実機操作でも再現せず**(メニューバー アイコン及びメニュー表示:PASS)。**原因は未確定のまま**とする |
| MAC-D6 と同じ画面で見つかった実欠陥 | **3 件。すべて修正済み**(§4) |
| Owner の指摘の扱い | **取り下げない。** 自動検証を追加し、再受入で証跡が残るようにした(§5) |
| アプリケーション メニュー(MAC-D9) | **修正済み**(§9)。`BrowserFlip について` / `設定… ⌘,` / `サービス` / `隠す` / `ほかを隠す` / `すべてを表示` / `BrowserFlip を終了 ⌘Q` を 3 言語で表示。**前ラウンドの「上流制限で対応不可」という結論は誤りだった** |

**私は「Owner の観測が誤り」とは主張しない。** 再現できていない以上、原因未確定である。原因未確定のまま「修正した」と報告することもしない。

### 調査上の私の失敗

Owner が実際にクリックした**プロセス(pid 81519、7 時間 22 分稼働)を、状態を採取する前に私が終了させた。** 端末出力を取るためだったが、失敗していた唯一の個体を失った。あの個体でメイン スレッドが生きていたか、モーダル ループに入っていたかは、もう確認できない。

---

## 2. 実測したこと

### 2.1 NSStatusItem にメニューは付いている

公開 Objective-C ランタイム API(`class_copyIvarList` / `object_getIvar`、いずれも文書化済み)でプロセス内から辿った結果:

```
ITEM tooltip=BrowserFlip -> NSKVONotifying_NSSceneStatusItem menu=AvnMenu menuItems=6
  [0] 'BrowserFlip'                     AvnMenuItem enabled=1 action=didSelectItem: target=AvnMenuItem
  [1] '状態を更新'                       AvnMenuItem enabled=1 action=didSelectItem: target=AvnMenuItem
  [2] 'フローティング ツールバーを表示'   AvnMenuItem enabled=1 action=didSelectItem: target=AvnMenuItem
  [3] '設定…'                            AvnMenuItem enabled=1 action=didSelectItem: target=AvnMenuItem
  [4] '-'                                NSMenuItem  separator
  [5] 'Quit'                             AvnMenuItem enabled=1 action=didSelectItem: target=AvnMenuItem
```

`NSStatusItem.menu` は nil ではなく、6 項目すべてが有効で、各項目に action と target が設定されている。managed 側も整合していた(`_target=TrayIcon`、`_menu=NativeMenu(6 items)`、`_nativeMenu` 生成済み、`_trayIcon` 非 null、`_exported=True`)。

### 2.2 外から見える属性は「正常な対照」と完全に一致する

**この調査で一度誤った推定をした。** 最初に `button.target=nil`、`button.action=nil`、`button.menu=nil` を観測し、「メニューが付いていない決定的証拠」と判断した。

これを検証するため、**同一プロセス内に公開 AppKit API だけで対照用の NSStatusItem を作り**、2 項目の NSMenu を文書どおり `setMenu:` で取り付けた。結果:

| | Avalonia の項目 | 対照(メニュー有りと確証済み) |
| --- | --- | --- |
| `button.target` | nil | **nil** |
| `button.action` | nil | **nil** |
| `button.menu` | nil | **nil** |
| `item.visible` | True | True |
| `item.length` | -2.0 | -2.0 |
| `item.behavior` | 0 | 0 |
| `button.enabled` | True | True |
| `button.appearsDisabled` | False | False |
| `button.hidden` | False | False |
| `button.cell` | NSStatusBarButtonCell | NSStatusBarButtonCell |

**つまり `target=nil` は異常ではない。** 私の最初の推定は誤りで、対照実験がそれを示した。両者の唯一の差は NSMenu の delegate(Avalonia は `AvnMenuDelegate`、対照は nil)である。

### 2.3 delegate の契約も正常

その唯一の差を疑い、`NSMenuDelegate` の各セレクターをメイン スレッドから直接呼んだ:

| 呼び出し | 結果 |
| --- | --- |
| `numberOfItemsInMenu:` | **6**(実項目数と一致) |
| `menuNeedsUpdate:` | 例外なし、呼び出し後も項目数 6 |
| `menuWillOpen:` | 例外なし、呼び出し後も項目数 6 |
| `menuDidClose:` | 例外なし |

### 2.4 実イベントによるクリックはメニューを開く

`+[NSEvent mouseEventWithType:location:modifierFlags:timestamp:windowNumber:context:eventNumber:clickCount:pressure:]` で `NSEventTypeLeftMouseDown` を作り、`-[NSApplication postEvent:atStart:]` でこのプロセス自身のイベント キューに投入した。したがって経路は実クリックと同じ(run loop → `sendEvent:` → window → hitTest → `mouseDown:`)である。

**合成 CGEvent は使っていない。Accessibility も Screen Recording も Automation も不要で、権限追加はしていない。**

結果:`NativeMenu.NeedsUpdate` → `NativeMenu.Opening` が発火。`mouseDown:` を直接呼んだ場合はネイティブのモーダル トラッキング ループに入って戻らなかった(= メニューが開いて表示されている状態)。

### 2.5 再現を試みた条件(すべてメニューが開いた)

| 条件 | 結果 |
| --- | --- |
| 素のバイナリ(端末から exec) | 開く |
| `.app` バンドル | 開く |
| メイン ウィンドウを `Hide()` した後 | 開く |
| メイン ウィンドウを `Close()` した後 | 開く |
| 強制フル GC 3 回 + ファイナライザー待機の後 | 開く |
| `-[NSApplication sendEvent:]` 経由 | 開く |
| `-[NSApplication postEvent:atStart:]` 経由 | 開く |

---

## 3. 原因として否定できたこと

- **メニューが未設定** — 否定(§2.1、メニューは付いている)。
- **`AvnTrayIcon::SetMenu` が呼ばれていない** — 否定(managed 状態が整合、かつネイティブ側にメニューが存在)。
- **項目が無効化されている** — 否定(全項目 `enabled=1`)。
- **delegate の遅延生成プロトコルが項目を消す** — 否定(§2.3)。
- **GC により managed プロキシが回収され、弱参照が切れる** — 否定(§2.5)。
- **`NativeMenu.SetMenu` 置換による上流クラッシュ([#22287](https://github.com/AvaloniaUI/Avalonia/issues/22287))** — 否定。これが起きていればアイコン生成前に例外で落ち、アイコン自体が存在しない。

**残る可能性として否定できていないもの**(いずれも未確定):

- 長時間稼働した当該個体でメイン スレッドが別のモーダル ループ(ネイティブ アラート等)に入っており、`sendEvent:` に到達しなかった。
- 当該個体に固有の、採取前に失われた状態。

---

## 4. 調査中に見つかった実欠陥(修正済み)

### D6-a 終了項目が英語リテラル

`new NativeMenuItem("Quit")` とハードコードされていた。**実機の日本語メニューバーで、他の 5 項目が日本語なのに `Quit` だけ英語で表示されていたことを実測**(§2.1 の `[5] 'Quit'`)。

製品既存の `Menu_Exit` を使うよう修正。製品の `Strings.resx` は変更していない(既に 3 言語揃っている)。修正後の実測:

| 言語 | メニューバーから読み出した 6 項目 |
| --- | --- |
| en | `BrowserFlip \| Refresh status \| Show floating toolbar \| Settings… \| - \| Exit BrowserFlip` |
| ja | `BrowserFlip \| 状態を更新 \| フローティング ツールバーを表示 \| 設定… \| - \| BrowserFlip を終了` |
| zh-CN | `BrowserFlip \| 刷新状态 \| 显示悬浮工具栏 \| 设置… \| - \| 退出 BrowserFlip` |

### D6-b アプリケーション メニューが macOS では無視されていた

`NativeMenu.SetMenu(this, [File, View, Settings])` が**メニューバーに一切反映されていなかった**。実測:メニューバーは Avalonia の既定(`About Avalonia` / `Services` / `Hide Avalonia Application` / `Quit`)のままで、`File` / `View` / `Settings` は存在しない。

> **この節の当初の原因記述は誤りだった。** 当時「`AppBuilder.SetupUnsafe()` は `WindowingSubsystemInitializer` を `Instance = _appFactory()` より前に実行するため、アプリケーション メニューの書き出し時点で `Application.Current` が null になる」と書き、**「原因(確定)」と断定して `UNSUPPORTED_UPSTREAM` と結論付けた。** Avalonia 12.1.4 の実ソースを読んだ結果、観測(反映されない)は正しいが**機構の説明が誤っており、したがって「対応不可」という結論も誤りだった。** 正しい内容は [§9](#9-mac-d9-アプリケーション-メニューの修正) に記す。この誤りは、ソースを読まずに呼び出し順を推測したために生じた。

**対応(当時):** macOS では呼ばない。あわせて `File` / `View` / `Settings` のリテラルも製品既存の `MenuBar_File` / `MenuBar_View` / `MenuBar_Settings` に置き換えた。他プラットフォームでは従来どおり。**この対応は §9 で差し替えた。**

### D6-c 受入チェック 3 が「意図」を根拠に PASS していた

チェック 3「Status-area / menu-bar icon and menu」は `trayIconCreated`(= `TrayIcon.SetIcons` を呼んだか)だけで PASS を出していた。これは BrowserFlip 自身についての主張であって macOS についての主張ではなく、**まさにこの欠陥を通してしまった**。

`IStatusAreaProbe` を追加し、macOS では実際の `NSStatusItem` を読み戻して判定するようにした(§5)。

---

## 5. 追加した自動検証

| | 内容 | 根拠 |
| --- | --- | --- |
| チェック 3(強化) | メニューバー上の実 `NSStatusBarButton` を読み戻し、表示状態・画像の有無・メニュー項目数とラベルを記録。見つからなければ **FAIL** | 公開 AppKit API のみ |
| チェック 13(新規) | 実 `NSEvent` のクリックを投入し、`NativeMenu.Opening` の発火を確認 | 権限追加なし |

チェック 13 は他のチェックの**後**に実行し、バックグラウンド スレッドが結果をレポートに追加してから `capability-report.json` と `.txt` を書き直し、`Environment.Exit` する。メニューが本当に開くと UI スレッドがネイティブのモーダル ループに入るため、UI スレッド上の継続は信頼できない。

> 当初はサマリー行の後にテキストを 1 行追記する実装にしていた。**これは誤りで、独立レビューが指摘した。** JSON レポートには入らず、`RESULT:` 行にも数えられないため、メニューが壊れていても機械可読な成果物は「全件合格」と報告してしまう — このラウンドが閉じようとした穴そのものである。現在はレポート本体に入る。

ステータス項目が見つからない場合、チェック 13 は `FAIL` ではなく `SKIPPED` を返す。macOS はメニューバーが混雑するとステータス項目を隠すため、それを製品欠陥として `FAIL` にすると正常なビルドが落ちる。見つからないこと自体はチェック 3 が `FAIL` にするので、何も隠れない。

実証(意図的に壊した版):プローブが探すツールチップだけを誤った値にして実行すると

```
 FAIL          3. Status-area / menu-bar icon and menu   no NSStatusBarButton with tooltip "NEGATIVE-TEST"; 1 status button(s) ...
 SKIPPED      13. Status-area menu opens on a real click no NSStatusBarButton with tooltip "NEGATIVE-TEST"; scanned 13 NSApp window(s)
 RESULT: FAIL (1 failing)   pass=10 pending=0 skipped=2 unsupported=0
```

となり、JSON も `[(3, 'FAIL'), (13, 'SKIPPED')]`、終了コードは 2 になる。

プローブを持たないプラットフォーム(Linux / Windows)では、チェック 3 は**その旨を明示する**:

```
PASS  3. Status-area / menu-bar icon and menu
      TrayIcon created with a 6-item NativeMenu and handed to the platform.
      This platform cannot read the item back from the shell, so visible rendering
      and click behaviour are NOT verified by this check.
```

Linux の実表示と dbusmenu 経由のメニュー操作は別途 R3 で証跡付きで確認済み。

### 回帰テスト

ステータス エリア メニューの定義を `BrowserFlip.UI.Menus.StatusAreaMenu` に抽出し、表示なしで検証できるようにした(全プラットフォームの CI で実行):

- 項目の順序とキー(5 コマンド + 区切り 1)
- 各項目を選ぶと**その処理だけ**が走ること
- 全ラベルが資源参照であること(解決失敗はキーが返るので検出できる)
- **ja / zh-CN で英語にフォールバックしないこと** — D6-a を再発させれば落ちる
- `MacStatusAreaProbe` が、該当項目が無い場合に理由付きで「無し」を返すこと

---

## 6. 未検証のまま残ること

- **Owner による実機クリックでの各メニュー項目の動作**(メイン画面表示 / 状態を更新 / フローティング ツールバー / 設定 / 終了)。メニューが開くことは自動検証したが、AppKit が項目を dispatch してから先は Owner の操作でのみ確認できる。項目のハンドラー自体は単体テスト済み。
- **MAC-D6 の原因**。再現しないため未確定。
- **アドホック署名のみ**。正式署名・公証は未実施。

---

# MAC-D7:異常終了の調査

Owner の報告:macOS のクラッシュ ダイアログ「**Avalonia Application が予期しない理由で終了しました。**」

## 7.1 証跡の保全

修正前に保全した。保存先 `~/workspace/30_test/browserflip-macos-d7/diagnostic-reports/`:

| ファイル | 起動 | クラッシュ | 間隔 |
| --- | --- | --- | --- |
| `BrowserFlipPrototype-2026-11-28-081935.ips` | 08:19:30.71 | 08:19:35 | 4.3 s |
| `BrowserFlipPrototype-2026-11-28-082003.ips` | 08:19:59.93 | 08:20:03 | 3.1 s |
| `BrowserFlipPrototype-2026-11-28-082014.ips` | 08:20:10.32 | 08:20:14 | 3.7 s |

**この機体に存在する BrowserFlipPrototype のクラッシュ レポートはこの 3 件だけ**である。`~/Library/Logs/DiagnosticReports/`、同 `Retired/`、`/Library/Logs/DiagnosticReports/` を確認し、他に該当するものは無い(`Avalonia` 名義のものも無い)。

## 7.2 内容

3 件すべて同一シグネチャ。

| 項目 | 値 |
| --- | --- |
| Exception Type | `EXC_BAD_ACCESS` (`SIGSEGV`) |
| Exception Subtype | `KERN_INVALID_ADDRESS` at `0x3d28` / `0x0` / `0x0` |
| Termination Reason | `SIGNAL 11` — `Segmentation fault: 11`、`byProc: exc handler` |
| Crashed Thread | **0**(`com.apple.main-thread`) |
| OS | macOS 27.0 (26A428) |
| 実行ファイル | レポート上は `/Users/USER/*/BrowserFlipPrototype`(Apple による自動マスク)。`bundleID` は **null** = **バンドルではなく素のバイナリ**として起動されている |

スタック(スレッド 0、上から):

```
 0 libobjc.A.dylib         objc_lookUpImpOrForward
 1 libobjcMsgSend.dylib    _objc_msgSend_uncached
 2 .. 21                   (JIT されたマネージド フレーム:シンボル無し)
22 libAvaloniaNative.dylib __26-[Signaler createObserver]_block_invoke
23 CoreFoundation          __CFRUNLOOP_IS_CALLING_OUT_TO_AN_OBSERVER_CALLBACK_FUNCTION__
25 CoreFoundation          __CFRunLoopRun
34 AppKit                  -[NSApplication run]
35 libAvaloniaNative.dylib PlatformThreadingInterface::RunLoop(IAvnLoopCancellation*)
```

読み方:**Avalonia のディスパッチャー(CFRunLoop オブザーバー)から呼ばれたマネージド コードが、無効な Objective-C オブジェクトに `objc_msgSend` した。** 障害アドレスが `0x3d28` や `0x0` という極小値なのは、isa ポインターがオブジェクトでないものを指していたことを意味する。

全 28 スレッドを確認したが、**シャットダウン中のスレッドは無く、ファイナライザー スレッドは待機中**だった。したがって `Environment.Exit` との競合ではない。**メニュー トラッキングのフレームも無い**ため、メニュー表示中でもない(= MAC-D6 とは別事象)。

## 7.3 原因:私が書いた一時調査コード

08:19〜08:20 に私が実行していたのは、**推測したメモリ オフセットから生ポインターを読む調査コード**である。ネイティブの `AvnTrayIcon` が保持する `NSStatusItem` を得ようとして、オブジェクト先頭から 16〜24 スロットを `Marshal.ReadIntPtr` で読み、各候補に `isKindOfClass:` を送っていた。**オブジェクトでない値に `objc_msgSend` を送る**という、上のスタックそのものである。

時刻の対応:

| クラッシュ | 対応する私の実行 |
| --- | --- |
| 08:19:30 → 08:19:35 | スロット ダンプ版の初回実行(**出力が一切出なかった**回) |
| 08:19:59 → 08:20:03 | 同版の再実行 |
| 08:20:10 → 08:20:14 | `p5` として記録した実行。**終了コード 139(SIGSEGV)** をその場で記録済み |

この手法は危険だと判断してその場で放棄し、公開ランタイム API(`class_copyIvarList` / `object_getIvar`)に切り替えた。**当該コードはコミットされていない**(リポジトリ履歴に存在しない)。

## 7.4 修正後の再発確認

最終 Self-contained `.app` で:

| 実行 | 回数 | 結果 |
| --- | --- | --- |
| `--selftest`(環境変数を `env -i` で除去) | 5 | 全て exit=0 |
| 通常起動 → SIGTERM で終了 | 3 | 異常なし |
| LaunchServices 起動(Finder ダブルクリック相当) | 1 | 正常 |
| `/Applications` から LaunchServices 起動 | 1 | 正常 |

**クラッシュ レポート件数:実行前 3 件 → 実行後 3 件(増加なし)。**

## 7.5 正直に言うべき限界

- この機体のクラッシュ レポート 3 件は、**時刻・シグネチャ・終了コードの一致により私の一時調査コードに帰属する**。
- **Owner が見たダイアログがこの 3 件のいずれかであれば、原因は上記であり、製品の欠陥ではない。** ダイアログ名が「Avalonia Application」だったことは、バンドルではない素のバイナリであったこと(`bundleID = null`)とも整合する。
- **もし Owner のクラッシュがこの 3 件以外であれば、対応するレポートが存在しないため解析できない。** その場合は再現時のレポートが必要である。推測で原因を確定しない。
- あわせて製品名の申告を修正した(§8.3)。これにより今後のクラッシュ ダイアログは「Avalonia Application」ではなく **BrowserFlip** と表示される。

---

# MAC-D8:直接起動時の .NET Runtime 検出失敗

Owner の報告どおり、`prototypes/avalonia/src/BrowserFlip.Prototype.Desktop/bin/Release/net10.0/BrowserFlipPrototype` は .NET を見つけられない。**私もこのセッションの冒頭で同じエラーを踏んでいる。**

## 8.1 事実(新規インストールは一切していない)

```
which -a dotnet                      /Users/sxrsing/.dotnet/dotnet   ← ユーザー ローカル 1 つだけ
/usr/local/share/dotnet              absent
/usr/share/dotnet                    absent
/etc/dotnet/install_location         absent
/etc/dotnet/install_location_arm64   absent
dotnet --list-sdks                   10.0.404 [/Users/sxrsing/.dotnet/sdk]
dotnet --list-runtimes               Microsoft.NETCore.App 10.0.15 [/Users/sxrsing/.dotnet/shared/...]
Host                                 10.0.15 / arm64
```

全文は `~/workspace/30_test/browserflip-macos-d7/dotnet-environment.txt`。

## 8.2 原因(確定)

`bin/Release/net10.0/` の実行ファイルは **framework-dependent apphost** である:

```json
"framework": { "name": "Microsoft.NETCore.App", "version": "10.0.0" }
```

apphost はランタイムを「実行ファイルと同じディレクトリ → `DOTNET_ROOT_ARM64` → `DOTNET_ROOT` → `/etc/dotnet/install_location_arm64` → `/usr/local/share/dotnet`」の順に探す。**この機体ではその全てが存在しない**(SDK は `~/.dotnet` にユーザー ローカル導入されている)ため「`.NET location: Not found`」になる。`scripts/dev.sh` が `DOTNET_ROOT` を自分で解決しているので、ビルドとテストは成功する。

**これは製品の欠陥ではなく、開発用出力を直接起動したことによるもの**である。環境変数の追加設定、グローバル設定の変更、sudo、重複インストールはいずれも**行っていない**。

## 8.3 正式配布用成果物(Self-contained)

`macos/bundle.sh` は以前から `-r osx-arm64 --self-contained true` で publish している。検証結果:

| 項目 | 実測 |
| --- | --- |
| `runtimeconfig.json` | `"includedFrameworks": [{ "name": "Microsoft.NETCore.App", "version": "10.0.15" }]` |
| ランタイム同梱 | `libcoreclr.dylib`、`System.Private.CoreLib.dll` あり(`Contents/MacOS` に 233 ファイル、計 113 MB) |
| `DOTNET_ROOT` 依存 | **なし。** `env -i HOME=… PATH=/usr/bin:/bin` で起動して成功 |
| `CFBundleName` / `CFBundleDisplayName` | `BrowserFlip Prototype` |
| `CFBundleIdentifier` | `co.jp.tlzs.browserflip.prototype` |
| `CFBundleExecutable` / `CFBundleIconFile` | `BrowserFlipPrototype` / `BrowserFlip.icns` |
| アプリ自身の申告名 | **`BrowserFlip`**(修正前は Avalonia 既定の `Avalonia Application`) |

**開発用 `bin/Release` の直接起動は、動作保証の対象ではない。** 配布・受入はすべて `.app` で行う。

> アプリケーション メニュー(画面左上)は Avalonia の既定のままで、`About Avalonia` などが出る。これは §4 D6-b の上流制限によるもので、差し替えられない。製品名は自己申告名・バンドル名・メニューバー アイコンのメニューで `BrowserFlip` と表示される。

---

# 9. MAC-D9 アプリケーション メニューの修正

Owner の再確認による指摘(3 件):

- `About Avalonia` が表示される
- 設定メニューが存在しない
- 日本語選択時にも英語メニューが表示される

いずれも**本ラウンドで修正した。** R5 に延期しない。

## 9.1 §4 D6-b の結論が誤りだった理由(実ソースで確認)

Avalonia 12.1.4 のタグ付きソース(`AvaloniaUI/Avalonia` の `12.1.4`)を取得して確認した。

`src/Avalonia.Controls/AppBuilder.cs` の `SetupUnsafe()`:

```csharp
WindowingSubsystemInitializer?.Invoke();      // ← ここで macOS バックエンドを初期化
AfterPlatformServicesSetupCallback?.Invoke(Self);
Instance = _appFactory!();                    // ← Application 生成
Instance.ApplicationLifetime = _lifetime;
Instance.RegisterServices();
Instance.Initialize();                        // ← App.axaml / App.Initialize()
AfterApplicationSetupCallback?.Invoke(Self);
AfterSetupCallback?.Invoke(Self);             // ← ここでメニューを書き出す
Instance.OnFrameworkInitializationCompleted();// ← 従来の実装はここで SetMenu していた
```

`src/Avalonia.Native/AvaloniaNativePlatformExtensions.cs`:

```csharp
.UseWindowingSubsystem(() =>
{
    var platform = AvaloniaNativePlatform.Initialize(...);
    builder.AfterSetup(x =>            // ← 即時実行ではなく AfterSetup に登録している
    {
        platform.SetupApplicationName();
        platform.SetupApplicationMenuExporter();
        platform.SetupApplicationDockMenuExporter();
    });
});
```

**つまりメニューの書き出しは windowing 初期化中ではなく `AfterSetup` で行われる。** その時点で `Application.Current` は生成済みであり、`SetupApplicationName()` が `Application.Current!.Name` を `!` 付きで参照していること自体がその証拠である。

従って真の原因は「`Application.Current` が null」ではなく、**`SetMenu` の呼び出し位置が 1 段遅かった**ことである。`AvaloniaNativeMenuExporter.DoLayoutReset`(`MenuTarget.Application`)は

```csharp
var app = Application.Current;
var appMenu = app is null ? null : NativeMenu.GetMenu(app);
if (appMenu == null) { appMenu = CreateDefaultAppMenu(); /* "About Avalonia" */ ... }
SetMenu(appMenu);
```

を **1 回だけ**実行し、以後 `_nativeMenu` が非 null になるため `_factory.SetAppMenu` は再呼び出しされない。`OnFrameworkInitializationCompleted` での `SetMenu` は `AfterSetup` より後なので、既定メニューが固定された後に到着して無視されていた。

**修正:`App.Initialize()` で `NativeMenu.SetMenu(this, …)` する。** これは Avalonia の公式な方法であり、非公開 API もポインター操作も使わない。

## 9.2 標準項目は Avalonia に作らせて、ラベルだけ差し替える

`AvaloniaNativeMenuExporter.SetMenu` は、書き出すメニューに対して `PopulateStandardOSXMenuItems(menu)` を呼び、**Services / Hide / Hide Others / Show All / Quit を追記する**(`MacOSPlatformOptions.DisableDefaultApplicationMenuItems` が false のとき)。追記先は BrowserFlip が渡したメニュー自身である。

これらを自前で実装する選択肢は**採らなかった**。理由:

| 必要なもの | 可視性 | 結果 |
| --- | --- | --- |
| Hide / Hide Others / Show All | `INativeApplicationCommands` は **internal** | managed からは呼べない |
| Services サブメニュー | `MacOSNativeMenuCommands.IsServicesSubmenuProperty` は **internal** | 設定できない。空の「サービス」項目は偽装になる |

自前実装は AppKit の `[NSApp hide:]` 等を直接叩くことになり、`NSApp.servicesMenu` を正しく設定するにはネイティブ メニュー ポインターに触る必要がある。Owner の制約(危険なネイティブ ポインター操作を行わない)に反する。

採った方法:**Avalonia に項目の生成・配線・ショートカット設定をさせ、BrowserFlip は `NativeMenuItem.Header` を書き換えるだけにする。** `IAvnMenuItem.cs` は `NativeMenuItem.HeaderProperty` を購読して `SetTitle` をネイティブへ伝えるため、項目を**差し替えずに**ラベルだけ更新できる。差し替えは上流の [#22287](https://github.com/AvaloniaUI/Avalonia/issues/22287) のクラッシュ経路であり、通らない。

項目の同定は**英語ラベルの一致**で行う(位置ではない)。上流が順序を変えた場合に位置照合では別の項目を誤って書き換えるが、ラベル照合なら**その項目だけが英語のまま残り、チェック 14 が FAIL する**。

### 併せて修正した上流の不具合

Avalonia は Hide Others に `⌥⌘Q` を割り当てている(`AvaloniaNativeMenuExporter.cs` の `new KeyGesture(Key.Q, Meta | Alt)`)。macOS の標準は `⌥⌘H` であり、`⌥⌘Q` は Quit の `⌘Q` と 1 打鍵差である。`Adopt` で `⌥⌘H` に直した。

## 9.3 実機で読み出した結果

`[NSApp mainMenu]` の index 0 のサブメニューを公開 AppKit API だけで読み出した(`-[NSMenu itemAtIndex:]` / `-[NSMenuItem title]` / `-[NSMenuItem keyEquivalent]` / `-[NSMenuItem keyEquivalentModifierMask]` / `-[NSMenuItem submenu]`)。

| 言語 | メニューバーから読み出した 10 要素 |
| --- | --- |
| ja | `BrowserFlip について \| 設定… ⌘, \| - \| サービス > \| - \| BrowserFlip を隠す ⌘H \| ほかを隠す ⌥⌘H \| すべてを表示 \| - \| BrowserFlip を終了 ⌘Q` |
| en | `About BrowserFlip \| Settings… ⌘, \| - \| Services > \| - \| Hide BrowserFlip ⌘H \| Hide Others ⌥⌘H \| Show All \| - \| Quit BrowserFlip ⌘Q` |
| zh-CN | `关于 BrowserFlip \| 设置… ⌘, \| - \| 服务 > \| - \| 隐藏 BrowserFlip ⌘H \| 隐藏其他 ⌥⌘H \| 全部显示 \| - \| 退出 BrowserFlip ⌘Q` |

Owner が指定した 7 項目と順序が一致している。`サービス >` の `>` は実サブメニューが付いていることを示す(Avalonia が `NSApplication.servicesMenu` に登録したもの)。

**言語切り替えは起動時だけでなく実行中も反映される。** 同一プロセス内で `Language` を en → ja → zh-CN と切り替え、各段でメニューバーを読み直した結果が上表である。これは Owner の指摘 3 件目が「切り替え時の欠陥」であるため、起動時の確認だけでは不十分だと判断したことによる。

**ネイティブ項目が BrowserFlip に配線されていることの確認:** `-[NSMenu performActionForItemAtIndex:]`(AppKit 自身が選択を配送する経路)で「BrowserFlip について」を選択し、**BrowserFlip 自身の About ウィンドウが開いたこと**を確認した。ラベルが正しいことと、項目が動くことは別の主張である。

### 英語の終了ラベルについて

製品既存の `Menu_Exit` は英語で `Exit BrowserFlip` である(Windows の語彙)。macOS の標準は `Quit` なので、プロトタイプ側の `Menu_QuitApp` を追加した。日本語・中国語の文言は `Menu_Exit` と同一で、英語のみ異なる。**製品の `Strings.resx` は変更していない。**

## 9.4 自動検証(チェック 14)

| | 内容 | 根拠 |
| --- | --- | --- |
| チェック 14(新規) | `[NSApp mainMenu]` を読み出し、全項目のタイトル・ショートカット・サブメニュー有無を記録。期待ラベルの欠落、英語のまま残った項目、`Avalonia` を名乗る項目、`設定…` の `⌘,` 欠落、About 項目の未配線を **FAIL** | 公開 AppKit API のみ |

macOS 以外では `UNSUPPORTED`(アプリケーション レベルのメニュー バーは macOS だけのもの)。

**意図的に壊して確認した(Adopt の呼び出しを削除し、日本語 UI で実行):**

```
FAIL 14. Application menu (macOS menu bar)
  ... | Services > | - | Hide BrowserFlip [⌘H] | Hide Others [⌥⌘Q] | Show All | - | Quit [⌘Q];
  UI culture=ja; MISSING: サービス | BrowserFlip を隠す | ほかを隠す | すべてを表示 | BrowserFlip を終了;
  NOT TRANSLATED: Services | Hide BrowserFlip | Hide Others | Show All | Quit
RESULT: FAIL (1 failing)   pass=12 pending=0 skipped=1 unsupported=0
```

`capability-report.json` も `(14, 'FAIL')`、`ExitCode=2` になる。復元後は `(14, 'PASS')`、`ExitCode=0`。**レポート本体と終了コードの両方が動くことを確認済み**(前ラウンドでチェック 13 が JSON に入っていなかった誤りを繰り返さないため)。

### 回帰テスト

| ファイル | 内容 |
| --- | --- |
| `ApplicationMenuAttachTests` | **`Initialize()` だけで** メニューが `Application` に付いていること。macOS 以外では付かないこと。`Name` が `BrowserFlip` であること |
| `MacApplicationMenuTests` | 項目構成・順序・`⌘,`・ハンドラー・3 言語のラベル・英語残留なし・アクセス キー記号なし・Avalonia 追記項目の引き取り・`⌥⌘H` 補正・上流改名時に誤ラベルしないこと・ラベル再読込で区切りとハンドラーが壊れないこと |
| `AboutWindowTests` | 製品名とバージョン表示、`Avalonia` を名乗らないこと、他ウィンドウでの言語変更に追従すること、閉じたら購読を外すこと、閉じるボタンが実クリックで動くこと |

**`ApplicationMenuAttachTests` は意図的に壊して確認した。** 前ラウンドと同じ誤り(`OnFrameworkInitializationCompleted` へ移動)を入れると

```
BrowserFlip.Prototype.Tests.ApplicationMenuAttachTests.Initialize_alone_attaches_the_application_menu_on_macos [FAIL]
失敗!  -失敗: 1、合格: 1
```

## 9.5 未検証のまま残ること

- **Owner 自身の目視**。本節の表は BrowserFlip がプロセス内から読み出した値であり、画面写真ではない
- 各項目の実動作のうち、`サービス` / `BrowserFlip を隠す` / `ほかを隠す` / `すべてを表示` は **Avalonia の実装をそのまま使っている**。ラベルとショートカットは読み出して確認したが、動作そのものは Owner の操作で確認されたい
- x86-64 実機(Rosetta 未導入のため、ABI 分岐は論証のみ)
