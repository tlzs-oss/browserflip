# macOS 署名・公証(評価と正式リリース手順)

**証明書は購入していません。署名も公証も実施していません。** 本書は、配布に何が必要かを事前に
確定させるための評価と、証明書取得後にそのまま実行できる正式手順です。

関連: [unified-architecture.md](unified-architecture.md) / [../prototypes/avalonia/README.md](../prototypes/avalonia/README.md)

---

## 0. 本書と実装の対応(ここが実態)

| 項目 | ファイル | 状態 |
| --- | --- | --- |
| `.app` の組み立て | `prototypes/avalonia/macos/bundle.sh` | **実施済み。** アドホック署名(`codesign --sign -`)のみ |
| 製品アイコン | `prototypes/avalonia/macos/make-icns.sh` → `Contents/Resources/BrowserFlip.icns` | **実施済み。** `CFBundleIconFile` も設定済み |
| `.dmg` の作成 | `prototypes/avalonia/macos/make-dmg.sh` | **実施済み。ただし DMG の署名は行いません**(§3 の手順 2 は未実装) |
| entitlements | `prototypes/avalonia/macos/entitlements.plist` | **用意のみ。** どのビルドでも使用していません(§3.1) |
| Hardened Runtime | — | **未実施**(証明書が必要) |
| Developer ID 署名 | — | **未実施** |
| 公証 / staple | — | **未実施** |

> 以前の版は存在しない `macos/entitlements.plist` を参照し、`make-dmg.sh` が行わない DMG 署名を
> あたかも自動化済みであるかのように書いていました。ファイルは作成し、スクリプトが行わないこと
> は行わないと明記しました。

---

## 1. 現状(実測)

`prototypes/avalonia/macos/verify-bundle.sh` の出力(macOS 27.0 arm64、Release ビルド):

```
 size              : 113M         (.app 展開時)
 architectures     : arm64
 minimum macOS     : 12.0
 bundle identifier : co.jp.tlzs.browserflip.prototype
 CFBundleIconFile  : BrowserFlip.icns
 icon file         : present, 300K, largest 512 px
 verify            : OK           (アドホック署名は構造的に正しい)
 authority         : none (ad-hoc or unsigned)
 Gatekeeper        : rejected
```

DMG は 50 MB(UDZO 圧縮、`artifacts/BrowserFlip-Prototype-arm64.dmg`)。

**`rejected` は想定どおりです。** アドホック署名(`codesign --sign -`)は自分の Mac でしか通用せず、
Hardened Runtime も entitlements も伴いません。他人の Mac では Gatekeeper が起動を拒否します。
**アドホック署名を「署名済み」と表現してはいけません。**

---

## 2. 配布に必要なもの

| # | 必要物 | 費用 / 条件 | 備考 |
| --- | --- | --- | --- |
| 1 | **Apple Developer Program 加入** | **年額 99 USD** | 組織加入には D-U-N-S 番号が必要(TLZS株式会社として取得済みか要確認) |
| 2 | **Developer ID Application 証明書** | 加入に含まれる | `.app` と `.dmg` の署名用。Mac App Store 外配布はこれ |
| 3 | **Developer ID Installer 証明書** | 加入に含まれる | `.pkg` を配る場合のみ。DMG 配布なら不要 |
| 4 | **Hardened Runtime** | 無償(署名オプション) | 公証の必須条件。`codesign --options runtime` |
| 5 | **公証(Notarization)** | 無償 | Apple のサーバーに送って審査。通常数分 |
| 6 | **Staple** | 無償 | 公証チケットを成果物に添付し、オフラインでも検証可能にする |
| 7 | **App Store Connect API キー** | 無償 | CI から公証する場合。App 固有パスワードでも可 |

**最小構成の費用は年額 99 USD のみ**です。公証自体は無償です。

---

## 3. 正式リリース手順(証明書取得後に実行する。**現時点では未実施**)

以下はリポジトリルートを作業ディレクトリとした場合のパスです。

