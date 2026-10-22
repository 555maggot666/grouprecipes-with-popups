using BepInEx;

namespace GroupRecipesWithPopups.Harness
{
    /// <summary>
    /// BepInEx only loads an assembly from <c>BepInEx/plugins</c> when it declares a plugin. This stub exists so
    /// the assembly (and with it <see cref="GroupRecipesSuite"/>) is loaded and can be discovered by
    /// ValheimTestKit. It intentionally does nothing.
    /// </summary>
    [BepInPlugin(PluginGuid, "GRWP Test Suite", "1.0.0")]
    [BepInDependency("555maggot666.ValheimTestKit", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("555maggot666.GroupRecipesWithPopups", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class SuiteLoader : BaseUnityPlugin
    {
        public const string PluginGuid = "555maggot666.GroupRecipesWithPopups.Suite";
    }
}
