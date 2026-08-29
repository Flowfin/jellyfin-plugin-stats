/*
 * The client and device view, read as a function from figures to markup.
 *
 * The conditions of issue #59 are what these are written against. The first,
 * that the breakdown identifies clients and devices and no user, is held at both
 * ends: the fold's row type has nowhere to put a user, which DimensionBreakdownTests
 * covers in the .NET suite, and this end is that the view reads two fields off a
 * row and looks at no third. The third condition, that a value nobody reported is
 * shown as such and is counted rather than dropped, is the drawing's half and is
 * asserted here.
 *
 * The second condition is not here and is not a view property. A client with a
 * single user behind it not becoming a way to read that user is a rule about
 * which rows a breakdown may return at all, which the issue places in the query
 * layer.
 *
 * Run with the test runner built into node:
 *
 *     node --test Jellyfin.Plugin.Stats.Tests/Pages/
 */

import assert from 'node:assert/strict';
import test from 'node:test';
import { clientsAndDevices } from '../../Jellyfin.Plugin.Stats/Pages/clientsAndDevices.js';

/**
 * One row as the fold produces it, with the delivery figures filled in.
 *
 * @param {string|null} name What the server called the member.
 * @param {{plays: number, unknown?: number, transcode?: number}} figures Its plays.
 * @returns {object} The row.
 */
function row(name, figures) {
    return {
        name,
        delivery: {
            plays: figures.plays,
            unknown: figures.unknown ?? 0,
            directPlay: figures.plays - (figures.unknown ?? 0) - (figures.transcode ?? 0),
            directStream: 0,
            transcode: figures.transcode ?? 0,
        },
    };
}

test('the view draws with no document present', () => {
    assert.equal(typeof document, 'undefined');
    assert.equal(typeof window, 'undefined');

    assert.match(
        clientsAndDevices({
            state: 'ready',
            dimension: 'client',
            plays: 3,
            rows: [row('Jellyfin Web', { plays: 3, transcode: 1 })],
        }),
        /^<figure /,
    );
});

test('a member the server named nothing for is drawn and counted, not dropped', () => {
    const drawn = clientsAndDevices({
        state: 'ready',
        dimension: 'client',
        plays: 7,
        rows: [row('Jellyfin Web', { plays: 5, transcode: 2 }), row(null, { plays: 2 })],
    });

    assert.match(drawn, /<title>No client reported: 2<\/title>/);
    assert.match(drawn, />No client reported<\/text>/);
    assert.match(drawn, /over 7 plays in this range, and every play is in exactly one of them/);
});

test('a client whose name is Unknown stays a different bar from the one nobody named', () => {
    /* The fold leaves this row nameless rather than writing the word, and this
     * is the case it left it for. A view labelling the nameless row "Unknown"
     * would draw one client and one group of plays nobody could attribute under
     * a single heading, and the reader has no way back from that. */
    const drawn = clientsAndDevices({
        state: 'ready',
        dimension: 'client',
        plays: 9,
        rows: [row('Unknown', { plays: 6, transcode: 1 }), row(null, { plays: 3 })],
    });

    assert.match(drawn, /<title>Unknown: 6<\/title>/);
    assert.match(drawn, /<title>No client reported: 3<\/title>/);
});

test('a device with no name reported is drawn under the words rather than an identifier', () => {
    const drawn = clientsAndDevices({
        state: 'ready',
        dimension: 'device',
        plays: 4,
        rows: [row('   ', { plays: 4, transcode: 4 })],
    });

    assert.match(drawn, /<title>No device reported: 4<\/title>/);
    assert.doesNotMatch(drawn, /No client reported/);
});

test('a member whose delivery was never reported has no re-encoded bar rather than a nought', () => {
    const drawn = clientsAndDevices({
        state: 'ready',
        dimension: 'client',
        plays: 5,
        rows: [row('Roku', { plays: 5, unknown: 5 })],
    });

    /* The upper drawing still counts the plays, because they happened. The lower
     * one says nothing, because a bar at nought there reads as a server that
     * re-encoded none of them, and what is true is that it never said. */
    assert.match(drawn, /<title>Roku: 5<\/title>/);
    assert.doesNotMatch(drawn, /<title>Roku: 0<\/title>/);
    assert.match(drawn, /1 of 1 not recorded/);
});

test('plays with no delivery method are counted in words rather than left in the picture', () => {
    const some = clientsAndDevices({
        state: 'ready',
        dimension: 'client',
        plays: 10,
        rows: [row('Jellyfin Web', { plays: 10, unknown: 4, transcode: 3 })],
    });

    assert.match(some, /<title>Jellyfin Web: 3<\/title>/);
    assert.match(some, /no delivery method for 4 of those plays/);
    assert.match(
        some,
        /counts the plays known to have been re-encoded and not the plays that were/,
    );

    const none = clientsAndDevices({
        state: 'ready',
        dimension: 'client',
        plays: 10,
        rows: [row('Jellyfin Web', { plays: 10, transcode: 3 })],
    });

    assert.match(none, /reported how it delivered every one of them/);
    assert.doesNotMatch(none, /no delivery method for/);
});

test('the count under the picture is the one the answer carries, not the sum of the bars', () => {
    /* The fold counts the plays it was handed separately from the rows it
     * produced, so that a fold which lost a row shows. A view adding the bars up
     * and calling that the total would put the two statements back together and
     * the loss would be invisible again. */
    const drawn = clientsAndDevices({
        state: 'ready',
        dimension: 'client',
        plays: 12,
        rows: [row('Jellyfin Web', { plays: 5 })],
    });

    assert.match(drawn, /1 over 12 plays in this range/);
});

