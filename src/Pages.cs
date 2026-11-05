using System.Diagnostics;
using System.Collections.Concurrent;
namespace Drex;

sealed class DupTag { public string? Path; public int Group; }

public sealed partial class MainForm : Form
{
    Process? _scanProc;
    bool _scanStopped;

    static void OpenInExplorer(string path)
    {
        try { Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
    }

    // ---------- Security ----------
    Control BuildSecurity()
    {
        var run = new GButton("Run checks", true); var quick = new GButton("Quick scan"); var full = new GButton("Full scan");
        var upd = new GButton("Update definitions"); var stop = new GButton("Stop waiting") { Enabled = false }; var open = new GButton("Windows Security");
        var custom = new GButton("Custom scan…"); var cfa = new GButton("Turn on ransomware protection");
        var status = StatusLabel("Powered by the Windows Defender that is built into Windows.");

        var ring = new Ring { Size = new Size(160, 160), Location = new Point(30, 44), Big = "—", Small = "score" };
        var card = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0), Accent = true };
        var cap = L("SECURITY SCORE", 8.5f, Theme.Muted, true, Theme.Surf); cap.Location = new Point(20, 16);
        var note = L("Based on the checks listed. This is a settings check, not a malware scan.", 8.5f, Theme.Muted, false, Theme.Surf);
        note.MaximumSize = new Size(180, 0); note.Location = new Point(20, 218);
        var when = L("", 8.5f, Theme.Silver, false, Theme.Surf); when.Location = new Point(20, 276);
        card.Controls.AddRange(new Control[] { cap, ring, note, when });

