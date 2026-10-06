using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;

namespace Tenantry.AspNetCore.Tests;

/// <summary>
/// A SignalR hub behind <c>UseTenantry()</c>, called over the hub's JSON protocol on the test server: which tenant a
/// hub method runs as, and how <c>hubOptions.AddTenantry()</c> refuses a tenant that is no longer active.
/// </summary>
public sealed class SignalRTests
{
    private const string RecordSeparator = "\u001e";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OverWebSockets_AHubMethodRunsAsTheTenantOfTheRequestThatOpenedTheConnection()
    {
        await using var app = await StartAsync(new Tenants(), filter: false);

        // Negotiation is a request of its own: the tenant it names is not the connection's.
        var token = await NegotiateAsync(app, "globex");
        await using var connection = await WebSocketConnection.OpenAsync(app, $"/hub?id={token}&tenant=acme");

        (await connection.InvokeAsync("Tenant")).GetString().Should().Be("acme");
    }

    [Fact]
    public async Task OverLongPolling_AHubMethodRunsAsTheTenantOfTheFirstPoll_AfterThatRequestHasEnded()
    {
        await using var app = await StartAsync(new Tenants(), filter: false);
        var token = await NegotiateAsync(app, "acme");
        using var client = app.GetTestClient();

        // The first poll starts the connection and returns at once. The sends and polls after it name another tenant.
        using (var firstPoll = await client.GetAsync($"/hub?id={token}&tenant=acme", Ct))
        {
            firstPoll.EnsureSuccessStatusCode();
        }

        await SendAsync(client, $"/hub?id={token}&tenant=globex", Handshake + Invocation("1", "Tenant"));

        var result = await PollForCompletionAsync(client, $"/hub?id={token}&tenant=globex", "1");

        result.GetProperty("result").GetString().Should().Be("acme");
    }

    [Fact]
    public async Task ARouteValueInTheHubsPattern_NamesTheConnectionsTenant()
    {
        await using var app = await StartAsync(new Tenants(), filter: true, route: true);
        await using var connection = await WebSocketConnection.OpenAsync(app, "/acme/hub");

        (await connection.InvokeAsync("Tenant")).GetString().Should().Be("acme");
    }

