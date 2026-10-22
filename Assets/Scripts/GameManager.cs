using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using MyDongari.Combat;
using MyDongari.Combat.Skills;
using MyDongari.Mech;
using MyDongari.Movement;
using MyDongari.Input;
using MyDongari.Feedback;
using MyDongari.UI;

namespace MyDongari.Core
{
    /// <summary>
    /// 매치 진행 단계. Title(모드 선택) → Countdown(입력 잠금) → Fight → RoundOver → 다음 라운드 Countdown … → MatchOver.
    /// 새 값은 뒤에 붙인다 (직렬화·로그 호환).
    /// </summary>
    public enum MatchPhase
    {
        Countdown,
        Fight,
        RoundOver,
        MatchOver,
        Title
    }

    /// <summary>VERSUS = 2인 화면 분할 3판 2선승, PRACTICE = P1 vs 연습 봇 한 화면 무제한.</summary>
    public enum GameMode
    {
        Versus,
        Practice
    }

    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        /// <summary>매치(전체 승부)가 끝났을 때 발생. 인자는 승리한 플레이어 번호(1 또는 2).
        /// HUD/게임오버 화면 등은 여기 구독해서 처리하면 됨.</summary>
        public event Action<int> OnMatchOver;

        /// <summary>라운드가 끝났을 때 발생. 인자는 라운드 승자(1 또는 2), 무승부면 0. 연습 모드에선 KO마다.</summary>
        public event Action<int> OnRoundOver;

        /// <summary>라운드 리셋 직후(카운트다운 시작) 발생. 인자는 라운드 번호(1부터, 무승부 재경기도 1 증가).</summary>
        public event Action<int> OnRoundStarted;

        /// <summary>카운트다운이 끝나고 입력이 풀리는 순간("FIGHT").</summary>
        public event Action OnFight;

        /// <summary>재매치로 새 매치가 시작될 때 (첫 매치 포함). 모드는 Mode로.</summary>
        public event Action OnMatchStarted;

        /// <summary>타이틀로 돌아갔을 때 (매치 도중이든 끝난 뒤든).</summary>
        public event Action OnReturnedToTitle;

        public bool IsMatchOver { get; private set; }
        public int MatchWinner { get; private set; }

        [Header("모드")]
        [Tooltip("켜면 실행 시 타이틀(VERSUS / PRACTICE 선택)부터. 끄면 아래 Versus Mode 값으로 바로 시작(테스트용)")]
        [SerializeField] private bool showTitleOnStart = true;
        [Tooltip("타이틀을 끈 경우의 시작 모드: 켜면 2인 대전(P1 vs P2), 끄면 P1 vs 연습 봇")]
        [SerializeField] private bool versusMode = true;

        [Header("연습(봇) 모드")]
        [Tooltip("연습 모드 상대(연습 봇). 2인 모드에선 꺼 둔다")]
        [SerializeField] private GameObject dummyOpponent;
        [Tooltip("2인 모드에서만 쓰는 오브젝트(P2 기체, P2 HUD 캔버스 등). 연습 모드에서 끈다")]
        [SerializeField] private GameObject[] versusOnlyObjects;
        [Tooltip("연습 모드에서 화면 전체를 쓰게 할 P1 카메라")]
        [SerializeField] private UnityEngine.Camera player1Camera;
        [Tooltip("연습 모드에서 한쪽이 쓰러진 뒤 두 기체를 되살릴 때까지(초). 카운트다운 없이 바로 이어진다")]
        [SerializeField] private float practiceRespawnDelay = 1f;

        [Header("무입력 타이틀 복귀 (로케 전시용)")]
        [Tooltip("켜면 아무 플레이어도 입력하지 않은 채 아래 시간이 지나면 타이틀로 돌아간다 (타이틀 화면 제외)")]
        [SerializeField] private bool idleReturnToTitle = false;
        [Tooltip("무입력 대기 시간(초, 실제 시간)")]
        [SerializeField] private float idleTimeout = 120f;

