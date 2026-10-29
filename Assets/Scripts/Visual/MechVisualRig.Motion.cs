using System;
using System.Collections.Generic;
using UnityEngine;
using MyDongari.Mech;

namespace MyDongari.Visual
{
    /// <summary>
    /// 포즈 라이브러리 기반 전신 모션의 공통 부분 (M1).
    /// 모션 = 시간 순서의 키(포즈 이름 + 도착 시각 + 이징). 키 사이를 이징 커브로 섞고, 관절마다 "앞당김(lead)"을 줘서
    /// 하체 → 골반 → 허리 → 어깨처럼 앞 관절이 먼저 도착하게 만든다. 포즈 이름이 null인 키 = 그 순간 밑에 깔린 자세(대기·걸음).
    /// 결과는 기존과 같은 "원래 자세 기준 오프셋"으로 TargetRotation에 섞어 넣으므로, 상하체 방향(Yaw, G2)·접지 보정(H3)·
    /// 발목 평행 보정은 그대로 그 위에서 돈다. 모션이 맡은 관절은 스프링을 건너뛰어(springBypass) 시간이 정확히 맞는다.
    /// 하체(Pelvis) 뼈가 없는 임시 상자는 이 경로를 쓰지 않는다 (예전 동작).
    /// </summary>
    public partial class MechVisualRig
    {
        [Header("포즈 라이브러리 (M1, 하체 뼈가 있는 모델만)")]
        [Tooltip("비워 두면 Resources/MechPoseLibrary, 없으면 코드 초안 값. 만들기·캡처는 Tools/Mech Pose Editor")]
        [SerializeField] private MechPoseLibrary poseLibrary;

        public MechPoseLibrary PoseLibrary => poseLibrary != null ? poseLibrary : MechPoseLibrary.Fallback;
        /// <summary>인스펙터에 직접 연결된 라이브러리 에셋 (없으면 null).</summary>
        public MechPoseLibrary AssignedPoseLibrary => poseLibrary;

        private BernardTuning.MotionValues M => _tuning.Get(this).Motion;

        /// <summary>전신 모션을 쓰는지: 하체 뼈가 있는 모델이고 튜닝에서 켜져 있을 때. 아니면 예전 포즈 코드.</summary>
        private bool UseMotions => HasLowerBody && M.enabled;

        // JointNames 순서의 관절 (없는 뼈는 null)
        private Joint[] _poseJoints;
        private readonly float[] _lead = new float[MechPoseLibrary.JointNames.Length];
        private readonly float[] _mask = new float[MechPoseLibrary.JointNames.Length];

        private struct MotionKey
        {
            public float Time;
            public string Pose;
            public string Ease;
            public MechPoseLibrary.Baked Baked;
        }

        private readonly MotionKey[] _keys = new MotionKey[12];
        private int _keyCount;

        private void BindPoseJoints()
        {
            _poseJoints = new[]
            {
                _pelvis, _body, _head,
                _armLUpper, _armLLower, _fistL,
                _armRUpper, _armRLower, _fistR,
                _hipL, _hipR, _legL, _legR, _shinL, _shinR
            };
        }

        // ---------------- 키 만들기 ----------------

        /// <summary>새 모션 키 목록 시작. 앞당김 0, 마스크 1(모든 관절)로 초기화.</summary>
        private void BeginMotion()
        {
            _keyCount = 0;
            for (int i = 0; i < _lead.Length; i++)
            {
                _lead[i] = 0f;
                _mask[i] = 1f;
            }
        }

        /// <summary>키 추가: time초에 pose(null = 밑에 깔린 자세)에 도착, 앞 키에서 오는 동안 ease 커브.</summary>
        private void Key(float time, string pose, string ease = MechPoseLibrary.EaseSmooth)
        {
            if (_keyCount >= _keys.Length) return;
            // 시간은 앞 키보다 뒤여야 한다 (튜닝 값이 0이어도 나눗셈이 터지지 않게)
            if (_keyCount > 0) time = Mathf.Max(time, _keys[_keyCount - 1].Time);
            _keys[_keyCount++] = new MotionKey { Time = time, Pose = pose, Ease = ease };
        }

        /// <summary>관절 앞당김(초): 그 관절은 키 시각보다 이만큼 먼저 도착한다. mirror면 좌우 짝 관절에 넣는다.</summary>
        private void Lead(int jointIndex, float seconds, bool mirror)
        {
            _lead[mirror ? MechPoseLibrary.MirrorOf(jointIndex) : jointIndex] = seconds;
        }

