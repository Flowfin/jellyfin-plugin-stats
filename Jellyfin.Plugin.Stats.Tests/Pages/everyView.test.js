/*
 * The three situations, asked of every view in the directory rather than of the
 * views somebody remembered.
 *
 * The first condition of issue #64 is that each view has a test for each of the
 * three states. Four views carry them and each was written with cases of its
 * own, so the condition holds over the tree as it stands. What held it was
 * habit: nothing anywhere asked a view for the three, and a fifth view written
 * without them would draw an empty frame for a store that would not open and
 * pass every check in this repository.
 *
 * So the population is read out of the directory. A view added later is covered
 * by arriving rather than by somebody remembering. A rule under tools/invariants
 * cannot do this: a rule there is an expression that fails on a match, so it
 * refuses a module that contains something and not one that lacks three things,
 * and writing it would mean naming every view file by hand.
 *
 * What separates a view from the two other kinds of module here is read rather
 * than listed. The drawing module is the one that exports the notice. A page
 * module is one that exports a function whose name begins with `mount`, which is
 * the only thing in this directory that touches a document: it wires a page's
 * controls to a request and asks a view for the markup, so it draws nothing of
 * its own and owes no states. Every module that is neither is a view and owes
 * its three. That makes the export named after the file part of what a view is,
 * which is the shape they all already have, and one that departs from it fails
 * here rather than being quietly skipped.
 *
 * The three kinds are asserted to cover the directory, so a fourth kind added
 * later turns this suite red rather than being counted as a view that is missing
 * its states, or worse, skipped.
 *
 * The pages are asked something of their own, and it is the second condition of
 * the same issue. A view telling the three apart proves nothing to a reader
 * until something hands it one of the three, and what does that is the page
 * module: it makes the request and turns what comes back into a state. Each of
 * the two pages in the tree carries cases of its own for that, written by hand,
 * which is exactly the position the views were in before this file existed. So
 * the same treatment is given here - a page whose request fails must say so with
 * the reason, and a page whose request answers nothing must say the view is
 * empty, and neither may be drawn as the other. A third page written without
 * either turns this suite red rather than shipping a store that would not open
 * drawn as a server nobody has used.
 *
 * The words for the three are not repeated in this file. Each expectation is the
 * drawing module's own output for that state, so a change to the wording moves
 * the views and this file together and neither goes stale against the other.
 *
 * Run with the test runner built into node:
 *
 *     node --test Jellyfin.Plugin.Stats.Tests/Pages/
 */

