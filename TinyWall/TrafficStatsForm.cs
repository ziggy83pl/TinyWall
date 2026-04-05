// =====================================================================
// ULEPSZENIE: Statystyki ruchu sieciowego per-aplikacja
// Śledzi ile MB każda aplikacja wysłała/odebrała w bieżącej sesji.
// Dostępne z menu tray jako osobne okno.
// =====================================================================
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Windows.Forms;

namespace pylorak.TinyWall
{
    /// <summary>
    /// Śledzi statystyki ruchu sieciowego per-aplikacja w czasie rzeczywistym.
    /// </summary>
    internal static class TrafficStatsTracker
    {
        private sealed class AppStats
        {
            public string AppPath = "";
            public long BytesSent;
            public long BytesReceived;
            public int ConnectionCount;
            public DateTime FirstSeen = DateTime.Now;
            public DateTime LastSeen  = DateTime.Now;
        }

        private static readonly ConcurrentDictionary<string, AppStats> Stats = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Rejestruje ruch dla danej aplikacji (wywoływane przez ConnectionsForm).
        /// </summary>
        public static void RecordTraffic(string appPath, long bytesSent, long bytesReceived)
        {
            if (string.IsNullOrEmpty(appPath)) return;

            var stat = Stats.GetOrAdd(appPath, path => new AppStats { AppPath = path });
            lock (stat)
            {
                stat.BytesSent     += bytesSent;
                stat.BytesReceived += bytesReceived;
                stat.ConnectionCount++;
                stat.LastSeen = DateTime.Now;
            }
        }

        public static IReadOnlyList<(string App, long Sent, long Recv, int Conns, DateTime LastSeen)> GetSnapshot()
        {
            return Stats.Values
                .OrderByDescending(s => s.BytesSent + s.BytesReceived)
                .Select(s => (s.AppPath, s.BytesSent, s.BytesReceived, s.ConnectionCount, s.LastSeen))
                .ToList();
        }

        public static void Reset() => Stats.Clear();
    }

    /// <summary>
    /// Okno statystyk ruchu per-aplikacja z auto-odświeżaniem.
    /// </summary>
    internal class TrafficStatsForm : Form
    {
        private ListView listView;
        private Button btnReset;
        private Button btnClose;
        private Button btnExport;
        private Label lblInfo;
        private System.Windows.Forms.Timer refreshTimer;
        private ProgressBar barTotal;
        private Label lblTotal;

        internal TrafficStatsForm()
        {
            BuildUI();
            Utils.SetRightToLeft(this);
            this.Icon = Resources.Icons.firewall;
            Utils.ApplyDarkModeIfEnabled(this);
            RefreshStats();
        }

        private void BuildUI()
        {
            this.Text = "TinyWall — Statystyki ruchu per-aplikacja";
            this.Size = new Size(860, 520);
            this.MinimumSize = new Size(600, 400);
            this.StartPosition = FormStartPosition.CenterScreen;

            // --- Panel górny ---
            var panelTop = new Panel { Dock = DockStyle.Top, Height = 36 };
            lblInfo = new Label { Text = "Statystyki od uruchomienia sesji (auto-odświeżanie co 3s)", AutoSize = true, Top = 10, Left = 8 };
            panelTop.Controls.Add(lblInfo);

            // --- Panel pasek sumy ---
            var panelBar = new Panel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(6, 4, 6, 4) };
            lblTotal = new Label { Text = "Łącznie: 0 MB", AutoSize = true, Top = 8, Left = 6 };
            panelBar.Controls.Add(lblTotal);

            // --- ListView ---
            listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Font = new Font("Consolas", 8.5f)
            };

            listView.Columns.AddRange(new[]
            {
                new ColumnHeader { Text = "#",              Width = 35  },
                new ColumnHeader { Text = "Aplikacja",      Width = 230 },
                new ColumnHeader { Text = "Wysłano ↑",      Width = 100 },
                new ColumnHeader { Text = "Odebrano ↓",     Width = 100 },
                new ColumnHeader { Text = "Łącznie",        Width = 100 },
                new ColumnHeader { Text = "Połączenia",     Width = 90  },
                new ColumnHeader { Text = "Ostatnia aktyw.",Width = 145 },
            });

            // Kliknięcie kolumny = sortowanie
            listView.ColumnClick += OnColumnClick;

            // --- Panel dolny ---
            var panelBottom = new Panel { Dock = DockStyle.Bottom, Height = 40 };

            btnReset = new Button { Text = "🔄 Resetuj statystyki", Width = 150, Height = 26, Left = 8, Top = 7 };
            btnReset.Click += (s, e) => { TrafficStatsTracker.Reset(); RefreshStats(); };

