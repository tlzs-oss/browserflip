# macOS 署名・公証の必要条件(評価のみ)

**証明書は購入していません。** 配布に何が必要かを事前に確定させるための評価です。

関連: [unified-architecture.md](unified-architecture.md) / [../prototypes/avalonia/README.md](../prototypes/avalonia/README.md)

---

## 1. 現状(実測)

`prototypes/avalonia/macos/verify-bundle.sh` の出力:

```
 size              : 112M          (.app 展開時)
 architectures     : arm64
 minimum macOS     : 12.0
 bundle identifier : co.jp.tlzs.browserflip.prototype
 verify            : OK            (アドホック署名は構造的に正しい)
 authority         : none (ad-hoc or unsigned)
 Gatekeeper        : rejected
```

DMG は 49 MB(UDZO 圧縮)。

**`rejected` は想定どおりです。** アドホック署名(`codesign --sign -`)は自分の Mac でしか通用しません。他人の Mac では Gatekeeper が起動を拒否します。

---

## 2. 配布に必要なもの

| # | 必要物 | 費用 / 条件 | 備考 |
| --- | --- | --- | --- |
| 1 | **Apple Developer Program 加入** | **年額 99 USD** | 組織加入には D-U-N-S 番号が必要(TLZS株式会社として取得済みか要確認) |
| 2 | **Developer ID Application 証明書** | 加入に含まれる | `.app` の署名用。Mac App Store 外配布はこれ |
| 3 | **Developer ID Installer 証明書** | 加入に含まれる | `.pkg` を配る場合のみ。DMG 配布なら不要 |
| 4 | **Hardened Runtime** | 無償(ビルド設定) | 公証の必須条件 |
| 5 | **公証(Notarization)** | 無償 | Apple のサーバーに送って審査。通常数分 |
| 6 | **Staple** | 無償 | 公証チケットを成果物に添付し、オフラインでも検証可能にする |
| 7 | **App 固有パスワード or API キー** | 無償 | CI から公証するなら App Store Connect API キーが適切 |

**最小構成の費用は年額 99 USD のみ**です。公証自体は無償です。

---

## 3. 実行手順(証明書取得後)

```sh
# 1. Hardened Runtime 付きで署名(内側から外側へ)
codesign --force --options runtime --timestamp \
  --sign "Developer ID Application: <TEAM NAME> (<TEAMID>)" \
  --entitlements macos/entitlements.plist \
  "BrowserFlip.app"

# 2. DMG を作って署名
hdiutil create -volname "BrowserFlip" -srcfolder staging -ov -format UDZO BrowserFlip.dmg
codesign --force --timestamp --sign "Developer ID Application: ..." BrowserFlip.dmg

# 3. 公証に提出(App Store Connect API キー)
xcrun notarytool submit BrowserFlip.dmg \
  --key AuthKey_XXXX.p8 --key-id XXXX --issuer <UUID> --wait

# 4. チケットを添付
xcrun stapler staple BrowserFlip.dmg
xcrun stapler validate BrowserFlip.dmg

# 5. 検証(別の Mac で "accepted" になること)
spctl --assess --type execute --verbose=4 BrowserFlip.app
```

### entitlements

.NET アプリは JIT を使うため、Hardened Runtime では以下が必要になる見込みです。

```xml
<key>com.apple.security.cs.allow-jit</key><true/>
<key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
```

> これは .NET の一般的な要件に基づく**予測**で、実機検証はしていません。証明書取得後に最小の組み合わせを実測して確定させるべきです。

---

## 4. CI での自動化

GitHub Actions から公証する場合に必要なシークレット:

| シークレット | 内容 |
| --- | --- |
| `MACOS_CERTIFICATE_P12` | Developer ID 証明書(base64) |
| `MACOS_CERTIFICATE_PASSWORD` | p12 のパスワード |
| `APPSTORE_API_KEY_P8` / `_KEY_ID` / `_ISSUER_ID` | 公証用 API キー |

既存の Windows Release Workflow と同じく、**公開は手動**に留める方針を維持できます。

---

## 5. Owner の判断が必要な事項

1. **Apple Developer Program に加入するか**(年額 99 USD)。加入しない限り macOS 版の公開配布はできません。
2. 加入する場合、**個人名義か TLZS株式会社名義か**。組織名義は D-U-N-S 番号が必要で取得に時間がかかります。
3. 配布形式は **DMG**(推奨、Installer 証明書不要)か `.pkg` か。
4. Intel Mac (`osx-x64`) も配るか。配るなら universal binary 化(`lipo`)が追加で必要です。

**本ラウンドでは証明書を購入せず、上記の評価のみを行いました。**

---

## 6. 署名なしでの配布可否

| 方法 | 可否 |
| --- | --- |
| 署名・公証あり DMG | **推奨。** 普通にダブルクリックで開ける |
| 未署名 DMG + 「右クリック → 開く」案内 | 技術的には可能だが、macOS 15 以降は手順が増え、一般ユーザーには勧められない |
| Homebrew Cask | Cask 自体は未署名アプリも扱えるが、Gatekeeper の問題は解決しない |

現行 Windows 版も Authenticode 署名なしで配布し、SmartScreen の警告を README で案内しています([README.md](../README.md))。macOS は Windows より制約が厳しいため、**同じ方針は取れません**。
