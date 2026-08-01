using System;

namespace Common.MiniGame
{
    /// <summary>
    /// オンライン対戦のミニゲームで、プレイ中の途中経過（タップ連打なら連打数）を配り合うための細い経路。
    ///
    /// 途中経過は**進行を進める決定ではなく見た目だけの情報**なので、アクションストリーム
    /// （<c>OnlineGameSync</c>・全順序＋同時に待てるのは 1 箇所）には載せない。取りこぼしても
    /// 次の値がすぐ来るし、ミニゲーム中は誰もストリームを読めないため、そもそも噛み合わない。
    ///
    /// 起動側（Main）が実際の送受信を差し込み、ミニゲーム側は <see cref="Publish"/> で自分の値を出し、
    /// <see cref="Values"/> を毎フレーム読んで相手の表示を更新する（ポーリングなので取りこぼしに強い）。
    /// </summary>
    public sealed class MiniGameProgressChannel
    {
        private readonly Action<int> _publish;

        public MiniGameProgressChannel(int participantCount, Action<int> publish)
        {
            Values = new int[participantCount < 1 ? 1 : participantCount];
            _publish = publish;
        }

        /// <summary>参加者ごとの最新の途中経過（index＝ミニゲーム内の参加者・0＝自分）。</summary>
        public int[] Values { get; }

        /// <summary>自分の途中経過を配る。</summary>
        public void Publish(int value)
        {
            if (Values.Length > 0)
            {
                Values[0] = value;
            }
            _publish?.Invoke(value);
        }

        /// <summary>受信した他プレイヤーの途中経過を書き込む（範囲外は無視）。</summary>
        public void Apply(int participant, int value)
        {
            if (participant > 0 && participant < Values.Length)
            {
                Values[participant] = value;
            }
        }
    }
}
