using System.Net;
using System.Text.Json;
using AMSpeedUp.Core.Configuration;

namespace AMSpeedUp.Core.Licensing;

/// <summary>
/// Lemon Squeezy's License API. It's designed to be called from apps: it needs only the customer's
/// key, never a secret API key. Every successful answer is checked against this store, product and
/// variant, so a key sold for anything else doesn't count.
/// </summary>
public sealed class LemonSqueezyLicenceService(HttpClient http, LemonSqueezySettings settings) : ILicenceService
{
    public static readonly Uri BaseAddress = new("https://api.lemonsqueezy.com/v1/licenses/");

    public static HttpClient CreateHttpClient(HttpMessageHandler? handler = null)
    {
        var client = handler is null ? new HttpClient() : new HttpClient(handler);
        client.BaseAddress = BaseAddress;
        client.Timeout = TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"AMSpeedUp/{AppInfo.Version}");
        return client;
    }

    public async Task<LicenceResponse> ActivateAsync(string key, string instanceName, CancellationToken cancellationToken = default)
    {
        var response = await PostAsync("activate", [new("license_key", key), new("instance_name", instanceName)], "activated", cancellationToken).ConfigureAwait(false);

        // A genuine key for someone else's product: give its activation slot straight back.
        if (response.Outcome == LicenceOutcome.WrongProduct && response.Info?.InstanceId is { } instance)
        {
            await DeactivateAsync(key, instance, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    public Task<LicenceResponse> ValidateAsync(string key, string instanceId, CancellationToken cancellationToken = default) =>
        PostAsync("validate", [new("license_key", key), new("instance_id", instanceId)], "valid", cancellationToken);

    public Task<LicenceResponse> DeactivateAsync(string key, string instanceId, CancellationToken cancellationToken = default) =>
        PostAsync("deactivate", [new("license_key", key), new("instance_id", instanceId)], "deactivated", cancellationToken);

    private async Task<LicenceResponse> PostAsync(string endpoint, KeyValuePair<string, string>[] form, string successField, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        string body;
        try
        {
            using var content = new FormUrlEncodedContent(form);
            response = await http.PostAsync(endpoint, content, cancellationToken).ConfigureAwait(false);
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new LicenceResponse(LicenceOutcome.NoAnswer, ex.Message);
        }

        using (response)
        {
            // Busy, down or rate limited: that's not an answer about the key.
            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            {
                return new LicenceResponse(LicenceOutcome.NoAnswer, $"The licence server answered {(int)response.StatusCode}.");
            }

            return Interpret(response.StatusCode, body, successField, settings);
        }
    }

    /// <summary>Turns Lemon Squeezy's answer into an outcome. Public so it can be tested with sample answers.</summary>
    public static LicenceResponse Interpret(HttpStatusCode status, string body, string successField, LemonSqueezySettings settings)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return new LicenceResponse(LicenceOutcome.NoAnswer, $"The licence server sent something unreadable ({(int)status}).");
        }

        using (document)
        {
            var root = document.RootElement;
            var error = Text(root, "error");
            var succeeded = root.TryGetProperty(successField, out var flag) && flag.ValueKind == JsonValueKind.True;
            var keyStatus = root.TryGetProperty("license_key", out var licenceKey) ? Text(licenceKey, "status") : null;
            var info = ReadInfo(root);

            if (succeeded)
            {
                // Deactivating doesn't need the product check: it only ever gives a slot back.
                if (successField != "deactivated" && !BelongsToThisProduct(root, settings))
                {
                    return new LicenceResponse(LicenceOutcome.WrongProduct, error, info);
                }

                return keyStatus switch
                {
                    "expired" => new LicenceResponse(LicenceOutcome.Expired, error, info),
                    "disabled" => new LicenceResponse(LicenceOutcome.Disabled, error, info),
                    _ => new LicenceResponse(LicenceOutcome.Valid, error, info),
                };
            }

            if (keyStatus == "expired")
            {
                return new LicenceResponse(LicenceOutcome.Expired, error, info);
            }

            if (keyStatus == "disabled")
            {
                return new LicenceResponse(LicenceOutcome.Disabled, error, info);
            }

            if (error is not null && error.Contains("activation limit", StringComparison.OrdinalIgnoreCase))
            {
                return new LicenceResponse(LicenceOutcome.LimitReached, error, info);
            }

            if (status is HttpStatusCode.NotFound or HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity
                || (status == HttpStatusCode.OK && root.TryGetProperty(successField, out _)))
            {
                return new LicenceResponse(LicenceOutcome.NotFound, error, info);
            }

            return new LicenceResponse(LicenceOutcome.NoAnswer, error ?? $"Unexpected answer ({(int)status}).");
        }
    }

    private static bool BelongsToThisProduct(JsonElement root, LemonSqueezySettings settings)
    {
        if (!root.TryGetProperty("meta", out var meta))
        {
            return false;
        }

        return Number(meta, "store_id") == settings.StoreId
               && Number(meta, "product_id") == settings.ProductId
               && (settings.VariantId is not { } variant || Number(meta, "variant_id") == variant);
    }

    private static LicenceInfo ReadInfo(JsonElement root)
    {
        root.TryGetProperty("license_key", out var key);
        root.TryGetProperty("instance", out var instance);
        root.TryGetProperty("meta", out var meta);

        return new LicenceInfo
        {
            InstanceId = instance.ValueKind == JsonValueKind.Object ? Text(instance, "id") : null,
            ActivationLimit = key.ValueKind == JsonValueKind.Object ? (int?)Number(key, "activation_limit") : null,
            ActivationUsage = key.ValueKind == JsonValueKind.Object ? (int?)Number(key, "activation_usage") : null,
            ExpiresAt = key.ValueKind == JsonValueKind.Object && DateTimeOffset.TryParse(Text(key, "expires_at"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var expires) ? expires : null,
            CustomerName = meta.ValueKind == JsonValueKind.Object ? Text(meta, "customer_name") : null,
        };
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n) ? n : null;
}