        [Header("라운드 설정")]
        [SerializeField] private int roundsToWin = 2;

        [Header("라운드 연출")]
        [Tooltip("라운드 시작 카운트다운(초). 이 동안 두 기체 모두 입력을 받지 않는다")]
        [SerializeField] private float countdownDuration = 3f;
        [Tooltip("FIGHT 글자를 띄워 두는 시간(초). 입력은 FIGHT와 동시에 풀린다")]
        [SerializeField] private float fightBannerDuration = 1f;

        [Header("리스폰 설정")]
        [Tooltip("라운드가 끝나고 다음 라운드 카운트다운이 시작될 때까지(초)")]
        [SerializeField] private float respawnDelay = 3f;
        [SerializeField] private Transform player1SpawnPoint;
        [SerializeField] private Transform player2SpawnPoint;

        [Header("참조")]
        [SerializeField] private GameObject player1;
        [Tooltip("2인 모드의 P2 기체. 현재 상대(연습 모드면 봇)는 코드에서 Player2로 바뀐다")]
        [SerializeField] private GameObject player2;

        private int _player1Wins = 0;
        private int _player2Wins = 0;
        private bool _roundActive = false;
        public bool IsRoundActive => _roundActive;

        public MatchPhase Phase { get; private set; } = MatchPhase.Countdown;
        public GameMode Mode { get; private set; } = GameMode.Versus;
        public int RoundNumber { get; private set; }
        /// <summary>직전 라운드 승자. 0이면 무승부.</summary>
        public int LastRoundWinner { get; private set; }
        /// <summary>현재 단계(카운트다운·라운드 종료 대기)의 남은 시간(초).</summary>
        public float PhaseTimeRemaining => _phaseTimer;
        public bool ShowFightBanner => Phase == MatchPhase.Fight && _fightBannerTimer > 0f;

        private float _phaseTimer = 0f;
        private float _fightBannerTimer = 0f;
        private float _lastInputTime;

        public int RoundsToWin => roundsToWin;
        public int Player1Wins => _player1Wins;
        public int Player2Wins => _player2Wins;
        public bool VersusMode => Mode == GameMode.Versus;
        /// <summary>현재 대결 중인 기체. 연습 모드면 Player2는 연습 봇.</summary>
        public GameObject Player1 => player1;
        public GameObject Player2 => player2;

        // 인스펙터의 player2(2인 모드 P2). 모드를 오갈 때 player2를 이것과 봇 사이에서 바꾼다
        private GameObject _versusPlayer2;
        private Rect _player1VersusRect = new Rect(0f, 0f, 1f, 1f);
        private VersusInputPairing _pairing;

        /// <summary>2인 구성(입력이 있는 P2 기체)이 있는지. Setup Versus Scene 전 씬이면 false.</summary>
        public bool CanStartVersus => _versusPlayer2 != null && _versusPlayer2.GetComponent<InputReader>() != null;
        /// <summary>연습 상대(봇): Dummy Opponent, 없으면 입력이 없는 player2(Versus 세팅 전 씬의 DummyTarget).</summary>
        public GameObject PracticeOpponent =>
            dummyOpponent != null ? dummyOpponent :
            _versusPlayer2 != null && _versusPlayer2.GetComponent<InputReader>() == null ? _versusPlayer2 : null;
        public bool CanStartPractice => PracticeOpponent != null;
        /// <summary>연습 봇 두뇌 (없으면 null).</summary>
        public MeleeDummy PracticeBot => PracticeOpponent != null ? PracticeOpponent.GetComponent<MeleeDummy>() : null;

        /// <summary>모드와 상관없이 이 씬의 모든 대전 기체(P1, 2인용 P2, 연습 봇). 꺼진 기체는 이벤트가 안 나므로 구독해 둬도 안전.</summary>
        public IEnumerable<GameObject> AllMechs
        {
            get
            {
                var mechs = new List<GameObject>();
                foreach (GameObject mech in new[] { player1, _versusPlayer2, PracticeOpponent })
                {
                    if (mech != null && !mechs.Contains(mech)) mechs.Add(mech);
                }
                return mechs;
            }
        }

