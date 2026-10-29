using UnityEngine;

namespace MyDongari.Core
{
    /// <summary>
    /// 소행성 표면 맵 설정 (T1·T2). 메뉴 Tools/Build Asteroid Arena가 이 값으로 맵을 만든다 — 값을 바꾸면 메뉴를 다시 누른다.
    /// 에셋이 없으면 메뉴가 Assets/Arena/AsteroidArenaConfig.asset을 기본값으로 만든다.
    /// </summary>
    [CreateAssetMenu(menuName = "Mydongari/Asteroid Arena Config", fileName = "AsteroidArenaConfig")]
    public class AsteroidArenaConfig : ScriptableObject
    {
        [Header("공통")]
        [Tooltip("지면 판정(평평한 바닥) 높이(m, 월드 Y). 기존 맵 바닥과 같게 두면 기체 높이가 그대로")]
        public float groundHeight = -1.01f;
        [Tooltip("요철·바위 모양 난수 씨앗. 바꾸면 다른 모양")]
        public int seed = 7;

        [Header("크레이터 바닥 (판정 = 평평한 박스, 외형 = 약한 요철 원판)")]
        [Tooltip("바닥 지름(m)")]
        public float floorDiameter = 90f;
        [Tooltip("외형 요철 높이(m, 판정 없음)")]
        public float floorNoiseHeight = 0.08f;
        [Tooltip("요철 크기(작을수록 넓고 완만한 요철)")]
        public float floorNoiseScale = 0.12f;
        [Tooltip("바닥 메시 고리 수·둘레 분할 수")]
        public int floorRings = 36;
        public int floorSectors = 96;

        [Header("가장자리 (외형 = 완만한 경사 + 테두리 바위, 판정 = 보이지 않는 원형 벽)")]
        [Tooltip("경사 폭(m, 바닥 가장자리부터 바깥으로)")]
        public float slopeWidth = 16f;
        [Tooltip("경사 바깥 끝 높이(m)")]
        public float slopeHeight = 7f;
        [Tooltip("테두리 바위 개수")]
        public int rimRockCount = 26;
        [Tooltip("테두리 바위 높이 범위(m)")]
        public Vector2 rimRockHeight = new Vector2(6f, 13f);
        [Tooltip("테두리 바위 반지름 범위(m)")]
        public Vector2 rimRockRadius = new Vector2(3f, 6f);
        [Tooltip("보이지 않는 벽 높이(m)·두께(m)·조각 수")]
        public float wallHeight = 30f;
        public float wallThickness = 2f;
        public int wallSegments = 48;

        [Header("바위 (판정 있음)")]
        public int rockCount = 4;
        [Tooltip("바위 높이 범위(m)")]
        public Vector2 rockHeight = new Vector2(2f, 3f);
        [Tooltip("바위 반지름 범위(m)")]
        public Vector2 rockRadius = new Vector2(1.6f, 2.6f);
        [Tooltip("중앙에서 이 반지름(m) 안은 비운다")]
        public float rockClearRadius = 20f;
        [Tooltip("바위를 놓는 바깥 한계 반지름(m)")]
        public float rockMaxRadius = 36f;
        [Tooltip("다면체 일그러짐 정도(0 = 둥근 다면체)")]
        [Range(0f, 0.6f)] public float rockDistortion = 0.3f;

        [Header("스폰")]
        [Tooltip("두 스폰 사이 거리(m, 중앙 기준 양쪽으로 절반씩, 서로 마주 봄)")]
        public float spawnDistance = 30f;

        [Header("색 (M_ToonMech 기반 머티리얼의 Base Color)")]
        public Color groundColor = new Color32(0x6B, 0x61, 0x58, 0xFF);
        public Color rockColor = new Color32(0x5C, 0x52, 0x4A, 0xFF);
        public Color rimColor = new Color32(0x64, 0x5A, 0x51, 0xFF);

        [Header("행성 (T2)")]
        [Tooltip("행성 반지름(m)")]
        public float planetRadius = 220f;
        [Tooltip("중앙에서 행성까지 거리(m). 카메라 먼 평면(1000m)보다 가까워야 보인다")]
        public float planetDistance = 760f;
        [Tooltip("행성 방향(°): 수평 방위(0 = +Z)·지평선 위 높이")]
        public float planetAzimuth = 35f;
        public float planetElevation = 10f;
        public Color planetColor = new Color(0.32f, 0.45f, 0.62f);
        public Color planetShadowColor = new Color(0.05f, 0.07f, 0.12f);
        public Color planetAtmosphereColor = new Color(0.45f, 0.7f, 1f);
        [Tooltip("대기 림 라이트 세기·좁기")]
        public float planetRimIntensity = 1.6f;
        public float planetRimPower = 3f;

        [Header("조명 (T2)")]
        [Tooltip("해(디렉셔널 라이트) 높이(°)·방위(°)")]
        public float sunElevation = 20f;
        public float sunAzimuth = -40f;
        public float sunIntensity = 2.2f;
        public Color sunColor = Color.white;
        [Tooltip("앰비언트(평면 색) — 푸르스름하게 약하게")]
        public Color ambientColor = new Color(0.07f, 0.09f, 0.14f);
    }
}
