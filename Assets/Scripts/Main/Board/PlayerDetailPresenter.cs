using System;
using System.Collections.Generic;
using System.Threading;
using Common.Character;
using Cysharp.Threading.Tasks;
using Main.Item;
using Main.Money;
using R3;
using UnityEngine;
using UnityEngine.UIElements;

namespace Main.Board
{
    /// <summary>
    /// 上部のネームプレートをクリックしたときに開くプレイヤー詳細モーダル。キャラアイコン・名前に加えて、
    /// プレートから外した所持金（<see cref="MoneyModel"/>）・占領地（<see cref="TerritoryModel"/>）と、
    /// そのプレイヤーの所持アイテム（<see cref="ItemModel"/>。同じアイテムは 1 行にまとめて「x2」）を表示する。
    /// 所持金・占領地は開いている間だけ購読してリアルタイムに追従する。
    /// 見せるだけで盤面には触らないので誰の手番でも開ける（<see cref="BoardCellInfoPresenter"/> と同じ）。
    /// 開いている間だけ Board の <see cref="UIDocument.sortingOrder"/> を持ち上げて回転中のルーレットより前面に出す
    /// （<see cref="ItemModalPresenter"/> と同じ規約）。<c>BoardPresenter</c> が生成する協調クラス。
    /// 開いたままシーンが破棄されても購読が残らないよう <see cref="IDisposable"/> にして
    /// <c>BoardPresenter</c> の <see cref="CompositeDisposable"/> に載せる。
    /// </summary>
    public sealed class PlayerDetailPresenter : IDisposable
    {
        private const string OpenClass = "item-modal--open";
        // モーダルを開いている間だけ Board の UIDocument を前面へ持ち上げる SortingOrder。
        // ルーレット(10)・ミニゲームトリガ(20)より上、Common のオプションオーバーレイ(1000+)より下。
        private const float RaisedSortingOrder = 100f;
        private const string AvatarEmptyClass = "player-detail__avatar--empty";
        // アイコン画像を貼ったバッジに付けて USS 描画の下地（色・枠）を消すクラス。
        private const string BadgeImageClass = "player-detail__badge--image";

        private readonly VisualElement _overlay;
        private readonly VisualElement _card;
        private readonly VisualElement _avatar;
        private readonly Label _name;
        // 自分＝人間プレイヤーの詳細を開いたときだけ出す「（あなた）」。
        private readonly Label _you;
        private readonly Label _moneyValue;
        private readonly VisualElement _territoryRow;
        private readonly Label _territoryValue;
        private readonly VisualElement _itemList;

        private readonly MoneyModel _money;
        private readonly TerritoryModel _territory;
        private readonly ItemModel _items;
        private readonly CpuCharacterPicker _characterPicker;
        private readonly BoardIconLoader _iconLoader;
        // 自分＝人間プレイヤーの席（ネームプレートと同じく「（あなた）」の出し分けに使う）。
        private readonly int _humanPlayer;
        // アイテム絵のロード（BoardPresenter のキャッシュ経由。未配置なら null で名前だけ出す）。
        private readonly Func<ItemDefinition, CancellationToken, UniTask<Sprite>> _itemSpriteLoader;
        private readonly UIDocument _document;
        private readonly CancellationToken _ct;

        // 開くたびにロードし直さないよう、キャラの丸アイコンはキャラ単位でキャッシュする。
        private readonly Dictionary<CharacterId, Sprite> _avatarSprites = new();

        // 開いている間だけ張る購読（所持金・占領地）。閉じるときに破棄する。
        private IDisposable _moneySubscription;
        private IDisposable _territorySubscription;
        // カードに付けているプレイヤー色クラス（開き直しで前のプレイヤーの色が残らないよう外す）。
        private string _colorClass;
        // いま表示しているキャラ（画像ロードの完了が開き直しに追い越されたかの判定に使う）。
        private CharacterId _currentCharacter;
        private float _baseSortingOrder;

