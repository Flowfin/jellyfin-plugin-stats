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
 * caption and is asserted here rather than left to a reader of the source.
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
function row(reason, plays) {
    return { reason, plays };
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
    const drawn = whyTheServerTranscodes(ready([row('VideoCodecNotSupported', 4)]));

    assert.match(drawn, /<title>VideoCodecNotSupported: 4<\/title>/);
    assert.match(drawn, />VideoCodecNotSupported<\/text>/);
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
    const drawn = whyTheServerTranscodes(ready([row('SomethingALaterServerReports', 5)]));

    assert.match(drawn, /<title>SomethingALaterServerReports: 5<\/title>/);
    assert.match(drawn, /<dt class="stats-view-reason-name">SomethingALaterServerReports<\/dt>/);
    assert.match(drawn, /This build has no sentence for this reason/);
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
    const failed = whyTheServerTranscodes({
        state: 'failed',
        reason: 'The store could not be opened.',
    });

    assert.equal(new Set([ready4, empty, loading, failed]).size, 4);
    assert.match(empty, /Nothing recorded yet/);
    assert.match(loading, /Still loading/);
    assert.match(failed, /Could not be read/);
    assert.match(failed, /The store could not be opened\./);
});

test('an answer that names no state is refused rather than drawn as ready', () => {
    assert.throws(
        () => whyTheServerTranscodes({ rows: [row('ContainerNotSupported', 1)] }),
        /state/,
    );
    assert.throws(() => whyTheServerTranscodes(null), /state/);
});
