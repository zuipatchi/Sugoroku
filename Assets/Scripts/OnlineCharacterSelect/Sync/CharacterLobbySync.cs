using System;
using System.Collections.Generic;
using System.Threading;
using Common.Character;
using Common.GameSession;
using Cysharp.Threading.Tasks;
using R3;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace OnlineCharacterSelect.Sync
{
    /// <summary>
    /// オンラインのキャラ選択ロビーの状態同期。UGS セッションの
    /// プレイヤープロパティ（各自の希望キャラ・決定フラグ）とセッション共有プロパティ
    /// （ホストが書くロック表・開始ロースター）を使って「被らないキャラ選択」を成立させる。
    /// ホストが審判となり <see cref="CharacterClaimResolver"/> で先着ロックを決めて全員へ共有する。
    /// NGO は使わない（NetworkManager は Main でしか存在しないため）。反映は既存の相手待ち
    /// （<c>MatchingService.WaitForPlayerAsync</c>）と同じくライブなセッションオブジェクトのポーリングで行う。
    /// </summary>
    public sealed class CharacterLobbySync : IDisposable
    {
        // プレイヤープロパティ（各自が自分のぶんだけ書く）。
        private const string CharKey = "char";
        private const string ReadyKey = "ready";
        // セッション共有プロパティ（ホストだけが書く）。
        private const string StateKey = "lobbyState";

        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(600);

        private readonly GameSessionModel _gameSession;
        private readonly OnlineRosterSessionModel _roster;

        // UI へ公開するロック表（キャラ→所有者Id）。
        private readonly ReactiveProperty<IReadOnlyDictionary<CharacterId, string>> _locks =
            new(new Dictionary<CharacterId, string>());
        // 開始が確定したら true（ロースターは _roster へ保存済み）。
        private readonly ReactiveProperty<bool> _started = new(false);

        // 自分のローカル選択状態（プレゼンターが更新）。ポーリングで差分があればプロパティへ書く。
        private CharacterId? _localSelection;
        private bool _localReady;
        private CharacterId? _writtenSelection;
        private bool _writtenReady;
        private bool _hasWritten;

        // ホストのロック状態（sticky）。
        private Dictionary<CharacterId, string> _hostLocks = new();
        private bool _hostStarted;
        private string _lastWrittenState;
        private string _lastAppliedState;

        public CharacterLobbySync(GameSessionModel gameSession, OnlineRosterSessionModel roster)
        {
            _gameSession = gameSession;
            _roster = roster;
        }

        private ISession Session => _gameSession.Session;

        /// <summary>自分の UGS プレイヤーId。</summary>
        public string MyPlayerId { get; private set; }

        /// <summary>ロック表（キャラ→所有者Id）。UI のグレーアウトに使う。</summary>
        public ReadOnlyReactiveProperty<IReadOnlyDictionary<CharacterId, string>> Locks => _locks;

        /// <summary>開始が確定したか。</summary>
        public ReadOnlyReactiveProperty<bool> Started => _started;

        /// <summary>キャラ <paramref name="character"/> が自分以外にロックされているか。</summary>
        public bool IsLockedByOther(CharacterId character)
        {
            return _locks.CurrentValue.TryGetValue(character, out string owner) && owner != MyPlayerId;
        }

        /// <summary>自分の希望キャラが自分にロックできているか（決定ボタンの有効判定に使う）。</summary>
        public bool OwnsSelection()
        {
            return _localSelection.HasValue
                   && _locks.CurrentValue.TryGetValue(_localSelection.Value, out string owner)
                   && owner == MyPlayerId;
        }

        /// <summary>希望キャラを選ぶ（まだ決定はしない）。他人にロックされたキャラは選べない。</summary>
        public void Select(CharacterId character)
        {
            if (IsLockedByOther(character))
            {
                return;
            }
            _localSelection = character;
            _localReady = false;
        }

        /// <summary>現在の希望キャラで「決定」する（他人にロックされていなければ）。</summary>
        public void Confirm()
        {
            if (!_localSelection.HasValue || IsLockedByOther(_localSelection.Value))
            {
                return;
            }
            _localReady = true;
        }

        /// <summary>現在のローカル選択（ハイライト用）。</summary>
        public CharacterId? CurrentSelection => _localSelection;

        /// <summary>決定済みか。</summary>
        public bool IsReady => _localReady;

        public void Initialize()
        {
            MyPlayerId = Session?.CurrentPlayer?.Id ?? string.Empty;
        }

        /// <summary>成立するまで（または破棄まで）ポーリングし続ける。</summary>
        public async UniTask RunLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && !_started.Value)
                {
                    ISession session = Session;
                    if (session == null)
                    {
                        return;
                    }

                    // (1) 自分のローカル選択に変化があればプレイヤープロパティへ書く（ポーリングで throttle）。
                    await FlushLocalSelectionAsync(session, ct);

                    // (2) 全員の希望を読む。
                    IReadOnlyList<PlayerChoice> choices = ReadChoices(session);

                    // (3) ホストなら先着ロックを更新し、開始条件を満たせばロースターを確定して共有する。
                    if (session.IsHost)
                    {
                        await HostArbitrateAsync(session, choices, ct);
                    }

                    // (4) 共有された状態を読んで UI・開始へ反映する。
                    // UGS の await 後は非メインスレッドになり得るため、Reactive/UI 更新前にメインスレッドへ戻す。
                    await UniTask.SwitchToMainThread(ct);
                    ApplySharedState(session);

                    await UniTask.Delay(PollInterval, cancellationToken: ct);
                }
            }
            catch (OperationCanceledException)
            {
                // シーン破棄時のキャンセルは正常系。
            }
            catch (Exception e)
            {
                Debug.LogError($"キャラ選択ロビーの同期に失敗: {e}");
            }
        }

        private async UniTask FlushLocalSelectionAsync(ISession session, CancellationToken ct)
        {
            if (_hasWritten && _writtenSelection == _localSelection && _writtenReady == _localReady)
            {
                return;
            }

            IPlayer me = session.CurrentPlayer;
            int value = _localSelection.HasValue ? (int)_localSelection.Value : -1;
            me.SetProperty(CharKey, new PlayerProperty(value.ToString()));
            me.SetProperty(ReadyKey, new PlayerProperty(_localReady ? "1" : "0"));
            await session.SaveCurrentPlayerDataAsync().AsUniTask().AttachExternalCancellation(ct);

            _writtenSelection = _localSelection;
            _writtenReady = _localReady;
            _hasWritten = true;
        }

        private static IReadOnlyList<PlayerChoice> ReadChoices(ISession session)
        {
            List<PlayerChoice> choices = new(session.Players.Count);
            foreach (IReadOnlyPlayer player in session.Players)
            {
                CharacterId? desired = null;
                bool ready = false;
                if (player.Properties != null)
                {
                    if (player.Properties.TryGetValue(CharKey, out PlayerProperty charProp)
                        && int.TryParse(charProp?.Value, out int charInt) && charInt >= 0)
                    {
                        desired = (CharacterId)charInt;
                    }
                    if (player.Properties.TryGetValue(ReadyKey, out PlayerProperty readyProp))
                    {
                        ready = readyProp?.Value == "1";
                    }
                }
                choices.Add(new PlayerChoice(player.Id, desired, ready));
            }
            return choices;
        }

        private async UniTask HostArbitrateAsync(ISession session, IReadOnlyList<PlayerChoice> choices, CancellationToken ct)
        {
            _hostLocks = new Dictionary<CharacterId, string>(
                (IDictionary<CharacterId, string>)CharacterClaimResolver.ResolveLocks(_hostLocks, choices));

            if (!_hostStarted
                && CharacterClaimResolver.AllSettled(choices, _hostLocks, session.MaxPlayers))
            {
                _hostStarted = true;
            }

            string json = BuildStateJson(session);
            if (json == _lastWrittenState)
            {
                return;
            }

            IHostSession host = session.AsHost();
            host.SetProperty(StateKey, new SessionProperty(json));
            await host.SavePropertiesAsync().AsUniTask().AttachExternalCancellation(ct);
            _lastWrittenState = json;
        }

        private string BuildStateJson(ISession session)
        {
            LobbyStateDto dto = new();
            foreach (KeyValuePair<CharacterId, string> pair in _hostLocks)
            {
                dto.locks.Add(new LockDto { ch = (int)pair.Key, player = pair.Value });
            }

            dto.started = _hostStarted;
            if (_hostStarted)
            {
                // 確定ロック（キャラ→所有者）から席順ロースターを組む。所有者の参加時刻で並べる。
                List<(string, DateTime, CharacterId)> settled = new(_hostLocks.Count);
                foreach (KeyValuePair<CharacterId, string> pair in _hostLocks)
                {
                    DateTime joined = JoinedOf(session, pair.Value);
                    settled.Add((pair.Value, joined, pair.Key));
                }
                IReadOnlyList<RosterSeat> roster = CharacterClaimResolver.BuildRoster(settled);
                foreach (RosterSeat seat in roster)
                {
                    dto.roster.Add(new SeatDto { ch = (int)seat.Character, player = seat.PlayerId });
                }
            }

            return JsonUtility.ToJson(dto);
        }

        private static DateTime JoinedOf(ISession session, string playerId)
        {
            foreach (IReadOnlyPlayer player in session.Players)
            {
                if (player.Id == playerId)
                {
                    return player.Joined;
                }
            }
            return DateTime.MinValue;
        }

        private void ApplySharedState(ISession session)
        {
            if (session.Properties == null
                || !session.Properties.TryGetValue(StateKey, out SessionProperty stateProp)
                || string.IsNullOrEmpty(stateProp?.Value))
            {
                return;
            }

            // 共有状態が変わっていなければ Reactive/UI を触らない（毎ティックの無駄な再構築を避ける）。
            if (stateProp.Value == _lastAppliedState)
            {
                return;
            }
            _lastAppliedState = stateProp.Value;

            LobbyStateDto dto = JsonUtility.FromJson<LobbyStateDto>(stateProp.Value);
            if (dto == null)
            {
                return;
            }

            Dictionary<CharacterId, string> locks = new();
            if (dto.locks != null)
            {
                foreach (LockDto lockDto in dto.locks)
                {
                    locks[(CharacterId)lockDto.ch] = lockDto.player;
                }
            }
            _locks.Value = locks;

            if (dto.started && dto.roster != null && dto.roster.Count > 0)
            {
                StoreRosterAndStart(dto);
            }
        }

        private void StoreRosterAndStart(LobbyStateDto dto)
        {
            List<CharacterId> seats = new(dto.roster.Count);
            int mySeat = 0;
            for (int i = 0; i < dto.roster.Count; i++)
            {
                seats.Add((CharacterId)dto.roster[i].ch);
                if (dto.roster[i].player == MyPlayerId)
                {
                    mySeat = i;
                }
            }
            _roster.SetRoster(seats, mySeat);
            _started.Value = true;
        }

        public void Dispose()
        {
            _locks.Dispose();
            _started.Dispose();
        }

        // JsonUtility 用のシリアライズ DTO。
        [Serializable]
        private sealed class LobbyStateDto
        {
            public List<LockDto> locks = new();
            public bool started;
            public List<SeatDto> roster = new();
        }

        [Serializable]
        private sealed class LockDto
        {
            public int ch;
            public string player;
        }

        [Serializable]
        private sealed class SeatDto
        {
            public int ch;
            public string player;
        }
    }
}
