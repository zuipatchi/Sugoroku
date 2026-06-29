# Live2D Cubism SDK — アニメーション実装ノウハウ

Unity + Live2D Cubism SDK でアニメーションを再生するときのハマりポイントと解決策。

SDK バージョン: Cubism SDK for Unity 5.x 系で確認。

---

## フレーム更新パイプライン

Live2D モデルは 1 フレームに以下の順序で更新される。この順序を理解しておくと原因調査が速い。

```
① Animator.Update
     AnimationClip のカーブ → CubismParameter.Value に書き込む

② CubismModel.Update
     TryReadParameters()        ネイティブモデル → Unity パラメーターへ同期
     RestoreParameters()        前フレームの保存値でパラメーターを上書き ← ★要注意

③ CubismUpdateController.LateUpdate  (ICubismUpdatable を実行順に呼ぶ)
     CubismFadeController   order=100    フェード計算・パラメーター合成
     CubismParameterStore   order=150    現在値を保存
     CubismPoseController   order=200    パーツ表示切替
     CubismRenderController order=10000  メッシュ・マテリアル更新
```

---

## ハマりポイントと対処法

### 1. `.motion3.json` のパラメーター ID がモデルと一致していない

AnimationClip は `Parameters/<パラメーターID>` というパスで CubismParameter を参照する。
パラメーター ID は `.cdi3.json` の `"Id"` フィールドに記載されており、Live2D Editor 上の**表示名（Name）とは別物**。

```json
// MyModel.cdi3.json
"Parameters": [
  { "Id": "ParamMouthOpenY", "Name": "口 開閉Y" }
]
```

```json
// motion.motion3.json — Id は表示名ではなく ID を使う
"Curves": [
  { "Target": "Parameter", "Id": "ParamMouthOpenY", ... }
]
```

`.motion3.json` を修正したら Unity Editor で `.model3.json` を **右クリック → Reimport** して `.anim` と `.fade.asset` を再生成する。

---

### 2. `CubismParameterStore` が Animator の値を毎フレーム上書きする

`CubismModel.Update()` は毎フレーム `CubismParameterStore.RestoreParameters()` を呼び出し、  
前フレームに保存した値でパラメーターを上書きする。

**問題が起きるケース**：デフォルト（アイドル）アニメーションがパラメーターを一切動かさない場合。  
保存値が常に初期値（0 など）のままになるため、別のアニメーションに切り替えても値がすぐ初期値に戻ってしまい、見た目上アニメーションしない。

**対処**：`CubismParameterStore` を無効化する。  
`RestoreParameters()` 内で `if (!enabled) return;` しているため、無効化するだけで上書きが止まる。

```csharp
// インスタンス化直後にランタイムで無効化する例
foreach (Component c in modelRoot.GetComponentsInChildren<Component>())
{
    if (c.GetType().Name == "CubismParameterStore" && c is Behaviour b)
    {
        b.enabled = false;
        break;
    }
}
```

> **注意**：この無効化が不要なのは、すべてのアニメーションが常に正しいパスでパラメーターを駆動している場合のみ。

---

### 3. `FadeMotionList` なしで AnimatorController だけで再生する

`CubismFadeMotionList` をセットアップせず、Unity 標準の AnimatorController + `.anim` だけでアニメーションを再生したい場合は、以下のコンポーネントを無効化する。

| コンポーネント | 無効化する理由 |
|---|---|
| `CubismFadeController` | `FadeMotionList` が null のとき `CubismFadeStateObserver.OnStateEnter` が NullRef を起こす |
| `CubismParameterStore` | 上記「ハマりポイント 2」の通り、Animator 値を上書きしてしまう |

`CubismMotionController` は AnimatorController が設定されていると `_isActive = false` のまま何もしないため、無効化不要。

---

### 4. SDK 内の NullRef パッチ（`FadeMotionList` 未設定時）

`CubismFadeController` を無効化しても、AnimatorController のステート遷移時に  
`CubismFadeStateObserver`（StateMachineBehaviour）が発火して NullRef になる。  
以下の 2 か所に null ガードを追加する。

**`CubismFadeStateObserver.cs` — `OnStateEnter`**

```csharp
_cubismFadeMotionList = fadeController.CubismFadeMotionList;

// この行を追加
if (_cubismFadeMotionList == null)
{
    return;
}

_isStateTransitionFinished = false;
// ...以降の処理
```

**`CubismMotionController.cs` — `OnEnable`**

```csharp
// 変更前: Debug.LogError("..."); return;
// 変更後:
if (_cubismFadeMotionList == null)
{
    return;
}
```

---

### 5. ソート順（描画順）の設定

`CubismRenderController._sortingOrder` のセッターは  
`if (value == _sortingOrder) return;` の早期リターンがあり、  
**プレハブにシリアライズした値は起動時に反映されない**。

描画順を変えるには、各 `CubismRenderer` の `_localSortingOrder` をプレハブ YAML で直接変更する。

```
MeshRenderer.sortingOrder = _sortingOrder + _renderOrder + _localSortingOrder
```

`_localSortingOrder` は `ApplySorting()` が毎フレーム参照するため、プレハブへの直書きが確実に反映される。

```yaml
# プレハブ YAML 内の CubismRenderer コンポーネント
m_EditorClassIdentifier: Live2D.Cubism::Live2D.Cubism.Rendering.CubismRenderer
_localSortingOrder: 50   # 0 から 50 に変更 → 他モデルより前面に
```

モデルに複数の CubismRenderer がある場合はすべて変更する。

---

### 6. アイドルモーションは空カーブにする（`FadeMotionList` なし構成）

`FadeMotionList` を使わない構成で「アイドル中はポーズを保つだけ」でよい場合、  
アイドルの `.motion3.json` のカーブを空にするのが最もシンプルで安全。

誤ったパラメーター ID のカーブが残っていると「ハマりポイント 2」が発生する。

```json
{
  "Version": 3,
  "Meta": {
    "Duration": 2.0,
    "Fps": 30.0,
    "Loop": true,
    "CurveCount": 0,
    "TotalSegmentCount": 0,
    "TotalPointCount": 0
  },
  "Curves": []
}
```

変更後は `.model3.json` を Reimport する。

---

## FadeMotionList ありで正規構成にする場合

`CubismFadeController` を有効にしてフェード付きアニメーションを使う正規構成では、  
`CubismFadeMotionList` のセットアップが必要になる。以下のエディタスクリプトを参考にする。

```csharp
// エディタスクリプト例
[MenuItem("Live2D/Setup FadeMotionList")]
static void Setup()
{
    // 1. モデルディレクトリから CubismFadeMotionData を収集
    // 2. 対応する .anim の AnimationEvent から InstanceId を読み取る
    // 3. CubismFadeMotionList アセットを生成して CubismFadeController に割り当てる
    // 4. プレハブとして保存
}
```
