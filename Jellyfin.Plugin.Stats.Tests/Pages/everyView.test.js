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
 * What separates a view from the module they draw with is read rather than
 * listed. The drawing module is the one that exports the notice, and every other
 * module here is a view and owes its three states. That makes the export named
 * after the file part of what a view is, which is the shape all four already
 * have, and a fifth that departs from it fails here rather than being quietly
 * skipped.
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

const loaded = await Promise.all(
    readdirSync(fileURLToPath(new URL(DIRECTORY, import.meta.url)))
        .filter((name) => name.endsWith('.js'))
        .sort()
        .map(async (name) => ({ name, exports: await import(DIRECTORY + name) })),
);

const drawing = loaded.filter((module) => typeof module.exports.stateNotice === 'function');
const views = loaded.filter((module) => typeof module.exports.stateNotice !== 'function');

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

test('the page directory holds one drawing module and views beside it', () => {
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
