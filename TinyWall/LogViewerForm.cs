// =====================================================================
// ULEPSZENIE: Log Viewer z filtrowaniem
// Nowe okno dostępne z menu tray - pokazuje historię logów firewalla
// z możliwością filtrowania po aplikacji, IP, akcji i eksportu do CSV.
// =====================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace pylorak.TinyWall
{
    internal class LogViewerForm : Form
    {
        // --- Kontrolki UI ---
        private ListView listView;
        private TextBox txtSearch;
        private ComboBox cmbFilter;
        private Button btnRefresh;
        private Button btnExportCsv;
        private Button btnClear;
        private Button btnClose;
        private Label lblCount;
        private Panel panelTop;
        private Panel panelBottom;
        private ColumnHeader colTime;
        private ColumnHeader colAction;
        private ColumnHeader colApp;
        private ColumnHeader colProto;
        private ColumnHeader colDir;
        private ColumnHeader colLocalIp;
        private ColumnHeader colLocalPort;
        private ColumnHeader colRemoteIp;
        private ColumnHeader colRemotePort;

        // --- Dane ---
        private List<LogLine> AllEntries = new();

        private sealed class LogLine
        {
            public DateTime Timestamp;
            public string Action   = "";
            public string App      = "";
            public string Protocol = "";
            public string Dir      = "";
            public string LocalIp  = "";
            public string LocalPort= "";
            public string RemoteIp = "";
            public string RemotePort = "";
            public bool IsBlocked;
        }

        internal LogViewerForm()
        {
            BuildUI();
            Utils.SetRightToLeft(this);
            this.Icon = Resources.Icons.firewall;
            Utils.ApplyDarkModeIfEnabled(this);
            LoadLogs();
        }

        // ---------------------------------------------------------------
        // Budowanie interfejsu
        // ---------------------------------------------------------------
        private void BuildUI()
        {
            this.Text = "TinyWall — Przeglądarka logów";
            this.Size = new Size(1100, 650);
            this.MinimumSize = new Size(800, 500);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.KeyPreview = true;
            this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

            // --- Panel górny (filtry) ---
            panelTop = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6, 8, 6, 4) };

            var lblSearch = new Label { Text = "Szukaj:", AutoSize = true, Top = 12, Left = 6 };
            txtSearch = new TextBox { Left = 60, Top = 9, Width = 220, Height = 22 };
            txtSearch.TextChanged += (s, e) => ApplyFilter();
            txtSearch.PlaceholderText = "IP, aplikacja, port...";

            var lblFilter = new Label { Text = "Filtr:", AutoSize = true, Top = 12, Left = 295 };
            cmbFilter = new ComboBox
            {
                Left = 330, Top = 9, Width = 140, DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbFilter.Items.AddRange(new object[] {
                "Wszystkie", "Tylko zablokowane", "Tylko dozwolone", "TCP", "UDP", "Wychodzące", "Przychodzące"
            });
            cmbFilter.SelectedIndex = 0;
            cmbFilter.SelectedIndexChanged += (s, e) => ApplyFilter();

            btnRefresh = new Button { Text = "⟳ Odśwież", Left = 490, Top = 8, Width = 90, Height = 26 };
            btnRefresh.Click += (s, e) => { LoadLogs(); ApplyFilter(); };

            btnExportCsv = new Button { Text = "⬇ Eksport CSV", Left = 590, Top = 8, Width = 110, Height = 26 };
            btnExportCsv.Click += ExportToCsv;

            btnClear = new Button { Text = "🗑 Wyczyść", Left = 710, Top = 8, Width = 90, Height = 26 };
            btnClear.Click += ClearLogs;

            lblCount = new Label { Text = "0 wpisów", AutoSize = true, Top = 14, Left = 820 };

            panelTop.Controls.AddRange(new Control[] {
                lblSearch, txtSearch, lblFilter, cmbFilter,
                btnRefresh, btnExportCsv, btnClear, lblCount
            });

            // --- Panel dolny (przyciski) ---
            panelBottom = new Panel { Dock = DockStyle.Bottom, Height = 38 };
            btnClose = new Button
            {
                Text = "Zamknij", Width = 90, Height = 26,
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom
            };
            btnClose.Click += (s, e) => Close();
            panelBottom.Controls.Add(btnClose);
            btnClose.Location = new Point(panelBottom.Width - 100, 6);
            btnClose.Anchor = AnchorStyles.Right | AnchorStyles.Top;

            // --- ListView ---
            listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                VirtualMode = false,
                AllowColumnReorder = true,
                Font = new Font("Consolas", 8.5f)
            };

            colTime      = new ColumnHeader { Text = "Czas",          Width = 145 };
            colAction    = new ColumnHeader { Text = "Akcja",         Width = 80  };
            colApp       = new ColumnHeader { Text = "Aplikacja",     Width = 180 };
            colProto     = new ColumnHeader { Text = "Proto",         Width = 55  };
            colDir       = new ColumnHeader { Text = "Kierunek",      Width = 90  };
            colLocalIp   = new ColumnHeader { Text = "Lokalny IP",    Width = 130 };
            colLocalPort = new ColumnHeader { Text = "Port lok.",     Width = 70  };
            colRemoteIp  = new ColumnHeader { Text = "Zdalny IP",     Width = 130 };
            colRemotePort= new ColumnHeader { Text = "Port zdal.",    Width = 70  };

            listView.Columns.AddRange(new[] {
                colTime, colAction, colApp, colProto, colDir,
                colLocalIp, colLocalPort, colRemoteIp, colRemotePort
            });

            // Kolorowanie wierszy
            listView.OwnerDraw = false;

            this.Controls.AddRange(new Control[] { listView, panelTop, panelBottom });
        }

        // ---------------------------------------------------------------
        // Wczytuje logi z pliku audit logu TinyWall
        // ---------------------------------------------------------------
        private void LoadLogs()
        {
            AllEntries.Clear();

            // Wczytaj log firewalla (wpisy z GUI i Service)
            string logDir = Path.Combine(Utils.AppDataPath, "logs");
            string[] logFiles = {
                Path.Combine(logDir, $"{Utils.LOG_ID_GUI}.log"),
                Path.Combine(logDir, $"{Utils.LOG_ID_SERVICE}.log")
            };

            foreach (string logFile in logFiles)
            {
                if (!File.Exists(logFile)) continue;

                try
                {
                    string[] lines = File.ReadAllLines(logFile, Encoding.UTF8);
                    foreach (string line in lines)
                    {
                        var entry = ParseLogLine(line);
                        if (entry != null)
                            AllEntries.Add(entry);
                    }
                }
                catch { /* ignoruj błędy odczytu */ }
            }

            // Uzupełnij o wpisy z Windows Event Log (firewall blocked/allowed)
            LoadWindowsFirewallEvents();

            // Posortuj od najnowszych
            AllEntries.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        }

        private void LoadWindowsFirewallEvents()
        {
            try
            {
                // Odczytaj ostatnie 500 zdarzeń z logu Windows Security
                using var eventLog = new System.Diagnostics.EventLog("Security");
                var entries = eventLog.Entries
                    .Cast<System.Diagnostics.EventLogEntry>()
                    .Where(e => e.InstanceId == 5157 || e.InstanceId == 5156 ||
                                e.InstanceId == 5152 || e.InstanceId == 5154)
                    .OrderByDescending(e => e.TimeGenerated)
                    .Take(500);

                foreach (var e in entries)
                {
                    bool isBlocked = (e.InstanceId == 5157 || e.InstanceId == 5152);
                    string msg = e.Message ?? "";

                    AllEntries.Add(new LogLine
                    {
                        Timestamp  = e.TimeGenerated,
                        Action     = isBlocked ? "ZABLOKOWANO" : "DOZWOLONO",
                        App        = ExtractField(msg, "Application Name:"),
                        Protocol   = MapProtocol(ExtractField(msg, "Protocol:")),
                        Dir        = ExtractField(msg, "Direction:").Contains("%%14593") ? "Wychodzące" : "Przychodzące",
                        LocalIp    = ExtractField(msg, "Source Address:"),
                        LocalPort  = ExtractField(msg, "Source Port:"),
                        RemoteIp   = ExtractField(msg, "Destination Address:"),
                        RemotePort = ExtractField(msg, "Destination Port:"),
                        IsBlocked  = isBlocked
                    });
                }
            }
            catch
            {
                // Windows Event Log może być niedostępny bez uprawnień admina
            }
        }

        private static string ExtractField(string msg, string fieldName)
        {
            int idx = msg.IndexOf(fieldName, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "";
            int start = idx + fieldName.Length;
            int end = msg.IndexOf('\n', start);
            return end < 0
                ? msg[start..].Trim()
                : msg[start..end].Trim();
        }

        private static string MapProtocol(string protoNum) => protoNum switch
        {
            "6"   => "TCP",
            "17"  => "UDP",
            "1"   => "ICMP",
            "58"  => "ICMPv6",
            "47"  => "GRE",
            "41"  => "IPv6",
            _     => protoNum
        };

        private static LogLine? ParseLogLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;

            // Format naszego audit logu: "2024-01-15 14:32:01 | [AUDIT] ..."
            try
            {
                if (line.Length < 20) return null;

                // Wyodrębnij timestamp jeśli jest
                if (!DateTime.TryParseExact(
                    line[..19], "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime ts))
                    ts = DateTime.Now;

                bool isBlocked = line.Contains("ZABLOKOWANO") || line.Contains("BLOCKED") || line.Contains("block");

                return new LogLine
                {
                    Timestamp = ts,
                    Action    = isBlocked ? "ZABLOKOWANO" : "AUDIT",
                    App       = "",
                    Protocol  = "",
                    Dir       = "",
                    LocalIp   = "",
                    LocalPort = "",
                    RemoteIp  = "",
                    RemotePort= line.Length > 20 ? line[20..].Trim() : line,
                    IsBlocked = isBlocked
                };
            }
            catch { return null; }
        }

        // ---------------------------------------------------------------
        // Filtrowanie i wyświetlanie
        // ---------------------------------------------------------------
        private void ApplyFilter()
        {
            string search = txtSearch.Text.Trim().ToLowerInvariant();
            int filterIdx = cmbFilter.SelectedIndex;

            var filtered = AllEntries.Where(e =>
            {
                // Filtr combo
                bool passFilter = filterIdx switch
                {
                    1 => e.IsBlocked,
                    2 => !e.IsBlocked,
                    3 => e.Protocol == "TCP",
                    4 => e.Protocol == "UDP",
                    5 => e.Dir.Contains("Wychod", StringComparison.OrdinalIgnoreCase),
                    6 => e.Dir.Contains("Przych", StringComparison.OrdinalIgnoreCase),
                    _ => true
                };

                if (!passFilter) return false;

                // Szukaj tekstowa
                if (!string.IsNullOrEmpty(search))
                {
                    return e.App.Contains(search, StringComparison.OrdinalIgnoreCase)
                        || e.RemoteIp.Contains(search, StringComparison.OrdinalIgnoreCase)
                        || e.LocalIp.Contains(search, StringComparison.OrdinalIgnoreCase)
                        || e.RemotePort.Contains(search, StringComparison.OrdinalIgnoreCase)
                        || e.LocalPort.Contains(search, StringComparison.OrdinalIgnoreCase)
                        || e.Action.Contains(search, StringComparison.OrdinalIgnoreCase)
                        || e.Protocol.Contains(search, StringComparison.OrdinalIgnoreCase);
                }

                return true;
            }).Take(2000).ToList(); // max 2000 wpisów w liście

            listView.BeginUpdate();
            listView.Items.Clear();

            foreach (var e in filtered)
            {
                string appName = string.IsNullOrEmpty(e.App)
                    ? ""
                    : Path.GetFileName(e.App);

                var item = new ListViewItem(e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
                item.SubItems.Add(e.Action);
                item.SubItems.Add(appName);
                item.SubItems.Add(e.Protocol);
                item.SubItems.Add(e.Dir);
                item.SubItems.Add(e.LocalIp);
                item.SubItems.Add(e.LocalPort);
                item.SubItems.Add(e.RemoteIp);
                item.SubItems.Add(e.RemotePort);
                item.Tag = e;

                // Kolorowanie: czerwony = zablokowane, zielony = dozwolone
                if (e.IsBlocked)
                    item.BackColor = Color.FromArgb(255, 230, 230);
                else if (e.Action == "DOZWOLONO")
                    item.BackColor = Color.FromArgb(230, 255, 230);
                else
                    item.BackColor = Color.FromArgb(230, 240, 255); // niebieski = audit

                listView.Items.Add(item);
            }

            listView.EndUpdate();
            lblCount.Text = $"{filtered.Count} z {AllEntries.Count} wpisów";
        }

        // ---------------------------------------------------------------
        // Eksport do CSV
        // ---------------------------------------------------------------
        private void ExportToCsv(object? sender, EventArgs e)
        {
            using var sfd = new SaveFileDialog
            {
                Title = "Eksportuj logi do CSV",
                Filter = "Pliki CSV (*.csv)|*.csv",
                FileName = $"TinyWall_logs_{DateTime.Now:yyyy-MM-dd_HH-mm}.csv",
                DefaultExt = "csv"
            };

            if (sfd.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Czas;Akcja;Aplikacja;Protokół;Kierunek;Lokalny IP;Port lok.;Zdalny IP;Port zdal.");

                foreach (ListViewItem item in listView.Items)
                {
                    string Esc(string v) => v.Contains(';') ? $"\"{v}\"" : v;
                    sb.AppendLine(string.Join(";", item.SubItems.Cast<ListViewItem.ListViewSubItem>()
                        .Select(s => Esc(s.Text))));
                }

                File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);

                MessageBox.Show(this,
                    $"Zapisano {listView.Items.Count} wpisów do:\n{sfd.FileName}",
                    "Eksport zakończony", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Błąd: {ex.Message}", "Błąd eksportu",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---------------------------------------------------------------
        // Czyszczenie logów
        // ---------------------------------------------------------------
        private void ClearLogs(object? sender, EventArgs e)
        {
            if (MessageBox.Show(this,
                "Czy na pewno chcesz wyczyścić wszystkie logi TinyWall?\nOperacja jest nieodwracalna.",
                "Potwierdzenie", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            try
            {
                string logDir = Path.Combine(Utils.AppDataPath, "logs");
                if (Directory.Exists(logDir))
                    foreach (var f in Directory.GetFiles(logDir, "*.log"))
                        File.WriteAllText(f, "", Encoding.UTF8);

                AllEntries.Clear();
                listView.Items.Clear();
                lblCount.Text = "0 wpisów";

                MessageBox.Show(this, "Logi zostały wyczyszczone.",
                    "TinyWall", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Błąd: {ex.Message}", "Błąd",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyFilter();
        }
    }
}
