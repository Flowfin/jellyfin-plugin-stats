/*
 * The view of which clients and which devices the plays came from, and how many
 * of them the server had to re-encode.
 *
 * When something transcodes the useful question is what asked it to, and the
 * answer is a client and a device rather than a person. Issue #59.
 *
 * The fold behind this returns one row per member with the delivery figures for
 * the plays under it, and it deliberately leaves two things to whoever draws the
 * rows. This module is where both are decided.
 *
 * The first is what to call the row holding the plays the server named nothing
 * for. The fold carries no name there rather than the word unknown, because a
 * client that calls itself Unknown is a real client and a made-up label folds
 * the two into one bar that is neither. What is written here is a phrase instead
 * of that word, so a reader can tell the group nobody could name from a client
 * whose name happens to be it.
 *
 * The second is division. Neither the fold nor the figures underneath it divide
 * anything, on the grounds that a share over a range with no plays in it has no
 * answer. This view does not divide either, and shows the plays that were
 * re-encoded as their own drawing under the plays they came out of, so the
 * reader reads one against the other. Two counts a reader can check beat one
 * percentage they cannot: a member whose delivery the server never reported has
 * a share that rests on a denominator smaller than its bar, and a drawing of
 * percentages has nowhere to say which member that was.
 *
 * A member the server reported no delivery method for at all is drawn as absent
 * in the second drawing rather than as nought. Nought there says the server
 * never re-encoded anything for that member, and what is true is that nothing is
 * known about it. That is the rule the drawing module already holds, where a
 * value that is not known is written as null and is never drawn as zero.
 *
 * It names nobody, and that is a property of what it reads rather than of what
 * it was careful to leave out: two fields are taken off each row and no third is
 * looked at, and the dimensions it will draw are a closed set of two that has no
 * user in it. Issue #41 holds why a breakdown that can group by a user is the
 * thing the consent rule stands in front of.
 *
 * The answer it is handed says which situation it is in, and the four are ready,
 * nothing recorded yet, still loading and could not be read. An answer that says
 * nothing is refused rather than taken as ready, because a breakdown with no
 * rows is what a quiet server looks like and also what a failed request looks
 * like. Issue #64.
 *
 * No document, no window, no network and no clock, the same as the drawing
 * module. docs/headless-tests.md.
 */

import { barBreakdown, escapeText, stateNotice } from './charts.js';

/* What the group of members too few accounts stand behind is called in the
 * picture. A wording rather than a name, and one that says what the bar IS: a
 * label reading like a client would put a thing nobody uses in a list of things
 * people use, and a label reading like a person would be the one reading issue
 * #41's third condition refuses in as many words. */
const GROUPED_TOGETHER = 'Grouped together, too few accounts to show separately';

/* What an answer may say it is. Ready is the one that carries rows; the other
 * three are the situations issue #64 asks every view to tell apart. */
const STATES = ['ready', 'empty', 'loading', 'failed'];

/* The two things a set of plays may be broken down by here, and the words each
 * one is drawn under. The set is closed and is the same set the fold groups by,
 * which is a user short of the set somebody would otherwise reach for. */
const DIMENSIONS = {
    client: {
        plays: 'Plays by client',
        transcoded: 'Plays re-encoded, by client',
        nothingNamed: 'No client reported',
        sentence: 'One bar per client application, most plays first.',
    },
    device: {
        plays: 'Plays by device',
        transcoded: 'Plays re-encoded, by device',
        nothingNamed: 'No device reported',
        sentence: 'One bar per device, most plays first.',
    },
};

/**
 * Draws the members of one dimension with the plays they account for and the
 * plays of theirs the server re-encoded, or says which of the other three
 * situations the view is in.
 *
 * @param {{state: string, dimension?: string, plays?: number, rows?: ReadonlyArray<{name: string|null, delivery: {plays: number, unknown: number, transcode: number}}>, combined?: {plays: number, unknown: number, transcode: number}|null}} answer The breakdown, as the server folded it, or the state it is in instead.
 * @returns {string} The view.
 */
export function clientsAndDevices(answer) {
    const state = stateOf(answer);
    const dimension = dimensionOf(answer);

    if (state !== 'ready') {
        return (
            '<figure class="stats-view stats-view-breakdown">' +
            stateNotice(state, { title: dimension.plays }) +
            '</figure>'
        );
    }

    /* Two fields and no third. Whatever else a row arrives carrying does not
     * reach the drawing, which is what makes "this view names no user" a
     * statement about the code rather than about the data it was tested with. */
    const named = (answer.rows ?? []).map((row) => ({
        label: labelOf(row, dimension),
        plays: row.delivery.plays,
        unknown: row.delivery.unknown,
        transcode: row.delivery.transcode,
    }));

    /* The group the fold put the members too few accounts stand behind into. It
     * is drawn last and under a wording of this view's own, because it is the
     * one bar in the picture that is not a client or a device. It arrives with
     * no name and no key on purpose, and inventing one here would put it in the
     * picture as though somebody used it. Issue #41. */
    const folded = answer.combined
        ? [
              {
                  label: GROUPED_TOGETHER,
                  plays: answer.combined.plays,
                  unknown: answer.combined.unknown,
                  transcode: answer.combined.transcode,
              },
          ]
        : [];

    const members = named.concat(folded);

    const unreported = members.reduce((running, member) => running + member.unknown, 0);

    return (
        '<figure class="stats-view stats-view-breakdown">' +
        barBreakdown(
            members.map((member) => ({ label: member.label, value: member.plays })),
            { title: dimension.plays, description: dimension.sentence },
        ) +
        barBreakdown(
            members.map((member) => ({
                label: member.label,
                /* A member whose plays were all delivered by a method the server
                 * never reported has no bar here. Its re-encoded count is nought
                 * and nought is the one reading that is certainly wrong: it says
                 * the server sent everything as it was, and what is true is that
                 * nothing was reported either way. */
                value: member.plays === member.unknown ? null : member.transcode,
            })),
            {
                title: dimension.transcoded,
                description:
                    'The plays above that were re-encoded, in the same order, so the two ' +
                    'drawings are read against each other.',
            },
        ) +
        '<figcaption class="stats-view-note">' +
        escapeText(caption(answer, members, unreported, folded.length > 0)) +
        '</figcaption>' +
        '</figure>'
    );
}

