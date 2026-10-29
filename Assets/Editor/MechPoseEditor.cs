using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MyDongari.Visual;

namespace MyDongari.EditorTools
{
    /// <summary>
    /// 포즈 라이브러리 캡처·미리보기 창 (M1). 메뉴 Tools/Mech Pose Editor.
    /// 플레이 중: "리그 멈춤"을 켜고 씬에서 bernard 뼈를 직접 돌린 뒤 "현재 자세를 포즈로 저장". 미리보기는 리그가 그 포즈만 보여 준다.
    /// 플레이 전(에디터): "기준 자세 기억"으로 지금 자세를 원래 자세로 잡고, 뼈를 돌려 저장, 끝나면 "원래 자세로 되돌리기".
    /// 플레이 전에는 bernard 원본 계층(Body 아래 Pelvis)이라 몸통을 돌리면 다리도 같이 돌아 보인다 — 실제 게임은 Pelvis 아래 Body라
    /// 관절별 값은 같지만 보이는 모습이 다르니, 하체·몸통이 같이 걸린 포즈는 플레이 중 캡처를 권장한다.
    /// </summary>
    public class MechPoseEditor : EditorWindow
    {
        private const string AssetFolder = "Assets/Resources";
        private static readonly string AssetPath = AssetFolder + "/" + MechPoseLibrary.ResourcesName + ".asset";

        private MechVisualRig _rig;
        private MechPoseLibrary _library;
        private int _poseIndex;
        private string _newPoseName = "";
        private bool _mirror;
        private bool _skipUnchanged;
        private Vector2 _scroll;

        // 플레이 전 편집용: 기준 자세(원래 자세) 기억
        private class Snapshot
        {
            public Transform Bone;
            public Quaternion LocalRotation;
            public Vector3 LocalPosition;
            public Quaternion BoneFromMech;
            public Vector3 Correction;
        }

        private Snapshot[] _snapshot;
        private MechVisualRig _snapshotRig;

        [MenuItem("Tools/Mech Pose Editor")]
        private static void Open()
        {
            GetWindow<MechPoseEditor>("Mech Pose Editor");
        }

        [MenuItem("Tools/Create Mech Pose Library (초안 값)")]
        private static void CreateLibraryMenu()
        {
            MechPoseLibrary library = EnsureLibraryAsset(out bool created);
            Selection.activeObject = library;
            EditorUtility.DisplayDialog("Mech Pose Library",
                created ? $"{AssetPath}를 초안 포즈로 만들었습니다. 기체에 연결하지 않아도 Resources에서 자동으로 씁니다."
                        : $"이미 있습니다: {AssetDatabase.GetAssetPath(library)}", "확인");
        }

        private static MechPoseLibrary EnsureLibraryAsset(out bool created)
        {
            created = false;
            MechPoseLibrary existing = AssetDatabase.LoadAssetAtPath<MechPoseLibrary>(AssetPath);
            if (existing != null) return existing;
            if (!AssetDatabase.IsValidFolder(AssetFolder)) AssetDatabase.CreateFolder("Assets", "Resources");
            MechPoseLibrary library = CreateInstance<MechPoseLibrary>();
            library.ResetToDraft();
            AssetDatabase.CreateAsset(library, AssetPath);
            AssetDatabase.SaveAssets();
            created = true;
            return library;
        }

        private void OnSelectionChange()
        {
            if (Selection.activeGameObject == null) return;
            MechVisualRig rig = Selection.activeGameObject.GetComponentInParent<MechVisualRig>();
            if (rig != null && rig != _rig)
            {
                _rig = rig;
                Repaint();
            }
        }

