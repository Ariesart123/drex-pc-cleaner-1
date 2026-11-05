using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Http;
using System.Windows.Forms;
namespace Drex;

public static class ProfilePage
{
    public static Control Build(UserProfile profile, MainForm mainForm)
    {
        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Theme.Bg
        };
        Native.DarkScroll(flow);
        flow.SizeChanged += (_, _) =>
        {
            foreach (Control c in flow.Controls)
                c.Width = Math.Max(500, flow.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6);
        };

        // Profile Header Card
        var headerCard = new Card { Height = 180, Margin = new Padding(0, 0, 0, 12) };
        var avatar = new PictureBox
        {
            Size = new Size(80, 80),
            Location = new Point(24, 24),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent
        };
        if (!string.IsNullOrEmpty(profile.PictureUrl))
        {
            try
            {
                using var client = new HttpClient();
                var bytes = client.GetByteArrayAsync(profile.PictureUrl).GetAwaiter().GetResult();
                using var ms = new MemoryStream(bytes);
                avatar.Image = Image.FromStream(ms);
            }
            catch { avatar.Image = AppIcon.Get(80).ToBitmap(); }
        }
        else
        {
            avatar.Image = AppIcon.Get(80).ToBitmap();
        }
        avatar.Paint += (s, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = new GraphicsPath();
            path.AddEllipse(0, 0, 80, 80);
            g.SetClip(path);
            if (avatar.Image != null)
                g.DrawImage(avatar.Image, 0, 0, 80, 80);
        };

        var name = L(profile.DisplayName, 18f, Theme.Txt, true, Theme.Surf);
        name.Location = new Point(120, 24);
        name.AutoSize = true;

        var email = L(profile.Email, 10f, Theme.Silver, false, Theme.Surf);
        email.Location = new Point(120, 56);
        email.AutoSize = true;