        // ---------------- 메뉴 ----------------

        private enum MenuKind { None, Title, MatchOver, PracticePause }

        private const string OptionVersus = "VERSUS";
        private const string OptionPractice = "PRACTICE";
        private const string OptionRematch = "REMATCH";
        private const string OptionTitle = "TITLE";
        private const string OptionContinue = "CONTINUE";

        private MenuKind _menu = MenuKind.None;
        private int _menuOpenedFrame;
        /// <summary>지금 열린 메뉴의 선택지. 메뉴가 없으면 null.</summary>
        public string[] MenuOptions { get; private set; }
        public int MenuIndex { get; private set; }
        public bool IsMenuOpen => MenuOptions != null;
        public bool IsPracticePaused => _menu == MenuKind.PracticePause;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            // 빌드에서 처리 안 된 예외도 Logs/PlayLogs/errors.txt에 남긴다
            ErrorLog.Hook();

            _versusPlayer2 = player2;
            if (player1Camera != null) _player1VersusRect = player1Camera.rect;
            _pairing = GetComponent<VersusInputPairing>();

            // 첫 배치. 타이틀이면 두 사람이 다 메뉴를 조작할 수 있게 2인 배치를 깔아 둔다
            ApplyLayout(StartLayout());

            // 씬에 안 붙여 둔 경우에도 히트스톱이 동작하게 (수치 조정은 인스펙터에서 직접 추가해서)
            if (GetComponent<HitStop>() == null) gameObject.AddComponent<HitStop>();
            // 상태 피드백(팝업·틴트·효과음)도 같은 방식. 효과음 클립을 넣으려면 씬에 직접 붙여 둔다
            if (GetComponent<CombatFeedback>() == null) gameObject.AddComponent<CombatFeedback>();
            // 빌드 운영(ESC 길게 종료·커서·전체 화면). 에디터에선 기본으로 아무것도 안 한다
            if (GetComponent<AppRuntime>() == null) gameObject.AddComponent<AppRuntime>();
            // 타이틀·메뉴·연습 표시. 캔버스를 런타임에 스스로 만든다 (씬 편집 불필요)
            if (GetComponent<MenuUI>() == null) gameObject.AddComponent<MenuUI>();
            // 연습 모드 단축키 (봇 모드 순환·리셋·히트 프리셋)
            if (GetComponent<PracticeHotkeys>() == null) gameObject.AddComponent<PracticeHotkeys>();
        }

        private GameMode StartLayout()
        {
            if (showTitleOnStart) return CanStartVersus ? GameMode.Versus : GameMode.Practice;
            if (versusMode && CanStartVersus) return GameMode.Versus;
            return CanStartPractice ? GameMode.Practice : GameMode.Versus;
        }

        /// <summary>
        /// 화면·기체 배치를 모드에 맞춘다. VERSUS: P2 기체·P2 HUD 켬, 봇 끔, P1 카메라 왼쪽 절반.
        /// PRACTICE: P2 쪽 끔, 봇 켬, P1 카메라 전체. 상대 연결도 다시 한다. 장치 배정은 ApplyPairing.
        /// </summary>
        private void ApplyLayout(GameMode mode)
        {
            if (mode == GameMode.Versus && !CanStartVersus) mode = GameMode.Practice;
            if (mode == GameMode.Practice && !CanStartPractice) mode = GameMode.Versus;
            Mode = mode;
            bool versus = mode == GameMode.Versus;

            GameObject bot = PracticeOpponent;
            if (versusOnlyObjects != null)
            {
                foreach (GameObject go in versusOnlyObjects)
                {
                    if (go != null && go != bot) go.SetActive(versus);
                }
            }
            // Versus 세팅 전 씬은 봇이 곧 player2라 끄지 않는다
            if (bot != null && bot != _versusPlayer2) bot.SetActive(!versus);
            else if (bot != null) bot.SetActive(true);

            player2 = versus ? _versusPlayer2 : bot;
            if (player1Camera != null) player1Camera.rect = versus ? _player1VersusRect : new Rect(0f, 0f, 1f, 1f);
            LinkOpponents();
        }

        /// <summary>
        /// player1·player2를 서로의 상대로 연결한다. 전투 코드는 태그 대신 이 참조로 상대를 찾는다.
        /// 모드에 따라 상대가 바뀌므로 인스펙터 지정과 상관없이 매번 덮어쓴다.
        /// </summary>
        private void LinkOpponents()
        {
            if (player1 == null || player2 == null) return;

            MechIdentity id1 = MechIdentity.GetOrAdd(player1);
            MechIdentity id2 = MechIdentity.GetOrAdd(player2);
            id1.SetPlayerIndex(1);
            id2.SetPlayerIndex(2);
            id1.SetOpponent(id2);
            id2.SetOpponent(id1);
        }

        /// <summary>패드 배정을 현재 모드로. PlayerInput 사용자가 생긴 뒤(Start 이후)에 불러야 한다.</summary>
        private void ApplyPairing()
        {
            if (_pairing != null) _pairing.Apply(Mode == GameMode.Versus);
        }

        private void Start()
        {
            ApplyPairing();
            if (showTitleOnStart) EnterTitle();
            else StartMatch();
        }

        // ---------------- 모드 전환 ----------------

        /// <summary>타이틀로. 진행 중인 기동을 끊고 두 기체를 스폰 위치에 세운 뒤 모드 선택 메뉴를 연다.</summary>
        public void EnterTitle()
        {
            _roundActive = false;
            IsMatchOver = false;
            StopActiveMoves(player1);
            StopActiveMoves(player2);

            ApplyLayout(CanStartVersus ? GameMode.Versus : GameMode.Practice);
            ApplyPairing();
            ResetMech(player1, player1SpawnPoint);
            ResetMech(player2, player2SpawnPoint);
            SetPlayersInputLocked(true);

            Phase = MatchPhase.Title;
            var options = new List<string>();
            if (CanStartVersus) options.Add(OptionVersus);
            if (CanStartPractice) options.Add(OptionPractice);
            OpenMenu(MenuKind.Title, options.ToArray());

            OnReturnedToTitle?.Invoke();
        }

        /// <summary>모드를 정해 새 매치를 시작한다.</summary>
        public void StartMode(GameMode mode)
        {
            StopActiveMoves(player1);
            StopActiveMoves(player2);
            ApplyLayout(mode);
            ApplyPairing();
            CloseMenu();
            StartMatch();
        }

        /// <summary>승수·매치 상태를 비우고 1라운드부터. 재매치는 씬 리로드 없이 이걸로 한다.</summary>
        private void StartMatch()
        {
            _player1Wins = 0;
            _player2Wins = 0;
            IsMatchOver = false;
            MatchWinner = 0;
            LastRoundWinner = 0;
            RoundNumber = 0;
            OnMatchStarted?.Invoke();
            StartRound();
        }

        // ---------------- 진행 ----------------

        private void Update()
        {
            CheckIdle();

            switch (Phase)
            {
                case MatchPhase.Title:
                case MatchPhase.MatchOver:
                    HandleMenuInput();
                    break;

                case MatchPhase.Countdown:
                    _phaseTimer -= Time.deltaTime;
                    if (_phaseTimer <= 0f) BeginFight();
                    break;

                case MatchPhase.Fight:
                    if (_fightBannerTimer > 0f) _fightBannerTimer -= Time.deltaTime;
                    if (Mode == GameMode.Practice)
                    {
                        // 연습에서 나갈 길: Start → CONTINUE / TITLE
                        if (IsPracticePaused) HandleMenuInput();
                        else if (AnyStartPressed()) OpenPracticePause();
                        else CheckPracticeKO();
                    }
                    else
                    {
                        CheckRoundEnd();
                    }
                    break;

                case MatchPhase.RoundOver:
                    _phaseTimer -= Time.deltaTime;
                    if (_phaseTimer > 0f) break;
                    if (Mode == GameMode.Practice) RespawnPractice();
                    else StartRound();
                    break;
            }
        }

        /// <summary>무입력 타이틀 복귀: 타이틀이 아닌 곳에서 idleTimeout 동안 아무 입력이 없으면 타이틀로.</summary>
        private void CheckIdle()
        {
            float now = Time.unscaledTime;
            if (!idleReturnToTitle || Phase == MatchPhase.Title || AnyPlayerInput())
            {
                _lastInputTime = now;
                return;
            }
            if (now - _lastInputTime >= idleTimeout)
            {
                _lastInputTime = now;
                CloseMenu();
                EnterTitle();
            }
        }

        private bool AnyPlayerInput()
        {
            foreach (GameObject mech in new[] { player1, player2 })
            {
                if (mech == null || !mech.activeInHierarchy) continue;
                InputReader input = mech.GetComponent<InputReader>();
                if (input != null && input.AnyInputThisFrame) return true;
            }
            return false;
        }

        private void CheckRoundEnd()
        {
            bool p1Dead = IsDead(player1);
            bool p2Dead = IsDead(player2);

            if (p1Dead && p2Dead)
            {
                // 같은 프레임 동시 사망: 무승부, 승수 없이 다시
                RoundEnd(winner: 0);
            }
            else if (p1Dead)
            {
                RoundEnd(winner: 2);
            }
            else if (p2Dead)
            {
                RoundEnd(winner: 1);
            }
        }

        private static bool IsDead(GameObject mech)
        {
            Health health = mech != null ? mech.GetComponent<Health>() : null;
            return health != null && health.IsDead;
        }

        private void StartRound()
        {
            if (IsMatchOver) return;

            RoundNumber++;
            _roundActive = false;
            Phase = MatchPhase.Countdown;
            _phaseTimer = countdownDuration;
            _fightBannerTimer = 0f;

            // 앵커 클로는 상대 쪽 잠금을 잡고 있을 수 있고, 런지는 상태 전환 시 "빗나감"으로 끝나므로
            // 두 기체 모두 진행 중인 기동부터 조용히 끊은 뒤 나머지를 리셋한다
            StopActiveMoves(player1);
            StopActiveMoves(player2);
            ResetMech(player1, player1SpawnPoint);
            ResetMech(player2, player2SpawnPoint);
            SetPlayersInputLocked(true);

            OnRoundStarted?.Invoke(RoundNumber);
        }

        private void BeginFight()
        {
            Phase = MatchPhase.Fight;
            _roundActive = true;
            _fightBannerTimer = fightBannerDuration;
            SetPlayersInputLocked(false);
            OnFight?.Invoke();
        }

        // ---------------- 연습 모드 ----------------

        /// <summary>연습 모드: 쓰러지면 승수 없이 잠깐 KO를 보여 준 뒤 그대로 이어서 (라운드 제한 없음).</summary>
        private void CheckPracticeKO()
        {
            bool p1Dead = IsDead(player1);
            bool p2Dead = IsDead(player2);
            if (!p1Dead && !p2Dead) return;

            _roundActive = false;
            LastRoundWinner = p1Dead && p2Dead ? 0 : p1Dead ? 2 : 1;
            SetPlayersInputLocked(true);
            Phase = MatchPhase.RoundOver;
            _phaseTimer = practiceRespawnDelay;
            OnRoundOver?.Invoke(LastRoundWinner);
        }

        private void RespawnPractice()
        {
            RoundNumber++;
            OnRoundStarted?.Invoke(RoundNumber);
            ResetPracticeNow();
            Phase = MatchPhase.Fight;
            _roundActive = true;
            _fightBannerTimer = 0f;
            SetPlayersInputLocked(false);
            OnFight?.Invoke();
        }

        /// <summary>
        /// 연습 모드 즉시 리셋: 두 기체 체력·부스트·히트·위치·상태를 라운드 시작과 같은 방식으로 되돌린다.
        /// 카운트다운 없이 바로 이어서 움직일 수 있다. 리셋 단축키를 누른 프레임의 다른 게임 입력은 비워 둔다
        /// (리셋 직후 같은 프레임 입력이 새 위치에서 나가지 않게).
        /// </summary>
        public void ResetPracticeNow()
        {
            if (Mode != GameMode.Practice) return;
            StopActiveMoves(player1);
            StopActiveMoves(player2);
            ResetMech(player1, player1SpawnPoint);
            ResetMech(player2, player2SpawnPoint);
            ClearFrameInput(player1);
            ClearFrameInput(player2);
        }

        private void OpenPracticePause()
        {
            // 메뉴가 열린 동안은 봇도 멈추고(라운드 비활성) 게임 입력도 잠근다
            _roundActive = false;
            SetPlayersInputLocked(true);
            OpenMenu(MenuKind.PracticePause, new[] { OptionContinue, OptionTitle });
        }

        // ---------------- 메뉴 입력 ----------------

        private void OpenMenu(MenuKind kind, string[] options)
        {
            _menu = kind;
            MenuOptions = options.Length > 0 ? options : null;
            MenuIndex = 0;
            // 메뉴를 연 버튼 입력이 같은 프레임에 결정으로 읽히지 않게
            _menuOpenedFrame = Time.frameCount;
        }

        private void CloseMenu()
        {
            _menu = MenuKind.None;
            MenuOptions = null;
            MenuIndex = 0;
        }

        private void HandleMenuInput()
        {
            if (MenuOptions == null || Time.frameCount == _menuOpenedFrame) return;

            // 지금 켜져 있는 플레이어 누구나 조작할 수 있다 (2인 배치면 P1·P2 둘 다)
            int vertical = 0;
            bool confirm = false;
            foreach (GameObject mech in new[] { player1, player2 })
            {
                if (mech == null || !mech.activeInHierarchy) continue;
                InputReader input = mech.GetComponent<InputReader>();
                if (input == null) continue;
                vertical += input.MenuVertical;
                confirm |= input.MenuConfirmPressed;
            }

            int count = MenuOptions.Length;
            if (vertical != 0) MenuIndex = ((MenuIndex - Math.Sign(vertical)) % count + count) % count;
            if (confirm) ConfirmMenu(MenuOptions[MenuIndex]);
        }

        private void ConfirmMenu(string option)
        {
            switch (option)
            {
                case OptionVersus:
                    StartMode(GameMode.Versus);
                    break;
                case OptionPractice:
                    StartMode(GameMode.Practice);
                    break;
                case OptionRematch:
                    CloseMenu();
                    StartMatch();
                    break;
                case OptionTitle:
                    CloseMenu();
                    EnterTitle();
                    break;
                case OptionContinue:
                    CloseMenu();
                    _roundActive = true;
                    SetPlayersInputLocked(false);
                    break;
            }
        }

        // ---------------- 공용 ----------------

        private void SetPlayersInputLocked(bool locked)
        {
            SetInputLocked(player1, locked);
            SetInputLocked(player2, locked);
        }

        private static void SetInputLocked(GameObject mech, bool locked)
        {
            if (mech == null) return;
            InputReader input = mech.GetComponent<InputReader>();
            if (input != null) input.SetInputLocked(locked);
        }

        private static void ClearFrameInput(GameObject mech)
        {
            if (mech == null) return;
            InputReader input = mech.GetComponent<InputReader>();
            if (input != null) input.ClearFrameInput();
        }

        /// <summary>현재 대결 중인 아무 플레이어의 시작 버튼 (각자 페어링된 패드 Start / 키보드 Enter).</summary>
        private bool AnyStartPressed()
        {
            return IsStartPressed(player1) || IsStartPressed(player2);
        }

        private static bool IsStartPressed(GameObject mech)
        {
            if (mech == null || !mech.activeInHierarchy) return false;
            InputReader input = mech.GetComponent<InputReader>();
            return input != null && input.StartPressed;
        }

        private static void StopActiveMoves(GameObject mech)
        {
            if (mech == null) return;
            AnchorClaw claw = mech.GetComponent<AnchorClaw>();
            if (claw != null) claw.ResetForRound();
            ThrusterLunge lunge = mech.GetComponent<ThrusterLunge>();
            if (lunge != null) lunge.ResetForRound();
        }

        /// <summary>
        /// 기체 하나를 라운드 시작 상태로 되돌린다. 스폰 포인트가 없으면 위치·회전만 그대로 두고 나머지는 리셋.
        /// 더미처럼 일부 컴포넌트가 없는 기체도 있으니 전부 null 체크.
        /// </summary>
        private static void ResetMech(GameObject mech, Transform spawnPoint)
        {
            if (mech == null) return;

            Rigidbody rb = mech.GetComponent<Rigidbody>();
            if (spawnPoint != null)
            {
                mech.transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);
                if (rb != null)
                {
                    // 트랜스폼만 옮기면 다음 물리 스텝에 리지드바디가 예전 자세로 덮어쓸 수 있다
                    rb.position = spawnPoint.position;
                    rb.rotation = spawnPoint.rotation;
                }
            }
            if (rb != null && !rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            ExternalControlLock controlLock = mech.GetComponent<ExternalControlLock>();
            if (controlLock != null) controlLock.ResetLock();

            Health health = mech.GetComponent<Health>();
            if (health != null) health.ResetHealth();

            BoostGauge boost = mech.GetComponent<BoostGauge>();
            if (boost != null) boost.ResetForRound();

            HeatGauge heat = mech.GetComponent<HeatGauge>();
            if (heat != null) heat.ResetForRound();

            MeleeSystem melee = mech.GetComponent<MeleeSystem>();
            if (melee != null) melee.ResetForRound();

            ImpactSlam slam = mech.GetComponent<ImpactSlam>();
            if (slam != null) slam.ResetForRound();

            LockOnSystem lockOn = mech.GetComponent<LockOnSystem>();
            if (lockOn != null) lockOn.ResetForRound();

            MechStateMachine stateMachine = mech.GetComponent<MechStateMachine>();
            if (stateMachine != null) stateMachine.ResetForRound();

            // 카메라 시점도 스폰 방향으로 (안 하면 Idle이 기체를 예전 카메라 방향으로 돌려버림)
            MechIdentity identity = mech.GetComponent<MechIdentity>();
            MyDongari.Camera.MechCamera mechCamera = identity != null ? identity.MechCamera : null;
            if (mechCamera != null) mechCamera.ResetView(mech.transform.eulerAngles.y);
        }

        private void RoundEnd(int winner)
        {
            _roundActive = false;
            LastRoundWinner = winner;
            // 라운드가 끝난 뒤엔 살아남은 쪽도 멈춘다 (다음 라운드 리셋 전 추가 타격 방지)
            SetPlayersInputLocked(true);

            if (winner == 1) _player1Wins++;
            else if (winner == 2) _player2Wins++;
            // winner == 0: 무승부, 승수 증가 없이 다음 라운드

            OnRoundOver?.Invoke(winner);

            if (_player1Wins >= roundsToWin || _player2Wins >= roundsToWin)
            {
                IsMatchOver = true;
                MatchWinner = _player1Wins >= roundsToWin ? 1 : 2;
                Phase = MatchPhase.MatchOver;
                OpenMenu(MenuKind.MatchOver, new[] { OptionRematch, OptionTitle });
                OnMatchOver?.Invoke(MatchWinner);
            }
            else
            {
                Phase = MatchPhase.RoundOver;
                _phaseTimer = respawnDelay;
            }
        }
    }
}
