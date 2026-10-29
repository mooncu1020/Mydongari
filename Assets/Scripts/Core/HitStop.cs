using System;
using System.Collections.Generic;
using UnityEngine;
using MyDongari.Combat;
using MyDongari.Mech;
using MyDongari.Visual;

namespace MyDongari.Core
{
    /// <summary>
    /// 명중 순간 아주 짧게 시간을 멈춰 타격감을 준다. Time.timeScale 방식이라 화면 분할 두 사람이 같이 멈추는데,
    /// 1대1에서는 어차피 둘 다 그 타격의 당사자라 허용한다.
    /// 스턴 타이머 등 게임 로직은 scaled deltaTime을 쓰므로 히트스톱 동안 자동으로 멈추고,
    /// 결과적으로 런지 스턴은 히트스톱이 끝난 뒤부터 흐른다. 히트스톱 자체 길이는 실제 시간(unscaled)으로 잰다.
    /// 전투 코드는 히트스톱을 모른다. 각 기체 MeleeSystem.OnHitResolved를 구독만 한다.
    /// </summary>
    public class HitStop : MonoBehaviour
    {
        private float _endTime;
        private bool _active;
        private float _savedTimeScale = 1f;
        private readonly List<Action> _unsubscribers = new List<Action>();

        private void Start()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null) return;
            // 모드(VERSUS/PRACTICE)에 따라 상대가 바뀌므로 P2·연습 봇 모두 구독 (꺼진 기체는 이벤트가 안 난다)
            foreach (GameObject mech in gm.AllMechs) Subscribe(mech);
        }

        private void Subscribe(GameObject mech)
        {
            if (mech == null) return;
            MeleeSystem melee = mech.GetComponent<MeleeSystem>();
            if (melee == null) return;

            // 히트스톱은 GameManager에 하나뿐이라, 맞은 기체의 튜닝 에셋에서 길이를 읽는다
            MechTuning tuning = mech.GetComponent<MechTuning>();
            Action<HitInfo, HitResult> onHit = (info, result) => ErrorLog.Run(() => OnHitResolved(tuning, info, result), "HitStop.onHit");
            melee.OnHitResolved += onHit;
            _unsubscribers.Add(() => melee.OnHitResolved -= onHit);
        }

        private void OnHitResolved(MechTuning tuning, HitInfo info, HitResult result)
        {
            BernardTuning bernard = tuning != null ? tuning.Values : BernardTuning.Fallback;
            BernardTuning.HitStopValues values = bernard.HitStop;
            if (!values.enabled) return;
            // 전신 모션 기체의 파일 드라이버는 판정(누른 순간)이 아니라 외형 타격 순간에 리그가 건다 (M6)
            if (info.Type == AttackType.PileDriver && MechVisualRig.HandlesPileDriverImpactOf(info.Attacker)) return;
            Request(DurationFor(values, bernard.HitFeel, info, result));
        }

        private static float DurationFor(BernardTuning.HitStopValues values, BernardTuning.HitFeelValues feel, HitInfo info, HitResult result)
        {
            // 패리는 "안 맞음"이라 멈추지 않는다
            if (result == HitResult.Parried) return 0f;
            if (result == HitResult.Clash) return values.clash;
            // N1: 세기 등급별 (원·투 약 / 쓰리·대시 어택 중 / 런지·태클·파일 드라이버·슬램 강)
            if (feel.gradeHitStop) return feel.Grade(info.Strength).hitStop;
            AttackType type = info.Type;

            switch (type)
            {
                case AttackType.Punch: return values.punch;
                case AttackType.Lunge:
                case AttackType.Tackle:
                case AttackType.DashAttack: return values.lungeTackle;
                default: return values.other;
            }
        }

        /// <summary>지금부터 duration(실제 시간) 동안 멈춘다. 겹치면 더 늦게 끝나는 쪽으로 늘린다.</summary>
        public void Request(float duration)
        {
            if (duration <= 0f) return;

            _endTime = Mathf.Max(_endTime, Time.unscaledTime + duration);
            if (_active) return;

            _active = true;
            _savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        private void Update()
        {
            // timeScale이 0이어도 Update는 돈다 (deltaTime만 0)
            if (_active && Time.unscaledTime >= _endTime) Restore();
        }

        private void Restore()
        {
            if (!_active) return;
            _active = false;
            Time.timeScale = _savedTimeScale > 0f ? _savedTimeScale : 1f;
        }

        private void OnDisable()
        {
            // 멈춘 채로 꺼지면 게임 전체가 정지 상태로 남는다
            Restore();
        }

        private void OnDestroy()
        {
            foreach (Action unsubscribe in _unsubscribers) unsubscribe();
            _unsubscribers.Clear();
        }
    }
}
