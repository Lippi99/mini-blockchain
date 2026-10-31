using Microsoft.Extensions.Options;
using MiniChain.Core;
using MiniChain.Node;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<NodeOptions>().Bind(builder.Configuration.GetSection(NodeOptions.Section));
builder.Services.ConfigureHttpJsonOptions(json => MiniChainJson.Configure(json.SerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);

// The node's state. Blockchain and Mempool are not thread-safe; only NodeState touches them, under a lock.
builder.Services.AddSingleton(services => new Blockchain(services.GetRequiredService<IOptions<NodeOptions>>().Value.ToChainParameters()));
builder.Services.AddSingleton<Mempool>();
builder.Services.AddSingleton<NodeState>();
builder.Services.AddSingleton<NodeIdentity>();
builder.Services.AddSingleton<PeerRegistry>();

// Talking to peers.
builder.Services.AddHttpClient(HttpPeerClient.HttpClientName, (services, client) =>
    client.Timeout = TimeSpan.FromSeconds(services.GetRequiredService<IOptions<NodeOptions>>().Value.PeerTimeoutSeconds));
builder.Services.AddSingleton<IPeerClient, HttpPeerClient>();
builder.Services.AddSingleton<GossipService>();
builder.Services.AddHostedService(services => services.GetRequiredService<GossipService>());
builder.Services.AddSingleton<ChainSyncService>();
builder.Services.AddHostedService(services => services.GetRequiredService<ChainSyncService>());

// Mining.
builder.Services.AddSingleton<MiningService>();
builder.Services.AddHostedService<BackgroundMiner>();

var app = builder.Build();

// Create the node's state now rather than on the first request, so bad settings stop the node at startup.
var node = app.Services.GetRequiredService<NodeState>();
var identity = app.Services.GetRequiredService<NodeIdentity>();
app.Services.GetRequiredService<PeerRegistry>();
app.Lifetime.ApplicationStarted.Register(() =>
{
    var summary = node.GetSummary();
    Log.NodeStarted(app.Logger, identity.Url, summary.Height, summary.NextDifficulty, identity.MinerAddress);
});

app.MapNodeApi();
app.Run();

/// <summary>Exposed so integration tests can start nodes with WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program;
