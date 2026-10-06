// (c) J@mBeL.net 2010-2026, John Ambeliotis. Part of jaNET Framework, licensed under the GNU GPL version 3 or later (see LICENSE).
// Small SVG gauge (replaces Raphael + JustGage).

const SVG_NS = 'http://www.w3.org/2000/svg';
const DEFAULT_COLORS = ['#0000ff', '#00ff00', '#ff0000'];

function svg(name, attrs = {}, text) {
    const el = document.createElementNS(SVG_NS, name);
    for (const [key, value] of Object.entries(attrs)) el.setAttribute(key, value);
    if (text !== undefined) el.textContent = text;
    return el;
}

function hexToRgb(hex) {
    const n = parseInt(hex.slice(1), 16);
    return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
}

// Colour for a position (0..1) along evenly spaced level colours.
export function levelColor(colors, fraction) {
    const f = Math.min(1, Math.max(0, fraction));
    if (colors.length === 1) return colors[0];
    const scaled = f * (colors.length - 1);
    const i = Math.min(colors.length - 2, Math.floor(scaled));
    const t = scaled - i;
    const a = hexToRgb(colors[i]);
    const b = hexToRgb(colors[i + 1]);
    return 'rgb(' + a.map((c, k) => Math.round(c + (b[k] - c) * t)).join(',') + ')';
}

export function formatValue(value, decimals) {
    const n = Number(value);
    if (!Number.isFinite(n)) return '–';
    return decimals ? n.toFixed(1) : String(Math.round(n));
}

/**
 * @param {HTMLElement} host   element that receives the gauge
 * @param {{title:string,label:string,min:number,max:number,decimals?:boolean,levelColors?:string[]}} options
 */
export function createGauge(host, options) {
    const { title, label, min, max, decimals = false, levelColors = DEFAULT_COLORS } = options;
    const arc = 'M30 105 A70 70 0 0 1 170 105';

    const root = svg('svg', { viewBox: '0 0 200 140', role: 'img' });
    const track = svg('path', { d: arc, class: 'g-track' });
    const bar = svg('path', { d: arc, class: 'g-value', pathLength: 100, 'stroke-dasharray': '0 100' });
    const text = svg('text', { x: 100, y: 100, class: 'g-text' }, '–');
    root.append(
        svg('text', { x: 100, y: 16, class: 'g-title' }, title),
        track, bar, text,
        svg('text', { x: 100, y: 124, class: 'g-label' }, label),
        svg('text', { x: 30, y: 128, class: 'g-limit', 'text-anchor': 'middle' }, String(min)),
        svg('text', { x: 170, y: 128, class: 'g-limit', 'text-anchor': 'middle' }, String(max))
    );
    root.setAttribute('aria-label', title + ': no data');
    host.replaceChildren(root);

    return {
        refresh(value) {
            const n = Number(value);
            if (!Number.isFinite(n)) return;
            const fraction = (n - min) / (max - min);
            const clamped = Math.min(1, Math.max(0, fraction));
            bar.setAttribute('stroke-dasharray', (clamped * 100).toFixed(2) + ' 100');
            bar.setAttribute('stroke', levelColor(levelColors, clamped));
            text.textContent = formatValue(n, decimals);
            root.setAttribute('aria-label', title + ': ' + text.textContent + ' ' + label);
        }
    };
}
