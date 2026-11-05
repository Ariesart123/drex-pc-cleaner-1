using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
namespace Drex;

public sealed class LoginForm : Form
{
    readonly Panel _card = new();
    readonly Panel _googleBtn = new();
    readonly Panel _divider = new();
    readonly TextBox _email = new();
    readonly TextBox _password = new();
    readonly TextBox _confirmPassword = new();
    readonly GButton _loginBtn = new("Sign in", true);
    readonly GButton _registerBtn = new("Create account");
    readonly LinkLabel _forgotLink = new();
    readonly LinkLabel _verifyEmailLink = new();
    readonly LinkLabel _switchToRegister = new();
    readonly LinkLabel _switchToLogin = new();
    readonly Label _title = new();
    readonly Label _subtitle = new();
    readonly Label _error = new();
    readonly PictureBox _logo = new();
    readonly CheckBox _rememberMe = new();
    bool _isRegisterMode = false;
    
    public UserProfile? AuthenticatedProfile { get; private set; }

    public LoginForm()
    {
        Text = "Welcome to DREX PC CLEANER";
        BackColor = Theme.Bg;
        ForeColor = Theme.Txt;
        Font = Theme.F(9.5f);
        ClientSize = new Size(480, 640);
        MinimumSize = new Size(440, 580);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = true;
        Icon = AppIcon.Get(48);
        DoubleBuffered = true;

        BuildUI();
    }