        var list = new DarkList(("Check", 210), ("Status", 100), ("Details", 520));
        var mid = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, BackColor = Theme.Bg };
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 232)); mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Dock = DockStyle.Fill; list.Dock = DockStyle.Fill;
        mid.Controls.Add(card); mid.Controls.Add(list);

        void Buttons(bool enabled) { run.Enabled = enabled; quick.Enabled = enabled; full.Enabled = enabled; upd.Enabled = enabled; custom.Enabled = enabled; cfa.Enabled = enabled; }

        async Task RunChecks()
        {
            if (!Begin(out _)) return;
            Buttons(false); status.Text = "Checking Windows protections…";
            try
            {
                var rep = await Task.Run(() => Security.CheckAll());
                list.BeginUpdate(); list.Items.Clear();
                foreach (var c in rep.Checks)
                {
                    var col = c.Status == "Good" ? Theme.Good : c.Status == "Warning" ? Theme.Warn : c.Status == "Risk" ? Theme.Bad : Theme.Muted;
                    var it = new ListViewItem(new[] { c.Name, c.Status == "Good" ? "Good" : c.Status == "Risk" ? "At risk" : c.Status, c.Detail });
                    it.UseItemStyleForSubItems = false;
                    it.SubItems[1].ForeColor = col;
                    list.Items.Add(it);
                }
                list.EndUpdate();
                ring.Value = rep.Score; ring.Big = rep.Score.ToString(); ring.Small = "out of 100";
                when.Text = "Checked " + DateTime.Now.ToString("HH:mm");
                status.Text = "Checks complete."; ApplyReport(rep);
            }
            catch (Exception ex) { status.Text = "Check failed: " + ex.Message; SetTray(TrayState.Unknown, "could not verify protection"); }
            finally { End(); Buttons(true); }
        }

        async Task Defender(string title, string script)
        {
            if (!Begin(out _)) return;
            Buttons(false); stop.Enabled = true; SetTray(TrayState.Scanning, title + " running in Windows Defender");
            status.Text = title + " is running inside Windows Defender. This can take a while — you can keep using DREX.";
            (int Code, string Out, string Err) res;
            _scanStopped = false;
            try { res = await Task.Run(() => Security.RunPs(script, p => _scanProc = p)); }
            catch (Exception ex) { res = (-1, "", ex.Message); }
            bool killed = _scanStopped;
            _scanProc = null; stop.Enabled = false; End(); Buttons(true);
            string msg;
            if (killed) msg = title + ": stopped waiting. Windows Defender may continue in the background.";
            else if (res.Out.StartsWith("OK")) msg = title + " finished.";
            else
            {
                var detail = (res.Out + " " + res.Err).Trim().Split('\n')[0];
                msg = title + " could not run: " + (detail.Length == 0 ? "unknown error" : detail) + (IsAdmin ? "" : "  (Try “Restart as admin” in Settings.)");
            }
            status.Text = msg; History.Add("Security: " + msg); if (!Visible) Notify(msg + " Open Windows Security to review results.", ToolTipIcon.Info);
            await RunChecks();
        }

        run.Click += async (_, _) => await RunChecks();
        _secQuick = () => Defender("Quick scan", Security.QuickScan);
        _secFull = async () =>
        {
            if (MessageBox.Show(this, "A full scan can take a long time and use noticeable CPU while it runs. Start it?", "Full scan", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                await Defender("Full scan", Security.FullScan);
        };
        _secUpdate = () => Defender("Definition update", Security.UpdateDefs);
        _secCheck = RunChecks;
        _secCustom = p => Defender("USB / custom scan", Security.CustomScan(p));
        quick.Click += async (_, _) => await _secQuick();
        full.Click += async (_, _) => await _secFull();
        upd.Click += async (_, _) => await _secUpdate();
        custom.Click += async (_, _) =>
        {
            using var d = new FolderBrowserDialog { Description = "Choose a folder to scan with Windows Defender" };
            if (d.ShowDialog(this) == DialogResult.OK) await Defender("Custom scan", Security.CustomScan(d.SelectedPath));
        };
        cfa.Click += async (_, _) =>
        {
            if (MessageBox.Show(this, "Controlled Folder Access protects Documents, Pictures and similar folders from ransomware. Some programs may be blocked from saving there until you allow them in Windows Security.\n\nTurn it on? (Administrator rights are required.)", "Ransomware protection", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                await Defender("Ransomware protection", Security.EnableCfa);
        };
        stop.Click += (_, _) => { _scanStopped = true; try { _scanProc?.Kill(true); } catch { } };
        open.Click += (_, _) => { try { Process.Start(new ProcessStartInfo("windowsdefender:") { UseShellExecute = true }); } catch { } };

        var page = Stack((MainForm.Header("Security", "Powered by Windows Defender. DREX has no antivirus engine of its own."), false),
            (Bar(run, quick, custom, full, upd, cfa, stop, open), false), (mid, true), (status, false));
        page.HandleCreated += async (_, _) => { if (!_busy) await RunChecks(); };
        return page;
    }

    // ---------- Threats (Defender detection history) ----------
    Control BuildThreats()
    {
        var refresh = new GButton("Refresh", true); var open = new GButton("Windows Security");
        var list = new DarkList(("Detected", 150), ("Threat", 260), ("Location", 520), ("Result", 130));
        var status = StatusLabel("Reads Windows Defender's detection history. Use Windows Security to remove or restore items.");
        async Task Load()
        {
            refresh.Enabled = false; status.Text = "Reading Windows Defender history…";
            try
            {
                var rows = await Task.Run(() => Security.Threats());
                list.BeginUpdate(); list.Items.Clear();
                foreach (var t in rows)
                {
                    var it = new ListViewItem(new[] { t.Time, t.Name, t.Location, t.Handled ? "Action taken" : "Not resolved" });
                    it.UseItemStyleForSubItems = false; it.SubItems[3].ForeColor = t.Handled ? Theme.Good : Theme.Warn;
                    list.Items.Add(it);
                }
                list.EndUpdate();
                status.Text = rows.Count == 0 ? "Windows Defender reports zero threats. Your system is clean." : $"{rows.Count} detection(s) in Defender history.";
            }
            catch (Exception) { status.Text = "Unable to read Defender history. Try running as administrator."; }
            finally { refresh.Enabled = true; }
        }
        refresh.Click += async (_, _) => await Load();
        open.Click += (_, _) => { try { Process.Start(new ProcessStartInfo("windowsdefender:") { UseShellExecute = true }); } catch { } };
        var page = Stack((MainForm.Header("Threats", "Detection history from Windows Defender."), false), (Bar(refresh, open), false), (list, true), (status, false));
        page.HandleCreated += async (_, _) => await Load();
        return page;
    }

    // ---------- Startup ----------
    Control BuildStartup()
    {
        var list = new DarkList(("Status", 90), ("Name", 170), ("Publisher", 150), ("Location", 150), ("Note", 200), ("Command", 420));
        var status = StatusLabel("");
        var refresh = new GButton("Refresh"); var dis = new GButton("Disable selected", true); var en = new GButton("Enable selected"); var open = new GButton("Windows Startup Apps");

        void Load()
        {
            list.BeginUpdate(); list.Items.Clear(); int n = 0;
            foreach (var e in StartupScanner.Read())
            {
                n++;
                var it = new ListViewItem(new[] { e.Enabled ? "Enabled" : "Disabled", e.Name, e.Publisher, e.Location, e.Note, e.Command }) { Tag = e };
                if (!e.Enabled) it.ForeColor = Theme.Muted; else if (e.Note != "") it.ForeColor = Theme.Warn;
                list.Items.Add(it);
            }
            list.EndUpdate();
            status.Text = $"{n} startup entries. Amber = looks like an updater/helper or points to a missing file. Disabling is reversible.";
        }
        void Apply(bool enable)
        {
            if (list.SelectedItems.Count == 0) { MessageBox.Show(this, "Select one or more entries first.", "DREX PC CLEANER"); return; }
            int ok = 0; string err = "";
            foreach (ListViewItem it in list.SelectedItems)
            {
                var e = (StartupEntry)it.Tag!;
                var r = StartupScanner.SetEnabled(e, enable);
                if (r.ok) { ok++; History.Add($"Startup {(enable ? "enabled" : "disabled")}: {e.Name}"); } else err = r.err;
            }
            Load();
            status.Text = $"{ok} entr{(ok == 1 ? "y" : "ies")} {(enable ? "enabled" : "disabled")}." + (err.Length > 0 ? " Some failed (administrator rights may be needed): " + err : " Takes effect at next sign-in.");
        }
        refresh.Click += (_, _) => Load();
        dis.Click += (_, _) => Apply(false);
        en.Click += (_, _) => Apply(true);
        open.Click += (_, _) => StartupScanner.OpenSettings();
        Load();
        return Stack((MainForm.Header("Startup", "See what launches with Windows. Nothing is ever disabled automatically."), false),
            (Bar(refresh, dis, en, open), false), (list, true), (status, false));
    }

    // ---------- Large Files ----------
    Control BuildLarge()
    {
        var path = Input(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 300);
        var browse = new GButton("Browse…"); var mb = Num(1, 1000000, _s.LargeFileMb, 90);
        var scan = new GButton("Scan", true); var cancel = new GButton("Cancel") { Enabled = false }; var show = new GButton("Open location");
        var list = new DarkList(("Name", 260), ("Size", 110), ("Path", 600));
        var status = StatusLabel("Files are never deleted automatically.");

        browse.Click += (_, _) => Browse(path);
        scan.Click += async (_, _) =>
        {
            if (!Directory.Exists(path.Text)) { MessageBox.Show(this, "Folder not found.", "DREX PC CLEANER"); return; }
            if (!Begin(out var ct)) return;
            scan.Enabled = false; cancel.Enabled = true; list.Items.Clear(); status.Text = "Scanning…";
            _s.LargeFileMb = (int)mb.Value; long min = (long)mb.Value * 1024 * 1024;
            var found = new List<BigFile>(); var pending = new ConcurrentQueue<BigFile>();
            using var tick = new System.Windows.Forms.Timer { Interval = 500 };
            void Drain() { while (pending.TryDequeue(out var f)) found.Add(f); status.Text = $"Scanning… {found.Count} large file(s) so far"; }
            tick.Tick += (_, _) => Drain(); tick.Start();
            string end;
            try { await Task.Run(() => LargeFileFinder.Scan(path.Text, min, f => pending.Enqueue(f), ct), ct); end = "Scan complete."; }
            catch (OperationCanceledException) { end = "Scan cancelled."; }
            catch (Exception) { end = "Scan failed. Check folder permissions."; }
            tick.Stop(); while (pending.TryDequeue(out var f)) found.Add(f);
            list.BeginUpdate();
            foreach (var f in found.OrderByDescending(x => x.Size)) list.Items.Add(new ListViewItem(new[] { f.Name, Fmt.Size(f.Size), f.Path }));
            list.EndUpdate();
            status.Text = $"{end} {found.Count} file(s) found.";
            End(); scan.Enabled = true; cancel.Enabled = false;
        };
        cancel.Click += (_, _) => _cts?.Cancel();
        show.Click += (_, _) => { if (list.SelectedItems.Count > 0) OpenInExplorer(list.SelectedItems[0].SubItems[2].Text); };
        return Stack((MainForm.Header("Large Files", "Find what is using your space. DREX only shows files — you decide what to do."), false),
            (Bar(path, browse, Cap("Min size (MB)"), mb, scan, cancel, show), false), (list, true), (status, false));
    }

    // ---------- Duplicates ----------
    Control BuildDupes()
    {
        var down = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var path = Input(Directory.Exists(down) ? down : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 300);
        var browse = new GButton("Browse…"); var mb = Num(1, 100000, 1, 80);
        var scan = new GButton("Find duplicates", true); var cancel = new GButton("Cancel") { Enabled = false };
        var pick = new GButton("Select extra copies"); var del = new GButton("Move to Recycle Bin") { Enabled = false }; var show = new GButton("Open location");
        var list = new DarkList(("File", 300), ("Size", 100), ("Folder", 460), ("Modified", 150));
        var status = StatusLabel("Compares file contents (SHA-256). Deleted files go to the Recycle Bin so you can restore them.");

        browse.Click += (_, _) => Browse(path);
        scan.Click += async (_, _) =>
        {
            if (!Directory.Exists(path.Text)) { MessageBox.Show(this, "Folder not found.", "DREX PC CLEANER"); return; }
            if (!Begin(out var ct)) return;
            scan.Enabled = false; del.Enabled = false; cancel.Enabled = true; list.Items.Clear();
            var prog = new Progress<string>(s => status.Text = s);
            long min = (long)mb.Value * 1024 * 1024; string end;
            try
            {
                var groups = await Task.Run(() => DuplicateFinder.Find(path.Text, min, s => ((IProgress<string>)prog).Report(s), ct), ct);
                list.BeginUpdate(); int gi = 0; long waste = 0;
                foreach (var g in groups)
                {
                    gi++; long w = g.Size * (g.Files.Count - 1); waste += w;
                    var head = new ListViewItem(new[] { $"{g.Files.Count} copies · {Fmt.Size(g.Size)} each · {Fmt.Size(w)} reclaimable", "", "", "" }) { Tag = new DupTag { Path = null, Group = gi }, ForeColor = Theme.AccentHi };
                    list.Items.Add(head);
                    foreach (var f in g.Files)
                    {
                        string mod = ""; try { mod = File.GetLastWriteTime(f).ToString("yyyy-MM-dd HH:mm"); } catch { }
                        list.Items.Add(new ListViewItem(new[] { Path.GetFileName(f), Fmt.Size(g.Size), Path.GetDirectoryName(f) ?? "", mod }) { Tag = new DupTag { Path = f, Group = gi } });
                    }
                }
                list.EndUpdate();
                end = groups.Count == 0 ? "Nothing to clean — your folder is already tidy." : $"{groups.Count} duplicate group(s) — up to {Fmt.Size(waste)} reclaimable. Use “Select extra copies”, review, then move to Recycle Bin.";
            }
            catch (OperationCanceledException) { end = "Scan cancelled."; }
            catch (Exception) { end = "Duplicate scan failed. Check folder access."; }
            status.Text = end; End(); scan.Enabled = true; cancel.Enabled = false; del.Enabled = list.Items.Count > 0;
        };
        cancel.Click += (_, _) => _cts?.Cancel();
        pick.Click += (_, _) =>
        {
            var seen = new HashSet<int>();
            foreach (ListViewItem it in list.Items)
            {
                var t = (DupTag)it.Tag!;
                it.Selected = t.Path != null && !seen.Add(t.Group);
            }
        };
        show.Click += (_, _) => { if (list.SelectedItems.Count > 0 && list.SelectedItems[0].Tag is DupTag t && t.Path != null) OpenInExplorer(t.Path); };
        del.Click += async (_, _) =>
        {
            var chosen = list.SelectedItems.Cast<ListViewItem>().Where(i => i.Tag is DupTag t && t.Path != null).ToList();
            if (chosen.Count == 0) { MessageBox.Show(this, "Select the copies you want to remove first.", "DREX PC CLEANER"); return; }
            var allByGroup = list.Items.Cast<ListViewItem>().Where(i => i.Tag is DupTag t && t.Path != null).GroupBy(i => ((DupTag)i.Tag!).Group);
            foreach (var g in allByGroup)
                if (g.All(i => chosen.Contains(i))) { MessageBox.Show(this, "You selected every copy in at least one group. Keep at least one copy of each file.", "DREX PC CLEANER"); return; }
            if (MessageBox.Show(this, $"Move {chosen.Count} file(s) to the Recycle Bin?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (!Begin(out _)) return;
            del.Enabled = false; scan.Enabled = false;
            var paths = chosen.Select(i => ((DupTag)i.Tag!).Path!).ToList();
            var okSet = await Task.Run(() => paths.Where(p => Native.Recycle(p)).ToHashSet());
            foreach (var i in chosen) { if (okSet.Contains(((DupTag)i.Tag!).Path!)) list.Items.Remove(i); }
            History.Add($"Duplicates: moved {okSet.Count} file(s) to Recycle Bin");
            status.Text = $"Moved {okSet.Count} of {chosen.Count} file(s) to the Recycle Bin." + (okSet.Count < chosen.Count ? " Some could not be moved (in use or no permission)." : "");
            End(); scan.Enabled = true; del.Enabled = list.Items.Count > 0;
        };
        return Stack((MainForm.Header("Duplicate Finder", "Finds identical files by content, so only true copies are listed."), false),
            (Bar(path, browse, Cap("Min size (MB)"), mb, scan, cancel), false), (Bar(pick, del, show), false), (list, true), (status, false));
    }

    // ---------- Disk Usage ----------
    Control BuildDisk()
    {
        var path = Input(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 340);
        var browse = new GButton("Browse…"); var go = new GButton("Analyze", true); var cancel = new GButton("Cancel") { Enabled = false }; var open = new GButton("Open folder");
        var list = new DarkList(("Folder", 280), ("Size", 110), ("Share of total", 300), ("Path", 520)) { BarColumn = 2 };
        var status = StatusLabel("Double-click a folder to drill into it.");

        async Task Analyze()
        {
            if (!Directory.Exists(path.Text)) { MessageBox.Show(this, "Folder not found.", "DREX PC CLEANER"); return; }
            if (!Begin(out var ct)) return;
            go.Enabled = false; cancel.Enabled = true; list.Items.Clear();
            var rows = new List<(string name, string path, long size)>(); string end; string root = path.Text;
            try
            {
                var dirs = await Task.Run(() => DiskUsage.SubDirs(root), ct);
                int i = 0;
                foreach (var d in dirs)
                {
                    status.Text = $"Measuring {++i}/{dirs.Count}: {Path.GetFileName(d)}";
                    long sz = await Task.Run(() => DiskUsage.SizeOf(d, ct), ct);
                    rows.Add((Path.GetFileName(d), d, sz));
                }
                long loose = await Task.Run(() => DiskUsage.LooseFiles(root), ct);
                if (loose > 0) rows.Add(("(files in this folder)", root, loose));
                end = "Done.";
            }
            catch (OperationCanceledException) { end = "Cancelled (showing what was measured)."; }
            catch (Exception) { end = "Analysis failed. Check folder permissions."; }
            long total = rows.Sum(r => r.size);
            list.BeginUpdate();
            foreach (var r in rows.OrderByDescending(x => x.size))
                list.Items.Add(new ListViewItem(new[] { r.name, Fmt.Size(r.size), total > 0 ? (r.size * 100.0 / total).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "0", r.path }));
            list.EndUpdate();
            status.Text = $"{end} {Fmt.Size(total)} in {root}. Double-click a folder to drill into it.";
            End(); go.Enabled = true; cancel.Enabled = false;
        }

        browse.Click += (_, _) => Browse(path);
        go.Click += async (_, _) => await Analyze();
        cancel.Click += (_, _) => _cts?.Cancel();
        open.Click += (_, _) => { if (list.SelectedItems.Count > 0) OpenInExplorer(list.SelectedItems[0].SubItems[3].Text); };
        list.DoubleClick += async (_, _) =>
        {
            if (list.SelectedItems.Count == 0 || _busy) return;
            var p = list.SelectedItems[0].SubItems[3].Text;
            if (Directory.Exists(p) && !p.Equals(path.Text, StringComparison.OrdinalIgnoreCase)) { path.Text = p; await Analyze(); }
        };
        return Stack((MainForm.Header("Disk Usage", "See which folders are taking the space."), false), (Bar(path, browse, go, cancel, open), false), (list, true), (status, false));
    }

    // ---------- Web Blocker ----------
    Control BuildWeb()
    {
        var input = Input("", 300, "example.com");
        var add = new GButton("Block site", true); var rem = new GButton("Unblock selected"); var adm = new GButton("Restart as admin") { Visible = !IsAdmin };
        var list = new DarkList(("Blocked site", 500));
        var status = StatusLabel("");
        var domains = new List<string>();

        void Load()
        {
            domains = HostsBlocker.Read();
            list.BeginUpdate(); list.Items.Clear();
            foreach (var d in domains) list.Items.Add(new ListViewItem(d));
            list.EndUpdate();
            status.Text = $"{domains.Count} site(s) blocked. Changes need administrator rights.";
        }
        void Save(List<string> next, string okMsg)
        {
            var r = HostsBlocker.Write(next);
            if (r.ok) { History.Add("Web blocker: " + okMsg); Load(); status.Text = okMsg; }
            else status.Text = "Unable to update hosts file. Run as administrator to modify system files.";
        }
        add.Click += (_, _) =>
        {
            var d = HostsBlocker.Sanitize(input.Text);
            if (d == null) { status.Text = "Enter a valid site such as example.com"; return; }
            if (domains.Contains(d)) { status.Text = d + " is already blocked."; return; }
            Save(domains.Concat(new[] { d }).ToList(), "Blocked " + d);
            input.Clear();
        };
        rem.Click += (_, _) =>
        {
            if (list.SelectedItems.Count == 0) return;
            var gone = list.SelectedItems.Cast<ListViewItem>().Select(i => i.Text).ToHashSet();
            Save(domains.Where(d => !gone.Contains(d)).ToList(), $"Unblocked {gone.Count} site(s)");
        };
        var social = new GButton("Block social media"); var ads = new GButton("Block ad trackers");
        void Preset(string name, string[] ds)
        {
            if (MessageBox.Show(this, $"Block {ds.Length} well-known {name} sites system-wide?\n\nOnly the exact domains (and www.) are blocked. You can unblock any of them later.", "Web Blocker", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Save(domains.Union(ds).ToList(), $"Blocked {name} preset");
        }
        social.Click += (_, _) => Preset("social media", new[] { "facebook.com", "instagram.com", "twitter.com", "x.com", "tiktok.com", "snapchat.com" });
        ads.Click += (_, _) => Preset("ad/tracker", new[] { "doubleclick.net", "googleadservices.com", "googlesyndication.com", "adservice.google.com", "adnxs.com", "taboola.com", "outbrain.com", "criteo.com", "scorecardresearch.com", "quantserve.com", "hotjar.com" });
        adm.Click += (_, _) => RestartAsAdmin();
        Load();
        return Stack((MainForm.Header("Web Blocker", "Blocks sites system-wide using the Windows hosts file (parental-control style)."), false),
            (Bar(input, add, rem, adm), false), (Bar(Cap("Presets"), social, ads), false), (list, true), (status, false));
    }

    // ---------- Backup ----------
    Control BuildBackup()
    {
        var src = Input(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents"), 320);
        var bsrc = new GButton("Browse…");
        var dst = Input(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"DREX_Backup_{DateTime.Now:yyyyMMdd_HHmm}.zip"), 320);
        var bdst = new GButton("Save as…");
        var start = new GButton("Start backup", true); var cancel = new GButton("Cancel") { Enabled = false };
        var big = L("Ready", 22, Theme.Accent, true); var detail = StatusLabel("Copies a folder into a single compressed ZIP. Files in use are skipped and counted.");
        var holder = new Panel { BackColor = Theme.Bg, Height = 56 }; big.Location = new Point(0, 8); holder.Controls.Add(big);

        bsrc.Click += (_, _) => Browse(src);
        bdst.Click += (_, _) => { using var d = new SaveFileDialog { Filter = "ZIP archive|*.zip", FileName = Path.GetFileName(dst.Text) }; if (d.ShowDialog(this) == DialogResult.OK) dst.Text = d.FileName; };
        start.Click += async (_, _) =>
        {
            if (!Directory.Exists(src.Text)) { MessageBox.Show(this, "Source folder not found.", "DREX PC CLEANER"); return; }
            if (string.IsNullOrWhiteSpace(dst.Text)) return;
            if (!Begin(out var ct)) return;
            start.Enabled = false; cancel.Enabled = true; big.Text = "Backing up…";
            var prog = new Progress<(int, long)>(p => detail.Text = $"{p.Item1:N0} files · {Fmt.Size(p.Item2)} processed");
            string target = dst.Text; string s = src.Text;
            try
            {
                var r = await Task.Run(() => BackupEngine.Run(s, target, (f, b) => ((IProgress<(int, long)>)prog).Report((f, b)), ct), ct);
                big.Text = "Backup complete";
                detail.Text = $"{r.files:N0} files · {Fmt.Size(r.bytes)} read · ZIP is {Fmt.Size(new FileInfo(target).Length)} · {r.skipped} skipped (in use)";
                History.Add($"Backup: {r.files} files to {target}");
            }
            catch (OperationCanceledException) { big.Text = "Cancelled"; detail.Text = "Backup cancelled; partial file removed."; try { File.Delete(target); } catch { } }
            catch (Exception ex) { big.Text = "Backup failed"; detail.Text = ex.Message; try { File.Delete(target); } catch { } }
            End(); start.Enabled = true; cancel.Enabled = false;
        };
        cancel.Click += (_, _) => _cts?.Cancel();
        var rzip = Input("", 320, "Backup ZIP to restore"); var brz = new GButton("Choose ZIP…");
        var rdst = Input(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Restored"), 320); var brd = new GButton("Browse…");
        var restore = new GButton("Restore (never overwrites)");
        brz.Click += (_, _) => { using var d = new OpenFileDialog { Filter = "ZIP archive|*.zip" }; if (d.ShowDialog(this) == DialogResult.OK) rzip.Text = d.FileName; };
        brd.Click += (_, _) => Browse(rdst);
        restore.Click += async (_, _) =>
        {
            if (!File.Exists(rzip.Text)) { MessageBox.Show(this, "Choose a backup ZIP first.", "DREX PC CLEANER"); return; }
            if (!Begin(out var ct)) return;
            restore.Enabled = false; start.Enabled = false; cancel.Enabled = true; big.Text = "Restoring…";
            var prog = new Progress<int>(n => detail.Text = $"{n:N0} files restored");
            string z = rzip.Text, d2 = rdst.Text;
            try
            {
                var r = await Task.Run(() => BackupEngine.Restore(z, d2, n => ((IProgress<int>)prog).Report(n), ct), ct);
                big.Text = "Restore complete";
                detail.Text = $"{r.restored:N0} files restored to {d2} · {r.skipped} skipped (already exist or in use) · {r.blocked} blocked (unsafe path)";
                History.Add($"Restore: {r.restored} files to {d2}");
            }
            catch (OperationCanceledException) { big.Text = "Cancelled"; detail.Text = "Restore cancelled; files already restored were kept."; }
            catch (Exception ex) { big.Text = "Restore failed"; detail.Text = ex.Message; }
            End(); restore.Enabled = true; start.Enabled = true; cancel.Enabled = false;
        };
        return Stack((MainForm.Header("Backup", "Compress an important folder into a ZIP, and restore it later."), false),
            (Bar(Cap("Folder"), src, bsrc), false), (Bar(Cap("Save to"), dst, bdst), false), (Bar(start, cancel), false),
            (Bar(Cap("Restore"), rzip, brz), false), (Bar(Cap("Into"), rdst, brd, restore), false), (holder, false), (detail, false));
    }
}
