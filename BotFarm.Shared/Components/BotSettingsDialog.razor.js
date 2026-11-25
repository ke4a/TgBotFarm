// Emoji picker for the bot settings dialog, built on emoji-picker-element.

import { Picker } from '../lib/emoji-picker-element/index.js';

// Emoji data served by this app, so the picker never calls a CDN.
const dataSource = new URL('../lib/emoji-picker-element-data/en/emojibase/data.json', import.meta.url).href;

/**
 * Shows a picker inside hostEl and reports each picked emoji to dotNetRef's PickEmoji method.
 */
export function mount(hostEl, dotNetRef) {
    const picker = new Picker({ dataSource });
    // The app's theme colours the picker, so it does not follow the system setting on its own.
    picker.classList.add('light');
    picker.addEventListener('emoji-click', event => {
        dotNetRef.invokeMethodAsync('PickEmoji', event.detail.unicode);
    });
    hostEl.replaceChildren(picker);
}
