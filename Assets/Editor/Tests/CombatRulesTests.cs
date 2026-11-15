using NUnit.Framework;
using UnityEngine;
using MyDongari.Bridge;
using MyDongari.Core.VirtualOn;
using MyDongari.Game.Combat;

namespace MyDongari.EditorTools.Tests
{
    /// <summary>
    /// 전투 순수 로직의 EditMode 테스트 (틱 단위, 씬·물리 없음). Window → General → Test Runner → EditMode에서 돌린다.
    /// 명령줄: Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml
    /// </summary>
    public class CombatRulesTests
    {
        /// <summary>우리 값이 얹힌 전투 수치 (에셋 없이 코드 기본값으로 — 원 280 / 투 280 / 쓰리 1100).</summary>
        private static VoCombatSpec Spec()
        {
            StyleOverrides style = ScriptableObject.CreateInstance<StyleOverrides>();
            VoCombatSpec c = style.EffectiveCombat;
            Object.DestroyImmediate(style);
            return c;
        }

        private static HitInfo Punch(VoCombatSpec c, int step, CombatLedger source = null)
        {
            return new HitInfo
            {
                Type = AttackType.Punch,
                Kind = HitKind.Melee,
                Damage = 25f,
                Impact = step >= 3 ? c.impactC1 : (step == 2 ? c.impactA1Second : c.impactA1First),
                ComboStep = step,
                Source = source,
                // 공격자가 −z 쪽에 서서 +z로 때린다
                Direction = Vector3.forward
            };
        }

        // ---------------- 원·투 → 경직, 쓰리 → 다운 ----------------

        [Test]
        public void OneAndTwoStagger_ThreeKnocksDown()
        {
            VoCombatSpec c = Spec();
            var posture = new DefenderPosture();
            var meter = new ImpactMeter();

            HitDecision one = HitRules.Decide(c, Punch(c, 1), posture, meter.Value, false);
            Assert.AreEqual(Reaction.Stagger, one.Reaction, "원은 경직");
            meter.Add(one.ImpactAdded);

            // 콤보 간격(원 ② 끝 → 투 ① 16f + 판정 3f)만큼 줄어든 뒤 투
            for (int i = 0; i < 20; i++) meter.Tick(c.impactDecay);
            HitDecision two = HitRules.Decide(c, Punch(c, 2), posture, meter.Value, false);
            Assert.AreEqual(Reaction.Stagger, two.Reaction, "원 + 투를 연달아 맞아도 경직 (다운 문턱 600 아래)");
            meter.Add(two.ImpactAdded);

            HitDecision three = HitRules.Decide(c, Punch(c, 3), posture, 0f, false);
            Assert.AreEqual(Reaction.Down, three.Reaction, "쓰리는 단독으로 다운");
        }

        [Test]
        public void ThreeAlone_KnocksDown()
        {
            VoCombatSpec c = Spec();
            HitDecision three = HitRules.Decide(c, Punch(c, 3), new DefenderPosture(), 0f, false);
            Assert.AreEqual(620f, three.ImpactAdded, 0.0001f, "쓰리 충격치 = 우리 값 620");
            Assert.AreEqual(Reaction.Down, three.Reaction, "단독 쓰리(620 ≥ 600)는 다운");
        }

        [Test]
        public void OneTwoThree_AllHit_EndsInDown_NotBlown()
        {
            VoCombatSpec c = Spec();
            var posture = new DefenderPosture();
            // 가장 불리한 경우: 세 타가 감소 없이 같은 틱에 쌓여도 날아감 문턱(1200)을 넘지 않는다
            float worst = c.impactA1First + c.impactA1Second;
            Assert.AreEqual(Reaction.Down, HitRules.Decide(c, Punch(c, 3), posture, worst, false).Reaction, "원·투 누적 최대(560) 위의 쓰리");

            // 실제 콤보 간격으로: 원 → (히트스톱 15f + ② 나머지 + 투 ① 16f + 판정 3f) → 투 → 같은 간격 → 쓰리
            var meter = new ImpactMeter();
            HitDecision one = HitRules.Decide(c, Punch(c, 1), posture, meter.Value, false);
            Assert.AreEqual(Reaction.Stagger, one.Reaction);
            meter.Add(one.ImpactAdded);
            for (int i = 0; i < 40; i++) meter.Tick(c.impactDecay);
            HitDecision two = HitRules.Decide(c, Punch(c, 2), posture, meter.Value, false);
            Assert.AreEqual(Reaction.Stagger, two.Reaction);
            meter.Add(two.ImpactAdded);
            for (int i = 0; i < 46; i++) meter.Tick(c.impactDecay);
            HitDecision three = HitRules.Decide(c, Punch(c, 3), posture, meter.Value, false);
            Assert.AreEqual(Reaction.Down, three.Reaction, "원투쓰리를 전부 맞아도 날아감이 아니라 다운");
        }

