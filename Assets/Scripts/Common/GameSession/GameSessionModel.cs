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

        /// <summary>一人用モードを選択する。オンラインセッションは持たない。</summary>
        public void SetSinglePlayer()
        {
            Session = null;
            Mode = GameMode.SinglePlayer;
        }

        public async UniTask LeaveCurrentSessionAsync()
        {
            if (Session == null)
            {
                return;
            }
            try
            {
                await Session.LeaveAsync().AsUniTask();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Session 退出失敗: {e.Message}");
            }
            Session = null;
        }

        public void Dispose()
        {
            Session?.LeaveAsync().AsUniTask().Forget(e => Debug.LogWarning($"Session 退出失敗: {e.Message}"));
            Session = null;
        }
    }
}
