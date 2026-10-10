// (c) J@mBeL.net 2010-2026, John Ambeliotis. Part of jaNET Framework, GNU GPL version 3 or later; see LICENSE.

export function httpPageUrl(currentHref, settings, cacheBust) {
    const port = settings.split(/\r?\n/)[1]?.trim() || '8080';
    if (!/^\d+$/.test(port) || Number(port) < 1 || Number(port) > 65535)
        throw new Error('The saved HTTP port is invalid. Check Web Server settings.');
    const url = new URL(currentHref);
    url.protocol = 'http:';
    url.port = port;
    url.pathname = '/www/';
    // A distinct URL also bypasses a previously cached permanent HTTPS redirect.
    url.search = new URLSearchParams({ transport: 'http', v: String(cacheBust) }).toString();
    return url.href;
}
