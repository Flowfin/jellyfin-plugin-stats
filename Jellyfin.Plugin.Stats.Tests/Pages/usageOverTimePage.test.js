/*
 * The request behind the usage-over-time view, driven as functions rather than
 * through a browser. Issue #57.
 *
 * Everything here is a value in and a value out: the range is measured back from
 * a moment the case supplies, the client is an object the case wrote, and the
 * markup comes back as a string. No test in this tree drives a browser and none
 * needs to, which is what the module being shaped this way buys.
 * docs/headless-tests.md.
 *
 * What is not driven here is `mountUsageOverTime`, which is the one function in
 * the module that touches a document. That is a disclosure and not an oversight:
 * the headless policy refuses the test that would drive it, and what stands
 * behind those lines instead is that there are four of them.
 */

import assert from 'node:assert/strict';
import test from 'node:test';
import {
    LONGEST_RANGE_IN_DAYS,
    USAGE_PATH,
    boundSentence,
    forDrawing,
    rangeOf,
    usageOverTimeMarkup,
} from '../../Jellyfin.Plugin.Stats/Pages/usageOverTimePage.js';

/* A fixed moment, so the ranges below are the same on every run and in every
 * zone. A case that read the clock would ask a different question in June. */
const NOON = new Date('2026-03-14T12:00:00.000Z');

/* One day of the answer, in the shape the endpoint writes it. */
const ADay = (day, watched, plays, unknown, transcode) => ({
    day,
    watched,
    delivery: {
        unknown,
        directPlay: plays - unknown - transcode,
        directStream: 0,
        transcode,
        plays,
    },
});

const AnAnswer = (rows) => ({
    rows,
    plays: rows.reduce((count, row) => count + row.delivery.plays, 0),
    watched: '02:00:00',
    zoneId: 'Europe/Berlin',
});

test('a range is measured back from the moment it was given', () => {
    assert.deepEqual(rangeOf(7, NOON), {
        from: '2026-03-07T12:00:00.000Z',
        to: '2026-03-14T12:00:00.000Z',
    });
});

test('a range longer than the plugin answers over is not asked for', () => {
    assert.doesNotThrow(() => rangeOf(LONGEST_RANGE_IN_DAYS, NOON));
    assert.throws(() => rangeOf(LONGEST_RANGE_IN_DAYS + 1, NOON), /at most/);
});

test('a range of no days, or of part of one, is refused', () => {
    for (const days of [0, -1, 1.5, '30', null, undefined]) {
        assert.throws(() => rangeOf(days, NOON), /whole number|at most/);
    }
});

test('a range measured back from nothing is refused rather than read off a clock', () => {
    for (const now of [undefined, null, 'now', new Date('nonsense')]) {
        assert.throws(() => rangeOf(30, now), /measured back from/);
    }
});

test('the answer is mapped into what the drawing takes', () => {
    const drawn = forDrawing(
        AnAnswer([
            ADay('2026-03-13', '01:00:00', 3, 1, 1),
            ADay('2026-03-14', '01:00:00', 2, 0, 2),
        ]),
    );

    assert.equal(drawn.state, 'ready');
    assert.equal(drawn.zone, 'Europe/Berlin');
    assert.equal(drawn.plays, 5);
    assert.equal(drawn.watchedMinutes, 120);
    assert.deepEqual(drawn.days, [
        { day: '2026-03-13', watchedMinutes: 60, delivery: { plays: 3, unknown: 1, transcode: 1 } },
        { day: '2026-03-14', watchedMinutes: 60, delivery: { plays: 2, unknown: 0, transcode: 2 } },
    ]);
});

test('nothing but the fields the drawing reads is carried over', () => {
    const answer = AnAnswer([ADay('2026-03-14', '01:00:00', 1, 0, 0)]);

    answer.userId = '6f9619ff-8b86-d011-b42d-00c04fc964ff';
    answer.rows[0].userName = 'somebody';

    const drawn = forDrawing(answer);

    assert.equal(JSON.stringify(drawn).includes('6f9619ff'), false);
    assert.equal(JSON.stringify(drawn).includes('somebody'), false);
    assert.deepEqual(Object.keys(drawn.days[0]).sort(), ['day', 'delivery', 'watchedMinutes']);
});

test('a range with no days in it is a state rather than an empty picture', () => {
    assert.equal(forDrawing(AnAnswer([])).state, 'empty');
});

test('an answer with no zone is refused rather than drawn under one of the page own', () => {
    const answer = AnAnswer([ADay('2026-03-14', '01:00:00', 1, 0, 0)]);

    delete answer.zoneId;

    assert.throws(() => forDrawing(answer), /zone/);
});

test('an answer that is not one is refused', () => {
    for (const answer of [null, undefined, 'days', 7]) {
        assert.throws(() => forDrawing(answer), /body the endpoint returned/);
    }
});

test('an answer carrying no days at all is refused', () => {
    assert.throws(
        () => forDrawing({ zoneId: 'Europe/Berlin', watched: '00:00:00', plays: 0 }),
        /read as a list/,
    );
});

test('the days are asked for in one request and never one per day', async () => {
    const asked = [];
    const client = AClientAnswering(
        AnAnswer([
            ADay('2026-03-13', '01:00:00', 1, 0, 0),
            ADay('2026-03-14', '01:00:00', 1, 0, 0),
        ]),
        asked,
    );

    await usageOverTimeMarkup(client, { days: 30, now: NOON });

    assert.equal(asked.length, 1);
    assert.equal(asked[0].path, USAGE_PATH);
    assert.deepEqual(asked[0].params, rangeOf(30, NOON));
});

test('a request that fails is drawn as a failure and never as an empty range', async () => {
    const client = {
        getUrl: () => 'url',
        getJSON: () => Promise.reject(new Error('D:\jellyfin\stats.db is away')),
    };

    const markup = await usageOverTimeMarkup(client, { days: 30, now: NOON });

    assert.equal(markup.includes('stats-chart-failed'), true);
    assert.equal(markup.includes('stats-chart-empty'), false);

    /* And says nothing about why. The words a request fails with are the ones
     * nearest to hand and they are not this plugin's to give out: the reason it
     * knows names a file in the server storage, which the operator reads on the
     * settings page and a signed-in reader can do nothing with. Issue #64. */
    assert.equal(markup.includes('D:'), false);
    assert.equal(markup.includes('stats.db'), false);
    assert.equal(markup.includes('is away'), false);
});

test('the page states the bound rather than meeting it', () => {
    assert.equal(boundSentence().includes(String(LONGEST_RANGE_IN_DAYS)), true);
});

function AClientAnswering(answer, asked) {
    return {
        getUrl: (path, params) => {
            asked.push({ path, params });
            return 'url';
        },
        getJSON: () => Promise.resolve(answer),
    };
}
