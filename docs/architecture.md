# アーキテクチャ

BrowserFlip の構成と主要な処理フローを説明します。対象バージョンは 0.1.0(MVP、開発中)です。

- 要件: [requirements.md](requirements.md) / テスト: [testing.md](testing.md) / 関連 OSS 調査: [oss-research.md](oss-research.md)
- 参考: [Launch the Default Apps settings page (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-default-apps-settings)

## 1. 設計方針

| 方針 | 内容 |
| --- | --- |
| Windows の確認を尊重 | 既定ブラウザーは Windows の設定画面でユーザー自身が確定する。BrowserFlip は設定画面を開き、結果を観察するだけ。 |
| 読み取り専用 | レジストリは `writable: false` でのみ開く。`UserChoice` / `UserChoiceLatest` への書き込み、ハッシュ計算・偽造、DISM の既定関連付け XML は一切使わない。 |
| 管理者権限不要 | 昇格を要求しない(`asInvoker`)。 |
| 起動する URI の制限 | `ShellUriLauncher` は `ms-settings:` スキーム以外を例外で拒否する。 |
| 依存ゼロ | 本体プロジェクトはサードパーティ パッケージに依存しない(テスト用パッケージのみ `Directory.Packages.props` で集中管理)。 |
| テスト容易性 | OS への依存はすべて `Platform` 層のインターフェイス越しに扱い、単体テストではフェイクに差し替える。 |

## 2. レイヤー構成

| プロジェクト | ターゲット | 役割 |
| --- | --- | --- |
| `src/BrowserFlip.App` | `net10.0-windows` (WPF) | View / ViewModel(MVVM)、テーマ、DPI、多言語リソース |
| `src/BrowserFlip.Core` | `net10.0-windows` | 検出・既定状態・設定起動・ポリシー判定のロジック、および Windows 実装 |
| `tests/BrowserFlip.Core.Tests` | xUnit | Core の単体テスト(`InternalsVisibleTo` で internal も検証) |
| `tests/BrowserFlip.App.Tests` | xUnit (WPF) | ViewModel・多言語・設定ファイルの単体テスト(Core のフェイクを共有) |

```mermaid
flowchart TB
    subgraph App["BrowserFlip.App (WPF / MVVM)"]
        V["Views (MainWindow, BrowserCard)"]
        VM["ViewModels (MainViewModel, BrowserCardViewModel)"]
        RES["Strings.resx / Strings.ja.resx / Strings.zh-CN.resx"]
        V --> VM
        VM --> RES
    end

    subgraph Core["BrowserFlip.Core"]
        subgraph Services["Core services"]
            BD["Detection: BrowserDetector"]
            DBS["Defaults: DefaultBrowserService"]
            CT["Defaults: ChangeTracker"]
            SL["Settings: SettingsLauncher"]
            PI["Settings: PolicyInspector"]
        end
        subgraph Platform["Platform abstractions"]
            IRR["IRegistryReader"]
            IFS["IFileSystem"]
            IIS["IIndirectStringResolver"]
            IAQ["IAssociationQuery"]
            IUL["IUriLauncher"]
            IOS["IOsInfo"]
        end
        subgraph Impl["Windows implementations"]
            WRR["WindowsRegistryReader"]
            PFS["PhysicalFileSystem"]
            SIS["ShellIndirectStringResolver"]
            SAQ["ShellAssociationQuery"]
            SUL["ShellUriLauncher"]
            EOI["EnvironmentOsInfo"]
        end
    end

    subgraph Win["Windows"]
        REG[("Registry (HKCU / HKLM 64/32)")]
        SHL["shlwapi.dll: AssocQueryStringW, SHLoadIndirectString"]
        SET["Windows Settings (ms-settings:)"]
    end

    VM --> BD
    VM --> DBS
    VM --> CT
    VM --> SL
    BD --> IRR
    BD --> IFS
    BD --> IIS
    DBS --> IAQ
    SL --> IUL
    SL --> IOS
    SL --> PI
    PI --> IRR
    IRR -.-> WRR
    IFS -.-> PFS
    IIS -.-> SIS
    IAQ -.-> SAQ
    IUL -.-> SUL
    IOS -.-> EOI
    EOI -->|"UBR"| IRR
    WRR -->|"read-only"| REG
    SAQ --> SHL
    SIS --> SHL
    SUL -->|"ShellExecute"| SET
```


### 主要な型(Core)

| 名前空間 | 型 | 概要 |
| --- | --- | --- |
| `Detection` | `BrowserDetector` | 登録済みブラウザーを列挙し `BrowserInfo` を返す |
| `Detection` | `BrowserInfo` / `BrowserHealth` / `RegistrationScope` | ブラウザー情報。`Health` は `Ok` / `ExecutableMissing` / `NotInRegisteredApplications` |
| `Detection` | `RegistryValueParser` | `shell\open\command`・`DefaultIcon` 文字列の解析、キー パスの正規化 |
| `Defaults` | `DefaultBrowserService` | `http` / `https` の現在のハンドラーを取得し、検出済みブラウザーに対応付ける |
| `Defaults` | `DefaultBrowserState` / `DefaultStatus` / `BrowserDefaultRole` | 状態スナップショット。`Status` は `None` / `Consistent` / `Mismatch` / `Unrecognized` |
| `Defaults` | `ChangeTracker` / `ChangeOutcome` | 設定画面での変更結果を判定する |
| `Settings` | `SettingsLauncher` / `LaunchResult` / `LaunchKind` | 設定画面を開く。ディープリンク → トップページの順にフォールバック |
| `Settings` | `PolicyInspector` / `PolicyStatus` | グループ ポリシーの読み取り |

## 3. ブラウザー検出フロー

検索順は HKCU(既定ビュー)→ HKLM 64 ビット → HKLM 32 ビット。同じキー名が複数にある場合は先に見つかったもの(ユーザー単位)が優先されます。

```mermaid
flowchart TD
    A["Detect()"] --> B["Load RegisteredApplications (HKCU, HKLM64, HKLM32)"]
    B --> C{"Next root/view in search order"}
    C -->|"none left"| Z["Sort by DisplayName and return"]
    C --> D["Enumerate subkeys of Software\Clients\StartMenuInternet"]
    D --> E{"Id already found?"}
    E -->|"yes"| D
    E -->|"no"| F{"Capabilities\URLAssociations has http or https?"}
    F -->|"no"| D
    F -->|"yes"| G["Parse exe from shell\open\command"]
    G --> H["Parse DefaultIcon (path, index)"]
    H --> I["Display name: default value, then ApplicationName, then LocalizedString, resolve @-strings, else Id"]
    I --> J["Match RegisteredApplications by Capabilities path (same scope first), then by name"]
    J --> K{"exe exists?"}
    K -->|"no"| K1["Health = ExecutableMissing"]
    K -->|"yes"| L{"Registered app found?"}
    L -->|"no"| L1["Health = NotInRegisteredApplications"]
    L -->|"yes"| L2["Health = Ok"]
    K1 --> M["Add BrowserInfo"]
    L1 --> M
    L2 --> M
    M --> D
    D -->|"done"| C
```

- `http` / `https` の URL 関連付けを宣言しないキー(例: Windows 11 の旧 `IEXPLORE.EXE` 登録)は除外します。
- 実行ファイル パスはレジストリから得るのみで、ハードコードはしません。
- MVP では `StartMenuInternet` に登録されないパッケージ版(MSIX / Microsoft Store)ブラウザー、およびそれを対象とする `registeredAUMID=` ディープリンクには対応していません(Phase 2 で予定)。

## 4. Windows 設定への遷移フロー

```mermaid
flowchart TD
    A["User clicks a browser card"] --> B{"Health = ExecutableMissing?"}
    B -->|"yes"| B1["LaunchKind.BrowserUnavailable (nothing launched)"]
    B -->|"no"| C["PolicyInspector.Inspect()"]
    C --> D{"NoControlPanel, or SettingsPageVisibility hides defaultapps?"}
    D -->|"yes"| D1["LaunchKind.BlockedByPolicy (message shown)"]
    D -->|"no"| E{"SupportsAppDeepLink(build, UBR) and RegisteredApplicationName set?"}
    E -->|"yes"| F["ms-settings:defaultapps?registeredAppUser=NAME or registeredAppMachine=NAME"]
    F --> G{"Launch succeeded?"}
    G -->|"yes"| G1["LaunchKind.OpenedAppPage"]
    G -->|"no"| H
    E -->|"no"| H["ms-settings:defaultapps"]
    H --> I{"Launch succeeded?"}
    I -->|"yes"| I1["LaunchKind.OpenedTopPage"]
    I -->|"no"| I2["LaunchKind.Failed (error message)"]
    G1 --> J["User confirms with 'Set default' in Settings"]
    I1 --> J
```

- `SupportsAppDeepLink(build, ubr)` は Microsoft Learn の記載に従い、Windows 11 21H2 + 2023-04 累積更新(10.0.22000.1817 以上)、22H2 + 2023-04 更新(10.0.22621.1555 以上)、23H2 以降で `true`。Windows 10 と、それより古い Windows 11 ビルドは `ms-settings:defaultapps` を開く。UBR は `EnvironmentOsInfo` が `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion` の `UBR`(DWORD)から読み取る(取得できなければ 0)。
- `registeredAppUser` / `registeredAppMachine` は `RegisteredApplications` が見つかったハイブ(`RegisteredApplicationScope`)で決まります。値は `Uri.EscapeDataString` でエスケープします。
- ディープリンクのパラメーターを解釈しないビルドでも設定は「既定のアプリ」トップページを開くため、安全に劣化します。
- `DefaultAssociationsConfiguration`(HKLM)が設定されている場合は遷移を止めず、「サインイン時に元に戻る可能性がある」旨の警告を App 層で表示します(`PolicyStatus.AssociationsManaged`)。

## 5. 状態確認と変更監視

既定の取得は `AssocQueryStringW(ASSOCF_IS_PROTOCOL, ASSOCSTR_PROGID)` で行います(`UserChoice` / `UserChoiceLatest` の格納形式に依存しない、ドキュメント化された読み取り方法)。取得タイミングは起動時、設定を開く前、待機中のポーリング(約 1.5 秒間隔、最大 3 分)、ウィンドウ再アクティブ化時です。ポーリング間隔とタイムアウトは App 層の責務で、`ChangeTracker` 自体は時間を持ちません。

```mermaid
sequenceDiagram
    actor U as User
    participant VM as MainViewModel
    participant DBS as DefaultBrowserService
    participant SL as SettingsLauncher
    participant CT as ChangeTracker
    participant WS as Windows Settings

    VM->>DBS: GetState(browsers) as "before"
    U->>VM: Click card (target)
    VM->>SL: Open(target)
    SL->>WS: ms-settings:defaultapps...
    VM->>CT: new ChangeTracker(target, before)
    loop Every ~1.5 s, up to 3 min
        VM->>DBS: GetState(browsers)
        VM->>CT: Evaluate(current, userReturned=false)
        CT-->>VM: Pending or AppliedToTarget
    end
    U->>WS: Click "Set default" (or cancel)
    U->>VM: Return to BrowserFlip (window activated)
    VM->>DBS: GetState(browsers)
    VM->>CT: Evaluate(current, userReturned=true)
    CT-->>VM: Outcome
    Note over VM: AppliedToTarget: success, badge moves
    Note over VM: PartiallyApplied: only http or https changed
    Note over VM: ChangedToOther: default changed to another app
    Note over VM: NoChange: info message (cancelled)
```

`ChangeTracker.Evaluate` の判定:

```mermaid
flowchart TD
    A["Evaluate(current, userReturned)"] --> Z{"ProgIDs unchanged from 'before' and target was not already Both?"}
    Z -->|"yes"| F
    Z -->|"no"| B{"Target role in current state"}
    B -->|"Both"| R1["AppliedToTarget"]
    B -->|"HttpOnly or HttpsOnly"| C{"userReturned?"}
    C -->|"yes"| R2["PartiallyApplied"]
    C -->|"no"| P["Pending"]
    B -->|"NotDefault"| D{"ProgIDs differ from 'before'?"}
    D -->|"yes"| E{"userReturned?"}
    E -->|"yes"| R3["ChangedToOther"]
    E -->|"no"| P
    D -->|"no"| F{"userReturned?"}
    F -->|"yes"| R4["NoChange"]
    F -->|"no"| P
```

- `http` と `https` は Windows 側で一瞬ずれて更新されることがあるため、片方のみの状態はユーザーが戻るまで `Pending` とします。
- `userReturned` は「ウィンドウが再アクティブ化された」または「待機がタイムアウトした」ときに `true` を渡します。
- 最初の分岐により、元から片方だけ既定だったブラウザーを選んで何も変更しなかった場合は `PartiallyApplied` ではなく `NoChange` になります。
- 再アクティブ化時に片方のみ反映済みだった場合、`MainViewModel` は約 3 秒の猶予を置いてから `PartiallyApplied` を報告します(もう片方の書き込み待ち)。

### 既定状態(`DefaultStatus`)の判定

| 条件 | Status |
| --- | --- |
| `http` / `https` ともに未設定 | `None` |
| 片方のみ設定 | `Mismatch` |
| 両方が同じ検出済みブラウザー | `Consistent` |
| 両方が別の検出済みブラウザー、または片方のみ検出済み | `Mismatch` |
| 両方未検出で ProgID が同じ | `Unrecognized` |
| 両方未検出で ProgID が異なる | `Mismatch` |

ProgID とブラウザーの対応付けは、(1) 同じプロトコルの ProgID 一致、(2) いずれかのプロトコルで宣言された ProgID 一致、(3) 実行ファイル パス一致(更新で ProgID が変わった場合)の順で行います。

## 6. App 層の設計

- MVVM。ViewModel は Core のサービスのみを使い、Win32 を直接呼ばない(アイコン抽出 `IconLoader` とタイトルバー配色 `ThemeManager` のみ App 層の Services で P/Invoke を使用)。
- テーマ: Windows のアプリ テーマ(ライト/ダーク)に追従。ハイ コントラスト時はシステム カラーを使用。
- 高 DPI: アプリケーション マニフェストで Per-Monitor V2 を宣言。
- 多言語: `Strings.resx`(英語、ニュートラル)/ `Strings.ja.resx` / `Strings.zh-CN.resx`。ユーザーの Windows 表示言語から自動選択し、実行中に切り替え可能。言語に応じて `xml:lang` とフォント(ja: Yu Gothic UI / zh-CN: Microsoft YaHei UI)を切り替え。
- アクセシビリティ: カードはキーボードで操作可能(Tab / Enter / Space)、`AutomationProperties.Name` を設定。
