namespace Main.Board
{
    /// <summary>
    /// プレイヤーの状況（所持金・占領地）に添えるアイコン画像の Addressable アドレス。
    /// 上部ネームプレート（<see cref="PlayerNameplateView"/>）と詳細モーダル（<see cref="PlayerDetailPresenter"/>）が
    /// 同じ絵を使うので、アドレスはここ 1 か所に置く（未配置なら各画面が USS 描画のバッジにフォールバックする）。
    /// </summary>
    public static class PlayerStatIcons
    {
        /// <summary>所持金に添えるコインのアイコン。</summary>
        public const string CoinAddress = "Image/Icon/CoinIcon";

        /// <summary>占領地に添える陣地のアイコン。</summary>
        public const string TerritoryAddress = "Image/Icon/TeritoryIcon";
    }
}
