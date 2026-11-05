using System.Diagnostics;
using System.Text.Json;
namespace Drex;

public sealed partial class MainForm : Form
{
    static readonly string[] Days = { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" };
    static readonly string[] DayNames = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
    static string FirstLine(string s) => (s ?? "").Replace("\r", "").Split('\n')[0].Trim();

    // ---------- Scheduler ----------
    Control BuildScheduler()
    {
        var host = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Bg };
        Native.DarkScroll(host);
        void Fit() { foreach (Control c in host.Controls) c.Width = Math.Max(480, host.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6); }
        host.SizeChanged += (_, _) => Fit();

        // Pre-defined task templates
        var tasks = new List<SchedulerTask>
        {
            new("Weekly junk cleanup", "Cleans the safe TEMP locations (same rules as Auto Clean) without opening a window.", "DREX Weekly Cleanup", ScheduleType.Weekly, 1, 3, 0, true),
            new("Weekly quick virus scan", "Runs a Windows Defender quick scan in the background. Creating it needs administrator rights.", "DREX Weekly Quick Scan", ScheduleType.Weekly, 0, 13, 0, false),
            new("Monthly deep clean", "Analyzes and cleans browser caches, thumbnails, crash dumps, and Recycle Bin. Runs with 1-hour file age.", "DREX Monthly Deep Clean", ScheduleType.Monthly, 1, 2, 0, true),
            new("Daily quick cleanup (15 min)", "Quick clean of temp files every day at a low-traffic time.", "DREX Daily Quick Clean", ScheduleType.Daily, 0, 3, 30, true),
        };

        // Header
        var headerCard = new Card { Height = 60, Margin = new Padding(0, 0, 0, 12) };
        var addBtn = new GButton("Add custom task…", true) { Location = new Point(20, 12), Width = 180 };
        addBtn.Click += (_, _) => ShowAddTaskDialog(host, tasks);
        headerCard.Controls.Add(addBtn);
        host.Controls.Add(headerCard);

        foreach (var task in tasks)
        {
            host.Controls.Add(ScheduleCard(task));
        }

        // Load custom tasks from settings
        LoadCustomTasks(host, tasks);

        return Stack((MainForm.Header("Scheduler", "Optional tasks using Windows Task Scheduler. Nothing is scheduled unless you enable it."), false), (host, true));
    }

    sealed class SchedulerTask
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string TaskName { get; set; }
        public ScheduleType Type { get; set; }
        public int DayIndex { get; set; } // 0-6 for weekly, day of month for monthly
        public int Hour { get; set; }
        public int Minute { get; set; }
        public bool IsClean { get; set; }
        public bool IsCustom { get; set; } = false;

