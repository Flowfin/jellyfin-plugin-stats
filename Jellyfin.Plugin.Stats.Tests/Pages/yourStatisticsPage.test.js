/*
 * The requests the page a user opens about themselves makes, and what it does
 * with the answers. Issue #61.
 *
 * The second condition of that issue is asserted here and it is asserted over
 * the requests rather than over the source: the page is driven with a client
 * that records every address it is asked for, and each one has to be a member of
 * the closed set of self addresses under the account that opened the page. That
 * catches a request added later that nothing else in this file mentions, which
 * is what the condition is about, and it catches a report endpoint reached for
 * one figure - the shape the first condition refuses, because a personal number
 * beside a server one is a subtraction with somebody else on the other side.
 *
 * The figures endpoint is #274 and is not on `master`. Everything below drives
 * this module with a client of its own, so the suite says what the page asks for
 * and what it draws from an answer, and it says nothing about a server. What a
 * live server does with the address is that issue's to prove.
 *
 * Run with the test runner built into node:
 *
 *     node --test Jellyfin.Plugin.Stats.Tests/Pages/
 */

import assert from 'node:assert/strict';
import test from 'node:test';
import { WINDOWS } from '../../Jellyfin.Plugin.Stats/Pages/yourStatistics.js';
import {
    SELF_PATHS,
    WINDOW_OPENED_ON,
    choicesForDrawing,
    consentAnswerFrom,
    consentPathFor,
    consentRequest,
    deletionRequest,
    forDrawing,
    playsPathFor,
    statisticsPathFor,
    yourStatisticsMarkup,
} from '../../Jellyfin.Plugin.Stats/Pages/yourStatisticsPage.js';

/* The account the page is opened by. */
const CALLER = '6f9619ff-8b86-d011-b42d-00c04fc964ff';

/* Another account, used to prove an address is built from the account the page
 * was handed rather than from anything the module holds. */
const SOMEBODY_ELSE = '3fa85f64-5717-4562-b3fc-2c963f66afa6';

/**
 * Every address this page may reach on behalf of one account, derived from the
 * set the module declares rather than written out again.
 *
 * @param {string} userId The account.
 * @returns {Set<string>} The addresses.
 */
function selfAddresses(userId) {
    return new Set([
        ...WINDOWS.map((each) => statisticsPathFor(userId, each.id)),
        consentPathFor(userId),
        playsPathFor(userId),
    ]);
}

/**
 * What the figures endpoint answers for a month somebody watched things in.
 *
 * @param {object} [changed] What to say differently.
 * @returns {object} The body.
 */
function figuresBody(changed = {}) {
    return {
        window: 'last30Days',
        zoneId: 'Europe/Berlin',
        plays: 41,
        watched: '21:30:00',
        finished: 33,
        abandoned: 8,
        points: [{ label: '2026-08-01', watched: '01:02:00' }],
        topItems: [{ name: 'The Bear', plays: 9, itemId: SOMEBODY_ELSE }],
        degraded: {},
        ...changed,
    };
}

/**
 * What the consent endpoint answers.
 *
 * @param {object} [changed] What to say differently.
 * @returns {object} The body.
 */
function consentBody(changed = {}) {
    return {
        answered: true,
        agreed: true,
        agreedUtc: '2026-08-01T09:00:00Z',
        withdrawnUtc: null,
        agreedToVersion: 1,
        currentVersion: 1,
        wording: 'What this server records about you.',
        ...changed,
    };
}

/**
 * A dashboard client that writes down every address it is asked for.
 *
 * `getUrl` hands the path straight back, so what is recorded is the path this
 * module built rather than whatever a live client would prefix it with.
 *
 * @param {(path: string) => object} answerFor What each address answers.
 * @returns {{asked: Array<string>, getUrl: Function, getJSON: Function}} The client.
 */
function aRecordingClient(answerFor) {
    const asked = [];

    return {
        asked,
        getUrl: (path) => path,
        getJSON: (path) => {
            asked.push(path);

            return Promise.resolve(answerFor(path));
        },
    };
}

/**
 * A client that answers each of the page's two reads with its own body.
 *
 * @param {string} userId The account the page is opened by.
 * @param {object} [figures] What the figures endpoint answers.
 * @param {object} [consent] What the consent endpoint answers.
 * @returns {object} The client.
 */
