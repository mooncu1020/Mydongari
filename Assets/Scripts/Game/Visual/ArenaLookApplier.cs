using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MyDongari.Game.Visual
{
    /// <summary>
    /// ArenaLook(화면 정리 값)을 실행 중에 적용한다: 하늘 색, 경기장 채도, 외곽선 굵기, 해 방향, HUD 폰트·글자 색.
    /// 씬을 편집하지 않도록 시작할 때 스스로 만들어지고, 씬·머티리얼 에셋은 건드리지 않는다 —
    /// 하늘과 경기장은 머티리얼 사본에 쓰고, 공유 머티리얼인 외곽선만 값을 바꿨다가 끝날 때 되돌린다(에디터 플레이에서 에셋이 바뀐 채 남지 않게).
    /// 기체 색(1P·2P)은 CombatFeedback의 틴트가 맡는다.
    /// </summary>
    public class ArenaLookApplier : MonoBehaviour
    {
        private const string SkyShaderName = "MyDongari/DaySky";
        private const string OutlineShaderName = "Hidden/MyDongari/ScreenOutline";
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int OutlineThicknessId = Shader.PropertyToID("_OutlineThickness");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var go = new GameObject("ArenaLookApplier");
            DontDestroyOnLoad(go);
            go.AddComponent<ArenaLookApplier>();
        }

        private Material _outline;
        private float _outlineOriginal;
        private Material _skyOriginal;
        private Material _skyCopy;
        private readonly List<Material> _copies = new List<Material>();

        private void Start()
        {
            ArenaLook look = ArenaLook.Current;
            Core.ErrorLog.Run(() => ApplySky(look), "ArenaLook.sky");
            Core.ErrorLog.Run(() => ApplyArena(look), "ArenaLook.arena");
            Core.ErrorLog.Run(() => ApplyOutline(look), "ArenaLook.outline");
            Core.ErrorLog.Run(() => ApplySun(look), "ArenaLook.sun");
            Core.ErrorLog.Run(() => ApplyHud(look), "ArenaLook.hud");
        }

        private void ApplySky(ArenaLook look)
        {
            Material sky = RenderSettings.skybox;
            if (!look.skyEnabled || sky == null || sky.shader == null || sky.shader.name != SkyShaderName) return;
            _skyOriginal = sky;
            _skyCopy = new Material(sky) { name = sky.name + " (ArenaLook)" };
            _skyCopy.SetColor("_ZenithColor", look.skyZenith);
            _skyCopy.SetColor("_HorizonColor", look.skyHorizon);
            _skyCopy.SetColor("_CloudColor", look.cloudColor);
            _skyCopy.SetColor("_CloudShadeColor", look.cloudShadeColor);
            _skyCopy.SetColor("_CloudGapColor", look.cloudGapColor);
            RenderSettings.skybox = _skyCopy;
        }

        /// <summary>경기장 아래 렌더러의 머티리얼을 사본으로 바꾸고 기본색의 채도·밝기를 낮춘다 (같은 머티리얼은 사본 하나를 같이 쓴다).</summary>
        private void ApplyArena(ArenaLook look)
        {
            if (!look.arenaEnabled || string.IsNullOrEmpty(look.arenaRootName)) return;
            GameObject root = GameObject.Find(look.arenaRootName);
            if (root == null) return;
            var map = new Dictionary<Material, Material>();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material source = materials[i];
                    if (source == null || !source.HasProperty(BaseColorId)) continue;
                    if (!map.TryGetValue(source, out Material copy))
                    {
                        copy = new Material(source) { name = source.name + " (ArenaLook)" };
                        Color color = source.GetColor(BaseColorId);
                        Color.RGBToHSV(color, out float h, out float s, out float v);
                        Color muted = Color.HSVToRGB(h, s * look.arenaSaturation, Mathf.Clamp01(v * look.arenaBrightness));
                        muted.a = color.a;
                        copy.SetColor(BaseColorId, muted);
                        map[source] = copy;
                        _copies.Add(copy);
                    }
                    materials[i] = copy;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }
        }

        private void ApplyOutline(ArenaLook look)
        {
            if (!look.outlineEnabled) return;
            foreach (Material material in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (material == null || material.shader == null || material.shader.name != OutlineShaderName) continue;
                if (!material.HasProperty(OutlineThicknessId)) continue;
                _outline = material;
                _outlineOriginal = material.GetFloat(OutlineThicknessId);
                material.SetFloat(OutlineThicknessId, look.outlineThickness);
                break;
            }
        }

        private static void ApplySun(ArenaLook look)
        {
            Light sun = RenderSettings.sun;
            if (!look.sunEnabled || sun == null) return;
            sun.transform.rotation = Quaternion.Euler(look.sunElevation, look.sunAzimuth, 0f);
        }

        /// <summary>씬의 UI 글자를 공용 폰트로, 색은 일반(흰색 계열)·경고(색이 들어간 것) 두 가지로.</summary>
        private static void ApplyHud(ArenaLook look)
        {
            if (!look.hudEnabled) return;
            Font font = ArenaLook.HudFont;
            foreach (Text text in FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (font != null) text.font = font;
                Color.RGBToHSV(text.color, out _, out float saturation, out _);
                Color target = saturation > 0.3f ? look.hudWarningColor : look.hudTextColor;
                target.a = text.color.a;
                text.color = target;
            }
        }

        private void OnDestroy()
        {
            // 공유 머티리얼에 쓴 값은 되돌린다 (에디터 플레이 모드에서 에셋이 바뀐 채 남지 않게)
            if (_outline != null) _outline.SetFloat(OutlineThicknessId, _outlineOriginal);
            if (_skyCopy != null && RenderSettings.skybox == _skyCopy) RenderSettings.skybox = _skyOriginal;
            if (_skyCopy != null) Destroy(_skyCopy);
            foreach (Material copy in _copies)
            {
                if (copy != null) Destroy(copy);
            }
        }
    }
}
