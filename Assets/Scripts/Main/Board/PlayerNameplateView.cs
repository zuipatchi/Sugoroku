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
    /// 画面上部に出す自分（人間プレイヤー）のネームプレート。キャラの丸アイコン・選択キャラ名・
    /// 所持金（<see cref="MoneyModel"/> を購読してリアルタイム更新・マイナスは赤字）・
    /// 占領地の数（<see cref="TerritoryModel"/> を購読して「占拠数 / 総数」で表示・陣地マスが無い盤面では非表示）
    /// を表示する。相手（CPU 等）は表示しない。購読は呼び出し元の <see cref="CompositeDisposable"/> で管理する。
    /// </summary>
    public sealed class PlayerNameplateView
    {
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

        /// <summary>ヘッダー <paramref name="playerHeader"/> に人間プレイヤーのネームプレートを構築する。</summary>
        public void Build(VisualElement playerHeader)
        {
            for (int player = 0; player < _participants.Count; player++)
            {
                if (_participants.KindOf(player) != PlayerKind.Human)
                {
                    continue; // 相手（CPU 等）は表示しない。自分の情報だけを出す。
                }

                CharacterId id = _characterPicker.ResolveCharacter(player);
                string characterName = CharacterCatalog.Find(id).DisplayName;

                // 横型レイアウト：左に丸アイコン、右に情報列（キャラ名／所持金／占領地）。
                VisualElement plate = new() { pickingMode = PickingMode.Ignore };
                plate.AddToClassList("board-nameplate");

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

                playerHeader.Add(plate);
            }
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
        /// ネームプレート内の占領地表示（旗風バッジ＋「占拠数 / 総数」）。<see cref="TerritoryModel.Changed"/> を
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
            void Update()
            {
                int total = _territory.Total;
                territoryValue.text = $"{_territory.CountOwnedBy(player)} / {total}";
                territoryRow.style.display = total > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }

            Update();
            _disposables.Add(_territory.Changed.Subscribe(_ => Update()));

            return territoryRow;
        }
    }
}
