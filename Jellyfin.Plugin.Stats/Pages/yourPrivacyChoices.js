/*
 * The two controls a person has over their own history, and the words that say
 * what each one does before it is used. Issue #61, third condition.
 *
 * They are a view of their own rather than part of the figures beside them, and
 * that is the whole reason this file exists. A reader whose figures could not be
 * read is exactly the reader most likely to want to withdraw or to delete, and a
 * page that drew one notice over everything would take both controls away at
 * that moment. Two views on one page fail apart instead.
 *
 * What the consent control governs is stated on the control, in the sentence, in
 * the words the ruling of 2026-08-20 on #61 settled: consent decides what other
 * people may see, and never what this person sees about themselves, and never
 * whether the rows are kept. A control labelled "share my statistics" beside a
 * page that already shows the reader everything invites the opposite reading -
 * that turning it off hides the history from the system - and somebody who
 * believes that has withdrawn under a misunderstanding rather than decided.
 *
 * What the deletion control does is stated the same way and for a harder reason:
 * it is permanent, it happens while the request is being served, there is no
 * second step and nothing is kept for a change of mind. That sentence is in
 * front of the control rather than in a dialogue after it, because a warning
 * shown after somebody has already decided is a warning about a decision they
 * have made.
 *
 * The wording of the agreement is the server's and never this page's. It arrives
 * on the answer beside the version it is, so the words a person reads and the
 * version their agreement records are one fetch and cannot drift apart. A page
 * carrying its own copy would show one version's words over another version's
 * number, which is the drift the stored version exists to catch.
 *
 * No document, no window, no network and no clock, the same as the drawing
 * module. docs/headless-tests.md.
 */

import { escapeText, stateNotice } from './charts.js';

/* What an answer may say it is. Ready is the one that carries the choices; the
 * other three are the situations issue #64 asks every view to tell apart. */
const STATES = ['ready', 'empty', 'loading', 'failed'];

/* What agreeing governs, said before the control rather than on it. Three
 * sentences and each one is a different misreading being closed: that consent
 * decides whether anything is recorded, that it decides what this reader may see
 * about themselves, and that it is permanent once given. Exported so the suite
 * asserts the words that are on the page rather than a second copy of them. */
export const WHAT_AGREEING_GOVERNS = [
    'Agreeing decides what other people may see about you. It does not decide whether your ' +
        'plays are recorded: they are recorded either way, and an administrator can always see ' +
        'them as figures that name nobody.',
    'It does not decide what you see. This page shows you your own history whether you have ' +
        'agreed, refused, or never been asked.',
    'You can withdraw at any time, here, and it takes effect from the moment you do. Withdrawing ' +
        'does not delete anything - the control below does that.',
];

/* What deleting does, said before the control. The last sentence is the one a
 * reader most often assumes the other way round. Exported for the reason the
 * block above it is. */
export const WHAT_DELETING_DOES = [
    'This removes your play history from this plugin permanently. It happens as soon as you ' +
        'ask, there is no second step, and nothing is kept anywhere for a change of mind.',
    'It removes your rows only. Figures that name nobody were folded from them and are not ' +
        'recomputed, so the totals an administrator sees for the server do not change.',
    'It does not stop future recording. Plays after this go on being recorded, and what other ' +
        'people may see about them is the choice above.',
];

/**
 * Draws the two controls with what each does stated in front of it, or says
 * which of the other three situations the view is in.
 *
 * @param {{state: string, reason?: string, answered?: boolean, agreed?: boolean, agreedToVersion?: number, currentVersion?: number, wording?: string}} answer What this account has said, and the wording it is about, or the state it is in instead.
 * @returns {string} The view.
 */
export function yourPrivacyChoices(answer) {
    const state = stateOf(answer);

    if (state !== 'ready') {
        return (
            '<section class="stats-view stats-view-your-choices">' +
            stateNotice(state, { title: 'Your choices' }) +
            '</section>'
        );
    }

    return (
        '<section class="stats-view stats-view-your-choices">' +
        '<h2 class="stats-view-your-choices-title">Your choices</h2>' +
        consent(answer) +
        deletion() +
        '</section>'
    );
}

/**
 * The consent control, under what it governs and the words being agreed to.
 *
 * @param {object} answer What this account has said.
 * @returns {string} The markup.
 */
