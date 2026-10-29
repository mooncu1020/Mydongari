using System;
using System.Collections.Generic;
using UnityEngine;

namespace MyDongari.Visual
{
    /// <summary>
    /// 이름 있는 전신 포즈 모음 (M1). 포즈 = 관절별 회전 오프셋(°, 기체 축 X 오른쪽·Y 위·Z 앞) + 골반 위치 오프셋(m, 기체 축).
    /// 전부 "원래 자세(쉴 때 자세) 기준"이라 MechVisualRig의 다른 오프셋과 같은 공간이다.
    /// 포즈는 오른손잡이(오른 주먹·왼발 앞) 기준으로 적고, 왼쪽 동작은 좌우 반전해서 쓴다.
    /// 값은 에디터 도구(Tools/Mech Pose Editor)로 뼈를 직접 돌려 캡처해 덮어쓴다. 아래 코드 값은 초안이다.
    /// 에셋이 없으면 Resources/MechPoseLibrary, 그것도 없으면 이 초안 값으로 만든 인스턴스를 쓴다.
    /// </summary>
    [CreateAssetMenu(menuName = "Mydongari/Mech Pose Library", fileName = "MechPoseLibrary")]
    public class MechPoseLibrary : ScriptableObject
    {
        public const string ResourcesName = "MechPoseLibrary";

        /// <summary>포즈가 다루는 관절 = 리그 슬롯 이름. 스커트(허벅지 스프링)·발목(평행 보정)은 자동이라 없다.</summary>
        public static readonly string[] JointNames =
        {
            "Pelvis", "Body", "Head",
            "Arm_L_Upper", "Arm_L_Lower", "Fist_L",
            "Arm_R_Upper", "Arm_R_Lower", "Fist_R",
            "Hip.L", "Hip.R", "Leg_L", "Leg_R", "Shin.L", "Shin.R"
        };

        public const int PelvisIndex = 0, BodyIndex = 1, HeadIndex = 2,
            ArmLUpperIndex = 3, ArmLLowerIndex = 4, FistLIndex = 5,
            ArmRUpperIndex = 6, ArmRLowerIndex = 7, FistRIndex = 8,
            HipLIndex = 9, HipRIndex = 10, LegLIndex = 11, LegRIndex = 12, ShinLIndex = 13, ShinRIndex = 14;

        /// <summary>좌우 반전 짝 (가운데 관절은 자기 자신).</summary>
        public static int MirrorOf(int index)
        {
            switch (index)
            {
                case ArmLUpperIndex: return ArmRUpperIndex;
                case ArmLLowerIndex: return ArmRLowerIndex;
                case FistLIndex: return FistRIndex;
                case ArmRUpperIndex: return ArmLUpperIndex;
                case ArmRLowerIndex: return ArmLLowerIndex;
                case FistRIndex: return FistLIndex;
                case HipLIndex: return HipRIndex;
                case HipRIndex: return HipLIndex;
                case LegLIndex: return LegRIndex;
                case LegRIndex: return LegLIndex;
                case ShinLIndex: return ShinRIndex;
                case ShinRIndex: return ShinLIndex;
                default: return index;
            }
        }

        public static int IndexOf(string jointName) => Array.IndexOf(JointNames, jointName);

        /// <summary>좌우 반전: 기체 X축(오른쪽)을 뒤집으면 X축 회전(앞뒤)은 그대로, Y(수평 회전)·Z(좌우 기울기)는 부호가 바뀐다.</summary>
        public static Vector3 MirrorRotation(Vector3 euler) => new Vector3(euler.x, -euler.y, -euler.z);
        public static Vector3 MirrorPosition(Vector3 position) => new Vector3(-position.x, position.y, position.z);

        // ---------------- 이징 커브 이름 (모션 코드가 이 이름으로 찾는다) ----------------
        public const string EaseLinear = "Linear";
        /// <summary>빠르게 출발해 끝에서 멈춤 (뻗기·타격).</summary>
        public const string EaseSnap = "Snap";
        /// <summary>천천히 출발·천천히 도착 (자세 전환).</summary>
        public const string EaseSmooth = "Smooth";
        /// <summary>느리게 출발해 끝에서 가라앉음 (무게 있는 복귀).</summary>
        public const string EaseHeavy = "Heavy";
        /// <summary>천천히 출발해 끝에서 빨라짐 (웅크렸다 터지기 직전).</summary>
        public const string EaseIn = "EaseIn";

