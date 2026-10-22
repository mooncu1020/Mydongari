using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MyDongari.Combat;
using MyDongari.Combat.Skills;
using MyDongari.Core;
using MyDongari.Mech;
using MyDongari.Movement;

namespace MyDongari.EditorTools
{
    /// <summary>
    /// BernardTuning 에셋 생성·연결, 그리고 씬에 저장돼 있던 예전 컴포넌트 값을 에셋으로 옮기는 메뉴.
    /// 예전 값은 각 컴포넌트에 [HideInInspector]로 숨겨 둔 필드에서 SerializedObject로 읽는다.
    /// </summary>
    public static class TuningSetup
    {
        private const string ResourcesFolder = "Assets/Resources";
        private static readonly string AssetPath = ResourcesFolder + "/" + BernardTuning.ResourcesName + ".asset";

        /// <summary>(예전 값이 있던 컴포넌트, 그 필드 이름, 에셋 안의 경로)</summary>
        private struct FieldMap
        {
            public System.Type Component;
            public string Legacy;
            public string Path;
            public FieldMap(System.Type component, string legacy, string path)
            {
                Component = component;
                Legacy = legacy;
                Path = path;
            }
        }

        private static readonly FieldMap[] Maps =
        {
            // 이동 (MechController)
            new FieldMap(typeof(MechController), "walkSpeed", "movement.walkSpeed"),
            new FieldMap(typeof(MechController), "runSpeed", "movement.runSpeed"),
            new FieldMap(typeof(MechController), "sprintSpeed", "movement.sprintSpeed"),
            new FieldMap(typeof(MechController), "maxWalkSpeed", "movement.maxWalkSpeed"),
            new FieldMap(typeof(MechController), "maxRunSpeed", "movement.maxRunSpeed"),
            new FieldMap(typeof(MechController), "maxSprintSpeed", "movement.maxSprintSpeed"),
            new FieldMap(typeof(MechController), "rotationSpeed", "movement.rotationSpeed"),
            new FieldMap(typeof(MechController), "hardLockTurnSpeed", "movement.hardLockTurnSpeed"),
            new FieldMap(typeof(MechController), "turnResistance", "movement.turnResistance"),
            new FieldMap(typeof(MechController), "spinUpTime", "movement.spinUpTime"),
            // 부스트
            new FieldMap(typeof(BoostGauge), "maxBoost", "boost.maxBoost"),
            new FieldMap(typeof(BoostGauge), "recoveryDelay", "boost.recoveryDelay"),
            new FieldMap(typeof(BoostGauge), "recoveryRate", "boost.recoveryRate"),
            new FieldMap(typeof(BoostGauge), "sprintDrainRate", "boost.sprintDrainRate"),
            new FieldMap(typeof(BoostGauge), "exhaustRecoverRatio", "boost.exhaustRecoverRatio"),
            new FieldMap(typeof(BoostGauge), "sideStepCost", "boost.sideStepCost"),
            new FieldMap(typeof(BoostGauge), "quickTurnCost", "boost.quickTurnCost"),
            new FieldMap(typeof(BoostGauge), "slideCost", "boost.slideCost"),
            // 히트
            new FieldMap(typeof(HeatGauge), "maxHeat", "heat.maxHeat"),
            new FieldMap(typeof(HeatGauge), "coolingRate", "heat.coolingRate"),
            new FieldMap(typeof(HeatGauge), "sprintCoolingReduction", "heat.sprintCoolingReduction"),
            // 펀치 / 맞딜
            new FieldMap(typeof(MeleeSystem), "meleeRange", "punch.range"),
            new FieldMap(typeof(MeleeSystem), "punchHalfAngle", "punch.halfAngle"),
            new FieldMap(typeof(MeleeSystem), "meleeDamage", "punch.damage"),
            new FieldMap(typeof(MeleeSystem), "meleeCooldown", "punch.cooldown"),
            new FieldMap(typeof(MeleeSystem), "heatPerPunch", "punch.heatPerPunch"),
            new FieldMap(typeof(MeleeSystem), "clashKnockback", "punch.clashKnockback"),
            // 가드
            new FieldMap(typeof(MeleeSystem), "guardMeleeReduction", "guard.meleeReduction"),
            new FieldMap(typeof(MeleeSystem), "guardProjectileReduction", "guard.projectileReduction"),
            new FieldMap(typeof(MeleeSystem), "guardHeatPerBlock", "guard.heatPerBlock"),
            new FieldMap(typeof(MeleeSystem), "guardBreakStun", "guard.breakStun"),
            new FieldMap(typeof(MeleeSystem), "guardReraiseDelay", "guard.reraiseDelay"),
            new FieldMap(typeof(MeleeSystem), "guardDamping", "guard.damping"),
            new FieldMap(typeof(MeleeSystem), "guardReleaseDamping", "guard.releaseDamping"),
            // 패리
            new FieldMap(typeof(MeleeSystem), "parryWindow", "parry.window"),
            new FieldMap(typeof(MeleeSystem), "parryReflectRatio", "parry.reflectRatio"),
            new FieldMap(typeof(MeleeSystem), "parryReflectKnockback", "parry.reflectKnockback"),
            // 태클
            new FieldMap(typeof(MeleeSystem), "tackleDamage", "tackle.damage"),
            new FieldMap(typeof(MeleeSystem), "tackleKnockback", "tackle.knockback"),
            new FieldMap(typeof(MeleeSystem), "tackleRange", "tackle.range"),
            new FieldMap(typeof(MeleeSystem), "tackleHalfAngle", "tackle.halfAngle"),
            new FieldMap(typeof(MeleeSystem), "tackleCooldown", "tackle.cooldown"),
            new FieldMap(typeof(MeleeSystem), "victimStunDuration", "tackle.victimStun"),
            new FieldMap(typeof(MeleeSystem), "tackleRecoveryStun", "tackle.recoveryStun"),
            // 대시 어택
            new FieldMap(typeof(MeleeSystem), "dashAttackDamage", "dashAttack.damage"),
            new FieldMap(typeof(MeleeSystem), "dashAttackKnockback", "dashAttack.knockback"),
            new FieldMap(typeof(MeleeSystem), "dashAttackStun", "dashAttack.stun"),
            // 런지
            new FieldMap(typeof(ThrusterLunge), "lungeDistance", "lunge.distance"),
            new FieldMap(typeof(ThrusterLunge), "lungeSpeed", "lunge.speed"),
            new FieldMap(typeof(ThrusterLunge), "endSpeedRatio", "lunge.endSpeedRatio"),
            new FieldMap(typeof(ThrusterLunge), "timeoutMultiplier", "lunge.timeoutMultiplier"),
            new FieldMap(typeof(ThrusterLunge), "boostCost", "lunge.boostCost"),
            new FieldMap(typeof(ThrusterLunge), "damage", "lunge.damage"),
            new FieldMap(typeof(ThrusterLunge), "hitStun", "lunge.hitStun"),
            new FieldMap(typeof(ThrusterLunge), "hitKnockback", "lunge.hitKnockback"),
            new FieldMap(typeof(ThrusterLunge), "hitRadius", "lunge.hitRadius"),
            new FieldMap(typeof(ThrusterLunge), "hitForwardOffset", "lunge.hitForwardOffset"),
            new FieldMap(typeof(ThrusterLunge), "missLockDelay", "lunge.missLockDelay"),
            // 앵커 클로
            new FieldMap(typeof(AnchorClaw), "pullSpeed", "anchorClaw.pullSpeed"),
            new FieldMap(typeof(AnchorClaw), "pullDuration", "anchorClaw.pullDuration"),
            new FieldMap(typeof(AnchorClaw), "cooldown", "anchorClaw.cooldown"),
            new FieldMap(typeof(AnchorClaw), "hookImpactSpeed", "anchorClaw.hookImpactSpeed"),
            new FieldMap(typeof(AnchorClaw), "hookEaseTime", "anchorClaw.hookEaseTime"),
            new FieldMap(typeof(AnchorClaw), "hookImpactStun", "anchorClaw.hookImpactStun"),
            new FieldMap(typeof(AnchorClaw), "pileDriverDamage", "anchorClaw.pileDriverDamage"),
            // 임팩트 슬램
            new FieldMap(typeof(ImpactSlam), "slamRange", "impactSlam.range"),
            new FieldMap(typeof(ImpactSlam), "slamAngle", "impactSlam.angle"),
            new FieldMap(typeof(ImpactSlam), "slamDamage", "impactSlam.damage"),
            new FieldMap(typeof(ImpactSlam), "slamKnockback", "impactSlam.knockback"),
            new FieldMap(typeof(ImpactSlam), "slamKnockbackLift", "impactSlam.knockbackLift"),
            new FieldMap(typeof(ImpactSlam), "cooldown", "impactSlam.cooldown"),
            // 락온
            new FieldMap(typeof(LockOnSystem), "lockOnRange", "lockOn.softRange"),
            new FieldMap(typeof(LockOnSystem), "lockOnAngle", "lockOn.softAngle"),
            new FieldMap(typeof(LockOnSystem), "hardLockEnterRange", "lockOn.hardEnterRange"),
            new FieldMap(typeof(LockOnSystem), "hardLockExitRange", "lockOn.hardExitRange"),
            new FieldMap(typeof(LockOnSystem), "hardLockMaxAngle", "lockOn.hardMaxAngle"),
            new FieldMap(typeof(LockOnSystem), "hardLockTrackSpeed", "lockOn.hardTrackSpeed"),
            // 히트스톱 (기체가 아니라 GameManager 오브젝트에 있다)
            new FieldMap(typeof(HitStop), "enableHitStop", "hitStop.enabled"),
            new FieldMap(typeof(HitStop), "punchDuration", "hitStop.punch"),
            new FieldMap(typeof(HitStop), "lungeTackleDuration", "hitStop.lungeTackle"),
            new FieldMap(typeof(HitStop), "clashDuration", "hitStop.clash"),
            new FieldMap(typeof(HitStop), "otherDuration", "hitStop.other"),
        };

