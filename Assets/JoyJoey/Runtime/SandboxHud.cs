using System;
using UnityEngine;

namespace JoyJoey.Sandbox
{
    /// <summary>Training tools stay outside the combat simulation and use unscaled time.</summary>
    internal sealed class SandboxHud : IDisposable
    {
        readonly SandboxRunner _runner;
        GUIStyle _title, _heading, _body, _small, _number, _button, _label, _mono;
        Font _font;
        float _width, _height;
        static readonly Color Muted = new Color(.56f, .65f, .72f);
        static readonly Color Pale = new Color(.9f, .95f, .97f);
        static readonly Color CardColor = new Color(.055f, .085f, .13f, .96f);
        static readonly Color Accent = new Color(.35f, .92f, .87f);
        public SandboxHud(SandboxRunner runner) { _runner = runner; }

        void EnsureStyles()
        {
            if (_body != null) return;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _body = Style(16, Pale);
            _small = Style(12, Muted);
            _title = Style(24, Pale, FontStyle.Bold);
            _heading = Style(13, Accent, FontStyle.Bold);
            _label = Style(11, Muted, FontStyle.Bold);
            _number = Style(38, Pale, FontStyle.Bold);
            _mono = Style(13, Pale);
            _button = Style(13, Pale, FontStyle.Bold);
            _button.alignment = TextAnchor.MiddleCenter;
            _button.normal.background = Texture2D.whiteTexture;
            _button.normal.textColor = Pale;
            _button.hover.background = Texture2D.whiteTexture;
            _button.hover.textColor = Color.white;
            _button.active.background = Texture2D.whiteTexture;
            _button.active.textColor = Accent;
            _button.padding = new RectOffset(10, 10, 6, 6);
        }

        GUIStyle Style(int size, Color color, FontStyle weight = FontStyle.Normal)
        {
            var style = new GUIStyle { font = _font, fontSize = size, fontStyle = weight, wordWrap = true, richText = false };
            style.normal.textColor = color;
            return style;
        }

