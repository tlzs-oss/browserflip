# Linux 適合性評価(R2 事前検証)

**本段階で Linux 製品は作りません。** 将来 Linux に展開する際に何が共有でき、何を作り直し、何を諦めるかを先に確定させるための評価です。

関連: [unified-architecture.md](unified-architecture.md) / [cross-platform-dev.md](cross-platform-dev.md)

---

## 0. この評価の確度

| 根拠の種類 | 内容 |
| --- | --- |
| **実測済み(x64)** | GitHub Actions の ubuntu-latest で**実際にアプリを起動**(Xvfb = 実 X11 サーバー)。ビルド・テスト・自己診断すべて PASS |
| **実測済み(ARM64)** | Docker Desktop の linux/arm64 コンテナで**実機 ARM64 実行**。実 `.desktop` 走査で 2 ブラウザー検出、実アイコンテーマ解決、設定の再起動間保持、起動 0.4 秒 / RSS 185 MB を計測 |
| **未実測** | **Wayland コンポジター上の動作**、**GNOME Shell / KDE Plasma の実セッション**、実際の `xdg-settings set` の効果、Flatpak/Snap ブラウザーの列挙、**実 Firefox / Chromium の `.desktop`**(コンテナの Ubuntu では snap ラッパーしか無く導入不可) |
| **調査ベース** | 以下の機能分類は公開仕様と既知の制約に基づく評価です |

> **コンパイル成功は動作保証ではありません。** 実機検証は Linux ラウンドで別途必要です。

---

## 1. 機能分類

### A. 三プラットフォーム統一で実装できるもの

| 機能 | 根拠 |
| --- | --- |
| ブラウザー検出のデータモデル | `BrowserInfo` は `.desktop` ファイル名を ProgID 相当として扱える |
| 既定状態の判定ロジック | `DefaultBrowserState` / `ChangeTracker` は OS 非依存 |
| UI 全体(ウィンドウ、一覧、設定) | X11 は `Avalonia.Desktop` に標準搭載。**Wayland は別パッケージ `Avalonia.Wayland` と `UseWaylandWithFallback()` が必須**(本プロトタイプは参照済み)。同パッケージは `xdg_wm_base >= 3` を要求し、満たさないコンポジターでは X11 にフォールバックする |
| 多言語(en / ja / zh-CN) | `.resx` + `ResourceManager` は OS 非依存 |
| 設定の永続化 | `JsonSettingsStore` が `$XDG_CONFIG_HOME/BrowserFlip` に解決(`SpecialFolder.ApplicationData`)。**コード変更不要** |
| ブラウザー アイコン | `.desktop` の `Icon=` → XDG icon theme 探索。実装は必要だが概念は共通 |

### B. プラットフォーム専用実装が必要なもの

| 機能 | Linux での手段 | 備考 |
| --- | --- | --- |
| ブラウザー一覧 | `/usr/share/applications`、`~/.local/share/applications`、Flatpak の exports を走査し、`MimeType=` に `x-scheme-handler/https` を含む `.desktop` を抽出 | `IBrowserCatalog` の新実装 |
| 既定ブラウザー読み取り | `xdg-settings get default-web-browser`、または `~/.config/mimeapps.list` の `x-scheme-handler/http(s)` | `IAssociationQuery` の新実装 |
| 既定ブラウザー変更 | `xdg-settings set default-web-browser <id>.desktop` | `IDefaultBrowserSwitcher` の新実装 |
| アイコン解決 | XDG Icon Theme 仕様に従う探索(hicolor フォールバック) | `IAppIconProvider` の新実装 |
| システム トレイ | X11: XEmbed systray / Wayland: StatusNotifierItem (D-Bus) | Avalonia の `TrayIcon` が SNI を使用 |

### C. 機能低下(デグレード)を受け入れる必要があるもの

| 機能 | 問題 | 対応方針 |
| --- | --- | --- |
| **フローティング ツールバー(位置指定)** | **ネイティブ Wayland クライアントは自分のウィンドウをグローバル座標に配置できません。**これは設計上の決定で、拡張を足す提案にも Wayland メンテナは強く反対しています | X11 クライアント(ネイティブ X11 / XWayland)では従来どおり。ネイティブ Wayland では「位置保存・復元」を無効化し、コンポジタ任せにする |
| **常に手前に表示(Topmost)** | `xdg-shell` に always-on-top 要求はありません。wlroots 系の `wlr-layer-shell` では可能ですが GNOME / KDE では一般に使えません | ネイティブ Wayland では Topmost を **UNSUPPORTED** として UI で明示。X11 クライアントは `_NET_WM_STATE_ABOVE` で従来どおり(XWayland を含む。§3-1 と下の注を参照) |

