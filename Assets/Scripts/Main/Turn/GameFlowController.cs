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
    /// 一人用モードは [Human, Cpu] の 1 対 1、オンラインは [Human] のみで従来どおり回る。
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
                    int player = _turn.CurrentPlayer.CurrentValue;
                    int result = await SpinForAsync(player, ct);
                    await _boardPresenter.AdvanceAsync(player, result, ct);

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

        private async UniTask<int> SpinForAsync(int player, CancellationToken ct)
        {
            // 手番開始時にルーレットを Idle へ戻し、前手番の Stopped を待ち受け対象から外す。
            _rouletteModel.Reset();

            if (_participants.KindOf(player) == PlayerKind.Human)
            {
                _roulette.SetInteractable(true);
                int value = await _roulette.WaitForManualSpinAsync(ct);
                _roulette.SetInteractable(false);
                return value;
            }

            // CPU：手動不可にして少し間を置いてから円盤を自動で回す。
            _roulette.SetInteractable(false);
            await UniTask.Delay(TimeSpan.FromSeconds(CpuThinkSeconds), cancellationToken: ct);
            return await _roulette.AutoSpinAsync(ct);
        }
    }
}
