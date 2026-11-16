using UnityEngine;

namespace MyDongari.Game.Combat
{
    /// <summary>공격 종류 (우리 기술 이름). 로그·외형·패리 반사 여부가 이걸로 갈린다.</summary>
    public enum AttackType
    {
        Punch,
        DashAttack,
        Tackle,
        Lunge,
        Slam,
        PileDriver,
        Projectile,
        /// <summary>뛰어 베기 (원본 점프 근접).</summary>
        JumpSlash
    }

    /// <summary>
    /// 피격 판정 종류 (전투 사양서 2.1·2.4·6.3). 반응 단계와 히트스톱이 이걸로 갈린다.
    /// 일반 근접·강타는 때린 쪽도 히트스톱, 근접 계열 2(대시 근접)·3(점프 근접)은 때린 쪽이 멈추지 않는다. 강타는 가드 불능 + 무조건 날아감.
    /// </summary>
    public enum HitKind
    {
        Melee,
        Melee2,
        Melee3,
        Strong,
        /// <summary>사격 (근접 무적·히트스톱과 무관, 가드 불가).</summary>
        Shot
    }

    /// <summary>
    /// 공격 세기 등급 (N1). 화면 흔들림·피격 반응 포즈·이펙트 크기가 이걸로 갈린다 (연출 전용 — 히트스톱은 등급과 무관하게 15f).
    /// 약 = 콤보 원·투, 중 = 쓰리·대시 어택, 강 = 런지·태클·파일 드라이버·슬램·뛰어 베기.
    /// </summary>
    public enum AttackStrength
    {
        Light,
        Medium,
        Heavy
    }

    /// <summary>피격 판단 결과. 로그·연출·후속 처리용.</summary>
    public enum HitResult
    {
        Hit,
        Guarded,
        /// <summary>가드 중에 맞았지만 가드가 깨짐 (강타, 또는 충격치 판정값이 날아감 문턱 이상).</summary>
        GuardBroken,
        Parried,
        Clash,
        /// <summary>무적(근접 무적·다운 중)이라 판정 무시. 피해·반응·이펙트 없음, 로그만</summary>
        Invulnerable
    }

    /// <summary>공격자가 피격자에게 넘기는 한 번의 타격 기록. 틱 끝에 피격자의 기록에 쌓이고 다음 틱 맨 앞에 판단된다.</summary>
    public struct HitInfo
    {
        public AttackType Type;
        public HitKind Kind;
        public float Damage;
        /// <summary>충격치 (전투 사양서 2.1). 피격자의 누적값에 더해진다.</summary>
        public float Impact;
        public GameObject Attacker;
        /// <summary>공격자의 피격 기록 (같은 틱 맞교환 판단용). 전투 허브가 없는 공격자(투사체)는 null.</summary>
        public CombatLedger Source;
        /// <summary>공격 방향 = 피격자가 밀려나는 방향 (수평). 근접은 공격자 → 피격자.</summary>
        public Vector3 Direction;
        /// <summary>펀치 콤보 단계 (1 원, 2 투, 3 쓰리). 펀치가 아니면 0</summary>
        public int ComboStep;
        /// <summary>가드를 무시한다 (피해 감소·가드 경직 없음). 파일 드라이버.</summary>
        public bool IgnoresGuard;
        /// <summary>패리가 안 된다. 파일 드라이버(잡기 후속타).</summary>
        public bool IgnoresParry;
        /// <summary>판단된 반응 단계. 피격자가 적용할 때 채운다 (연출·로그용).</summary>
        public Reaction Reaction;
        /// <summary>날아가기 전 대기(틱). 파일 드라이버가 외형 어퍼컷이 닿는 순간에 맞추려고 쓴다.</summary>
        public int LaunchDelayFrames;
        /// <summary>지상 대시의 슈퍼아머로 버텼다 (경직급인데 반응 없이 무적만). 피격자가 적용할 때 채운다 (연출·로그용).</summary>
        public bool ArmoredByDash;

        /// <summary>이 타격의 세기 등급 (연출용).</summary>
        public AttackStrength Strength => StrengthOf(Type, ComboStep);

        public static AttackStrength StrengthOf(AttackType type, int comboStep)
        {
            switch (type)
            {
                case AttackType.Punch: return comboStep >= 3 ? AttackStrength.Medium : AttackStrength.Light;
                case AttackType.DashAttack: return AttackStrength.Medium;
                case AttackType.Lunge:
                case AttackType.Tackle:
                case AttackType.PileDriver:
                case AttackType.JumpSlash:
                case AttackType.Slam: return AttackStrength.Heavy;
                default: return AttackStrength.Light;
            }
        }

