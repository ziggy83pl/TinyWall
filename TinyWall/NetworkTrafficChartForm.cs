// =====================================================================
// ULEPSZENIE: Wykres ruchu sieciowego w czasie rzeczywistym
// Animowany scrolling chart rysowany przez GDI+ (bez zewnętrznych lib).
// Odświeżanie co 1 sekundę, historia 60 sekund, download + upload.
// =====================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net.NetworkInformation;
using System.Windows.Forms;

namespace pylorak.TinyWall
{
    internal class NetworkTrafficChartForm : Form
    {
        // --- Dane ---
        private const int HISTORY_SECONDS = 60;
        private readonly Queue<long> _downloadHistory = new();
        private readonly Queue<long> _uploadHistory   = new();
        private long _lastBytesReceived;
        private long _lastBytesSent;
        private long _peakDownload;
        private long _peakUpload;

        // --- UI ---
        private readonly Panel _chartPanel;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly Label _lblDownload;
        private readonly Label _lblUpload;
        private readonly Label _lblPeak;
        private readonly Label _lblTotal;
        private long _totalReceived;
        private long _totalSent;

        // Kolory
        private static readonly Color ColorDownload = Color.FromArgb(0, 180, 100);   // zielony
        private static readonly Color ColorUpload   = Color.FromArgb(30, 120, 220);  // niebieski
        private static readonly Color ColorGrid     = Color.FromArgb(60, 60, 60);
        private static readonly Color ColorBg       = Color.FromArgb(20, 20, 30);

        internal NetworkTrafficChartForm()
        {
            this.Text = "TinyWall — Ruch sieciowy w czasie rzeczywistym";
            this.Size = new Size(820, 500);
            this.MinimumSize = new Size(600, 380);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(30, 30, 40);
            this.ForeColor = Color.White;
            this.Icon = Resources.Icons.firewall;

            // Inicjalizacja historii zerami
            for (int i = 0; i < HISTORY_SECONDS; i++)
            {
                _downloadHistory.Enqueue(0);
                _uploadHistory.Enqueue(0);
            }

            // Pobierz baseline
            GetCurrentNetworkBytes(out _lastBytesReceived, out _lastBytesSent);

            // --- Panel wykresów ---
            _chartPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = ColorBg
            };
            _chartPanel.Paint += OnChartPaint;

            // --- Panel statystyk (góra) ---
            var panelStats = new Panel
            {
                Dock = DockStyle.Top, Height = 55,
                BackColor = Color.FromArgb(25, 25, 35),
                Padding = new Padding(12, 6, 12, 6)
            };

            _lblDownload = MakeStatLabel("⬇ Download: 0 B/s", ColorDownload, 10);
            _lblUpload   = MakeStatLabel("⬆ Upload:   0 B/s", ColorUpload,  240);
            _lblPeak     = MakeStatLabel("Peak: 0 / 0",       Color.Orange,  470);
            _lblTotal    = MakeStatLabel("Łącznie: 0 / 0",    Color.Silver,  10);
            _lblTotal.Top = 32;

            panelStats.Controls.AddRange(new Control[] { _lblDownload, _lblUpload, _lblPeak, _lblTotal });

            // --- Panel dolny ---
            var panelBottom = new Panel { Dock = DockStyle.Bottom, Height = 36, BackColor = Color.FromArgb(25, 25, 35) };
            var lblLegend = new Label
            {
                Text = "  ■ Download    ■ Upload    | Ostatnie 60 sekund",
                ForeColor = Color.Silver, AutoSize = true, Top = 10, Left = 8,
                Font = new Font("Consolas", 9f)
            };
            var btnClose = new Button
            {
                Text = "Zamknij", Width = 80, Height = 26, Top = 5,
                Anchor = AnchorStyles.Right | AnchorStyles.Top,
                FlatStyle = FlatStyle.Flat, ForeColor = Color.White,
                BackColor = Color.FromArgb(60, 60, 80)
            };
            btnClose.Left = panelBottom.Width - 90;
            btnClose.Click += (s, e) => Close();
            btnClose.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            panelBottom.Controls.AddRange(new Control[] { lblLegend, btnClose });

            this.Controls.AddRange(new Control[] { _chartPanel, panelStats, panelBottom });

            // --- Timer odświeżania ---
            _timer = new System.Windows.Forms.Timer { Interval = 1000 };
            _timer.Tick += OnTick;
            _timer.Start();

            this.FormClosed += (s, e) => _timer.Stop();
            this.Resize += (s, e) => _chartPanel.Invalidate();
        }

        private static Label MakeStatLabel(string text, Color color, int left)
        {
            return new Label
            {
                Text = text, ForeColor = color, AutoSize = true,
                Top = 8, Left = left,
                Font = new Font("Consolas", 10f, FontStyle.Bold)
            };
        }

