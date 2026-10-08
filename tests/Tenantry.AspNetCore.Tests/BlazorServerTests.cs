using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.AspNetCore.TestHost;
using Tenantry.AspNetCore.Internal;

namespace Tenantry.AspNetCore.Tests;

/// <summary>
/// The circuit handler <c>AddInteractiveServerComponents().AddTenantry()</c> adds: in a circuit opened over Blazor
/// Server's hub, with <c>hubOptions.AddTenantry()</c> on every hub as well, and called directly with the tenant current,
/// as Blazor calls it for a circuit's inbound activity.
/// </summary>
public sealed partial class BlazorServerTests
{
    private static readonly TenantDescriptor<string> Acme = new() { TenantId = "acme", Name = "Acme" };

    private readonly SignalRTests.Tenants _tenants = new();
    private int _runs;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // A circuit that stops responding fails the test at the time limit.
    [Fact(Timeout = 30_000)]
    public async Task ACircuit_EndsAtItsNextActivity_OnceItsTenantIsSuspended()
    {
        await using var app = await StartAsync();
        await using var circuit = await CircuitConnection.OpenAsync(app, "acme");

        await circuit.SendActivityAsync();
        await circuit.ReceiveAsync("JS.EndInvokeDotNet");

        _tenants.Suspend("acme");
        await circuit.SendActivityAsync();

        // The hub filter lets Blazor's own hub through, so the circuit handler refuses the activity and the circuit ends.
        (await circuit.ReceiveErrorAsync()).Should().Contain("this circuit will be terminated");
        await app.Services.GetRequiredService<ClosedCircuits>().First.WaitAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AnActiveTenantsActivityRuns()
    {
        await using var provider = Build();
        using var circuit = provider.CreateScope();

        using (Current(provider))
        {
            await Activity(circuit)(null!);
        }

        _runs.Should().Be(1);
    }

    [Fact]
    public async Task ASuspendedTenantsActivity_IsRefused()
    {
        await using var provider = Build();
        using var circuit = provider.CreateScope();
        _tenants.Suspend("acme");

        using (Current(provider))
        {
            await Activity(circuit).Awaiting(run => run(null!)).Should().ThrowAsync<TenantInactiveException>();
        }

        _runs.Should().Be(0);
    }

    [Fact]
    public async Task ADeletedTenantsActivity_IsRefused()
    {
        await using var provider = Build();
        using var circuit = provider.CreateScope();
        _tenants.Delete("acme");

        using (Current(provider))
        {
            await Activity(circuit).Awaiting(run => run(null!)).Should().ThrowAsync<TenantNotFoundException>();
        }

        _runs.Should().Be(0);
    }

    [Fact]
    public async Task ACircuitWithoutATenant_IsNotChecked()
    {
        await using var provider = Build();
        using var circuit = provider.CreateScope();
        _tenants.Suspend("acme");

        await Activity(circuit)(null!);

        _runs.Should().Be(1);
    }

    [Fact]
    public void AddingItTwice_AddsOneHandler()
    {
        ServiceCollection services = new();
        services.AddServerSideBlazor().AddTenantry().AddTenantry();

        services.Count(descriptor => descriptor.ImplementationType == typeof(TenantActivityCircuitHandler)).Should().Be(1);
    }

    // An app with both of Tenantry's checks, which renders an interactive server component on its home page.
    private async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddTenantry<string>(tenant => tenant
            .ResolveFromQueryString("tenant")
            .UseStore(_ => _tenants)
            .ValidateTenantActivity(t => !t.As<SignalRTests.AppTenant>().Suspended));
        builder.Services.AddSignalR(options => options.AddTenantry());
        builder.Services.AddRazorComponents().AddInteractiveServerComponents().AddTenantry();
        builder.Services.AddSingleton<ClosedCircuits>();
        builder.Services.AddScoped<CircuitHandler, ClosedCircuitHandler>();

        var app = builder.Build();
        app.UseTenantry();
        app.UseAntiforgery();
        app.MapRazorComponents<HomePage>().AddInteractiveServerRenderMode();
        await app.StartAsync(Ct);
        return app;
    }