        /// <summary>하체(골반·엉덩이·다리·정강이)를 이 비율만큼만 모션이 맡는다. 나머지는 밑에 깔린 걸음.</summary>
        private void MaskLowerBody(float weight)
        {
            _mask[MechPoseLibrary.PelvisIndex] = weight;
            _mask[MechPoseLibrary.HipLIndex] = weight;
            _mask[MechPoseLibrary.HipRIndex] = weight;
            _mask[MechPoseLibrary.LegLIndex] = weight;
            _mask[MechPoseLibrary.LegRIndex] = weight;
            _mask[MechPoseLibrary.ShinLIndex] = weight;
            _mask[MechPoseLibrary.ShinRIndex] = weight;
        }

        /// <summary>키 끝 시각 (모션 길이).</summary>
        private float MotionLength => _keyCount > 0 ? _keys[_keyCount - 1].Time : 0f;

        // ---------------- 평가 ----------------

        /// <summary>
        /// 지금까지 만든 키를 t초(모션 시작 기준)에서 평가해 weight만큼 섞는다. mirror면 좌우 반전 포즈.
        /// 관절마다 t + 앞당김으로 구간을 찾아 앞 포즈 → 다음 포즈를 다음 키의 이징으로 섞고,
        /// 그 결과를 지금까지 쌓인 목표(대기·걸음·가드 등)와 weight × 마스크로 섞는다.
        /// </summary>
        private void PlayMotion(float t, float weight, bool mirror)
        {
            if (_keyCount == 0 || weight <= 0f || _poseJoints == null) return;
            MechPoseLibrary library = PoseLibrary;
            for (int i = 0; i < _keyCount; i++) _keys[i].Baked = library.Get(_keys[i].Pose, mirror);

            for (int j = 0; j < _poseJoints.Length; j++)
            {
                Joint joint = _poseJoints[j];
                if (joint == null || joint.Target == null) continue;
                float w = Mathf.Clamp01(weight * _mask[j]);
                if (w <= 0f) continue;

                Vector3 baseRotation = joint.TargetRotation;
                Vector3 value = SampleRotation(library, t + _lead[j], j, baseRotation);
                joint.TargetRotation = Vector3.Lerp(baseRotation, value, w);
                joint.MotionWeight = Mathf.Max(joint.MotionWeight, w);

                // 골반 위치 오프셋은 골반 관절과 같은 앞당김·마스크로
                if (j == MechPoseLibrary.PelvisIndex)
                {
                    Vector3 basePosition = joint.TargetPosition;
                    joint.TargetPosition = Vector3.Lerp(basePosition, SamplePelvis(library, t + _lead[j], basePosition), w);
                }
            }
        }

        private Vector3 SampleRotation(MechPoseLibrary library, float t, int j, Vector3 baseRotation)
        {
            if (!FindSegment(t, out int a, out int b, out float s)) return RotationAt(a, j, baseRotation);
            float e = library.Evaluate(_keys[b].Ease, s);
            return Vector3.Lerp(RotationAt(a, j, baseRotation), RotationAt(b, j, baseRotation), e);
        }

        private Vector3 SamplePelvis(MechPoseLibrary library, float t, Vector3 basePosition)
        {
            if (!FindSegment(t, out int a, out int b, out float s)) return PelvisAt(a, basePosition);
            float e = library.Evaluate(_keys[b].Ease, s);
            return Vector3.Lerp(PelvisAt(a, basePosition), PelvisAt(b, basePosition), e);
        }

        /// <summary>t가 속한 구간 [a, b]와 진행(0~1). 처음 키 앞·마지막 키 뒤면 false, a = 그 끝 키.</summary>
        private bool FindSegment(float t, out int a, out int b, out float s)
        {
            s = 0f;
            b = 0;
            if (t <= _keys[0].Time)
            {
                a = 0;
                return false;
            }
            int last = _keyCount - 1;
            if (t >= _keys[last].Time)
            {
                a = last;
                return false;
            }
            a = 0;
            while (a + 1 < last && t >= _keys[a + 1].Time) a++;
            b = a + 1;
            float span = _keys[b].Time - _keys[a].Time;
            s = span > 0f ? (t - _keys[a].Time) / span : 1f;
            return true;
        }

        private Vector3 RotationAt(int key, int j, Vector3 baseRotation)
        {
            MechPoseLibrary.Baked baked = _keys[key].Baked;
            return baked != null && baked.Has[j] ? baked.Rotation[j] : baseRotation;
        }