        /// <summary>근접 계열인지 (근접 무적·히트스톱·가드 대상).</summary>
        public bool IsMelee => Kind != HitKind.Shot;
        /// <summary>강타: 가드 불능 + 무조건 날아감.</summary>
        public bool IsStrong => Kind == HitKind.Strong;
        /// <summary>맞히면 때린 쪽도 히트스톱이 걸리는 종류 (일반 근접·강타).</summary>
        public bool StopsAttacker => Kind == HitKind.Melee || Kind == HitKind.Strong;
        /// <summary>같은 틱에 서로 맞으면 맞딜이 되는 묶음: 펀치끼리(1), 대시 공격·태클끼리(2), 그 밖은 0(맞딜 없음).</summary>
        public int ClashFamily => Type == AttackType.Punch ? 1 : (Type == AttackType.DashAttack || Type == AttackType.Tackle ? 2 : 0);
        /// <summary>패리하면 공격자에게 피해·넉백을 되돌리는 종류 (런지·태클·대시 어택).</summary>
        public bool ReflectsOnParry => Type == AttackType.Tackle || Type == AttackType.DashAttack || Type == AttackType.Lunge;
    }

    /// <summary>
    /// 공격 버튼 한 번의 진단 기록 (펀치가 왜 안 맞았는지 가리는 용도). 펀치 휘두르기마다 하나, 펀치가 안 나간 누름에도 하나.
    /// 결과: hit·guarded·guard_broken·parried·clash / miss_range(사거리 밖) / invulnerable(상대 무적) / super_armor(대시 슈퍼아머)
    /// / canceled_…(판정 창 전에 끊김) / zone_out_lunge(구역 밖이라 런지) / lunge_cooldown(런지 쿨다운이라 아무것도 안 나감).
    /// </summary>
    public struct PunchTrace
    {
        public const string MissRange = "miss_range";
        public const string ZoneOutLunge = "zone_out_lunge";
        public const string LungeCooldown = "lunge_cooldown";

        /// <summary>콤보 단계 (1 원, 2 투, 3 쓰리). 펀치가 안 나간 누름은 0.</summary>
        public int Step;
        /// <summary>시작 틱의 상대까지 수평 거리(m)·정면에서 벗어난 각(°)·높이차(나 − 상대, m).</summary>
        public float StartDistance;
        public float StartAngle;
        public float StartHeightDiff;
        /// <summary>시작 틱에 상대가 근접 구역 안이었는지.</summary>
        public bool InZone;
        /// <summary>판정 창 동안 공격 캡슐과 상대 피격 캡슐 사이의 가장 작은 틈(m). 0 이하 = 닿음. 창이 안 열렸으면 큰 값.</summary>
        public float MinGap;
        /// <summary>판정을 본 틱 수.</summary>
        public int WindowTicks;
        public string Result;

        /// <summary>닿은 타격의 판단 결과를 진단 이름으로.</summary>
        public static string ResultOf(HitInfo info, HitResult result)
        {
            switch (result)
            {
                case HitResult.Invulnerable: return "invulnerable";
                case HitResult.Guarded: return "guarded";
                case HitResult.GuardBroken: return "guard_broken";
                case HitResult.Parried: return "parried";
                case HitResult.Clash: return "clash";
                default: return info.ArmoredByDash ? "super_armor" : "hit";
            }
        }

        public override string ToString()
        {
            System.Globalization.CultureInfo c = System.Globalization.CultureInfo.InvariantCulture;
            string gap = WindowTicks > 0 ? MinGap.ToString("0.00", c) : "-";
            return string.Format(c, "{0} step={1} d={2:0.00} a={3:0} h={4:0.00} zone={5} gap={6} window={7}",
                Result, Step, StartDistance, StartAngle, StartHeightDiff, InZone ? 1 : 0, gap, WindowTicks);
        }
    }

    /// <summary>전투 시간 환산. 전투의 모든 시간은 60Hz 틱 수로 센다 — 초는 외형·표시에만 쓴다.</summary>
    public static class CombatTime
    {
        public const int TickRate = 60;
        /// <summary>틱 → 초 (외형 포즈 길이·HUD 표시용).</summary>
        public static float Seconds(int frames) => frames / (float)TickRate;
        public static float Seconds(float frames) => frames / TickRate;
        /// <summary>초 → 틱 (외형 모션 길이처럼 초로 적힌 값을 전투 틱에 맞출 때).</summary>
        public static int Frames(float seconds) => Mathf.Max(0, Mathf.RoundToInt(seconds * TickRate));
    }
}
