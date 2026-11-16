# セキュリティ ポリシー / Security Policy

日本語の説明の後に [English version](#english) があります。

## サポート対象バージョン

| バージョン | サポート |
| --- | --- |
| 0.1.x | :white_check_mark: |
| < 0.1 | :x: |

## 脆弱性の報告

**公開 Issue・Discussion・Pull Request で脆弱性を報告しないでください。**

GitHub の **プライベート脆弱性報告** を使用してください。

1. リポジトリの **Security** タブを開きます。
2. **Report a vulnerability** をクリックします。
3. 影響を受けるバージョン、OS ビルド、再現手順、想定される影響を記入します。

メンテナーは内容を確認し、修正と公開(必要に応じて GitHub Security Advisory の発行)を調整します。報告者のクレジット表記を希望される場合はお知らせください。

## 対象範囲

対象:

- 本リポジトリのソース コード(`src/`)
- 本リポジトリの GitHub Releases で配布されるバイナリ
- ビルド / リリース用のワークフローとスクリプト

例: 管理者権限なしでの意図しない権限昇格、`ms-settings:` 以外の URI やプロセスの起動、レジストリへの書き込み、改ざんされたレジストリ値による任意コード実行、リリース成果物の改ざん。

対象外:

- Windows 自体、各ブラウザー、サードパーティ ソフトウェアの脆弱性(各ベンダーへ報告してください)
- 既に管理者権限やローカル レジストリの書き込み権限を持つ攻撃者を前提とする問題
- 非公式に再配布されたバイナリ

## セキュリティ原則

BrowserFlip は次の原則に基づいて設計されています。これらに反する動作はセキュリティ上の問題として扱います。

- **管理者権限を必要としない。**
- **レジストリは読み取り専用。** すべてのキーを `writable: false` で開きます。
- **`UserChoice` / `UserChoiceLatest` に書き込まず、UserChoice ハッシュを計算・偽造しない。** DISM の既定関連付け XML も使用しません。
- **既定ブラウザーの変更は Windows の設定画面でユーザーが確定する。** Windows の確認を省略しません。
- **起動するのは `ms-settings:` URI のみ。** それ以外のスキームは拒否します。
- **サードパーティの実行時依存なし。** テスト用パッケージはライセンス許可リストで監査します。
- **テレメトリなし、ネットワーク アクセスなし。**

---

<a id="english"></a>

## English

### Supported versions

| Version | Supported |
| --- | --- |
| 0.1.x | :white_check_mark: |
| < 0.1 | :x: |

### Reporting a vulnerability

**Please do not report vulnerabilities in public issues, discussions or pull requests.**

Use GitHub **private vulnerability reporting**: open the repository's **Security** tab and click **Report a vulnerability**. Include the affected version, Windows build, reproduction steps and expected impact. Maintainers will triage the report and coordinate a fix and disclosure (including a GitHub Security Advisory where appropriate). Let us know if you would like to be credited.

### Scope

In scope: source code in this repository, binaries published on this repository's GitHub Releases, and the build/release workflows and scripts. Examples: unintended privilege escalation, launching anything other than `ms-settings:` URIs, registry writes, code execution through crafted registry values, tampered release artifacts.

Out of scope: vulnerabilities in Windows, browsers or other third-party software (report them to the vendor); issues that assume an attacker already has administrator rights or write access to the local registry; unofficial redistributions.

### Security principles

- No administrator rights required.
- Read-only registry access (`writable: false`).
- Never writes `UserChoice` / `UserChoiceLatest`, never computes or forges the UserChoice hash, no DISM default-association XML.
- The user confirms the change in Windows Settings; Windows' confirmation is never skipped.
- Only `ms-settings:` URIs are launched.
- No third-party runtime dependencies.
- No telemetry and no network access.

Behavior that violates these principles is treated as a security issue.
