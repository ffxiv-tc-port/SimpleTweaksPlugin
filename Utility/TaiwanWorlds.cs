using Lumina.Excel.Sheets;

namespace SimpleTweaksPlugin.Utility;

/// <summary>
/// Taiwan worlds all have <c>IsPublic == false</c>, and DC 151 also holds test worlds,
/// so the eight official worlds can only be identified by a hard-coded row id range.
/// </summary>
public static class TaiwanWorlds {
    public const byte Region = 8;

    public static bool IsOfficial(uint worldId) => worldId is >= 4028 and <= 4035;

    /// <summary><c>IsPublic</c>, or one of the Taiwan official worlds.</summary>
    public static bool IsPlayable(World world) => world.IsPublic || IsOfficial(world.RowId);
}