> **XWayland の扱い(上流ソース由来・実機未検証)**
> XWayland 上の X11 クライアントに対しても、mutter は `_NET_WM_STATE_ABOVE` を `_NET_SUPPORTED` に
> 広告し(`src/x11/meta-x11-display.c` `set_supported_hint` / `src/x11/atomnames.h`)、
> `meta_window_make_above` → `META_LAYER_TOP` で適用します。`MetaWindowXwayland` は
> `MetaWindowX11` の派生で、レイヤーや `_NET_WM_STATE` の扱いを上書きしていません。
> ConfigureRequest の x/y も、`disable-workarounds`(既定 false)が有効でなく対話的グラブ中でも
> なければ適用されます(`src/x11/window-x11.c` `meta_window_move_resize_request`)。
> kwin も `NET::KeepAbove` を広告し `WinInfo::changeState` → `setKeepAbove` で適用します。
> **ただし、これは上流ソースの読解であって実機計測ではありません。** 既知の制限:
> mutter は最大化中は ABOVE レイヤーを外す / X11 ウィンドウは WM グループの最大レイヤーを継承する /
> kwin のウィンドウ ルールで keepAbove を無効化できる / XWayland の位置要求はスケールされたうえで
> mutter の制約エンジンでクランプされる。実機で機能しないと判明した場合の正しい対処は、
> XWayland を `Capability.Unavailable` に下げてその計測結果を理由に書くことであり、
> 申告したまま黙って壊れている状態を残すことではありません。
| **システム トレイ(GNOME)** | GNOME Shell は標準でトレイ非対応。AppIndicator 拡張が必要 | トレイ生成失敗を検知し、ウィンドウのみで動作する縮退モードへ |
| **マルチモニター座標** | Wayland ではグローバル座標が取得できないため、「作業領域内に収める」ロジックが機能しません | Wayland では位置制御を行わない |
| **既定変更時の確認ダイアログ** | `xdg-settings` は**確認なしで即座に適用**します。Windows / macOS は OS が確認します | BrowserFlip 自身が確認ダイアログを出す。`SwitchMechanism.CommandLineTool` で UI が分岐可能(設計済み)。**無人実行の例外:** `--selftest <dir> --switch-default <id>` の両方が指定されたときだけ、そのフラグ自体を同意とみなす(`CommandLineConsent`)。実測(Ubuntu 26.04.1 GNOME Wayland)で、応答する者がいないモーダル ダイアログを開いて**無期限にハングしレポートを一切出力しなかった**ための対処。通常の GUI 操作と fail-closed な既定(`NeverConfirms`)は変更していない。自己診断レポートの検査 6 には同意の出所を明記する |
| **Flatpak 配布時の既定変更** | Flatpak サンドボックス内から `xdg-settings` でホストの既定を変えるのは制限されます | Flatpak では既定変更を **UNSUPPORTED** とし、読み取り専用で提供。または配布形式から Flatpak を外す |

---

## 2. 配布形式の評価

| 形式 | 既定ブラウザー変更 | トレイ | 推奨度 |
| --- | --- | --- | --- |
| **AppImage** | 可(サンドボックスなし) | 可 | **第一候補** |
| **.deb / .rpm** | 可 | 可 | 第二候補(ディストリごとのビルドが必要) |
| **Flatpak** | 制限あり | 可 | 既定変更が主機能のため不適 |
| **Snap** | 制限あり(strict confinement) | 制限あり | 不適 |

.NET 10 の self-contained 発行(`linux-x64` / `linux-arm64`)が前提です。

---

## 3. 推奨する Linux 対応方針

1. **X11 を第一級サポート、ネイティブ Wayland を機能限定サポート**とする。能力の判定根拠は
   `XDG_SESSION_TYPE` ではなく、**Avalonia が実際に初期化したウィンドウ バックエンド**
   (`TopLevel.PlatformImpl` の実装アセンブリが `Avalonia.X11` か `Avalonia.Wayland` か)である。
   `UsePlatformDetect()` + `UseWaylandWithFallback()` は Wayland 初期化に失敗すると X11 へ
   暗黙にフォールバックするため、環境変数からはどちらが選ばれたか分からない。
   **実測:** GNOME Wayland セッションで `WAYLAND_DISPLAY` を外し `DISPLAY=:0` で起動すると、
   `XDG_SESSION_TYPE` は `wayland` のままだがプロセスは X11 クライアントであり、
   「Wayland には always-on-top 要求が無い」という申告は**偽**になる。
   したがって **ネイティブ Wayland / XWayland / ネイティブ X11** の三分類で申告する
   (§1-C の制約が当てはまるのは**ネイティブ Wayland のときだけ**)。
2. 能力申告は既存の 4 値語彙(PASS / FAIL / SKIPPED / **UNSUPPORTED**)をそのまま UI に出す。Wayland で Topmost を黙って無視するのではなく、無効化して理由を示す。
3. 配布は **AppImage** を第一候補とする。
4. 既定変更は Linux だけ BrowserFlip 自身が確認ダイアログを出す(他 OS は OS が出す)。

---

## 4. Linux ラウンドで最初に検証すべきこと

| # | 検証項目 | 理由 |
| --- | --- | --- |
| 1 | Ubuntu (GNOME/Wayland) と KDE (X11) での実起動 | 本ラウンドは**コンパイルのみ**。実動作は未知数 |
| 2 | `xdg-settings set default-web-browser` の実効果 | ディストリ差が大きい |
| 3 | `TrayIcon` が GNOME / KDE で実際に出るか | GNOME は拡張依存 |
| 4 | Wayland でフローティング ツールバーが出せるか | 出せなければ機能自体を Wayland で外す |
| 5 | `.desktop` 走査でブラウザーが正しく列挙できるか | Flatpak / Snap 版ブラウザーの扱い |
