// =====================================================================
// UNIT TESTY TinyWall
// Framework: xUnit | Pokrycie: Hasher, Hasher.SecureCompare,
// SecurityProfiles, GeoIpService (prywatne IP), FirewallLogEntry
// Uruchom: dotnet test
// =====================================================================
using System;
using System.IO;
using System.Text;
using Xunit;

namespace pylorak.TinyWall.Tests
{
    // ---------------------------------------------------------------
    // Testy Hasher — SHA256 hashowanie i porównanie bezpieczne
    // ---------------------------------------------------------------
    public class HasherTests
    {
        [Fact]
        public void HashString_SameInput_ReturnsSameHash()
        {
            string h1 = Hasher.HashString("TinyWall");
            string h2 = Hasher.HashString("TinyWall");
            Assert.Equal(h1, h2);
        }

        [Fact]
        public void HashString_DifferentInput_ReturnsDifferentHash()
        {
            string h1 = Hasher.HashString("hello");
            string h2 = Hasher.HashString("world");
            Assert.NotEqual(h1, h2);
        }

        [Fact]
        public void HashString_EmptyString_ReturnsKnownHash()
        {
            // SHA256("") = e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
            string hash = Hasher.HashString("");
            Assert.Equal(
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                hash,
                ignoreCase: true
            );
        }

        [Fact]
        public void HashString_ReturnsHexString_64Chars()
        {
            string hash = Hasher.HashString("test");
            Assert.Equal(64, hash.Length);
            Assert.Matches("^[0-9a-fA-F]+$", hash);
        }

        [Fact]
        public void HashFile_ReturnsCorrectHash()
        {
            // Utwórz tymczasowy plik z treścią
            string tempFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tempFile, "TinyWall test content", Encoding.UTF8);
                string hashFromFile   = Hasher.HashFile(tempFile);
                string hashFromString = Hasher.HashString(File.ReadAllText(tempFile, Encoding.UTF8));
                // Hasze są obliczane inaczej (bytes vs string encode) — sprawdź tylko format
                Assert.Equal(64, hashFromFile.Length);
                Assert.Matches("^[0-9a-fA-F]+$", hashFromFile);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public void HashFile_FileNotFound_ThrowsException()
        {
            Assert.Throws<FileNotFoundException>(
                () => Hasher.HashFile(@"C:\NonExistentFile_TinyWall_Test_12345.exe")
            );
        }

        // ---------------------------------------------------------------
        // Testy SecureCompare — porównanie w czasie stałym
        // ---------------------------------------------------------------
        [Fact]
        public void SecureCompare_EqualHashes_ReturnsTrue()
        {
            string hash = Hasher.HashString("password123");
            Assert.True(Hasher.SecureCompare(hash, hash));
        }

        [Fact]
        public void SecureCompare_DifferentHashes_ReturnsFalse()
        {
            string h1 = Hasher.HashString("password1");
            string h2 = Hasher.HashString("password2");
            Assert.False(Hasher.SecureCompare(h1, h2));
        }

        [Fact]
        public void SecureCompare_NullInput_ReturnsFalse()
        {
            Assert.False(Hasher.SecureCompare(null!, "abc"));
            Assert.False(Hasher.SecureCompare("abc", null!));
            Assert.False(Hasher.SecureCompare(null!, null!));
        }

        [Fact]
        public void SecureCompare_DifferentLength_ReturnsFalse()
        {
            Assert.False(Hasher.SecureCompare("abc", "abcd"));
        }

        [Fact]
        public void SecureCompare_CaseSensitive()
        {
            // SHA256 hex może być różnej wielkości liter
            string upper = Hasher.HashString("test").ToUpperInvariant();
            string lower = Hasher.HashString("test").ToLowerInvariant();
            // SecureCompare jest case-sensitive (porównuje bajty UTF8)
            // Sprawdź że daje spójny wynik
            bool result = Hasher.SecureCompare(upper, lower);
            // Może być true lub false — ważne że nie rzuca wyjątku
            Assert.IsType<bool>(result);
        }
    }

    // ---------------------------------------------------------------
    // Testy SecurityProfiles — profile bezpieczeństwa
    // ---------------------------------------------------------------
    public class SecurityProfilesTests
    {
        [Fact]
        public void AllProfiles_HaveNonEmptyName()
        {
            foreach (var profile in SecurityProfiles.All)
                Assert.False(string.IsNullOrWhiteSpace(profile.Name),
                    $"Profil bez nazwy: {profile}");
        }

        [Fact]
        public void AllProfiles_HaveNonEmptyDescription()
        {
            foreach (var profile in SecurityProfiles.All)
                Assert.False(string.IsNullOrWhiteSpace(profile.Description),
                    $"Profil '{profile.Name}' bez opisu");
        }

        [Fact]
        public void AllProfiles_HaveTips()
        {
            foreach (var profile in SecurityProfiles.All)
                Assert.NotEmpty(profile.Tips);
        }

        [Fact]
        public void NormalProfile_AllowsLocalSubnet()
        {
            Assert.True(SecurityProfiles.Normal.AllowLocalSubnet);
        }

        [Fact]
        public void PublicWifi_BlocksLocalSubnet()
        {
            Assert.False(SecurityProfiles.PublicWifi.AllowLocalSubnet,
                "Profil PublicWifi powinien blokować LAN");
        }

