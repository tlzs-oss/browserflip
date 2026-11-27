# 既知の欠陥 — CJK フォント同梱時の SIGABRT

**状態:** 未解決 / 原因未確定。**本ラウンドではこれ以上の調査を行いません。**
**影響:** Linux 配布物に CJK フォントを同梱できません。システム フォント利用方式を暫定採用します。

> **原因は特定できていません。** 症状は fontconfig / FreeType / HarfBuzz / SkiaSharp / Avalonia が
> 関わる経路で発生しますが、**どの層の欠陥かは確定していません**。本文書は再現条件と
> 実測値の記録であり、特定の上流ライブラリの不具合と断定するものではありません。

---

## 1. 症状

バンドルした `NotoSansCJK-Regular.ttc` を fontconfig の探索パスに加えた状態でアプリを起動すると、
ウィンドウが表示される前に異常終了します。

```
free(): invalid pointer
Aborted (core dumped)      終了コード 134 (SIGABRT)
```

標準出力・ログには上記 1 行以外なにも出ません。セルフテストの出力ディレクトリは作成されません。

---

## 2. 再現条件

計測環境はすべて実機(下記 §5)。デスクトップ セッションは KDE Plasma X11。

```sh
# 再現する(最小)
mkdir -p /tmp/fonttest/fonts
cp /usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc /tmp/fonttest/fonts/
rm -rf ~/.cache/fontconfig
XDG_DATA_DIRS="/usr/share:/tmp/fonttest" ./BrowserFlipPrototype --selftest /tmp/out
#   -> free(): invalid pointer / exit 134

# 再現しない(同じコマンドでフォントを置かないだけ)
mkdir -p /tmp/fonttest2/fonts            # 空
rm -rf ~/.cache/fontconfig
XDG_DATA_DIRS="/usr/share:/tmp/fonttest2" ./BrowserFlipPrototype --selftest /tmp/out
#   -> exit 0
```

**システムの同一ファイルをそのまま使う分には起きません。** 追加の探索パスに同じフォントを
置いた時だけ発生します。

---

## 3. A/B 比較(実測)

| # | 条件 | 試行 | 結果 |
| --- | --- | --- | --- |
| 1 | 追加パスに `.ttc` のコピーを配置 | 2 | **異常終了 2/2** |
| 2 | **同じ追加パスを空にする**(1 の対照) | 2 | 正常終了 2/2 |
| 3 | フォント同梱なし・fontconfig キャッシュ cold | 5 | 正常終了 5/5 |
| 4 | フォント同梱なし・キャッシュ warm | 2 | 正常終了 2/2 |
| 5 | AppImage squashfs マウント上のフォントを参照(prepend) | 3 | 異常終了 3/3 |
| 6 | AppImage squashfs マウント上のフォントを参照(append) | 3 | 異常終了 3/3 |
| 7 | 同フォントを ext4 に退避して参照・キャッシュ cold | 2 | 異常終了 2/2 |
| 8 | 同上・キャッシュ warm | 2 | 正常終了 2/2 |
| 9 | 起動前に `fc-cache -f <dir>` を別プロセスで実行 | 4 | 異常終了 4/4 |
| 10 | システム CJK を隠し、バンドル フォントのみ可視 | 3 | 異常終了 3/3 |
| 11 | 同梱なし AppImage・全キャッシュ cold | 4 | **正常終了 4/4(11 PASS)** |

**決定的な対照は #1 対 #2** です。ディレクトリにフォントを置くか置かないかの違いだけで
結果が反転します。

squashfs 固有ではなく(#7 で ext4 でも再現)、キャッシュ生成タイミングだけでもなく
(#9 で事前生成しても再現)、重複フォントの衝突でもありません(#10 でシステム側を
隠しても再現)。

---

## 4. 成果物への影響(実測値)

| 版 | サイズ | 起動 |
| --- | --- | --- |
| フォント同梱あり | 53,611,104 バイト | **起動せず** |
| **フォント同梱なし(採用)** | **39,066,208 バイト** | **cold キャッシュで 4/4 成功、11 PASS** |

---

## 5. 関連する依存のバージョン

| 要素 | バージョン |
| --- | --- |
| OS | Ubuntu 26.04.1 LTS (Resolute Raccoon) aarch64 |
| カーネル | 7.0.0-44-generic |
| glibc | 2.43 |
| fontconfig | 2.17.1 (`libfontconfig1` 2.17.1-3ubuntu1) |
| FreeType | `libfreetype6` 2.14.2+dfsg-1ubuntu0.2 |
| HarfBuzz | `libharfbuzz0b` 12.3.2-2 |
| フォント パッケージ | `fonts-noto-cjk` 1:20240730+repack1-1build1 |
| 対象フォント | `NotoSansCJK-Regular.ttc` 19,484,784 バイト |
| 〃 sha256 | `b76b0433203017ca80401b2ee0dd69350349871c4b19d504c34dbdd80541690a` |
| アプリ | Avalonia 12.1.4.0 / .NET 10 / self-contained linux-arm64 |
| デスクトップ | KDE Plasma 6.6.7 (X11) / GNOME Shell 50.1 |

---

## 6. 調査できなかったこと

**バックトレースは取得できていません。** 本 QA VM は Apple Virtualization 上の ARM64 ゲストで、
gdb が `Unable to fetch SVE/SSVE vector length: Invalid argument` で起動できません。
原因層の特定にはこれが必要です。

---

## 7. 将来の対応候補(本ラウンドでは実施しない)

1. gdb が動く環境(x86_64 Linux 実機など)でバックトレースを取得する
2. `.ttc`(コレクション)ではなく**単一フェイスの `.otf`** で再現するか確認する
3. `Avalonia.Fonts.Inter` と同じ**アプリ内埋め込みフォント**方式を評価する
   (fontconfig を経由しないため、この経路自体を回避できる可能性がある)
4. 再現する最小の C# プログラムを作り、上流に報告できる形にする

フォント内蔵対応を進めるかどうかは **Owner の判断**です。
