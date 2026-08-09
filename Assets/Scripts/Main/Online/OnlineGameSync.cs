using System;
using System.Collections.Generic;
using System.Threading;
using Common.GameSession;
using Cysharp.Threading.Tasks;
using R3;
using Unity.Netcode;
using UnityEngine;

namespace Main.Online
{
    /// <summary>
    /// 盤面の進行を全クライアントで一致させるアクションストリーム。
    ///
    /// 「ゲームを進める決定」（<see cref="GameAction"/>）は必ず 1 人だけが決めて <see cref="Publish"/> し、
    /// ホストが唯一の順序付け役（sequencer）として全員へ再配信する。**決めた本人も含め、適用するのは
    /// 受信したアクションだけ**なので、全クライアントで適用順が一致する。
    ///
    /// 一人用モード（<see cref="GameMode.SinglePlayer"/>）でも同じストリームを通す（<see cref="Publish"/> が
    /// 即ローカルのキューへ積まれるだけ）。これで進行のコードパスがオンラインと一本化する。
    ///
    /// NGO の名前付きメッセージは接続確立時に 1 度だけ永続登録し、受信は <see cref="ActionStream"/> に
    /// バッファする（[docs/networking.md](../../../../docs/networking.md) 8 の恒久策）。
    ///
    /// 使うチャンネルは 3 本。**進行の決定だけがストリームに載る**。
    /// <list type="bullet">
    /// <item><c>SGRK_GameAction</c>: 進行の決定。ホストが通し番号（<see cref="GameAction.Seq"/>）を振って配る</item>
    /// <item><c>SGRK_Control</c>: 一時停止・復帰・取りこぼし申告。順序付けが要らないので即座に処理する</item>
    /// <item><c>SGRK_MiniGameProgress</c>: ミニゲームの途中経過（見た目だけの情報）</item>
    /// </list>
    ///
    /// 切断は「対戦の打ち切り」ではなく「一時停止して復帰を待つ」（<see cref="SessionReconnector.GraceSeconds"/> 秒）。
    /// 復帰したクライアントは受信済みの通し番号を申告し、ホストが <see cref="ActionLog"/> から
    /// 取りこぼしぶんだけを送り直すので、盤面をまるごと送らずに追いつける。
    /// </summary>
    public sealed class OnlineGameSync : IDisposable
    {
        // NGO の名前付きメッセージ名。ホストはクライアントからの発行を受けて再配信し、
        // クライアントはホストからの再配信だけを受ける（両方向で同じ名前を使ってよい）。
        private const string MessageName = "SGRK_GameAction";
        // ミニゲームの途中経過（見た目だけの情報）を流す別経路。アクションストリームと違い
        // 順序保証もキューも要らない（取りこぼしても次の値がすぐ来る）。
        private const string ProgressMessageName = "SGRK_MiniGameProgress";
        // 一時停止・復帰・取りこぼし申告を流す別経路。盤面を進める決定ではないのでストリームには載せず、
        // 受信側が即座に処理する（進行の待ち受けと順序を取り合わない）。
        private const string ControlMessageName = "SGRK_Control";

        private readonly GameSessionModel _gameSession;
        private readonly OnlineRosterSessionModel _roster;
        private readonly NgoMessenger _messenger;
        private readonly SessionReconnector _reconnector;
        private readonly ActionStream _stream = new();
        // ホストが配ったアクションの台帳（復帰したクライアントへ取りこぼしを送り直すのに使う）。
        private readonly ActionLog _log = new();
        // ホストが持つ「NGO のクライアント id → 席」。切断した相手の席を名前で出すために使う。
        private readonly Dictionary<ulong, int> _seatByClient = new();
        // 切断中に自分が発行したアクション（復帰したらこの順で送り直す）。
        private readonly Queue<GameAction> _pendingOutbound = new();
        // 接続が切れた（誰かが退出した）ことを UI へ伝える。
        private readonly ReactiveProperty<bool> _sessionLost = new(false);
        // 誰かの切断で進行を止めているか（復帰待ち）。UI は入力を止めて待機表示を出す。
        private readonly ReactiveProperty<bool> _paused = new(false);
        // 切断時に待機中の NextAsync を打ち切るためのトークン。
        private readonly CancellationTokenSource _abortCts = new();
        // 再接続・猶予待ちなど、このインスタンスの寿命に紐づく裏の処理を止めるトークン。
        private readonly CancellationTokenSource _lifeCts = new();

