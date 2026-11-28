# Windows 側への引継ぎ(R3/R4)

macOS 側の作業は収束した。本書は **Windows 側 CC が物理実機検証と統合をそのまま引き継げるようにするための引継ぎ文書**である。

前提の確定記録は [r3-r4-outcome.md](r3-r4-outcome.md) にある。本書はそこから Windows 側が行う作業だけを抜き出したもので、重複する数値は再掲しない。

---

## 1. 引継ぎ時点の状態

| 項目 | 状態 |
| --- | --- |
| `MAC_OWNER_ACCEPTANCE` | **PASS**(HEAD `e2b729a` で Owner が目視・操作確認) |
| `MAC_DEVELOPMENT` | **COMPLETED**(追加開発・追加修正は行わない) |
| `WINDOWS_PHYSICAL_ACCEPTANCE` | **DEFERRED** ← **本書の対象** |
| `FINAL_INTEGRATION` | **PENDING_WINDOWS_ACCEPTANCE_AND_REVIEW** |
| `RELEASE` | **NOT_AUTHORIZED** |

Windows については **CI(windows-latest)で実ウィンドウ描画と自己診断まで合格している**(`RESULT: PASS pass=11 pending=0 skipped=1 unsupported=1`)。**物理実機での目視受入は行われていない。** CI 合格を実機受入として報告してはならない。

出荷中の WPF 製品(Windows v0.3.0)は本ラウンドで**一切変更していない**。`src/` への差分は空、`BrowserFlip.App.Tests` 405 件は変更なしで合格している。

## 2. PR #10 / #13 の関係、統合順序、必要な Review

### 関係

```
main
 └─ PR #10  feat/unified-cross-platform-r2     (R2: Avalonia 技術プロトタイプ)   base = main
      └─ PR #13  feat/unified-cross-platform-r3r4  (R3/R4: 3 アダプター + 受入修正)  base = #10 の head
```

**PR #13 は PR #10 の上に積まれたスタック PR である。** #13 の base は `main` ではなく `feat/unified-cross-platform-r2`。

| PR | 内容 | base | 規模 | CI | Review |
| --- | --- | --- | --- | --- | --- |
| **#10** | R2 Avalonia 技術プロトタイプ | `main` | 48 ファイル / +3,729 / −0 | 全 pass | **`REVIEW_REQUIRED`**(`sxrsing` 指名、未実施) |
| **#13** | R3/R4 三プラットフォーム アダプター、能力モデル、実機受入修正 | `feat/unified-cross-platform-r2` | 88 ファイル / +13,392 / −221(受入時点) | **全 9 チェック pass** | **未実施**(`sxrsing` 指名) |

> #10 の CI は当時の構成で合格している。**Windows / Linux のプロトタイプ ジョブは #13 で追加されたため、#10 の CI 結果には含まれない。** 3 プラットフォームの CI 結果は #13 側で確認すること。

### 統合順序

1. **PR #10 を `main` へマージする。**
2. #10 のマージ後、GitHub は **PR #13 の base を自動的に `main` へ付け替える**(base ブランチが削除される設定の場合)。付け替わらない場合は手動で `main` に変更する。
3. #13 の base 付け替え後に **CI を再度通す**。base が変わると差分が変わるため、#10 の内容を含んだ状態での CI 結果が必要になる。
4. **PR #13 を `main` へマージする。**

#13 を #10 のブランチへ先にマージしてから #10 を `main` へ入れる順序でもよい。その場合 #10 の PR 差分が #13 の内容を含むことになるため、**#10 の Review をその拡大した差分に対して取り直す必要がある。** 上記 1→4 の順序を推奨する。

### 必要な Review

| | |
| --- | --- |
| 必要な承認数 | **1**(ブランチ保護設定 `required_approving_review_count: 1`) |
| 承認者 | **`sxrsing`**(両 PR で指名済み) |
| 自己承認 | **不可。** #13 の作者は `tlzskk` であり GitHub が自己承認を禁止する |
| 管理者バイパス | **禁止。** 使用していない。使用してはならない |

**両 PR が未承認のまま残っている。** `sxrsing` の承認なしにマージしてはならない。

### 統合時に消してはならないもの

- 未統合ブランチ `feat/unified-cross-platform-r2` および `feat/unified-cross-platform-r3r4`
- `~/workspace/30_test/browserflip-macos-d7/`(`.app` / DMG / クラッシュ証跡 / `.NET` 環境記録)
- `~/workspace/30_test/browserflip-macos-d9/`(アプリケーション メニューの受入証跡)

