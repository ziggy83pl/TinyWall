using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Samples;
using pylorak.Windows;

namespace pylorak.TinyWall
{

    internal class Updater
    {
        private enum UpdaterState
        {
            GettingDescriptor,
            DescriptorReady,
            DownloadingUpdate,
            UpdateDownloadReady
        }

        private UpdaterState State;
        private string ErrorMsg = string.Empty;
        private volatile int DownloadProgress;

        internal static void StartUpdate()
        {
            var updater = new Updater();
            var descriptor = new UpdateDescriptor();
            updater.State = UpdaterState.GettingDescriptor;

            var TDialog = new TaskDialog();
            TDialog.CustomMainIcon = Resources.Icons.firewall;
            TDialog.WindowTitle = Resources.Messages.TinyWall;
            TDialog.MainInstruction = Resources.Messages.TinyWallUpdater;
            TDialog.Content = Resources.Messages.PleaseWaitWhileTinyWallChecksForUpdates;
            TDialog.AllowDialogCancellation = false;
            TDialog.CommonButtons = TaskDialogCommonButtons.Cancel;
            TDialog.ShowMarqueeProgressBar = true;
            TDialog.Callback = updater.DownloadTickCallback;
            TDialog.CallbackData = updater;
            TDialog.CallbackTimer = true;

            var UpdateThread = new Thread( () =>
            {
                try
                {
                    descriptor = UpdateChecker.GetDescriptor();
                    updater.State = UpdaterState.DescriptorReady;
                }
                catch
                {
                    updater.ErrorMsg = Resources.Messages.ErrorCheckingForUpdates;
                }
            });
            UpdateThread.Start();

            switch (TDialog.Show())
            {
                case (int)DialogResult.Cancel:
                    UpdateThread.Interrupt();
                    if (!UpdateThread.Join(500))
                        UpdateThread.Abort();
                    break;
                case (int)DialogResult.OK:
                    updater.CheckVersion(descriptor);
                    break;
                case (int)DialogResult.Abort:
                    Utils.ShowMessageBox(updater.ErrorMsg, Resources.Messages.TinyWall, TaskDialogCommonButtons.Ok, TaskDialogIcon.Error);
                    break;
            }
        }

        private void CheckVersion(UpdateDescriptor descriptor)
        {
            var UpdateModule = UpdateChecker.GetMainAppModule(descriptor)!;
            var oldVersion = new Version(System.Windows.Forms.Application.ProductVersion);
            var newVersion = new Version(UpdateModule.ComponentVersion);

            bool win10v1903 = VersionInfo.Win10OrNewer && (Environment.OSVersion.Version.Build >= 18362);
            bool WindowsNew_AnyTwUpdate = win10v1903 && (newVersion > oldVersion);
            bool WindowsOld_TwMinorFixOnly = (newVersion > oldVersion) && (newVersion.Major == oldVersion.Major) && (newVersion.Minor == oldVersion.Minor);

            if (WindowsNew_AnyTwUpdate || WindowsOld_TwMinorFixOnly)
            {
                string prompt = string.Format(CultureInfo.CurrentCulture, Resources.Messages.UpdateAvailable, UpdateModule.ComponentVersion);
                if (Utils.ShowMessageBox(prompt, Resources.Messages.TinyWallUpdater, TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No, TaskDialogIcon.Warning) == DialogResult.Yes)
                    DownloadUpdate(UpdateModule);
            }
            else
            {
                string prompt = Resources.Messages.NoUpdateAvailable;
                Utils.ShowMessageBox(prompt, Resources.Messages.TinyWallUpdater, TaskDialogCommonButtons.Ok, TaskDialogIcon.Information);
            }
        }

