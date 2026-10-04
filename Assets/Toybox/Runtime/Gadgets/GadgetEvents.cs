using System;
using Toybox.Gadgets;

namespace Toybox.Engine
{
    /// <summary>
    /// The gadgets' channels on game.Events (LEVELS 2.0: every gadget event has a mirror here, delivered
    /// when the tick ends). All carry a <see cref="GadgetEvent"/>: the gadget, where it happened, and the
    /// numbers named at each event. Only the pressure plate's events end in "Pressed" / "Released": the
    /// audio presenter gives every event with such a name the button sound.
    /// </summary>
    public sealed partial class GameEvents
    {
        /// <summary>A pressure plate went down. Prop: what pressed it (null: the player). Value: its load / MinMass.</summary>
        public event Action<GadgetEvent> PlatePressed { add => Ch(ref platePressed).Add(value); remove => Ch(ref platePressed).Remove(value); }
        public void RaisePlatePressed(GadgetEvent e) => Ch(ref platePressed).Raise(e);
        Channel<GadgetEvent> platePressed;

        /// <summary>A pressure plate came back up.</summary>
        public event Action<GadgetEvent> PlateReleased { add => Ch(ref plateReleased).Add(value); remove => Ch(ref plateReleased).Remove(value); }
        public void RaisePlateReleased(GadgetEvent e) => Ch(ref plateReleased).Raise(e);
        Channel<GadgetEvent> plateReleased;

        /// <summary>A settled prop was too light for a plate. Value: load / MinMass.</summary>
        public event Action<GadgetEvent> PlateRejected { add => Ch(ref plateRejected).Add(value); remove => Ch(ref plateRejected).Remove(value); }
        public void RaisePlateRejected(GadgetEvent e) => Ch(ref plateRejected).Raise(e);
        Channel<GadgetEvent> plateRejected;

        /// <summary>A hazard zone caught the player.</summary>
        public event Action<GadgetEvent> HazardCaught { add => Ch(ref hazardCaught).Add(value); remove => Ch(ref hazardCaught).Remove(value); }
        public void RaiseHazardCaught(GadgetEvent e) => Ch(ref hazardCaught).Raise(e);
        Channel<GadgetEvent> hazardCaught;

        /// <summary>A leash sent a prop back. Prop: the prop; Position: where it was.</summary>
        public event Action<GadgetEvent> LeashReturned { add => Ch(ref leashReturned).Add(value); remove => Ch(ref leashReturned).Remove(value); }
        public void RaiseLeashReturned(GadgetEvent e) => Ch(ref leashReturned).Raise(e);
        Channel<GadgetEvent> leashReturned;

        /// <summary>Something broke. Prop: what hit it; Value: its speed into it.</summary>
        public event Action<GadgetEvent> BreakableBroke { add => Ch(ref breakableBroke).Add(value); remove => Ch(ref breakableBroke).Remove(value); }
        public void RaiseBreakableBroke(GadgetEvent e) => Ch(ref breakableBroke).Raise(e);
        Channel<GadgetEvent> breakableBroke;

        /// <summary>Something hit a breakable too weakly. Value: mass / MinMass.</summary>
        public event Action<GadgetEvent> BreakableBonked { add => Ch(ref breakableBonked).Add(value); remove => Ch(ref breakableBonked).Remove(value); }
        public void RaiseBreakableBonked(GadgetEvent e) => Ch(ref breakableBonked).Raise(e);
        Channel<GadgetEvent> breakableBonked;

        /// <summary>A socket seated its prop.</summary>
        public event Action<GadgetEvent> SocketSeated { add => Ch(ref socketSeated).Add(value); remove => Ch(ref socketSeated).Remove(value); }
        public void RaiseSocketSeated(GadgetEvent e) => Ch(ref socketSeated).Raise(e);
        Channel<GadgetEvent> socketSeated;

        /// <summary>A seated prop was taken out of its socket.</summary>
        public event Action<GadgetEvent> SocketUnseated { add => Ch(ref socketUnseated).Add(value); remove => Ch(ref socketUnseated).Remove(value); }
        public void RaiseSocketUnseated(GadgetEvent e) => Ch(ref socketUnseated).Raise(e);
        Channel<GadgetEvent> socketUnseated;

