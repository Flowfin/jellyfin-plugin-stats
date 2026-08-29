/*
 * The transcode reason view, read as a function from figures to markup.
 *
 * The conditions of issue #60 are what these are written against. The first,
 * that every reason the server can report has a sentence, is held at the other
 * end: this file cannot see the server's enum, and TranscodeReasonSentenceTests
 * in the .NET suite walks it and asks the module for each member. What is held
 * here is everything about that list a reader meets on the page: that the
 * sentence for a drawn reason is drawn with it, and that a reason with no
 * sentence is still counted and said to be unexplained rather than dropped or
 * invented for.
 *
 * The second condition, that the page says one play can carry several reasons
 * and the shares therefore total more than the whole, is a sentence in the
 * caption and is asserted here rather than left to a reader of the source. It
 * is asserted for the watched time as well as for the plays, which is issue
 * #242: the time under a reason is the whole of every play carrying it, so the
 * column totals more than the range holds and the page says so.
 *
 * The description's two remaining halves are here too: the bars are the watched
 * time the fold ordered the rows by, and what the server re-encoded with is
 * listed under them rather than drawn as a second set of bars, because that one
 * is a partition and the reasons are not.
 *
 * The third, that the view names no user, is asserted the way the client and
 * device view asserts its own: a row is handed fields the view has no business
 * with and the markup is checked for them. The fold's row type has nowhere to
 * put a user either, which is TranscodeReasonBreakdownTests in the .NET suite.
 *
 * Run with the test runner built into node:
 *
 *     node --test Jellyfin.Plugin.Stats.Tests/Pages/
 */

import assert from 'node:assert/strict';
import test from 'node:test';
import { whyTheServerTranscodes } from '../../Jellyfin.Plugin.Stats/Pages/whyTheServerTranscodes.js';

/**
 * One row as the fold produces it.
 *
 * @param {string} reason The reason, as the server spelled it.
 * @param {number} plays How many plays recorded it.
 * @returns {object} The row.
 */
function row(reason, plays, watchedMinutes = 60) {
    return { reason, plays, watchedMinutes };
}

/**
 * A ready answer over the rows given.
 *
 * @param {ReadonlyArray<object>} rows The rows.
 * @param {{plays?: number, playsWithAReason?: number}} [counts] The two counts the fold carries.
 * @returns {object} The answer.
 */
function ready(rows, counts = {}) {
    return {
        state: 'ready',
        plays: counts.plays ?? 10,
        playsWithAReason: counts.playsWithAReason ?? rows.length,
        watchedMinutes: counts.watchedMinutes ?? 600,
        rows,
    };
}

test('the view draws with no document present', () => {
    assert.equal(typeof document, 'undefined');
    assert.equal(typeof window, 'undefined');

    assert.match(whyTheServerTranscodes(ready([row('ContainerNotSupported', 3)])), /^<figure /);
});

test('a reason is drawn under the name the server gave it, unchanged', () => {
    /* The names are not tidied anywhere in this plugin, and the page is the last
     * place they could be. An administrator meets these words in the server's
     * own log, and a prettier spelling here is one they cannot look up. */
    const drawn = whyTheServerTranscodes(ready([row('VideoCodecNotSupported', 4, 75)]));

    assert.match(drawn, /<title>VideoCodecNotSupported: 75<\/title>/);
    assert.match(drawn, />VideoCodecNotSupported<\/text>/);
});

test('the bar is the watched time and the play count is beside it', () => {
    /* Issue #60 asks for the rows ordered by watched time. A row ordered by one
     * figure and drawn at the length of another is a picture that contradicts
     * its own order, so the bar is that same figure; the count is still there,
     * because the two readings disagree and that is the part worth seeing. */
    const drawn = whyTheServerTranscodes(
        ready([row('ContainerNotSupported', 400, 400), row('VideoCodecNotSupported', 4, 480)]),
    );

    assert.match(drawn, /<title>ContainerNotSupported: 400<\/title>/);
    assert.match(drawn, /<title>VideoCodecNotSupported: 480<\/title>/);
    assert.match(drawn, /400 plays\. 400 minutes watched/);
    assert.match(drawn, /4 plays\. 480 minutes watched/);
});

test('a row with no watched time is drawn as an absent bar and never as nought', () => {
    /* The drawing module tells an absent reading from a nought one, and a bar of
     * nought under a reason the answer carried no time for is the reading this
     * view has no right to make. Issue #64. */
    const drawn = whyTheServerTranscodes(ready([{ reason: 'ContainerNotSupported', plays: 3 }]));

    assert.doesNotMatch(drawn, /<title>ContainerNotSupported: 0<\/title>/);
});

