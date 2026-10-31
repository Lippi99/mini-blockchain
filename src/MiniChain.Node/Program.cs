using Microsoft.Extensions.Options;
using MiniChain.Core;
using MiniChain.Node;
using MiniChain.Node.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<NodeOptions>().Bind(builder.Configuration.GetSection(NodeOptions.Section));
builder.Services.ConfigureHttpJsonOptions(json => MiniChainJson.Configure(json.SerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);

// Storage: where the chain and the node's own wallet are kept between restarts.
builder.Services.AddSingleton(services => NodeStorage.ResolveDataDirectory(
    services.GetRequiredService<IOptions<NodeOptions>>().Value,
    services.GetRequiredService<IConfiguration>(),
    services.GetRequiredService<IHostEnvironment>()));
builder.Services.AddSingleton(services => NodeStorage.CreateStore(
    services.GetRequiredService<IOptions<NodeOptions>>().Value,
    services.GetRequiredService<NodeDataDirectory>()));

// The node's state. Blockchain and Mempool are not thread-safe; only NodeState touches them, under a lock.
builder.Services.AddSingleton(services => NodeStorage.LoadChain(
    services.GetRequiredService<IChainStore>(),
    services.GetRequiredService<IOptions<NodeOptions>>().Value.ToChainParameters()));
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

// Load the chain and create the node's state now rather than on the first request, so a corrupted data
// file or a bad setting stops the node at startup with a clear message.
NodeState node;
NodeIdentity identity;
try
{
    node = app.Services.GetRequiredService<NodeState>();
    identity = app.Services.GetRequiredService<NodeIdentity>();
    app.Services.GetRequiredService<PeerRegistry>();
}
catch (Exception e) when (e is InvalidDataException or InvalidOperationException or ArgumentException)
{
    Log.StoredChainUnusable(app.Logger, e.Message);
    throw;
}

Log.ChainStorage(app.Logger, app.Services.GetRequiredService<IChainStore>().Location);
if (identity.WalletPath is not null)
    Log.NodeWallet(app.Logger, identity.WalletPath);
app.Lifetime.ApplicationStarted.Register(() =>
{
    var summary = node.GetSummary();
    Log.NodeStarted(app.Logger, identity.Url, summary.Height, summary.NextDifficulty, identity.MinerAddress);
});

app.MapNodeApi();
app.Run();

/// <summary>Exposed so integration tests can start nodes with WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program;
