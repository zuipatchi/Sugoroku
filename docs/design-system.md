# デザインシステム

UI Toolkit（UXML + USS）を使用したUIの設計ルールをまとめる。

## USS ファイルの使い方

スタイルはインライン記述せず、シーンごとの USS ファイルに定義してクラスで適用する。

```xml
<!-- UXML の先頭で USS を読み込む -->
<ui:UXML ...>
    <Style src="MyScene.uss" />
    ...
    <ui:Button class="btn-accent" style="width: 160px;" />
</ui:UXML>
```

USS ファイルは対応する UXML と同じディレクトリに配置する（例: `View/Matching.uss`）。

---

## オプションボタンとの重なり防止

Common シーンのオプションアイコン（`right:0 / top:0`、60×60px）は全シーンに `position:absolute` で重なる。
新しいシーンを作成する際は、**右上エリアに UI 要素を配置しない**ように設計すること。

- メインコンテンツは中央寄せ・左寄せで配置する
- 右上に要素を置く場合は `right` を `70px` 以上、`top` を `70px` 以上あける
- フルスクリーンのオーバーレイ（ローディング・モーダル暗幕など）は `position:absolute` で全画面を覆うため重なりは問題なし

---

## カラーパレット

| 用途 | 値 |
|---|---|
| カード背景 | `rgb(22, 22, 35)` |
| オーバーレイ暗幕 | `rgba(0, 0, 0, 0.55)` |
| ボーダー | `rgba(255, 255, 255, 0.15)` |
| 区切り線 | `rgba(255, 255, 255, 0.1)` |
| テキスト（見出し） | `rgb(240, 240, 255)` |
| テキスト（本文・ラベル） | `rgb(180, 180, 210)` |
| テキスト（ボタン） | `rgb(255, 255, 255)` |
| アクセント（ボタン背景） | `rgb(70, 90, 180)` |

---

## タイポグラフィ

| 用途 | font-size | font-style |
|---|---|---|
| モーダルタイトル | `20px` | normal（既定フォントが Bold のため指定不要） |
| ラベル（項目名） | `13px` | normal |
| ボタンテキスト | `14px` | normal |

> 既定フォントが NotoSansJP **Bold** なので、`-unity-font-style: bold` を重ねると faux bold で太くなりすぎる。原則 normal のまま使う。

#### 文字色とコントラスト

SDF フォントはエッジをアンチエイリアスで描画するため、**低コントラストの文字（暗い背景に対して暗めの色）は細く見え、高コントラストの文字（暗い背景に対して白）は太く見える**（同じウェイトでも錯視で太さが変わって見える）。複数のボタンを並べて太さを揃えたいときは、文字色のコントラストを揃える。例として `btn-secondary` の文字色は `rgb(230, 230, 245)` とし、白文字の `btn-accent` と並べても太さが揃って見えるようにしている。

### 日本語フォント

ゲーム全体の既定フォントは **NotoSansJP Bold (SDF)** で、テーマ [UnityDefaultRuntimeTheme.tss](../Assets/UI%20Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss) の `.unity-text-element` に `-unity-font-definition` を設定して全テキスト要素（Label / Button 等）へ継承させている。**Label ごとにインラインで `-unity-font` を指定する必要はない**。

- フォントアセット: [Assets/Font/NotoSansJP-Bold SDF.asset](../Assets/Font/NotoSansJP-Bold%20SDF.asset)（Atlas Population Mode = Dynamic。未収録グリフは実行時にソース TTF [NotoSansJP-Bold.ttf](../Assets/Font/NotoSansJP-Bold.ttf) から補完される）
- 太さは Bold (700) で焼いてあるため、`-unity-font-style: bold` を重ねると faux bold で過剰に太くなる場合がある。見出しをさらに強調したいときのみ使う。
- 別の太さ・別フォントに差し替える場合は、新しい SDF を作って TSS の url を差し替える（または PanelSettings の Text Settings に PanelTextSettings を割り当てる）。

