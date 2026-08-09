using System.Collections.Generic;

namespace Main.Board
{
    /// <summary>
    /// マスに止まったときに見せる文言（フレーバーテキスト）の**既定値**。
    ///
    /// 文言の編集は <see cref="BoardCellMessageCatalog"/> 資産（Cell Message Editor）で行うのが本筋で、
    /// ここは「資産をまだ作っていない／割り当てていない」ときのフォールバックと、
    /// 資産の新規作成時に流し込む初期値の 2 役を持つ（資産を割り当てたらそちらが唯一の情報源になり、
    /// ここは一切参照されない）。
    ///
    /// 文言はマスごとの設定ではなくイベント種別ごとに全マップ共通で持つ（マスの画像を種別ごとに解決する
    /// <see cref="BoardEventArtCatalog"/>・説明文の <see cref="BoardEventDescription"/> と同じ方式）。
    /// </summary>
    public static class BoardCellMessageDefaults
    {
        // 文言を書くときの決まり：
        // ・**文末に「。」は使わない**（言い切り・「！」・「…」・「？」で終える。テンポを出すためと、
        //   縦横に小さい帯へ載せるので終止符が間延びして見えるため。EditMode テストが検出する）
        // ・**全角 14 文字以内**にすると 1 行に収まる（Board.uss の .cell-message の max-width 94% ＋
        //   font-size 28px ＋ -unity-text-outline-width 2px から決まる＝縁取りのぶん 1 文字が 32px 占める。
        //   14 文字が入るよう寸法を逆算してあるので余裕は 0.6 文字ぶんしかない。超えても折り返して表示できるが、
        //   2 行になる。Cell Message Editor のプレビューで確かめられる）
        // ・1 種別につき 10 件前後。同じマスに何度止まっても違う文言が出るようにするため

        // スタート＝ゴール（経路 index 0）はイベント種別ではなく位置で決まるので専用プールを持つ
        // （BoardEventDescription.StartDescription と同じ扱い）。
        private static readonly string[] StartPool =
        {
            "スタート地点に帰ってきた！",
            "ここが始まりの場所だ",
            "ぐるりと一周してきた",
            "旅はまだまだ続く",
            "見慣れた景色に戻ってきた",
            "ふりだしに立っている",
            "また一周ぶん強くなった！",
            "出発点をふたたび踏んだ",
            "ここから何度でもやり直せる",
            "一周の旅を終えて戻った"
        };

        private static readonly string[] NonePool =
        {
            "ひと息ついた",
            "何事もなく通り過ぎた",
            "空を見上げるといい天気だ",
            "足を止めて深呼吸した",
            "とくに何も起こらない",
            "のんびり景色をながめた",
            "平和な時間が流れている",
            "靴ひもを結び直した",
            "遠くで鳥の声がした",
            "少し休んで体力を戻した"
        };

        private static readonly string[] ForwardPool =
        {
            "追い風が吹いた！",
            "近道を見つけた！",
            "足が軽い！どんどん行こう！",
            "風に乗ってひとっ飛び！",
            "調子に乗って走り出した！",
            "下り坂を一気にかけ下りた！",
            "ショートカット発見！",
            "誰かに背中を押された！",
            "うれしくなって駆け出した！",
            "追い風に運ばれていく！"
        };

        private static readonly string[] BackPool =
        {
            "道を間違えた…",
            "忘れ物を取りに戻った",
            "石につまずいて転んだ！",
            "来た道を引き返すはめに…",
            "強い向かい風に押し戻された！",
            "地図を逆さに持っていた…",
            "落とし物を探して引き返す",
            "うっかり道を戻ってしまった",
            "通行止めで迂回するはめに…",
            "つい後ろが気になって…"
        };

        private static readonly string[] MiniGamePool =
        {
            "勝負の時間だ！",
            "腕の見せどころ！",
            "挑戦者があらわれた！",
            "ここで一勝負といこうか",
            "決着をつけようじゃないか！",
            "実力を見せてやる！",
            "誰が一番か決めよう！",
            "負けるわけにはいかない！",
            "観客がざわめいている！",
            "さあ、はじめようか！"
        };

        private static readonly string[] MoneyUpPool =
        {
            "道ばたでお金を拾った！",
            "臨時収入が入った！",
            "宝箱を見つけた！",
            "届けたお礼にお金をもらった！",
            "くじが当たった！",
            "へそくりを掘り当てた！",
            "財布が急に重くなった！",
            "思わぬ小遣いが転がってきた",
            "空から小銭が降ってきた！",
            "商売がうまくいった！"
        };

        private static readonly string[] MoneyDownPool =
        {
            "お金を落とした！",
            "財布に穴が空いていた…",
            "うっかり買いすぎてしまった…",
            "通行料を取られた…",
            "スリに狙われた！",
            "気づいたら小銭が消えていた…",
            "高い買い物をしてしまった…",
            "財布が急に軽くなった…",
            "割れた壺の代金を払った…",
            "落とし穴に小銭を落とした…"
        };

        private static readonly string[] TerritoryPool =
        {
            "ここは私の土地だ！",
            "旗を立てて宣言した！",
            "見晴らしのいい土地だ！",
            "この場所、いただき！",
            "ここに旗を突き立てた！",
            "領地がまた広がった！",
            "この一帯は自分のものだ！",
            "誰にも渡さないぞ！",
            "良い土地を見つけたぞ！",
            "ここに拠点をかまえる！"
        };

        private static readonly string[] ItemPool =
        {
            "掘り出し物があるかも？",
            "いらっしゃいませ！",
            "店主が手招きしている",
            "何かいい道具はないだろうか",
            "品物がずらりと並んでいる",
            "値札をのぞいてみるか",
            "お買い物の時間だ！",
            "店の奥から声がかかった",
            "今日は何が並んでいる？",
            "使えそうな道具を探そう"
        };

        /// <summary>スタート＝ゴール（経路 index 0）の既定文言。</summary>
        public static IReadOnlyList<string> StartMessages => StartPool;

        /// <summary>イベント <paramref name="cellEvent"/> の既定文言（未定義の種別は通常マスのプール）。</summary>
        public static IReadOnlyList<string> Messages(BoardCellEvent cellEvent)
        {
            switch (cellEvent)
            {
                case BoardCellEvent.Forward:
                    return ForwardPool;
                case BoardCellEvent.Back:
                    return BackPool;
                case BoardCellEvent.MiniGame:
                    return MiniGamePool;
                case BoardCellEvent.MoneyUp:
                    return MoneyUpPool;
                case BoardCellEvent.MoneyDown:
                    return MoneyDownPool;
                case BoardCellEvent.Territory:
                    return TerritoryPool;
                case BoardCellEvent.Item:
                    return ItemPool;
                default:
                    return NonePool;
            }
        }

        /// <summary>
        /// 既定文言のプールを引く。<paramref name="isStart"/> が true（経路 index 0）なら
        /// イベント種別に依らずスタート専用プールを返す。
        /// </summary>
        public static IReadOnlyList<string> Pool(BoardCellEvent cellEvent, bool isStart)
        {
            return isStart ? StartMessages : Messages(cellEvent);
        }
    }
}
