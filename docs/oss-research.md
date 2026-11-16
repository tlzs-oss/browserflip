# BrowserFlip OSS 調査レポート

## 1. 調査方法と日付

- 調査日: 2026-11-16（JST）
- 方法（すべて読み取りのみ。リポジトリ作成・Issue・Star・コメントは一切行っていない）
  - `gh api repos/<owner>/<repo>`: 実在確認、SPDX ライセンス、`pushed_at`、Star 数、archived を取得
  - `gh search repos` / `gh search code`（code search はレート上限 10/分に当たったため途中から `contents` API でソースを直接取得）
  - `gh api repos/<owner>/<repo>/contents/<path>`: 該当ソースを取得して grep し、既定ブラウザの変更方式を確認
  - Chromium は GitHub ミラー `chromium/chromium`、Firefox は `mozilla-firefox/firefox`（main）から取得
  - WebFetch / WebSearch: Microsoft Learn、kolbi.cz、firefox-source-docs、報道記事
- 表記ルール: API やページから取れた値だけを書く。確認できなかったものは **未確認** と書く。「最終更新」は GitHub API の `pushed_at`（UTC）。

## 2. 比較表

| name | URL | License (SPDX) | 最終更新 (pushed_at) | Win11 | 変更方式 | 品質 | 既知の不具合 | セキュリティリスク | 再利用可否 |
|---|---|---|---|---|---|---|---|---|---|
| BrowserPicker | https://github.com/mortenn/BrowserPicker | MIT | 2026-11-03 | 対応 | 自分をブラウザ（URL ハンドラ）として登録し、ユーザーが Settings で既定に選ぶ。UserChoice は診断のため読むだけ | 高（WPF、519★、v3.2.5 2026-10-24） | Issue「BrowserPicker is bypassed for HTTP/HTTPS links on Windows 11 Build 26200」 | 低（UserChoice には書き込まない） | アイデアのみ参考にする（MIT なのでコード流用も可能だが方針上しない） |
| Hurl | https://github.com/U-C-S/Hurl | MIT | 2026-10-29 | 対応（README に Win11 の手順あり） | ブラウザ選択プロキシ。`ms-settings:defaultapps` を開くだけ（パラメータなし） | 高（WinUI 3 + Rust ランチャー、474★、v0.10.0） | Dia 関連の Issue など（既定ブラウザ設定とは無関係） | 低 | アイデアのみ参考（MIT） |
| BrowserMux (alxbd) | https://github.com/alxbd/browsermux | MIT | 2026-08-02 | 対応（README: Win10 1809+ / Win11） | ブラウザ選択プロキシ。インストーラが HKCU/HKLM の `Capabilities`・`RegisteredApplications` を登録し、`ms-settings:defaultapps` を開く | 中（0★、WinUI 3 / .NET 9） | 未確認 | 低 | アイデアのみ参考（MIT） |
| DefaultSetter (Nuliax7) | https://github.com/Nuliax7/DefaultSetter | MIT | 2022-07-01 | README は「Windows 10/11」と記載 | **UserChoice ハッシュを偽装し、直接書き込む**（`GenerateHash` で MD5 ベースの独自ハッシュを計算） | 低（0★、単一ファイル） | 2024-02 以降は UCPD に阻まれる見込み（推定。動作は未確認） | **高（保護機構の回避）** | **禁止** |
| set-default-browser (sampalmer) | https://github.com/sampalmer/set-default-browser | Unlicense | 2026-03-18（archived=true） | 非対応（README:「1803 and higher are not supported」） | 旧コントロールパネルアプレットを UI Automation で操作 | 低（アーカイブ済み） | Win10 1803 以降は動かない | 中（UI の代理操作） | 不可（方式が古く、使えない） |
| SetDefaultBrowserUI (legach) | https://github.com/legach/SetDefaultBrowserUI | MIT | 2023-09-13 | 未確認 | Kolbicz 製のクローズドソース `SetDefaultBrowser.exe` を呼び出すラッパー | 低（1★） | 未確認 | 高（UserChoice ハッシュを偽装するバイナリに依存） | 不可 |
| SetDefaultBrowser (Christoph Kolbicz) | https://kolbi.cz/blog/2017/11/10/setdefaultbrowser-set-the-default-browser-per-user-on-windows-10-and-server-2016-build-1607/ | クローズドソース（EULA あり、商用は要見積り） | v1.5 2020-02-20。2024-03-05 にメンテ終了、SetUserFTA 2.0 へ統合と告知 | 未記載 | UserChoice を書き込み、「Microsoft custom hash」を自前で計算（作者が明言） | 不明（ソースなし） | 2024-02 の UCPD 導入で動かなくなった（報道） | **高** | **禁止** |
| SetUserFTA (Christoph Kolbicz) | kolbi.cz（個別ページは未取得） | クローズドソース | 未確認 | 未確認 | UserChoice ハッシュを偽装（SetDefaultBrowser の後継） | 不明 | UCPD で阻まれた（報道） | **高** | **禁止** |
| PS-SFTA | https://github.com/DanysysTeam/PS-SFTA | API では none（LICENSE ファイルなし。README とスクリプトヘッダには「MIT License」と記載） | 2022-10-10 | README は「Windows 10/11」と記載 | **UserChoice ハッシュを偽装**（PureBasic 版ハッシュアルゴリズムの移植） | 中（390★） | UCPD 導入後は http/https に効かない見込み（未検証） | **高** | **禁止** |
| FTA-Manager (feiglein74) | https://github.com/feiglein74/FTA-Manager | MIT | 2026-10-10 | 未確認 | UserChoice ハッシュ計算（`UserChoiceHash.cs`）、regini、ACL 操作（`remove-deny-acl.ps1`） | 低（3★） | 未確認 | **高（ACL 改変を伴う）** | **禁止** |
| set-fileassoc | https://github.com/default-username-was-already-taken/set-fileassoc | Unlicense | 2020-09-26 | 未確認 | 説明文:「bypassing the built-in prot(ection)」 | 低 | 未確認 | **高** | **禁止** |
| Junction | https://github.com/sonnyp/Junction | GPL-3.0 | 2026-09-25 | 非対象（Linux/GNOME） | アプリ選択プロキシ | 高（634★） | — | — | 不可（GPL かつ Windows 非対象） |
| BrowserSelect | https://github.com/zumoshi/BrowserSelect | GPL-2.0 | 2024-02-03 | 未確認 | ブラウザ選択プロキシ（BrowserMux README での言及） | 未確認 | 未確認 | 未確認 | 不可（GPL。コードは使わない） |
| Chromium | https://github.com/chromium/chromium | BSD-3-Clause | 2026-11-16 | 対応 | **Win11:** `ms-settings:defaultapps?registeredAppUser=` / `registeredAppMachine=` を ShellExecute で開く。**Win10:** `IApplicationActivationManager::ActivateApplication` で `page=SettingsPageAppsDefaults` を開く | 高 | — | 低（公式の手段のみ） | 設計の参考として最適。BSD のため流用は可能だが方針上は自前実装 |
| Firefox | https://github.com/mozilla-firefox/firefox | MPL-2.0（API 上は NOASSERTION） | 2026-11-16 | 対応 | ① UserChoice ハッシュを計算して書き込む経路（`WindowsUserChoice.cpp` / `SetDefaultBrowser.cpp`）② Settings を開く経路（`ms-settings:defaultapps?registeredAppUser/Machine/registeredAUMID`）③ `LaunchAdvancedAssociationUI` へのフォールバック | 高 | firefox-source-docs:「UserChoice 方式は最新の Windows 11 ではもう現実的でない」 | ①は**禁止**、②は参考にしてよい | ②の考え方だけ参考にする。MPL-2.0 のファイル単位コピーレフトがあり、①のハッシュコードは絶対に流用しない |