> **WebGL では NotoSansJP に無い記号は豆腐（□）になる**。絵文字・ダインバット系（鉛筆 `✎`、`✕`、小三角 `▾` など）はこのフォントに未収録で、**エディタでは OS フォントへフォールバックして見えても WebGL ビルドでは豆腐になる**。閉じる＝`×`(U+00D7)、ドロップダウン矢印＝`▼`(U+25BC) のように収録済みグリフを使うか、画像アイコン（USS `background-image`）／テキストに置き換える。矢印（→←↑↓）・三点リーダ（…）・星（★☆）・●■・引用符は収録済み。

---

## スペーシング

| 用途 | 値 |
|---|---|
| カード内パディング | `28px 32px` |
| セクション間マージン | `18px` |
| 最終セクション下マージン | `24px` |
| タイトル下マージン | `16px` |
| 区切り線下マージン | `20px` |
| ラベル〜スライダー間 | `4px` |

---

## コンポーネント

USS クラス定義の実装例は以下を参照。

- [Assets/Scripts/Matching/View/Matching.uss](../Assets/Scripts/Matching/View/Matching.uss) — マッチングシーン
- [Assets/AddressableAssets/Modal/Modal.uss](../Assets/AddressableAssets/Modal/Modal.uss) — オプションモーダル

### カード（`.card`）

```css
.card {
    background-color: rgb(22, 22, 35);
    border-top-left-radius: 16px; border-top-right-radius: 16px;
    border-bottom-left-radius: 16px; border-bottom-right-radius: 16px;
    border-left-width: 1px; border-right-width: 1px;
    border-top-width: 1px; border-bottom-width: 1px;
    border-left-color: rgba(255, 255, 255, 0.15); /* 他3辺も同じ */
    padding-top: 32px; padding-right: 32px; padding-bottom: 32px; padding-left: 32px;
}
```

### 区切り線（`.divider`）

```css
.divider {
    height: 1px;
    background-color: rgba(255, 255, 255, 0.1);
    margin-bottom: 16px;
}
```

### ボタン

```css
/* アクセントボタン（主要アクション） */
.btn-accent {
    background-color: rgb(70, 90, 180);
    color: rgb(255, 255, 255);
    border-top-left-radius: 8px; /* 他3角も同じ */
    border-left-width: 0; /* 他3辺も同じ */
    padding-top: 10px; padding-right: 10px; padding-bottom: 10px; padding-left: 10px;
    font-size: 14px;
    -unity-text-align: middle-center;
}

/* セカンダリボタン（補助アクション） */
.btn-secondary {
    background-color: rgba(255, 255, 255, 0.07);
    color: rgb(230, 230, 245); /* btn-accent と見た目の太さを揃えるため明るめ。下記「文字色とコントラスト」参照 */
    border-top-left-radius: 8px; /* 他3角も同じ */
    border-left-width: 1px; /* 他3辺も同じ */
    border-left-color: rgba(255, 255, 255, 0.15); /* 他3辺も同じ */
    padding-top: 10px; padding-right: 10px; padding-bottom: 10px; padding-left: 10px;
    font-size: 14px;
    -unity-text-align: middle-center;
}
```

**C# コードで生成したボタンのホバー・押下効果**

`Button` をコードで生成してインラインスタイル（`button.style.backgroundColor = ...`）を設定している場合、USS の `:hover` / `:active` 擬似クラスは**インラインスタイルに上書きされて効かない**（インラインスタイルが優先される）。この場合は PointerEvent コールバックで対応する。

```csharp
private static void AddButtonHoverEffect(Button button, Color baseColor)
{
    Color hoverColor = new Color(
        Mathf.Clamp01(baseColor.r + 0.12f),
        Mathf.Clamp01(baseColor.g + 0.12f),
        Mathf.Clamp01(baseColor.b + 0.12f), baseColor.a);
    Color activeColor = new Color(
        Mathf.Clamp01(baseColor.r - 0.1f),
        Mathf.Clamp01(baseColor.g - 0.1f),
        Mathf.Clamp01(baseColor.b - 0.1f), baseColor.a);
    button.RegisterCallback<PointerEnterEvent>(_ => button.style.backgroundColor = new StyleColor(hoverColor));
    button.RegisterCallback<PointerLeaveEvent>(_ => button.style.backgroundColor = new StyleColor(baseColor));
    button.RegisterCallback<PointerDownEvent>(_ => button.style.backgroundColor = new StyleColor(activeColor));
    button.RegisterCallback<PointerUpEvent>(_ => button.style.backgroundColor = new StyleColor(hoverColor));
}
```