import assert from 'node:assert/strict';
import test from 'node:test';
import { readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { stateNotice } from '../../Jellyfin.Plugin.Stats/Pages/charts.js';

const DIRECTORY = '../../Jellyfin.Plugin.Stats/Pages/';

/* The three a view is asked for, and a reason on the one that carries one. A
 * failure is the state a reader would otherwise only find in the log, so it is
 * the one asserted with the words that travel on it. */
const STATES = [
    { state: 'empty', reason: undefined },
    { state: 'loading', reason: undefined },
    { state: 'failed', reason: 'The store could not be opened.' },
];

/* What an answer has to carry beside its state, per view.
 *
 * A view may resolve something before it looks at the state, and refusing there
 * is deliberate rather than an oversight: a heading a view worked out for itself
 * is reported on the request that failed as well as on the one that succeeded.
 * That cannot be derived from the module, so it is written here, and it is the
 * only hand-written thing in this file.
 *
 * It cannot go stale in silence. The case below asserts these keys are exactly
 * the views the directory holds, so a view added without an entry turns this
 * suite red and is read by whoever adds it, which is the same moment they are
 * told to give it the three states. A missing entry is a failure and never a
 * skip. */
const BESIDE_THE_STATE = {
    'clientsAndDevices.js': { dimension: 'device' },
    'usageByHourAndWeekday.js': {},
    'usageOverTime.js': {},
    'whyTheServerTranscodes.js': {},
    'yourYear.js': {},
};

/* What a page has to be asked, and what an answer holding nothing looks like to
 * it.
 *
 * The question a page asks is its own - one takes a number of days and the
 * moment to measure them back from, another takes an account - and so is the
 * shape of an answer that carries no figures. Neither can be read off the
 * module, so both are written here, and this is the second hand-written thing in
 * this file.
 *
 * It cannot go stale in silence either. The case below asserts these keys are
 * exactly the pages the directory holds, so a page added without an entry turns
 * this suite red and is read by whoever adds it. A missing entry is a failure
 * and never a skip. */
const ASKED_OF_A_PAGE = {
    'usageOverTimePage.js': {
        asked: { days: 7, now: new Date('2026-03-14T12:00:00.000Z') },
        answeringNothing: { rows: [], plays: 0, watched: '00:00:00', zoneId: 'Europe/Berlin' },
    },
    'yourYearPage.js': {
        asked: { userId: '6f9619ff-8b86-d011-b42d-00c04fc964ff' },
        answeringNothing: { held: [], keptFrom: null },
    },
};

/* What a failure says when it reaches a reader. The words are the case's own and
 * travel through the page, so a page that dropped the reason and drew a bare
 * failure fails below rather than passing on the state alone. */
const WHY_IT_COULD_NOT_ANSWER = 'The store could not be opened.';

const loaded = await Promise.all(
    readdirSync(fileURLToPath(new URL(DIRECTORY, import.meta.url)))
        .filter((name) => name.endsWith('.js'))
        .sort()
        .map(async (name) => ({ name, exports: await import(DIRECTORY + name) })),
);

const drawing = loaded.filter((module) => typeof module.exports.stateNotice === 'function');
const pages = loaded.filter(
    (module) =>
        typeof module.exports.stateNotice !== 'function' &&
        Object.keys(module.exports).some(
            (name) => name.startsWith('mount') && typeof module.exports[name] === 'function',
        ),
);
const views = loaded.filter((module) => !drawing.includes(module) && !pages.includes(module));

/**
 * What a view is expected to have put into its markup for a state.
 *
 * The drawing module writes the notice as an opening and a body, and only the
 * opening carries the title a view chooses for itself. So the body is the part
 * every view produces identically for one state, and it is taken from the module
 * rather than spelled out here.
 *
 * @param {string} state One of the three.
 * @param {string|undefined} reason What to say about a failure.
 * @returns {string} The body of the notice.
 */
function noticeBody(state, reason) {
    const notice = stateNotice(state, reason === undefined ? {} : { reason });
    const at = notice.indexOf('<text class="stats-chart-');

    assert.notEqual(
        at,
        -1,
        'The drawing module no longer writes a state notice this file can recognise, so every ' +
            'expectation below would be vacuous.',
    );

    return notice.slice(at);
}

/**
 * The function a view module is read through.
 *
 * @param {{name: string, exports: object}} module The module.
 * @returns {Function} Its view function.
 */
function viewOf(module) {
    return module.exports[module.name.replace(/\.js$/, '')];
}

/**
 * An answer that says it is in one of the three situations and nothing more.
 *
 * @param {{name: string}} module The view it is for.
 * @param {{state: string, reason: string|undefined}} situation Which situation.
 * @returns {object} The answer.
 */
function answerFor(module, situation) {
    return { ...BESIDE_THE_STATE[module.name], state: situation.state, reason: situation.reason };
}

test('the page directory holds one drawing module, its views, and the pages that wire them', () => {
    /* Without this the file passes by finding nothing. A directory that stopped
     * matching what is read above would leave every case underneath it running
     * over an empty list and reporting green. */
    assert.equal(
        drawing.length,
        1,
        'The words for the three states are written once so that two views cannot spell one of ' +
            'them differently, and that holds only while one module writes them.',
    );
    assert.ok(views.length > 0, 'No view was found next to the drawing module.');

    /* The three kinds cover the directory. A module that is none of them would
     * otherwise be counted as a view and asked for states it does not owe, or
     * be dropped out of every list here without anything saying so. */
    assert.equal(
        drawing.length + pages.length + views.length,
        loaded.length,
        'A module in this directory is the drawing, a view, or a page that wires one, and ' +
            'something here is none of the three.',
    );
});

test('every view in the directory is one this file knows how to ask', () => {
    assert.deepEqual(
        Object.keys(BESIDE_THE_STATE).sort(),
        views.map((module) => module.name).sort(),
        'A view was added or removed without this file moving. Until the list above matches the ' +
            'directory, a view is being asked for nothing rather than for its three states.',
    );
});

test('every view is reached through an export named after its file', () => {
    for (const module of views) {
        assert.equal(
            typeof viewOf(module),
            'function',
            `${module.name} exports no function called ${module.name.replace(/\.js$/, '')}, so ` +
                'nothing here can ask it for its three states and it would be skipped rather ' +
                'than checked.',
        );
    }
});

test('every view says which of the three situations it is in', () => {
    for (const module of views) {
        const view = viewOf(module);

        for (const situation of STATES) {
            const markup = view(answerFor(module, situation));

            assert.ok(
                markup.includes(noticeBody(situation.state, situation.reason)),
                `${module.name} does not say it is in the ${situation.state} state. A view that ` +
                    'draws an empty frame instead reads as a quiet server, which is the ' +
                    'confusion the three states exist to end.',
            );
        }
    }
});

test('every view tells the three apart rather than drawing one frame for all of them', () => {
    for (const module of views) {
        const view = viewOf(module);
        const drawn = STATES.map((situation) => view(answerFor(module, situation)));

        assert.equal(
            new Set(drawn).size,
            STATES.length,
            `${module.name} draws the same thing for two of the three states, so a reader cannot ` +
                'tell them apart on sight.',
        );
    }
});

test('every view refuses an answer that names no state', () => {
    for (const module of views) {
        const view = viewOf(module);

        assert.throws(
            () => view({ ...BESIDE_THE_STATE[module.name] }),
            /state/,
            `${module.name} reads an answer naming no state as ready. A request that failed and ` +
                'forgot to say so would be drawn as figures nobody has.',
        );
        assert.throws(() => view(null), /state/, `${module.name} reads a missing answer as ready.`);
    }
});

/**
 * The function a page module is asked through.
 *
 * Derived rather than listed: a page makes its request and hands back markup
 * through one exported function whose name ends in `Markup`, which is the shape
 * both pages in the tree already have. A page that departs from it fails the
 * case below rather than being quietly skipped.
 *
 * @param {{exports: object}} module The page module.
 * @returns {Function|undefined} Its asking function, where it has one.
 */
function askingOf(module) {
    const name = Object.keys(module.exports).find(
        (key) => key.endsWith('Markup') && typeof module.exports[key] === 'function',
    );

    return name === undefined ? undefined : module.exports[name];
}

/**
 * A dashboard client that answers every request with one body.
 *
 * @param {object} body What to answer with.
 * @returns {{getUrl: Function, getJSON: Function}} The client.
 */
function aClientAnswering(body) {
    return { getUrl: () => 'url', getJSON: () => Promise.resolve(body) };
}

/**
 * A dashboard client whose every request fails.
 *
 * @returns {{getUrl: Function, getJSON: Function}} The client.
 */
function aClientThatCannotAnswer() {
    return {
        getUrl: () => 'url',
        getJSON: () => Promise.reject(new Error(WHY_IT_COULD_NOT_ANSWER)),
    };
}

test('every page that wires a view is one this file knows how to ask', () => {
    assert.deepEqual(
        Object.keys(ASKED_OF_A_PAGE).sort(),
        pages.map((module) => module.name).sort(),
        'A page was added or removed without this file moving. Until the list above matches the ' +
            'directory, a page is being asked for nothing rather than for the two states only it ' +
            'can produce.',
    );
});

test('every page is asked through one function that hands back what was drawn', () => {
    for (const module of pages) {
        assert.equal(
            typeof askingOf(module),
            'function',
            `${module.name} exports no function whose name ends in Markup, so nothing here can ` +
                'ask it what it draws when a request fails, and it would be skipped rather than ' +
                'checked.',
        );
    }
});

test('a page whose request fails says so, with the reason, and never that the view is empty', async () => {
    for (const module of pages) {
        const drawn = await askingOf(module)(
            aClientThatCannotAnswer(),
            ASKED_OF_A_PAGE[module.name].asked,
        );

        assert.ok(
            drawn.includes(noticeBody('failed', WHY_IT_COULD_NOT_ANSWER)),
            `${module.name} does not say the view could not be read, or does not carry the ` +
                'reason it was given. A reader who is not told is left to find it in the log, ' +
                'which is the one place this condition says they must not have to look.',
        );

        assert.ok(
            !drawn.includes(noticeBody('empty', undefined)),
            `${module.name} draws a failed request as a view with nothing in it. A store that ` +
                'would not open and a server nobody has used are different facts, and drawing ' +
                'them the same way destroys the difference before a reader can see it.',
        );
    }
});

test('a page whose request answers nothing says the view is empty and never that it failed', async () => {
    for (const module of pages) {
        const drawn = await askingOf(module)(
            aClientAnswering(ASKED_OF_A_PAGE[module.name].answeringNothing),
            ASKED_OF_A_PAGE[module.name].asked,
        );

        assert.ok(
            drawn.includes(noticeBody('empty', undefined)),
            `${module.name} does not say the view has nothing in it when the server answered ` +
                'with no figures.',
        );

        assert.ok(
            !drawn.includes(noticeBody('failed', WHY_IT_COULD_NOT_ANSWER)),
            `${module.name} draws an answer holding nothing as a failure, which tells a reader ` +
                'something is broken when what is true is that nothing was recorded.',
        );
    }
});