```sh
APP="prototypes/avalonia/artifacts/BrowserFlip Prototype.app"
DMG="prototypes/avalonia/artifacts/BrowserFlip-Prototype-arm64.dmg"
IDENTITY="Developer ID Application: <TEAM NAME> (<TEAMID>)"

# 0. .app を作る(ここまでは自動化済み。アドホック署名が付いた状態になる)
prototypes/avalonia/macos/bundle.sh Release

# 1. Hardened Runtime 付きで .app を署名し直す(内側から外側へ)
#    bundle.sh が付けたアドホック署名は --force で置き換わります。
codesign --force --options runtime --timestamp \
  --entitlements prototypes/avalonia/macos/entitlements.plist \
  --sign "$IDENTITY" "$APP"
codesign --verify --deep --strict --verbose=2 "$APP"

# 2. 署名済みの .app から DMG を作り、DMG 自体も署名する
#    ★ make-dmg.sh はこの署名を行いません。また make-dmg.sh は bundle.sh を呼び直すため、
#      そのまま使うと手順 1 の署名が捨てられます。リリース時は hdiutil を直接使ってください。
STAGING="$(mktemp -d)"; cp -R "$APP" "$STAGING/"; ln -s /Applications "$STAGING/Applications"
hdiutil create -volname "BrowserFlip Prototype" -srcfolder "$STAGING" -ov -format UDZO "$DMG"
rm -rf "$STAGING"
codesign --force --timestamp --sign "$IDENTITY" "$DMG"

# 3. 公証に提出する
xcrun notarytool submit "$DMG" \
  --key AuthKey_XXXX.p8 --key-id XXXX --issuer <UUID> --wait

# 4. チケットを添付する
xcrun stapler staple "$DMG"
xcrun stapler validate "$DMG"

# 5. 検証(別の Mac で "accepted" になること)
spctl --assess --type execute --verbose=4 "$APP"
```

リリース時に手順 0〜2 を一本化するスクリプト(署名 ID を引数に取り、署名を挟んで DMG を作る)を
追加するのが望ましいですが、**署名できない状態で書いても検証できない**ため、証明書取得後に
作成します。

### 3.1 entitlements

`prototypes/avalonia/macos/entitlements.plist` を用意しました。内容:

```xml
<key>com.apple.security.cs.allow-jit</key><true/>
<key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
```

.NET(CoreCLR)は JIT で生成したメモリを実行するため、Hardened Runtime 下ではこの 2 つが必要に
なる見込みです。

> これは .NET の一般的な要件に基づく**予測**であり、**署名済みビルドでの実機検証はしていません**
> (証明書がないため検証不能)。証明書取得後に、2 つのキーを 1 つずつ外して起動を確認し、
> 本当に必要な最小の組み合わせを実測して確定させてください。

ライブラリ検証の無効化(`disable-library-validation`)や `allow-dyld-environment-variables` は
**入れていません**。BrowserFlip はサードパーティのプラグインを読み込まないため不要です。

App Sandbox の entitlement も入れていません。既定ブラウザーの変更は公開 API
`NSWorkspace.setDefaultApplication(at:toOpenURLsWithScheme:)` が OS 自身の同意ダイアログで
制御するもので、entitlement で許可される類のものではありません。**entitlements にこの同意
ダイアログを抑止する手段はなく、抑止してもいけません。**

---

## 4. CI での自動化(未実施)

GitHub Actions から公証する場合に必要なシークレット:

| シークレット | 内容 |
| --- | --- |
| `MACOS_CERTIFICATE_P12` | Developer ID 証明書(base64) |
| `MACOS_CERTIFICATE_PASSWORD` | p12 のパスワード |
| `APPSTORE_API_KEY_P8` / `_KEY_ID` / `_ISSUER_ID` | 公証用 API キー |

既存の Windows Release Workflow と同じく、**公開は手動**に留める方針を維持できます。
現在の CI に macOS の署名・公証ジョブはありません。

---

## 5. Owner の判断が必要な事項

1. **Apple Developer Program に加入するか**(年額 99 USD)。**現時点では加入を見送っており**、
   加入しない限り macOS 版の公開配布はできません。
2. 加入する場合、**個人名義か TLZS株式会社名義か**。組織名義は D-U-N-S 番号が必要で取得に時間が
   かかります。
3. 配布形式は **DMG**(推奨、Installer 証明書不要)か `.pkg` か。
4. Intel Mac (`osx-x64`) も配るか。配るなら universal binary 化(`lipo`)が追加で必要です。

---

## 6. 署名なしでの配布可否

| 方法 | 可否 |
| --- | --- |
| 署名・公証あり DMG | **推奨。** 普通にダブルクリックで開ける |
| 未署名 DMG + 「右クリック → 開く」案内 | 技術的には可能だが、macOS 15 以降は手順が増え、一般ユーザーには勧められない |
| Homebrew Cask | Cask 自体は未署名アプリも扱えるが、Gatekeeper の問題は解決しない |

現行 Windows 版も Authenticode 署名なしで配布し、SmartScreen の警告を README で案内しています
([README.md](../README.md))。macOS は Windows より制約が厳しいため、**同じ方針は取れません**。

**現時点の `.dmg` は検証用のビルド成果物であり、配布物ではありません。**
