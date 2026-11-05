// everything still packed inside; unload it and it lands under whoever pulled it out.
using System.Numerics;
using Content.Shared._NW.Vehicles;
using Content.Shared.ActionBlocker;
using Content.Shared.DoAfter;
using Content.Shared.DragDrop;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Map;

namespace Content.Server._NW.Vehicles;

public sealed partial class NWVehicleSystem
{
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedNWVehicleSystem _sharedNWVehicle = default!;

    private void InitializeCargo()
    {
        SubscribeLocalEvent<NWVehicleComponent, DragDropTargetEvent>(OnCargoDragDrop);
        SubscribeLocalEvent<NWVehicleComponent, NWVehicleCargoDoAfterEvent>(OnCargoDoAfter);
        SubscribeLocalEvent<NWVehicleComponent, GetVerbsEvent<AlternativeVerb>>(OnCargoVerbs);
        SubscribeLocalEvent<NWVehicleComponent, NWVehicleLoadCargoMessage>(OnLoadCargoMessage);
        SubscribeLocalEvent<NWVehicleComponent, NWVehicleUnloadCargoMessage>(OnUnloadCargoMessage);
    }

    /// <summary>
    /// The console has no way to choose a crate on the user's behalf, so it loads the one
    /// </summary>
    private void OnLoadCargoMessage(Entity<NWVehicleComponent> ent, ref NWVehicleLoadCargoMessage args)
    {
        var user = args.Actor;

        if (!_cabinPulling.TryGetPulledEntity(user, out var pulledCrate) || pulledCrate is not { } crate)
        {
            _popup.PopupEntity(Loc.GetString("nw-vehicle-cargo-nothing-pulled"), ent, user);
            return;
        }

        if (!_sharedNWVehicle.CanStoreCargo(ent, crate))
        {
            _popup.PopupEntity(Loc.GetString("nw-vehicle-cargo-will-not-fit"), ent, user);
            return;
        }

        StartCargoDoAfter(ent, user, crate, loading: true);
    }

    private void OnUnloadCargoMessage(Entity<NWVehicleComponent> ent, ref NWVehicleUnloadCargoMessage args)
    {
        if (!TryGetEntity(args.Crate, out var crate) || !_sharedNWVehicle.GetCargo(ent).Contains(crate.Value))
            return;

        StartCargoDoAfter(ent, args.Actor, crate.Value, loading: false);
    }

    private void OnCargoDragDrop(Entity<NWVehicleComponent> ent, ref DragDropTargetEvent args)
    {
        if (args.Handled || !_sharedNWVehicle.CanStoreCargo(ent, args.Dragged))
            return;

        args.Handled = true;
        StartCargoDoAfter(ent, args.User, args.Dragged, loading: true);
    }

    private void OnCargoVerbs(Entity<NWVehicleComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess || !_actionBlocker.CanInteract(args.User, ent))
            return;

        var user = args.User;
        foreach (var crate in _sharedNWVehicle.GetCargo(ent))
        {
            var stored = crate;
            args.Verbs.Add(new AlternativeVerb
            {
                Act = () => StartCargoDoAfter(ent, user, stored, loading: false),
                Category = VerbCategory.Eject,
                Text = Name(stored),
            });
        }
    }

    /// <summary>
    /// Hauling a crate either way costs the same as climbing aboard: it is the same
    /// amount of wrestling something heavy through the same cabin door. Only a crate
    /// still out in the world is passed as the do-after's Used, so that dragging it
    /// out of reach cancels the load; one already in the bay cannot go anywhere.
    /// </summary>
    private void StartCargoDoAfter(Entity<NWVehicleComponent> ent, EntityUid user, EntityUid crate, bool loading)
    {
        if (HasComp<NWGroundVehicleComponent>(ent) && ent.Comp.State != NWVehicleState.Parked)
            return;

        var doAfter = new DoAfterArgs(
            EntityManager,
            user,
            BoardingDuration,
            new NWVehicleCargoDoAfterEvent(GetNetEntity(crate)),
            ent.Owner,
            target: ent.Owner,
            used: loading ? crate : null)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            DistanceThreshold = 2f,
            BlockDuplicate = true,
        };

        _doAfter.TryStartDoAfter(doAfter);
    }

    private void OnCargoDoAfter(Entity<NWVehicleComponent> ent, ref NWVehicleCargoDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !TryGetEntity(args.Crate, out var crateUid))
            return;

        args.Handled = true;
        if (HasComp<NWGroundVehicleComponent>(ent) && ent.Comp.State != NWVehicleState.Parked)
            return;
        var crate = crateUid.Value;
        var container = _container.EnsureContainer<Container>(ent.Owner, SharedNWVehicleSystem.CargoContainerId);

        if (container.Contains(crate))
            DropCargo(ent, crate, container, args.User);
        else if (_sharedNWVehicle.CanStoreCargo(ent, crate))
        {
            if (TryComp<NWVehicleCabinComponent>(ent, out var cabin))
            {
                if (!EnsureCabin((ent.Owner, cabin)) || cabin.Grid is not { } grid) return;
                _cabinPulling.TryStopPull(crate, user: args.User);
                _transform.SetCoordinates(crate, new EntityCoordinates(grid, cabin.EntryPoints[0]));
            }
            else
                _container.Insert(crate, container);
        }

        UpdateUi(ent);
    }

    /// <summary>
    /// Where a crate ends up when it is hauled out on the same level. Someone standing
    /// outside gets it at their feet, but a crewman unloading from a seat is buckled to the
    /// craft, so their own coordinates are the craft's origin: handing those over parents the
    /// the craft flies. Their crates come out clear of the hull, resolved against the grid.
    /// </summary>
    private EntityCoordinates GetUnloadCoordinates(Entity<NWVehicleComponent> ent, EntityUid user)
    {
        var userXform = Transform(user);
        if (userXform.ParentUid != ent.Owner)
            return _transform.GetMoverCoordinates(user, userXform);

        // Just past the hull's half-height, so the crate sits at the cabin door rather than
        // beneath the fuselage. Local, so it follows whichever way the craft is facing.
        return _transform.GetMoverCoordinates(new EntityCoordinates(ent.Owner, new Vector2(0f, -1.2f)));
    }

    /// <summary>
    /// On the ground the crate comes out beside whoever hauled it out. Airborne that is not
    /// an option: the one unloading is strapped in, so their coordinates are the craft's, and
    /// down to ground level, directly beneath the craft.
    /// </summary>
    private void DropCargo(Entity<NWVehicleComponent> ent, EntityUid crate, BaseContainer container, EntityUid user)
    {
        _container.Remove(crate, container, destination: GetUnloadCoordinates(ent, user));
    }
}