        public void Draw()
        {
            EnsureStyles();
            Matrix4x4 saved = GUI.matrix;
            Color color = GUI.color;
            float scale = Mathf.Min(Screen.width / 1440f, Screen.height / 810f);
            scale = Mathf.Max(.3f, scale);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            _width = Screen.width / scale;
            _height = Screen.height / scale;
            var sim = _runner.Simulation;
            float margin = 24;
            Label(margin, 20, 360, 32, "JOY JOEY  /  COMBAT LAB", _title);
            Label(margin, 52, 350, 18, "CIRCO ONÍRICO     ·     SANDBOX DE COMBATE", _label);
            Label(_width - 400, 24, 376, 20, _runner.InputDevice + "  ·  60 Hz" + (_runner.SlowMotion ? "  ·  0.35×" : "") + (_runner.Paused ? "  ·  PAUSA" : ""), _heading, TextAnchor.UpperRight);
            Label(_width - 390, 49, 366, 18, "F1 ayuda   ·   F2 hitboxes   ·   R reiniciar", _small, TextAnchor.UpperRight);

            float cardWidth = (_width - margin * 2 - 24) / 3;
            float x1 = margin, x2 = margin + cardWidth + 12, x3 = x2 + cardWidth + 12;
            Panel(x1, 82, cardWidth, 103, Accent);
            Label(x1 + 16, 95, 100, 18, "JOEY", _heading);
            Label(x1 + 16, 119, cardWidth - 125, 26, sim.LastAction ?? "Listo", _body);
            Label(x1 + 16, 153, cardWidth - 32, 18, (sim.IsCharging ? "CARGANDO " + sim.ChargeTicks + "f" : sim.Player.State) + "  ·  " + (sim.CanCancel ? "CANCEL ABIERTO" : sim.Player.Grounded ? "suelo" : "aire"), _small);
            Label(x1 + cardWidth - 90, 107, 70, 45, sim.StyleRank, _number, TextAnchor.MiddleRight);
            Label(x1 + cardWidth - 96, 154, 80, 18, "ESTILO", _label, TextAnchor.UpperRight);

            Color ragColor = sim.SelectedRag == RagStyle.Strongman ? new Color(1, .70f, .27f) : sim.SelectedRag == RagStyle.Jester ? new Color(.77f, .50f, 1) : new Color(.38f, .70f, 1);
            Panel(x2, 82, cardWidth, 103, ragColor);
            Label(x2 + 16, 95, cardWidth - 105, 20, sim.SelectedRag.ToString().ToUpperInvariant(), _heading);
            Label(x2 + cardWidth - 100, 95, 84, 20, sim.Transformed ? "TRAJE ACTIVO" : "Q / E  ·  L2 / R2", _small, TextAnchor.UpperRight);
            Label(x2 + 16, 124, 75, 25, "FUN", _body);
            Label(x2 + cardWidth - 89, 120, 73, 30, sim.Fun.ToString("0") + "%", _body, TextAnchor.UpperRight);
            Fill(new Rect(x2 + 66, 127, cardWidth - 157, 10), new Color(.14f, .19f, .24f));
            Fill(new Rect(x2 + 66, 127, (cardWidth - 157) * Mathf.Clamp01(sim.Fun / 100), 10), ragColor);
            Label(x2 + 16, 153, cardWidth - 32, 18, sim.Transformed ? "H / L1 para salir · FUN se consume" : "H / L1 transforma · golpear genera FUN", _small);

            Panel(x3, 82, cardWidth, 103, SandboxRunner.DummyColor);
            Label(x3 + 16, 95, 240, 20, "DUMMY · "+sim.TargetProfile.DisplayName.ToUpperInvariant(), _heading);
            Label(x3 + 16, 122, cardWidth - 140, 28, DummyName(sim.DummyBehavior) + (sim.Dummy.Hitstun > 0 ? " · STUN " + sim.Dummy.Hitstun + "f" : sim.DummyVulnerable?" · JUGGLE":" · libre"), _body);
            Label(x3 + cardWidth - 115, 106, 99, 44, sim.Dummy.Damage.ToString("0") + "%", _number, TextAnchor.MiddleRight);
            Label(x3 + 16, 153, cardWidth - 32, 18, "ARM "+sim.Dummy.Armor.ToString("0")+" · JUG "+sim.Dummy.JuggleMeter.ToString("0")+"/"+sim.TargetProfile.AirJuggleThreshold.ToString("0")+" · F6 perfil", _small);

            if (sim.ComboHits > 0)
            {
                float comboX = _width - 252;
                Panel(comboX, 205, 228, 113, Accent);
                Label(comboX + 16, 217, 90, 48, sim.ComboHits.ToString("00"), _number);
                Label(comboX + 83, 227, 130, 30, "HITS", _heading);
                Label(comboX + 16, 272, 196, 30, sim.ComboDamage.ToString("0.#") + " daño   ·   récord " + sim.BestCombo, _small);
            }
            else
            {
                Label(_width - 344, 207, 320, 20, "RUTA PARA EMPEZAR", _heading, TextAnchor.UpperRight);
                Label(_width - 344, 234, 320, 56, "J → J → S + J mantenido\nJ en aire → K", _body, TextAnchor.UpperRight);
            }

            DrawWorldLabels();
            if (_runner.DebugHitboxes) DrawDebug();
            DrawBottomBar();
            if (_runner.Paused && !_runner.HelpVisible)
            {
                Panel(_width * .5f - 155, _height * .5f - 50, 310, 88, new Color(1, .75f, .32f));
                Label(_width * .5f - 135, _height * .5f - 34, 270, 28, "SIMULACIÓN EN PAUSA", _heading, TextAnchor.UpperCenter);
                Label(_width * .5f - 135, _height * .5f - 2, 270, 22, "Esc / Start continúa     ·     . avanza 1 frame", _small, TextAnchor.UpperCenter);
            }
            if (_runner.HelpVisible) DrawHelp();
            GUI.matrix = saved;
            GUI.color = color;
        }

