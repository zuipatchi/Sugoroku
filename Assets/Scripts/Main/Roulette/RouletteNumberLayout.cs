using System;
using Common.GameSession;
using Main.Turn;
using R3;

namespace Main.Roulette
{
    /// <summary>
    /// 円盤の「セクター → 出目（進むマス数）」の割り当てを保持し、**スピンのたびに引き直す**。
    /// 各参加者は <see cref="MinNumber"/>〜<see cref="MaxNumber"/> から**重複なし**で
    /// K 個（1 キャラあたりの数字枚数）の数字を持つ（同じキャラが 1 を 2 枚持つことはないが、
    /// 別のキャラがそれぞれ 1 を持つのはあり）。抽選の本体は純粋関数
    /// <see cref="RouletteMath.GenerateSectorNumbers"/>。
    ///
    /// **オンラインでは全員が同じ割り当てを持っていなければならない**。配るのは停止セクターの整数 1 つだけで、
    /// 「進む人」と「マス数」はそれぞれのクライアントがこの表から復元するため、表が食い違うと
    /// 同じセクターで止まったのに進むマス数がずれる。そこで抽選結果もスピンごとの種も配らず、
    /// **「部屋の全員で同じ値になるセッション ID」と「スピン回数」から各クライアントが同じ表を組む**
    /// （＝復元できる情報は配らないという既存の方針どおり。新しい待ち合わせも増えない）。
    /// スピン回数は手番ごとに <see cref="Reroll"/> を呼ぶ <c>GameFlowController</c> が進める。
    /// 全クライアントが同じアクション列で同じ回数だけループを回すので、回数もそろう。
    /// 一人用モードは合わせる相手がいないので実行ごとのランダムな基準種で引く。
    ///
    /// 盤面の進行（<c>GameFlowController</c>）と円盤の見た目（<see cref="RoulettePresenter"/>）で
    /// 同じ表を使うため DI で Scoped 共有する（<c>CpuCharacterPicker</c> と同じ理由）。
    /// 引き直しは <see cref="Version"/> で Presenter へ流し、円盤の数字バッジを貼り替えてもらう。
    /// </summary>
    public sealed class RouletteNumberLayout : IDisposable
    {
        /// <summary>各参加者が引く数字の下限。</summary>
        public const int MinNumber = 1;

        /// <summary>各参加者が引く数字の上限。</summary>
        public const int MaxNumber = 6;

        /// <summary>
        /// 1 キャラあたりの数字枚数（K）の既定値。通常は <see cref="RoulettePresenter"/> の
        /// 設定値で <see cref="Initialize"/> されるので、これが使われるのは初期化前に引かれたときだけ。
        /// </summary>
        public const int DefaultNumbersPerCharacter = 3;

        // FNV-1a の定数（種づくりに使う）。
        private const uint FnvOffset = 2166136261u;
        private const uint FnvPrime = 16777619u;

        private readonly GameParticipants _participants;
        private readonly GameSessionModel _gameSession;
        // 引き直すたびに増やす。Presenter が購読して円盤の数字バッジを貼り替える。
        private readonly ReactiveProperty<int> _version = new(0);

        // セクター index → 出目。null なら未抽選（初回参照で組む）。
        private int[] _steps;
        private int _numbersPerCharacter = DefaultNumbersPerCharacter;
        // 何回目のスピンぶんの数字か。全クライアントで同じ値になる（＝同じ種で引ける）。
        private int _spinIndex;
        // 抽選の基準種。1 度決めたらゲーム中は変えない（スピンごとの種はこれと _spinIndex から作る）。
        private int? _baseSeed;

        public RouletteNumberLayout(GameParticipants participants, GameSessionModel gameSession)
        {
            _participants = participants;
            _gameSession = gameSession;
        }

        /// <summary>セクター総数（＝参加者数 × K）。引き直しても変わらない。</summary>
        public int SectorCount => Table.Length;

        /// <summary>数字を引き直した通知（値は引き直した回数）。円盤の数字バッジの貼り替えに使う。</summary>
        public ReadOnlyReactiveProperty<int> Version => _version;