        private void OnDisable()
        {
            // 창을 닫을 때 멈춤·미리보기를 남기지 않는다
            if (_rig != null)
            {
                _rig.PoseEditFreeze = false;
                if (Application.isPlaying) _rig.ClearPreviewPose();
            }
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            _rig = (MechVisualRig)EditorGUILayout.ObjectField("기체 리그", _rig, typeof(MechVisualRig), true);
            if (_library == null) _library = FindLibrary();
            _library = (MechPoseLibrary)EditorGUILayout.ObjectField("포즈 라이브러리", _library, typeof(MechPoseLibrary), false);

            if (_library == null)
            {
                EditorGUILayout.HelpBox("포즈 라이브러리 에셋이 없습니다. 지금은 코드 초안 값으로 동작하고, 캡처하려면 에셋이 필요합니다.", MessageType.Info);
                if (GUILayout.Button("라이브러리 에셋 만들기 (초안 값)")) _library = EnsureLibraryAsset(out _);
                EditorGUILayout.EndScrollView();
                return;
            }
            if (_rig == null)
            {
                EditorGUILayout.HelpBox("씬에서 기체(Mech 또는 그 아래 뼈)를 선택하세요.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawPoseSelector();
            EditorGUILayout.Space();
            if (Application.isPlaying) DrawPlayMode();
            else DrawEditMode();

            EditorGUILayout.EndScrollView();
        }

        private MechPoseLibrary FindLibrary()
        {
            if (_rig != null && _rig.AssignedPoseLibrary != null) return _rig.AssignedPoseLibrary;
            MechPoseLibrary library = AssetDatabase.LoadAssetAtPath<MechPoseLibrary>(AssetPath);
            if (library != null) return library;
            string[] guids = AssetDatabase.FindAssets("t:MechPoseLibrary");
            return guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<MechPoseLibrary>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
        }

        private string[] PoseNameList()
        {
            var names = new List<string>();
            foreach (MechPoseLibrary.Pose pose in _library.Poses)
            {
                if (pose != null && !string.IsNullOrEmpty(pose.name)) names.Add(pose.name);
            }
            return names.ToArray();
        }

        /// <summary>지금 고른 포즈 이름. 새 이름 칸이 채워져 있으면 그것.</summary>
        private string SelectedPose
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_newPoseName)) return _newPoseName.Trim();
                string[] names = PoseNameList();
                return names.Length > 0 ? names[Mathf.Clamp(_poseIndex, 0, names.Length - 1)] : null;
            }
        }

        private void DrawPoseSelector()
        {
            string[] names = PoseNameList();
            _poseIndex = EditorGUILayout.Popup("포즈", Mathf.Clamp(_poseIndex, 0, Mathf.Max(0, names.Length - 1)), names);
            _newPoseName = EditorGUILayout.TextField(new GUIContent("새 포즈 이름", "채우면 위 목록 대신 이 이름으로 저장·미리보기"), _newPoseName);
            _mirror = EditorGUILayout.Toggle(new GUIContent("좌우 반전으로 미리보기", "왼 주먹 펀치처럼 반전해서 쓰는 모습 확인"), _mirror);
            _skipUnchanged = EditorGUILayout.Toggle(new GUIContent("안 바뀐 관절은 빼고 저장",
                "원래 자세와 0.5° 안쪽인 관절을 포즈에서 뺀다. 빠진 관절은 그 순간 밑에 깔린 자세(걸음 등)가 그대로 나온다"), _skipUnchanged);
            EditorGUILayout.LabelField("저장·미리보기 대상", SelectedPose ?? "(없음)");
        }

        // ---------------- 플레이 중 ----------------

        private void DrawPlayMode()
        {
            EditorGUILayout.LabelField("플레이 중", EditorStyles.boldLabel);
            bool freeze = EditorGUILayout.Toggle(new GUIContent("리그 멈춤 (뼈 직접 돌리기)", "켜면 리그가 뼈를 쓰지 않는다. 씬에서 뼈를 돌린 뒤 저장"), _rig.PoseEditFreeze);
            if (freeze != _rig.PoseEditFreeze)
            {
                _rig.PoseEditFreeze = freeze;
                if (freeze) _rig.ClearPreviewPose();
            }

            if (GUILayout.Button("현재 자세를 이 포즈로 저장"))
            {
                Undo.RecordObject(_library, "Capture Mech Pose");
                if (_rig.CapturePose(_library, SelectedPose, _skipUnchanged, out string message))
                {
                    EditorUtility.SetDirty(_library);
                    AssetDatabase.SaveAssetIfDirty(_library);
                    WarnIfNotUsed();
                }
                ShowNotification(new GUIContent(message));
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("이 포즈 미리보기"))
            {
                _rig.PoseEditFreeze = false;
                _rig.SetPreviewPose(SelectedPose, _mirror);
            }
            if (GUILayout.Button("미리보기 끄기")) _rig.ClearPreviewPose();
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_rig.PreviewPoseName)) EditorGUILayout.HelpBox($"미리보기 중: {_rig.PreviewPoseName}", MessageType.None);
            if (_rig.PreviewPoseName != null || _rig.PoseEditFreeze)
            {
                EditorGUILayout.HelpBox("미리보기 → 리그 멈춤 순서로 켜면 그 포즈에서 시작해 뼈를 고칠 수 있다.", MessageType.None);
            }
        }

        private void WarnIfNotUsed()
        {
            // 리그가 실제로 읽는 라이브러리가 이 에셋이 아니면 저장해도 게임에 안 보인다
            if (_rig.PoseLibrary != _library)
            {
                Debug.LogWarning($"[MechPoseEditor] {_rig.name}은 다른 라이브러리({_rig.PoseLibrary.name})를 쓰고 있습니다. " +
                                 $"MechVisualRig → Pose Library에 {_library.name}을 연결하거나 {AssetPath}로 두세요.", _rig);
            }
        }

        // ---------------- 플레이 전 ----------------

        private void DrawEditMode()
        {
            EditorGUILayout.LabelField("플레이 전 (씬 자세 직접 편집)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("1) 기준 자세 기억 → 2) 뼈를 돌림 → 3) 저장 → 4) 원래 자세로 되돌리기. 되돌리지 않고 씬을 저장하면 그 자세가 원래 자세가 된다.",
                MessageType.Warning);

            bool hasSnapshot = _snapshot != null && _snapshotRig == _rig;
            if (GUILayout.Button(hasSnapshot ? "기준 자세 다시 기억 (지금 자세로)" : "기준 자세 기억 (편집 시작)")) TakeSnapshot();

            using (new EditorGUI.DisabledScope(!hasSnapshot))
            {
                if (GUILayout.Button("현재 자세를 이 포즈로 저장")) CaptureEditMode();
                if (GUILayout.Button("이 포즈 미리보기")) PreviewEditMode();
                if (GUILayout.Button("원래 자세로 되돌리기")) RestoreSnapshot();
            }
        }

        private void TakeSnapshot()
        {
            int count = MechPoseLibrary.JointNames.Length;
            _snapshot = new Snapshot[count];
            _snapshotRig = _rig;
            for (int i = 0; i < count; i++)
            {
                RigSlot slot = _rig.GetPoseSlot(i);
                Transform bone = slot != null ? slot.target : null;
                if (bone == null) continue;
                _snapshot[i] = new Snapshot
                {
                    Bone = bone,
                    LocalRotation = bone.localRotation,
                    LocalPosition = bone.localPosition,
                    BoneFromMech = _rig.BoneFromMechOf(bone),
                    Correction = slot.rotationCorrection
                };
            }
            ShowNotification(new GUIContent("기준 자세를 기억했습니다"));
        }

        private void CaptureEditMode()
        {
            var names = new List<string>();
            var rotations = new List<Vector3>();
            Vector3 pelvisOffset = Vector3.zero;
            for (int i = 0; i < _snapshot.Length; i++)
            {
                Snapshot snap = _snapshot[i];
                if (snap == null || snap.Bone == null) continue;
                Quaternion localOffset = Quaternion.Inverse(snap.LocalRotation) * snap.Bone.localRotation;
                Vector3 euler = MechVisualRig.LocalOffsetToMechEuler(localOffset, snap.BoneFromMech, snap.Correction);
                if (_skipUnchanged && euler.sqrMagnitude < 0.25f) continue;
                names.Add(MechPoseLibrary.JointNames[i]);
                rotations.Add(euler);
                if (i == MechPoseLibrary.PelvisIndex)
                {
                    Vector3 delta = snap.Bone.localPosition - snap.LocalPosition;
                    Vector3 world = snap.Bone.parent != null ? snap.Bone.parent.TransformVector(delta) : delta;
                    pelvisOffset = _rig.transform.InverseTransformDirection(world);
                }
            }
            Undo.RecordObject(_library, "Capture Mech Pose");
            _library.SetPose(SelectedPose, pelvisOffset, names, rotations);
            EditorUtility.SetDirty(_library);
            AssetDatabase.SaveAssetIfDirty(_library);
            ShowNotification(new GUIContent($"{SelectedPose}: 관절 {names.Count}개 저장"));
        }

        private void PreviewEditMode()
        {
            MechPoseLibrary.Baked baked = _library.Get(SelectedPose, _mirror);
            if (baked == null)
            {
                ShowNotification(new GUIContent("포즈가 없습니다"));
                return;
            }
            var bones = new List<Object>();
            foreach (Snapshot snap in _snapshot) if (snap != null && snap.Bone != null) bones.Add(snap.Bone);
            Undo.RecordObjects(bones.ToArray(), "Preview Mech Pose");
            for (int i = 0; i < _snapshot.Length; i++)
            {
                Snapshot snap = _snapshot[i];
                if (snap == null || snap.Bone == null) continue;
                Vector3 euler = baked.Has[i] ? baked.Rotation[i] : Vector3.zero;
                snap.Bone.localRotation = snap.LocalRotation * MechVisualRig.MechEulerToLocalOffset(euler, snap.BoneFromMech, snap.Correction);
                if (i == MechPoseLibrary.PelvisIndex)
                {
                    Vector3 world = _rig.transform.TransformDirection(baked.PelvisOffset);
                    Vector3 local = snap.Bone.parent != null ? snap.Bone.parent.InverseTransformVector(world) : world;
                    snap.Bone.localPosition = snap.LocalPosition + local;
                }
            }
        }

        private void RestoreSnapshot()
        {
            var bones = new List<Object>();
            foreach (Snapshot snap in _snapshot) if (snap != null && snap.Bone != null) bones.Add(snap.Bone);
            Undo.RecordObjects(bones.ToArray(), "Restore Mech Pose");
            foreach (Snapshot snap in _snapshot)
            {
                if (snap == null || snap.Bone == null) continue;
                snap.Bone.localRotation = snap.LocalRotation;
                snap.Bone.localPosition = snap.LocalPosition;
            }
            ShowNotification(new GUIContent("원래 자세로 되돌렸습니다"));
        }
    }
}