function consent(answer) {
    const version = versionOf(answer);
    const agreed = answer.agreed === true;

    return (
        '<div class="stats-view-your-choices-consent">' +
        '<h3>Being named to an administrator</h3>' +
        sentences('stats-view-your-choices-governs', WHAT_AGREEING_GOVERNS) +
        `<p class="stats-view-your-choices-stands">${escapeText(whereItStands(answer, version))}</p>` +
        '<div class="stats-view-your-choices-wording">' +
        wording(answer) +
        '</div>' +
        '<button type="button" class="stats-view-your-choices-consent-control" ' +
        `data-answer="${agreed ? 'withdraw' : 'agree'}" ` +
        `data-wording-version="${escapeText(version)}">` +
        escapeText(agreed ? 'Withdraw my agreement' : 'Agree to being named') +
        '</button>' +
        '</div>'
    );
}

/**
 * Where this account's answer stands, in one sentence.
 *
 * Never asked and refused are told apart. An account that has never been asked
 * has not said no, and a page reading the two the same could never tell somebody
 * there is a question waiting for them.
 *
 * An agreement to wording this build has moved past is its own sentence, because
 * it is an agreement standing over words the person has not read.
 *
 * @param {object} answer What this account has said.
 * @param {number} version The version of the wording this build ships.
 * @returns {string} The sentence.
 */
function whereItStands(answer, version) {
    if (answer.answered !== true) {
        return 'You have not been asked yet, so nothing has been agreed and nothing refused.';
    }

    if (answer.agreed !== true) {
        return 'You are not agreeing at the moment, so no view shows an administrator that these plays are yours.';
    }

    return answer.agreedToVersion === version
        ? 'You are agreeing to the words below as they stand.'
        : 'You agreed to an earlier version of the words below. They have changed since, so read ' +
              'them again and agree afresh if you still want to.';
}

/**
 * The words being agreed to, as the server wrote them.
 *
 * Drawn as the paragraphs the server separated with a blank line. A page that
 * put the whole text in one element would run an agreement together into a wall
 * a reader skips, and the one thing this control depends on is that it was read.
 *
 * @param {object} answer What this account has said, and the wording it is about.
 * @returns {string} The markup.
 */
function wording(answer) {
    const words = answer.wording;

    if (typeof words !== 'string' || words.trim() === '') {
        throw new Error(
            'The words being agreed to are the ones the server sent, and this answer carries ' +
                'none. A page that supplied its own would ask somebody to agree to text the ' +
                'record does not hold.',
        );
    }

    return words
        .split(/\n[ \t]*\n/)
        .map((paragraph) => paragraph.trim())
        .filter((paragraph) => paragraph !== '')
        .map((paragraph) => `<p>${escapeText(paragraph)}</p>`)
        .join('');
}

/**
 * The deletion control, under what it does.
 *
 * @returns {string} The markup.
 */
function deletion() {
    return (
        '<div class="stats-view-your-choices-deletion">' +
        '<h3>Deleting your history</h3>' +
        sentences('stats-view-your-choices-does', WHAT_DELETING_DOES) +
        '<button type="button" class="stats-view-your-choices-delete-control">' +
        'Delete my play history' +
        '</button>' +
        '</div>'
    );
}

/**
 * A block of sentences, one paragraph each.
 *
 * @param {string} className What the block is.
 * @param {ReadonlyArray<string>} said The sentences.
 * @returns {string} The markup.
 */
function sentences(className, said) {
    return (
        `<div class="${className}">` +
        said.map((sentence) => `<p>${escapeText(sentence)}</p>`).join('') +
        '</div>'
    );
}

/**
 * Which of the four situations the answer says it is in, or a refusal.
 *
 * @param {unknown} answer The answer.
 * @returns {string} The state.
 */
function stateOf(answer) {
    const state = answer === null || typeof answer !== 'object' ? undefined : answer.state;

    if (!STATES.includes(state)) {
        throw new Error(
            'This view is not drawn without a state saying whether the choices are ready, ' +
                'absent, still coming or unreadable. An answer that says nothing looks exactly ' +
                'like an account that has agreed to nothing, and offering a control over a ' +
                `state nobody read is worse than offering none. The states are ${STATES.join(', ')}.`,
        );
    }

    return state;
}

/**
 * The version of the wording this build ships, or a refusal.
 *
 * An agreement records the version the person was shown. A control that sent a
 * version it worked out for itself would record an agreement to whatever the
 * server happened to hold at that moment, which may not be the text on the page.
 *
 * @param {object} answer What this account has said.
 * @returns {number} The version.
 */
function versionOf(answer) {
    const version = answer.currentVersion;

    if (!Number.isInteger(version) || version < 1) {
        throw new Error(
            'This view is not drawn without the version of the wording it is showing. An ' +
                'agreement names the version the person read, and a control that named one of ' +
                'its own would record an agreement to text nobody was shown.',
        );
    }

    return version;
}
