using System.Text.RegularExpressions;

namespace ScumRconTool.Services;

public enum BuiltinChatCommandId
{
    InsuranceQuote, InsuranceList, InsuranceRefund, Confirm, Cancel,
    ClaimAll, Reward, Quiz, Mechs, BuyEvent, Challenges, ShopOrder, ShopOrders, RedeemCode
}

public sealed record BuiltinChatCommandDefinition(
    BuiltinChatCommandId Id, string Syntax, string Aliases, string DescriptionDe, string DescriptionEn);

// Required commands live here, not in editable settings JSON. UI and dispatch share this catalog.
public static class BuiltinChatCommandCatalog
{
    public const string QuizPattern = @"^/quiz(?<number>\d+)(?:\s+(?<answer>.*))?$";
    public const string RewardPrefix = "/reward-";
    public static IReadOnlyList<BuiltinChatCommandDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        new BuiltinChatCommandDefinition(BuiltinChatCommandId.InsuranceQuote, "/versicherung ID [1|2|4]", "/insurance",
            "Fahrzeug und Eigentümer prüfen, Preise oder ein Angebot anzeigen. Abschluss mit /ja innerhalb von 60 Sekunden. Aktivierung, Preise und Rabatte: Fahrzeugversicherung → Einstellungen.",
            "Verify vehicle ownership and show prices or a quote. Confirm with /yes within 60 seconds. Enable and configure prices/discounts in Vehicle insurance → Settings."),
        new BuiltinChatCommandDefinition(BuiltinChatCommandId.InsuranceList, "/versicherungen", "/insurances",
            "Eigene Versicherungsverträge und Vertrags-IDs privat anzeigen. Benötigt aktivierte Fahrzeugversicherung.", "Privately list your insurance policies and IDs. Requires enabled vehicle insurance."),
        new BuiltinChatCommandDefinition(BuiltinChatCommandId.InsuranceRefund, "/getrefund V-ID", "",
            "Ersatz für den eigenen freigegebenen Versicherungsfall anfordern. Zerstörungsprüfung und Schutz vor doppeltem Spawn bleiben aktiv.", "Request a replacement for your approved insurance claim. Destruction checks and duplicate-spawn protection remain active."),
        new BuiltinChatCommandDefinition(BuiltinChatCommandId.Confirm, "/ja", "/yes · ja (Vote)",
            "Offenes Versicherungsangebot oder kostenpflichtigen Vote bestätigen, niemals beides. /yes bestätigt Versicherungen; die Vote-Bestätigung verwendet /ja oder ja.", "Confirm a pending insurance quote or paid vote, never both. /yes confirms insurance; paid votes use /ja or ja."),
        new BuiltinChatCommandDefinition(BuiltinChatCommandId.Cancel, "/nein", "/no",
            "Ein offenes Versicherungsangebot abbrechen. Keine Abbuchung.", "Cancel a pending insurance quote. No payment is made."),
        new BuiltinChatCommandDefinition(BuiltinChatCommandId.ClaimAll, "/claimall", "",
            "Alle offenen eigenen Challenge-/Quiz-Belohnungen abholen. Geld, Fame und Lootpacks werden durch die bestehende Rewardlogik geprüft.", "Claim all your pending challenge/quiz rewards. Money, fame and loot packs use the existing reward checks."),
        new BuiltinChatCommandDefinition(BuiltinChatCommandId.Reward, "/reward-CODE", "Persönlicher Code / Personal code",
            "Eine eigene Challenge-/Quiz-Belohnung abholen. CODE wird automatisch vergeben; fremde Codes können nicht eingelöst werden. Einzelne private Codes stehen nicht im Katalog.", "Claim one of your challenge/quiz rewards. CODE is generated automatically; another player's code cannot be redeemed. Individual private codes are not listed here."),
        new BuiltinChatCommandDefinition(BuiltinChatCommandId.Quiz, "/quizN ANTWORT", "/quiz1 … · /quiz2 …",
            "Auf Quiz Nummer N antworten. Quiznummer, Zeitfenster, Versuche und Belohnungen werden unter Herausforderungen gepflegt. Es wird kein eigener Command je Quiz benötigt.", "Answer quiz number N. Configure quiz numbers, time windows, attempts and rewards under Challenges. No separate command per quiz is needed."),
        new BuiltinChatCommandDefinition(BuiltinChatCommandId.Mechs, "/mechs", "",
            "Den zuletzt lokal gespeicherten Mech-Status des Setting Randomizers abfragen. Kein zusätzlicher FTP-Abruf pro Chatbefehl.", "Read the Setting Randomizer's last locally stored mech status. No additional FTP request per chat command."),
        new BuiltinChatCommandDefinition(BuiltinChatCommandId.BuyEvent, "/buyevent [NAME]", "",
            "Ohne Name kaufbare Events anzeigen; mit Name ein Buyzone-Event kaufen. Namen, Preise und Verfügbarkeit werden in der Skriptzone eingestellt.", "List buyable events, or buy a Buyzone event by name. Configure names, prices and availability in Script zone."),
        new BuiltinChatCommandDefinition(BuiltinChatCommandId.Challenges, "/wc", "/challenge · /challenges",
            "Eigenen Challenge-Stand privat abfragen. Eine passende eigene Chatregel hat hier wie bisher Vorrang; ohne eigene Regel greift dieser eingebaute Befehl.", "Privately show your challenge progress. A matching custom chat rule retains precedence; this built-in handles requests without a custom rule."),
        new BuiltinChatCommandDefinition(BuiltinChatCommandId.ShopOrder, "/bestellung CODE", "/order CODE",
            "Eine auf der Webseite vorbereitete Bestellung abholen. Der Kontostand wird unmittelbar vor Abbuchung und Ausgabe erneut geprüft.",
            "Collect an order prepared on the website. The balance is checked again immediately before debit and delivery."),
        new BuiltinChatCommandDefinition(BuiltinChatCommandId.ShopOrders, "/bestellungen", "/orders",
            "Eigene noch offene Bestellcodes privat anzeigen.", "Privately list your pending order codes."),
        new BuiltinChatCommandDefinition(BuiltinChatCommandId.RedeemCode, "/CODE", "z. B. /starter · e.g. /starter",
            "Zusätzlich werden deine konfigurierten RedeemCodes erkannt. Konkrete Codes, Nutzungslimits und Aktionen werden weiterhin unter RedeemCodes gepflegt. Dieses Muster fängt keine unbekannten Befehle ab.", "Configured redeem codes are also recognized. Manage actual codes, usage limits and actions under RedeemCodes. This pattern does not intercept unknown commands.")
    });

    public static BuiltinChatCommandId? Resolve(string? text)
    {
        text = (text ?? "").Trim();
        var token = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? "";
        if (Regex.IsMatch(text, QuizPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return BuiltinChatCommandId.Quiz;
        if (text.StartsWith(RewardPrefix, StringComparison.OrdinalIgnoreCase)) return BuiltinChatCommandId.Reward;
        return token switch
        {
            "/versicherung" or "/insurance" => BuiltinChatCommandId.InsuranceQuote,
            "/versicherungen" or "/insurances" => BuiltinChatCommandId.InsuranceList,
            "/getrefund" => BuiltinChatCommandId.InsuranceRefund,
            "/ja" or "/yes" => BuiltinChatCommandId.Confirm,
            "/nein" or "/no" => BuiltinChatCommandId.Cancel,
            "/buyevent" => BuiltinChatCommandId.BuyEvent,
            "/claimall" when text.Equals(token, StringComparison.OrdinalIgnoreCase) => BuiltinChatCommandId.ClaimAll,
            "/mechs" when text.Equals(token, StringComparison.OrdinalIgnoreCase) => BuiltinChatCommandId.Mechs,
            "/wc" or "/challenge" or "/challenges" when text.Equals(token, StringComparison.OrdinalIgnoreCase) => BuiltinChatCommandId.Challenges,
            "/bestellung" or "/order" => BuiltinChatCommandId.ShopOrder,
            "/bestellungen" or "/orders" when text.Equals(token, StringComparison.OrdinalIgnoreCase) => BuiltinChatCommandId.ShopOrders,
            _ => null
        };
    }
    public static bool Is(BuiltinChatCommandId id, string? text) => Resolve(text) == id;
    public static bool IsInsurance(string? text) => Resolve(text) is BuiltinChatCommandId.InsuranceQuote or BuiltinChatCommandId.InsuranceList or BuiltinChatCommandId.InsuranceRefund;
}
