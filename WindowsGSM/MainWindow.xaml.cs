﻿using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;
using System.Net;
using System.IO.Compression;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using MahApps.Metro;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using System.Windows.Media.Imaging;
using Newtonsoft.Json.Linq;

namespace WindowsGSM
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, WindowShowStyle nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private enum WindowShowStyle : uint
        {
            Hide = 0,
            ShowNormal = 1,
            Show = 5,
            ShowMinNoActivate = 7
        }

        private enum ServerStatus
        {
            Started = 0,
            Starting = 1,
            Stopped = 2,
            Stopping = 3,
            Restarted = 4,
            Restarting = 5,
            Updated = 6,
            Updating = 7,
            Backuped = 8,
            Backuping = 9,
            Restored = 10,
            Restoring = 11,
            Deleting = 12
        }

        public static readonly string WGSM_VERSION = "v1.8.0";
        public static readonly int MAX_SERVER = 100;
        public static readonly string WGSM_PATH = Process.GetCurrentProcess().MainModule.FileName.Replace(@"\WindowsGSM.exe", "");

        private readonly NotifyIcon notifyIcon;

        private Install InstallWindow;
        private Import ImportWindow;

        private static readonly ServerStatus[] g_iServerStatus = new ServerStatus[MAX_SERVER + 1];

        private static readonly Process[] g_Process = new Process[MAX_SERVER + 1];

        private static readonly bool[] g_bAutoRestart = new bool[MAX_SERVER + 1];
        private static readonly bool[] g_bAutoStart = new bool[MAX_SERVER + 1];
        private static readonly bool[] g_bAutoUpdate = new bool[MAX_SERVER + 1];
        private static readonly bool[] g_bUpdateOnStart = new bool[MAX_SERVER + 1];

        private static readonly bool[] g_bDiscordAlert = new bool[MAX_SERVER + 1];
        private static readonly string[] g_DiscordWebhook = new string[MAX_SERVER + 1];

        private static string g_DonorType = "";

        public static readonly Functions.ServerConsole[] g_ServerConsoles = new Functions.ServerConsole[MAX_SERVER + 1];

        public MainWindow()
        {
            InitializeComponent();

            Title = "WindowsGSM " + WGSM_VERSION;

            RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM");
            if (key == null)
            {
                key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\WindowsGSM");
                key.SetValue("HardWareAcceleration", "True");
                key.SetValue("UIAnimation", "True");
                key.SetValue("DarkTheme", "False");
                key.SetValue("StartOnBoot", "False");
                key.SetValue("DonorTheme", "False");
                key.SetValue("DonorAuthKey", "");
                key.SetValue("Height", "540");
                key.SetValue("Width", "960");

                MahAppSwitch_HardWareAcceleration.IsChecked = true;
                MahAppSwitch_UIAnimation.IsChecked = true;
                MahAppSwitch_DarkTheme.IsChecked = false;
                MahAppSwitch_StartOnBoot.IsChecked = false;
                MahAppSwitch_DonorTheme.IsChecked = false;
            }
            else
            {
                MahAppSwitch_HardWareAcceleration.IsChecked = ((key.GetValue("HardWareAcceleration") ?? false).ToString() == "True") ? true : false;
                MahAppSwitch_UIAnimation.IsChecked = ((key.GetValue("UIAnimation") ?? false).ToString() == "True") ? true : false;
                MahAppSwitch_DarkTheme.IsChecked = ((key.GetValue("DarkTheme") ?? false).ToString() == "True") ? true : false;
                MahAppSwitch_StartOnBoot.IsChecked = ((key.GetValue("StartOnBoot") ?? false).ToString() == "True") ? true : false;
                MahAppSwitch_DonorTheme.IsChecked = ((key.GetValue("DonorTheme") ?? false).ToString() == "True") ? true : false;

                if (MahAppSwitch_DonorTheme.IsChecked ?? false)
                {
                    string authKey = (key.GetValue("DonorAuthKey") == null) ? "" : key.GetValue("DonorAuthKey").ToString();
                    if (!String.IsNullOrWhiteSpace(authKey))
                    {
#pragma warning disable 4014
                        ActivateDonorTheme(authKey);
#pragma warning restore
                    }
                }

                Height = (key.GetValue("Height") == null) ? 540 : double.Parse(key.GetValue("Height").ToString());
                Width = (key.GetValue("Width") == null) ? 960 : double.Parse(key.GetValue("Width").ToString());
            }
            key.Close();

            RenderOptions.ProcessRenderMode = (MahAppSwitch_HardWareAcceleration.IsChecked ?? false) ? System.Windows.Interop.RenderMode.SoftwareOnly : System.Windows.Interop.RenderMode.Default;
            WindowTransitionsEnabled = MahAppSwitch_UIAnimation.IsChecked ?? false;
            ThemeManager.ChangeAppTheme(App.Current, (MahAppSwitch_DarkTheme.IsChecked ?? false) ? "BaseDark" : "BaseLight");
            //Not required - it is set by windows settings
            //SetStartOnBoot(MahAppSwitch_StartOnBoot.IsChecked ?? false);

            notifyIcon = new NotifyIcon
            {
                BalloonTipTitle = "WindowsGSM",
                BalloonTipText = "WindowsGSM is running in the background",
                Text = "WindowsGSM",
                BalloonTipIcon = ToolTipIcon.Info
            };

            Stream iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Images/WindowsGSM.ico")).Stream;
            if (iconStream != null)
            {
                notifyIcon.Icon = new System.Drawing.Icon(iconStream);
            }

            notifyIcon.BalloonTipClicked += OnBalloonTipClick;
            notifyIcon.MouseClick += NotifyIcon_MouseClick;

            //Set All server status to stopped
            for (int i = 0; i <= MAX_SERVER; i++)
            {
                g_iServerStatus[i] = ServerStatus.Stopped;
                g_ServerConsoles[i] = new Functions.ServerConsole(i.ToString());
            }

            LoadServerTable();

            if (ServerGrid.Items.Count > 0)
            {
                ServerGrid.SelectedItem = ServerGrid.Items[0];
            }

            AutoStartServer();
        }

        private void RefreshServerList_Click(object sender, RoutedEventArgs e)
        {
            LoadServerTable();
        }

        public void LoadServerTable()
        {
            var selectedrow = (Functions.ServerTable)ServerGrid.SelectedItem;

            int num_row = ServerGrid.Items.Count;
            for (int i = 0; i < num_row; i++)
            {
                ServerGrid.Items.RemoveAt(0);
            }

            //Add server to datagrid
            for (int i = 1; i <= MAX_SERVER; i++)
            {
                string serverid_path = Path.Combine(WGSM_PATH, "servers", i.ToString());
                if (!Directory.Exists(serverid_path))
                {
                    continue;
                }

                var serverConfig = new Functions.ServerConfig(i.ToString());

                if (!serverConfig.IsWindowsGSMConfigExist())
                {
                    continue;
                }

                string status;
                switch (g_iServerStatus[i])
                {
                    case ServerStatus.Started: status = "Started"; break;
                    case ServerStatus.Starting: status = "Starting"; break;
                    case ServerStatus.Stopped: status = "Stopped"; break;
                    case ServerStatus.Stopping: status = "Stopping"; break;
                    case ServerStatus.Restarted: status = "Restarted"; break;
                    case ServerStatus.Restarting: status = "Restarting"; break;
                    case ServerStatus.Updated: status = "Updated"; break;
                    case ServerStatus.Updating: status = "Updating"; break;
                    case ServerStatus.Backuped: status = "Backuped"; break;
                    case ServerStatus.Backuping: status = "Backuping"; break;
                    case ServerStatus.Restored: status = "Restored"; break;
                    case ServerStatus.Restoring: status = "Restoring"; break;
                    case ServerStatus.Deleting: status = "Deleteing"; break;
                    default:
                        {
                            g_iServerStatus[i] = ServerStatus.Stopped;
                            status = "Stopped";
                            break;
                        }
                }

                var row = new Functions.ServerTable
                {
                    ID = i.ToString(),
                    Game = serverConfig.ServerGame,
                    Icon = "/WindowsGSM;component/" + GameServer.Data.Icon.ResourceManager.GetString(serverConfig.ServerGame),
                    Status = status,
                    Name = serverConfig.ServerName,
                    IP = serverConfig.ServerIP,
                    Port = serverConfig.ServerPort,
                    Defaultmap = serverConfig.ServerMap,
                    Maxplayers = serverConfig.ServerMaxPlayer
                };

                ServerGrid.Items.Add(row);

                if (selectedrow != null)
                {
                    if (selectedrow.ID == row.ID)
                    {
                        ServerGrid.SelectedItem = row;
                    }
                }

                g_bAutoRestart[i] = serverConfig.AutoRestart;
                g_bAutoStart[i] = serverConfig.AutoStart;
                g_bAutoUpdate[i] = serverConfig.AutoUpdate;
                g_bUpdateOnStart[i] = serverConfig.UpdateOnStart;
                g_bDiscordAlert[i] = serverConfig.DiscordAlert;
                g_DiscordWebhook[i] = serverConfig.DiscordWebhook;
            }

            grid_action.Visibility = (ServerGrid.Items.Count != 0) ? Visibility.Visible : Visibility.Hidden;
        }

        private async void AutoStartServer()
        {
            int num_row = ServerGrid.Items.Count;
            for (int i = 0; i < num_row; i++)
            {
                var server = (Functions.ServerTable)ServerGrid.Items[i];
                if (g_bAutoStart[Int32.Parse(server.ID)])
                {
                    await GameServer_Start(server, " | Auto Start");
                }
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //Save height and width
            RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true);
            if (key != null)
            {
                key.SetValue("Height", Height.ToString());
                key.SetValue("Width", Width.ToString());
                key.Close();
            }

            //Shutdown all server before WindowsGSM close
            bool hasServerRunning = false;
            for (int i = 0; i <= MAX_SERVER; i++)
            {
                if (g_Process[i] != null)
                {
                    if (!g_Process[i].HasExited)
                    {
                        hasServerRunning = true;

                        break;
                    }
                }
            }

            if (!hasServerRunning) { return; }

            MessageBoxResult result = System.Windows.MessageBox.Show("Are you sure to quit?\n(All game servers will be stopped)", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;

                return;
            }

            for (int i = 0; i <= MAX_SERVER; i++)
            {
                if (g_Process[i] == null)
                {
                    continue;
                }

                if (!g_Process[i].HasExited)
                {
                    SetForegroundWindow(g_Process[i].MainWindowHandle);
                    SendKeys.SendWait("stop");
                    SendKeys.SendWait("{ENTER}");
                    SendKeys.SendWait("{ENTER}");

                    if (!g_Process[i].HasExited)
                    {
                        g_Process[i].Kill();
                    }
                }
            }
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var row = (Functions.ServerTable)ServerGrid.SelectedItem;

            if (row != null)
            {
                if (g_iServerStatus[Int32.Parse(row.ID)] == ServerStatus.Stopped)
                {
                    button_Start.IsEnabled = true;
                    button_Stop.IsEnabled = false;
                    button_Restart.IsEnabled = false;
                    button_Console.IsEnabled = false;
                    button_Update.IsEnabled = true;
                    button_Backup.IsEnabled = true;

                    textbox_servercommand.IsEnabled = false;
                    button_servercommand.IsEnabled = false;
                }
                else if (g_iServerStatus[Int32.Parse(row.ID)] == ServerStatus.Started)
                {
                    button_Start.IsEnabled = false;
                    button_Stop.IsEnabled = true;
                    button_Restart.IsEnabled = true;
                    button_Console.IsEnabled = Functions.ServerConsole.IsToggleable(row.Game);
                    button_Update.IsEnabled = false;
                    button_Backup.IsEnabled = false;

                    textbox_servercommand.IsEnabled = true;
                    button_servercommand.IsEnabled = true;
                }
                else
                {
                    button_Start.IsEnabled = false;
                    button_Stop.IsEnabled = false;
                    button_Restart.IsEnabled = false;
                    button_Console.IsEnabled = false;
                    button_Update.IsEnabled = false;
                    button_Backup.IsEnabled = false;

                    textbox_servercommand.IsEnabled = false;
                    button_servercommand.IsEnabled = false;
                }

                button_Status.Content = row.Status.ToUpper();
                button_Status.Background = (g_iServerStatus[Int32.Parse(row.ID)] == ServerStatus.Started) ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.Orange;
                textBox_ServerGame.Text = row.Game;

                button_autorestart.Content = (g_bAutoRestart[Int32.Parse(row.ID)]) ? "TRUE" : "FALSE";
                button_autorestart.Background = (g_bAutoRestart[Int32.Parse(row.ID)]) ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.Red;

                button_autostart.Content = (g_bAutoStart[Int32.Parse(row.ID)]) ? "TRUE" : "FALSE";
                button_autostart.Background = (g_bAutoStart[Int32.Parse(row.ID)]) ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.Red;

                button_autoupdate.Content = (g_bAutoUpdate[Int32.Parse(row.ID)]) ? "TRUE" : "FALSE";
                button_autoupdate.Background = (g_bAutoUpdate[Int32.Parse(row.ID)]) ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.Red;

                button_updateonstart.Content = (g_bUpdateOnStart[Int32.Parse(row.ID)]) ? "TRUE" : "FALSE";
                button_updateonstart.Background = (g_bUpdateOnStart[Int32.Parse(row.ID)]) ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.Red;

                button_discordalert.Content = (g_bDiscordAlert[Int32.Parse(row.ID)]) ? "TRUE" : "FALSE";
                button_discordalert.Background = (g_bDiscordAlert[Int32.Parse(row.ID)]) ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.Red;
                button_discordtest.IsEnabled = (g_bDiscordAlert[Int32.Parse(row.ID)]) ? true : false;

                Functions.ServerConsole.Refresh(row.ID);
            }
        }

        private void Install_Click(object sender, RoutedEventArgs e)
        {
            if (InstallWindow == null && ImportWindow == null)
            {
                InstallWindow = new Install();
                InstallWindow.Closed += new EventHandler(InstallWindow_Closed);

                //Add games to ComboBox
                int i = 0;
                string servergame = "";
                while (servergame != null)
                {
                    servergame = GameServer.Data.List.ResourceManager.GetString((++i).ToString());
                    if (servergame == null)
                    {
                        break;
                    }

                    var row = new Images.Row { Image = "/WindowsGSM;component/" + GameServer.Data.Icon.ResourceManager.GetString(servergame), Name = servergame };
                    InstallWindow.comboBox.Items.Add(row);
                }
            }
            else
            {
                if (InstallWindow != null)
                {
                    InstallWindow.Activate();
                    InstallWindow.WindowState = WindowState.Normal;
                }
                else if (ImportWindow != null)
                {
                    ImportWindow.Activate();
                    ImportWindow.WindowState = WindowState.Normal;
                }
            }
        }

        private void InstallWindow_Closed(object sender, EventArgs e)
        {
            InstallWindow = null;
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            if (InstallWindow == null && ImportWindow == null)
            {
                ImportWindow = new Import();
                ImportWindow.Closed += new EventHandler(ImportWindow_Closed);

                //Add games to ComboBox
                int i = 0;
                string servergame = "";
                while (servergame != null)
                {
                    servergame = GameServer.Data.List.ResourceManager.GetString((++i).ToString());
                    if (servergame == null)
                    {
                        break;
                    }

                    var row = new Images.Row { Image = "/WindowsGSM;component/" + GameServer.Data.Icon.ResourceManager.GetString(servergame), Name = servergame };
                    ImportWindow.comboBox.Items.Add(row);
                }
            }
            else
            {
                if (InstallWindow != null)
                {
                    InstallWindow.Activate();
                    InstallWindow.WindowState = WindowState.Normal;
                }
                else if (ImportWindow != null)
                {
                    ImportWindow.Activate();
                    ImportWindow.WindowState = WindowState.Normal;
                }
            }
        }

        private void ImportWindow_Closed(object sender, EventArgs e)
        {
            ImportWindow = null;
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (g_iServerStatus[Int32.Parse(server.ID)] != ServerStatus.Stopped) { return; }

            MessageBoxResult result = System.Windows.MessageBox.Show("Do you want to delete this server?\n(There is no comeback)", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) { return; }

            await GameServer_Delete(server);
        }

        private async void Button_DiscordEdit_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            string webhookUrl = Functions.ServerConfig.GetDiscordWebhookUrl(server.ID);

            var settings = new MetroDialogSettings
            {
                AffirmativeButtonText = "Save",
                DefaultText = webhookUrl
            };

            webhookUrl = await this.ShowInputAsync("Discord Webhook URL", "Please enter the discord webhook url.", settings);

            //If pressed cancel or key is null or whitespace
            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                return;
            }

            Functions.ServerConfig.SetDiscordWebhookUrl(server.ID, webhookUrl);
        }

        private async void Button_DiscordWebhookTest_Click(object sender, RoutedEventArgs e)
        {
            var row = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (row == null) { return; }

            if (!g_bDiscordAlert[Int32.Parse(row.ID)]) { return; }

            var webhook = new Discord.Webhook(g_DiscordWebhook[Int32.Parse(row.ID)], g_DonorType);
            await webhook.Send(row.ID, row.Game, "Webhook Test Alert", row.Name, row.IP, row.Port);
        }

        private void Button_ServerCommand_Click(object sender, RoutedEventArgs e)
        {
            string command = textbox_servercommand.Text;
            textbox_servercommand.Text = "";

            if (string.IsNullOrWhiteSpace(command)) { return; }

            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            SendCommand(server, command);
        }

        private void Textbox_ServerCommand_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (textbox_servercommand.Text.Length != 0)
                {
                    g_ServerConsoles[0].Add(textbox_servercommand.Text);
                }

                Button_ServerCommand_Click(this, new RoutedEventArgs());
            }
        }

        private void Textbox_ServerCommand_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.IsDown && e.Key == Key.Up)
            {
                e.Handled = true;
                textbox_servercommand.Text = g_ServerConsoles[0].GetPreviousCommand();
            }
            else if (e.IsDown && e.Key == Key.Down)
            {
                e.Handled = true;
                textbox_servercommand.Text = g_ServerConsoles[0].GetNextCommand();
            }
        }

        private async void Actions_Start_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            //Reload WindowsGSM.cfg on start
            int i = Int32.Parse(server.ID);
            var serverConfig = new Functions.ServerConfig(i.ToString());
            g_bAutoRestart[i] = serverConfig.AutoRestart;
            g_bAutoStart[i] = serverConfig.AutoStart;
            g_bAutoUpdate[i] = serverConfig.AutoUpdate;
            g_bUpdateOnStart[i] = serverConfig.UpdateOnStart;
            g_bDiscordAlert[i] = serverConfig.DiscordAlert;
            g_DiscordWebhook[i] = serverConfig.DiscordWebhook;

            await GameServer_Start(server);
        }

        private void Actions_Stop_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            GameServer_Stop(server);
        }

        private void Actions_Restart_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            GameServer_Restart(server);
        }

        private void Actions_ToggleConsole_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            Process p = g_Process[Int32.Parse(server.ID)];
            if (p == null) { return; }

            //If console is useless, return
            if (!Functions.ServerConsole.IsToggleable(server.Game)) { return; }

            IntPtr hWnd = p.MainWindowHandle;
            ShowWindow(hWnd, (ShowWindow(hWnd, WindowShowStyle.Hide)) ? (WindowShowStyle.Hide) : (WindowShowStyle.ShowNormal));
        }

        private async void Actions_Update_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (g_iServerStatus[Int32.Parse(server.ID)] != ServerStatus.Stopped) { return; }

            MessageBoxResult result = System.Windows.MessageBox.Show("Do you want to update this server?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) { return; }

            await GameServer_Update(server);
        }

        private async void Actions_Backup_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (g_iServerStatus[Int32.Parse(server.ID)] != ServerStatus.Stopped) { return; }

            MessageBoxResult result = System.Windows.MessageBox.Show("Do you want to backup on this server?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) { return; }

            await GameServer_Backup(server);
        }

        private async void Actions_RestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (g_iServerStatus[Int32.Parse(server.ID)] != ServerStatus.Stopped) { return; }

            MessageBoxResult result = System.Windows.MessageBox.Show("Do you want to restore backup on this server?\n(All server files will be overwritten)", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) { return; }

            await GameServer_RestoreBackup(server);
        }

        private async Task GameServer_Start(Functions.ServerTable server, string notes = "")
        {
            if (g_iServerStatus[Int32.Parse(server.ID)] != ServerStatus.Stopped) { return; }

            string error = "";
            if (!IsValidIPAddress(server.IP))
            {
                error += " IP address is not valid.";
            }

            if (!IsValidPort(server.Port))
            {
                error += " Port number is not valid.";
            }

            if (error != "")
            {
                Log(server.ID, "Server: Fail to start");
                Log(server.ID, "[ERROR]" + error);

                return;
            }

            Process p = g_Process[Int32.Parse(server.ID)];
            if (p != null) { return; }

            if (g_bUpdateOnStart[Int32.Parse(server.ID)])
            {
                await GameServer_Update(server, " | Update on Start");
            }

            g_iServerStatus[Int32.Parse(server.ID)] = ServerStatus.Starting;
            Log(server.ID, "Action: Start" + notes);
            SetServerStatus(server, "Starting");

            var gameServer = await Server_BeginStart(server);
            if (gameServer == null) { return; }

            g_iServerStatus[Int32.Parse(server.ID)] = ServerStatus.Started;
            Log(server.ID, "Server: Started");
            if (!string.IsNullOrWhiteSpace(gameServer.Notice))
            {
                Log(server.ID, "[Notice] " + gameServer.Notice);
            }
            SetServerStatus(server, "Started");

            if (g_bDiscordAlert[Int32.Parse(server.ID)])
            {
                var webhook = new Discord.Webhook(g_DiscordWebhook[Int32.Parse(server.ID)], g_DonorType);
                await webhook.Send(server.ID, server.Game, "Started", server.Name, server.IP, server.Port);
            }
        }

        private async void GameServer_Stop(Functions.ServerTable server)
        {
            if (g_iServerStatus[Int32.Parse(server.ID)] != ServerStatus.Started) { return; }

            Process p = g_Process[Int32.Parse(server.ID)];
            if (p == null) { return; }

            //Begin stop
            g_iServerStatus[Int32.Parse(server.ID)] = ServerStatus.Stopping;
            Log(server.ID, "Action: Stop");
            SetServerStatus(server, "Stopping");

            bool stopGracefully = await Server_BeginStop(server, p);

            Log(server.ID, "Server: Stopped");
            if (!stopGracefully)
            {
                Log(server.ID, "[NOTICE] Server fail to stop gracefully");
            }
            g_iServerStatus[Int32.Parse(server.ID)] = ServerStatus.Stopped;
            SetServerStatus(server, "Stopped");

            if (g_bDiscordAlert[Int32.Parse(server.ID)])
            {
                var webhook = new Discord.Webhook(g_DiscordWebhook[Int32.Parse(server.ID)], g_DonorType);
                await webhook.Send(server.ID, server.Game, "Stopped", server.Name, server.IP, server.Port);
            }
        }

        private async void GameServer_Restart(Functions.ServerTable server)
        {
            if (g_iServerStatus[Int32.Parse(server.ID)] != ServerStatus.Started) { return; }

            Process p = g_Process[Int32.Parse(server.ID)];
            if (p == null) { return; }

            g_Process[Int32.Parse(server.ID)] = null;

            //Begin Restart
            g_iServerStatus[Int32.Parse(server.ID)] = ServerStatus.Restarting;
            Log(server.ID, "Action: Restart");
            SetServerStatus(server, "Restarting");

            await Server_BeginStop(server, p);
            var gameServer = await Server_BeginStart(server);
            if (gameServer == null) { return; }

            g_iServerStatus[Int32.Parse(server.ID)] = (int)ServerStatus.Started;
            Log(server.ID, "Server: Restarted");
            if (!string.IsNullOrWhiteSpace(gameServer.Notice))
            {
                Log(server.ID, "[Notice] " + gameServer.Notice);
            }
            SetServerStatus(server, "Started");

            if (g_bDiscordAlert[Int32.Parse(server.ID)])
            {
                var webhook = new Discord.Webhook(g_DiscordWebhook[Int32.Parse(server.ID)], g_DonorType);
                await webhook.Send(server.ID, server.Game, "Restarted", server.Name, server.IP, server.Port);
            }
        }

        private async Task<dynamic> Server_BeginStart(Functions.ServerTable server)
        {
            dynamic gameServer = GameServer.ClassObject.Get(server.Game, new Functions.ServerConfig(server.ID));
            Process p = await gameServer.Start();

            //Fail to start
            if (p == null)
            {
                g_iServerStatus[Int32.Parse(server.ID)] = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to start");
                Log(server.ID, "[ERROR] " + gameServer.Error);
                SetServerStatus(server, "Stopped");

                return null;
            }

            g_Process[Int32.Parse(server.ID)] = p;
            p.Exited += (sender, e) => OnGameServerExited(sender, e, server);

            await Task.Run(() =>
            {
                try
                {
                    if (!p.StartInfo.CreateNoWindow)
                    {
                        while (!p.HasExited && !ShowWindow(p.MainWindowHandle, WindowShowStyle.ShowMinNoActivate)) { }
                    }

                    p.WaitForInputIdle();
                }
                catch
                {

                }
            });

            //An error may occur on ShowWindow if not adding this 
            if (p.HasExited)
            {
                g_Process[Int32.Parse(server.ID)] = null;

                g_iServerStatus[Int32.Parse(server.ID)] = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to start");
                Log(server.ID, "[ERROR] Exit Code: " + p.ExitCode.ToString());
                SetServerStatus(server, "Stopped");

                return null;
            }

            ShowWindow(p.MainWindowHandle, WindowShowStyle.Hide);

            StartAutoUpdateCheck(server);

            return gameServer;
        }

        private async Task<bool> Server_BeginStop(Functions.ServerTable server, Process p)
        {
            g_Process[Int32.Parse(server.ID)] = null;

            dynamic gameServer = GameServer.ClassObject.Get(server.Game, null);
            await gameServer.Stop(p);

            if (!p.HasExited)
            {
                p.Kill();
            }

            g_ServerConsoles[Int32.Parse(server.ID)].Clear();

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="server"></param>
        /// <param name="silenceCheck"></param>
        /// <param name="forceUpdate"></param>
        /// <returns> (IsUpdateSuccess, RemoteBuild, gameServer)</returns>
        private async Task<(bool, string, dynamic)> Server_BeginUpdate(Functions.ServerTable server, bool silenceCheck, bool forceUpdate)
        {
            dynamic gameServer = GameServer.ClassObject.Get(server.Game, new Functions.ServerConfig(server.ID));

            string localVersion = gameServer.GetLocalBuild();
            if (string.IsNullOrWhiteSpace(localVersion) && !silenceCheck)
            {
                Log(server.ID, $"[NOTICE] {gameServer.Error}");
            }

            string remoteVersion = await gameServer.GetRemoteBuild();
            if (string.IsNullOrWhiteSpace(remoteVersion) && !silenceCheck)
            {
                Log(server.ID, $"[NOTICE] {gameServer.Error}");
            }

            if (!silenceCheck)
            {
                Log(server.ID, $"Checking: Version ({localVersion}) => ({remoteVersion})");
            }

            if ((!string.IsNullOrWhiteSpace(localVersion) && !string.IsNullOrWhiteSpace(remoteVersion) && localVersion != remoteVersion) || forceUpdate)
            {
                return (await gameServer.Update(), remoteVersion, gameServer);
            }

            return (true, remoteVersion, gameServer);
        }

        private async Task<bool> GameServer_Update(Functions.ServerTable server, string notes = "")
        {
            if (g_iServerStatus[Int32.Parse(server.ID)] != ServerStatus.Stopped)
            {
                return false;
            }

            //Begin Update
            g_iServerStatus[Int32.Parse(server.ID)] = ServerStatus.Updating;
            Log(server.ID, "Action: Update" + notes);
            SetServerStatus(server, "Updating");

            (bool updated, string remoteVersion, dynamic gameServer) = await Server_BeginUpdate(server, silenceCheck: false, forceUpdate: true);

            Activate();

            if (updated)
            {
                Log(server.ID, $"Server: Updated ({remoteVersion})");
            }
            else
            {
                Log(server.ID, "Server: Fail to update");
                Log(server.ID, "[ERROR] " + gameServer.Error);
            }

            g_iServerStatus[Int32.Parse(server.ID)] = ServerStatus.Stopped;
            SetServerStatus(server, "Stopped");

            return true;
        }

        private async Task<bool> GameServer_Backup(Functions.ServerTable server)
        {
            if (g_iServerStatus[Int32.Parse(server.ID)] != ServerStatus.Stopped)
            {
                return false;
            }

            //Begin backup
            g_iServerStatus[Int32.Parse(server.ID)] = ServerStatus.Backuping;
            Log(server.ID, "Action: Backup");
            SetServerStatus(server, "Backuping");

            string startPath = WGSM_PATH + @"\servers\" + server.ID;
            string zipPath = WGSM_PATH + @"\backups\" + server.ID;
            string zipFile = zipPath + @"\backup-id-" + server.ID + ".zip";

            if (!Directory.Exists(zipPath))
            {
                Directory.CreateDirectory(zipPath);
            }

            if (File.Exists(zipFile))
            {
                await Task.Run(() =>
                {
                    try
                    {
                        File.Delete(zipFile);
                    }
                    catch
                    {

                    }
                });

                if (File.Exists(zipFile))
                {
                    g_iServerStatus[Int32.Parse(server.ID)] = ServerStatus.Stopped;
                    Log(server.ID, "Server: Fail to backup");
                    Log(server.ID, "[ERROR] Fail to delete old backup");
                    SetServerStatus(server, "Stopped");

                    return false;
                }
            }

            await Task.Run(() => ZipFile.CreateFromDirectory(startPath, zipFile));

            if (!File.Exists(zipFile))
            {
                g_iServerStatus[Int32.Parse(server.ID)] = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to backup");
                Log(server.ID, "[ERROR] Cannot create zipfile");
                SetServerStatus(server, "Stopped");

                return false;
            }

            g_iServerStatus[Int32.Parse(server.ID)] = ServerStatus.Stopped;
            Log(server.ID, "Server: Backuped");
            SetServerStatus(server, "Stopped");

            return true;
        }

        private async Task<bool> GameServer_RestoreBackup(Functions.ServerTable server)
        {
            if (g_iServerStatus[Int32.Parse(server.ID)] != ServerStatus.Stopped)
            {
                return false;
            }

            string zipFile = WGSM_PATH + @"\backups\" + server.ID + @"\backup-id-" + server.ID + ".zip";
            string extractPath = WGSM_PATH + @"\servers\" + server.ID;

            if (!File.Exists(zipFile))
            {
                Log(server.ID, "Server: Fail to restore backup");
                Log(server.ID, "[ERROR] Backup not found");

                return false;
            }

            if (Directory.Exists(extractPath))
            {
                await Task.Run(() =>
                {
                    try
                    {
                        Directory.Delete(extractPath, true);
                    }
                    catch
                    {

                    }
                });

                if (Directory.Exists(extractPath))
                {
                    Log(server.ID, "Server: Fail to restore backup");
                    Log(server.ID, "[ERROR] Extract path is not accessible");

                    return false;
                }
            }

            //Begin backup
            g_iServerStatus[Int32.Parse(server.ID)] = ServerStatus.Restoring;
            Log(server.ID, "Action: Restore Backup");
            SetServerStatus(server, "Restoring");

            await Task.Run(() => ZipFile.ExtractToDirectory(zipFile, extractPath));

            g_iServerStatus[Int32.Parse(server.ID)] = ServerStatus.Stopped;
            Log(server.ID, "Server: Restored");
            SetServerStatus(server, "Stopped");

            return true;
        }

        private async Task<bool> GameServer_Delete(Functions.ServerTable server)
        {
            if (g_iServerStatus[Int32.Parse(server.ID)] != ServerStatus.Stopped)
            {
                return false;
            }

            //Begin delete
            g_iServerStatus[Int32.Parse(server.ID)] = ServerStatus.Deleting;
            Log(server.ID, "Action: Delete");
            SetServerStatus(server, "Deleting");

            //Remove firewall rule
            var firewall = new WindowsFirewall(null, Functions.Path.Get(server.ID));
            await firewall.RemoveRuleEx();

            string serverPath = WGSM_PATH + @"\servers\" + server.ID;

            await Task.Run(() =>
            {
                try
                {
                    if (Directory.Exists(serverPath))
                    {
                        Directory.Delete(serverPath, true);
                    }
                }
                catch
                {

                }
            });

            await Task.Delay(100);

            if (Directory.Exists(serverPath))
            {
                Log(server.ID, "Server: Fail to delete server");
                Log(server.ID, "[ERROR] Directory is not accessible");

                g_iServerStatus[Int32.Parse(server.ID)] = ServerStatus.Stopped;
                SetServerStatus(server, "Stopped");

                return false;
            }

            Log(server.ID, "Server: Deleted server");

            LoadServerTable();

            return true;
        }

        private async void OnGameServerExited(object sender, EventArgs e, Functions.ServerTable server)
        {
            await System.Windows.Application.Current.Dispatcher.Invoke(async () =>
            {
                int serverId = Int32.Parse(server.ID);

                if (g_iServerStatus[serverId] == ServerStatus.Started)
                {
                    g_iServerStatus[serverId] = ServerStatus.Stopped;
                    Log(server.ID, "Server: Crashed");
                    SetServerStatus(server, "Stopped");

                    if (g_bDiscordAlert[serverId])
                    {
                        var webhook = new Discord.Webhook(g_DiscordWebhook[serverId], g_DonorType);
                        await webhook.Send(server.ID, server.Game, "Crashed", server.Name, server.IP, server.Port);
                    }

                    g_Process[serverId] = null;

                    if (g_bAutoRestart[serverId])
                    {
                        await Task.Delay(1000);

                        var gameServer = await Server_BeginStart(server);
                        if (gameServer == null) { return; }

                        g_iServerStatus[Int32.Parse(server.ID)] = ServerStatus.Started;
                        Log(server.ID, "Server: Started | Auto Restart");
                        if (!string.IsNullOrWhiteSpace(gameServer.Notice))
                        {
                            Log(server.ID, "[Notice] " + gameServer.Notice);
                        }
                        SetServerStatus(server, "Started");

                        if (g_bDiscordAlert[Int32.Parse(server.ID)])
                        {
                            var webhook = new Discord.Webhook(g_DiscordWebhook[Int32.Parse(server.ID)], g_DonorType);
                            await webhook.Send(server.ID, server.Game, "Started", server.Name, server.IP, server.Port);
                        }
                    }
                }
            });
        }

        const int UPDATE_INTERVAL_MINUTE = 10;
        private async void StartAutoUpdateCheck(Functions.ServerTable server)
        {
            int serverId = Int32.Parse(server.ID);

            //Save the process of game server
            Process p = g_Process[serverId];

            dynamic gameServer = GameServer.ClassObject.Get(server.Game, new Functions.ServerConfig(server.ID));

            string localVersion = gameServer.GetLocalBuild();

            while (!p.HasExited)
            {
                if (!g_bAutoUpdate[serverId] || g_iServerStatus[serverId] == ServerStatus.Updating)
                {
                    await Task.Delay(1000);

                    continue;
                }

                await Task.Delay(60000 * UPDATE_INTERVAL_MINUTE);

                //Try to get local build again if not found just now
                if (string.IsNullOrWhiteSpace(localVersion))
                {
                    localVersion = gameServer.GetLocalBuild();
                }

                //Get remote build
                string remoteVersion = await gameServer.GetRemoteBuild();

                //Continue if success to get localVersion and remoteVersion
                if (!string.IsNullOrWhiteSpace(localVersion) && !string.IsNullOrWhiteSpace(remoteVersion))
                {
                    if (g_iServerStatus[serverId] != ServerStatus.Started)
                    {
                        break;
                    }

                    Log(server.ID, $"Checking: Version ({localVersion}) => ({remoteVersion})");

                    if (localVersion != remoteVersion)
                    {
                        g_Process[serverId] = null;

                        //Begin stop
                        g_iServerStatus[serverId] = ServerStatus.Stopping;
                        SetServerStatus(server, "Stopping");

                        //Stop the server
                        await Server_BeginStop(server, p);

                        if (!p.HasExited)
                        {
                            p.Kill();
                        }

                        g_iServerStatus[serverId] = ServerStatus.Updating;
                        SetServerStatus(server, "Updating");

                        //Update the server
                        bool updated = await gameServer.Update();

                        if (updated)
                        {
                            Log(server.ID, $"Server: Updated ({remoteVersion})");
                        }
                        else
                        {
                            Log(server.ID, "Server: Fail to update");
                            Log(server.ID, "[ERROR] " + gameServer.Error);
                        }

                        //Start the server
                        g_iServerStatus[serverId] = ServerStatus.Starting;
                        SetServerStatus(server, "Starting");

                        var gameServerStart = await Server_BeginStart(server);
                        if (gameServerStart == null) { return; }

                        g_iServerStatus[serverId] = ServerStatus.Started;
                        SetServerStatus(server, "Started");

                        break;
                    }
                }
                else if (string.IsNullOrWhiteSpace(localVersion))
                {
                    Log(server.ID, $"[NOTICE] Fail to get local build.");
                }
                else if (string.IsNullOrWhiteSpace(remoteVersion))
                {
                    Log(server.ID, $"[NOTICE] Fail to get remote build.");
                }
            }
        }

        private void SetServerStatus(Functions.ServerTable server, string status)
        {
            server.Status = status;

            for (int i = 0; i < ServerGrid.Items.Count; i++)
            {
                var temp = ServerGrid.Items[i] as Functions.ServerTable;

                if (server.ID == temp.ID)
                {
                    if (ServerGrid.SelectedItem == ServerGrid.Items[i])
                    {
                        ServerGrid.Items.RemoveAt(i);
                        ServerGrid.Items.Insert(i, server);
                        ServerGrid.SelectedItem = ServerGrid.Items[i];
                    }
                    else
                    {
                        ServerGrid.Items.RemoveAt(i);
                        ServerGrid.Items.Insert(i, server);
                    }

                    break;
                }
            }
        }

        public void Log(string serverId, string logText)
        {
            string log = $"[{DateTime.Now.ToString("MM/dd/yyyy-HH:mm:ss")}][#{serverId}] {logText}" + Environment.NewLine;
            string logPath = WGSM_PATH + "/logs/";
            if (!Directory.Exists(logPath))
            {
                Directory.CreateDirectory(logPath);
            }

            string logFile = logPath + $"L{DateTime.Now.ToString("yyyyMMdd")}.log";
            if (!File.Exists(logFile))
            {
                File.Create(logFile).Dispose();
            }

            File.AppendAllText(logFile, log);

            textBox_wgsmlog.AppendText(log);
            textBox_wgsmlog.ScrollToEnd();
        }

        private void Button_ClearServerConsole_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            g_ServerConsoles[Int32.Parse(server.ID)].Clear();
            console.Clear();
        }


        private void Button_ClearWGSMLog_Click(object sender, RoutedEventArgs e)
        {
            textBox_wgsmlog.Clear();
        }

        private void SendCommand(Functions.ServerTable server, string command)
        {
            Process p = g_Process[Int32.Parse(server.ID)];
            if (p == null) { return; }

            if (server.Game == GameServer._7DTD.FullName)
            {
                g_ServerConsoles[Int32.Parse(server.ID)].InputFor7DTD(p, command);
                return;
            }

            g_ServerConsoles[Int32.Parse(server.ID)].Input(p, command);
        }

        private static bool IsValidIPAddress(string ip)
        {
            if (String.IsNullOrWhiteSpace(ip))
            {
                return false;
            }

            string[] splitValues = ip.Split('.');
            if (splitValues.Length != 4)
            {
                return false;
            }

            return splitValues.All(r => byte.TryParse(r, out byte tempForParsing));
        }

        private static bool IsValidPort(string port)
        {
            if (!Int32.TryParse(port, out int portnum))
            {
                return false;
            }

            return portnum > 1 && portnum < 65535;
        }

        private void Browse_ServerBackups_Click(object sender, RoutedEventArgs e)
        {
            var row = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (row == null) { return; }

            string path = WGSM_PATH + @"\backups\" + row.ID;
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            Process.Start(path);
        }

        private void Browse_ServerConfigs_Click(object sender, RoutedEventArgs e)
        {
            var row = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (row == null) { return; }

            string path = WGSM_PATH + @"\servers\" + row.ID + @"\configs";
            if (Directory.Exists(path))
            {
                Process.Start(path);
            }
        }

        private void Browse_ServerFiles_Click(object sender, RoutedEventArgs e)
        {
            var row = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (row == null) { return; }

            string path = WGSM_PATH + @"\servers\" + row.ID + @"\serverfiles";
            if (Directory.Exists(path))
            {
                Process.Start(path);
            }
        }

        private void Button_Discord_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("https://discord.gg/bGc7t2R");
        }

        private void Button_Patreon_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("https://www.patreon.com/WindowsGSM/");
        }

        private void Button_Settings_Click(object sender, RoutedEventArgs e)
        {
            RightWindowCommandsOverlayBehavior = WindowCommandsOverlayBehavior.HiddenTitleBar;
            WindowButtonCommandsOverlayBehavior = WindowCommandsOverlayBehavior.HiddenTitleBar;

            MahAppFlyout.IsOpen = !MahAppFlyout.IsOpen;
        }

        private void HardWareAcceleration_IsCheckedChanged(object sender, EventArgs e)
        {
            RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true);
            if (key != null)
            {
                key.SetValue("HardWareAcceleration", (MahAppSwitch_HardWareAcceleration.IsChecked ?? false).ToString());
                key.Close();
            }

            RenderOptions.ProcessRenderMode = (MahAppSwitch_HardWareAcceleration.IsChecked ?? false) ? System.Windows.Interop.RenderMode.SoftwareOnly : System.Windows.Interop.RenderMode.Default;
        }

        private void UIAnimation_IsCheckedChanged(object sender, EventArgs e)
        {
            RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true);
            if (key != null)
            {
                key.SetValue("UIAnimation", (MahAppSwitch_UIAnimation.IsChecked ?? false).ToString());
                key.Close();
            }

            WindowTransitionsEnabled = MahAppSwitch_UIAnimation.IsChecked ?? false;
        }

        private void DarkTheme_IsCheckedChanged(object sender, EventArgs e)
        {
            RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true);
            if (key != null)
            {
                key.SetValue("DarkTheme", (MahAppSwitch_DarkTheme.IsChecked ?? false).ToString());
                key.Close();
            }

            ThemeManager.ChangeAppTheme(App.Current, (MahAppSwitch_DarkTheme.IsChecked ?? false) ? "BaseDark" : "BaseLight");
        }

        private void StartOnBoot_IsCheckedChanged(object sender, EventArgs e)
        {
            RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true);
            if (key != null)
            {
                key.SetValue("StartOnBoot", (MahAppSwitch_StartOnBoot.IsChecked ?? false).ToString());
                key.Close();
            }

            SetStartOnBoot(MahAppSwitch_StartOnBoot.IsChecked ?? false);
        }

        private void SetStartOnBoot(bool enable)
        {
            string taskName = "WindowsGSM";
            string wgsmPath = Process.GetCurrentProcess().MainModule.FileName;
            if (enable)
            {
                Process.Start("schtasks", $"/create /tn {taskName} /tr \"{wgsmPath}\" /sc onlogon /rl HIGHEST /f");
            }
            else
            {
                Process.Start("schtasks", $"/delete /tn {taskName} /f");
            }
        }

        private async void DonorTheme_IsCheckedChanged(object sender, EventArgs e)
        {
            RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true);

            //If switch is checked
            if (!MahAppSwitch_DonorTheme.IsChecked ?? false)
            {
                SetDonorTheme();
                key.SetValue("DonorTheme", (MahAppSwitch_DonorTheme.IsChecked ?? false).ToString());
                key.Close();
                return;
            }

            //If switch is not checked
            key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true);
            string authKey = (key.GetValue("DonorAuthKey") == null) ? "" : key.GetValue("DonorAuthKey").ToString();

            var settings = new MetroDialogSettings
            {
                AffirmativeButtonText = "Activate",
                DefaultText = authKey
            };

            authKey = await this.ShowInputAsync("Donor Theme (Patreon)", "Please enter the activation key.", settings);

            //If pressed cancel or key is null or whitespace
            if (String.IsNullOrWhiteSpace(authKey))
            {
                MahAppSwitch_DonorTheme.IsChecked = false;
                key.Close();
                return;
            }

            ProgressDialogController controller = await this.ShowProgressAsync("Authenticating...", "Please wait...");
            controller.SetIndeterminate();
            bool success = await ActivateDonorTheme(authKey);
            await controller.CloseAsync();

            if (success)
            {
                key.SetValue("DonorTheme", "True");
                key.SetValue("DonorAuthKey", authKey);
                await this.ShowMessageAsync("Success!", "Thanks for your donation! Here is your Donor Theme.");
            }
            else
            {
                key.SetValue("DonorTheme", "False");
                key.SetValue("DonorAuthKey", "");
                await this.ShowMessageAsync("Fail to activate.", "Please visit https://windowsgsm.com/patreon/ to get the key.");

                MahAppSwitch_DonorTheme.IsChecked = false;
            }
            key.Close();
        }

        private async Task<bool> ActivateDonorTheme(string authKey)
        {
            try
            {
                using (var webClient = new WebClient())
                {
                    string json = await webClient.DownloadStringTaskAsync($"https://windowsgsm.com/patreon/patreonAuth.php?auth={authKey}");
                    bool success = (JObject.Parse(json)["success"].ToString() == "True") ? true : false;

                    if (success)
                    {
                        string name = JObject.Parse(json)["name"].ToString();
                        Title = $"WindowsGSM {WGSM_VERSION} - Patreon: {name}";

                        string type = JObject.Parse(json)["type"].ToString();
                        SetDonorTheme(type);

                        g_DonorType = type;

                        return true;
                    }
                }
            }
            catch
            {

            }

            return false;
        }

        private void SetDonorTheme(string type = "")
        {
            //Set theme
            AppTheme theme = ThemeManager.GetAppTheme((MahAppSwitch_DarkTheme.IsChecked ?? false) ? "BaseDark" : "BaseLight");
            string color = "Teal";
            switch (type)
            {
                case "BRONZE":
                    color = "Orange";
                    break;
                case "GOLD":
                    color = "Amber";
                    break;
                case "EMERALD":
                    color = "Emerald";
                    break;
            }
            ThemeManager.ChangeAppStyle(App.Current, ThemeManager.GetAccent(color), theme);

            //Set icon
            string uriPath = "pack://application:,,,/Images/WindowsGSM";
            switch (type)
            {
                case "BRONZE":
                case "GOLD":
                case "EMERALD":
                    uriPath += $"-{type}";
                    break;
            }
            uriPath += ".ico";
            var iconUri = new Uri(uriPath, UriKind.RelativeOrAbsolute);
            Icon = BitmapFrame.Create(iconUri);

            //Set notify icon
            Stream iconStream = System.Windows.Application.GetResourceStream(new Uri(uriPath)).Stream;
            if (iconStream != null)
            {
                notifyIcon.Icon = new System.Drawing.Icon(iconStream);
            }
        }

        private void Help_OnlineDocumentation_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("https://docs.windowsgsm.com");
        }

        private void Help_ReportIssue_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("https://github.com/BattlefieldDuck/WindowsGSM/issues");
        }

        private void Help_CheckForUpdate_Click(object sender, RoutedEventArgs e)
        {
            string messageText = "Your WindowsGSM is up to date.";

            string latestVersion = GetLatestVersion();
            if (latestVersion != WGSM_VERSION)
            {
                messageText = "A new version of WindowsGSM is available, would you like to browse the release page?";

                MessageBoxResult result = System.Windows.MessageBox.Show("Current version: " + WGSM_VERSION + "\nLatest version: " + latestVersion + "\n\n" + messageText, "Check for Update", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    Process.Start("https://github.com/BattlefieldDuck/WindowsGSM/releases");
                }
            }
            else
            {
                System.Windows.MessageBox.Show("Current version: " + WGSM_VERSION + "\nLatest version: " + latestVersion + "\n\n" + messageText, "Check for Update", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private string GetLatestVersion()
        {
            var webRequest = System.Net.WebRequest.Create("https://api.github.com/repos/BattlefieldDuck/WindowsGSM/releases/latest") as HttpWebRequest;
            if (webRequest != null)
            {
                webRequest.Method = "GET";
                webRequest.UserAgent = "Anything";
                webRequest.ServicePoint.Expect100Continue = false;

                try
                {
                    using (var responseReader = new StreamReader(webRequest.GetResponse().GetResponseStream()))
                    {
                        string json = responseReader.ReadToEnd();
                        string version = JObject.Parse(json)["tag_name"].ToString();

                        return version;
                    }
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        private void Tools_GlobalServerListCheck_Click(object sender, RoutedEventArgs e)
        {
            var row = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (row == null) { return; }

            if (row.Game == GameServer.MCPE.FullName || row.Game == GameServer.MC.FullName)
            {
                Log(row.ID, $"This feature is not applicable on {row.Game}");
                return;
            }

            string publicIP = GetPublicIP();
            if (publicIP == null)
            {
                Log(row.ID, "Fail to check. Reason: Fail to get the public ip.");
                return;
            }

            string messageText = $"Server Name: {row.Name}\nPublic IP: {publicIP}\nServer Port: {row.Port}";
            if (Tools.GlobalServerList.IsServerOnSteamServerList(publicIP, row.Port))
            {
                System.Windows.MessageBox.Show(messageText + "\n\nResult: Online\n\nYour server is on the global server list!", "Global Server List Check", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show(messageText + "\n\nResult: Offline\n\nYour server is not on the global server list.", "Global Server List Check", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Tools_InstallSourcemodMetamod_Click(object sender, RoutedEventArgs e)
        {

        }

        private string GetPublicIP()
        {
            try
            {
                using (var webClient = new WebClient())
                {
                    return webClient.DownloadString("https://ipinfo.io/ip").Replace("\n", "");
                }
            }
            catch
            {
                return null;
            }
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
        }

        private void OnBalloonTipClick(object sender, EventArgs e)
        {
        }

        private void NotifyIcon_MouseClick(Object sender, System.Windows.Forms.MouseEventArgs e)
        {
            notifyIcon.Visible = false;

            WindowState = WindowState.Normal;
            Show();
        }

        private void Button_Hide_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            notifyIcon.Visible = true;
            notifyIcon.ShowBalloonTip(0);
            notifyIcon.Visible = false;
            notifyIcon.Visible = true;
        }

        private void Button_AutoRestart_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            g_bAutoRestart[Int32.Parse(server.ID)] = Functions.ServerConfig.ToggleSetting(server.ID, "autorestart");
            button_autorestart.Content = (g_bAutoRestart[Int32.Parse(server.ID)]) ? "TRUE" : "FALSE";
            button_autorestart.Background = (g_bAutoRestart[Int32.Parse(server.ID)]) ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.Red;
        }

        private void Button_AutoStart_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            g_bAutoStart[Int32.Parse(server.ID)] = Functions.ServerConfig.ToggleSetting(server.ID, "autostart");
            button_autostart.Content = (g_bAutoStart[Int32.Parse(server.ID)]) ? "TRUE" : "FALSE";
            button_autostart.Background = (g_bAutoStart[Int32.Parse(server.ID)]) ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.Red;
        }

        private void Button_AutoUpdate_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            g_bAutoUpdate[Int32.Parse(server.ID)] = Functions.ServerConfig.ToggleSetting(server.ID, "autoupdate");
            button_autoupdate.Content = (g_bAutoUpdate[Int32.Parse(server.ID)]) ? "TRUE" : "FALSE";
            button_autoupdate.Background = (g_bAutoUpdate[Int32.Parse(server.ID)]) ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.Red;
        }

        private void Button_UpdateOnStart_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            g_bUpdateOnStart[Int32.Parse(server.ID)] = Functions.ServerConfig.ToggleSetting(server.ID, "updateonstart");
            button_updateonstart.Content = (g_bUpdateOnStart[Int32.Parse(server.ID)]) ? "TRUE" : "FALSE";
            button_updateonstart.Background = (g_bUpdateOnStart[Int32.Parse(server.ID)]) ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.Red;
        }

        private void Button_DiscordAlert_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            g_bDiscordAlert[Int32.Parse(server.ID)] = Functions.ServerConfig.ToggleSetting(server.ID, "discordalert");
            button_discordalert.Content = (g_bDiscordAlert[Int32.Parse(server.ID)]) ? "TRUE" : "FALSE";
            button_discordalert.Background = (g_bDiscordAlert[Int32.Parse(server.ID)]) ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.Red;
            button_discordtest.IsEnabled = (g_bDiscordAlert[Int32.Parse(server.ID)]) ? true : false;
        }
    }
}
