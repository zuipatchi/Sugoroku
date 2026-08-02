using System;
using Cysharp.Threading.Tasks;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Common.GameSession
{
    public class GameSessionModel : IDisposable
    {
        public ISession Session { get; private set; }
        public bool HasSession => Session != null;
        public bool IsHost => Session?.IsHost ?? false;

        /// <summary>
        /// 参加中のセッション ID。<see cref="Session"/> と違い、切断で接続が切れても
        /// （<see cref="LeaveCurrentSessionAsync"/> で明示的に抜けるまで）保持し続ける。
        /// 再接続（<see cref="ReconnectAsync"/>）に必要。
        /// </summary>
        public string SessionId { get; private set; }

        /// <summary>作成/参加したルームの定員（<c>ISession.MaxPlayers</c>）。未接続時は null。
        /// Main 側（<c>GameParticipants</c>）が Multiplayer アセンブリに依存せず参加者数を決めるために公開する。</summary>
        public int? SessionMaxPlayers => Session?.MaxPlayers;

        /// <summary>現在のプレイ形態。既定はオンライン。</summary>
        public GameMode Mode { get; private set; } = GameMode.Online;

        /// <summary>
        /// セッションを離脱する直前に発火する（セッションを持っているときだけ）。
        /// 対戦中なら Main 側（<c>OnlineGameSync</c>）がこれを拾って「自分から抜ける」ことを相手へ知らせる。
        /// 知らせないと、相手は通信断と区別できず復帰を待ち続けてしまう。
        /// </summary>
        public event Action Leaving;

        public void SetSession(ISession session)
        {
            Session = session;
            SessionId = session?.Id;
            Mode = GameMode.Online;
        }

        /// <summary>
        /// 切断したセッションへ入り直す（対戦中の一時切断からの復帰）。SDK が Lobby への再参加と
        /// Relay / NGO の張り直しまで面倒を見るので、こちらで <c>StartClient</c> は呼ばない。
        /// 成功すると <see cref="Session"/> が新しいセッションに差し替わる。
        ///
        /// セッション ID が無い（そもそもオンラインでない・退出済み）ときと、ルームが既に消えている・
        /// メンバーから外されているときは false を返す。呼び出し側は諦めて対戦を終了させる。
        /// </summary>
        public async UniTask<bool> ReconnectAsync()
        {
            string reconnectingTo = SessionId;
            if (string.IsNullOrEmpty(reconnectingTo))
            {
                return false;
            }

            try
            {
                ISession session = await MultiplayerService.Instance
                    .ReconnectToSessionAsync(reconnectingTo)
                    .AsUniTask();

                if (SessionId != reconnectingTo)
                {
                    // 待っている間に自分から抜けた（ホームに戻る・タイトルへ戻る）。
                    // 入り直したぶんをそのままにするとルームに残ってしまうので畳む。
                    session?.LeaveAsync().AsUniTask().Forget(e => Debug.LogWarning($"Session 退出失敗: {e.Message}"));
                    return false;
                }

                // Mode / SessionId は据え置きたいので SetSession をそのまま使う（同じ ID で上書きされるだけ）。
                SetSession(session);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Session 再接続失敗: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 一人用モードを選択する。オンラインセッションは持たない。
        /// セッションが残っていれば離脱する（NGO の接続はセッションが握っているので、
        /// 放置すると一人用で遊んでいる間も Relay に繋がったままになる）。
        /// </summary>
        public void SetSinglePlayer()
        {
            ISession leaving = Session;
            if (leaving != null)
            {
                Leaving?.Invoke();
            }
            Session = null;
            // 明示的に抜けたので、もう戻る先は無い（再接続の対象から外す）。
            SessionId = null;
            Mode = GameMode.SinglePlayer;
            leaving?.LeaveAsync().AsUniTask().Forget(e => Debug.LogWarning($"Session 退出失敗: {e.Message}"));
        }

        /// <summary>
        /// 現在のセッションを離脱する（NGO の接続も一緒に閉じられる）。
        /// 参照は待つ前に手放すので、離脱中に呼び直されても二重に離脱しない。
        /// </summary>
        public async UniTask LeaveCurrentSessionAsync()
        {
            ISession leaving = Session;
            // 接続が切れていても（Session は null でも）ルーム自体は残るので、
            // 明示的な退出では必ず再接続の対象から外す。
            SessionId = null;
            if (leaving == null)
            {
                return;
            }
            // 接続を閉じる前に「自分から抜ける」ことを相手へ知らせる（復帰待ちをさせないため）。
            Leaving?.Invoke();
            Session = null;
            try
            {
                await leaving.LeaveAsync().AsUniTask();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Session 退出失敗: {e.Message}");
            }
        }

        public void Dispose()
        {
            Session?.LeaveAsync().AsUniTask().Forget(e => Debug.LogWarning($"Session 退出失敗: {e.Message}"));
            Session = null;
        }
    }
}
