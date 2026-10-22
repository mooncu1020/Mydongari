using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using MyDongari.Core;
using MyDongari.Input;
using MyDongari.UI;
using UnityCamera = UnityEngine.Camera;

namespace MyDongari.EditorTools
{
    /// <summary>
    /// 1인 + 더미 씬을 2인 화면 분할 대전 씬으로 바꾸는 메뉴.
    /// 씬 YAML을 손으로 고치지 않기 위해 에디터 API로 복제·배선하고, 전부 Undo 한 번으로 되돌릴 수 있게 한다.
    /// </summary>
    public static class VersusSceneSetup
    {
        private const string P2Name = "Mech_P2";
        private const string P2CanvasName = "Canvas_P2";
        private const string OpponentBarName = "OpponentHPBar";

        [MenuItem("Mydongari/Setup Versus Scene")]
        private static void Setup()
        {
            GameManager gameManager = Object.FindFirstObjectByType<GameManager>();
            if (gameManager == null)
            {
                EditorUtility.DisplayDialog("Versus Setup", "씬에 GameManager가 없습니다.", "확인");
                return;
            }
            if (GameObject.Find(P2Name) != null)
            {
                EditorUtility.DisplayDialog("Versus Setup", $"이미 {P2Name}가 있습니다. 다시 하려면 Undo하거나 지운 뒤 실행하세요.", "확인");
                return;
            }

            SerializedObject gmSo = new SerializedObject(gameManager);
            GameObject p1 = gmSo.FindProperty("player1").objectReferenceValue as GameObject;
            GameObject currentP2 = gmSo.FindProperty("player2").objectReferenceValue as GameObject;
            Transform spawn2 = gmSo.FindProperty("player2SpawnPoint").objectReferenceValue as Transform;
            if (p1 == null || p1.GetComponent<PlayerInput>() == null)
            {
                EditorUtility.DisplayDialog("Versus Setup", "GameManager의 Player1에 PlayerInput이 있는 기체(Mech)를 먼저 지정하세요.", "확인");
                return;
            }
            // 지금 Player2가 입력 없는 기체(더미)면 1인 모드용 상대로 보관한다
            GameObject dummy = currentP2 != null && currentP2.GetComponent<PlayerInput>() == null ? currentP2 : null;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Mydongari Setup Versus Scene");
            int undoGroup = Undo.GetCurrentGroup();

            // 0) 복제 전에 P1에 빠진 기체 컴포넌트를 붙여 둔다 (P2가 같은 구성을 갖게)
            if (p1.GetComponent<MyDongari.Combat.ThrusterLunge>() == null)
            {
                Undo.AddComponent<MyDongari.Combat.ThrusterLunge>(p1);
            }

            // 1) P2 기체 복제. 카메라가 Mech 자식이라 같이 복제되고, 기체 내부 참조는 복제본끼리 자동 재연결된다
            Vector3 p2Pos = spawn2 != null ? spawn2.position : p1.transform.position + p1.transform.forward * 10f;
            Quaternion p2Rot = spawn2 != null ? spawn2.rotation : Quaternion.LookRotation(-p1.transform.forward);
            GameObject p2 = Object.Instantiate(p1, p2Pos, p2Rot);
            p2.name = P2Name;
            Undo.RegisterCreatedObjectUndo(p2, "Create P2");
            ClearOpponent(p1);
            ClearOpponent(p2);

            // 2) 카메라 좌우 분할. 오디오 리스너는 P1 것 하나만 남긴다
            UnityCamera p1Cam = p1.GetComponentInChildren<UnityCamera>(true);
            UnityCamera p2Cam = p2.GetComponentInChildren<UnityCamera>(true);
            if (p1Cam != null)
            {
                Undo.RecordObject(p1Cam, "Split P1 Camera");
                p1Cam.rect = new Rect(0f, 0f, 0.5f, 1f);
            }
            if (p2Cam != null)
            {
                p2Cam.rect = new Rect(0.5f, 0f, 0.5f, 1f);
                p2Cam.gameObject.tag = "Untagged";
                AudioListener listener = p2Cam.GetComponent<AudioListener>();
                if (listener != null) Undo.DestroyObjectImmediate(listener);
            }

            // 3) HUD 2벌: 기존 캔버스를 P1 카메라 화면에 붙이고, 복제해서 P2 카메라 화면에 붙인다
            HUDController p1Hud = Object.FindFirstObjectByType<HUDController>();
            GameObject p2CanvasGo = null;
            if (p1Hud != null)
            {
                Canvas p1Canvas = p1Hud.GetComponentInParent<Canvas>();
                if (p1Canvas != null)
                {
                    AttachCanvasToCamera(p1Canvas, p1Cam);
                    p2CanvasGo = Object.Instantiate(p1Canvas.gameObject);
                    p2CanvasGo.name = P2CanvasName;
                    Undo.RegisterCreatedObjectUndo(p2CanvasGo, "Create P2 Canvas");
                    AttachCanvasToCamera(p2CanvasGo.GetComponent<Canvas>(), p2Cam);
                }

                WireHud(p1Hud, p1, p1Cam);
                HUDController p2Hud = p2CanvasGo != null ? p2CanvasGo.GetComponentInChildren<HUDController>(true) : null;
                if (p2Hud != null) WireHud(p2Hud, p2, p2Cam);
            }

            // 4) GameManager: P2 지정, 더미는 1인 모드용으로 보관
            gmSo.Update();
            gmSo.FindProperty("versusMode").boolValue = true;
            gmSo.FindProperty("player2").objectReferenceValue = p2;
            gmSo.FindProperty("dummyOpponent").objectReferenceValue = dummy;
            gmSo.FindProperty("player1Camera").objectReferenceValue = p1Cam;
            SerializedProperty versusOnly = gmSo.FindProperty("versusOnlyObjects");
            versusOnly.arraySize = p2CanvasGo != null ? 2 : 1;
            versusOnly.GetArrayElementAtIndex(0).objectReferenceValue = p2;
            if (p2CanvasGo != null) versusOnly.GetArrayElementAtIndex(1).objectReferenceValue = p2CanvasGo;
            gmSo.ApplyModifiedProperties();

            // 5) 패드 고정 배정
            VersusInputPairing pairing = gameManager.GetComponent<VersusInputPairing>();
            if (pairing == null) pairing = Undo.AddComponent<VersusInputPairing>(gameManager.gameObject);
            SerializedObject pairSo = new SerializedObject(pairing);
            pairSo.FindProperty("player1Input").objectReferenceValue = p1.GetComponent<PlayerInput>();
            pairSo.FindProperty("player2Input").objectReferenceValue = p2.GetComponent<PlayerInput>();
            pairSo.ApplyModifiedProperties();

            // 6) 로거
            PlayLogger logger = Object.FindFirstObjectByType<PlayLogger>();
            if (logger != null)
            {
                SerializedObject logSo = new SerializedObject(logger);
                logSo.FindProperty("player1").objectReferenceValue = p1;
                logSo.FindProperty("player2").objectReferenceValue = p2;
                logSo.ApplyModifiedProperties();
            }

            // 7) 가운데 공용 라운드 UI
            RoundUISetup.CreateIfMissing();

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(p1.scene);
            Selection.activeGameObject = p2;

            EditorUtility.DisplayDialog("Versus Setup",
                $"{P2Name} 생성, 화면 분할, HUD 2벌, GameManager·PlayLogger·패드 배정 연결 완료.\n" +
                (dummy != null ? $"{dummy.name}는 1인 모드(GameManager → Versus Mode 끄기)용 상대로 보관했습니다.\n" : "") +
                "Ctrl+Z 한 번으로 전부 되돌릴 수 있습니다. 확인 후 씬을 저장하세요.", "확인");
        }

