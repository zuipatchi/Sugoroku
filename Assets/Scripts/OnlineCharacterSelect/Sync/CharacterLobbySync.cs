using System;
using System.Collections.Generic;
using System.Threading;
using Common.Board;
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
        private readonly BoardSessionModel _board;

        // UI へ公開するロック表（キャラ→所有者Id）。
        private readonly ReactiveProperty<IReadOnlyDictionary<CharacterId, string>> _locks =
            new(new Dictionary<CharacterId, string>());
        // ロビーに到着してポーリングを始めた人数（＝自分のプロパティを 1 度でも書いた人数）。
        // シーン遷移とカード絵のロードで到着に差が出るため、全員そろうまで選択させない判定に使う。
        private readonly ReactiveProperty<int> _presentCount = new(0);
        // 開始が確定したら true（ロースターは _roster へ保存済み）。
        private readonly ReactiveProperty<bool> _started = new(false);
        // 誰かが抜けた・セッションが失われた＝このロビーは成立しない（解散）。
        private readonly ReactiveProperty<bool> _sessionLost = new(false);

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

        public CharacterLobbySync(GameSessionModel gameSession, OnlineRosterSessionModel roster, BoardSessionModel board)
        {
            _gameSession = gameSession;
            _roster = roster;
            _board = board;
        }

        private ISession Session => _gameSession.Session;

        /// <summary>自分の UGS プレイヤーId。</summary>
        public string MyPlayerId { get; private set; }

        /// <summary>ロック表（キャラ→所有者Id）。UI のグレーアウトに使う。</summary>
        public ReadOnlyReactiveProperty<IReadOnlyDictionary<CharacterId, string>> Locks => _locks;

        /// <summary>ロビーに到着した人数（自分のプロパティを 1 度でも書いた人数）。</summary>
        public ReadOnlyReactiveProperty<int> PresentCount => _presentCount;

        /// <summary>そろうべき人数（ルーム定員）。未接続なら 0。</summary>
        public int ExpectedCount => Session?.MaxPlayers ?? 0;

        /// <summary>
        /// 全員がロビーに到着したか。到着前は誰のロックも集計されないため、
        /// 先に選んだ人がロックを得られないまま待ち続けることになる。そろうまで選択させない。
        /// </summary>
        public bool AllPresent => ExpectedCount > 0 && _presentCount.CurrentValue >= ExpectedCount;

        /// <summary>開始が確定したか。</summary>
        public ReadOnlyReactiveProperty<bool> Started => _started;

        /// <summary>
        /// 誰かがロビーから抜けた（またはセッションが失われた）か。抜けた人のキャラは永久に確定しないので、
        /// 全員が待ち続けることになる。立ったら <c>OnlineCharacterSelectPresenter</c> がロビーを解散する。
        /// 自分から退出したときは立てない（画面はもう遷移しているため）。
        /// </summary>
        public ReadOnlyReactiveProperty<bool> SessionLost => _sessionLost;

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

        /// <summary>
        /// 希望キャラを選ぶ（まだ決定はしない）。他人にロックされたキャラは選べない。
        /// 全員がロビーに到着するまで（<see cref="AllPresent"/>）と、解散後（<see cref="SessionLost"/>）は選べない。
        /// </summary>
        public void Select(CharacterId character)
        {
            if (!CanOperate || IsLockedByOther(character))
            {
                return;
            }
            _localSelection = character;
            _localReady = false;
        }

        /// <summary>現在の希望キャラで「決定」する（他人にロックされていなければ）。</summary>
        public void Confirm()
        {
            if (!CanOperate || !_localSelection.HasValue || IsLockedByOther(_localSelection.Value))
            {
                return;
            }
            _localReady = true;
        }

        /// <summary>
        /// 選択・決定を受け付けてよいか。全員そろう前は集計してもらえず、解散後はもう誰も集計しない。
        /// UI 側でもボタンを無効化するが、別経路から抜けられないようロジック側でも塞ぐ。
        /// </summary>
        private bool CanOperate => AllPresent && !_sessionLost.CurrentValue;

        /// <summary>現在のローカル選択（ハイライト用）。</summary>
        public CharacterId? CurrentSelection => _localSelection;

        /// <summary>決定済みか。</summary>
        public bool IsReady => _localReady;

        public void Initialize()
        {
            MyPlayerId = Session?.CurrentPlayer?.Id ?? string.Empty;
            ApplyInitialSelection();
        }

        /// <summary>
        /// 席順（参加順）ごとの初期キャラを自分の希望として置く（1P=のらどっく / 2P=ザニザニマン / …）。
        /// 席ごとにずらすので初期状態では誰とも被らず、そのまま「決定」するだけでも成立する。
        /// 全員そろう前に呼ぶため <see cref="Select"/> の受付制限（<see cref="AllPresent"/>）は通さない。
        /// </summary>
        private void ApplyInitialSelection()
        {
            ISession session = Session;
            if (session == null || string.IsNullOrEmpty(MyPlayerId))
            {
                return;
            }

            List<(string playerId, DateTime joined)> players = new(session.Players.Count);
            foreach (IReadOnlyPlayer player in session.Players)
            {
                players.Add((player.Id, player.Joined));
            }

            _localSelection = CharacterCatalog.DefaultFor(CharacterClaimResolver.SeatIndexOf(players, MyPlayerId));
            _localReady = false;
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
                        // 自分から退出してセッションを手放した。画面はもう遷移しているので解散通知は要らない。
                        return;
                    }

                    // (0) 誰かが抜けていたらロビーを解散する。ここは満室になってから来る画面なので、
                    // 人数が定員を割ったら退出とみなしてよい。抜けた人のキャラは永久に確定せず、
                    // 残った全員が「他のプレイヤーを待っています」から戻れなくなるため、待たずに畳む。
                    // ループ先頭は必ずメインスレッド（初回＝呼び出し元／2 回目以降＝Delay の再開）なのでそのまま通知してよい。
                    if (HasLeaver(session))
                    {
                        _sessionLost.Value = true;
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
                    _presentCount.Value = CountPresent(session);
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
                // ホストが抜けてセッションごと消えたときも、プロパティの読み書きが例外になってここへ来る。
                // 同期が止まった以上ロビーは成立しないので、ログを残して解散する。
                Debug.LogError($"キャラ選択ロビーの同期に失敗: {e}");
                await NotifySessionLostAsync(ct);
            }
        }

        /// <summary>
        /// 誰かがロビーから抜けたか。マッチングは満室（<c>AvailableSlots == 0</c>）になってから
        /// このロビーへ遷移するので、ロビーにいる間の人数は常に定員と等しいはず。
        /// </summary>
        private static bool HasLeaver(ISession session)
        {
            return session.MaxPlayers > 0 && session.PlayerCount < session.MaxPlayers;
        }

        /// <summary>
        /// 解散を UI へ知らせる。UGS の await 後は非メインスレッドになり得るため、
        /// Reactive を触る前にメインスレッドへ戻す。
        /// </summary>
        private async UniTask NotifySessionLostAsync(CancellationToken ct)
        {
            try
            {
                await UniTask.SwitchToMainThread(ct);
                _sessionLost.Value = true;
            }
            catch (OperationCanceledException)
            {
                // シーン破棄後は誰も見ていないので知らせなくてよい。
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

        /// <summary>
        /// ロビーに到着した人数を数える。到着した人は最初のポーリングで自分の希望キャラ（未選択なら -1）を
        /// 書くので、<see cref="CharKey"/> を持っているかどうかで「ロビーのループが動き出したか」が分かる。
        /// </summary>
        private static int CountPresent(ISession session)
        {
            int present = 0;
            foreach (IReadOnlyPlayer player in session.Players)
            {
                if (player.Properties != null && player.Properties.ContainsKey(CharKey))
                {
                    present++;
                }
            }
            return present;
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

            // ホストが選んだマップ（資産名）をゲストへ共有する。全員がこの識別子で Main の盤面を解決する。
            dto.board = _board.SelectedId;
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
            // ホストが選んだマップをゲストの Common セッションへも反映してから開始する
            // （ホスト側は既に自分の選択値なので冪等。空ならカタログ既定へフォールバックする）。
            _board.Select(dto.board);

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
            _presentCount.Dispose();
            _started.Dispose();
            _sessionLost.Dispose();
        }

        // JsonUtility 用のシリアライズ DTO。
        [Serializable]
        private sealed class LobbyStateDto
        {
            public List<LockDto> locks = new();
            public bool started;
            public List<SeatDto> roster = new();
            // ホストが選んだマップの識別子（資産名）。空ならカタログ既定へフォールバック。
            public string board = string.Empty;
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
