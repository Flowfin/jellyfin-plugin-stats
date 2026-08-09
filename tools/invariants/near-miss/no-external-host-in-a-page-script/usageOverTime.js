/*
 * The usage over time view, with its drawing left to somebody else's server.
 *
 * This is the near miss for no-external-host-in-a-page-script. It is the shape
 * the closest prior art has, moved into the file a page keeps its script in,
 * which is the file the HTML rule beside it cannot read.
 *
 * The one character version of the mistake is on the second import. A module
 * path beginning with a single slash is the plugin's own asset, served from the
 * server the page was loaded from. A second slash makes it a host, and the
 * browser goes to the internet for it over whatever scheme the page used. The
 * two look alike in a diff and behave nothing alike on a server with no way
 * out.
 */

import { Chart } from 'https://cdn.jsdelivr.net/npm/chart.js/+esm';
import { palette } from '//cdn.jsdelivr.net/npm/chartjs-plugin-colorschemes/+esm';

const face = new FontFace('Inter', "url('https://fonts.example.net/inter.woff2')");

export async function draw(target, range) {
    const rows = await fetch('https://stats.example.net/aggregate?range=' + range);
    const worker = new Worker('https://cdn.example.net/stats-worker.js');

    target.innerHTML = '<img src="https://img.example.net/spinner.gif" alt="" />';

    return new Chart(target, {
        type: 'line',
        data: await rows.json(),
        options: { plugins: { colorschemes: { scheme: palette } }, font: face, worker }
    });
}