        private Vector3 PelvisAt(int key, Vector3 basePosition)
        {
            MechPoseLibrary.Baked baked = _keys[key].Baked;
            return baked != null ? baked.PelvisOffset : basePosition;
        }

        /// <summary>포즈 하나를 밑에 깔린 자세에 그대로 더한다 (기본 자세처럼 걸음·숨쉬기가 그 위에 얹히는 층).</summary>
        private void AddPose(string poseName, float weight, bool mirror = false)
        {
            if (weight <= 0f || _poseJoints == null) return;
            MechPoseLibrary.Baked baked = PoseLibrary.Get(poseName, mirror);
            if (baked == null) return;
            for (int j = 0; j < _poseJoints.Length; j++)
            {
                if (baked.Has[j]) AddRotation(_poseJoints[j], baked.Rotation[j] * weight);
            }
            AddPosition(_pelvis, baked.PelvisOffset * weight);
        }

        /// <summary>모션 시작 시각 기준 경과가 [0, length) 안인지.</summary>
        private bool MotionActive(float start, float length) => InWindow(start, length);

        /// <summary>모션이 맡은 관절은 스프링을 건너뛰어 목표로 바로 (단계 시간이 정확히 맞게). 스프링 뒤에 부른다.</summary>
        private void ApplySpringBypass(Joint joint)
        {
            if (joint.MotionWeight <= 0f) return;
            float b = Mathf.Clamp01(joint.MotionWeight * M.springBypass);
            joint.Rotation = Vector3.Lerp(joint.Rotation, joint.TargetRotation, b);
            joint.RotationVelocity *= 1f - b;
            joint.Position = Vector3.Lerp(joint.Position, joint.TargetPosition, b);
            joint.PositionVelocity *= 1f - b;
        }

        // ---------------- 에디터 도구 (미리보기·멈춤·캡처) ----------------

        /// <summary>켜면 리그가 뼈를 쓰지 않는다 — 플레이 중 씬에서 뼈를 직접 돌려 캡처할 때.</summary>
        [NonSerialized] public bool PoseEditFreeze;
        private string _previewPose;
        private bool _previewMirror;

        public string PreviewPoseName => _previewPose;

        /// <summary>플레이 중 포즈 하나만 그대로 보여 준다 (다른 동작·스프링 없음, 상하체 방향·접지 보정은 그대로).</summary>
        public void SetPreviewPose(string poseName, bool mirror)
        {
            _previewPose = poseName;
            _previewMirror = mirror;
        }

        public void ClearPreviewPose() => _previewPose = null;

        private void PreviewLateUpdate(BernardTuning.VisualValues v, float dt)
        {
            MechPoseLibrary.Baked baked = PoseLibrary.Get(_previewPose, _previewMirror);
            foreach (Joint joint in _joints)
            {
                if (joint.Target == null) continue;
                joint.Rotation = Vector3.zero;
                joint.Position = Vector3.zero;
                joint.RotationVelocity = Vector3.zero;
                joint.PositionVelocity = Vector3.zero;
            }
            if (baked != null && _poseJoints != null)
            {
                for (int j = 0; j < _poseJoints.Length; j++)
                {
                    Joint joint = _poseJoints[j];
                    if (joint != null && joint.Target != null && baked.Has[j]) joint.Rotation = baked.Rotation[j];
                }
                if (_pelvis != null) _pelvis.Position = baked.PelvisOffset;
            }
            foreach (Joint joint in _joints)
            {
                if (joint.Target == null) continue;
                if (joint == _ankleL) LevelAnkle(joint, v, _pelvis, _hipL, _legL, _shinL);
                else if (joint == _ankleR) LevelAnkle(joint, v, _pelvis, _hipR, _legR, _shinR);
                Apply(joint);
            }
            UpdateGrounding(v, dt);
        }