注: GitHub 上で「BrowserMux」という名前のリポジトリは複数見つかった（artrixdotdev/browsermux 0★ MIT、lloydeverett/browsermux 0★ ライセンスなし、DTRHnet/BMUX はブラウザ拡張）。Windows のブラウザセレクタに当たるのは alxbd/browsermux。`kolbasa/set-default-browser` と `jwbuiter/set-default-browser` は 404 で、存在しなかった。

## 3. 各プロジェクト詳細

### 3.1 BrowserPicker (mortenn/BrowserPicker)
- 概要: 「Smart browser selector for Windows」。WPF（`BrowserPicker.UI.csproj` に `<UseWPF>true</UseWPF>`）。README によると .NET 10 Desktop Runtime 版と、ランタイム同梱の Portable 版を配布。519★、未解決 Issue と PR は合計 32 件。最新リリースは v3.2.5（2026-10-24）。
- 方式: 自分を既定ブラウザとして登録し、ユーザーに「Settings → Apps → Default apps → Web browser を Browser Picker に変更」してもらう（README 41 行目）。`FeedbackViewModel.cs` は `UrlAssociations\http(s)\UserChoice` の `ProgId`/`Hash` を**読み取って診断に使うだけ**で、書き込みはしない。
- 品質: 構成がしっかりしており（Common / UI / Windows 層の分割、tests、CSharpier、XamlStyler）、BrowserFlip と同じ WPF/.NET 系統。
- 既知の不具合: 「BrowserPicker is bypassed for HTTP/HTTPS links on Windows 11 Build 26200」が未解決（Win11 の最新ビルドでの挙動差を示す例）。
- 再利用: MIT のため帰属表示をすれば流用は可能。ただし方針どおり「ブラウザ検出」「設定画面への誘導」のアイデアだけを参考にする。