test('what the server re-encoded with is listed under the bars, and says it adds up', () => {
    /* The second half of issue #60's description. It is a list and not a second
     * set of bars because it is a partition and the reasons are not, and two
     * pictures of one range invite the addition the caption refuses. */
    const drawn = whyTheServerTranscodes({
        ...ready([row('ContainerNotSupported', 3, 110)]),
        acceleration: [
            { type: 'qsv', plays: 2, watchedMinutes: 90 },
            { type: null, plays: 1, watchedMinutes: 20 },
        ],
    });

    assert.match(drawn, /<dt class="stats-view-acceleration-name">qsv<\/dt>/);
    assert.match(drawn, /2 plays, 90 minutes watched\./);
    assert.match(drawn, /<dt class="stats-view-acceleration-name">No acceleration reported<\/dt>/);
    assert.match(drawn, /cannot tell the two apart/);
    assert.match(drawn, /unlike the bars above these rows do add up to the plays in this range/);
});

test('an answer carrying no acceleration list draws none rather than an empty one', () => {
    const drawn = whyTheServerTranscodes(ready([row('ContainerNotSupported', 3, 110)]));

    assert.doesNotMatch(drawn, /stats-view-accelerations-list/);
    assert.doesNotMatch(drawn, /No acceleration reported/);
});

test('an acceleration name carrying markup is drawn as text', () => {
    const drawn = whyTheServerTranscodes({
        ...ready([row('ContainerNotSupported', 1, 10)]),
        acceleration: [{ type: '<script>alert(1)</script>', plays: 1, watchedMinutes: 10 }],
    });

    assert.doesNotMatch(drawn, /<script>/);
    assert.match(drawn, /&lt;script&gt;/);
});

test('each drawn reason carries its sentence beside it', () => {
    const drawn = whyTheServerTranscodes(
        ready([row('ContainerNotSupported', 6), row('AudioChannelsNotSupported', 2)]),
    );

    assert.match(drawn, /<dt class="stats-view-reason-name">ContainerNotSupported<\/dt>/);
    assert.match(drawn, /does not read the file format the media is packaged in/);
    assert.match(drawn, /<dt class="stats-view-reason-name">AudioChannelsNotSupported<\/dt>/);
    assert.match(drawn, /has more channels than the client plays/);
});

test('a reason this build has no sentence for is counted and said to be unexplained', () => {
    /* A stored row outlives the assembly that wrote it and a newer server
     * reports names this build never saw. Dropping the row would take plays out
     * of the picture the whole view is about, and writing a sentence for it
     * would put a guess in the column an administrator acts on. */
    const drawn = whyTheServerTranscodes(ready([row('SomethingALaterServerReports', 5, 55)]));

    assert.match(drawn, /<title>SomethingALaterServerReports: 55<\/title>/);
    assert.match(drawn, /<dt class="stats-view-reason-name">SomethingALaterServerReports<\/dt>/);
    assert.match(drawn, /This build has no sentence for this reason/);
});

test('one play carrying several reasons puts its whole watched time under each', () => {
    /* The fixture issue #242 asks for: one play of ninety minutes with three
     * reasons on it. The fold counts ninety under each of the three and the
     * range still holds ninety, so the page shows the figure three times and
     * says why that is not a miscount. A view that divided would print thirty
     * and a reader adding the column up would get the right total for the
     * wrong reason. */
    const drawn = whyTheServerTranscodes(
        ready(
            [
                row('ContainerNotSupported', 1, 90),
                row('VideoCodecNotSupported', 1, 90),
                row('AudioCodecNotSupported', 1, 90),
            ],
            { plays: 1, playsWithAReason: 1, watchedMinutes: 90 },
        ),
    );

    assert.equal(drawn.match(/90 minutes watched/g).length, 3);
    assert.doesNotMatch(drawn, /30 minutes watched/);
    assert.match(drawn, /the whole of every play under this reason and no share of one/);
    assert.match(drawn, /these times can total more than the 90 minutes this range holds/);
});

test('the caption says the times are counted the same way as the bars', () => {
    const drawn = whyTheServerTranscodes(
        ready([row('ContainerNotSupported', 8, 400), row('VideoCodecNotSupported', 6, 300)], {
            plays: 10,
            playsWithAReason: 9,
            watchedMinutes: 500,
        }),
    );

    assert.match(drawn, /A play carrying three reasons puts the whole of its watched time/);
    assert.match(drawn, /these times can total more than the 500 minutes this range holds/);
    assert.match(drawn, /a divided figure is a length of time nobody watched/);
});

test('the period in the caption is the one the answer carries, never a sum over the rows', () => {
    /* Adding the rows up gives seven hundred minutes over a range holding five
     * hundred, and printing that as the range is the misreading the sentence
     * exists to stop. */
    const drawn = whyTheServerTranscodes(
        ready([row('ContainerNotSupported', 8, 400), row('VideoCodecNotSupported', 6, 300)], {
            plays: 10,
            playsWithAReason: 9,
            watchedMinutes: 500,
        }),
    );

    assert.doesNotMatch(drawn, /700 minutes this range/);
});

