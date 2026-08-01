namespace Common.Character
{
    /// <summary>
    /// 選択中のキャラクターをシーンをまたいで保持する Common シングルトン。
    /// <see cref="GameSession.GameSessionModel"/> と同様に、セッション中の選択状態を持つ。
    /// </summary>
    public sealed class CharacterSessionModel
    {
        /// <summary>現在選択されているキャラクター。既定は 1P の初期キャラ（＝カタログ先頭）。</summary>
        public CharacterId Selected { get; private set; } = CharacterCatalog.DefaultFor(0);

        public void Select(CharacterId id)
        {
            Selected = id;
        }
    }
}
