# R5 検討候補

R3/R4 の受入で挙がったが、**R3/R4 の範囲外として意図的に実施しなかった**項目。Owner の指示により、新規開発ラウンドを開始せずここに記録する。

いずれも「実装済み機能の欠陥」ではない。欠陥として扱って修正したものは [macos-menu-bar.md](macos-menu-bar.md) にある。

---

## 1. 製品 UI の全面再設計

プロトタイプの画面は能力モデルの検証用に作られたもので、製品の画面設計ではない。

| 現状 | 備考 |
| --- | --- |
| メイン ウィンドウに「Avalonia prototype」と表示 | 技術検証の対象を明示するためで、製品名ではない |
| 設定ファイルの絶対パスを常時表示 | 検証時に実際の保存先を確認するため |
| 起動回数(launch counter)を常時表示 | 設定の永続化が効いていることを目視するため |
| 言語選択欄が言語コード(`en` / `ja` / `zh-CN`)を表示 | 曖昧さは無いが、製品なら `English` / `日本語` / `中文` が妥当 |
| 設定が独立ウィンドウのみ(メイン画面内にタブ等が無い) | [現在の導線](../prototypes/avalonia/README.md#設定画面へのアクセス)を参照 |

## 2. アプリケーション メニューの製品化

macOS の画面左上のメニューは Avalonia の既定のままで、`About Avalonia` が出る。設定項目も無い。

**原因は上流の制限で、回避策が無いことを実測で確認済み**(`AppBuilder` が `Application` 生成前にメニューを書き出す。[macos-menu-bar.md §4 D6-b](macos-menu-bar.md) と [AvaloniaUI/Avalonia#22287](https://github.com/AvaloniaUI/Avalonia/issues/22287))。

R5 で検討しうる方向:

- 上流の修正([#22813](https://github.com/AvaloniaUI/Avalonia/pull/22813) 系)が入った Avalonia への更新を待つ
- `About BrowserFlip` / `設定…` / `BrowserFlip を終了` を製品として整えたメニューに差し替える
- 併せて `Application.Name`(現在 `BrowserFlip`)と `CFBundleName`(現在 `BrowserFlip Prototype`)の関係を決める

> R3/R4 では**呼び出し自体を行わない**ことにした。黙って無視される呼び出しを残すと「メニューバーがある」と偽ることになるため。メニューの内容はすべてメニューバー アイコンのメニューから到達できる。

## 3. デバッグ表示の整理

上記 1 の表のうち、設定ファイル パス・起動回数・「Avalonia prototype」表記は検証用の表示である。製品化の際は、出すか・どこに出すか(例:設定画面の「詳細」セクションのみ)を決める必要がある。

## 4. 言語選択欄の表示名

言語コードではなく各言語の自称(endonym)を出す。R3/R4 では**表示が古い言語のまま残る欠陥**だけを修正し、表示内容そのものは変更していない(「新規機能を追加しない」ため)。

---

## 記録しないこと

次は R5 候補ではなく、**現時点で確定している制約**である。別の場所に記録済み。

- CJK フォントの同梱(既知欠陥、[known-defect-font-bundling.md](known-defect-font-bundling.md))
- GNOME ネイティブ X11(`UNSUPPORTED_UPSTREAM`)
- 署名・公証(Apple Developer Program 加入保留)
- Linux x64(成果物なし)
- MAC-D6 の原因(未確定)
