using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Main.Board;
using Main.Roulette;
using R3;
using VContainer.Unity;

namespace Main.Turn
{
    /// <summary>
    /// ターン進行を統括する。手番に応じてルーレット（人間は手動・CPU は自動）を回し、
    /// 出た目ぶん手番プレイヤーのコマを進め、勝者が出るまで手番を巡回させる。
    /// これまで各 Presenter に散在していた「ルーレット停止 → コマ前進」の連鎖をここへ集約する。
    /// 一人用モードは [Human, Cpu, ...]、オンラインは接続した実プレイヤーぶんの [Human, Human, ...]（最低 2 人）で回る。
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

        public GameFlowController(
            GameParticipants participants,
            TurnModel turn,
            BoardModel board,
            RouletteModel rouletteModel,
            RoulettePresenter roulette,
            BoardPresenter boardPresenter,
            NetworkModel network)
        {
            _participants = participants;
            _turn = turn;
            _board = board;
            _rouletteModel = rouletteModel;
            _roulette = roulette;
            _boardPresenter = boardPresenter;
            _network = network;
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
                    RouletteOutcome outcome = await SpinForAsync(spinner, ct);
                    // ルーレットが消えてからコマを動かす（停止 → 出目を見せて非表示 → 前進）。
                    await _roulette.WaitForHideAsync(ct);
                    await _boardPresenter.AdvanceAsync(outcome.Player, outcome.Steps, ct);

                    if (_board.IsFinished)
                    {
                        break;
                    }
                    _turn.Next();
                }
            }
            catch (OperationCanceledException)
            {
                // シーン破棄によるキャンセルは正常終了として扱う（PlayMode テストの注意点参照）。
            }
        }

        private async UniTask<RouletteOutcome> SpinForAsync(int spinner, CancellationToken ct)
        {
            // 手番開始時にルーレットを Idle へ戻し、前手番の Stopped を待ち受け対象から外す。
            _rouletteModel.Reset();

            if (_participants.KindOf(spinner) == PlayerKind.Human)
            {
                _roulette.SetInteractable(true);
                RouletteOutcome outcome = await _roulette.WaitForManualSpinAsync(ct);
                _roulette.SetInteractable(false);
                return outcome;
            }

            // CPU：手動不可にして少し間を置いてから円盤を自動で回す。
            _roulette.SetInteractable(false);
            await UniTask.Delay(TimeSpan.FromSeconds(CpuThinkSeconds), cancellationToken: ct);
            return await _roulette.AutoSpinAsync(ct);
        }
    }
}