    void BuildUI()
    {
        // Logo
        _logo.Image = AppIcon.Get(96).ToBitmap();
        _logo.Size = new Size(96, 96);
        _logo.SizeMode = PictureBoxSizeMode.Zoom;
        _logo.BackColor = Color.Transparent;

        // Title
        _title.Text = "DREX PC CLEANER";
        _title.Font = Theme.F(28f, true);
        _title.ForeColor = Theme.Txt;
        _title.AutoSize = true;

        _subtitle.Text = "Sign in to protect and optimize your PC";
        _subtitle.Font = Theme.F(10f);
        _subtitle.ForeColor = Theme.Muted;
        _subtitle.AutoSize = true;

        // Card
        _card.BackColor = Theme.Surf;
        _card.Dock = DockStyle.Fill;
        _card.Padding = new Padding(40);
        _card.Paint += (s, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Theme.Round(new Rectangle(0, 0, _card.Width - 1, _card.Height - 1), 16);
            using var br = new SolidBrush(Theme.Surf);
            g.FillPath(br, path);
            using var pen = new Pen(Theme.Surf2, 1f);
            g.DrawPath(pen, path);
        };

        // Google Button
        _googleBtn.Height = 52;
        _googleBtn.Cursor = Cursors.Hand;
        _googleBtn.Paint += (s, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(1, 1, _googleBtn.Width - 3, _googleBtn.Height - 3);
            using var path = Theme.Round(r, 10);
            using var br = new SolidBrush(Theme.Surf);
            g.FillPath(br, path);
            using var pen = new Pen(Theme.Surf2, 1f);
            g.DrawPath(pen, path);

            // Google "G" logo (simplified) - using TextRenderer to avoid DrawString issues
            using var font = Theme.F(18f, true);
            var text = "G";
            var textSize = TextRenderer.MeasureText(text, font);
            int centerX = _googleBtn.Width / 2;
            int centerY = _googleBtn.Height / 2;
            int textX = centerX - textSize.Width / 2;
            int textY = centerY - textSize.Height / 2;

            // Draw colored G using TextRenderer for better reliability
            var colors = new[] { Color.FromArgb(66, 133, 244), Color.FromArgb(219, 68, 55), Color.FromArgb(244, 180, 0), Color.FromArgb(66, 133, 244), Color.FromArgb(52, 168, 83), Color.FromArgb(219, 68, 55) };
            for (int i = 0; i < text.Length; i++)
            {
                var c = text[i].ToString();
                var cSize = TextRenderer.MeasureText(c, font);
                using var br2 = new SolidBrush(colors[i % colors.Length]);
                TextRenderer.DrawText(g, c, font, new Rectangle(textX + i * cSize.Width, textY, cSize.Width, cSize.Height), colors[i % colors.Length], TextFormatFlags.NoPrefix);
            }

            var labelRect = new Rectangle(44, 0, _googleBtn.Width - 44, 52);
            TextRenderer.DrawText(g, "Continue with Google", Theme.F(11f), labelRect, Theme.Txt, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };
        _googleBtn.Click += async (s, e) => await StartGoogleAuth();

        // Divider
        _divider.Height = 1;
        _divider.BackColor = Theme.Surf2;
        _divider.Paint += (s, e) =>
        {
            var g = e.Graphics;
            g.DrawLine(new Pen(Theme.Surf2), 0, 0, _divider.Width, 0);
            var text = "or continue with email";
            var font = Theme.F(9f);
            var textSize = TextRenderer.MeasureText(text, font);
            var textRect = new Rectangle((_divider.Width - textSize.Width) / 2, -10, textSize.Width, 20);
            using var bg = new SolidBrush(Theme.Surf);
            g.FillRectangle(bg, textRect);
            TextRenderer.DrawText(g, text, font, textRect, Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        };

        // Email
        _email.Width = 360;
        _email.BackColor = Theme.Surf2;
        _email.ForeColor = Theme.Txt;
        _email.BorderStyle = BorderStyle.FixedSingle;
        _email.Font = Theme.F(10f);
        _email.PlaceholderText = "Email address";

        // Password
        _password.Width = 360;
        _password.BackColor = Theme.Surf2;
        _password.ForeColor = Theme.Txt;
        _password.BorderStyle = BorderStyle.FixedSingle;
        _password.Font = Theme.F(10f);
        _password.PlaceholderText = "Password";
        _password.UseSystemPasswordChar = true;

        // Remember Me Checkbox
        _rememberMe.Text = "Remember me";
        _rememberMe.Font = Theme.F(9f);
        _rememberMe.ForeColor = Theme.Silver;
        _rememberMe.AutoSize = true;
        _rememberMe.Cursor = Cursors.Hand;

        // Confirm Password (register mode only)
        _confirmPassword.Width = 360;
        _confirmPassword.BackColor = Theme.Surf2;
        _confirmPassword.ForeColor = Theme.Txt;
        _confirmPassword.BorderStyle = BorderStyle.FixedSingle;
        _confirmPassword.Font = Theme.F(10f);
        _confirmPassword.PlaceholderText = "Confirm password";
        _confirmPassword.UseSystemPasswordChar = true;
        _confirmPassword.Visible = false;

        // Login Button
        _loginBtn.Height = 48;
        _loginBtn.Width = 360;
        _loginBtn.Click += async (s, e) => await HandleLogin();

        // Register Button
        _registerBtn.Height = 48;
        _registerBtn.Width = 360;
        _registerBtn.Primary = false;
        _registerBtn.Click += async (s, e) => await HandleRegister();

        // Forgot Password Link
        _forgotLink.Text = "Forgot password?";
        _forgotLink.LinkColor = Theme.Accent;
        _forgotLink.ActiveLinkColor = Theme.AccentHi;
        _forgotLink.VisitedLinkColor = Theme.Accent;
        _forgotLink.Font = Theme.F(9f);
        _forgotLink.AutoSize = true;
        _forgotLink.LinkClicked += (s, e) => ShowForgotPassword();

        // Verify Email Link
        _verifyEmailLink.Text = "Verify email";
        _verifyEmailLink.LinkColor = Theme.Accent;
        _verifyEmailLink.ActiveLinkColor = Theme.AccentHi;
        _verifyEmailLink.VisitedLinkColor = Theme.Accent;
        _verifyEmailLink.Font = Theme.F(9f);
        _verifyEmailLink.AutoSize = true;
        _verifyEmailLink.LinkClicked += (s, e) => ShowVerifyEmailDialog();
        _verifyEmailLink.Visible = false; // Only show when needed

        // Switch Links
        _switchToRegister.Text = "Don't have an account? Create one";
        _switchToRegister.LinkColor = Theme.Accent;
        _switchToRegister.ActiveLinkColor = Theme.AccentHi;
        _switchToRegister.VisitedLinkColor = Theme.Accent;
        _switchToRegister.Font = Theme.F(9f);
        _switchToRegister.AutoSize = true;
        _switchToRegister.LinkClicked += (s, e) => SwitchToRegister();

        _switchToLogin.Text = "Already have an account? Sign in";
        _switchToLogin.LinkColor = Theme.Accent;
        _switchToLogin.ActiveLinkColor = Theme.AccentHi;
        _switchToLogin.VisitedLinkColor = Theme.Accent;
        _switchToLogin.Font = Theme.F(9f);
        _switchToLogin.AutoSize = true;
        _switchToLogin.Visible = false;
        _switchToLogin.LinkClicked += (s, e) => SwitchToLogin();

        // Error Label
        _error.ForeColor = Theme.Bad;
        _error.Font = Theme.F(9f);
        _error.AutoSize = true;
        _error.Visible = false;
        _error.MaximumSize = new Size(360, 0);

        // Layout
        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent
        };

        flow.Controls.AddRange(new Control[] {
            _logo, _title, _subtitle,
            _googleBtn,
            _divider,
            _email, _password, _rememberMe, _confirmPassword,
            _loginBtn, _registerBtn,
            _forgotLink, _verifyEmailLink, _switchToRegister, _switchToLogin,
            _error
        });

        foreach (Control c in flow.Controls)
        {
            c.Margin = new Padding(0, 0, 0, 16);
        }
        _divider.Margin = new Padding(0, 8, 0, 16);
        _forgotLink.Margin = new Padding(0, 0, 0, 24);
        _switchToRegister.Margin = new Padding(0, 0, 0, 0);
        _switchToLogin.Margin = new Padding(0, 0, 0, 0);

        _card.Controls.Add(flow);
        flow.Location = new Point(0, 0);
        flow.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _card.Controls.Add(flow);

        var wrapper = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(40) };
        wrapper.Controls.Add(_card);
        _card.Dock = DockStyle.Fill;
        Controls.Add(wrapper);

        Shown += (s, e) => _email.Focus();
        AcceptButton = _loginBtn;
    }