        [Test]
        public void OriginalC1Impact_WouldBlowAwayAfterOneTwo()
        {
            // 덮어쓰기를 끈 원본 C1(1100)이면 원·투 누적 위에서 날아감이 된다 — 620으로 낮춘 이유
            VoCombatSpec c = Spec();
            HitInfo original = Punch(c, 3);
            original.Impact = VoCombatSpec.Original().impactC1;
            Assert.AreEqual(Reaction.Blown, HitRules.Decide(c, original, new DefenderPosture(), 480f, false).Reaction);
        }

        [Test]
        public void StrongHit_BlowsAway_EvenThroughGuard()
        {
            VoCombatSpec c = Spec();
            HitInfo lunge = new HitInfo { Type = AttackType.Lunge, Kind = HitKind.Strong, Damage = 15f, Impact = c.impactStrong, Direction = Vector3.forward };
            // 가드가 공격자(−z 쪽)를 보고 있다
            var guarding = new DefenderPosture { Guarding = true, FacingYaw = 180f };
            HitDecision d = HitRules.Decide(c, lunge, guarding, 0f, false);
            Assert.AreEqual(Reaction.Blown, d.Reaction);
            Assert.AreEqual(HitResult.GuardBroken, d.Result);
            Assert.AreEqual(15f * c.guardDamageScale, d.Damage, 0.0001f, "강타도 가드 중이면 피해는 6%");
        }

        [Test]
        public void JumpingAndDashing_UseTheirOwnRows()
        {
            VoCombatSpec c = Spec();
            Assert.AreEqual(Reaction.InvulnOnly, HitRules.Decide(c, Punch(c, 1), new DefenderPosture { Airborne = true }, 0f, false).Reaction, "점프 중 경직급 = 반응 없이 무적");
            Assert.AreEqual(Reaction.AirDown, HitRules.Decide(c, Punch(c, 3), new DefenderPosture { Airborne = true }, 0f, false).Reaction, "점프 중 다운급 = 공중 다운");
            Assert.AreEqual(Reaction.InvulnOnly, HitRules.Decide(c, Punch(c, 1), new DefenderPosture { Dashing = true }, 0f, false).Reaction, "지상 대시 중 경직급 = 슈퍼아머");
            Assert.AreEqual(Reaction.Down, HitRules.Decide(c, Punch(c, 3), new DefenderPosture { Dashing = true }, 0f, false).Reaction, "지상 대시 중 다운급 = 다운");
            Assert.AreEqual(HitResult.Invulnerable, HitRules.Decide(c, Punch(c, 3), new DefenderPosture { MeleeInvulnerable = true }, 0f, false).Result, "근접 무적이면 통째로 무시");
        }

        // ---------------- 가드 중 충격치 10% 누적 ----------------

        [Test]
        public void Guard_TakesSixPercentDamage_AndTenPercentImpact()
        {
            VoCombatSpec c = Spec();
            var guarding = new DefenderPosture { Guarding = true, FacingYaw = 180f };
            HitDecision d = HitRules.Decide(c, Punch(c, 3), guarding, 0f, false);
            Assert.AreEqual(HitResult.Guarded, d.Result);
            Assert.AreEqual(Reaction.GuardStagger, d.Reaction);
            Assert.AreEqual(c.impactC1 * 0.10f, d.ImpactAdded, 0.0001f);
            Assert.AreEqual(25f * 0.06f, d.Damage, 0.0001f);
            Assert.IsTrue(d.StopsAttacker, "가드됨이면 때린 쪽도 히트스톱");

            // 판정값이 날아감 문턱 이상이면 가드가 깨진다
            HitDecision broken = HitRules.Decide(c, Punch(c, 3), guarding, c.blownThreshold - 50f, false);
            Assert.AreEqual(HitResult.GuardBroken, broken.Result);
            Assert.AreEqual(Reaction.Blown, broken.Reaction);
        }