function aClientFor(userId, figures = figuresBody(), consent = consentBody()) {
    return aRecordingClient((path) => (path === consentPathFor(userId) ? consent : figures));
}

test('every request the page makes is a self address under the account that opened it', async () => {
    const client = aClientFor(CALLER);

    await yourStatisticsMarkup(client, { userId: CALLER });

    const allowed = selfAddresses(CALLER);

    assert.ok(client.asked.length > 0, 'The page made no request, so nothing here is asserted.');

    for (const path of client.asked) {
        assert.ok(
            allowed.has(path),
            `The page asked for ${path}, which is not one of the self addresses this account ` +
                'may be shown. Every figure on this page belongs to the account that opened ' +
                'it, and an address outside this set is either another account rows or a ' +
                'total for the server.',
        );
    }
});

test('the page reaches no report endpoint, so there is no server total to difference against', async () => {
    const client = aClientFor(CALLER);

    await yourStatisticsMarkup(client, { userId: CALLER });

    for (const path of client.asked) {
        assert.ok(
            !path.includes('Reports'),
            `The page asked for ${path}. The report endpoints answer for the whole server, and ` +
                'a personal figure drawn beside a server one lets a reader subtract everybody ' +
                'else out.',
        );
    }
});

test('every window the page offers is asked for at a self address too', async () => {
    for (const window of WINDOWS) {
        const client = aClientFor(CALLER, figuresBody({ window: window.id }));

        await yourStatisticsMarkup(client, { userId: CALLER, window: window.id });

        const allowed = selfAddresses(CALLER);

        for (const path of client.asked) {
            assert.ok(allowed.has(path), `${window.id} is read from ${path}.`);
        }
    }
});

test('the addresses are built from the account the page was handed and never from another', () => {
    for (const path of selfAddresses(SOMEBODY_ELSE)) {
        assert.ok(
            path.includes(SOMEBODY_ELSE),
            `${path} does not name the account it was built for.`,
        );
        assert.ok(!path.includes(CALLER), `${path} names an account it was not built for.`);
    }
});

test('an account is escaped into an address rather than pasted into one', () => {
    assert.ok(
        consentPathFor('a/b').includes('a%2Fb'),
        'An account carrying a separator would reach a different address from the one this page ' +
            'meant to ask for.',
    );
});

test('a window the plugin does not fold is refused rather than asked for', () => {
    assert.throws(
        () => statisticsPathFor(CALLER, 'lastFortnight'),
        /window/,
        'A window nobody folds is asked for anyway, and the refusal comes back to be drawn as a ' +
            'stretch of time this person watched nothing in.',
    );
});

test('the page opens on the shortest of the three windows', async () => {
    const client = aClientFor(CALLER);

    await yourStatisticsMarkup(client, { userId: CALLER });

    assert.ok(
        client.asked.includes(statisticsPathFor(CALLER, WINDOW_OPENED_ON)),
        'The page opens on a window other than the one it declares it opens on.',
    );
});

test('the answer is mapped into what the drawing takes', () => {
    const drawing = forDrawing(figuresBody());

    assert.equal(drawing.state, 'ready');
    assert.equal(drawing.window, 'last30Days');
    assert.equal(drawing.zone, 'Europe/Berlin');
    assert.equal(drawing.plays, 41);
    assert.equal(drawing.watchedMinutes, 1290);
    assert.equal(drawing.finished, 33);
    assert.equal(drawing.abandoned, 8);
    assert.deepEqual(drawing.points, [{ label: '2026-08-01', value: 62 }]);
});

test('the identifier the server folded a top row from does not reach the drawing', () => {
    assert.deepEqual(forDrawing(figuresBody()).topItems, [{ name: 'The Bear', plays: 9 }]);
});

test('an account with no plays in the window is drawn as empty and never as a page of noughts', () => {
    assert.equal(
        forDrawing(figuresBody({ plays: 0, points: [], topItems: [] })).state,
        'empty',
        'A window holding nothing is drawn as figures that are all nought, which reads as a ' +
            'measurement rather than as an absence.',
    );
});

test('an answer carrying no plays at all is refused rather than read as nought', () => {
    assert.throws(
        () => forDrawing(figuresBody({ plays: null })),
        /whole number/,
        'A missing figure is read as an answer, so a fold that failed is drawn as a person who ' +
            'watched nothing.',
    );
});

