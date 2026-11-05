using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace LuckyNumber.POC
{
    /// <summary>
    /// Run rules for the prototype: claim a face, then land it N times within the roll budget.
    /// IMGUI HUD on purpose: zero setup, works in WebGL, easy to throw away.
    /// </summary>
    public class LuckyNumberGame : MonoBehaviour
    {
        public enum Phase { Claim, Play, Won, Lost }

        public DiePlanet die;
        public PlanetWalker walker;
        public int rollsPerRun = 7;
        public int hitsToWin = 3;

        public Phase CurrentPhase { get; private set; } = Phase.Claim;
        public int LuckyNumber { get; private set; }
        public int RollsUsed { get; private set; }
        public int Hits { get; private set; }

        string _lastMessage = "";
        GUIStyle _big, _body;

        void OnEnable()
        {
            die.Settled += OnSettled;
        }

        void OnDisable()
        {
            die.Settled -= OnSettled;
        }

        void Start()
        {
            walker.slamEnabled = false;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.rKey.wasPressedThisFrame)
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                return;
            }

            if (CurrentPhase == Phase.Claim && kb.cKey.wasPressedThisFrame && walker.Grounded)
            {
                int face = walker.CurrentFace;
                LuckyNumber = die.Number(face);
                die.SetLucky(face);
                walker.slamEnabled = true;
                CurrentPhase = Phase.Play;
                _lastMessage = $"Your lucky number is {LuckyNumber}. Make it face the sun.";
            }

            // Prototype toggle for the "Lead core" upgrade idea.
            if (kb.lKey.wasPressedThisFrame)
                die.loadedStrength = die.loadedStrength > 0f ? 0f : 900f;
        }

        void OnSettled(int face)
        {
            if (CurrentPhase != Phase.Play) return;
            RollsUsed++;
            int n = die.Number(face);
            bool hit = n == LuckyNumber;
            if (hit) Hits++;

            _lastMessage = (hit ? $"{n}! Your number landed." : $"Rolled {n}.") + Flavour(n);

            if (Hits >= hitsToWin) { CurrentPhase = Phase.Won; walker.slamEnabled = false; }
            else if (RollsUsed >= rollsPerRun) { CurrentPhase = Phase.Lost; walker.slamEnabled = false; }
        }

        static string Flavour(int n) => n switch
        {
            7 => "  Lucky seven.",
            8 => "  Eight: prosperity.",
            4 => "  Four: an unlucky face.",
            13 => "  Thirteen: cursed.",
            20 => "  Natural 20!",
            1 => "  Natural 1. Ouch.",
            _ => ""
        };

        void OnGUI()
        {
            _big ??= new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _body ??= new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true, normal = { textColor = Color.white } };

            GUILayout.BeginArea(new Rect(20, 20, 560, 400));
            switch (CurrentPhase)
            {
                case Phase.Claim:
                    GUILayout.Label("Claim your lucky number", _big);
                    GUILayout.Label("Walk onto a face and press C. That number is yours for the run.", _body);
                    break;
                case Phase.Play:
                    GUILayout.Label($"Lucky number: {LuckyNumber}", _big);
                    GUILayout.Label($"Hits {Hits}/{hitsToWin}    Rolls {RollsUsed}/{rollsPerRun}", _body);
                    break;
                case Phase.Won:
                    GUILayout.Label("You made your own luck!", _big);
                    GUILayout.Label("Press R to play again.", _body);
                    break;
                case Phase.Lost:
                    GUILayout.Label("Out of rolls", _big);
                    GUILayout.Label("Press R to try again.", _body);
                    break;
            }
            if (!string.IsNullOrEmpty(_lastMessage)) GUILayout.Label(_lastMessage, _body);
            if (die.State != DiePlanet.RollState.Idle) GUILayout.Label($"Rolling... {die.AngularVelocity.magnitude:0.0} rad/s", _body);
            if (die.loadedStrength > 0f) GUILayout.Label("Lead core ON (L to toggle)", _body);
            GUILayout.EndArea();

            // Charge bar
            if (walker.Charge01 > 0f)
            {
                var r = new Rect(Screen.width * 0.5f - 100, Screen.height - 60, 200, 16);
                GUI.Box(r, GUIContent.none);
                GUI.Box(new Rect(r.x, r.y, r.width * walker.Charge01, r.height), GUIContent.none);
            }

            GUI.Label(new Rect(20, Screen.height - 34, 1200, 30),
                "WASD move | hold Space charge, release jump | Space / LMB in air: slam | RMB or Q/E orbit | C claim | L lead core | R restart", _body);
        }
    }
}
