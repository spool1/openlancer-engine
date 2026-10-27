using LibreLancer.Missions;
using LibreLancer.Missions.Directives;
using LibreLancer.World;
using LibreLancer.World.Components;
using Xunit;

namespace LibreLancer.Tests;

public class DirectiveRunnerTests
{
    [Fact]
    public void CruiseContinuesBetweenGotoDirectives()
    {
        Assert.False(DirectiveRunnerComponent.ShouldStopAtTarget(
            GotoKind.GotoCruise, new GotoVecDirective()));
    }

    [Fact]
    public void CruiseStopsBeforeDelayOrEndOfList()
    {
        Assert.True(DirectiveRunnerComponent.ShouldStopAtTarget(
            GotoKind.GotoCruise, new DelayDirective()));
        Assert.True(DirectiveRunnerComponent.ShouldStopAtTarget(
            GotoKind.GotoCruise, null));
    }

    [Fact]
    public void NoCruiseGotoAlwaysStopsAtTarget()
    {
        Assert.True(DirectiveRunnerComponent.ShouldStopAtTarget(
            GotoKind.GotoNoCruise, new GotoVecDirective()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AvoidanceDirectiveUpdatesAutopilot(bool enabled)
    {
        var ship = new GameObject();
        var autopilot = new AutopilotComponent(ship);
        ship.AddComponent(autopilot);
        var runner = new DirectiveRunnerComponent(ship);

        runner.SetDirectives([new AvoidanceDirective { Avoidance = enabled }], null!);

        Assert.Equal(enabled, autopilot.AvoidanceEnabled);
    }

    // An order to dock with or to follow an object that is not in the world (OL-0074). The ship goes on to its next
    // order, and the game does not crash. A mission play can give an old order whose target is in another system.
    [Theory]
    [InlineData("dock")]
    [InlineData("follow")]
    public void AnOrderWithAMissingTargetGoesOnToTheNextOrder(string kind)
    {
        using var world = new GameWorld(null, null, null, null, initPhys: false);
        var ship = new GameObject { Nickname = "ol_ship" };
        var autopilot = new AutopilotComponent(ship);
        ship.AddComponent(autopilot);
        var runner = new DirectiveRunnerComponent(ship);
        MissionDirective order = kind == "dock"
            ? new DockDirective { Target = "ol_missing_gate" }
            : new FollowDirective { Target = "ol_missing_ship" };

        runner.SetDirectives([order, new AvoidanceDirective { Avoidance = true }], world);

        Assert.True(autopilot.AvoidanceEnabled);
        Assert.Null(ship.Formation);
    }
}
