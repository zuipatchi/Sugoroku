using System.Collections.Generic;
using Common.Character;
using Main.Turn;

namespace Main.Board
{
    /// <summary>
    /// 盤面のコマ・ネームプレートに使うキャラの解決を担う。人間は選択中のキャラ、
    /// CPU は人間と違うキャラをランダムに 1 度だけ選んでゲーム中は固定する。
    /// 抽選の本体は純粋関数 <see cref="PickCpuCharacter"/> に切り出してあり、単体テスト可能。
    /// </summary>
    public sealed class CpuCharacterPicker
    {
        private readonly GameParticipants _participants;
        private readonly CharacterSessionModel _characterSession;
        private CharacterId? _cpuCharacter;

        public CpuCharacterPicker(GameParticipants participants, CharacterSessionModel characterSession)
        {
            _participants = participants;
            _characterSession = characterSession;
        }

        /// <summary>プレイヤー <paramref name="player"/> のコマに割り当てるキャラを解決する。</summary>
        public CharacterId ResolveCharacter(int player)
        {
            if (_participants.KindOf(player) == PlayerKind.Human)
            {
                return _characterSession.Selected;
            }

            // CPU は人間と違うキャラをランダムに選ぶ。1 度だけ決めてゲーム中は固定する。
            if (_cpuCharacter == null)
            {
                // オフセット 1..(Count-1) を足すことで、必ず人間と別のキャラ index になる。
                int offset = UnityEngine.Random.Range(1, CharacterCatalog.All.Count);
                _cpuCharacter = PickCpuCharacter(_characterSession.Selected, offset);
            }
            return _cpuCharacter.Value;
        }

        /// <summary>
        /// 人間の選択キャラ <paramref name="human"/> から <paramref name="offset"/>（1..カタログ数-1）ぶん
        /// 表示順にずらしたキャラを返す純粋関数。offset が範囲内なら必ず人間と別のキャラになる。
        /// </summary>
        public static CharacterId PickCpuCharacter(CharacterId human, int offset)
        {
            IReadOnlyList<CharacterDefinition> all = CharacterCatalog.All;
            int humanIndex = CharacterCatalog.IndexOf(human);
            return all[(humanIndex + offset) % all.Count].Id;
        }
    }
}