        [Test]
        public void Guard_DoesNotCoverAttacksFromBehind()
        {
            VoCombatSpec c = Spec();
            // 공격자는 −z 쪽. 정면이 +z(yaw 0)면 등 뒤에서 맞는 것
            var back = new DefenderPosture { Guarding = true, FacingYaw = 0f };
            Assert.AreEqual(HitResult.Hit, HitRules.Decide(c, Punch(c, 1), back, 0f, false).Result);
            // 정면에서 100° 벗어난 공격자는 아직 막는다(±112.5°), 120°는 못 막는다
            Assert.IsTrue(HitRules.GuardCovers(c, 180f - 100f, Vector3.forward));
            Assert.IsFalse(HitRules.GuardCovers(c, 180f - 120f, Vector3.forward));
        }

        // ---------------- 경직 중 입력 무시 ----------------

        [Test]
        public void Stagger_IgnoresInput_For28Ticks()
        {
            VoCombatSpec c = Spec();
            var react = new ReactionTimers();
            react.BeginPush(c.staggerFrames, c.staggerSpeed, Vector3.forward, guard: false);
            int locked = 0;
            for (int i = 0; i < 100 && !react.AcceptsInput; i++)
            {
                Assert.IsTrue(react.IsStaggered);
                react.TickInvuln();
                react.TickPush(1f);
                react.EndTick();
                locked++;
            }
            Assert.AreEqual(c.staggerFrames, locked, "경직은 28틱 동안 입력을 받지 않는다");

            // 그동안 누른 공격은 버퍼에도 남지 않는다: 버퍼는 경직 중 매 틱 비운다 (WeaponSystem과 같은 규칙)
            var buffer = new AttackInputBuffer();
            buffer.Press();
            buffer.Clear();
            Assert.IsFalse(buffer.Tick(c.meleeBufferFrames, () => AttackType.Punch, out _, out _));
        }

        [Test]
        public void AttackBuffer_KeepsPressFor4Ticks()
        {
            VoCombatSpec c = Spec();
            var buffer = new AttackInputBuffer();
            buffer.Press();
            // 4틱 동안은 못 나가다가 5번째 시도에 나간다 → 버퍼로 발동
            for (int i = 0; i < c.meleeBufferFrames; i++) Assert.IsFalse(buffer.Tick(c.meleeBufferFrames, () => null, out _, out _));
            Assert.IsTrue(buffer.Tick(c.meleeBufferFrames, () => AttackType.Punch, out AttackType started, out bool fromBuffer));
            Assert.AreEqual(AttackType.Punch, started);
            Assert.IsTrue(fromBuffer);

            buffer.Press();
            for (int i = 0; i <= c.meleeBufferFrames; i++) buffer.Tick(c.meleeBufferFrames, () => null, out _, out _);
            Assert.IsFalse(buffer.Tick(c.meleeBufferFrames, () => AttackType.Punch, out _, out _), "창이 지나면 버려진다");
        }

        // ---------------- 히트스톱 15f 동안 수평 이동 0, 대시 공격은 때린 쪽이 안 멈춤 ----------------

        [Test]
        public void HitStop_FreezesPushFor15Ticks_ThenStaggerRunsFullLength()
        {
            VoCombatSpec c = Spec();
            var react = new ReactionTimers();
            react.BeginHitStop(c.hitStopFrames);
            react.GrantInvuln(c.hitInvulnFrames);
            react.BeginPush(c.staggerFrames, c.staggerSpeed, Vector3.forward, guard: false);

            float moved = 0f;
            int frozen = 0;
            while (react.IsHitStopped)
            {
                react.TickInvuln();
                moved += react.TickPush(0.78f);
                react.EndTick();
                frozen++;
            }
            Assert.AreEqual(c.hitStopFrames, frozen);
            Assert.AreEqual(0f, moved, "히트스톱 동안 수평 이동 0");
            Assert.AreEqual(c.staggerFrames, react.PushFrames, "히트스톱 동안 경직 시간은 흐르지 않는다");
            Assert.IsFalse(react.IsMeleeInvulnerable, "무적 타이머(4f)는 히트스톱 중에도 준다");

            int ticks = 0;
            while (react.IsPushed)
            {
                moved += react.TickPush(0.78f);
                react.EndTick();
                ticks++;
            }
            Assert.AreEqual(c.staggerFrames, ticks);
            // 마지막 틱은 멈추는 틱이라 27틱 동안 1.301 × 0.78씩
            Assert.AreEqual((c.staggerFrames - 1) * c.staggerSpeed * 0.78f, moved, 0.001f);
        }

