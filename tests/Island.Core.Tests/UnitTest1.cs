using Island.Core.Configuration;
using Island.Core.Contracts;
using Island.Core.Domain;
using Island.Core.Theming;

namespace Island.Core.Tests;

public sealed class IslandStateAndModelsTests
{
    [Fact]
    public void CreateInitial_ReturnsCompactPassiveIdleNormalState()
    {
        var now = new DateTimeOffset(2026, 10, 29, 16, 0, 0, TimeSpan.Zero);
        var state = IslandStateSnapshot.CreateInitial(now);

        Assert.Equal(IslandVisibility.Compact, state.Visibility);
        Assert.Equal(IslandInteractionState.Passive, state.Interaction);
        Assert.Equal(IslandContentKind.Idle, state.Content);
        Assert.Equal(IslandPolicyMode.Normal, state.Policy);
        Assert.False(state.IsUserProtected);
        Assert.Equal(now, state.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(IslandInteractionState.Passive, false)]
    [InlineData(IslandInteractionState.ActiveInUse, true)]
    [InlineData(IslandInteractionState.PinnedOpen, true)]
    public void IsUserProtected_ReflectsInteractionDimension(IslandInteractionState interaction, bool expectedProtected)
    {
        var state = IslandStateSnapshot.CreateInitial(DateTimeOffset.UtcNow) with
        {
            Interaction = interaction
        };

        Assert.Equal(expectedProtected, state.IsUserProtected);
    }

    [Fact]
    public void CreateDefaultSettings_DisablesPrivateDiagnosticsByDefault()
    {
        var settings = AppSettingsDocument.CreateDefault();

        Assert.Equal(AppSettingsDocument.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal(BuiltInThemes.DefaultSolidThemeId, settings.SelectedThemeId);
        Assert.False(settings.DiagnosticsIncludePrivateContent);
        Assert.Contains(IntegrationModuleId.Media, settings.EnabledModules);
        Assert.Contains(IntegrationModuleId.Notifications, settings.EnabledModules);
    }
}
