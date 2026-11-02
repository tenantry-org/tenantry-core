# SecureApi

This project builds the application of the `tenantry-api` template against this repository's projects, so that the
integration tests ([`SecureApiTests.cs`](../../tests/Tenantry.IntegrationTests/SecureApiTests.cs)) and the samples'
smoke run exercise the code the template generates. It has no code of its own: its source files are the template's,
in [`templates/content/tenantry-api`](../../templates/content/tenantry-api).

To start an application of your own from it:

```bash
dotnet new install Tenantry.Templates
dotnet new tenantry-api -n Orders.Api
```

`dotnet run` here starts it in Development on port 5160. The comment at the top of the template's
[`Program.cs`](../../templates/content/tenantry-api/Program.cs) has the `curl` calls to try.
