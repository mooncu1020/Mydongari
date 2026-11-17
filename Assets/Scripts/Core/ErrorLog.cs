using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MyDongari.Core
{
    /// <summary>
    /// 로케테스트 빌드용 예외 기록. 로그·피드백·외형 코드는 전투 이벤트를 구독하므로, 거기서 예외가 나면
    /// 이벤트를 부른 전투 코드(ReceiveHit 등)까지 끊긴다. 그래서 구독 쪽은 try/catch로 감싸 Report로 넘기고,
    /// 처리 안 된 예외(Unity 로그의 Exception)도 같은 파일 `Logs/PlayLogs/errors.txt`에 남긴다.
    /// 같은 예외가 매 프레임 반복돼도 파일이 커지지 않게 1, 2, 4, 8 … 번째만 쓴다.
    /// </summary>
    public static class ErrorLog
    {
        private const string FileName = "errors.txt";

        private static string _folder;
        private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
        private static bool _hooked;
        // 우리가 쓴 Debug.LogException이 다시 logMessageReceived로 들어와 두 번 기록되지 않게
        private static bool _reporting;

        /// <summary>로그 폴더(PlayLogger와 같은 곳). 정하기 전엔 실행 위치 기준 Logs/PlayLogs.</summary>
        public static string Folder
        {
            get
            {
                if (string.IsNullOrEmpty(_folder))
                {
                    DirectoryInfo parent = Directory.GetParent(Application.dataPath);
                    string root = parent != null ? parent.FullName : Application.persistentDataPath;
                    _folder = Path.Combine(root, "Logs", "PlayLogs");
                }
                return _folder;
            }
            set => _folder = value;
        }

        /// <summary>처리 안 된 예외도 기록하도록 Unity 로그를 구독한다. 여러 번 불러도 한 번만.</summary>
        public static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            Application.logMessageReceived += OnLogMessage;
        }

        /// <summary>잡은 예외를 기록하고 게임은 계속 돈다. where = "클래스.메서드".</summary>
        public static void Report(Exception e, string where)
        {
            if (e == null) return;
            Write(where, e.GetType().Name + ": " + e.Message, e.StackTrace);
#if UNITY_EDITOR
            // 에디터에선 Console에서도 바로 보이게 (파일에 두 번 쓰지 않도록 표시)
            _reporting = true;
            try { Debug.LogException(e); }
            finally { _reporting = false; }
#endif
        }

        /// <summary>action을 실행하고, 예외가 나면 기록만 하고 넘어간다.</summary>
        public static void Run(Action action, string where)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Report(e, where);
            }
        }

        private static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (_reporting) return;
            if (type != LogType.Exception && type != LogType.Assert) return;
            Write("unhandled", condition, stackTrace);
        }

        private static void Write(string where, string message, string stackTrace)
        {
            string key = where + "|" + message;
            Counts.TryGetValue(key, out int count);
            count++;
            Counts[key] = count;
            // 1, 2, 4, 8 … 번째만 남긴다 (같은 예외가 매 프레임 나도 파일이 폭주하지 않게)
            if ((count & (count - 1)) != 0) return;

            try
            {
                Directory.CreateDirectory(Folder);
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ({count}회째) {where}: {message}\n{stackTrace}\n";
                File.AppendAllText(Path.Combine(Folder, FileName), line);
            }
            catch (Exception)
            {
                // 기록 실패(권한 등)로 게임을 멈추지 않는다
            }
        }
    }
}
