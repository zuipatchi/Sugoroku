using System;
using System.Collections.Generic;
using UnityEngine;

namespace Main.Board
{
    /// <summary>
    /// マスに止まったときに見せる文言（フレーバーテキスト）のカタログ資産。
    /// イベント種別ごとに文言をいくつか持っておき、着地のたびに 1 つをランダムに選んで
    /// <see cref="BoardPresenter"/> がマス画像の下に表示する。
    ///
    /// 編集は「Window > Sugoroku > Cell Message Editor」（<c>CellMessageEditorWindow</c>）で行う。
    /// 資産にしてあるのは、文言を直すたびに再コンパイルが要らないようにするため
    /// （<see cref="BoardCatalog"/> と同じく <see cref="BoardPresenter"/> へインスペクタで割り当てる）。
    ///
    /// 未割り当てのときは <see cref="BoardCellMessageDefaults"/> の既定文言にフォールバックするので、
    /// 資産を作らなくても従来どおり動く。**割り当てたら資産の中身がそのまま出る**（プールを空にした
    /// マスでは文言が出ない＝意図的に消せる）。
    ///
    /// **オンラインでは抽選をホストが 1 回だけ行い、選んだ文言の index を配って全員が同じ文言を出す**
    /// （<see cref="Online.GameActionType.CellMessage"/>）。そのため抽選
    /// （<see cref="PickIndex(BoardCellEvent, bool, System.Random)"/>）と
    /// index からの取り出し（<see cref="MessageAt"/>）を分けてある（文字列そのものは配らない＝
    /// 全クライアントが同じ資産を持つので index だけで復元できる）。乱数源は呼び出し側が渡し、
    /// null なら先頭を返して決定的にふるまう（<see cref="Money.MoneyCellRule"/> と同じ規約）。
    /// </summary>
    [CreateAssetMenu(menuName = "Sugoroku/Cell Message Catalog", fileName = "BoardCellMessageCatalog")]
    public sealed class BoardCellMessageCatalog : ScriptableObject
    {
        /// <summary>イベント種別 1 つぶんの文言プール（資産に保存する単位）。</summary>
        [Serializable]
        private sealed class MessagePool
        {
            [SerializeField] private BoardCellEvent _event;
            [SerializeField] private List<string> _messages = new();

            public MessagePool(BoardCellEvent cellEvent, IReadOnlyList<string> messages)
            {
                _event = cellEvent;
                Assign(messages);
            }

            public BoardCellEvent Event => _event;

            public IReadOnlyList<string> Messages => _messages;

            public void Assign(IReadOnlyList<string> messages)
            {
                _messages.Clear();
                if (messages == null)
                {
                    return;
                }
                for (int i = 0; i < messages.Count; i++)
                {
                    _messages.Add(messages[i]);
                }
            }
        }

        // スタート＝ゴール（経路 index 0）はイベント種別ではなく位置で決まるので別に持つ。
        [SerializeField] private List<string> _startMessages = new();
        [SerializeField] private List<MessagePool> _pools = new();

        /// <summary>スタート＝ゴール（経路 index 0）の文言プール。</summary>
        public IReadOnlyList<string> StartMessages => _startMessages;

        /// <summary>イベント <paramref name="cellEvent"/> の文言プール（未登録なら空）。</summary>
        public IReadOnlyList<string> Messages(BoardCellEvent cellEvent)
        {
            MessagePool pool = FindPool(cellEvent);
            return pool != null ? pool.Messages : Array.Empty<string>();
        }

        /// <summary>
        /// 着地したマスで見せる文言を 1 つ選ぶ。<paramref name="isStart"/> が true（経路 index 0）なら
        /// イベント種別に依らずスタート専用プールから選ぶ。プールが空のときだけ null を返す。
        /// 乱数源 <paramref name="rng"/> が null のときはプールの先頭を返す（決定的）。
        /// </summary>
        public string Pick(BoardCellEvent cellEvent, bool isStart, System.Random rng)
        {
            return PickFrom(PoolOf(cellEvent, isStart), rng);
        }

        /// <summary>
        /// <paramref name="catalog"/> 資産（未割り当てなら <see cref="BoardCellMessageDefaults"/> の既定文言）から
        /// 文言を 1 つ選ぶ。呼び出し側が資産の有無で分岐しなくて済むようにするための入り口。
        /// </summary>
        public static string Pick(
            BoardCellMessageCatalog catalog, BoardCellEvent cellEvent, bool isStart, System.Random rng)
        {
            return PickFrom(PoolOf(catalog, cellEvent, isStart), rng);
        }

        /// <summary>
        /// プールから 1 件選ぶ共通の規約。空なら null、乱数源が null なら先頭（決定的）。
        /// </summary>
        public static string PickFrom(IReadOnlyList<string> pool, System.Random rng)
        {
            return At(pool, PickIndexFrom(pool, rng));
        }

        /// <summary>
        /// <see cref="PickFrom"/> と同じ抽選で、選んだ文言そのものではなく**プール内の index** を返す
        /// （空なら -1）。オンラインでホストが選んだ文言を配るために使う（文字列ではなく index を送る）。
        /// </summary>
        public static int PickIndexFrom(IReadOnlyList<string> pool, System.Random rng)
        {
            if (pool == null || pool.Count == 0)
            {
                return -1;
            }
            return rng == null ? 0 : rng.Next(pool.Count);
        }

