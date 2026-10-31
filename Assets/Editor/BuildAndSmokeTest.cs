using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MyDongari.EditorTools
{
    /// <summary>
    /// 빌드 스모크 테스트: 열린 씬 저장 → Builds/날짜_시간 폴더에 빌드 → 실행(창 모드) → 몇 초 뒤 종료 →
    /// Player.log에서 Exception·Error·NaN 줄을 찾아 콘솔에 요약. 메뉴 Tools/Build And Smoke Test.
    /// 기다리는 동안 에디터가 멈추지 않게 EditorApplication.update로 진행한다. 로그는 빌드 폴더의 Player.log(-logFile로 지정).
    /// </summary>
    public static class BuildAndSmokeTest
    {
        private const string BuildsFolder = "Builds";
        private const float RunSeconds = 10f;
        // 종료 요청(창 닫기) 뒤 이 시간 안에 안 끝나면 강제 종료
        private const float CloseGraceSeconds = 3f;
        private const int MaxListedLines = 30;
        /// <summary>빌드 쪽(AppRuntime)이 이 인자를 보면 전체 화면을 강제하지 않는다.</summary>
        public const string SmokeTestArgument = "-smokeTest";

        private static Process _process;
        private static string _logPath;
        private static string _buildFolder;
        private static double _startTime;
        private static double _closeRequestTime = -1;

        [MenuItem("Tools/Build And Smoke Test")]
        private static void Run()
        {
            if (_process != null)
            {
                Debug.LogWarning("[SmokeTest] 이미 실행 중입니다.");
                return;
            }

            // 1) 씬 저장
            if (!EditorSceneManager.SaveOpenScenes())
            {
                Debug.LogError("[SmokeTest] 씬 저장 실패 — 중단");
                return;
            }

            if (!TryBuild(out string exePath) || !TryLaunch(exePath)) return;
            _startTime = EditorApplication.timeSinceStartup;
            _closeRequestTime = -1;
            Debug.Log($"[SmokeTest] 실행 — {RunSeconds:0}초 뒤 종료합니다");
            EditorApplication.update += Tick;
        }

        /// <summary>
        /// 명령줄(batchmode)용 같은 테스트: 빌드 → 실행 10초 → 종료 → Player.log 요약을 동기로 돌리고 에디터를 끝낸다.
        /// 씬 저장은 하지 않는다(batchmode는 열린 씬이 없고, 빌드는 Build Settings 씬 파일을 그대로 쓴다).
        /// 종료 코드: 0 = 로그 깨끗, 2 = Exception·Error·NaN 줄 있음, 1 = 빌드·실행 실패.
        /// 예: Unity.exe -batchmode -projectPath . -executeMethod MyDongari.EditorTools.BuildAndSmokeTest.RunFromCommandLine -logFile build.log
        /// </summary>
        public static void RunFromCommandLine()
        {
            int exitCode = 1;
            try
            {
                if (TryBuild(out string exePath) && TryLaunch(exePath))
                {
                    string how;
                    if (_process.WaitForExit((int)(RunSeconds * 1000f)))
                    {
                        how = $"10초 전에 종료됨(크래시 가능), 종료 코드 {SafeExitCode()}";
                    }
                    else
                    {
                        try
                        {
                            _process.CloseMainWindow();
                        }
                        catch
                        {
                            // 창이 없으면 아래 강제 종료로
                        }
                        if (_process.WaitForExit((int)(CloseGraceSeconds * 1000f)))
                        {
                            how = "정상 종료 요청 후 끝남";
                        }
                        else
                        {
                            _process.Kill();
                            _process.WaitForExit(2000);
                            how = "창 닫기에 응답이 없어 강제 종료";
                        }
                    }
                    _process.Dispose();
                    _process = null;
                    int matches = Summarize(how);
                    exitCode = matches == 0 ? 0 : matches > 0 ? 2 : 1;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                exitCode = 1;
            }
            EditorApplication.Exit(exitCode);
        }

        // 2) 빌드: Builds/날짜_시간/제품명.exe
        private static bool TryBuild(out string exePath)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _buildFolder = Path.GetFullPath(Path.Combine(BuildsFolder, stamp));
            Directory.CreateDirectory(_buildFolder);
            string exeName = SafeFileName(PlayerSettings.productName) + ".exe";
            exePath = Path.Combine(_buildFolder, exeName);
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) scenes = new[] { EditorSceneManager.GetActiveScene().path };

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[SmokeTest] 빌드 실패: {summary.result}, 오류 {summary.totalErrors}개 — {_buildFolder}");
                return false;
            }
            Debug.Log($"[SmokeTest] 빌드 성공 ({summary.totalTime.TotalSeconds:0}초, {summary.totalSize / (1024f * 1024f):0.0}MB): {exePath}");
            return true;
        }

        // 3) 실행: 로그 위치를 빌드 폴더로, 창 모드로
        private static bool TryLaunch(string exePath)
        {
            _logPath = Path.Combine(_buildFolder, "Player.log");
            var start = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"-logFile \"{_logPath}\" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 {SmokeTestArgument}",
                WorkingDirectory = _buildFolder,
                UseShellExecute = false
            };
            try
            {
                _process = Process.Start(start);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SmokeTest] 실행 실패: {e.Message}");
                _process = null;
                return false;
            }
            return _process != null;
        }

        private static void Tick()
        {
            if (_process == null)
            {
                EditorApplication.update -= Tick;
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            bool exited;
            try
            {
                exited = _process.HasExited;
            }
            catch
            {
                exited = true;
            }

            if (exited)
            {
                // 10초 전에 스스로 끝났으면 크래시일 수 있다
                bool early = _closeRequestTime < 0;
                Finish(early ? $"10초 전에 종료됨(크래시 가능), 종료 코드 {SafeExitCode()}" : "정상 종료 요청 후 끝남");
                return;
            }

            if (_closeRequestTime < 0 && now - _startTime >= RunSeconds)
            {
                _closeRequestTime = now;
                try
                {
                    _process.CloseMainWindow();
                }
                catch
                {
                    // 창이 없으면 아래 강제 종료로
                }
            }
            else if (_closeRequestTime >= 0 && now - _closeRequestTime >= CloseGraceSeconds)
            {
                try
                {
                    _process.Kill();
                    _process.WaitForExit(2000);
                }
                catch
                {
                    // 이미 끝났으면 무시
                }
                Finish("창 닫기에 응답이 없어 강제 종료");
            }
        }

        private static string SafeExitCode()
        {
            try
            {
                return _process.ExitCode.ToString();
            }
            catch
            {
                return "알 수 없음";
            }
        }

        private static void Finish(string how)
        {
            EditorApplication.update -= Tick;
            _process?.Dispose();
            _process = null;
            Summarize(how);
        }

        // 4) Player.log 요약
        private static readonly Regex Pattern = new Regex(@"exception|error|\bNaN\b", RegexOptions.IgnoreCase);

        /// <summary>요약을 콘솔과 SmokeTestSummary.txt에 남기고 걸린 줄 수를 돌려준다 (로그가 없으면 -1).</summary>
        private static int Summarize(string how)
        {
            if (!File.Exists(_logPath))
            {
                Debug.LogError($"[SmokeTest] Player.log가 없습니다: {_logPath} ({how})");
                return -1;
            }

            string[] lines;
            using (var stream = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                lines = reader.ReadToEnd().Split('\n');
            }

            int exceptions = 0, errors = 0, nans = 0;
            var matches = new List<(int line, string text)>();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                if (!Pattern.IsMatch(line)) continue;
                if (line.IndexOf("exception", StringComparison.OrdinalIgnoreCase) >= 0) exceptions++;
                if (line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0) errors++;
                if (Regex.IsMatch(line, @"\bNaN\b", RegexOptions.IgnoreCase)) nans++;
                matches.Add((i + 1, line.Trim()));
            }

            var text = new StringBuilder();
            text.AppendLine($"[SmokeTest] {how}. 로그 {lines.Length}줄 — Exception {exceptions}줄, Error {errors}줄, NaN {nans}줄");
            text.AppendLine($"로그: {_logPath}");
            // 같은 줄이 반복되면 한 번만, 처음 나온 줄 번호와 횟수로
            foreach (var group in matches.GroupBy(m => m.text).Take(MaxListedLines))
            {
                int count = group.Count();
                text.AppendLine($"  {group.First().line}행{(count > 1 ? $" (×{count})" : "")}: {Shorten(group.Key)}");
            }
            int distinct = matches.Select(m => m.text).Distinct().Count();
            if (distinct > MaxListedLines) text.AppendLine($"  … 다른 줄 {distinct - MaxListedLines}종류 더 (로그 파일 참고)");

            File.WriteAllText(Path.Combine(_buildFolder, "SmokeTestSummary.txt"), text.ToString());
            if (matches.Count == 0) Debug.Log(text.ToString());
            else Debug.LogWarning(text.ToString());
            return matches.Count;
        }

        private static string Shorten(string line) => line.Length > 200 ? line.Substring(0, 200) + "…" : line;

        private static string SafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "Game" : name;
        }
    }
}
