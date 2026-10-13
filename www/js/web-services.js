// (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later; see LICENSE.
export function webServiceCommand({ name, endpoint, node, format, namespace = '', index = '' }) {
    const id = name.trim().replace(/ /g, '_');
    if (!/^[A-Za-z0-9_-]+$/.test(id)) throw new Error('Use letters, numbers, underscores or hyphens for the service name.');
    if (format !== 'JSON' && format !== 'XML') throw new Error('Choose JSON or XML.');
    try { if (!['http:', 'https:'].includes(new URL(endpoint).protocol)) throw new Error(); }
    catch { throw new Error('Enter an HTTP or HTTPS endpoint.'); }
    if (!node.trim()) throw new Error('Enter a node path.');
    const locked = value => {
        if (/[\r\n]/.test(value) || /<\/?lock>/i.test(value)) throw new Error('Enter a single value without lock markers.');
        return '<lock>' + value + '</lock>';
    };
    let extra = '';
    // Hidden XML fields must not become positional JSON arguments.
    if (format === 'XML') {
        if (namespace.trim()) {
            if (!/^(?:xmlns:)?[A-Za-z_][\w.-]*=.+$/.test(namespace.trim())) throw new Error('Enter a namespace such as s=urn:sensors.');
            extra = ' ' + locked(namespace.trim());
        }
        if (index.trim() && (!/^\d+$/.test(index.trim()) || Number(index) > 2147483647)) throw new Error('Use a zero-based, non-negative integer for Index.');
    }
    return 'judo ' + format.toLowerCase() + ' add ' + id + ' ' + locked(endpoint.trim()) + extra + ' ' + locked(node.trim()) +
        (format === 'XML' && index.trim() ? ' ' + index.trim() : '');
}

export function webServiceFields(document) {
    const xml = document.getElementById('radio-xml').checked;
    document.getElementById('wsHiddenInputs').hidden = !xml;
    for (const id of ['wsNamespace', 'wsIndex']) document.getElementById(id).disabled = !xml;
}
