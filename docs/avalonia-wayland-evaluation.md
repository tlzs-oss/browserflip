# Avalonia.Wayland 採用評価

`Avalonia.Wayland` を恒久的な依存として受け入れてよいかの評価です。きっかけは実測で見つかった欠陥で、
**Wayland セッション(XWayland なし)でプロトタイプが `XOpenDisplay failed` により SIGABRT で異常終了した**
ことでした(終了コード 134)。`Avalonia.Desktop` には Wayland バックエンドが含まれていません。

関連: [linux-feasibility.md](linux-feasibility.md) / [unified-architecture.md](unified-architecture.md)

---

## 0. 各主張の根拠

| 節 | 主張 | 根拠の種類 |
| --- | --- | --- |
| 1 | パッケージの出自・ライセンス・構成 | **実測**(nuspec / nupkg / NuGet API を直接取得) |
| 2 | `Avalonia.Desktop` に Wayland が含まれない | **実測**(12.1.4 と 12.2.0 の nuspec を直接確認) |
| 3 | 非 Linux ビルドへの容量影響 | **実測**(osx-arm64 の A/B publish 差分) |
| 4 | コンポジターの `xdg_wm_base` 要求 | **実測**(sway 1.9 / weston 13 の起動ログ) |
| 5 | 実セッションでの安定性 | **未検証**(Linux QA VM ラウンドで検証中) |

---

## 1. パッケージの素性

| 項目 | 値 |
| --- | --- |
| ID | `Avalonia.Wayland` |
| 採用バージョン | 12.1.4 |
| 作者 | **Avalonia Team**(サードパーティ製ではなく公式) |
| ライセンス | **MIT**(`<license type="expression">MIT</license>`) |
| 出自 | `github.com/AvaloniaUI/Avalonia`、`release/12.1.4`、commit `2d79ba55fae1b1e71c69f93a668080c9029e6a31` |
| 構成 | `lib/net8.0` と `lib/net10.0` の**マネージド DLL のみ**。`runtimes/` のネイティブ資産は**なし** |
| 推移的依存 | `NWayland` 0.11.0(MIT)、`Avalonia.FreeDesktop`(MIT) |

本体に同梱される SBOM(CycloneDX)上の構成要素も `Avalonia.BuildServices` / `MicroCom.Runtime` /
`NWayland` / `System.IO.Pipelines` / `Tmds.DBus.Protocol` で、**すべて MIT** です。
再配布にあたりコピーレフトの制約は生じません。

### リリース履歴 — 新しいパッケージであること

| バージョン | 公開日 |
| --- | --- |
| 12.1.0 | 2026-07-09 |
| 12.1.1 | 2026-07-29 |
| 12.1.2 | 2026-09-02 |
| 12.1.3 | 2026-09-22 |
| **12.1.4(採用)** | **2026-10-09** |
| 12.1.5 | 2026-11-02 |
| 12.1.6 | 2026-11-09 |
| 12.2.0 | 2026-11-23 |

**全 8 バージョン、すべて 12.1 系以降です。** このパッケージは Avalonia 12.1 で初めて登場しました。
公式・MIT である一方、**公開から約 4 か月半しか経っていません**。これは本評価で最も重い
リスク要因であり、§5 の扱いの根拠です。

---

## 2. 明示参照は回避できない

`Avalonia.Desktop` の nuspec を直接確認した結果:

| `Avalonia.Desktop` | 含まれるバックエンド |
| --- | --- |
| 12.1.4 | `Avalonia.Win32`、`Avalonia.X11`、`Avalonia.Native` |
| **12.2.0(最新)** | `Avalonia.Win32`、`Avalonia.X11`、`Avalonia.Native` |

**最新の 12.2.0 でも Wayland は含まれていません。** 「次のバージョンまで待てば `Avalonia.Desktop`
だけで済む」という期待は、現時点では根拠がありません。`Avalonia.Wayland` の明示参照と
`UseWaylandWithFallback()` の呼び出しは、当面どちらも必要です。

---

## 3. 非 Linux プラットフォームへの影響 — 実測 864 KiB

`Avalonia.Desktop` のみを参照する最小プロジェクトと、そこに `Avalonia.Wayland` を足した
最小プロジェクトを osx-arm64 向けに publish し、出力を差分比較しました。