## 3. Windows 物理実機で検証すべき項目

**CI が確認していないのは「実機のユーザーから見た挙動」である。** 以下を物理実機で目視確認すること。未確認の項目を PASS として報告しないこと。

### 3.1 起動と基本表示

- [ ] アプリケーションが起動し、メイン ウィンドウが表示される
- [ ] ウィンドウ タイトルとアプリ名が `BrowserFlip` になっている(`Avalonia Application` ではない)
- [ ] インストール済みブラウザーが列挙される
- [ ] 現在の既定ブラウザーが正しく表示される
- [ ] ブラウザー アイコンが表示される(空白のプレースホルダーではない)

### 3.2 通知領域(タスクトレイ)

- [ ] 通知領域にアイコンが**実際に表示される**(D-Bus 登録成功やコード上の `SetIcons` 呼び出しを表示成功として報告しない)
- [ ] アイコンのクリックでメニューが開く
- [ ] メニュー項目:`BrowserFlip` / `状態を更新` / `フローティング ツールバーを表示` / `設定…` / 区切り / `BrowserFlip を終了`
- [ ] 各項目が実際に動作する

> macOS では MAC-D6 として「アイコンは見えるがクリックしてもメニューが開かない」が報告され、**原因未確定のまま再現しなくなっている**。Windows でも**アイコン表示とクリック動作を別項目として確認**すること。

### 3.3 設定画面(3 経路すべて)

- [ ] メイン ウィンドウの **「設定…」ボタン**(「状態を更新」の隣)から開く
- [ ] **通知領域メニュー**の `設定…` から開く
- [ ] **ウィンドウ メニュー**(`ファイル` / `表示` / `設定`)の `設定…` から開く
- [ ] 「常に手前に表示」が **ON の場合も OFF の場合も**、設定ウィンドウが前面に見える
- [ ] 設定ウィンドウが二重に開かない
- [ ] 閉じたあと再度開ける

> macOS では「オーナーが最前面で設定ウィンドウがそうでない」ために設定画面がオーナーの真下に開き、Owner が 2 回見つけられなかった。**ON / OFF 両方を確認すること。**

### 3.4 多言語(en / ja / zh-CN)

- [ ] 3 言語すべてで UI が切り替わる
- [ ] **実行中に切り替えて**、メイン画面・設定画面・通知領域メニュー・ウィンドウ メニューが**即座に**追随する
- [ ] 言語選択欄自体の表示(「システムに合わせる」)が現在の言語になっている
- [ ] 英語のまま残る項目がない

> macOS では、ラベルが生成時の資源参照だったために通知領域メニューが起動時の言語で固定され、日本語 UI に中国語の `设置…` が残っていた。

### 3.5 レイアウト(未確認事項)

- [ ] **フッターのボタン行で `設定…` のラベルが切れていない。** ボタン行は折り返さないため、実フォント幅が headless 測定を超えると切れる可能性がある。推測でレイアウトを変えていないので、**実画面での確認が必要**([r3-r4-outcome.md §7-8](r3-r4-outcome.md))

### 3.6 既定ブラウザーの変更と復元

- [ ] 既定ブラウザーを変更できる
- [ ] 元のブラウザーに復元できる
- [ ] 変更後に表示が正しく更新される

> **ユーザーの明確な承認なしに実機の既定ブラウザーを変更しないこと。** Windows では「設定を開く」方式であり、最終的な切り替えは Windows 設定アプリ側でユーザーが行う。**API 呼び出しの成功を既定ブラウザーの実切替成功として報告しないこと。**
> 自動検証からの実変更は `--switch-default <id>` を明示的に渡したときだけ実行される。CI は渡さない。

### 3.7 その他

- [ ] フローティング ツールバーの表示・位置保存
- [ ] 「常に手前に表示」が実際に効く
- [ ] 設定が再起動後も保持される
- [ ] 通常終了と再起動で異常が出ない
- [ ] 出荷中の WPF 製品(v0.3.0)が従来どおり動作する(**退行がないこと**)

## 4. Windows でのビルド・実行手順

リポジトリ全体の開発入口は [cross-platform-dev.md](cross-platform-dev.md)、プロトタイプ固有の手順は [../prototypes/avalonia/README.md](../prototypes/avalonia/README.md) にある。要点のみ:

