# Linux 配布と CJK フォント — 実測結果

Ubuntu 26.04.1 LTS ARM64(TLZS-Linux-ARM64-QA、8 コア / 16 GB)の**実機**で測定しました。
推定値はありません。測っていない項目は「未測定」と書いています。

関連: [linux-feasibility.md](linux-feasibility.md) / [avalonia-wayland-evaluation.md](avalonia-wayland-evaluation.md)

---

## 1. CJK フォントの選択肢

### ライセンス — 同梱に障害はありません

パッケージに同梱されている著作権ファイル(`/usr/share/doc/fonts-noto-cjk/copyright`)を
直接読んだ結果です。要約記事ではありません。

| 項目 | 実測値 |
| --- | --- |
| ライセンス | **SIL Open Font License 1.1**(`License: SIL-1.1`) |
| 著作権表示 | `Copyright: 2010-2012, Google Corporation` |
| **Reserved Font Name** | **宣言されていません** |

OFL 1.1 はアプリケーションへの同梱・再配布を明示的に許可します。条件は
**著作権表示とライセンス全文を同梱すること**、および**フォント自体を別のライセンスで
再配布しないこと**です。

> **Reserved Font Name が宣言されていない点が重要です。** OFL では、サブセット化は
> 「Modified Version」の作成にあたり、RFN が宣言されている場合はその名前を使えません。
> Noto CJK の copyright には RFN 宣言が無いため(`with Reserved Font Name` 句が存在しない)、
> **サブセット化しても Noto の名前を保持できます。** §3 の軽量化案はこれに依拠します。

### サイズ — システム全体は 89 MiB、必要なのは 1 ウェイトだけ

| ファイル | サイズ |
| --- | --- |
| `NotoSansCJK-Bold.ttc` | 19.12 MiB |
| `NotoSansCJK-Regular.ttc` | **18.58 MiB** |
| `fonts-noto-cjk` 全体(4 ファイル) | **89.0 MiB** |

`NotoSansCJK-Regular.ttc` 1 本で ja / zh-CN / zh-TW / ko をすべて賄えます
(`fc-match sans:lang=ja` と `sans:lang=zh-cn` の両方がこのファイルに解決)。
**約 100 MB のフォント パッケージ全体を同梱する理由はありません。**

---

## 2. AppImage — 実測

### 構成と依存関係

AppDir を構築し、**実際のデスクトップ セッション(KDE Plasma X11)で起動**しました。

| 項目 | 実測値 |
| --- | --- |
| AppDir(フォント無し) | 110 MB |
| AppDir(+ CJK 1 ウェイト) | 129 MB |
| **squashfs / zstd(フォント無し)** | **37.6 MiB** |
| **squashfs / zstd(+ CJK 1 ウェイト)** | **51.7 MiB** |
| **フォントによる増加** | **+14.1 MiB(圧縮後)** |
| ホストに要求する共有ライブラリ | `libc` `libstdc++` `libm` `libgcc_s` `libdl` `libpthread` のみ |
| **解決できない依存** | **0 件**(`ldd` に `not found` なし) |

自己完結(self-contained)ビルドなので .NET ランタイムの導入は不要です。
Skia / HarfBuzz のネイティブ ライブラリも同梱されています。

### 起動結果

```
./AppDir/AppRun --selftest <dir>      (KDE Plasma X11 セッション内)
exit=0
self-test on Linux (KDE, X11)
RESULT: PASS   pass=11 skipped=1 unsupported=0
```

実 X11 ウィンドウ(ハンドル `440000F`)が描画され、ja / zh-CN の文字列も解決しました。

> **単一ファイル化(`.AppImage`)は未完了です。** AppDir と squashfs までは実測しましたが、
> ランタイム ヘッダーを連結する `appimagetool` は Ubuntu アーカイブに存在せず、
> GitHub リリースからのバイナリ取得が本セッションの実行ポリシーで拒否されたためです。
> 残るのは squashfs に約 1〜3 MB のランタイム スタブを連結する工程のみで、
> **サイズ・依存関係・起動可否という評価対象はすべて測定済み**です。

### 前提条件(本機で確認済み)

`/dev/fuse` 有り / `libfuse2t64` 2.9.9 導入済み / `fusermount`・`fusermount3` 有り /
`mksquashfs` 有り。AppImage の実行に必要なものは揃っています。

---

## 3. クリーン環境での表示確認

「CJK フォントが入っていないマシン」を fontconfig で再現して比較しました
(`FONTCONFIG_FILE` で可視ディレクトリを限定)。

| 条件 | ja フェイス数 | `fc-match sans:lang=ja` |
| --- | --- | --- |
| CJK フォント無し(= 素のクリーン環境) | **0** | `DejaVuSans.ttf`(CJK 字形を持たない → **豆腐**) |
| 同梱フォントのみ可視(= AppImage の実行時) | **10** | `AppDir/usr/share/fonts/NotoSansCJK-Regular.ttc` |

同梱フォントのみの状態でアプリを起動し、`exit=0`、セルフテスト項目 8 が PASS
(`ja="状態を更新"` / `zh-CN="刷新状态"`)となることを確認しました。

**フォールバックの仕組み:** `AppRun` が `XDG_DATA_DIRS` の先頭に `AppDir/usr/share` を
差し込みます。fontconfig はこのパスの `fonts/` を走査するため、**アプリ側のコード変更は不要**で、
システムに CJK フォントがある場合はそちらが優先されます(同梱分は純粋な保険)。

---

## 4. 推奨

| 案 | 圧縮後の増加 | 評価 |
| --- | --- | --- |
| 何も同梱しない | 0 | **不可**。CJK フォントの無い環境で日本語・中国語が豆腐になります |
| `fonts-noto-cjk` 全体(89 MiB) | 約 +60 MiB 超(未測定) | **不可**。Owner の指示どおり、安易な全体同梱は避けます |
| **`NotoSansCJK-Regular.ttc` 1 本(18.58 MiB)** | **+14.1 MiB** | **推奨**。ja/zh/ko を 1 ファイルで賄え、実測で豆腐が解消 |
| サブセット化(JIS 第 1・2 水準 + 常用簡体字など) | 未測定 | 将来の最適化候補。RFN が無いので名前を保持したまま可能(§1) |

**推奨は 1 ウェイト同梱(+14.1 MiB)です。** 37.6 MiB → 51.7 MiB になりますが、
多言語対応を謳う以上、CJK が表示できない配布物は成立しません。
サブセット化はさらに削れる見込みですが、**未測定のため今回は提案に留めます。**

ライセンス順守として、配布物に以下を同梱してください。

- `OFL.txt`(SIL Open Font License 1.1 全文)
- `Copyright 2010-2012 Google Corporation` の著作権表示