        /// <summary>A socket refused a prop. Index: the FitState.</summary>
        public event Action<GadgetEvent> SocketRejected { add => Ch(ref socketRejected).Add(value); remove => Ch(ref socketRejected).Remove(value); }
        public void RaiseSocketRejected(GadgetEvent e) => Ch(ref socketRejected).Raise(e);
        Channel<GadgetEvent> socketRejected;

        /// <summary>A door started to move. Index: 1 opening, 0 closing.</summary>
        public event Action<GadgetEvent> DoorStarted { add => Ch(ref doorStarted).Add(value); remove => Ch(ref doorStarted).Remove(value); }
        public void RaiseDoorStarted(GadgetEvent e) => Ch(ref doorStarted).Raise(e);
        Channel<GadgetEvent> doorStarted;

        /// <summary>A door is fully open.</summary>
        public event Action<GadgetEvent> DoorOpened { add => Ch(ref doorOpened).Add(value); remove => Ch(ref doorOpened).Remove(value); }
        public void RaiseDoorOpened(GadgetEvent e) => Ch(ref doorOpened).Raise(e);
        Channel<GadgetEvent> doorOpened;

        /// <summary>A door is fully closed.</summary>
        public event Action<GadgetEvent> DoorClosed { add => Ch(ref doorClosed).Add(value); remove => Ch(ref doorClosed).Remove(value); }
        public void RaiseDoorClosed(GadgetEvent e) => Ch(ref doorClosed).Raise(e);
        Channel<GadgetEvent> doorClosed;

        /// <summary>A return port put a prop back into the room.</summary>
        public event Action<GadgetEvent> PortEjected { add => Ch(ref portEjected).Add(value); remove => Ch(ref portEjected).Remove(value); }
        public void RaisePortEjected(GadgetEvent e) => Ch(ref portEjected).Raise(e);
        Channel<GadgetEvent> portEjected;

        /// <summary>A fit gauge shows something else. Index: the FitState.</summary>
        public event Action<GadgetEvent> GaugeChanged { add => Ch(ref gaugeChanged).Add(value); remove => Ch(ref gaugeChanged).Remove(value); }
        public void RaiseGaugeChanged(GadgetEvent e) => Ch(ref gaugeChanged).Raise(e);
        Channel<GadgetEvent> gaugeChanged;

        /// <summary>A wind stream changed its strength. Value: the strength.</summary>
        public event Action<GadgetEvent> WindChanged { add => Ch(ref windChanged).Add(value); remove => Ch(ref windChanged).Remove(value); }
        public void RaiseWindChanged(GadgetEvent e) => Ch(ref windChanged).Raise(e);
        Channel<GadgetEvent> windChanged;

        /// <summary>A sail was caught by the stream and pinned.</summary>
        public event Action<GadgetEvent> SailMoored { add => Ch(ref sailMoored).Add(value); remove => Ch(ref sailMoored).Remove(value); }
        public void RaiseSailMoored(GadgetEvent e) => Ch(ref sailMoored).Raise(e);
        Channel<GadgetEvent> sailMoored;

        /// <summary>A sail could not lift its rider. Value: capacity / rider mass.</summary>
        public event Action<GadgetEvent> SailStalled { add => Ch(ref sailStalled).Add(value); remove => Ch(ref sailStalled).Remove(value); }
        public void RaiseSailStalled(GadgetEvent e) => Ch(ref sailStalled).Raise(e);
        Channel<GadgetEvent> sailStalled;

        /// <summary>A sail took off with its rider.</summary>
        public event Action<GadgetEvent> SailLaunched { add => Ch(ref sailLaunched).Add(value); remove => Ch(ref sailLaunched).Remove(value); }
        public void RaiseSailLaunched(GadgetEvent e) => Ch(ref sailLaunched).Raise(e);
        Channel<GadgetEvent> sailLaunched;

        /// <summary>A sail arrived.</summary>
        public event Action<GadgetEvent> SailDocked { add => Ch(ref sailDocked).Add(value); remove => Ch(ref sailDocked).Remove(value); }
        public void RaiseSailDocked(GadgetEvent e) => Ch(ref sailDocked).Raise(e);
        Channel<GadgetEvent> sailDocked;

        /// <summary>A bounce pad threw the player. Value: launch speed; Value2: impact speed.</summary>
        public event Action<GadgetEvent> Bounced { add => Ch(ref bounced).Add(value); remove => Ch(ref bounced).Remove(value); }
        public void RaiseBounced(GadgetEvent e) => Ch(ref bounced).Raise(e);
        Channel<GadgetEvent> bounced;