test('a row carrying no watched time says so rather than saying nought', () => {
    /* A reason whose plays nobody watched a minute of and a reason the answer
     * carried no time for are different facts. Issue #64. */
    const drawn = whyTheServerTranscodes(
        ready([{ reason: 'ContainerNotSupported', plays: 3 }], { watchedMinutes: 120 }),
    );

    assert.match(drawn, /Watched time not recorded for this reason\./);
    assert.doesNotMatch(drawn, /0 minutes watched/);
});

test('a watched time with a tail of digits is drawn to a tenth of a minute', () => {
    /* The fold sums ticks and divides once, so an ordinary range arrives here
     * carrying more digits than a dashboard reader wants. */
    const drawn = whyTheServerTranscodes(
        ready([row('ContainerNotSupported', 3, 41.666666666666664)], {
            watchedMinutes: 41.666666666666664,
        }),
    );

    assert.match(drawn, /41\.7 minutes watched/);
    assert.doesNotMatch(drawn, /41\.66/);
});

test('the caption says the bars total more than the plays', () => {
    const drawn = whyTheServerTranscodes(
        ready([row('ContainerNotSupported', 8), row('VideoCodecNotSupported', 6)], {
            plays: 10,
            playsWithAReason: 9,
        }),
    );

    assert.match(drawn, /2 reasons over 9 of the 10 plays in this range/);
    assert.match(drawn, /add up to more than the plays they came from/);
    assert.match(drawn, /not a division of the plays/);
});

test('the counts in the caption are the ones the answer carries, never a sum over the bars', () => {
    /* Adding the bars up gives a number larger than the plays, and printing
     * that number as a play count is exactly the misreading the caption exists
     * to prevent. The fold counts the plays it was handed separately, so the
     * caption has a figure to print that is not the sum. */
    const drawn = whyTheServerTranscodes(
        ready([row('ContainerNotSupported', 8), row('VideoCodecNotSupported', 6)], {
            plays: 10,
            playsWithAReason: 9,
        }),
    );

    assert.doesNotMatch(drawn, /14 plays/);
});

test('the rows are drawn in the order they arrived and are not sorted again', () => {
    const drawn = whyTheServerTranscodes(
        ready([row('AudioIsExternal', 1), row('ContainerNotSupported', 9)]),
    );

    assert.ok(
        drawn.indexOf('>AudioIsExternal</text>') < drawn.indexOf('>ContainerNotSupported</text>'),
        'The view reordered rows the fold had already ordered, so the order is decided twice ' +
            'and the two can disagree.',
    );
});

test('the view names no user, whatever a row arrives carrying', () => {
    const drawn = whyTheServerTranscodes(
        ready([
            {
                reason: 'ContainerNotSupported',
                plays: 3,
                watchedMinutes: 120,
                userId: '9f1b7f9c-2f42-4c2d-9d3d-1b2f3a4b5c6d',
                userName: 'Ada',
            },
        ]),
    );

    assert.doesNotMatch(drawn, /Ada/);
    assert.doesNotMatch(drawn, /9f1b7f9c/);
});

test('a range with rows but no reasons in them draws no list of sentences', () => {
    const drawn = whyTheServerTranscodes(ready([], { plays: 12, playsWithAReason: 0 }));

    assert.doesNotMatch(drawn, /stats-view-reasons-list/);
    assert.match(drawn, /0 reasons over 0 of the 12 plays in this range/);
});

test('a name carrying markup is drawn as text', () => {
    const drawn = whyTheServerTranscodes(ready([row('<script>alert(1)</script>', 2)]));

    assert.doesNotMatch(drawn, /<script>/);
    assert.match(drawn, /&lt;script&gt;/);
});

test('each of the four situations is drawn as itself', () => {
    const ready4 = whyTheServerTranscodes(ready([row('ContainerNotSupported', 1)]));
    const empty = whyTheServerTranscodes({ state: 'empty' });
    const loading = whyTheServerTranscodes({ state: 'loading' });
    const failed = whyTheServerTranscodes({ state: 'failed' });

    assert.equal(new Set([ready4, empty, loading, failed]).size, 4);
    assert.match(empty, /Nothing recorded yet/);
    assert.match(loading, /Still loading/);
    assert.match(failed, /Statistics unavailable/);
    assert.match(failed, /operator has the details/);
});

test('an answer that names no state is refused rather than drawn as ready', () => {
    assert.throws(
        () => whyTheServerTranscodes({ rows: [row('ContainerNotSupported', 1)] }),
        /state/,
    );
    assert.throws(() => whyTheServerTranscodes(null), /state/);
});
