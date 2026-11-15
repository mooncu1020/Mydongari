using UnityEngine;

namespace MyDongari.Game.Bot
{
    /// <summary>연습 봇 행동 모드. 순서(정수)는 씬에 저장돼 있어 바꾸지 않는다 — 새 모드는 끝에 붙인다.</summary>
    public enum BotMode
    {
        /// <summary>가만히 (아무것도 안 누름).</summary>
        Idle,
        /// <summary>가드만 (✕를 계속 누름 — 가드 자동 선회로 상대를 본다).</summary>
        AlwaysGuard,
        /// <summary>공격만: 걷기·앞 대시로 다가가 원투쓰리·뛰어 베기·근접 스텝 펀치, 중거리엔 런지·대시 공격. 가드는 안 한다.</summary>
        Approach,
        /// <summary>거리를 두고(6~12m) 런지·대시 공격.</summary>
        Lunger,
        /// <summary>상대 공격을 보고 패리, 성공하면 반격.</summary>
        Parrier,
        /// <summary>섞어서: 상황 보고 공격·가드·패리·옆 대시 회피·점프를 고른다.</summary>
        Mixed
    }

    /// <summary>두뇌 수치 (거리 m, 시간 틱, 확률 0~1). MeleeDummy 인스펙터에서 고친다.</summary>
    [System.Serializable]
    public class BotBrainTuning
    {
        [Header("반응 지연 (틱) — 상대 움직임을 보고 알아차리기까지")]
        [Tooltip("가드만 모드")]
        public int reactionGuard = 20;
        [Tooltip("공격만 모드")]
        public int reactionApproach = 18;
        [Tooltip("런지 모드")]
        public int reactionLunger = 18;
        [Tooltip("패리 모드 (짧아야 펀치의 첫 판정 19틱 전에 패리 창을 연다)")]
        public int reactionParrier = 10;
        [Tooltip("섞어서 모드")]
        public int reactionMixed = 15;

        [Header("거리 (m)")]
        [Tooltip("이보다 멀면 앞 대시로 다가간다")]
        public float dashApproachDistance = 14f;
        [Tooltip("런지를 노리는 거리 (근접 구역 밖 ~ 이 거리)")]
        public float lungeMaxDistance = 8.5f;
        [Tooltip("런지 모드: 이보다 가까우면 물러난다")]
        public float lungerMinDistance = 6f;
        [Tooltip("런지 모드: 이보다 멀면 다가간다")]
        public float lungerMaxDistance = 12f;
        [Tooltip("상대 공격에 반응하는 거리 (이 안에서 본 공격만 막거나 피한다)")]
        public float threatDistance = 9f;
        [Tooltip("근접 구역 판정 대신 쓰는 거리: 이 안이면 펀치 계열을 고른다 (근접 구역 바깥 70u ≈ 6.1m)")]
        public float meleeDistance = 5.5f;
        [Tooltip("이 각도(°) 안에 상대가 있어야 공격을 누른다 (조준)")]
        public float aimAngle = 20f;

        [Header("간격 (틱)")]
        [Tooltip("공격 하나를 끝낸 뒤 다음 공격을 고르기까지")]
        public int attackIntervalFrames = 40;
        [Tooltip("패리 뒤 반격 펀치까지")]
        public int counterDelayFrames = 6;
        [Tooltip("섞어서 모드: 옆걸음 방향을 바꾸는 간격")]
        public int strafeSwitchFrames = 50;

        [Header("확률")]
        [Range(0f, 1f)] [Tooltip("패리 모드: 본 공격마다 패리를 누를 확률")]
        public float parryChance = 0.7f;
        [Range(0f, 1f)] [Tooltip("섞어서 모드: 위협에 가드(패리)로 답할 확률 — 나머지는 옆 대시 회피·맞받아치기")]
        public float mixedGuardChance = 0.45f;
        [Range(0f, 1f)] [Tooltip("섞어서 모드: 위협에 옆 대시로 피할 확률")]
        public float mixedEvadeChance = 0.35f;
    }

    /// <summary>
    /// 연습 봇의 행동 선택 (입력 생성과 분리 — 무엇을 할지만 정하고, 어떻게 누를지는 BotPad).
    /// 상대는 반응 지연만큼 지난 장면(OpponentView), 자기는 지금 상태(SelfView)만 본다. 상대 입력은 모른다.
    /// V4 팀전 봇은 이 클래스의 표적·상황 판단을 바꾸고 BotPad는 그대로 쓴다.
    /// </summary>
    public class BotBrain
    {
        private readonly BotBrainTuning _t;
        private readonly System.Random _random;

        private int _cooldown;
        private int _lastSeenTick = -1;
        private SeenAction _lastSeenAction;
        private int _counterIn = -1;
        private int _strafeDir = 1;
        private int _strafeTimer;
        private BotOrderKind _wakeChoice = BotOrderKind.WakeStill;
        private bool _wakeChosen;