        /// <summary>
        /// 着地したマスで見せる文言を 1 つ選び、**プール内の index** を返す（空なら -1）。
        /// 引数の意味は <see cref="Pick(BoardCellEvent, bool, System.Random)"/> と同じ。
        /// </summary>
        public int PickIndex(BoardCellEvent cellEvent, bool isStart, System.Random rng)
        {
            return PickIndexFrom(PoolOf(cellEvent, isStart), rng);
        }

        /// <summary>
        /// <paramref name="catalog"/> 資産（未割り当てなら既定文言）から文言を 1 つ選び、
        /// **プール内の index** を返す（空なら -1）。<see cref="MessageAt"/> と対で使う。
        /// </summary>
        public static int PickIndex(
            BoardCellMessageCatalog catalog, BoardCellEvent cellEvent, bool isStart, System.Random rng)
        {
            return PickIndexFrom(PoolOf(catalog, cellEvent, isStart), rng);
        }

        /// <summary>
        /// プールの <paramref name="index"/> 番目の文言（範囲外・空プールなら null）。
        /// <see cref="PickIndex(BoardCellMessageCatalog, BoardCellEvent, bool, System.Random)"/> で
        /// 選んだ index を全クライアントで同じ文言に戻すための取り出し口。
        /// </summary>
        public static string MessageAt(
            BoardCellMessageCatalog catalog, BoardCellEvent cellEvent, bool isStart, int index)
        {
            return At(PoolOf(catalog, cellEvent, isStart), index);
        }

        /// <summary>
        /// この資産で <paramref name="cellEvent"/>（スタート指定ならスタート専用）に使うプール。
        /// </summary>
        private IReadOnlyList<string> PoolOf(BoardCellEvent cellEvent, bool isStart)
        {
            return isStart ? StartMessages : Messages(cellEvent);
        }

        /// <summary>
        /// <paramref name="catalog"/>（未割り当てなら既定文言）で使うプール。抽選・取り出しの
        /// どちらも同じプールを見るように、資産の有無の分岐はここ 1 か所に閉じ込める。
        /// </summary>
        private static IReadOnlyList<string> PoolOf(
            BoardCellMessageCatalog catalog, BoardCellEvent cellEvent, bool isStart)
        {
            return catalog != null
                ? catalog.PoolOf(cellEvent, isStart)
                : BoardCellMessageDefaults.Pool(cellEvent, isStart);
        }

        /// <summary>プールの <paramref name="index"/> 番目（範囲外なら null）。</summary>
        private static string At(IReadOnlyList<string> pool, int index)
        {
            if (pool == null || index < 0 || index >= pool.Count)
            {
                return null;
            }
            return pool[index];
        }

        /// <summary>スタート専用プールを差し替える（エディタから編集するため）。</summary>
        public void SetStartMessages(IReadOnlyList<string> messages)
        {
            _startMessages.Clear();
            if (messages == null)
            {
                return;
            }
            for (int i = 0; i < messages.Count; i++)
            {
                _startMessages.Add(messages[i]);
            }
        }

        /// <summary>イベント <paramref name="cellEvent"/> のプールを差し替える（エディタから編集するため）。</summary>
        public void SetMessages(BoardCellEvent cellEvent, IReadOnlyList<string> messages)
        {
            MessagePool pool = FindPool(cellEvent);
            if (pool == null)
            {
                _pools.Add(new MessagePool(cellEvent, messages));
                return;
            }
            pool.Assign(messages);
        }

        /// <summary>
        /// 定義済みのイベント種別ぶんのプールを enum の並び順でそろえる（無いものは空で追加）。
        /// 新しい <see cref="BoardCellEvent"/> を足したときに、既存の資産でも編集欄が出るようにするためのもの。
        /// </summary>
        public void EnsurePools()
        {
            List<MessagePool> ordered = new();
            foreach (BoardCellEvent cellEvent in Enum.GetValues(typeof(BoardCellEvent)))
            {
                MessagePool pool = FindPool(cellEvent);
                ordered.Add(pool ?? new MessagePool(cellEvent, Array.Empty<string>()));
            }
            _pools = ordered;
        }

        /// <summary>すべてのプールを <see cref="BoardCellMessageDefaults"/> の既定文言で埋め直す。</summary>
        public void ResetToDefaults()
        {
            SetStartMessages(BoardCellMessageDefaults.StartMessages);
            _pools.Clear();
            foreach (BoardCellEvent cellEvent in Enum.GetValues(typeof(BoardCellEvent)))
            {
                _pools.Add(new MessagePool(cellEvent, BoardCellMessageDefaults.Messages(cellEvent)));
            }
        }

        private MessagePool FindPool(BoardCellEvent cellEvent)
        {
            for (int i = 0; i < _pools.Count; i++)
            {
                if (_pools[i] != null && _pools[i].Event == cellEvent)
                {
                    return _pools[i];
                }
            }
            return null;
        }

        // 資産を新規作成したときに Unity（エディタ）が呼ぶ。空の資産だと文言が 1 つも出なくなるので、
        // 既定文言を入れた状態から編集を始められるようにしておく。
        private void Reset()
        {
            ResetToDefaults();
        }
    }
}
