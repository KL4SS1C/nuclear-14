using System.Numerics;
using Content.Shared._NW.Vehicles;
using NUnit.Framework;
namespace Content.Tests.Shared._NW;
[TestFixture]
public sealed class NWVehicleImpactTests
{
    [TestCase(0f, 0f, 0.85f, 0.85f, true)]
    [TestCase(0f, 0f, 0.91f, 0f, false)]
    [TestCase(3f, 0f, 3.85f, 0.85f, true)]
    [TestCase(3f, 0f, 2f, 0.91f, false)]
    [TestCase(3f, 3f, 0f, 3f, false)]
    [TestCase(-3f, -3f, -1.5f, -1.6f, true)]
    public void ChecksSweptSquare(float dx, float dy, float x, float y, bool hit)
    {
        Assert.That(NWVehicleImpactMath.InSweptSquare(new Vector2(x, y), Vector2.Zero,
            new Vector2(dx, dy), 0.9f), Is.EqualTo(hit));
    }

    [TestCase(0.49f, 1.95f, true)]
    [TestCase(0.51f, 0.50f, false)]
    [TestCase(0f, 1.99f, false)]
    public void CompactVanSweepFollowsFacing(float side, float front, bool hit)
    {
        var size = new Vector2(0.5f, 0.97f);
        Assert.That(NWVehicleImpactMath.InSweptRectangle(new Vector2(side, front),
            Vector2.Zero, Vector2.UnitY, size), Is.EqualTo(hit));
        Assert.That(NWVehicleImpactMath.InSweptRectangle(new Vector2(front, -side),
            Vector2.Zero, Vector2.UnitX, new Vector2(size.Y, size.X)), Is.EqualTo(hit));
    }
}
