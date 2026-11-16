# 変更履歴 / Changelog

このプロジェクトの主な変更点をこのファイルに記録します。

形式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に基づき、バージョン番号は [Semantic Versioning](https://semver.org/lang/ja/) に従います。

## [Unreleased]

### 追加

- プロジェクトのドキュメント(README 英語 / 日本語 / 簡体字中国語、アーキテクチャ、要件、テスト方針、コントリビューション ガイド、セキュリティ ポリシー)と Issue / PR テンプレート。

## [0.2.0] - 未リリース

デスクトップでの操作性を強化するリリースです。v0.1 のブラウザー検出・既定ブラウザーの取得・Windows 設定への遷移は変更していません。設計は [docs/desktop-design.ja.md](docs/desktop-design.ja.md)、操作方法は [docs/user-guide.ja.md](docs/user-guide.ja.md) を参照してください。

### 追加

- **専用アプリアイコン (V2-01)**: 地球と 2 本の循環矢印によるオリジナル図案(既存ブラウザーのロゴに似せない)。SVG マスター、PNG 16〜512 px、多解像度 ICO(16 / 20 / 24 / 32 / 40 / 48 / 64 / 256 px)と生成スクリプト。EXE・ウィンドウ・タスクバー・通知領域に適用。コードと同じ MIT ライセンス。
- **常に手前に表示 (V2-02)**: 通常画面とコンパクトバーのピン ボタン、メニューから切り替え。全画面アプリ / プレゼンテーション実行中(`SHQueryUserNotificationState`)は一時的に解除。
- **コンパクトバー (V2-03)**: 横(高さ 44 / 52 DIP)/ 縦(幅 48 / 56 DIP)の小型ツールバー。既定ブラウザーの強調表示、ツールチップ、通常画面に戻る・表示オプション ボタン、ブラウザーが多い場合のスクロール、右クリック メニュー。
- **画面端へのドッキング (V2-04)**: 左 / 右 / 上 / 下。作業領域(タスクバーを除く)の内側に配置し、ドラッグ終了時に端から 24 DIP 以内ならスナップ。AppBar 登録はせず、作業領域を変更しない。
- **自動的に隠す (V2-05)**: ドッキング中のみ。離脱 600 ms 後に収納して 8 × 56 DIP のタブを残し、タブ上 150 ms の滞在・クリック・Enter・通知領域アイコンで展開。マウス ボタン押下中・ドラッグ中・メニュー表示中・キーボード フォーカス中は収納しない。
- **通知領域アイコン (V2-06)**: 既定で有効。左クリックで表示、右クリック メニュー(BrowserFlip を表示 / 常に手前に表示 / コンパクトバー / 画面端にドッキング / ドッキング位置 / 自動的に隠す / 設定… / 終了)。Explorer の再起動後に自動で再登録。「ウィンドウを閉じても通知領域で動作を継続」(既定 OFF)。
- **複数モニター / DPI 対応 (V2-08)**: 物理ピクセルでの配置、拡大率の異なるモニター間の移動、モニターの接続・切断・解像度 / 拡大率変更・スリープ復帰・タスクバー変更後の再配置、画面外に保存された位置の補正。
- **表示オプション**: テーマ(システム / ライト / ダーク)、バーのサイズ(小 / 中)、ドッキングしていないときの向き(横 / 縦)。
- コマンドライン オプション `--settings <パス>`(別の設定ファイルを使用)。
- 日本語の操作説明([docs/user-guide.ja.md](docs/user-guide.ja.md))と v0.2 設計書([docs/desktop-design.ja.md](docs/desktop-design.ja.md))。
- `DockLayout` / `AutoHideController` / 設定ファイル / `ShellViewModel` / Win32 実装(モニター・配置・通知領域)の単体テスト。

### 変更

- **設定の永続化 (V2-07)**: `settings.json` に言語に加えて表示設定(テーマ、表示モード、最前面、ドッキング、自動的に隠す、バーのサイズと向き、通知領域、ウィンドウとバーの位置)を保存。フィールドごとに検証して不正な値だけを既定値に戻し、一時ファイル経由で置き換えて保存。v0.1 の設定ファイルはそのまま読み込める。
- メイン ウィンドウの位置と大きさを保存・復元(位置は物理ピクセル、大きさは DIP)。
- テーマを Windows に従うだけでなく、ライト / ダークに固定できるようにした。
- メイン ウィンドウを閉じると終了する動作は v0.1 と同じ(「ウィンドウを閉じても通知領域で動作を継続」が ON の場合のみ通知領域に残る)。

## [0.1.0] - 未リリース (MVP)

### 追加

- **ブラウザー自動検出 (F01)**: HKCU / HKLM(64 ビット / 32 ビット)の `Software\Clients\StartMenuInternet` と `Software\RegisteredApplications` から検出。実行ファイル パスのハードコードなし。アンインストール済み(exe なし)や `RegisteredApplications` 未登録の状態を判定。
- **カード一覧 UI (F02)**: 2 列カード、アイコン、「現在の既定」バッジ、ダークモード、Per-Monitor V2 高 DPI、English / 日本語 / 简体中文 の実行時切り替え。
- **Windows 設定を開く (F03)**: ディープリンク対応の Windows 11(21H2 ビルド 22000.1817 以上、22H2 ビルド 22621.1555 以上、23H2 以降。UBR で判定)では `ms-settings:defaultapps?registeredAppUser|registeredAppMachine=<名前>`、それ以外は `ms-settings:defaultapps` にフォールバック。
- **既定の取得と変更監視 (F04)**: `AssocQueryStringW` による `http` / `https` の現在のハンドラー取得、変更結果の判定(`AppliedToTarget` / `PartiallyApplied` / `ChangedToOther` / `NoChange`)、不一致警告。
- **エラー処理 (F05)**: アンインストール済みブラウザー、不完全な登録、設定を開けない場合、グループ ポリシー(`NoControlPanel`、`SettingsPageVisibility`、`DefaultAssociationsConfiguration`)、ユーザーのキャンセル。
- xUnit による Core の単体テスト(フェイク レジストリ / 関連付け / ランチャー)。
- CI: Windows ビルド、単体テスト、`dotnet format` チェック、CodeQL、依存関係レビューとライセンス許可リスト、シークレット スキャン、リリース成果物(win-x64 / win-arm64 の自己完結型シングルファイル zip と `SHA256SUMS`)の生成。

### セキュリティ

- 管理者権限不要、レジストリは読み取り専用、`UserChoice` / `UserChoiceLatest` への書き込みやハッシュ偽造は行わない、起動する URI は `ms-settings:` のみ、サードパーティの実行時依存なし、テレメトリ / ネットワーク アクセスなし。

[Unreleased]: https://github.com/tlzs-oss/browserflip/commits/main
[0.2.0]: https://github.com/tlzs-oss/browserflip/commits/main
[0.1.0]: https://github.com/tlzs-oss/browserflip/commits/main
