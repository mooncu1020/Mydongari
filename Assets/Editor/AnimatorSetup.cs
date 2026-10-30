using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using MyDongari.Visual;

namespace MyDongari.EditorTools
{
    /// <summary>
    /// 반복 동작(Idle/Walk/Run/Sprint)용 Animator Controller를 만들고 씬의 모델 Animator에 붙인다.
    /// 파라미터: Speed(수평 속도 m/s), MoveState(0 Idle, 1 Walk, 2 Run, 3 Sprint — MechVisualRig가 MechStateMachine 상태로 넣음).
    /// 상태의 클립 칸은 비워 둔다(사용자가 넣음). 클립이 빈 상태는 Write Defaults로 기본 자세에 머물러 자세 변화가 없다.
    /// 공격·방어·피격 동작은 여전히 MechVisualRig 코드가 그 위에 덧씌운다.
    /// </summary>
    public static class AnimatorSetup
    {
        public const string ControllerPath = "Assets/Animations/MechLocomotion.controller";
        private const float TransitionDuration = 0.15f;

        [MenuItem("Mydongari/Visual/Create Locomotion Animator Controller")]
        private static void CreateMenu()
        {
            AnimatorController controller = LoadController();
            bool created = controller == null;
            if (created) controller = Create();

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Mydongari Assign Locomotion Controller");
            int undoGroup = Undo.GetCurrentGroup();

            int assigned = 0;
            foreach (MechVisualRig rig in Object.FindObjectsByType<MechVisualRig>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Animator animator = rig.GetComponentInChildren<Animator>(true);
                if (animator == null) continue;
                Configure(animator, controller);
                SerializedObject so = new SerializedObject(rig);
                so.FindProperty("animator").objectReferenceValue = animator;
                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
                assigned++;
            }

            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeObject = controller;
            EditorUtility.DisplayDialog("Locomotion Animator",
                (created ? $"{ControllerPath}를 만들었습니다.\n" : $"기존 {ControllerPath}를 사용합니다.\n") +
                $"Animator가 있는 기체 {assigned}대에 연결했습니다" +
                (assigned == 0 ? " (리깅된 모델을 Apply Model To Mechs로 붙이면 자동으로 연결됩니다)" : "") +
                ".\n\n컨트롤러를 열어 Idle / Walk / Run / Sprint 상태의 Motion 칸에 클립을 넣으세요. 비워 둔 상태는 코드 걷기 동작이 대신합니다.", "확인");
        }

        public static AnimatorController LoadController()
        {
            return AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        }

        private static AnimatorController Create()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Animations")) AssetDatabase.CreateFolder("Assets", "Animations");
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter(MechVisualRig.SpeedParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(MechVisualRig.MoveStateParameter, AnimatorControllerParameterType.Int);

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            for (int i = 0; i < MechVisualRig.LocomotionStates.Length; i++)
            {
                AnimatorState state = stateMachine.AddState(MechVisualRig.LocomotionStates[i], new Vector3(300f, 60f * i, 0f));
                // 클립은 사용자가 넣는다. 비어 있으면 Write Defaults로 기본(쉴 때) 자세 = 자세 변화 없음
                state.motion = null;
                state.writeDefaultValues = true;
                if (i == 0) stateMachine.defaultState = state;

                // 어느 상태에서든 MoveState가 바뀌면 바로 그 상태로 (MechStateMachine 상태를 그대로 따름)
                AnimatorStateTransition transition = stateMachine.AddAnyStateTransition(state);
                transition.AddCondition(AnimatorConditionMode.Equals, i, MechVisualRig.MoveStateParameter);
                transition.hasExitTime = false;
                transition.hasFixedDuration = true;
                transition.duration = TransitionDuration;
                transition.canTransitionToSelf = false;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        /// <summary>모델 Animator에 컨트롤러를 붙이고, 모델이 기체를 움직이지 않게 루트 모션을 끈다.</summary>
        public static void Configure(Animator animator, RuntimeAnimatorController controller)
        {
            Undo.RecordObject(animator, "Configure Animator");
            if (controller != null) animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
    }
}
