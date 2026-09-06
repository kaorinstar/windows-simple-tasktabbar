# 構成の考え方

[English version](architecture.md)

## フォルダー構成

```
windows-simple-tasktabbar/
├── WindowsSimpleTaskTabBar.sln                 ソリューション（プロジェクトのまとめ）
├── Directory.Build.props          全プロジェクト共通の設定
├── global.json                    使用する .NET SDK の指定
├── .editorconfig                  書き方と改行コードの統一設定
├── .gitattributes                 Git上の改行コードの統一設定
├── .gitignore                     Gitの除外設定
├── LICENSE
├── README.md                      英語版
├── README.ja.md                   日本語版
├── version.md                     変更履歴。リリースの説明文もここから作ります
├── version.ja.md                  変更履歴の日本語版
├── .github/
│   └── workflows/
│       ├── build.yml              push とプルリクエストでのビルドとテスト
│       └── release.yml            ビルド・テスト・配布物の作成とリリース
├── docs/
│   ├── architecture.md            英語版
│   └── architecture.ja.md         この文書
├── src/
│   ├── WindowsSimpleTaskTabBar.Core/           画面に依存しない処理
│   │   ├── Layout/
│   │   │   ├── BarMetrics.cs      バーの高さとDPIから決まる描画寸法
│   │   │   └── TabStrip.cs        タブ幅・あふれ・スクロールの計算
│   │   └── Settings/
│   │       └── AppSettings.cs     設定項目と既定値
│   └── WindowsSimpleTaskTabBar/                アプリ本体
│       ├── Program.cs             起動処理
│       ├── Interop/
│       │   └── NativeMethods.cs   Windows API の呼び出し定義
│       ├── Services/
│       │   ├── SettingsStore.cs   設定ファイルの読み書き
│       │   └── WindowService.cs   ウィンドウの列挙・前面化・終了
│       └── UI/
│           ├── MainForm.cs        画面本体（AppBar登録・描画・操作）
│           └── SettingsForm.cs    設定画面
└── tests/
    └── WindowsSimpleTaskTabBar.Tests/          単体テスト
        ├── AppSettingsTests.cs
        ├── BarMetricsTests.cs
        └── TabStripTests.cs
```

## なぜ src と tests を分けるのか

.NET の一般的な構成に合わせています。ソースとテストが同じ階層に並ぶと、
プロジェクトが増えたときに見分けがつかなくなります。

## なぜ Core を分けるのか

`WindowsSimpleTaskTabBar.Core` には、画面やWindows APIに依存しない処理だけを置きます。
これにより次の2点が実現します。

1. **どのOS上でもテストできます。** Windows API に依存する処理はWindows上でしか動かないため、
   混ざっているとテストの実行環境が限られます。
2. **テストの対象が明確になります。** 計算やルールの判定は Core に置き、画面の描画とAPI呼び出しは
   アプリ本体に置く、という切り分けです。

対象フレームワークは `netstandard2.0` です。.NET 8 と .NET Framework 4.8 の両方から使えます。

なお、アプリ本体は Core を **DLLとして参照せず、ソースを直接取り込んでいます**。
DLL参照にすると配布ファイルが2つになり、「実行ファイル1つで配れる」という利点が失われるためです。
テストプロジェクトは Core をライブラリとして参照します。

## 2つのフレームワークを同時に作る仕組み

`src/WindowsSimpleTaskTabBar/WindowsSimpleTaskTabBar.csproj` の `TargetFrameworks` に2つ指定しています。

```xml
<TargetFrameworks>net8.0-windows;net48</TargetFrameworks>
```

1回のビルドで、次の2つが同時に作られます。ソースコードは共通です。

- `net48`：Windows に標準で入っている .NET Framework 4.8 向け。配布先の準備が不要です。
  **配布するのはこちらだけです。**
- `net8.0-windows`：.NET 8 向け。毎回ビルドとテストを行いますが配布はしません。同じソースを
  別のコンパイラで検査する目的と、将来 .NET Framework が使えなくなった場合の備えです。

.NET 8 を同梱した版も一時期配布していましたが、取りやめました。約69MBかつ x64専用で、
利点があるのはサポートが終了したWindowsに限られ、そこには .NET Framework 4.8 を後から
導入できるためです。

フレームワークごとの差は、`Program.cs` の `#if NETFRAMEWORK` で切り分けています。

## 層の関係

```
Program.cs
   ↓
UI/MainForm.cs  ──→  Core/Layout/（計算）
   ↓
Services/WindowService.cs（ウィンドウ操作）
   ↓
Interop/NativeMethods.cs（Windows API）
```

上の層から下の層だけを呼びます。逆向きの呼び出しはしません。
`NativeMethods` の呼び出しは `Interop` に閉じ込め、他の場所には書かない方針です。
