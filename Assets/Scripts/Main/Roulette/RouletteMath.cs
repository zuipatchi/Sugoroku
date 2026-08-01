using System;

namespace Main.Roulette
{
    /// <summary>
    /// ルーレットの「円盤の回転角度」から「針の真下にあるセクター」を求める純粋関数群と、
    /// セクターへの「参加者」「出目（マス数）」の割り当てロジック。
    ///
    /// 角度の規約:
    /// - 円盤は時計回りに回転する（UI Toolkit の <see cref="UnityEngine.UIElements.Rotate"/> 正値と一致）。
    /// - 針は画面上部（12 時方向）に固定。
    /// - セクター i（0 始まり）は、回転 0 のとき上部から時計回りに
    ///   [i * sectorAngle, (i+1) * sectorAngle) の範囲を占める。
    ///
    /// 割り当ての規約（「止まったキャラが進む」方式）:
    /// - 円盤は参加者を均等に並べる。セクター総数 = 参加者数 × K（K = 1 キャラあたりの数字枚数）。
    /// - セクター i の参加者は <see cref="ParticipantForSector"/> ＝ i を参加者数で巡回（ラウンドロビン）。
    /// - セクター i の出目（進むマス数）は <see cref="StepsForSector"/> ＝ (i ÷ 参加者数) + 1。
    ///   これにより各参加者はちょうど同じ数字セット 1〜K を 1 枚ずつ持つ（数字も全キャラ同じ）。
    /// - この割り当ては「セクター ↔ (進む人, 出目)」が 1 対 1 なので、オンラインでは
    ///   **停止セクターの整数 1 つ**を配れば全員が同じ結果を復元できる。
    /// </summary>
    public static class RouletteMath
    {
        /// <summary>1 セクターあたりの中心角（度）。</summary>
        public static float SectorAngle(int count)
        {
            return 360f / count;
        }

        /// <summary>
        /// 円盤のセクター総数。参加者を均等に並べ、各参加者が数字 1〜<paramref name="numbersPerParticipant"/> を
        /// 1 枚ずつ持つよう、参加者数 × K を返す。どちらも最低 1 に丸める。
        /// </summary>
        public static int SectorCount(int participantCount, int numbersPerParticipant)
        {
            int participants = participantCount < 1 ? 1 : participantCount;
            int perParticipant = numbersPerParticipant < 1 ? 1 : numbersPerParticipant;
            return participants * perParticipant;
        }

        /// <summary>
        /// 回転角度 <paramref name="rotationDegrees"/> のとき、針の真下にあるセクターの index（0 始まり）。
        /// </summary>
        public static int ResultFromRotation(float rotationDegrees, int count)
        {
            float sector = SectorAngle(count);
            // 円盤を rotation 回した後の上部は、円盤ローカルでは -rotation の位置に当たる。
            float local = Mod(-rotationDegrees, 360f);
            int index = (int)Math.Floor(local / sector);
            if (index < 0)
            {
                index = 0;
            }
            if (index >= count)
            {
                index = count - 1;
            }
            return index;
        }

        /// <summary>
        /// セクター <paramref name="sectorIndex"/>（0 始まり）に割り当てる参加者 index。
        /// 参加者を巡回（ラウンドロビン）で均等に並べる（セクター i → i % 参加者数）。
        /// </summary>
        public static int ParticipantForSector(int sectorIndex, int participantCount)
        {
            int participants = participantCount < 1 ? 1 : participantCount;
            return ((sectorIndex % participants) + participants) % participants;
        }

        /// <summary>
        /// セクター <paramref name="sectorIndex"/>（0 始まり）の出目（進むマス数）。
        /// 参加者ごとに同じ数字セット 1〜K を持つよう、(sectorIndex ÷ 参加者数) + 1 を返す。
        /// </summary>
        public static int StepsForSector(int sectorIndex, int participantCount)
        {
            int participants = participantCount < 1 ? 1 : participantCount;
            int wrapped = sectorIndex < 0 ? 0 : sectorIndex;
            return (wrapped / participants) + 1;
        }

        /// <summary>
        /// セクター <paramref name="sectorIndex"/> の中心が針の真下に来る回転角（0 以上 360 未満）。
        /// <see cref="ResultFromRotation"/> にこの角度を渡すと <paramref name="sectorIndex"/> が返る。
        /// </summary>
        public static float RotationForSectorCenter(int sectorIndex, int count)
        {
            return Mod(-((sectorIndex + 0.5f) * SectorAngle(count)), 360f);
        }

        /// <summary>
        /// 回転角 <paramref name="rotation"/> にいちばん近い、セクター <paramref name="sectorIndex"/> の中心の回転角。
        /// 自然に減速したときの停止角（予測値）をセクターの真ん中へ寄せて、
        /// 「どのセクターで止まるか」を減速に入る時点で確定させるために使う。
        /// </summary>
        public static float NearestRotationForSectorCenter(float rotation, int sectorIndex, int count)
        {
            float center = RotationForSectorCenter(sectorIndex, count);
            // 現在角との差を [-180, 180) に畳んで、前後どちらでも近いほうの中心へ寄せる。
            float delta = Mod(center - Mod(rotation, 360f) + 180f, 360f) - 180f;
            return rotation + delta;
        }

        /// <summary>
        /// 現在の回転角 <paramref name="currentRotation"/> から前方（時計回り）へ回して、
        /// セクター <paramref name="sectorIndex"/> の中心で止まる回転角を返す。
        /// <paramref name="extraTurns"/> 周ぶん余分に回して自然な見た目にする（常に現在角以上を返す）。
        /// </summary>
        public static float NextRotationFor(float currentRotation, int sectorIndex, int count, int extraTurns)
        {
            float center = RotationForSectorCenter(sectorIndex, count);
            float delta = Mod(center - Mod(currentRotation, 360f), 360f);
            int turns = extraTurns < 0 ? 0 : extraTurns;
            return currentRotation + delta + 360f * turns;
        }

        private static float Mod(float a, float m)
        {
            return ((a % m) + m) % m;
        }
    }
}