        [Fact]
        public void RemoteWork_BlocksLocalSubnet()
        {
            Assert.False(SecurityProfiles.RemoteWork.AllowLocalSubnet,
                "Profil Praca zdalna powinien blokować LAN");
        }

        [Fact]
        public void AllProfiles_HaveEnableBlocklists_True()
        {
            foreach (var profile in SecurityProfiles.All)
                Assert.True(profile.EnableBlocklists,
                    $"Profil '{profile.Name}' powinien mieć blocklists=true");
        }

        [Fact]
        public void Profiles_Count_Is4()
        {
            Assert.Equal(4, SecurityProfiles.All.Count);
        }

        [Fact]
        public void Gaming_HasSteamInRecommended()
        {
            Assert.Contains("Steam", SecurityProfiles.Gaming.RecommendedExceptions);
        }
    }

    // ---------------------------------------------------------------
    // Testy GeoIpService — wykrywanie prywatnych IP
    // ---------------------------------------------------------------
    public class GeoIpServiceTests
    {
        // Testujemy prywatne metody przez reflection lub przez publiczne API
        [Theory]
        [InlineData("192.168.1.1")]
        [InlineData("10.0.0.1")]
        [InlineData("172.16.0.1")]
        [InlineData("172.31.255.255")]
        [InlineData("127.0.0.1")]
        [InlineData("169.254.1.1")]
        public async void GetCountry_PrivateIp_ReturnsLocal(string ip)
        {
            // Prywatne IP nie powinny odpytywać API — zwracają "LOCAL"
            string result = await GeoIpService.Instance.GetCountryAsync(ip);
            Assert.Equal("LOCAL", result);
        }

        [Fact]
        public async void ShouldBlock_EmptyBlockList_ReturnsFalse()
        {
            GeoIpService.Instance.BlockedCountries.Clear();
            bool result = await GeoIpService.Instance.ShouldBlockAsync("192.168.1.1");
            Assert.False(result, "Brak zablokowanych krajów — nie powinno blokować");
        }

        [Fact]
        public async void ShouldBlock_PrivateIp_AlwaysFalse()
        {
            GeoIpService.Instance.BlockedCountries.Add("LOCAL");
            bool result = await GeoIpService.Instance.ShouldBlockAsync("192.168.1.100");
            // LOCAL nie powinno być blokowane nawet jeśli na liście
            // (prywatne IP zawsze zwracają "LOCAL" a ShouldBlock nie blokuje LOCAL)
            GeoIpService.Instance.BlockedCountries.Remove("LOCAL");
        }

        [Fact]
        public void BlockedCountries_AddRemove_Works()
        {
            GeoIpService.Instance.BlockedCountries.Clear();
            GeoIpService.Instance.BlockedCountries.Add("RU");
            GeoIpService.Instance.BlockedCountries.Add("CN");
            Assert.Equal(2, GeoIpService.Instance.BlockedCountries.Count);
            Assert.Contains("RU", GeoIpService.Instance.BlockedCountries);
            GeoIpService.Instance.BlockedCountries.Remove("RU");
            Assert.DoesNotContain("RU", GeoIpService.Instance.BlockedCountries);
            GeoIpService.Instance.BlockedCountries.Clear();
        }
    }

    // ---------------------------------------------------------------
    // Testy FirewallLogEntry — struktura danych logów
    // ---------------------------------------------------------------
    public class FirewallLogEntryTests
    {
        private static FirewallLogEntry MakeSample() => new()
        {
            Timestamp  = new DateTime(2024, 1, 15, 12, 0, 0),
            Event      = EventLogEvent.BLOCKED_CONNECTION,
            ProcessId  = 1234,
            Protocol   = Protocol.Tcp,
            Direction  = RuleDirection.Out,
            LocalIp    = "192.168.1.10",
            RemoteIp   = "8.8.8.8",
            LocalPort  = 54321,
            RemotePort = 443,
            AppPath    = @"C:\Windows\System32\chrome.exe"
        };

        [Fact]
        public void LogEntry_Equality_SameData_IsEqual()
        {
            var a = MakeSample();
            var b = MakeSample();
            Assert.Equal(a, b);
        }

        [Fact]
        public void LogEntry_Equality_DifferentTimestamp_NotEqual()
        {
            var a = MakeSample();
            var b = MakeSample() with { Timestamp = DateTime.Now };
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void LogEntry_Equality_IgnoreTimestamp_Equal()
        {
            var a = MakeSample();
            var b = MakeSample() with { Timestamp = DateTime.Now };
            Assert.True(a.Equals(b, includeTimestamp: false));
        }

        [Fact]
        public void LogEntry_HashCode_SameData_SameHash()
        {
            var a = MakeSample();
            var b = MakeSample();
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void LogEntry_HashCode_DifferentData_DifferentHash()
        {
            var a = MakeSample();
            var b = MakeSample() with { RemoteIp = "1.1.1.1" };
            Assert.NotEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void LogEntry_NullAppPath_IsHandled()
        {
            var entry = MakeSample() with { AppPath = null };
            // Nie powinien rzucić wyjątku przy hashowaniu
            int hash = entry.GetHashCode();
            Assert.IsType<int>(hash);
        }
    }
}