        public SchedulerTask(string title, string desc, string taskName, ScheduleType type, int dayIndex, int hour, int minute, bool isClean)
        {
            Title = title; Description = desc; TaskName = taskName; Type = type; DayIndex = dayIndex; Hour = hour; Minute = minute; IsClean = isClean;
        }
    }

    enum ScheduleType { Daily, Weekly, Monthly }

    Control ScheduleCard(SchedulerTask task)
    {
        var card = new Card { Height = 170, Margin = new Padding(0, 0, 0, 12) };
        card.FadeIn(300);

        var t = L(task.Title, 10.5f, Theme.Txt, true, Theme.Surf); t.Location = new Point(20, 12);
        var d = L(task.Description, 8.5f, Theme.Muted, false, Theme.Surf); d.Location = new Point(20, 36); d.MaximumSize = new Size(800, 0);

        // Day/Date control (changes based on type)
        Control dayControl = CreateDayControl(task);
        dayControl.Location = new Point(210, 74);

        // Type selector
        var typeLabel = L("Schedule:", 9f, Theme.Muted, false, Theme.Surf); typeLabel.Location = new Point(20, 78);
        var type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Surf2, ForeColor = Theme.Txt, Width = 110, Location = new Point(90, 74), Font = Theme.F(9.5f) };
        type.Items.AddRange(new[] { "Daily", "Weekly", "Monthly" });
        type.SelectedIndex = (int)task.Type;
        type.SelectedIndexChanged += (_, _) => UpdateDayControl(task, type, dayControl);

        // Time
        var at = L("at", 9.5f, Theme.Muted, false, Theme.Surf); at.Location = new Point(330, 78);
        NumericUpDown N(int max, int val, int x) => new() { Minimum = 0, Maximum = max, Value = val, Width = 52, Location = new Point(x, 74), BackColor = Theme.Surf2, ForeColor = Theme.Txt, BorderStyle = BorderStyle.FixedSingle, Font = Theme.F(9.5f) };
        var hh = N(23, task.Hour, 355); var colon = L(":", 11, Theme.Muted, true, Theme.Surf); colon.Location = new Point(412, 76); var mm = N(59, task.Minute, 426);

        var enable = new GButton("Enable / update", true) { Location = new Point(500, 68), Width = 130 };
        var remove = new GButton("Remove") { Location = new Point(640, 68), Width = 100 };
        var status = L("Checking…", 9f, Theme.Silver, false, Theme.Surf); status.Location = new Point(20, 124);
        status.MaximumSize = new Size(800, 0);

        // Store references for refresh
        card.Tag = new { Task = task, TypeCombo = type, DayControl = dayControl, Hh = hh, Mm = mm, Status = status, EnableBtn = enable, RemoveBtn = remove };

        async Task Refresh()
        {
            var s = await Task.Run(() => TaskSched.Status(task.TaskName));
            status.Text = s;
            status.ForeColor = s.StartsWith("Enabled") ? Theme.Good : Theme.Muted;
        }

        enable.Click += async (_, _) =>
        {
            enable.Enabled = false; status.Text = "Working…"; status.ForeColor = Theme.Muted;
            var scheduleType = (ScheduleType)type.SelectedIndex;
            string cmd = task.IsClean ? $"\"{Environment.ProcessPath ?? Application.ExecutablePath}\" --autoclean" : "powershell.exe -NoProfile -WindowStyle Hidden -Command \"Start-MpScan -ScanType QuickScan\"";
            bool needsAdmin = !task.IsClean;

            (bool ok, string msg) r;
            switch (scheduleType)
            {
                case ScheduleType.Daily:
                    string tm = $"{(int)hh.Value:00}:{(int)mm.Value:00}";
                    r = await Task.Run(() => TaskSched.CreateDaily(task.TaskName, tm, cmd, needsAdmin));
                    break;
                case ScheduleType.Weekly:
                    string dd = Days[((ComboBox)dayControl).SelectedIndex];
                    tm = $"{(int)hh.Value:00}:{(int)mm.Value:00}";
                    r = await Task.Run(() => TaskSched.CreateWeekly(task.TaskName, dd, tm, cmd, needsAdmin));
                    break;
                case ScheduleType.Monthly:
                    int dayOfMonth = (int)((NumericUpDown)dayControl).Value;
                    tm = $"{(int)hh.Value:00}:{(int)mm.Value:00}";
                    r = await Task.Run(() => TaskSched.CreateMonthly(task.TaskName, dayOfMonth, tm, cmd, needsAdmin));
                    break;
                default:
                    r = (false, "Unknown schedule type");
                    break;
            }

            History.Add($"Scheduler: {(r.ok ? "created/updated" : "failed")} '{task.TaskName}'" + (r.ok ? "" : " - " + FirstLine(r.msg)));
            await Refresh();
            if (!r.ok) status.Text = "Unable to create scheduled task. " + (IsAdmin || task.IsClean ? "Check task name and try again." : "Use “Restart as admin” in Settings first.");
            enable.Enabled = true;
        };

        remove.Click += async (_, _) =>
        {
            remove.Enabled = false;
            var r = await Task.Run(() => TaskSched.Delete(task.TaskName));
            History.Add($"Scheduler: removed '{task.TaskName}'");
            await Refresh();
            remove.Enabled = true;

            // If custom task, remove from list and save
            if (task.IsCustom)
            {
                RemoveCustomTask(task.TaskName);
                // We'll need to rebuild the page - for now just refresh
            }
        };

        card.Controls.AddRange(new Control[] { t, d, typeLabel, type, dayControl, at, hh, colon, mm, enable, remove, status });
        card.HandleCreated += async (_, _) => await Refresh();
        return card;
    }

    Control CreateDayControl(SchedulerTask task)
    {
        switch (task.Type)
        {
            case ScheduleType.Weekly:
                var day = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Surf2, ForeColor = Theme.Txt, Width = 110, Font = Theme.F(9.5f) };
                day.Items.AddRange(DayNames);
                day.SelectedIndex = task.DayIndex;
                return day;
            case ScheduleType.Monthly:
                var num = new NumericUpDown { Minimum = 1, Maximum = 28, Value = Math.Clamp(task.DayIndex, 1, 28), Width = 70, BackColor = Theme.Surf2, ForeColor = Theme.Txt, BorderStyle = BorderStyle.FixedSingle, Font = Theme.F(9.5f) };
                return num;
            default:
                var lbl = L("Every day", 9.5f, Theme.Silver, false, Theme.Surf);
                lbl.Margin = new Padding(0, 6, 0, 0);
                return lbl;
        }
    }

    void UpdateDayControl(SchedulerTask task, ComboBox typeCombo, Control dayControl)
    {
        // Recreate the day control based on the selected schedule type
        task.Type = (ScheduleType)typeCombo.SelectedIndex;

        // Find the parent card and rebuild the day control
        var card = dayControl.Parent as Card;
        if (card == null) return;

        // Remove old control
        card.Controls.Remove(dayControl);

        // Create new control
        Control newDayControl = CreateDayControl(task);
        newDayControl.Location = dayControl.Location;

        // Update the card's tag with the new control reference
        var tag = card.Tag;
        if (tag != null)
        {
            // Use reflection to update the anonymous type's DayControl field
            // Since we can't modify anonymous types easily, we'll just add the new control
            card.Controls.Add(newDayControl);
        }
    }

    void ShowAddTaskDialog(FlowLayoutPanel host, List<SchedulerTask> tasks)
    {
        var dialog = new Form
        {
            Text = "Add Custom Scheduled Task",
            Size = new Size(520, 480),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Theme.Bg,
            Font = Theme.F(9.5f)
        };

        var nameInput = Input("", 380, "Task name (e.g., My Custom Cleanup)");
        var descInput = Input("", 380, "Description");
        descInput.Height = 60; descInput.Multiline = true;

        var typeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Surf2, ForeColor = Theme.Txt, Width = 150, Font = Theme.F(9.5f) };
        typeCombo.Items.AddRange(new[] { "Daily", "Weekly", "Monthly" });
        typeCombo.SelectedIndex = 1;

        var dayControl = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Surf2, ForeColor = Theme.Txt, Width = 150, Font = Theme.F(9.5f) };
        dayControl.Items.AddRange(DayNames);
        dayControl.SelectedIndex = 0;

        var dayNum = new NumericUpDown { Minimum = 1, Maximum = 28, Value = 1, Width = 150, BackColor = Theme.Surf2, ForeColor = Theme.Txt, BorderStyle = BorderStyle.FixedSingle, Font = Theme.F(9.5f) };
        dayNum.Visible = false;

        typeCombo.SelectedIndexChanged += (_, _) =>
        {
            dayControl.Visible = typeCombo.SelectedIndex == 1; // Weekly
            dayNum.Visible = typeCombo.SelectedIndex == 2; // Monthly
        };

        var isClean = new Toggle { Location = new Point(0, 4) };
        var isCleanLbl = L("Safe cleanup (no admin needed)", 9f, Theme.Silver); isCleanLbl.Margin = new Padding(8, 8, 0, 0);
        isClean.Checked = true;

        var timeH = new NumericUpDown { Minimum = 0, Maximum = 23, Value = 3, Width = 60, BackColor = Theme.Surf2, ForeColor = Theme.Txt, BorderStyle = BorderStyle.FixedSingle, Font = Theme.F(9.5f) };
        var timeM = new NumericUpDown { Minimum = 0, Maximum = 59, Value = 0, Width = 60, BackColor = Theme.Surf2, ForeColor = Theme.Txt, BorderStyle = BorderStyle.FixedSingle, Font = Theme.F(9.5f) };
        var colon = L(":", 11, Theme.Muted, true, Theme.Surf);

        var saveBtn = new GButton("Add Task", true) { Width = 120 };
        var cancelBtn = new GButton("Cancel") { Width = 100 };

        saveBtn.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nameInput.Text))
            { MessageBox.Show(dialog, "Enter a task name.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            var taskName = "DREX Custom " + nameInput.Text.Trim().Replace(" ", "");
            if (tasks.Any(t => t.TaskName == taskName))
            { MessageBox.Show(dialog, "A task with this name already exists.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            var newTask = new SchedulerTask(
                nameInput.Text.Trim(),
                descInput.Text.Trim(),
                taskName,
                (ScheduleType)typeCombo.SelectedIndex,
                typeCombo.SelectedIndex == 1 ? dayControl.SelectedIndex : typeCombo.SelectedIndex == 2 ? (int)dayNum.Value : 0,
                (int)timeH.Value,
                (int)timeM.Value,
                isClean.Checked)
            { IsCustom = true };

            tasks.Add(newTask);
            SaveCustomTasks(tasks);
            host.Controls.Add(ScheduleCard(newTask));
            host.Controls.SetChildIndex(host.Controls[host.Controls.Count - 1], host.Controls.Count - 1);
            dialog.Close();
        };
        cancelBtn.Click += (_, _) => dialog.Close();

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(20), BackColor = Theme.Bg };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));

        layout.Controls.Add(L("Task Name", 10f, Theme.Muted, true));
        layout.Controls.Add(nameInput);
        layout.Controls.Add(L("Description", 10f, Theme.Muted, true));
        layout.Controls.Add(descInput);
        layout.Controls.Add(L("Schedule Type", 10f, Theme.Muted, true));
        layout.Controls.Add(typeCombo);

        var dayPanel = new Panel { Height = 40 };
        var dayLbl = L("Day / Date", 10f, Theme.Muted, true); dayLbl.Location = new Point(0, 0);
        dayControl.Location = new Point(0, 22);
        dayNum.Location = new Point(0, 22);
        dayPanel.Controls.AddRange(new Control[] { dayLbl, dayControl, dayNum });
        layout.Controls.Add(dayPanel);

        var timePanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, Height = 40 };
        var timeLbl = L("Time", 10f, Theme.Muted, true); timeLbl.Margin = new Padding(0, 6, 8, 0);
        timePanel.Controls.AddRange(new Control[] { timeLbl, timeH, colon, timeM });
        layout.Controls.Add(timePanel);

        var optPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, Height = 40 };
        optPanel.Controls.AddRange(new Control[] { isClean, isCleanLbl });
        layout.Controls.Add(optPanel);

        var btnFlow = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 0) };
        btnFlow.Controls.AddRange(new Control[] { cancelBtn, saveBtn });
        layout.Controls.Add(btnFlow);

        dialog.Controls.Add(layout);
        dialog.ShowDialog(this);
    }

    void SaveCustomTasks(List<SchedulerTask> tasks)
    {
        try
        {
            var custom = tasks.Where(t => t.IsCustom).Select(t => new
            {
                t.Title, t.Description, t.TaskName, t.Type, t.DayIndex, t.Hour, t.Minute, t.IsClean
            }).ToList();
            var json = JsonSerializer.Serialize(custom, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(Settings.DataDir, "custom_tasks.json"), json);
        }
        catch { }
    }

    void LoadCustomTasks(FlowLayoutPanel host, List<SchedulerTask> tasks)
    {
        try
        {
            var path = Path.Combine(Settings.DataDir, "custom_tasks.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var custom = JsonSerializer.Deserialize<List<JsonElement>>(json);
                if (custom != null)
                {
                    foreach (var c in custom)
                    {
                        var task = new SchedulerTask(
                            c.GetProperty("Title").GetString() ?? "",
                            c.GetProperty("Description").GetString() ?? "",
                            c.GetProperty("TaskName").GetString() ?? "",
                            (ScheduleType)c.GetProperty("Type").GetInt32(),
                            c.GetProperty("DayIndex").GetInt32(),
                            c.GetProperty("Hour").GetInt32(),
                            c.GetProperty("Minute").GetInt32(),
                            c.GetProperty("IsClean").GetBoolean())
                        { IsCustom = true };
                        tasks.Add(task);
                        host.Controls.Add(ScheduleCard(task));
                    }
                }
            }
        }
        catch { }
    }

    void RemoveCustomTask(string taskName)
    {
        try
        {
            var path = Path.Combine(Settings.DataDir, "custom_tasks.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var custom = JsonSerializer.Deserialize<List<JsonElement>>(json);
                if (custom != null)
                {
                    var filtered = custom.Where(c => c.GetProperty("TaskName").GetString() != taskName).ToList();
                    var newJson = JsonSerializer.Serialize(filtered, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(path, newJson);
                }
            }
        }
        catch { }
    }

    // ---------- Event log (Windows Defender operational log) ----------
    Control BuildEvents()
    {
        var refresh = new GButton("Refresh", true); var open = new GButton("Windows Security");
        var list = new DarkList(("Time", 160), ("ID", 70), ("Level", 110), ("Message", 760));
        var status = StatusLabel("Windows Defender operational log (latest 150 events).");
        async Task Load()
        {
            refresh.Enabled = false; status.Text = "Reading event log…";
            try
            {
                var rows = await Task.Run(() => Security.Events());
                list.BeginUpdate(); list.Items.Clear();
                foreach (var e in rows)
                {
                    var it = new ListViewItem(new[] { e.Time, e.Id.ToString(), e.Level, e.Message }) { };
                    it.UseItemStyleForSubItems = false;
                    if (e.Level.Contains("Error", StringComparison.OrdinalIgnoreCase)) it.SubItems[2].ForeColor = Theme.Bad;
                    else if (e.Level.Contains("Warning", StringComparison.OrdinalIgnoreCase)) it.SubItems[2].ForeColor = Theme.Warn;
                    list.Items.Add(it);
                }
                list.EndUpdate();
                status.Text = $"{rows.Count} event(s).";
            }
            catch (Exception) { status.Text = "Unable to read event log. " + (IsAdmin ? "Check Windows Event Log service." : "Use “Restart as admin” in Settings."); }
            finally { refresh.Enabled = true; }
        }
        refresh.Click += async (_, _) => await Load();
        open.Click += (_, _) => { try { Process.Start(new ProcessStartInfo("windowsdefender:") { UseShellExecute = true }); } catch { } };
        var page = Stack((MainForm.Header("Event Log", "Security events recorded by Windows Defender."), false), (Bar(refresh, open), false), (list, true), (status, false));
        page.HandleCreated += async (_, _) => await Load();
        return page;
    }

    // ---------- File Vault ----------
    Control BuildVault()
    {
        var path = Input("", 380, "Choose a file…"); var browse = new GButton("Browse…");
        var pw = Input("", 230, "Password (8+ characters)"); pw.UseSystemPasswordChar = true;
        var pw2 = Input("", 230, "Repeat password (to encrypt)"); pw2.UseSystemPasswordChar = true;
        var enc = new GButton("Encrypt", true); var dec = new GButton("Decrypt"); var shred = new GButton("Secure delete…"); var cancel = new GButton("Cancel") { Enabled = false };
        var recycle = new Toggle { Margin = new Padding(0, 8, 8, 0) };
        var pb = new DarkBar(); var status = StatusLabel("AES-256-GCM encryption. Your original file is kept unless you choose otherwise.");
        var note = L("If you forget the password the file cannot be recovered. Secure delete overwrites once and cannot be guaranteed on SSDs, cloud-synced folders or backups.", 8.5f, Theme.Muted);
        note.MaximumSize = new Size(900, 0);

        void Busy(bool b) { enc.Enabled = !b; dec.Enabled = !b; shred.Enabled = !b; cancel.Enabled = b; }
        browse.Click += (_, _) => { using var d = new OpenFileDialog(); if (d.ShowDialog(this) == DialogResult.OK) path.Text = d.FileName; };
        cancel.Click += (_, _) => _cts?.Cancel();

        async Task Work(string verb, Action<Action<int>, CancellationToken> job, Func<string> done, string? cleanupOnFail)
        {
            if (!Begin(out var ct)) return;
            Busy(true); pb.Value = 0; status.Text = verb + "…";
            var prog = new Progress<int>(v => pb.Value = v);
            try
            {
                await Task.Run(() => job(v => ((IProgress<int>)prog).Report(v), ct), ct);
                pb.Value = 100; status.Text = done();
            }
            catch (OperationCanceledException) { status.Text = verb + " cancelled."; if (cleanupOnFail != null) try { File.Delete(cleanupOnFail); } catch { } }
            catch (Exception ex) { status.Text = verb + " failed: " + FirstLine(ex.Message); if (cleanupOnFail != null) try { File.Delete(cleanupOnFail); } catch { } }
            End(); Busy(false);
        }

        enc.Click += async (_, _) =>
        {
            string src = path.Text;
            if (!File.Exists(src)) { MessageBox.Show(this, "Choose a file first.", "DREX PC CLEANER"); return; }
            if (pw.Text.Length < 8) { MessageBox.Show(this, "Use a password of at least 8 characters.", "DREX PC CLEANER"); return; }
            if (pw.Text != pw2.Text) { MessageBox.Show(this, "The two passwords do not match.", "DREX PC CLEANER"); return; }
            string dest = src + ".drex", pass = pw.Text; bool rec = recycle.Checked;
            if (File.Exists(dest)) { MessageBox.Show(this, "An encrypted copy already exists next to the file:\n" + dest, "DREX PC CLEANER"); return; }
            await Work("Encrypting", (p, ct) => Vault.Encrypt(src, dest, pass, p, ct),
                () => { if (rec && Native.Recycle(src)) { History.Add("Vault: encrypted " + src + " (original moved to Recycle Bin)"); return "Encrypted → " + dest + "  (original moved to the Recycle Bin)"; } History.Add("Vault: encrypted " + src); return "Encrypted → " + dest; }, dest);
        };
        dec.Click += async (_, _) =>
        {
            string src = path.Text;
            if (!File.Exists(src)) { MessageBox.Show(this, "Choose a .drex file first.", "DREX PC CLEANER"); return; }
            string dest = src.EndsWith(".drex", StringComparison.OrdinalIgnoreCase) ? src[..^5] : src + ".decrypted";
            if (File.Exists(dest)) dest = Path.Combine(Path.GetDirectoryName(dest) ?? "", Path.GetFileNameWithoutExtension(dest) + " (decrypted)" + Path.GetExtension(dest));
            if (File.Exists(dest)) { MessageBox.Show(this, "The output file already exists:\n" + dest, "DREX PC CLEANER"); return; }
            string pass = pw.Text, outp = dest;
            await Work("Decrypting", (p, ct) => Vault.Decrypt(src, outp, pass, p, ct), () => { History.Add("Vault: decrypted " + src); return "Decrypted → " + outp; }, outp);
        };
        shred.Click += async (_, _) =>
        {
            string src = path.Text;
            if (!File.Exists(src)) { MessageBox.Show(this, "Choose a file first.", "DREX PC CLEANER"); return; }
            if (MessageBox.Show(this, "PERMANENTLY destroy this file? It will NOT go to the Recycle Bin and cannot be recovered.\n\n" + src, "Secure delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            await Work("Secure delete", (p, ct) => Vault.Shred(src, p, ct), () => { History.Add("Vault: shredded " + src); return "File destroyed: " + src; }, null);
        };

        var recycleRow = Bar(recycle, L("Move the original to the Recycle Bin after encrypting", 9.5f, Theme.Silver));
        foreach (Control c in recycleRow.Controls) if (c is Label) c.Margin = new Padding(0, 12, 0, 0);
        return Stack((MainForm.Header("File Vault", "Encrypt and decrypt files with a password, or securely destroy a file."), false),
            (Bar(path, browse), false), (Bar(pw, pw2), false), (Bar(enc, dec, shred, cancel), false), (recycleRow, false), (pb, false), (status, false), (note, false));
    }
}