            btnExport = new Button { Text = "⬇ Eksport CSV", Width = 120, Height = 26, Left = 166, Top = 7 };
            btnExport.Click += ExportToCsv;

            btnClose = new Button { Text = "Zamknij", Width = 90, Height = 26, Top = 7 };
            btnClose.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            btnClose.Left = panelBottom.Width - 100;
            btnClose.Click += (s, e) => Close();

            panelBottom.Controls.AddRange(new Control[] { btnReset, btnExport, btnClose });
            btnClose.Anchor = AnchorStyles.Right | AnchorStyles.Top;

            // --- Timer odświeżania ---
            refreshTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            refreshTimer.Tick += (s, e) => RefreshStats();
            refreshTimer.Start();

            this.Controls.AddRange(new Control[] { listView, panelBar, panelTop, panelBottom });
            this.FormClosed += (s, e) => refreshTimer.Stop();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024)        return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F2} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        private void RefreshStats()
        {
            var snapshot = TrafficStatsTracker.GetSnapshot();
            long totalBytes = snapshot.Sum(s => s.Sent + s.Recv);
            lblTotal.Text = $"Łączny ruch sesji: {FormatBytes(totalBytes)} | Aplikacji: {snapshot.Count}";

            listView.BeginUpdate();
            listView.Items.Clear();

            int rank = 1;
            foreach (var (app, sent, recv, conns, lastSeen) in snapshot)
            {
                string appName = string.IsNullOrEmpty(app) ? "(nieznana)" : Path.GetFileName(app);
                long total = sent + recv;

                var item = new ListViewItem(rank.ToString());
                item.SubItems.Add(appName);
                item.SubItems.Add(FormatBytes(sent));
                item.SubItems.Add(FormatBytes(recv));
                item.SubItems.Add(FormatBytes(total));
                item.SubItems.Add(conns.ToString());
                item.SubItems.Add(lastSeen.ToString("HH:mm:ss"));
                item.Tag = (sent, recv, total);
                item.ToolTipText = app;

                // Kolorowanie wg wielkości ruchu
                if (total > 100L * 1024 * 1024)      // > 100 MB
                    item.BackColor = Color.FromArgb(255, 220, 220);
                else if (total > 10L * 1024 * 1024)  // > 10 MB
                    item.BackColor = Color.FromArgb(255, 240, 210);
                else if (total > 1L * 1024 * 1024)   // > 1 MB
                    item.BackColor = Color.FromArgb(255, 255, 210);

                listView.Items.Add(item);
                rank++;
            }

            listView.EndUpdate();
        }

        private int _sortColumn = 4; // domyślnie: Łącznie
        private bool _sortAscending = false;

        private void OnColumnClick(object? sender, ColumnClickEventArgs e)
        {
            if (_sortColumn == e.Column)
                _sortAscending = !_sortAscending;
            else
            {
                _sortColumn = e.Column;
                _sortAscending = false;
            }
            listView.ListViewItemSorter = new TrafficListSorter(_sortColumn, _sortAscending);
            listView.Sort();
        }

        private void ExportToCsv(object? sender, EventArgs e)
        {
            using var sfd = new SaveFileDialog
            {
                Title = "Eksportuj statystyki",
                Filter = "Pliki CSV (*.csv)|*.csv",
                FileName = $"TinyWall_traffic_{DateTime.Now:yyyy-MM-dd_HH-mm}.csv"
            };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Aplikacja;Wysłano;Odebrano;Łącznie;Połączenia;Ostatnia aktywność");

                foreach (ListViewItem item in listView.Items)
                {
                    sb.AppendLine(string.Join(";",
                        item.SubItems.Cast<ListViewItem.ListViewSubItem>()
                            .Skip(1).Select(s => s.Text)));
                }

                File.WriteAllText(sfd.FileName, sb.ToString(), System.Text.Encoding.UTF8);
                MessageBox.Show(this, $"Zapisano do:\n{sfd.FileName}", "Eksport",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Błąd: {ex.Message}", "Błąd", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private sealed class TrafficListSorter : System.Collections.IComparer
        {
            private readonly int _col;
            private readonly bool _asc;
            public TrafficListSorter(int col, bool asc) { _col = col; _asc = asc; }

            public int Compare(object? x, object? y)
            {
                var a = (ListViewItem)x!;
                var b = (ListViewItem)y!;
                string ta = a.SubItems.Count > _col ? a.SubItems[_col].Text : "";
                string tb = b.SubItems.Count > _col ? b.SubItems[_col].Text : "";
                int cmp = string.Compare(ta, tb, StringComparison.OrdinalIgnoreCase);
                return _asc ? cmp : -cmp;
            }
        }
    }
}
