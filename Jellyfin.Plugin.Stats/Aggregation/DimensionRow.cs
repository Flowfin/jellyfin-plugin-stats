namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// One member of a dimension and how the plays under it were delivered.
/// </summary>
/// <remarks>
/// Nothing here identifies a person. That is a property of the type rather than
/// of the code that fills it in: there is no field for a user, so a breakdown
/// cannot carry one by being written carelessly, and adding one is a change
/// somebody has to make on purpose and defend.
/// </remarks>
/// <param name="Key">
/// What the plays were grouped on, which is the client's own name or the
/// server's identifier for the device. Empty where the server reported neither,
/// which is the row every unattributed play falls into rather than being
/// dropped out of the answer.
/// </param>
/// <param name="Name">
/// What to call the row, spelled as the server reported it, and null where the
/// server reported nothing to call it. Null rather than the word unknown: a
/// client genuinely named "Unknown" is a real client, and a made-up label would
/// fold it into the group of plays nobody could name. Whoever draws the row
/// decides the wording, the same way the page module writes a value it does not
/// know as null and never as zero.
/// </param>
/// <param name="Delivery">
/// How the plays under this row divide between the ways the server delivered
/// them, and how many there were. It is the same four figures the whole range
/// answers with, so a client's transcoded share and the server's are read the
/// same way rather than being two definitions that drift.
/// </param>
public sealed record DimensionRow(string Key, string? Name, DeliveryMethodShares Delivery);