        [Serializable]
        public class JointOffset
        {
            [Tooltip("관절 이름 (JointNames 중 하나)")]
            public string joint;
            [Tooltip("회전 오프셋(°, 기체 축). X+ = 앞으로 숙임/정강이는 무릎 굽힘, 위팔 X- = 앞·위로 듦. Y = 수평 회전. Z = 좌우 기울기")]
            public Vector3 rotation;
        }

        [Serializable]
        public class Pose
        {
            public string name;
            [Tooltip("골반 위치 오프셋(m, 기체 축: X 오른쪽, Y 위, Z 앞). 접지 보정이 켜져 있으면 Y는 발이 땅에 붙도록 다시 맞춰진다")]
            public Vector3 pelvisOffset;
            [Tooltip("이 포즈가 정하는 관절만. 없는 관절은 그 순간 밑에 깔린 자세(대기·걸음 등)를 그대로 둔다")]
            public List<JointOffset> joints = new List<JointOffset>();
        }

        [Serializable]
        public class Ease
        {
            public string name;
            [Tooltip("가로 0~1 = 단계 진행, 세로 0~1 = 앞 포즈 → 다음 포즈 섞기")]
            public AnimationCurve curve;
        }

        [Header("포즈 (오른손잡이 기준, 왼쪽은 좌우 반전)")]
        [SerializeField] private List<Pose> poses = new List<Pose>();
        [Header("이징 커브 (포즈 사이 블렌드)")]
        [SerializeField] private List<Ease> eases = new List<Ease>();

        public IReadOnlyList<Pose> Poses => poses;

        /// <summary>런타임 조회용으로 펼친 포즈. 관절 배열은 JointNames 순서.</summary>
        public class Baked
        {
            public readonly Vector3[] Rotation = new Vector3[JointNames.Length];
            public readonly bool[] Has = new bool[JointNames.Length];
            public Vector3 PelvisOffset;
        }

        private readonly Dictionary<string, Baked> _baked = new Dictionary<string, Baked>();
        private readonly Dictionary<string, Baked> _bakedMirror = new Dictionary<string, Baked>();
        private readonly Dictionary<string, AnimationCurve> _easeCache = new Dictionary<string, AnimationCurve>();
        private bool _cacheValid;

        private void OnEnable() => _cacheValid = false;
        // 인스펙터에서 값을 고치면 플레이 중에도 바로 반영되게 펼친 값을 버린다
        private void OnValidate() => _cacheValid = false;

        public void MarkDirty() => _cacheValid = false;

        private void EnsureCache()
        {
            if (_cacheValid) return;
            _baked.Clear();
            _bakedMirror.Clear();
            _easeCache.Clear();
            foreach (Pose pose in poses)
            {
                if (pose == null || string.IsNullOrEmpty(pose.name)) continue;
                var baked = new Baked { PelvisOffset = pose.pelvisOffset };
                var mirror = new Baked { PelvisOffset = MirrorPosition(pose.pelvisOffset) };
                foreach (JointOffset offset in pose.joints)
                {
                    int i = offset != null ? IndexOf(offset.joint) : -1;
                    if (i < 0) continue;
                    baked.Rotation[i] = offset.rotation;
                    baked.Has[i] = true;
                    int m = MirrorOf(i);
                    mirror.Rotation[m] = MirrorRotation(offset.rotation);
                    mirror.Has[m] = true;
                }
                _baked[pose.name] = baked;
                _bakedMirror[pose.name] = mirror;
            }
            foreach (Ease ease in eases)
            {
                if (ease != null && !string.IsNullOrEmpty(ease.name) && ease.curve != null) _easeCache[ease.name] = ease.curve;
            }
            _cacheValid = true;
        }

        /// <summary>
        /// 이름으로 포즈. 이 에셋에 없으면 코드 초안에서 찾는다 (에셋을 만든 뒤 코드에 새로 생긴 포즈 — 예: N2 잽·훅).
        /// 초안에도 없으면 null (그 단계는 밑에 깔린 자세 그대로).
        /// </summary>
        public Baked Get(string poseName, bool mirror)
        {
            if (string.IsNullOrEmpty(poseName)) return null;
            EnsureCache();
            if ((mirror ? _bakedMirror : _baked).TryGetValue(poseName, out Baked baked)) return baked;
            return this != Draft ? Draft.Get(poseName, mirror) : null;
        }

