using System;
using System.Threading;
using Common.Character;
using Cysharp.Threading.Tasks;
using Main.Money;
using Main.Turn;
using R3;
using UnityEngine;
using UnityEngine.UIElements;

namespace Main.Board
{
    /// <summary>
    /// 画面上部に出す全プレイヤーのネームプレート。1 枚は「キャラの丸アイコン＋キャラ名＋所持金＋占領地」の
    /// 縦型カードで、参加者ぶん（最大 4 人）を横 1 行に並べて 1 画面へ収める（ページ送りは無い）。
    /// 自分＝人間プレイヤーのプレートにだけ、名前の下に「（あなた）」を添える。
    /// **所持金（<see cref="MoneyModel"/>）と占領地（<see cref="TerritoryModel"/>）は購読して常に最新を出す**
    /// ので、誰がいくら持っていて勝利までどれだけ近いかをモーダルを開かずに見比べられる
    /// （所持アイテムと、占領地の内訳のような細かい情報はプレートをクリックして開く詳細モーダル
    /// ＝<see cref="PlayerDetailPresenter"/> に出す）。
    /// プレートは上辺のアクセントをそのプレイヤー色にして、盤面の色分け（コマ・陣地）の凡例にする。
    /// 購読はシーンが生きている間ずっと張るので、<c>BoardPresenter</c> の
    /// <see cref="CompositeDisposable"/> に載せて破棄してもらう。
    /// </summary>
    public sealed class PlayerNameplateView : IDisposable
    {
        private const string AvatarEmptyClass = "board-nameplate__avatar--empty";
        // アイコン画像を貼った行頭バッジに付けて USS 描画の下地（色・枠）を消すクラス。
        private const string StatIconImageClass = "board-nameplate__stat-icon--image";

        private readonly GameParticipants _participants;
        private readonly CpuCharacterPicker _characterPicker;
        // キャラの丸アイコンを Addressables からロードするローダ（BoardPresenter が持つ共有インスタンス）。
        private readonly BoardIconLoader _iconLoader;
        private readonly MoneyModel _money;
        private readonly TerritoryModel _territory;
        // 自分＝人間プレイヤーの席。そのプレートにだけ「（あなた）」を添える。
        private readonly int _humanPlayer;
        // 画像ロードを打ち切るためのトークン（シーン破棄）。
        private readonly CancellationToken _ct;
        // プレートをクリックしたときに呼ぶハンドラ（詳細モーダルを開く。引数＝プレイヤー index）。
        private readonly Action<int> _onPlateClicked;

        // 所持金・占領地の購読（プレートと同じ寿命＝シーンが消えるまで）。
        private readonly CompositeDisposable _subscriptions = new();

        public PlayerNameplateView(
            GameParticipants participants,
            CpuCharacterPicker characterPicker,
            BoardIconLoader iconLoader,
            MoneyModel money,
            TerritoryModel territory,
            int humanPlayer,
            CancellationToken ct,
            Action<int> onPlateClicked)
        {
            _participants = participants;
            _characterPicker = characterPicker;
            _iconLoader = iconLoader;
            _money = money;
            _territory = territory;
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

        /// <summary>プレートに張った購読を落とす（表示の後始末はシーンごと消えるので不要）。</summary>
        public void Dispose()
        {
            _subscriptions.Dispose();
        }

        /// <summary>
        /// プレイヤー <paramref name="player"/> 1 人ぶんの縦型ネームプレート
        /// （上から丸アイコン・キャラ名・所持金・占領地）。
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

            plate.Add(BuildStats(player));
            return plate;
        }

        /// <summary>
        /// キャラ名の下に並べる所持金・占領地の 2 行。どちらも購読して値が変わるたびに書き換える。
        /// 詳細モーダルと違って見出しは置かず、アイコンと数字だけでプレート幅（104px）に収める。
        /// </summary>
        private VisualElement BuildStats(int player)
        {
            VisualElement stats = new() { pickingMode = PickingMode.Ignore };
            stats.AddToClassList("board-nameplate__stats");

            Label moneyValue = BuildStatRow(
                stats,
                PlayerStatIcons.CoinAddress,
                "board-nameplate__stat-icon--coin",
                "board-nameplate__stat-value--money");
            _subscriptions.Add(_money.Money(player).Subscribe(value => moneyValue.text = value.ToString("N0")));

            Label territoryValue = BuildStatRow(
                stats,
                PlayerStatIcons.TerritoryAddress,
                "board-nameplate__stat-icon--flag",
                "board-nameplate__stat-value--territory");
            // 陣地マスが無い盤面では行ごと隠すので、値ラベルの親＝行そのものを持っておく。
            VisualElement territoryRow = territoryValue.parent;

            // 分母は陣地マス総数ではなく勝利に必要な数（＝RequiredToWin）にして、勝利までの近さを見せる。
            // 陣地マスが無い盤面では勝敗が付かないので行ごと隠す（詳細モーダルと同じ扱い）。
            void UpdateTerritory()
            {
                territoryValue.text = $"{_territory.CountOwnedBy(player)}/{_territory.RequiredToWin}";
                territoryRow.style.display = _territory.Total > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // 陣地マスの登録（TerritoryModel.Initialize）はプレート構築より後になることがあるが、
            // 登録でも Changed が飛ぶのでここで購読しておけば行の表示も一緒に整う。
            UpdateTerritory();
            _subscriptions.Add(_territory.Changed.Subscribe(_ => UpdateTerritory()));

            return stats;
        }

        /// <summary>
        /// 統計 1 行（行頭アイコン＋数値）を <paramref name="stats"/> に足し、書き換える数値ラベルを返す。
        /// アイコン画像は非同期にロードし、未配置なら <paramref name="iconModifierClass"/> の
        /// USS 描画バッジ（コインは丸・陣地は四角）のまま見せる。
        /// </summary>
        private Label BuildStatRow(
            VisualElement stats, string iconAddress, string iconModifierClass, string valueModifierClass)
        {
            VisualElement row = new() { pickingMode = PickingMode.Ignore };
            row.AddToClassList("board-nameplate__stat");

            VisualElement icon = new() { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("board-nameplate__stat-icon");
            icon.AddToClassList(iconModifierClass);
            row.Add(icon);
            LoadStatIconAsync(iconAddress, icon).Forget();

            Label value = new(string.Empty) { pickingMode = PickingMode.Ignore };
            value.AddToClassList("board-nameplate__stat-value");
            value.AddToClassList(valueModifierClass);
            row.Add(value);

            stats.Add(row);
            return value;
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

        /// <summary>
        /// 所持金・占領地の行頭アイコンをロードして貼る。成功したら USS 描画の下地（色・枠）を消す
        /// （詳細モーダルの行頭バッジと同じ扱い）。未配置・キャンセルなら何もしない。
        /// </summary>
        private async UniTaskVoid LoadStatIconAsync(string address, VisualElement icon)
        {
            if (_iconLoader == null)
            {
                return;
            }
            Sprite sprite = await _iconLoader.LoadSpriteAsync(address, "ネームプレートアイコン", _ct);
            if (sprite == null || icon == null)
            {
                return;
            }
            icon.style.backgroundImage = new StyleBackground(sprite);
            icon.AddToClassList(StatIconImageClass);
        }
    }
}
