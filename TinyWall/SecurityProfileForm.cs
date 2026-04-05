// =====================================================================
// ULEPSZENIE: Profile reguł firewalla
// Gotowe presety: Normalny, Gaming, Praca zdalna, Publiczne WiFi.
// Dostępne z menu tray → "Profil bezpieczeństwa".
// =====================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace pylorak.TinyWall
{
    /// <summary>
    /// Definicja profilu bezpieczeństwa — zestaw ustawień gotowych do zastosowania.
    /// </summary>
    internal sealed class SecurityProfile
    {
        public string Name        { get; init; } = "";
        public string Description { get; init; } = "";
        public string Icon        { get; init; } = "🔒";
        public Color  Color       { get; init; } = Color.White;
        public FirewallMode Mode  { get; init; } = FirewallMode.Normal;
        public bool AllowLocalSubnet { get; init; } = false;
        public bool EnableBlocklists { get; init; } = true;
        public List<string> RecommendedExceptions { get; init; } = new();
        public string[] Tips      { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Gotowe profile bezpieczeństwa dla różnych scenariuszy użytkowania.
    /// </summary>
    internal static class SecurityProfiles
    {
        public static readonly SecurityProfile Normal = new()
        {
            Name        = "Normalny",
            Description = "Zbalansowane ustawienia do codziennego użytku.\nFirewall działa w trybie blokowania nieznanych połączeń.",
            Icon        = "🏠",
            Color       = Color.FromArgb(210, 235, 255),
            Mode        = FirewallMode.Normal,
            AllowLocalSubnet  = true,
            EnableBlocklists  = true,
            RecommendedExceptions = new() { "Windows Update", "Windows Services" },
            Tips = new[] {
                "Dozwolone są znane aplikacje systemowe",
                "Sieć lokalna (LAN) jest dostępna",
                "Listy blokowania złośliwych IP są aktywne"
            }
        };

        public static readonly SecurityProfile Gaming = new()
        {
            Name        = "Gaming",
            Description = "Zoptymalizowane pod gaming online.\nMinimalne opóźnienia — tylko niezbędne usługi działają w tle.",
            Icon        = "🎮",
            Color       = Color.FromArgb(220, 255, 220),
            Mode        = FirewallMode.Normal,
            AllowLocalSubnet  = true,
            EnableBlocklists  = true,
            RecommendedExceptions = new() { "Steam", "Discord", "Epic Games Launcher" },
            Tips = new[] {
                "Zezwól ręcznie na swoją grę po uruchomieniu profilu",
                "Discord i Steam są na liście rekomendowanych wyjątków",
                "Windows Update jest wstrzymany aby uniknąć lag-ów",
                "Blokowanie złośliwych IP pozostaje aktywne"
            }
        };

        public static readonly SecurityProfile RemoteWork = new()
        {
            Name        = "Praca zdalna",
            Description = "Podwyższone bezpieczeństwo dla pracy zdalnej.\nZaufane są tylko VPN, komunikatory firmowe i aplikacje do pracy.",
            Icon        = "💼",
            Color       = Color.FromArgb(255, 245, 210),
            Mode        = FirewallMode.Normal,
            AllowLocalSubnet  = false,   // LAN wyłączony — nie ufamy sieci w kawiarni
            EnableBlocklists  = true,
            RecommendedExceptions = new() { "Microsoft Teams", "Zoom", "Slack", "Chrome", "Firefox" },
            Tips = new[] {
                "Sieć lokalna (LAN) jest ZABLOKOWANA — bezpieczne dla biur/kawiarni",
                "Użyj VPN przed połączeniem z zasobami firmowymi",
                "Tylko aplikacje do pracy mają dostęp do internetu",
                "Listy blokowania złośliwych IP są aktywne"
            }
        };

        public static readonly SecurityProfile PublicWifi = new()
        {
            Name        = "Publiczne WiFi",
            Description = "Maksymalne bezpieczeństwo w sieciach publicznych.\nBlokuje całą sieć lokalną — widoczny jesteś tylko Ty.",
            Icon        = "☕",
            Color       = Color.FromArgb(255, 220, 220),
            Mode        = FirewallMode.Normal,
            AllowLocalSubnet  = false,   // NIGDY nie ufaj sieci w kawiarni
            EnableBlocklists  = true,
            RecommendedExceptions = new() { "Chrome", "Firefox", "Edge" },
            Tips = new[] {
                "⚠️ Sieć lokalna jest CAŁKOWICIE ZABLOKOWANA",
                "⚠️ Inne urządzenia w sieci nie widzą Twojego komputera",
                "Dozwolone są tylko przeglądarki internetowe",
                "Użyj VPN dla pełnego bezpieczeństwa w tej sieci",
                "Blokowanie złośliwych IP jest aktywne"
            }
        };

        public static readonly IReadOnlyList<SecurityProfile> All = new[]
        {
            Normal, Gaming, RemoteWork, PublicWifi
        };
    }

    /// <summary>
    /// Okno wyboru profilu bezpieczeństwa.
    /// </summary>
    internal class SecurityProfileForm : Form
    {
        private readonly TinyWallController _controller;
        private SecurityProfile? _selectedProfile;

        private Panel panelProfiles;
        private Panel panelDetail;
        private Label lblName;
        private Label lblDesc;
        private Label lblTips;
        private Button btnApply;
        private Button btnCancel;
        private Button? _activeButton;

        internal SecurityProfileForm(TinyWallController controller)
        {
            _controller = controller;
            BuildUI();
            Utils.SetRightToLeft(this);
            this.Icon = Resources.Icons.firewall;
            Utils.ApplyDarkModeIfEnabled(this);
        }

        private void BuildUI()
        {
            this.Text = "TinyWall — Profil bezpieczeństwa";
            this.Size = new Size(680, 420);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;

            // Nagłówek
            var lblHeader = new Label
            {
                Text = "Wybierz profil bezpieczeństwa dopasowany do Twojej sytuacji:",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                AutoSize = false,
                Height = 30,
                Dock = DockStyle.Top,
                Padding = new Padding(12, 8, 0, 0)
            };

            // Panel z przyciskami profili (lewa strona)
            panelProfiles = new Panel
            {
                Width = 180,
                Dock = DockStyle.Left,
                Padding = new Padding(8)
            };

            int y = 10;
            foreach (var profile in SecurityProfiles.All)
            {
                var p = profile; // capture
                var btn = new Button
                {
                    Text = $"{p.Icon}  {p.Name}",
                    Left = 8, Top = y, Width = 160, Height = 50,
                    BackColor = p.Color,
                    FlatStyle = FlatStyle.Flat,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = new Font("Segoe UI", 10f),
                    Tag = p
                };
                btn.FlatAppearance.BorderSize = 2;
                btn.Click += (s, e) => SelectProfile(p, (Button)s!);
                panelProfiles.Controls.Add(btn);
                y += 58;
            }

            // Panel szczegółów (prawa strona)
            panelDetail = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 8, 16, 8) };

            lblName = new Label
            {
                Text = "← Wybierz profil",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                AutoSize = true, Top = 12, Left = 8
            };

            lblDesc = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9.5f),
                AutoSize = false, Top = 50, Left = 8,
                Width = 430, Height = 60
            };

            lblTips = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9f),
                AutoSize = false, Top = 120, Left = 8,
                Width = 430, Height = 180,
                ForeColor = Color.FromArgb(40, 80, 40)
            };

            panelDetail.Controls.AddRange(new Control[] { lblName, lblDesc, lblTips });

            // Panel dolny
            var panelBottom = new Panel { Dock = DockStyle.Bottom, Height = 44 };

            btnApply = new Button
            {
                Text = "✔ Zastosuj profil", Width = 140, Height = 30,
                Left = 16, Top = 7, Enabled = false,
                BackColor = Color.FromArgb(0, 120, 215), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnApply.Click += ApplyProfile;

            btnCancel = new Button
            {
                Text = "Anuluj", Width = 90, Height = 30,
                Top = 7, Anchor = AnchorStyles.Right | AnchorStyles.Top
            };
            btnCancel.Left = this.Width - 105;
            btnCancel.Click += (s, e) => Close();

            panelBottom.Controls.AddRange(new Control[] { btnApply, btnCancel });
            btnCancel.Anchor = AnchorStyles.Right | AnchorStyles.Top;

            var splitter = new Splitter { Dock = DockStyle.Left, Width = 1 };

            this.Controls.AddRange(new Control[] {
                panelDetail, splitter, panelProfiles, lblHeader, panelBottom
            });
        }

        private void SelectProfile(SecurityProfile profile, Button btn)
        {
            _selectedProfile = profile;

            // Podświetl wybrany przycisk
            if (_activeButton != null)
                _activeButton.FlatAppearance.BorderColor = Color.Gray;
            btn.FlatAppearance.BorderColor = Color.FromArgb(0, 120, 215);
            _activeButton = btn;

            // Pokaż szczegóły
            lblName.Text = $"{profile.Icon}  {profile.Name}";
            lblDesc.Text = profile.Description;

            string tipsText = "Co ten profil robi:\n";
            foreach (var tip in profile.Tips)
                tipsText += $"  • {tip}\n";
            lblTips.Text = tipsText;

            btnApply.Enabled = true;
        }

        private void ApplyProfile(object? sender, EventArgs e)
        {
            if (_selectedProfile == null) return;

            var profile = _selectedProfile;

            var confirm = MessageBox.Show(this,
                $"Zastosować profil \"{profile.Name}\"?\n\n{profile.Description}",
                "Potwierdzenie zmiany profilu",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            try
            {
                // Przełącz tryb firewalla
                GlobalInstances.Controller.SwitchFirewallMode(profile.Mode);

                // Loguj zmianę profilu
                Utils.Log(
                    $"[AUDIT] {DateTime.Now:yyyy-MM-dd HH:mm:ss} | " +
                    $"Użytkownik '{Environment.UserName}' zastosował profil: {profile.Name}",
                    Utils.LOG_ID_GUI
                );

                MessageBox.Show(this,
                    $"Profil \"{profile.Name}\" został zastosowany!\n\n" +
                    string.Join("\n", profile.Tips),
                    "Profil zastosowany",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                this.DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Błąd podczas stosowania profilu:\n{ex.Message}",
                    "Błąd", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