        private static void ClearOpponent(GameObject mech)
        {
            // 복제 전에 인스펙터에서 지정해 둔 상대(더미 등)가 있으면 GameManager가 새로 연결하도록 비운다
            MechIdentity identity = mech.GetComponent<MechIdentity>();
            if (identity == null) return;
            SerializedObject so = new SerializedObject(identity);
            so.FindProperty("opponent").objectReferenceValue = null;
            so.ApplyModifiedProperties();
        }

        private static void AttachCanvasToCamera(Canvas canvas, UnityCamera cam)
        {
            if (canvas == null || cam == null) return;
            Undo.RecordObject(canvas, "Attach Canvas To Camera");
            // Screen Space - Camera 캔버스는 그 카메라의 viewport 영역에만 그려진다 → 화면 분할 HUD
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
        }

        private static void WireHud(HUDController hud, GameObject mech, UnityCamera cam)
        {
            SerializedObject so = new SerializedObject(hud);
            so.FindProperty("heatGauge").objectReferenceValue = mech.GetComponent<MyDongari.Mech.HeatGauge>();
            so.FindProperty("boostGauge").objectReferenceValue = mech.GetComponent<MyDongari.Mech.BoostGauge>();
            so.FindProperty("playerHealth").objectReferenceValue = mech.GetComponent<MyDongari.Combat.Health>();
            so.FindProperty("lockOnSystem").objectReferenceValue = mech.GetComponent<MyDongari.Combat.LockOnSystem>();
            so.FindProperty("mechStateMachine").objectReferenceValue = mech.GetComponent<MyDongari.Movement.MechStateMachine>();
            so.FindProperty("viewCamera").objectReferenceValue = cam;

            LinearGauge opponentFill = CreateOpponentBar(hud);
            if (opponentFill != null) so.FindProperty("opponentHpBarFill").objectReferenceValue = opponentFill;
            so.ApplyModifiedProperties();
        }

        /// <summary>자기 HP 바를 복제해 화면 위쪽 가운데에 작은 상대 체력 바를 만든다.</summary>
        private static LinearGauge CreateOpponentBar(HUDController hud)
        {
            Transform existing = hud.transform.Find(OpponentBarName);
            Transform hpBar = hud.transform.Find("HPBar");
            if (existing == null && hpBar == null) return null;

            GameObject bar;
            if (existing != null)
            {
                bar = existing.gameObject;
            }
            else
            {
                bar = Object.Instantiate(hpBar.gameObject, hpBar.parent);
                bar.name = OpponentBarName;
                Undo.RegisterCreatedObjectUndo(bar, "Create Opponent HP Bar");
                RectTransform rect = (RectTransform)bar.transform;
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -40f);
                rect.localScale = Vector3.one * 0.6f;
            }

            foreach (LinearGauge gauge in bar.GetComponentsInChildren<LinearGauge>(true))
            {
                if (gauge.isBackground) continue;
                Undo.RecordObject(gauge, "Tint Opponent HP Bar");
                gauge.color = new Color(1f, 0.35f, 0.35f);
                return gauge;
            }
            return null;
        }
    }
}