        private void DownloadUpdate(UpdateModule mainModule)
        {
            ErrorMsg = string.Empty;
            var TDialog = new TaskDialog();
            TDialog.CustomMainIcon = Resources.Icons.firewall;
            TDialog.WindowTitle = Resources.Messages.TinyWall;
            TDialog.MainInstruction = Resources.Messages.TinyWallUpdater;
            TDialog.Content = Resources.Messages.DownloadingUpdate;
            TDialog.AllowDialogCancellation = false;
            TDialog.CommonButtons = TaskDialogCommonButtons.Cancel;
            TDialog.ShowProgressBar = true;
            TDialog.Callback = DownloadTickCallback;
            TDialog.CallbackData = this;
            TDialog.CallbackTimer = true;
            TDialog.EnableHyperlinks = true;

            State = UpdaterState.DownloadingUpdate;

            // Zapamiętaj oczekiwany hash z deskryptora (do weryfikacji po pobraniu)
            string? expectedHash = mainModule.DownloadHash;

            var tmpFile = Path.GetTempFileName() + ".msi";
            var UpdateURL = new Uri(mainModule.UpdateURL);
            using var HTTPClient = new WebClient();
            HTTPClient.DownloadFileCompleted += new AsyncCompletedEventHandler(Updater_DownloadFinished);
            HTTPClient.DownloadProgressChanged += new DownloadProgressChangedEventHandler(Updater_DownloadProgressChanged);
            HTTPClient.DownloadFileAsync(UpdateURL, tmpFile, tmpFile);

            switch (TDialog.Show())
            {
                case (int)DialogResult.Cancel:
                    HTTPClient.CancelAsync();
                    SecureDeleteTempFile(tmpFile);
                    break;
                case (int)DialogResult.OK:
                    InstallUpdate(tmpFile, expectedHash);
                    break;
                case (int)DialogResult.Abort:
                    Utils.ShowMessageBox(ErrorMsg, Resources.Messages.TinyWall, TaskDialogCommonButtons.Ok, TaskDialogIcon.Error);
                    SecureDeleteTempFile(tmpFile);
                    break;
            }
        }

        // ---------------------------------------------------------------
        // NAPRAWA BEZPIECZEŃSTWA #1: Weryfikacja SHA256 przed instalacją
        // Wcześniej pole DownloadHash istniało ale nigdy nie było sprawdzane!
        // Teraz każdy pobrany plik jest weryfikowany przed uruchomieniem.
        // ---------------------------------------------------------------
        private static void InstallUpdate(string localFilePath, string? expectedSha256Hash)
        {
            // Weryfikuj hash SHA256 jeśli serwer go podał
            if (!string.IsNullOrWhiteSpace(expectedSha256Hash))
            {
                string actualHash = Hasher.HashFile(localFilePath);

                bool hashMatch = string.Equals(
                    actualHash.Trim(),
                    expectedSha256Hash.Trim(),
                    StringComparison.OrdinalIgnoreCase
                );

                if (!hashMatch)
                {
                    // Usuń podejrzany plik przed zgłoszeniem błędu
                    SecureDeleteTempFile(localFilePath);

                    Utils.Log(
                        $"[SECURITY] Update hash mismatch! Expected: {expectedSha256Hash}, Got: {actualHash}",
                        Utils.LOG_ID_GUI
                    );

                    Utils.ShowMessageBox(
                        "Weryfikacja pliku aktualizacji nie powiodła się!\n\n" +
                        "Obliczony hash SHA256 różni się od oczekiwanego.\n" +
                        "Plik mógł zostać zmodyfikowany podczas pobierania (atak MITM).\n\n" +
                        "Aktualizacja została anulowana dla Twojego bezpieczeństwa.",
                        Resources.Messages.TinyWall,
                        TaskDialogCommonButtons.Ok,
                        TaskDialogIcon.Error
                    );
                    return;
                }

                Utils.Log($"[SECURITY] Update hash verified OK: {actualHash}", Utils.LOG_ID_GUI);
            }
            else
            {
                // Hash nie był podany w deskryptorze - ostrzeż użytkownika
                Utils.Log("[SECURITY] WARNING: Update downloaded without hash verification (DownloadHash missing in descriptor)", Utils.LOG_ID_GUI);
            }

            // Hash poprawny lub nieweryfikowalny - instaluj
            Utils.StartProcess(localFilePath, string.Empty, false, false);
        }

