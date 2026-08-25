/*
 * The view of why the server did not pass plays through as they were.
 *
 * The reasons are the actionable part of this plugin. A server re-encoding four
 * fifths of its plays because one client cannot read one container is a fixable
 * problem, and the count on its own is only an alarm. Issue #60.
 *
 * The names are the server's own, unchanged. `docs/transcode-reasons.md` says
 * why they are never tidied: an administrator meets these words in the server's
 * log, and a plugin that renamed them would print something that cannot be
 * looked up. So each row is drawn under the name the server gave and the
 * sentence explaining it is written beside the row rather than in place of it.
 *
 * What a sentence claims and what it does not. It says what condition the name
 * reports. It does not say what the server did about that condition, because
 * the row records the reason and never the remedy, and a page asserting the
 * remedy would be reading a decision out of a value that does not hold one.
 *
 * A name this module has no sentence for is drawn with the row and said to be
 * unexplained. That case is not hypothetical and it is not the same as a
 * mistake: a stored row outlives the assembly that wrote it, and a server newer
 * than this build reports reasons this build has never seen. Dropping the row
 * would take plays out of a picture whose whole point is where the re-encoding
 * comes from, and inventing a sentence would put a guess in the column a reader
 * acts on.
 *
 * The rows are drawn in the order they arrive. The fold sorts them by the
 * watched time under each, then by the plays, then on the server's own
 * spelling, so the order is decided once, where the rows are made, rather than
 * twice. The bars are that same watched time, because a row ordered by one
 * figure and drawn at the length of another is a picture that contradicts its
 * own order; the play count stays beside the row, where the two readings can be
 * compared instead of one standing in for the other.
 *
 * What the server re-encoded with is drawn under the bars and not as bars. It
 * is a partition — a play carries one acceleration and every reason the server
 * gave — so putting it in the same picture as the reasons would invite exactly
 * the addition the caption spends a sentence refusing. It is listed with its
 * own sentence saying that these rows do add up.
 *
 * The bars do not add up to the plays and the caption says so before a reader
 * works it out. A play carries every reason the server gave for it, so the same
 * play is counted under each of them. That sentence is the reason
 * docs/transcode-reasons.md exists, and this view carries it rather than
 * pointing at a document a dashboard reader cannot open.
 *
 * The watched time beside each reason is the whole of the time the plays under
 * it were watched for, counted in full and never divided between the reasons
 * one play carries. So the minutes here can total more than the range holds,
 * for exactly the same arithmetic as the bars, and the caption says that in a
 * sentence rather than leaving a reader to discover it by adding the column up.
 * A divided figure would read as a share of the range and would be a length of
 * time nobody watched: the server did not spend a third of a play on the
 * container and two thirds on the codecs, it re-encoded one play under all
 * three conditions at once. Issue #242.
 *
 * It names nobody. Two fields are taken off each row, the reason and the plays
 * under it, and no third is looked at. The fold it reads has no user in its row
 * shape either, so this is a property of the code rather than of the rows it
 * was tested with. Issue #41 holds why a breakdown that can group by a user is
 * the thing the consent rule stands in front of.
 *
 * The answer it is handed says which situation it is in, and the four are ready,
 * nothing recorded yet, still loading and could not be read. An answer that says
 * nothing is refused rather than taken as ready, because a breakdown with no
 * rows is what a server that re-encodes nothing looks like and also what a
 * failed request looks like. Issue #64.
 *
 * No document, no window, no network and no clock, the same as the drawing
 * module. docs/headless-tests.md.
 */

import { barBreakdown, escapeText, stateNotice } from './charts.js';

/* What an answer may say it is. Ready is the one that carries rows; the other
 * three are the situations issue #64 asks every view to tell apart. */
const STATES = ['ready', 'empty', 'loading', 'failed'];

/* What the drawing is called, used for the three situations as well, so a view
 * that could not be read still says which view it was. */
const TITLE = 'Why plays were re-encoded';

/* One sentence per reason the server can report, keyed on the exact name the
 * server spells it with, which is the name the capture fold stores on the row.
 *
 * This is the list issue #60's first condition is about. Nothing here can make
 * a reason the server adds later a build failure: the names belong to an enum
 * in the server's own package, and a page keyed on that enum compiles happily
 * without a new member. What holds the list instead is a case in the .NET suite
 * that walks the enum this build compiles against and asks this object for each
 * member, and what that case can and cannot see is written where it lives.
 *
 * The sentences describe the condition, in the words an administrator would use
 * about their own server rather than the words the server source uses about
 * itself. */