| | 出力サイズ |
| --- | --- |
| `Avalonia.Desktop` のみ | 27,652 KiB |
| `+ Avalonia.Wayland` | 28,516 KiB |
| **差分** | **864 KiB** |

増えたアセンブリは次の 2 つだけです。

| アセンブリ | サイズ |
| --- | --- |
| `Avalonia.Wayland.dll` | 236,544 バイト |
| `NWayland.dll` | 640,000 バイト |

> `Avalonia.FreeDesktop.dll` / `Tmds.DBus.Protocol.dll` / `MicroCom.Runtime.dll` は
> **`Avalonia.X11` 経由で元から入っており、Wayland の費用ではありません。**
> 参照の有無にかかわらず publish 出力に存在します。

**評価:** 非 Linux 向け成果物が約 0.9 MiB 増えます。ネイティブ資産は増えず、
Windows / macOS の実行時挙動は変わりません(`UseWaylandWithFallback()` は
Linux 以外では何もしません)。31 MiB の成果物に対して約 3% の増加です。

### 判明した設計上の弱点

`UseWaylandWithFallback()` の呼び出しは `Program.cs` で**無条件**に書かれています。
そのためパッケージ参照を外すと **Windows / macOS を含む全プラットフォームでコンパイルが
壊れます**(`CS1061: 'AppBuilder' に 'UseWaylandWithFallback' の定義が含まれておらず`)。
実際に参照を外して publish し、この挙動を確認しました。

これは「Linux のときだけ依存したい」という選択肢が現状は取れないことを意味します。
864 KiB を許容するか、呼び出し側を条件コンパイルに変えるかのどちらかです。
**本評価では 864 KiB を許容する案を推奨します** — 条件コンパイルは
プラットフォーム分岐をエントリポイントに持ち込み、三プラットフォーム統一という
本来の設計方針を薄めるためです。

---

## 4. 実行時要求 — `xdg_wm_base >= 3`

`Avalonia.Wayland` は `xdg_wm_base` インターフェイスのバージョン 3 以上を要求します。
満たさないコンポジターでは Wayland バックエンドの初期化が失敗し、X11 にフォールバックします。

| コンポジター | 広告バージョン | 結果 |
| --- | --- | --- |
| sway 1.9 | 2 | 要求を満たさず **X11 にフォールバック** |
| weston 13 | 5 | 要求を満たす |

> **この差は実測値です。** CI の Wayland ジョブで sway を使った場合、
> 「Wayland で動いた」という結論は出せません。実際には X11 経路を通っています。
> GNOME Shell / KDE Plasma の実セッションでの確認が別途必要です。

---

## 5. 結論と推奨

**採用を推奨します。** ただし以下の条件付きです。

| | 判断 |
| --- | --- |
| ライセンス | **問題なし。** 本体・推移的依存ともすべて MIT で、再配布に制約はありません |
| 出自 | **問題なし。** Avalonia 公式リポジトリのリリースブランチから生成された署名付きパッケージです |
| 代替手段 | **なし。** 最新の `Avalonia.Desktop` にも Wayland は含まれません |
| 容量 | **許容範囲。** 非 Linux で +864 KiB(約 3%)、ネイティブ資産の増加なし |
| 成熟度 | **要注意。** 公開から約 4 か月半、全 8 バージョン。実セッションでの検証が済むまで安定とは言えません |

### 採用しない場合に起きること

代替は「Wayland を捨てて X11 のみ対応する」ことですが、これは実質的に不可能です。
Ubuntu は 21.04 以降 GNOME のデフォルトセッションが Wayland であり、XWayland が
入っていない環境では**アプリが起動せず異常終了します**(本評価の発端)。
パッケージを外すなら、せめて「Wayland セッションでは起動できない」と検出して
正常終了する処理が必要で、それは機能として後退します。

### 残作業

| # | 項目 | 状態 |
| --- | --- | --- |
| 1 | GNOME + Wayland 実セッションでの起動確認 | Linux QA VM ラウンドで検証中 |
| 2 | KDE Plasma + Wayland 実セッションでの起動確認 | Linux QA VM ラウンドで検証中 |
| 3 | 12.1.5 / 12.1.6 / 12.2.0 への更新可否 | 未着手。実セッション検証が済んでから判断する |
| 4 | `xdg_wm_base` 要求を満たさないコンポジターでのフォールバック確認 | sway 1.9 で実測済み(X11 に落ちる) |
