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
│       ├── release.yml            ビルド・テスト・配布物の作成とリリース
│       └── report-build-status.yml  push のビルド失敗時に課題を作成
├── docs/
│   ├── architecture.md            英語版
│   └── architecture.ja.md         この文書
├── src/
│   ├── WindowsSimpleTaskTabBar.Core/           画面に依存しない処理
│   │   ├── Grouping/
│   │   │   └── TabGrouping.cs     どのタブがどのグループかと、行の並び順
│   │   ├── Layout/
│   │   │   ├── BarMetrics.cs      バーの高さとDPIから決まる描画寸法
│   │   │   └── TabStrip.cs        タブ幅・あふれ・スクロールの計算
│   │   └── Settings/
│   │       ├── AppGroup.cs        利用者が作った1つのグループ
│   │       └── AppSettings.cs     設定項目と既定値
│   └── WindowsSimpleTaskTabBar/                アプリ本体
│       ├── Program.cs             起動処理
│       ├── Interop/
│       │   └── NativeMethods.cs   Windows API の呼び出し定義
│       ├── Services/
│       │   ├── ProcessInfoCache.cs  ウィンドウごとの実行ファイルの記憶
│       │   ├── SettingsStore.cs   設定ファイルの読み書き
│       │   └── WindowService.cs   ウィンドウの列挙・前面化・終了
│       └── UI/
│           ├── MainForm.cs        画面本体（AppBar登録・描画・操作）
│           └── SettingsForm.cs    設定画面
└── tests/
    └── WindowsSimpleTaskTabBar.Tests/          単体テスト
        ├── AppSettingsTests.cs
        ├── BarMetricsTests.cs
        ├── TabGroupingTests.cs
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
   │             ──→  Core/Grouping/（どのタブがどのグループか）
   ↓
Services/WindowService.cs（ウィンドウ操作）
   ↓