        // 受信済みの最後の通し番号（復帰時にここから先を送り直してもらう）。
        private int _lastSeenSeq = GameAction.NoSeq;
        // ホストが復帰を待っている間の猶予タイマー（復帰したらキャンセルする）。
        private CancellationTokenSource _graceCts;
        private bool _reconnecting;
        private bool _registered;
        private bool _disposed;

        public OnlineGameSync(
            GameSessionModel gameSession,
            OnlineRosterSessionModel roster,
            NgoMessenger messenger,
            SessionReconnector reconnector)
        {
            _gameSession = gameSession;
            _roster = roster;
            _messenger = messenger;
            _reconnector = reconnector;
        }

        /// <summary>オンライン対戦か（一人用モードなら false）。</summary>
        public bool IsOnline => _gameSession.Mode == GameMode.Online;

        /// <summary>自分の席（参加者 index）。一人用・ロースター未確定なら 0。</summary>
        public int MySeat => IsOnline && _roster.HasRoster ? _roster.MySeat : 0;

        /// <summary>接続が切れた（対戦の続行が不可能になった）か。</summary>
        public ReadOnlyReactiveProperty<bool> SessionLost => _sessionLost;

        /// <summary>
        /// 誰かの切断で進行を止めているか。立っている間は入力（スピン・アイテム）を受け付けず、
        /// 復帰（<see cref="GameActionType.Resume"/>）か猶予切れ（<see cref="SessionLost"/>）まで待つ。
        /// </summary>
        public ReadOnlyReactiveProperty<bool> Paused => _paused;

        /// <summary>一時停止の原因になった席（不明なら -1）。自分の席なら「自分が再接続中」。</summary>
        public int PausedSeat { get; private set; } = -1;

        /// <summary>復帰を待つ猶予（秒）。待機表示のカウントダウンに使う。</summary>
        public int PauseGraceSeconds { get; private set; } = SessionReconnector.GraceSeconds;

        /// <summary>
        /// 席 <paramref name="seat"/> の決定をこのクライアントが行うか。
        /// オンラインは自分の席だけ、一人用は全席（人間席は手動・CPU 席は自動）をローカルが決める。
        /// </summary>
        public bool IsLocalDecider(int seat)
        {
            return !IsOnline || seat == MySeat;
        }

        /// <summary>
        /// このクライアントがホストか（一人用モードは配る相手がいないので常に true＝自分がホスト扱い）。
        /// 席に紐づかない決定＝**誰が着地したかに関係なく 1 人だけが決めればよい抽選**
        /// （<see cref="GameActionType.CellMessage"/> のマスの文言）をホストに任せるために使う。
        /// </summary>
        public bool IsHost => !IsOnline || _gameSession.IsHost;

        /// <summary>
        /// 接続確立後に呼ぶ。名前付きメッセージのハンドラを登録し、切断を監視し始める。
        /// 復帰したときも（NGO が作り直されるので）登録し直す。一人用モードでは何もしない。
        /// </summary>
        public void OnConnected()
        {
            if (_disposed || !IsOnline)
            {
                return;
            }

            RegisterHandlers();

            NetworkManager nm = NetworkManager.Singleton;
            if (nm != null)
            {
                // 二重購読を避けるため、外してから付け直す（復帰時にも通る）。
                nm.OnClientDisconnectCallback -= OnClientDisconnected;
                nm.OnClientDisconnectCallback += OnClientDisconnected;
            }
            _gameSession.Leaving -= OnLeavingSession;
            _gameSession.Leaving += OnLeavingSession;

            // ゲストは「自分がここまで受け取った」をホストへ知らせる。初回はまだ 0 なので送り直しは起きず、
            // ホストが「この NGO クライアント id はこの席」と覚えるための挨拶として働く（切断時の名前表示に使う）。
            if (!_gameSession.IsHost)
            {
                SendControlToHost(GameAction.Resync(MySeat, _lastSeenSeq));
            }
        }

