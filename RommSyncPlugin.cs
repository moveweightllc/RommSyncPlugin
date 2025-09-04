using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing; // Provided by System.Drawing.Common
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace RommSyncPlugin
{
    public class RommSyncPlugin : IGameMenuItemPlugin, ISystemMenuItemPlugin
    {
        private static readonly HttpClient client = new HttpClient();
        private string rommBaseUrl = "";
        private string apiToken = "";
        private readonly string settingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "RommSyncPlugin", "settings.json");
        private readonly string outputFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "RommSyncPlugin", "romm_sync_output.json");

        // IGameMenuItemPlugin implementation
        public bool SupportsMultipleGames => false;
        public string Caption => "Sync with Romm";
        public Image IconImage => null;
        public bool ShowInLaunchBox => true;
        public bool ShowInBigBox => false;
        public bool GetIsValidForGame(IGame selectedGame) => true;
        public bool GetIsValidForGames(IGame[] selectedGames) => false;

        // ISystemMenuItemPlugin implementation
        public string SystemMenuItemCaption => "Romm Sync Settings";
        public Image SystemMenuItemIconImage => null;
        public bool ShowInLaunchBoxSystemMenu => true;
        public bool ShowInBigBoxSystemMenu => false;
        public bool AllowInBigBoxWhenLocked => false;

        public void OnSelected(IGame selectedGame)
        {
            LoadSettings();
            if (string.IsNullOrEmpty(rommBaseUrl) || string.IsNullOrEmpty(apiToken))
            {
                ShowSettingsForm(true);
            }
            else
            {
                SyncWithRomm();
            }
        }

        public void OnSelected(IGame[] selectedGames)
        {
            // Not implemented as SupportsMultipleGames is false
        }

        public void OnSelected()
        {
            ShowSettingsForm(false);
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(settingsFile))
                {
                    var json = File.ReadAllText(settingsFile);
                    var settings = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    rommBaseUrl = settings.ContainsKey("RommBaseUrl") ? settings["RommBaseUrl"] : "";
                    apiToken = settings.ContainsKey("ApiToken") ? settings["ApiToken"] : "";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading settings: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveSettings()
        {
            try
            {
                var settings = new Dictionary<string, string>
                {
                    { "RommBaseUrl", rommBaseUrl },
                    { "ApiToken", apiToken }
                };
                Directory.CreateDirectory(Path.GetDirectoryName(settingsFile));
                File.WriteAllText(settingsFile, JsonConvert.SerializeObject(settings, Formatting.Indented));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving settings: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowSettingsForm(bool requiredForSync)
        {
            using (var form = new Form { Text = "Romm Sync Settings", Size = new Size(400, 220), StartPosition = FormStartPosition.CenterScreen })
            {
                var urlLabel = new Label { Text = "Romm Server URL (e.g., http://192.168.0.94:8080):", Location = new Point(20, 20), AutoSize = true };
                var urlTextBox = new TextBox { Text = rommBaseUrl, Location = new Point(20, 40), Width = 340 };
                var tokenLabel = new Label { Text = "API Token (from Romm /api/token):", Location = new Point(20, 70), AutoSize = true };
                var tokenTextBox = new TextBox { Text = apiToken, Location = new Point(20, 90), Width = 340 };
                var saveButton = new Button { Text = "Save", Location = new Point(20, 120), Width = 100 };
                var cancelButton = new Button { Text = "Cancel", Location = new Point(130, 120), Width = 100 };
                var helpLabel = new Label { Text = "Get your API token from Romm's web UI login.", Location = new Point(20, 150), AutoSize = true, ForeColor = Color.Gray };

                saveButton.Click += (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(urlTextBox.Text) || string.IsNullOrWhiteSpace(tokenTextBox.Text))
                    {
                        MessageBox.Show("Please enter both a valid URL and API token.", "Invalid Input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    if (!Uri.TryCreate(urlTextBox.Text.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                    {
                        MessageBox.Show("Please enter a valid URL (e.g., http://192.168.0.94:8080).", "Invalid URL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    rommBaseUrl = urlTextBox.Text.Trim();
                    apiToken = tokenTextBox.Text.Trim();
                    SaveSettings();
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                };
                cancelButton.Click += (s, e) => form.Close();

                form.Controls.AddRange(new Control[] { urlLabel, urlTextBox, tokenLabel, tokenTextBox, saveButton, cancelButton, helpLabel });
                var result = form.ShowDialog();
                if (result == DialogResult.OK && requiredForSync)
                {
                    SyncWithRomm();
                }
            }
        }

        private async void SyncWithRomm()
        {
            try
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiToken);
                var romsResponse = await client.GetAsync($"{rommBaseUrl}/api/roms");
                romsResponse.EnsureSuccessStatusCode();
                var romsJson = await romsResponse.Content.ReadAsStringAsync();
                var roms = JsonConvert.DeserializeObject<List<RommGame>>(romsJson);

                var platformResponse = await client.GetAsync($"{rommBaseUrl}/api/platforms");
                platformResponse.EnsureSuccessStatusCode();
                var platformsJson = await platformResponse.Content.ReadAsStringAsync();
                var platforms = JsonConvert.DeserializeObject<List<RommPlatform>>(platformsJson);

                var outputGames = new List<OutputGame>();
                foreach (var rom in roms)
                {
                    var platform = platforms.Find(p => p.id == rom.platform_id);
                    if (platform == null) continue;

                    var game = new OutputGame
                    {
                        Title = rom.name,
                        Platform = platform.name,
                        ApplicationPath = $"{rommBaseUrl}/api/play/{rom.id}",
                        Notes = rom.summary ?? ""
                    };

                    if (!string.IsNullOrEmpty(rom.cover_image))
                    {
                        var imageResponse = await client.GetAsync($"{rommBaseUrl}/api/resources/{rom.cover_image}");
                        if (imageResponse.IsSuccessStatusCode)
                        {
                            var imageBytes = await imageResponse.Content.ReadAsByteArrayAsync();
                            var imagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", platform.name, "Box - Front", $"{rom.id}.jpg");
                            Directory.CreateDirectory(Path.GetDirectoryName(imagePath));
                            File.WriteAllBytes(imagePath, imageBytes);
                            game.FrontImagePath = imagePath;
                        }
                    }

                    outputGames.Add(game);
                }

                File.WriteAllText(outputFile, JsonConvert.SerializeObject(outputGames, Formatting.Indented));
                MessageBox.Show($"Romm library synced successfully! Data saved to {outputFile}. Please manually import games into LaunchBox.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (HttpRequestException ex)
            {
                MessageBox.Show($"Failed to connect to Romm server. Check URL and token: {ex.Message}", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error syncing with Romm: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    public class RommGame
    {
        public int id { get; set; }
        public string name { get; set; }
        public int platform_id { get; set; }
        public string cover_image { get; set; }
        public string summary { get; set; }
    }

    public class RommPlatform
    {
        public int id { get; set; }
        public string name { get; set; }
    }

    public class OutputGame
    {
        public string Title { get; set; }
        public string Platform(No such file or directory) { get; set; }
        public string ApplicationPath { get; set; }
        public string Notes { get; set; }
        public string FrontImagePath { get; set; }
    }
}