        [MenuItem("Mydongari/Tuning/Create Default Tuning And Assign")]
        private static void CreateAndAssignMenu()
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Mydongari Create Tuning");
            int undoGroup = Undo.GetCurrentGroup();

            bool created;
            BernardTuning tuning = EnsureAsset(out created);
            int assigned = AssignToSceneMechs(tuning);

            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeObject = tuning;
            EditorUtility.DisplayDialog("Bernard Tuning",
                (created ? $"{AssetPath}를 코드 기본값으로 만들었습니다.\n" : $"기존 에셋을 사용합니다: {AssetDatabase.GetAssetPath(tuning)}\n") +
                $"기체 {assigned}대(MeleeSystem이 있는 오브젝트)에 MechTuning을 연결했습니다. 씬을 저장하세요.\n\n" +
                "씬에서 조정해 둔 예전 값을 옮기려면 Mydongari/Tuning/Copy P1 Values To Tuning을 실행하세요.", "확인");
        }

        [MenuItem("Mydongari/Tuning/Copy P1 Values To Tuning")]
        private static void CopyP1ValuesMenu()
        {
            GameManager gameManager = Object.FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
            GameObject p1 = gameManager != null
                ? new SerializedObject(gameManager).FindProperty("player1").objectReferenceValue as GameObject
                : null;
            if (p1 == null)
            {
                EditorUtility.DisplayDialog("Bernard Tuning", "GameManager의 Player1 기체를 찾지 못했습니다.", "확인");
                return;
            }

            bool created;
            BernardTuning tuning = EnsureAsset(out created);
            if (!EditorUtility.DisplayDialog("Bernard Tuning",
                    $"{p1.name}의 컴포넌트에 저장돼 있던 예전 값(숨겨진 필드)과 GameManager의 HitStop 값을\n" +
                    $"{AssetDatabase.GetAssetPath(tuning)}에 덮어씁니다. (Ctrl+Z로 되돌릴 수 있음)", "복사", "취소"))
            {
                return;
            }

            SerializedObject dst = new SerializedObject(tuning);
            var changes = new List<string>();
            var missing = new HashSet<string>();
            foreach (FieldMap map in Maps)
            {
                // 히트스톱만 GameManager 쪽, 나머지는 P1 기체
                GameObject owner = map.Component == typeof(HitStop) ? gameManager.gameObject : p1;
                Component component = owner.GetComponent(map.Component);
                if (component == null)
                {
                    missing.Add($"{owner.name}/{map.Component.Name}");
                    continue;
                }

                SerializedProperty from = new SerializedObject(component).FindProperty(map.Legacy);
                SerializedProperty to = dst.FindProperty(map.Path);
                if (from == null || to == null)
                {
                    missing.Add($"{map.Component.Name}.{map.Legacy} → {map.Path} (필드 없음)");
                    continue;
                }

                if (to.propertyType == SerializedPropertyType.Boolean)
                {
                    if (to.boolValue != from.boolValue) changes.Add($"{map.Path}: {to.boolValue} → {from.boolValue}");
                    to.boolValue = from.boolValue;
                }
                else
                {
                    if (!Mathf.Approximately(to.floatValue, from.floatValue)) changes.Add($"{map.Path}: {to.floatValue} → {from.floatValue}");
                    to.floatValue = from.floatValue;
                }
            }
            // ApplyModifiedProperties는 Undo에 기록된다
            dst.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            var report = new StringBuilder();
            report.AppendLine(changes.Count == 0 ? "바뀐 값 없음 (에셋과 씬 값이 같음)." : $"바뀐 값 {changes.Count}개:");
            // 다이얼로그가 화면을 넘지 않게 앞쪽만 보여 준다 (전체 값은 에셋 인스펙터에서 확인)
            for (int i = 0; i < changes.Count && i < 25; i++) report.AppendLine(changes[i]);
            if (changes.Count > 25) report.AppendLine($"… 외 {changes.Count - 25}개");
            if (missing.Count > 0) report.AppendLine("\n건너뜀(컴포넌트 없음): " + string.Join(", ", missing));

            Selection.activeObject = tuning;
            EditorUtility.DisplayDialog("Bernard Tuning", report.ToString(), "확인");
        }