        /// <summary>
        /// 決定を発行する。オンラインのゲストはホストへ送るだけで、適用は再配信を受けてから行う
        /// （ホストと一人用は自分のキューへ直接積む）。
        /// 切断中は送れないのでキューに積んでおき、復帰したときにこの順で送り直す。
        /// </summary>
        public void Publish(GameAction action)
        {
            if (_disposed)
            {
                return;
            }

            if (!IsOnline)
            {
                Accept(action);
                return;
            }

            if (_gameSession.IsHost)
            {
                Broadcast(action);
                return;
            }

            if (_reconnecting)
            {
                // 切断中。復帰してからホストへ送る（ミニゲームの結果値などを取りこぼさないため）。
                _pendingOutbound.Enqueue(action);
                return;
            }

            try
            {
                _messenger.SendJson(MessageName, NetworkManager.ServerClientId, GameActionCodec.Encode(action));
            }
            catch (Exception e)
            {
                // 切断の検知（OnClientDisconnectCallback）より先に送信が失敗することがある。
                // 捨てると進行が止まるので、復帰後に送り直せるよう積んでおく。
                Debug.LogWarning($"アクションの送信に失敗しました。復帰後に送り直します: {e.Message}");
                _pendingOutbound.Enqueue(action);
            }
        }

        /// <summary>
        /// ミニゲームの途中経過（席 <paramref name="seat"/> の値 <paramref name="value"/>）を全員へ流す。
        /// **進行を進める決定ではない**ので <see cref="Publish"/> のストリームには載せない
        /// （ミニゲーム中は誰もストリームを読めないうえ、順序も再送も要らないため）。
        /// </summary>
        public void PublishProgress(int seat, int value)
        {
            if (_disposed || !IsOnline || _reconnecting)
            {
                return;
            }

            string json = GameActionCodec.Encode(GameAction.MiniGameScore(seat, value));
            if (_gameSession.IsHost)
            {
                SendProgressToOthers(json);
                return;
            }
            _messenger.SendJson(ProgressMessageName, NetworkManager.ServerClientId, json);
        }

        /// <summary>
        /// 他プレイヤーの途中経過を受け取ったときに呼ばれる（引数は 席・値）。
        /// ミニゲームのプレイ中だけ購読され、受け取った値は表示に使うだけ。
        /// </summary>
        public event Action<int, int> ProgressReceived;

        /// <summary>
        /// 次のアクションを取り出す。届くまで待つ（切断時はキャンセル例外で抜ける）。
        /// 同時に待てるのは 1 箇所だけ（<see cref="ActionStream"/> 参照）。
        /// </summary>
        public async UniTask<GameAction> NextAsync(CancellationToken ct)
        {
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(ct, _abortCts.Token);
            return await _stream.NextAsync(linked.Token);
        }

        // ホストの再配信。通し番号を振って台帳に残し、自分以外の全員へ送ってから自分のキューへ積む
        // （送信を先にするのは、ローカル適用が次のアクションを発行しても順序が入れ替わらないようにするため）。
        private void Broadcast(GameAction action)
        {
            GameAction numbered = _log.Append(action);
            try
            {
                _messenger.SendJsonToOthers(MessageName, GameActionCodec.Encode(numbered));
            }
            catch (Exception e)
            {
                // 送信に失敗しても自分の進行だけは止めない（相手の離脱は切断検知が拾う）。
                Debug.LogWarning($"アクションの再配信に失敗しました: {e.Message}");
            }
            Accept(numbered);
        }

        // 受信したアクションを適用キューへ積む。取りこぼしの送り直しで重複しても、
        // 通し番号で二重適用を弾く（採番前＝一人用の NoSeq はそのまま通す）。
        private void Accept(GameAction action)
        {
            if (action.Seq != GameAction.NoSeq)
            {
                if (action.Seq <= _lastSeenSeq)
                {
                    return;
                }
                _lastSeenSeq = action.Seq;
            }
            _stream.Push(action);
        }

        private void OnMessageReceived(ulong senderId, string json)
        {
            if (_disposed)
            {
                return;
            }
            if (!GameActionCodec.TryDecode(json, out GameAction action))
            {
                Debug.LogWarning($"不正なゲームアクションを受信しました: {json}");
                return;
            }

            // 退出通知は進行を進めるアクションではないので、キューに積まず即座に打ち切りへ回す。
            // 自分から抜けた人からの通知なので復帰は待たない。ゲスト同士には伝わらないため、
            // ホストは残りの全員へ中継してから打ち切る。
            if (action.Type == GameActionType.Leave)
            {
                if (_gameSession.IsHost)
                {
                    CancelGrace();
                    try
                    {
                        _messenger.SendJsonToOthers(MessageName, json, senderId);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"退出通知の中継に失敗しました: {e.Message}");
                    }
                }
                HandleSessionLost();
                return;
            }