test('an answer with no zone is refused rather than drawn under one the page chose', () => {
    assert.throws(() => forDrawing(figuresBody({ zoneId: '' })), /zone/);
});

test('the two timestamps on a consent answer do not reach the controls', () => {
    const drawing = choicesForDrawing(consentBody());

    assert.deepEqual(Object.keys(drawing).sort(), [
        'agreed',
        'agreedToVersion',
        'answered',
        'currentVersion',
        'state',
        'wording',
    ]);
});

test('a figures request that fails leaves the controls on the page', async () => {
    const client = {
        getUrl: (path) => path,
        getJSON: (path) =>
            path === consentPathFor(CALLER)
                ? Promise.resolve(consentBody())
                : Promise.reject(new Error('The store could not be opened.')),
    };

    const drawn = await yourStatisticsMarkup(client, { userId: CALLER });

    assert.ok(
        drawn.includes('Statistics unavailable'),
        'The figures half does not say that it could not be read.',
    );
    assert.ok(
        !drawn.includes('The store could not be opened.'),
        'The words the request failed with reached the reader. What a failure here can name is a ' +
            'file in the server storage, which the operator reads on the settings page and nobody ' +
            'else reads at all.',
    );
    assert.ok(
        drawn.includes('stats-view-your-choices-delete-control'),
        'A reader whose figures could not be read loses the control that deletes them, which is ' +
            'the moment they are most likely to want it.',
    );
    assert.ok(
        drawn.includes('stats-view-your-choices-consent-control'),
        'A reader whose figures could not be read loses the control that withdraws their ' +
            'agreement.',
    );
});

test('a consent request that fails leaves the figures on the page', async () => {
    const client = {
        getUrl: (path) => path,
        getJSON: (path) =>
            path === consentPathFor(CALLER)
                ? Promise.reject(new Error('The store could not be opened.'))
                : Promise.resolve(figuresBody()),
    };

    const drawn = await yourStatisticsMarkup(client, { userId: CALLER });

    assert.ok(drawn.includes('<dd>41</dd>'), 'The figures went with the controls.');
});

test('what a click on the consent control says is read off the control it was drawn on', () => {
    assert.deepEqual(consentAnswerFrom({ answer: 'agree', wordingVersion: '4' }), {
        agreed: true,
        wordingVersion: 4,
    });

    assert.deepEqual(consentAnswerFrom({ answer: 'withdraw', wordingVersion: '4' }), {
        agreed: false,
        wordingVersion: 4,
    });
});

test('a control carrying no version records nothing rather than a version the page worked out', () => {
    assert.throws(
        () => consentAnswerFrom({ answer: 'agree' }),
        /version/,
        'An agreement is recorded against whatever the server holds at the moment of the click, ' +
            'which on a page left open across an upgrade is text nobody was shown.',
    );
});

test('the two requests that change something are self addresses as well', () => {
    const client = { getUrl: (path) => path };

    const consenting = consentRequest(client, CALLER, {
        agreed: true,
        wordingVersion: 1,
    });

    assert.equal(consenting.type, 'PUT');
    assert.equal(consenting.url, consentPathFor(CALLER));
    assert.ok(selfAddresses(CALLER).has(consenting.url));
    assert.equal(consenting.data, '{"agreed":true,"wordingVersion":1}');

    const deleting = deletionRequest(client, CALLER);

    assert.equal(deleting.type, 'DELETE');
    assert.equal(deleting.url, playsPathFor(CALLER));
    assert.ok(selfAddresses(CALLER).has(deleting.url));
});

test('the deletion names no window, which is how the endpoint is asked for everything', () => {
    const deleting = deletionRequest({ getUrl: (path) => path }, CALLER);

    assert.ok(
        !deleting.url.includes('from') && !deleting.url.includes('to'),
        'A window built here would be this page working two instants out of a clock the rows ' +
            'were never folded against, and one end without the other is refused by the ' +
            'endpoint.',
    );
});

test('the set of addresses this page may reach is the one the module declares', () => {
    assert.deepEqual(Object.keys(SELF_PATHS).sort(), ['consent', 'plays', 'statistics']);

    for (const path of Object.values(SELF_PATHS)) {
        assert.ok(
            path.startsWith('Stats/Users/{userId}/'),
            `${path} is not an address under the calling account, so the server has no account ` +
                'in the route to check the caller against.',
        );
    }
});
