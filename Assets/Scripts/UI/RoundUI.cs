using UnityEngine;
using UnityEngine.UI;
using MyDongari.Core;

namespace MyDongari.UI
{
    /// <summary>
    /// 화면 분할 가운데 공용 UI: 카운트다운 / FIGHT / 라운드 승자 / 매치 결과와 양쪽 승수.
    /// GameManager 상태를 읽기만 한다 (라운드 흐름은 GameManager가 결정).
    /// 기본 폰트에 한글 글리프가 없을 수 있어 표시 문구는 영어로 둔다.
    /// </summary>
    public class RoundUI : MonoBehaviour
    {
        [Header("텍스트")]
        [SerializeField] private Text centerText;
        [SerializeField] private Text subText;
        [SerializeField] private Text player1WinsText;
        [SerializeField] private Text player2WinsText;

        [Header("문구")]
        [SerializeField] private string fightLabel = "FIGHT!";
        [SerializeField] private string drawLabel = "DRAW";
        [SerializeField] private string rematchHint = "Press START (Enter) to rematch";
        [Tooltip("승수 표시에 쓰는 기호 (승리 1회당 하나)")]
        [SerializeField] private string winMark = "●";
        [SerializeField] private string emptyMark = "○";

        private void Update()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null) return;

            switch (gm.Phase)
            {
                case MatchPhase.Countdown:
                    SetText(centerText, Mathf.CeilToInt(Mathf.Max(gm.PhaseTimeRemaining, 0.01f)).ToString());
                    SetText(subText, $"ROUND {gm.RoundNumber}");
                    break;

                case MatchPhase.Fight:
                    SetText(centerText, gm.ShowFightBanner ? fightLabel : "");
                    SetText(subText, "");
                    break;

                case MatchPhase.RoundOver:
                    SetText(centerText, gm.LastRoundWinner == 0 ? drawLabel : $"P{gm.LastRoundWinner} WIN");
                    SetText(subText, "");
                    break;

                case MatchPhase.MatchOver:
                    SetText(centerText, $"P{gm.MatchWinner} WINS THE MATCH");
                    SetText(subText, rematchHint);
                    break;
            }

            SetText(player1WinsText, "P1 " + Marks(gm.Player1Wins, gm.RoundsToWin));
            SetText(player2WinsText, Marks(gm.Player2Wins, gm.RoundsToWin) + " P2");
        }

        private string Marks(int wins, int roundsToWin)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < roundsToWin; i++) sb.Append(i < wins ? winMark : emptyMark);
            return sb.ToString();
        }

        private static void SetText(Text text, string value)
        {
            if (text != null && text.text != value) text.text = value;
        }
    }
}
