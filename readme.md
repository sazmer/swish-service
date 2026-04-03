# swish-service

Minimal Linux-friendly Swish gateway.

## Endpoints

- GET /health
- POST /swish/test

## Required environment variables

- SWISH_GATEWAY_API_KEY
- SWISH_PFX_BASE64
- SWISH_PFX_PASSWORD
- SWISH_ROOT_CA_PEM_BASE64
- SWISH_PAYEE_ALIAS
- SWISH_CALLBACK_URL
- SWISH_BASE_URL

## Local settings

Create `appsettings.Local.json` next to `Program.cs`. The file is ignored by git.

Example:

```json
{
  "Swish": {
    "ApiKey": "local-dev-key",
    "BaseUrl": "https://mss.cpc.getswish.net/swish-cpcapi",
    "PayeeAlias": "1234679304",
    "CallbackUrl": "https://www.beachgears.se/api/swish/callback",
    "PfxPath": "/Users/samuelhenriksson/swish-service/certs/Swish_Merchant_TestCertificate_1234679304.p12",
    "PfxPassword": "swish",
    "RootCaPath": "/Users/samuelhenriksson/swish-service/certs/Swish_TLS_RootCA.pem"
  }
}
```