        /// <summary>
        /// 플레이 중: 지금 뼈 자세를 원래 자세 기준 오프셋으로 재서 포즈로 저장한다 (상하체 방향 Yaw는 빼고).
        /// skipUnchanged면 원래 자세와 0.5° 안쪽인 관절은 빼서, 그 관절은 밑에 깔린 자세가 그대로 나오게.
        /// </summary>
        public bool CapturePose(MechPoseLibrary library, string poseName, bool skipUnchanged, out string message)
        {
            message = null;
            if (library == null || string.IsNullOrEmpty(poseName))
            {
                message = "라이브러리와 포즈 이름이 필요합니다";
                return false;
            }
            if (_poseJoints == null)
            {
                message = "리그가 아직 초기화되지 않았습니다 (플레이 중에만)";
                return false;
            }

            var names = new List<string>();
            var rotations = new List<Vector3>();
            for (int j = 0; j < _poseJoints.Length; j++)
            {
                Joint joint = _poseJoints[j];
                if (joint == null || joint.Target == null) continue;
                Quaternion localOffset = Quaternion.Inverse(joint.RestRotation) * joint.Target.localRotation;
                Vector3 euler = LocalOffsetToMechEuler(localOffset, joint.BoneFromMech, joint.Slot.rotationCorrection);
                euler.y = Mathf.DeltaAngle(0f, euler.y - joint.Yaw);
                if (skipUnchanged && euler.sqrMagnitude < 0.25f) continue;
                names.Add(MechPoseLibrary.JointNames[j]);
                rotations.Add(euler);
            }

            Vector3 pelvisOffset = Vector3.zero;
            if (_pelvis != null && _pelvis.Target != null)
            {
                Vector3 localDelta = _pelvis.Target.localPosition - _pelvis.RestPosition;
                Transform parent = _pelvis.Target.parent;
                Vector3 world = parent != null ? parent.TransformVector(localDelta) : localDelta;
                pelvisOffset = transform.InverseTransformDirection(world);
            }

            library.SetPose(poseName, pelvisOffset, names, rotations);
            message = $"{poseName}: 관절 {names.Count}개 저장";
            return true;
        }

        /// <summary>뼈 로컬 오프셋 회전 → 기체 축 오일러(°, -180~180). Apply의 역변환.</summary>
        public static Vector3 LocalOffsetToMechEuler(Quaternion localOffset, Quaternion boneFromMech, Vector3 correctionEuler)
        {
            Quaternion mechOffset = Quaternion.Inverse(boneFromMech) * localOffset * boneFromMech;
            Quaternion correction = Quaternion.Euler(correctionEuler);
            Vector3 e = (Quaternion.Inverse(correction) * mechOffset * correction).eulerAngles;
            return new Vector3(Mathf.DeltaAngle(0f, e.x), Mathf.DeltaAngle(0f, e.y), Mathf.DeltaAngle(0f, e.z));
        }

        /// <summary>기체 축 오일러 → 뼈 로컬 오프셋 회전 (Apply와 같은 식).</summary>
        public static Quaternion MechEulerToLocalOffset(Vector3 euler, Quaternion boneFromMech, Vector3 correctionEuler)
        {
            Quaternion correction = Quaternion.Euler(correctionEuler);
            Quaternion mechOffset = correction * Quaternion.Euler(euler) * Quaternion.Inverse(correction);
            return boneFromMech * mechOffset * Quaternion.Inverse(boneFromMech);
        }

        /// <summary>"기체 기준 → 뼈 기준" 회전 (Bind와 같은 식). 에디터에서 플레이 전 자세로 계산할 때.</summary>
        public Quaternion BoneFromMechOf(Transform bone)
        {
            return Quaternion.Inverse(Quaternion.Inverse(transform.rotation) * bone.rotation);
        }

        /// <summary>포즈 관절 index(JointNames 순서)의 슬롯. 선택 슬롯이 비어 있으면 이름으로 찾은 뼈 (플레이 전에도 동작).</summary>
        public RigSlot GetPoseSlot(int index)
        {
            switch (index)
            {
                case MechPoseLibrary.PelvisIndex: return ResolveOptionalSlot(pelvis, PelvisName);
                case MechPoseLibrary.BodyIndex: return body;
                case MechPoseLibrary.HeadIndex: return head;
                case MechPoseLibrary.ArmLUpperIndex: return armLUpper;
                case MechPoseLibrary.ArmLLowerIndex: return armLLower;
                case MechPoseLibrary.FistLIndex: return fistL;
                case MechPoseLibrary.ArmRUpperIndex: return armRUpper;
                case MechPoseLibrary.ArmRLowerIndex: return armRLower;
                case MechPoseLibrary.FistRIndex: return fistR;
                case MechPoseLibrary.HipLIndex: return ResolveOptionalSlot(hipL, HipLName);
                case MechPoseLibrary.HipRIndex: return ResolveOptionalSlot(hipR, HipRName);
                case MechPoseLibrary.LegLIndex: return legL;
                case MechPoseLibrary.LegRIndex: return legR;
                case MechPoseLibrary.ShinLIndex: return ResolveOptionalSlot(shinL, ShinLName);
                case MechPoseLibrary.ShinRIndex: return ResolveOptionalSlot(shinR, ShinRName);
                default: return null;
            }
        }
    }
}
