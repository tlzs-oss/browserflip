# 変更履歴 / Changelog

このプロジェクトの主な変更点をこのファイルに記録します。

形式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に基づき、バージョン番号は [Semantic Versioning](https://semver.org/lang/ja/) に従います。

## [Unreleased]

### 追加

- プロジェクトのドキュメント(README 英語 / 日本語 / 簡体字中国語、アーキテクチャ、要件、テスト方針、コントリビューション ガイド、セキュリティ ポリシー)と Issue / PR テンプレート。

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
[0.1.0]: https://github.com/tlzs-oss/browserflip/commits/main