        /// <summary>프로젝트에 BernardTuning 에셋이 있으면 그걸, 없으면 Resources에 코드 기본값으로 만든다.</summary>
        public static BernardTuning EnsureAsset(out bool created)
        {
            created = false;
            BernardTuning existing = AssetDatabase.LoadAssetAtPath<BernardTuning>(AssetPath);
            if (existing != null) return existing;

            string[] guids = AssetDatabase.FindAssets("t:" + nameof(BernardTuning));
            if (guids.Length > 0)
            {
                return AssetDatabase.LoadAssetAtPath<BernardTuning>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            if (!AssetDatabase.IsValidFolder(ResourcesFolder)) AssetDatabase.CreateFolder("Assets", "Resources");
            BernardTuning tuning = ScriptableObject.CreateInstance<BernardTuning>();
            AssetDatabase.CreateAsset(tuning, AssetPath);
            AssetDatabase.SaveAssets();
            created = true;
            return tuning;
        }

        /// <summary>씬의 모든 기체(MeleeSystem이 있는 오브젝트 — 플레이어 기체와 더미)에 같은 에셋을 연결한다.</summary>
        public static int AssignToSceneMechs(BernardTuning tuning)
        {
            int count = 0;
            foreach (MeleeSystem melee in Object.FindObjectsByType<MeleeSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                AssignTo(melee.gameObject, tuning);
                count++;
            }
            return count;
        }

        /// <summary>기체 하나에 MechTuning을 붙이고(없으면) 에셋을 연결한다. 호출 쪽 Undo 그룹에 포함된다.</summary>
        public static void AssignTo(GameObject mech, BernardTuning tuning)
        {
            if (mech == null || tuning == null) return;
            MechTuning holder = mech.GetComponent<MechTuning>();
            if (holder == null) holder = Undo.AddComponent<MechTuning>(mech);

            SerializedObject so = new SerializedObject(holder);
            so.FindProperty("tuning").objectReferenceValue = tuning;
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(mech.scene);
        }
    }
}
