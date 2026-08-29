/*
 * The two controls a person has over their own history. Issue #61, third
 * condition: each says what it does before it is used, not after.
 *
 * "Before" is asserted as a position in the markup rather than as the sentence
 * merely being present somewhere. A warning under a button is a warning about a
 * decision somebody has already made, and it passes a test that only asks
 * whether the words are on the page.
 *
 * The words themselves are read out of the module rather than written again
 * here, so a change to what a control says moves the module and this file
 * together and neither goes stale against the other.
 *
 * The three states are asked of this view by everyView.test.js along with every
 * other view in the directory, so they are not asked again here.
 *
 * Run with the test runner built into node:
 *
 *     node --test Jellyfin.Plugin.Stats.Tests/Pages/
 */

import assert from 'node:assert/strict';
import test from 'node:test';
import { escapeText } from '../../Jellyfin.Plugin.Stats/Pages/charts.js';
import {
    WHAT_AGREEING_GOVERNS,
    WHAT_DELETING_DOES,
    yourPrivacyChoices,
} from '../../Jellyfin.Plugin.Stats/Pages/yourPrivacyChoices.js';

/* The words the server ships, in the shape it ships them: paragraphs with a
 * blank line between. Shortened, because what is asserted here is that the page
 * draws what it was handed and not what the text says. */
const WORDING =
    'What this server records about you\n\nThis plugin keeps a row for each thing you play.' +
    '\n\nAgreeing lets an administrator see those rows as yours, by name.';

/**
 * What the consent endpoint answers for an account that has agreed to the words
 * this build ships.
 *
 * @param {object} [changed] What to say differently.
 * @returns {object} The answer.
 */
function choices(changed = {}) {
    return {
        state: 'ready',
        answered: true,
        agreed: true,
        agreedToVersion: 3,
        currentVersion: 3,
        wording: WORDING,
        ...changed,
    };
}

/**
 * Where a control sits in the markup.
 *
 * @param {string} drawn The view.
 * @param {string} className What the control is.
 * @returns {number} Where it begins.
 */
function controlAt(drawn, className) {
    const at = drawn.indexOf(className);

    assert.notEqual(at, -1, `There is no ${className} on the page, so nothing here is asserted.`);

    return at;
}

test('what agreeing governs is on the page in front of the control, not after it', () => {
    const drawn = yourPrivacyChoices(choices());
    const control = controlAt(drawn, 'stats-view-your-choices-consent-control');

    for (const sentence of WHAT_AGREEING_GOVERNS) {
        const at = drawn.indexOf(escapeText(sentence));

        assert.notEqual(at, -1, `The page does not say: ${sentence}`);
        assert.ok(
            at < control,
            'A sentence saying what agreeing governs sits after the control that does it, so a ' +
                'reader meets it once they have already decided.',
        );
    }
});

test('what deleting does is on the page in front of the control, not after it', () => {
    const drawn = yourPrivacyChoices(choices());
    const control = controlAt(drawn, 'stats-view-your-choices-delete-control');

    for (const sentence of WHAT_DELETING_DOES) {
        const at = drawn.indexOf(escapeText(sentence));

        assert.notEqual(at, -1, `The page does not say: ${sentence}`);
        assert.ok(
            at < control,
            'A sentence saying what deleting does sits after the control that does it. The ' +
                'deletion is permanent and happens as soon as it is asked for, so a warning ' +
                'underneath it is a warning about something already gone.',
        );
    }
});

test('the control says agreeing governs what other people see and not whether rows are kept', () => {
    const said = WHAT_AGREEING_GOVERNS.join(' ');

    assert.ok(
        said.includes('does not decide whether your plays are recorded'),
        'The words leave a reader able to believe that withdrawing stops the recording, which ' +
            'is the misreading the ruling of 2026-08-20 on #61 says the sentence must close.',
    );

    assert.ok(
        said.includes('does not decide what you see'),
        'The words leave a reader able to believe that withdrawing hides their own history from ' +
            'them.',
    );
});

test('the deletion control says it does not stop future recording', () => {
    assert.ok(
        WHAT_DELETING_DOES.join(' ').includes('does not stop future recording'),
        'A reader who deletes their history to stop being recorded is told nothing, and finds ' +
            'out by watching it fill up again.',
    );
});

test('the words being agreed to are the ones the server sent, drawn as its paragraphs', () => {
    const drawn = yourPrivacyChoices(choices());

    assert.ok(
        drawn.includes('<p>What this server records about you</p>'),
        'The wording the answer carried is not drawn as the paragraphs the server wrote.',
    );
    assert.ok(
        drawn.includes('<p>Agreeing lets an administrator see those rows as yours, by name.</p>'),
        'A paragraph of the wording is missing, so somebody would be agreeing to part of a text.',
    );
});

test('the wording is escaped on its way into the page', () => {
    const drawn = yourPrivacyChoices(choices({ wording: 'Read <script>alert(1)</script> first.' }));

    assert.ok(drawn.includes('&lt;script&gt;'), 'The wording is not escaped.');

    /* A plain string test and not a pattern. The four sites in this suite that
     * spell the negative half as a regular expression are what raises
     * js/bad-tag-filter, which is open and argued on #263 and waiting on a
     * decision. This asserts the same thing without adding a fifth member to a
     * set nobody has ruled on yet. */
    assert.ok(!drawn.includes('<script>'), 'The wording reaches the page as markup.');
});

test('an account that has never been asked has not refused', () => {
    const drawn = yourPrivacyChoices(
        choices({ answered: false, agreed: false, agreedToVersion: 0 }),
    );

    assert.ok(
        drawn.includes('You have not been asked yet'),
        'An account nobody has asked is told it has refused, so the question waiting for it is ' +
            'never put.',
    );
    assert.ok(
        drawn.includes('data-answer="agree"'),
        'The control offers a withdrawal to somebody who has agreed to nothing.',
    );
});

test('an account that has withdrawn is offered the agreement again and told where it stands', () => {
    const drawn = yourPrivacyChoices(choices({ agreed: false }));

    assert.ok(drawn.includes('You are not agreeing at the moment'), 'The state is not said.');
    assert.ok(drawn.includes('data-answer="agree"'), 'The control does not offer agreeing.');
});

test('an agreement standing over words that have changed is said to be one', () => {
    const drawn = yourPrivacyChoices(choices({ agreedToVersion: 2, currentVersion: 3 }));

    assert.ok(
        drawn.includes('You agreed to an earlier version'),
        'An agreement to wording this build has moved past is drawn as an agreement to what is ' +
            'on the page, so somebody is shown as agreeing to text they have not read.',
    );
});

test('the control carries the version of the wording it was drawn from', () => {
    assert.ok(
        yourPrivacyChoices(choices({ currentVersion: 7 })).includes('data-wording-version="7"'),
        'The control names no version, so an agreement made from it records one the page never ' +
            'showed.',
    );
});

test('the controls are refused rather than drawn over words the answer did not carry', () => {
    assert.throws(
        () => yourPrivacyChoices(choices({ wording: '   ' })),
        /words being agreed to/,
        'A control is offered with no text beside it, so somebody agrees to a blank.',
    );
});

test('the controls are refused rather than drawn against a version the page chose', () => {
    assert.throws(
        () => yourPrivacyChoices(choices({ currentVersion: undefined })),
        /version/,
        'A control with no version records an agreement against whatever the server held at the ' +
            'moment of the click.',
    );
});
