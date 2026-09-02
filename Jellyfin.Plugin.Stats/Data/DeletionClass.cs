using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.Stats.Data;

/// <summary>
/// What a deletion says about the rows it removes, out of the two statements a
/// deletion here can be making.
/// </summary>
/// <remarks>
/// A closed set of this plugin's own, and a stored value. The numbering outlives
/// the assembly that wrote it, so a member is added at the end and no member's
/// number ever moves, the same rule <see cref="PlayClosedBy"/> is written under.
/// <para>
/// The two are different statements about the same rows. Retention says the raw
/// rows have aged out and every figure computed from them stands; a corrective
/// deletion says those plays stop being counted, so a figure that counted them
/// has to move. Nothing in the rows themselves separates the two, which is why
/// it is an argument: a store that inferred the class from which rows went, or
/// from who asked, would be reading the answer off the wrong thing.
/// </para>
/// <para>
/// No member is zero, and that is the point of the numbering rather than a
/// style. Zero is what an argument nobody supplied reads as, so a call that
/// passed <c>default</c> would otherwise arrive as a retention deletion and be
/// recorded as one. The store refuses a value it was not given a name for, so
/// the two ways of failing to choose - omitting the argument and passing a
/// number - fail at the compiler and at the store rather than being answered.
/// </para>
/// <para>
/// Issue #251.
/// </para>
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1008:Enums should have zero value",
    Justification = "The rule asks for a member at nought so that an unset field reads as something sensible, and here there is nothing sensible for it to read as. The two members say opposite things about whether a figure standing over the removed rows still holds, so a member at nought would be a third answer that means neither, stored on a deletion whose rows are gone and cannot be looked at again. Leaving nought unnamed is what turns the two ways of failing to choose into failures: omitting the argument does not compile, and passing default is refused where the deletion is performed rather than being recorded as retention.")]
public enum DeletionClass
{
    /// <summary>
    /// The rows aged out of the retention window.
    /// </summary>
    /// <remarks>
    /// The plays happened and are still counted. What has gone is this
    /// plugin's copy of the rows they were recorded in, which is what a
    /// retention window is for, and an aggregate standing over them was
    /// computed while they were there and remains true about what was watched.
    /// <para>
    /// It is the class of the sweep and not of the caller. A retention sweep
    /// that happens to remove the last rows of an account nobody has deleted is
    /// still retention.
    /// </para>
    /// <para>
    /// It is also the class of the sweep and not of the table. A daily aggregate
    /// deleted because it is older than the window configured for it went away
    /// for the same reason a play row does, so it is recorded here and not as
    /// corrective; recording it there would give one reason two names depending
    /// on which table the deletion happened in, which is the distinction this
    /// vocabulary exists to make unnecessary. Corrective belongs to the
    /// aggregate deletion that is already there for it: the one that drops a day
    /// a corrective deletion emptied.
    /// </para>
    /// <para>
    /// That a rollup can outlive the rows it was folded from, so that deleting
    /// it destroys the only remaining record of a period, is a consequence and
    /// not a second reason. It follows from which of the two windows an
    /// installation set longer rather than from anything about the deletion, so
    /// the same removal would carry different classes on two servers if it were
    /// written here. It is said beside the settings instead, in
    /// <c>docs/configuration.md</c> and <c>docs/plugin-data.md</c>. Issue #315.
    /// </para>
    /// </remarks>
    Retention = 1,

    /// <summary>
    /// The plays stop being counted.
    /// </summary>
    /// <remarks>
    /// A deleted account, or a person removing their own history. The statement
    /// is about the plays rather than about the copy of them: every figure that
    /// counted those plays is now a figure over rows somebody asked to have
    /// stop existing, so it has to move rather than stand.
    /// <para>
    /// It is the class of the removal and not of the window it fell in. A
    /// corrective deletion whose rows were also inside the retention window is
    /// still corrective, and answering it as retention would leave the figures
    /// that count those plays standing.
    /// </para>
    /// </remarks>
    Corrective = 2
}
