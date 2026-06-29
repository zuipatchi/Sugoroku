using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Main
{
    public class NgoMessenger : IDisposable
    {
        private readonly List<string> _registered = new();

        private CustomMessagingManager Messaging => NetworkManager.Singleton.CustomMessagingManager;

        public void Send<T>(string messageName, ulong targetClientId, T data)
        {
            string json = JsonUtility.ToJson(data);
            // networking.md: Unicode 文字の最大バイト数を考慮して json.Length * 2 + 8 でバッファを確保する
            using FastBufferWriter writer = new(json.Length * 2 + 8, Allocator.Temp);
            writer.WriteValueSafe(json);
            Messaging.SendNamedMessage(messageName, targetClientId, writer);
        }

        public void Register<T>(string messageName, Action<ulong, T> handler)
        {
            _registered.Add(messageName);
            Messaging.RegisterNamedMessageHandler(messageName, (senderId, reader) =>
            {
                reader.ReadValueSafe(out string json);
                T data = JsonUtility.FromJson<T>(json);
                handler(senderId, data);
            });
        }

        public void Unregister(string messageName)
        {
            _registered.Remove(messageName);
            Messaging.UnregisterNamedMessageHandler(messageName);
        }

        public void Dispose()
        {
            if (NetworkManager.Singleton?.CustomMessagingManager == null) return;
            foreach (string name in _registered)
            {
                Messaging.UnregisterNamedMessageHandler(name);
            }
            _registered.Clear();
        }
    }
}
