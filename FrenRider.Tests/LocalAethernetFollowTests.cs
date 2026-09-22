using System.Numerics;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class LocalAethernetFollowTests
{
    [Fact]
    public void LocalJumpRequiresFreshUniqueConnectedDestinationAndIsConsumedOnce()
    {
        var originPosition = new Vector3(100, 0, 100);
        var origin = new LocalAethernetNode(1, 10, new(100, 100), "Origin");
        var destination = new LocalAethernetNode(2, 10, new(150.1f, 100), "Destination");
        var network = new LocalAethernetNetwork(origin, [origin, destination], 15f);
        var before = new LocalAethernetSample("Fren@World", 10, originPosition, 1000);
        var after = before with { Position = new(150.1f, 0, 100), Timestamp = 1200 };
        var detector = new LocalAethernetFollowDetector();

        bool Observe(LocalAethernetSample? sample, long now = 1200,
            LocalAethernetNetwork? catalog = null, Vector3? follower = null)
            => detector.Observe(sample, catalog ?? network, originPosition,
                follower ?? originPosition, now, out _);

        bool Jump(LocalAethernetSample next, LocalAethernetNetwork? catalog = null,
            Vector3? follower = null, LocalAethernetSample? start = null)
        {
            detector.Clear();
            Assert.False(Observe(start ?? before, 1000));
            return Observe(next, next.Timestamp, catalog, follower);
        }

        Assert.False(Jump(after with { Position = new(149.9f, 0, 100) }));
        Assert.False(Jump(after with { Position = new(150, 0, 100) })); // Exactly 50.
        Assert.True(Jump(after)); // Greater than 50.
        Assert.False(Observe(after)); // Same party snapshot.
        Assert.False(Observe(after with { Timestamp = 1300 }, 1300)); // Fresh duplicate position.

        Assert.True(Jump(after with { Position = new(175.1f, 0, 100) })); // Exactly 25 from destination.
        Assert.False(Jump(after with { Position = new(175.2f, 0, 100) }));
        Assert.False(Jump(after with { Territory = 99 })); // Outside the connected network.
        Assert.True(Jump(after with { Territory = 11 }, network with
        {
            Nodes = [origin, destination with { Territory = 11 }],
        })); // A normal network can span territories.
        Assert.False(Jump(after, network with { Nodes = [origin] }));
        Assert.False(Jump(after, network with
        {
            Nodes = [origin, destination, destination with { Id = 3, Name = "Other" }],
        }));
        Assert.False(Jump(after, network with
        {
            Nodes = [origin with { Alias = "Destination" }, destination],
        })); // Name IPC must also be unambiguous.

        Assert.False(Jump(after, follower: new(111, 0, 100)));
        Assert.False(Jump(after, follower: new(100, 15, 100)));
        Assert.False(Jump(after, start: before with { Position = new(80, 0, 100) }));
        var smallShardNetwork = network with { InteractionDistance = 4.6f };
        Assert.True(Jump(after, smallShardNetwork, new(104, 0, 100)));
        Assert.False(Jump(after, smallShardNetwork, new(105, 0, 100)));
        Assert.False(Jump(after, smallShardNetwork, start: before with { Position = new(95, 0, 100) }));

        Assert.False(Jump(after with { Timestamp = 4001 })); // Previous sample expired.
        Assert.False(Jump(after with { Target = "Different Fren@World" }));
        Assert.False(Jump(after with { Position = Vector3.Zero }));
        Assert.False(Jump(after with { Position = new(float.NaN, 0, 100) }));
        detector.Clear();
        Assert.False(Observe(before, 1000));
        Assert.False(Observe(after, 4201)); // Current sample expired.
        Assert.False(Observe(before, 1000));
        Assert.False(Observe(after with { Timestamp = 3900 }, 4100)); // Only the older sample expired.
        Assert.False(Observe(before, 1000));
        Assert.False(Observe(null));
        Assert.False(Observe(after)); // Missing samples clear the origin observation.
        detector.Clear();
        Assert.False(Observe(before, 1000));
        Assert.False(detector.Observe(after, null, originPosition, originPosition, 1200, out _));
        Assert.False(Observe(after with { Timestamp = 1300 }, 1300)); // Missing catalog consumes jump.
        detector.Clear();
        Assert.False(Observe(after)); // Disable/target/transition reset cannot replay it.
    }
}