    [Fact]
    public async Task AHubMethodRunsWithoutATenant_WhenTheConnectionNamedNone()
    {
        await using var app = await StartAsync(new Tenants(), filter: true);
        await using var connection = await WebSocketConnection.OpenAsync(app, "/hub");

        (await connection.InvokeAsync("Tenant")).ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task TheFilter_LetsAnActiveTenantsCallsRun()
    {
        await using var app = await StartAsync(new Tenants(), filter: true);
        await using var connection = await WebSocketConnection.OpenAsync(app, "/hub?tenant=acme");

        (await connection.InvokeAsync("Tenant")).GetString().Should().Be("acme");
        (await connection.InvokeAsync("Tenant")).GetString().Should().Be("acme");
    }

    [Fact]
    public async Task TheFilter_RefusesTheNextCall_OnceTheTenantIsSuspended()
    {
        Tenants tenants = new();
        await using var app = await StartAsync(tenants, filter: true);
        await using var connection = await WebSocketConnection.OpenAsync(app, "/hub?tenant=acme");
        (await connection.InvokeAsync("Tenant")).GetString().Should().Be("acme");

        tenants.Suspend("acme");

        var error = await connection.InvokeForErrorAsync("Tenant");
        error.Should().Contain(nameof(TenantInactiveException)).And.Contain("'acme' is not active");
        tenants.CallsRun.Should().Be(1, "the refused call does not run");
    }

    [Fact]
    public async Task TheFilter_RefusesTheNextCall_OnceTheTenantIsDeleted()
    {
        Tenants tenants = new();
        await using var app = await StartAsync(tenants, filter: true);
        await using var connection = await WebSocketConnection.OpenAsync(app, "/hub?tenant=acme");
        (await connection.InvokeAsync("Tenant")).GetString().Should().Be("acme");

        tenants.Delete("acme");

        var error = await connection.InvokeForErrorAsync("Tenant");
        error.Should().Contain(nameof(TenantNotFoundException)).And.Contain("'acme' was not found");
        tenants.CallsRun.Should().Be(1);
    }

    [Fact]
    public async Task TheFilter_OnOneHubsOptions_RefusesThatHubsCalls()
    {
        Tenants tenants = new();
        await using var app = await StartAsync(tenants, filter: true, oneHub: true);
        await using var connection = await WebSocketConnection.OpenAsync(app, "/hub?tenant=acme");

        tenants.Suspend("acme");

        (await connection.InvokeForErrorAsync("Tenant")).Should().Contain(nameof(TenantInactiveException));
        tenants.CallsRun.Should().Be(0);
    }

    [Fact]
    public async Task TheFilter_ReadsTheTenantThroughTheCache_SoAnInvalidationReachesAnOpenConnection()
    {
        Tenants tenants = new();
        await using var app = await StartAsync(tenants, filter: true, cache: true);
        await using var connection = await WebSocketConnection.OpenAsync(app, "/hub?tenant=acme");
        (await connection.InvokeAsync("Tenant")).GetString().Should().Be("acme");
        tenants.Suspend("acme");

        (await connection.InvokeAsync("Tenant")).GetString().Should().Be("acme", "the cached tenant is still active");

        await app.Services.GetRequiredService<ITenantInvalidator<string>>().InvalidateAsync("acme", Ct);

        (await connection.InvokeForErrorAsync("Tenant")).Should().Contain(nameof(TenantInactiveException));
    }

    [Fact]
    public async Task TheFilter_WithoutAddTenantry_FailsTheCall_AndSaysWhy()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = true;
            options.AddTenantry();
        });

        await using var app = builder.Build();
        app.MapHub<PlainHub>("/hub");
        await app.StartAsync(Ct);
        await using var connection = await WebSocketConnection.OpenAsync(app, "/hub");

        (await connection.InvokeForErrorAsync("Ping")).Should().Contain("Tenantry is not registered");
    }

    private static async Task<WebApplication> StartAsync(
        Tenants tenants,
        bool filter,
        bool cache = false,
        bool route = false,
        bool oneHub = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(tenants);
        builder.Services.AddTenantry<string>(tenant =>
        {
            if (route)
            {
                tenant.ResolveFromRouteValue();
            }
            else
            {
                tenant.ResolveFromQueryString("tenant");
            }

            tenant.UseStore(_ => tenants)
                .ValidateTenantActivity(t => !t.As<AppTenant>().Suspended);

            if (cache)
            {
                tenant.CacheTenants();
            }
        });
        var signalR = builder.Services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = true;

            if (filter && !oneHub)
            {
                options.AddTenantry();
            }
        });

        if (filter && oneHub)
        {
            signalR.AddHubOptions<TenantHub>(options => options.AddTenantry());
        }

        var app = builder.Build();
        app.UseTenantry();
        app.MapHub<TenantHub>(route ? "/{tenant}/hub" : "/hub");
        await app.StartAsync(Ct);
        return app;
    }

    private static async Task<string> NegotiateAsync(WebApplication app, string tenant)
    {
        using var client = app.GetTestClient();
        using var response = await client.PostAsync($"/hub/negotiate?negotiateVersion=1&tenant={tenant}", null, Ct);
        response.EnsureSuccessStatusCode();
        using var negotiation = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return negotiation.RootElement.GetProperty("connectionToken").GetString()!;
    }

    private static string Handshake => """{"protocol":"json","version":1}""" + RecordSeparator;

    private static string Invocation(string id, string target) =>
        $$"""{"type":1,"invocationId":"{{id}}","target":"{{target}}","arguments":[]}""" + RecordSeparator;

    private static async Task SendAsync(HttpClient client, string path, string messages)
    {
        using var content = new StringContent(messages, Encoding.UTF8);
        using var response = await client.PostAsync(path, content, Ct);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<JsonElement> PollForCompletionAsync(HttpClient client, string path, string invocationId)
    {
        while (true)
        {
            var body = await client.GetStringAsync(path, Ct);

            foreach (var message in body.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var element = JsonDocument.Parse(message).RootElement;

                if (element.TryGetProperty("invocationId", out var id) && id.GetString() == invocationId)
                {
                    return element;
                }
            }
        }
    }

    public sealed class TenantHub(ITenantContext<string> tenant, Tenants tenants) : Hub
    {
        public string? Tenant()
        {
            tenants.CallRun();
            return tenant.CurrentTenantId;
        }
    }

    public sealed class PlainHub : Hub
    {
        public string Ping() => Context.ConnectionId;
    }

    /// <summary>A tenant that can be suspended.</summary>
    public sealed class AppTenant : TenantDescriptor<string>
    {
        public bool Suspended { get; init; }
    }

    /// <summary>The tenant store, which suspends and deletes tenants while a connection is open.</summary>
    public sealed class Tenants : ITenantStore<string>
    {
        private readonly ConcurrentDictionary<string, AppTenant> _tenants = new()
        {
            ["acme"] = new AppTenant { TenantId = "acme", Name = "Acme" },
            ["globex"] = new AppTenant { TenantId = "globex", Name = "Globex" },
        };

        private int _callsRun;

        public int CallsRun => Volatile.Read(ref _callsRun);

        public void CallRun() => Interlocked.Increment(ref _callsRun);

        public void Suspend(string tenantId) =>
            _tenants[tenantId] = new AppTenant { TenantId = tenantId, Name = _tenants[tenantId].Name, Suspended = true };

        public void Delete(string tenantId) => _tenants.TryRemove(tenantId, out _);

        public ValueTask<ITenantDescriptor<string>?> GetTenantAsync(string tenantId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ITenantDescriptor<string>?>(_tenants.GetValueOrDefault(tenantId));

        public ValueTask<IReadOnlyList<ITenantDescriptor<string>>> GetAllTenantsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ITenantDescriptor<string>>>([.. _tenants.Values]);
    }

    /// <summary>
    /// A hub connection over a WebSocket, opened with the token of a negotiation (<c>id</c>) or, as a client that skips
    /// negotiation does, without one.
    /// </summary>
    private sealed class WebSocketConnection : IAsyncDisposable
    {
        private readonly WebSocket _socket;
        private readonly Queue<string> _received = new();
        private int _nextId;

        private WebSocketConnection(WebSocket socket) => _socket = socket;

        public static async Task<WebSocketConnection> OpenAsync(WebApplication app, string path)
        {
            var socket = await app.GetTestServer().CreateWebSocketClient()
                .ConnectAsync(new Uri($"ws://localhost{path}"), Ct);
            WebSocketConnection connection = new(socket);
            await connection.SendAsync(Handshake);
            (await connection.ReceiveAsync()).Should().Be("{}", "the server accepts the handshake");
            return connection;
        }

        public async Task<JsonElement> InvokeAsync(string target)
        {
            var completion = await CallAsync(target);
            completion.TryGetProperty("error", out var error).Should().BeFalse(error.ToString());
            return completion.TryGetProperty("result", out var result) ? result : default;
        }

        public async Task<string> InvokeForErrorAsync(string target)
        {
            var completion = await CallAsync(target);
            completion.TryGetProperty("error", out var error).Should().BeTrue("the call is refused");
            return error.GetString()!;
        }

        public async ValueTask DisposeAsync()
        {
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, Ct);
            _socket.Dispose();
        }

        private async Task<JsonElement> CallAsync(string target)
        {
            var id = (++_nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await SendAsync(Invocation(id, target));

            while (true)
            {
                var element = JsonDocument.Parse(await ReceiveAsync()).RootElement;

                // Pings (type 6) are skipped.
                if (element.GetProperty("type").GetInt32() == 3 && element.GetProperty("invocationId").GetString() == id)
                {
                    return element;
                }
            }
        }

        private Task SendAsync(string messages) =>
            _socket.SendAsync(Encoding.UTF8.GetBytes(messages), WebSocketMessageType.Text, endOfMessage: true, Ct);

        private async Task<string> ReceiveAsync()
        {
            while (_received.Count == 0)
            {
                var buffer = new byte[4096];
                using MemoryStream frame = new();
                WebSocketReceiveResult result;

                do
                {
                    result = await _socket.ReceiveAsync(buffer, Ct);
                    frame.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                foreach (var message in Encoding.UTF8.GetString(frame.ToArray())
                             .Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    _received.Enqueue(message);
                }
            }

            return _received.Dequeue();
        }
    }
}
