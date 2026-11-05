// Adapted from KL4SS1C/RMC-14-For-import, b7a8b0291a6b93beecf49ddb09cc2bfd7ef8c2cf.
// Original MIT notice: see LICENSES/RMC-LICENSE.TXT in the distribution.
using System;
using Robust.Shared.Maths;

namespace Content.Shared._NW.Vehicles;

public static class NWVehicleMotion
{
    /// <summary>RMC cardinal input: horizontal wins when two perpendicular keys are held.</summary>
    public static Vector2i GetInputDirection(NWVehicleControlInput buttons)
    {
        var x = ((buttons & NWVehicleControlInput.Right) != 0 ? 1 : 0)
            - ((buttons & NWVehicleControlInput.Left) != 0 ? 1 : 0);
        var y = ((buttons & NWVehicleControlInput.Forward) != 0 ? 1 : 0)
            - ((buttons & NWVehicleControlInput.Back) != 0 ? 1 : 0);
        return x != 0 ? new Vector2i(x, 0) : new Vector2i(0, y);
    }

    public readonly record struct DriveProfile(
        float MaxSpeed,
        float MaxReverseSpeed,
        float Acceleration,
        float ReverseAcceleration,
        float Deceleration);

    public readonly record struct DriveSpeedResult(
        float CurrentSpeed,
        bool ReversingInput,
        bool ChangingDirection);

    public static float StepTerrainSpeed(float currentSpeed, DriveProfile road, Vector2i facing,
        Vector2i input, bool hasInput, float rollingResistance, float terrainFactor, float frameTime)
    {
        // Build the limits from the road profile every tick. Never scale last tick's limits.
        var profile = road with
        {
            MaxSpeed = road.MaxSpeed * terrainFactor,
            MaxReverseSpeed = road.MaxReverseSpeed * terrainFactor,
            Acceleration = road.Acceleration * terrainFactor,
            ReverseAcceleration = road.ReverseAcceleration * terrainFactor,
        };
        // Discard excess entry momentum, including impulses read back from the physics body.
        var speed = Math.Clamp(currentSpeed, -profile.MaxReverseSpeed, profile.MaxSpeed);
        var next = hasInput
            ? StepDriveSpeed(speed, profile, facing, input, true, false, frameTime).CurrentSpeed
            : StepIdleSpeed(speed, rollingResistance, frameTime);
        return Math.Clamp(next, -profile.MaxReverseSpeed, profile.MaxSpeed);
    }

    public static float StepIdleSpeed(float currentSpeed, float deceleration, float frameTime)
    {
        if (currentSpeed > 0f)
            return MathF.Max(0f, currentSpeed - deceleration * frameTime);

        if (currentSpeed < 0f)
            return MathF.Min(0f, currentSpeed + deceleration * frameTime);

        return 0f;
    }

    public static float StepPushSpeed(
        float currentSpeed,
        float maxSpeed,
        float acceleration,
        float deceleration,
        bool hasInput,
        bool isCommittedToMove,
        float frameTime)
    {
        var hasInputForSpeed = hasInput || isCommittedToMove;
        float targetSpeed;
        float accel;

        if (!hasInputForSpeed)
        {
            targetSpeed = 0f;
            accel = deceleration;
        }
        else
        {
            targetSpeed = maxSpeed;
            accel = acceleration;
        }

        return StepTowardsTargetSpeed(currentSpeed, targetSpeed, accel, deceleration, frameTime);
    }

    public static DriveSpeedResult StepDriveSpeed(
        float currentSpeed,
        DriveProfile profile,
        Vector2i facing,
        Vector2i inputDir,
        bool hasInput,
        bool isCommittedToMove,
        float frameTime)
    {
        var hasInputForSpeed = hasInput || isCommittedToMove;
        var reversing = hasInput && facing != Vector2i.Zero && inputDir == -facing;

        float targetSpeed;
        float accel;

        if (!hasInputForSpeed)
        {
            targetSpeed = 0f;
            accel = profile.Deceleration;
        }
        else if (reversing)
        {
            if (currentSpeed > 0f)
            {
                targetSpeed = 0f;
                accel = profile.Deceleration;
            }
            else
            {
                targetSpeed = -profile.MaxReverseSpeed;
                accel = profile.ReverseAcceleration;
            }
        }
        else
        {
            if (currentSpeed < 0f && hasInputForSpeed)
            {
                targetSpeed = 0f;
                accel = profile.Deceleration;
            }
            else
            {
                targetSpeed = profile.MaxSpeed;
                accel = profile.Acceleration;
            }
        }

        var steppedSpeed = StepTowardsTargetSpeed(currentSpeed, targetSpeed, accel, profile.Deceleration, frameTime);
        var changingDirection =
            MathF.Abs(steppedSpeed) > 0.01f &&
            ((reversing && steppedSpeed > 0f) ||
             (!reversing && steppedSpeed < 0f));

        return new DriveSpeedResult(steppedSpeed, reversing, changingDirection);
    }

    private static float StepTowardsTargetSpeed(
        float currentSpeed,
        float targetSpeed,
        float accelerateTowardTarget,
        float decelerateTowardTarget,
        float frameTime)
    {
        if (currentSpeed < targetSpeed)
            return MathF.Min(currentSpeed + accelerateTowardTarget * frameTime, targetSpeed);

        if (currentSpeed > targetSpeed)
            return MathF.Max(currentSpeed - decelerateTowardTarget * frameTime, targetSpeed);

        return currentSpeed;
    }
}