        void DrawWorldLabels()
        {
            var sim = _runner.Simulation;
            WorldLabel(sim.Player.Position + Vector2.up * 2.15f, "JOEY", Accent);
            WorldLabel(sim.Dummy.Position + Vector2.up * 2.15f, sim.Dummy.Hitstun > 0 ? sim.Dummy.Hitstun + "f STUN" : "DUMMY", SandboxRunner.DummyColor);
        }

        void WorldLabel(Vector2 position, string text, Color color)
        {
            Vector3 pixel = _runner.ArenaCamera.WorldToScreenPoint(position);
            if (pixel.z < 0) return;
            float x = pixel.x / Screen.width * _width, y = (1 - pixel.y / Screen.height) * _height;
            if (y < 190 || y > _height - 105) return;
            Color original = _label.normal.textColor;
            _label.normal.textColor = color;
            Label(x - 70, y, 140, 19, text, _label, TextAnchor.UpperCenter);
            _label.normal.textColor = original;
        }

        void DrawDebug()
        {
            var sim = _runner.Simulation;
            Panel(24, 205, 286, 247, new Color(.49f, .66f, .75f));
            Label(40, 217, 254, 20, "INSPECTOR DE COMBATE", _heading);
            Label(40, 247, 254, 21, "tick " + sim.Tick + " · freeze " + sim.Hitstop + " · buf " + sim.BufferedInputs, _mono);
            Label(40, 271, 254, 21, "v = " + sim.Player.Velocity.ToString("F2"), _mono);
            Label(40, 295, 254, 42, "move " + sim.Player.MoveId + " / " + sim.Player.MoveTick + "f\n" + (sim.Player.Move != null ? (sim.Player.Move.Cancels==CancelActions.None?"Termina en "+sim.Player.Move.Total+"f · sin cancel":"cancel " + sim.Player.Move.CancelOpen + "–" + sim.Player.Move.CancelClose + "f") : "doble salto " + sim.Player.AirJumps + " · dash aéreo " + sim.Player.AirDashes), _mono);
            var inputs = _runner.InputHistory;
            for (int i = 0; i < Mathf.Min(5, inputs.Count); i++) Label(40 + (i >= 3 ? 128 : 0), 353 + (i >= 3 ? i - 3 : i) * 24, 126, 22, inputs[i], i == 0 ? _heading : _small);
            Label(40, 426, 254, 17, "cancel: " + sim.LastCancel, _small);
        }

        void DrawBottomBar()
        {
            float y = _height - 95;
            Fill(new Rect(0, y - 9, _width, 104), new Color(.036f, .054f, .085f, .97f));
            Fill(new Rect(24, y - 9, _width - 48, 1), new Color(.2f, .29f, .34f));
            string controls = _width < 1300 ? "WASD mover   Space salto   J / □ básico   K / △ Rag   L / ○ trick   Shift / R1 dash   H / L1 traje" : "WASD / stick  mover    Space / ×  salto    J / □  básico    K / △  Rag    L / ○  trick    Shift / R1  dash    H / L1  traje";
            Label(24, y + 4, _width - 48, 22, controls, _body);
            Label(24, y + 38, Mathf.Max(200, _width - 510), 34, "Las continuaciones dependen de alcance, alturas y tiempos · F6 ligero/pesado", _small);
            float start = _width - 454;
            if (Button(start, y + 36, 83, 29, "R  RESET")) _runner.ResetPractice();
            if (Button(start + 91, y + 36, 99, 29, "F2  CAJAS")) _runner.ToggleDebug();
            if (Button(start + 198, y + 36, 116, 29, "F5  " + (_runner.SlowMotion ? "0.35×" : "LENTO"))) _runner.ToggleSlow();
            if (Button(start + 322, y + 36, 108, 29, "F1  AYUDA")) _runner.ToggleHelp();
        }