            // ホストはクライアントからの発行を全員へ再配信する（順序付けはホストが一手に担う）。
            // クライアントはホストからの再配信をそのまま適用キューへ積む。
            if (_gameSession.IsHost)
            {
                _seatByClient[senderId] = action.Seat;
                Broadcast(action);
            }
            else
            {
                Accept(action);
            }
        }

        // 制御メッセージの受信。進行を進めないので、ストリームには積まずここで処理し切る。
        private void OnControlReceived(ulong senderId, string json)
        {
            if (_disposed || !GameActionCodec.TryDecode(json, out GameAction action))
            {
                return;
            }

            switch (action.Type)
            {
                case GameActionType.Pause:
                    SetPaused(true, action.Seat, action.GraceSeconds);
                    break;
                case GameActionType.Resume:
                    SetPaused(false, -1, SessionReconnector.GraceSeconds);
                    break;
                case GameActionType.Resync:
                    if (_gameSession.IsHost)
                    {
                        HandleResync(senderId, action);
                    }
                    break;
            }
        }

        /// <summary>
        /// ホストの復帰対応。申告された通し番号より後のアクションを送り直してから復帰を知らせる。
        /// 一時停止していれば解除も配る（ホストが切断に気づいていなかった場合でも復帰させられるよう、
        /// 一時停止の有無に依らず本人へは必ず <see cref="GameActionType.Resume"/> を返す）。
        /// </summary>
        private void HandleResync(ulong senderId, GameAction action)
        {
            _seatByClient[senderId] = action.Seat;

            IReadOnlyList<GameAction> missing = _log.Since(action.LastSeq);
            try
            {
                // 取りこぼしを配信順に送ってから復帰を知らせる（先に動かすと差分を適用する前に進んでしまう）。
                for (int i = 0; i < missing.Count; i++)
                {
                    _messenger.SendJson(MessageName, senderId, GameActionCodec.Encode(missing[i]));
                }
                _messenger.SendJson(
                    ControlMessageName, senderId, GameActionCodec.Encode(GameAction.Resume(action.Seat)));
            }
            catch (Exception e)
            {
                // 送り切れなかった。相手は復帰できずまた切断扱いになるので、猶予はそのまま走らせる。
                Debug.LogWarning($"取りこぼしの送り直しに失敗しました: {e.Message}");
                return;
            }

            if (missing.Count > 0)
            {
                Debug.Log($"席 {action.Seat} へ取りこぼし {missing.Count} 件を送り直しました。");
            }

            // 復帰したので猶予待ちを止めて、全員の一時停止を解く。
            CancelGrace();
            if (_paused.CurrentValue)
            {
                BroadcastControl(GameAction.Resume(action.Seat));
            }
        }

        // 途中経過の受信。ホストは受け取ったものを残りの全員へそのまま中継する（ゲスト同士は繋がっていないため）。
        private void OnProgressReceived(ulong senderId, string json)
        {
            if (_disposed || !GameActionCodec.TryDecode(json, out GameAction action))
            {
                return;
            }

            if (_gameSession.IsHost)
            {
                SendProgressToOthers(json, senderId);
            }
            ProgressReceived?.Invoke(action.Seat, action.MiniGameValue);
        }

