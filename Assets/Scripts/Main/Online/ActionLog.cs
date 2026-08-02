using System.Collections.Generic;

namespace Main.Online
{
    /// <summary>
    /// ホストが配ったアクションを配った順に覚えておく台帳。再接続してきたクライアントが
    /// 「seq N まで受け取った」と申告してきたら（<see cref="GameActionType.Resync"/>）、
    /// <see cref="Since"/> でそれ以降だけを取り出して送り直す。
    ///
    /// 盤面をまるごと送るスナップショットではなく「取りこぼした決定だけ」を送るので、
    /// 復帰側は通常と同じ受信経路（<see cref="ActionStream"/>）でそのまま適用でき、
    /// 演出や消費のコードパスを二重に持たずに済む。
    ///
    /// ホストだけが持つ（ゲストは採番も再送もしない）。1 手番あたり数個・値型の小さなアクションなので
    /// 対戦 1 回ぶんを丸ごと持っても問題にならない＝古いぶんを捨てないので、
    /// どれだけ長く切断していても取りこぼしを埋められる。純粋ロジックなので EditMode テストの対象。
    /// </summary>
    public sealed class ActionLog
    {
        private readonly List<GameAction> _actions = new();

        /// <summary>最後に振った通し番号（まだ 1 つも配っていなければ <see cref="GameAction.NoSeq"/>）。</summary>
        public int LastSeq { get; private set; } = GameAction.NoSeq;

        /// <summary>記録しているアクションの数。</summary>
        public int Count => _actions.Count;

        /// <summary>
        /// 次の通し番号を振って記録する。戻り値は採番済みのアクション（これを全員へ配る）。
        /// 採番はホストの再配信時に 1 度だけ行うので、番号は配信順そのものになる。
        /// </summary>
        public GameAction Append(GameAction action)
        {
            LastSeq++;
            GameAction numbered = action.WithSeq(LastSeq);
            _actions.Add(numbered);
            return numbered;
        }

        /// <summary>
        /// 通し番号が <paramref name="lastSeq"/> より後のアクションを配信順に返す
        /// （<paramref name="lastSeq"/> が 0 以下なら全部・取りこぼしが無ければ空）。
        /// </summary>
        public IReadOnlyList<GameAction> Since(int lastSeq)
        {
            List<GameAction> missing = new();
            for (int i = 0; i < _actions.Count; i++)
            {
                if (_actions[i].Seq > lastSeq)
                {
                    missing.Add(_actions[i]);
                }
            }
            return missing;
        }
    }
}
