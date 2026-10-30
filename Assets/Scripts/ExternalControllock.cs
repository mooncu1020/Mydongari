using UnityEngine;

namespace MyDongari.Core
{
  
    public class ExternalControlLock : MonoBehaviour
    {
        private int _lockCount = 0;
        public bool IsLocked => _lockCount > 0;

        public void Acquire() => _lockCount++;
        public void Release() => _lockCount = Mathf.Max(0, _lockCount - 1);

        /// <summary>라운드 리셋용. 짝이 안 맞은 Acquire가 남아 있어도 강제로 풀어 준다.</summary>
        public void ResetLock() => _lockCount = 0;
    }
}