// Quill editor for composing Telegram messages. Quill itself is loaded globally by the host page.

const Parchment = Quill.import('parchment');

// Telegram renders <span class="tg-spoiler"> as a spoiler.
const spoiler = new Parchment.Attributor('spoiler', 'class', {
    scope: Parchment.Scope.INLINE,
    whitelist: ['tg-spoiler']
});
Quill.register(spoiler, true);

// Only formats that Telegram's HTML parse mode understands.
const telegramFormats = ['bold', 'italic', 'underline', 'strike', 'link', 'spoiler'];

const editors = new WeakMap();

/**
 * Creates the editor inside editorEl, using toolbarEl as its toolbar, and focuses it.
 */
export function initialize(editorEl, toolbarEl) {
    const quill = new Quill(editorEl, {
        formats: telegramFormats,
        modules: {
            toolbar: {
                container: toolbarEl,
                handlers: {
                    spoiler(value) {
                        this.quill.format('spoiler', value ? 'tg-spoiler' : false, Quill.sources.USER);
                    }
                }
            }
        },
        theme: 'snow'
    });

    editors.set(editorEl, quill);
    quill.focus();
}

/**
 * Returns the editor content as Telegram-compatible HTML, or null when the editor is empty.
 */
export function getContent(editorEl) {
    const quill = editors.get(editorEl);
    if (!quill || !quill.getText().trim()) {
        return null;
    }

    // Telegram HTML has no paragraphs or line breaks: paragraphs become newlines,
    // and only &lt; &gt; &amp; &quot; are accepted as named entities.
    return quill.getSemanticHTML()
        .replaceAll(/<p(\s[^>]*)?>/g, '')
        .replaceAll(/<\/p>/g, '\n')
        .replaceAll(/<br\s*\/?>/g, '')
        .replaceAll('&nbsp;', ' ');
}