        public BotBrain(BotBrainTuning tuning, int seed)
        {
            _t = tuning;
            _random = new System.Random(seed);
        }

        /// <summary>모드의 반응 지연(틱).</summary>
        public int ReactionFrames(BotMode mode)
        {
            switch (mode)
            {
                case BotMode.AlwaysGuard: return _t.reactionGuard;
                case BotMode.Approach: return _t.reactionApproach;
                case BotMode.Lunger: return _t.reactionLunger;
                case BotMode.Parrier: return _t.reactionParrier;
                case BotMode.Mixed: return _t.reactionMixed;
                default: return 0;
            }
        }

        public void Reset()
        {
            _cooldown = 0;
            _counterIn = -1;
            _lastSeenTick = -1;
            _lastSeenAction = SeenAction.None;
            _wakeChosen = false;
        }

        /// <summary>내가 패리에 성공했다 (자기 결과 — 사람도 화면으로 안다). 패리 모드는 반격을 예약한다.</summary>
        public void NotifyParried()
        {
            _counterIn = _t.counterDelayFrames;
        }

        private float Next() => (float)_random.NextDouble();
        private int RandomSide() => _random.Next(2) == 0 ? -1 : 1;

        /// <summary>
        /// 이번 틱의 주문. busy = 가상 패드가 한 번짜리 행동을 진행 중(그동안의 주문은 무시된다).
        /// aimBearing = 돌아볼 방위(본 상대 쪽, 상대가 안 보이면 null).
        /// </summary>
        public BotOrder Think(BotMode mode, in SelfView self, in OpponentView seen, bool busy, out float? aimBearing)
        {
            aimBearing = null;
            if (_cooldown > 0) _cooldown--;
            if (_strafeTimer > 0) _strafeTimer--;
            if (_counterIn > 0) _counterIn--;
            if (!self.Valid || !seen.Valid || mode == BotMode.Idle) return new BotOrder(BotOrderKind.Neutral);

            Vector3 offset = seen.Position - self.Position;
            offset.y = 0f;
            float distance = offset.magnitude;
            float bearing = offset.sqrMagnitude > 0.0001f ? Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg : self.Yaw;
            float angleOff = Mathf.Abs(Mathf.DeltaAngle(self.Yaw, bearing));
            aimBearing = bearing;

            // 다운: 기상 선입력 (모드별로 한 번 골라 둔다)
            if (self.KnockedDown)
            {
                if (!_wakeChosen)
                {
                    _wakeChosen = true;
                    _wakeChoice = PickWake(mode);
                }
                return new BotOrder(_wakeChoice, RandomSideStable());
            }
            _wakeChosen = false;

            // 새로 본 상대 행동 (반응 지연이 지난 장면 기준)
            bool newSight = seen.Tick != _lastSeenTick;
            bool attackStarted = newSight && seen.Attacking && _lastSeenAction != seen.Action;
            if (newSight)
            {
                _lastSeenTick = seen.Tick;
                _lastSeenAction = seen.Action;
            }
            if (busy) return new BotOrder(BotOrderKind.Neutral);

            switch (mode)
            {
                case BotMode.AlwaysGuard:
                    return new BotOrder(BotOrderKind.HoldGuard);
                case BotMode.Approach:
                    return Attack(self, seen, distance, angleOff);
                case BotMode.Lunger:
                    return Lunger(self, distance, angleOff);
                case BotMode.Parrier:
                    return Parrier(self, seen, distance, angleOff, attackStarted);
                case BotMode.Mixed:
                    return Mixed(self, seen, distance, angleOff, attackStarted);
            }
            return new BotOrder(BotOrderKind.Neutral);
        }

        private int _wakeSide = 1;

        private int RandomSideStable() => _wakeSide;

        private BotOrderKind PickWake(BotMode mode)
        {
            _wakeSide = RandomSide();
            switch (mode)
            {
                case BotMode.Idle:
                case BotMode.AlwaysGuard:
                    return BotOrderKind.WakeStill;
                case BotMode.Approach:
                    return BotOrderKind.WakeAttack;
                case BotMode.Lunger:
                    return BotOrderKind.WakeDash;
                case BotMode.Parrier:
                    return BotOrderKind.WakeStill;
                default:
                {
                    float r = Next();
                    if (r < 0.3f) return BotOrderKind.WakeAttack;
                    if (r < 0.6f) return BotOrderKind.WakeDash;
                    if (r < 0.8f) return BotOrderKind.WakeJump;
                    return BotOrderKind.WakeStill;
                }
            }
        }

        private bool CanStartAction(in SelfView self) => !self.Locked && !self.Melee && !self.Lunging && !self.Landing && self.Grounded;