```powershell
# プロトタイプ全体のビルド(Windows では net10.0 と net10.0-windows の両方)
cd prototypes\avalonia
dotnet build BrowserFlip.Prototype.slnx -c Release

# テスト(Windows アダプターのテストを含む)
dotnet test tests\BrowserFlip.Prototype.Tests\BrowserFlip.Prototype.Tests.csproj -c Release

# Windows 版を publish して起動
dotnet publish src\BrowserFlip.Prototype.Desktop\BrowserFlip.Prototype.Desktop.csproj `
  -c Release -f net10.0-windows -o publish-win --nologo
.\publish-win\BrowserFlipPrototype.exe
```

### 自己診断を実機で走らせる

```powershell
# ユーザー本人の設定ファイルを書き換えないこと。--settings-path で隔離する。
.\publish-win\BrowserFlipPrototype.exe --selftest C:\temp\selftest-out --settings-path C:\temp\settings.json
```

- `--settings-path` を渡すと `SelfTest.SettingsAreIsolated` が立ち、**永続化を伴う診断**(言語巡回など)もそこでだけ実行される。渡さない場合、それらの診断は実行されず理由が表示される。
- プロトタイプは `WinExe` なのでコンソールに何も出さない。`Start-Process -Wait` で終了を待ち、`selftest-out\capability-report.txt` を読むこと(CI の手順と同じ)。
- 終了コード:`0` = 全件合格、`2` = FAIL あり、`3` = PENDING あり。
- `capability-report.json` が機械可読の正。**`RESULT:` 行と JSON の両方に結果が入ることを前回ラウンドで確認している。**

### Windows で期待される自己診断の結果

```
RESULT: PASS   pass=11 pending=0 skipped=1 unsupported=1
```

- `skipped=1` はチェック 6(既定ブラウザーの実変更)。`--switch-default` を渡さない限り実行されない
- `unsupported=1` はチェック 14(macOS アプリケーション メニュー)。Windows にアプリケーション レベルのメニュー バーは無いため、**これは正しい結果である**

## 5. Windows 側で注意すべき実装上の点

| 点 | 内容 |
| --- | --- |
| ターゲット フレームワーク | Windows ホストではテスト/デスクトップ プロジェクトに `net10.0-windows` が追加される。`net10.0` だけをビルドすると Windows アダプターも Windows 合成ルートもコンパイルされない |
| アプリケーション メニュー | Windows ではウィンドウ メニュー(`ファイル` / `表示` / `設定`)。`Application` への `NativeMenu` 取り付けは **macOS 専用**で、Windows では意図的に行わない([macos-menu-bar.md §9](macos-menu-bar.md)) |
| 能力モデル | Windows は 4 能力すべて `Available`。`OsConfirmsDefaultBrowserChange` の扱いは [unified-architecture.md §4](unified-architecture.md) を参照 |
| スキップされるテスト | Windows CI では 31 件がプラットフォーム依存でスキップされる(macOS 専用テストなど)。これは正常 |
| 設定ファイルの場所 | 設定画面の下部に絶対パスが表示される。検証時はそれが**隔離ファイルを指していること**を確認する |

## 6. 禁止事項(Owner 指示)

- **管理者バイパス**
- **未承認の PR マージ**(`sxrsing` の承認が必要)
- **新バージョンの公開**(`RELEASE = NOT_AUTHORIZED`)
- **R5 開発の開始**([r5-candidates.md](r5-candidates.md) は記録のみ)
- **未統合ブランチおよび必要な成果物の削除**(§2 末尾)
- **未検証事項を PASS として報告すること**([r3-r4-outcome.md §7](r3-r4-outcome.md))
- **出荷中 WPF 製品の変更**および **Windows v0.3.0 の破壊**
- **ユーザーの明確な承認なしに実機の既定ブラウザーを変更すること**
- **ユーザー自身の設定ファイルを検証によって上書きすること**

## 7. 引継ぎ後の完了条件

1. Windows 物理実機で §3 を確認し、**確認できた項目と確認できなかった項目を分けて**報告する
2. 見つかった欠陥は修正するか、残存事項として記録する
3. `sxrsing` による PR #10 / #13 の Review を得る
4. §2 の順序で統合する
5. 統合後、`FINAL_INTEGRATION` の状態を Owner が判定する

リリースは `RELEASE = NOT_AUTHORIZED` のまま。**Owner の明示的な承認なしに公開しない。**
