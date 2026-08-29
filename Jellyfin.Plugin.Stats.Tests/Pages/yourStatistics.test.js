/*
 * The figures a person opens about themselves, read as a function from an answer
 * to markup. Issue #61.
 *
 * The three states are asked of this view by everyView.test.js along with every
 * other view in the directory, so they are not asked again here. What is here is
 * what only this view owes: that a figure the server could not read in full says
 * so where the figure would have been rather than being drawn as a small number,
 * that the window and the zone are the ones the answer carried, and that nothing
 * drawn here is a total anybody could subtract somebody else out of.
 *
 * Run with the test runner built into node:
 *
 *     node --test Jellyfin.Plugin.Stats.Tests/Pages/
 */

import assert from 'node:assert/strict';
import test from 'node:test';
import { WINDOWS, yourStatistics } from '../../Jellyfin.Plugin.Stats/Pages/yourStatistics.js';

/**
 * A month of somebody's own figures, with everything the view reads on it.
 *
 * @param {object} [changed] What to say differently.
 * @returns {object} The answer.
 */
function figures(changed = {}) {
    return {
        state: 'ready',
        window: 'last30Days',
        zone: 'Europe/Berlin',
        plays: 41,
        watchedMinutes: 1290,
        finished: 33,
        abandoned: 8,
        points: [
            { label: '2026-08-01', value: 62 },
            { label: '2026-08-02', value: null },
            { label: '2026-08-03', value: 118 },
        ],
        topItems: [
            { name: 'The Bear', plays: 9 },
            { name: 'Andor', plays: 6 },
        ],
        degraded: {},
        ...changed,
    };
}

test('the four figures a person asked for are drawn under words that name them', () => {
    const drawn = yourStatistics(figures());

    for (const words of ['Plays', 'Minutes watched', 'Played to the end', 'Left unfinished']) {
        assert.ok(drawn.includes(`<dt>${words}</dt>`), `${words} is not drawn.`);
    }

    assert.ok(drawn.includes('<dd>41</dd>'), 'The plays are not drawn.');
    assert.ok(drawn.includes('<dd>1290</dd>'), 'The watched minutes are not drawn.');
    assert.ok(drawn.includes('<dd>33</dd>'), 'What was played to the end is not drawn.');
    assert.ok(drawn.includes('<dd>8</dd>'), 'What was left unfinished is not drawn.');
});

test('a figure nobody has is drawn as unrecorded and never as nought', () => {
    const drawn = yourStatistics(figures({ finished: null, abandoned: null }));

    assert.ok(
        drawn.includes('<dd>not recorded</dd>'),
        'A figure the fold could not answer for is drawn as nought, so a reader cannot tell it ' +
            'from a figure that genuinely is nought - which on this page is their own history.',
    );
});

test('a figure the server could not read in full carries the reason where the figure would be', () => {
    const drawn = yourStatistics(
        figures({
            finished: null,
            abandoned: null,
            degraded: { completion: 'more plays than this window reads at once' },
        }),
    );

    assert.ok(
        drawn.includes('<dd>more plays than this window reads at once</dd>'),
        'A figure that was cut short is drawn without its reason, so it reads as a figure ' +
            'nobody recorded.',
    );
});

test('a degraded figure takes only itself away and never the page', () => {
    const drawn = yourStatistics(
        figures({ degraded: { topItems: 'more plays than this window reads at once' } }),
    );

    assert.ok(drawn.includes('<dd>41</dd>'), 'The plays went with the figure that degraded.');
    assert.ok(
        drawn.includes('more plays than this window reads at once'),
        'The reason the top list was cut short is not on the page.',
    );
    assert.ok(
        !drawn.includes('The Bear'),
        'A degraded top list is drawn anyway, so a reader is shown part of a list as though it ' +
            'were the list.',
    );
});