        /// <summary>공격만: 멀면 대시·걷기로 다가가고, 근접 거리에선 펀치 계열, 중거리에선 런지·대시 공격.</summary>
        private BotOrder Attack(in SelfView self, in OpponentView seen, float distance, float angleOff)
        {
            if (!CanStartAction(self)) return new BotOrder(BotOrderKind.Neutral);
            bool aimed = angleOff <= _t.aimAngle;
            if (seen.Action == SeenAction.Down) return new BotOrder(distance > _t.meleeDistance ? BotOrderKind.WalkToward : BotOrderKind.Neutral);

            if (distance <= _t.meleeDistance)
            {
                if (_cooldown > 0 || !aimed) return new BotOrder(BotOrderKind.Neutral);
                _cooldown = _t.attackIntervalFrames;
                float r = Next();
                if (r < 0.55f) return new BotOrder(BotOrderKind.Combo, 1, r < 0.2f ? 2 : 3);
                if (r < 0.75f) return new BotOrder(BotOrderKind.JumpSlash);
                if (r < 0.9f && self.Standing) return new BotOrder(BotOrderKind.CloseStep, RandomSide());
                return new BotOrder(BotOrderKind.Combo, 1, 1);
            }
            if (distance <= _t.lungeMaxDistance)
            {
                if (_cooldown > 0 || !aimed) return new BotOrder(BotOrderKind.WalkToward);
                _cooldown = _t.attackIntervalFrames;
                if (self.LungeReady && Next() < 0.6f) return new BotOrder(BotOrderKind.Lunge);
                return new BotOrder(BotOrderKind.DashAttack);
            }
            if (distance > _t.dashApproachDistance && self.DashAllowed) return new BotOrder(BotOrderKind.DashToward);
            return new BotOrder(BotOrderKind.WalkToward);
        }

        /// <summary>거리를 두고 런지·대시 공격.</summary>
        private BotOrder Lunger(in SelfView self, float distance, float angleOff)
        {
            if (!CanStartAction(self)) return new BotOrder(BotOrderKind.Neutral);
            if (distance < _t.lungerMinDistance) return new BotOrder(BotOrderKind.WalkAway);
            if (distance > _t.lungerMaxDistance) return new BotOrder(BotOrderKind.WalkToward);
            if (_cooldown > 0 || angleOff > _t.aimAngle) return new BotOrder(BotOrderKind.Neutral);
            _cooldown = _t.attackIntervalFrames;
            if (distance <= _t.lungeMaxDistance && self.LungeReady) return new BotOrder(BotOrderKind.Lunge);
            return new BotOrder(BotOrderKind.DashAttack);
        }

        /// <summary>상대 공격을 본 순간 확률로 패리(가드를 새로 누름). 패리에 성공하면 반격 펀치.</summary>
        private BotOrder Parrier(in SelfView self, in OpponentView seen, float distance, float angleOff, bool attackStarted)
        {
            if (_counterIn == 0 && CanStartAction(self))
            {
                _counterIn = -1;
                return new BotOrder(BotOrderKind.Combo, 1, 1);
            }
            if (attackStarted && distance <= _t.threatDistance && self.Grounded && Next() < _t.parryChance)
            {
                return new BotOrder(BotOrderKind.Parry);
            }
            return new BotOrder(BotOrderKind.Neutral);
        }

        /// <summary>섞어서: 위협이면 가드·패리 / 옆 대시 / 맞받아치기, 아니면 옆걸음을 섞으며 공격.</summary>
        private BotOrder Mixed(in SelfView self, in OpponentView seen, float distance, float angleOff, bool attackStarted)
        {
            if (_counterIn == 0 && CanStartAction(self))
            {
                _counterIn = -1;
                return new BotOrder(BotOrderKind.Combo, 1, 2);
            }
            if (attackStarted && distance <= _t.threatDistance && self.Grounded)
            {
                float r = Next();
                if (r < _t.mixedGuardChance) return new BotOrder(BotOrderKind.Parry);
                if (r < _t.mixedGuardChance + _t.mixedEvadeChance && self.Standing && self.DashAllowed)
                    return new BotOrder(BotOrderKind.SideDash, RandomSide());
            }
            // 공격을 쉬는 동안엔 옆걸음으로 상대 정면을 비튼다
            if (_cooldown > _t.attackIntervalFrames / 2 && distance <= _t.lungeMaxDistance && CanStartAction(self))
            {
                if (_strafeTimer <= 0)
                {
                    _strafeTimer = _t.strafeSwitchFrames;
                    _strafeDir = RandomSide();
                }
                if (Next() < 0.01f) return new BotOrder(BotOrderKind.Jump);
                return new BotOrder(BotOrderKind.Strafe, _strafeDir);
            }
            return Attack(self, seen, distance, angleOff);
        }
    }
}
