// (c) J@mBeL.net 2010-2026. GNU GPL version 3 or later.
import * as THREE from 'three';
import { OrbitControls } from '../vendor/three/OrbitControls.js';
import { mapLabels } from './studio-model.js';
const colors = { instruction: 0x2386ca, function: 0x15978c, event: 0x9667cb, condition: 0xbd871a, program: 0xc95660, command: 0x546e85, delay: 0x718250, text: 0x657680, raw: 0x657680, cycle: 0xc93e42, missing: 0xc93e42 };
export function createGraph(container, select) {
    const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false, preserveDrawingBuffer: true, powerPreference: 'low-power' });
    renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
    renderer.domElement.setAttribute('aria-label', '3D dependency map. Node details are available in the dependency list.');
    container.append(renderer.domElement);
    const scene = new THREE.Scene(), camera = new THREE.PerspectiveCamera(42, 1, .1, 1000);
    const controls = new OrbitControls(camera, renderer.domElement); controls.enableDamping = false; controls.maxDistance = 450; controls.minDistance = 3;
    controls.addEventListener('change', render);
    let group = new THREE.Group(), meshes = [], data, focus, active = -1, visible = false, playing = false, frame = 0, lastStep = 0;
    scene.add(group);
    const raycaster = new THREE.Raycaster(), pointer = new THREE.Vector2();
    let down;
    renderer.domElement.addEventListener('pointerdown', event => down = { x: event.clientX, y: event.clientY });
    renderer.domElement.addEventListener('pointerup', event => {
        if (!down || Math.hypot(event.clientX - down.x, event.clientY - down.y) > 6) return;
        const bounds = renderer.domElement.getBoundingClientRect();
        pointer.set((event.clientX - bounds.left) / bounds.width * 2 - 1, -(event.clientY - bounds.top) / bounds.height * 2 + 1);
        raycaster.setFromCamera(pointer, camera);
        const hit = raycaster.intersectObjects(meshes)[0];
        if (hit) highlight(hit.object.userData.node.id);
    });
    renderer.domElement.addEventListener('webglcontextlost', event => { event.preventDefault(); playing = false; });
    function render() { if (visible) renderer.render(scene, camera); }
    function resize() {
        const width = container.clientWidth, height = container.clientHeight;
        if (!width || !height) return;
        renderer.setSize(width, height, false); camera.aspect = width / height; camera.updateProjectionMatrix(); render();
    }
    const observer = new ResizeObserver(resize); observer.observe(container);
    function theme() { scene.background = new THREE.Color(getComputedStyle(document.body).backgroundColor); render(); }
    function label(text, width = 3.9, height = 1.05, tint = '#ffffff') {
        const canvas = document.createElement('canvas'); canvas.width = 512; canvas.height = 128;
        const ctx = canvas.getContext('2d'); ctx.clearRect(0, 0, 512, 128); ctx.font = '600 56px system-ui'; ctx.textAlign = 'center'; ctx.textBaseline = 'middle'; ctx.fillStyle = tint;
        ctx.fillText(text, 256, 64, 472);
        const texture = new THREE.CanvasTexture(canvas); texture.colorSpace = THREE.SRGBColorSpace;
        const caption = new THREE.Mesh(new THREE.PlaneGeometry(width, height), new THREE.MeshBasicMaterial({ map: texture, transparent: true, depthTest: false, depthWrite: false, side: THREE.DoubleSide }));
        caption.renderOrder = 2; return caption;
    }
    function outline(width, height, inset, color) {
        const shape = new THREE.Shape();
        shape.moveTo(-width / 2, -height / 2); shape.lineTo(width / 2, -height / 2); shape.lineTo(width / 2, height / 2); shape.lineTo(-width / 2, height / 2); shape.closePath();
        const hole = new THREE.Path(), x = width / 2 - inset, y = height / 2 - inset;
        hole.moveTo(-x, -y); hole.lineTo(-x, y); hole.lineTo(x, y); hole.lineTo(x, -y); hole.closePath(); shape.holes.push(hole);
        return new THREE.Mesh(new THREE.ShapeGeometry(shape), new THREE.MeshBasicMaterial({ color, side: THREE.DoubleSide }));
    }
    function clear() {
        const textures = new Set();
        group.traverse(object => {
            object.geometry?.dispose();
            if (object.material) { if (object.material.map) textures.add(object.material.map); object.material.dispose(); }
        });
        for (const texture of textures) texture.dispose();
        scene.remove(group); group = new THREE.Group(); scene.add(group); meshes = []; focus = null; active = -1;
    }
    function update(graph) {
        clear(); data = graph;
        const layers = new Map();
        for (const node of graph.nodes) { if (!layers.has(node.depth)) layers.set(node.depth, []); layers.get(node.depth).push(node); }
        const positions = new Map();
        for (const [depth, nodes] of layers) nodes.forEach((node, index) => positions.set(node.id, new THREE.Vector3((index - (nodes.length - 1) / 2) * 5, -depth * 3, depth % 2 ? -.55 : .55)));
        for (const edge of graph.edges) {
            const from = positions.get(edge.from), to = positions.get(edge.to);
            const start = from.clone().add(new THREE.Vector3(0, -.7, 0)), end = to.clone().add(new THREE.Vector3(0, .7, 0));
            const middle = start.clone().lerp(end, .5);
            const curve = new THREE.CubicBezierCurve3(start, new THREE.Vector3(start.x, middle.y, start.z), new THREE.Vector3(end.x, middle.y, end.z), end);
            const geometry = new THREE.BufferGeometry().setFromPoints(curve.getPoints(28));
            group.add(new THREE.Line(geometry, new THREE.LineBasicMaterial({ color: 0x8296a6, transparent: true, opacity: .65 })));
            const arrow = new THREE.Mesh(new THREE.ConeGeometry(.08, .23, 6), new THREE.MeshBasicMaterial({ color: 0x8296a6 })); arrow.rotation.z = Math.PI; arrow.position.copy(end); arrow.position.y += .1; group.add(arrow);
            if (edge.label) { const tag = label(edge.label, 3, .5, '#6c91aa'); tag.position.copy(middle); tag.position.x += .7; group.add(tag); }
        }
        if (!graph.nodes.length) { render(); return; }
        // Share a label atlas instead of allocating a GPU texture for every node.
        const columns = 4, rows = Math.max(1, Math.ceil(graph.nodes.length / columns));
        const canvas = document.createElement('canvas'); canvas.width = columns * 512; canvas.height = rows * 128;
        const ctx = canvas.getContext('2d'); ctx.textAlign = 'center'; ctx.textBaseline = 'middle'; ctx.fillStyle = '#ffffff';
        for (const [index, node] of graph.nodes.entries()) {
            const title = node.label.replace(/^judo inset run /, 'Call: ').replace(/^judo serial /, 'Serial: '), x = (index % columns) * 512 + 256, y = Math.floor(index / columns) * 128;
            ctx.font = '500 28px system-ui'; ctx.fillText(mapLabels[node.kind] ?? node.kind, x, y + 30, 472);
            ctx.font = '600 46px system-ui'; ctx.fillText(title.length > 18 ? title.slice(0, 15) + '...' : title, x, y + 85, 472);
        }
        const atlas = new THREE.CanvasTexture(canvas); atlas.colorSpace = THREE.SRGBColorSpace; atlas.generateMipmaps = false; atlas.minFilter = THREE.LinearFilter;
        for (const [index, node] of graph.nodes.entries()) {
            const mesh = new THREE.Mesh(new THREE.BoxGeometry(4.2, 1.4, .3), new THREE.MeshBasicMaterial({ color: colors[node.kind] ?? colors.raw }));
            mesh.position.copy(positions.get(node.id)); mesh.userData.node = node; group.add(mesh); meshes.push(mesh);
            const geometry = new THREE.PlaneGeometry(3.9, 1.05), uv = geometry.attributes.uv, column = index % columns, row = Math.floor(index / columns);
            for (let vertex = 0; vertex < uv.count; vertex++) uv.setXY(vertex, (column + uv.getX(vertex)) / columns, 1 - (row + 1 - uv.getY(vertex)) / rows);
            const caption = new THREE.Mesh(geometry, new THREE.MeshBasicMaterial({ map: atlas, transparent: true, depthTest: false, depthWrite: false, side: THREE.DoubleSide })); caption.renderOrder = 2;
            caption.position.copy(mesh.position); caption.position.z += .2; group.add(caption); mesh.userData.caption = caption;
        }
        // A two-tone frame stays visible against both themes and every category color.
        focus = new THREE.Group(); focus.add(outline(4.6, 1.8, .1, 0xffffff), outline(4.4, 1.6, .1, 0x172027)); focus.visible = false; group.add(focus);
        fit(); render();
    }
    function fit() {
        if (!meshes.length) return;
        resize();
        const bounds = new THREE.Box3().setFromObject(group), center = bounds.getCenter(new THREE.Vector3()), size = bounds.getSize(new THREE.Vector3());
        const distance = Math.max(size.y, size.x / camera.aspect, 5) / (2 * Math.tan(THREE.MathUtils.degToRad(camera.fov / 2))) * 1.15;
        controls.target.copy(center); camera.position.set(center.x + .4, center.y + .6, center.z + distance); camera.far = Math.max(1000, distance * 3); camera.updateProjectionMatrix(); controls.update(); render();
    }
    function highlight(index) {
        active = index;
        if (focus) focus.visible = false;
        for (const mesh of meshes) {
            const selected = mesh.userData.node.id === active;
            mesh.scale.setScalar(selected ? 1.08 : 1);
            mesh.material.color.setHex(selected ? 0xffe45c : colors[mesh.userData.node.kind] ?? colors.raw);
            mesh.userData.caption.material.color.setHex(selected ? 0x172027 : 0xffffff);
            if (selected && focus) { focus.visible = true; focus.position.copy(mesh.position); focus.position.z += .17; focus.scale.copy(mesh.scale); }
        }
        if (data?.nodes[active]) select(data.nodes[active]); render();
    }
    function step() { if (data?.nodes.length) highlight((active + 1) % data.nodes.length); }
    function zoom(factor) { const offset = camera.position.clone().sub(controls.target); const distance = THREE.MathUtils.clamp(offset.length() * factor, controls.minDistance, controls.maxDistance); camera.position.copy(controls.target).add(offset.setLength(distance)); controls.update(); render(); }
    function tick(time) {
        frame = 0; if (!visible || !playing) return;
        if (time - lastStep > 1000) { step(); lastStep = time; }
        frame = requestAnimationFrame(tick);
    }
    function animate(value) { playing = value; if (frame) cancelAnimationFrame(frame); frame = 0; if (playing && visible) frame = requestAnimationFrame(tick); }
    function setVisible(value) { visible = value; resize(); animate(playing); render(); }
    theme();
    return { update, fit, zoom, step, animate, setVisible, theme, dispose() { animate(false); observer.disconnect(); controls.dispose(); clear(); renderer.dispose(); } };
}