        /// <summary>A seesaw was struck. Value: the strike load M; Value2: f.</summary>
        public event Action<GadgetEvent> SeesawStruck { add => Ch(ref seesawStruck).Add(value); remove => Ch(ref seesawStruck).Remove(value); }
        public void RaiseSeesawStruck(GadgetEvent e) => Ch(ref seesawStruck).Raise(e);
        Channel<GadgetEvent> seesawStruck;

        /// <summary>A seesaw threw a rider. Prop: null for the player; Value: the speed.</summary>
        public event Action<GadgetEvent> SeesawLaunched { add => Ch(ref seesawLaunched).Add(value); remove => Ch(ref seesawLaunched).Remove(value); }
        public void RaiseSeesawLaunched(GadgetEvent e) => Ch(ref seesawLaunched).Raise(e);
        Channel<GadgetEvent> seesawLaunched;

        /// <summary>A seesaw is back at rest.</summary>
        public event Action<GadgetEvent> SeesawReturned { add => Ch(ref seesawReturned).Add(value); remove => Ch(ref seesawReturned).Remove(value); }
        public void RaiseSeesawReturned(GadgetEvent e) => Ch(ref seesawReturned).Raise(e);
        Channel<GadgetEvent> seesawReturned;

        /// <summary>A seesaw shot its projectile. Position: where from; Value: its speed, straight up.</summary>
        public event Action<GadgetEvent> SeesawProjectile { add => Ch(ref seesawProjectile).Add(value); remove => Ch(ref seesawProjectile).Remove(value); }
        public void RaiseSeesawProjectile(GadgetEvent e) => Ch(ref seesawProjectile).Raise(e);
        Channel<GadgetEvent> seesawProjectile;

        /// <summary>The train's engine passed the station. Index: the lap.</summary>
        public event Action<GadgetEvent> TrainAtStation { add => Ch(ref trainAtStation).Add(value); remove => Ch(ref trainAtStation).Remove(value); }
        public void RaiseTrainAtStation(GadgetEvent e) => Ch(ref trainAtStation).Raise(e);
        Channel<GadgetEvent> trainAtStation;

        /// <summary>A carrier bed gripped a prop. Index: the bed.</summary>
        public event Action<GadgetEvent> CarrierCaptured { add => Ch(ref carrierCaptured).Add(value); remove => Ch(ref carrierCaptured).Remove(value); }
        public void RaiseCarrierCaptured(GadgetEvent e) => Ch(ref carrierCaptured).Raise(e);
        Channel<GadgetEvent> carrierCaptured;

        /// <summary>A carried prop left its bed.</summary>
        public event Action<GadgetEvent> CarrierDropped { add => Ch(ref carrierDropped).Add(value); remove => Ch(ref carrierDropped).Remove(value); }
        public void RaiseCarrierDropped(GadgetEvent e) => Ch(ref carrierDropped).Raise(e);
        Channel<GadgetEvent> carrierDropped;

        /// <summary>A float platform left its rest height.</summary>
        public event Action<GadgetEvent> FloatLeft { add => Ch(ref floatLeft).Add(value); remove => Ch(ref floatLeft).Remove(value); }
        public void RaiseFloatLeft(GadgetEvent e) => Ch(ref floatLeft).Raise(e);
        Channel<GadgetEvent> floatLeft;

        /// <summary>A float platform reached its top.</summary>
        public event Action<GadgetEvent> FloatArrived { add => Ch(ref floatArrived).Add(value); remove => Ch(ref floatArrived).Remove(value); }
        public void RaiseFloatArrived(GadgetEvent e) => Ch(ref floatArrived).Raise(e);
        Channel<GadgetEvent> floatArrived;

        /// <summary>A path drive finished a segment. Index: which.</summary>
        public event Action<GadgetEvent> PathSegmentEnded { add => Ch(ref pathSegmentEnded).Add(value); remove => Ch(ref pathSegmentEnded).Remove(value); }
        public void RaisePathSegmentEnded(GadgetEvent e) => Ch(ref pathSegmentEnded).Raise(e);
        Channel<GadgetEvent> pathSegmentEnded;