const REASONS = {
    ContainerNotSupported: 'The client does not read the file format the media is packaged in.',
    VideoCodecNotSupported: 'The client does not decode the video codec the file is encoded with.',
    AudioCodecNotSupported: 'The client does not decode the audio codec the file is encoded with.',
    SubtitleCodecNotSupported:
        'The client does not render the subtitles in the form they are stored in.',
    AudioIsExternal:
        'The audio for this play is a separate file from the video rather than a track inside it.',
    SecondaryAudioNotSupported:
        'The play asked for a second audio track alongside the first and the client takes one.',
    VideoProfileNotSupported:
        'The video uses a profile of its codec that the client does not implement.',
    VideoLevelNotSupported:
        'The video is encoded at a level of its codec above what the client accepts.',
    VideoResolutionNotSupported: 'The picture is larger than the resolution the client accepts.',
    VideoBitDepthNotSupported:
        'The video carries more bits per colour sample than the client reads, ten to a client that reads eight.',
    VideoFramerateNotSupported: 'The video runs at a frame rate the client does not accept.',
    RefFramesNotSupported:
        'The video refers back to more earlier frames than the client keeps while decoding.',
    AnamorphicVideoNotSupported:
        'The video stores pixels that are not square and the client would draw the picture at the wrong shape.',
    InterlacedVideoNotSupported:
        'The video is interlaced and the client does not weave the two fields into a frame.',
    AudioChannelsNotSupported: 'The audio has more channels than the client plays.',
    AudioProfileNotSupported:
        'The audio uses a profile of its codec that the client does not implement.',
    AudioSampleRateNotSupported: 'The audio is sampled at a rate the client does not accept.',
    AudioBitDepthNotSupported: 'The audio carries more bits per sample than the client accepts.',
    ContainerBitrateExceedsLimit: 'The file as a whole is above the bitrate this play was allowed.',
    VideoBitrateNotSupported:
        'The video is above the bitrate the client or the connection was allowed.',
    AudioBitrateNotSupported:
        'The audio is above the bitrate the client or the connection was allowed.',
    UnknownVideoStreamInfo:
        'The server could not read enough about the video stream to tell whether the client would play it.',
    UnknownAudioStreamInfo:
        'The server could not read enough about the audio stream to tell whether the client would play it.',
    DirectPlayError: 'Sending the file as it is was attempted for this play and did not work.',
    VideoRangeTypeNotSupported:
        'The video is in a dynamic range the client does not show, high dynamic range to a client that draws the standard one.',
    VideoCodecTagNotSupported:
        'The video is in a codec the client reads but is tagged in the file in a way the client rejects.',
    StreamCountExceedsLimit:
        'The file holds more streams than this play was allowed to send at once.',
    VideoRotationNotSupported:
        'The video carries a rotation the client does not apply while drawing it.',
};

/**
 * Draws one row per reason the plays in this range recorded, with the sentence
 * for each beneath the picture, or says which of the other three situations the
 * view is in.
 *
 * @param {{state: string, reason?: string, plays?: number, playsWithAReason?: number, watchedMinutes?: number, rows?: ReadonlyArray<{reason: string, plays: number, watchedMinutes?: number}>, acceleration?: ReadonlyArray<{type: string|null, plays: number, watchedMinutes?: number}>}} answer The breakdown, as the server folded it, or the state it is in instead.
 * @returns {string} The view.
 */
export function whyTheServerTranscodes(answer) {
    const state = stateOf(answer);

    if (state !== 'ready') {
        return (
            '<figure class="stats-view stats-view-reasons">' +
            stateNotice(state, { title: TITLE, reason: answer.reason }) +
            '</figure>'
        );
    }

    /* Three fields and no fourth. Whatever else a row arrives carrying does not
     * reach the drawing, which is what makes "this view names no user" a
     * statement about the code rather than about the data it was tested with. */
    const rows = (answer.rows ?? []).map((row) => ({
        name: typeof row.reason === 'string' ? row.reason : '',
        plays: row.plays,
        watchedMinutes: typeof row.watchedMinutes === 'number' ? row.watchedMinutes : null,
    }));

    return (
        '<figure class="stats-view stats-view-reasons">' +
        barBreakdown(
            rows.map((row) => ({
                label: row.name,
                /* Null and never nought where the answer carries no time. A bar
                 * of nought and a bar nobody has a figure for are drawn
                 * differently by the drawing module, which is issue #64. */
                value: row.watchedMinutes === null ? null : rounded(row.watchedMinutes),
            })),
            {
                title: TITLE,
                description:
                    'One bar per reason the server gave, most watched time first, under the ' +
                    'names the server writes in its own log.',
            },
        ) +
        glossary(rows) +
        accelerations(answer) +
        '<figcaption class="stats-view-note">' +
        escapeText(caption(answer, rows)) +
        '</figcaption>' +
        '</figure>'
    );
}

