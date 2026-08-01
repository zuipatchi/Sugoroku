using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Main.Board;
using Main.Online;
using Main.Roulette;
using R3;
using UnityEngine;
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
    /// スピンは「回し始め（<see cref="GameActionType.SpinStart"/>）」と「停止位置の確定
    /// （<see cref="GameActionType.Spin"/>）」の 2 段で配る。前者で全員の円盤が同時に回り始め、
    /// 後者は円盤が止まる**前**（押下を離した瞬間）に配られるので、全員がほぼ同時に減速して止まる。
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

                    // スピンが流れてくるまで、先に届いたアクション（回し始めの合図・アイテム使用）を
                    // 捌きながら待つ。待っている間に決着したら（「勝利」アイテムなど）来ないスピンを待たずに抜ける。
                    GameAction? spinAction = await WaitForSpinAsync(decidedHere, ct);
                    if (spinAction == null)
                    {
                        break;
                    }

                    GameAction spin = spinAction.Value;
                    int advancing = RouletteMath.ParticipantForSector(spin.Sector, _participants.Count);
                    int steps = RouletteMath.StepsForSector(spin.Sector, _participants.Count);

                    // 自分で回した側の円盤は既に同じセクターへ向けて減速している。受信側だけ
                    // 同じセクター・同じ減速時間で止めて見せる（SpinStart で既に回っているならそのまま減速へ）。
                    if (!decidedHere)
                    {
                        await _roulette.PlaySpinToAsync(spin.Sector, spin.StopMillis / 1000f, ct);
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
        /// スピンのアクションが届くまで待つ。先に届いたアクションは捌いて待ち続ける。
        /// - 回し始めの合図（<see cref="GameActionType.SpinStart"/>）: 自分で回していない側は円盤を回し始める
        ///   （自分で回している側は既に回っているので読み飛ばす）。
        /// - アイテム使用など: 適用する（アイテムは自分の手番かつルーレット未回転のときだけ使えるので、
        ///   必ずスピンより前に流れてくる）。適用の結果として決着したら（「勝利」アイテム）、
        ///   来ないスピンを待たないよう null を返す。
        /// </summary>
        private async UniTask<GameAction?> WaitForSpinAsync(bool decidedHere, CancellationToken ct)
        {
            while (true)
            {
                GameAction action = await _sync.NextAsync(ct);
                if (action.Type == GameActionType.Spin)
                {
                    return action;
                }

                if (action.Type == GameActionType.SpinStart)
                {
                    if (!decidedHere)
                    {
                        _roulette.BeginRemoteSpin();
                    }
                    continue;
                }

                await _boardPresenter.ApplyActionAsync(action, ct);
                if (_board.IsFinished)
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// 手番の人のスピンをこのクライアントで駆動し、回し始めと停止位置をストリームへ発行する。
        /// 発行するだけで適用はしない（適用は受信側＝<see cref="StartAsync"/> のループが行う）。
        ///
        /// 停止位置は円盤が止まるのを待たず「押下を離した瞬間」（<see cref="RouletteModel.Decided"/>）に
        /// 発行する。これで他プレイヤーの円盤も同じタイミングで減速に入り、結果がほぼ同時に出る。
        /// </summary>
        private async UniTaskVoid DriveSpinAsync(int spinner, CancellationToken ct)
        {
            try
            {
                // 回し始める前に待ち受けを張る（押下→確定が先に走っても取りこぼさないように）。
                Task<SpinDecision> decided = _rouletteModel.Decided.FirstAsync(ct);
                PublishSpinStartAsync(spinner, ct).Forget();

                if (_participants.KindOf(spinner) == PlayerKind.Human)
                {
                    _roulette.SetInteractable(true);
                }
                else
                {
                    // CPU：手動不可にして少し間を置いてから円盤を自動で回す。
                    _roulette.SetInteractable(false);
                    await UniTask.Delay(TimeSpan.FromSeconds(CpuThinkSeconds), cancellationToken: ct);
                    await _roulette.AutoSpinAsync(ct);
                }

                SpinDecision decision = await decided;
                _roulette.SetInteractable(false);
                // 停止セクターは「進む人＋マス数」と 1 対 1 なので、セクターと減速時間だけを配ればよい。
                _sync.Publish(GameAction.Spin(
                    spinner, decision.Sector, Mathf.RoundToInt(decision.StopSeconds * 1000f)));
            }
            catch (OperationCanceledException)
            {
                // シーン破棄・切断によるキャンセルは正常終了として扱う。
            }
        }

        /// <summary>
        /// 円盤が回り始めたら（<see cref="RouletteState.Spinning"/>）その合図を配る。
        /// 手番の人が押した瞬間に他プレイヤーの円盤も回り出すので、結果待ちの間が空かない。
        /// </summary>
        private async UniTaskVoid PublishSpinStartAsync(int spinner, CancellationToken ct)
        {
            try
            {
                await _rouletteModel.State.Where(state => state == RouletteState.Spinning).FirstAsync(ct);
                _sync.Publish(GameAction.SpinStart(spinner));
            }
            catch (OperationCanceledException)
            {
                // シーン破棄・切断によるキャンセルは正常終了として扱う。
            }
        }
    }
}