### 3.2 Hurl (U-C-S/Hurl)
- WinUI 3（Windows App SDK）+ Rust 製ランチャー、Inno Setup。474★、v0.10.0（2026-09-26）。
- 方式: `SettingsPage.xaml.cs` で `Launcher.LaunchUriAsync(new Uri("ms-settings:defaultapps"))` を呼ぶだけ。アプリ単位のパラメータは使っていない。README の案内は「Settings > Apps > Default apps > Hurl > Set as default browser」。
- リスクは低い。BrowserFlip とは目的が違う（Hurl はリンクを開くたびにブラウザを選ぶもの。BrowserFlip は OS の既定を切り替えるもの）。

### 3.3 BrowserMux (alxbd/browsermux)
- WinUI 3 / .NET 9。URL を受ける `BrowserMux.Handler` は AOT ビルドで、名前付きパイプ経由でメインアプリに渡す。0★、2026-08 以降の更新なし。
- `RegistrySetup.cs` は ProgId、`Software\Clients\StartMenuInternet\<App>\Capabilities`、`Software\RegisteredApplications` を確認し、UserChoice は読むだけ。設定画面からは `Process.Start("ms-settings:defaultapps")` を呼ぶ。
- 参考になる点: 「`RegisteredApplications` に登録済みかどうか」の判定は、BrowserFlip で `registeredAppUser` と `registeredAppMachine` のどちらを使うか決める処理と同じ考え方。

### 3.4 DefaultSetter (Nuliax7/DefaultSetter)
- README の機能一覧:「Generate User Choice Hash」「Set File Type Association」「Set Protocol Association」。`DefaultSetter.cs` は UserChoice キーを削除して作り直し、`Hash` と `ProgId` を書き込む。ハッシュは MD5 と定数（`0x1EC90001L` など）を使う独自アルゴリズムの再実装。
- **保護機構の回避に当たるため、BrowserFlip ではコードもアルゴリズムも一切参照しない。**

### 3.5 sampalmer/set-default-browser
- アーカイブ済み、Unlicense。README:「Windows 10 version 1709 or below. Versions 1803 and higher are not supported since this app uses UI automation for a control panel applet that is now removed from Windows.」。代替として kolbi.cz を案内している。

### 3.6 SetDefaultBrowser / SetUserFTA（Christoph Kolbicz、クローズドソース）
- kolbi.cz の該当ページによると、レジストリキーは「a Microsoft custom hash function」で保護されており、作者はハッシュを計算して書き込むが計算方法は公開しない（「since its not a public code, i will not share more information about it」）。v1.5 は 2020-02-20。2024-03-05 付けで「SetDefaultBrowser will not be maintained anymore and its functionality integrated into SetUserFTA 2.0」。商用利用は見積りが必要。
- 報道（Neowin、Techzine、gHacks など）によると、2024-02 の更新（Win10 KB5034763 / Win11 KB5034765）で入った UCPD.sys により SetUserFTA と SetDefaultBrowser が動かなくなり、Kolbicz 本人が最初に気付いた。
- legach/SetDefaultBrowserUI（MIT）はこの exe を `./Resources/SetDefaultBrowser.exe` として呼ぶ WPF ラッパーなので、同じく使えない。

