using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Main.Board;
using Main.Online;
using Main.Roulette;
using R3;
using VContainer.Unity;

namespace Main.Turn
{
    /// <summary>
    /// ターン進行を統括する。手番に応じてルーレット（人間は手動・CPU は自動）を回し、
    /// 出た目ぶん「進む人」のコマを進め、勝者が出るまで手番を巡回させる。
    ///
    /// 進行は <see cref="OnlineGameSync"/> のアクションストリームで駆動する。
    /// 手番の人だけがスピン結果を決めて発行し、**決めた本人も含め全員が受信したアクションを適用**するため、
    /// オンラインでも全クライアントで同じ順に同じ盤面が進む。一人用モードも同じ経路を通る
    /// （発行が即ローカルのキューへ積まれるだけ）。
    ///
    /// 一人用モードは [Human, Cpu, ...]、オンラインはルーム定員ぶんの [Human, Human, ...]（最低 2 人）で回る。
    /// </summary>
    public sealed class GameFlowController : IAsyncStartable
    {
        // CPU が回し始める前の「考える」間（秒）。
        private const float CpuThinkSeconds = 0.6f;

        private readonly GameParticipants _participants;
        private readonly TurnModel _turn;
        private readonly BoardModel _board;
        private readonly RouletteModel _rouletteModel;
        private readonly RoulettePresenter _roulette;
        private readonly BoardPresenter _boardPresenter;
        private readonly NetworkModel _network;
        private readonly OnlineGameSync _sync;

        public GameFlowController(
            GameParticipants participants,
            TurnModel turn,
            BoardModel board,
            RouletteModel rouletteModel,
            RoulettePresenter roulette,
            BoardPresenter boardPresenter,
            NetworkModel network,
            OnlineGameSync sync)
        {
            _participants = participants;
            _turn = turn;
            _board = board;
            _rouletteModel = rouletteModel;
            _roulette = roulette;
            _boardPresenter = boardPresenter;
            _network = network;
            _sync = sync;
        }

        public async UniTask StartAsync(CancellationToken ct)
        {
            try
            {
                // ネットワーク接続（一人用は即 Connected）を待ってから進行を始める。
                await _network.State.Where(state => state == NetworkState.Connected).FirstAsync(ct);

                while (!_board.IsFinished)
                {
                    // 手番プレイヤー＝スピンする人。進む人はルーレットが止まったキャラで決まる（自分を含む全参加者）。
                    int spinner = _turn.CurrentPlayer.CurrentValue;
                    // 手番開始時にルーレットを Idle へ戻し、前手番の Stopped を待ち受け対象から外す。
                    _rouletteModel.Reset();

                    // 決めるのは手番の人だけ。他のクライアントは回せないようにして結果を待つ。
                    bool decidedHere = _sync.IsLocalDecider(spinner);
                    if (decidedHere)
                    {
                        DriveSpinAsync(spinner, ct).Forget();
                    }
                    else
                    {
                        _roulette.SetInteractable(false);
                    }

                    // スピンが流れてくるまで、先に届いたアクション（アイテム使用）を適用しながら待つ。
                    // 待っている間に決着したら（「勝利」アイテムなど）来ないスピンを待たずに抜ける。
                    GameAction? spinAction = await WaitForSpinAsync(ct);
                    if (spinAction == null)
                    {
                        break;
                    }

                    GameAction spin = spinAction.Value;
                    int advancing = RouletteMath.ParticipantForSector(spin.Sector, _participants.Count);
                    int steps = RouletteMath.StepsForSector(spin.Sector, _participants.Count);

                    // 自分で回した側の円盤は既に止まっている。受信側だけ同じセクターまで回して見せる。
                    if (!decidedHere)
                    {
                        await _roulette.PlaySpinToAsync(spin.Sector, ct);
                    }

                    // ルーレットが消えてからコマを動かす（停止 → 出目を見せて非表示 → 前進）。
                    await _roulette.WaitForHideAsync(ct);
                    await _boardPresenter.AdvanceAsync(advancing, steps, ct);

                    if (_board.IsFinished)
                    {
                        break;
                    }
                    _turn.Next();
                }
            }
            catch (OperationCanceledException)
            {
                // シーン破棄・切断によるキャンセルは正常終了として扱う（PlayMode テストの注意点参照）。
            }
        }

        /// <summary>
        /// スピンのアクションが届くまで待つ。先にアイテム使用などのアクションが届いたら適用して待ち続ける
        /// （アイテムは自分の手番かつルーレット未回転のときだけ使えるので、必ずスピンより前に流れてくる）。
        /// 適用の結果として決着したら（「勝利」アイテム）、来ないスピンを待たないよう null を返す。
        /// </summary>
        private async UniTask<GameAction?> WaitForSpinAsync(CancellationToken ct)
        {
            while (true)
            {
                GameAction action = await _sync.NextAsync(ct);
                if (action.Type == GameActionType.Spin)
                {
                    return action;
                }

                await _boardPresenter.ApplyActionAsync(action, ct);
                if (_board.IsFinished)
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// 手番の人のスピンをこのクライアントで駆動し、止まったセクターをストリームへ発行する。
        /// 発行するだけで適用はしない（適用は受信側＝<see cref="StartAsync"/> のループが行う）。
        /// </summary>
        private async UniTaskVoid DriveSpinAsync(int spinner, CancellationToken ct)
        {
            try
            {
                RouletteOutcome outcome;
                if (_participants.KindOf(spinner) == PlayerKind.Human)
                {
                    _roulette.SetInteractable(true);
                    outcome = await _roulette.WaitForManualSpinAsync(ct);
                    _roulette.SetInteractable(false);
                }
                else
                {
                    // CPU：手動不可にして少し間を置いてから円盤を自動で回す。
                    _roulette.SetInteractable(false);
                    await UniTask.Delay(TimeSpan.FromSeconds(CpuThinkSeconds), cancellationToken: ct);
                    outcome = await _roulette.AutoSpinAsync(ct);
                }

                // 停止セクターは「進む人＋マス数」から一意に復元できるので、セクター 1 つだけを配ればよい。
                int sector = RouletteMath.SectorFor(outcome.Player, outcome.Steps, _participants.Count);
                _sync.Publish(GameAction.Spin(spinner, sector));
            }
            catch (OperationCanceledException)
            {
                // シーン破棄・切断によるキャンセルは正常終了として扱う。
            }
        }
    }
}