/**
 * What the drawings say about themselves under the picture.
 *
 * The play count is the one the answer carries rather than the sum of the rows.
 * The fold counts the plays it was handed separately from the rows it produced
 * so that the two are separate statements, and a view that added the bars up
 * and called that the total would make them one again.
 *
 * @param {{plays?: number}} answer The breakdown.
 * @param {ReadonlyArray<{label: string}>} members The rows being drawn.
 * @param {number} unreported How many plays carried no delivery method.
 * @param {boolean} anyFolded Whether one of the bars is the grouped-together one.
 * @returns {string} The sentence.
 */
function caption(answer, members, unreported, anyFolded) {
    const plays = typeof answer.plays === 'number' ? answer.plays : null;
    const counted =
        plays === null
            ? `${members.length} in this range.`
            : `${members.length} over ${plays} plays in this range, and every play is in exactly one of them.`;

    /* Said in the caption and not only in a bar label, because a reader who
     * counts the bars and finds fewer clients than they know their server has
     * would otherwise conclude the plugin lost some. What it did was decline to
     * name them. */
    const grouping = anyFolded
        ? ' One bar is the members too few accounts use for this view to show them ' +
          'separately, put together so that their plays are still counted.'
        : '';

    if (unreported === 0) {
        return `${counted} The server reported how it delivered every one of them.${grouping}`;
    }

    return (
        `${counted} The server reported no delivery method for ${unreported} of those plays, ` +
        'so a bar in the lower drawing counts the plays known to have been re-encoded and ' +
        `not the plays that were.${grouping}`
    );
}

/**
 * What to call one row.
 *
 * A row the server named nothing for is drawn rather than dropped, because the
 * rows are a partition and taking one out would leave a reader adding the bars
 * up and meeting a larger play count beside them. Several such rows can arrive
 * under one dimension, a device with an identifier and no name being the case,
 * and they are drawn under one wording rather than under an identifier: the
 * identifier says nothing to the administrator reading the picture, and this
 * issue asks for the word rather than for an invented name.
 *
 * @param {{name: string|null}} row The row.
 * @param {{nothingNamed: string}} dimension The words this dimension is drawn under.
 * @returns {string} The label.
 */
function labelOf(row, dimension) {
    const name = typeof row.name === 'string' ? row.name.trim() : '';

    return name === '' ? dimension.nothingNamed : name;
}

/**
 * Which of the four situations the answer says it is in, or a refusal.
 *
 * An answer that names no state is refused rather than drawn. A view cannot work
 * out from an empty set of rows whether what is in front of it is a server
 * nobody has played anything on, a request still in flight or a store that would
 * not open, and the caller is the only party that knows.
 *
 * @param {unknown} answer The answer.
 * @returns {string} The state.
 */
function stateOf(answer) {
    const state = answer === null || typeof answer !== 'object' ? undefined : answer.state;

    if (!STATES.includes(state)) {
        throw new Error(
            'This view is not drawn without a state saying whether its rows are ready, ' +
                'absent, still coming or unreadable. An answer that says nothing looks exactly ' +
                `like a server nobody has used. The states are ${STATES.join(', ')}.`,
        );
    }

    return state;
}

/**
 * Which dimension the answer was folded over, or a refusal.
 *
 * The set is closed here and not widened by what arrives. A view that took the
 * wording from the answer would let whatever produced it choose what the picture
 * claims to be about, and the fold groups by two things.
 *
 * @param {unknown} answer The answer.
 * @returns {{plays: string, transcoded: string, nothingNamed: string, sentence: string}} The words to draw it under.
 */
function dimensionOf(answer) {
    const named = answer === null || typeof answer !== 'object' ? undefined : answer.dimension;

    if (typeof named !== 'string' || !Object.prototype.hasOwnProperty.call(DIMENSIONS, named)) {
        throw new Error(
            `There is no breakdown called ${named}. A view drawn under a heading it worked out ` +
                'for itself would name one dimension over the rows of another, which reads ' +
                `exactly like a correct picture. The breakdowns are ${Object.keys(DIMENSIONS).join(', ')}.`,
        );
    }

    return DIMENSIONS[named];
}