        [Test]
        public void DashAttackAndJumpSlash_DoNotStopTheAttacker()
        {
            VoCombatSpec c = Spec();
            var posture = new DefenderPosture();
            HitInfo dash = new HitInfo { Type = AttackType.DashAttack, Kind = HitKind.Melee2, Damage = 20f, Impact = c.impactDashMelee, Direction = Vector3.forward };
            HitInfo slash = new HitInfo { Type = AttackType.JumpSlash, Kind = HitKind.Melee3, Damage = 30f, Impact = c.impactJumpMelee, Direction = Vector3.forward };
            Assert.IsFalse(HitRules.Decide(c, dash, posture, 0f, false).StopsAttacker, "대시 공격(근접 계열 2)");
            Assert.IsFalse(HitRules.Decide(c, slash, posture, 0f, false).StopsAttacker, "뛰어 베기(근접 계열 3)");
            Assert.IsTrue(HitRules.Decide(c, Punch(c, 1), posture, 0f, false).StopsAttacker, "펀치(일반 근접)");

            // 기록으로: 대시 공격을 맞힌 공격자에게는 히트스톱 표시가 안 선다, 펀치를 맞힌 공격자에게는 선다
            var attacker = new CombatLedger();
            var victim = new CombatLedger();
            dash.Source = attacker;
            victim.Add(dash);
            victim.Decide(c, posture);
            Assert.IsFalse(attacker.AttackerStopPending);
            victim.EndDecide();
            victim.ClearApplied();
            victim.Add(Punch(c, 1, attacker));
            victim.Decide(c, posture);
            Assert.IsTrue(attacker.AttackerStopPending);

            // 맞은 쪽이 이미 근접 무적이면 때린 쪽도 멈추지 않는다
            var attacker2 = new CombatLedger();
            var victim2 = new CombatLedger();
            victim2.Add(Punch(c, 1, attacker2));
            victim2.Decide(c, new DefenderPosture { MeleeInvulnerable = true });
            Assert.IsFalse(attacker2.AttackerStopPending);
        }

        // ---------------- 파고들기가 12.5u에서 멈추고 상대를 넘지 않음 ----------------

        /// <summary>상대가 정면 distance(u)에 서 있을 때 한 휘두르기를 끝까지 돌리고 남은 거리를 돌려준다.</summary>
        private static float RunApproach(VoCombatSpec c, VoMeleeMove move, float distance, out float minDistance)
        {
            VoSpec v = VoSpec.Original();
            var drive = new VoMeleeDrive();
            drive.Begin(move, 1, true, distance, c);
            minDistance = distance;
            for (int i = 0; i < 500 && drive.IsActive; i++)
            {
                float speed = drive.Tick(c, v, true, distance, 0f, false, out _);
                Assert.LessOrEqual(speed, distance + 0.0001f, "한 틱 이동량은 상대 거리를 넘지 않는다");
                distance -= speed;
                minDistance = Mathf.Min(minDistance, distance);
            }
            Assert.IsFalse(drive.IsActive);
            return distance;
        }

        [Test]
        public void Approach_StopsAtStopDistance_AndNeverPassesOpponent()
        {
            VoCombatSpec c = Spec();
            foreach (float start in new[] { 13f, 20f, 40f, 69f })
            {
                float end = RunApproach(c, c.a1First, start, out float min);
                Assert.GreaterOrEqual(min, 0f, "상대를 넘지 않는다");
                Assert.LessOrEqual(end, c.a1First.stopDistance + 0.0001f, $"{start}u에서 시작하면 정지 거리(12.5u) 안까지 붙는다");
                // 정지 거리 아래로는 마지막 한 틱의 이동량(최대 7.251 × 0.78)만큼만 더 들어간다
                Assert.Greater(end, c.a1First.stopDistance - c.a1First.baseSpeed * 0.78f - 0.0001f);
            }
            // 쓰리(C1)는 정지 거리 15u
            float third = RunApproach(c, c.c1, 50f, out _);
            Assert.LessOrEqual(third, c.c1.stopDistance + 0.0001f);
            Assert.Greater(third, c.c1.stopDistance - c.c1.baseSpeed * 0.78f - 0.0001f);
        }

