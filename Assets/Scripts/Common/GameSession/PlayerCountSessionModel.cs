namespace Common.GameSession
{
    /// <summary>
    /// 一人用モードのプレイヤー人数（自分＋CPU の合計）をシーンをまたいで保持する Common シングルトン。
    /// MapSelect で <see cref="Min"/>〜<see cref="Max"/> の範囲で選び、Main の GameParticipants が
    /// 参加者リスト（[Human, Cpu, Cpu, ...]）の生成に使う。<see cref="Board.BoardSessionModel"/> の人数版。
    /// オンラインモードでは参加者数はマッチングで決まるためこの値は使わない。
    /// </summary>
    public sealed class PlayerCountSessionModel
    {
        /// <summary>選べる最小人数（自分＋CPU 1 人）。</summary>
        public const int Min = 2;

        /// <summary>選べる最大人数。キャラ総数（8）に収まる範囲。</summary>
        public const int Max = 8;

        /// <summary>選択中の人数（自分＋CPU の合計）。既定は <see cref="Min"/>。</summary>
        public int Count { get; private set; } = Min;

        /// <summary>人数を選ぶ。範囲外は <see cref="Min"/>〜<see cref="Max"/> にクランプする。</summary>
        public void Select(int count)
        {
            Count = count < Min ? Min : count > Max ? Max : count;
        }
    }
}