        void DrawHelp()
        {
            Fill(new Rect(0, 0, _width, _height), new Color(.015f, .025f, .045f, .86f));
            float width = Mathf.Min(1160, _width - 70), height = Mathf.Min(626, _height - 80);
            float x = (_width - width) * .5f, y = (_height - height) * .5f;
            Panel(x, y, width, height, Accent);
            Label(x + 30, y + 23, width - 240, 36, "CIRCO ONÍRICO  /  PRÁCTICA LIBRE", _title);
            Label(x + 30, y + 66, width - 60, 25, "Prueba alturas, distancias y cierres. La simulación está pausada mientras lees.", _small);
            if (Button(x + width - 165, y + 25, 135, 32, "F1  CERRAR")) _runner.ToggleHelp();
            float left = x + 30, right = x + width * .51f, col = width * .45f;
            Label(left, y + 114, col, 24, "CONTROLES", _heading);
            Label(left, y + 148, col, 188,
                "WASD / stick o D-pad     Movimiento y dirección\n" +
                "Space / ×                         Salto · mantener = más altura\n" +
                "J / □                                Básico · tap / hold / repetir\n" +
                "K / △                               Ataque Rag · mantener carga\n" +
                "L / ○                                Trick / reposicionamiento\n" +
                "Shift / R1                          Dash en suelo y aire\n" +
                "H / L1                               Entrar / salir del traje\n" +
                "Q, E / L2, R2                     Ciclar Rag fuera del traje", _body);
            Label(left, y + 355, col, 24, "MOVILIDAD", _heading);
            Label(left, y + 388, col, 135,
                "Doble salto, salto variable, coyote time y buffer.\nS + salto atraviesa una plataforma fina.\nEn el aire, S acelera la caída. Salta contra una pared.\nCada ataque conserva la inercia que tenga configurada.\nAl terminar el movimiento, vuelve a actuar y reposiciónate.", _body);

            Label(right, y + 114, col, 24, "PRIMER COMBO", _heading);
            Label(right, y + 148, col, 105,
                "1   Acércate al dummy y pulsa J dos veces.\n2   S + J mantenido: launcher, también en whiff.\n3   Suelta S y pulsa J en el aire.\n4   W + K aéreo: disparo que sostiene al enemigo.", _body);
            Label(right, y + 276, col, 24, "TRES RAGS, TRES RITMOS", _heading);
            Label(right, y + 309, col, 96,
                "STRONGMAN   Golpes pesados y lanzamiento.\nJESTER                Multigolpes para extender juggles.\nILLUSIONIST        Reposición y persecución.\nGenera FUN con golpes; H transforma tu set de básicos.", _body);
            Label(right, y + 429, col, 24, "HERRAMIENTAS DE ENTRENAMIENTO", _heading);
            Label(right, y + 462, col, 89,
                "R / Select reiniciar     F2 cajas y datos de combate\nF3 dummy quieto/escape/salto   F6 ligero/pesado\nF4 lanzamiento fijo/escalado   F5 cámara lenta\nEsc / Start pausa     . un frame", _body);
            Label(x + 30, y + height - 39, width - 60, 22, "El rival terrestre es vulnerable en stun o en aire. Los cierres alejan/derriban; no hay bloqueos de combo ocultos.", _small);
        }

        static string DummyName(DummyMode mode) => mode == DummyMode.Escape ? "Escape" : mode == DummyMode.Jump ? "Salto" : "Quieto";
        void Panel(float x, float y, float width, float height, Color accent)
        {
            Fill(new Rect(x, y, width, height), CardColor);
            Fill(new Rect(x, y, width, 2), accent);
            Fill(new Rect(x, y + height - 1, width, 1), new Color(.13f, .20f, .26f));
        }
        static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = previous;
        }
        bool Button(float x, float y, float width, float height, string text)
        {
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(.16f, .24f, .29f);
            bool result = GUI.Button(new Rect(x, y, width, height), text, _button);
            GUI.backgroundColor = prev;
            return result;
        }
        static void Label(float x, float y, float width, float height, string text, GUIStyle style, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            TextAnchor previous = style.alignment; style.alignment = alignment;
            GUI.Label(new Rect(x, y, width, height), text ?? "", style); style.alignment = previous;
        }
        public void Dispose() { }
    }
}