        [Test]
        public void Approach_DoesNotMove_WhenAlreadyInsideStopDistance_OrTooFar()
        {
            VoCombatSpec c = Spec();
            Assert.AreEqual(10f, RunApproach(c, c.a1First, 10f, out _), 0.0001f, "정지 거리 안이면 파고들지 않는다");
            Assert.AreEqual(150f, RunApproach(c, c.a1First, 150f, out _), 0.0001f, "시작 때 100u 밖이면 추적 카운터가 없어 수평 이동이 없다");
        }

        [Test]
        public void Combo_QueuedDuringAttackPhase_SkipsRecovery()
        {
            VoCombatSpec c = Spec();
            VoSpec v = VoSpec.Original();
            var drive = new VoMeleeDrive();
            drive.Begin(c.a1First, 1, true, 10f, c);
            Assert.IsFalse(drive.Queue(c.a1Second), "① 파고들기 중엔 예약이 안 된다");
            int ticks = 0;
            while (drive.Phase == VoMeleeDrive.DrivePhase.Approach)
            {
                Assert.IsTrue(drive.CanCancel, "① 전체는 점프·대시 취소 가능");
                drive.Tick(c, v, true, 10f, 0f, false, out _);
                ticks++;
            }
            Assert.AreEqual(c.a1First.approachFrames, ticks);
            Assert.IsTrue(drive.Queue(c.a1Second));
            int hitTicks = 0;
            for (int i = 0; i < c.a1First.attackFrames; i++)
            {
                Assert.AreEqual(i < c.a1First.hitStart, drive.CanCancel, "② 판정 창 시작부터는 잠긴다");
                drive.Tick(c, v, true, 10f, 0f, false, out _);
                if (drive.HitActiveThisTick) hitTicks++;
            }
            Assert.AreEqual(c.a1First.hitEnd - c.a1First.hitStart, hitTicks, "판정 창 3~8f = 6틱");
            Assert.AreEqual(2, drive.Step, "② 끝에서 ③을 건너뛰고 다음 타로");
            Assert.AreEqual(VoMeleeDrive.DrivePhase.Approach, drive.Phase);
        }

        // ---------------- 같은 틱 맞교환이 1P·2P 대칭 ----------------

        private static void Exchange(bool firstDecidesFirst, out HitDecision toP1, out HitDecision toP2)
        {
            VoCombatSpec c = Spec();
            var p1 = new CombatLedger();
            var p2 = new CombatLedger();
            // 같은 틱 끝에 서로의 펀치가 맞았다
            HitInfo fromP1 = Punch(c, 1, p1);
            HitInfo fromP2 = Punch(c, 1, p2);
            fromP2.Direction = Vector3.back;
            p2.Add(fromP1);
            p1.Add(fromP2);
            var posture = new DefenderPosture();
            if (firstDecidesFirst)
            {
                p1.Decide(c, posture);
                p2.Decide(c, posture);
            }
            else
            {
                p2.Decide(c, posture);
                p1.Decide(c, posture);
            }
            p1.EndDecide();
            p2.EndDecide();
            toP1 = p1.Decisions[0];
            toP2 = p2.Decisions[0];
        }

        [Test]
        public void SameTickExchange_IsSymmetric_BothClash()
        {
            Exchange(true, out HitDecision a1, out HitDecision a2);
            Exchange(false, out HitDecision b1, out HitDecision b2);
            foreach (HitDecision d in new[] { a1, a2, b1, b2 })
            {
                Assert.AreEqual(HitResult.Clash, d.Result, "같은 틱에 서로의 펀치가 맞으면 맞딜");
                Assert.AreEqual(0f, d.Damage, "둘 다 피해 0");
                Assert.AreEqual(Reaction.None, d.Reaction);
            }
        }

