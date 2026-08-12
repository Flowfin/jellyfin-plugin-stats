# Reason counts add up to more than the plays, and that is the right answer

A play that was not passed through carries every reason the server gave for it,
and a server usually gives more than one. A play whose audio codec is
unsupported on the client and whose container the client cannot read carries
both reasons. So a breakdown of reasons over a range is a count of reason
sightings, not a count of plays, and adding the rows up gives a number larger
than the number of plays in the same range.

This is written down because the two numbers appear next to each other on the
same page, and a reader who meets them without this sentence concludes the
plugin is counting wrong.

## The two questions, and why only one of them adds up to the play count

**How each play was delivered** has exactly one answer per play. The server
reports a method at the start and it is kept as it was read:

    git grep -n "Unknown = 0\|DirectPlay = 1\|DirectStream = 2\|Transcode = 3" -- Jellyfin.Plugin.Stats/Data/PlayMethod.cs
    Jellyfin.Plugin.Stats/Data/PlayMethod.cs:16:    Unknown = 0,
    Jellyfin.Plugin.Stats/Data/PlayMethod.cs:21:    DirectPlay = 1,
    Jellyfin.Plugin.Stats/Data/PlayMethod.cs:27:    DirectStream = 2,
    Jellyfin.Plugin.Stats/Data/PlayMethod.cs:32:    Transcode = 3

Four values, one per play, so the four shares do add up to the play count for
the range. A play the server never reported a method for is reported as unknown
and is never counted as direct, which was decided on issue #53 on 2026-08-09:
counting missing information as the good outcome makes a chart where an absent
answer looks like success.

**Why a play was not passed through** has as many answers per play as the
server gave. They are kept as a list on the play rather than as one value:

    git grep -n "Reasons { get; init; }" -- Jellyfin.Plugin.Stats/Data/TranscodeSummary.cs
    Jellyfin.Plugin.Stats/Data/TranscodeSummary.cs:65:    public required IReadOnlyList<string> Reasons { get; init; }

So the reason breakdown and the method shares are answers to different
questions, and only the second is a partition of the plays.

## What the list does and does not repeat

Within one play a reason appears once, however many times the server repeated
it while the play ran. The fold that collects them checks before it adds:

    git grep -n "if (!_reasons.Contains(name, StringComparer.Ordinal))" -- Jellyfin.Plugin.Stats/Capture/TranscodeFold.cs
    Jellyfin.Plugin.Stats/Capture/TranscodeFold.cs:209:            if (!_reasons.Contains(name, StringComparer.Ordinal))

That matters for what the breakdown means. A row saying a reason was seen four
hundred times is four hundred plays that hit it, not four hundred progress
reports on one long film. What it is not is a partition, because the same four
hundred plays are also counted under every other reason they carried.

Reasons are taken as the server reported them and are never worked out from the
codecs afterwards. A reason the plugin inferred would be a guess presented in
the same column as an observation, and an administrator acting on the chart
cannot tell the two apart.

## What is not built yet

Both halves of the arithmetic above are now written down in code rather than
only here. `DeliveryMethodShares` folds a sequence of rows into the four figures
and counts the rows it was given, so what it reports adds up to the plays it
read. `TranscodeReasonBreakdown` folds the same sequence into one row per reason
and counts the plays under each, so the rows add up to more:

    git grep -n "public static DeliveryMethodShares Over\|public static TranscodeReasonBreakdown Over" -- Jellyfin.Plugin.Stats/Aggregation/
    Jellyfin.Plugin.Stats/Aggregation/DeliveryMethodShares.cs:94:    public static DeliveryMethodShares Over(IEnumerable<PlayRecord> plays)
    Jellyfin.Plugin.Stats/Aggregation/TranscodeReasonBreakdown.cs:85:    public static TranscodeReasonBreakdown Over(IEnumerable<PlayRecord> plays)

Both take a sequence and not a range, because choosing the range is a query and
there is none. They are the arithmetic under a report rather than reports, and
nothing calls either of them. No query layer exists and no endpoint exists:

    git grep -lE "ControllerBase|ApiController|HttpGet|HttpPost" -- '*.cs'
    tools/invariants/near-miss/no-query-from-the-request/SecondSortOrder.cs

The single hit is a near miss under `tools/invariants`, which is not compiled
into either project.

So this document describes what the stored rows support and what a report over
them will therefore say. It is not a report that one exists. Issue #53 stays
open on the shares over a range, on the split by client under the consent rule,
and on serving any of it; issue #51 holds the query layer all three need.

One thing the reason fold does not do is invent a row for a play that recorded
nothing. Most plays record no reason because the server passed them through and
there was nothing to report, and the fold reads the summary rather than the
delivery method, so it cannot tell those from a play that was re-encoded and
reported no reason. It reports how many plays recorded any reason at all, and
telling the two apart means reading the reasons and the method together, which
is a comparison they can lose: issue #158 holds the case where they disagree.

## What keeps this document true

`TranscodeReasonDocumentTests` reads this file against the row. A delivery
method added without this document naming it fails the suite, and so does a
rewrite that drops the sentence about the counts, so the part a reader relies on
cannot go stale quietly.

The other half of the basis is held by the compiler rather than by a test.
Reasons being a collection on the play is what makes the counts exceed the
plays, and changing that to one value stops the fold and the store compiling
before any assertion could run. There is no test for it here, because a test
that cannot be shown to fire for the reason it names is not worth the line.

Neither of them reaches the arithmetic. Each fold has a suite of its own for
that. `DeliveryMethodSharesTests` holds what this document claims about the
delivery half: the four figures add up to the sequence the fold was handed, a
play the server reported no method for is counted as unknown and never as
direct, and a row carrying a method this build has no name for is counted rather
than dropped.

`TranscodeReasonBreakdownTests` holds the reason half, and the claim it is
really about is the sentence at the top of this file. A play that recorded
several reasons is counted under each of them, so three plays carrying two
reasons each produce rows totalling six against a play count of three, which is
this document's point as an assertion rather than as prose. Beside it: a play is
counted once under a reason however often the stored row repeats it, a play that
recorded nothing is in the play count and under no row, and two spellings of one
name are two rows, because a fold that tidied them would be inferring an
equivalence nobody reported.