/**
 * What the drawing says about itself under the picture.
 *
 * The two counts are the ones the answer carries rather than sums over the
 * bars. The fold counts the plays it was handed and the plays that recorded any
 * reason separately from the rows it produced, so the three are separate
 * statements, and a view that added the bars up and called that a total would
 * make them one again and print the number this caption exists to warn about.
 *
 * @param {{plays?: number, playsWithAReason?: number, watchedMinutes?: number}} answer The breakdown.
 * @param {ReadonlyArray<{name: string}>} rows The rows being drawn.
 * @returns {string} The sentence.
 */
function caption(answer, rows) {
    const plays = typeof answer.plays === 'number' ? answer.plays : null;
    const withAReason =
        typeof answer.playsWithAReason === 'number' ? answer.playsWithAReason : null;

    const counted =
        plays === null || withAReason === null
            ? `${rows.length} reasons in this range.`
            : `${rows.length} reasons over ${withAReason} of the ${plays} plays in this range.`;

    return (
        `${counted} A play carries every reason the server gave for it and a server usually ` +
        'gives more than one, so the same play is counted under each of its reasons and the ' +
        'bars add up to more than the plays they came from. That is the right answer here and ' +
        'not a miscount: these rows are not a division of the plays. ' +
        timeCaption(answer)
    );
}

/**
 * The same warning for the watched time, which is the other figure on this view
 * a reader can add up.
 *
 * The period is the one the fold counted rather than a sum over the rows, for
 * the reason the play counts give: a sum over the rows is the number this
 * sentence exists to warn about, and printing it as the period would state the
 * misreading as fact.
 *
 * @param {{watchedMinutes?: number}} answer The breakdown.
 * @returns {string} The sentence.
 */
function timeCaption(answer) {
    const period = typeof answer.watchedMinutes === 'number' ? answer.watchedMinutes : null;

    const held =
        period === null
            ? 'the time this range holds'
            : `the ${minutes(period)} minutes this range holds`;

    return (
        'The watched time under each reason is counted the same way. A play carrying three ' +
        'reasons puts the whole of its watched time under all three rather than a third of ' +
        `it under each, so these times can total more than ${held}. Nothing here is divided ` +
        'between the reasons of one play, because a divided figure is a length of time ' +
        'nobody watched.'
    );
}

/**
 * A figure in minutes, as text.
 *
 * Rounded to a tenth of a minute rather than printed as the fold handed it
 * over. The fold sums exact ticks and divides once at the end, so a range of
 * ordinary plays arrives here with a tail of digits that says nothing a reader
 * of a dashboard wants. Nothing reads a figure back off this page, so the
 * rounding is the last thing that happens to the number rather than something
 * a later step has to undo.
 *
 * @param {number} value The figure the fold handed over.
 * @returns {string} The figure as text.
 */
function minutes(value) {
    return String(rounded(value));
}

/**
 * The same rounding as a number, for the drawing rather than for the text.
 *
 * The bar and the sentence beside it are rounded once and in one place, so a
 * reader comparing the two never meets a bar drawn at one figure and labelled
 * with another.
 *
 * @param {number} value The figure the fold handed over.
 * @returns {number} The figure, to a tenth of a minute.
 */
function rounded(value) {
    return Math.round(value * 10) / 10;
}

/**
 * The sentence for each reason drawn, under the picture.
 *
 * Only the reasons in front of the reader are explained. A list of every reason
 * the server can report would be a page of conditions that did not happen,
 * sitting under a chart of the ones that did, and the sentence a reader came
 * for would be somewhere in it.
 *
 * @param {ReadonlyArray<{name: string, plays: number}>} rows The rows being drawn.
 * @returns {string} The list, or nothing where there is no row.
 */