### 3.7 PS-SFTA (DanysysTeam/PS-SFTA)
- `SFTA.ps1` v1.2.0。ヘッダに「LMongrain - Hash Algorithm PureBasic Version」とある。`Set-FTA` / `Set-PTA` が UserChoice を削除してハッシュ付きで再作成するほか、`ApplicationAssociationToasts` も書き換える。GitHub API ではライセンス未検出（LICENSE ファイルなし）だが、README とヘッダには MIT と記載。
- ハッシュ偽装に当たるため **禁止**。

### 3.8 Chromium（BSD-3-Clause）
- `base/win/default_apps_util.cc`（パス上の最新コミット e3daa5b4de73、2026-05-26）
  - `LaunchSettingsDefaultApps(app_name, is_per_user_install)`: `ms-settings:defaultapps?` に `registeredAppUser=`（per-user インストール）または `registeredAppMachine=`（per-machine）を付け、アプリ名を URL エスケープして（コメント:「'+' も escape が必要」）`ShellExecute` で開く。
  - `LaunchDefaultAppsSettingsModernDialog(protocol)`: `IApplicationActivationManager::ActivateApplication("windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel", "page=SettingsPageAppsDefaults")` で開く。コメント:「Scrolling to a specific protocol is only possible on Windows 10」（Win10 では `&target=` を付けて特定項目までスクロールする）。
- `chrome/installer/util/shell_util.cc`（553feff79d2c、2026-11-09）の `ShowMakeChromeDefaultSystemUI`: Win11 以上なら `LaunchSettingsDefaultApps`、Win10 以下か失敗した場合は `LaunchDefaultAppsSettingsModernDialog(L"http")`。UserChoice の書き込みやハッシュ計算はしていない。
- `chrome/browser/shell_integration_win.cc`（5f7162f9e4a5）: `GetDefaultBrowserSetPermission` は `SET_DEFAULT_INTERACTIVE`（ユーザー操作が必要）を返す。
- BrowserFlip の手本に最も近い。BSD-3 なので帰属表示をすれば流用もできるが、方針上は自前で実装する。

### 3.9 Firefox（MPL-2.0）
- `browser/components/shell/WindowsDefaultBrowser.cpp`（ccee8f28885b、2026-10-27）の `LaunchModernSettingsDialogDefaultApps()`:
  - Win11 以上なら `LaunchMsSettingsProtocol()`。パッケージ版（MSIX）は `ms-settings:defaultapps?registeredAUMID=%s!App`（PackageFamilyName!App）、そうでなければ `HKCU\Software\RegisteredApplications` に登録があるか（または HKLM に登録がないか）で `registeredAppUser=%s` と `registeredAppMachine=%s` を使い分け、`ShellExecuteExW` で開く。
  - Win10 は `ActivateApplication(... "page=SettingsPageAppsDefaults&target=SystemSettings_DefaultApps_Browser")`。build 14965 未満向けの回避コードもある。
- `nsWindowsShellService.cpp`（7d43e90e8a47、2026-11-03）: `SetDefaultBrowser()` はヘルパー（`/SetAsDefaultAppUser`）を実行してから Settings を開く。失敗した場合は `IApplicationAssociationRegistrationUI::LaunchAdvancedAssociationUI` にフォールバックする。また `IsUserChoiceProtectionDriverRunning`（UCPD サービスの有無）を確認している。
- `toolkit/mozapps/defaultagent/SetDefaultBrowser.cpp` と `browser/components/shell/WindowsUserChoice.cpp`（3d38dd2ac6a0、2026-10-26）: `GenerateUserChoiceHash(ext, sid, progId, timestamp)` でハッシュを計算し、UserChoice の `ProgID`/`Hash` を書き込む（`SetUserChoiceRegistry`）。**これはハッシュ偽装に当たり、MPL のコードでもあるため、絶対に参照・流用しない。**
- firefox-source-docs（Set Default）:「UCPD prevents modification of select UserChoice registry keys」（http(s) と .pdf がブロック対象）、UserChoice 方式は「no longer viable to use this in modern releases of Windows 11」。