        /// <summary>
        /// 1 キャラあたりの数字枚数（K）を指定して最初の割り当てを組む。
        /// 円盤を組む <see cref="RoulettePresenter"/> が注入直後に呼ぶ。
        /// </summary>
        public void Initialize(int numbersPerCharacter)
        {
            _numbersPerCharacter = numbersPerCharacter < 1 ? 1 : numbersPerCharacter;
            Regenerate();
        }

        /// <summary>
        /// 次のスピンぶんの数字を引き直す（手番の開始ごとに <c>GameFlowController</c> が呼ぶ）。
        /// **全クライアントが同じ回数だけ呼ぶ**ことで、配らずに同じ表がそろう。
        /// </summary>
        public void Reroll()
        {
            _spinIndex++;
            Regenerate();
        }

        /// <summary>セクター <paramref name="sector"/> の出目（進むマス数）。範囲外は最小値。</summary>
        public int StepsForSector(int sector)
        {
            int[] table = Table;
            if (sector < 0 || sector >= table.Length)
            {
                return MinNumber;
            }
            return table[sector];
        }

        /// <summary>
        /// 文字列から種を作る（FNV-1a）。<c>string.GetHashCode</c> は実行ごとに値が変わり得るため使えない
        /// （クライアントごとに違う表が組まれてしまう）。<c>System.Random</c> に渡せるよう非負に丸める。
        /// </summary>
        public static int StableHash(string value)
        {
            unchecked
            {
                uint hash = FnvOffset;
                if (!string.IsNullOrEmpty(value))
                {
                    foreach (char ch in value)
                    {
                        hash ^= ch;
                        hash *= FnvPrime;
                    }
                }
                return NonNegative(hash);
            }
        }

        /// <summary>
        /// 基準種 <paramref name="baseSeed"/> と <paramref name="spinIndex"/> 回目のスピンから、
        /// そのスピンの種を作る純粋関数。単純な加算だと隣り合う種で似た並びが出かねないのでビットを混ぜる。
        /// </summary>
        public static int MixSeed(int baseSeed, int spinIndex)
        {
            unchecked
            {
                uint hash = FnvOffset;
                hash = MixBytes(hash, (uint)baseSeed);
                hash = MixBytes(hash, (uint)spinIndex);
                return NonNegative(hash);
            }
        }

        public void Dispose()
        {
            _version.Dispose();
        }

        private int[] Table
        {
            get
            {
                if (_steps == null)
                {
                    Regenerate();
                }
                return _steps;
            }
        }

        private void Regenerate()
        {
            _steps = RouletteMath.GenerateSectorNumbers(
                _participants.Count,
                _numbersPerCharacter,
                MinNumber,
                MaxNumber,
                new Random(MixSeed(BaseSeed, _spinIndex)));
            _version.Value++;
        }

        // 抽選の基準種。オンラインは部屋の全員で同じ値になるセッション ID から作り、
        // 一人用モードは合わせる相手がいないので実行ごとのランダムにする。
        private int BaseSeed
        {
            get
            {
                _baseSeed ??= ResolveBaseSeed();
                return _baseSeed.Value;
            }
        }

        private int ResolveBaseSeed()
        {
            if (_gameSession.Mode == GameMode.Online)
            {
                // セッション ID が取れなかったときも固定値に落として全員をそろえる（そこでランダムに倒すと
                // クライアントごとに違う表になり、同じセクターで止まったのに進むマス数がずれてしまう）。
                return StableHash(_gameSession.SessionId);
            }
            return new Random().Next();
        }

        private static uint MixBytes(uint hash, uint value)
        {
            unchecked
            {
                for (int i = 0; i < 4; i++)
                {
                    hash ^= (value >> (i * 8)) & 0xFFu;
                    hash *= FnvPrime;
                }
                return hash;
            }
        }

        // System.Random に渡せるよう非負へ丸める（int.MinValue は Random が受け付けない）。
        private static int NonNegative(uint hash)
        {
            unchecked
            {
                return (int)(hash & 0x7FFFFFFF);
            }
        }
    }
}