        var providerBadge = new Panel
        {
            Size = new Size(100, 28),
            Location = new Point(120, 88),
            BackColor = Theme.AccentMuted
        };
        providerBadge.Paint += (s, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Theme.Round(new Rectangle(0, 0, 100, 28), 14);
            using var br = new SolidBrush(Theme.AccentMuted);
            g.FillPath(br, path);
            var providerText = profile.Provider == "google" ? "Google" : "Email";
            TextRenderer.DrawText(g, providerText, Theme.F(9f, true), new Rectangle(0, 0, 100, 28), Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };

        var created = L($"Member since {profile.CreatedAt:MMMM yyyy}", 9f, Theme.Muted, false, Theme.Surf);
        created.Location = new Point(120, 120);
        created.AutoSize = true;

        var lastLogin = L($"Last login: {profile.LastLoginAt:yyyy-MM-dd HH:mm}", 9f, Theme.Muted, false, Theme.Surf);
        lastLogin.Location = new Point(120, 144);
        lastLogin.AutoSize = true;

        var editBtn = new GButton("Edit Profile") { Location = new Point(120, 156), Width = 140 };
        editBtn.Click += (s, e) => ShowEditProfileDialog(profile, mainForm, name, email);

        var signOutBtn = new GButton("Sign Out") { Location = new Point(270, 156), Width = 120 };
        signOutBtn.Primary = false;
        signOutBtn.Click += async (s, e) =>
        {
            if (MessageBox.Show(mainForm, "Sign out of your account? You'll need to sign in again next time.", "Sign Out", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                signOutBtn.Enabled = false;
                signOutBtn.Text = "Signing out...";
                var result = await AuthService.SignOutAsync();
                if (result.Success)
                {
                    mainForm.HandleLogout();
                }
                else
                {
                    signOutBtn.Enabled = true;
                    signOutBtn.Text = "Sign Out";
                    MessageBox.Show(mainForm, $"Sign out failed: {result.ErrorMessage}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        };

        var deleteBtn = new GButton("Delete Account") { Location = new Point(400, 156), Width = 140 };
        deleteBtn.Primary = false;
        deleteBtn.Click += async (s, e) =>
        {
            if (MessageBox.Show(mainForm, "Permanently delete your account and all data? This cannot be undone.", "Delete Account", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            {
                if (MessageBox.Show(mainForm, "This will delete all your settings, history, and encrypted files. Are you absolutely sure?", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                {
                    try
                    {
                        var dir = Settings.DataDir;
                        if (Directory.Exists(dir))
                            Directory.Delete(dir, true);
                    }
                    catch { }
                    UserProfileStore.SignOut();
                    Application.Restart();
                }
            }
        };

        headerCard.Controls.AddRange(new Control[] { avatar, name, email, providerBadge, created, lastLogin, editBtn, signOutBtn, deleteBtn });

        // Security Card
        var securityCard = new Card { Height = 120, Margin = new Padding(0, 0, 0, 12) };
        var secTitle = L("Account Security", 12f, Theme.Txt, true, Theme.Surf);
        secTitle.Location = new Point(20, 16);
        var changePwdBtn = new GButton("Change Password") { Location = new Point(20, 56), Width = 160 };
        changePwdBtn.Click += (s, e) => ShowChangePasswordDialog(profile, mainForm);
        var sessionsBtn = new GButton("Manage Sessions") { Location = new Point(190, 56), Width = 160 };
        sessionsBtn.Primary = false;
        sessionsBtn.Click += (s, e) => ShowSessionsDialog(profile, mainForm);
        securityCard.Controls.AddRange(new Control[] { secTitle, changePwdBtn, sessionsBtn });

        // Activity Card
        var activityCard = new Card { Margin = new Padding(0, 0, 0, 12) };
        var actTitle = L("Recent Activity", 12f, Theme.Txt, true, Theme.Surf);
        actTitle.Location = new Point(20, 16);
        var actList = new DarkList(("Time", 160), ("Action", 200), ("Details", 400)) { Dock = DockStyle.Fill };
        actList.Height = 300;
        foreach (var entry in profile.LoginHistory.Take(20))
        {
            var item = new ListViewItem(new[]
            {
                entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                entry.Success ? "Sign in" : "Failed sign in",
                entry.FailureReason.Length > 0 ? entry.FailureReason : entry.UserAgent
            });
            item.UseItemStyleForSubItems = false;
            if (!entry.Success) item.SubItems[1].ForeColor = Theme.Bad;
            actList.Items.Add(item);
        }
        var clearBtn = new GButton("Clear History") { Width = 120 };
        clearBtn.Primary = false;
        clearBtn.Click += (s, e) =>
        {
            if (MessageBox.Show(mainForm, "Clear all login history?", "Clear History", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                UserProfileStore.CurrentProfile?.LoginHistory.Clear();
                UserProfileStore.Save(UserProfileStore.CurrentProfile!);
                actList.Items.Clear();
            }
        };
        var actLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Surf };
        actLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        actLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        actLayout.Controls.Add(actList);
        var btnPanel = new Panel { Height = 50, BackColor = Theme.Surf };
        clearBtn.Location = new Point(20, 10);
        btnPanel.Controls.Add(clearBtn);
        activityCard.Controls.Add(actTitle);
        activityCard.Controls.Add(actLayout);
        activityCard.Controls.Add(btnPanel);

        // Layout
        flow.Controls.Add(headerCard);
        flow.Controls.Add(securityCard);
        flow.Controls.Add(activityCard);

        return Stack((MainForm.Header("Profile", "Manage your account, security, and activity"), false), (flow, true));
    }

    static void ShowEditProfileDialog(UserProfile profile, MainForm mainForm, Label nameLabel, Label emailLabel)
    {
        var dialog = new Form
        {
            Text = "Edit Profile",
            Size = new Size(440, 300),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Theme.Bg,
            Font = Theme.F(9.5f)
        };

        var nameInput = Input(profile.DisplayName, 340);
        var emailInput = Input(profile.Email, 340);
        emailInput.ReadOnly = true;
        emailInput.BackColor = Theme.Surf;

        var saveBtn = new GButton("Save", true) { Width = 120 };
        var cancelBtn = new GButton("Cancel") { Width = 120 };

        saveBtn.Click += (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(nameInput.Text))
            {
                MessageBox.Show(dialog, "Display name cannot be empty", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            profile.DisplayName = nameInput.Text.Trim();
            UserProfileStore.Save(profile);
            nameLabel.Text = profile.DisplayName;
            dialog.Close();
        };
        cancelBtn.Click += (s, e) => dialog.Close();

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(20), BackColor = Theme.Bg };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        layout.Controls.Add(L("Display Name", 10f, Theme.Muted, true));
        layout.Controls.Add(nameInput);
        layout.Controls.Add(L("Email (cannot be changed)", 10f, Theme.Muted, true));
        layout.Controls.Add(emailInput);
        var btnFlow = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 0) };
        btnFlow.Controls.AddRange(new Control[] { cancelBtn, saveBtn });
        layout.Controls.Add(btnFlow);

        dialog.Controls.Add(layout);
        dialog.ShowDialog(mainForm);
    }

    static void ShowChangePasswordDialog(UserProfile profile, MainForm mainForm)
    {
        if (profile.Provider != "manual")
        {
            MessageBox.Show(mainForm, "Password change is only available for email/password accounts.", "Not Available", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var dialog = new Form
        {
            Text = "Change Password",
            Size = new Size(440, 340),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Theme.Bg,
            Font = Theme.F(9.5f)
        };

        var currentInput = Input("", 340);
        currentInput.UseSystemPasswordChar = true;
        currentInput.PlaceholderText = "Current password";
        var newInput = Input("", 340);
        newInput.UseSystemPasswordChar = true;
        newInput.PlaceholderText = "New password (8+ chars)";
        var confirmInput = Input("", 340);
        confirmInput.UseSystemPasswordChar = true;
        confirmInput.PlaceholderText = "Confirm new password";

        var saveBtn = new GButton("Change Password", true) { Width = 160 };
        var cancelBtn = new GButton("Cancel") { Width = 120 };

        saveBtn.Click += async (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(currentInput.Text))
            { MessageBox.Show(dialog, "Enter your current password", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            if (string.IsNullOrWhiteSpace(newInput.Text))
            { MessageBox.Show(dialog, "Enter a new password", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            if (newInput.Text.Length < 8)
            { MessageBox.Show(dialog, "Password must be at least 8 characters", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            if (newInput.Text != confirmInput.Text)
            { MessageBox.Show(dialog, "Passwords do not match", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            var success = await AuthService.ChangePasswordAsync(currentInput.Text, newInput.Text, confirmInput.Text);
            if (success)
            {
                MessageBox.Show(dialog, "Password changed successfully", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                dialog.Close();
            }
            else
            {
                MessageBox.Show(dialog, "Current password is incorrect", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        cancelBtn.Click += (s, e) => dialog.Close();

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(20), BackColor = Theme.Bg };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        layout.Controls.Add(L("Current Password", 10f, Theme.Muted, true));
        layout.Controls.Add(currentInput);
        layout.Controls.Add(L("New Password", 10f, Theme.Muted, true));
        layout.Controls.Add(newInput);
        layout.Controls.Add(L("Confirm New Password", 10f, Theme.Muted, true));
        layout.Controls.Add(confirmInput);
        var btnFlow = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 0) };
        btnFlow.Controls.AddRange(new Control[] { cancelBtn, saveBtn });
        layout.Controls.Add(btnFlow);

        dialog.Controls.Add(layout);
        dialog.ShowDialog(mainForm);
    }

    static void ShowSessionsDialog(UserProfile profile, MainForm mainForm)
    {
        var dialog = new Form
        {
            Text = "Active Sessions",
            Size = new Size(560, 420),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Theme.Bg,
            Font = Theme.F(9.5f)
        };

        var list = new DarkList(("Device", 200), ("Last Activity", 160), ("IP Address", 120)) { Dock = DockStyle.Fill };
        foreach (var session in profile.ActiveSessions)
        {
            var item = new ListViewItem(new[]
            {
                session.DeviceInfo,
                session.LastActivityAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                session.IpAddress
            }) { Tag = session };
            if (session.IsCurrent) item.SubItems[0].Text += " (current)";
            list.Items.Add(item);
        }

        var revokeBtn = new GButton("Revoke Selected") { Width = 140 };
        revokeBtn.Primary = false;
        revokeBtn.Click += (s, e) =>
        {
            if (list.SelectedItems.Count == 0)
            {
                MessageBox.Show(dialog, "Select a session to revoke", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            var session = (ActiveSession)list.SelectedItems[0].Tag!;
            if (session.IsCurrent)
            {
                MessageBox.Show(dialog, "Cannot revoke the current session. Sign out instead.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (MessageBox.Show(dialog, $"Revoke session on {session.DeviceInfo}?", "Revoke Session", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                UserProfileStore.RemoveSession(session.SessionId);
                list.Items.Remove(list.SelectedItems[0]);
            }
        };

        var revokeAllBtn = new GButton("Revoke All Others") { Width = 160 };
        revokeAllBtn.Primary = false;
        revokeAllBtn.Click += (s, e) =>
        {
            if (MessageBox.Show(dialog, "Revoke all sessions except the current one?", "Revoke All", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                var currentId = profile.ActiveSessions.FirstOrDefault(x => x.IsCurrent)?.SessionId;
                var toRemove = profile.ActiveSessions.Where(x => x.SessionId != currentId).Select(x => x.SessionId).ToList();
                foreach (var id in toRemove) UserProfileStore.RemoveSession(id);
                dialog.Close();
            }
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(20), BackColor = Theme.Bg };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        layout.Controls.Add(list);
        var btnFlow = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 0) };
        btnFlow.Controls.AddRange(new Control[] { revokeAllBtn, revokeBtn });
        layout.Controls.Add(btnFlow);

        dialog.Controls.Add(layout);
        dialog.ShowDialog(mainForm);
    }

    static Control Stack(params (Control c, bool fill)[] rows)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Theme.Bg, Padding = new Padding(30, 24, 30, 16) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var (c, fill) in rows)
        {
            bool auto = c is FlowLayoutPanel fl && fl.AutoSize;
            t.RowStyles.Add(fill ? new RowStyle(SizeType.Percent, 100) : auto ? new RowStyle(SizeType.AutoSize) : new RowStyle(SizeType.Absolute, c.Height + 12));
            c.Dock = DockStyle.Fill; c.Margin = new Padding(0, 0, 0, 12);
            t.Controls.Add(c);
        }
        return t;
    }

    static Label L(string text, float size = 9.5f, Color? color = null, bool bold = false, Color? bg = null) =>
        new() { Text = text, AutoSize = true, Font = Theme.F(size, bold), ForeColor = color ?? Theme.Silver, BackColor = bg ?? Theme.Bg, UseMnemonic = false };

    static TextBox Input(string text, int w = 340, string placeholder = "") => new TextBox
    {
        Text = text, Width = w, BackColor = Theme.Surf2, ForeColor = Theme.Txt, BorderStyle = BorderStyle.FixedSingle,
        Font = Theme.F(10f), Margin = new Padding(0, 6, 8, 0), PlaceholderText = placeholder
    };
}