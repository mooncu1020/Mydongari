using System.Collections.Generic;
using UnityEngine;
using MyDongari.Core.VirtualOn;

namespace MyDongari.Game.Combat
{
    /// <summary>패리로 공격자에게 되돌아가는 것 (피해·넉백). 공격자가 자기 틱의 정산 단계에서 받는다.</summary>
    public struct ReflectRecord
    {
        public float Damage;
        /// <summary>공격자가 밀려나는 방향 (수평).</summary>
        public Vector3 Direction;
        public HitInfo Info;
    }

    /// <summary>
    /// 기체 한 대의 피격 기록 (전투 사양서 4.1의 "피격 기록"). 순수 로직 — 틱 단위, Unity 시간과 무관.
    /// 한 틱 동안 맞은 공격이 Add로 쌓이고(반응 없음), 다음 틱 맨 앞에 Decide가 판단한다(규칙은 HitRules).
    /// 두 기체가 모두 "움직이기 전 상태"로 판단하므로 같은 틱 맞교환이 1P·2P 대칭이다:
    /// 서로의 기록에 같은 묶음(펀치끼리, 대시 공격끼리)의 타격이 있으면 둘 다 맞딜(피해 0)이 된다.
    /// 공격자에게 돌아가는 것(패리 반사, 때린 쪽 히트스톱)도 판단할 때 공격자 기록에 적어 두어, 둘 다 같은 틱의 자기 정산에서 받는다.
    /// </summary>
    public class CombatLedger
    {
        private readonly List<HitInfo> _incoming = new List<HitInfo>();
        private readonly List<HitDecision> _decisions = new List<HitDecision>();
        private readonly List<ReflectRecord> _reflects = new List<ReflectRecord>();

        /// <summary>이번 틱에 쌓인(아직 판단 전) 기록 수.</summary>
        public int IncomingCount => _incoming.Count;
        /// <summary>판단이 끝나 적용을 기다리는 피격.</summary>
        public IReadOnlyList<HitDecision> Decisions => _decisions;
        /// <summary>내가 때린 공격이 패리돼 되돌아온 것.</summary>
        public IReadOnlyList<ReflectRecord> Reflects => _reflects;
        /// <summary>내가 때린 공격이 맞아서(또는 막혀서) 나도 히트스톱이 걸려야 하는지. 정산 때 읽고 지운다.</summary>
        public bool AttackerStopPending { get; private set; }

        /// <summary>피격 기록에 쌓는다 (틱 중 아무 때나 — 판단은 다음 틱 맨 앞).</summary>
        public void Add(HitInfo info)
        {
            _incoming.Add(info);
        }

        /// <summary>source가 때린, 같은 맞딜 묶음의 기록이 아직 판단 전으로 쌓여 있는지.</summary>
        public bool HasIncomingFrom(CombatLedger source, int clashFamily)
        {
            if (source == null || clashFamily == 0) return false;
            for (int i = 0; i < _incoming.Count; i++)
            {
                if (_incoming[i].Source == source && _incoming[i].ClashFamily == clashFamily) return true;
            }
            return false;
        }

        /// <summary>
        /// 쌓인 기록을 판단한다. 쓰기는 내 판단 목록과 공격자의 반사·히트스톱 표시에만 하므로, 두 기체가 어느 순서로 불러도 결과가 같다
        /// (맞딜은 상대의 "판단 전 기록"을 읽기만 한다 — 그래서 모든 기체의 Decide가 끝난 뒤 EndDecide로 비운다).
        /// 같은 틱 여러 발은 충격치를 합산하며 차례로 판단한다.
        /// </summary>
        public void Decide(VoCombatSpec c, DefenderPosture posture)
        {
            float impact = posture.Impact;
            for (int i = 0; i < _incoming.Count; i++)
            {
                HitInfo info = _incoming[i];
                bool clash = info.Source != null && info.Source.HasIncomingFrom(this, info.ClashFamily);
                HitDecision decision = HitRules.Decide(c, info, posture, impact, clash);
                impact += decision.ImpactAdded;
                if (decision.Result == HitResult.Parried && info.ReflectsOnParry && info.Source != null)
                {
                    info.Source._reflects.Add(new ReflectRecord { Damage = info.Damage, Direction = -info.Direction, Info = info });
                }
                if (decision.StopsAttacker && info.Source != null) info.Source.AttackerStopPending = true;
                _decisions.Add(decision);
            }
        }

        /// <summary>모든 기체의 Decide가 끝난 뒤: 판단한 기록을 비운다.</summary>
        public void EndDecide()
        {
            _incoming.Clear();
        }

        /// <summary>적용이 끝난 판단·반사·히트스톱 표시를 비운다.</summary>
        public void ClearApplied()
        {
            _decisions.Clear();
            _reflects.Clear();
            AttackerStopPending = false;
        }

        public void Reset()
        {
            _incoming.Clear();
            ClearApplied();
        }
    }
}
