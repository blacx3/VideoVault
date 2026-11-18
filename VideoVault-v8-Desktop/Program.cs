using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal static class DesktopProgram
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new VideoVaultWindow());
    }
}

internal sealed class VideoVaultWindow : Form
{
    private readonly WebView2 browser = new() { Dock = DockStyle.Fill };
    private WebApplication? server;
    private bool starting;
    private bool closing;
    private bool shutdownComplete;

    public VideoVaultWindow()
    {
        Text = "VideoVault";
        Width = 1180;
        Height = 780;
        MinimumSize = new Size(720, 500);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        ControlBox = true;
        MaximizeBox = true;
        MinimizeBox = true;
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Q)
            {
                e.SuppressKeyPress = true;
                Close();
            }
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Exit VideoVault (Ctrl+Q)", null, (_, _) => Close());
        ContextMenuStrip = menu;
        BackColor = Color.FromArgb(15, 17, 24);
        Controls.Add(browser);
        Shown += StartApp;
        FormClosing += CloseApp;
    }

    private async void StartApp(object? sender, EventArgs e)
    {
        if (starting) return;
        starting = true;
        try
        {
            server = VideoVaultBackend.Create();
            await server.StartAsync();
            if (closing) return;
            string userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VideoVault", "WebView2");
            Directory.CreateDirectory(userData);
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
            await browser.EnsureCoreWebView2Async(environment);
            if (closing) return;
            // Form handles window close, Alt+F4 and Ctrl+Q when it has focus.
            // CoreWebView2 does not expose AcceleratorKeyPressed in this SDK.
            browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            browser.CoreWebView2.Navigate("http://127.0.0.1:3000/");
        }
        catch (Exception ex)
        {
            if (closing || IsDisposed) return;
            MessageBox.Show(this,
                "VideoVault could not start:\n\n" + ex.Message +
                "\n\nClose any older VideoVault server using port 3000, then try again.",
                "VideoVault", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private async void CloseApp(object? sender, FormClosingEventArgs e)
    {
        if (shutdownComplete) return;

        // Wait for the server we own to release 127.0.0.1:3000
        // before letting the desktop window and dotnet-run process exit.
        e.Cancel = true;
        if (closing) return;
        closing = true;
        try
        {
            if (browser.CoreWebView2 is not null) browser.CoreWebView2.Stop();
            if (server is not null)
            {
                using var timeout = new System.Threading.CancellationTokenSource(
                    TimeSpan.FromSeconds(5));
                try { await server.StopAsync(timeout.Token); }
                catch (OperationCanceledException) { }
                finally { await server.DisposeAsync(); }
                server = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("VideoVault shutdown: " + ex);
        }
        finally
        {
            shutdownComplete = true;
            if (!IsDisposed) Close();
        }
    }
}