        /// <summary>이징 값(0~1). 이 에셋에 없으면 코드 초안 커브, 그것도 없으면 직선.</summary>
        public float Evaluate(string easeName, float s)
        {
            s = Mathf.Clamp01(s);
            EnsureCache();
            if (easeName != null && _easeCache.TryGetValue(easeName, out AnimationCurve curve)) return curve.Evaluate(s);
            return this != Draft && easeName != null ? Draft.Evaluate(easeName, s) : s;
        }

        private static MechPoseLibrary _draft;

        /// <summary>코드 초안 값만 든 인스턴스 (에셋에 없는 포즈·커브를 찾을 때).</summary>
        public static MechPoseLibrary Draft
        {
            get
            {
                if (_draft == null)
                {
                    _draft = CreateInstance<MechPoseLibrary>();
                    _draft.name = "MechPoseLibrary (코드 초안 참고용)";
                    _draft.hideFlags = HideFlags.DontSave;
                    _draft.ResetToDraft();
                }
                return _draft;
            }
        }

        /// <summary>코드 초안에는 있는데 이 에셋에 없는 포즈·커브를 초안 값으로 추가한다. 추가한 개수.</summary>
        public int AddMissingDraftPoses()
        {
            int added = 0;
            foreach (Pose pose in Draft.poses)
            {
                if (FindPose(pose.name) != null) continue;
                var copy = new Pose { name = pose.name, pelvisOffset = pose.pelvisOffset };
                foreach (JointOffset offset in pose.joints) copy.joints.Add(new JointOffset { joint = offset.joint, rotation = offset.rotation });
                poses.Add(copy);
                added++;
            }
            foreach (Ease ease in Draft.eases)
            {
                if (eases.Exists(e => e != null && e.name == ease.name)) continue;
                eases.Add(new Ease { name = ease.name, curve = new AnimationCurve(ease.curve.keys) });
                added++;
            }
            _cacheValid = false;
            return added;
        }

        public bool HasPose(string poseName) => FindPose(poseName) != null;

        public Pose FindPose(string poseName)
        {
            foreach (Pose pose in poses)
            {
                if (pose != null && pose.name == poseName) return pose;
            }
            return null;
        }

        /// <summary>포즈를 통째로 바꾼다 (없으면 추가). 캡처 도구가 쓴다.</summary>
        public void SetPose(string poseName, Vector3 pelvisOffset, IList<string> jointNames, IList<Vector3> rotations)
        {
            Pose pose = FindPose(poseName);
            if (pose == null)
            {
                pose = new Pose { name = poseName };
                poses.Add(pose);
            }
            pose.pelvisOffset = pelvisOffset;
            pose.joints.Clear();
            for (int i = 0; i < jointNames.Count; i++)
            {
                pose.joints.Add(new JointOffset { joint = jointNames[i], rotation = rotations[i] });
            }
            _cacheValid = false;
        }

        // ---------------- 기본값 ----------------

        private static MechPoseLibrary _fallback;

        /// <summary>기체에 연결이 없을 때: Resources/MechPoseLibrary → 없으면 초안 값 인스턴스.</summary>
        public static MechPoseLibrary Fallback
        {
            get
            {
                if (_fallback == null)
                {
                    _fallback = Resources.Load<MechPoseLibrary>(ResourcesName);
                    if (_fallback == null)
                    {
                        _fallback = CreateInstance<MechPoseLibrary>();
                        _fallback.name = "MechPoseLibrary (코드 초안)";
                        _fallback.hideFlags = HideFlags.DontSave;
                        _fallback.ResetToDraft();
                    }
                }
                return _fallback;
            }
        }

        private void Reset() => ResetToDraft();

