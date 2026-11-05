using Content.Shared._NW.Vehicles;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._NW;

[TestFixture]
public sealed class NWVehicleMotionTests
{
    private static readonly NWVehicleMotion.DriveProfile Profile = new(11, 4, 7, 4, 12);

    [Test]
    public void CardinalControlsDoNotActAsSteering()
    {
        Assert.That(NWVehicleMotion.GetInputDirection(NWVehicleControlInput.Left), Is.EqualTo(new Vector2i(-1, 0)));
        Assert.That(NWVehicleMotion.GetInputDirection(NWVehicleControlInput.Right), Is.EqualTo(new Vector2i(1, 0)));
        Assert.That(NWVehicleMotion.GetInputDirection(NWVehicleControlInput.Forward), Is.EqualTo(new Vector2i(0, 1)));
        Assert.That(NWVehicleMotion.GetInputDirection(NWVehicleControlInput.Back), Is.EqualTo(new Vector2i(0, -1)));
    }

    [Test]
    public void SimultaneousKeysFollowRmcPriority()
    {
        Assert.That(NWVehicleMotion.GetInputDirection(NWVehicleControlInput.Forward | NWVehicleControlInput.Right), Is.EqualTo(new Vector2i(1, 0)));
        Assert.That(NWVehicleMotion.GetInputDirection(NWVehicleControlInput.Forward | NWVehicleControlInput.Back), Is.EqualTo(Vector2i.Zero));
        Assert.That(NWVehicleMotion.GetInputDirection(NWVehicleControlInput.Left | NWVehicleControlInput.Right), Is.EqualTo(Vector2i.Zero));
    }

    [TestCase(3f, 1.8f)]
    [TestCase(-3f, -1.8f)]
    [TestCase(0.5f, 0f)]
    [TestCase(-0.5f, 0f)]
    public void ReleasingKeysBrakesWithoutReversing(float speed, float expected)
    {
        Assert.That(NWVehicleMotion.StepIdleSpeed(speed, 12, 0.1f), Is.EqualTo(expected).Within(0.0001));
    }

    [Test]
    public void OppositeDirectionStopsBeforeReverse()
    {
        var facing = new Vector2i(0, 1);
        var result = NWVehicleMotion.StepDriveSpeed(0.5f, Profile, facing, -facing, true, false, 1f);
        Assert.That(result.CurrentSpeed, Is.Zero);
        result = NWVehicleMotion.StepDriveSpeed(result.CurrentSpeed, Profile, facing, -facing, true, false, 1f);
        Assert.That(result.CurrentSpeed, Is.EqualTo(-4f));
    }

    [Test]
    public void ForwardInputStopsReverseBeforeAccelerating()
    {
        var facing = new Vector2i(1, 0);
        var result = NWVehicleMotion.StepDriveSpeed(-0.5f, Profile, facing, facing, true, false, 1f);
        Assert.That(result.CurrentSpeed, Is.Zero);
        result = NWVehicleMotion.StepDriveSpeed(0f, Profile, facing, facing, true, false, 10f);
        Assert.That(result.CurrentSpeed, Is.EqualTo(11f));
    }

    [TestCase(13f, 4f, 0.2f)]
    [TestCase(8f, 2f, 0.6f)]
    [TestCase(13f, 4f, 0.5f)]
    public void WaterCapsMomentumInBothDirectionsAndRoadSpeedRecovers(float top, float reverse, float factor)
    {
        var road = new NWVehicleMotion.DriveProfile(top, reverse, 8f, 4f, 12f);
        var facing = new Vector2i(0, 1);
        var forwardSpeed = top * 10f;
        var reverseSpeed = -reverse * 10f;
        const float dt = 1f / 60f;
        for (var i = 0; i < 600; i++)
        {
            forwardSpeed = NWVehicleMotion.StepTerrainSpeed(forwardSpeed, road, facing, facing, true, 12f, factor, dt);
            reverseSpeed = NWVehicleMotion.StepTerrainSpeed(reverseSpeed, road, facing, -facing, true, 12f, factor, dt);
            Assert.That(forwardSpeed, Is.EqualTo(top * factor).Within(0.0001f));
            Assert.That(reverseSpeed, Is.EqualTo(-reverse * factor).Within(0.0001f));
        }

        var coast = NWVehicleMotion.StepTerrainSpeed(top, road, facing, Vector2i.Zero, false, 12f, factor, dt);
        Assert.That(coast, Is.LessThan(forwardSpeed));
        var exit = NWVehicleMotion.StepTerrainSpeed(forwardSpeed, road, facing, facing, true, 12f, 1f, dt);
        Assert.That(exit, Is.EqualTo(forwardSpeed + road.Acceleration * dt).Within(0.0001f));
    }
}