        public PlayerDetailPresenter(
            VisualElement overlay,
            MoneyModel money,
            TerritoryModel territory,
            ItemModel items,
            CpuCharacterPicker characterPicker,
            BoardIconLoader iconLoader,
            int humanPlayer,
            Func<ItemDefinition, CancellationToken, UniTask<Sprite>> itemSpriteLoader,
            UIDocument document,
            CancellationToken ct)
        {
            _overlay = overlay;
            _money = money;
            _territory = territory;
            _items = items;
            _characterPicker = characterPicker;
            _iconLoader = iconLoader;
            _humanPlayer = humanPlayer;
            _itemSpriteLoader = itemSpriteLoader;
            _document = document;
            _ct = ct;

            _card = overlay.Q<VisualElement>("PlayerDetailCard");
            _avatar = overlay.Q<VisualElement>("PlayerDetailAvatar");
            _name = overlay.Q<Label>("PlayerDetailName");
            _you = overlay.Q<Label>("PlayerDetailYou");
            _moneyValue = overlay.Q<Label>("PlayerDetailMoney");
            _territoryRow = overlay.Q<VisualElement>("PlayerDetailTerritoryRow");
            _territoryValue = overlay.Q<Label>("PlayerDetailTerritory");
            _itemList = overlay.Q<VisualElement>("PlayerDetailItems");

            // 行頭のアイコン画像（アドレスはネームプレートと共通＝PlayerStatIcons）。
            // 未配置なら USS 描画の下地バッジのままにする。
            LoadBadgeAsync(PlayerStatIcons.CoinAddress, overlay.Q<VisualElement>("PlayerDetailMoneyIcon")).Forget();
            LoadBadgeAsync(PlayerStatIcons.TerritoryAddress, overlay.Q<VisualElement>("PlayerDetailTerritoryIcon")).Forget();

            Button closeButton = overlay.Q<Button>("PlayerDetailCloseButton");
            if (closeButton != null)
            {
                closeButton.clicked += Close;
            }

            // 暗幕のクリックでも閉じる。カード内のクリックは target が暗幕にならないため閉じない。
            overlay.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == _overlay)
                {
                    Close();
                }
            });
        }

        /// <summary>プレイヤー <paramref name="player"/> の詳細モーダルを開く。</summary>
        public void Open(int player)
        {
            CharacterId id = _characterPicker.ResolveCharacter(player);
            if (_name != null)
            {
                _name.text = CharacterCatalog.Find(id).DisplayName;
            }
            // 「（あなた）」は自分の席を開いたときだけ名前の下に出す。
            if (_you != null)
            {
                _you.style.display = player == _humanPlayer ? DisplayStyle.Flex : DisplayStyle.None;
            }
            ApplyPlayerColor(player);
            ApplyAvatar(id);
            SubscribeStats(player);
            BuildItems(player);

            // 既に開いている状態で別プレートから再度開かれても、持ち上げ済みの値を基準として
            // 取り込まないよう、閉→開の遷移でだけ SortingOrder を退避・変更する。
            if (_document != null && !_overlay.ClassListContains(OpenClass))
            {
                _baseSortingOrder = _document.sortingOrder;
                _document.sortingOrder = RaisedSortingOrder;
            }

            _overlay.AddToClassList(OpenClass);
        }

        private void Close()
        {
            _overlay.RemoveFromClassList(OpenClass);
            DisposeSubscriptions();

            if (_document != null)
            {
                _document.sortingOrder = _baseSortingOrder;
            }
        }

        /// <summary>カード上辺のアクセントをそのプレイヤー色（コマ・ネームプレートと同じ）に差し替える。</summary>
        private void ApplyPlayerColor(int player)
        {
            if (_card == null)
            {
                return;
            }
            if (_colorClass != null)
            {
                _card.RemoveFromClassList(_colorClass);
            }
            _colorClass = $"player-detail__card--p{PlayerColors.IndexOf(player)}";
            _card.AddToClassList(_colorClass);
        }

        /// <summary>キャラの丸アイコンを貼る。キャッシュに無ければロードしてから貼る（未配置なら空の枠のまま）。</summary>
        private void ApplyAvatar(CharacterId id)
        {
            _currentCharacter = id;
            if (_avatar == null)
            {
                return;
            }
            if (_avatarSprites.TryGetValue(id, out Sprite cached) && cached != null)
            {
                _avatar.style.backgroundImage = new StyleBackground(cached);
                _avatar.RemoveFromClassList(AvatarEmptyClass);
                return;
            }

            _avatar.style.backgroundImage = StyleKeyword.None;
            _avatar.AddToClassList(AvatarEmptyClass);
            LoadAvatarAsync(id).Forget();
        }

        private async UniTaskVoid LoadAvatarAsync(CharacterId id)
        {
            if (_iconLoader == null)
            {
                return;
            }
            Sprite sprite = await _iconLoader.LoadSpriteAsync(CharacterCatalog.Find(id).PieceIconAddress, "キャラアイコン", _ct);
            if (sprite == null || _avatar == null)
            {
                return;
            }
            _avatarSprites[id] = sprite;
            // ロード中に別プレイヤーへ開き直されていたら、そちらの絵を上書きしない。
            if (_currentCharacter != id)
            {
                return;
            }
            _avatar.style.backgroundImage = new StyleBackground(sprite);
            _avatar.RemoveFromClassList(AvatarEmptyClass);
        }

        /// <summary>
        /// 所持金・占領地を購読して表示に反映する。開くたびに張り直し、閉じるときに破棄する
        /// （モーダルを開いたまま相手の手番が進んでも値が追従する）。
        /// </summary>
        private void SubscribeStats(int player)
        {
            DisposeSubscriptions();

            if (_moneyValue != null)
            {
                _moneySubscription = _money.Money(player).Subscribe(value =>
                {
                    _moneyValue.text = value.ToString("N0");
                });
            }

            if (_territoryValue == null || _territoryRow == null)
            {
                return;
            }

            // 分母は陣地マス総数ではなく勝利に必要な数（総数÷プレイヤー数の切り上げ＝RequiredToWin）を出し、勝利までの進捗を示す。
            // 陣地マスが無い盤面（総数 0）では行ごと隠す。
            void Update()
            {
                _territoryValue.text = $"{_territory.CountOwnedBy(player)} / {_territory.RequiredToWin}";
                _territoryRow.style.display = _territory.Total > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }

            Update();
            _territorySubscription = _territory.Changed.Subscribe(_ => Update());
        }

        /// <summary>開いたまま破棄された場合に備えて購読を落とす（表示の後始末はシーンごと消えるので不要）。</summary>
        public void Dispose()
        {
            DisposeSubscriptions();
        }

        private void DisposeSubscriptions()
        {
            _moneySubscription?.Dispose();
            _moneySubscription = null;
            _territorySubscription?.Dispose();
            _territorySubscription = null;
        }

        /// <summary>
        /// 所持アイテムの一覧を組み直す。同じアイテムは行を増やさず、取得順を保って「x2」の枚数で表す
        /// （右下の手札と同じ見せ方）。1 つも持っていなければ「なし」を出す。
        /// </summary>
        private void BuildItems(int player)
        {
            if (_itemList == null)
            {
                return;
            }

            _itemList.Clear();

            IReadOnlyList<ItemId> hand = _items.Items(player);
            if (hand.Count == 0)
            {
                Label empty = new("なし") { pickingMode = PickingMode.Ignore };
                empty.AddToClassList("player-detail__item-empty");
                _itemList.Add(empty);
                return;
            }

            List<ItemId> order = new();
            Dictionary<ItemId, int> counts = new();
            foreach (ItemId item in hand)
            {
                if (counts.TryGetValue(item, out int count))
                {
                    counts[item] = count + 1;
                    continue;
                }
                counts[item] = 1;
                order.Add(item);
            }

            foreach (ItemId item in order)
            {
                _itemList.Add(BuildItemRow(item, counts[item]));
            }
        }

        /// <summary>アイテム 1 行（絵・名前・2 枚以上なら枚数）。絵は非同期にロードして貼る。</summary>
        private VisualElement BuildItemRow(ItemId item, int count)
        {
            ItemDefinition def = ItemCatalog.Find(item);

            VisualElement row = new() { pickingMode = PickingMode.Ignore };
            row.AddToClassList("player-detail__item");

            VisualElement icon = new() { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("player-detail__item-icon");
            row.Add(icon);
            LoadItemIconAsync(def, icon).Forget();

            Label name = new(def?.DisplayName ?? item.ToString()) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("player-detail__item-name");
            row.Add(name);

            if (count > 1)
            {
                Label countLabel = new($"x{count}") { pickingMode = PickingMode.Ignore };
                countLabel.AddToClassList("player-detail__item-count");
                row.Add(countLabel);
            }

            return row;
        }

        /// <summary>アイテム絵をロードして行のアイコンに貼る。未配置・キャンセル・行の作り直し後なら何もしない。</summary>
        private async UniTaskVoid LoadItemIconAsync(ItemDefinition def, VisualElement icon)
        {
            if (_itemSpriteLoader == null || def == null)
            {
                return;
            }
            Sprite sprite = await _itemSpriteLoader(def, _ct);
            // 待っている間に別プレイヤーで一覧を組み直していたら、この行はもう親から外れている。
            if (sprite == null || icon == null || icon.parent == null)
            {
                return;
            }
            icon.style.backgroundImage = new StyleBackground(sprite);
        }

        /// <summary>
        /// 所持金・占領地の行頭バッジ <paramref name="badge"/> にアイコン画像をロードして貼る。
        /// 成功時は USS 描画の下地（色・枠）を消す <see cref="BadgeImageClass"/> を付ける。
        /// 未配置・キャンセルなら何もしない（USS 描画の下地バッジのまま）。
        /// </summary>
        private async UniTaskVoid LoadBadgeAsync(string address, VisualElement badge)
        {
            if (_iconLoader == null || badge == null)
            {
                return;
            }
            Sprite sprite = await _iconLoader.LoadSpriteAsync(address, "プレイヤー詳細アイコン", _ct);
            if (sprite == null)
            {
                return;
            }
            badge.style.backgroundImage = new StyleBackground(sprite);
            badge.AddToClassList(BadgeImageClass);
        }
    }
}
