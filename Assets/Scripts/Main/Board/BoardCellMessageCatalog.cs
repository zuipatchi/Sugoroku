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
    /// 抽選は各クライアントがローカルに行う（見た目だけで盤面の状態には影響しないので、オンラインでも
    /// アクションストリームに載せて配る必要がない）。乱数源は呼び出し側が渡し、null なら先頭を返して
    /// 決定的にふるまう（<see cref="Money.MoneyCellRule"/> と同じ規約）。
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
            return PickFrom(isStart ? StartMessages : Messages(cellEvent), rng);
        }

        /// <summary>
        /// <paramref name="catalog"/> 資産（未割り当てなら <see cref="BoardCellMessageDefaults"/> の既定文言）から
        /// 文言を 1 つ選ぶ。呼び出し側が資産の有無で分岐しなくて済むようにするための入り口。
        /// </summary>
        public static string Pick(
            BoardCellMessageCatalog catalog, BoardCellEvent cellEvent, bool isStart, System.Random rng)
        {
            return catalog != null
                ? catalog.Pick(cellEvent, isStart, rng)
                : PickFrom(BoardCellMessageDefaults.Pool(cellEvent, isStart), rng);
        }

        /// <summary>
        /// プールから 1 件選ぶ共通の規約。空なら null、乱数源が null なら先頭（決定的）。
        /// </summary>
        public static string PickFrom(IReadOnlyList<string> pool, System.Random rng)
        {
            if (pool == null || pool.Count == 0)
            {
                return null;
            }
            return pool[rng == null ? 0 : rng.Next(pool.Count)];
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
