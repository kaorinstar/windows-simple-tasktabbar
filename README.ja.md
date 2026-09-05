# windows-simple-tasktabbar

[English version](README.md)

タスクバーの上に横一列の常駐バーを表示し、起動中のウィンドウを Chrome 風のタブとして並べる
Windows アプリです。

複数のウィンドウを1つの枠に取り込む（統合する）機能は**ありません**。表示と切り替えだけを
担当します。

## 目的

Windows は、前面にないアプリが自分を前面へ出すことを制限しています。この制限のため、
切り替えたいウィンドウが後ろに残り、タスクバーのボタンが点滅するだけで切り替わらないことが
あります。本アプリは3段階の前面化処理でこれを解決します。詳しくは
「[前面化を確実にする仕組み](#前面化を確実にする仕組み)」を参照してください。

## 動作

- **タブをクリック** — そのウィンドウを前面に出します。最小化されていれば元に戻します。
- **前面にあるタブをクリック** — 最小化します（タスクバーと同じ動きです）。
- **× をクリック、または中クリック** — そのウィンドウを閉じます。
- **バーの上で右クリック** — 再読み込みと終了のメニューを表示します。
- ウィンドウの増減、切り替え、タイトル変更を検知して自動更新します。保険として2秒ごとにも
  更新します。

## ダウンロード

[リリース](https://github.com/kaorinstar/windows-simple-tasktabbar/releases)に3種類を添付しています。

| 版 | 大きさ | 必要な前提 | 用途 |
|---|---|---|---|
| `WindowsSimpleTaskTabBar-net48.exe` | 約23KB | なし（Windows 10 バージョン1903以降、Windows 11） | 通常はこれを使います |
| `WindowsSimpleTaskTabBar-net8.exe` | 約171KB | .NET 8 Desktop Runtime（x64） | すでにランタイムがある環境向け |
| `WindowsSimpleTaskTabBar-standalone.exe` | 約69MB | なし（x64のみ） | 古いWindows向け |

net48版は .NET Framework 4.8 を使います。Windows 10 バージョン1903以降と Windows 11 には
標準で含まれているため、配布先での準備は不要です。設定ファイルも付属しない1ファイル構成です。

## 導入のしかた

インストーラーは不要です。

1. 実行ファイルを任意のフォルダーに置きます（例：`C:\Tools\WindowsSimpleTaskTabBar\`）。
2. ダブルクリックで起動します。

署名していないため、初回起動時に SmartScreen の警告が出ます。「詳細情報」から実行してください。

終了は、バーの上で右クリックして「Exit」を選びます。

Windows の起動時に自動で立ち上げる場合は、`Windows` + `R` キーで `shell:startup` を開き、
実行ファイルのショートカットを置いてください。

## ソースからビルドする

必要なものは .NET 8 SDK だけです。Linux や macOS でもビルドの検証ができます（アプリ自体の
動作は Windows のみです）。

```
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

配布用の実行ファイルは次のように作ります。

```
# A. 導入不要版
dotnet publish src/WindowsSimpleTaskTabBar/WindowsSimpleTaskTabBar.csproj -c Release -f net48 -o artifacts/net48

# B. 軽量版
dotnet publish src/WindowsSimpleTaskTabBar/WindowsSimpleTaskTabBar.csproj -c Release -f net8.0-windows -r win-x64 --self-contained false -p:PublishSingleFile=true -o artifacts/net8

# C. 単体版
dotnet publish src/WindowsSimpleTaskTabBar/WindowsSimpleTaskTabBar.csproj -c Release -f net8.0-windows -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o artifacts/standalone
```

## フォルダー構成

```
├── src/
│   ├── WindowsSimpleTaskTabBar.Core/    画面やAPIに依存しない処理
│   └── WindowsSimpleTaskTabBar/         アプリ本体
├── tests/
│   └── WindowsSimpleTaskTabBar.Tests/   単体テスト
├── docs/                                設計の説明
└── .github/workflows/                   自動ビルドの設定
```

詳しくは [docs/architecture.ja.md](docs/architecture.ja.md) を参照してください。

## 自動ビルド

`main` への push とプルリクエストのたびに、GitHub Actions が Windows 環境でビルドと
単体テストを実行します。警告もエラー扱いのため、警告が残っていると失敗します。

`v0.1.0` のようにタグを付けて push すると、リリースが作られ、上記3種類が添付されます。

## 前面化を確実にする仕組み

Windows は、前面にないアプリが自分を前面へ出すことを制限しています。このため
`SetForegroundWindow` を呼ぶだけでは失敗し、タスクバーのボタンが点滅するだけになります。

`Services/WindowService.cs` では、次の順に3段階で試みます。

1. そのまま `SetForegroundWindow` を実行します。
2. 失敗した場合、現在の前面ウィンドウおよび対象ウィンドウの入力スレッドに、自分のスレッドを
   一時的に関連付けてから前面化します（`AttachThreadInput`）。
3. それでも失敗した場合、Altキーの押下と解放を送って制限を解除してから前面化します。

またバー自体は、クリック時の前面化を許可しています（`WS_EX_NOACTIVATE` は付けていません）。
自分が前面にいる状態からの前面化は制限を受けないため、成功率が上がります。バーは細いため、
見た目上の影響はほとんどありません。

## 既知の制約

- **管理者権限で動くアプリは操作できません。** Windows の権限分離により、通常権限のプロセスは
  管理者権限のウィンドウを前面化も終了もできません。本アプリを管理者権限で起動すれば操作
  できますが、常時その状態で常駐することになります。
- **対応するのはプライマリモニターのみです。**
- **全画面表示のアプリの下に隠れます。** AppBar の通常の動作です。
- **タブの並べ替え（ドラッグ）は未実装です。**
- **タブが入りきらない分は表示されません。** 折り返しもスクロールもありません。

## 今後の予定

1. タブのドラッグによる並べ替え
2. タブが多いときの横スクロール、またはアプリ単位のグループ化
3. マルチモニター対応
4. マウスを乗せたときのウィンドウ内容のプレビュー
5. よく使うアプリのピン留め
6. スタートアップ登録機能

## 開発に参加する場合

課題の報告とプルリクエストを歓迎します。次の点にご注意ください。

- **コード、コメント、文書は英語で記述します。** `README.ja.md` と `docs/architecture.ja.md` は
  英語版の翻訳であり、内容を一致させます。
- `-warnaserror` を付けたビルドが通ることが条件です。
- `net48` 版を壊さないでください。ランタイムの導入なしに単一の実行ファイルで配れることは、
  本プロジェクトの必須要件です。

## ライセンス

MIT
