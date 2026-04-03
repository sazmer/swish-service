using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddSingleton<SwishHttpClientFactory>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    ok = true,
    service = "bg-swish-gateway",
    timestampUtc = DateTime.UtcNow
}));

app.MapPost("/swish/test", async Task<IResult> (
    HttpContext httpContext,
    SwishHttpClientFactory factory,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    var options = configuration.GetSection("Swish").Get<SwishGatewayOptions>() ?? new SwishGatewayOptions();

    if (!string.IsNullOrWhiteSpace(options.ApiKey))
    {
        var providedApiKey = httpContext.Request.Headers["X-Api-Key"].ToString();
        if (!string.Equals(providedApiKey, options.ApiKey, StringComparison.Ordinal))
            return TypedResults.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(options.PfxBase64) && string.IsNullOrWhiteSpace(options.PfxPath))
        return TypedResults.BadRequest("Missing Swish PFX configuration.");

    if (string.IsNullOrWhiteSpace(options.PfxPassword))
        return TypedResults.BadRequest("Missing SWISH_PFX_PASSWORD.");

    if (string.IsNullOrWhiteSpace(options.RootCaPemBase64) && string.IsNullOrWhiteSpace(options.RootCaPath))
        return TypedResults.BadRequest("Missing Swish root CA configuration.");

    using var client = factory.CreateClient(options);
    var instructionId = Guid.NewGuid().ToString("N").ToUpperInvariant();
    var endpoint = $"api/v2/paymentrequests/{instructionId}";
    var body = """
               {
                 "payeePaymentReference": "123456789012345678901234567890",
                 "callbackUrl": "__CALLBACK_URL__",
                 "payerAlias": "46702064695",
                 "payeeAlias": "__PAYEE_ALIAS__",
                 "amount": "1.00",
                 "currency": "SEK",
                 "message": "LINUX-CONTAINER-TEST"
               }
               """
        .Replace("__CALLBACK_URL__", options.CallbackUrl)
        .Replace("__PAYEE_ALIAS__", options.PayeeAlias);

    using var request = new HttpRequestMessage(HttpMethod.Put, endpoint)
    {
        Content = new StringContent(body)
    };
    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

    using var response = await client.SendAsync(request, cancellationToken);

    return TypedResults.Ok(new
    {
        statusCode = (int)response.StatusCode,
        reasonPhrase = response.ReasonPhrase,
        location = response.Headers.Location?.ToString(),
        responseBody = await response.Content.ReadAsStringAsync(cancellationToken)
    });
});

app.Run();

internal sealed class SwishHttpClientFactory
{
    public HttpClient CreateClient(SwishGatewayOptions options)
    {
        var pfxBytes = LoadPfxBytes(options);
        var rootCaPem = LoadRootCaPem(options);

        var certificate = X509CertificateLoader.LoadPkcs12(
            pfxBytes,
            options.PfxPassword,
            X509KeyStorageFlags.DefaultKeySet | X509KeyStorageFlags.Exportable);

        var rootCertificate = X509Certificate2.CreateFromPem(rootCaPem);

        var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(certificate);
        handler.ServerCertificateCustomValidationCallback = (_, serverCertificate, _, _) =>
        {
            if (serverCertificate is null)
                return false;

            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(rootCertificate);

            return chain.Build(new X509Certificate2(serverCertificate));
        };

        return new HttpClient(handler)
        {
            BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/")
        };
    }

    private static byte[] LoadPfxBytes(SwishGatewayOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.PfxBase64))
            return Convert.FromBase64String(options.PfxBase64);

        if (!string.IsNullOrWhiteSpace(options.PfxPath))
            return File.ReadAllBytes(options.PfxPath);

        throw new InvalidOperationException("Missing Swish PFX configuration.");
    }

    private static string LoadRootCaPem(SwishGatewayOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.RootCaPemBase64))
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(options.RootCaPemBase64));

        if (!string.IsNullOrWhiteSpace(options.RootCaPath))
            return File.ReadAllText(options.RootCaPath);

        throw new InvalidOperationException("Missing Swish root CA configuration.");
    }
}

internal sealed class SwishGatewayOptions
{
    public string? ApiKey { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string PayeeAlias { get; set; } = string.Empty;
    public string CallbackUrl { get; set; } = string.Empty;
    public string? PfxBase64 { get; set; }
    public string? PfxPath { get; set; }
    public string? PfxPassword { get; set; }
    public string? RootCaPemBase64 { get; set; }
    public string? RootCaPath { get; set; }
}