test('the view names no user, whatever the rows carry', () => {
    const drawn = clientsAndDevices({
        state: 'ready',
        dimension: 'client',
        plays: 2,
        rows: [
            {
                name: 'Jellyfin Web',
                /* None of these is a field the fold produces. They are here
                 * because the shape that leaks is a view composing its own text
                 * out of a row, and a view tested only with well-formed rows
                 * would not show it. Measured by writing that shape: a caption
                 * naming the first row's user reddens this test and nothing else
                 * in the file. */
                key: 'jellyfin-web',
                userName: 'Ada',
                userId: '6f9619ff-8b86-d011-b42d-00c04fc964ff',
                itemName: 'An episode',
                delivery: { plays: 2, unknown: 0, directPlay: 1, directStream: 0, transcode: 1 },
            },
        ],
    });

    assert.doesNotMatch(drawn, /Ada/);
    assert.doesNotMatch(drawn, /6f9619ff/);
    assert.doesNotMatch(drawn, /An episode/);
    assert.doesNotMatch(drawn, /jellyfin-web/);
});

test('the group too few accounts stand behind is drawn, said to be a group, and counted', () => {
    /* Issue #41's third condition at the surface a person reads. The fold hands
     * this view a figure with no name and no key; what the view may not do is
     * leave it out, which would lose plays out of a partition, or draw it under
     * something that reads like one more client. */
    const drawn = clientsAndDevices({
        state: 'ready',
        dimension: 'client',
        plays: 8,
        rows: [row('Jellyfin Web', { plays: 4, transcode: 1 }), row('Findroid', { plays: 2 })],
        combined: { plays: 2, unknown: 0, directPlay: 2, directStream: 0, transcode: 0 },
    });

    assert.match(drawn, /<title>Grouped together, too few accounts to show separately: 2<\/title>/);
    assert.match(drawn, /3 over 8 plays in this range, and every play is in exactly one of them/);
    assert.match(drawn, /too few accounts use for this view to show them separately/);
});

test('the group is not drawn as a member when the fold had nothing to group', () => {
    /* A breakdown that withheld nothing and one that folded an empty group are
     * different statements. Drawing a bar for the second would tell a reader
     * that something was kept from them when nothing was. */
    const drawn = clientsAndDevices({
        state: 'ready',
        dimension: 'client',
        plays: 6,
        rows: [row('Jellyfin Web', { plays: 4 }), row('Findroid', { plays: 2 })],
        combined: null,
    });

    assert.doesNotMatch(drawn, /Grouped together/);
    assert.doesNotMatch(drawn, /too few accounts/);
    assert.match(drawn, /2 over 6 plays in this range/);
});

test('the group is said to be a group and never named like a person', () => {
    /* The wording is what this condition is about, so it is read rather than
     * assumed. A label that could be somebody's name, or a word like other that
     * says nothing about why the bar exists, would pass a test that only checked
     * the bar was there. */
    const drawn = clientsAndDevices({
        state: 'ready',
        dimension: 'device',
        plays: 5,
        rows: [row('A tablet', { plays: 3 })],
        combined: { plays: 2, unknown: 0, directPlay: 2, directStream: 0, transcode: 0 },
    });

    assert.match(drawn, /Grouped together/);
    assert.match(drawn, /too few accounts to show separately/);
});

test('a breakdown with no state is refused rather than drawn', () => {
    const ready = {
        dimension: 'client',
        plays: 1,
        rows: [row('Jellyfin Web', { plays: 1 })],
    };

    assert.throws(() => clientsAndDevices(ready), /state/);
    assert.throws(() => clientsAndDevices({ ...ready, state: 'done' }), /state/);
    assert.throws(() => clientsAndDevices(null), /state/);
});

test('a breakdown this view does not have is refused rather than drawn under a wrong heading', () => {
    const ready = { state: 'ready', plays: 0, rows: [] };

    assert.throws(() => clientsAndDevices({ ...ready, dimension: 'user' }), /user/);
    assert.throws(() => clientsAndDevices({ ...ready, dimension: 'library' }), /library/);
    assert.throws(() => clientsAndDevices(ready), /no breakdown called/);
});

test('the heading is refused before the state, so a view asking for the wrong one hears about it while loading', () => {
    /* Both are faults in the caller and neither depends on the rows. A view that
     * resolved the heading only once an answer was ready would report the
     * mistake on the request that succeeded and stay quiet on the three that did
     * not. */
    assert.throws(() => clientsAndDevices({ state: 'loading', dimension: 'user' }), /user/);
});

test('each of the four situations is drawn as itself', () => {
    const ready = clientsAndDevices({
        state: 'ready',
        dimension: 'device',
        plays: 1,
        rows: [row('A television', { plays: 1 })],
    });
    const empty = clientsAndDevices({ state: 'empty', dimension: 'device' });
    const loading = clientsAndDevices({ state: 'loading', dimension: 'device' });
    const failed = clientsAndDevices({ state: 'failed', dimension: 'device' });

    assert.match(ready, /<title>A television: 1<\/title>/);
    assert.match(empty, /Nothing recorded yet/);
    assert.match(loading, /Still loading/);
    assert.match(failed, /Statistics unavailable/);
    assert.match(failed, /operator has the details/);

    /* The four are told apart by what they say and not only by being different
     * strings, so a view that drew the same empty frame for all of them fails
     * here rather than passing on four markup blobs nobody compared. */
    assert.equal(new Set([ready, empty, loading, failed]).size, 4);
});