        // 自分以外へ途中経過を送る。落ちても表示が一瞬遅れるだけなので、失敗は黙って捨てる。
        private void SendProgressToOthers(string json, ulong? exclude = null)
        {
            try
            {
                _messenger.SendJsonToOthers(ProgressMessageName, json, exclude);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 自分からセッションを抜ける直前（ホームに戻る・オプションの「タイトルへ戻る」）。
        /// 接続が閉じる前に退出を知らせて、相手に無駄な復帰待ち（最大 <see cref="SessionReconnector.GraceSeconds"/> 秒）を
        /// させないようにする。届かなくても猶予切れで終了に倒れるので、失敗は握りつぶしてよい。
        /// </summary>
        private void OnLeavingSession()
        {
            if (_disposed || !IsOnline)
            {
                return;
            }

            string json = GameActionCodec.Encode(GameAction.Leave(MySeat));
            try
            {
                if (_gameSession.IsHost)
                {
                    _messenger.SendJsonToOthers(MessageName, json);
                }
                else
                {
                    _messenger.SendJson(MessageName, NetworkManager.ServerClientId, json);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"退出通知の送信に失敗しました: {e.Message}");
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (_disposed)
            {
                return;
            }

            // 自分から離脱したとき（ホームに戻る・オプションの「タイトルへ戻る」）は、
            // 既にセッションを手放している。復帰させる相手はいないので打ち切るだけ。
            if (!_gameSession.HasSession)
            {
                HandleSessionLost();
                return;
            }

            NetworkManager nm = NetworkManager.Singleton;
            bool isSelf = nm == null || clientId == nm.LocalClientId;

            // ホストから見たゲストの切断。ゲスト同士には切断が伝わらない（NGO はクライアント同士を
            // 繋がない）ので、ホストが一時停止を配って全員で復帰を待つ。
            if (_gameSession.IsHost && !isSelf)
            {
                HostWaitForReturnAsync(SeatOfClient(clientId), clientId).Forget();
                return;
            }

            // 自分が切れた。セッションへ入り直して、取りこぼしたぶんを送り直してもらう。
            SelfReconnectAsync().Forget();
        }

        /// <summary>
        /// ホストが席 <paramref name="seat"/> の復帰を猶予いっぱい待つ。戻ってくれば
        /// <see cref="HandleResync"/> が猶予をキャンセルして再開させる。戻らなければ対戦を終了する。
        /// </summary>
        private async UniTaskVoid HostWaitForReturnAsync(int seat, ulong clientId)
        {
            if (_graceCts != null)
            {
                // 既に誰かの復帰を待っている（複数人が同時に切れた）。この対戦はもう成立しないので、
                // 先に始まっている猶予の満了に任せる。
                return;
            }

            _graceCts = new CancellationTokenSource();
            BroadcastControl(GameAction.Pause(seat, SessionReconnector.GraceSeconds));

            try
            {
                using CancellationTokenSource linked =
                    CancellationTokenSource.CreateLinkedTokenSource(_graceCts.Token, _lifeCts.Token);
                await UniTask.Delay(
                    TimeSpan.FromSeconds(SessionReconnector.GraceSeconds), cancellationToken: linked.Token);

                // 猶予切れ。残っている全員へ退出を知らせて打ち切る。
                _messenger.SendJsonToOthers(
                    MessageName, GameActionCodec.Encode(GameAction.Leave(seat)), clientId);
                HandleSessionLost();
            }
            catch (OperationCanceledException)
            {
                // 復帰した（HandleResync がキャンセル）か、シーン破棄。どちらも正常系。
            }
            finally
            {
                _graceCts?.Dispose();
                _graceCts = null;
            }
        }

        /// <summary>
        /// 自分の再接続。復帰できたらハンドラを張り直し（NGO が作り直されるため）、
        /// 受信済みの通し番号を申告して取りこぼしを送り直してもらう。
        /// 猶予いっぱい試しても戻れなければ対戦を終了する。
        /// </summary>
        private async UniTaskVoid SelfReconnectAsync()
        {
            if (_reconnecting)
            {
                return;
            }
            _reconnecting = true;
            _registered = false; // NGO が張り直されるので、登録済みの扱いを落とす
            SetPaused(true, MySeat, SessionReconnector.GraceSeconds);

            try
            {
                if (!await _reconnector.RunAsync(_lifeCts.Token))
                {
                    HandleSessionLost();
                    return;
                }

                RegisterHandlers();

                if (_gameSession.IsHost)
                {
                    // ホストは台帳の持ち主なので取りこぼしようがない（申告する相手もいない）。
                    // 自分で一時停止を解いて、残っている全員にも解除を配る。
                    BroadcastControl(GameAction.Resume(MySeat));
                }
                else
                {
                    // 取りこぼしを送り直してもらう。一時停止はホストの Resume を受けてから解く
                    // （差分が届く前に動かさないため）。
                    SendControlToHost(GameAction.Resync(MySeat, _lastSeenSeq));
                }
                FlushPendingOutbound();
            }
            catch (OperationCanceledException)
            {
                // シーン破棄は正常系。
            }
            finally
            {
                _reconnecting = false;
            }
        }

        // 切断中に溜めた発行ぶんをホストへ流す（発行した順を保つ）。
        // 途中で失敗したらそこで止めて残りは積んだままにする（順序が入れ替わらないように）。
        private void FlushPendingOutbound()
        {
            while (_pendingOutbound.Count > 0)
            {
                GameAction pending = _pendingOutbound.Peek();
                try
                {
                    _messenger.SendJson(
                        MessageName, NetworkManager.ServerClientId, GameActionCodec.Encode(pending));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"溜めていたアクションの送信に失敗しました: {e.Message}");
                    return;
                }
                _pendingOutbound.Dequeue();
            }
        }

        // ホストが覚えている「NGO のクライアント id → 席」。まだ何も受け取っていない相手は -1。
        private int SeatOfClient(ulong clientId)
        {
            return _seatByClient.TryGetValue(clientId, out int seat) ? seat : -1;
        }

        private void RegisterHandlers()
        {
            if (_registered)
            {
                return;
            }
            _messenger.RegisterJson(MessageName, OnMessageReceived);
            _messenger.RegisterJson(ProgressMessageName, OnProgressReceived);
            _messenger.RegisterJson(ControlMessageName, OnControlReceived);
            _registered = true;
        }

        private void SendControlToHost(GameAction action)
        {
            try
            {
                _messenger.SendJson(
                    ControlMessageName, NetworkManager.ServerClientId, GameActionCodec.Encode(action));
            }
            catch (Exception e)
            {
                // 届かなければ復帰を知らせられないが、進行は止めない（猶予切れで終了へ倒れる）。
                Debug.LogWarning($"制御メッセージの送信に失敗しました: {e.Message}");
            }
        }

        // ホストが制御メッセージを全員へ配る（自分にも適用する）。
        private void BroadcastControl(GameAction action)
        {
            try
            {
                _messenger.SendJsonToOthers(ControlMessageName, GameActionCodec.Encode(action));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"制御メッセージの配信に失敗しました: {e.Message}");
            }

            if (action.Type == GameActionType.Pause)
            {
                SetPaused(true, action.Seat, action.GraceSeconds);
            }
            else if (action.Type == GameActionType.Resume)
            {
                SetPaused(false, -1, SessionReconnector.GraceSeconds);
            }
        }

        private void SetPaused(bool paused, int seat, int graceSeconds)
        {
            if (_disposed || _sessionLost.CurrentValue)
            {
                return;
            }
            PausedSeat = seat;
            PauseGraceSeconds = graceSeconds;
            _paused.Value = paused;
        }

        private void CancelGrace()
        {
            _graceCts?.Cancel();
        }

        // 対戦の続行が不可能になった。待機中の進行を打ち切って UI へ知らせる。
        private void HandleSessionLost()
        {
            if (_disposed || _sessionLost.CurrentValue)
            {
                return;
            }

            // 自分から離脱したとき（オプションの「タイトルへ戻る」など）は、セッションを手放してから
            // NGO が閉じるので既に Session が null になっている。相手の退出ではないので
            // 「相手が退出しました」は出さず、待機中の進行を打ち切るだけにする
            // （離脱の完了を待つ間、自分の画面に退出通知が出てしまうのを防ぐ）。
            if (!_gameSession.HasSession)
            {
                _abortCts.Cancel();
                return;
            }

            // 先に打ち切りを立ててから一時停止を解く。順番が逆だと、解除の購読が
            // 「まだ決着していない」と見て閉じたはずの入力を戻してしまう。
            _sessionLost.Value = true;
            _paused.Value = false;
            _abortCts.Cancel();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            NetworkManager nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.OnClientDisconnectCallback -= OnClientDisconnected;
            }
            _gameSession.Leaving -= OnLeavingSession;
            if (_registered)
            {
                _messenger.Unregister(MessageName);
                _messenger.Unregister(ProgressMessageName);
                _messenger.Unregister(ControlMessageName);
                _registered = false;
            }
            ProgressReceived = null;

            _lifeCts.Cancel();
            _lifeCts.Dispose();
            _graceCts?.Cancel();
            _graceCts?.Dispose();
            _graceCts = null;
            _abortCts.Cancel();
            _abortCts.Dispose();
            _stream.Dispose();
            _sessionLost.Dispose();
            _paused.Dispose();
        }
    }
}
