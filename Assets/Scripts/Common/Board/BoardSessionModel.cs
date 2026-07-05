namespace Common.Board
{
    /// <summary>
    /// 選択中のマップをシーンをまたいで保持する Common シングルトン。
    /// <see cref="Character.CharacterSessionModel"/> のマップ版。
    /// マップ本体（BoardDefinition）は Main アセンブリにあり Common からは参照できないため、
    /// ここでは識別子（マップ資産名）だけを持ち、実体の解決はカタログ（Main の BoardCatalog）が行う。
    /// </summary>
    public sealed class BoardSessionModel
    {
        /// <summary>選択中マップの識別子（マップ資産名）。未選択なら空文字。</summary>
        public string SelectedId { get; private set; } = string.Empty;

        /// <summary>マップが選択済みか。未選択ならカタログ既定（先頭）にフォールバックする。</summary>
        public bool HasSelection => !string.IsNullOrEmpty(SelectedId);

        public void Select(string id)
        {
            SelectedId = id ?? string.Empty;
        }
    }
}