## 4. Windows 公式の設定遷移手段（出典付き）

| 手段 | 内容 | 対応 OS | 出典 |
|---|---|---|---|
| `ms-settings:defaultapps` | 「既定のアプリ」ページを開く | Win10 / Win11 | MS Learn「Launch Windows Settings」 |
| `ms-settings:defaultapps?registeredAppUser=<URI エスケープ名>` | `HKCU\Software\RegisteredApplications` の値名。per-user インストールのアプリのページを直接開く | Windows 11 21H2（2023-04 累積更新）、22H2（2023-04 累積更新）、23H2 以降 | MS Learn「Launch the Default Apps settings page」 |
| `ms-settings:defaultapps?registeredAppMachine=<URI エスケープ名>` | `HKLM\Software\RegisteredApplications` の値名。per-machine インストール用（公式サンプルは `"Microsoft Edge"`） | 同上 | 同上 |
| `ms-settings:defaultapps?registeredAUMID=<URI エスケープ AUMID>` | パッケージアプリ（`uap:FileTypeAssociation` / `uap:Protocol` を宣言したもの）用 | 同上 | 同上 |
| `ms-settings:defaultbrowsersettings` | 「Deprecated in Windows 11」 | — | MS Learn「Launch Windows Settings」 |
| `IApplicationActivationManager::ActivateApplication(immersivecontrolpanel AUMID, "page=SettingsPageAppsDefaults[&target=...]")` | 非公開の挙動を観察して使っている手段（Chromium/Firefox が Win10 向けに使用） | Win10 | Chromium `default_apps_util.cc`、Firefox `WindowsDefaultBrowser.cpp`（MS の公式ドキュメントは未確認） |
| `IApplicationAssociationRegistrationUI::LaunchAdvancedAssociationUI` | Remarks:「Starting in Windows 10, this does not launch the association dialog box. It displays a dialog to the user informing them that they can change the default programs ... in their Settings」 | Vista 以降（Win10 以降は案内ダイアログのみ） | MS Learn API ページ |

**パラメータ名の注意**: 依頼文の `registeredAUMIDApp` は公式のパラメータ名ではない。公式名は **`registeredAUMID`**（MS Learn）で、Firefox も `registeredAUMID=%s!App` と書いている（`!App` は AUMID の一部）。

**最小ビルド**: MS Learn の記載は「Windows 11, version 21H2 (with 2023-04 Cumulative Update), 22H2 (with 2023-04 Cumulative Update), and 23H2 or later」。同じページの registeredAUMID の注記に `MaxVersionTested` の値として `10.0.22000.1817`（21H2）と `10.0.22621.1555`（22H2）が出ており、これが 2023-04 累積更新後のビルドに当たると読める（KB 番号との対応は未確認）。**Windows 10 については、アプリ単位パラメータのサポート記載はない**（Chromium も Win11 以上でしか使っていない）。依頼文にある「KB5019311 世代で導入」は確認できなかった（**未確認**）。

**MS の方針（公式）**: MS Learn「Windows app defaults platform」（ms.date 2026-09-26）に以下の記載がある。
- 「Windows does not allow programmatic changes to default apps without user interaction in system UI.」
- 「User setting data for app defaults is obfuscated in registry data stores. Registry-based changes are not supported for apps.」
- 「protected by a Windows filter driver (UCPD.sys) that blocks apps from writing app defaults data.」
- ベストプラクティスとして「Use the `ms-settings:defaultapps` URI to launch the Default Apps settings page or your app's page」「Respect User Choice: Avoid aggressive prompts」を挙げ、Store ポリシー 10.2.8 にも言及している。