        // ---------------------------------------------------------------
        // Odczyt bieżącego ruchu sieciowego ze wszystkich interfejsów
        // ---------------------------------------------------------------
        private static void GetCurrentNetworkBytes(out long received, out long sent)
        {
            received = 0; sent = 0;
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var stats = ni.GetIPv4Statistics();
                    received += stats.BytesReceived;
                    sent     += stats.BytesSent;
                }
            }
            catch { }
        }

        private void OnTick(object? sender, EventArgs e)
        {
            GetCurrentNetworkBytes(out long curReceived, out long curSent);

            long downSpeed = Math.Max(0, curReceived - _lastBytesReceived);
            long upSpeed   = Math.Max(0, curSent - _lastBytesSent);

            _lastBytesReceived = curReceived;
            _lastBytesSent     = curSent;
            _totalReceived    += downSpeed;
            _totalSent        += upSpeed;

            // Dodaj do historii (FIFO)
            _downloadHistory.Enqueue(downSpeed);
            _uploadHistory.Enqueue(upSpeed);
            if (_downloadHistory.Count > HISTORY_SECONDS) _downloadHistory.Dequeue();
            if (_uploadHistory.Count   > HISTORY_SECONDS) _uploadHistory.Dequeue();

            _peakDownload = Math.Max(_peakDownload, downSpeed);
            _peakUpload   = Math.Max(_peakUpload,   upSpeed);

            // Aktualizuj etykiety
            _lblDownload.Text = $"⬇ Download: {FormatSpeed(downSpeed)}";
            _lblUpload.Text   = $"⬆ Upload:   {FormatSpeed(upSpeed)}";
            _lblPeak.Text     = $"Peak ⬇{FormatSpeed(_peakDownload)}  ⬆{FormatSpeed(_peakUpload)}";
            _lblTotal.Text    = $"Łącznie sesja: ⬇{FormatBytes(_totalReceived)}  ⬆{FormatBytes(_totalSent)}";

            _chartPanel.Invalidate();
        }

        // ---------------------------------------------------------------
        // Rysowanie wykresu — GDI+ scrolling line chart
        // ---------------------------------------------------------------
        private void OnChartPaint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = _chartPanel.Width;
            int h = _chartPanel.Height;
            int padL = 70, padR = 12, padT = 15, padB = 30;
            int chartW = w - padL - padR;
            int chartH = h - padT - padB;

            // Tło
            g.Clear(ColorBg);

            // Maksymalna wartość (oś Y)
            long maxVal = 1024; // minimum 1 KB/s
            foreach (var v in _downloadHistory) maxVal = Math.Max(maxVal, v);
            foreach (var v in _uploadHistory)   maxVal = Math.Max(maxVal, v);
            maxVal = (long)(maxVal * 1.2); // 20% margines

            // Siatka pozioma (5 linii)
            using var gridPen = new Pen(ColorGrid, 1) { DashStyle = DashStyle.Dot };
            using var axisFont = new Font("Consolas", 7.5f);
            using var axisBrush = new SolidBrush(Color.FromArgb(160, 160, 160));

            for (int i = 0; i <= 5; i++)
            {
                int y = padT + (int)(chartH * (1.0 - i / 5.0));
                g.DrawLine(gridPen, padL, y, padL + chartW, y);
                long val = (long)(maxVal * i / 5.0);
                g.DrawString(FormatSpeed(val), axisFont, axisBrush, 2, y - 8);
            }

            // Siatka pionowa (10 linii = co 6 sekund)
            for (int i = 0; i <= 10; i++)
            {
                int x = padL + (int)(chartW * i / 10.0);
                g.DrawLine(gridPen, x, padT, x, padT + chartH);
                int sec = HISTORY_SECONDS - (int)(HISTORY_SECONDS * i / 10.0);
                if (sec > 0)
                    g.DrawString($"-{sec}s", axisFont, axisBrush, x - 12, padT + chartH + 4);
            }

            // Ramka wykresu
            using var framePen = new Pen(Color.FromArgb(80, 80, 100), 1);
            g.DrawRectangle(framePen, padL, padT, chartW, chartH);

            // Rysuj dane download i upload
            DrawLine(g, _downloadHistory, ColorDownload, padL, padT, chartW, chartH, maxVal);
            DrawLine(g, _uploadHistory,   ColorUpload,   padL, padT, chartW, chartH, maxVal);

            // Legenda w rogu wykresu
            using var legendFont = new Font("Consolas", 8f, FontStyle.Bold);
            g.FillRectangle(new SolidBrush(Color.FromArgb(150, 0, 150, 70)),  padL + 8, padT + 6, 12, 3);
            g.DrawString("Download", legendFont, new SolidBrush(ColorDownload), padL + 24, padT + 2);
            g.FillRectangle(new SolidBrush(Color.FromArgb(150, 30, 120, 220)), padL + 8, padT + 20, 12, 3);
            g.DrawString("Upload",   legendFont, new SolidBrush(ColorUpload),   padL + 24, padT + 16);
        }

        private static void DrawLine(
            Graphics g, Queue<long> data, Color color,
            int padL, int padT, int chartW, int chartH, long maxVal)
        {
            var vals = new List<long>(data);
            if (vals.Count < 2) return;

            var points = new PointF[vals.Count];
            float stepX = (float)chartW / (HISTORY_SECONDS - 1);

            for (int i = 0; i < vals.Count; i++)
            {
                float x = padL + i * stepX;
                float y = padT + chartH - (float)(vals[i] * chartH) / maxVal;
                y = Math.Clamp(y, padT, padT + chartH);
                points[i] = new PointF(x, y);
            }

            // Wypełnienie pod linią (przezroczyste)
            var fillPoints = new PointF[points.Length + 2];
            fillPoints[0] = new PointF(points[0].X, padT + chartH);
            Array.Copy(points, 0, fillPoints, 1, points.Length);
            fillPoints[^1] = new PointF(points[^1].X, padT + chartH);

            using var fillBrush = new SolidBrush(Color.FromArgb(40, color));
            g.FillPolygon(fillBrush, fillPoints);

            // Linia
            using var pen = new Pen(color, 2f) { LineJoin = LineJoin.Round };
            g.DrawLines(pen, points);
        }

        private static string FormatSpeed(long bytesPerSec)
        {
            if (bytesPerSec < 1024)             return $"{bytesPerSec} B/s";
            if (bytesPerSec < 1024 * 1024)      return $"{bytesPerSec / 1024.0:F1} KB/s";
            return $"{bytesPerSec / (1024.0 * 1024):F2} MB/s";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024)              return $"{bytes} B";
            if (bytes < 1024 * 1024)       return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F2} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}
