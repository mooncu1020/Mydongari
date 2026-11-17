using System;
using System.IO;
using UnityEngine;
using MyDongari.Game.Input;

namespace MyDongari.Game.Flow
{
    /// <summary>
    /// 화면 확인용 스크린샷 (개발 도구). 빌드를 "-lookShots 폴더"로 실행하면 타이틀 → 조작법 화면 → VERSUS 시작 직후 →
    /// 두 기체가 가까워진 장면을 차례로 PNG로 남기고 스스로 종료한다. 에디터 창 없이 화면 정리 결과(색·하늘·외곽선·HUD 글자)를 보려고 만들었다.
    /// 평소 실행에는 아무 영향이 없다(인자가 없으면 만들어지지 않는다).
    /// </summary>
    public class LookShotRunner : MonoBehaviour
    {
        public const string Argument = "-lookShots";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateIfRequested()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, Argument);
            if (index < 0 || index + 1 >= args.Length) return;
            var go = new GameObject("LookShotRunner");
            DontDestroyOnLoad(go);
            go.AddComponent<LookShotRunner>()._folder = args[index + 1];
        }

        private string _folder;
        private int _step;
        private int _wait;
        private InputReader _p1, _p2;

        private void Shot(string name)
        {
            Directory.CreateDirectory(_folder);
            ScreenCapture.CaptureScreenshot(Path.Combine(_folder, name + ".png"));
        }

        private void FixedUpdate()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null) return;
            _wait++;
            switch (_step)
            {
                case 0:
                    if (_wait < 90) return;
                    Shot("1_title");
                    Next();
                    break;
                case 1:
                    if (_wait < 10) return;
                    gm.SetControlsOpen(true);
                    Next();
                    break;
                case 2:
                    if (_wait < 30) return;
                    Shot("2_controls");
                    Next();
                    break;
                case 3:
                    if (_wait < 10) return;
                    gm.SetControlsOpen(false);
                    if (!gm.CanStartVersus)
                    {
                        Application.Quit(0);
                        return;
                    }
                    gm.StartMode(GameMode.Versus);
                    Next();
                    break;
                case 4:
                    if (gm.Mode != GameMode.Versus || !gm.IsRoundActive || _wait < 60) return;
                    Shot("3_versus_start");
                    _p1 = gm.Player1.GetComponent<InputReader>();
                    _p2 = gm.Player2.GetComponent<InputReader>();
                    if (_p1 != null) _p1.SetScripted(true);
                    if (_p2 != null) _p2.SetScripted(true);
                    Next();
                    break;
                case 5:
                    // 둘 다 앞으로 걸어 가까워진다 (스폰은 50m 떨어져 있다)
                    if (_p1 != null) _p1.ScriptState(Vector2.up, false, false, false);
                    if (_p2 != null) _p2.ScriptState(Vector2.up, false, false, false);
                    float distance = Vector3.Distance(gm.Player1.transform.position, gm.Player2.transform.position);
                    if (distance > 7f && _wait < 600) return;
                    if (_p1 != null) _p1.ScriptState(Vector2.zero, false, false, false);
                    if (_p2 != null) _p2.ScriptState(Vector2.zero, false, false, true);
                    Next();
                    break;
                case 6:
                    if (_wait < 40) return;
                    Shot("4_versus_close");
                    // 1P가 가드 중인 2P를 친다 → 가드 불꽃
                    if (_p1 != null) _p1.ScriptPressAttack();
                    Next();
                    break;
                case 7:
                    if (_wait < 26) return;
                    Shot("5_versus_hit");
                    Next();
                    break;
                case 8:
                    if (_wait < 30) return;
                    Application.Quit(0);
                    _step = 99;
                    break;
            }
        }

        private void Next()
        {
            _step++;
            _wait = 0;
        }
    }
}
