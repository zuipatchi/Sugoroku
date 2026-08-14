namespace Main.Roulette
{
    /// <summary>
    /// 1 回のスピンで確定した「止まり方」。押下を離した（＝減速に入った）**瞬間**に決まり、
    /// 円盤が実際に止まるのを待たずに確定できる（<see cref="RouletteSpinPhysics.PredictStopRotation"/>）。
    ///
    /// オンラインではこれを回り終わる前に配ることで、他プレイヤーの円盤も同じタイミングで減速に入り、
    /// 結果がほぼ同時に出る。進む参加者と出目は <see cref="Sector"/> から
    /// （<see cref="RouletteMath.ParticipantForSector"/>／<see cref="RouletteNumberLayout.StepsForSector"/> で）導ける。
    /// </summary>
    public readonly struct SpinDecision
    {
        /// <summary>止まるセクターの index。</summary>
        public readonly int Sector;

        /// <summary>離してから止まるまでの時間（秒）。受信側も同じ時間で減速させて止まる瞬間をそろえる。</summary>
        public readonly float StopSeconds;

        public SpinDecision(int sector, float stopSeconds)
        {
            Sector = sector;
            StopSeconds = stopSeconds;
        }
    }
}
