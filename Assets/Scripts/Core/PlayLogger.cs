using UnityEngine;
using System.IO;
using System.Text;
using MyDongari.Combat;
using MyDongari.Mech;
using MyDongari.Movement;
using MyDongari.Input;

namespace MyDongari.Core
{
    public class PlayLogger : MonoBehaviour
    {
        [Header("설정")]
        [SerializeField] private float logInterval = 0.1f;
        [SerializeField] private string fileName = "playlog";

        [Header("P1 참조")]
        [SerializeField] private GameObject player1;

        [Header("P2 참조")]
        [SerializeField] private GameObject player2;

        private float _logTimer = 0f;
        private StringBuilder _sb;
        private string _filePath;
        private bool _isLogging = false;

        private void Start()
        {
            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _filePath = Application.dataPath + "/" + fileName + "_" + timestamp + ".csv";
            _sb = new StringBuilder();

            _sb.AppendLine("time,p1_hp,p1_boost,p1_heat,p1_state,p1_posX,p1_posZ,p1_moveX,p1_moveY,p2_hp,p2_boost,p2_heat,p2_state,p2_posX,p2_posZ,p2_moveX,p2_moveY,distance");

            _isLogging = true;
        }

        private void Update()
        {
            if (!_isLogging) return;
            if (GameManager.Instance != null && !GameManager.Instance.IsRoundActive) return;

            _logTimer += Time.deltaTime;
            if (_logTimer < logInterval) return;
            _logTimer = 0f;

            LogFrame();
        }

        private void LogFrame()
        {
            float time = Time.time;

            float p1Hp = 0f, p1Boost = 0f, p1Heat = 0f;
            string p1State = "none";
            float p1PosX = 0f, p1PosZ = 0f;
            float p1MoveX = 0f, p1MoveY = 0f;

            float p2Hp = 0f, p2Boost = 0f, p2Heat = 0f;
            string p2State = "none";
            float p2PosX = 0f, p2PosZ = 0f;
            float p2MoveX = 0f, p2MoveY = 0f;

            float distance = 0f;

            if (player1 != null)
            {
                Health h = player1.GetComponent<Health>();
                BoostGauge bg = player1.GetComponent<BoostGauge>();
                HeatGauge hg = player1.GetComponent<HeatGauge>();
                MechStateMachine sm = player1.GetComponent<MechStateMachine>();
                InputReader ir = player1.GetComponent<InputReader>();

                if (h != null) p1Hp = h.CurrentHealth;
                if (bg != null) p1Boost = bg.CurrentBoost;
                if (hg != null) p1Heat = hg.CurrentHeat;
                if (sm != null) p1State = sm.CurrentStateName;
                if (ir != null)
                {
                    p1MoveX = ir.MoveInput.x;
                    p1MoveY = ir.MoveInput.y;
                }
                p1PosX = player1.transform.position.x;
                p1PosZ = player1.transform.position.z;
            }

            if (player2 != null)
            {
                Health h = player2.GetComponent<Health>();
                BoostGauge bg = player2.GetComponent<BoostGauge>();
                HeatGauge hg = player2.GetComponent<HeatGauge>();
                MechStateMachine sm = player2.GetComponent<MechStateMachine>();
                InputReader ir = player2.GetComponent<InputReader>();

                if (h != null) p2Hp = h.CurrentHealth;
                if (bg != null) p2Boost = bg.CurrentBoost;
                if (hg != null) p2Heat = hg.CurrentHeat;
                if (sm != null) p2State = sm.CurrentStateName;
                if (ir != null)
                {
                    p2MoveX = ir.MoveInput.x;
                    p2MoveY = ir.MoveInput.y;
                }
                p2PosX = player2.transform.position.x;
                p2PosZ = player2.transform.position.z;
            }

            if (player1 != null && player2 != null)
            {
                distance = Vector3.Distance(player1.transform.position, player2.transform.position);
            }

            _sb.AppendLine(string.Format("{0:F2},{1:F1},{2:F1},{3:F1},{4},{5:F2},{6:F2},{7:F2},{8:F2},{9:F1},{10:F1},{11:F1},{12},{13:F2},{14:F2},{15:F2},{16:F2},{17:F2}",
                time, p1Hp, p1Boost, p1Heat, p1State, p1PosX, p1PosZ, p1MoveX, p1MoveY,
                p2Hp, p2Boost, p2Heat, p2State, p2PosX, p2PosZ, p2MoveX, p2MoveY, distance));
        }

        private void OnApplicationQuit()
        {
            SaveLog();
        }

        private void OnDestroy()
        {
            SaveLog();
        }

        private void SaveLog()
        {
            if (_sb == null || _sb.Length == 0) return;
            File.WriteAllText(_filePath, _sb.ToString());
            _sb.Clear();
        }
    }
}