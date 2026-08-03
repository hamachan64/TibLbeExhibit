namespace STYLY.TIB_Template.Leaderboard
{
    /// <summary>
    /// リーダーボード（トップN表示）1件分のエントリ。
    /// </summary>
    [System.Serializable]
    public readonly struct LeaderboardEntry
    {
        public readonly int ClientNo;
        public readonly int Score;

        public LeaderboardEntry(int clientNo, int score)
        {
            ClientNo = clientNo;
            Score = score;
        }
    }
}
