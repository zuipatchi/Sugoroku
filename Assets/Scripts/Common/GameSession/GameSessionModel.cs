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

        /// <summary>作成/参加したルームの定員（<c>ISession.MaxPlayers</c>）。未接続時は null。
        /// Main 側（<c>GameParticipants</c>）が Multiplayer アセンブリに依存せず参加者数を決めるために公開する。</summary>
        public int? SessionMaxPlayers => Session?.MaxPlayers;

        /// <summary>現在のプレイ形態。既定はオンライン。</summary>
        public GameMode Mode { get; private set; } = GameMode.Online;

        public void SetSession(ISession session)
        {
            Session = session;
            Mode = GameMode.Online;
        }

        /// <summary>
        /// 一人用モードを選択する。オンラインセッションは持たない。
        /// セッションが残っていれば離脱する（NGO の接続はセッションが握っているので、
        /// 放置すると一人用で遊んでいる間も Relay に繋がったままになる）。
        /// </summary>
        public void SetSinglePlayer()
        {
            ISession leaving = Session;
            Session = null;
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
            if (leaving == null)
            {
                return;
            }
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