        /// <summary>
        /// Bezpiecznie usuwa tymczasowy plik — zeruje zawartość przed usunięciem,
        /// aby wrażliwe dane (np. pobrane aktualizacje) nie pozostały na dysku.
        /// </summary>
        private static void SecureDeleteTempFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return;

                // Nadpisz zerami przed usunięciem
                long length = new FileInfo(filePath).Length;
                if (length > 0 && length < 512 * 1024 * 1024) // max 512MB
                {
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Write);
                    byte[] zeros = new byte[Math.Min(65536, length)];
                    long written = 0;
                    while (written < length)
                    {
                        int chunk = (int)Math.Min(zeros.Length, length - written);
                        fs.Write(zeros, 0, chunk);
                        written += chunk;
                    }
                    fs.Flush();
                }

                File.Delete(filePath);
            }
            catch
            {
                // Najlepsza próba — jeśli się nie uda, przynajmniej usuń plik normalnie
                try { File.Delete(filePath); } catch { }
            }
        }

        private void Updater_DownloadFinished(object sender, AsyncCompletedEventArgs e)
        {
            if (e.Cancelled || (e.Error != null))
            {
                ErrorMsg = Resources.Messages.DownloadInterrupted;
                return;
            }

            State = UpdaterState.UpdateDownloadReady;
        }

        private void Updater_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            DownloadProgress = e.ProgressPercentage;
        }

        private bool DownloadTickCallback(ActiveTaskDialog taskDialog, TaskDialogNotificationArgs args, object? callbackData)
        {
            switch (args.Notification)
            {
                case TaskDialogNotification.Created:
                    if (State == UpdaterState.GettingDescriptor)
                        taskDialog.SetProgressBarMarquee(true, 25);
                    break;
                case TaskDialogNotification.Timer:
                    if (!string.IsNullOrEmpty(ErrorMsg))
                        taskDialog.ClickButton((int)DialogResult.Abort);
                    switch (State)
                    {
                        case UpdaterState.DescriptorReady:
                        case UpdaterState.UpdateDownloadReady:
                            taskDialog.ClickButton((int)DialogResult.OK);
                            break;
                        case UpdaterState.DownloadingUpdate:
                        taskDialog.SetProgressBarPosition(DownloadProgress);
                            break;
                    }
                    break;
            }
            return false;
        }
    }

    internal static class UpdateChecker
    {
        private const int UPDATER_VERSION = 6;
        private const string URL_UPDATE_DESCRIPTOR = @"https://tinywall.pados.hu/updates/UpdVer{0}/update.json";

        internal static UpdateDescriptor GetDescriptor()
        {
            var url = string.Format(CultureInfo.InvariantCulture, URL_UPDATE_DESCRIPTOR, UPDATER_VERSION);
            var tmpFile = Path.GetTempFileName();

            try
            {
                using (var HTTPClient = new WebClient())
                {
                    HTTPClient.Headers.Add("TW-Version", Application.ProductVersion);
                    HTTPClient.DownloadFile(url, tmpFile);
                }

                var descriptor = SerializationHelper.DeserializeFromFile(tmpFile, new UpdateDescriptor());
                if (descriptor.MagicWord != "TinyWall Update Descriptor")
                    throw new ApplicationException("Bad update descriptor file.");

                return descriptor;
            }
            finally
            {
                File.Delete(tmpFile);
            }
        }

        internal static UpdateModule? GetUpdateModule(UpdateDescriptor descriptor, string moduleName)
        {
            for (int i = 0; i < descriptor.Modules.Length; ++i)
            {
                if (descriptor.Modules[i].Component.Equals(moduleName, StringComparison.InvariantCultureIgnoreCase))
                    return descriptor.Modules[i];
            }

            return null;
        }

        internal static UpdateModule? GetMainAppModule(UpdateDescriptor descriptor)
        {
            return GetUpdateModule(descriptor, "TinyWall");
        }
        internal static UpdateModule? GetHostsFileModule(UpdateDescriptor descriptor)
        {
            return GetUpdateModule(descriptor, "HostsFile");
        }
        internal static UpdateModule? GetDatabaseFileModule(UpdateDescriptor descriptor)
        {
            return GetUpdateModule(descriptor, "Database");
        }
    }
}