        [Test]
        public void OneSidedHit_NextTick_IsNotAClash()
        {
            VoCombatSpec c = Spec();
            var p1 = new CombatLedger();
            var p2 = new CombatLedger();
            p2.Add(Punch(c, 1, p1));
            p1.Decide(c, new DefenderPosture());
            p2.Decide(c, new DefenderPosture());
            p1.EndDecide();
            p2.EndDecide();
            Assert.AreEqual(0, p1.Decisions.Count);
            Assert.AreEqual(HitResult.Hit, p2.Decisions[0].Result);
            // 다음 틱에 2P의 펀치가 맞아도 지난 틱 기록은 비워졌으므로 맞딜이 아니다
            p1.ClearApplied();
            p2.ClearApplied();
            p1.Add(Punch(c, 1, p2));
            p1.Decide(c, new DefenderPosture());
            Assert.AreEqual(HitResult.Hit, p1.Decisions[0].Result);
        }

        // ---------------- 30fps와 60fps 결과 같음 ----------------

        /// <summary>
        /// 화면 프레임률 fps로 seconds초 동안, VoTickDriver와 같은 방식(고정 1/60초 스텝 누적)으로 전투 틱을 돌린다.
        /// 전투 로직은 틱 수만 보므로 프레임을 어떻게 묶어도 결과가 같아야 한다.
        /// </summary>
        private static string Simulate(int fps, int totalTicks)
        {
            VoCombatSpec c = Spec();
            VoSpec v = VoSpec.Original();
            var drive = new VoMeleeDrive();
            var meter = new ImpactMeter();
            var react = new ReactionTimers();
            float distance = 60f;
            drive.Begin(c.a1First, 1, true, distance, c);
            meter.Add(c.impactA1First);
            react.BeginHitStop(c.hitStopFrames);
            react.BeginPush(c.staggerFrames, c.staggerSpeed, Vector3.forward, false);
            float pushed = 0f;
            bool queued = false;

            int ticksDone = 0;
            int frame = 0;
            // 프레임마다 "지금까지 흘렀어야 할 틱 수"만큼 돌린다 (30fps면 프레임당 2틱, 60fps면 1틱)
            while (ticksDone < totalTicks)
            {
                frame++;
                int due = Mathf.Min(totalTicks, frame * 60 / fps);
                for (; ticksDone < due; ticksDone++)
                {
                    meter.Tick(c.impactDecay);
                    react.TickInvuln();
                    pushed += react.TickPush(0.78f);
                    if (!react.IsHitStopped && drive.IsActive)
                    {
                        if (!queued && drive.CanQueue) queued = drive.Queue(c.a1Second);
                        distance -= drive.Tick(c, v, true, distance, 0f, false, out _);
                    }
                    react.EndTick();
                }
            }
            return $"{distance:R}|{pushed:R}|{meter.Value:R}|{drive.Step}|{drive.Phase}|{drive.Frame}|{react.PushFrames}";
        }

        [Test]
        public void ThirtyFpsAndSixtyFps_GiveTheSameResult()
        {
            foreach (int ticks in new[] { 10, 31, 60, 120 })
            {
                string at60 = Simulate(60, ticks);
                Assert.AreEqual(at60, Simulate(30, ticks), $"{ticks}틱 뒤 30fps");
                Assert.AreEqual(at60, Simulate(20, ticks), $"{ticks}틱 뒤 20fps");
                Assert.AreEqual(at60, Simulate(144, ticks), $"{ticks}틱 뒤 144fps");
            }
        }

        // ---------------- 피해 비율 = 원본 템진 ----------------