可能なら、インラインスタイルを使わず USS クラスのみでスタイルを管理して `:hover` / `:active` を効かせるほうが簡潔。

**背景画像ボタンの押下フィードバック（scale 変化）**

背景が PNG 画像で背景色変化が見えないボタンには、`scale` の transition でホバー拡大・押下縮小のフィードバックを付ける。

```css
.action-button {
    transition-property: scale;
    transition-duration: 0.1s;
}
.action-button:hover  { scale: 1.06 1.06; }
.action-button:active { scale: 0.94 0.94; }
```

### リスト項目（`.room-item`）

```css
.room-item {
    background-color: rgba(255, 255, 255, 0.05);
    border-top-left-radius: 8px; /* 他3角も同じ */
    border-left-width: 1px; /* 他3辺も同じ */
    border-left-color: rgba(255, 255, 255, 0.1); /* 他3辺も同じ */
    padding-top: 14px; padding-right: 16px; padding-bottom: 14px; padding-left: 16px;
    margin-bottom: 8px;
    color: rgb(180, 180, 210);
    font-size: 14px;
    -unity-text-align: middle-left;
}
.room-item:hover {
    background-color: rgba(70, 90, 180, 0.2);
    border-left-color: rgba(70, 90, 180, 0.5); /* 他3辺も同じ */
}
```

---

## インタラクション演出ルール

### 擬似クラスと役割

| 擬似クラス | トリガー | 演出 |
|---|---|---|
| `:hover` | マウスカーソルが乗った | 背景を少し明るくする |
| `:focus` | キーボード・ゲームパッドで選択中 | `:hover` より少し明るくする |
| `:active` | 押下中（マウスボタンを押している間） | 背景を暗くし、`scale` で縮小 |

擬似クラスは **`:hover` → `:focus` → `:active`** の順で定義する（特異度が同じため、後に書いたものが優先される）。

### ボタンの値の目安

**`btn-accent`**（ベース: `rgb(70, 90, 180)`）

| 擬似クラス | 背景色 | scale |
|---|---|---|
| `:hover` | `rgb(85, 105, 198)` | — |
| `:focus` | `rgb(95, 115, 210)` | — |
| `:active` | `rgb(50, 65, 145)` | `0.96` |

**`btn-secondary`**（ベース: `rgba(255, 255, 255, 0.07)`）

| 擬似クラス | 背景色 | scale |
|---|---|---|
| `:hover` | `rgba(255, 255, 255, 0.13)` | — |
| `:focus` | `rgba(255, 255, 255, 0.18)` | — |
| `:active` | `rgba(255, 255, 255, 0.03)` | `0.96` |

**リスト項目（`.room-item` など）**

`:active` はボタンより控えめに `scale: 0.98` とし、背景のアクセントカラー透明度を上げる。

### USS スニペット

```css
/* ボタン擬似クラスの記述順（常にこの順番で書く） */
.btn-accent:hover  { background-color: ...; }
.btn-accent:focus  { background-color: ...; }
.btn-accent:active { background-color: ...; scale: 0.96; }
```

---

## オーバーレイ

モーダル表示時はゲーム画面を暗幕で覆う。

```xml
<!-- position: absolute で全画面を覆う -->
<ui:VisualElement style="
    position: absolute; width: 100%; height: 100%;
    background-color: rgba(0, 0, 0, 0.55);">
    <!-- align-items: center; justify-content: center でカードを中央配置 -->
    <ui:VisualElement style="flex-grow: 1; align-items: center; justify-content: center;"/>
</ui:VisualElement>
```

---

## アイコン

アイコンは SVG を Addressables に配置し、UXML の `background-image` で参照する。

```
Assets/AddressableAssets/Icon/
  sliders-solid-full.svg   オプション設定アイコン
```

常に表示するアイコン（オプションボタンなど）は `position: absolute` で配置する。

```xml
<!-- 右上に固定表示する例 -->
<ui:Image style="position: absolute; right: 0; top: 0; width: 5%; height: 5%;"/>
```

---

## UIDocument の設定

| 設定 | 値 | 理由 |
|---|---|---|
| SortingOrder | `1000` | 他のUIより手前に描画するため |
