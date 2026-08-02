using System;
using System.Threading;
using Common.Character;
using Cysharp.Threading.Tasks;
using Main.Turn;
using UnityEngine;
using UnityEngine.UIElements;

namespace Main.Board
{
    /// <summary>
    /// 画面上部に出す全プレイヤーのネームプレート。1 枚は「キャラの丸アイコン＋キャラ名」だけの
    /// 縦型カードで、参加者ぶん（最大 4 人）を横 1 行に並べて 1 画面へ収める（ページ送りは無い）。
    /// 自分＝人間プレイヤーのプレートにだけ、名前の下に「（あなた）」を添える。
    /// 所持金・占領地・所持アイテムはプレートをクリックして開く詳細モーダル
    /// （<see cref="PlayerDetailPresenter"/>）に出す。
    /// プレートは上辺のアクセントをそのプレイヤー色にして、盤面の色分け（コマ・陣地）の凡例にする。
    /// </summary>
    public sealed class PlayerNameplateView
    {
        private const string AvatarEmptyClass = "board-nameplate__avatar--empty";

        private readonly GameParticipants _participants;
        private readonly CpuCharacterPicker _characterPicker;
        // キャラの丸アイコンを Addressables からロードするローダ（BoardPresenter が持つ共有インスタンス）。
        private readonly BoardIconLoader _iconLoader;
        // 自分＝人間プレイヤーの席。そのプレートにだけ「（あなた）」を添える。
        private readonly int _humanPlayer;
        // 画像ロードを打ち切るためのトークン（シーン破棄）。
        private readonly CancellationToken _ct;
        // プレートをクリックしたときに呼ぶハンドラ（詳細モーダルを開く。引数＝プレイヤー index）。
        private readonly Action<int> _onPlateClicked;

        public PlayerNameplateView(
            GameParticipants participants,
            CpuCharacterPicker characterPicker,
            BoardIconLoader iconLoader,
            int humanPlayer,
            CancellationToken ct,
            Action<int> onPlateClicked)
        {
            _participants = participants;
            _characterPicker = characterPicker;
            _iconLoader = iconLoader;
            _humanPlayer = humanPlayer;
            _ct = ct;
            _onPlateClicked = onPlateClicked;
        }

        /// <summary>ヘッダー <paramref name="playerHeader"/> に全プレイヤーのプレートを横 1 行で構築する。</summary>
        public void Build(VisualElement playerHeader)
        {
            for (int player = 0; player < _participants.Count; player++)
            {
                playerHeader.Add(BuildPlate(player));
            }
        }

        /// <summary>
        /// プレイヤー <paramref name="player"/> 1 人ぶんの縦型ネームプレート（上に丸アイコン、下にキャラ名）。
        /// クリックで詳細モーダルを開くので <see cref="Button"/> で作る（中の要素はクリックを通す）。
        /// </summary>
        private Button BuildPlate(int player)
        {
            CharacterId id = _characterPicker.ResolveCharacter(player);

            Button plate = new();
            plate.AddToClassList("board-nameplate");
            // 上辺アクセントをプレイヤー色（コマ・陣地と同じ）にして、盤面の色分けの凡例にする。
            plate.AddToClassList($"board-nameplate--p{PlayerColors.IndexOf(player)}");
            plate.clicked += () => _onPlateClicked?.Invoke(player);

            plate.Add(BuildAvatar(id));

            Label nameLabel = new(CharacterCatalog.Find(id).DisplayName) { pickingMode = PickingMode.Ignore };
            nameLabel.AddToClassList("board-nameplate__name");
            plate.Add(nameLabel);

            // 自分のプレートだけ、名前の下に「（あなた）」を添えて一目で見分けられるようにする。
            if (player == _humanPlayer)
            {
                Label youLabel = new("（あなた）") { pickingMode = PickingMode.Ignore };
                youLabel.AddToClassList("board-nameplate__you");
                plate.Add(youLabel);
            }

            return plate;
        }

        /// <summary>
        /// ネームプレート上部に出すキャラの丸バッジアイコン（盤面コマと同じ <see cref="CharacterDefinition.PieceIconAddress"/>）。
        /// 画像は Addressables から遅延ロードして貼る（未配置・キャンセルなら空の枠のまま）。
        /// </summary>
        private VisualElement BuildAvatar(CharacterId id)
        {
            VisualElement avatar = new() { pickingMode = PickingMode.Ignore };
            avatar.AddToClassList("board-nameplate__avatar");
            avatar.AddToClassList(AvatarEmptyClass);

            LoadAvatarAsync(id, avatar).Forget();
            return avatar;
        }

        /// <summary>キャラの丸バッジ画像をロードしてアイコンに貼る。未配置・キャンセルなら何もしない（空の枠のまま）。</summary>
        private async UniTaskVoid LoadAvatarAsync(CharacterId id, VisualElement avatar)
        {
            if (_iconLoader == null)
            {
                return;
            }
            string address = CharacterCatalog.Find(id).PieceIconAddress;
            Sprite sprite = await _iconLoader.LoadSpriteAsync(address, "キャラアイコン", _ct);
            if (sprite == null || avatar == null)
            {
                return;
            }
            avatar.style.backgroundImage = new StyleBackground(sprite);
            avatar.RemoveFromClassList(AvatarEmptyClass);
        }
    }
}