        [Test]
        public void Damage_KeepsOriginalRatios_ScaledSoOneIsTen()
        {
            VoCombatSpec c = Spec();
            Assert.AreEqual(10f, c.Damage(c.damageA1First), 0.0001f, "원 = 10 (체력 100 기준)");
            // 기술 사이의 비율은 원본 그대로 (A1 1타 290 기준)
            Assert.AreEqual(130f / 290f, c.Damage(c.damageA1Second) / c.Damage(c.damageA1First), 0.0001f, "투");
            Assert.AreEqual(380f / 290f, c.Damage(c.damageC1) / c.Damage(c.damageA1First), 0.0001f, "쓰리");
            Assert.AreEqual(340f / 290f, c.Damage(c.damageJumpMelee) / c.Damage(c.damageA1First), 0.0001f, "뛰어 베기");
            Assert.AreEqual(275f / 290f, c.Damage(c.damageDashMelee) / c.Damage(c.damageA1First), 0.0001f, "대시 공격");
            Assert.AreEqual(435f / 290f, c.Damage(c.damageA2) / c.Damage(c.damageA1First), 0.0001f, "런지");
            Assert.AreEqual(460f / 290f, c.Damage(c.damageC2) / c.Damage(c.damageA1First), 0.0001f, "차징 태클");
            // 덮어쓰기를 끄면 원본 스케일
            Assert.AreEqual(290f, VoCombatSpec.Original().Damage(VoCombatSpec.Original().damageA1First), 0.0001f);
        }

        // ---------------- 근접 구역·명중 기하 ----------------

        [Test]
        public void MeleeZone_FollowsSpecOrder()
        {
            VoCombatSpec c = VoCombatSpec.Original();
            Assert.IsTrue(VoMeleeZone.Evaluate(c, 10f, 180f, 0f, false, out bool inner) && inner, "안쪽 원(12.5u)은 각도와 무관");
            Assert.IsTrue(VoMeleeZone.Evaluate(c, 60f, 0f, 0f, false, out inner) && !inner, "정면 60u");
            Assert.IsFalse(VoMeleeZone.Evaluate(c, 70f, 0f, 0f, false, out _), "바깥 거리 70u는 '미만'");
            // 꼭짓점이 16.8u 뒤에 있어서 60u 거리의 실제 허용 각은 약 54° — 50°는 안, 60°는 밖
            Assert.IsTrue(VoMeleeZone.Evaluate(c, 60f, 50f, 0f, false, out _), "60u·50°는 원뿔 안");
            Assert.IsFalse(VoMeleeZone.Evaluate(c, 60f, 60f, 0f, false, out _), "60u·60°는 원뿔 밖");
            Assert.IsTrue(VoMeleeZone.Evaluate(c, 15f, 60f, 0f, false, out _), "가까우면 꼭짓점을 뒤로 물린 만큼 허용 각이 넓다");
            Assert.IsFalse(VoMeleeZone.Evaluate(c, 30f, 0f, 14f, false, out _), "내가 13u 넘게 위면 꺼진다");
            Assert.IsFalse(VoMeleeZone.Evaluate(c, 30f, 0f, -25f, false, out _), "상대가 24u 넘게 위면 꺼진다");
            Assert.IsTrue(VoMeleeZone.Evaluate(c, 30f, 0f, -25f, true, out _), "높이차 창 무시(뛰어 베기)");
        }

        [Test]
        public void SweptSphere_HitsCapsule_OnlyWhenPathCrossesIt()
        {
            Vector3 a = new Vector3(0f, 0.5f, 3f);
            Vector3 b = new Vector3(0f, 1.5f, 3f);
            // 구가 캡슐을 지나쳐 갔다(시작·끝 모두 안 닿지만 쓸고 간 길이 닿는다)
            Assert.IsTrue(HitGeometry.SweptSphereHitsCapsule(new Vector3(-3f, 1f, 3f), new Vector3(3f, 1f, 3f), 0.5f, a, b, 0.5f));
            Assert.IsFalse(HitGeometry.SweptSphereHitsCapsule(new Vector3(-3f, 1f, 1f), new Vector3(3f, 1f, 1f), 0.5f, a, b, 0.5f));
            Assert.IsFalse(HitGeometry.SweptSphereHitsCapsule(new Vector3(0f, 3.5f, 3f), new Vector3(0f, 3.5f, 3f), 0.5f, a, b, 0.5f), "머리 위로 지나가면 안 맞는다");
            // 점프 중 축소: 같은 구가 서 있는 캡슐엔 닿고 0.45배 캡슐엔 안 닿는다
            HitGeometry.UprightCapsule(new Vector3(0f, 0f, 3f), 0f, 2f, 0.5f, 1f, out Vector3 fa, out Vector3 fb, out float fr);
            HitGeometry.UprightCapsule(new Vector3(0f, 0f, 3f), 0f, 2f, 0.5f, 0.45f, out Vector3 sa, out Vector3 sb, out float sr);
            Vector3 low = new Vector3(0f, 0.2f, 2.2f);
            Assert.IsTrue(HitGeometry.SweptSphereHitsCapsule(low, low, 0.5f, fa, fb, fr));
            Assert.IsFalse(HitGeometry.SweptSphereHitsCapsule(low, low, 0.5f, sa, sb, sr));
        }