function glossary(rows) {
    if (rows.length === 0) {
        return '';
    }

    const entries = rows
        .map(
            (row) =>
                `<dt class="stats-view-reason-name">${escapeText(row.name)}</dt>` +
                `<dd class="stats-view-reason-watched">${escapeText(watchedUnder(row))}</dd>` +
                `<dd class="stats-view-reason-sentence">${escapeText(sentenceFor(row.name))}</dd>`,
        )
        .join('');

    return `<dl class="stats-view-reasons-list">${entries}</dl>`;
}

/**
 * How much of the plays under one reason was watched.
 *
 * A row the answer carries no figure for says so rather than saying nought. A
 * reason that held for plays nobody watched a minute of and a reason whose
 * watched time the answer never carried are different facts, and a nought said
 * for both is the one a reader would act on. Issue #64.
 *
 * @param {{plays: number, watchedMinutes: number|null}} row The row being drawn.
 * @returns {string} The sentence.
 */
function watchedUnder(row) {
    const plays = typeof row.plays === 'number' ? `${row.plays} plays. ` : '';

    if (row.watchedMinutes === null) {
        return `${plays}Watched time not recorded for this reason.`;
    }

    return (
        `${plays}${minutes(row.watchedMinutes)} minutes watched, which is the whole of every ` +
        'play under this reason and no share of one.'
    );
}

/**
 * What the server re-encoded these plays with, under the bars.
 *
 * Drawn as a list and not as bars, because it is a partition and the reasons
 * are not. Two pictures of the same range, one whose rows add up and one whose
 * rows do not, invite the reader to add both, and the one that must not be
 * added is the one this whole view is about.
 *
 * An answer carrying no acceleration list draws nothing here rather than an
 * empty one. A range that was re-encoded on hardware nobody recorded and an
 * answer from a build that does not carry the figure are different facts, and
 * an empty list said for both is the one a reader would act on. Issue #64.
 *
 * @param {{acceleration?: ReadonlyArray<{type: string|null, plays: number, watchedMinutes?: number}>}} answer The breakdown.
 * @returns {string} The list, or nothing.
 */
function accelerations(answer) {
    const rows = Array.isArray(answer.acceleration) ? answer.acceleration : [];

    if (rows.length === 0) {
        return '';
    }

    const entries = rows
        .map((row) => {
            const named = typeof row.type === 'string' && row.type !== '';
            const name = named ? row.type : 'No acceleration reported';
            const watched =
                typeof row.watchedMinutes === 'number'
                    ? `${minutes(row.watchedMinutes)} minutes watched`
                    : 'watched time not recorded';
            const said = named
                ? `${row.plays} plays, ${watched}.`
                : `${row.plays} plays, ${watched}. This group holds a play the server passed ` +
                  'through untouched as well as one it re-encoded without hardware, and these ' +
                  'figures cannot tell the two apart.';

            return (
                `<dt class="stats-view-acceleration-name">${escapeText(name)}</dt>` +
                `<dd class="stats-view-acceleration-figures">${escapeText(said)}</dd>`
            );
        })
        .join('');

    return (
        '<dl class="stats-view-accelerations-list">' +
        entries +
        '</dl>' +
        '<p class="stats-view-accelerations-note">' +
        escapeText(
            'A play carries one acceleration, so unlike the bars above these rows do add up ' +
                'to the plays in this range.',
        ) +
        '</p>'
    );
}

/**
 * What one reason means, or a sentence saying that this build does not know.
 *
 * The unknown case is what a row from a newer server looks like, and it is
 * answered rather than hidden. A reader who meets it can take the name to the
 * server's own documentation, which is the same place the sentences above came
 * from, and they can see that the plugin is not pretending to explain it.
 *
 * @param {string} name The reason, as the server spelled it.
 * @returns {string} The sentence.
 */
function sentenceFor(name) {
    if (Object.prototype.hasOwnProperty.call(REASONS, name)) {
        return REASONS[name];
    }

    return (
        'This build has no sentence for this reason. It is a name a newer server reports, so ' +
        'the plays under it are counted here and the explanation is the server documentation ' +
        'for that name.'
    );
}

/**
 * Which of the four situations the answer says it is in, or a refusal.
 *
 * An answer that names no state is refused rather than drawn. A view cannot work
 * out from an empty set of rows whether what is in front of it is a server that
 * re-encoded nothing, a request still in flight or a store that would not open,
 * and the caller is the only party that knows.
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
                `like a server that re-encoded nothing. The states are ${STATES.join(', ')}.`,
        );
    }

    return state;
}
