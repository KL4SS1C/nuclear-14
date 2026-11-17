namespace Content.Shared._NW.Telephone;

/// <summary>
///     Raised when playing rotary telephone ring sound.
/// </summary>
[ByRefEvent]
public readonly record struct NWTelephoneRingEvent(EntityUid Receiving, EntityUid Calling, EntityUid Actor);
