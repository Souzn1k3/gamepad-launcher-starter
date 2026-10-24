using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace GamepadLauncherStarter
{
    public sealed class SettingsForm : Form
    {
        private readonly AppRuntime _runtime;
        private readonly AppSettings _working;
        private bool _refreshingApps;
        private bool _refreshingPads;
        private bool _disposed;

        private TabControl _tabs;
        private CheckBox _chkEnabled;
        private CheckBox _chkTrayOnly;
        private CheckBox _chkNotifications;
        private CheckBox _chkStartupPad;
        private CheckBox _chkSkipFullscreen;
        private CheckBox _chkStartWithWindows;
        private CheckBox _chkManageSteam;
        private Button _btnRestoreSteam;
        private ComboBox _cmbPartial;
        private ListView _lvApps;
        private ListView _lvPads;
        private Label _lblPadsStatus;
        private TextBox _txtDiagnostics;

        public AppSettings ResultSettings { get; private set; }

        public SettingsForm(AppRuntime runtime)
        {
            _runtime = runtime;
            _working = SettingsStore.Clone(runtime.Settings);

            Font = new Font("Segoe UI", 9F);
            Text = "Gamepad Launcher Starter — настройки";
            Icon = AppIcon.Get();
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = true;
            ClientSize = new Size(880, 680);
            MinimumSize = new Size(760, 560);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);

            InitializeUi();
            LoadGeneral();
            RefreshApps();
            RefreshPads();
            RefreshDiagnostics();

            _runtime.StatusChanged += OnRuntimeStatusChanged;
        }

        private void InitializeUi()
        {
            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;
            _tabs.SelectedIndexChanged += delegate
            {
                if (_tabs.SelectedIndex == 3)
                    RefreshDiagnostics();
            };

            _tabs.TabPages.Add(CreateGeneralTab());
            _tabs.TabPages.Add(CreateAppsTab());
            _tabs.TabPages.Add(CreatePadsTab());
            _tabs.TabPages.Add(CreateDiagnosticsTab());

            Panel buttonPanel = new Panel();
            buttonPanel.Dock = DockStyle.Bottom;
            buttonPanel.Height = 50;
            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.Padding = new Padding(10, 8, 10, 8);
            Button ok = new Button();
            ok.Text = "ОК";
            ok.Width = 90;
            ok.Height = 30;
            Button cancel = new Button();
            cancel.Text = "Отмена";
            cancel.Width = 90;
            cancel.Height = 30;
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            buttonPanel.Controls.Add(buttons);
            ok.Click += OnSaveClick;

            Controls.Add(_tabs);
            Controls.Add(buttonPanel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private TabPage CreateGeneralTab()
        {
            TabPage page = new TabPage("Общие");

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Padding = new Padding(10);
            root.AutoScroll = true;

            GroupBox behavior = NewGroup("Поведение");
            TableLayoutPanel behaviorTable = InnerTable(behavior);
            _chkEnabled = NewCheck("Отслеживать подключение геймпада");
            AddRow(behaviorTable, _chkEnabled);
            _chkTrayOnly = NewCheck("Только трей: ничего не запускать, показывать только уведомление");
            AddRow(behaviorTable, _chkTrayOnly);

            Label partialLabel = NewLabel("Если часть выбранных приложений уже запущена:");
            AddRow(behaviorTable, partialLabel);
            _cmbPartial = new ComboBox();
            _cmbPartial.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbPartial.Width = 620;
            _cmbPartial.Anchor = AnchorStyles.Left;
            _cmbPartial.Items.Add("Запускать только те приложения, которые ещё не запущены");
            _cmbPartial.Items.Add("Ничего не делать, если хотя бы одно выбранное приложение уже запущено");
            AddRow(behaviorTable, _cmbPartial);

            _chkSkipFullscreen = NewCheck("Не реагировать в полноэкранном режиме");
            AddRow(behaviorTable, _chkSkipFullscreen);
            AddRow(behaviorTable, NewHint("Пока активно полноэкранное приложение (игра, видео, Big Picture), подключение геймпада игнорируется. " +
                                          "Если запуск должен срабатывать всегда — снимите галочку."));
            AddRootRow(root, behavior);

            GroupBox notifications = NewGroup("Уведомления");
            TableLayoutPanel notificationsTable = InnerTable(notifications);
            _chkNotifications = NewCheck("Показывать уведомления в трее");
            AddRow(notificationsTable, _chkNotifications);
            AddRow(notificationsTable, NewHint("Если выключить, никакие всплывающие сообщения показываться не будут. Все события останутся в журнале."));
            AddRootRow(root, notifications);

            GroupBox startup = NewGroup("Запуск программы");
            TableLayoutPanel startupTable = InnerTable(startup);
            _chkStartWithWindows = NewCheck("Запускать программу при входе в Windows");
            AddRow(startupTable, _chkStartWithWindows);
            _chkStartupPad = NewCheck("Запускать приложения, если геймпад уже подключён при старте программы");
            AddRow(startupTable, _chkStartupPad);
            _chkManageSteam = NewCheck("Убрать Steam из автозапуска Windows");
            AddRow(startupTable, _chkManageSteam);
            AddRow(startupTable, NewHint("Steam будет запускаться только при подключении геймпада. Снятие галочки и нажатие «ОК» восстановит прежний автозапуск Steam."));
            _btnRestoreSteam = new Button();
            _btnRestoreSteam.Text = "Вернуть автозапуск Steam";
            _btnRestoreSteam.AutoSize = true;
            _btnRestoreSteam.Margin = new Padding(0, 2, 0, 4);
            _btnRestoreSteam.Click += OnRestoreSteamClick;
            AddRow(startupTable, _btnRestoreSteam);
            AddRootRow(root, startup);

            page.Controls.Add(root);
            return page;
        }

        private TabPage CreateAppsTab()
        {
            TabPage page = new TabPage("Приложения");

            TableLayoutPanel layout = NewTabLayout();
            layout.RowCount = 3;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label hint = new Label();
            hint.Text = "Отметьте приложения, которые должны запускаться при подключении геймпада. " +
                        "Порядок строк не важен.";
            hint.AutoSize = false;
            hint.Height = 34;
            hint.Dock = DockStyle.Fill;
            hint.TextAlign = ContentAlignment.MiddleLeft;

            _lvApps = new ListView();
            _lvApps.Dock = DockStyle.Fill;
            _lvApps.View = View.Details;
            _lvApps.CheckBoxes = true;
            _lvApps.FullRowSelect = true;
            _lvApps.MultiSelect = false;
            _lvApps.HideSelection = false;
            _lvApps.Columns.Add("Приложение", 150);
            _lvApps.Columns.Add("Путь", 300);
            _lvApps.Columns.Add("Процесс", 95);
            _lvApps.Columns.Add("Режим", 80);
            _lvApps.Columns.Add("Если запущено", 135);
            _lvApps.ItemChecked += OnAppItemChecked;
            _lvApps.DoubleClick += delegate { EditSelectedApp(); };

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Top;
            buttons.AutoSize = true;
            buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttons.WrapContents = true;
            buttons.Padding = new Padding(0, 4, 0, 0);
            AddButton(buttons, "Добавить", delegate { AddApp(); });
            AddButton(buttons, "Изменить", delegate { EditSelectedApp(); });
            AddButton(buttons, "Удалить", delegate { DeleteSelectedApp(); });
            AddButton(buttons, "Обновить пути", delegate { RefreshPaths(); });
            AddButton(buttons, "Восстановить пресеты", delegate { RestorePresets(); });
            AddButton(buttons, "Запустить сейчас", delegate { LaunchSelectedApp(); });

            layout.Controls.Add(hint, 0, 0);
            layout.Controls.Add(_lvApps, 0, 1);
            layout.Controls.Add(buttons, 0, 2);
            page.Controls.Add(layout);
            return page;
        }

        private TabPage CreatePadsTab()
        {
            TabPage page = new TabPage("Геймпады");

            TableLayoutPanel layout = NewTabLayout();
            layout.RowCount = 4;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label hint = new Label();
            hint.Text = "Галочка означает, что геймпад учитывается. Снимите её, чтобы игнорировать устройство. " +
                        "Изменения применяются по кнопке «ОК».";
            hint.AutoSize = false;
            hint.Height = 36;
            hint.Dock = DockStyle.Fill;
            hint.TextAlign = ContentAlignment.MiddleLeft;

            _lblPadsStatus = new Label();
            _lblPadsStatus.AutoSize = false;
            _lblPadsStatus.Height = 24;
            _lblPadsStatus.Dock = DockStyle.Fill;
            _lblPadsStatus.TextAlign = ContentAlignment.MiddleLeft;
            _lblPadsStatus.Font = new Font(Font, FontStyle.Bold);

            _lvPads = new ListView();
            _lvPads.Dock = DockStyle.Fill;
            _lvPads.View = View.Details;
            _lvPads.CheckBoxes = true;
            _lvPads.FullRowSelect = true;
            _lvPads.MultiSelect = false;
            _lvPads.HideSelection = false;
            _lvPads.Columns.Add("Устройство", 260);
            _lvPads.Columns.Add("Идентификатор", 360);
            _lvPads.Columns.Add("Учёт", 130);
            _lvPads.ItemChecked += OnPadItemChecked;

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Top;
            buttons.AutoSize = true;
            buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttons.WrapContents = true;
            buttons.Padding = new Padding(0, 4, 0, 0);
            AddButton(buttons, "Обновить список", delegate
            {
                if (_runtime.Watcher != null)
                    _runtime.Watcher.RescanNow();
                RefreshPads();
            });
            AddButton(buttons, "Симулировать подключение", delegate { SimulateArrival(); });

            layout.Controls.Add(hint, 0, 0);
            layout.Controls.Add(_lblPadsStatus, 0, 1);
            layout.Controls.Add(_lvPads, 0, 2);
            layout.Controls.Add(buttons, 0, 3);
            page.Controls.Add(layout);
            return page;
        }

        private TabPage CreateDiagnosticsTab()
        {
            TabPage page = new TabPage("Диагностика");

            TableLayoutPanel layout = NewTabLayout();
            layout.RowCount = 2;
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _txtDiagnostics = new TextBox();
            _txtDiagnostics.Dock = DockStyle.Fill;
            _txtDiagnostics.Multiline = true;
            _txtDiagnostics.ReadOnly = true;
            _txtDiagnostics.ScrollBars = ScrollBars.Both;
            _txtDiagnostics.WordWrap = false;
            _txtDiagnostics.Font = new Font("Consolas", 9F);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Top;
            buttons.AutoSize = true;
            buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttons.WrapContents = true;
            buttons.Padding = new Padding(0, 4, 0, 0);
            AddButton(buttons, "Обновить", delegate { RefreshDiagnostics(); });
            AddButton(buttons, "Скопировать", delegate { CopyDiagnostics(); });
            AddButton(buttons, "Открыть журнал", delegate { OpenLog(); });
            AddButton(buttons, "Папка данных", delegate { OpenDataFolder(); });

            layout.Controls.Add(_txtDiagnostics, 0, 0);
            layout.Controls.Add(buttons, 0, 1);
            page.Controls.Add(layout);
            return page;
        }

        private static TableLayoutPanel NewTabLayout()
        {
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.Padding = new Padding(8);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return layout;
        }

        private static TableLayoutPanel NewVerticalTable()
        {
            TableLayoutPanel table = new TableLayoutPanel();
            table.Dock = DockStyle.Top;
            table.AutoSize = true;
            table.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            table.ColumnCount = 1;
            table.RowCount = 1;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Margin = new Padding(0);
            return table;
        }

        private static void AddRow(TableLayoutPanel table, Control control)
        {
            int row;
            if (table.Controls.Count == 0)
            {
                row = 0;
            }
            else
            {
                row = table.RowCount;
                table.RowCount = row + 1;
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
            control.Margin = new Padding(0, 2, 0, 5);
            table.Controls.Add(control, 0, row);
        }

        private static void AddRootRow(TableLayoutPanel root, Control control)
        {
            int row;
            if (root.Controls.Count == 0)
            {
                row = 0;
            }
            else
            {
                row = root.RowCount;
                root.RowCount = row + 1;
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
            control.Dock = DockStyle.Fill;
            root.Controls.Add(control, 0, row);
        }

        private static GroupBox NewGroup(string title)
        {
            GroupBox group = new GroupBox();
            group.Text = title;
            group.AutoSize = true;
            group.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            group.Dock = DockStyle.Fill;
            group.Padding = new Padding(12, 8, 12, 10);
            group.Margin = new Padding(0, 0, 0, 10);
            group.Controls.Add(NewVerticalTable());
            return group;
        }

        private static TableLayoutPanel InnerTable(GroupBox group)
        {
            return (TableLayoutPanel)group.Controls[0];
        }

        private static CheckBox NewCheck(string text)
        {
            CheckBox check = new CheckBox();
            check.Text = text;
            check.AutoSize = true;
            check.Anchor = AnchorStyles.Left;
            return check;
        }

        private static Label NewLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Anchor = AnchorStyles.Left;
            return label;
        }

        private static Label NewHint(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.ForeColor = Color.Gray;
            label.MaximumSize = new Size(700, 0);
            label.Anchor = AnchorStyles.Left;
            return label;
        }

        private static void AddButton(FlowLayoutPanel panel, string text, EventHandler handler)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.Height = 29;
            button.Margin = new Padding(0, 0, 6, 6);
            button.Click += handler;
            panel.Controls.Add(button);
        }

        private void LoadGeneral()
        {
            _chkEnabled.Checked = _working.Enabled;
            _chkTrayOnly.Checked = _working.TrayOnly;
            _chkNotifications.Checked = _working.ShowNotifications;
            _chkStartupPad.Checked = _working.LaunchOnStartIfConnected;
            _chkSkipFullscreen.Checked = _working.SkipWhenFullscreenGame;
            _chkStartWithWindows.Checked = _working.StartWithWindows;
            _chkManageSteam.Checked = _working.ManageSteamAutostart;
            _btnRestoreSteam.Enabled = !string.IsNullOrEmpty(_working.SteamAutostartBackup);

            _cmbPartial.SelectedIndex = _working.PartialMode == PartialLaunchMode.DoNothingIfAnyRunning ? 1 : 0;
        }

        private void RefreshApps()
        {
            if (_lvApps == null)
                return;
            _refreshingApps = true;
            try
            {
                _lvApps.BeginUpdate();
                _lvApps.Items.Clear();
                foreach (AppEntry app in _working.Apps)
                {
                    if (app == null)
                        continue;
                    string pathText = app.ExecutablePath;
                    if (string.IsNullOrEmpty(pathText))
                        pathText = "(не найден)";
                    else if (!File.Exists(pathText))
                        pathText = pathText + " (не найден)";

                    ListViewItem item = new ListViewItem(app.Name);
                    item.SubItems.Add(pathText);
                    item.SubItems.Add(LauncherService.ResolveProcessName(app));
                    item.SubItems.Add(app.LaunchMode == LaunchMode.Minimized ? "Свёрнуто" : "Окно");
                    item.SubItems.Add(app.ShowWindowIfRunning ? "Развернуть" : "Не трогать");
                    item.Tag = app;
                    item.Checked = app.Enabled;
                    item.ForeColor = app.Enabled ? SystemColors.ControlText : Color.Gray;
                    _lvApps.Items.Add(item);
                }
                _lvApps.EndUpdate();
            }
            finally
            {
                _refreshingApps = false;
            }
        }

        private void RefreshPads()
        {
            if (_lvPads == null || _lblPadsStatus == null)
                return;
            _refreshingPads = true;
            try
            {
                List<GamepadDevice> devices = _runtime.Watcher != null
                    ? _runtime.Watcher.Current
                    : new List<GamepadDevice>();

                _lvPads.BeginUpdate();
                _lvPads.Items.Clear();
                foreach (GamepadDevice device in devices)
                {
                    bool ignored = GamepadWatcher.IsIgnored(device, _working);
                    ListViewItem item = new ListViewItem(device.DisplayName);
                    item.SubItems.Add(device.InstanceGuid);
                    item.SubItems.Add(ignored ? "игнорируется" : "учитывается");
                    item.Tag = device;
                    item.Checked = !ignored;
                    _lvPads.Items.Add(item);
                }
                _lvPads.EndUpdate();

                if (devices.Count == 0)
                    _lblPadsStatus.Text = "Геймпады не обнаружены.";
                else
                    _lblPadsStatus.Text = "Подключено геймпадов: " + devices.Count;
            }
            finally
            {
                _refreshingPads = false;
            }
        }

        private void RefreshDiagnostics()
        {
            if (_txtDiagnostics == null)
                return;
            try
            {
                _txtDiagnostics.Text = _runtime.BuildDiagnostics();
                _txtDiagnostics.SelectionStart = 0;
                _txtDiagnostics.SelectionLength = 0;
            }
            catch (Exception ex)
            {
                _txtDiagnostics.Text = "Не удалось собрать диагностику: " + ex.Message;
            }
        }

        private void OnAppItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (_refreshingApps || e == null || e.Item == null)
                return;
            AppEntry app = e.Item.Tag as AppEntry;
            if (app == null)
                return;
            app.Enabled = e.Item.Checked;
            e.Item.ForeColor = app.Enabled ? SystemColors.ControlText : Color.Gray;
        }

        private void OnPadItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (_refreshingPads || e == null || e.Item == null)
                return;
            GamepadDevice device = e.Item.Tag as GamepadDevice;
            if (device == null)
                return;

            bool ignored = !e.Item.Checked;
            SetIgnored(device, ignored);
            if (e.Item.SubItems.Count > 2)
                e.Item.SubItems[2].Text = ignored ? "игнорируется" : "учитывается";
        }

        private void SetIgnored(GamepadDevice device, bool ignored)
        {
            for (int i = _working.IgnoredDevices.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_working.IgnoredDevices[i].Guid, device.InstanceGuid, StringComparison.OrdinalIgnoreCase))
                    _working.IgnoredDevices.RemoveAt(i);
            }
            if (ignored)
            {
                IgnoredDevice entry = new IgnoredDevice();
                entry.Guid = device.InstanceGuid;
                entry.Name = device.DisplayName;
                _working.IgnoredDevices.Add(entry);
            }
        }

        private void AddApp()
        {
            AppEntry entry = new AppEntry();
            entry.Id = Guid.NewGuid().ToString("N");
            entry.Name = "Новое приложение";
            entry.Enabled = true;
            entry.LaunchMode = LaunchMode.Normal;
            entry.ShowWindowIfRunning = true;
            using (AppEditForm form = new AppEditForm(entry))
            {
                if (form.ShowDialog(this) == DialogResult.OK && form.Result != null)
                {
                    _working.Apps.Add(form.Result);
                    RefreshApps();
                }
            }
        }

        private void EditSelectedApp()
        {
            if (_lvApps.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Выберите приложение в списке.", "Приложения",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ListViewItem item = _lvApps.SelectedItems[0];
            AppEntry app = item.Tag as AppEntry;
            if (app == null)
                return;

            using (AppEditForm form = new AppEditForm(app))
            {
                if (form.ShowDialog(this) == DialogResult.OK && form.Result != null)
                {
                    int index = _working.Apps.IndexOf(app);
                    if (index >= 0)
                        _working.Apps[index] = form.Result;
                    RefreshApps();
                }
            }
        }

        private void DeleteSelectedApp()
        {
            if (_lvApps.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Выберите приложение в списке.", "Приложения",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            AppEntry app = _lvApps.SelectedItems[0].Tag as AppEntry;
            if (app == null)
                return;
            DialogResult answer = MessageBox.Show(this,
                "Удалить «" + app.Name + "» из списка?",
                "Удаление", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
                return;
            _working.Apps.Remove(app);
            RefreshApps();
        }

        private void RefreshPaths()
        {
            AppCatalog.AutodetectPaths(_working.Apps);
            RefreshApps();
            MessageBox.Show(this,
                "Пути встроенных приложений обновлены. Проверьте список.",
                "Пути", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void RestorePresets()
        {
            bool added = false;
            if (_working.FindApp(AppCatalog.SteamId) == null)
            {
                _working.Apps.Add(AppCatalog.CreateSteam());
                added = true;
            }
            if (_working.FindApp(AppCatalog.EpicId) == null)
            {
                _working.Apps.Add(AppCatalog.CreateEpic());
                added = true;
            }
            if (_working.FindApp(AppCatalog.DiscordId) == null)
            {
                _working.Apps.Add(AppCatalog.CreateDiscord());
                added = true;
            }
            AppCatalog.AutodetectPaths(_working.Apps);
            RefreshApps();
            MessageBox.Show(this,
                added ? "Отсутствующие пресеты добавлены." : "Все встроенные пресеты уже есть в списке.",
                "Пресеты", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void LaunchSelectedApp()
        {
            if (_lvApps.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Выберите приложение в списке.", "Запуск",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            AppEntry app = _lvApps.SelectedItems[0].Tag as AppEntry;
            if (app == null)
                return;

            LaunchResult result = LauncherService.LaunchOrShow(app);
            MessageBox.Show(this, app.Name + ": " + result.Message, "Проверка запуска",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void SimulateArrival()
        {
            DialogResult answer = MessageBox.Show(this,
                "Будет выполнена имитация подключения геймпада с текущими (несохранёнными) настройками.\n" +
                "Приложения могут быть запущены или развёрнуты. Продолжить?",
                "Симуляция", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
                return;
            _runtime.SimulateArrival(_working);
        }

        private void OnRestoreSteamClick(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_working.SteamAutostartBackup))
            {
                MessageBox.Show(this, "Резервная копия автозапуска Steam не найдена.",
                    "Steam", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult answer = MessageBox.Show(this,
                "Вернуть запись Steam в автозапуск Windows после нажатия «ОК»?",
                "Steam", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
                return;

            _chkManageSteam.Checked = false;
            _btnRestoreSteam.Enabled = false;
            MessageBox.Show(this, "Автозапуск Steam будет восстановлен после нажатия «ОК».",
                "Steam", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void CopyDiagnostics()
        {
            if (_txtDiagnostics == null || string.IsNullOrEmpty(_txtDiagnostics.Text))
                return;
            try
            {
                Clipboard.SetText(_txtDiagnostics.Text);
                MessageBox.Show(this, "Диагностика скопирована в буфер обмена.",
                    "Диагностика", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось скопировать: " + ex.Message,
                    "Диагностика", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenLog()
        {
            try
            {
                if (!string.IsNullOrEmpty(Logger.LogPath) && File.Exists(Logger.LogPath))
                    Process.Start("notepad.exe", Logger.LogPath);
                else
                    MessageBox.Show(this, "Файл журнала пока отсутствует.", "Журнал",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось открыть журнал: " + ex.Message,
                    "Журнал", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenDataFolder()
        {
            try
            {
                AppPaths.EnsureDataDirectory();
                Process.Start("explorer.exe", AppPaths.DataDirectory);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось открыть папку: " + ex.Message,
                    "Папка данных", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            _working.Enabled = _chkEnabled.Checked;
            _working.TrayOnly = _chkTrayOnly.Checked;
            _working.ShowNotifications = _chkNotifications.Checked;
            _working.LaunchOnStartIfConnected = _chkStartupPad.Checked;
            _working.SkipWhenFullscreenGame = _chkSkipFullscreen.Checked;
            _working.StartWithWindows = _chkStartWithWindows.Checked;
            _working.ManageSteamAutostart = _chkManageSteam.Checked;
            _working.PartialMode = _cmbPartial.SelectedIndex == 1
                ? PartialLaunchMode.DoNothingIfAnyRunning
                : PartialLaunchMode.LaunchMissing;

            List<string> unavailable = new List<string>();
            foreach (AppEntry app in _working.Apps)
            {
                if (app == null)
                    continue;
                if (string.IsNullOrEmpty(app.ProcessName))
                    app.ProcessName = LauncherService.ResolveProcessName(app);
                if (!app.Enabled)
                    continue;
                if (string.IsNullOrEmpty(app.ExecutablePath))
                    unavailable.Add(app.Name + " — путь не указан");
                else if (!File.Exists(app.ExecutablePath))
                    unavailable.Add(app.Name + " — файл не найден");
            }

            if (!_working.TrayOnly && unavailable.Count > 0)
            {
                DialogResult answer = MessageBox.Show(this,
                    "Некоторые включённые приложения недоступны:\n\n" +
                    string.Join(Environment.NewLine, unavailable.ToArray()) +
                    "\n\nОни будут пропущены при запуске. Продолжить?",
                    "Проверка", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes)
                    return;
            }

            ResultSettings = _working;
            DialogResult = DialogResult.OK;
        }

        private void OnRuntimeStatusChanged(object sender, EventArgs e)
        {
            if (_disposed || !IsHandleCreated)
                return;
            try
            {
                RefreshPads();
                if (_tabs != null && _tabs.SelectedIndex == 3)
                    RefreshDiagnostics();
            }
            catch
            {
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try
            {
                _runtime.StatusChanged -= OnRuntimeStatusChanged;
            }
            catch
            {
            }
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _disposed = true;
                try
                {
                    _runtime.StatusChanged -= OnRuntimeStatusChanged;
                }
                catch
                {
                }
            }
            base.Dispose(disposing);
        }
    }
}
