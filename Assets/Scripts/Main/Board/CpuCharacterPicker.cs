using System;
using System.Collections.Generic;
using Common.Character;
using Main.Turn;

namespace Main.Board
{
    /// <summary>
    /// 盤面のコマ・ネームプレートに使うキャラの解決を担う。人間は選択中のキャラ、
    /// CPU は人間とも他の CPU とも被らないキャラをランダムに 1 度だけ配ってゲーム中は固定する。
    /// 抽選の本体（人間を除いたキャラ index のシャッフル）は純粋関数
    /// <see cref="ShuffledNonHumanIndices"/> に切り出してあり、単体テスト可能。
    /// </summary>
    public sealed class CpuCharacterPicker
    {
        private readonly GameParticipants _participants;
        private readonly CharacterSessionModel _characterSession;
        // CPU プレイヤー index → 割り当てたキャラ。初回解決時に全 CPU ぶんまとめて決めて固定する。
        private Dictionary<int, CharacterId> _cpuCharacters;

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

            EnsureCpuCharacters();
            return _cpuCharacters[player];
        }

        /// <summary>
        /// 全 CPU プレイヤーに、人間とも他の CPU とも被らないキャラを配る（1 度だけ実行して固定）。
        /// 人間を除いたキャラ index をシャッフルし、参加者順に CPU へ割り当てる。
        /// CPU 数はキャラ総数 -1 以内（プレイ人数上限 4 人＝CPU 3 人）なので必ず全員別キャラになる。
        /// </summary>
        private void EnsureCpuCharacters()
        {
            if (_cpuCharacters != null)
            {
                return;
            }
            _cpuCharacters = new Dictionary<int, CharacterId>();

            int humanIndex = CharacterCatalog.IndexOf(_characterSession.Selected);
            IReadOnlyList<int> pool = ShuffledNonHumanIndices(humanIndex, CharacterCatalog.All.Count, new Random());
            if (pool.Count == 0)
            {
                return; // キャラが 1 種類しかない想定外ケース（人間ぶんで尽きる）。
            }

            int cpuOrder = 0;
            for (int player = 0; player < _participants.Count; player++)
            {
                if (_participants.KindOf(player) != PlayerKind.Cpu)
                {
                    continue;
                }
                int index = pool[cpuOrder % pool.Count];
                _cpuCharacters[player] = CharacterCatalog.All[index].Id;
                cpuOrder++;
            }
        }

        /// <summary>
        /// 人間のキャラ index <paramref name="humanIndex"/> を除いたキャラ index の一覧を
        /// <paramref name="rng"/> でシャッフルして返す純粋関数。返り値の要素はすべて人間と異なり互いに重複しない。
        /// </summary>
        public static IReadOnlyList<int> ShuffledNonHumanIndices(int humanIndex, int catalogCount, Random rng)
        {
            List<int> pool = new(catalogCount);
            for (int i = 0; i < catalogCount; i++)
            {
                if (i != humanIndex)
                {
                    pool.Add(i);
                }
            }

            // Fisher–Yates シャッフル。
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            return pool;
        }
    }
}
