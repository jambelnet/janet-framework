// (c) J@mBeL.net 2010-2026. GNU GPL version 3 or later.
import { mapLabels } from './studio-model.js';
const ns = 'http://www.w3.org/2000/svg';
const colors = { instruction: '#2386ca', function: '#15978c', event: '#9667cb', condition: '#bd871a', program: '#c95660', command: '#546e85', delay: '#718250', text: '#657680', template: '#657680', raw: '#657680', cycle: '#c93e42', missing: '#c93e42' };
function svgElement(tag, attributes = {}, text) {
    const node = document.createElementNS(ns, tag);
    for (const [key, value] of Object.entries(attributes)) node.setAttribute(key, value);
    if (text != null) node.textContent = text;
    return node;
}
export function createFlow(container, select) {
    const svg = svgElement('svg', { role: 'group', 'aria-label': '2D dependency map', tabindex: 0 }); container.append(svg);
    let data, active = -1, visible = false, playing = false, frame = 0, lastStep = 0, bounds, viewport, down, moved = false;
    const elements = new Map();
    function render() { if (viewport) svg.setAttribute('viewBox', `${viewport.x} ${viewport.y} ${viewport.width} ${viewport.height}`); }
    function fit() {
        if (!bounds || !container.clientWidth || !container.clientHeight) return;
        const ratio = container.clientWidth / container.clientHeight;
        const width = Math.max(bounds.width, bounds.height * ratio), height = width / ratio;
        viewport = { x: bounds.x - (width - bounds.width) / 2, y: bounds.y - (height - bounds.height) / 2, width, height }; render();
    }
    function zoom(factor, x = .5, y = .5) {
        if (!viewport || !bounds) return;
        const width = Math.max(bounds.width / 8, Math.min(bounds.width * 4, viewport.width * factor)), height = width * viewport.height / viewport.width;
        viewport = { x: viewport.x + (viewport.width - width) * x, y: viewport.y + (viewport.height - height) * y, width, height }; render();
    }
    function highlight(id) {
        active = id;
        for (const [index, element] of elements) { element.classList.toggle('is-highlighted', index === id); element.setAttribute('aria-pressed', String(index === id)); }
        if (data?.nodes[id]) select(data.nodes[id]);
    }
    function update(graph) {
        animate(false); data = graph; active = -1; elements.clear(); svg.replaceChildren();
        const defs = svgElement('defs'), marker = svgElement('marker', { id: 'flowArrow', viewBox: '0 0 10 10', refX: 9, refY: 5, markerWidth: 6, markerHeight: 6, orient: 'auto-start-reverse' });
        marker.append(svgElement('path', { d: 'M 0 0 L 10 5 L 0 10 z', fill: '#8296a6' })); defs.append(marker); svg.append(defs);
        const layers = new Map(), positions = new Map();
        for (const node of graph.nodes) { if (!layers.has(node.depth)) layers.set(node.depth, []); layers.get(node.depth).push(node); }
        let maximum = 1;
        for (const [depth, nodes] of layers) { maximum = Math.max(maximum, nodes.length); nodes.forEach((node, index) => positions.set(node.id, { x: (index - (nodes.length - 1) / 2) * 280, y: depth * 150 })); }
        for (const edge of graph.edges) {
            const from = positions.get(edge.from), to = positions.get(edge.to), middle = (from.y + 76 + to.y) / 2;
            svg.append(svgElement('path', { d: `M ${from.x} ${from.y + 76} C ${from.x} ${middle}, ${to.x} ${middle}, ${to.x} ${to.y}`, class: 'flow-edge', 'marker-end': 'url(#flowArrow)' }));
            if (edge.label) { const caption = svgElement('text', { x: (from.x + to.x) / 2 + 10, y: middle - 5, class: 'flow-edge-label' }, edge.label.length > 28 ? edge.label.slice(0, 25) + '...' : edge.label); caption.append(svgElement('title', {}, edge.label)); svg.append(caption); }
        }
        for (const node of graph.nodes) {
            const position = positions.get(node.id), group = svgElement('g', { transform: `translate(${position.x - 115} ${position.y})`, class: 'flow-node', role: 'button', tabindex: 0, 'aria-label': `${mapLabels[node.kind]}: ${node.detail}`, 'aria-pressed': 'false' });
            group.style.setProperty('--node-color', colors[node.kind] ?? colors.raw);
            group.append(svgElement('title', {}, node.detail), svgElement('rect', { x: -3, y: -3, width: 236, height: 82, rx: 4, class: 'flow-halo' }), svgElement('rect', { width: 230, height: 76, rx: 4, class: 'flow-box' }));
            group.append(svgElement('text', { x: 14, y: 23, class: 'flow-kind' }, mapLabels[node.kind] ?? node.kind));
            const title = node.label.replace(/^judo inset run /, 'Call: ').replace(/^judo serial /, 'Serial: ');
            group.append(svgElement('text', { x: 14, y: 51, class: 'flow-title' }, title.length > 28 ? title.slice(0, 25) + '...' : title));
            group.addEventListener('click', () => { if (!moved) highlight(node.id); });
            group.addEventListener('keydown', event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); highlight(node.id); } });
            svg.append(group); elements.set(node.id, group);
        }
        bounds = { x: -maximum * 280 / 2 - 30, y: -40, width: maximum * 280 + 60, height: Math.max(150, ...graph.nodes.map(node => node.depth * 150 + 156)) }; fit();
    }
    svg.addEventListener('wheel', event => { event.preventDefault(); const rect = svg.getBoundingClientRect(); zoom(event.deltaY > 0 ? 1.15 : 1 / 1.15, (event.clientX - rect.left) / rect.width, (event.clientY - rect.top) / rect.height); }, { passive: false });
    svg.addEventListener('pointerdown', event => { if (!viewport || event.button !== 0) return; moved = false; down = { id: event.pointerId, x: event.clientX, y: event.clientY, viewport: { ...viewport } }; });
    svg.addEventListener('pointermove', event => {
        if (!down || event.pointerId !== down.id) return;
        const dx = event.clientX - down.x, dy = event.clientY - down.y;
        if (!moved && Math.hypot(dx, dy) < 6) return;
        if (!moved) svg.setPointerCapture(event.pointerId); moved = true;
        viewport = { ...down.viewport, x: down.viewport.x - dx * down.viewport.width / container.clientWidth, y: down.viewport.y - dy * down.viewport.height / container.clientHeight }; render();
    });
    const release = () => { down = null; };
    window.addEventListener('pointerup', release); window.addEventListener('pointercancel', release); svg.addEventListener('lostpointercapture', release);
    function step() { if (data?.nodes.length) highlight((active + 1) % data.nodes.length); }
    function tick(time) { frame = 0; if (!visible || !playing) return; if (time - lastStep > 1000) { step(); lastStep = time; } frame = requestAnimationFrame(tick); }
    function animate(value) { playing = value; if (frame) cancelAnimationFrame(frame); frame = 0; if (playing && visible) { lastStep = performance.now(); frame = requestAnimationFrame(tick); } }
    const observer = new ResizeObserver(() => { if (visible) fit(); }); observer.observe(container);
    return { update, fit, zoom, step, animate, theme() {}, setVisible(value) { visible = value; animate(playing); if (value && !viewport) fit(); }, dispose() { animate(false); observer.disconnect(); window.removeEventListener('pointerup', release); window.removeEventListener('pointercancel', release); svg.remove(); } };
}