        // ---------------- 지연 표적 (이동 사양서 1.2 ②-1) ----------------

        /// <summary>
        /// 원·투의 추적 회전을 틱 단위로 흉내 낸다 (VoMotor 근접 추적 모드와 같은 계산: 지연 표적 갱신 → 표적 방위로 최대 trackRate 회전).
        /// 상대는 60u 앞에서 옆으로 걷는 중(이미 20f째 같은 행동). 돌린 각의 합(°)을 돌려준다.
        /// </summary>
        private static float TrackedDegrees(VoCombatSpec c, VoSpec v, VoMeleeMove move, bool delayed)
        {
            var target = new VoDelayedTarget();
            object walking = new object();
            // 옆 걷기 2.70u/f × 근접 감속 0.78 (u 단위로 계산 — 1u = 1)
            float lateral = v.walkSpeed.side * v.closeSlowScale;
            Vector3 opponent = new Vector3(0f, 0f, 60f);
            float yaw = 0f;
            for (int i = 0; i < 20; i++)
            {
                opponent.x += lateral;
                target.Update(opponent, walking, delayed, v.delayedTargetFrames, v.delayedTargetFollow);
            }
            // 펀치 시작 직전에 정면으로 맞춰 둔다 (둘 다 같은 출발점)
            yaw = Mathf.Atan2(opponent.x, opponent.z) * Mathf.Rad2Deg;
            float turned = 0f;
            float max = VoUnits.AngleUnitsToDegrees(move.trackRate);
            // 시작 틱은 돌지 않고, 다음 틱부터 trackTurnFrames 동안
            for (int frame = 1; frame <= c.trackTurnFrames + 1; frame++)
            {
                opponent.x += lateral;
                Vector3 t = target.Update(opponent, walking, delayed, v.delayedTargetFrames, v.delayedTargetFollow);
                if (frame == 1) continue;
                float bearing = Mathf.Atan2(t.x, t.z) * Mathf.Rad2Deg;
                float step = Mathf.Clamp(Mathf.DeltaAngle(yaw, bearing), -max, max);
                yaw += step;
                turned += step;
            }
            return turned;
        }

        [Test]
        public void DelayedTarget_PunchTracksSideWalkerLess()
        {
            VoCombatSpec c = Spec();
            VoSpec v = new VoSpec();
            Assert.IsTrue(v.delayedTargetEnabled, "원본 기본값은 지연 표적 켬");
            foreach (VoMeleeMove move in new[] { c.a1First, c.a1Second })
            {
                float without = TrackedDegrees(c, v, move, false);
                float with = TrackedDegrees(c, v, move, true);
                Assert.Greater(without, 0f, "옆으로 걷는 상대 쪽으로 돈다");
                Assert.Less(with, without - 0.5f, "지연 표적이 있으면 덜 따라간다");
            }
        }

        [Test]
        public void DelayedTarget_SnapsWhenActionChanges_FollowsHalfAfter15()
        {
            var target = new VoDelayedTarget();
            object a = new object();
            object b = new object();
            Vector3 p = Vector3.zero;
            // 처음 15틱(0~14)은 같은 행동이어도 바로 맞춘다
            for (int i = 0; i < 15; i++)
            {
                p.x = i;
                Assert.AreEqual(p.x, target.Update(p, a, true, 15, 0.5f).x, 1e-4f, "15f 전엔 상대 위치 그대로");
            }
            // 15틱째부터 반씩: (14 + 15) × 0.5
            p.x = 15f;
            Assert.AreEqual(14.5f, target.Update(p, a, true, 15, 0.5f).x, 1e-4f);
            Assert.IsTrue(target.IsLagging);
            // 행동이 바뀌면 바로 맞춘다
            p.x = 16f;
            Assert.AreEqual(16f, target.Update(p, b, true, 15, 0.5f).x, 1e-4f);
            Assert.IsFalse(target.IsLagging);
        }
    }
}