**2023 年の MS 声明**: Windows Experience Blog「A principled approach to app pinning and app defaults in Windows」（2023-03-17）。報道によれば、Settings への deep link URI を自社アプリと他社アプリに同等に提供すると表明している。本文は直接取得できておらず、正確な URI の記載は**未確認**。
**「Set default」ボタン（ブラウザのワンクリック設定）**: Win11 の Settings ではブラウザのアプリ画面に「Set default」ボタンがある（firefox-source-docs:「Windows 11 offers a single "Set default" button」）。報道によると、EEA では 2025-06 以降（Beta Build 26120.4151 から）このボタンが http/https/.htm/.html に加えて ftp・read・.mht・.svg・.xml なども設定し、タスクバーにもピン留めするよう拡張された。23H2 での導入時期や MS 一次資料は**未確認**。

## 5. 禁止手法とその理由

| 手法 | 例 | 禁止理由 |
|---|---|---|
| UserChoice の `ProgId`/`Hash` を直接書き込む | DefaultSetter、PS-SFTA、FTA-Manager、Firefox の UserChoice 経路 | MS 公式が「Registry-based changes are not supported for apps」と明記している。ハッシュが合わないと Windows が既定をリセットする |
| UserChoice ハッシュの偽装（MS 独自ハッシュの再実装） | SetUserFTA、SetDefaultBrowser（Kolbicz）、PS-SFTA、DefaultSetter、`WindowsUserChoice.cpp` | ユーザー保護機構（「without user interaction」の禁止）を意図的に回避するもの。マルウェアと同じ手口とみなされる危険がある。Store ポリシー 10.2.8 にも反する |
| UCPD.sys の無効化、ACL 改変、exe 名を変えての回避 | FTA-Manager の `remove-deny-acl.ps1`、報道にある回避手順 | OS のセキュリティ機能を壊す。MS は回避策を順次塞いでいる（報道） |
| UI Automation で Settings を代理操作する | sampalmer/set-default-browser（旧 CPL） | ユーザーの意思を偽装することになる。UI が変わるとすぐ壊れる |
| DISM の既定アプリ関連付け XML / GPO / MDM（ApplicationDefaults CSP） | 管理者向けの機能 | 管理された環境で管理者が使う正規手段であり、エンドユーザー向けアプリが使うべきものではない。BrowserFlip の範囲外 |
| `ApplicationAssociationToasts` を書き換えて通知を抑える | PS-SFTA | ユーザーへの通知を抑止するため |

これらのプロジェクトについては、ソースを読んで「参考にする」こと自体も避ける（クリーンルームの観点）。

## 6. BrowserFlip への推奨

1. **コードは自前で書き、他プロジェクトからはアイデアだけを取り入れる。** 今回の調査範囲には、そのままコピーする価値のある小さなコードは無かった。
2. **ブラウザの列挙**: `HKLM` と `HKCU` の `Software\Clients\StartMenuInternet\*`（`Capabilities\ApplicationName`、`DefaultIcon`、`shell\open\command`）と、`Software\RegisteredApplications` の値名との対応を自前で読む（Chromium の shell_util や BrowserMux の RegistrySetup と同じ考え方）。**読み取りのみ。**
3. **遷移ロジック**（Chromium/Firefox の公開設計と MS Learn に基づく）:
   - Win11 かつ「2023-04 累積更新以降のビルド」なら `ms-settings:defaultapps?registeredAppUser=<名前>`（HKCU に登録がある場合）、なければ `registeredAppMachine=<名前>`。名前は `Uri.EscapeDataString` でエスケープする（`+` も %2B にする。Chromium のコメント参照）。
   - MSIX/Store 版のブラウザで AUMID が分かる場合は `registeredAUMID=<AUMID>`。
   - Win10 や古い Win11 ビルド、または起動失敗時は、パラメータなしの `ms-settings:defaultapps` にフォールバックする（`ActivateApplication` の非公開ページ ID は使わない方が安全）。
   - 起動は `Process.Start(new ProcessStartInfo(uri){ UseShellExecute = true })`、または WinRT の `Launcher.LaunchUriAsync`。