        /// <summary>A path drive ran to its end.</summary>
        public event Action<GadgetEvent> PathFinished { add => Ch(ref pathFinished).Add(value); remove => Ch(ref pathFinished).Remove(value); }
        public void RaisePathFinished(GadgetEvent e) => Ch(ref pathFinished).Raise(e);
        Channel<GadgetEvent> pathFinished;

        /// <summary>A piece of a hinge chain went over. Index: which (0 based).</summary>
        public event Action<GadgetEvent> ChainPieceFell { add => Ch(ref chainPieceFell).Add(value); remove => Ch(ref chainPieceFell).Remove(value); }
        public void RaiseChainPieceFell(GadgetEvent e) => Ch(ref chainPieceFell).Raise(e);
        Channel<GadgetEvent> chainPieceFell;

        /// <summary>The last piece of a hinge chain hit its target.</summary>
        public event Action<GadgetEvent> ChainTargetStruck { add => Ch(ref chainTargetStruck).Add(value); remove => Ch(ref chainTargetStruck).Remove(value); }
        public void RaiseChainTargetStruck(GadgetEvent e) => Ch(ref chainTargetStruck).Raise(e);
        Channel<GadgetEvent> chainTargetStruck;

        /// <summary>The last piece of a hinge chain fell flat without reaching its target.</summary>
        public event Action<GadgetEvent> ChainFellShort { add => Ch(ref chainFellShort).Add(value); remove => Ch(ref chainFellShort).Remove(value); }
        public void RaiseChainFellShort(GadgetEvent e) => Ch(ref chainFellShort).Raise(e);
        Channel<GadgetEvent> chainFellShort;

        /// <summary>A nested prop appeared. Index: its place in the set.</summary>
        public event Action<GadgetEvent> NestRevealed { add => Ch(ref nestRevealed).Add(value); remove => Ch(ref nestRevealed).Remove(value); }
        public void RaiseNestRevealed(GadgetEvent e) => Ch(ref nestRevealed).Raise(e);
        Channel<GadgetEvent> nestRevealed;

        /// <summary>A laser zapped the player.</summary>
        public event Action<GadgetEvent> LaserZapped { add => Ch(ref laserZapped).Add(value); remove => Ch(ref laserZapped).Remove(value); }
        public void RaiseLaserZapped(GadgetEvent e) => Ch(ref laserZapped).Raise(e);
        Channel<GadgetEvent> laserZapped;

        /// <summary>Every beam over the lane is blocked.</summary>
        public event Action<GadgetEvent> LaserAllClear { add => Ch(ref laserAllClear).Add(value); remove => Ch(ref laserAllClear).Remove(value); }
        public void RaiseLaserAllClear(GadgetEvent e) => Ch(ref laserAllClear).Raise(e);
        Channel<GadgetEvent> laserAllClear;

        /// <summary>A water volume's surface moved. Value: the surface height; Value2: the volume.</summary>
        public event Action<GadgetEvent> WaterLevelChanged { add => Ch(ref waterLevelChanged).Add(value); remove => Ch(ref waterLevelChanged).Remove(value); }
        public void RaiseWaterLevelChanged(GadgetEvent e) => Ch(ref waterLevelChanged).Raise(e);
        Channel<GadgetEvent> waterLevelChanged;

        /// <summary>A water volume passed water on. Value: how much this tick.</summary>
        public event Action<GadgetEvent> WaterOverflowing { add => Ch(ref waterOverflowing).Add(value); remove => Ch(ref waterOverflowing).Remove(value); }
        public void RaiseWaterOverflowing(GadgetEvent e) => Ch(ref waterOverflowing).Raise(e);
        Channel<GadgetEvent> waterOverflowing;

        /// <summary>Deep water swept the player away.</summary>
        public event Action<GadgetEvent> WaterSwept { add => Ch(ref waterSwept).Add(value); remove => Ch(ref waterSwept).Remove(value); }
        public void RaiseWaterSwept(GadgetEvent e) => Ch(ref waterSwept).Raise(e);
        Channel<GadgetEvent> waterSwept;

        /// <summary>A sponge started to drink.</summary>
        public event Action<GadgetEvent> SpongeSoaking { add => Ch(ref spongeSoaking).Add(value); remove => Ch(ref spongeSoaking).Remove(value); }
        public void RaiseSpongeSoaking(GadgetEvent e) => Ch(ref spongeSoaking).Raise(e);
        Channel<GadgetEvent> spongeSoaking;

