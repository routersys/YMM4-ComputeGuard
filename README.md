# ComputeGuard for YMM4

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](#)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](#)

---

YMM4 のプラグインが、計算を CPU と GPU のどちらで行うかを決めるためのライブラリです。
GPU で計算した結果を、画面に出してよいかも判定します。
GPU の結果に異常が見つかると、その GPU では YMM4 を終了するまで CPU で計算します。
DLL ではなくソースとして各プラグインに取り込むため、プラグイン同士が互いの版に影響しません。状態もプラグインごとに分かれます。

---

## 目次

1. [概要](#概要)
2. [動作要件](#動作要件)
3. [導入方法](#導入方法)
4. [使い方](#使い方)
5. [取り込む側の前提](#取り込む側の前提)
6. [主な機能](#主な機能)
   - [1. ソースの取り込みによる配布](#1-ソースの取り込みによる配布)
   - [2. プラグインごとの状態](#2-プラグインごとの状態)
   - [3. 決定論の約束](#3-決定論の約束)
   - [4. デバイスの識別](#4-デバイスの識別)
   - [5. 経路の選択](#5-経路の選択)
   - [6. 健全性の状態と自己試験](#6-健全性の状態と自己試験)
   - [7. 異常の報告](#7-異常の報告)
   - [8. 検査と許容値](#8-検査と許容値)
7. [誤差のモデル](#誤差のモデル)
8. [検出できる範囲](#検出できる範囲)
9. [動作の上限値](#動作の上限値)
10. [制限事項](#制限事項)
11. [注意事項](#注意事項)
12. [動作の確認](#動作の確認)
13. [免責事項](#免責事項)
14. [ライセンス](#ライセンス)

---

## 概要

GPU の計算は、ドライバーやハードウェアの不具合で誤った値を返すことがあります。本ライブラリを取り込むプラグインは、同じ計算を CPU と GPU の両方で行えるように作ります。以下では、CPU で計算する道筋を CPU の経路、GPU で計算する道筋を GPU の経路と呼びます。

プラグインは、計算が要るたびに、どちらの経路で計算するかを本ライブラリに尋ねます。GPU の経路で計算したときは、結果を画面に出す前に、検査を本ライブラリに渡して判定を受けます。検査では、GPU の結果から求めた測定値を参照値と比べ、差が許容値に収まるかを見ます。参照値には、CPU で計算した値や、0 のように初めから分かっている値を使います。判定で不合格になったフレームは、CPU の経路で計算し直します。

本ライブラリが扱うのは、経路の選択と、GPU の結果の判定と、異常の報告の受け渡しの 3 つです。計算、測定値と参照値の算出、誤差の解析は、取り込んだプラグインが行います。本ライブラリは、その材料として、デバイスの識別、誤差のモデル、許容値の表し方、抜き取る位置の決め方を提供します。報告は送信せず、呼び出し側へ渡します。送信は、プラグインが [YMM4-Telemetry](https://github.com/routersys/YMM4-Telemetry) などに任せます。

プラグインのプロジェクトが `ComputeGuard.props` を読み込むと、`ComputeGuard` フォルダーのソースがプラグインのアセンブリに取り込まれます。DLL は増えず、ILRepack の対象にも加わりません。

---

## 動作要件

| 項目 | 要件 |
|---|---|
| 対象フレームワーク | .NET 10.0 |
| 対象プラットフォーム | Windows 10 バージョン 2004（ビルド 19041）以降 / Windows 11（64bit） |
| 参照するアセンブリ | Vortice.DXGI.dll、Vortice.DirectX.dll、SharpGen.Runtime.dll、SharpGen.Runtime.COM.dll |
| 実行環境 | YMM4 上で動作するプラグイン |

参照するアセンブリは YMM4 本体に同梱されています。ビルドでは `YMM4DirPath` を通じて参照します。

---

## 導入方法

サブモジュールとして追加します。

```
git submodule add https://github.com/routersys/YMM4-ComputeGuard.git YMM4-ComputeGuard
```

プラグインの csproj へ 1 行足します。パスはサブモジュールを置いた位置に合わせます。

```xml
<Import Project="..\YMM4-ComputeGuard\ComputeGuard.props" />
```

プラグインの csproj が、動作要件に挙げた 4 つのアセンブリを参照していることを確かめます。参照していないものは、次の形で足します。

```xml
<Reference Include="Vortice.DXGI">
  <HintPath>$(YMM4DirPath)Vortice.DXGI.dll</HintPath>
  <Private>false</Private>
</Reference>
```

---

## 使い方

以下の例では、YMM4-Telemetry も取り込んでいるものとします。

`ComputeGuardian` は、プラグインの中で 1 つだけ作り、`static` なメンバーに置きます。報告の受け渡し先は必ず指定します。

```csharp
using ComputeGuard;
using Telemetry;

internal static class SampleComputeGuard
{
    public static ComputeGuardian Guardian { get; } = new(new ComputeGuardianOptions
    {
        Report = TelemetryReporter.Report,
    });
}
```

`ComputeDevice` は、エフェクトを作るときに 1 回だけ読んで持っておきます。`devices` は、YMM4 がエフェクトに渡す `IGraphicsDevicesAndContext` です。UMA は、CPU と GPU が同じメモリを使う構成です。UMA かどうかは、YMM4 の Direct3D 11 のデバイスに問い合わせて渡します。この問い合わせには Vortice.Direct3D11.dll の参照が要ります。

```csharp
bool isUma = devices.D3D.Device.CheckFeatureOptions2().UnifiedMemoryArchitecture;
var device = ComputeDevice.FromAdapter(devices.DXGI.Adapter, isUma);
```

自己試験は、GPU の経路を初めて選ぶ前に、GPU が正しく計算できるかを確かめる処理です。自己試験の処理は、検査の一覧を返すデリゲート `Func<ComputeDevice, IReadOnlyList<ComputeCheck>>` として、プラグインが用意します。たとえば、決まった小さな入力を GPU で 2 回計算します。そして、2 回の結果でビットが違う値の個数と、1 回目の結果と CPU で計算した結果との差を検査にします。自己試験のデリゲートは 1 回だけ作ってフィールドに置き、毎回同じものを渡します。

```csharp
IReadOnlyList<ComputeCheck> RunSelfTest(ComputeDevice device)
{
    var first = ConvolveTestPatternOnGpu();
    var second = ConvolveTestPatternOnGpu();
    var reference = ConvolveTestPatternOnCpu();
    return
    [
        ComputeCheck.Count("determinism", CountDifferentBits(first, second)),
        new ComputeCheck("pattern", LargestDifference(first, reference), 0d, new ComputeTolerance(patternUlps, patternScale, 0)),
    ];
}
```

計算するフレームごとに、経路を尋ねます。作業量を表す `ComputeWorkload` には、計算する領域の幅と高さと、GPU の経路で要る GPU メモリのバイト数を入れます。GPU の経路では、検査を作って判定を受けます。GPU の経路で例外が起きたときは、失敗の種類とその例外を `Fail` に渡します。判定で不合格になったときと、例外が起きたときは、CPU の経路で計算し直します。

```csharp
var workload = new ComputeWorkload(width, height, gpuMemoryBytes);
var decision = SampleComputeGuard.Guardian.Select(device, workload, selfTest);
var accepted = false;
if (decision.Route == ComputeRoute.Gpu)
{
    try
    {
        ComputeCheck[] checks = RunOnGpu();
        accepted = SampleComputeGuard.Guardian.Judge(device, workload, checks);
    }
    catch (Exception exception)
    {
        SampleComputeGuard.Guardian.Fail(device, workload, Classify(exception), exception);
    }
}

if (!accepted)
    RunOnCpu();
```

検査は、たとえば次のように作ります。抜き取る位置は `ComputeSampling.Fill` で決め、その位置の値を CPU でも計算して参照値にします。`Fill` の最初の引数は、抜き取る位置を決めるキーです。キーには、入力から決まる値を渡します。

```csharp
Span<ComputeSample> samples = stackalloc ComputeSample[16];
ComputeSampling.Fill(sourceHash, width, height, samples);

checks[0] = ComputeCheck.Count("nonfinite", nonFiniteCount);
checks[1] = new ComputeCheck("sum", gpuSum, referenceSum, new ComputeTolerance(sumUlps, sumScale, 0));
for (var index = 0; index < samples.Length; index++)
    checks[2 + index] = new ComputeCheck("spot", gpuValues[index], cpuValues[index], new ComputeTolerance(spotUlps, spotScales[index], 0));
```

`Classify` が返す失敗の種類は、[異常の報告](#7-異常の報告)の表のうち、呼び出し側が `Fail` に渡す 3 つから選びます。例外の型との対応は、プラグインが使う GPU の API に合わせて決めます。

型はすべて `internal` であり、取り込み先のアセンブリの外からは見えません。

---

## 取り込む側の前提

判定が意味を持つには、取り込む側が次の前提を守る必要があります。

1. 経路を決める入力に、時刻、処理時間、GPU の負荷、その時点の空きメモリを使わない。
2. GPU の結果は、画面に出す前に必ず判定にかける。
3. GPU と CPU の計算は同じ順序で書き、浮動小数点数のアトミック操作を使わない。
4. GPU で丸めの起きる浮動小数点数の演算は、[誤差のモデル](#誤差のモデル)の最初の表で上限を導く対象とした演算に限る。三角関数などは CPU で計算して渡す。
5. GPU のシェーダーは、精度を緩める最適化を許さない指定でコンパイルする。シェーダーのコンパイラーである DXC では、`-Gis` を指定する。C# で書いた計算シェーダーを Direct3D 12 で動かすライブラリである [ComputeWeave](https://github.com/routersys/ComputeWeave) では、`CompileOptions.IeeeStrictness` を指定する。
6. CPU の計算は、スレッドの数によらず同じ結果を出す。
7. 0 でなく絶対値が $`2^{-126}`$ より小さい値を GPU へ渡さない。CPU の側で 0 にそろえてから渡し、CPU の計算にも同じ値を使う。
8. 許容値は誤差の解析から導き、実測の誤差に合わせて広げない。

許容値を実測で決めると、別の健全な GPU では誤差が許容値を超えることがあります。そうなると、その入力が来たときに判定で不合格になり、そのアダプターは YMM4 を終了するまで CPU に固定されます。

---

## 主な機能

### 1. ソースの取り込みによる配布

YMM4 の `PluginAssemblyLoader` は、`user\plugin` の下にあるすべての DLL を、既定の `AssemblyLoadContext` へ読み込みます。同じ ID のアセンブリは、最初に読まれた 1 つだけが使われます。そのため、共有の DLL を配ると、どの版が使われるかがディレクトリの列挙順で決まります。ソースとして取り込めば、各プラグインは自分のアセンブリの中で完結します。

### 2. プラグインごとの状態

YMM4-Telemetry は、送信数などの状態をプロセス全体で共有します。本ライブラリは、状態をプラグインの間で共有しません。

本ライブラリは、書き換わる `static` な値を持ちません。状態は `ComputeGuardian` のインスタンスが持ち、プラグインはそのインスタンスを 1 つだけ `static` なメンバーに置きます。そのため、状態はプラグインごとに分かれ、YMM4 を起動し直すと初めの状態に戻ります。あるプラグインが GPU の異常を見つけても、ほかのプラグインの判断は変わりません。

### 3. 決定論の約束

健全性の状態は、アダプターを GPU の計算に使ってよいかを表す値です。本ライブラリは、次の 2 つを守ります。

- 経路は、作業量、デバイス、健全性の状態の 3 つだけで決まります。デバイスについては、有無と、実行中に変わらない性質だけを使います。時刻、処理時間、GPU の負荷、その時点の空きメモリは使いません。
- 健全性の状態は、`Untested` から `Trusted` か `Pinned` へ、`Trusted` から `Pinned` へと一方向にだけ移り、戻りません。

健全性の状態は、[健全性の状態と自己試験](#6-健全性の状態と自己試験)で詳しく説明します。

GPU の負荷で経路を切り替えると、同じ入力でも実行のたびに経路が変わり、出力が変わります。そのため、経路を決める材料を上の 3 つに限っています。

取り込む側が[前提](#取り込む側の前提)を守れば、出力は入力と経路と環境で決まります。ここでの環境とは、GPU、ドライバーのバージョン、CPU、Windows、.NET のランタイムの組です。GPU の経路でも、三角関数などの値は CPU で計算して渡すので、CPU と Windows と .NET のランタイムが結果に関わります。したがって、同じ環境に同じ入力を与えれば、`Pinned` へ移らない限り、出力は 1 ビットも変わりません。`Pinned` へ移った後は、CPU の経路の出力になります。GPU の経路が誤差の上限を守っていれば、`Pinned` へ移る前と後の出力の差は、両方の経路の誤差の上限を足した値を超えません。

### 4. デバイスの識別

`ComputeDevice.FromAdapter` は、DXGI のアダプターから次の値を読みます。LUID は、OS がアダプターに割り当てる識別子です。ソフトウェア アダプターは、GPU の命令を CPU で実行するアダプターです。

| 値 | 取得元 |
|---|---|
| LUID | `IDXGIAdapter.Description` |
| ベンダー ID、デバイス ID、サブシステム ID、リビジョン | `IDXGIAdapter.Description` |
| ドライバーのバージョン | `IDXGIAdapter.CheckInterfaceSupport` に `IDXGIDevice` を渡して得る値 |
| ソフトウェア アダプターか | `IDXGIAdapter1.Description1` の `Flags` |
| 容量 | UMA なら `SharedSystemMemory`、それ以外なら `DedicatedVideoMemory` |
| 名前 | `IDXGIAdapter.Description` |

LUID は OS を起動し直すと変わるので、OS が起動している間にアダプターを見分けるためだけに使い、例外のメッセージには入れません。

UMA かどうかは、`FromAdapter` が読むのではなく、呼び出し側が渡します。

### 5. 経路の選択

`Select` は次の表を上の行から順に調べ、最初に条件を満たした行の経路を返します。

| 順 | 条件 | 経路 | `ComputeRouteReason` |
|---|---|---|---|
| 1 | デバイスを渡されなかった | CPU | `NoDevice` |
| 2 | 健全性の状態が `Pinned` である | CPU | `Pinned` |
| 3 | ソフトウェア アダプターである | CPU | `SoftwareAdapter` |
| 4 | `ComputeWorkload.GpuMemoryBytes` が、容量の 4 分の 1 を超える | CPU | `MemoryLimit` |
| 5 | 健全性の状態が `Trusted` である。`Untested` なら、先に自己試験を走らせる | GPU | `Trusted` |
| 6 | それ以外 | CPU | `Pinned` |

ソフトウェア アダプターを CPU の経路へ回すのは、そのほうが速いためです。YMM4 のプラグインである Wave Optics の次の版を試作し、計算する領域が 1952×1112 の畳み込みを測りました。Windows のソフトウェア アダプターである Microsoft Basic Render Driver で GPU の経路を通すと約 275 ms、CPU の経路では約 81 ms でした。

### 6. 健全性の状態と自己試験

健全性の状態は、アダプターを GPU の計算に使ってよいかを表します。状態は、アダプターの LUID ごとに持ちます。

| 状態 | 意味 | 移る先 |
|---|---|---|
| `Untested` | この起動で、まだ自己試験をしていない | 自己試験に合格なら `Trusted`。自己試験の不合格、判定の不合格、`Fail` で `Pinned` |
| `Trusted` | 自己試験に合格し、まだ異常が無い | 判定の不合格か `Fail` で `Pinned` |
| `Pinned` | CPU に固定した。YMM4 を終了するまで戻らない | なし |

自己試験は、`Select` が GPU の経路を選ぶ前に走らせます。同じアダプターについて同時に経路を尋ねられても、走らせるのは 1 回だけです。自己試験の処理が例外を投げた場合と、`null` を返した場合も、不合格として扱います。空の一覧を返したときは合格です。

状態は `Interlocked` の操作で移します。複数のスレッドで同時に判定の不合格や `Fail` が起きても、`Pinned` へ移すのは 1 回だけで、報告も最大 1 回です。

### 7. 異常の報告

状態を `Pinned` へ移したとき、報告の受け渡し先を 1 回だけ呼びます。ただし、失敗の種類が `ResourceExhausted` のときは呼びません。

| `ComputeFailure` | 使う場面 | 報告 |
|---|---|---|
| `SelfTestFailed` | 本ライブラリが、自己試験の不合格に使う | する |
| `CheckFailed` | 本ライブラリが、判定の不合格に使う | する |
| `DeviceLost` | GPU のデバイスが失われたときに、呼び出し側が `Fail` に渡す | する |
| `ResourceExhausted` | GPU の資源を確保できなかったときに、呼び出し側が `Fail` に渡す | しない。CPU には固定する |
| `UnexpectedException` | GPU の経路でそのほかの例外が起きたときに、呼び出し側が `Fail` に渡す | する |

受け渡し先に渡す値は `ComputeAnomalyException` で、メッセージには次の値を入れます。

- 失敗の種類
- 検査で不合格になったときは、最初に不合格になった検査 1 件の名前、測定値、参照値、許容値
- アダプターの名前、ベンダー ID とデバイス ID とサブシステム ID とリビジョン、ドライバーのバージョン、ソフトウェア アダプターかどうか
- 計算する領域の幅と高さ、必要な GPU メモリ

メッセージに入れた値は、`ComputeAnomalyException.Anomaly` から `ComputeAnomaly` として型のまま読めます。原因の例外があるときは、`InnerException` に入れます。

YMM4-Telemetry は、同じ報告を見分けるために、指紋という値を作ります。指紋の材料は、例外とその原因の例外のそれぞれから取る、次の 2 つです。

- 型の完全名
- スタックトレースのうち、取り込み先のアセンブリに属するフレームの型名とメソッド名

取り込み先のアセンブリに属するフレームが 1 つも無いときは、すべてのフレームを使います。本ライブラリが渡す例外は、投げていないのでスタックトレースを持ちません。そのため、原因の例外が無い報告は、どのプラグインから渡しても同じ指紋になります。

自己試験の処理が `null` を返したときは、本ライブラリの中で起きた `NullReferenceException` が原因の例外になります。そのフレームは `ComputeGuardian` の中にあるので、この報告も、どのプラグインから渡しても同じ指紋になります。ただし、原因の例外が無い報告とは別の指紋です。

YMM4-Telemetry は、同じ指紋の報告を、プロセス全体で決まった数までしか送りません。サーバーに送る内容にはプラグインの名前が入るので、受け取った側ではプラグインごとに見分けられます。

### 8. 検査と許容値

`ComputeCheck` は、検査 1 件分の名前、測定値、参照値、許容値を持ちます。測定値と参照値の差の絶対値が、許容値以下なら合格です。

`ComputeTolerance` は、許容値を次の式で表します。

$$
\text{許容値} = \text{ULP の数} \times 2^{-23} \times \text{尺度} + \text{0 にされうる演算の数} \times 2^{-126}
$$

- ULP は、浮動小数点数の最下位の桁 1 つ分の幅です。$`2^{-23}`$ は、float の 1.0 における ULP です。
- 尺度は、誤差の大きさを決める量で、計算ごとに誤差の解析から決めます。たとえば総和なら、項の絶対値の和です。
- $`2^{-126}`$ は、float の正の正規化数の最小値です。0 でなく、絶対値がこれより小さい値を、非正規化数と呼びます。GPU は、非正規化数を演算の入力と出力で 0 にします。CPU は 0 にしないので、その差を許容値に含めます。
- 0 にされうる演算の数は、出力が非正規化数になりうる演算の数です。[前提](#取り込む側の前提)を守れば GPU への入力に非正規化数は来ないので、0 にされるのは演算の出力だけです。0 にすることで値が変わる幅は、1 回につき $`2^{-126}`$ 未満です。

負の値か、許容値が有限にならない組み合わせを渡すと、コンストラクターは `ArgumentOutOfRangeException` を投げます。許容値は常に有限なので、NaN と無限大はどの値と比べても合格しません。

個数の検査は `ComputeCheck.Count` で作り、個数が 0 のときだけ合格です。NaN と無限大の個数や、自己試験の 2 回の結果でビットが違う値の個数に使います。

抜き取る位置は `ComputeSampling.Fill` で決めます。最初の 5 か所は四隅と中央で、残りはキーと番号から整数のハッシュで決めます。整数の演算だけを使うので、どの PC でも同じ位置になります。i 番目の位置は、キーと i と幅と高さだけで決まり、抜き取る位置の総数には左右されません。

---

## 誤差のモデル

許容値を導くときに拠る規則を、ここにまとめます。プラグインごとに別の前提で上限を導かないようにするためです。規則は、Microsoft Learn の [Floating-point rules (Direct3D 11)](https://learn.microsoft.com/en-us/windows/win32/direct3d11/floating-point-rules) にある 32 ビットの浮動小数点数の規則です。

| 演算 | 1 回あたりの誤差 |
|---|---|
| 加算、減算、乗算 | 1 ULP 未満 |
| mad や dp3 などの融合演算 | 展開した各段に 1 ULP |
| sqrt | 1 ULP |
| 除算 | 逆数を求めてから乗算する 2 段の計算の誤差を超えない |
| rcp と rsq の命令 | 別に緩い規定がある。上限を導く対象にしない |
| 非正規化数 | 演算の入力と出力で、符号を残して 0 にする |

Direct3D の規則は、加算、減算、乗算の誤差を 0.5 ULP 以内と定めています。一方で、最も近い値への丸めの代わりに切り捨てることも許しています。そのため、上限には 1 ULP 未満を使います。

| 値 | 内容 |
|---|---|
| `ComputeErrorModel.GpuOperationError` | GPU の 1 回の演算の相対誤差の上限。$`2^{-23}`$ |
| `ComputeErrorModel.CpuOperationError` | 最も近い値へ丸める CPU の 1 回の演算の相対誤差の上限。$`2^{-24}`$ |
| `ComputeErrorModel.Gamma(n, u)` | 1 回の相対誤差の上限が u の演算を n 回重ねたときの相対誤差の上限 n u / (1 - n u)。n か u が負のとき、u が有限でないとき、n u が 1 以上のときは例外を投げる |
| `ComputeErrorModel.UlpDistance(a, b)` | 一方の float から他方まで、表せる値をいくつ進むか。隣り合う float の距離は 1、+0 と -0 の距離は 0、NaN との距離は `long.MaxValue` |

三角関数などの超越関数は、この規則に誤差の定めが無いため、上限を導けません。

GPU と CPU で同じ順序の式を書いても、結果が 1 ビット単位で一致するとは限りません。Wave Optics の試作では、`CompileOptions.IeeeStrictness` を指定してシェーダーをコンパイルしました。コンパイルで得た中間コードである DXIL に、精度を緩める最適化を許す `fast` フラグと、融合演算はありませんでした。それでも、Intel Iris Xe Graphics の結果と CPU の単精度の結果の差の絶対値は、最大で $`4.2 \times 10^{-7}`$ でした。ドライバーのバージョンは 32.0.101.7085 です。差は DXIL より後の段階で生じているので、2 つの経路は許容値で比べます。

---

## 検出できる範囲

不変量は、総和のように、出力の全体を計算しなくても入力から正しい値が分かる量です。

| 検出できる | 検出できない |
|---|---|
| NaN と無限大 | 抜き取らなかった位置だけに出て、不変量を変えない誤り |
| 上限を超える誤差のうち、不変量か抜き取った位置に表れるもの | 上限の中に収まる誤り |
| 小さな入力でも表れる系統的な誤り。自己試験で全要素を比べた場合 | 判定の後で、保存していた結果が壊れること |
| 自己試験の入力で、計算のたびに結果が変わること | Direct2D の合成の段で起きる誤り |
| 呼び出し側が `Fail` で知らせたデバイスの消失 | |

何を検査するかは呼び出し側が決めるので、検出できる範囲も呼び出し側の検査で決まります。

---

## 動作の上限値

| 項目 | 値 |
|---|---|
| GPU の経路を選べる作業量 | `GpuMemoryBytes` が容量の 4 分の 1 以下 |
| 決まった位置で抜き取る数 | 先頭から最大 5 か所 |
| 自己試験を走らせる回数 | 1 回の起動で、プラグインとアダプターの組ごとに最大 1 回 |
| 報告する回数 | 1 回の起動で、プラグインとアダプターの組ごとに最大 1 回 |

---

## 制限事項

- 本ライブラリは、GPU の誤りを自分では見つけません。見つけられるのは、呼び出し側が渡した検査に表れた誤りと、自己試験の処理が投げた例外と `null` の戻り値と、`Fail` で知らされた失敗だけです。
- 固定は、一時的な不具合によるものでも、YMM4 を終了するまで続きます。
- 自己試験は、1 回の起動で、プラグインとアダプターの組ごとに最大 1 回です。合格した後に起きた不具合は、毎回の検査でしか見つけられません。
- 超越関数を使う計算には、誤差の規則から上限を導けません。
- 原因の例外が無い報告は、どのプラグインから渡しても YMM4-Telemetry では同じ指紋になります。自己試験の処理が `null` を返した報告も、プラグインによらず、それとは別の 1 つの指紋になります。本ライブラリを取り込んだプラグインが多いと、同じ起動の中で後から渡した報告は送られないことがあります。
- 容量にはアダプターに固有の値を使い、ほかのアプリケーションが使っている GPU メモリは考慮しません。GPU メモリを確保できなかったときは、呼び出し側が `Fail` に `ResourceExhausted` を渡します。本ライブラリは報告せずに、そのアダプターを CPU に固定します。

---

## 注意事項

- 自己試験を走らせるフレームは、その分だけ時間がかかります。その間に同じアダプターについて `Select` を呼んだほかのスレッドのうち、[経路の選択](#5-経路の選択)の表の 5 行目まで進んだものは、自己試験が終わるまで待ちます。
- 報告の受け渡し先が例外を投げると、その例外は `Select`、`Judge`、`Fail` の呼び出し側へ伝わります。状態は既に `Pinned` へ移っています。
- 自己試験に合格し、判定で合格が続く間は、`Select` と `Judge` ではヒープへの割り当てが起きません。ただし、自己試験のデリゲートには毎回同じものを渡す必要があります。変数や `this` をキャプチャするラムダと、インスタンスメソッドを渡すと、そのたびにデリゲートが割り当てられます。`FromAdapter` はアダプターの名前の文字列を割り当てるので、`ComputeDevice` は 1 回読んで持っておきます。

---

## 動作の確認

単体試験は次のコマンドで実行します。実行には Microsoft.Testing.Platform を使います。

```
dotnet test ComputeGuard.Tests/ComputeGuard.Tests.csproj
```

デバイスの識別の試験は、PC にある DXGI のアダプターをすべて読みます。Microsoft Basic Render Driver はどの Windows にもあるので、GPU の無い環境でも試験を飛ばしません。Intel Iris Xe Graphics を積んだ PC では、次の値が読めることを確かめています。2 回読んでも同じ値でした。UMA の行は、Direct3D 11 のデバイスに問い合わせた値です。

| 値 | Intel Iris Xe Graphics | Microsoft Basic Render Driver |
|---|---|---|
| ベンダー ID | 0x8086 | 0x1414 |
| デバイス ID | 0xA7A0 | 0x008C |
| ドライバーのバージョン | 32.0.101.7085 | 10.0.26100.9549 |
| ソフトウェア アダプターか | いいえ | はい |
| UMA | はい | はい |

変異試験は、`tools/mutations.json` に定義した変異を 1 つずつ実装に入れ、試験が落ちるかどうかを測ります。落ちない変異は、その振る舞いを守る試験が無いという意味になります。

```
pwsh -File tools/Invoke-MutationTest.ps1
```

---

## 免責事項

本ライブラリは MIT ライセンスのもとで公開されています。

本ソフトウェアは「現状のまま」提供されており、明示または黙示を問わず、商品性、特定目的への適合性、および権利非侵害に関する保証を含む、いかなる種類の保証も行いません。

作者は、本ライブラリの使用または使用不能に起因するいかなる損害についても、一切の責任を負いません。ご利用は自己責任でお願いします。

---

## ライセンス

[MIT License](LICENSE.txt)