    void SwitchToRegister()
    {
        _isRegisterMode = true;
        _title.Text = "Create your account";
        _subtitle.Text = "Enter your details to get started";
        _loginBtn.Visible = false;
        _registerBtn.Visible = true;
        _confirmPassword.Visible = true;
        _forgotLink.Visible = false;
        _verifyEmailLink.Visible = false;
        _switchToRegister.Visible = false;
        _switchToLogin.Visible = true;
        _email.Text = "";
        _password.Text = "";
        _confirmPassword.Text = "";
        _error.Visible = false;
        _email.Focus();
    }

    void SwitchToLogin()
    {
        _isRegisterMode = false;
        _title.Text = "DREX PC CLEANER";
        _subtitle.Text = "Sign in to protect and optimize your PC";
        _loginBtn.Visible = true;
        _registerBtn.Visible = false;
        _confirmPassword.Visible = false;
        _forgotLink.Visible = true;
        _switchToRegister.Visible = true;
        _switchToLogin.Visible = false;

        // Show verify email link only if there's an unverified manual account
        var profile = UserProfileStore.Load();
        _verifyEmailLink.Visible = profile != null && profile.Provider == "manual" && !profile.IsVerified;

        _email.Text = "";
        _password.Text = "";
        _error.Visible = false;
        _email.Focus();
    }