4. **現在の既定ブラウザの表示**は `IApplicationAssociationRegistration::QueryCurrentDefault("http", AT_URLPROTOCOL, AL_EFFECTIVE)` を使う（MS が推奨する照会 API）。UserChoice は読まない方が無難（「obfuscated」とされているため）。
5. **絶対にやらないこと**: UserChoice への書き込み、ハッシュ計算、UCPD・ACL への干渉、UI Automation によるクリック代行、`LaunchAdvancedAssociationUI` への依存（Win10 以降は案内ダイアログが出るだけ）。
6. **ライセンス**: MIT の BrowserPicker / Hurl / BrowserMux はアイデアの参考にとどめる。万一コードを使う場合は、`THIRD-PARTY-NOTICES` に著作権表示と MIT 全文を入れる。Chromium（BSD-3）も同様。Firefox（MPL-2.0）、BrowserSelect（GPL-2.0）、Junction（GPL-3.0）のコードは持ち込まない。ライセンスが不明確なもの（PS-SFTA、Kolbicz 製品）とクローズドソースも持ち込まない。
7. **テスト上の注意**: BrowserPicker の Issue（Win11 Build 26200 で HTTP/HTTPS がバイパスされる）が示すように、最新ビルドでは挙動が変わり得る。Win11 23H2 / 24H2 / 25H2 系で実機確認を推奨する。

## 7. 出典 URL 一覧

- https://github.com/mortenn/BrowserPicker （readme.md、src/BrowserPicker.UI/ViewModels/FeedbackViewModel.cs、BrowserPicker.UI.csproj）
- https://github.com/U-C-S/Hurl （README.md、Source/Hurl.App/Views/SettingsPage.xaml.cs）
- https://github.com/alxbd/browsermux （README.md、src/BrowserMux.Core/Services/RegistrySetup.cs、src/BrowserMux.App/ViewModels/SettingsViewModel.cs）
- https://github.com/Nuliax7/DefaultSetter （DefaultSetter.cs）
- https://github.com/sampalmer/set-default-browser
- https://github.com/legach/SetDefaultBrowserUI （SetDefaultBrowserUI/Services/SdbWrapper.cs）
- https://github.com/DanysysTeam/PS-SFTA （SFTA.ps1）
- https://github.com/feiglein74/FTA-Manager
- https://github.com/default-username-was-already-taken/set-fileassoc
- https://github.com/sonnyp/Junction ／ https://github.com/zumoshi/BrowserSelect
- https://github.com/chromium/chromium/blob/main/base/win/default_apps_util.cc
- https://github.com/chromium/chromium/blob/main/chrome/installer/util/shell_util.cc
- https://github.com/chromium/chromium/blob/main/chrome/browser/shell_integration_win.cc
- https://github.com/mozilla-firefox/firefox/blob/main/browser/components/shell/WindowsDefaultBrowser.cpp
- https://github.com/mozilla-firefox/firefox/blob/main/browser/components/shell/nsWindowsShellService.cpp
- https://github.com/mozilla-firefox/firefox/blob/main/browser/components/shell/WindowsUserChoice.cpp
- https://github.com/mozilla-firefox/firefox/blob/main/toolkit/mozapps/defaultagent/SetDefaultBrowser.cpp
- https://firefox-source-docs.mozilla.org/widget/windows/shell/set-default.html
- https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings （ms.date 2026-11-02）
- https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-default-apps-settings （ms.date 2025-01-11、updated 2026-05-29）
- https://learn.microsoft.com/en-us/windows/apps/develop/windows-integration/default-apps-platform （ms.date 2026-09-26）
- https://learn.microsoft.com/en-us/windows/win32/api/shobjidl/nf-shobjidl-iapplicationassociationregistrationui-launchadvancedassociationui
- https://kolbi.cz/blog/2017/11/10/setdefaultbrowser-set-the-default-browser-per-user-on-windows-10-and-server-2016-build-1607/
- https://blogs.windows.com/windowsexperience/2023/03/17/a-principled-approach-to-app-pinning-and-app-defaults-in-windows/ （本文は未取得。検索結果と報道で確認）
- https://www.neowin.net/news/microsoft-quietly-adds-windows-ucpd-driver-to-block-registry-hacks-for-default-app-switches （UCPD、KB5034763/KB5034765）
- https://www.techzine.eu/news/applications/118510/new-windows-driver-blocks-software-that-changes-default-web-browser
- https://binary.ninja/2025/03/25/default-browser-upcd.html
- https://www.windowscentral.com/software-apps/windows-11/windows-11-dma-compliance-features-edge-default-announcement-2025 （EEA の「Set default」拡張）