Interop/NativeMethods.cs（Windows API）
```

上の層から下の層だけを呼びます。逆向きの呼び出しはしません。
`NativeMethods` の呼び出しは `Interop` に閉じ込め、他の場所には書かない方針です。

## アプリ単位で行をまとめる仕組み

`Core/Grouping/TabGrouping.cs` は、画面にもWindows APIにも依存しない2つの判断を担います。
どのタブがどのグループに属するかと、行をどの順で描くかです。バーはタブごとのグループ名を渡し、
並び順を受け取ります。この規則は単体テストで確かめられます。

`Arrange` は、すでに並べ替えたあとの行を渡されたら、そのまま返さなければなりません。
`RefreshTabs` は毎秒4回動くため、2回目で動いてしまうものは3回目でも動き、行が落ち着かなく
なるからです。グループは最初のウィンドウの位置に置き、グループ内の順序は元のまま保つことで、
これが成り立ちます。

並べ替えるのは `_tabs` ではなく `_order` です。`_order` は次の更新をまたいで残る表示順であり、
ドラッグは同じ添字で両方のリストを動かします。片方だけを並べ替えると、2つの内容が食い違います。
そのため、グループ化を無効に戻しても、タブはグループ化後の位置に残ります。開いた順はどこにも
記録していないためです。

**ドラッグは、グループの中ではタブ1枚を、グループの外へ出るとグループごと動かします。**
どちらになるかは `TabGrouping.PlanDrag` が決め、`TabStrip.MoveRange` が適用します。適用先は
内容を一致させる必要がある3つ、`_order`、`_tabs`、そして次のドラッグ判定が読むグループ名の
並びです。

タブ1枚だけをグループの外へ出すことはできません。グループ分けは次の更新で計算し直すため、
外へ出たことを記録する場所がなく、250ミリ秒以内に元へ戻るからです。利用者がたった今行った
操作を勝手に元へ戻す動きは、そもそも動かせない動きより分かりにくくなります。

グループごとであれば、この問題は起きません。理由は `Arrange` が従っている規則そのものです。
`Arrange` はグループを「最初のウィンドウがある位置」の順に並べます。したがって、グループの
タブをまとめて塊で動かした結果は、`Arrange` がそのまま返す並びと一致します。何度計算しても
変わらないため、移動が保たれます。テストの `ArrangingAfterAGroupMoveChangesNothing` が
これを守ります。ここが崩れると、動かしたグループが元の位置へ跳ね返ります。

ウィンドウが1つだけのアプリと、実行ファイルを読めなかったウィンドウは、要素1個の塊として
扱われるので、単体で移動します。

**レイアウトの計算には一切手を入れていません。** `TabStrip.Measure` はすべてのタブに同じ幅と
同じ間隔を与え、`DropIndex`、`ScrollToShow`、当たり判定はいずれも同じ刻みを前提にしています。
グループの境目だけ間隔を広げると、これらをまとめて書き換えることになり、「描かれている位置」と
「落ちる位置」が一致するという前提も崩れます。そこでグループは、タブ上端の色帯で示します。
同じグループのタブの間では色帯を隙間ごしにつなげ、グループ全体が1本の帯に見えるようにします。
グループの境目には、その同じ隙間の中に区切り線を引きます。もっと広い間隔が必要になったときは、
測り方をもう1つ用意し、刻みを読むすべての場所をそちらへ移す必要があります。それは別の変更です。

ウィンドウが1つだけのグループには色を付けません。色帯は「これらは同じまとまりです」と伝える
ものであり、1つしかないタブには伝えることがありません。同じアプリが2つ以上ない行のすべてに
色を付ければ、バー全体が色付きになるだけで、何も伝わりません。

## ウィンドウの実行ファイルを調べる方法

`WindowService.GetExecutablePath` は `PROCESS_QUERY_LIMITED_INFORMATION` でプロセスを開き、
`QueryFullProcessImageNameW` で問い合わせます。`Process.MainModule.FileName` のほうが短く
書けますが、こちらは `PROCESS_VM_READ` を必要とします。この権限は、管理者権限のプロセスや
ビット数の異なるプロセスに対して拒否され、しかも結果ではなく例外が返ります。限定的な権限は
どちらの場合も許可されるため、完全には操作できない管理者権限のウィンドウでも、正しいグループに
入ります。

電卓・設定・フォトなどのパッケージアプリは、`ApplicationFrameHost.exe` が持つ
`ApplicationFrameWindow` の中に描かれます。そのまま問い合わせるとすべて同じ実行ファイルを
返し、1つのグループにまとまってしまうため、枠の子ウィンドウを調べます。

`ProcessInfoCache` が結果を記憶します。失敗した結果も記憶します。`RefreshTabs` は毎秒4回動く
ので、問い合わせを許可されないウィンドウに毎回聞き直すことになるからです。有効期限はありません。
ウィンドウの持ち主のプロセスは途中で変わらないためです。エントリは、アイコンのキャッシュと同じ
「今あるウィンドウの一覧」を使って捨てます。そのため、Windows が同じハンドルを再利用しても、
古い内容が返ることはありません。

グループ分けの判定にはフルパスではなくファイル名を使います。人がアプリを見分けるのはファイル名
だからです。同じプログラムを別のフォルダーに2つ入れても、見ている人にとっては1つのアプリです。
その代わり、どちらも `app.exe` という名前の無関係な2つのプログラムは、1つとして扱われます。

## 資源の持ち主と、漏れの見つけ方

このバーはログインしている間ずっと動きます。解放し忘れた資源は、その回のセッションが終わるまで
戻ってきません。そのため、どのオブジェクトを誰が持つのかを決めてあります。

| オブジェクト | 持ち主 | 解放する場所 |
|---|---|---|
| ウィンドウのアイコン | `MainForm` の `_iconCache` | ウィンドウを閉じたとき、アイコンを取り直したとき、`ReleaseResources` |
| フォント、ツールチップ、2つのメニュー、通知領域アイコン、タイマー | `MainForm` | `ReleaseResources` |
| `ExtractIconExW` で得たアイコンハンドル | `MainForm` | `ReleaseResources` の `DestroyIcon` |
| `SetWinEventHook` のフック | `MainForm` の `_hooks` | `ReleaseResources` の `UnhookWinEvent` |
| AppBar の登録 | `MainForm` | `ReleaseResources` の `UnregisterAppBar` |
| `OpenProcess` で得たプロセスハンドル | `WindowService.GetExecutablePath` | 同じメソッドの `finally` の `CloseHandle` |
| 描画中の `Pen`、`SolidBrush`、`GraphicsPath` | 囲っている `using` | `using` を抜けるとき |
| `SettingsForm` の各コントロール | 追加先の `Controls` | フォーム自身の `Dispose` |

`ReleaseResources` は2か所から呼ばれ、実際の処理は最初の1回だけ行います。1つは
`OnFormClosing` で、バーを閉じた時点で画面の領域を返すためです。もう1つは `Dispose(bool)` で、
閉じずに破棄された場合でも登録を残さないためです。基底クラスの `Dispose` より先に動かします。
AppBar の登録解除には、まだ有効なウィンドウハンドルが必要だからです。

これを守るために、3つの解析ルールを `.editorconfig` で警告に設定しています。ビルドは警告を
エラーとして扱うため、違反があればビルドが失敗します。

- `CA1001`：破棄が必要なフィールドを持つのに、自身が破棄可能でない型
- `CA2000`：作ったまま破棄されないオブジェクト
- `CA2213`：`Dispose` が解放していないフィールド

`Directory.Build.props` の `EnableNETAnalyzers` は、これらを `net48` 側にも適用するための設定
です。この指定がないと、SDK は `net48` を解析しません。

Windows Forms のコンテナが持ち主になっているフィールド、たとえば `Controls` に追加した
コントロールやメニューに追加した項目には、持ち主を書いた `SuppressMessage` を付けています。
`CA2213` はこの種の所有関係を判別できず、二重の解放を求めてくるためです。`CA2000` に
`dispose_ownership_transfer_at_method_call` を設定しているのも同じ理由です。この設定がないと、
コレクションに追加したコントロールがすべて「破棄し忘れ」と報告されます。

解析はソースコードを読むだけなので、動かしている間に増え続ける状態までは分かりません。そこは人
が確認します。

1. バーを起動し、タスクマネージャーの「詳細」タブを開きます。
2. 「メモリ」「ハンドル」「USER オブジェクト」「GDI オブジェクト」の列を表示し、
   `WindowsSimpleTaskTabBar.exe` を探します。
3. ウィンドウの開閉、バーの高さの変更、タブの並べ替え、メニューの開閉を、30分以上続けます。
4. 4つの数値が上下しながら落ち着けば問題ありません。上がり続ける数値があれば、それが報告すべき
   兆候です。
