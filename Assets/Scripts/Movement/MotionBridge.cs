using UnityEngine;
using MyDongari.Movement.V8;

namespace MyDongari.Movement
{
    /// <summary>
    /// 외부 속도 통로 (V8). 전투 코드(넉백·넉다운·런지·앵커 클로·몸 충돌·리스폰)가 기체를 밀 때 리지드바디 대신 이걸 부른다.
    /// V8 이동기가 켜진 기체(MechMotor 활성, 리지드바디 키네마틱)면 이동기의 외부 속도로 넣고,
    /// 아니면(예전 이동기·연습 봇) 예전처럼 리지드바디에 그대로 쓴다. 그래서 전투 코드는 이동기를 몰라도 된다.
    /// </summary>
    public static class MotionBridge
    {
        private static MechMotor ActiveMotor(Rigidbody rb)
        {
            if (rb == null) return null;
            MechMotor motor = MechMotor.Of(rb);
            return motor != null && motor.IsActive ? motor : null;
        }

        /// <summary>지금 속도(m/s). V8이면 이동기가 지난 틱에 실제로 움직인 속도.</summary>
        public static Vector3 GetVelocity(Rigidbody rb)
        {
            if (rb == null) return Vector3.zero;
            MechMotor motor = ActiveMotor(rb);
            return motor != null ? motor.Velocity : rb.linearVelocity;
        }

        /// <summary>속도를 통째로 지정 (넉다운 비행, 앵커 클로 끌어오기 등). V8이면 외부 속도로.</summary>
        public static void SetVelocity(Rigidbody rb, Vector3 velocity)
        {
            if (rb == null) return;
            MechMotor motor = ActiveMotor(rb);
            if (motor != null) motor.SetExternalVelocity(velocity);
            else if (!rb.isKinematic) rb.linearVelocity = velocity;
        }

        /// <summary>수평 속도만 지정하고 세로 속도는 그대로.</summary>
        public static void SetHorizontalVelocity(Rigidbody rb, Vector3 horizontal)
        {
            if (rb == null) return;
            MechMotor motor = ActiveMotor(rb);
            if (motor != null)
            {
                motor.SetExternalHorizontal(horizontal);
                return;
            }
            if (!rb.isKinematic) rb.linearVelocity = new Vector3(horizontal.x, rb.linearVelocity.y, horizontal.z);
        }

        /// <summary>순간 충격(ForceMode.Impulse와 같은 뜻, 질량으로 나눈다). V8이면 외부 속도에 더한다.</summary>
        public static void AddImpulse(Rigidbody rb, Vector3 impulse)
        {
            if (rb == null) return;
            MechMotor motor = ActiveMotor(rb);
            if (motor != null) motor.AddExternalImpulse(impulse / Mathf.Max(0.0001f, rb.mass));
            else if (!rb.isKinematic) rb.AddForce(impulse, ForceMode.Impulse);
        }

        /// <summary>순간이동 (리스폰). V8이면 이동기 상태도 같이 맞춘다.</summary>
        public static void Teleport(Rigidbody rb, Vector3 position, Quaternion rotation)
        {
            if (rb == null) return;
            rb.position = position;
            rb.rotation = rotation;
            MechMotor motor = ActiveMotor(rb);
            if (motor != null) motor.Teleport(position, rotation);
            else if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }
}
