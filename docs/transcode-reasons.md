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

## The watched time under a reason does the same thing

The row carries how long the play was actually watched for, and a breakdown by
reason answers that question the same way it answers the play count: the whole
of a play's watched time goes under every reason it carries. A ninety-minute
play with three reasons on it puts ninety minutes under each of the three, so
the column totals two hundred and seventy over a range that holds ninety.

    git grep -n "watched\[reason\] = watchedSoFar + ticks;" -- Jellyfin.Plugin.Stats/Aggregation/TranscodeReasonBreakdown.cs
    Jellyfin.Plugin.Stats/Aggregation/TranscodeReasonBreakdown.cs:225:                watched[reason] = watchedSoFar + ticks;

Decided on issue #60 on 2026-08-24 and built under #242. The alternative was
dividing the play's time between its reasons, and it was refused for the reason
an inferred reason is refused two paragraphs below: thirty minutes under the
container is a length of time nobody watched. The server did not spend a third
of that play on the container and the rest on the codecs; it re-encoded one play
under all three conditions at once, and every figure the division produced would
be arithmetic on top of an observation rather than the observation.

What it costs is that the times are not a partition either, which is this
document's opening sentence applied to a second column. The fold therefore
carries the period as its own figure rather than leaving it to be summed:

    git grep -n "public double WatchedMinutes\b" -- Jellyfin.Plugin.Stats/Aggregation/TranscodeReasonBreakdown.cs
    Jellyfin.Plugin.Stats/Aggregation/TranscodeReasonBreakdown.cs:153:    public double WatchedMinutes { get; }

and the view that draws the rows says the same thing in a sentence a dashboard
reader meets, rather than pointing at this file.

## What the rows are ordered by, and what does add up

The rows are ordered by the watched time under each and not by the plays. Four
hundred one-minute plays under one reason and four two-hour plays under another
are a hundred to one on a count and the other way round on the time, and only
the second is a server spending its evening re-encoding. Both figures are
carried, because the two readings disagree and the disagreement is the part
worth seeing; what the ordering settles is which of them decides the height of a
bar.

Beside the reasons is what the server re-encoded with, and that one IS a
partition. A play carries every reason the server gave and exactly one hardware
acceleration, so those rows add up to the plays they came from and to the
minutes:

    git grep -n "public IReadOnlyList<TranscodeAccelerationCount> Acceleration" -- Jellyfin.Plugin.Stats/Aggregation/TranscodeReasonBreakdown.cs
    Jellyfin.Plugin.Stats/Aggregation/TranscodeReasonBreakdown.cs:133:    public IReadOnlyList<TranscodeAccelerationCount> Acceleration { get; }

It is a separate list rather than a column on a reason row for exactly that
reason. Crossing the two produces a figure that is neither: the plays under one
reason are not divided between accelerations without the invention the watched
time refuses two paragraphs above.

The row for plays the server reported no acceleration for is last and is not
called software. It holds a play the server passed through untouched as well as
one it re-encoded on the processor, because this fold reads the summary and not
the delivery method, and issue #158 is where those two accounts of one row
disagreeing lives.

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
    Jellyfin.Plugin.Stats/Aggregation/TranscodeReasonBreakdown.cs:174:    public static TranscodeReasonBreakdown Over(IEnumerable<PlayRecord> plays)

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
this document's point as an assertion rather than as prose. The watched time is
held there too, by a case handing the fold one ninety-minute play with four
reasons on it and asserting ninety under each of the four against a period of
ninety. Beside it: a play is counted once under a reason however often the
stored row repeats it, a play that recorded nothing is in the play count and
under no row, a play watched for no time is still a play under its reasons, and
two spellings of one name are two rows, because a fold that tidied them would be
inferring an equivalence nobody reported. The ordering and the partition above
are held there as well: a rare reason that cost hours outranks a common one that
cost minutes, the acceleration rows add up to the plays while the reason rows do
not, and the plays with no reported acceleration are their own row and come
last.
