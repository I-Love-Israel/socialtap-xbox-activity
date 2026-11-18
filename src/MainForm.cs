using System.Drawing;
using System.Windows.Forms;

namespace SocialTapXboxActivity;

/// <summary>
/// Taskbar window with the "now playing" card. Shows the game's cover art and title with
/// "Playing through Xbox" underneath, or the Xbox logo with "Idle on Xbox" when no game is running.
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly Color CardColor = Color.FromArgb(31, 31, 31);
    private static readonly Color MutedText = Color.FromArgb(160, 160, 160);
    private static readonly Color XboxGreen = Color.FromArgb(16, 124, 16);
    private static readonly Color SocialTapOrange = Color.FromArgb(255, 102, 0);

    private readonly SocialTapClient _client = new();
    private readonly System.Windows.Forms.Timer _scanTimer = new() { Interval = (int)Config.GameScanInterval.TotalMilliseconds };
    private readonly System.Windows.Forms.Timer _presenceTimer = new() { Interval = (int)Config.PresenceInterval.TotalMilliseconds };

    private readonly PictureBox _icon = new()
    {
        Location = new Point(18, 19),
        Size = new Size(112, 112),
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.FromArgb(40, 40, 40),
    };

    private readonly Label _title = MakeLabel(new Point(146, 24), 246, 12f, FontStyle.Bold, Color.White);
    private readonly Label _subtitle = MakeLabel(new Point(146, 62), 246, 9.5f, FontStyle.Regular, XboxGreen);
    private readonly Label _detail = MakeLabel(new Point(146, 90), 246, 8.5f, FontStyle.Regular, MutedText);
    private readonly Label _status = MakeLabel(new Point(16, 176), 408, 8.5f, FontStyle.Regular, MutedText);
    private readonly Button _signIn = MakeButton("Sign in to SocialTap", new Point(16, 206), SocialTapOrange, Color.Black);
    private readonly Button _signOut = MakeButton("Sign out", new Point(224, 206), Color.FromArgb(51, 51, 51), Color.White);

    private Image? _xboxLogo;
    private string? _coverPath;
    private bool _scanning;

    public MainForm()
    {
        Text = "SocialTap Xbox Activity";
        ClientSize = new Size(440, 250);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        BackColor = Color.FromArgb(17, 17, 17);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9f);

        var card = new Panel
        {
            Location = new Point(16, 16),
            Size = new Size(408, 148),
            BackColor = CardColor,
        };
        card.Controls.AddRange(new Control[] { _icon, _title, _subtitle, _detail });

        _signIn.Click += SignIn_Click;
        _signOut.Click += SignOut_Click;

        Controls.AddRange(new Control[] { card, _status, _signIn, _signOut });

        Load += MainForm_Load;
        FormClosed += (_, _) =>
        {
            _scanTimer.Stop();
            _presenceTimer.Stop();
            _icon.Image = null;
            _xboxLogo?.Dispose();
        };
    }

    private void MainForm_Load(object? sender, EventArgs e)
    {
        _xboxLogo = XboxLogo.Create(256);
        ShowGame(null);
        UpdateButtons();
        _status.Text = _client.IsSignedIn
            ? "Signed in to SocialTap. Idle status is reported every minute."
            : "Not signed in. Sign in to report your idle status to SocialTap.";

        _scanTimer.Tick += async (_, _) => await ScanAsync();
        _presenceTimer.Tick += async (_, _) => await ReportPresenceAsync();
        _scanTimer.Start();
        _presenceTimer.Start();

        _ = ScanAsync();
        _ = ReportPresenceAsync();
    }

    private async Task ScanAsync()
    {
        if (_scanning)
        {
            return;
        }

        _scanning = true;
        try
        {
            var game = await Task.Run(GameDetector.FindRunningGame);
            ShowGame(game);
        }
        catch (Exception ex)
        {
            _status.Text = "Game scan failed: " + ex.Message;
        }
        finally
        {
            _scanning = false;
        }
    }

    private void ShowGame(GameInfo? game)
    {
        if (game is null)
        {
            _coverPath = null;
            SetIcon(_xboxLogo);
            _title.Text = "Idle on Xbox";
            _subtitle.Text = string.Empty;
            _detail.Text = string.Empty;
            return;
        }

        if (game.CoverPath != _coverPath)
        {
            _coverPath = game.CoverPath;
            SetIcon(LoadCover(game.CoverPath) ?? _xboxLogo);
        }

        _title.Text = game.Title;
        _subtitle.Text = "Playing through Xbox";
        _detail.Text = $"{game.ProcessName} · playing for {FormatDuration(DateTime.Now - game.StartedAt)}";
    }

    private void SetIcon(Image? image)
    {
        var old = _icon.Image;
        _icon.Image = image;
        if (old is not null && !ReferenceEquals(old, _xboxLogo))
        {
            old.Dispose();
        }
    }

    private static Image? LoadCover(string? path)
    {
        if (path is null)
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var image = Image.FromStream(stream);
            return new Bitmap(image);
        }
        catch
        {
            return null;
        }
    }

    private async Task ReportPresenceAsync()
    {
        if (_client.IsSignedIn)
        {
            var error = await _client.ReportIdleAsync(NativeMethods.IdleSeconds());
            _status.Text = error ?? "Connected to SocialTap. Idle status is reported every minute.";
        }

        UpdateButtons();
    }

    private async void SignIn_Click(object? sender, EventArgs e)
    {
        _signIn.Enabled = false;
        _status.Text = "Approve access in the browser window that just opened...";
        try
        {
            await _client.SignInAsync();
            await ReportPresenceAsync();
        }
        catch (Exception ex)
        {
            _status.Text = "Sign-in failed: " + ex.Message;
        }
        finally
        {
            UpdateButtons();
        }
    }

    private async void SignOut_Click(object? sender, EventArgs e)
    {
        await _client.SignOutAsync();
        _status.Text = "Signed out. Idle status is no longer reported.";
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        _signIn.Enabled = !_client.IsSignedIn;
        _signOut.Enabled = _client.IsSignedIn;
    }

    private static string FormatDuration(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes:00}m"
            : $"{span.Minutes}m";
    }

    private static Label MakeLabel(Point location, int width, float size, FontStyle style, Color color) => new()
    {
        Location = location,
        Size = new Size(width, 24),
        Font = new Font("Segoe UI", size, style),
        ForeColor = color,
        BackColor = Color.Transparent,
        AutoEllipsis = true,
    };

    private static Button MakeButton(string text, Point location, Color back, Color fore) => new()
    {
        Text = text,
        Location = location,
        Size = new Size(200, 30),
        FlatStyle = FlatStyle.Flat,
        BackColor = back,
        ForeColor = fore,
        Cursor = Cursors.Hand,
    };
}