    async Task StartGoogleAuth()
    {
        SetBusy(true);
        _error.Visible = false;

        try
        {
            var result = await AuthService.InitiateGoogleAuthAsync();

            if (result == "AUTH_SUCCESS")
            {
                // Load the profile that was saved during authentication
                var profile = UserProfileStore.Load();
                if (profile != null && profile.Provider == "google")
                {
                    AuthenticatedProfile = profile;
                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                }
                ShowError("Authentication succeeded but profile not found");
            }
            else if (result == "AUTH_CANCELLED")
            {
                ShowError("Google authentication was cancelled");
            }
            else
            {
                ShowError(result); // AUTH_ERROR: message
            }
        }
        catch (Exception ex)
        {
            ShowError($"Google authentication failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    async Task HandleLogin()
    {
        if (string.IsNullOrWhiteSpace(_email.Text))
        { ShowError("Please enter your email"); return; }
        if (string.IsNullOrWhiteSpace(_password.Text))
        { ShowError("Please enter your password"); return; }

        SetBusy(true);
        _error.Visible = false;

        try
        {
            var result = await AuthService.LoginWithEmailPasswordAsync(_email.Text.Trim(), _password.Text);
            if (result.Success)
            {
                var profile = result.Profile!;

                // Check if email is verified (for manual accounts)
                if (profile.Provider == "manual" && !profile.IsVerified)
                {
                    ShowError("Please verify your email address first. Check your inbox for the verification link.");
                    return;
                }

                // Handle remember me
                if (_rememberMe.Checked)
                {
                    AuthService.GenerateRememberMeToken(profile);
                }
                else
                {
                    AuthService.ClearRememberMeToken();
                }

                AuthenticatedProfile = result.Profile;
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                ShowError(result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            ShowError($"Login failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    async Task HandleRegister()
    {
        if (string.IsNullOrWhiteSpace(_email.Text))
        { ShowError("Please enter your email"); return; }
        if (!IsValidEmail(_email.Text))
        { ShowError("Please enter a valid email address"); return; }
        if (string.IsNullOrWhiteSpace(_password.Text))
        { ShowError("Please enter a password"); return; }
        if (_password.Text.Length < 8)
        { ShowError("Password must be at least 8 characters"); return; }
        if (_password.Text != _confirmPassword.Text)
        { ShowError("Passwords do not match"); return; }

        SetBusy(true);
        _error.Visible = false;

        try
        {
            var result = await AuthService.RegisterWithEmailPasswordAsync(_email.Text.Trim(), _password.Text, _confirmPassword.Text);
            if (result.Success)
            {
                var profile = result.Profile!;

                // Generate and send email verification token
                var verificationResult = await AuthService.SendVerificationEmailAsync(profile.Email);
                if (verificationResult.Success)
                {
                    MessageBox.Show(this,
                        "Account created successfully! A verification email has been sent to your email address.\n\nPlease check your inbox and click the verification link to activate your account.",
                        "Account Created", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                // Don't auto-login - user needs to verify email first
                ShowError("Account created! Please check your email to verify your account before signing in.");

                // Show verify email link since there's now an unverified manual account
                _verifyEmailLink.Visible = true;
            }
            else
            {
                ShowError(result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            ShowError($"Registration failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    void ShowForgotPassword()
    {
        var email = Microsoft.VisualBasic.Interaction.InputBox("Enter your email address to reset your password:", "Reset Password", _email.Text);
        if (!string.IsNullOrWhiteSpace(email) && IsValidEmail(email))
        {
            _ = AuthService.RequestPasswordResetAsync(email);
            MessageBox.Show(this, "If an account exists, a password reset link has been sent.", "Password Reset", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    void ShowVerifyEmailDialog()
    {
        var profile = UserProfileStore.Load();
        if (profile == null || profile.Provider != "manual")
        {
            MessageBox.Show(this, "No unverified manual account found.", "Verify Email", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (profile.IsVerified)
        {
            MessageBox.Show(this, "Your email is already verified. You can now sign in.", "Already Verified", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var token = Microsoft.VisualBasic.Interaction.InputBox(
            "Enter the verification token from your email:",
            "Verify Email",
            "");

        if (string.IsNullOrWhiteSpace(token))
            return;

        if (AuthService.VerifyEmailToken(token.Trim()))
        {
            UserProfileStore.Save(UserProfileStore.CurrentProfile!);
            MessageBox.Show(this, "Email verified successfully! You can now sign in.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(this, "Invalid or expired verification token. Please request a new verification email.", "Invalid Token", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void ShowError(string message)
    {
        _error.Text = message;
        _error.Visible = true;
    }

    void SetBusy(bool busy)
    {
        _googleBtn.Enabled = !busy;
        _loginBtn.Enabled = !busy;
        _registerBtn.Enabled = !busy;
        _email.Enabled = !busy;
        _password.Enabled = !busy;
        _confirmPassword.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email && email.Contains(".");
        }
        catch { return false; }
    }

    }