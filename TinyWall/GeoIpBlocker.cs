// =====================================================================
// ULEPSZENIE: GeoIP Blocking
// Blokuje połączenia sieciowe z/do wybranych krajów.
// Używa darmowego API ip-api.com (bez klucza, limit 45 req/min).
// Cache wyników na dysku — po restarcie nie odpytuje API ponownie.
// =====================================================================
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace pylorak.TinyWall
{
    // ---------------------------------------------------------------
    // Silnik GeoIP — lookup + cache + blokowanie
    // ---------------------------------------------------------------
    public sealed class GeoIpService : IDisposable
    {
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(5),
            DefaultRequestHeaders = { { "User-Agent", "TinyWall-GeoIP/1.0" } }
        };

        // Cache IP → kod kraju (np. "PL", "RU", "CN")
        private readonly ConcurrentDictionary<string, string> _cache = new();
        private readonly string _cacheFile;
        private readonly SemaphoreSlim _rateLimiter = new(1, 1); // max 1 req na raz
        private DateTime _lastRequest = DateTime.MinValue;

        // Kraje do zablokowania (kody ISO 3166-1 alpha-2)
        public HashSet<string> BlockedCountries { get; } = new(StringComparer.OrdinalIgnoreCase);

        public static readonly GeoIpService Instance = new();

        private GeoIpService()
        {
            _cacheFile = Path.Combine(Utils.AppDataPath, "geoip_cache.json");
            LoadCache();
        }

        /// <summary>
        /// Zwraca kod kraju dla podanego IP (async, z cache).
        /// Zwraca "" jeśli nieznany lub lokalny.
        /// </summary>
        public async Task<string> GetCountryAsync(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return "";
            if (IsPrivateIp(ip)) return "LOCAL";

            if (_cache.TryGetValue(ip, out string? cached))
                return cached;

            // Rate limit: max 45 req/min → min 1.4s między requestami
            await _rateLimiter.WaitAsync();
            try
            {
                var elapsed = DateTime.Now - _lastRequest;
                if (elapsed.TotalMilliseconds < 1400)
                    await Task.Delay(1400 - (int)elapsed.TotalMilliseconds);

                string url = $"http://ip-api.com/json/{ip}?fields=countryCode,status";
                string json = await Http.GetStringAsync(url);
                _lastRequest = DateTime.Now;

                using var doc = JsonDocument.Parse(json);
                string status = doc.RootElement.GetProperty("status").GetString() ?? "";
                if (status == "success")
                {
                    string country = doc.RootElement.GetProperty("countryCode").GetString() ?? "";
                    _cache[ip] = country;
                    SaveCacheEntry(ip, country);
                    return country;
                }
            }
            catch { /* API niedostępne — nie blokuj */ }
            finally
            {
                _rateLimiter.Release();
            }

            return "";
        }

        /// <summary>
        /// Sprawdza czy dane IP powinno być zablokowane.
        /// </summary>
        public async Task<bool> ShouldBlockAsync(string ip)
        {
            if (BlockedCountries.Count == 0) return false;
            string country = await GetCountryAsync(ip);
            return !string.IsNullOrEmpty(country) && BlockedCountries.Contains(country);
        }

        private static bool IsPrivateIp(string ip)
        {
            if (!IPAddress.TryParse(ip, out var addr)) return true;
            byte[] b = addr.GetAddressBytes();
            if (b.Length != 4) return false;
            return (b[0] == 10)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 127)
                || (b[0] == 169 && b[1] == 254);
        }

        private void LoadCache()
        {
            try
            {
                if (!File.Exists(_cacheFile)) return;
                string json = File.ReadAllText(_cacheFile, Encoding.UTF8);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict == null) return;
                foreach (var kv in dict)
                    _cache[kv.Key] = kv.Value;
            }
            catch { }
        }

        private void SaveCacheEntry(string ip, string country)
        {
            Task.Run(() =>
            {
                try
                {
                    var dict = new Dictionary<string, string>(_cache);
                    string json = JsonSerializer.Serialize(dict);
                    File.WriteAllText(_cacheFile, json, Encoding.UTF8);
                }
                catch { }
            });
        }

        public void Dispose()
        {
            _rateLimiter.Dispose();
        }
    }

    // ---------------------------------------------------------------
    // Okno konfiguracji GeoIP Blockera
    // ---------------------------------------------------------------
    internal class GeoIpBlockerForm : Form
    {
        // Pełna lista krajów: kod → nazwa
        private static readonly (string Code, string Name)[] AllCountries =
        {
            ("AF","Afganistan"), ("AL","Albania"), ("DZ","Algieria"), ("AD","Andora"),
            ("AO","Angola"), ("AR","Argentyna"), ("AM","Armenia"), ("AU","Australia"),
            ("AT","Austria"), ("AZ","Azerbejdżan"), ("BS","Bahamy"), ("BH","Bahrajn"),
            ("BD","Bangladesz"), ("BY","Białoruś"), ("BE","Belgia"), ("BZ","Belize"),
            ("BJ","Benin"), ("BT","Bhutan"), ("BO","Boliwia"), ("BA","Bośnia i Hercegowina"),
            ("BW","Botswana"), ("BR","Brazylia"), ("BN","Brunei"), ("BG","Bułgaria"),
            ("BF","Burkina Faso"), ("BI","Burundi"), ("CV","Wyspy Zielonego Przylądka"),
            ("KH","Kambodża"), ("CM","Kamerun"), ("CA","Kanada"), ("CF","Republika Środkowoafrykańska"),
            ("TD","Czad"), ("CL","Chile"), ("CN","Chiny"), ("CO","Kolumbia"),
            ("KM","Komory"), ("CG","Kongo"), ("CD","Dem. Rep. Konga"), ("CR","Kostaryka"),
            ("HR","Chorwacja"), ("CU","Kuba"), ("CY","Cypr"), ("CZ","Czechy"),
            ("DK","Dania"), ("DJ","Dżibuti"), ("DO","Dominikana"), ("EC","Ekwador"),
            ("EG","Egipt"), ("SV","Salwador"), ("GQ","Gwinea Równikowa"), ("ER","Erytrea"),
            ("EE","Estonia"), ("SZ","Eswatini"), ("ET","Etiopia"), ("FJ","Fidżi"),
            ("FI","Finlandia"), ("FR","Francja"), ("GA","Gabon"), ("GM","Gambia"),
            ("GE","Gruzja"), ("DE","Niemcy"), ("GH","Ghana"), ("GR","Grecja"),
            ("GT","Gwatemala"), ("GN","Gwinea"), ("GW","Gwinea Bissau"), ("GY","Gujana"),
            ("HT","Haiti"), ("HN","Honduras"), ("HU","Węgry"), ("IS","Islandia"),
            ("IN","Indie"), ("ID","Indonezja"), ("IR","Iran"), ("IQ","Irak"),
            ("IE","Irlandia"), ("IL","Izrael"), ("IT","Włochy"), ("JM","Jamajka"),
            ("JP","Japonia"), ("JO","Jordania"), ("KZ","Kazachstan"), ("KE","Kenia"),
            ("KP","Korea Północna"), ("KR","Korea Południowa"), ("KW","Kuwejt"),
            ("KG","Kirgistan"), ("LA","Laos"), ("LV","Łotwa"), ("LB","Liban"),
            ("LS","Lesotho"), ("LR","Liberia"), ("LY","Libia"), ("LI","Liechtenstein"),
            ("LT","Litwa"), ("LU","Luksemburg"), ("MG","Madagaskar"), ("MW","Malawi"),
            ("MY","Malezja"), ("MV","Malediwy"), ("ML","Mali"), ("MT","Malta"),
            ("MR","Mauretania"), ("MX","Meksyk"), ("MD","Mołdawia"), ("MC","Monako"),
            ("MN","Mongolia"), ("ME","Czarnogóra"), ("MA","Maroko"), ("MZ","Mozambik"),
            ("MM","Myanmar"), ("NA","Namibia"), ("NP","Nepal"), ("NL","Holandia"),
            ("NZ","Nowa Zelandia"), ("NI","Nikaragua"), ("NE","Niger"), ("NG","Nigeria"),
            ("MK","Macedonia Północna"), ("NO","Norwegia"), ("OM","Oman"),
            ("PK","Pakistan"), ("PA","Panama"), ("PG","Papua Nowa Gwinea"), ("PY","Paragwaj"),
            ("PE","Peru"), ("PH","Filipiny"), ("PL","Polska"), ("PT","Portugalia"),
            ("QA","Katar"), ("RO","Rumunia"), ("RU","Rosja"), ("RW","Rwanda"),
            ("SA","Arabia Saudyjska"), ("SN","Senegal"), ("RS","Serbia"), ("SL","Sierra Leone"),
            ("SG","Singapur"), ("SK","Słowacja"), ("SI","Słowenia"), ("SO","Somalia"),
            ("ZA","Republika Południowej Afryki"), ("SS","Sudan Południowy"), ("ES","Hiszpania"),
            ("LK","Sri Lanka"), ("SD","Sudan"), ("SR","Surinam"), ("SE","Szwecja"),
            ("CH","Szwajcaria"), ("SY","Syria"), ("TW","Tajwan"), ("TJ","Tadżykistan"),
            ("TZ","Tanzania"), ("TH","Tajlandia"), ("TL","Timor Wschodni"), ("TG","Togo"),
            ("TT","Trynidad i Tobago"), ("TN","Tunezja"), ("TR","Turcja"),
            ("TM","Turkmenistan"), ("UG","Uganda"), ("UA","Ukraina"), ("AE","ZEA"),
            ("GB","Wielka Brytania"), ("US","USA"), ("UY","Urugwaj"), ("UZ","Uzbekistan"),
            ("VE","Wenezuela"), ("VN","Wietnam"), ("YE","Jemen"), ("ZM","Zambia"),
            ("ZW","Zimbabwe"),
        };

        private CheckedListBox listCountries;
        private TextBox txtSearch;
        private Button btnApply;
        private Button btnClose;
        private Button btnBlockHighRisk;
        private Label lblStatus;

        internal GeoIpBlockerForm()
        {
            BuildUI();
            Utils.SetRightToLeft(this);
            this.Icon = Resources.Icons.firewall;
            Utils.ApplyDarkModeIfEnabled(this);
            LoadCurrentSettings();
        }

        private void BuildUI()
        {
            this.Text = "TinyWall — Blokowanie GeoIP (według kraju)";
            this.Size = new Size(520, 600);
            this.MinimumSize = new Size(420, 450);
            this.StartPosition = FormStartPosition.CenterScreen;

            var lblHeader = new Label
            {
                Text = "Zaznacz kraje, z których ruch ma być BLOKOWANY:",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Dock = DockStyle.Top, Height = 30,
                Padding = new Padding(8, 6, 0, 0)
            };

            var panelSearch = new Panel { Dock = DockStyle.Top, Height = 36 };
            var lblSearch = new Label { Text = "Szukaj:", AutoSize = true, Top = 10, Left = 8 };
            txtSearch = new TextBox { Left = 65, Top = 7, Width = 200, PlaceholderText = "Nazwa kraju..." };
            txtSearch.TextChanged += FilterList;
            panelSearch.Controls.AddRange(new Control[] { lblSearch, txtSearch });

            listCountries = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                Font = new Font("Segoe UI", 9.5f)
            };
            PopulateList("");

            lblStatus = new Label
            {
                Dock = DockStyle.Top, Height = 24,
                Text = "Zaznaczono: 0 krajów",
                ForeColor = Color.DarkRed,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Padding = new Padding(8, 4, 0, 0)
            };
            listCountries.ItemCheck += (s, e) =>
            {
                int cnt = listCountries.CheckedItems.Count;
                if (e.NewValue == CheckState.Checked) cnt++;
                else if (e.NewValue == CheckState.Unchecked) cnt--;
                lblStatus.Text = $"Zaznaczono: {cnt} krajów do blokowania";
                lblStatus.ForeColor = cnt > 0 ? Color.DarkRed : Color.DarkGreen;
            };

            var panelBottom = new Panel { Dock = DockStyle.Bottom, Height = 80 };

            btnBlockHighRisk = new Button
            {
                Text = "⚠ Zaznacz kraje wysokiego ryzyka",
                Left = 8, Top = 8, Width = 230, Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(255, 230, 200)
            };
            btnBlockHighRisk.Click += SelectHighRisk;

            var btnClear = new Button
            {
                Text = "Wyczyść wszystko", Left = 248, Top = 8, Width = 130, Height = 28
            };
            btnClear.Click += (s, e) =>
            {
                for (int i = 0; i < listCountries.Items.Count; i++)
                    listCountries.SetItemChecked(i, false);
            };

            btnApply = new Button
            {
                Text = "✔ Zastosuj", Left = 8, Top = 42, Width = 120, Height = 30,
                BackColor = Color.FromArgb(0, 120, 215), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnApply.Click += ApplySettings;

            btnClose = new Button { Text = "Zamknij", Left = 140, Top = 42, Width = 90, Height = 30 };
            btnClose.Click += (s, e) => Close();

            panelBottom.Controls.AddRange(new Control[] { btnBlockHighRisk, btnClear, btnApply, btnClose });

            this.Controls.AddRange(new Control[] { listCountries, lblStatus, panelSearch, lblHeader, panelBottom });
        }

        private void PopulateList(string filter)
        {
            var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in listCountries.CheckedItems)
            {
                string code = ((string)item)[..2];
                blocked.Add(code);
            }

            listCountries.Items.Clear();
            foreach (var (code, name) in AllCountries)
            {
                if (!string.IsNullOrEmpty(filter) &&
                    !name.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                    !code.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;
                int idx = listCountries.Items.Add($"{code}  {name}");
                if (blocked.Contains(code) || GeoIpService.Instance.BlockedCountries.Contains(code))
                    listCountries.SetItemChecked(idx, true);
            }
        }

        private void FilterList(object? sender, EventArgs e) => PopulateList(txtSearch.Text.Trim());

        private void SelectHighRisk(object? sender, EventArgs e)
        {
            // Kraje często źródłem ataków wg raportów cybersecurity
            var highRisk = new HashSet<string> { "CN", "RU", "KP", "IR", "SY", "CU", "VN", "BY" };
            for (int i = 0; i < listCountries.Items.Count; i++)
            {
                string code = ((string)listCountries.Items[i]!)[..2];
                if (highRisk.Contains(code))
                    listCountries.SetItemChecked(i, true);
            }
        }

        private void LoadCurrentSettings()
        {
            for (int i = 0; i < listCountries.Items.Count; i++)
            {
                string code = ((string)listCountries.Items[i]!)[..2];
                if (GeoIpService.Instance.BlockedCountries.Contains(code))
                    listCountries.SetItemChecked(i, true);
            }
        }

        private void ApplySettings(object? sender, EventArgs e)
        {
            GeoIpService.Instance.BlockedCountries.Clear();
            foreach (var item in listCountries.CheckedItems)
            {
                string code = ((string)item)[..2];
                GeoIpService.Instance.BlockedCountries.Add(code);
            }

            // Zapisz ustawienia
            SaveGeoIpSettings();

            string msg = GeoIpService.Instance.BlockedCountries.Count == 0
                ? "GeoIP blocking wyłączony (brak zaznaczonych krajów)."
                : $"Blokowanie aktywne dla {GeoIpService.Instance.BlockedCountries.Count} krajów:\n" +
                  string.Join(", ", GeoIpService.Instance.BlockedCountries);

            MessageBox.Show(this, msg, "GeoIP Blocking", MessageBoxButtons.OK, MessageBoxIcon.Information);

            Utils.Log(
                $"[AUDIT] {DateTime.Now:yyyy-MM-dd HH:mm:ss} | GeoIP blocking zaktualizowany: {msg}",
                Utils.LOG_ID_GUI
            );

            Close();
        }

        private static void SaveGeoIpSettings()
        {
            try
            {
                string path = Path.Combine(Utils.AppDataPath, "geoip_settings.json");
                string json = JsonSerializer.Serialize(new List<string>(GeoIpService.Instance.BlockedCountries));
                File.WriteAllText(path, json, Encoding.UTF8);
            }
            catch { }
        }

        public static void LoadGeoIpSettings()
        {
            try
            {
                string path = Path.Combine(Utils.AppDataPath, "geoip_settings.json");
                if (!File.Exists(path)) return;
                string json = File.ReadAllText(path, Encoding.UTF8);
                var list = JsonSerializer.Deserialize<List<string>>(json);
                if (list == null) return;
                GeoIpService.Instance.BlockedCountries.Clear();
                foreach (var code in list)
                    GeoIpService.Instance.BlockedCountries.Add(code);
            }
            catch { }
        }
    }
}
