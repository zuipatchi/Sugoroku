using System.Collections.Generic;
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
    /// 画面上部に出す全プレイヤーのネームプレート。各プレートはキャラの丸アイコン・キャラ名・
    /// 所持金（<see cref="MoneyModel"/> を購読してリアルタイム更新・マイナスは赤字）・
    /// 占領地の数（<see cref="TerritoryModel"/> を購読して「占拠数 / 勝利に必要な数」で表示・陣地マスが無い盤面では非表示）
    /// を横型で並べる。プレートは横 1 行に置き、1 画面に最大 <see cref="PlatesPerPage"/> 人ぶん表示する。
    /// 人数が超えるぶんは左右端の三角ボタンでページ送りする（3〜4 人で 2 ページ）。
    /// 購読は呼び出し元の <see cref="CompositeDisposable"/> で管理する。
    /// </summary>
    public sealed class PlayerNameplateView
    {
        // 1 画面（1 ページ）に並べるネームプレートの最大数。
        private const int PlatesPerPage = 2;

        private const string AvatarEmptyClass = "board-nameplate__avatar--empty";
        // 所持金・占領地の行頭に置くアイコン画像の Addressable アドレス。未配置なら USS 描画の下地バッジにフォールバックする。
        private const string CoinIconAddress = "Image/Icon/CoinIcon";
        private const string TerritoryIconAddress = "Image/Icon/TeritoryIcon";
        // アイコン画像を貼ったバッジに付けて USS 描画の下地（色・枠）を消すクラス。
        private const string BadgeImageClass = "board-nameplate__badge--image";

        private readonly GameParticipants _participants;
        private readonly MoneyModel _money;
        private readonly TerritoryModel _territory;
        private readonly CpuCharacterPicker _characterPicker;
        // キャラの丸アイコンや所持金・占領地のバッジ画像を Addressables からロードするローダ（BoardPresenter が持つ共有インスタンス）。
        private readonly BoardIconLoader _iconLoader;
        // 画像ロードを打ち切るためのトークン（シーン破棄）。
        private readonly CancellationToken _ct;
        private readonly CompositeDisposable _disposables;

        // 生成した各プレイヤーのプレート（表示順＝参加者 index 順）。ページ送りで表示/非表示を切り替える。
        private readonly List<VisualElement> _plates = new();
        private Button _prevButton;
        private Button _nextButton;
        private int _page;
        private int _pageCount;

        public PlayerNameplateView(
            GameParticipants participants,
            MoneyModel money,
            TerritoryModel territory,
            CpuCharacterPicker characterPicker,
            BoardIconLoader iconLoader,
            CancellationToken ct,
            CompositeDisposable disposables)
        {
            _participants = participants;
            _money = money;
            _territory = territory;
            _characterPicker = characterPicker;
            _iconLoader = iconLoader;
            _ct = ct;
            _disposables = disposables;
        }

        /// <summary>
        /// ヘッダー <paramref name="playerHeader"/> に「左三角｜プレート列｜右三角」を構築する。
        /// プレートは全プレイヤーぶん作り、現在ページの <see cref="PlatesPerPage"/> 人ぶんだけ表示する。
        /// </summary>
        public void Build(VisualElement playerHeader)
        {
            // 左のページ送り（三角）。人数が 1 ページに収まるときは非表示。
            _prevButton = BuildPagerButton("player-pager--prev", "player-pager__triangle--left");
            _prevButton.clicked += () => ChangePage(-1);
            playerHeader.Add(_prevButton);

            // 中央：現在ページのプレートを中央寄せで並べるトラック（flex-grow で三角の間を埋めて中央に置く）。
            VisualElement trackWrap = new() { pickingMode = PickingMode.Ignore };
            trackWrap.AddToClassList("player-header__track-wrap");
            VisualElement track = new() { pickingMode = PickingMode.Ignore };
            track.AddToClassList("player-header__track");
            trackWrap.Add(track);

            for (int player = 0; player < _participants.Count; player++)
            {
                VisualElement plate = BuildPlate(player);
                _plates.Add(plate);
                track.Add(plate);
            }
            playerHeader.Add(trackWrap);

            // 右のページ送り（三角）。
            _nextButton = BuildPagerButton("player-pager--next", "player-pager__triangle--right");
            _nextButton.clicked += () => ChangePage(1);
            playerHeader.Add(_nextButton);

            _pageCount = Mathf.Max(1, (_participants.Count + PlatesPerPage - 1) / PlatesPerPage);
            _page = 0;
            ApplyPage();
        }

        /// <summary>プレイヤー <paramref name="player"/> 1 人ぶんの横型ネームプレート（左に丸アイコン、右に名前／所持金／占領地）。</summary>
        private VisualElement BuildPlate(int player)
        {
            CharacterId id = _characterPicker.ResolveCharacter(player);
            string characterName = CharacterCatalog.Find(id).DisplayName;

            VisualElement plate = new() { pickingMode = PickingMode.Ignore };
            plate.AddToClassList("board-nameplate");
            // 上辺アクセントをプレイヤー色（コマ・陣地と同じ）にして、盤面の色分けの凡例にする。
            plate.AddToClassList($"board-nameplate--p{PlayerColors.IndexOf(player)}");

            plate.Add(BuildAvatar(id));

            VisualElement info = new() { pickingMode = PickingMode.Ignore };
            info.AddToClassList("board-nameplate__info");

            // 1 段目：キャラ名。
            Label nameLabel = new(characterName) { pickingMode = PickingMode.Ignore };
            nameLabel.AddToClassList("board-nameplate__name");
            info.Add(nameLabel);

            // 2・3 段目：所持金・占領地。
            info.Add(BuildMoneyRow(player));
            info.Add(BuildTerritoryRow(player));

            plate.Add(info);
            return plate;
        }

        /// <summary>ページ送りの三角ボタン。三角形は USS の border トリックで描く子要素で表す。</summary>
        private static Button BuildPagerButton(string modifierClass, string triangleClass)
        {
            Button button = new();
            button.AddToClassList("player-pager");
            button.AddToClassList(modifierClass);

            VisualElement triangle = new() { pickingMode = PickingMode.Ignore };
            triangle.AddToClassList("player-pager__triangle");
            triangle.AddToClassList(triangleClass);
            button.Add(triangle);
            return button;
        }

        /// <summary>ページを <paramref name="delta"/> ぶん動かして表示を更新する（端でクランプ）。</summary>
        private void ChangePage(int delta)
        {
            _page = Mathf.Clamp(_page + delta, 0, _pageCount - 1);
            ApplyPage();
        }

        /// <summary>
        /// 現在ページのプレートだけ表示し、他は隠す。三角ボタンは 1 ページに収まるなら両方隠し、
        /// 複数ページなら常に出して端では無効化（薄く）する。
        /// </summary>
        private void ApplyPage()
        {
            int start = _page * PlatesPerPage;
            for (int i = 0; i < _plates.Count; i++)
            {
                bool visible = i >= start && i < start + PlatesPerPage;
                _plates[i].style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }

            bool paged = _pageCount > 1;
            _prevButton.style.display = paged ? DisplayStyle.Flex : DisplayStyle.None;
            _nextButton.style.display = paged ? DisplayStyle.Flex : DisplayStyle.None;
            _prevButton.SetEnabled(_page > 0);
            _nextButton.SetEnabled(_page < _pageCount - 1);
        }

        /// <summary>
        /// ネームプレート左端に出すキャラの丸バッジアイコン（盤面コマと同じ <see cref="CharacterDefinition.PieceIconAddress"/>）。
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
        /// 所持金・占領地の行頭バッジ <paramref name="badge"/> にアイコン画像をロードして貼る。
        /// 成功時は USS 描画の下地（色・枠）を消す <see cref="BadgeImageClass"/> を付ける。
        /// 未配置・キャンセルなら何もしない（USS 描画の下地バッジのまま）。
        /// </summary>
        private async UniTaskVoid LoadBadgeAsync(string address, VisualElement badge)
        {
            if (_iconLoader == null)
            {
                return;
            }
            Sprite sprite = await _iconLoader.LoadSpriteAsync(address, "ネームプレートアイコン", _ct);
            if (sprite == null || badge == null)
            {
                return;
            }
            badge.style.backgroundImage = new StyleBackground(sprite);
            badge.AddToClassList(BadgeImageClass);
        }

        /// <summary>
        /// ネームプレート内の所持金表示（コイン風バッジ＋金額）。プレイヤー <paramref name="player"/> の
        /// <see cref="MoneyModel.Money"/> を購読してリアルタイムに更新し、マイナス時は赤字にする。
        /// </summary>
        private VisualElement BuildMoneyRow(int player)
        {
            VisualElement moneyRow = new() { pickingMode = PickingMode.Ignore };
            moneyRow.AddToClassList("board-nameplate__money");

            VisualElement coin = new() { pickingMode = PickingMode.Ignore };
            coin.AddToClassList("board-nameplate__coin");
            moneyRow.Add(coin);
            LoadBadgeAsync(CoinIconAddress, coin).Forget();

            Label moneyValue = new() { pickingMode = PickingMode.Ignore };
            moneyValue.AddToClassList("board-nameplate__money-value");
            moneyRow.Add(moneyValue);

            _disposables.Add(_money.Money(player).Subscribe(value =>
            {
                moneyValue.text = value.ToString("N0");
                moneyValue.EnableInClassList("board-nameplate__money-value--negative", value < 0);
            }));

            return moneyRow;
        }

        /// <summary>
        /// ネームプレート内の占領地表示（旗風バッジ＋「占拠数 / 勝利に必要な数」）。<see cref="TerritoryModel.Changed"/> を
        /// 購読してリアルタイムに更新する。陣地マスが無い盤面（総数 0）では行を非表示にする。
        /// </summary>
        private VisualElement BuildTerritoryRow(int player)
        {
            VisualElement territoryRow = new() { pickingMode = PickingMode.Ignore };
            territoryRow.AddToClassList("board-nameplate__territory");

            VisualElement flag = new() { pickingMode = PickingMode.Ignore };
            flag.AddToClassList("board-nameplate__flag");
            territoryRow.Add(flag);
            LoadBadgeAsync(TerritoryIconAddress, flag).Forget();

            Label territoryValue = new() { pickingMode = PickingMode.Ignore };
            territoryValue.AddToClassList("board-nameplate__territory-value");
            territoryRow.Add(territoryValue);

            // ネームプレート構築が陣地マスの初期化より先でも後でも正しく出るよう、初期値を直接読んで
            // 反映してから変化を購読する（Changed は Initialize / Claim の両方で発火する）。
            // 分母は陣地マス総数ではなく勝利に必要な数（過半数＝RequiredToWin）を出し、勝利までの進捗を示す。
            void Update()
            {
                int total = _territory.Total;
                territoryValue.text = $"{_territory.CountOwnedBy(player)} / {_territory.RequiredToWin}";
                territoryRow.style.display = total > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }

            Update();
            _disposables.Add(_territory.Changed.Subscribe(_ => Update()));

            return territoryRow;
        }
    }
}
