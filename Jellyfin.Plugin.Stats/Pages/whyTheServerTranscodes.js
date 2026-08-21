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
 * The rows are drawn in the order they arrive. The fold sorts them by the plays
 * under each and settles ties on the server's own spelling, so the order is
 * decided once, where the rows are made, rather than twice.
 *
 * The bars do not add up to the plays and the caption says so before a reader
 * works it out. A play carries every reason the server gave for it, so the same
 * play is counted under each of them. That sentence is the reason
 * docs/transcode-reasons.md exists, and this view carries it rather than
 * pointing at a document a dashboard reader cannot open.
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
 * @param {{state: string, reason?: string, plays?: number, playsWithAReason?: number, rows?: ReadonlyArray<{reason: string, plays: number}>}} answer The breakdown, as the server folded it, or the state it is in instead.
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

    /* Two fields and no third. Whatever else a row arrives carrying does not
     * reach the drawing, which is what makes "this view names no user" a
     * statement about the code rather than about the data it was tested with. */
    const rows = (answer.rows ?? []).map((row) => ({
        name: typeof row.reason === 'string' ? row.reason : '',
        plays: row.plays,
    }));

    return (
        '<figure class="stats-view stats-view-reasons">' +
        barBreakdown(
            rows.map((row) => ({ label: row.name, value: row.plays })),
            {
                title: TITLE,
                description:
                    'One bar per reason the server gave, most plays first, under the names the ' +
                    'server writes in its own log.',
            },
        ) +
        glossary(rows) +
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
 * @param {{plays?: number, playsWithAReason?: number}} answer The breakdown.
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
        'not a miscount: these rows are not a division of the plays.'
    );
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
                `<dd class="stats-view-reason-sentence">${escapeText(sentenceFor(row.name))}</dd>`,
        )
        .join('');

    return `<dl class="stats-view-reasons-list">${entries}</dl>`;
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
