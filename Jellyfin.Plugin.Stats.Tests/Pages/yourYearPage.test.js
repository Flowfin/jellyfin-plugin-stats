/*
 * The requests behind the wrap-up, driven as functions rather than through a
 * browser. Issue #67.
 *
 * The client is an object each case wrote, so what is asserted is which paths
 * the page asks for and what it does with the two answers. No test in this tree
 * drives a browser and none needs to. docs/headless-tests.md.
 *
 * What is not driven here is `mountYourYear`, the one function in the module
 * that touches a document. That is a disclosure rather than an oversight.
 */

import assert from 'node:assert/strict';
import test from 'node:test';
import {
    forDrawing,
    yearPathFor,
    yearToOpen,
    yearsPathFor,
    yourYearMarkup,
} from '../../Jellyfin.Plugin.Stats/Pages/yourYearPage.js';

const ALICE = '6f9619ff8b86d011b42d00c04fc964ff';

/* The years an account has, as the endpoint answers them. */
const AYearList = (held, keptFrom = '2025-01-01') => ({ held, keptFrom });

/* One folded year, in the shape the endpoint writes it. */
const AYear = (year, extras = {}) => ({
    year,
    zoneId: 'Europe/Berlin',
    coverage: {
        year,
        wholeYear: true,
        firstDayCovered: `${year}-01-01`,
        lastDayCovered: `${year}-12-31`,
        daysCovered: 365,
    },
    anythingRecorded: true,
    plays: 120,
    watched: '40.06:30:00',
    distinctItems: 44,
    finished: 90,
    abandoned: 30,
    topItems: [
        {
            key: '11111111-2222-3333-4444-555555555555',
            name: 'An item',
            plays: 9,
            watched: '05:00:00',
        },
    ],
    ...extras,
});

test('the years and one year are asked for at the addresses the plugin serves', async () => {
    const asked = [];

    await yourYearMarkup(
        AClientAnswering({ years: AYearList([2024, 2026]), year: AYear(2026) }, asked),
        {
            userId: ALICE,
        },
    );

    assert.deepEqual(asked, [yearsPathFor(ALICE), yearPathFor(ALICE, 2026)]);
});

test('the page opens on the newest year the store holds and not on the year the server is in', () => {
    assert.equal(yearToOpen(AYearList([2019, 2024, 2021]), undefined), 2024);
});

test('a year a reader chose is opened where the store holds it', () => {
    assert.equal(yearToOpen(AYearList([2019, 2024]), 2019), 2019);
});

test('a year the store holds nothing of is refused rather than opened empty', () => {
    assert.throws(() => yearToOpen(AYearList([2024]), 2023), /holds no plays/);
});

test('an account with no years at all has no year to open', () => {
    assert.throws(() => yearToOpen(AYearList([]), undefined), /no years in the store/);
});

test('a year is asked for as a whole number', () => {
    for (const year of ['2026', 2026.5, null, undefined]) {
        assert.throws(() => yearPathFor(ALICE, year), /whole number/);
    }
});

test('the answer is mapped into what the drawing takes', () => {
    const drawn = forDrawing(AYear(2026), AYearList([2024, 2026], '2025-03-01'));

    assert.equal(drawn.state, 'ready');
    assert.equal(drawn.year, 2026);
    assert.equal(drawn.zone, 'Europe/Berlin');
    assert.deepEqual(drawn.years, { held: [2024, 2026], keptFrom: '2025-03-01' });
    assert.equal(drawn.watchedMinutes, 40 * 24 * 60 + 390);
    assert.equal(drawn.plays, 120);
    assert.deepEqual(drawn.topItems, [{ name: 'An item', plays: 9 }]);
});

test('the identifier the server folded a top row from does not reach the drawing', () => {
    const drawn = forDrawing(AYear(2026), AYearList([2026]));

    assert.equal(JSON.stringify(drawn).includes('11111111-2222'), false);
    assert.deepEqual(Object.keys(drawn.topItems[0]).sort(), ['name', 'plays']);
});

test('a year with nothing recorded carries no watched time rather than nought', () => {
    const drawn = forDrawing(
        AYear(2026, { anythingRecorded: false, plays: null, watched: null, topItems: [] }),
        AYearList([2026]),
    );

    assert.equal(drawn.watchedMinutes, null);
    assert.equal(drawn.anythingRecorded, false);
});

test('a year with no zone on it is refused rather than drawn under one of the page own', () => {
    assert.throws(() => forDrawing(AYear(2026, { zoneId: undefined }), AYearList([2026])), /zone/);
});

test('an account the store holds no years for is drawn as empty and never as a failure', async () => {
    const markup = await yourYearMarkup(AClientAnswering({ years: AYearList([]) }, []), {
        userId: ALICE,
    });

    assert.equal(markup.includes('stats-chart-empty'), true);
    assert.equal(markup.includes('stats-chart-failed'), false);
});

test('a request that fails is drawn as a failure and never as a year with nothing in it', async () => {
    const client = {
        getUrl: (path) => path,
        getJSON: () => Promise.reject(new Error('D:\jellyfin\stats.db is away')),
    };

    const markup = await yourYearMarkup(client, { userId: ALICE });

    assert.equal(markup.includes('stats-chart-failed'), true);
    assert.equal(markup.includes('stats-chart-empty'), false);

    /* And carries nothing out of the failure. The reason this plugin knows
     * names a file in the server storage; it reaches the operator on the
     * settings page and reaches a reader of this page not at all. Issue #64. */
    assert.equal(markup.includes('D:'), false);
    assert.equal(markup.includes('stats.db'), false);
    assert.equal(markup.includes('is away'), false);
});

test('a year that fails after the years were read is still a failure', async () => {
    const client = {
        getUrl: (path) => path,
        getJSON: (path) =>
            path === yearsPathFor(ALICE)
                ? Promise.resolve(AYearList([2026]))
                : Promise.reject(new Error('the year could not be folded')),
    };

    const markup = await yourYearMarkup(client, { userId: ALICE });

    assert.equal(markup.includes('stats-chart-failed'), true);
    assert.equal(markup.includes('the year could not be folded'), false);
});

test('the wrap-up that is drawn carries the years the selector offers', async () => {
    const markup = await yourYearMarkup(
        AClientAnswering({ years: AYearList([2024, 2026]), year: AYear(2026) }, []),
        { userId: ALICE },
    );

    assert.equal(markup.includes('data-year="2026"'), true);
    assert.equal(markup.includes('data-year="2024"'), true);
    assert.equal(markup.includes('data-year="2025"'), false);
});

function AClientAnswering(answers, asked) {
    return {
        getUrl: (path) => {
            asked.push(path);
            return path;
        },
        getJSON: (path) =>
            Promise.resolve(path === yearsPathFor(ALICE) ? answers.years : answers.year),
    };
}
