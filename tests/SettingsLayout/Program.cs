using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using ScumRconTool;
using ScumRconTool.ViewModels;

internal static class Program
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var directory = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath(".");
            var main = XDocument.Load(Path.Combine(directory, "MainWindow.xaml"));
            var insurance = XDocument.Load(Path.Combine(directory, "Views", "VehicleInsuranceView.xaml"));
            var tabs = main.Descendants(Wpf + "TabControl").Single(e => (string?)e.Attribute(X + "Name") == "MainTabs");
            var settings = tabs.Elements().Single(e => (string?)e.Attribute("Tag") == "Settings");
            var integrations = tabs.Elements().Single(e => (string?)e.Attribute("Tag") == "IntegrationsMaintenance");
            var discord = tabs.Elements().Single(e => (string?)e.Attribute("Tag") == "DiscordBridge");
            var chat = tabs.Elements().Single(e => (string?)e.Attribute("Tag") == "ChatCommands");
            Require(tabs.Elements(Wpf + "TabItem").Count() == 16, "Expected sixteen main tabs.");
            Require(!settings.ToString().Contains("Vote") && !settings.ToString().Contains("UpdateLatestJsonUrl") && !settings.ToString().Contains("WebApi"), "Optional and vote configuration leaked into Setup.");
            Require(!discord.ToString().Contains("UpdateLatestJsonUrl"), "Update feed leaked into Discord.");
            Require(new UiTextProvider("de")["IntegrationsMaintenance"] == "Erweitert" && new UiTextProvider("en")["IntegrationsMaintenance"] == "Advanced", "Optional integrations must be labelled Advanced.");
            Require(main.Descendants().Where(e => e.Attributes().Any(a => a.Value.Contains("Binding Settings.Shop", StringComparison.Ordinal)))
                .All(e => e.AncestorsAndSelf().Contains(integrations)), "Shop configuration leaked outside Advanced.");
            var votes = chat.Descendants(Wpf + "TabItem").Single(e => (string?)e.Attribute("Header") == "{Binding Texts[VotesTab]}");
            Require(votes.ToString().Contains("Settings.VotePrice") && votes.ToString().Contains("Settings.VoteCooldownHours"), "Votes are not separately configurable.");
            Require(main.Descendants(Wpf + "ItemsControl").Count(e => (string?)e.Attribute("ItemsSource") == "{Binding SettingRandomizerPacks}") == 1, "Dice-set editor lost or duplicated.");
            foreach (var key in new[] { "WeeklyTaskWebApiEndpointUrl", "EconomyWebApiEndpointUrl", "ShopWorkerApiEndpointUrl", "VehicleInsurance.WebEndpoint", "UsageDirectoryEndpointUrl", "UpdateLatestJsonUrl" })
                Require(integrations.ToString().Contains("Settings." + key), $"Integration missing: {key}");
            foreach (var name in new[] { "RedeemCodesTab", "SettingRandomizerTab", "EconomyTab", "LootPacksTab", "ScriptsTab", "LogsTab", "SettingsTab", "GgconHttpPasswordBox", "InsuranceWebApiTokenBox" })
                Require(main.Descendants().Count(e => (string?)e.Attribute(X + "Name") == name) == 1, $"Missing/duplicate navigation or password field: {name}");
            Require(!main.Descendants(Wpf + "TextBox").Any(e => ((string?)e.Attribute("Text"))?.Contains("Settings.GgconHttpPassword") == true), "HTTP password is not masked.");

            var helpCount = 0;
            var translationSource = File.ReadAllText(Path.Combine(directory, "ViewModels", "UiTextProvider.cs")) + File.ReadAllText(Path.Combine(directory, "ViewModels", "UiTextProvider.Setup.cs"));
            var translationKeys = Regex.Matches(translationSource, "\\[\"([^\"]+)\"\\]").Select(m => m.Groups[1].Value).ToHashSet();
            foreach (var doc in new[] { main, insurance })
            {
                foreach (var element in doc.Descendants().Where(e => e.Name == Wpf + "TextBox" || e.Name == Wpf + "ComboBox" || e.Name == Wpf + "CheckBox"))
                {
                    var binding = element.Attributes().Select(a => a.Value).FirstOrDefault(v => v.StartsWith("{Binding Settings.", StringComparison.Ordinal));
                    if (binding is null) continue;
                    var path = Regex.Match(binding, @"Binding Settings\.([\w.]+)").Groups[1].Value;
                    var type = typeof(BotSettings);
                    foreach (var part in path.Split('.'))
                        type = type.GetProperty(part)?.PropertyType ?? throw new InvalidOperationException($"Unknown setting: {path}");
                    Require(element.Attribute("ToolTip") != null, $"No help for {path}");
                    helpCount++;
                }
                foreach (var attribute in doc.Descendants().Attributes())
                {
                    foreach (Match match in Regex.Matches(attribute.Value, @"Texts\[([^\]]+)\]"))
                    {
                        var key = match.Groups[1].Value;
                        foreach (var language in new[] { "de", "en" })
                            Require(translationKeys.Contains(key) && !string.IsNullOrWhiteSpace(new UiTextProvider(language)[key]), $"Missing translation: {language}/{key}");
                    }
                }
            }
            Console.WriteLine($"PASS: structure, setting paths, navigation, translations; {helpCount} setting fields with help.");

            if (args.Contains("--render"))
            {
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/RedRavenRconTool;component/Themes/DarkRedTheme.xaml", UriKind.Relative)
                });
                var output = Path.Combine(directory, ".codex_tmp", "settings-preview");
                Directory.CreateDirectory(output);
                foreach (var tab in new[] { settings, discord, chat, integrations })
                    Render(main.Root!, tab, output);
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Render(XElement originalRoot, XElement tab, string output)
    {
        var windowMarkup = new XElement(Wpf + "Window", originalRoot.Attributes().Where(a => a.IsNamespaceDeclaration).Select(a => new XAttribute(a)), new XElement(Wpf + "TabControl", new XElement(tab)));
        foreach (var declaration in windowMarkup.Attributes().Where(a => a.IsNamespaceDeclaration && a.Value.StartsWith("clr-namespace:")).ToArray())
            declaration.Value += ";assembly=RedRavenRconTool";
        var events = new HashSet<string> { "Click", "Loaded", "PasswordChanged", "SelectionChanged", "TextChanged", "SizeChanged", "MouseDown", "MouseMove", "MouseUp", "MouseWheel", "MouseLeftButtonDown", "MouseLeftButtonUp" };
        foreach (var attribute in windowMarkup.Descendants().Attributes().Where(a => events.Contains(a.Name.LocalName)).ToArray()) attribute.Remove();
        var window = (Window)XamlReader.Parse(windowMarkup.ToString());
        window.DataContext = new PreviewModel(); // Never load accounts, instantiate MainViewModel or start services.
        var rootTabs = (TabControl)window.Content;
        var subTabs = Descendants(rootTabs).OfType<TabControl>().Where(t => !ReferenceEquals(t, rootTabs)).First();
        var tag = (string)tab.Attribute("Tag")!;
        for (var i = 0; i < subTabs.Items.Count; i++)
        {
            subTabs.SelectedIndex = i;
            rootTabs.Measure(new Size(1120, 720));
            rootTabs.Arrange(new Rect(0, 0, 1120, 720));
            rootTabs.UpdateLayout();
            var image = new RenderTargetBitmap(1120, 720, 96, 96, PixelFormats.Pbgra32);
            image.Render(rootTabs);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(Path.Combine(output, $"{tag}-{i}.png"));
            encoder.Save(file);
            Console.WriteLine($"PASS: WPF layout {tag}/{i}");
        }
        window.Close();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

public sealed class PreviewModel
{
    public BotSettings Settings { get; } = new() { Host = "your-server.example", DiscordServerName = "Your SCUM server" };
    public UiTextProvider Texts { get; } = new("de");
    public IEnumerable<object> ChatCommandRules { get; } = [];
    public IEnumerable<string> BroadcastMessageTypes { get; } = ["Cyan", "Yellow"];
    public string VersionText => "Preview";
}
