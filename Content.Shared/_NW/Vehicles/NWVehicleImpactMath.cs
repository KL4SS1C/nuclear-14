using System.Numerics;
namespace Content.Shared._NW.Vehicles;

public static class NWVehicleImpactMath
{
    public static bool InSweptSquare(Vector2 point, Vector2 start, Vector2 end, float halfWidth)
        => InSweptRectangle(point, start, end, new Vector2(halfWidth));

    public static bool InSweptRectangle(Vector2 point, Vector2 start, Vector2 end, Vector2 halfSize)
    {
        // Test the segment against the rectangle around the point. Its separating axes
        // are X, Y and the normal to the segment, including for diagonal slides.
        var halfTravel = (end - start) * 0.5f;
        var offset = point - (start + halfTravel);
        if (MathF.Abs(offset.X) > halfSize.X + MathF.Abs(halfTravel.X) ||
            MathF.Abs(offset.Y) > halfSize.Y + MathF.Abs(halfTravel.Y))
            return false;

        var cross = offset.X * halfTravel.Y - offset.Y * halfTravel.X;
        return MathF.Abs(cross) <= halfSize.X * MathF.Abs(halfTravel.Y) + halfSize.Y * MathF.Abs(halfTravel.X);
    }
}
