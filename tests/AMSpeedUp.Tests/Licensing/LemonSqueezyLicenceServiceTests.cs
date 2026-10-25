using System.Net;
using System.Text;
using AMSpeedUp.Core.Configuration;
using AMSpeedUp.Core.Licensing;

namespace AMSpeedUp.Tests.Licensing;

public class LemonSqueezyLicenceServiceTests
{
    private static readonly LemonSqueezySettings Settings = new() { StoreId = 506037, ProductId = 1471539, VariantId = 2294639 };

    private const string Meta = """
        "meta": { "store_id": 506037, "order_id": 2, "order_item_id": 3, "product_id": 1471539, "product_name": "AM Speed Up Pro",
                  "variant_id": 2294639, "variant_name": "Pro", "customer_id": 5, "customer_name": "Jo Bloggs", "customer_email": "jo@example.com" }
        """;

    private static string Key(string status = "active", int limit = 3, int usage = 1, string expires = "null") => $$"""
        "license_key": { "id": 1, "status": "{{status}}", "key": "38B1460A-5104-4067-A91D-77B872934D51", "activation_limit": {{limit}},
                         "activation_usage": {{usage}}, "created_at": "2026-10-25T10:00:00.000000Z", "expires_at": {{expires}} }
        """;

    private const string Instance = """
        "instance": { "id": "47596ad9-a811-4ebf-ac8a-03fc7b6d2a17", "name": "AM Speed Up PC 1A2B3C4D", "created_at": "2026-10-25T10:00:00.000000Z" }
        """;

    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri Uri, string Body, string? Accept, string? ContentType)> Requests { get; } = [];

        public Exception? Throws { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!, await request.Content!.ReadAsStringAsync(cancellationToken),
                request.Headers.Accept.ToString(), request.Content.Headers.ContentType?.MediaType));
            if (Throws is not null)
            {
                throw Throws;
            }

            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static (LemonSqueezyLicenceService Service, FakeHandler Handler) Create(HttpStatusCode status, string body, Exception? throws = null)
    {
        var handler = new FakeHandler(status, body) { Throws = throws };
        return (new LemonSqueezyLicenceService(LemonSqueezyLicenceService.CreateHttpClient(handler), Settings), handler);
    }

    [Fact]
    public async Task Activation_posts_the_key_and_a_PC_label_as_a_form_with_no_API_key()
    {
        var (service, handler) = Create(HttpStatusCode.OK, $$"""{ "activated": true, "error": null, {{Key()}}, {{Instance}}, {{Meta}} }""");

        await service.ActivateAsync("38B1460A-5104-4067-A91D-77B872934D51", "AM Speed Up PC 1A2B3C4D");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.lemonsqueezy.com/v1/licenses/activate", request.Uri.ToString());
        Assert.Equal("license_key=38B1460A-5104-4067-A91D-77B872934D51&instance_name=AM+Speed+Up+PC+1A2B3C4D", request.Body);
        Assert.Equal("application/json", request.Accept);
        Assert.Equal("application/x-www-form-urlencoded", request.ContentType);
    }

    [Fact]
    public async Task A_good_activation_returns_the_instance_and_how_many_PCs_it_is_on()
    {
        var (service, _) = Create(HttpStatusCode.OK, $$"""{ "activated": true, "error": null, {{Key(usage: 2)}}, {{Instance}}, {{Meta}} }""");

        var response = await service.ActivateAsync("key", "pc");

        Assert.Equal(LicenceOutcome.Valid, response.Outcome);
        Assert.Equal("47596ad9-a811-4ebf-ac8a-03fc7b6d2a17", response.Info!.InstanceId);
        Assert.Equal(3, response.Info.ActivationLimit);
        Assert.Equal(2, response.Info.ActivationUsage);
        Assert.Equal("Jo Bloggs", response.Info.CustomerName);
        Assert.Null(response.Info.ExpiresAt);
    }

    [Fact]
    public async Task Validation_sends_the_key_and_this_PCs_instance()
    {
        var (service, handler) = Create(HttpStatusCode.OK, $$"""{ "valid": true, "error": null, {{Key()}}, {{Instance}}, {{Meta}} }""");

        var response = await service.ValidateAsync("KEY", "instance-1");

        Assert.Equal(LicenceOutcome.Valid, response.Outcome);
        Assert.EndsWith("/licenses/validate", handler.Requests[0].Uri.ToString());
        Assert.Equal("license_key=KEY&instance_id=instance-1", handler.Requests[0].Body);
    }

    [Theory]
    [InlineData(999999, 1471539, 2294639)]
    [InlineData(506037, 1, 2294639)]
    [InlineData(506037, 1471539, 1)]
    public void A_genuine_key_for_a_different_store_product_or_variant_does_not_count(long store, long product, long variant)
    {
        var meta = Meta.Replace("506037", store.ToString()).Replace("\"product_id\": 1471539", $"\"product_id\": {product}").Replace("2294639", variant.ToString());

        var response = LemonSqueezyLicenceService.Interpret(HttpStatusCode.OK, $$"""{ "valid": true, "error": null, {{Key()}}, {{Instance}}, {{meta}} }""", "valid", Settings);

        Assert.Equal(LicenceOutcome.WrongProduct, response.Outcome);
    }

    [Fact]
    public void A_good_answer_with_no_product_details_does_not_count()
    {
        var response = LemonSqueezyLicenceService.Interpret(HttpStatusCode.OK, $$"""{ "valid": true, "error": null, {{Key()}}, {{Instance}}, "meta": null }""", "valid", Settings);

        Assert.Equal(LicenceOutcome.WrongProduct, response.Outcome);
    }

    [Fact]
    public void The_variant_is_not_checked_when_none_is_configured()
    {
        var anyVariant = Settings with { VariantId = null };
        var meta = Meta.Replace("2294639", "123");

        var response = LemonSqueezyLicenceService.Interpret(HttpStatusCode.OK, $$"""{ "valid": true, "error": null, {{Key()}}, {{Instance}}, {{meta}} }""", "valid", anyVariant);

        Assert.Equal(LicenceOutcome.Valid, response.Outcome);
    }

    [Fact]
    public async Task Activating_a_key_for_another_product_gives_the_slot_straight_back()
    {
        var meta = Meta.Replace("\"product_id\": 1471539", "\"product_id\": 42");
        var (service, handler) = Create(HttpStatusCode.OK, $$"""{ "activated": true, "deactivated": true, "error": null, {{Key()}}, {{Instance}}, {{meta}} }""");

        var response = await service.ActivateAsync("key", "pc");

        Assert.Equal(LicenceOutcome.WrongProduct, response.Outcome);
        Assert.Equal(2, handler.Requests.Count);
        Assert.EndsWith("/licenses/deactivate", handler.Requests[1].Uri.ToString());
        Assert.Contains("instance_id=47596ad9-a811-4ebf-ac8a-03fc7b6d2a17", handler.Requests[1].Body);
    }

    [Fact]
    public void An_unknown_key_is_not_found()
    {
        var response = LemonSqueezyLicenceService.Interpret(HttpStatusCode.NotFound,
            """{ "valid": false, "error": "license_key not found.", "license_key": null, "instance": null, "meta": null }""", "valid", Settings);

        Assert.Equal(LicenceOutcome.NotFound, response.Outcome);
        Assert.Equal("license_key not found.", response.ServerMessage);
    }

    [Fact]
    public void A_removed_instance_is_not_found()
    {
        var response = LemonSqueezyLicenceService.Interpret(HttpStatusCode.OK,
            $$"""{ "valid": false, "error": "license_key instance not found.", {{Key()}}, "instance": null, {{Meta}} }""", "valid", Settings);

        Assert.Equal(LicenceOutcome.NotFound, response.Outcome);
    }

    [Theory]
    [InlineData("expired", LicenceOutcome.Expired)]
    [InlineData("disabled", LicenceOutcome.Disabled)]
    public void Expired_and_switched_off_keys_are_recognised(string status, LicenceOutcome expected)
    {
        var invalid = LemonSqueezyLicenceService.Interpret(HttpStatusCode.OK,
            $$"""{ "valid": false, "error": "This license key is {{status}}.", {{Key(status)}}, {{Instance}}, {{Meta}} }""", "valid", Settings);
        var oddlyValid = LemonSqueezyLicenceService.Interpret(HttpStatusCode.OK,
            $$"""{ "valid": true, "error": null, {{Key(status)}}, {{Instance}}, {{Meta}} }""", "valid", Settings);

        Assert.Equal(expected, invalid.Outcome);
        Assert.Equal(expected, oddlyValid.Outcome);
    }

    [Fact]
    public void Reaching_the_PC_limit_is_recognised()
    {
        var response = LemonSqueezyLicenceService.Interpret(HttpStatusCode.BadRequest,
            $$"""{ "activated": false, "error": "This license key has reached the activation limit.", {{Key(limit: 3, usage: 3)}}, {{Meta}} }""", "activated", Settings);

        Assert.Equal(LicenceOutcome.LimitReached, response.Outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task A_busy_or_broken_server_is_no_answer_not_a_bad_key(HttpStatusCode status)
    {
        // Even if the error page happens to carry a "disabled" status, it mustn't count against the key.
        var (service, _) = Create(status, """{ "valid": false, "error": "Server error", "license_key": { "status": "disabled" } }""");

        Assert.Equal(LicenceOutcome.NoAnswer, (await service.ValidateAsync("key", "instance")).Outcome);
    }

    [Fact]
    public async Task No_internet_is_no_answer()
    {
        var (service, _) = Create(HttpStatusCode.OK, "", new HttpRequestException("No such host is known."));

        var response = await service.ValidateAsync("key", "instance");

        Assert.Equal(LicenceOutcome.NoAnswer, response.Outcome);
        Assert.False(response.GotAnAnswer);
    }

    [Fact]
    public async Task A_timeout_is_no_answer()
    {
        var (service, _) = Create(HttpStatusCode.OK, "", new TaskCanceledException("timed out"));

        Assert.Equal(LicenceOutcome.NoAnswer, (await service.ValidateAsync("key", "instance")).Outcome);
    }

    [Fact]
    public void An_unreadable_answer_is_no_answer()
    {
        Assert.Equal(LicenceOutcome.NoAnswer, LemonSqueezyLicenceService.Interpret(HttpStatusCode.OK, "<html>Gateway</html>", "valid", Settings).Outcome);
    }

    [Fact]
    public async Task Deactivation_succeeds_without_a_product_check()
    {
        var (service, handler) = Create(HttpStatusCode.OK, $$"""{ "deactivated": true, "error": null, {{Key()}}, "meta": null }""");

        var response = await service.DeactivateAsync("key", "instance-1");

        Assert.Equal(LicenceOutcome.Valid, response.Outcome);
        Assert.Equal("license_key=key&instance_id=instance-1", handler.Requests[0].Body);
    }

    [Fact]
    public void Expiry_dates_are_read()
    {
        var response = LemonSqueezyLicenceService.Interpret(HttpStatusCode.OK,
            $$"""{ "valid": true, "error": null, {{Key(expires: "\"2027-10-25T10:00:00.000000Z\"")}}, {{Instance}}, {{Meta}} }""", "valid", Settings);

        Assert.Equal(new DateTimeOffset(2027, 10, 25, 10, 0, 0, TimeSpan.Zero), response.Info!.ExpiresAt);
    }

    [Fact]
    public void The_shipped_settings_point_at_the_AM_Software_store()
    {
        var settings = AppSettings.LoadEmbedded();

        Assert.Equal(506037, settings.LemonSqueezy!.StoreId);
        Assert.Equal(1471539, settings.LemonSqueezy.ProductId);
        Assert.Equal(2294639, settings.LemonSqueezy.VariantId);
        Assert.True(settings.LemonSqueezy.IsConfigured);
        Assert.True(settings.HasCheckoutUrl);
        Assert.StartsWith("https://am-software.lemonsqueezy.com/", settings.CheckoutUrl);
    }
}