    private ServiceProvider Build()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddTenantry<string>(tenant => tenant
            .UseStore(_ => _tenants)
            .ValidateTenantActivity(t => !t.As<SignalRTests.AppTenant>().Suspended));
        services.AddRazorComponents().AddInteractiveServerComponents().AddTenantry();
        return services.BuildServiceProvider();
    }

    private static IDisposable Current(IServiceProvider provider) =>
        provider.GetRequiredService<ITenantContextSetter<string>>().MakeCurrent(Acme);

    // The handler from the circuit's scope, around an activity that counts its runs.
    private Func<CircuitInboundActivityContext, Task> Activity(IServiceScope circuit) =>
        circuit.ServiceProvider.GetServices<CircuitHandler>().OfType<TenantActivityCircuitHandler>().Single()
            .CreateInboundActivityHandler(_ =>
            {
                _runs++;
                return Task.CompletedTask;
            });

    [Route("/")]
    public sealed class HomePage : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<Greeting>(0);
            builder.AddComponentRenderMode(new InteractiveServerRenderMode(prerender: false));
            builder.CloseComponent();
        }
    }

    public sealed class Greeting : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, "Hello");
    }

    /// <summary>Completes when the first circuit closes.</summary>
    private sealed class ClosedCircuits
    {
        private readonly TaskCompletionSource _first = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task First => _first.Task;

        public void Closed() => _first.TrySetResult();
    }

    private sealed class ClosedCircuitHandler(ClosedCircuits closed) : CircuitHandler
    {
        public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            closed.Closed();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A Blazor Server circuit, opened as its browser script opens one: the page's component marker, a hub connection
    /// over a WebSocket in Blazor's MessagePack protocol, then the calls that start the circuit and add the component.
    /// </summary>
    private sealed partial class CircuitConnection : IAsyncDisposable, IInvocationBinder
    {
        private readonly WebSocket _socket;
        private readonly IHubProtocol _protocol;
        private readonly Queue<HubMessage> _received = new();
        private byte[] _unread = [];

        private CircuitConnection(WebSocket socket, IHubProtocol protocol)
        {
            _socket = socket;
            _protocol = protocol;
        }

        public static async Task<CircuitConnection> OpenAsync(WebApplication app, string tenant)
        {
            using var client = app.GetTestClient();
            var page = await client.GetStringAsync($"/?tenant={tenant}", Ct);
            var marker = BlazorMarker().Match(page).Groups[1].Value;

            var protocol = app.Services.GetServices<IHubProtocol>().Single(p => p.Name == "blazorpack");
            var socket = await app.GetTestServer().CreateWebSocketClient()
                .ConnectAsync(new Uri($"ws://localhost/_blazor?tenant={tenant}"), Ct);
            CircuitConnection circuit = new(socket, protocol);

            await socket.SendAsync(
                Encoding.UTF8.GetBytes("""{"protocol":"blazorpack","version":1}""" + "\u001e"),
                WebSocketMessageType.Text,
                endOfMessage: true,
                Ct);
            Encoding.UTF8.GetString(await circuit.ReceiveFrameAsync()).Should().Be("{}\u001e", "the server accepts the handshake");

            await circuit.SendAsync(new InvocationMessage(
                "1", "StartCircuit", ["http://localhost/", "http://localhost/", "[]", ""]));
            var started = await circuit.ReceiveAsync<CompletionMessage>(_ => true);
            started.Error.Should().BeNull();

            await circuit.SendAsync(new InvocationMessage(
                "UpdateRootComponents",
                [$$"""{"batchId":1,"operations":[{"type":"add","ssrComponentId":1,"marker":{{marker}}}]}""", ""]));
            await circuit.ReceiveAsync("JS.RenderBatch");
            return circuit;
        }

        // The interactive component's marker in the server-rendered page.
        [GeneratedRegex("<!--Blazor:(\\{.*?\\})-->")]
        private static partial Regex BlazorMarker();

        // A JavaScript interop call to a .NET method that does not exist: Blazor answers it with a failure, and it is
        // the circuit's inbound activity.
        public ValueTask SendActivityAsync() =>
            SendAsync(new InvocationMessage("BeginInvokeDotNetFromJS", ["1", "NoSuchAssembly", "NoSuchMethod", 0L, "[]"]));

        // Skips the messages before the next call of the browser method named, and returns that call.
        public Task<HubInvocationMessage> ReceiveAsync(string target) =>
            ReceiveAsync<HubInvocationMessage>(message => message switch
            {
                InvocationMessage invocation => invocation.Target == target,
                InvocationBindingFailureMessage failure => failure.Target == target,
                _ => false,
            });

        // The error Blazor's script shows when the circuit ends.
        public async Task<string?> ReceiveErrorAsync() =>
            (await ReceiveAsync<InvocationMessage>(message => message.Target == "JS.Error")).Arguments[0] as string;

        public async ValueTask DisposeAsync()
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, Ct);
            }

            _socket.Dispose();
        }

        // Only the error message's argument is read: the other browser methods' arguments fail to bind, which still
        // gives their names.
        Type IInvocationBinder.GetReturnType(string invocationId) => typeof(string);

        IReadOnlyList<Type> IInvocationBinder.GetParameterTypes(string methodName) =>
            methodName == "JS.Error" ? [typeof(string)] : Type.EmptyTypes;

        Type IInvocationBinder.GetStreamItemType(string streamId) => typeof(object);

        private async Task<TMessage> ReceiveAsync<TMessage>(Func<TMessage, bool> match)
            where TMessage : HubMessage
        {
            while (true)
            {
                while (_received.Count == 0)
                {
                    ReadOnlySequence<byte> input = new([.. _unread, .. await ReceiveFrameAsync()]);

                    while (_protocol.TryParseMessage(ref input, this, out var message))
                    {
                        _received.Enqueue(message);
                    }

                    _unread = input.ToArray();
                }

                if (_received.Dequeue() is TMessage received && match(received))
                {
                    return received;
                }
            }
        }

        private ValueTask SendAsync(HubMessage message) =>
            _socket.SendAsync(_protocol.GetMessageBytes(message), WebSocketMessageType.Binary, endOfMessage: true, Ct);

        private async Task<byte[]> ReceiveFrameAsync()
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

            return frame.ToArray();
        }
    }
}