        /// <summary>A sponge started to wring itself out.</summary>
        public event Action<GadgetEvent> SpongeWringing { add => Ch(ref spongeWringing).Add(value); remove => Ch(ref spongeWringing).Remove(value); }
        public void RaiseSpongeWringing(GadgetEvent e) => Ch(ref spongeWringing).Raise(e);
        Channel<GadgetEvent> spongeWringing;

        /// <summary>A sponge is full. Value: what it holds.</summary>
        public event Action<GadgetEvent> SpongeFull { add => Ch(ref spongeFull).Add(value); remove => Ch(ref spongeFull).Remove(value); }
        public void RaiseSpongeFull(GadgetEvent e) => Ch(ref spongeFull).Raise(e);
        Channel<GadgetEvent> spongeFull;

        /// <summary>A sponge holds nothing any more.</summary>
        public event Action<GadgetEvent> SpongeDry { add => Ch(ref spongeDry).Add(value); remove => Ch(ref spongeDry).Remove(value); }
        public void RaiseSpongeDry(GadgetEvent e) => Ch(ref spongeDry).Raise(e);
        Channel<GadgetEvent> spongeDry;

        /// <summary>A portal doorway stands and is open for business.</summary>
        public event Action<GadgetEvent> PortalSettled { add => Ch(ref portalSettled).Add(value); remove => Ch(ref portalSettled).Remove(value); }
        public void RaisePortalSettled(GadgetEvent e) => Ch(ref portalSettled).Raise(e);
        Channel<GadgetEvent> portalSettled;

        /// <summary>The player went through a portal doorway. Value: the scale before; Value2: after.</summary>
        public event Action<GadgetEvent> PortalCrossed { add => Ch(ref portalCrossed).Add(value); remove => Ch(ref portalCrossed).Remove(value); }
        public void RaisePortalCrossed(GadgetEvent e) => Ch(ref portalCrossed).Raise(e);
        Channel<GadgetEvent> portalCrossed;

        /// <summary>A portal doorway had no room for the player on either side.</summary>
        public event Action<GadgetEvent> PortalBlocked { add => Ch(ref portalBlocked).Add(value); remove => Ch(ref portalBlocked).Remove(value); }
        public void RaisePortalBlocked(GadgetEvent e) => Ch(ref portalBlocked).Raise(e);
        Channel<GadgetEvent> portalBlocked;

        /// <summary>A recall pad fetched its prop.</summary>
        public event Action<GadgetEvent> Recalled { add => Ch(ref recalled).Add(value); remove => Ch(ref recalled).Remove(value); }
        public void RaiseRecalled(GadgetEvent e) => Ch(ref recalled).Raise(e);
        Channel<GadgetEvent> recalled;

        /// <summary>A machine run began.</summary>
        public event Action<GadgetEvent> MachineStarted { add => Ch(ref machineStarted).Add(value); remove => Ch(ref machineStarted).Remove(value); }
        public void RaiseMachineStarted(GadgetEvent e) => Ch(ref machineStarted).Raise(e);
        Channel<GadgetEvent> machineStarted;

        /// <summary>A machine run stopped. Text: the last link that fired.</summary>
        public event Action<GadgetEvent> MachineFizzled { add => Ch(ref machineFizzled).Add(value); remove => Ch(ref machineFizzled).Remove(value); }
        public void RaiseMachineFizzled(GadgetEvent e) => Ch(ref machineFizzled).Raise(e);
        Channel<GadgetEvent> machineFizzled;

        /// <summary>A machine is set up again.</summary>
        public event Action<GadgetEvent> MachineReset { add => Ch(ref machineReset).Add(value); remove => Ch(ref machineReset).Remove(value); }
        public void RaiseMachineReset(GadgetEvent e) => Ch(ref machineReset).Raise(e);
        Channel<GadgetEvent> machineReset;

        /// <summary>A machine ran to its end.</summary>
        public event Action<GadgetEvent> MachineDone { add => Ch(ref machineDone).Add(value); remove => Ch(ref machineDone).Remove(value); }
        public void RaiseMachineDone(GadgetEvent e) => Ch(ref machineDone).Raise(e);
        Channel<GadgetEvent> machineDone;
    }
}
