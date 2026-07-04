using Common.Character;
using Main.Money;
using Main.Turn;
using R3;
using UnityEngine.UIElements;

namespace Main.Board
{
    /// <summary>
    /// 画面上部に出す自分（人間プレイヤー）のネームプレート。YOU ロールタグ・選択キャラ名・
    /// 所持金（<see cref="MoneyModel"/> を購読してリアルタイム更新・マイナスは赤字）を表示する。
    /// 相手（CPU 等）は表示しない。購読は呼び出し元の <see cref="CompositeDisposable"/> で管理する。
    /// </summary>
    public sealed class PlayerNameplateView
    {
        private readonly GameParticipants _participants;
        private readonly MoneyModel _money;
        private readonly CpuCharacterPicker _characterPicker;
        private readonly CompositeDisposable _disposables;

        public PlayerNameplateView(
            GameParticipants participants,
            MoneyModel money,
            CpuCharacterPicker characterPicker,
            CompositeDisposable disposables)
        {
            _participants = participants;
            _money = money;
            _characterPicker = characterPicker;
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

                VisualElement plate = new() { pickingMode = PickingMode.Ignore };
                plate.AddToClassList("board-nameplate");

                Label role = new("YOU") { pickingMode = PickingMode.Ignore };
                role.AddToClassList("board-nameplate__role");
                plate.Add(role);

                Label nameLabel = new(characterName) { pickingMode = PickingMode.Ignore };
                nameLabel.AddToClassList("board-nameplate__name");
                plate.Add(nameLabel);

                plate.Add(BuildMoneyRow(player));

                playerHeader.Add(plate);
            }
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
    }
}
