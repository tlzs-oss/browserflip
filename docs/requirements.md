# 要件定義

BrowserFlip — "One Click. Your Browser." の MVP(0.1.0)要件です。設計は [architecture.md](architecture.md)、検証方法は [testing.md](testing.md) を参照してください。

## 1. 目的とスコープ

Windows 10/11 で、インストール済みブラウザーの中から既定ブラウザーを「ワンクリックで Windows の正規の設定画面へ到達して」切り替えられるようにする。既定の変更そのものは Windows の設定画面でユーザーが確定し、BrowserFlip は Windows の確認を省略・偽装しない。

対象 OS: Windows 10 バージョン 1809(ビルド 17763)以降 / Windows 11。アーキテクチャ: x64 / Arm64。

## 2. 機能要件(MVP)

### F01 ブラウザー自動検出

HKCU と HKLM(64 ビット / 32 ビット ビュー)の `Software\Clients\StartMenuInternet` と `Software\RegisteredApplications` から、インストール済みブラウザーを検出する。

受け入れ基準:

- [ ] 実行ファイル パスをハードコードせず、レジストリの登録情報のみで検出する。
- [ ] `Capabilities\URLAssociations` に `http` または `https` を宣言するキーのみをブラウザーとして扱う。
- [ ] HKCU の登録は同名の HKLM の登録より優先される。
- [ ] 表示名は既定値 → `Capabilities\ApplicationName` → `LocalizedString` の順で取得し、`@` 形式の間接文字列は解決する。取得できなければキー名を使う。
- [ ] `RegisteredApplications` との対応付け(Capabilities パス一致、なければ名前一致)を行い、ディープリンク用の名前とスコープを保持する。
- [ ] 表示名順(大文字小文字を区別しない)に並べる。
- 範囲外(MVP): `registeredAUMID` で登録されるパッケージ版(MSIX / Store)ブラウザーの検出は Phase 2 で対応予定。

### F02 カード一覧 UI

受け入れ基準:

- [ ] 2 列のカードで、アイコン・名前・「現在の既定」バッジを表示する。
- [ ] カード全体がクリック可能で、キーボード(Tab / Enter / Space)でも操作できる。
- [ ] Windows のアプリ テーマ(ライト / ダーク)に追従する。
- [ ] Per-Monitor V2 高 DPI に対応し、モニター間移動や拡大率 100%〜200% でぼやけ・レイアウト崩れがない。
- [ ] UI 言語は English / 日本語 / 简体中文。OS の言語から自動選択し、実行中に切り替えられる。

### F03 Windows 設定を開く

受け入れ基準:

- [ ] ディープリンク対応の Windows 11(21H2: 10.0.22000.1817 以上、22H2: 10.0.22621.1555 以上、23H2 以降。いずれも 2023 年 4 月の累積更新プログラムで対応、[Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-default-apps-settings) 参照)かつ `RegisteredApplications` 登録がある場合、`ms-settings:defaultapps?registeredAppUser=<名前>` または `registeredAppMachine=<名前>` を開く。
- [ ] それ以外、またはディープリンクの起動に失敗した場合は `ms-settings:defaultapps` にフォールバックする。
- [ ] 既定の確定はユーザーが設定画面の「既定値に設定」で行う。Windows の確認を省略しない。
- [ ] `ms-settings:` 以外の URI は起動しない。

### F04 現在の既定の取得と変更監視

受け入れ基準:

- [ ] `AssocQueryStringW(ASSOCF_IS_PROTOCOL, ASSOCSTR_PROGID)` で `http` / `https` の現在のハンドラーを取得する。
- [ ] 起動時、設定を開く前、変更待機中(約 1.5 秒間隔のポーリング、最大 3 分)、ウィンドウ再アクティブ化時に再取得する。
- [ ] 変更結果を `AppliedToTarget` / `PartiallyApplied` / `ChangedToOther` / `NoChange` として判定し、UI に反映する。
- [ ] `http` と `https` のハンドラーが異なる場合は不一致警告を表示する。
- [ ] 検出済みブラウザー以外が既定の場合も、その旨(未認識)を表示する。

### F05 エラー処理

| ケース | 期待動作 | 受け入れ基準 |
| --- | --- | --- |
| ブラウザーがアンインストール済み(exe なし) | カードを無効表示 | [ ] クリックしても設定を開かず、理由を表示する |
| 登録が不完全(URL 関連付けなし / `RegisteredApplications` なし) | 前者は一覧から除外、後者はトップページへフォールバック | [ ] クラッシュせず、可能な範囲で操作を継続できる |
| 設定画面を開けない | エラー メッセージ | [ ] シェル起動の失敗を例外で落とさずメッセージ表示する |
| グループ ポリシー `NoControlPanel` / `SettingsPageVisibility` で `defaultapps` が非表示 | ブロック メッセージ | [ ] 設定を起動せず、管理者のポリシーによる旨を表示する |
| グループ ポリシー `DefaultAssociationsConfiguration` | 警告 | [ ] サインイン時に変更が元に戻る可能性がある旨を表示する |
| ユーザーがキャンセル | 情報メッセージ | [ ] 戻った時点で変更がなければ「変更されませんでした」を表示する |

## 3. 非機能要件

| 区分 | 要件 |
| --- | --- |
| 権限 | 管理者権限を要求しない(`asInvoker`)。 |
| 安全性 | レジストリは読み取り専用。`UserChoice` / `UserChoiceLatest` への書き込み、UserChoice ハッシュの計算・偽造、DISM の既定関連付け XML の使用を禁止する。 |
| 依存関係 | 本体にサードパーティの実行時依存を持たない。テスト用パッケージはライセンス許可リストで監査する。 |
| プライバシー | テレメトリなし、ネットワーク アクセスなし。 |
| 多言語 | 英語・日本語・簡体字中国語。すべてのリソース キーが 3 言語に存在すること(単体テストで強制)。 |
| 表示 | Per-Monitor V2 高 DPI、ダークモード。 |
| アクセシビリティ | キーボードのみで全操作が可能。すべての操作要素に `AutomationProperties.Name` を設定し、スクリーン リーダーで読み上げられる。 |
| 品質 | ビルド警告をエラー扱い(`TreatWarningsAsErrors`)、`dotnet format` 準拠、Core の単体テストが CI で成功する。 |
| 性能 | 起動から一覧表示まで体感的に即時(目安 1 秒以内)。ポーリングは待機中のみ行う。 |

## 4. Phase 2(ロードマップ、未実装)

- パッケージ版(MSIX / Microsoft Store)ブラウザーの検出と `ms-settings:defaultapps?registeredAUMID=` によるディープリンク
- システム トレイ常駐
- グローバル ホットキー
- 起動時チェック(既定ブラウザーの変化を通知)
- ポータブル版
- インストーラー
- 更新通知
- 自動化コマンドの調査(Windows がサポートする仕組みが存在する場合のみ。UserChoice の直接書き込みやハッシュ偽造は引き続き行わない)

## 5. MVP 完了条件

- [ ] Windows 11 実機で起動する。
- [ ] インストール済みブラウザーを検出して表示する。
- [ ] 現在の既定ブラウザーを表示する。
- [ ] カードから Windows 設定の該当ページを開ける。
- [ ] 設定で変更後、BrowserFlip に戻ると表示が更新される。
- [ ] `http` / `https` の不一致を検出して警告する。
- [ ] 管理者権限なしで動作する。
- [ ] 単体テストが成功し、コード レビューが完了している。
- [ ] 依存関係のライセンス監査が完了している。
- [ ] [testing.md](testing.md) の実機受け入れテストに合格している。
