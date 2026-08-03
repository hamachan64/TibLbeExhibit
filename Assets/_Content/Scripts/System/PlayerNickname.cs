namespace STYLY.TIB_Template
{
    /// <summary>
    /// ClientNo からプレイヤーの自動ニックネームを生成する。
    /// 名前の自由入力UIは作らず、接続時に自動的に「ハンターNo.xx」形式の名前を割り当てる。
    /// </summary>
    public static class PlayerNickname
    {
        /// <summary>
        /// ClientNo を "ハンターNo.01" 形式（2桁ゼロ埋め、3桁以上はそのまま）にフォーマットする。
        /// </summary>
        public static string Format(int clientNo)
        {
            return $"ハンターNo.{clientNo:00}";
        }
    }
}
