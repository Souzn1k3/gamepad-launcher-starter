using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace GamepadLauncherStarter
{
    public sealed class AppEditForm : Form
    {
        private readonly AppEntry _entry;
        private readonly TextBox _txtName;
        private readonly TextBox _txtPath;
        private readonly TextBox _txtProcess;
        private readonly TextBox _txtArguments;
        private readonly TextBox _txtMinimizedArguments;
        private readonly ComboBox _cmbMode;
        private readonly CheckBox _chkEnabled;
        private readonly CheckBox _chkShowWindow;

        public AppEntry Result { get; private set; }

        public AppEditForm(AppEntry entry)
        {
            _entry = entry != null ? entry.Clone() : new AppEntry();
            if (string.IsNullOrEmpty(_entry.Id))
                _entry.Id = Guid.NewGuid().ToString("N");

            Font = new Font("Segoe UI", 9F);
            Text = _entry.BuiltIn ? "Настройка пресета — " + _entry.Name : "Настройка приложения";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(660, 480);
            MinimumSize = new Size(560, 430);
            Icon = AppIcon.Get();
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);

            TableLayoutPanel table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.Padding = new Padding(14);
            table.ColumnCount = 2;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.RowCount = 11;
            for (int i = 0; i < 10; i++)
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _txtName = new TextBox();
            _txtPath = new TextBox();
            _txtProcess = new TextBox();
            _txtArguments = new TextBox();
            _txtMinimizedArguments = new TextBox();
            _cmbMode = new ComboBox();
            _chkEnabled = new CheckBox();
            _chkShowWindow = new CheckBox();

            AddField(table, 0, "Название:", _txtName);

            _chkEnabled.Text = "Включено";
            _chkEnabled.AutoSize = true;
            _chkEnabled.Margin = new Padding(0, 4, 0, 4);
            table.Controls.Add(_chkEnabled, 1, 1);

            Panel pathPanel = new Panel();
            pathPanel.Dock = DockStyle.Fill;
            pathPanel.Margin = new Padding(0, 3, 0, 3);
            pathPanel.Height = 27;
            Button browse = new Button();
            browse.Text = "Обзор...";
            browse.Dock = DockStyle.Right;
            browse.Width = 92;
            browse.Margin = new Padding(4, 0, 0, 0);
            _txtPath.Dock = DockStyle.Fill;
            pathPanel.Controls.Add(_txtPath);
            pathPanel.Controls.Add(browse);
            table.Controls.Add(pathPanel, 1, 2);
            AddLabel(table, 2, "Путь к .exe:");

            AddField(table, 3, "Имя процесса:", _txtProcess);
            AddField(table, 4, "Аргументы:", _txtArguments);
            AddField(table, 5, "Аргументы (свёрнуто):", _txtMinimizedArguments);

            Label hint = new Label();
            hint.AutoSize = true;
            hint.MaximumSize = new Size(470, 0);
            hint.ForeColor = Color.Gray;
            hint.Text = "Имя процесса — без .exe. Для встроенных пресетов путь можно не указывать: он определится автоматически. " +
                        "Аргументы для свёрнутого запуска можно оставить пустыми — тогда будут использованы обычные.";
            hint.Margin = new Padding(0, 2, 0, 8);
            table.Controls.Add(hint, 1, 6);
            table.SetColumnSpan(hint, 1);

            _cmbMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbMode.Width = 330;
            _cmbMode.Anchor = AnchorStyles.Left;
            _cmbMode.Items.Add("Обычное окно");
            _cmbMode.Items.Add("Свёрнуто (в трей, если поддерживается)");
            _cmbMode.Margin = new Padding(0, 3, 0, 3);
            table.Controls.Add(_cmbMode, 1, 7);
            AddLabel(table, 7, "Режим запуска:");

            _chkShowWindow.Text = "Разворачивать окно, если приложение уже запущено";
            _chkShowWindow.AutoSize = true;
            _chkShowWindow.Margin = new Padding(0, 4, 0, 4);
            table.Controls.Add(_chkShowWindow, 1, 8);

            Label secondHint = new Label();
            secondHint.AutoSize = true;
            secondHint.MaximumSize = new Size(640, 0);
            secondHint.ForeColor = Color.Gray;
            secondHint.Text = "Программа никогда не закрывает игры и другие запущенные приложения.";
            secondHint.Margin = new Padding(0, 6, 0, 0);
            table.Controls.Add(secondHint, 1, 9);

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

            Controls.Add(table);
            Controls.Add(buttonPanel);
            AcceptButton = ok;
            CancelButton = cancel;
            ok.Click += OnOk;

            _txtName.Text = _entry.Name;
            _txtPath.Text = _entry.ExecutablePath;
            _txtProcess.Text = _entry.ProcessName;
            _txtArguments.Text = _entry.Arguments;
            _txtMinimizedArguments.Text = _entry.MinimizedArguments;
            _chkEnabled.Checked = _entry.Enabled;
            _chkShowWindow.Checked = _entry.ShowWindowIfRunning;
            _cmbMode.SelectedIndex = _entry.LaunchMode == LaunchMode.Minimized ? 1 : 0;

            browse.Click += OnBrowse;
        }

        private void OnBrowse(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Выберите исполняемый файл";
                dialog.Filter = "Программы (*.exe)|*.exe|Все файлы (*.*)|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                string oldPath = _txtPath.Text;
                string oldProcess = _txtProcess.Text.Trim();
                string oldDerived = "";
                try
                {
                    if (!string.IsNullOrEmpty(oldPath))
                        oldDerived = Path.GetFileNameWithoutExtension(oldPath);
                }
                catch
                {
                }

                _txtPath.Text = dialog.FileName;
                if (oldProcess.Length == 0 || string.Equals(oldProcess, oldDerived, StringComparison.OrdinalIgnoreCase))
                    _txtProcess.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            }
        }

        private void OnOk(object sender, EventArgs e)
        {
            string name = _txtName.Text.Trim();
            if (name.Length == 0)
            {
                MessageBox.Show(this, "Укажите название приложения.", "Проверка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string path = _txtPath.Text.Trim();
            if (path.Length == 0 && !_entry.BuiltIn)
            {
                MessageBox.Show(this, "Укажите путь к исполняемому файлу.", "Проверка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (path.Length > 0 && !File.Exists(path))
            {
                DialogResult answer = MessageBox.Show(this,
                    "Файл не найден:\n" + path + "\n\nСохранить настройку всё равно?",
                    "Проверка", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes)
                    return;
            }

            string process = _txtProcess.Text.Trim();
            if (process.Length == 0 && path.Length > 0)
                process = Path.GetFileNameWithoutExtension(path);

            _entry.Name = name;
            _entry.Enabled = _chkEnabled.Checked;
            _entry.ExecutablePath = path;
            _entry.ProcessName = process;
            _entry.Arguments = _txtArguments.Text;
            _entry.MinimizedArguments = _txtMinimizedArguments.Text;
            _entry.LaunchMode = _cmbMode.SelectedIndex == 1 ? LaunchMode.Minimized : LaunchMode.Normal;
            _entry.ShowWindowIfRunning = _chkShowWindow.Checked;

            Result = _entry;
            DialogResult = DialogResult.OK;
        }

        private static void AddField(TableLayoutPanel table, int row, string label, Control control)
        {
            AddLabel(table, row, label);
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(0, 3, 0, 3);
            table.Controls.Add(control, 1, row);
        }

        private static void AddLabel(TableLayoutPanel table, int row, string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Anchor = AnchorStyles.Left;
            label.Margin = new Padding(0, 6, 8, 3);
            table.Controls.Add(label, 0, row);
        }
    }
}