        /// <summary>
        /// 모든 포즈·커브를 초안 값으로. 새 에셋을 만들 때 자동으로 불린다(Reset).
        /// 각도 규칙(기체 축 기준, 쉴 때 자세에서 얼마나): 몸통 X+ = 앞으로 숙임, Y- = 오른 어깨가 앞으로 오게 비틂, Z- = 오른쪽으로 기울임.
        /// 위팔 X- = 앞·위로 듦(-90 = 앞으로 수평), X+ = 뒤로. 왼 위팔 Z+ / 오른 위팔 Z- = 몸 안쪽으로 붙임.
        /// 아래팔 X- = 팔꿈치를 더 굽힘, X+ = 폄. 다리 X- = 앞으로, 정강이 X+ = 무릎 굽힘. 머리 X+ = 숙임.
        /// </summary>
        public void ResetToDraft()
        {
            poses = new List<Pose>
            {
                // M2 기본 자세: 주먹 가슴 높이, 팔꿈치 몸통에 붙임, 상체 약간 숙임, 무릎 굽힘
                P(PoseNames.LowGuard, Vector3.zero,
                    J("Body", 8, 0, 0), J("Head", -4, 0, 0),
                    J("Arm_L_Upper", -35, 0, 14), J("Arm_L_Lower", -62, 0, 0), J("Fist_L", -10, 0, 0),
                    J("Arm_R_Upper", -30, 0, -14), J("Arm_R_Lower", -66, 0, 0), J("Fist_R", -10, 0, 0),
                    J("Leg_L", -8, 0, 0), J("Leg_R", -4, 0, 0), J("Shin.L", 18, 0, 0), J("Shin.R", 18, 0, 0)),
                // M2 전력질주 돌격 자세: 주먹 더 끌어당김, 상체 더 숙임 (다리는 걸음이 맡는다)
                P(PoseNames.SprintGuard, Vector3.zero,
                    J("Body", 22, 0, 0), J("Head", -12, 0, 0),
                    J("Arm_L_Upper", -18, 0, 20), J("Arm_L_Lower", -84, 0, 0), J("Fist_L", -12, 0, 0),
                    J("Arm_R_Upper", -18, 0, -20), J("Arm_R_Lower", -84, 0, 0), J("Fist_R", -12, 0, 0),
                    J("Shin.L", 8, 0, 0), J("Shin.R", 8, 0, 0)),

                // M3 펀치 (오른 주먹, 오른발 = 뒷발)
                P(PoseNames.PunchWindup, Vector3.zero,
                    J("Pelvis", 0, 6, 0), J("Body", 6, 10, 0), J("Head", 0, -8, 0),
                    J("Arm_R_Upper", -22, 0, -8), J("Arm_R_Lower", -82, 0, 0), J("Fist_R", -10, 0, 0),
                    J("Arm_L_Upper", -42, 0, 12), J("Arm_L_Lower", -70, 0, 0),
                    J("Leg_L", -10, 0, 0), J("Leg_R", 4, 0, 0), J("Shin.L", 20, 0, 0), J("Shin.R", 24, 0, 0)),
                P(PoseNames.PunchStrike, Vector3.zero,
                    J("Pelvis", 0, -14, 0), J("Body", 14, -22, 0), J("Head", 2, 18, 0),
                    J("Arm_R_Upper", -86, 0, -6), J("Arm_R_Lower", 64, 0, 0), J("Fist_R", 4, 0, 0),
                    J("Arm_L_Upper", -50, 0, 16), J("Arm_L_Lower", -86, 0, 0),
                    J("Leg_L", -12, 0, 0), J("Leg_R", 8, 0, 0), J("Shin.L", 24, 0, 0), J("Shin.R", 6, 0, 0)),
                P(PoseNames.PunchFollow, new Vector3(0f, 0f, 0.04f),
                    J("Pelvis", 0, -16, 0), J("Body", 20, -26, 0), J("Head", 4, 20, 0),
                    J("Arm_R_Upper", -88, 0, -6), J("Arm_R_Lower", 66, 0, 0), J("Fist_R", 4, 0, 0),
                    J("Arm_L_Upper", -50, 0, 16), J("Arm_L_Lower", -88, 0, 0),
                    J("Leg_L", -14, 0, 0), J("Leg_R", 10, 0, 0), J("Shin.L", 28, 0, 0), J("Shin.R", 4, 0, 0)),
                // N2 원투쓰리 콤보: 원 = 앞손(왼손) 잽, 투 = 위 Punch*(뒷손 스트레이트, 허리 크게), 쓰리 = 앞손 훅
                P(PoseNames.JabWindup, Vector3.zero,
                    J("Body", 4, -6, 0),
                    J("Arm_L_Upper", -30, 0, 10), J("Arm_L_Lower", -76, 0, 0),
                    J("Arm_R_Upper", -38, 0, -16), J("Arm_R_Lower", -70, 0, 0),
                    J("Shin.L", 22, 0, 0), J("Shin.R", 20, 0, 0)),
                P(PoseNames.JabStrike, new Vector3(0f, 0f, 0.02f),
                    J("Pelvis", 0, 4, 0), J("Body", 8, 12, 0), J("Head", 0, -10, 0),
                    J("Arm_L_Upper", -86, 0, 4), J("Arm_L_Lower", 62, 0, 0), J("Fist_L", 4, 0, 0),
                    J("Arm_R_Upper", -42, 0, -18), J("Arm_R_Lower", -80, 0, 0),
                    J("Leg_L", -10, 0, 0), J("Leg_R", 4, 0, 0), J("Shin.L", 22, 0, 0), J("Shin.R", 18, 0, 0)),
                P(PoseNames.JabFollow, new Vector3(0f, 0f, 0.03f),
                    J("Pelvis", 0, 5, 0), J("Body", 10, 14, 0), J("Head", 0, -12, 0),
                    J("Arm_L_Upper", -87, 0, 4), J("Arm_L_Lower", 64, 0, 0), J("Fist_L", 4, 0, 0),
                    J("Arm_R_Upper", -42, 0, -18), J("Arm_R_Lower", -80, 0, 0),
                    J("Leg_L", -11, 0, 0), J("Leg_R", 5, 0, 0), J("Shin.L", 23, 0, 0), J("Shin.R", 17, 0, 0)),
                P(PoseNames.HookWindup, new Vector3(0f, -0.03f, 0f),
                    J("Pelvis", 0, -8, 0), J("Body", 10, -18, 0), J("Head", 0, 12, 0),
                    J("Arm_L_Upper", -60, 0, -35), J("Arm_L_Lower", -96, 0, 0),
                    J("Arm_R_Upper", -44, 0, -18), J("Arm_R_Lower", -84, 0, 0),
                    J("Leg_L", -12, 0, 0), J("Shin.L", 32, 0, 0), J("Shin.R", 22, 0, 0)),
                P(PoseNames.HookStrike, Vector3.zero,
                    J("Pelvis", 0, 18, 0), J("Body", 12, 34, 0), J("Head", 0, -20, 0),
                    J("Arm_L_Upper", -80, 0, -55), J("Arm_L_Lower", -90, 0, 0), J("Fist_L", 0, 0, 0),
                    J("Arm_R_Upper", -46, 0, -18), J("Arm_R_Lower", -86, 0, 0),
                    J("Leg_L", -8, 0, 0), J("Leg_R", 6, 0, 0), J("Shin.L", 26, 0, 0), J("Shin.R", 14, 0, 0)),
                P(PoseNames.HookFollow, new Vector3(0f, 0f, 0.03f),
                    J("Pelvis", 0, 22, 0), J("Body", 16, 40, 0), J("Head", 2, -22, 0),
                    J("Arm_L_Upper", -78, 0, -50), J("Arm_L_Lower", -92, 0, 0),
                    J("Arm_R_Upper", -46, 0, -18), J("Arm_R_Lower", -86, 0, 0),
                    J("Leg_L", -9, 0, 0), J("Leg_R", 7, 0, 0), J("Shin.L", 28, 0, 0), J("Shin.R", 12, 0, 0)),
                // 달리는 중 펀치: 앞발(왼발) 디딤 — 하체에만, 걸음 세기만큼
                P(PoseNames.PunchStepIn, Vector3.zero,
                    J("Leg_L", -26, 0, 0), J("Shin.L", 30, 0, 0), J("Leg_R", 14, 0, 0), J("Shin.R", 8, 0, 0)),

                // M4 스러스터 런지
                P(PoseNames.LungeCrouch, new Vector3(0f, -0.04f, 0f),
                    J("Body", 26, 0, 0), J("Head", -16, 0, 0),
                    J("Arm_R_Upper", 34, 0, -6), J("Arm_R_Lower", -62, 0, 0),
                    J("Arm_L_Upper", -40, 0, 12), J("Arm_L_Lower", -80, 0, 0),
                    J("Leg_L", -26, 0, 0), J("Leg_R", -14, 0, 0), J("Shin.L", 42, 0, 0), J("Shin.R", 42, 0, 0)),
                P(PoseNames.LungeFlight, new Vector3(0f, 0.22f, 0f),
                    J("Body", 30, 8, 0), J("Head", -20, -6, 0),
                    J("Arm_R_Upper", 40, 0, -6), J("Arm_R_Lower", -70, 0, 0),
                    J("Arm_L_Upper", -46, 0, 12), J("Arm_L_Lower", -76, 0, 0),
                    J("Leg_L", -36, 0, 0), J("Leg_R", 26, 0, 0), J("Shin.L", 52, 0, 0), J("Shin.R", 36, 0, 0)),
                P(PoseNames.LungePlant, new Vector3(0f, -0.05f, 0f),
                    J("Pelvis", 0, 8, 0), J("Body", 28, 12, 0), J("Head", -14, -10, 0),
                    J("Arm_R_Upper", -16, 0, -6), J("Arm_R_Lower", -92, 0, 0),
                    J("Arm_L_Upper", -46, 0, 12), J("Arm_L_Lower", -76, 0, 0),
                    J("Leg_L", -40, 0, 0), J("Leg_R", 30, 0, 0), J("Shin.L", 70, 0, 0), J("Shin.R", 20, 0, 0)),
                P(PoseNames.LungeStrike, new Vector3(0f, -0.04f, 0.05f),
                    J("Pelvis", 0, -16, 0), J("Body", 24, -26, 0), J("Head", -10, 20, 0),
                    J("Arm_R_Upper", -88, 0, -4), J("Arm_R_Lower", 66, 0, 0), J("Fist_R", 4, 0, 0),
                    J("Arm_L_Upper", -50, 0, 16), J("Arm_L_Lower", -90, 0, 0),
                    J("Leg_L", -40, 0, 0), J("Leg_R", 36, 0, 0), J("Shin.L", 64, 0, 0), J("Shin.R", 4, 0, 0)),

                // M5 가드 계열
                P(PoseNames.Guard, Vector3.zero,
                    J("Body", 16, 0, 0), J("Head", 14, 0, 0),
                    J("Arm_L_Upper", -56, 0, 24), J("Arm_L_Lower", -106, 0, 0), J("Fist_L", -10, 0, 0),
                    J("Arm_R_Upper", -56, 0, -24), J("Arm_R_Lower", -106, 0, 0), J("Fist_R", -10, 0, 0),
                    J("Leg_L", -10, 0, 0), J("Leg_R", -6, 0, 0), J("Shin.L", 30, 0, 0), J("Shin.R", 30, 0, 0)),
                P(PoseNames.GuardBlocked, Vector3.zero,
                    J("Body", 6, 0, 0), J("Head", 10, 0, 0),
                    J("Arm_L_Upper", -40, 0, 22), J("Arm_L_Lower", -122, 0, 0),
                    J("Arm_R_Upper", -40, 0, -22), J("Arm_R_Lower", -122, 0, 0),
                    J("Leg_L", -4, 0, 0), J("Leg_R", 4, 0, 0), J("Shin.L", 34, 0, 0), J("Shin.R", 30, 0, 0)),
                // 오른 팔뚝으로 바깥으로 쳐냄
                P(PoseNames.ParryDeflect, Vector3.zero,
                    J("Body", 10, 14, 0), J("Head", 8, -10, 0),
                    J("Arm_R_Upper", -72, 0, 32), J("Arm_R_Lower", -68, 0, 0), J("Fist_R", -6, 0, 0),
                    J("Arm_L_Upper", -54, 0, 24), J("Arm_L_Lower", -104, 0, 0),
                    J("Shin.L", 26, 0, 0), J("Shin.R", 26, 0, 0)),
                P(PoseNames.GuardBreak, new Vector3(0f, 0f, -0.05f),
                    J("Body", -22, 0, 0), J("Head", -20, 0, 0),
                    J("Arm_L_Upper", -22, 0, -55), J("Arm_L_Lower", -20, 0, 0),
                    J("Arm_R_Upper", -22, 0, 55), J("Arm_R_Lower", -20, 0, 0),
                    J("Leg_L", 8, 0, 0), J("Leg_R", -10, 0, 0), J("Shin.L", 26, 0, 0), J("Shin.R", 24, 0, 0)),

                // M6 앵커 클로 (왼팔) → 파일 드라이버 (오른 어퍼컷)
                P(PoseNames.ClawWindup, Vector3.zero,
                    J("Body", 4, -16, 0),
                    J("Arm_L_Upper", 36, 0, -10), J("Arm_L_Lower", -70, 0, 0),
                    J("Arm_R_Upper", -40, 0, -12), J("Arm_R_Lower", -80, 0, 0),
                    J("Shin.L", 20, 0, 0), J("Shin.R", 22, 0, 0)),
                P(PoseNames.ClawThrow, Vector3.zero,
                    J("Body", 10, 18, 0), J("Head", 0, -12, 0),
                    J("Arm_L_Upper", -86, 0, 0), J("Arm_L_Lower", 64, 0, 0),
                    J("Arm_R_Upper", -40, 0, -12), J("Arm_R_Lower", -84, 0, 0),
                    J("Leg_L", -16, 0, 0), J("Shin.L", 22, 0, 0), J("Shin.R", 16, 0, 0)),
                P(PoseNames.ClawYank, new Vector3(0f, 0f, -0.04f),
                    J("Body", -12, -20, 0), J("Head", -6, 10, 0),
                    J("Arm_L_Upper", -30, 0, -6), J("Arm_L_Lower", -100, 0, 0),
                    J("Arm_R_Upper", -38, 0, -12), J("Arm_R_Lower", -84, 0, 0),
                    J("Leg_L", -16, 0, 0), J("Leg_R", 22, 0, 0), J("Shin.L", 26, 0, 0), J("Shin.R", 6, 0, 0)),
                P(PoseNames.ClawReel, new Vector3(0f, -0.05f, 0f),
                    J("Pelvis", 0, 18, 0), J("Body", 20, 20, 0), J("Head", -12, -16, 0),
                    J("Arm_R_Upper", 30, 0, -6), J("Arm_R_Lower", -96, 0, 0), J("Fist_R", -16, 0, 0),
                    J("Arm_L_Upper", -46, 0, 10), J("Arm_L_Lower", -80, 0, 0),
                    J("Leg_L", -30, 0, 0), J("Leg_R", -10, 0, 0), J("Shin.L", 50, 0, 0), J("Shin.R", 56, 0, 0)),
                P(PoseNames.UppercutRise, new Vector3(0f, 0.03f, 0f),
                    J("Pelvis", 0, 6, 0), J("Body", 6, 6, 0), J("Head", -10, -6, 0),
                    J("Arm_R_Upper", -40, 0, -6), J("Arm_R_Lower", -112, 0, 0), J("Fist_R", -20, 0, 0),
                    J("Arm_L_Upper", -42, 0, 14), J("Arm_L_Lower", -88, 0, 0),
                    J("Leg_L", -14, 0, 0), J("Leg_R", -4, 0, 0), J("Shin.L", 26, 0, 0), J("Shin.R", 20, 0, 0)),
                P(PoseNames.UppercutStrike, new Vector3(0f, 0.08f, 0f),
                    J("Pelvis", 0, -12, 0), J("Body", -14, -18, 0), J("Head", -26, 10, 0),
                    J("Arm_R_Upper", -150, 0, -8), J("Arm_R_Lower", -60, 0, 0), J("Fist_R", -10, 0, 0),
                    J("Arm_L_Upper", -40, 0, 16), J("Arm_L_Lower", -92, 0, 0),
                    J("Leg_L", -4, 0, 0), J("Leg_R", 4, 0, 0), J("Shin.L", 4, 0, 0), J("Shin.R", 4, 0, 0)),

                // M7 대시 공격 (달리던 자세에서 스트레이트)·태클 (어깨 앞세운 돌격)·슬램 (두 주먹 내려찍기)
                P(PoseNames.DashWindup, Vector3.zero,
                    J("Body", 28, 10, 0), J("Head", -14, -8, 0),
                    J("Arm_R_Upper", 24, 0, -6), J("Arm_R_Lower", -90, 0, 0),
                    J("Arm_L_Upper", -42, 0, 14), J("Arm_L_Lower", -82, 0, 0)),
                P(PoseNames.DashStrike, new Vector3(0f, 0f, 0.05f),
                    J("Pelvis", 0, -12, 0), J("Body", 24, -24, 0), J("Head", -12, 20, 0),
                    J("Arm_R_Upper", -86, 0, -4), J("Arm_R_Lower", 64, 0, 0), J("Fist_R", 4, 0, 0),
                    J("Arm_L_Upper", -50, 0, 16), J("Arm_L_Lower", -90, 0, 0),
                    J("Leg_L", -30, 0, 0), J("Leg_R", 22, 0, 0), J("Shin.L", 36, 0, 0), J("Shin.R", 8, 0, 0)),
                P(PoseNames.TackleWindup, new Vector3(0f, -0.03f, 0f),
                    J("Body", 20, 16, 0), J("Head", -10, -10, 0),
                    J("Arm_R_Upper", -10, 0, -12), J("Arm_R_Lower", -110, 0, 0),
                    J("Arm_L_Upper", -30, 0, 12), J("Arm_L_Lower", -90, 0, 0),
                    J("Shin.L", 30, 0, 0), J("Shin.R", 30, 0, 0)),
                P(PoseNames.TackleShoulder, new Vector3(0f, -0.04f, 0.06f),
                    J("Pelvis", 0, -10, 0), J("Body", 32, -38, 0), J("Head", 10, 30, 0),
                    J("Arm_R_Upper", -16, 0, -26), J("Arm_R_Lower", -116, 0, 0),
                    J("Arm_L_Upper", 30, 0, -6), J("Arm_L_Lower", -60, 0, 0),
                    J("Leg_L", -30, 0, 0), J("Leg_R", 26, 0, 0), J("Shin.L", 40, 0, 0), J("Shin.R", 10, 0, 0)),
                P(PoseNames.SlamRaise, new Vector3(0f, 0.04f, 0f),
                    J("Body", -16, 0, 0), J("Head", -20, 0, 0),
                    J("Arm_L_Upper", -165, 0, -10), J("Arm_L_Lower", -40, 0, 0),
                    J("Arm_R_Upper", -165, 0, 10), J("Arm_R_Lower", -40, 0, 0),
                    J("Shin.L", 10, 0, 0), J("Shin.R", 10, 0, 0)),
                P(PoseNames.SlamDown, new Vector3(0f, -0.06f, 0.04f),
                    J("Body", 38, 0, 0), J("Head", 20, 0, 0),
                    J("Arm_L_Upper", -50, 0, 8), J("Arm_L_Lower", -20, 0, 0),
                    J("Arm_R_Upper", -50, 0, -8), J("Arm_R_Lower", -20, 0, 0),
                    J("Leg_L", -20, 0, 0), J("Leg_R", -20, 0, 0), J("Shin.L", 46, 0, 0), J("Shin.R", 46, 0, 0)),
            };

            eases = new List<Ease>
            {
                new Ease { name = EaseLinear, curve = AnimationCurve.Linear(0f, 0f, 1f, 1f) },
                new Ease { name = EaseSmooth, curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f) },
                new Ease { name = EaseSnap, curve = new AnimationCurve(new Keyframe(0f, 0f, 3f, 3f), new Keyframe(1f, 1f, 0f, 0f)) },
                new Ease { name = EaseHeavy, curve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(0.6f, 0.88f, 1.1f, 1.1f), new Keyframe(1f, 1f, 0f, 0f)) },
                new Ease { name = EaseIn, curve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2f, 2f)) },
            };
            _cacheValid = false;
        }

        private static Pose P(string name, Vector3 pelvisOffset, params JointOffset[] joints)
        {
            return new Pose { name = name, pelvisOffset = pelvisOffset, joints = new List<JointOffset>(joints) };
        }

        private static JointOffset J(string joint, float x, float y, float z)
        {
            return new JointOffset { joint = joint, rotation = new Vector3(x, y, z) };
        }
    }

    /// <summary>모션 코드가 쓰는 포즈 이름. 라이브러리에서 이름을 바꾸면 여기도 같이.</summary>
    public static class PoseNames
    {
        public const string LowGuard = "LowGuard";
        public const string SprintGuard = "SprintGuard";
        public const string PunchWindup = "PunchWindup";
        public const string PunchStrike = "PunchStrike";
        public const string PunchFollow = "PunchFollow";
        public const string PunchStepIn = "PunchStepIn";
        public const string JabWindup = "JabWindup";
        public const string JabStrike = "JabStrike";
        public const string JabFollow = "JabFollow";
        public const string HookWindup = "HookWindup";
        public const string HookStrike = "HookStrike";
        public const string HookFollow = "HookFollow";
        public const string LungeCrouch = "LungeCrouch";
        public const string LungeFlight = "LungeFlight";
        public const string LungePlant = "LungePlant";
        public const string LungeStrike = "LungeStrike";
        public const string Guard = "Guard";
        public const string GuardBlocked = "GuardBlocked";
        public const string ParryDeflect = "ParryDeflect";
        public const string GuardBreak = "GuardBreak";
        public const string ClawWindup = "ClawWindup";
        public const string ClawThrow = "ClawThrow";
        public const string ClawYank = "ClawYank";
        public const string ClawReel = "ClawReel";
        public const string UppercutRise = "UppercutRise";
        public const string UppercutStrike = "UppercutStrike";
        public const string DashWindup = "DashWindup";
        public const string DashStrike = "DashStrike";
        public const string TackleWindup = "TackleWindup";
        public const string TackleShoulder = "TackleShoulder";
        public const string SlamRaise = "SlamRaise";
        public const string SlamDown = "SlamDown";
    }
}
