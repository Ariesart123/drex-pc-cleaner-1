namespace Drex;

public sealed partial class MainForm : Form
{
    HashSetCompact _hashes = HashSetCompact.Create(Array.Empty<(ulong hi, ulong lo)>());

    // ---------- Scanner + Quarantine ----------
    Control BuildScanner()
    {
        _hashes = HashScanner.Load();
        var down = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var path = Input(Directory.Exists(down) ? down : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 340);
        var browse = new GButton("Browse…"); var scan = new GButton("Scan", true); var cancel = new GButton("Cancel") { Enabled = false };
        var import = new GButton("Import hash list…"); var test = new GButton("Self-test");
        var tabScan = new GButton("Scan results", true); var tabQ = new GButton("Quarantine");
        var quar = new GButton("Quarantine selected"); var restore = new GButton("Restore selected"); var del = new GButton("Delete permanently");
        var dList = new DarkList(("Detection", 280), ("File", 520), ("SHA-256", 330));
        var qList = new DarkList(("File", 280), ("Original location", 500), ("Quarantined", 150)) { Visible = false };
        dList.Dock = DockStyle.Fill; qList.Dock = DockStyle.Fill;
        var holder = new Panel { BackColor = Theme.Bg }; holder.Controls.Add(dList); holder.Controls.Add(qList);
        var status = StatusLabel("");

        void Hint() => status.Text = $"Hash list: {_hashes.Count:N0} entries. DREX detects only the EICAR test file and files on YOUR hash list — Windows Defender is the real protection.";
        void LoadQ()
        {
            qList.BeginUpdate(); qList.Items.Clear();
            foreach (var q in Quarantine.List().OrderByDescending(x => x.When))
                qList.Items.Add(new ListViewItem(new[] { q.Name, q.OriginalPath, q.When.ToString("yyyy-MM-dd HH:mm") }) { Tag = q });
            qList.EndUpdate();
        }
        void Tab(bool q) { dList.Visible = !q; qList.Visible = q; if (q) LoadQ(); }
        tabScan.Click += (_, _) => Tab(false);
        tabQ.Click += (_, _) => Tab(true);
        browse.Click += (_, _) => Browse(path);

        import.Click += async (_, _) =>
        {
            using var d = new OpenFileDialog { Filter = "Text/CSV hash lists|*.txt;*.csv;*.list|All files|*.*" };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                string f = d.FileName; int added = await Task.Run(() => HashScanner.Import(f));
                _hashes = await Task.Run(() => HashScanner.Load());
                History.Add($"Scanner: imported {added} hashes");
                status.Text = $"Imported {added:N0} new hash(es). Total {_hashes.Count:N0} (limit 1,000,000).";
            }
            catch (Exception) { status.Text = "Import failed. Ensure the file is a valid text/CSV hash list."; }
        };
        test.Click += (_, _) => status.Text = HashScanner.SelfTest();

        scan.Click += async (_, _) =>
        {
            if (!Directory.Exists(path.Text)) { MessageBox.Show(this, "Folder not found.", "DREX PC CLEANER"); return; }
            if (!Begin(out var ct)) return;
            scan.Enabled = false; cancel.Enabled = true; Tab(false); dList.Items.Clear(); status.Text = "Scanning…";
            var prog = new Progress<string>(s => status.Text = s); string root = path.Text; var hs = _hashes;
            try
            {
                var rep = await Task.Run(() => HashScanner.Scan(root, hs, s => ((IProgress<string>)prog).Report(s), ct), ct);
                dList.BeginUpdate();
                foreach (var d in rep.Found) { var it = new ListViewItem(new[] { d.Name, d.Path, d.Sha256 }) { Tag = d, ForeColor = Theme.Bad }; dList.Items.Add(it); }
                dList.EndUpdate();
                status.Text = $"Scanned {rep.Scanned:N0} file(s): {rep.Found.Count} detection(s), {rep.SkippedLarge} over 256 MB skipped, {rep.Errors} unreadable. " + (hs.Count == 0 ? "Import a hash list to enable malware detection. EICAR test file is always detected." : "");
                History.Add($"Scanner: {rep.Scanned} files, {rep.Found.Count} detections in {root}");
            }
            catch (OperationCanceledException) { status.Text = "Scan cancelled."; }
            catch (Exception) { status.Text = "Scan failed. Check folder permissions and try again."; }
            End(); scan.Enabled = true; cancel.Enabled = false;
        };
        cancel.Click += (_, _) => _cts?.Cancel();

        quar.Click += (_, _) =>
        {
            var sel = dList.SelectedItems.Cast<ListViewItem>().Where(i => i.Tag is Detection).ToList();
            if (sel.Count == 0) { MessageBox.Show(this, "Select detected files first.", "DREX PC CLEANER"); return; }
            if (MessageBox.Show(this, $"Move {sel.Count} file(s) into DREX quarantine? You can restore them later.", "Quarantine", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int ok = 0; string err = "";
            foreach (var it in sel) { var d = (Detection)it.Tag!; var r = Quarantine.Add(d); if (r.ok) { ok++; dList.Items.Remove(it); History.Add("Quarantine: " + d.Path); } else err = r.msg; }
            status.Text = $"{ok} file(s) quarantined." + (err.Length > 0 ? " Some failed: " + FirstLine(err) : "");
        };
        restore.Click += (_, _) =>
        {
            var sel = qList.SelectedItems.Cast<ListViewItem>().Where(i => i.Tag is QuarItem).ToList();
            if (sel.Count == 0) { Tab(true); status.Text = "Open the Quarantine tab and select items to restore."; return; }
            if (MessageBox.Show(this, "Restore the selected file(s) to their original location? Only do this if you are sure they are safe.", "Restore", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            int ok = 0; string err = "";
            foreach (var it in sel) { var r = Quarantine.Restore((QuarItem)it.Tag!); if (r.ok) { ok++; History.Add("Quarantine restore: " + ((QuarItem)it.Tag!).OriginalPath); } else err = r.msg; }
            LoadQ(); status.Text = $"{ok} file(s) restored." + (err.Length > 0 ? " Some failed: " + FirstLine(err) : "");
        };
        del.Click += (_, _) =>
        {
            var sel = qList.SelectedItems.Cast<ListViewItem>().Where(i => i.Tag is QuarItem).ToList();
            if (sel.Count == 0) { Tab(true); status.Text = "Open the Quarantine tab and select items to delete."; return; }
            if (MessageBox.Show(this, $"Permanently delete {sel.Count} quarantined file(s)? This cannot be undone.", "Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            int ok = 0; foreach (var it in sel) if (Quarantine.Delete((QuarItem)it.Tag!).ok) { ok++; History.Add("Quarantine delete: " + ((QuarItem)it.Tag!).Name); }
            LoadQ(); status.Text = $"{ok} file(s) deleted.";
        };

        Hint();
        return Stack((MainForm.Header("Scanner", "DREX's own light scanner and quarantine. Complements — never replaces — Windows Defender."), false),
            (Bar(path, browse, scan, cancel, import, test), false), (Bar(tabScan, tabQ, quar, restore, del), false), (holder, true), (status, false));
    }
}
