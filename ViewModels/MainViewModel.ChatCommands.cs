using ScumRconTool.Services;

namespace ScumRconTool.ViewModels;

public sealed partial class MainViewModel
{
    private readonly SemaphoreSlim _shopOrderGate = new(1, 1);

    private async Task<bool> HandleBuiltinChatCommandAsync(ChatLogMessage message, CancellationToken ct)
    {
        // Confirmation ownership stays inside insurance; an unrelated /ja falls through to paid votes.
        if (await HandleVehicleInsuranceChatAsync(message, ct)) return true;
        return BuiltinChatCommandCatalog.Resolve(message.Message) switch
        {
            BuiltinChatCommandId.Quiz => await HandleQuizChallengeChatCommandAsync(message, ct),
            BuiltinChatCommandId.Mechs => await HandleMechsChatCommandAsync(message, ct),
            BuiltinChatCommandId.ShopOrder or BuiltinChatCommandId.ShopOrders => await HandleShopOrderChatCommandAsync(message, ct),
            _ => false
        };
    }

    private async Task<bool> HandleShopOrderChatCommandAsync(ChatLogMessage message, CancellationToken ct)
    {
        var commandId = BuiltinChatCommandCatalog.Resolve(message.Message);
        if (commandId is not (BuiltinChatCommandId.ShopOrder or BuiltinChatCommandId.ShopOrders)) return false;
        if (string.IsNullOrWhiteSpace(message.SteamId)) return true;
        var german = Texts.IsGerman;
        var api = new GgconHttpApiService(Settings);
        async Task Reply(string de, string en) => await api.SendMessageAsync(german ? de : en, "Cyan", message.SteamId, ct);
        if (!Settings.ShopOrdersEnabled)
        {
            await Reply("Bestellungen sind derzeit deaktiviert.", "Orders are currently disabled.");
            return true;
        }

        await _shopOrderGate.WaitAsync(ct);
        try
        {
            var shop = new ShopOrderApiService(Settings);
            if (commandId == BuiltinChatCommandId.ShopOrders)
            {
                var orders = await shop.ListAsync(message.SteamId, ct);
                if (orders.Count == 0) await Reply("Du hast keine offenen Bestellungen.", "You have no pending orders.");
                foreach (var pendingOrder in orders.Take(8))
                    await api.SendMessageAsync($"{pendingOrder.PackName}: /bestellung {pendingOrder.Code} ({pendingOrder.Price}$)", "Cyan", message.SteamId, ct);
                return true;
            }

            var parts = message.Message.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                await Reply("Nutzung: /bestellung CODE", "Usage: /order CODE");
                return true;
            }

            ShopOrderReservation order;
            try { order = await shop.ReserveAsync(parts[1].Trim().ToUpperInvariant(), message.SteamId, ct); }
            catch (ShopWorkerException ex) when (ex.Message is "order_not_found" or "order_expired" or "order_not_pending")
            {
                await Reply("Bestellcode ungültig, abgelaufen oder bereits verwendet.", "Order code is invalid, expired, or already used.");
                return true;
            }

            static bool SafeSpawnCode(string value) => !string.IsNullOrWhiteSpace(value) &&
                value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-');
            if (!string.Equals(order.SteamId, message.SteamId, StringComparison.Ordinal) ||
                order.Price is < 1 or > 100_000_000 || order.Fulfillment.Entries.Count == 0 || order.Fulfillment.Entries.Count > 100 ||
                order.Fulfillment.Entries.Any(x => !SafeSpawnCode(x.Code) || x.Quantity is < 1 or > 100))
            {
                await shop.ReviewAsync(order, "Invalid fulfillment payload; no debit attempted.", ct);
                await Reply("Bestellung ist fehlerhaft und wurde zur Adminprüfung gesperrt.", "Order is invalid and was held for admin review.");
                return true;
            }

            var account = await api.GetPlayerAccountAsync(message.SteamId, ct);
            if (!account.AccountBalance.HasValue || account.AccountBalance.Value < order.Price)
            {
                await shop.ReleaseAsync(order, "Insufficient balance at in-game redemption; no debit.", ct);
                await Reply($"Nicht genügend Scummies. Benötigt: {order.Price}$.", $"Insufficient Scummies. Required: {order.Price}$.");
                return true;
            }

            try
            {
                // From here on, an uncertain response is never retried automatically.
                await api.RemovePlayerCurrencyAsync(message.SteamId, order.Price, ct);
            }
            catch (Exception ex)
            {
                try { await shop.ReviewAsync(order, "Debit outcome uncertain: " + ex.Message, ct); } catch { }
                AppLogService.WriteException("ShopOrder.Debit", ex);
                await Reply("Zahlung muss geprüft werden. Es erfolgt keine automatische Wiederholung.", "Payment requires review. It will not be retried automatically.");
                return true;
            }

            try
            {
                if (order.Fulfillment.Type.Equals("vehicle", StringComparison.OrdinalIgnoreCase))
                {
                    if (order.Fulfillment.Entries.Count != 1 || order.Fulfillment.Entries[0].Quantity != 1)
                        throw new InvalidDataException("Vehicle order must contain exactly one vehicle.");
                    await api.InsuranceMutationAsync("/spawn-vehicle", new { steamId = message.SteamId, vehicle = order.Fulfillment.Entries[0].Code }, ct);
                }
                else if (order.Fulfillment.Type.Equals("items", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var entry in order.Fulfillment.Entries)
                        await api.ExecuteCommandAsync($"#ExecAs {message.SteamId} #SpawnItem {entry.Code} {entry.Quantity}", ct);
                }
                else throw new InvalidDataException("Unknown fulfillment type.");

                await shop.CompleteAsync(order, ct);
                Log($"Shop order {order.Code}: {order.Name} delivered to {message.PlayerName}/{message.SteamId} for {order.Price}$.");
                await Reply($"Bestellung '{order.Name}' ausgeliefert. {order.Price}$ wurden abgebucht.", $"Order '{order.Name}' delivered. {order.Price}$ was charged.");
            }
            catch (Exception ex)
            {
                try { await shop.ReviewAsync(order, "Debit succeeded; delivery outcome uncertain: " + ex.Message, ct); } catch { }
                AppLogService.WriteException("ShopOrder.Delivery", ex);
                await Reply("Bezahlt, aber Ausgabe muss durch einen Admin geprüft werden. Keine automatische Wiederholung.", "Paid, but delivery requires admin review. No automatic retry will occur.");
            }
            return true;
        }
        catch (Exception ex)
        {
            AppLogService.WriteException("ShopOrder", ex);
            await Reply("Bestellung konnte gerade nicht geprüft werden. Bitte später erneut versuchen.", "The order could not be checked right now. Please try again later.");
            return true;
        }
        finally { _shopOrderGate.Release(); }
    }

    private void AddRequiredChatCommandEntries()
    {
        foreach (var definition in BuiltinChatCommandCatalog.All)
            ChatCommandRules.Add(ChatCommandRuleEditorViewModel.FromBuiltin(definition, () => Texts.IsGerman));
    }
}
