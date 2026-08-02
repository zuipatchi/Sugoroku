using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;

namespace Main
{
    /// <summary>
    /// NGO の名前付きメッセージを JSON 文字列で送受信する薄いラッパー。
    /// ペイロードの型付けは上位層（<see cref="Online.GameActionCodec"/> など）に任せ、
    /// ここは「誰に送るか」「どう受けるか」だけを扱う。
    /// </summary>
    public class NgoMessenger : IDisposable
    {
        private readonly List<string> _registered = new();

        private CustomMessagingManager Messaging => NetworkManager.Singleton?.CustomMessagingManager;

        /// <summary>指定のクライアントへ JSON 文字列を送る。</summary>
        public void SendJson(string messageName, ulong targetClientId, string json)
        {
            CustomMessagingManager messaging = Messaging;
            if (messaging == null)
            {
                return;
            }
            // networking.md: Unicode 文字の最大バイト数を考慮して json.Length * 2 + 8 でバッファを確保する
            using FastBufferWriter writer = new(json.Length * 2 + 8, Allocator.Temp);
            writer.WriteValueSafe(json);
            messaging.SendNamedMessage(messageName, targetClientId, writer);
        }

        /// <summary>
        /// 自分以外の全接続クライアントへ JSON 文字列を送る（ホストの再配信用）。
        /// 自分ぶんは送らないので、呼び出し側がローカル適用を明示的に行う（適用順を確定させるため）。
        /// <paramref name="excludeClientId"/> を渡すとそのクライアントも除外する（退出直後の相手など）。
        /// </summary>
        public void SendJsonToOthers(string messageName, string json, ulong? excludeClientId = null)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || nm.CustomMessagingManager == null)
            {
                return;
            }
            foreach (ulong clientId in nm.ConnectedClientsIds)
            {
                if (clientId == nm.LocalClientId || clientId == excludeClientId)
                {
                    continue;
                }
                SendJson(messageName, clientId, json);
            }
        }

        /// <summary>
        /// JSON 文字列のまま受け取るハンドラを登録する。
        /// 同じ名前で登録し直すとハンドラは差し替わる（再接続で <c>CustomMessagingManager</c> が
        /// 作り直されたときに張り直すため）。解除用の控えは重複させない。
        /// </summary>
        public void RegisterJson(string messageName, Action<ulong, string> handler)
        {
            CustomMessagingManager messaging = Messaging;
            if (messaging == null)
            {
                return;
            }
            if (!_registered.Contains(messageName))
            {
                _registered.Add(messageName);
            }
            messaging.RegisterNamedMessageHandler(messageName, (senderId, reader) =>
            {
                reader.ReadValueSafe(out string json);
                handler(senderId, json);
            });
        }

        public void Unregister(string messageName)
        {
            _registered.Remove(messageName);
            Messaging?.UnregisterNamedMessageHandler(messageName);
        }

        public void Dispose()
        {
            CustomMessagingManager messaging = Messaging;
            if (messaging == null)
            {
                _registered.Clear();
                return;
            }
            foreach (string name in _registered)
            {
                messaging.UnregisterNamedMessageHandler(name);
            }
            _registered.Clear();
        }
    }
}