test('the window and the zone drawn are the ones the answer carried', () => {
    assert.ok(
        yourStatistics(figures()).includes('The last 30 days, by day, in Europe/Berlin.'),
        'The window sentence does not say what the figures cover.',
    );

    assert.ok(
        yourStatistics(figures({ window: 'last12Months' })).includes(
            'The last 12 months, by month, in Europe/Berlin.',
        ),
        'A window grouped by month is described as one grouped by day.',
    );

    assert.ok(
        yourStatistics(figures({ window: 'allTime', points: [] })).includes(
            'Everything of yours the store still holds, in Europe/Berlin.',
        ),
        'All time is described as a fixed stretch of days.',
    );
});

test('the zone is stated on every window and not only on the grouped ones', () => {
    for (const window of WINDOWS) {
        assert.ok(
            yourStatistics(figures({ window: window.id })).includes('Europe/Berlin'),
            `${window.id} is drawn without the zone its days were read in, so a reader takes it ` +
                'for a reading with no boundary at all.',
        );
    }
});

test('all time is drawn as totals and never as a line over one point', () => {
    const drawn = yourStatistics(figures({ window: 'allTime' }));

    assert.ok(drawn.includes('<dd>41</dd>'), 'The all-time totals are not drawn.');
    assert.ok(
        !drawn.includes('Your watched time'),
        'A series is drawn over a window that has none, which is a picture of one reading ' +
            'pretending to be a trend.',
    );
});

test('the page says in words that it holds no total anybody could be subtracted out of', () => {
    assert.ok(
        yourStatistics(figures()).includes('Nothing on this page is a total for the server'),
        'The one sentence a reader needs in order to know what this page is not is missing.',
    );
});

test('the identifier the server folded a top row from does not reach the drawing', () => {
    const drawn = yourStatistics(
        figures({
            topItems: [
                { name: 'The Bear', plays: 9, itemId: '3fa85f64-5717-4562-b3fc-2c963f66afa6' },
            ],
        }),
    );

    assert.ok(drawn.includes('The Bear'), 'The name of the item is not drawn.');
    assert.ok(
        !drawn.includes('3fa85f64'),
        'The identifier the fold grouped on reaches the page. It says nothing to the person ' +
            'reading their own figures, and a page asset is served to anybody who asks for it.',
    );
});

test('a top row the server could not name is drawn as unnamed rather than as nothing', () => {
    const drawn = yourStatistics(figures({ topItems: [{ name: '  ', plays: 4 }] }));

    assert.ok(drawn.includes('Not named'), 'A row with no name is dropped rather than drawn.');
});

test('the window being shown is the one the selector marks', () => {
    const drawn = yourStatistics(figures({ window: 'last12Months' }));

    assert.ok(
        drawn.includes('data-window="last12Months" aria-current="true"'),
        'The window being shown is not marked, so a reader cannot see which of the three they ' +
            'are looking at.',
    );

    assert.ok(
        !drawn.includes('data-window="allTime" aria-current="true"'),
        'A window that is not being shown is marked as current.',
    );
});

test('figures over a window this view does not know are refused rather than drawn under a heading', () => {
    assert.throws(
        () => yourStatistics(figures({ window: 'lastFortnight' })),
        /window/,
        'A window the view cannot describe is drawn anyway, which puts one window name over ' +
            "another window's numbers.",
    );
});

test('figures with no zone are refused rather than drawn under a boundary the page chose', () => {
    assert.throws(
        () => yourStatistics(figures({ zone: '   ' })),
        /zone/,
        'A window drawn without the zone its days were read in is a different set of rows ' +
            'reading exactly like this one.',
    );
});

test('an answer that does not say what could not be read is refused rather than read as an assurance', () => {
    assert.throws(
        () => yourStatistics(figures({ degraded: undefined })),
        /could not read/,
        'An absent record of what was cut short is read as nothing having been cut short, so a ' +
            'figure that stopped early is drawn as a figure that is simply small.',
    );
});
