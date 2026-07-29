using System;
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
    /// </summary>
    public sealed class OnlineGameSync : IDisposable
    {
        // NGO の名前付きメッセージ名。ホストはクライアントからの発行を受けて再配信し、
        // クライアントはホストからの再配信だけを受ける（両方向で同じ名前を使ってよい）。
        private const string MessageName = "SGRK_GameAction";

        private readonly GameSessionModel _gameSession;
        private readonly OnlineRosterSessionModel _roster;
        private readonly NgoMessenger _messenger;
        private readonly ActionStream _stream = new();
        // 接続が切れた（誰かが退出した）ことを UI へ伝える。
        private readonly ReactiveProperty<bool> _sessionLost = new(false);
        // 切断時に待機中の NextAsync を打ち切るためのトークン。
        private readonly CancellationTokenSource _abortCts = new();

        private bool _registered;
        private bool _disposed;

        public OnlineGameSync(
            GameSessionModel gameSession, OnlineRosterSessionModel roster, NgoMessenger messenger)
        {
            _gameSession = gameSession;
            _roster = roster;
            _messenger = messenger;
        }

        /// <summary>オンライン対戦か（一人用モードなら false）。</summary>
        public bool IsOnline => _gameSession.Mode == GameMode.Online;

        /// <summary>自分の席（参加者 index）。一人用・ロースター未確定なら 0。</summary>
        public int MySeat => IsOnline && _roster.HasRoster ? _roster.MySeat : 0;

        /// <summary>接続が切れた（対戦の続行が不可能になった）か。</summary>
        public ReadOnlyReactiveProperty<bool> SessionLost => _sessionLost;

        /// <summary>
        /// 席 <paramref name="seat"/> の決定をこのクライアントが行うか。
        /// オンラインは自分の席だけ、一人用は全席（人間席は手動・CPU 席は自動）をローカルが決める。
        /// </summary>
        public bool IsLocalDecider(int seat)
        {
            return !IsOnline || seat == MySeat;
        }

        /// <summary>
        /// 接続確立後に呼ぶ。名前付きメッセージのハンドラを 1 度だけ永続登録し、切断を監視し始める。
        /// 一人用モードでは何もしない。
        /// </summary>
        public void OnConnected()
        {
            if (_disposed || !IsOnline || _registered)
            {
                return;
            }

            _messenger.RegisterJson(MessageName, OnMessageReceived);
            _registered = true;

            NetworkManager nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.OnClientDisconnectCallback += OnClientDisconnected;
            }
        }

        /// <summary>
        /// 決定を発行する。オンラインのゲストはホストへ送るだけで、適用は再配信を受けてから行う
        /// （ホストと一人用は自分のキューへ直接積む）。
        /// </summary>
        public void Publish(GameAction action)
        {
            if (_disposed)
            {
                return;
            }

            if (!IsOnline)
            {
                _stream.Push(action);
                return;
            }

            if (_gameSession.IsHost)
            {
                Broadcast(action);
                return;
            }

            _messenger.SendJson(MessageName, NetworkManager.ServerClientId, GameActionCodec.Encode(action));
        }

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

        // ホストの再配信。自分以外の全員へ送ってから、自分のキューへ積む
        // （送信を先にするのは、ローカル適用が次のアクションを発行しても順序が入れ替わらないようにするため）。
        private void Broadcast(GameAction action)
        {
            try
            {
                _messenger.SendJsonToOthers(MessageName, GameActionCodec.Encode(action));
            }
            catch (Exception e)
            {
                // 送信に失敗しても自分の進行だけは止めない（相手の離脱は切断検知が拾う）。
                Debug.LogWarning($"アクションの再配信に失敗しました: {e.Message}");
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
            if (action.Type == GameActionType.Leave)
            {
                HandleSessionLost();
                return;
            }

            // ホストはクライアントからの発行を全員へ再配信する（順序付けはホストが一手に担う）。
            // クライアントはホストからの再配信をそのまま適用キューへ積む。
            if (_gameSession.IsHost)
            {
                Broadcast(action);
            }
            else
            {
                _stream.Push(action);
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (_disposed)
            {
                return;
            }

            // ホストではゲストの離脱、ゲストでは自分の切断としてここへ来る。
            // ゲスト同士には切断が伝わらない（NGO はクライアント同士を繋がない）ため、
            // ホストが残りの全員へ退出を知らせる。
            NetworkManager nm = NetworkManager.Singleton;
            if (_gameSession.IsHost && nm != null && clientId != nm.LocalClientId)
            {
                _messenger.SendJsonToOthers(
                    MessageName, GameActionCodec.Encode(GameAction.Leave(-1)), clientId);
            }

            HandleSessionLost();
        }

        // 対戦の続行が不可能になった。待機中の進行を打ち切って UI へ知らせる。
        private void HandleSessionLost()
        {
            if (_disposed || _sessionLost.Value)
            {
                return;
            }
            _sessionLost.Value = true;
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
            if (_registered)
            {
                _messenger.Unregister(MessageName);
                _registered = false;
            }

            _abortCts.Cancel();
            _abortCts.Dispose();
            _stream.Dispose();
            _sessionLost.Dispose();
        }
    }
}
