using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ScumRconTool.Services;

namespace ScumRconTool.Views;

public sealed class UsageDirectoryConsentDialog : Window
{
    private const string GithubUrl = "https://github.com/LMNT-Gaming/ScumRconTool";
    private const string PrivacyUrl = UsageDirectoryService.PrivacyUrl;

    private UsageDirectoryConsentDialog(bool german)
    {
        Title = german ? "Freiwillige LMNT Serverliste" : "Optional LMNT server directory";
        Width = 680;
        MaxHeight = 760;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(18, 19, 25));
        Foreground = Brushes.White;

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock
        {
            Text = german ? "Deine freiwillige Zustimmung" : "Your voluntary consent",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        });
        panel.Children.Add(Paragraph(german
            ? "Möchtest du deinen SCUM-Server in der öffentlichen LMNT Serverliste anzeigen? Die Nutzung des Tools ist nicht von deiner Zustimmung abhängig. Eine Ablehnung hat keinerlei Nachteile."
            : "Would you like to show your SCUM server in the public LMNT server directory? Use of the tool does not depend on your consent. Declining has no disadvantages."));
        panel.Children.Add(Heading(german ? "An die LMNT-API übertragen" : "Sent to the LMNT API"));
        panel.Children.Add(Paragraph(german
            ? "- konfigurierter Servername\n- öffentliche IP-Adresse aus dem SCUM/RCON-Hostfeld\n- installierte Tool-Version\n- Zeitpunkt der letzten Meldung\n- zufällige Installations-ID und Sicherheitstoken"
            : "- configured server name\n- public IP address from the SCUM/RCON host field\n- installed tool version\n- time of the latest report\n- random installation ID and security token"));
        panel.Children.Add(Heading(german ? "Öffentlich angezeigt" : "Shown publicly"));
        panel.Children.Add(Paragraph(german
            ? "Servername, SCUM-Server-IP, Tool-Version und letzter Meldezeitpunkt. Das Sicherheitstoken wird niemals angezeigt und serverseitig nur als Hash gespeichert."
            : "Server name, SCUM server IP, tool version, and latest report time. The security token is never displayed and is stored by the server only as a hash."));
        panel.Children.Add(Heading(german ? "Nicht übertragen" : "Not transmitted"));
        panel.Children.Add(Paragraph(german
            ? "Passwörter, Discord-Token, Logs, Chatdaten, Spieler-IDs und Inhalte deiner Scripts werden nicht übertragen. Nach Zustimmung erfolgt beim Start und danach alle 15 Minuten eine Aktualisierung."
            : "Passwords, Discord tokens, logs, chat data, player IDs, and script contents are not transmitted. After consent, an update is sent at startup and every 15 minutes."));
        panel.Children.Add(LinkParagraph(
            german
                ? "Das Red Raven RCON Tool ist Open Source. Wenn du Zweifel hast, kannst du vor deiner Entscheidung den vollständigen Quellcode auf GitHub ansehen: "
                : "Red Raven RCON Tool is open source. If you have any doubts, you can review the complete source code on GitHub before deciding: ",
            "LMNT-Gaming/ScumRconTool", GithubUrl));
        panel.Children.Add(LinkParagraph(
            german
                ? "Du kannst deine Zustimmung jederzeit in den Einstellungen widerrufen. Das Tool fordert dann die Löschung des Eintrags an. Datenschutzhinweise: "
                : "You can withdraw your consent at any time in Settings. The tool will then request deletion of the listing. Privacy information: ",
            PrivacyUrl, PrivacyUrl));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var decline = new Button { Content = german ? "Nein, nicht anzeigen" : "No, do not list", MinWidth = 155, Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
        decline.Click += (_, _) => DialogResult = false;
        var accept = new Button { Content = german ? "Ja, freiwillig zustimmen" : "Yes, I voluntarily consent", MinWidth = 190 };
        accept.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(decline);
        buttons.Children.Add(accept);
        panel.Children.Add(buttons);

        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    public static bool ShowConsent(bool german)
    {
        var dialog = new UsageDirectoryConsentDialog(german) { Owner = Application.Current?.MainWindow };
        return dialog.ShowDialog() == true;
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        FontSize = 16,
        Margin = new Thickness(0, 12, 0, 4)
    };

    private static TextBlock Paragraph(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(Color.FromRgb(202, 205, 215)),
        LineHeight = 21
    };

    private static TextBlock LinkParagraph(string prefix, string label, string url)
    {
        var block = Paragraph(prefix);
        block.Inlines.Clear();
        block.Inlines.Add(new Run(prefix));
        var link = new Hyperlink(new Run(label)) { NavigateUri = new Uri(url) };
        link.RequestNavigate += (_, args) =>
        {
            Process.Start(new ProcessStartInfo(args.Uri.AbsoluteUri) { UseShellExecute = true });
            args.Handled = true;
        };
        block.Inlines.Add(link);
        return block;
    }
}
