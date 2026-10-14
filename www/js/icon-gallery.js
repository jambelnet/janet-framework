// (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later; see LICENSE.
import { iconPath, matchingIcons, loadIconCatalog, iconButtons, categoryOptions } from './icon-library.js';
const $ = id => document.getElementById(id);
let catalog = null, selected = '';
function select(icon) {
    selected = iconPath(icon); $('librarySelection').hidden = false;
    $('selectedIconPreview').src = selected; $('selectedIconName').textContent = icon.label;
    $('selectedIconPath').value = selected; $('downloadIcon').href = selected; $('copyIconStatus').textContent = '';
    render(); $('librarySelection').scrollIntoView({ block: 'nearest' });
}
function render() {
    if (!catalog) return;
    const icons = matchingIcons(catalog, $('librarySearch').value, $('libraryCategory').value);
    $('libraryIcons').replaceChildren(...iconButtons(document, icons, select, selected));
    $('libraryStatus').textContent = icons.length ? icons.length + ' icons' : 'No matching icons.';
}
async function load() {
    catalog = null; $('libraryIcons').replaceChildren(); $('libraryRetry').hidden = true; $('libraryCategory').disabled = true;
    $('libraryStatus').textContent = 'Loading icons...';
    try {
        catalog = await loadIconCatalog(); $('libraryCategory').replaceChildren(...categoryOptions(document, catalog));
        $('libraryCategory').value = ''; $('libraryCategory').disabled = false; render();
    } catch (error) { $('libraryStatus').textContent = error.message; $('libraryRetry').hidden = false; }
}
$('librarySearch').addEventListener('input', render); $('libraryCategory').addEventListener('change', render);
$('libraryRetry').addEventListener('click', load);
$('copyIconPath').addEventListener('click', async () => {
    try { await navigator.clipboard.writeText($('selectedIconPath').value); $('copyIconStatus').textContent = 'Path copied.'; }
    catch { $('selectedIconPath').focus(); $('selectedIconPath').select(); $('copyIconStatus').textContent = 'Clipboard unavailable.'; }
});
load();
