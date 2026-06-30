using System;
using System.Collections.Generic;
using System.Threading;
using Common.SceneManagement;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace Title.Video
{
    /// <summary>
    /// タイトル背景に動画を 1 回だけ再生する。動画は <see cref="Application.streamingAssetsPath"/> 配下の
    /// ファイルを URL で参照し（WebGL は VideoClip アセット非対応のため URL 方式で全プラットフォーム共通にする）、
    /// <see cref="VideoPlayer"/> で <see cref="RenderTexture"/> に描画して、UXML の全画面背景
    /// 要素（VideoBackground）に貼り付ける。音声はミュートしてタイトル BGM をそのまま流す。
    /// 再生が最後まで進んだら（ループせず）最後のフレームで止め、その上にタイトル文言（TitleText）を表示する。
    /// 表示前に最初のフレームまで用意するため <see cref="ISceneReady"/> を実装する。
    /// 動画が未配置・再生不可の場合は USS のベース色のまま、文言だけ表示する。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class TitleVideoPresenter : MonoBehaviour, ISceneReady
    {
        // StreamingAssets からの相対パス（WebGL でも URL として解決できる）。
        [SerializeField] private string _videoPath = "Video/TitleMovie.mp4";
        // 準備がこの秒数で完了しなければ動画を諦めて文言だけ表示する（黒画面で固まらせない保険）。
        [SerializeField] private float _prepareTimeoutSeconds = 8f;

        // タイトル文言（3 行）。1 文字ずつ上から降らせる。
        private static readonly string[] TitleLines = { "ドラゴン", "ファミリー", "すごろく" };
        // 文字ごとの登場ディレイ（秒）。降ってくる順番の間隔。
        private const float CharStaggerSeconds = 0.09f;

        private UIDocument _uiDocument;
        private VisualElement _videoBackground;
        private VisualElement _titleText;
        private readonly List<VisualElement> _titleChars = new();
        private VideoPlayer _videoPlayer;
        private RenderTexture _renderTexture;
        private UniTask _initTask;
        private bool _initStarted;

        private void Awake()
        {
            _uiDocument = GetComponent<UIDocument>();
        }

        // Title は遷移経由（ReadyAsync）と直接起動（BootLoader / エディタで Title から Play）の
        // 両方で開かれる。直接起動では ReadyAsync が呼ばれないため、Start でも初期化を起動して
        // 動画の取りこぼし（初回だけ再生されない）を防ぐ。
        private void Start()
        {
            EnsureInitStarted();
        }

        // 遷移でこのシーンに入った場合に呼ばれる。初期化（動画準備）完了まで待ってからフェードインさせる。
        public async UniTask ReadyAsync(CancellationToken ct)
        {
            EnsureInitStarted();
            await _initTask.AttachExternalCancellation(ct);
        }

        // 初期化を一度だけ起動する。Start と ReadyAsync のどちらが先に来ても取りこぼさない。
        private void EnsureInitStarted()
        {
            if (_initStarted)
            {
                return;
            }
            _initStarted = true;
            _initTask = InitializeAsync(destroyCancellationToken).Preserve();
        }

        // 破棄・遷移キャンセルは正常系として握りつぶし、fire-and-forget（直接起動時）でも安全にする。
        private async UniTask InitializeAsync(CancellationToken ct)
        {
            try
            {
                await RunInitAsync(ct);
            }
            catch (OperationCanceledException)
            {
            }
        }

        // フェードイン前に動画を準備し、最初のフレームを用意してから再生を始める。
        private async UniTask RunInitAsync(CancellationToken ct)
        {
            VisualElement root = _uiDocument.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("Title の rootVisualElement が見つかりませんでした。");
                return;
            }

            _videoBackground = root.Q<VisualElement>("VideoBackground");
            _titleText = root.Q<VisualElement>("TitleText");
            if (_videoBackground == null)
            {
                Debug.LogError("VideoBackground 要素が見つかりませんでした。");
                return;
            }
            BuildTitle();

            _videoPlayer = gameObject.AddComponent<VideoPlayer>();
            _videoPlayer.playOnAwake = false;
            // WebGL では VideoClip 非対応のため URL（StreamingAssets）で再生する。
            _videoPlayer.source = VideoSource.Url;
            _videoPlayer.url = $"{Application.streamingAssetsPath}/{_videoPath}";
            // ループせず 1 回だけ再生し、最後のフレームで止める。
            _videoPlayer.isLooping = false;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            // 映像のみ・BGM維持: 動画の音声は鳴らさない。
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            _videoPlayer.waitForFirstFrame = true;
            _videoPlayer.skipOnDrop = true;

            bool prepared = await TryPrepareAsync(ct);
            if (!prepared)
            {
                // 再生できない場合は文言だけ表示する（タイトルは通常どおり機能する）。
                ShowTitleText();
                return;
            }

            // URL 再生はサイズが準備後に確定するので、ここで RenderTexture を作る。
            _renderTexture = new RenderTexture((int)_videoPlayer.width, (int)_videoPlayer.height, 0);
            _renderTexture.Create();
            _videoPlayer.targetTexture = _renderTexture;

            _videoBackground.style.backgroundImage =
                new StyleBackground(Background.FromRenderTexture(_renderTexture));
            // 再生終了でタイトル文言を表示する（最後のフレームはそのまま残る）。
            _videoPlayer.loopPointReached += OnPlaybackFinished;
            // 再生中に GPU デバイス喪失などのエラーが起きても、黒画面で固まらず文言を出す保険。
            _videoPlayer.errorReceived += OnPlaybackError;
            _videoPlayer.Play();
        }

        // 再生が最後まで進んだら呼ばれる（isLooping=false なので 1 回だけ）。
        private void OnPlaybackFinished(VideoPlayer source)
        {
            ShowTitleText();
        }

        // 再生中のエラー（DXGI_ERROR_DEVICE_REMOVED 等）。動画は諦めて文言だけ出す。
        private void OnPlaybackError(VideoPlayer source, string message)
        {
            Debug.LogWarning($"タイトル動画の再生中にエラーが発生しました（{_videoPlayer.url}）: {message}");
            ShowTitleText();
        }

        // 3 行ぶんの行コンテナと 1 文字ずつのラベルを生成する。初期は隠れた状態（USS の .title-char）。
        // 文字ごとに transition-delay をずらして、降ってくる順番の間隔を作る。
        private void BuildTitle()
        {
            if (_titleText == null)
            {
                return;
            }

            _titleText.Clear();
            _titleChars.Clear();

            int globalIndex = 0;
            foreach (string line in TitleLines)
            {
                VisualElement row = new() { pickingMode = PickingMode.Ignore };
                row.AddToClassList("title-line");

                foreach (char character in line)
                {
                    Label charLabel = new() { text = character.ToString(), pickingMode = PickingMode.Ignore };
                    charLabel.AddToClassList("title-char");
                    charLabel.style.transitionDelay = new List<TimeValue>
                    {
                        new TimeValue(globalIndex * CharStaggerSeconds, TimeUnit.Second),
                    };
                    row.Add(charLabel);
                    _titleChars.Add(charLabel);
                    globalIndex++;
                }

                _titleText.Add(row);
            }
        }

        // 全文字に visible クラスを付与。各文字は自分の transition-delay ぶん遅れて降りてくる。
        private void ShowTitleText()
        {
            foreach (VisualElement charLabel in _titleChars)
            {
                charLabel.EnableInClassList("title-char--visible", true);
            }
        }

        // 最初のフレームをデコードし終えるまで待つ（フェードインで黒画面を見せないため）。
        // 準備に成功したら true、再生不可（ファイル無し・コーデック非対応）やタイムアウトなら false を返す。
        // 準備完了もエラーも飛んでこないケース（WebGL でのストール等）に備えてタイムアウトで打ち切る。
        private async UniTask<bool> TryPrepareAsync(CancellationToken ct)
        {
            UniTaskCompletionSource<bool> tcs = new();
            void OnPrepared(VideoPlayer source)
            {
                tcs.TrySetResult(true);
            }
            void OnError(VideoPlayer source, string message)
            {
                Debug.LogWarning($"タイトル動画の準備に失敗しました（{_videoPlayer.url}）: {message}");
                tcs.TrySetResult(false);
            }

            _videoPlayer.prepareCompleted += OnPrepared;
            _videoPlayer.errorReceived += OnError;

            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_prepareTimeoutSeconds));
            try
            {
                _videoPlayer.Prepare();
                return await tcs.Task.AttachExternalCancellation(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // 元の ct ではなくタイムアウトでのキャンセル。動画を諦めて先に進む。
                Debug.LogWarning($"タイトル動画の準備がタイムアウトしました（{_videoPlayer.url}）。文言のみ表示します。");
                return false;
            }
            finally
            {
                _videoPlayer.prepareCompleted -= OnPrepared;
                _videoPlayer.errorReceived -= OnError;
            }
        }

        private void OnDestroy()
        {
            if (_videoPlayer != null)
            {
                _videoPlayer.loopPointReached -= OnPlaybackFinished;
                _videoPlayer.errorReceived -= OnPlaybackError;
                _videoPlayer.Stop();
                // RenderTexture を解放する前にデコーダから切り離す（破棄済み RT への参照を残さない）。
                _videoPlayer.targetTexture = null;
            }
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }
        }
    }
}
