using UnityEngine;

namespace MyDongari.Core
{
  
    public class ExternalControlLock : MonoBehaviour
    {
        private int _lockCount = 0;
        public bool IsLocked => _lockCount > 0;

        public void Acquire() => _lockCount++;
        public void Release() => _lockCount = Mathf.Max(0, _lockCount - 1);
    }
}