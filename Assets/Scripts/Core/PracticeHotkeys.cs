using UnityEngine;
using MyDongari.Combat;
using MyDongari.Input;
using MyDongari.Mech;

namespace MyDongari.Core
{
    /// <summary>
    /// 연습 모드 단축키 (P1 입력만, PRACTICE의 Fight 중에만 — 메뉴·KO·카운트다운 중엔 무시).
    /// 봇 모드 순환: 패드 Select 짧게 / Tab. 즉시 리셋: 패드 Select 길게 / R. 내 히트 프리셋: 1~4.
    /// 키 판정은 InputReader가, 여기는 "연습 중이면 실행"만 한다. GameManager 오브젝트에 하나 (자동 부착).
    /// </summary>
    public class PracticeHotkeys : MonoBehaviour
    {
        [Tooltip("키보드 1~4에 대응하는 내 히트 비율 (과열·가드 깨짐 테스트용). 1.0이면 과열 상태")]
        [SerializeField] private float[] heatPresets = { 0f, 0.5f, 0.9f, 1f };

        private void Update()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.Mode != GameMode.Practice) return;
            if (gm.Phase != MatchPhase.Fight || gm.IsPracticePaused) return;

            GameObject player = gm.Player1;
            InputReader input = player != null ? player.GetComponent<InputReader>() : null;
            if (input == null) return;

            if (input.BotCyclePressed)
            {
                MeleeDummy bot = gm.PracticeBot;
                if (bot != null) bot.CycleMode();
            }

            // 리셋은 히트 프리셋보다 먼저: 같은 프레임에 둘 다 누르면 리셋 후 프리셋이 남는다
            if (input.PracticeResetPressed) gm.ResetPracticeNow();

            int preset = input.HeatPresetPressed;
            if (preset >= 0 && preset < heatPresets.Length)
            {
                HeatGauge heat = player.GetComponent<HeatGauge>();
                if (heat != null) heat.SetHeatRatio(heatPresets[preset]);
            }
        }
    }
}
