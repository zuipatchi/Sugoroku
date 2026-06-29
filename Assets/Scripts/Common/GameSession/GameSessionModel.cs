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

        public void SetSession(ISession session)
        {
            Session = session;
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
