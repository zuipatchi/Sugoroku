using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Main.Board
{
    /// <summary>
    /// AssetStore のパーティクル Prefab（ワールド空間）を UI Toolkit の前面に重ねて再生する汎用プレイヤー。
    /// UI Toolkit の ScreenSpaceOverlay はワールドオブジェクトを常に覆い隠すため、専用カメラで
    /// RenderTexture に描画し、加算ブレンドの uGUI Canvas 上の RawImage で前面に合成する
    /// （docs/effects.md「1. UI Toolkit の上にパーティクルを表示できない問題」の RenderTexture 方式）。
    /// <see cref="BoardPresenter"/> が所持し、勝利（花火）・敗北（雨）など決着演出を一度だけ再生する。
    /// インスタンスごとに 1 回だけ再生するので、勝利用・敗北用で別インスタンスを持つ。
    /// </summary>
    public sealed class ScreenEffectPlayer
    {
        // TagManager.asset の 6 番に用意した専用レイヤー。名前解決に失敗したときのフォールバック index。
        private const int FallbackEffectLayer = 6;
        private const string EffectLayerName = "Effect";
        // RawImage を UI Toolkit（PanelSettings の sortingOrder ＝ 最大でも 100）より確実に前面へ出す値。
        private const int OverlaySortingOrder = 200;

        private readonly GameObject _prefab;
        private readonly Shader _additiveShader;
        // 二重再生を防ぐ（勝者は 1 度しか確定しないが購読の多重発火に備える）。
        private bool _played;

        public ScreenEffectPlayer(GameObject prefab, Shader additiveShader)
        {
            _prefab = prefab;
            _additiveShader = additiveShader;
        }

        /// <summary>
        /// エフェクトを一度だけ再生する。Prefab・シェーダー・メインカメラが未設定なら何もしない。
        /// <paramref name="count"/> 発を横に広げ、<paramref name="stagger"/> 秒ずつ時間差で打ち上げる。
        /// <paramref name="keepUntilCancelled"/> が false なら実再生時間ぶん待って後片付け、true なら
        /// <paramref name="ct"/> がキャンセルされる（＝シーン破棄など）まで再生し続けてから後片付けする
        /// （雨のようにループさせ続けたいエフェクト向け）。
        /// </summary>
        /// <param name="distance">エフェクトカメラ前方に Prefab を置く距離。</param>
        /// <param name="verticalOffset">画面中央からの縦オフセット（負で下＝下から打ち上がって見える）。</param>
        /// <param name="scale">Prefab の表示スケール。</param>
        /// <param name="count">打ち上げる発数（1 以上）。</param>
        /// <param name="stagger">1 発ごとの打ち上げ間隔（秒）。</param>
        /// <param name="keepUntilCancelled">true なら ct キャンセルまで再生し続ける（実再生時間で片付けない）。</param>
        public async UniTask PlayAsync(
            float distance,
            float verticalOffset,
            float scale,
            int count,
            float stagger,
            bool keepUntilCancelled,
            CancellationToken ct)
        {
            if (_played || _prefab == null || _additiveShader == null)
            {
                return;
            }

            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                return;
            }
            _played = true;
            count = Mathf.Max(1, count);
            stagger = Mathf.Max(0f, stagger);

            int effectLayer = LayerMask.NameToLayer(EffectLayerName);
            if (effectLayer < 0)
            {
                effectLayer = FallbackEffectLayer;
            }
            int effectMask = 1 << effectLayer;

            // メインカメラからエフェクトレイヤーを除外し、パーティクルが盤面裏に二重描画されないようにする。
            int originalCullingMask = mainCam.cullingMask;
            mainCam.cullingMask &= ~effectMask;

            RenderTexture rt = null;
            GameObject camObj = null;
            GameObject canvasObj = null;
            Material material = null;
            List<GameObject> instances = new(count);

            try
            {
                rt = new RenderTexture(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height), 16)
                {
                    name = "ScreenEffectRT"
                };
                rt.Create();

                // エフェクト専用カメラ（黒クリア＝加算で透明・エフェクトレイヤーだけを RenderTexture へ描く）。
                camObj = new GameObject("ScreenEffectCamera");
                Camera effectCam = camObj.AddComponent<Camera>();
                effectCam.clearFlags = CameraClearFlags.SolidColor;
                effectCam.backgroundColor = Color.black;
                effectCam.cullingMask = effectMask;
                effectCam.targetTexture = rt;
                effectCam.orthographic = mainCam.orthographic;
                effectCam.fieldOfView = mainCam.fieldOfView;
                effectCam.orthographicSize = mainCam.orthographicSize;
                effectCam.nearClipPlane = mainCam.nearClipPlane;
                effectCam.farClipPlane = mainCam.farClipPlane;
                effectCam.transform.SetPositionAndRotation(mainCam.transform.position, mainCam.transform.rotation);

                // 前面合成用の Screen Space Overlay Canvas ＋ 加算ブレンド RawImage。
                canvasObj = new GameObject("ScreenEffectCanvas");
                Canvas canvas = canvasObj.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = OverlaySortingOrder;

                material = new Material(_additiveShader) { mainTexture = rt };

                GameObject imgObj = new GameObject("ScreenEffectImage");
                imgObj.transform.SetParent(canvasObj.transform, false);
                RawImage img = imgObj.AddComponent<RawImage>();
                img.texture = rt;
                img.material = material;
                // ゲーム終了後の入力（ホームに戻る等）を塞がないよう、合成レイヤーはレイキャスト対象外にする。
                img.raycastTarget = false;
                RectTransform rect = img.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                // 画面に収まる横の広がり（距離・画角・アスペクトから半幅を出し、その 6 割まで広げる）。
                float halfHeight = effectCam.orthographic
                    ? effectCam.orthographicSize
                    : Mathf.Tan(effectCam.fieldOfView * 0.5f * Mathf.Deg2Rad) * distance;
                float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 0.5625f;
                float spreadX = halfHeight * aspect * 0.6f;

                // count 発を横に広げ、stagger 秒ずつ時間差で打ち上げる。各 Prefab の CFXR は自己破棄するが、
                // 途中でシーンが変わった場合に備えて生成物は instances で保持し finally でまとめて片付ける。
                float singlePlayback = 0f;
                for (int i = 0; i < count; i++)
                {
                    // -1〜+1 に正規化した横位置（1 発なら中央）。
                    float t = count == 1 ? 0f : (i / (float)(count - 1)) * 2f - 1f;
                    Vector3 spawn = effectCam.transform.position
                        + effectCam.transform.forward * distance
                        + effectCam.transform.up * verticalOffset
                        + effectCam.transform.right * (t * spreadX);
                    GameObject instance = UnityEngine.Object.Instantiate(_prefab, spawn, effectCam.transform.rotation);
                    instance.transform.localScale = Vector3.one * scale;
                    SetLayerRecursive(instance, effectLayer);
                    instances.Add(instance);

                    if (singlePlayback <= 0f)
                    {
                        singlePlayback = ComputePlaybackSeconds(instance);
                    }

                    if (i < count - 1 && stagger > 0f)
                    {
                        await UniTask.Delay(TimeSpan.FromSeconds(stagger), cancellationToken: ct);
                    }
                }

                if (keepUntilCancelled)
                {
                    // 雨などは固定秒数で消さず、シーン破棄（ct キャンセル）まで降らせ続ける。
                    // WaitUntilCanceled は例外を投げずキャンセルで正常完了し、そのまま finally で後片付けする。
                    await UniTask.WaitUntilCanceled(ct);
                }
                else
                {
                    // 最後の 1 発が終わるまで待つ。
                    await UniTask.Delay(TimeSpan.FromSeconds(singlePlayback), cancellationToken: ct);
                }
            }
            finally
            {
                // キャンセル（シーン遷移）時も含め、生成物を必ず後片付けする。
                if (mainCam != null)
                {
                    mainCam.cullingMask = originalCullingMask;
                }
                foreach (GameObject instance in instances)
                {
                    if (instance != null)
                    {
                        UnityEngine.Object.Destroy(instance);
                    }
                }
                if (canvasObj != null)
                {
                    UnityEngine.Object.Destroy(canvasObj);
                }
                if (camObj != null)
                {
                    UnityEngine.Object.Destroy(camObj);
                }
                if (material != null)
                {
                    UnityEngine.Object.Destroy(material);
                }
                if (rt != null)
                {
                    rt.Release();
                    UnityEngine.Object.Destroy(rt);
                }
            }
        }

        // Prefab 内の全 ParticleSystem から実再生時間を見積もる（docs/effects.md #7）。
        // バースト放出では duration と lifetime の長い方が見た目の長さになるので max を取り、
        // simulationSpeed で割って実時間へ補正する。極端な値は 1.5〜6 秒にクランプする。
        private static float ComputePlaybackSeconds(GameObject instance)
        {
            float longest = 0f;
            ParticleSystem[] systems = instance.GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem ps in systems)
            {
                ParticleSystem.MainModule main = ps.main;
                float lifetime = main.startLifetime.mode == ParticleSystemCurveMode.TwoConstants
                    ? main.startLifetime.constantMax
                    : main.startLifetime.constant;
                float speed = Mathf.Max(0.0001f, main.simulationSpeed);
                float playback = (Mathf.Max(main.duration, lifetime) + main.startDelay.constantMax) / speed;
                if (playback > longest)
                {
                    longest = playback;
                }
            }
            return Mathf.Clamp(longest, 1.5f, 6f);
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }
    }
}
